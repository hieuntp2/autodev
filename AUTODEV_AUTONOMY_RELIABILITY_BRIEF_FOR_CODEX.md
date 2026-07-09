# AutoDev Autonomy & Reliability Brief for Codex

> Executor: **Codex CLI** (high reasoning).
> Goal: AutoDev becomes a fully autonomous creative developer — no human
> approval mid-run, anchored only to the project's original goal — while every
> session reliably produces a verified result: exactly ONE awaited run per
> project, a build that passes, honest statuses, no hangs, and a prompt that
> evolves itself with full history.

This brief builds on the codebase as of 2026-07-09 (Phase 0+1 of
`AUTODEV_TOKEN_MODEL_OPTIMIZATION_BRIEF_FOR_CODEX.md` are already merged:
token accounting, `ClaudeJsonOutput`, `docs/CLI_CAPABILITIES.md`). Baseline:
build 0 warnings, 69 tests passing.

---

## Hard rules (same as previous briefs)

1. **Do not rewrite.** Extend existing services in place; match their style.
2. **No DB schema changes** (the app uses `EnsureCreated`). New per-run data →
   `RunMetadata` sidecar; new config → `Config/AutoDevOptions.cs` +
   appsettings.json; new history → files under `.ai-runner/`. Reuse existing
   DB columns where a gate is needed.
3. **Fail-soft everywhere.** Anything new that breaks must degrade to today's
   behavior, never crash a run.
4. **Honest statuses.** Never report success when validation failed or was
   skipped.
5. Every phase: `dotnet build` 0 warnings, all tests green, unit tests for new
   pure logic (style of `src/AutoDevRunner.Tests/LearningLoopTests.cs`).
6. Never put secrets in prompts, logs, or reports.

---

## Phase 0 — Bugfix: Claude JSON envelope must be parsed from stdout only

**Defect (found in review).** `ClaudeCliProvider.BuildInvocation` calls
`ClaudeJsonOutput.Parse(result.Combined)`, but `ProcessResult.Combined` is
`StdOut + "\n" + StdErr` (`Providers/ProcessRunner.cs`). The Claude CLI often
prints notices/warnings to stderr; ONE stderr line makes the JSON unparsable →
tokens/cost/session id are lost AND `Output` stays the raw JSON string, whose
`\n` escapes break `SummaryParser` (summary/resume/learning data lost for that
run).

**Fix.**
1. In `Providers/ClaudeCliProvider.cs`, parse the envelope from
   `result.StdOut` (the CLI writes the JSON envelope to stdout only). Keep
   using `result.Combined` for `ProviderOutputAnalyzer.Classify` and the
   quota/auth/reset extraction — error patterns may live in stderr.
2. Regression test in `ClaudeJsonOutputTests` / a new provider-level test:
   stdout = valid envelope, stderr = a warning line → tokens, cost, session id
   and `Text` are still extracted.

**Acceptance:** the new test fails before the fix and passes after; all
existing tests still pass.

---

## Phase 1 — Exactly one awaited run per session; no mid-work kills; no hangs

**Intent.** Each scheduled session (`--run-due`) executes **one run per
enabled project, sequentially, awaiting completion**. A run in progress is
never stopped or broken — but a hung provider must be detected and the run
must never sit in `Running` forever.

1. **Default config = single-run sessions.** In appsettings.json set
   `AutoDev:BurnTokens:Enabled = false` and `AutoDev:Continuous:Enabled =
   false`. Keep the burn/continuous feature intact — it just defaults off.
   (`DueProjectsRunner` already runs due projects sequentially and awaits each
   — verify, don't change.)

2. **Idle-based hang detection instead of mid-work kills.** Today
   `Project.MaxRunMinutes` (default 30) is an absolute timeout that kills the
   CLI even while it is actively working — that contradicts "wait for
   completion". Change the model:
   - Extend `ProcessRunner.RunAsync` with an optional `TimeSpan? idleTimeout`.
     Track the time of the last stdout/stderr line (`OutputDataReceived` /
     `ErrorDataReceived` already fire per line). A watchdog (e.g. a loop
     checking every 30s) kills the process ONLY when no output has arrived for
     `idleTimeout` — that is a hang, not work.
   - The absolute timeout remains as a hard backstop only. Reinterpret
     `Project.MaxRunMinutes` as that backstop and raise its C# default from 30
     to 240 (new projects only; existing DB rows keep their value — note in
     the run log when the backstop is below 60 minutes that it may interrupt
     long runs).
   - New options `AutoDev:Execution`: `IdleTimeoutMinutes` (default 15),
     `HeartbeatMinutes` (default 5).
   - Distinguish the two kills in the run reason: idle kill → reason
     `"hung: no output for N minutes"`; backstop kill → reason
     `"hard time cap (N minutes) reached"`. Both keep today's Timeout→Paused
     mapping and still capture partial changes (`PauseAsync` already does).

3. **Heartbeat.** While the provider runs, log every `HeartbeatMinutes`:
   `still running — last output XXs ago` (file log via `_log`, so a human
   tailing `logs/autodev-*.log` can see the run is alive, not stuck).

4. **Orphaned-run cleanup.** If the app/host crashes mid-run, the `RunRecord`
   stays `Running` forever. At startup (in `Program.cs` DB-init block), mark
   any `RunRecord` with `Status == Running` (or `Pending`) and no `FinishedAt`
   as `Failed` with reason `"orphaned: runner restarted mid-run"`, set
   `FinishedAt = DateTime.UtcNow`. Log how many were cleaned.

**Acceptance:**
- Unit tests: watchdog logic (pure part — given a last-output timestamp and
  now, decide keep-waiting/kill), reason strings for both kill kinds.
- A `--run-due` session with default config executes at most one run per
  project and returns only after the last one finishes.
- Startup with a stale `Running` row marks it Failed (verifiable with the
  existing in-memory/EnsureCreated test patterns if present, else assert the
  cleanup method's logic in isolation).

---

## Phase 2 — Result discipline: every session ends with a working build

**Intent.** "Có kết quả, chạy được, build không lỗi" — a session may not end
with a silently broken project. Validation must actually run, failures must
trigger bounded self-repair, and the outcome must be stated explicitly.

1. **Infer a validation command when none is configured.** New pure class
   `Services/ValidationCommandInferrer.cs`: when `project.ValidationCommand`
   is empty, inspect the repo root and return the first match —
   - `*.sln` or `*.csproj` anywhere top-level → `dotnet build`
   - `package.json` with a `build` script → `npm run build`
     (else with a `test` script → `npm test`)
   - `gradlew`/`gradlew.bat` → `./gradlew assembleDebug` (use `gradlew.bat`
     on Windows)
   - otherwise → null (not verifiable)
   Gate: `AutoDev:Validation:InferWhenMissing` (default **true**). Record in
   the run log which command was inferred. The inferred command is used for
   this run only — do not write it back to the project.

2. **Bounded self-repair loop.** In `RunOrchestrator`, when validation FAILS
   after the provider run:
   - Build a compact repair prompt (new `PromptBuilder.BuildRepair(...)`):
     the task title, the validation command, the TAIL of the validation output
     (cap ~3000 chars), the changed-files list, and hard instructions — *fix
     the build/test failure only, do not expand scope, do not start new
     features* — plus the required summary block.
   - Invoke the SAME provider again with it, then re-run validation.
   - Repeat up to `AutoDev:Validation:MaxRepairAttempts` (default 2). Config
     `0` disables repair (today's behavior).
   - Each attempt is logged (`Repair attempt 1/2 …`) and counted in the
     sidecar (`RepairAttempts` int?, add to `RunMetadata`).
   - Statuses stay honest: only a run whose FINAL validation passes may be
     `Success` and commit; otherwise `Failed` with the last validation output
     as reason (existing behavior — the repair loop just runs before that
     decision).

3. **Explicit session result.** In the run markdown (`WriteRunFilesAsync`) add
   a top section `## Session result` with exactly one of:
   - `SUCCESS — validation passed (<command>)`
   - `FAILED — <reason>`
   - `NOT VERIFIABLE — no validation command configured or inferable`
   Mirror the same string in the email report and as `SessionResult` (string?)
   in the sidecar. A run with changes but no possible validation is
   `NOT VERIFIABLE`, never `SUCCESS` — this matches the original improvement
   brief's verification discipline.

**Acceptance:** unit tests for the inferrer (each stack + none) and for the
repair-decision logic (fail→retry→pass ⇒ Success; fail→retry→fail ⇒ Failed,
attempts recorded). Existing orchestrator behavior unchanged when validation
passes first try.

---

## Phase 3 — Creative freedom by default (anchored to the goal, not to approvals)

**Intent.** The agent may change anything inside the repo without human
approval, as long as it serves `PROJECT_GOAL.md` / the brief and respects the
platform contract. Remove the friction that contradicts this; keep the two
anchors non-negotiable.

1. **Allow in-repo file deletions by default.** `BlockFileDeletions=true`
   currently blocks ANY deletion — normal refactoring (deleting a dead file)
   trips the hard gate. In appsettings set
   `AutoDev:Risk:BlockFileDeletions = false` (the capability stays; users can
   re-enable). Update `GuardrailService.PromptGuardrails` wording so that when
   deletions are allowed, the prompt says deletions inside the repo are
   permitted when they serve the task (instead of "never delete files").
2. **Keep — do not weaken:** `BlockOutOfProjectChanges` stays `true` (writes
   outside the repo remain always-blocked), the protected-paths guardrail
   (secrets/env files), and the platform contract section (non-negotiable
   stack). `AllowRiskyAutonomousRuns` is already `true` in appsettings —
   leave it.
3. **Prompt tone.** `PromptBuilder` already grants full authority ("You do NOT
   need to ask for approval — ever"). Review the Constraints section so risk
   wording nudges toward *small, reversible slices* without forbidding
   creative work; do not add any approval language.

**Acceptance:** with default config, a run whose only "risk" is deleting an
in-repo source file proceeds and commits; a run writing outside the repo is
still blocked. Prompt snapshot tests updated accordingly.

---

## Phase 4 — Self-evolving prompt directives with full history

**Intent.** The per-project prompt should improve itself run over run without
manual edits — but every change must be recorded and auditable.

**Already exists (do not duplicate):**
- The FULL effective prompt of every run is stored in the DB
  (`RunRecord.Prompt`) — the audit trail of what was actually sent.
- The project BRIEF already evolves with versioned history in the DB
  (`.ai-runner/brief-proposal.md` → `ProjectBriefService`, gated by
  `Project.AllowAiEditBrief`).

**New: a project-local prompt-directives layer, file-based with history.**
1. `.ai-runner/PROMPT.md` — standing directives for this project (tone,
   priorities, review checklist, style rules the agent has learned). When
   present, `PromptBuilder` injects it as a section
   `## Project prompt directives (self-evolved)` compacted to ~1200 chars
   (reuse `Compact`), placed after the goal sections and before the task.
2. **Evolution flow** (mirror `MaybeEvolveBriefAsync`, but file-based; new
   service `Services/PromptDirectivesService.cs`):
   - The prompt (only when `project.AllowAiEditBrief` is true — REUSE this
     existing column as the gate; no new DB column) tells the agent: to improve
     your own standing directives, write the full revised content to
     `.ai-runner/prompt-proposal.md`.
   - After the provider run (before changed-files capture, like the brief
     flow), if the proposal file exists: validate it (non-empty, ≤ 4000 chars;
     reject and log otherwise), archive the current `PROMPT.md` (if any) to
     `.ai-runner/prompt-history/<yyyyMMdd-HHmmss>.md`, write the new content
     to `PROMPT.md`, delete the proposal, log
     `Prompt directives evolved → prompt-history/<stamp>.md`.
   - Record `PromptDirectivesUpdated` (bool) in the sidecar.
   - Cap history: keep the newest 30 files, delete older ones (log the prune).
3. `.ai-runner/prompt-history/` and `PROMPT.md` are project files — they get
   committed with the run like other memory files (this is intentional: the
   history travels with the repo).
4. Fail-soft: unreadable/oversized proposals are logged and ignored; a missing
   `PROMPT.md` changes nothing.

**Acceptance:** unit tests for `PromptDirectivesService` (adopt proposal +
archive; reject oversized; prune history to 30) and a `PromptBuilder` test
showing the directives section appears when `PROMPT.md` content is provided
and is absent otherwise. The evolution instruction only appears in the prompt
when `AllowAiEditBrief` is true.

---

## Suggested implementation order

`Phase 0 → 1 → 2 → 3 → 4`. Phase 0 is a prerequisite bugfix (small). Phases
1–2 are the reliability core — land them before the freedom/evolution phases
so autonomous changes are always verified.

## Definition of done

1. Claude JSON parsing survives stderr noise (regression-tested).
2. Default config: one awaited run per project per session; burn/continuous
   off; a hung provider is killed only after `IdleTimeoutMinutes` of silence;
   heartbeat lines appear in the file log; no `RunRecord` can stay `Running`
   after a restart.
3. Every run ends with an explicit `## Session result` (SUCCESS / FAILED /
   NOT VERIFIABLE); validation runs (configured or inferred); a failing build
   triggers up to `MaxRepairAttempts` self-repair invocations before an honest
   `Failed`.
4. In-repo deletions no longer block autonomous runs by default;
   out-of-repo writes and secret files remain hard-blocked; platform contract
   unchanged.
5. `PROMPT.md` directives are injected when present, evolve via
   `prompt-proposal.md` behind `AllowAiEditBrief`, with archived history under
   `.ai-runner/prompt-history/` (max 30) and sidecar flag.
6. `dotnet build` → 0 warnings; `dotnet test` → all green (69 baseline + new).
7. All new behavior config-gated; defaults match this brief exactly.

## Out of scope (do NOT do)

- No DB/EF schema changes or migrations; no new DB columns.
- No changes to the summary contract (`=== AUTODEV SUMMARY ===` fields).
- No dashboard rework (existing pages may show new sidecar fields if trivial).
- No provider SDKs — CLI only.
- Do not remove the burn/continuous feature or the risk-gate capabilities —
  only their DEFAULTS change.

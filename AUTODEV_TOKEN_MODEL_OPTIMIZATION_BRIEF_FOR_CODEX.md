# AutoDev Token & Model Optimization Brief for Codex

> Executor: **Codex CLI** (high reasoning).
> Goal: make AutoDev spend tokens where they buy results — measured usage,
> budgeted prompts, right-sized models per task, and native session resume —
> WITHOUT rewriting the project.

---

## 1. Context — what exists today (verified against code, 2026-07-09)

AutoDev Runner is a single ASP.NET Core app (`src/AutoDevRunner/`, net8.0) that
runs Codex CLI / Claude CLI against target repos on a schedule. Relevant facts:

- **Pipeline**: `Services/RunOrchestrator.cs` — validate repo → branch → load
  brief/goal → learning lessons → optional OpenAI planner → task proposal →
  `PromptBuilder` → providers in priority order → summary parse → risk gate →
  guardrails → validation → commit/push → run files + JSON sidecar → retrospective → email.
- **Providers**: `Providers/CliProviderBase.cs` substitutes the prompt into an
  `Arguments` template (stdin by default) and classifies output via
  `ProviderOutputAnalyzer`. Current config (appsettings.json):
  - Codex: `exec --skip-git-repo-check --sandbox danger-full-access`
  - Claude: `-p --permission-mode bypassPermissions`
  - **No `--model`, no reasoning-effort flag, no per-task tiering.**
- **Session id**: `ProviderOutputAnalyzer.ExtractSessionId` captures it and
  `RunOrchestrator` stores it on `Project.ProviderSessionId` — **but nothing
  ever uses it**. Every run resends the full context prompt.
- **Usage**: `run.Usage` is a free-text string extracted by regex.
  `Services/CodexUsageReader.cs` reads rate-limit % from
  `~/.codex/sessions/**/rollout-*.jsonl` (quota windows only, not tokens).
  **No structured input/output token or cost accounting per run.**
- **Prompt**: `Services/PromptBuilder.cs` emits brief + platform contract +
  goal + roadmap + backlog + creative plan + skills + resume summary + memory +
  recent-run lessons + task + constraints + guardrails + required output.
  Sections have ad-hoc char caps (1600/900/900/700/700; summary 4000). The
  final prompt size is never measured or logged.
- **Learning loop** (new): `Services/RunHistoryService.cs` (reads recent run
  sidecars into `RunLessons`), `Services/RetrospectiveWriter.cs` (per-run
  `…run<id>.retro.md`), `TaskProposer.Propose(project, goal, lessons)` skips
  repeatedly-failing tasks. Gated by `AutoDev:Learning`.
- **Continuous / burn mode**: `Services/ContinuousRunner.cs` loops runs until
  provider usage hits `Continuous:MaxUsagePercent` (95%) or `MaxRunsPerSession`.
  It loops regardless of whether runs are producing value.
- **Sidecar**: `Models/RunMetadata.cs` + `Services/RunMetadataStore.cs` write
  `<repo>/.ai-runner/runs/<stamp>-run<id>.json`. **This is the extension point
  for new per-run data — the DB uses `EnsureCreated`, so DO NOT add EF columns.**
- Tests: `src/AutoDevRunner.Tests` (xUnit, 66 passing). Build is 0-warning.

## 2. Problems to solve (in value order)

1. **Token waste from full-context resend** — the session id is captured but
   native resume is never used; the same brief/goal/roadmap text is re-sent
   every run.
2. **One model for all tasks** — a "tidy docs" maintenance pass costs the same
   as a risky multi-file feature. No routing by task tier.
3. **No measurement** — without per-run input/output tokens and prompt size,
   optimization cannot be verified.
4. **Unbounded-ish prompt growth** — per-section caps exist but there is no
   global budget, no priority-based dropping, and resume-summary/lessons/memory
   overlap.
5. **Burn mode spends without checking value** — it should stop early when runs
   stop producing changes, and use cheap tiers for filler work.

## 3. Hard rules for the implementation

1. **Do not rewrite.** Extend the services named above in place. Preserve the
   existing folder structure, naming style, and comment density.
2. **No DB schema changes.** New per-run data goes into `RunMetadata` (JSON
   sidecar). New config goes into `Config/AutoDevOptions.cs` + appsettings.json.
3. **Fail-soft everywhere.** A missing CLI flag, unparsable JSON, or absent
   session must degrade to today's behavior, never crash a run.
4. **Defaults must preserve current behavior.** All new behavior ships behind
   config that defaults to today's semantics, except pure measurement (Phase 1)
   which is always-on and harmless.
5. **Every phase**: build with 0 warnings, all tests pass, add unit tests for
   pure logic (match the style of `src/AutoDevRunner.Tests/LearningLoopTests.cs`).
6. **Never put secrets in prompts, logs, or reports.**
7. CLI flags vary by installed version — **Phase 0 findings override any flag
   or model name suggested in this document.**

---

## Phase 0 — Verify installed CLI capabilities (do this first)

Run locally and record the results in `docs/CLI_CAPABILITIES.md`:

```powershell
codex --version
codex exec --help
codex exec resume --help   # does subcommand exist?
claude --version
claude --help              # look for: -p, --model, --resume, --output-format json, --max-turns
```

Document for each CLI: how to select a model, how to set reasoning effort
(Codex: `-c model_reasoning_effort=...`), how to resume a session headlessly,
how to get structured JSON output with token usage, and which models are
available on this machine's subscription. Also note the newest recommended
models (verify — do not trust this doc blindly): Codex → the newest
`*-codex` model the CLI lists; Claude → Opus 4.x for deep work,
Sonnet for standard, Haiku for light.

**Acceptance:** `docs/CLI_CAPABILITIES.md` exists with real command output
(models, flags), and every later phase references it instead of guessing flags.

---

## Phase 1 — Measure: per-run token/cost accounting + prompt size

**Goal:** every run records how many tokens it consumed and how big its prompt was.

1. `Models/RunMetadata.cs`: add nullable fields —
   `PromptChars`, `PromptEstTokens` (chars/4), `InputTokens`, `OutputTokens`,
   `CostUsd`, `Model` (all nullable so old sidecars still deserialize; the
   store already uses case-insensitive, null-ignoring JSON).
2. **Claude**: per Phase 0, prefer `claude -p --output-format json` (returns
   `usage`, `total_cost_usd`, `session_id`, and `result`). Add a fail-soft
   parser (new small class, e.g. `Providers/ClaudeJsonOutput.cs`): when the
   output parses as the JSON envelope, extract text + usage + session id; when
   it doesn't, fall back to treating the raw output as today. Feed extracted
   values through `ProviderInvocation` (extend the record with optional
   `InputTokens`/`OutputTokens`/`CostUsd`/`Model`).
3. **Codex**: extend `CodexUsageReader` (or a sibling) to also read the token
   counts that rollout files record per turn (verify shape in Phase 0 against
   a real `rollout-*.jsonl`); attribute the latest session's tokens to the run.
   Best-effort: null when unavailable.
4. `RunOrchestrator`: set `PromptChars`/`PromptEstTokens` when building the
   prompt; copy invocation token fields into the sidecar; include a one-line
   cost summary in the run markdown (`## Lifecycle & risk` section) and the
   email report.
5. Log line per run: `Prompt: N chars (~M tokens); provider used X in / Y out`.

**Acceptance:** a run's JSON sidecar shows prompt size always, and token/cost
fields when the CLI exposes them; unit tests cover the Claude JSON parser
(valid envelope, plain-text fallback) and the sidecar round-trip.

---

## Phase 2 — Prompt budget: global cap, priorities, dedupe

**Goal:** the prompt stays under a configurable budget and stops repeating itself.

1. New config `AutoDev:Prompt`: `MaxChars` (default `24000`), per-section caps
   moved from magic numbers into options (keep today's values as defaults).
2. In `PromptBuilder`, assemble sections with priorities; when the total
   exceeds `MaxChars`, shrink/drop lowest-priority first:
   - Never drop: platform contract, this run's task, constraints, guardrails,
     required output format.
   - Shrink in order: creative plan → roadmap → backlog → memory → resume
     summary → lessons (keep the do-NOT-retry list even when shrinking lessons).
3. Dedupe overlap: when `RunLessons` already lists recent outcomes, cap the
   resume summary harder (e.g. 1200 chars) — it repeats the same information.
   When a task IS already in progress (`project.CurrentTask` set), halve the
   backlog/roadmap budgets — they exist to pick a task, and one is picked.
4. Keep `Compact()` (head+tail+headings) as the shrinking primitive.

**Acceptance:** unit tests prove (a) an over-budget prompt lands under
`MaxChars`, (b) protected sections survive, (c) the required-output block is
always last and intact. Existing PromptBuilder tests still pass unchanged.

---

## Phase 3 — Model & reasoning tier routing

**Goal:** cheap tasks run on cheap models; hard/risky tasks get the strongest model.

1. Extend `ProviderCliOptions` with an optional `Tiers` map:
   ```jsonc
   "Codex": {
     "Enabled": true,
     "Command": "codex",
     "Arguments": "exec --skip-git-repo-check --sandbox danger-full-access", // fallback (today's behavior)
     "Tiers": {                       // optional; keys: Light | Standard | Deep
       "Light":    "exec --skip-git-repo-check --sandbox danger-full-access -m <cheap-codex-model> -c model_reasoning_effort=low",
       "Standard": "exec --skip-git-repo-check --sandbox danger-full-access -c model_reasoning_effort=medium",
       "Deep":     "exec --skip-git-repo-check --sandbox danger-full-access -m <best-codex-model> -c model_reasoning_effort=high"
     }
   },
   "Claude": {
     "Tiers": {
       "Light":    "-p --permission-mode bypassPermissions --model <haiku>",
       "Standard": "-p --permission-mode bypassPermissions --model <sonnet>",
       "Deep":     "-p --permission-mode bypassPermissions --model <opus>"
     }
   }
   ```
   Real model ids come from Phase 0. Missing `Tiers` or a missing key →
   fall back to `Arguments` (today's behavior).
2. New pure class `Services/TaskTierClassifier.cs` mapping a run to
   `Light | Standard | Deep`:
   - `Light`: proposal source is `maintenance`, or task text matches docs/tidy/
     comment/rename-only intents, and risk is `Safe`.
   - `Deep`: risk is `Risky` (but allowed to run), task matches a repeatedly-
     failing title being retried with a new approach, validation failed last
     run on the same task, or the task mentions multi-file refactor/architecture.
   - `Standard`: everything else.
3. `IAiProvider.RunAsync` gains the tier (or the resolved argument template) —
   thread it from `RunOrchestrator` through `CliProviderBase`. Record the tier
   and effective model in the sidecar + run markdown.
4. Config gate `AutoDev:ModelRouting:Enabled` (default **false** until Phase 0
   confirms flags; flipping it on is a one-line config change).

**Acceptance:** unit tests for the classifier (maintenance→Light, risky→Deep,
default→Standard, repeated-failure retry→Deep); provider falls back cleanly
when `Tiers` is absent; sidecar shows `Tier`/`Model`.

---

## Phase 4 — Native session resume (biggest token saver)

**Goal:** stop resending the whole context when continuing the same task.

1. Config `AutoDev:Resume`: `Enabled` (default false), `MaxResumedRuns`
   (default 3 — after N resumed runs, force a fresh session so context doesn't
   drift), `ResetOnTaskChange` (default true).
2. `RunOrchestrator` decides per run: **resume** when all of — feature enabled,
   `project.ProviderSessionId` present, same provider as last run, same
   `CurrentTask` as last run, resumed-run count < cap. Otherwise **fresh**.
3. On resume, build a **delta prompt** (new `PromptBuilder.BuildResume(...)`):
   task reminder, lessons' do-NOT-retry list, validation command, guardrails
   reminder, and the required-output block — target < 4000 chars. Skip brief/
   goal/roadmap/backlog/memory (the session already has them).
4. `CliProviderBase`: support a per-provider resume argument template with a
   `{SESSION_ID}` placeholder, e.g. (verify in Phase 0):
   - Codex: `exec resume {SESSION_ID} --skip-git-repo-check --sandbox danger-full-access`
   - Claude: `-p --resume {SESSION_ID} --permission-mode bypassPermissions`
5. **Fail-soft chain:** resume attempt that errors immediately (bad/expired
   session) → clear `ProviderSessionId`, retry once fresh with the full prompt
   in the same run. Track resumed-run count in the sidecar (or on
   `Project.ProviderSessionId` bookkeeping — no new DB columns; a
   `SessionRuns` counter can ride in the sidecar of the latest run).
6. Record `Resumed: true/false` in sidecar + markdown; Phase 1 numbers will
   show the before/after token difference.

**Acceptance:** with resume enabled and a valid session, the provider is
invoked with the resume template and the delta prompt (< 4000 chars); expired
session falls back to fresh within the same run; task change forces fresh;
unit tests cover the decision logic (pure function) and delta-prompt content.

---

## Phase 5 — Value-aware continuous (burn) mode

**Goal:** burn mode spends remaining quota on useful work, not repetition.

In `ContinuousRunner` (keep its current structure):
1. **Early stop:** after `StopAfterNoProgressRuns` (default 2) consecutive runs
   with zero changed files OR failed validation, stop the loop (log why).
2. **Distinct tasks:** pass lessons through so a title that just failed is not
   immediately re-picked within the same session (the learning loop already
   provides `IsRepeatedlyFailing`; here also avoid the *immediately previous*
   failed title).
3. **Tier for filler:** when the proposal source is `maintenance`, force the
   `Light` tier (when routing is enabled).
4. Config under `AutoDev:Continuous`: `StopAfterNoProgressRuns` (default 2),
   `LightTierForMaintenance` (default true).

**Acceptance:** unit tests for the stop-decision logic (pure function over a
sequence of run outcomes); a session's log clearly states the early-stop reason.

---

## Phase 6 — Planner call discipline (small)

1. Skip the OpenAI planner call when a task is already in progress AND the last
   run succeeded (the plan exists; the task hasn't changed) — config
   `AutoDev:Planner:SkipWhenTaskInProgress` (default true; today's behavior is
   it always calls when enabled).
2. Reviewer cadence stays as-is (planner is already fail-soft and disabled by
   default) — do NOT build new OpenAI features.

**Acceptance:** with a task in progress and a prior success, no planner HTTP
call is made (unit-testable by extracting the skip predicate).

---

## Suggested implementation order

`Phase 0 → 1 → 2 → 3 → 4 → 5 → 6`. Measurement (1) must land before
optimization (2–5) so improvements are provable. Phases 3 and 4 both touch the
provider layer — land 3 first (smaller), then 4 on top.

## Definition of done

1. `docs/CLI_CAPABILITIES.md` documents real flags/models of the installed CLIs.
2. Every run sidecar shows prompt size; token/cost fields filled when the CLI
   exposes them; run markdown + email include the cost line.
3. Prompts never exceed the configured budget; protected sections always survive.
4. With routing enabled, a maintenance run demonstrably uses the Light tier and
   a risky run the Deep tier (visible in sidecar `Tier`/`Model`).
5. With resume enabled, a continued task demonstrably sends the delta prompt
   via the CLI's native resume, and falls back safely when the session is stale.
6. Burn mode stops early after consecutive no-progress runs.
7. `dotnet build` → 0 warnings; `dotnet test` → all green (existing 66 + new).
8. All new behavior is config-gated with defaults preserving today's behavior
   (except always-on measurement).

## Out of scope (do NOT do)

- No DB/EF schema changes, no migrations.
- No new SaaS features, auth, or dashboard rework (a small "tokens/cost" column
  on existing run views is fine if trivial).
- No provider SDK integrations — CLI only.
- No prompt-content redesign beyond budgeting/dedupe (the contract format the
  parser relies on — `=== AUTODEV SUMMARY ===` fields — must not change).

# AutoDev Master Execution Prompt

You are a senior .NET + automation engineer working autonomously on this
repository (`AutoDevRunner.sln`). Execute ALL remaining phases of the two
briefs below, in the exact order given, in one continuous session. Do not
stop to ask questions, do not present options, do not wait for approval.
Decide and implement.

## Input briefs (read both fully before writing any code)

1. `AUTODEV_TOKEN_MODEL_OPTIMIZATION_BRIEF_FOR_CODEX.md` — call it **TOKEN**.
   Its Phase 0 and Phase 1 are ALREADY IMPLEMENTED and merged (token
   accounting, `Providers/ClaudeJsonOutput.cs`, `docs/CLI_CAPABILITIES.md`).
   Do not redo them. You will implement TOKEN Phases 2–6.
2. `AUTODEV_AUTONOMY_RELIABILITY_BRIEF_FOR_CODEX.md` — call it **AUTONOMY**.
   Nothing from it is implemented yet. You will implement AUTONOMY Phases 0–4.

The hard rules sections of BOTH briefs apply to every step: do not rewrite
the project, no DB/EF schema changes (sidecar/files/config only; reuse
existing DB columns), fail-soft everywhere, honest statuses, no secrets in
prompts/logs, match the existing code style and comment density.

## Execution order (reliability first, then optimization)

| Step | Phase | Summary |
|------|-------|---------|
| 1 | AUTONOMY 0 | Bugfix: parse Claude JSON envelope from StdOut only |
| 2 | AUTONOMY 1 | Single awaited run per session; idle-timeout hang detection; heartbeat; orphaned-run cleanup |
| 3 | AUTONOMY 2 | Validation inference; bounded self-repair loop; explicit `## Session result` |
| 4 | AUTONOMY 3 | Creative-freedom defaults (allow in-repo deletions; keep out-of-repo + secrets blocked) |
| 5 | AUTONOMY 4 | Self-evolving `.ai-runner/PROMPT.md` directives with archived history |
| 6 | TOKEN 2 | Prompt budget: global cap, priorities, dedupe |
| 7 | TOKEN 3 | Model & reasoning tier routing (Light/Standard/Deep) |
| 8 | TOKEN 4 | Native session resume + delta prompt |
| 9 | TOKEN 5 | Value-aware continuous (burn) mode |
| 10 | TOKEN 6 | Planner call discipline |

## Cross-brief integration notes (these override any conflict between briefs)

- **Step 6 (TOKEN 2)**: the section priority list must include the new
  `## Project prompt directives (self-evolved)` section from Step 5 —
  budget it like "memory" (shrinkable, never fully dropped below ~400 chars
  when present).
- **Step 7 (TOKEN 3)**: use the REAL model names recorded in
  `docs/CLI_CAPABILITIES.md`, not the placeholders in the TOKEN brief:
  Codex → `gpt-5.5` (Deep, `-c model_reasoning_effort=high`), default model
  (Standard), `gpt-5.4-mini` (Light). Claude → `claude-opus-4-8` (Deep),
  `claude-sonnet-5` (Standard), `claude-haiku-4-5` (Light).
  Ship `AutoDev:ModelRouting:Enabled = true` in appsettings since the flags
  are verified on this machine.
- **Step 8 (TOKEN 4)**: the repair loop from Step 3 should reuse native
  resume when available — a repair attempt continues the SAME provider
  session with the compact repair prompt (that is exactly the delta-prompt
  case). When resume is unavailable, fall back to a fresh invocation with the
  repair prompt as Step 3 already implemented. Ship `AutoDev:Resume:Enabled =
  true` (both CLIs' resume commands are verified in `docs/CLI_CAPABILITIES.md`).
- **Step 9 (TOKEN 5)**: burn/continuous mode is OFF by default after Step 2 —
  still implement the improvements; they apply whenever a user re-enables it.
- Timeout handling changed in Step 2 (idle-based): TOKEN phases must pass the
  idle-timeout behavior through unchanged — provider invocations added later
  (repair, resume) use the same `ProcessRunner` path with the same watchdog.

## Per-phase working protocol (mandatory)

For EACH step, in order:
1. Re-read that phase's section in its brief. Implement it fully, including
   its acceptance criteria and unit tests.
2. Run `dotnet build AutoDevRunner.sln` — must be 0 warnings, 0 errors.
3. Run `dotnet test AutoDevRunner.sln` — ALL tests must pass (baseline is 69
   green tests; each phase adds more). Never delete or weaken an existing
   test to make it pass; fix the code instead.
4. Commit with message `autodev: <AUTONOMY|TOKEN> phase <n> — <short title>`.
   One commit per step, on the current branch. Do not push.
5. Append one line to `docs/EXECUTION_LOG.md`:
   `<UTC timestamp> | step <k> | <phase> | tests: <count> green | <one-line note>`.
6. Move to the next step immediately.

If a step cannot fully meet an acceptance criterion (e.g. an assumption about
the environment turns out false), do NOT stop and do NOT silently skip:
implement the closest fail-soft version, note the gap explicitly in
`docs/EXECUTION_LOG.md`, and continue. Never leave the build red or tests
failing when moving on — that is the only hard gate.

## Final deliverable

After step 10, write `docs/EXECUTION_REPORT.md` summarizing: what each step
changed (files + behavior), final test count, every config default that
changed and its new value, gaps/deviations from the briefs (if any), and the
exact manual steps the user should take afterwards (e.g. review new
appsettings defaults, raise `MaxRunMinutes` on existing DB projects via the
dashboard). Then print that same summary as your final output, ending with
the standard `=== AUTODEV SUMMARY ===` block if one is requested by your
runner prompt; otherwise just the summary.

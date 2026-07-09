# AutoDev DB Phases 2–5 — Execution Prompt for Codex

You are a senior .NET engineer working autonomously on `AutoDevRunner.sln`.
Execute **Phases 2, 3, 4, and 5** of `AUTODEV_DATABASE_EVOLUTION_BRIEF_FOR_CODEX.md`,
in that order, in one continuous session. Do NOT ask questions, do NOT stop
between phases, do NOT wait for approval. Read that brief fully first — the
per-phase specs, acceptance criteria, and "Out of scope" there are binding.

## Current state (already done — do not redo)

- Phase 0 (NOT-VERIFIABLE fix) and Phase 1 (migration foundation) are merged and
  committed. The DB is now EF-migration-managed:
  - `Data/DatabaseInitializer.cs` runs at startup and applies pending migrations
    (paths: MigrateFresh / BaselineLegacy / MigrateExisting). For your new
    migrations the live DB takes the **MigrateExisting** path automatically on
    the next app start.
  - `Data/AppDbContextFactory.cs` is the design-time factory for `dotnet ef`.
  - First migration: `Migrations/20260709142122_InitialCreate`.
- Baseline: `dotnet build` 0 warnings, `dotnet test` **123 tests green**.
- `ProjectBrief` + `ProjectBriefService` are the proven pattern to mirror for
  any new versioned/append-only entity.

## Environment (critical — do not break)

- PostgreSQL 17 runs on **port 5433**. The real connection string lives in the
  git-ignored `src/AutoDevRunner/appsettings.Local.json` (already present). The
  committed `appsettings.json` still has a WRONG `5432/123456` placeholder — do
  NOT copy secrets into it and do NOT rely on it for the live DB.
- **How to add a schema change:** modify the model, then
  `dotnet ef migrations add <Name> -p src/AutoDevRunner -o Migrations`.
  This only scaffolds files; it does NOT touch the database.
- **Do NOT run `dotnet ef database update` manually.** The app applies migrations
  itself at startup via `DatabaseInitializer`. If you want to verify a migration
  applies cleanly to the live DB, run the app one-shot once at the end of a
  schema phase: `dotnet run --project src/AutoDevRunner -- --run-due` (it reads
  appsettings.Local.json, migrates, runs 0 projects, exits 0). Confirm the log
  line "applying N pending migration(s) to an existing migrated schema".

## Integration notes (override any ambiguity in the brief)

- **Phase 3 naming.** A FILE-based `PromptDirectivesService` (plural) already
  exists (loads `.ai-runner/PROMPT.md`, adopts `prompt-proposal.md`, archives
  history). Do NOT create a clashing type. Add the DB-backed versioning as a
  new service (e.g. `ProjectPromptService`, static like `ProjectBriefService`)
  and make the DB the source of truth, while keeping the existing file service
  as the repo-local mirror (write `PROMPT.md` out after adopting a new version).
  Wire it through the existing `RunOrchestrator` calls
  (`_promptDirectives.LoadAsync` / `AdoptProposalAsync`) — seed v1 from the file
  when the DB has no version, then prefer the DB version for injection.
- **Phase 2.** `RunRecord` gets the new metric columns; assign them in
  `RunOrchestrator.FinalizeAsync` (the values already exist as `_promptChars`,
  `_inputTokens`, `_outputTokens`, `_costUsd`, `_model`, `_tier`, `_resumed`,
  `_repairAttempts`, `_sessionResult`, `_validationInferred` [add this flag in
  `ResolveValidationCommand`], and `NotVerified = Success && !ValidationRun`).
  Keep writing the sidecar too. Put the metrics-aggregation logic in a pure,
  unit-tested class over a list of runs (no live DB in tests).
- **Phase 4.** Reuse `RunLessons.Norm` for the task key. `RunHistoryService`
  gets a DB-backed repeated-failure path with the file sidecars as fallback when
  the DB has no rows. AI settings self-tuning is gated by a NEW
  `Project.AllowAiEditSettings` (default false) and only whitelists
  `ValidationCommand`, `ProviderPriority`, `MaxRunMinutes`; never safety flags.
- **Phase 5.** Additive endpoints + minimal dashboard only; no framework change.
- All new services must be registered in `Program.cs` DI.

## Per-phase protocol (mandatory, repeat for phases 2→3→4→5)

1. Implement the phase fully, including its acceptance criteria and unit tests
   (match the style of `src/AutoDevRunner.Tests/LearningLoopTests.cs`).
2. For every model change, add exactly one EF migration (`migrations add`).
3. `dotnet build AutoDevRunner.sln` → must be 0 warnings, 0 errors.
4. `dotnet test AutoDevRunner.sln` → ALL green (123 baseline + your new tests).
   Never delete or weaken an existing test to pass; fix the code.
5. Commit on the current branch: `autodev: DB phase <n> — <short title>`
   (one commit per phase). Do not push.
6. Append one line to `docs/EXECUTION_LOG.md`:
   `<UTC ts> | DB phase <n> | tests: <count> green | <one-line note>`.
7. Continue to the next phase immediately.

If a step cannot fully meet an acceptance criterion, implement the closest
fail-soft version, note the gap in `docs/EXECUTION_LOG.md`, keep the build green
and tests passing, and continue. The only hard gate is: never move on with a red
build or failing tests. Migrations must never drop a column/table that holds
data.

## Final deliverable

After Phase 5, update `docs/EXECUTION_REPORT.md` with a new section covering DB
phases 2–5: files/behavior changed, each new migration name, final test count,
new config/flags and defaults, new API endpoints, and any gaps. Then print that
summary as your final output.

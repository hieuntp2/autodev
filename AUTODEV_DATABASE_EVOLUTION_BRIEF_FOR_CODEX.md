# AutoDev Database Evolution Brief for Codex

> Executor: **Codex CLI** (high reasoning).
> Goal: DB schema changes are now ALLOWED (via EF Core migrations). Use that
> freedom to make AutoDev evolve PER PROJECT: queryable run metrics, a
> per-project prompt-directive history that can be edited and self-evolves, and
> a durable per-project learning/self-tuning state. Do NOT rewrite the project.

Builds on the codebase as of 2026-07-09 (three prior briefs merged: learning
loop, token/model optimization, autonomy/reliability). Baseline: build 0
warnings, 115 tests passing, PostgreSQL via Npgsql, schema currently created
with `EnsureCreated` (NO migrations exist yet), `ProjectBrief` already stores
versioned brief history in the DB.

---

## Hard rules (updated — schema changes now allowed)

1. **Do not rewrite.** Extend existing services/models in place; match style.
2. **Schema changes ONLY through EF Core migrations.** After Phase 1, NEVER use
   `EnsureCreated` again; never hand-edit tables. Every model change ships with
   a generated migration checked into `src/AutoDevRunner/Migrations/`.
3. **Preserve existing data.** The user's DB was created by `EnsureCreated`
   (tables exist, no `__EFMigrationsHistory`). Migrations must adopt it as a
   baseline without dropping or recreating existing tables (see Phase 1).
4. **Startup must be idempotent and safe.** Applying migrations at boot must be
   a no-op when the DB is already current, and must never throw on a clean DB,
   a legacy EnsureCreated DB, or a fully-migrated DB.
5. **Fail-soft for features, fail-LOUD for migrations.** A feature that breaks
   degrades to prior behavior. A migration/baseline that cannot be applied
   safely must log a clear error and abort startup rather than run against a
   half-migrated schema.
6. Sidecars stay. Files under `.ai-runner/` (run sidecars, PROMPT.md, history)
   remain the repo-local audit trail; the DB becomes the queryable source of
   truth. Where both exist, DB is authoritative; keep writing the sidecar too.
7. Every phase: `dotnet build` 0 warnings, all tests green (115 baseline + new),
   unit tests for new pure logic. No secrets in prompts/logs.

---

## Phase 0 — Fix NOT-VERIFIABLE status (carryover, no DB needed)

`ValidationRepairPolicy.DetermineStatus` maps "no validation command
run" → `RunStatus.Failed`. Consequences: non-dotnet/npm/gradle projects never
commit, the learning loop counts them as failures, and the email subject says
"Failed" while the body says "NOT VERIFIABLE".

Fix: introduce a distinct outcome for "changed + passed all gates + not
verifiable". Keep honesty (never claim validation passed) without treating it
as failure:
- When `!run.ValidationRun` AND there are changes AND all gates/guardrails
  passed → status `Success`, commit allowed, but `SessionResult` stays
  `NOT VERIFIABLE …` and the run is flagged not-verified.
- The learning loop (`RunHistoryService.IsFailure`) must NOT count a
  not-verifiable run as a failure.
- Keep `Failed` only for real failures (validation ran and failed, provider
  error, guard/risk block, exception).

Acceptance: unit tests — not-verifiable-with-changes ⇒ Success + commit +
`NOT VERIFIABLE` session line + not counted as a learning-loop failure;
validation-ran-and-failed ⇒ Failed (unchanged).

---

## Phase 1 — Migration foundation + safe baseline of the existing DB

**This phase gates everything after it. Get it exactly right.**

1. Add package `Microsoft.EntityFrameworkCore.Design` to `AutoDevRunner.csproj`.
   Document `dotnet tool install --global dotnet-ef` in `docs/DATABASE.md`.
2. Generate the initial migration from the CURRENT model so it reproduces
   today's `EnsureCreated` schema exactly:
   `dotnet ef migrations add InitialCreate -o Migrations`.
   Then DIFF the generated `Up()` against the live schema (string-enum
   conversions on `Status`/`Provider`/`Author`, unique indexes on
   `Project.Name`, `ProviderState.Provider`, `(ProjectId,Version)` on briefs,
   cascade deletes, Npgsql `timestamptz` for `DateTime`). Fix the model or
   migration until they match — a mismatch here corrupts the baseline.
3. New service `Data/DatabaseInitializer.cs`, called from `Program.cs` in place
   of `EnsureCreatedAsync()`:
   - If the database/`Projects` table does NOT exist → `await
     db.Database.MigrateAsync()` (fresh DB, full history).
   - Else if `__EFMigrationsHistory` does NOT exist (legacy EnsureCreated DB) →
     **baseline**: use `db.GetService<IHistoryRepository>()` to
     `GetCreateIfNotExistsScript()` (create the history table) and
     `GetInsertScript(new HistoryRow("<InitialCreate id>", productVersion))`
     to mark InitialCreate as already applied WITHOUT running it, then
     `await db.Database.MigrateAsync()` to apply any newer migrations.
   - Else (history present) → `await db.Database.MigrateAsync()`.
   - Log which path ran and how many migrations were applied. On any exception,
     log and rethrow (abort startup — rule 5).
4. Keep the provider-state seeding + orphaned-run cleanup exactly as today,
   after initialization succeeds.
5. `docs/DATABASE.md`: how migrations work here, how to add one
   (`dotnet ef migrations add <Name> -p src/AutoDevRunner`), how baseline
   adoption works, how to reset a dev DB.

Acceptance:
- Fresh empty DB → boots, creates all tables, `__EFMigrationsHistory` has
  InitialCreate.
- Simulated legacy DB (tables present, no history) → boots, baseline row
  inserted, no table recreated, existing rows intact.
- Already-migrated DB → boot is a no-op.
- Unit-test the path-selection logic of `DatabaseInitializer` in isolation
  (given {db exists?, history exists?} → expected action) without a live DB.

---

## Phase 2 — Promote per-run metrics from sidecar into queryable columns

The token/cost/tier/resume/repair data currently lives only in the JSON
sidecar (`RunMetadata`). With migrations, make it queryable history on
`RunRecord` while STILL writing the sidecar (rule 6).

1. Add nullable columns to `RunRecord`: `PromptChars`, `PromptEstTokens`,
   `InputTokens`, `OutputTokens`, `CostUsd` (decimal), `Model`, `Tier`,
   `Resumed` (bool), `RepairAttempts`, `SessionResult`, `ValidationInferred`
   (bool), `NotVerified` (bool). Nullable so old rows are valid.
2. Migration `AddRunMetrics`. `RunOrchestrator.FinalizeAsync` sets these on the
   `RunRecord` (it already computes them for the sidecar — assign both).
3. Small read endpoints (extend existing API): per-project run history now can
   expose cost/tokens/tier; add `GET /api/projects/{id}/metrics` returning
   totals + last-N trend (runs, total cost, total in/out tokens, success rate,
   tokens-saved-by-resume estimate). Pure aggregation over `Runs`.

Acceptance: a completed run persists metrics to both DB and sidecar; the
metrics endpoint aggregates correctly (unit-test the aggregation over an
in-memory list of runs).

---

## Phase 3 — Per-project prompt directives in the DB, versioned + editable

The user's explicit ask: **prompt history per project that can be changed.**
Today prompt directives are file-only (`.ai-runner/PROMPT.md` +
`prompt-history/`). Promote them to a versioned DB entity, mirroring the proven
`ProjectBrief` pattern, while keeping the file as a repo-local mirror.

1. New entity `Models/PromptDirective.cs` (append-only): `Id`, `ProjectId`,
   `Version`, `Content`, `Author` (enum `User|Ai|Seed`, stored as string),
   `Note`, `CreatedAt`. FK to Project, unique `(ProjectId, Version)`, cascade
   delete — copy the ProjectBrief mapping in `OnModelCreating`. Add
   `DbSet<PromptDirective>`. Migration `AddPromptDirectives`.
2. New service `Services/PromptDirectiveService.cs` mirroring
   `ProjectBriefService`: `GetLatestContentAsync`, `AddVersionIfChangedAsync`
   (no-op when content unchanged; bumps version otherwise), `GetHistoryAsync`.
3. Seeding + source-of-truth:
   - One-time seed: if a project has no DB directive but `.ai-runner/PROMPT.md`
     exists, import it as version 1 (`Author=Seed`) — same pattern as brief
     seeding in `RunOrchestrator.LoadBriefAsync`.
   - `RunOrchestrator` loads the latest DB directive and injects it via the
     existing `PromptBuilder` directives section (replacing the file read as the
     primary source; still write the current content out to
     `.ai-runner/PROMPT.md` so the repo mirror stays current).
   - AI evolution: the existing `prompt-proposal.md` flow adopts into a NEW DB
     version (`Author=Ai`) instead of only archiving files; keep archiving the
     file history too. Gate unchanged (`Project.AllowAiEditBrief`).
4. Dashboard/API (mirror the brief endpoints if they exist; else add):
   `GET /api/projects/{id}/prompt` (latest + version),
   `GET /api/projects/{id}/prompt/history`,
   `PUT /api/projects/{id}/prompt` (user edit → new `Author=User` version),
   `POST /api/projects/{id}/prompt/revert/{version}` (new version cloning an old
   one — never mutate history).

Acceptance: editing the prompt via API creates a new version; AI proposal and
user edit both preserve full history; `PromptBuilder` injects the latest;
reverting creates a new version equal to the target; unit tests for
`PromptDirectiveService` (add/skip-unchanged/history/revert-as-new-version).

---

## Phase 4 — Durable per-project self-evolution state

"Tự tiến hóa theo project": make learning and self-tuning durable and
queryable per project, not recomputed each run from the last few file sidecars.

1. New entity `Models/ProjectLearningState.cs` (1:1 with Project): `ProjectId`
   (PK/FK), `TotalRuns`, `Successes`, `Failures`, `NotVerified`,
   `LastSuccessAt`, `LastFailureAt`, `RollingSuccessRate` (double, last-N
   window), `UpdatedAt`. Child `Models/ProjectTaskStat.cs`: `Id`, `ProjectId`,
   `TaskKeyNormalized`, `TaskTitle`, `Attempts`, `Failures`, `LastOutcome`,
   `LastAttemptAt`. Unique `(ProjectId, TaskKeyNormalized)`. Migration
   `AddProjectLearningState`.
2. `RunOrchestrator.FinalizeAsync` updates these each run (increment counters,
   upsert the task stat by normalized title — reuse `RunLessons.Norm`).
3. `RunHistoryService` gains a DB-backed path: repeated-failure detection reads
   `ProjectTaskStat` (durable, cross-session) instead of only the last-N file
   sidecars; keep the file path as fallback when DB is empty. Threshold config
   unchanged. This means a task that has failed repeatedly is remembered even
   after the sidecar window rolls off.
4. **Self-tuning with audit trail.** Let the agent propose per-project setting
   changes it has learned (e.g. a better validation command, preferred tier).
   Reuse the summary contract: parse an optional `SETTINGS_PROPOSAL:` field (or
   a `.ai-runner/settings-proposal.json`), and — gated by a new
   `Project.AllowAiEditSettings` (default false) — apply the whitelisted keys
   (`ValidationCommand`, `ProviderPriority`, `MaxRunMinutes`) to the Project row,
   recording each change in a new append-only `Models/ProjectSettingChange.cs`
   (`Id`, `ProjectId`, `Key`, `OldValue`, `NewValue`, `Source`, `CreatedAt`).
   Migration `AddProjectSettingChanges`. Never auto-change safety flags
   (`AllowRunOnMainBranch`, `AutoPush`, risk gates) — those stay human-only.

Acceptance: after N runs, `ProjectLearningState` reflects correct
counts/success rate; a task failing ≥ threshold is flagged from DB across
sessions; an AI settings proposal (when allowed) updates only whitelisted keys
and writes a `ProjectSettingChange` row; unit tests for the counter/upsert
logic and the settings whitelist (rejects non-whitelisted keys).

---

## Phase 5 — Surface it (lightweight dashboard/API), optional

Now the data is queryable, expose it without a heavy rewrite:
- Project detail: cost/token trend, success rate, prompt-directive version list
  (view/edit/revert), setting-change log, repeated-failing tasks.
- Keep it additive to the existing vanilla dashboard; no framework change.

Acceptance: endpoints return the new data; existing pages keep working.

---

## Suggested order & DoD

Order: `0 → 1 → 2 → 3 → 4 → 5`. Phase 1 gates all DB phases — do not start 2+
until baseline adoption is proven on all three DB states.

Definition of done:
1. NOT-VERIFIABLE runs are honestly reported, commit their work, and don't
   poison the learning loop.
2. Schema is migration-managed; a legacy EnsureCreated DB is adopted as a
   baseline with zero data loss; boot is idempotent.
3. Per-run metrics are queryable columns (plus the sidecar) with an aggregation
   endpoint.
4. Each project has a versioned, editable, self-evolving prompt-directive
   history in the DB with view/edit/revert and preserved history.
5. Each project has durable learning state + per-task stats, and (opt-in)
   audited AI self-tuning of whitelisted settings.
6. `dotnet build` 0 warnings; `dotnet test` all green; `docs/DATABASE.md`
   explains migrations and baseline adoption.
7. All new behavior config/flag-gated with safe defaults; every schema change
   is a checked-in migration.

## Working protocol (per phase)

Implement fully incl. acceptance + tests → `dotnet build` (0 warn) →
`dotnet test` (all green; never weaken a test) → commit
`autodev: DB phase <n> — <title>` → append a line to `docs/EXECUTION_LOG.md`
→ continue without stopping. If blocked, implement the closest safe version,
note the gap in the log, keep the build green, move on. For Phase 1 ONLY, if
baseline adoption cannot be made safe, STOP and report — do not risk the DB.

## Out of scope

- No switching DB providers; PostgreSQL stays.
- No destructive migrations (dropping columns/tables with data) without an
  explicit data-preserving path.
- No auth/multi-tenant/SaaS features.
- No change to the `=== AUTODEV SUMMARY ===` contract beyond ADDING the optional
  `SETTINGS_PROPOSAL:` field.

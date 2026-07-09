# AutoDev Master Execution Report

Final verification: `dotnet build AutoDevRunner.sln` and `dotnet test AutoDevRunner.sln` passed with 135 tests.

## Step Summary

1. AUTONOMY phase 0: `Providers/ClaudeCliProvider.cs` now parses Claude JSON envelopes from stdout only while keeping combined output for classification. Tests: `ClaudeJsonOutputTests`.
2. AUTONOMY phase 1: `ProcessRunner`, provider result handling, `RunOrchestrator`, `Program`, `Project`, options, and appsettings add idle-timeout watchdogs, heartbeat logs, hard-backstop reasons, orphan cleanup, and 240-minute new-project backstop. Tests: timeout policy, cleanup, options.
3. AUTONOMY phase 2: `ValidationCommandInferrer`, `ValidationRepairPolicy`, `SessionResultFormatter`, `PromptBuilder`, `RunOrchestrator`, `EmailService`, sidecars, options, and appsettings add inferred validation, bounded repair, and honest session results. Tests: inference, repair policy, session results.
4. AUTONOMY phase 3: `RiskOptions`, `GuardrailService`, `PromptBuilder`, appsettings, and tests allow focused in-repo deletions by default while keeping out-of-project and secret protections.
5. AUTONOMY phase 4: `PromptDirectivesService`, `PromptBuilder`, `RunOrchestrator`, `Program`, `RunMetadata`, and tests add `.ai-runner/PROMPT.md` directives, proposal adoption, history archiving/pruning, injection, and sidecar flag.
6. TOKEN phase 2: `PromptOptions`, `PromptBuilder`, `RunOrchestrator`, appsettings, and `PromptBudgetTests` add global prompt budget, configurable section caps, priority shrinking, resume-summary dedupe, and required-output preservation.
7. TOKEN phase 3: `TaskTierClassifier`, provider config/options, `CliProviderBase`, `IAiProvider`, `RunOrchestrator`, `RunMetadata`, appsettings, and tests add tier classification, tiered CLI templates, model extraction, routing gate, and sidecar tier field.
8. TOKEN phase 4: `ResumePolicy`, `PromptBuilder`, provider options/base, `RunOrchestrator`, `RunMetadata`, appsettings, and tests add native resume decisions, delta prompts, resume templates, stale-session fallback, repair-session resume, and sidecar tracking.
9. TOKEN phase 5: `ContinuousProgressPolicy`, `ContinuousRunner`, options, `RunOrchestrator`, appsettings, and tests add burn-mode no-progress early stop logging and maintenance light-tier toggle.
10. TOKEN phase 6: `PlannerCallPolicy`, `RunOrchestrator`, planner options, appsettings, and tests add the skip gate for successful in-progress tasks.

## Config Defaults Changed

- `AutoDev:BurnTokens:Enabled = false`
- `AutoDev:Continuous:Enabled = false`
- `AutoDev:Continuous:StopAfterNoProgressRuns = 2`
- `AutoDev:Continuous:LightTierForMaintenance = true`
- `AutoDev:Execution:IdleTimeoutMinutes = 15`
- `AutoDev:Execution:HeartbeatMinutes = 5`
- `AutoDev:Validation:InferWhenMissing = true`
- `AutoDev:Validation:MaxRepairAttempts = 2`
- `AutoDev:Risk:BlockFileDeletions = false`
- `AutoDev:Prompt:MaxChars = 24000` plus configurable section caps
- `AutoDev:ModelRouting:Enabled = true`
- `AutoDev:Resume:Enabled = true`, `MaxResumedRuns = 3`, `ResetOnTaskChange = true`
- `AutoDev:Planner:SkipWhenTaskInProgress = true`

## Gaps And Notes

- No DB/EF schema changes were made; new state lives in config or run sidecars.
- Existing DB projects keep their current `MaxRunMinutes`; only new `Project` instances default to 240.
- CLI resume/model paths are implemented from `docs/CLI_CAPABILITIES.md` and covered by pure tests, but no real Codex/Claude run was launched during this implementation.

## Manual Follow-Up

- Review `src/AutoDevRunner/appsettings.json` defaults, especially model IDs, resume templates, and planner settings.
- For existing projects with `MaxRunMinutes` below 60, raise the value in the dashboard if long runs should avoid the hard backstop.
- Keep secrets in `appsettings.Local.json`; do not move API keys into `appsettings.json`.

## DB Phases 2-5

Final verification for DB phases 2-5: `dotnet build AutoDevRunner.sln` passed with 0 warnings and 0 errors; `dotnet test AutoDevRunner.sln` passed with 135 tests.

### Phase 2 - Run Metrics

- Added nullable queryable run metric columns on `RunRecord`: prompt chars/tokens, provider input/output tokens, cost, model, tier, resumed, repair attempts, session result, validation inferred, and not-verified.
- `RunOrchestrator.FinalizeAsync` now persists the same metric values to DB rows that it already writes to run sidecars.
- Added `RunMetricsAggregator` and `GET /api/projects/{id}/metrics` for totals and last-N trend.
- Migration: `20260709144718_AddRunMetrics`.

### Phase 3 - Prompt Directives

- Added append-only `PromptDirective` versions with DB as source of truth and `.ai-runner/PROMPT.md` as the repo-local mirror.
- Added `ProjectPromptService` for add/skip/history/revert behavior.
- `RunOrchestrator` seeds v1 from `PROMPT.md`, injects the latest DB version, mirrors it back to file, and adopts AI prompt proposals as new DB versions.
- Added prompt endpoints: `GET /api/projects/{id}/prompt`, `GET /api/projects/{id}/prompt/history`, `PUT /api/projects/{id}/prompt`, `POST /api/projects/{id}/prompt/revert/{version}`.
- Migration: `20260709145152_AddPromptDirectives`.

### Phase 4 - Learning And Self-Tuning

- Added durable `ProjectLearningState` counters and `ProjectTaskStat` repeated-failure memory, updated from `RunOrchestrator.FinalizeAsync`.
- `RunHistoryService` now prefers DB task stats for repeated-failure detection and falls back to file sidecars when DB stats are empty.
- Added `Project.AllowAiEditSettings`, default `false`.
- Added `ProjectSettingChange` audit rows plus `ProjectSettingsTuner`; AI proposals can change only `ValidationCommand`, `ProviderPriority`, and `MaxRunMinutes`.
- Added optional `SETTINGS_PROPOSAL:` parsing and prompt contract text when project settings self-tuning is enabled.
- Migrations: `20260709145533_AddProjectLearningState`, `20260709145724_AddProjectSettingChanges`.

### Phase 5 - Dashboard/API Surface

- Added `GET /api/projects/{id}/learning` and `GET /api/projects/{id}/settings/changes`.
- Project detail now shows run metrics, prompt directive edit/history/revert, learning counters, repeated failing tasks, and setting-change audit rows.
- Project edit modal exposes the `AllowAiEditSettings` opt-in.

### Gaps And Notes

- No live `dotnet ef database update` was run; migrations remain app-applied at startup through `DatabaseInitializer`.
- No destructive migration was added. All DB phase migrations are additive for live data.

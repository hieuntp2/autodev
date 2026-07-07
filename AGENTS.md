# AutoDev Runner Agent Notes

## Project Purpose

AutoDev Runner is a Windows-first .NET 8 application that runs autonomous AI development loops against configured target repositories. It can:

- manage projects through a local dashboard and Web API;
- periodically run due projects from Windows Task Scheduler via `--run-due`;
- invoke AI CLI providers, currently Codex and Claude, using a generated prompt;
- ground runs with a project brief, project goal files, optional OpenAI planner output, selected AutoDev skills, and resume state;
- validate, assess risk, enforce guardrails, commit/push according to project policy, store run metadata, and send optional EmailJS reports.

The main app is under `src/AutoDevRunner`. `tools/autodev-orchestrator` is an older/side orchestration tool and should not be confused with the main runtime unless the task explicitly mentions it.

## Tech Stack

- .NET 8, ASP.NET Core minimal APIs, static dashboard in `wwwroot`.
- EF Core with Npgsql/PostgreSQL. Startup uses `EnsureCreated`, not migrations.
- xUnit tests in `src/AutoDevRunner.Tests`.
- Windows integration through `Microsoft.Extensions.Hosting.WindowsServices` and PowerShell installer scripts.
- AI providers are shell processes, not SDK calls, except for the optional OpenAI creative planner pre-step.

## Main Entry Points

- `src/AutoDevRunner/Program.cs`
  - Configures DI, file logging, Postgres, providers, skill registry, scheduler, and static dashboard.
  - `--run-due`, `run-due`, or `run` executes due projects once and exits.
  - Without run args, starts the local web host on `http://localhost:5099`.
- `src/AutoDevRunner/Api/ApiEndpoints.cs`
  - Minimal API routes for projects, runs, providers, settings, filesystem browse, goal/lifecycle/artifact views, and skill management.
- `src/AutoDevRunner/Services/RunOrchestrator.cs`
  - Core per-project pipeline: repo check, branch policy, brief/goal load, planner, task proposal, risk gate, skill selection, prompt build, provider invocation, summary parse, memory/brief updates, changed file tracking, guardrail, validation, commit/push, report files, DB state, email.
- `src/AutoDevRunner/Services/PromptBuilder.cs`
  - Builds the prompt and defines the required `=== AUTODEV SUMMARY ===` block that `SummaryParser` expects.
- `src/AutoDevRunner/Providers/*`
  - CLI provider abstraction and output classification for quota/auth/timeout/error.
- `src/AutoDevRunner/Skills/*`
  - Global AutoDev skill discovery, matching, toggling, and optional export to target repos.

## Data Model

- `Project`: target repo config, provider priority, validation command, branch/commit/push policy, current task/resume state, brief settings, and denormalized last-run status.
- `RunRecord`: one execution record, including provider, status, branch, prompt, creative plan, task, validation result, changed files, commit SHA, log path, lifecycle stage, and risk.
- `ProviderState`: provider availability/history such as auth/quota/success and known usage.
- `ProjectBrief`: append-only versioned brief history stored in the DB.

Enums:

- `RunStatus`: `Pending`, `Running`, `Success`, `Paused`, `Failed`, `QuotaLimit`, `AuthError`.
- `ProviderKind`: `Codex`, `Claude`.

## Runtime Files and Generated Data

Do not treat these as source unless the task is specifically about runtime output:

- `bin/`, `obj/`, `publish/`
- `.ai-runner/` and `**/.ai-runner/`
- `src/AutoDevRunner/.ai-runner/autodev.db*`
- `runner/workspaces/**` historical/sample run workspaces
- `docs/autodev/logs/*`

The repo currently includes some generated build output and runtime DB files even though they are not source. Avoid editing or relying on them.

## Configuration

- Main config: `src/AutoDevRunner/appsettings.json`.
- Local secret override: `appsettings.Local.json` loaded last by `Program.cs` and gitignored.
- PostgreSQL connection string is under `ConnectionStrings:Postgres`.
- Provider command templates:
  - If `Arguments` contains `{PROMPT}`, the prompt is inlined.
  - If it contains `{PROMPT_FILE}`, a temp prompt file path is substituted.
  - If it contains neither, the prompt is sent through stdin.
- `AutoDev:Continuous` controls back-to-back `--run-due` loops.
- `AutoDev:BurnTokens:Enabled` is the explicit switch for burn-token mode. When `true`, triggers loop until providers are no longer viable or the safety cap is reached. When `false`, due projects run once sequentially. If this setting is absent, the app falls back to legacy `AutoDev:Continuous:Enabled`.
- `AutoDev:Continuous` now holds the burn-token tuning details: usage ceiling, max runs per session, delay between runs, and quota cooldown.
- `AutoDev:Planner` controls the optional OpenAI Responses API planner.
- `AutoDev:Skills` controls global skill discovery from `AutoDevSkills/`.
- `AutoDev:Risk` controls autonomous risky-run policy, while file deletions and out-of-project changes can remain hard-blocked.

Do not add real secrets to `appsettings.json`.

## Important Services

- `GitService`: branch, status, changed file, commit, and push operations.
- `GuardrailService`: protected-file checks and prompt safety rules.
- `RiskAssessor`: classifies task intent and git changes as safe/normal/risky; blocks deletions/out-of-project writes when configured.
- `OpenAiCreativePlanner`: optional fail-soft OpenAI planner using Responses API and vector stores.
- `TaskProposer`: fallback task selection from project goal/backlog/ideas.
- `ProjectGoalService`: reads `.ai-runner/PROJECT_GOAL.md`, `ROADMAP.md`, `BACKLOG.md`, `IDEAS.md`, `DECISIONS.md` from target repos.
- `ProjectMemoryWriter`: optionally writes learned ideas/decisions/backlog items back into target repo memory files.
- `ArtifactTracker`: detects generated artifacts from changed files and summary declarations.
- `RunMetadataStore`: writes/reads JSON sidecars next to run markdown logs.
- `EmailService`: builds and sends EmailJS reports.
- `ContinuousRunner`, `DueProjectsRunner`, `SchedulerService`: run scheduling/execution loops.
- `RunLock`: prevents duplicate active sessions per project. It uses both in-process tracking and `<target-repo>/.ai-runner/run.lock` so manual dashboard runs and Windows Task Scheduler processes cannot run the same project at the same time. The lock file has pid/start/expiry metadata and is released in `RunOrchestrator`/`ContinuousRunner` finally paths.

## AutoDev Skills

Global skills live in `AutoDevSkills/`. Each skill has a `skill.json` manifest and optional `SKILL.md`, scripts, schema, presets, and samples.

Currently present:

- `pixel-animation-artist`
  - Scripts: `draw_pixel_animation.py`, `validate_pixel_animation.py`.
  - Used when project/task text matches animation/pixel-art triggers.

When changing the skill system, update tests and ensure the registry still works both from source and from published output. The app project copies `AutoDevSkills/**` into build output, excluding samples and Python cache.

## Dashboard and API

Dashboard files:

- `src/AutoDevRunner/wwwroot/index.html`
- `src/AutoDevRunner/wwwroot/app.js`
- `src/AutoDevRunner/wwwroot/styles.css`

API root: `/api`

Key routes include:

- `GET /api/overview`
- `GET/POST /api/projects`
- `GET/PUT/DELETE /api/projects/{id}`
- `POST /api/projects/{id}/run`
- `POST /api/projects/{id}/pause|resume|enable|disable`
- `GET /api/runs`, `GET /api/runs/{id}`, `GET /api/runs/{id}/log`
- `GET /api/providers`
- `GET /api/projects/{id}/goal|lifecycle|artifacts`
- `GET/POST /api/skills...`
- `GET /api/fs/browse`
- `GET /api/settings`

## Development Commands

From repo root:

```powershell
dotnet build AutoDevRunner.sln
dotnet test AutoDevRunner.sln
dotnet run --project src/AutoDevRunner
dotnet run --project src/AutoDevRunner -- --run-due
```

Windows scheduler scripts:

```powershell
.\scripts\installer.ps1
.\scripts\installer.ps1 -IntervalHours 8
.\scripts\installer.ps1 -NoDashboard
.\scripts\uninstaller.ps1
.\scripts\reset-db.ps1
```

The app is built as `WinExe` on Windows, so normal scheduled/headless runs do not show a console window. Inspect logs under the configured `logs` directory or through the dashboard.

## Test Coverage Map

Existing xUnit tests cover:

- prompt construction and platform/risk constraints;
- risk assessment;
- project goal and memory behavior;
- run metadata sidecars;
- artifact tracking;
- selected Phase 2 behavior.

Add focused tests for changes in `Services`, `Providers`, `Skills`, and API behavior. Prefer pure service tests where possible.

## Coding Conventions and Safety

- Nullable and implicit usings are enabled.
- Keep service responsibilities aligned with the existing structure; avoid moving orchestration logic into API endpoints.
- The app intentionally uses `EnsureCreated`; do not introduce EF migrations casually.
- Treat provider output parsing as heuristic. Clean exit code `0` is considered success before text pattern matching to avoid false positives from echoed prompts.
- Preserve the required summary marker and fields in `PromptBuilder` unless `SummaryParser` and tests are updated together.
- Do not weaken guardrails around secrets, destructive commands, protected branches, file deletion, or out-of-repo writes without explicit user direction.
- Use `rg`/`rg --files` for repo search and exclude `bin`, `obj`, `.vs`, `.git`, generated run folders, and runtime DB files.
- README text may display mojibake in some PowerShell output; avoid churny re-encoding edits unless the task is specifically documentation encoding cleanup.

## Common Change Locations

- Add/modify API response fields: `Api/Dtos.cs`, `Api/ApiEndpoints.cs`, related model/service tests.
- Change run pipeline behavior: `Services/RunOrchestrator.cs` plus focused service tests.
- Change burn-token/sequential mode: `Config/AutoDevOptions.cs`, `Program.cs`, `Services/SchedulerService.cs`, `Services/RunLauncher.cs`, `Services/ContinuousRunner.cs`, and config tests.
- Change project concurrency/session locking: `Services/RunLock.cs`, `Services/RunOrchestrator.cs`, `Services/RunLauncher.cs`, `Api/ApiEndpoints.cs`, and lock tests.
- Change prompt semantics: `Services/PromptBuilder.cs`, `Services/SummaryParser.cs`, prompt tests.
- Change provider invocation/classification: `Providers/CliProviderBase.cs`, `ProviderOutputAnalyzer.cs`, provider tests.
- Change risk/guardrails: `Services/RiskAssessor.cs`, `GuardrailService.cs`, risk tests.
- Change skill matching/export/runtime toggles: `Skills/*`, `AutoDevSkills/*`, skill-related API routes/tests.
- Change dashboard UX: `wwwroot/app.js`, `wwwroot/styles.css`, `wwwroot/index.html`.
- Change install/uninstall behavior: `scripts/*.ps1`.

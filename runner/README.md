# AutoDev Orchestrator

AutoDev Orchestrator is a reusable C#/.NET CLI that runs autonomous daily development workflows for configurable target projects.

It is separate from the target project it modifies. The runner owns project configs, templates, memory, workspaces, logs, reports, and automation code.

## What It Does

- Loads `projects/{projectId}.json`.
- Creates `workspaces/{projectId}/{yyyy-MM-dd}/`.
- Snapshots configured project docs and Git state.
- Builds a planner prompt from templates and project context.
- Calls the OpenAI Responses API to create a daily plan.
- Calls Codex CLI non-interactively to implement one task in the target repo.
- Runs configured build and test commands.
- Captures logs, changed files, and diffs.
- Enforces protected-path, diff-size, and build-pass commit policies.
- Writes a daily report and updates `workspaces/{projectId}/project-memory.md`.
- Commits and optionally pushes only when the configured safety rules allow it.

## Requirements

- .NET 8+
- Git
- Codex CLI available in `PATH`
- `OPENAI_API_KEY` set in the environment
- Target repo available locally at the configured `repoPath`

Optional:

- `OPENAI_PLANNER_MODEL` to override the default planner model.

## Setup

Restore and build the runner:

```powershell
dotnet restore
dotnet build
dotnet test
```

Set your OpenAI API key:

```powershell
$env:OPENAI_API_KEY = "your-api-key"
```

Confirm `projects/ai-pet.json` points to a real local target repo before running.

## Project Config

Project configs live in `projects/{projectId}.json`.

Important safety fields:

- `allowedWritePaths`: paths Codex should limit changes to.
- `protectedPaths`: paths that block auto-commit if changed.
- `maxDiffLines`: maximum diff line count allowed for auto-commit.
- `allowAutoCommit`: enables or disables committing.
- `allowAutoPush`: defaults to `false` for the MVP.
- `commitOnlyIfBuildPasses`: skips commit when build fails.

Do not put secrets in project config files.

## Running Manually

From this runner directory:

```powershell
dotnet run --project src\AutoDev.Cli -- run --project ai-pet
dotnet run --project src\AutoDev.Cli -- status --project ai-pet
```

After publishing, the executable assembly name is `autodev`, so the intended command shape is:

```powershell
autodev run --project ai-pet
autodev status --project ai-pet
```

## Workspace Structure

Each run creates:

```text
workspaces/{projectId}/{yyyy-MM-dd}/
  metadata.json
  run.log
  00-input/
  01-planning/
  02-implementation/
  03-verification/
  04-review/
  05-retrospective/
```

Inspect this folder first when diagnosing a run.

## Safety Rules

The runner does not snapshot known secret paths:

- `.env`
- `appsettings.Production.json`
- `local.properties`
- `secrets/`
- `keystore/`

The runner skips auto-commit when:

- protected paths changed
- direct requirement edits are blocked and product vision/scope changed
- diff lines exceed `maxDiffLines`
- build fails while `commitOnlyIfBuildPasses` is true
- `allowAutoCommit` is false

## Scheduling With Windows Task Scheduler

Example:

```powershell
$Action = New-ScheduledTaskAction `
  -Execute "D:\AutoDev\runner\autodev.exe" `
  -Argument "run --project ai-pet" `
  -WorkingDirectory "D:\AutoDev\runner"

$Trigger = New-ScheduledTaskTrigger `
  -Daily `
  -At 1:00AM

$Settings = New-ScheduledTaskSettingsSet `
  -StartWhenAvailable `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -ExecutionTimeLimit (New-TimeSpan -Hours 4)

Register-ScheduledTask `
  -TaskName "AutoDev AI Pet Daily Run" `
  -Action $Action `
  -Trigger $Trigger `
  -Settings $Settings `
  -Description "Runs autonomous AI development workflow for AI Pet project"
```

## Troubleshooting

- `OPENAI_API_KEY is not set.`: set the environment variable before running.
- `Codex CLI is not installed or not available in PATH.`: install Codex CLI or fix `PATH`.
- `Target repo path does not exist`: update `repoPath` in the project config.
- Build/test failures: inspect `03-verification/`.
- Guardrail blocks: inspect `04-review/reviewer-output.md` and `02-implementation/git-diff.patch`.

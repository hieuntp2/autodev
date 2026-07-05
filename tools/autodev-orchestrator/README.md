# autodev-orchestrator — Daily Pixel Garden creative loop

A small .NET 8 console tool that runs the autonomous creative loop for **Daily Pixel Garden**:
OpenAI plans (cheaply), Claude CLI / Codex implements, files carry the state. See
[`docs/autodev/AUTODEV_CREATIVE_LOOP.md`](../../docs/autodev/AUTODEV_CREATIVE_LOOP.md) for the big picture.

## Setup

1. .NET 8 SDK (already required by this repo).
2. Copy `appsettings.example.json` to `appsettings.json` (git-ignored, personal config) and set
   `OPENAI_API_KEY` there. No key yet? Set `AUTODEV_DRY_RUN` to `"true"` — the loop then uses a
   built-in sample plan and never calls OpenAI.
3. Alternative: a repo-root `.env` (see `.env.example`) or process env vars also work and override
   `appsettings.json`. Do **not** set `OPENAI_API_KEY` as a global Windows env var — it leaks into
   every tool (Codex CLI picks it up and switches auth mode).

## Commands

Run from anywhere inside the repo:

```powershell
dotnet run --project tools/autodev-orchestrator -- status
dotnet run --project tools/autodev-orchestrator -- report
dotnet run --project tools/autodev-orchestrator -- plan
dotnet run --project tools/autodev-orchestrator -- run
```

| Command | What it does |
| --- | --- |
| `status` | Plan date/theme, task counts by status, next task, last run, whether an OpenAI call is needed. |
| `report` | Writes `docs/autodev/reports/YYYY-MM-DD-report.md` (plan table, daily-log tail, git snapshot). |
| `plan` | Pending tasks remain → prints that and exits, **no OpenAI call**. Plan missing/completed → writes a report, makes **one** OpenAI call, saves `CURRENT_PLAN.json` + `plans/YYYY-MM-DD-plan.json`, appends `DAILY_LOG.md` / `IDEA_MEMORY.md`, writes the implementer prompt. |
| `run` | `status`, then report+plan if needed, then writes `docs/autodev/NEXT_IMPLEMENTER_PROMPT.md` for the next pending task. With `AUTODEV_RUN_IMPLEMENTER=true` it also launches `AUTODEV_IMPLEMENTER_COMMAND` (`{PROMPT_FILE}` → prompt path). |

Exit codes: `0` ok, `1` clear error (e.g. missing `OPENAI_API_KEY`, invalid plan JSON), `2` unknown command.

## Typical day

```powershell
# scheduled task (or you) runs:
dotnet run --project tools/autodev-orchestrator -- run

# then hand the task to Claude CLI manually, e.g.:
claude -p "Read the file docs/autodev/NEXT_IMPLEMENTER_PROMPT.md and execute the task exactly as described." --permission-mode acceptEdits
```

The implementer marks the task `completed` in `CURRENT_PLAN.json` and appends to `DAILY_LOG.md`.
Next `run` hands off the next task; when all are done, the next `run` generates a report and a fresh plan.

To fully automate the handoff, set in `.env`:

```
AUTODEV_RUN_IMPLEMENTER=true
AUTODEV_IMPLEMENTER_COMMAND=claude -p "Read the file {PROMPT_FILE} and execute the task exactly as described." --permission-mode acceptEdits
```

## Windows Task Scheduler (recommended for v1)

```powershell
# register (no admin needed; runs as you, only while you are logged in)
.\tools\autodev-orchestrator\windows\register-daily-pixel-garden-task.ps1              # daily at 09:00
.\tools\autodev-orchestrator\windows\register-daily-pixel-garden-task.ps1 -DailyTime "07:30"

# trigger immediately / remove
Start-ScheduledTask -TaskName DailyPixelGarden-AutoDev
.\tools\autodev-orchestrator\windows\register-daily-pixel-garden-task.ps1 -Unregister
```

The task runs `windows\run-autodev-once.ps1`, which loads `.env`, runs `run`, and appends
stdout/stderr to `docs/autodev/logs/autodev-YYYY-MM-DD.log`. You can also run that script
by hand: `.\tools\autodev-orchestrator\windows\run-autodev-once.ps1 -Command status`.

### Windows Service?

Not for v1. A service is only better if you need the loop to run while **no user is logged in** —
but then Claude/Codex CLI would not have your interactive login session, and this repo's existing
runner has the same constraint. If that day comes, wrap the orchestrator in a Worker Service like
`src/AutoDevRunner` does (`UseWindowsService`); the tool is already a clean single entry point.

## Cost control

- One OpenAI call per planning cycle, and only when the plan is empty or completed — `status` tells you in advance.
- `OPENAI_DAILY_MODEL=gpt-5.4-nano` by default; `OPENAI_MAX_OUTPUT_TOKENS=2500` caps output; JSON-mode keeps answers terse.
- Optional Monday reviews on `OPENAI_WEEKLY_MODEL` only if `OPENAI_ENABLE_WEEKLY_REVIEW=true`.
- Context sent is small and capped: the daily report, idea-memory tail — never source code.
- Prompt/completion token usage is printed on every call.

## Security

- `OPENAI_API_KEY` lives only in git-ignored files: `tools/autodev-orchestrator/appsettings.json`
  (preferred) or the repo-root `.env`. Never as a global Windows env var, never committed.
- `appsettings.json`, `.env`, and `docs/autodev/logs/` are all git-ignored.
- The key is never passed to the frontend, the implementer, or written to any generated file.
- Missing key → `plan`/`run` fail with a clear message instead of doing anything partial.

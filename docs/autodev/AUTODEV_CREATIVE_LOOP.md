# Daily Pixel Garden — Autonomous Creative Loop

A small web pixel world that grows by itself. Every day the loop adds something new:
a visual detail, a character, weather, a mood, a tiny event, a story line, an animation.

This is a fun personal project. The design principle is: **simple files, cheap AI calls,
deterministic handoff**. No database, no auth, no cloud.

## Roles

| Role | Who | Does | Never does |
| --- | --- | --- | --- |
| Creative Director / Planner | OpenAI (cheap model) | Reviews state, invents the next small plan, writes story & ideas | Edit source code, see full source code |
| Code Implementer | Claude CLI / Codex / autodev | Executes exactly one task from the plan, verifies, marks it done | Invent new plans, call OpenAI |
| Orchestrator | `tools/autodev-orchestrator` (.NET console app) | Glue: status, reports, one OpenAI call per cycle, handoff files | Creative decisions |

## The loop

```
Task Scheduler (daily)
      |
      v
autodev-orchestrator run
      |
      +--> read docs/autodev/CURRENT_PLAN.json
      |
      +--> pending tasks left? ----yes---> write NEXT_IMPLEMENTER_PROMPT.md
      |                                    (NO OpenAI call)                 \
      no                                                                     \
      |                                                                       v
      +--> write reports/YYYY-MM-DD-report.md                        implementer (Claude/Codex)
      +--> ONE OpenAI call: report + log tail + idea memory tail      does the task, then updates
      +--> save CURRENT_PLAN.json + plans/YYYY-MM-DD-plan.json        CURRENT_PLAN.json status +
      +--> append story to DAILY_LOG.md, ideas to IDEA_MEMORY.md      DAILY_LOG.md
      +--> write NEXT_IMPLEMENTER_PROMPT.md for the first task
```

The implementer runs either automatically (`AUTODEV_RUN_IMPLEMENTER=true` with
`AUTODEV_IMPLEMENTER_COMMAND`) or manually — you point Claude CLI / Codex at
`docs/autodev/NEXT_IMPLEMENTER_PROMPT.md` whenever you like. Task completion is
recorded in `CURRENT_PLAN.json`; the next `run` picks up the next pending task,
and only when all tasks are done does OpenAI get called again.

## Files

| File | Purpose |
| --- | --- |
| `CURRENT_PLAN.json` | The active plan; single source of truth for task status |
| `plans/YYYY-MM-DD-plan.json` | Immutable history of every plan |
| `reports/YYYY-MM-DD-report.md` | Daily state report (also the context sent to OpenAI) |
| `DAILY_LOG.md` | The garden's story, one entry per plan/task |
| `IDEA_MEMORY.md` | Long-lived idea pool so the Creative Director stays consistent |
| `NEXT_IMPLEMENTER_PROMPT.md` | Deterministic handoff to the code implementer |
| `OPENAI_PLANNER_PROMPT.md` | The system prompt sent to OpenAI (edit to steer the vibe) |
| `logs/` | Scheduled-run output logs (git-ignored) |

## Cost control

- OpenAI is called **at most once per cycle**, and only when the plan is empty or fully completed.
- Default model is cheap (`OPENAI_DAILY_MODEL=gpt-5.4-nano`); output capped by `OPENAI_MAX_OUTPUT_TOKENS` (2500).
- Optional Monday "weekly review" on a slightly better model (`OPENAI_ENABLE_WEEKLY_REVIEW=true`).
- OpenAI receives only the report, log tails, and idea memory — never source code.
- `AUTODEV_DRY_RUN=true` exercises the entire loop with zero API calls.

See [`../../tools/autodev-orchestrator/README.md`](../../tools/autodev-orchestrator/README.md) for setup,
commands, and Windows Task Scheduler registration.

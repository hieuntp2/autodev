# OpenAI as Creative Director

OpenAI's only job in this project is **planning, not coding**. It is the cheap,
occasional "imagination" of the garden; the hands are Claude CLI / Codex.

## What the Creative Director does

- Reads the daily report (plan state, story tail, git snapshot) and the idea memory.
- Returns one small JSON plan: theme, direction, 1-3 tasks with implementation prompts
  and acceptance criteria, a story-log entry, and a few future ideas.
- Keeps creative continuity across days (yesterday's rain can leave today's puddle).

## What it must never do

- Edit, generate, or review source code or diffs.
- Receive the app's source code (only reports/logs/ideas are sent).
- Be called while pending tasks remain — the orchestrator enforces this.
- Use expensive models or long outputs by default.
- Generate images (v1 draws everything with PixiJS primitives).

## How it is invoked

One HTTP call to the Chat Completions API per planning cycle:

- **System prompt:** the full contents of [`OPENAI_PLANNER_PROMPT.md`](OPENAI_PLANNER_PROMPT.md)
  — edit that file to change the garden's creative voice.
- **User message:** today's date, whether the app exists, the latest report,
  and the tail of `IDEA_MEMORY.md` (size-capped).
- **Settings:** JSON-mode output, `OPENAI_MAX_OUTPUT_TOKENS` cap,
  model from `OPENAI_DAILY_MODEL` (or `OPENAI_WEEKLY_MODEL` on Mondays when
  `OPENAI_ENABLE_WEEKLY_REVIEW=true`).

The response is validated against the plan schema, saved to `CURRENT_PLAN.json`
and `plans/`, and its story/ideas are appended to `DAILY_LOG.md` / `IDEA_MEMORY.md`.
If the JSON is invalid, the orchestrator fails loudly and changes nothing else.

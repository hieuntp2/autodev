# CLI Capabilities

Verified on 2026-07-09 from `D:\1. Project\0. autodev`.

## Codex CLI

Version:

```text
codex-cli 0.142.5
```

Relevant `codex exec --help` findings:

- Non-interactive command: `codex exec [OPTIONS] [PROMPT]`
- Stdin prompt support: omit `[PROMPT]` or pass `-`.
- Model selection: `-m, --model <MODEL>`.
- Reasoning/config override: `-c, --config <key=value>`, including `-c model_reasoning_effort="high"`.
- Sandbox flag: `-s, --sandbox <read-only|workspace-write|danger-full-access>`.
- JSON event output: `--json`.
- Last-message capture: `-o, --output-last-message <FILE>`.

Relevant `codex exec resume --help` findings:

```text
Usage: codex exec resume [OPTIONS] [SESSION_ID] [PROMPT]
```

- Headless resume exists.
- Resume by id: `codex exec resume <SESSION_ID> -`.
- Resume most recent: `codex exec resume --last -`.
- Resume also supports `-m, --model <MODEL>`, `-c, --config <key=value>`, `--json`, `--skip-git-repo-check`, and `--output-last-message`.

Model catalog:

- `codex debug models` is the usable local catalog command.
- `codex models` is not a catalog command in this non-TTY context; it returned `Error: stdin is not a terminal`.
- Local catalog output:

```text
slug              display_name      default_reasoning_level supported_reasoning_levels visibility supported_in_api
gpt-5.5           GPT-5.5           medium                  low,medium,high,xhigh      list       True
gpt-5.4           GPT-5.4           medium                  low,medium,high,xhigh      list       True
gpt-5.4-mini      GPT-5.4-Mini      medium                  low,medium,high,xhigh      list       True
codex-auto-review Codex Auto Review medium                  low,medium,high,xhigh      hide       True
```

Local catalog entries containing "codex":

```text
codex-auto-review Codex Auto Review medium low,medium,high,xhigh hide True
```

Current official OpenAI Codex model guidance:

- OpenAI recommends `gpt-5.5` for most Codex tasks and `gpt-5.4-mini` for faster/lower-cost lighter coding tasks.
- Official docs also list `gpt-5.3-codex-spark` as a research-preview Codex model for ChatGPT Pro users, but it is not present in this machine's local `codex debug models` catalog.
- Source: https://developers.openai.com/codex/models

Codex usage/token accounting:

- `codex exec --json` exposes structured events, but token usage was verified in local rollout JSONL files rather than CLI stdout.
- Local rollout files under `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl` include per-turn fields:

```text
payload.info.last_token_usage.input_tokens
payload.info.last_token_usage.cached_input_tokens
payload.info.last_token_usage.output_tokens
payload.info.last_token_usage.reasoning_output_tokens
payload.info.last_token_usage.total_tokens
payload.info.total_token_usage.input_tokens
payload.info.total_token_usage.output_tokens
payload.rate_limits.primary.used_percent
payload.rate_limits.secondary.used_percent
```

- The local rollout lines did not expose a model slug, only `payload.info.model_context_window`.

## Claude CLI

Version:

```text
2.1.195 (Claude Code)
```

Relevant `claude --help` findings:

- Non-interactive print mode: `-p, --print`.
- Model selection: `--model <model>`.
- Reasoning effort: `--effort <low|medium|high|xhigh|max>`.
- Session resume: `-r, --resume [value]`.
- Continue latest conversation in current directory: `-c, --continue`.
- Structured output: `--output-format <text|json|stream-json>`; JSON output only works with `--print`.
- JSON input streaming: `--input-format <text|stream-json>`.
- Permission mode: `--permission-mode <acceptEdits|auto|bypassPermissions|default|dontAsk|plan>`.
- Budget cap in print mode: `--max-budget-usd <amount>`.
- Session persistence can be disabled with `--no-session-persistence`.

Headless resume examples:

```powershell
claude -p --resume <SESSION_ID> --permission-mode bypassPermissions --output-format json
claude -p --continue --permission-mode bypassPermissions --output-format json
```

Structured JSON output:

```powershell
claude -p --permission-mode bypassPermissions --output-format json "<prompt>"
```

The JSON envelope is expected to include `result`, `session_id`, `usage`, and `total_cost_usd` when the CLI exposes them. Phase 1 parsing must fail soft and treat stdout as plain text when this envelope is absent or unparsable.

Model availability:

- This Claude CLI help does not expose a local model-list command.
- The CLI supports aliases and full model names through `--model`; help examples mention aliases such as `fable`, `opus`, `sonnet`, and full names such as `claude-fable-5`.
- Official Claude Code docs warn that aliases such as `fable`, `opus`, `sonnet`, and `haiku` resolve to provider defaults that may lag latest releases or be unavailable in an account. Pin full model IDs where deterministic routing matters.
- Source: https://code.claude.com/docs/en/model-config

Current official Anthropic model guidance:

- For complex agentic coding and enterprise work, start with `claude-opus-4-8`.
- Highest available capability: `claude-fable-5`.
- Standard speed/intelligence tier: `claude-sonnet-5`.
- Light/fast tier: `claude-haiku-4-5-20251001` or alias `claude-haiku-4-5`.
- Source: https://platform.claude.com/docs/en/about-claude/models/overview

## Phase 1 Implementation Notes

- Claude provider arguments can use `-p --permission-mode bypassPermissions --output-format json`.
- Claude JSON parsing must extract `result` as the provider text, preserve `session_id`, and record `usage.input_tokens`, cache input token fields, `usage.output_tokens`, `total_cost_usd`, and `model` when present.
- Codex provider token accounting should read `payload.info.last_token_usage` from the newest rollout JSONL written by the provider run. Leave token/model fields null when no current rollout snapshot is available.
- Later model-routing phases must use the concrete model names above instead of the placeholder names in the optimization brief.

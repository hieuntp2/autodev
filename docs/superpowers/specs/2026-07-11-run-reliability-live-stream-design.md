# Run Reliability and Live Stream Design

## Goal

Make scheduled Codex runs finish according to the provider's real terminal state, remain recoverable across runner or Windows restarts, validate Android projects with the correct toolchain, and stream useful activity to an already-open dashboard.

## Accepted scope

The implementation covers the failures observed in runs 122 and 123:

- consume Codex's JSONL automation protocol and treat `turn.completed` / `turn.failed` as terminal signals;
- parse only the final agent message instead of combined human-readable output;
- prevent oversized task-learning keys and make terminal persistence independent from optional learning/reporting;
- pin Codex model selection so user-global defaults cannot silently break scheduled runs;
- select Android Studio JBR for inferred Gradle validation on Windows;
- recover dead-process run locks and reconcile project snapshots after restart;
- retain live run events in a cross-process file and expose them through server-sent events (SSE);
- keep commit ownership in the runner and detect provider-created commits defensively.

## Architecture

### Codex provider protocol

Every Codex command template includes `--json`. `CodexJsonOutput` parses JSONL events into the final agent message, thread ID, terminal state, token counts, and live display lines. `ProcessRunner` accepts an optional terminal-line detector. When a documented terminal event arrives, it gives the CLI a short exit grace period; if the CLI remains alive, it terminates the process tree but returns the terminal outcome rather than an idle timeout.

Claude keeps its existing JSON-envelope path and providers without a terminal detector retain the existing exit-code behavior.

### Run state and finalization

A run and its project snapshot are persisted as `Running` before the provider starts. Summary parsing only extracts fields from a real `=== AUTODEV SUMMARY ===` block. Missing protocol output is retained as bounded text but cannot replace `CurrentTask` with diffs or logs.

Learning keys and display titles are bounded before they reach PostgreSQL. The terminal run/project state is saved before learning, retrospective, and email work. Optional post-processing failures are logged without leaving the run active.

### Validation environment

`ValidationEnvironmentResolver` supplies process environment overrides. For Gradle wrapper commands on Windows it uses a configured Java home when present, otherwise Android Studio's bundled JBR when installed. The override applies only to validation/repair commands, not globally to the machine.

### Locks and restarts

Lock files keep PID and process start time. Startup recovery removes a non-expired lock only when the owning PID is absent or its process start time does not match. Active matching processes remain protected. Orphaned Pending/Running records become Paused while task/session resume state is preserved.

### Live events

The scheduled runner and dashboard are separate processes, so live state is not stored only in memory. `RunEventStore` appends JSONL to `<repo>/.ai-runner/runs/run-<id>.events.jsonl` with sequence, timestamp, kind, and message. The API tails this file through `GET /api/runs/{id}/events` as SSE. Opening a running run replays existing events and follows new ones until a terminal event arrives.

The dashboard shows lifecycle messages, agent messages, commands, command results, file changes, heartbeats, and terminal status. Raw model reasoning is not displayed.

### Git ownership

Prompts tell providers not to commit or push. The runner captures the starting HEAD, inspects both working-tree changes and the committed `startHead..HEAD` range, applies guardrails to their union, and records an existing provider commit defensively. Normal commits remain the runner's responsibility after validation.

## Error handling

- `turn.failed` and JSONL `error` events become provider errors with the provider message.
- Malformed JSON lines are retained as ordinary output and never become structured task fields.
- A terminal event wins over a later process hang; a hard timeout before any terminal event remains a timeout.
- SSE clients may reconnect and replay the append-only event file.
- Missing event files return an empty stream rather than failing the run page.
- Learning, retrospective, metadata, and email failures are logged independently after terminal state persistence.

## Verification

- Unit tests cover JSONL parsing, terminal detection policy, summary rejection without a marker, bounded learning keys, validation Java selection, dead-lock recovery, and run-event storage.
- API/dashboard behavior is tested with focused service/API checks and an in-app browser pass.
- Full verification runs `dotnet build AutoDevRunner.sln` and `dotnet test AutoDevRunner.sln`.
- A read-only `codex exec --json -m gpt-5.5` smoke probe verifies the installed CLI emits `turn.completed` and exits successfully.

## Non-goals

- Exposing the dashboard beyond localhost or adding authentication.
- Persisting raw chain-of-thought/reasoning.
- Rewriting the scheduler or replacing PostgreSQL.
- Retroactively rewriting historical run 122/123 records without an explicit audited recovery operation.

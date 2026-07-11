# Run Reliability and Live Stream Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Codex runs terminate reliably, persist honest state, validate Android builds with JBR, recover after restarts, and stream live activity to the dashboard.

**Architecture:** Codex uses JSONL terminal events and a final-message parser; the runner writes cross-process JSONL event files that the dashboard tails via SSE. Terminal DB state is persisted before optional learning/reporting, while validation and lock recovery receive focused environment/process-aware policies.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, EF Core/Npgsql, xUnit, vanilla JavaScript, Server-Sent Events.

---

### Task 1: Codex JSONL protocol and terminal process handling

**Files:**
- Create: `src/AutoDevRunner/Providers/CodexJsonOutput.cs`
- Modify: `src/AutoDevRunner/Providers/ProcessRunner.cs`
- Modify: `src/AutoDevRunner/Providers/CliProviderBase.cs`
- Modify: `src/AutoDevRunner/Providers/CodexCliProvider.cs`
- Modify: `src/AutoDevRunner/appsettings.json`
- Test: `src/AutoDevRunner.Tests/CodexJsonOutputTests.cs`
- Test: `src/AutoDevRunner.Tests/ProcessTerminalPolicyTests.cs`

- [ ] Write parser tests using `thread.started`, `item.completed` agent/command events, `turn.completed` usage, `turn.failed`, and malformed lines.
- [ ] Run the focused tests and verify they fail because `CodexJsonOutput` and terminal policy do not exist.
- [ ] Implement `CodexJsonOutput.Parse`, live-line formatting, and terminal detection.
- [ ] Extend `ProcessResult`/`ProcessRunner.RunAsync` with an optional terminal detector and five-second exit grace; a completed terminal event returns exit code zero even if cleanup requires killing the process.
- [ ] Override Codex provider output handling and append `--json` plus explicit models to every Codex template.
- [ ] Run focused tests and the existing provider/timeout tests; expect all to pass.

### Task 2: Summary and learning data hardening

**Files:**
- Modify: `src/AutoDevRunner/Services/SummaryParser.cs`
- Modify: `src/AutoDevRunner/Services/ProjectLearningUpdater.cs`
- Modify: `src/AutoDevRunner/Services/RunHistoryService.cs`
- Test: `src/AutoDevRunner.Tests/PromptBuilderTests.cs`
- Test: `src/AutoDevRunner.Tests/ProjectLearningStateTests.cs`

- [ ] Add a regression test proving output without the summary marker cannot populate `TASK` or `NEXT_TASK`.
- [ ] Add a regression test proving a 15,000-character task produces an index key at most 256 characters and a display title at most 500 characters.
- [ ] Run the tests and verify both fail against current parsing/updater behavior.
- [ ] Require the marker for structured fields, retain only a bounded raw fallback, and bound normalized learning keys/titles.
- [ ] Run the focused tests; expect all to pass.

### Task 3: Restart reconciliation and dead lock recovery

**Files:**
- Modify: `src/AutoDevRunner/Services/RunStartupCleanup.cs`
- Modify: `src/AutoDevRunner/Services/RunLock.cs`
- Modify: `src/AutoDevRunner/Program.cs`
- Modify: `src/AutoDevRunner/Services/RunOrchestrator.cs`
- Test: `src/AutoDevRunner.Tests/RunStartupCleanupTests.cs`
- Test: `src/AutoDevRunner.Tests/RunLockTests.cs`

- [ ] Add tests that orphaned runs/projects become Paused while resume state survives, oversized resume tasks collapse to their first bounded line, dead-PID non-expired locks recover, and matching live locks remain protected.
- [ ] Run focused tests and verify the new behaviors fail.
- [ ] Implement cleanup/reconciliation, process-aware lock recovery, startup integration, and persist the project snapshot as Running at run creation.
- [ ] Run focused tests; expect all to pass.

### Task 4: Android validation environment

**Files:**
- Create: `src/AutoDevRunner/Services/ValidationEnvironmentResolver.cs`
- Modify: `src/AutoDevRunner/Config/AutoDevOptions.cs`
- Modify: `src/AutoDevRunner/Providers/ProcessRunner.cs`
- Modify: `src/AutoDevRunner/Services/RunOrchestrator.cs`
- Modify: `src/AutoDevRunner/appsettings.json`
- Test: `src/AutoDevRunner.Tests/ValidationEnvironmentResolverTests.cs`

- [ ] Add tests proving Gradle on Windows prefers configured Java, falls back to Android Studio JBR, and leaves non-Gradle commands unchanged.
- [ ] Run focused tests and verify failure because the resolver is missing.
- [ ] Add `Validation:JavaHome`, process environment overrides, and resolver use for validation commands.
- [ ] Run focused tests; expect all to pass.
- [ ] Run `gradlew.bat assembleDebug` in the Android target through the resolved environment and expect exit code zero.

### Task 5: Cross-process live event store and SSE API

**Files:**
- Create: `src/AutoDevRunner/Services/RunEventStore.cs`
- Modify: `src/AutoDevRunner/Program.cs`
- Modify: `src/AutoDevRunner/Services/RunOrchestrator.cs`
- Modify: `src/AutoDevRunner/Api/ApiEndpoints.cs`
- Test: `src/AutoDevRunner.Tests/RunEventStoreTests.cs`

- [ ] Add tests for deterministic event paths, append/replay ordering, JSON escaping, and terminal-event recognition.
- [ ] Run focused tests and verify failure because the event store is missing.
- [ ] Implement an append-only, file-share-safe JSONL sink and reader.
- [ ] Emit lifecycle/provider/heartbeat/terminal messages from the orchestrator.
- [ ] Add `GET /api/runs/{id}/events` using `text/event-stream`, replaying existing lines and tailing until terminal/disconnect.
- [ ] Run focused tests and API compilation tests; expect all to pass.

### Task 6: Dashboard live console

**Files:**
- Modify: `src/AutoDevRunner/wwwroot/app.js`
- Modify: `src/AutoDevRunner/wwwroot/styles.css`

- [ ] Add a live activity panel to Run detail and connect `EventSource` only while status is Pending/Running.
- [ ] Append lifecycle/agent/command/file/heartbeat events, cap retained DOM lines, close on terminal, and refresh final run details.
- [ ] Keep the existing completed markdown log loader as the historical fallback.
- [ ] Run a local dashboard and verify an SSE fixture renders/reconnects without console errors.

### Task 7: Git ownership and committed-change detection

**Files:**
- Modify: `src/AutoDevRunner/Services/PromptBuilder.cs`
- Modify: `src/AutoDevRunner/Services/GuardrailService.cs`
- Modify: `src/AutoDevRunner/Services/GitService.cs`
- Modify: `src/AutoDevRunner/Services/RunOrchestrator.cs`
- Test: `src/AutoDevRunner.Tests/GuardrailPromptTests.cs`
- Test: `src/AutoDevRunner.Tests/Phase2Tests.cs`

- [ ] Add tests requiring prompts to tell the provider not to commit/push and parsing committed name-status output since a base SHA.
- [ ] Run focused tests and verify failure against current prompt/GitService behavior.
- [ ] Capture starting HEAD, merge committed and working-tree changes for risk/artifact reporting, and record a provider-created HEAD defensively without double-committing.
- [ ] Run focused tests; expect all to pass.

### Task 8: Fail-safe final persistence and end-to-end verification

**Files:**
- Modify: `src/AutoDevRunner/Services/RunOrchestrator.cs`
- Test: `src/AutoDevRunner.Tests/Phase2Tests.cs`
- Test: `src/AutoDevRunner.Tests/ProjectLearningStateTests.cs`

- [ ] Add a focused policy/test seam proving terminal run/project state is established before optional learning work and learning failure cannot restore Running.
- [ ] Run the focused test and verify it fails before the refactor.
- [ ] Persist terminal state first; wrap learning, retrospective, metadata, and email follow-ups independently with warnings and safe change-tracker cleanup.
- [ ] Run `dotnet build AutoDevRunner.sln`; expect zero errors.
- [ ] Run `dotnet test AutoDevRunner.sln`; expect zero failures.
- [ ] Run a read-only Codex JSONL smoke probe and verify `turn.completed`, final text, usage, and exit code zero.
- [ ] Publish to a temporary output directory, start the app on a non-production port, verify `/health`, `/api/runs`, and SSE response headers, then stop the temporary process.

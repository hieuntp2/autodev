# AutoDev Improvement Brief for Claude

> Recommended Claude mode: **High reasoning / careful implementation**  
> Goal: improve the existing AutoDev project without rewriting it from scratch.

---

## 1. Project Context

AutoDev is a local-first autonomous development orchestrator.

The intended workflow is:

```text
Project Goal / Roadmap / Backlog
        ↓
Planner chooses or creates a small task
        ↓
PromptBuilder creates executor prompt
        ↓
Codex CLI / Claude CLI executes the task in the target repo
        ↓
AutoDev verifies build/test/output
        ↓
AutoDev writes logs, run report, retrospective, and next actions
        ↓
Next daily run learns from previous runs
```

This is not meant to become a big SaaS platform yet. It should first become a reliable personal automation tool that can run on a Windows PC and improve a target project every day.

Primary target project for now:

- Android Pixel Pet Companion / AI pet companion.
- Small daily improvements.
- Creative but controlled feature evolution.
- Uses `.ai-runner` project goal files to guide future work.

The user wants AutoDev to work like an autonomous development assistant:

- OpenAI is used as the creative planner/reviewer, with cost kept low.
- Codex CLI and/or Claude CLI are used as executors.
- The system can continue when one executor fails or quota is exhausted, but it must stop safely when no valid executor is available.
- It should run manually first, then through Windows Task Scheduler or a lightweight Windows Service.
- A local web dashboard is useful, but MVP must not require login.

---

## 2. Current Known State

Recent work already done or partially done:

1. **Project Goal Layer**
   - `ProjectGoalService` reads multiple `.ai-runner/*.md` files.
   - Missing goal files should only log warnings, not break the run.
   - `PromptBuilder` compacts long goal documents instead of dumping everything into the prompt.

2. **Sample project template**
   - A sample Pixel Pet project exists under something like:
     - `samples/pixel-pet/.ai-runner/PROJECT_GOAL.md`
     - `samples/pixel-pet/.ai-runner/ROADMAP.md`
   - The goal is a pet companion that grows through small testable daily changes.

3. **Task lifecycle**
   - Lifecycle includes states such as idea, planned, ready, running, learned, and failed.
   - A `Failed` state was added.
   - Run sidecar JSON files are stored under `.ai-runner/runs/*.json`.
   - The current implementation avoids forcing a DB schema change just for lifecycle state.

4. **Task selection**
   - Planner should be the main task selection mechanism.
   - `TaskProposer` can be a local fallback based on backlog, ideas, and maintenance signals.
   - Empty backlog should not mean AutoDev stops forever; it should be able to propose a small useful task.

---

## 3. Main Problem To Solve

The current project needs to become a dependable autonomous loop, not just a set of useful services.

The biggest gaps to fix are:

1. **Run reliability**
   - Every run should have a clear workspace, logs, status, and final report.
   - Failed runs must be visible and recoverable.

2. **Task quality**
   - Tasks should be small, testable, project-goal-aligned, and safe.
   - AutoDev should avoid vague “improve the app” tasks.

3. **Executor safety**
   - Codex/Claude executor prompts must include boundaries, verification commands, and expected output.
   - If an executor fails, AutoDev should classify the failure instead of blindly retrying forever.

4. **Verification discipline**
   - No task should be considered successful unless build/test/validation commands pass.
   - If verification is not configured, AutoDev should explicitly mark the task as “not verifiable” instead of pretending success.

5. **Learning loop**
   - After each run, AutoDev should write what changed, what failed, what it learned, and what should happen next.
   - Future planning should read recent run summaries.

6. **Cost control**
   - OpenAI should be called only when it adds value: planning, reviewing, generating next tasks, or creative direction.
   - Do not use OpenAI for simple file scanning that local code can do.

---

## 4. Implementation Principles

Claude must follow these principles:

1. **Do not rewrite the project.**
   - Inspect current architecture first.
   - Improve existing services and folder structure.
   - Preserve working code.

2. **Prefer boring reliability over fancy autonomy.**
   - A simple run loop that works is better than a complex agent that often fails.

3. **Everything must be auditable.**
   - Every run should leave files that a human can inspect.
   - Logs and reports should be plain text/Markdown where possible.

4. **Small daily tasks only.**
   - Each task should fit one run.
   - Avoid giant refactors unless explicitly listed in the roadmap.

5. **Local-first.**
   - Works on Windows.
   - Can be triggered manually.
   - Can later be scheduled through Windows Task Scheduler.

6. **No secrets leakage.**
   - Never write API keys into prompts, logs, reports, or screenshots.
   - Executor prompts must explicitly instruct the executor not to expose secrets.

7. **Target repo isolation.**
   - AutoDev should stay separate from the target project.
   - It should operate on a configured target path.

8. **Failure is a valid outcome.**
   - Failed tasks should be logged, classified, and converted into next actions.
   - Do not hide failures.

---

## 5. Desired `.ai-runner` Contract

Each target project should be able to contain:

```text
.ai-runner/
  PROJECT_GOAL.md
  ROADMAP.md
  BACKLOG.md
  IDEAS.md
  ARCHITECTURE.md
  CONSTRAINTS.md
  RUN_POLICY.md
  VERIFY.md
  runs/
    2026-07-09_001/
      input-context.md
      selected-task.md
      executor-prompt.md
      executor-output.md
      verify-log.txt
      changed-files.md
      run-report.md
      retrospective.md
      status.json
```

Not every file must exist. Missing optional files should warn, not crash.

Minimum required files:

```text
.ai-runner/PROJECT_GOAL.md
.ai-runner/RUN_POLICY.md
```

Recommended files:

```text
.ai-runner/ROADMAP.md
.ai-runner/BACKLOG.md
.ai-runner/VERIFY.md
```

---

## 6. Required Improvements

### Phase 0 — Audit Current Project

Before coding, inspect the current codebase and create/update:

```text
docs/AUTODEV_CURRENT_STATE.md
```

This document should include:

- Current projects/modules.
- Existing services and responsibilities.
- Current run flow.
- Existing `.ai-runner` support.
- Existing task lifecycle.
- Existing executor integration.
- Existing verification behavior.
- Biggest gaps.
- Proposed implementation plan.

Do not skip this. It prevents accidental rewrites.

Acceptance criteria:

- `AUTODEV_CURRENT_STATE.md` exists.
- It reflects actual code, not assumptions.
- It lists which files will be modified next.

---

### Phase 1 — Stabilize Run Workspace

Implement or improve a `RunWorkspaceService`.

Each run should create a unique folder:

```text
.ai-runner/runs/yyyy-MM-dd_HHmmss/
```

The folder should contain:

```text
input-context.md
selected-task.md
executor-prompt.md
executor-output.md
verify-log.txt
run-report.md
retrospective.md
status.json
```

`status.json` should include at least:

- run id
- project path
- start time
- end time
- selected task title
- selected executor
- status: success / failed / partial / skipped
- verification status
- changed file count
- error category, if any

Keep the JSON simple. Do not over-engineer schemas.

Acceptance criteria:

- Manual run creates a complete run folder.
- Failed run still creates a useful report.
- Missing optional files do not crash the run.

---

### Phase 2 — Improve Task Selection

Implement task selection with this priority:

1. Continue an unfinished safe task if it has clear next steps.
2. Pick a ready task from `BACKLOG.md`.
3. Pick a task from `ROADMAP.md`.
4. Use planner to propose a small task from project goal.
5. Use local fallback proposer if planner/OpenAI is unavailable.

Every selected task must have:

```text
Title
Reason
Expected files or areas
Expected output
Validation command
Risk level: low / medium / high
Stop condition
```

Rules:

- Prefer low-risk tasks.
- Avoid touching too many files.
- Avoid infrastructure refactors unless explicitly requested.
- If there is no validation command, mark the task as low confidence.

Acceptance criteria:

- Empty backlog still produces a reasonable small task.
- Task selection is written to `selected-task.md`.
- Task reason references project goal or roadmap.

---

### Phase 3 — Harden PromptBuilder

Improve executor prompts so Codex/Claude receives a clear contract.

Executor prompt should include:

1. Project goal summary.
2. Selected task.
3. Relevant constraints.
4. Files likely involved.
5. Exact validation command.
6. Definition of done.
7. Safety rules.
8. Output format expected from executor.

The prompt must tell the executor:

- Do not rewrite unrelated parts.
- Do not modify secrets or environment files.
- Do not delete user files.
- Prefer minimal diffs.
- Run validation if possible.
- Report changed files and any commands run.

Acceptance criteria:

- `executor-prompt.md` is human-readable.
- Prompt does not dump huge full documents unnecessarily.
- Prompt includes recent lessons from previous runs when available.

---

### Phase 4 — Executor Adapter Layer

Implement or improve an executor adapter interface.

Suggested interface responsibilities:

```text
ExecutorAdapter
  Name
  IsAvailable()
  Execute(prompt, workingDirectory, timeout)
  Capture stdout/stderr
  Return exit code and output
```

Supported adapters:

1. `CodexCliExecutor`
2. `ClaudeCliExecutor`
3. `DryRunExecutor`

Behavior:

- If Codex quota is exhausted or command fails with quota/rate-limit, classify it as quota failure.
- If Claude CLI is configured, optionally fall back to Claude.
- If no executor is available, create a skipped run report instead of crashing.
- Do not retry indefinitely.

Acceptance criteria:

- Executor failure does not crash the whole process.
- Failure category is visible in report.
- Dry run mode can generate prompt and report without modifying target repo.

---

### Phase 5 — Verification System

Implement project-specific verification based on `.ai-runner/VERIFY.md` or config.

Example `VERIFY.md`:

```md
# Verify Commands

## Default
```bash
./gradlew test
```

## Android build
```bash
./gradlew assembleDebug
```
```

For Windows projects, support commands like:

```powershell
dotnet build
npm test
npm run build
```

Rules:

- Verification command should run after executor finishes.
- Capture logs into `verify-log.txt`.
- Mark run as failed if verification fails.
- If verification is missing, mark as `partial` or `not verified`, not success.

Acceptance criteria:

- Successful validation is clearly shown.
- Failed validation is clearly shown.
- Verification output is stored in the run folder.

---

### Phase 6 — Git Safety

Add or improve Git safety checks.

Before execution:

- Detect if target repo is dirty.
- Write dirty state into `input-context.md`.
- Do not overwrite unrelated user changes.

Preferred behavior:

- Create a branch or worktree per run when configured.
- Commit only after verification succeeds, unless config says manual commit.
- Never auto-commit secrets, `.env`, key files, or huge generated binaries.

Protected paths should include at least:

```text
.env
.env.*
*.pfx
*.pem
*.key
secrets.json
appsettings.Production.json
node_modules/
bin/
obj/
build/
dist/
.gradle/
.git/
```

Acceptance criteria:

- Dirty repo is detected.
- Protected files are not modified silently.
- Run report includes changed files.

---

### Phase 7 — Learning Loop

After every run, generate:

```text
retrospective.md
```

It should include:

- What was attempted.
- What changed.
- What worked.
- What failed.
- Why it failed, if known.
- What should be tried next.
- What should be avoided next time.

Future task selection should read the last 3–5 retrospectives.

Acceptance criteria:

- Failed runs produce useful next-step recommendations.
- Planner can use previous lessons.
- Repeated failures are detected and not retried forever.

---

### Phase 8 — Local Dashboard, No Login

Add or improve a simple local dashboard only if it fits the current architecture.

MVP dashboard should show:

- Current project path.
- Last run status.
- Recent runs.
- Last selected task.
- Last executor used.
- Last verification result.
- Button/API endpoint to trigger a manual run.

No login is needed for now.

Keep it local only:

```text
http://localhost:<port>
```

Suggested API endpoints:

```text
GET  /api/status
GET  /api/runs
GET  /api/runs/{id}
POST /api/run
POST /api/run/dry
```

Acceptance criteria:

- Dashboard is simple and useful.
- It does not block CLI usage.
- It does not require auth.
- It does not introduce a heavy database unless one already exists.

---

### Phase 9 — Windows Scheduler / Service Support

Manual CLI must work first.

Suggested CLI commands:

```powershell
autodev run --project "D:\Projects\pixel-pet"
autodev run --project "D:\Projects\pixel-pet" --dry-run
autodev status --project "D:\Projects\pixel-pet"
```

Then add documentation:

```text
docs/WINDOWS_TASK_SCHEDULER.md
```

It should explain:

- How to run AutoDev once per day.
- How to run it manually.
- Where logs are stored.
- How to disable it.
- How to change target project.

Windows Service is optional. Do not build a complex service before CLI is stable.

Acceptance criteria:

- User can schedule daily runs with Windows Task Scheduler.
- User can stop/disable the schedule easily.
- Manual run remains the primary debugging path.

---

### Phase 10 — OpenAI Brain Cost Control

OpenAI should be used carefully.

Use OpenAI for:

- Creative planning.
- Choosing the next task when backlog is empty.
- Reviewing failed runs.
- Generating roadmap suggestions.
- Game/pet design reasoning.

Avoid OpenAI for:

- Listing files.
- Reading simple config.
- Basic status checks.
- Anything deterministic local code can do.

Suggested strategy:

```text
Use local backlog first.
Use local proposer second.
Use OpenAI planner only when needed.
Use OpenAI reviewer after failure or every N successful runs.
Compact context aggressively.
Never send secrets.
```

For game-design knowledge base:

- Only ingest public/allowed materials.
- Keep API keys in backend/service config only.
- Client/dashboard should call local AutoDev API, not OpenAI directly.

Acceptance criteria:

- Config can disable OpenAI entirely.
- Dry run shows whether OpenAI would be used.
- Reports include rough AI usage when available.

---

## 7. Android Pixel Pet Target Requirements

The sample target should push the Android Pixel Pet project forward.

Good daily tasks include:

- Add one small animation state.
- Improve animation timing.
- Add data-driven JSON config for states.
- Improve behavior transitions.
- Add idle variation.
- Add one unit test around state selection.
- Add one debug screen for current pet state.
- Refactor one small renderer function safely.

Bad daily tasks:

- Rewrite the whole renderer.
- Replace the architecture.
- Add cloud backend too early.
- Add login/payment/social features.
- Add large art packs without validation.

The pet should feel like it is developing day by day, but the codebase must remain stable.

---

## 8. Suggested Implementation Order

Claude should implement in this order:

1. Audit current code and write `docs/AUTODEV_CURRENT_STATE.md`.
2. Stabilize run workspace creation.
3. Ensure selected task is written clearly.
4. Improve executor prompt contract.
5. Add/clean executor adapter behavior.
6. Add verification command support.
7. Add run report and retrospective.
8. Add Git safety checks.
9. Add dry-run mode.
10. Add or improve local dashboard/API.
11. Add Windows Task Scheduler documentation.
12. Improve Pixel Pet sample `.ai-runner` files.

Do not jump directly to dashboard or Windows Service before run reliability works.

---

## 9. Definition of Done

The improvement is done when all of the following are true:

1. A user can run:

```powershell
autodev run --project "<target-project-path>" --dry-run
```

and get a complete run folder without modifying the target repo.

2. A user can run:

```powershell
autodev run --project "<target-project-path>"
```

and AutoDev will:

- Load `.ai-runner` goal files.
- Select or propose one small task.
- Build a clear executor prompt.
- Run configured executor or safely skip.
- Run verification if configured.
- Write run report.
- Write retrospective.
- Mark success/failure/partial honestly.

3. A failed executor or failed verification produces a useful report instead of a broken run.

4. No secrets are printed in prompts or logs.

5. The docs explain how to run manually and how to schedule daily runs on Windows.

6. The Pixel Pet sample can serve as a real target project template.

---

## 10. Final Instruction To Claude

Act as a senior .NET + automation engineer.

Your job is to improve the existing AutoDev project into a reliable autonomous daily development runner.

Do not overbuild. Do not rewrite. Do not add unnecessary SaaS features.

Prioritize:

1. Run reliability.
2. Clear logs and reports.
3. Safe executor integration.
4. Verification discipline.
5. Small project-goal-aligned tasks.
6. Windows local usability.

When unsure, choose the simpler implementation that makes the run loop more dependable.


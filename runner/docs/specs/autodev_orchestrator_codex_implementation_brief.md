# AutoDev Orchestrator — Codex Implementation Brief

> Target: Build a reusable C# automation service/CLI that can autonomously plan, implement, verify, log, and report daily development work for any target project by using ChatGPT/OpenAI API for planning/review and Codex CLI for implementation.

---

## 0. Recommended Reasoning / Model Effort

**Recommended effort for Codex:** Extra High

This is not a small feature task. It is a project scaffold + automation framework. Codex must plan carefully, keep the implementation modular, avoid overengineering the first version, and deliver a runnable MVP.

---

## 1. Goal

Build a separate project named **AutoDev Orchestrator**.

The system must be independent from the target project it modifies. It should be able to run against different projects by changing a project config file.

Example target projects:

- AI Pet Companion Android app
- nopCommerce tour website
- hocgi.vn
- workstatio.vn
- Any future repo with source code and docs

The orchestrator must:

1. Read project requirement/docs/source context.
2. Create one daily workspace folder.
3. Ask ChatGPT/OpenAI API to create the daily plan and Codex task.
4. Call Codex CLI to implement the generated task.
5. Run configured build/test commands.
6. Save logs, snapshots, diffs, and reports.
7. Commit and optionally push changes to the configured branch.
8. Keep daily retrospective and project memory.
9. Support Windows Task Scheduler by exposing CLI commands.

---

## 2. Non-Goals

Do **not** build these in the MVP:

- Web dashboard
- Database storage
- Multi-user authentication
- Telegram/OpenClaw integration
- Complex queue system
- Distributed workers
- Auto-merge to main branch
- Secret scanning SaaS integration
- Full Windows Service implementation

For the first version, build a clean **C# Console CLI**. It can later be wrapped by Windows Task Scheduler or converted to a Worker Service.

---

## 3. Core Design Principle

The orchestrator is separate from the project being coded.

```text
D:\AutoDev\
  runner\              # AutoDev Orchestrator source/build output
  projects\            # Project configs
  templates\           # Prompt templates
  memory\              # Global memory
  workspaces\          # Daily run folders and logs

D:\Projects\
  ai-pet-android\      # Target repo
  nopcommerce-tour\    # Another target repo
```

The target repo should contain only the project source/docs required for development.

The AutoDev workspace should contain:

- daily inputs
- daily plans
- Codex prompts
- build logs
- diffs
- reports
- retrospective
- metadata

---

## 4. High-Level Architecture

```text
AutoDev.Cli
  └── command line entrypoint

AutoDev.Core
  ├── project config models
  ├── daily run context
  ├── workspace creation
  ├── pipeline orchestration
  └── step result models

AutoDev.ProjectContext
  ├── reads target project docs
  ├── snapshots important files
  ├── builds context package for planner
  └── maintains project-memory.md

AutoDev.OpenAI
  ├── calls OpenAI Responses API
  ├── PlannerClient
  ├── ReviewerClient
  └── RetrospectiveClient

AutoDev.Codex
  ├── calls Codex CLI
  └── captures output logs

AutoDev.Git
  ├── checkout/pull
  ├── status
  ├── diff
  ├── commit
  └── push

AutoDev.Verification
  ├── runs build commands
  ├── runs test commands
  └── writes verification result
```

---

## 5. CLI Commands

Implement these commands:

```bash
autodev run --project ai-pet
autodev plan --project ai-pet
autodev implement --project ai-pet
autodev verify --project ai-pet
autodev review --project ai-pet
autodev retrospective --project ai-pet
autodev status --project ai-pet
```

### MVP Requirement

At minimum, implement:

```bash
autodev run --project ai-pet
autodev status --project ai-pet
```

Other commands can be routed internally but should be structured clearly for later expansion.

---

## 6. Suggested .NET Solution Structure

Create:

```text
AutoDev/
  AutoDev.sln
  src/
    AutoDev.Cli/
      AutoDev.Cli.csproj
      Program.cs

    AutoDev.Core/
      AutoDev.Core.csproj
      Models/
      Services/

    AutoDev.OpenAI/
      AutoDev.OpenAI.csproj
      OpenAIPlannerClient.cs
      OpenAIRetrospectiveClient.cs

    AutoDev.Codex/
      AutoDev.Codex.csproj
      CodexRunner.cs

    AutoDev.Git/
      AutoDev.Git.csproj
      GitService.cs

    AutoDev.Verification/
      AutoDev.Verification.csproj
      CommandRunner.cs
      VerificationRunner.cs

  projects/
    ai-pet.json

  templates/
    planner.md
    codex-task.md
    reviewer.md
    retrospective.md

  memory/
    global-lessons.md
    global-agent-rules.md

  README.md
```

Use .NET 8 or newer.

---

## 7. Project Config Format

Create config files under:

```text
projects/{projectId}.json
```

Example:

```json
{
  "projectId": "ai-pet",
  "displayName": "AI Pet Companion Android",
  "repoPath": "D:\\Projects\\ai-pet-android",
  "branch": "ai/autonomous-30-days",
  "projectType": "android",
  "mainRequirementFile": "docs/product/vision.md",
  "scopeFile": "docs/product/scope.md",
  "roadmapFile": "docs/product/roadmap.md",
  "backlogFile": "docs/active/backlog.md",
  "statusFile": "docs/active/implementation-status.md",
  "agentRulesFile": "AGENTS.md",
  "buildCommands": [
    ".\\gradlew.bat build"
  ],
  "testCommands": [
    ".\\gradlew.bat test"
  ],
  "allowedWritePaths": [
    "app/",
    "brain/",
    "memory/",
    "perception/",
    "ui-avatar/",
    "docs/active/"
  ],
  "protectedPaths": [
    "docs/product/vision.md",
    "docs/product/scope.md",
    ".env",
    "local.properties",
    "keystore/",
    "secrets/"
  ],
  "schedule": {
    "enabled": true,
    "mode": "windows-task-scheduler",
    "time": "01:00",
    "timezone": "Asia/Ho_Chi_Minh"
  },
  "dailyGoal": "Improve the Android AI Pet Companion so the pet feels more alive through mood, memory, avatar reaction, perception, audio, and optional cloud AI.",
  "maxTasksPerRun": 1,
  "maxDiffLines": 800,
  "allowRequirementProposal": true,
  "allowRequirementDirectEdit": false,
  "allowAutoCommit": true,
  "allowAutoPush": true,
  "commitOnlyIfBuildPasses": true
}
```

---

## 8. Daily Workspace Structure

Every run must create a folder:

```text
workspaces/{projectId}/{yyyy-MM-dd}/
```

Example:

```text
workspaces/ai-pet/2026-06-04/
  metadata.json
  run.log

  00-input/
    project-config.snapshot.json
    requirement.snapshot.md
    scope.snapshot.md
    roadmap.snapshot.md
    backlog.snapshot.md
    implementation-status.snapshot.md
    agent-rules.snapshot.md
    git-status-before.txt
    git-log-before.txt

  01-planning/
    planner-prompt.md
    planner-output.md
    daily-plan.md
    selected-task.md
    codex-task.md
    task.json
    run-contract.md

  02-implementation/
    codex-output.log
    git-status-after-implementation.txt
    changed-files.txt
    git-diff.patch

  03-verification/
    build-output.log
    test-output.log
    verification-result.md
    failure-analysis.md

  04-review/
    reviewer-prompt.md
    reviewer-output.md
    reviewer-fixes.log
    final-diff.patch

  05-retrospective/
    daily-report.md
    ai-learning-log.md
    next-day-suggestions.md
    proposed-requirement-updates.md
    proposed-agent-rule-updates.md
```

---

## 9. metadata.json

Create one `metadata.json` for each run:

```json
{
  "projectId": "ai-pet",
  "runDate": "2026-06-04",
  "branch": "ai/autonomous-30-days",
  "status": "created",
  "commitBefore": null,
  "commitAfter": null,
  "plannerModel": "gpt-5.5",
  "implementer": "codex-cli",
  "buildPassed": false,
  "testPassed": false,
  "filesChangedCount": 0,
  "linesAdded": 0,
  "linesDeleted": 0,
  "requirementProposalCreated": false,
  "completedSteps": [],
  "failedStep": null,
  "startedAt": null,
  "finishedAt": null
}
```

Update this file after each step.

---

## 10. Pipeline

Implement the `run` command as this pipeline:

```text
1. Load project config
2. Create daily workspace
3. Git sync target repo
4. Snapshot inputs
5. Build planner prompt
6. Call OpenAI planner
7. Write daily-plan.md, task.json, codex-task.md, run-contract.md
8. Call Codex CLI with codex-task.md
9. Capture Codex logs and git diff
10. Run build/test commands
11. Validate protected paths and diff size
12. If needed, call reviewer/fixer once
13. Generate daily report and retrospective
14. Commit if allowed and safe
15. Push if allowed
16. Update project-memory.md
17. Write final metadata
```

---

## 11. Guardrails

The service must enforce some guardrails in C#, not only in prompts.

### 11.1 Protected paths

If changed files include anything under `protectedPaths`, mark run as risky.

If `allowRequirementDirectEdit == false`, do not allow direct changes to:

```text
docs/product/vision.md
docs/product/scope.md
```

Instead, the planner/reviewer must write proposals to:

```text
05-retrospective/proposed-requirement-updates.md
```

### 11.2 Diff size

If diff is larger than `maxDiffLines`, do not auto-commit. Save the patch in workspace.

### 11.3 Build failure

If `commitOnlyIfBuildPasses == true` and build fails:

- call reviewer/fixer once
- run build again
- if still failing, do not commit
- write failure-analysis.md
- save patch only

### 11.4 Dependency changes

If these files change, require extra note in daily report:

```text
build.gradle
settings.gradle
gradle.properties
package.json
*.csproj
Directory.Packages.props
```

### 11.5 Secrets

Never print environment variables or API keys into logs.

Do not snapshot:

```text
.env
appsettings.Production.json
local.properties
secrets/
keystore/
```

---

## 12. OpenAI Planner Integration

Use the OpenAI API through `HttpClient` for MVP.

Read API key from:

```text
OPENAI_API_KEY
```

Do not hardcode it.

Create:

```csharp
OpenAIPlannerClient
```

Responsibilities:

- Accept `PlannerRequest`
- Send prompt to OpenAI Responses API
- Return raw text output
- Save raw response to workspace if useful, without secrets

### PlannerRequest

```csharp
public sealed class PlannerRequest
{
    public required ProjectConfig Project { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ProjectContext { get; init; }
    public required string Template { get; init; }
}
```

---

## 13. Planner Prompt Template

Create:

```text
templates/planner.md
```

Content:

```md
You are the Product Manager and Software Architect for an autonomous software development system.

You are planning one daily development task for Codex.

## Project

Project ID: {{projectId}}
Display Name: {{displayName}}
Project Type: {{projectType}}
Daily Goal: {{dailyGoal}}

## Rules

- Create exactly one small vertical slice for today's implementation.
- Prefer changes that produce visible product progress.
- Avoid large rewrites.
- Avoid scope creep.
- Do not directly edit protected requirement files.
- If product requirements should change, propose them separately.
- Codex will implement the generated task.
- The task must be buildable and testable.
- The plan must be specific enough for Codex CLI to execute.

## Protected Paths

{{protectedPaths}}

## Allowed Write Paths

{{allowedWritePaths}}

## Build Commands

{{buildCommands}}

## Test Commands

{{testCommands}}

## Project Context

{{projectContext}}

## Output Requirements

Return Markdown with these sections:

# Daily Plan

## Goal

## Why This Matters

## Selected Task

## Codex Task

## Likely Files Or Modules

## Acceptance Criteria

## Build And Test Commands

## Risks

## Must Not Do

## Fallback Task If Blocked

# task.json

Return a JSON code block with:
- title
- type
- priority
- scope
- reason
- modules
- acceptanceCriteria
- mustNotDo
- buildCommands
- riskLevel

# Run Contract

## Today Goal

## Allowed Changes

## Forbidden Changes

## Max Diff Size

## Required Verification

## Commit Policy

## Rollback Policy
```

---

## 14. Codex Task Prompt

Create:

```text
templates/codex-task.md
```

Content:

```md
You are the implementation agent for this project.

Follow AGENTS.md strictly if present.

You are running as part of an autonomous daily development workflow.

## Today's Task

{{selectedTask}}

## Run Contract

{{runContract}}

## Rules

- Implement only today's task.
- Do not rewrite unrelated code.
- Do not expand product scope.
- Do not edit protected files.
- Do not add large dependencies unless explicitly required.
- Prefer small, testable changes.
- Run the configured build/test commands if possible.
- Update project docs/status if the target project has docs/active/implementation-status.md or docs/active/backlog.md.
- If requirements should change, write a proposal rather than editing product vision/scope directly.

## Required Final Output

At the end, summarize:
- What changed
- Files changed
- Build/test result
- Product impact
- Remaining risks
```

---

## 15. Codex CLI Integration

Create:

```csharp
CodexRunner
```

The runner must call Codex CLI non-interactively.

Target command shape:

```bash
codex exec --cd "{repoPath}" --ask-for-approval never --sandbox workspace-write -
```

The prompt should be piped through standard input.

Capture:

- stdout
- stderr
- exit code
- duration

Write to:

```text
02-implementation/codex-output.log
```

If Codex executable is missing, fail with a clear message:

```text
Codex CLI is not installed or not available in PATH.
```

---

## 16. Git Integration

Create:

```csharp
GitService
```

Methods:

```csharp
Task CheckoutAndPullAsync(ProjectConfig project);
Task<string> GetCurrentCommitAsync(ProjectConfig project);
Task<string> GetStatusAsync(ProjectConfig project);
Task<string> GetChangedFilesAsync(ProjectConfig project);
Task<string> GetDiffAsync(ProjectConfig project);
Task CommitAsync(ProjectConfig project, string message);
Task PushAsync(ProjectConfig project);
```

Use external `git` process for MVP.

Do not depend on LibGit2Sharp initially.

---

## 17. Build/Test Verification

Create:

```csharp
VerificationRunner
```

Run configured commands from `ProjectConfig`.

Each command must execute in the target repo path.

Save logs:

```text
03-verification/build-output.log
03-verification/test-output.log
```

Create:

```text
03-verification/verification-result.md
```

It must include:

```md
# Verification Result

## Build

- Command:
- Passed:
- Exit Code:

## Tests

- Command:
- Passed:
- Exit Code:

## Summary

## Failure Notes
```

---

## 18. Retrospective

Create:

```text
templates/retrospective.md
```

Prompt:

```md
You are the retrospective agent for the autonomous development system.

Read the daily plan, Codex output, verification result, changed files, and final diff.

Write a concise retrospective.

Output:

# Daily Report

## What Was Planned

## What Was Implemented

## Build/Test Result

## Product Impact

## Risks

## What The AI Should Do Better Tomorrow

## Suggested Next Task

# AI Learning Log

## Good Decisions

## Mistakes

## Repeated Failure Patterns

## Rule Improvements Proposed

# Proposed Requirement Updates

Only include proposals. Do not rewrite the official scope directly.

# Proposed Agent Rule Updates

Only include proposals.
```

---

## 19. Project Memory

Maintain:

```text
workspaces/{projectId}/project-memory.md
```

This file should be updated after each run.

Suggested content:

```md
# Project Memory

## Current Product State

## Implemented Features

## Known Problems

## Repeated Failure Patterns

## Good Next Directions

## Do Not Repeat

## Pending Requirement Proposals
```

The planner should receive this file as compressed long-term context.

Do not feed all previous daily logs to OpenAI every day.

---

## 20. Windows Task Scheduler

Do not implement complex internal scheduling in MVP.

The CLI should be called by Windows Task Scheduler.

Example command:

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

---

## 21. README Requirements

Create a useful `README.md` with:

```md
# AutoDev Orchestrator

## What It Does

## Requirements

- .NET 8+
- Git
- Codex CLI
- OpenAI API key
- Target repo available locally

## Setup

## Project Config

## Running Manually

## Scheduling With Windows Task Scheduler

## Workspace Structure

## Safety Rules

## Troubleshooting
```

---

## 22. Example First Project Config

Create:

```text
projects/ai-pet.json
```

Use safe placeholder paths:

```json
{
  "projectId": "ai-pet",
  "displayName": "AI Pet Companion Android",
  "repoPath": "D:\\Projects\\ai-pet-android",
  "branch": "ai/autonomous-30-days",
  "projectType": "android",
  "mainRequirementFile": "docs/product/vision.md",
  "scopeFile": "docs/product/scope.md",
  "roadmapFile": "docs/product/roadmap.md",
  "backlogFile": "docs/active/backlog.md",
  "statusFile": "docs/active/implementation-status.md",
  "agentRulesFile": "AGENTS.md",
  "buildCommands": [
    ".\\gradlew.bat build"
  ],
  "testCommands": [
    ".\\gradlew.bat test"
  ],
  "allowedWritePaths": [
    "app/",
    "brain/",
    "memory/",
    "perception/",
    "ui-avatar/",
    "docs/active/"
  ],
  "protectedPaths": [
    "docs/product/vision.md",
    "docs/product/scope.md",
    ".env",
    "local.properties",
    "keystore/",
    "secrets/"
  ],
  "schedule": {
    "enabled": true,
    "mode": "windows-task-scheduler",
    "time": "01:00",
    "timezone": "Asia/Ho_Chi_Minh"
  },
  "dailyGoal": "Improve the Android AI Pet Companion so the pet feels more alive through mood, memory, avatar reaction, perception, audio, and optional cloud AI.",
  "maxTasksPerRun": 1,
  "maxDiffLines": 800,
  "allowRequirementProposal": true,
  "allowRequirementDirectEdit": false,
  "allowAutoCommit": true,
  "allowAutoPush": false,
  "commitOnlyIfBuildPasses": true
}
```

Default `allowAutoPush` should be `false` for MVP. The user can turn it on later.

---

## 23. Definition of Done

The project is done when:

1. `dotnet build` passes.
2. `autodev run --project ai-pet` can execute against a real or sample repo.
3. A daily workspace folder is created.
4. Input snapshots are saved.
5. Planner prompt and output are saved.
6. `daily-plan.md`, `task.json`, `codex-task.md`, and `run-contract.md` are generated.
7. Codex CLI is called with the generated task.
8. Codex output is logged.
9. Git diff and changed files are saved.
10. Build/test commands are run and logged.
11. Verification result is written.
12. Retrospective is generated.
13. Metadata is updated through the run.
14. Protected path validation exists.
15. Build-fail commit policy exists.
16. README explains how to run manually and how to schedule with Windows Task Scheduler.

---

## 24. Implementation Order

Codex should implement in this order:

### Step 1 — Solution scaffold

Create solution and projects.

### Step 2 — Config loading

Load `projects/{projectId}.json`.

### Step 3 — Workspace creation

Create daily folder and subfolders.

### Step 4 — Command runner

Implement reusable process runner.

### Step 5 — Git service

Implement checkout, pull, status, diff.

### Step 6 — Snapshot service

Copy configured docs and git info into `00-input`.

### Step 7 — OpenAI planner client

Call OpenAI API using `OPENAI_API_KEY`.

### Step 8 — Prompt rendering

Render templates with project context.

### Step 9 — Codex runner

Call Codex CLI using stdin and save output.

### Step 10 — Verification runner

Run build/test commands.

### Step 11 — Guardrail checks

Protected paths, diff size, build pass policy.

### Step 12 — Retrospective

Generate report and update project memory.

### Step 13 — Commit/push

Commit if allowed and safe.

### Step 14 — README and sample config

Finalize docs.

---

## 25. Practical Constraints

- Use simple file-based storage.
- Avoid database.
- Avoid web server.
- Avoid background daemon in MVP.
- Avoid unnecessary abstractions.
- Prioritize a working end-to-end run.
- Keep logs readable.
- Handle missing files gracefully.
- Fail fast with clear error messages.
- Do not hide failures.

---

## 26. Build/Test Commands For This Project

For AutoDev itself:

```bash
dotnet restore
dotnet build
dotnet test
```

If no tests exist yet, create at least basic tests for:

- project config loading
- workspace path creation
- protected path detection
- template rendering

---

## 27. Notes For Codex

This project is meant to orchestrate other coding agents. Be conservative with automation.

The most important quality is not fancy architecture. The most important quality is that the user can run this command:

```bash
autodev run --project ai-pet
```

And then inspect:

```text
workspaces/ai-pet/{today}/
```

to understand exactly what happened.

Build the MVP first. Make it reliable, transparent, and configurable.

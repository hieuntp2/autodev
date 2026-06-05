# AutoDev Orchestrator - Agent Instructions

## Mission

Build a reusable C#/.NET AutoDev Orchestrator that can plan, implement, verify, log, and report autonomous daily development work for configurable target projects.

The orchestrator is separate from the target projects it modifies.

## Primary Spec

Read this file first:

docs/specs/autodev_orchestrator_codex_implementation_brief.md

This spec is the source of truth for the MVP.

## Technology

- .NET 8 or newer
- C# console CLI first
- File-based storage
- External process calls for git and codex
- OpenAI API through HttpClient for MVP
- Windows Task Scheduler for scheduling

## Scope

Implement the MVP only.

Must support:

- autodev run --project ai-pet
- autodev status --project ai-pet
- project config loading from projects/{projectId}.json
- daily workspace creation
- input snapshots
- planner prompt generation
- OpenAI planner call
- Codex CLI execution
- build/test command execution
- git diff/status capture
- protected path validation
- daily report/retrospective
- safe commit policy

## Do Not Build Yet

Do not build:

- web dashboard
- database
- Windows Service
- Telegram/OpenClaw integration
- complex queue system
- multi-user auth
- cloud deployment
- auto-merge to main branch

## Folder Rules

AutoDev runner repo owns:

- docs/
- src/
- projects/
- templates/
- memory/
- workspaces/

Target project repos are external and configured through projects/*.json.

## Safety Rules

Do not hardcode API keys.

Read OpenAI API key from:

OPENAI_API_KEY

Do not log secrets.

Do not snapshot:

- .env
- appsettings.Production.json
- local.properties
- secrets/
- keystore/

Do not auto-commit if:

- build fails and commitOnlyIfBuildPasses is true
- protected paths were changed
- diff exceeds maxDiffLines

## Build/Test Commands

For AutoDev itself:

dotnet restore
dotnet build
dotnet test

If tests do not exist, add basic tests for:

- config loading
- workspace path creation
- protected path detection
- template rendering

## Implementation Style

Prefer simple, reliable code.

Use clear models and services.

Avoid unnecessary abstractions.

Fail fast with clear error messages.

Make logs readable.

The most important command is:

autodev run --project ai-pet

The user must be able to inspect:

workspaces/{projectId}/{yyyy-MM-dd}/

and understand exactly what happened.
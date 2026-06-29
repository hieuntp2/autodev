# Project Brief — <Your Project Name>

This file is read by AutoDev Runner and passed to the AI provider each run.
Place a copy named `ai-autonomous.md` in your target repository root (or point
the project's "Brief file" at any path).

## Product goal
What this product/tool is and who it's for.

## Tech stack
Languages, frameworks, key libraries.

## Current status
What exists today; what's stable; what's rough.

## Desired direction
Where you want it to go. Themes/epics, not micro-tasks (the AI will break these down).

## Allowed
- Read the codebase, plan, choose the most valuable task, implement it.
- Add/refactor tests and docs.
- Propose and build new features that fit the goal.

## Not allowed
- Don't touch <list sensitive areas, e.g. billing, auth secrets>.
- Don't change public API contracts without keeping backward compatibility.

## Build / test commands
- Build: `dotnet build` (or `npm run build`, etc.)
- Test: `dotnet test` (or `npm test`)

## Business / product notes
Any constraints, conventions, or context the AI should respect.

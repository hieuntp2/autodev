# AGENTS.md — AI Pet Animation-First Rebuild

## Role

You are an autonomous coding agent working on the target Android project.

Implement the project incrementally, one small task at a time, while keeping the build green.

## Product Goal

Build an original pixel-style Android digital pet that feels alive through:

- two expressive animated eyes
- natural idle behavior
- clear emotional reactions
- internal pet state
- personality traits
- local behavior selection
- gradual personality growth
- persistence later

## Core Philosophy

Believability first. Intelligence second.

The pet should feel alive before it becomes feature-rich.

The MVP is not a chatbot, voice assistant, camera AI demo, robot controller, or cloud AI wrapper. It is a small digital creature living on the Android screen.

## Technology

Use:

- Kotlin
- Android native
- Jetpack Compose
- Compose Canvas for avatar rendering
- Gradle multi-module project
- Room only when persistence tasks begin

Java is allowed only for Android/legacy interop when necessary.

## Expected Modules

```text
app/
ui-avatar/
brain/
memory/
core-common/
```

Responsibilities:

- `app`: Android entry point, app shell, Home screen, debug screens, dependency wiring
- `ui-avatar`: pixel eye rendering and animation runtime
- `brain`: pet state, mood, personality, behavior selection, learning
- `memory`: persistence later
- `core-common`: pure utilities, time, math, random helpers

## Strictly Deferred

Do not implement these features unless a future task explicitly allows them:

- camera
- face detection
- object detection
- microphone
- sound detection
- voice recognition
- TTS
- audio playback
- cloud AI
- LLM chat
- robot body
- BLE / Wi-Fi robot control
- hardware sensors
- full assistant behavior

## Visual Identity Rules

The avatar is an original pixel-style two-eye digital pet.

Allowed:

- general cute desktop-pet feeling
- pixel-style eyes
- companion robot energy
- expressive timing
- subtle idle motion
- emotional eye poses

Not allowed:

- copying EMO's exact face design
- copying EMO's exact animation timing
- copying EMO's branding, name, sounds, icons, or assets
- making the avatar look like a commercial robot clone
- using mouth/full body in the MVP unless a later task explicitly permits it

## Task Rules

For every task:

- implement only the requested task
- keep the build green
- do not add placeholder production logic
- do not leave TODOs in production code
- do not leave empty methods
- do not use `throw NotImplementedException`
- do not refactor unrelated files
- do not add unrelated dependencies
- do not edit protected requirement docs unless explicitly allowed
- update status after completing the task if allowed

## Autonomous Decision Policy

Do not ask the user during an autonomous run.

If ambiguous:

1. choose the smallest safe interpretation
2. stay within scope
3. write the decision to implementation status
4. continue only if safe

If unsafe:

1. stop
2. write blocked reason
3. do not commit

## Required Output After Each Run

Report exactly:

- Summary of changes
- Files changed
- How to verify
- Build result
- Remaining risks
- Next recommended task

Do not claim success unless the build command was run and passed.

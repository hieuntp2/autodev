# Daily Plan

## Goal

Create the foundation for a native, modular Android Kotlin project with a Home screen, supporting clean animated digital pet development, following the animation-first MVP.

## Why This Matters

A robust, modular Android project scaffold is critical to enable visible and incremental product progress. It ensures future animation, pet logic, and persistence can be added cleanly and independently, as required by all product, architecture, and agent rules. A working Home screen UI proves the skeleton is ready for iteration and for visual demos, laying the ground for all future "life" features.

## Selected Task

- R1 — Create Android Kotlin Project Skeleton

## Codex Task

Set up a multi-module Android project using Kotlin, Jetpack Compose, and the prescribed module structure:
- `app/`: Main app and entry activities/screens
- `ui-avatar/`: Will contain pixel eye avatar rendering and animation
- `brain/`: Pet state, mood, personality, and behavior logic
- `memory/`: Persistence and state saving logic (empty for now)
- `core-common/`: Time, math, and random helpers

Configure the initial Gradle settings, dependencies, and Compose integration. Implement a basic Home screen in `app/` showing text and an obvious Compose placeholder for the avatar module. Boilerplate only—no animation or logic yet. The app must be buildable and launchable in debug.

## Likely Files Or Modules

- `settings.gradle` (or `settings.gradle.kts`)
- `build.gradle` files (root and modules)
- Module source sets: `app/`, `ui-avatar/`, `brain/`, `memory/`, `core-common/`
- `app/src/main/AndroidManifest.xml`
- `app/src/main/java/.../MainActivity.kt`
- Jetpack Compose HomeScreen
- Placeholder Composable for avatar

## Acceptance Criteria

- The repo builds with `.\gradlew.bat assembleDebug` (Windows) and passes tests if any.
- App launches and displays a visible Home screen.
- Module structure matches:
    - `app/`
    - `ui-avatar/`
    - `brain/`
    - `memory/`
    - `core-common/`
- Compose is enabled for necessary modules.
- No camera/audio/cloud/robot code or dependencies are referenced.
- No placeholder pet logic is stubbed in production code.
- No protected or forbidden files are changed.

## Build And Test Commands

- `.\gradlew.bat assembleDebug`
- `.\gradlew.bat test`

## Risks

- Initial Compose and Gradle configuration issues (dependencies, module wiring)
- Misconfiguration of module dependencies, blocking later animation
- Overengineering (too much skeleton code)
- Not buildable on first try, or protected files touched by mistake

## Must Not Do

- Skip any required modules
- Add animation, state, or personality logic now
- Add dependencies for camera/audio/cloud/libraries not needed
- Add placeholder methods or leave empty classes
- Change protected requirement files

## Fallback Task If Blocked

If module creation or Compose integration is blocked for any reason, fallback to a single-module Compose app with clear TODO comments for future module extraction (NOT recommended unless multi-module setup is technically blocked by the build tooling/environment).

# task.json

```json
{
  "title": "Set up project skeleton with modular Compose-based structure and Home screen",
  "type": "feature",
  "priority": 1,
  "scope": "Create Gradle Android project with modules: app, ui-avatar, brain, memory, core-common. Configure Compose, set up buildable Home screen, and wire modules for future animated digital pet development. No animation or business logic yet.",
  "reason": "Enables all subsequent animation-first pet features. Provides visible product progress for iteration and developer alignment.",
  "modules": ["app", "ui-avatar", "brain", "memory", "core-common"],
  "acceptanceCriteria": [
    "Project builds with .\\gradlew.bat assembleDebug.",
    "App launches to a Home screen using Jetpack Compose.",
    "Modules are present and wired: app/, ui-avatar/, brain/, memory/, core-common/.",
    "No forbidden features (camera, audio, cloud, robot).",
    "No placeholder production logic.",
    "No protected files changed.",
    "Build passes."
  ],
  "mustNotDo": [
    "Implement animation logic.",
    "Stub business logic.",
    "Touch protected requirement files.",
    "Add unrelated dependencies.",
    "Change out-of-scope files."
  ],
  "buildCommands": [
    ".\\gradlew.bat assembleDebug",
    ".\\gradlew.bat test"
  ],
  "riskLevel": "low"
}
```

# Run Contract

## Today Goal

Deliver a multi-module, Compose-enabled Android Kotlin project skeleton with Home screen UI, following the animation-first product vision. Show visible progress.

## Allowed Changes

- Create and configure `app/`, `ui-avatar/`, `brain/`, `memory/`, `core-common/`.
- Set up Jetpack Compose for UI.
- Create a minimal HomeScreen with placeholder avatar slot using Compose.
- Update/initialize Gradle config (outside protected files).

## Forbidden Changes

- Editing any protected requirement/documentation/secrets files.
- Adding placeholder production logic, TODOs in code, or empty stubs.
- Adding camera/audio/cloud/robot dependencies.
- Writing outside the allowed paths.

## Max Diff Size

- Only files needed for initial module setup and a visible Home screen.
- No extra complex code; boilerplate only.

## Required Verification

- The build completes successfully (`.\gradlew.bat assembleDebug`).
- Launching the debug app shows the Home screen.
- No warnings/errors about missing modules or dependencies.
- Confirm no forbidden features or side effects.

## Commit Policy

- Only commit if the build is fully successful and acceptance criteria are met.
- Keep commits atomic and focused.
- Report the result and any deviations discovered.

## Rollback Policy

- If a blocking build or configuration error occurs, revert the affected files.
- Do not proceed to any next task or code generation until the skeleton is verified as healthy.
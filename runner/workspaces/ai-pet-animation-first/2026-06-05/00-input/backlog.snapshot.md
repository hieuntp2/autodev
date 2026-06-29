# Active Backlog — Animation-First AI Pet

## Rules for AutoDev

- Select only one task per run.
- Prefer the first incomplete task.
- Do not skip ahead unless status says the current task is done.
- Do not implement camera/audio/cloud/robot body.
- Keep diff small.
- Build after every task.
- Commit only if build passes.

---

## R1 — Create Android Kotlin Project Skeleton

Status: TODO

Goal: Create a native Android Kotlin project with modules ready for animation-first development.

Read first:
- AGENTS.md
- docs/product/vision.md
- docs/product/scope.md
- docs/product/roadmap.md
- docs/architecture/module_boundaries.md

Scope:
- Create Gradle project at repo root.
- Create modules: app, ui-avatar, brain, memory, core-common.
- Use Kotlin.
- Use Jetpack Compose.
- Add a basic Home screen.
- No pet animation yet beyond a placeholder.

Definition of Done:
- `./gradlew.bat assembleDebug` passes.
- App launches.
- Home screen is visible.
- No camera/audio/cloud/body dependencies exist.

---

## R2 — Render Original Pixel-Style Two-Eye Avatar

Status: TODO

Goal: Render a static original two-eye pixel avatar on Home screen.

Read first:
- docs/animation/pet_character_spec.md
- docs/animation/animation_style_guide.md
- docs/animation/eye_design_system.md
- docs/animation/animation_state_machine.md

Scope:
- Two eyes only.
- No mouth.
- No full body.
- No copied commercial robot design.
- Use Compose Canvas.

Definition of Done:
- Two pixel-style eyes are visible.
- Eyes are centered in the pet stage.
- Rendering is reusable for future animation.
- Build passes.

---

## R3 — Add Natural Blink Animation

Status: TODO

Goal: Make the eyes blink automatically with non-uniform natural timing.

Read first:
- docs/animation/animation_style_guide.md
- docs/animation/animation_state_machine.md
- docs/animation/animation_timing_rules.md

Scope:
- Blink only.
- No breathing/floating yet.
- No interaction reaction yet.

Definition of Done:
- Pet blinks without user input.
- Blink interval varies.
- Close/open timing feels natural.
- Build passes.

---

## R4 — Add Subtle Idle Breathing/Floating

Status: TODO

Goal: Add subtle idle motion so the pet does not feel static.

Read first:
- docs/animation/animation_style_guide.md
- docs/animation/animation_timing_rules.md

Scope:
- Idle motion only.
- Must work together with blink.
- No state/personality yet.

Definition of Done:
- Subtle breathing/floating is visible.
- Motion is calm and not distracting.
- Blink still works.
- Build passes.

---

## R5 — Add Emotion States and Debug Preview

Status: TODO

Goal: Support core emotion states and allow manual preview in a debug screen.

Read first:
- docs/animation/pet_character_spec.md
- docs/animation/emotion_catalog.md
- docs/animation/eye_design_system.md

Required emotions:
- IDLE
- HAPPY
- CURIOUS
- SLEEPY
- SAD
- EXCITED
- HUNGRY
- ANNOYED

Definition of Done:
- Debug UI can switch emotion.
- Each emotion is visually distinct.
- Blink/idle still work.
- Build passes.

---

## R6 — Add Tap Reaction

Status: TODO

Goal: Tapping the pet triggers a short expressive reaction.

Definition of Done:
- Tap reaction is visible.
- Reaction does not break blink/idle.
- Build passes.

---

## R7 — Add Long Press Reaction

Status: TODO

Goal: Long press triggers a different reaction from tap.

Definition of Done:
- Long press is visible.
- Reaction differs from tap.
- Build passes.

---

## R8 — Add Reaction Priority Runtime

Status: TODO

Goal: Introduce priority rules so reaction animations and idle animations do not conflict.

Definition of Done:
- Reaction > greeting > base emotion > idle.
- Runtime safely returns to base pose.
- Build passes.

---

## R9 — Add Anti-Repeat and Cooldown

Status: TODO

Goal: Prevent repetitive/spammy reactions.

Definition of Done:
- Same reaction is not repeated too often.
- Fast repeated taps are cooled down or softened.
- Build passes.

---

## R10 — Add Feed / Play / Rest Actions

Status: TODO

Goal: Add basic action buttons that trigger different reactions.

Definition of Done:
- Feed, Play, Rest buttons exist.
- Each triggers a distinct visible reaction.
- Build passes.

---

## R11 — Add PetMood and PetState Models

Status: TODO

Goal: Create internal state model for the pet.

Read first:
- docs/personality/pet_state_model.md
- docs/personality/personality_engine.md

Definition of Done:
- PetMood exists.
- PetState exists.
- Values are clamped safely.
- Build passes.

---

## R12 — Add Derived PetCondition Resolver

Status: TODO

Goal: Resolve high-level conditions from raw PetState.

Definition of Done:
- PetCondition model exists.
- Resolver maps state to conditions.
- Build passes.

---

## R13 — Map PetState to AvatarEmotion

Status: TODO

Goal: Drive avatar emotion from real pet state.

Definition of Done:
- Avatar emotion can be resolved from PetState.
- Debug screen can show state and resolved emotion.
- Build passes.

---

## R14 — Add PetState Debug Panel

Status: TODO

Goal: Show current PetState values for development.

Definition of Done:
- Debug panel shows mood, energy, hunger, sleepiness, social, bond.
- Values are real app state, not fake display-only text.
- Build passes.

---

## R15 — Add State Changes from Interactions

Status: TODO

Goal: Interactions update PetState.

Definition of Done:
- Tap/play/feed/rest change state.
- Changes affect resolved emotion.
- Build passes.

---

## R16 — Add PersonalityTraits Model

Status: TODO

Goal: Create slow-changing personality traits.

Definition of Done:
- PersonalityTraits model exists.
- Values are clamped 0.0..1.0.
- Build passes.

---

## R17 — Add BehaviorCandidate and BehaviorSelector

Status: TODO

Goal: Create foundation for selecting idle/reaction behaviors.

Definition of Done:
- Behavior candidates exist.
- Selector chooses a behavior deterministically with controlled randomness.
- Build passes.

---

## R18 — Score Behavior Using State and Traits

Status: TODO

Goal: Behavior selection uses PetState and PersonalityTraits.

Definition of Done:
- Traits affect scoring.
- State affects scoring.
- Build passes.

---

## R19 — Add Simple Reward Tracking

Status: TODO

Goal: Track whether user responded positively to recent behavior.

Definition of Done:
- Behavior reward model exists.
- Reward can bias future selection slightly.
- Build passes.

---

## R20 — Add Personality Debug Panel

Status: TODO

Goal: Make personality visible to developer.

Definition of Done:
- Debug panel shows traits and recent behavior preference.
- Build passes.

---

## R21 — Add Room Database

Status: TODO

Goal: Add persistence foundation.

Definition of Done:
- Room database initializes.
- Build passes.

---

## R22 — Persist PetState

Status: TODO

Goal: PetState survives restart.

Definition of Done:
- PetState loads/saves through Room.
- App restart preserves state.
- Build passes.

---

## R23 — Persist PersonalityTraits

Status: TODO

Goal: Personality survives restart.

Definition of Done:
- Traits load/save through Room.
- App restart preserves traits.
- Build passes.

---

## R24 — Apply Time Decay on App Open

Status: TODO

Goal: Make time away change pet state.

Definition of Done:
- Elapsed time is calculated.
- Hunger/sleepiness/social/energy change.
- Build passes.

---

## R25 — Add Basic Event Log

Status: TODO

Goal: Record core interactions and state changes.

Definition of Done:
- Events are stored.
- Debug viewer can show recent events.
- Build passes.

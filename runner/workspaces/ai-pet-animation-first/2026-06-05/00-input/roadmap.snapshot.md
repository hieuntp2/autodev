# Roadmap — Animation-First Rebuild

## Phase 1A — Animation Core

Goal: The pet feels alive before it has state or memory.

Tasks:

- R1 Create Android Kotlin project skeleton
- R2 Render original two-eye pixel avatar
- R3 Add natural blink animation
- R4 Add subtle idle breathing/floating
- R5 Add emotion states and debug preview

Exit Criteria:

- app opens
- eyes are visible
- eyes blink naturally
- idle motion exists
- emotions are previewable

## Phase 1B — Interaction Reactions

Goal: User input creates readable pet reactions.

Tasks:

- R6 Add tap reaction
- R7 Add long press reaction
- R8 Add reaction priority runtime
- R9 Add anti-repeat and cooldown
- R10 Add feed/play/rest action buttons

Exit Criteria:

- tap is visible
- long press is visible
- reactions interrupt idle safely
- repeated interactions do not spam identical animations

## Phase 1C — Pet State

Goal: Animation becomes driven by real internal state.

Tasks:

- R11 Add PetMood and PetState models
- R12 Add derived PetCondition resolver
- R13 Map PetState to AvatarEmotion
- R14 Add PetState debug panel
- R15 Add state changes from interactions

Exit Criteria:

- PetState exists
- state can be inspected
- emotion is not only manually selected
- interactions change state

## Phase 1D — Personality

Goal: The pet starts to behave differently over time.

Tasks:

- R16 Add PersonalityTraits model
- R17 Add BehaviorCandidate and BehaviorSelector
- R18 Score behavior using state + traits
- R19 Add simple reward tracking
- R20 Add personality debug panel

Exit Criteria:

- traits exist
- behavior selection depends on traits
- repeated user response slightly changes behavior preference

## Phase 1E — Persistence and Continuity

Goal: The pet survives restarts and time matters.

Tasks:

- R21 Add Room database
- R22 Persist PetState
- R23 Persist PersonalityTraits
- R24 Apply time decay on app open
- R25 Add basic event log

Exit Criteria:

- state survives app restart
- traits survive app restart
- returning later changes pet state
- event/status is observable

## Phase 2 — Deferred Audio and Perception

Only after Phase 1 feels alive:

- audio reaction
- microphone
- sound detection
- pre-recorded pet sounds
- camera
- face/object recognition
- voice
- cloud AI

## Current Priority

Start at R1. Do not skip ahead.

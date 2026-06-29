# Scope — Animation-First MVP

## In Scope

### Phase 1A — Animation Core

- Android Kotlin project skeleton
- Compose Home screen
- two-eye pixel avatar
- static eye rendering
- natural blink
- subtle breathing/floating
- emotion preview screen
- debug controls for animation

### Phase 1B — Interaction Reaction

- tap reaction
- long press reaction
- feed/play/rest buttons
- reaction priority
- reaction cooldown
- anti-repeat behavior
- return-to-base animation

### Phase 1C — Pet State

- PetState model
- PetMood model
- state constraints
- derived conditions
- state-to-emotion mapping
- debug state panel

### Phase 1D — Personality

- PersonalityTraits model
- behavior scoring
- trait-driven idle selection
- simple local behavior preference
- personality debug panel

### Phase 1E — Persistence and Continuity

- Room database
- persist PetState
- persist PersonalityTraits
- app-open time decay
- basic event log
- implementation status updates

## Out of Scope Until Explicitly Added

Do not implement:

- CameraX
- ML Kit
- face detection
- object detection
- microphone permission
- AudioRecord
- sound/VAD detection
- SoundPool/audio playback
- speech recognition
- TTS
- cloud AI
- OpenAI/Gemini integration
- local LLM
- robot body
- BLE
- Arduino/ESP32 control
- sensors
- navigation/SLAM
- complex mini games
- monetization
- account login
- social features

## Visual Scope

MVP avatar uses:

- two eyes only
- optional eye highlight
- optional small pixel particles later
- no mouth
- no nose
- no full body
- no copied commercial robot design

## Technical Scope

Use Kotlin, Jetpack Compose, Compose Canvas, modular Gradle, and Room only when persistence starts.

## Build Scope

Default build command:

```bash
./gradlew.bat assembleDebug
```

The agent must keep every task buildable.

## Scope Rule

If a task does not make the pet more alive, more expressive, more reactive, or more continuous over time, it should not be selected during Phase 1.

# Cross-Skill Contract (canonical)

> **This file is the single source of truth** for how the three pixel-pet skills
> cooperate: `pet-behavior-designer`, `eye-only-pixel-animation-artist`, and
> `retro-bit-sound-designer`. The other two skills carry a short pointer copy in
> their own `references/cross-skill-contract.md` that links back here. When this
> file changes, update the shared id conventions in the pointer copies and re-run
> the link/manifest validators.

## Ownership (no overlap)

| Concern | Owner skill | Never does |
| --- | --- | --- |
| Which reaction fires, its `animation_id`, optional `sound_cue_id`, priority, cooldown, interruptibility, timing intent, memory & personality deltas | **pet-behavior-designer** | draw frames, synthesise audio |
| Visual assets for an `animation_id` (frames, sprite sheet, preview GIF, `<id>.animation.json`) | **eye-only-pixel-animation-artist** | pick which reaction fires, make sound |
| Audio assets for a `sound_cue_id` (`.wav` / `.ogg`, `<id>.sound.json`) | **retro-bit-sound-designer** | draw frames, decide behavior |

The behavior skill is the **coordinator**: it selects ids and timing intent, then
emits precise handoff requests to the other two skills for any missing asset.

## Data flow

```
behavior event (event_id)
  -> reaction decision
       -> animation_id          (required — visual)
       -> sound_cue_id?         (optional — audio)
       -> priority              (0-99, banded below)
       -> interruptible         (bool)
       -> cooldownMs            (min gap before this reaction repeats)
       -> minVisibleMs          (min time on screen before same/lower priority replaces)
       -> memory delta          (summary update, not raw event log)
       -> personality delta     (small, bounded trait nudge)
```

## The animation ↔ sound link

Audio is bound to animation **through the animation manifest's `events[]` array**,
which already exists in the pixel animation schema
(`AutoDevSkills/pixel-animation-artist/schema/animation.schema.json`):

```json
"events": [
  { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }
]
```

* `value` on a `type:"sound"` event **is a `sound_cue_id`**.
* `eye-only-pixel-animation-artist` writes the hook (the id) into the manifest.
* `retro-bit-sound-designer` produces the matching asset under that id and records
  the same id in its `<id>.sound.json`.
* `retro-bit-sound-designer`'s `validate_sound_links.py` checks that every
  `type:"sound"` event value resolves to a real sound asset **or** is reported as
  an intentional stub — it never silently passes a dangling cue.

## Degradation rules

* **Missing optional sound asset** → runtime plays the animation silently. Behavior
  code must not crash or block on a missing sound. This is a warning, not an error.
* **Missing required animation asset** → this is an **error** and must be reported
  explicitly (never faked, never silently skipped). A behavior that references an
  `animation_id` with no committed assets is a validation failure.

## Stable id conventions

Ids are lowercase, stable, and human-readable. Never renumber or reuse an id for a
different meaning.

| Kind | Shape | Examples |
| --- | --- | --- |
| `event_id` | `domain.snake_case` (dotted) | `interaction.tap`, `interaction.long_press`, `sensor.pickup`, `sensor.shake`, `system.charging_start`, `lifecycle.return` |
| `state_id` | single snake token | `idle`, `happy`, `curious`, `sleepy`, `annoyed`, `surprised`, `charging`, `affectionate` |
| `animation_id` | `<theme>_<descriptor>[_<variant>]` | `blink_idle`, `curious_gaze_left`, `happy_reward_sparkle`, `charging_power_up` |
| `sound_cue_id` | `<theme>_<descriptor>_<audioword>` | `happy_reward_chirp`, `charging_power_hum`, `annoyed_short_buzz` |

Canonical worked example (used across all three skills' examples):

```
event_id:     interaction.tap
state_id:     happy
animation_id: happy_reward_sparkle
sound_cue_id: happy_reward_chirp
```

## Priority bands (shared by all skills)

| Band | Range | Use |
| --- | --- | --- |
| Ambient / idle | 0–9 | breathe, idle blink, idle gaze |
| Light interaction | 10–29 | single tap, look-at |
| Strong / emotional | 30–49 | happy hop, annoyed shake, affection |
| Sensor reaction | 50–69 | pickup, tilt, shake, proximity |
| System / device | 70–89 | charging, battery level change |
| Critical override | 90–99 | reserved for safety/system-forced states |

Higher band preempts lower **only if** the running animation is `interruptible`
or its `minVisibleMs` has elapsed.

## Target-project directory contract (Android)

Assets are written into the **target project**, never back into `AutoDevSkills/`.

```
app/src/main/assets/pet/animations/<animation_id>/    # owned by animation skill
app/src/main/assets/pet/sounds/<sound_cue_id>/        # owned by sound skill
app/src/main/assets/pet/behavior/                     # owned by behavior skill (config JSON)
```

Android runtime integration contracts these ids/manifests feed (files created by
the product work, **not** by these skills): `SpriteSheet.kt`, `SpriteAnimation.kt`,
`PetAnimationClips.kt`, `PixelPetRenderer.kt` (consume `animation.json` + sheet),
`PetBehaviorController.kt` (consumes behavior config, resolves `animation_id` →
clip and fires `sound_cue_id`).

## Product identity constraints (bind the animation skill)

The whole phone screen is the pet's **face**; there is **no default full body**.
The **cyan eyes** are the primary identity and emotional center. Accessories/effects
(hearts, sparkles, magnifier, sweat, stars, sleep "Z") are **temporary secondary**
elements. Background is **black**; pixels are **sharp** — no anti-aliasing, blur,
gradients, or soft shadows.

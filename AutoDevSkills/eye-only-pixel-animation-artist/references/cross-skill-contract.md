# Cross-Skill Contract (pointer)

This is a **short pointer stub**. The single source of truth is the canonical
contract maintained by the behavior skill:
[`../../pet-behavior-designer/references/cross-skill-contract.md`](../../pet-behavior-designer/references/cross-skill-contract.md).
Read that file for the full ownership, data-flow, degradation, priority-band and
Android directory rules. When the canonical file changes, this stub's id table
and the sound-hook rule below must be re-synced.

`eye-only-pixel-animation-artist` owns **visual assets for an `animation_id`**
(frames, sprite sheet, preview GIF, `<id>.animation.json`). It never picks which
reaction fires (that is `pet-behavior-designer`) and never makes sound (that is
`retro-bit-sound-designer`).

## Shared id table

| Kind | Shape | Examples |
| --- | --- | --- |
| `event_id` | `domain.snake_case` (dotted) | `interaction.tap`, `sensor.pickup`, `system.charging_start` |
| `state_id` | single snake token | `idle`, `happy`, `curious`, `sleepy`, `annoyed`, `charging` |
| `animation_id` | `<theme>_<descriptor>[_<variant>]` | `blink_idle`, `curious_gaze_left`, `happy_reward_sparkle` |
| `sound_cue_id` | `<theme>_<descriptor>_<audioword>` | `happy_reward_chirp`, `charging_power_hum` |

Canonical worked example: `interaction.tap` → state `happy` →
`animation_id: happy_reward_sparkle` → `sound_cue_id: happy_reward_chirp`.

## Sound-event hook rule

This skill writes the audio link into the manifest's `events[]`; the sound asset
is produced separately by `retro-bit-sound-designer`:

```json
"events": [
  { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }
]
```

- The `value` of a `type:"sound"` event **is a `sound_cue_id`**
  (`^[a-z][a-z0-9_]*$`). This skill only records the id — never the audio.
- A missing sound asset degrades to **silent playback** (warning, not error). A
  missing required **animation** asset is an **error** — report it, never fake it.

# Cross-skill contract (pointer)

> **This is a pointer stub, not the source of truth.** The canonical cross-skill
> contract lives at
> [`AutoDevSkills/pet-behavior-designer/references/cross-skill-contract.md`](../../pet-behavior-designer/references/cross-skill-contract.md).
> Read it for ownership boundaries, data flow, priority bands, degradation rules and
> the Android directory contract. When the canonical file changes, re-sync the id
> table below and re-run `scripts/validate_sound_links.py`.

## Where this skill sits

`retro-bit-sound-designer` **owns audio assets for a `sound_cue_id`** (`.wav`,
`.ogg`, `<id>.sound.json`). It never draws frames (that is
`eye-only-pixel-animation-artist`) and never decides which reaction fires (that is
`pet-behavior-designer`, the coordinator).

## Shared id conventions (mirror of the canonical table)

| Kind | Shape | Examples |
| --- | --- | --- |
| `event_id` | `domain.snake_case` | `interaction.tap`, `sensor.pickup`, `system.charging_start` |
| `state_id` | single snake token | `idle`, `happy`, `sleepy`, `annoyed`, `charging` |
| `animation_id` | `<theme>_<descriptor>[_<variant>]` | `happy_reward_sparkle`, `charging_power_up` |
| `sound_cue_id` | `<theme>_<descriptor>_<audioword>` | `happy_reward_chirp`, `charging_power_hum`, `annoyed_short_buzz` |

`sound_cue_id` must match `^[a-z][a-z0-9_]*$`. Ids are lowercase, stable, never
renumbered or reused for a different meaning.

Canonical worked example (shared across all three skills):

```
event_id:     interaction.tap
state_id:     happy
animation_id: happy_reward_sparkle
sound_cue_id: happy_reward_chirp
```

## The sound-hook rule

A sound asset is bound to an animation **through the animation manifest's `events[]`
array**:

```json
"events": [ { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" } ]
```

- `value` on a `type:"sound"` event **is a `sound_cue_id`**.
- The animation skill writes the hook; **this skill produces the asset under that id**
  and records the same id in `<id>.sound.json`.
- `scripts/validate_sound_links.py` verifies every hook resolves to a real asset, or
  is an intentional `"optional": true` stub.

## Degradation (mirror)

- Missing **optional** sound asset → runtime plays the animation **silently**; this
  is a **warning**, never a crash.
- Missing **required** animation asset → an **error**, reported explicitly.

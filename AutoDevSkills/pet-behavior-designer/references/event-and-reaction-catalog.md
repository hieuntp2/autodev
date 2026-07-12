# Event & Reaction Catalog

Canonical list of **normalized `event_id`s** the behavior layer understands, and
a starter reaction table. Ids follow the cross-skill contract
([`cross-skill-contract.md`](cross-skill-contract.md)): `event_id` is dotted
`domain.snake_case`; `state_id` is a single snake token; `animation_id` is
`<theme>_<descriptor>[_<variant>]`; `sound_cue_id` is
`<theme>_<descriptor>_<audioword>`.

Raw Android input/sensor callbacks are **normalized into these ids** before the
selector runs. Never branch on raw events inside the selector.

## Normalized event ids

### interaction.* (user touch)
| event_id | meaning |
| --- | --- |
| `interaction.tap` | single short tap on the face |
| `interaction.double_tap` | two taps within the double-tap window |
| `interaction.repeated_tap` | rapid taps beyond N/interval (collapsed, not queued) |
| `interaction.long_press` | press held past the long-press threshold |
| `interaction.swipe` | directional swipe across the face |

### sensor.* (device motion / environment)
| event_id | typical sensor | meaning |
| --- | --- | --- |
| `sensor.pickup` | accelerometer | device lifted from rest |
| `sensor.tilt` | accelerometer | sustained tilt beyond threshold |
| `sensor.rotation` | gyroscope | rotation about an axis |
| `sensor.shake` | accelerometer | shake gesture (debounced) |
| `sensor.proximity` | proximity | object/hand near the screen |
| `sensor.ambient_light` | light | ambient brightness band changed |

### system.* (device state)
| event_id | meaning |
| --- | --- |
| `system.charging_start` | charger connected |
| `system.charging_stop` | charger disconnected |
| `system.battery_low` | battery crossed the low threshold |
| `system.battery_level_change` | battery level band changed |

### time.* (time-of-day variation)
| event_id | meaning |
| --- | --- |
| `time.morning` / `time.day` / `time.evening` / `time.night` | entered a time band |

### lifecycle.* (app lifecycle — must be corruption-safe)
| event_id | meaning |
| --- | --- |
| `lifecycle.return` | user returned to the pet after absence |
| `lifecycle.restart` | cold start / process recreated (reload memory + traits) |

## Emotional states (starter set)

`idle`, `happy`, `curious`, `sleepy`, `annoyed`, `surprised`, `charging`,
`affectionate`. Each state declares a `baseAnimation` (its resting look).

## Starter reaction table

`event_id → state → animation_id → sound_cue_id → priority band`. Priority bands
come from the contract (idle 0–9, light interaction 10–29, strong/emotional
30–49, sensor 50–69, system/device 70–89, critical 90–99).

| event_id | state | animation_id | sound_cue_id | priority (band) |
| --- | --- | --- | --- | --- |
| `lifecycle.return` | idle | `blink_idle` | — | 5 (idle) |
| `interaction.tap` | happy | `happy_reward_sparkle` | `happy_reward_chirp` | 35 (emotional) |
| `interaction.long_press` | affectionate | `affection_heart_pulse` | `affection_soft_purr` | 40 (emotional) |
| `interaction.repeated_tap` | annoyed | `annoyed_shake` | `annoyed_short_buzz` | 45 (emotional) |
| `interaction.swipe` | curious | `curious_gaze_left` | — | 20 (light) |
| `sensor.pickup` | surprised | `surprised_look_up` | `surprised_gasp` | 55 (sensor) |
| `sensor.shake` | surprised | `surprised_shock` | `surprised_gasp` | 60 (sensor) |
| `sensor.proximity` | curious | `curious_lean_in` | — | 52 (sensor) |
| `sensor.ambient_light` | sleepy | `sleepy_dim_droop` | — | 50 (sensor) |
| `system.charging_start` | charging | `charging_power_up` | `charging_power_hum` | 75 (system) |
| `system.battery_low` | sleepy | `sleepy_low_energy` | — | 78 (system) |
| `time.night` | sleepy | `sleepy_yawn` | `sleepy_soft_sigh` | 8 (idle band, time-of-day flavor) |

Notes:
- `interaction.repeated_tap` is a **collapsed** event — the normalizer emits it
  instead of enqueuing N `interaction.tap`s (anti-queue rule).
- Any `animation_id`/`sound_cue_id` here that has no committed asset must be
  requested via a handoff (see SKILL.md, and the degradation rules in the
  contract). A missing **required animation** asset is an error; a missing
  **optional sound** degrades to silent.

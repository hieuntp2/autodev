# Example — Sensor Reaction (`sensor.shake` → surprised)

A shake makes the pet startle. This shows a sensor reaction in the **50–69**
band, an **unavailable-sensor fallback**, and **debounce** so a shake fires once.

## User-visible intent

Shaking the phone startles the pet — a sharp "surprised" shock in the eyes with a
gasp. On devices without a usable accelerometer, the pet still shows a softer
surprise instead of nothing.

## Normalized event + debounce

Raw accelerometer samples are gesture-detected into a single `sensor.shake`
event. A debounce window (aligned with `cooldownMs`) prevents one physical shake
from emitting a burst — see
[`../references/sensor-safety-and-battery-guide.md`](../references/sensor-safety-and-battery-guide.md).

## ReactionDecision

| field | value | why |
| --- | --- | --- |
| `id` | `shake_surprise` | unique reaction id |
| `event` | `sensor.shake` | normalized, debounced gesture |
| `state` | `surprised` | emotional target state |
| `animation_id` | `surprised_shock` | visual (owned by animation skill) |
| `sound_cue_id` | `surprised_gasp` | optional audio (owned by sound skill) |
| `priority` | `60` | sensor band (50–69); out-ranks touch/emotional |
| `interruptible` | `false` | the startle reads clearly before yielding |
| `cooldownMs` | `2000` | one startle per shake; no machine-gun startles |
| `minVisibleMs` | `800` | the shock is guaranteed visible |
| `requiresSensor` | `accelerometer` | needs motion sensing |
| `sensorFallback` | `alternate` | degrade gracefully if no accelerometer |
| `alternateAnimation` | `surprised_soft` | sensor-free substitute |
| `memoryDelta` | `{"summary":"shaken"}` | one compact summary |
| `personalityDelta` | `{"shyness":0.02}` | slow nudge, ≤ `maxDeltaPerEvent` |
| `weight` | `1.0` | tie-break weight |

### Config fragment

```json
{
  "id": "shake_surprise",
  "event": "sensor.shake",
  "state": "surprised",
  "animation_id": "surprised_shock",
  "sound_cue_id": "surprised_gasp",
  "priority": 60,
  "interruptible": false,
  "cooldownMs": 2000,
  "minVisibleMs": 800,
  "requiresSensor": "accelerometer",
  "sensorFallback": "alternate",
  "alternateAnimation": "surprised_soft",
  "memoryDelta": {"summary": "shaken"},
  "personalityDelta": {"shyness": 0.02},
  "weight": 1.0
}
```

### Simulate the fallback path

```bash
echo '[{"event":"interaction.tap","timeMs":0},{"event":"sensor.shake","timeMs":200,"sensor":"accelerometer"}]' > events.json

# accelerometer present -> fires surprised_shock and preempts the happy beat
python ../scripts/simulate_behavior.py --config behavior.json --events events.json --seed 7

# accelerometer ABSENT -> alternate fallback fires surprised_soft, marked DEGRADED
python ../scripts/simulate_behavior.py --config behavior.json --events events.json --seed 7 \
    --available-sensors proximity,light
```

Fallback behavior by `sensorFallback`:
- `ignore` → the shake candidate is dropped (no reaction).
- `degrade` → keeps `surprised_shock` but marks it **DEGRADED**.
- `alternate` → plays `surprised_soft` instead (this example).

## Asset handoffs emitted by this decision

- **→ `eye-only-pixel-animation-artist`** (REQUIRED): `animation_id:
  surprised_shock` (sharp startle, sensor band, non-looping, ~800 ms,
  `interruptible:false`) **and** the fallback `animation_id: surprised_soft`
  (gentler surprise for sensorless devices). A missing required animation is an
  **error**.
- **→ `retro-bit-sound-designer`** (OPTIONAL): `sound_cue_id: surprised_gasp`,
  bound through the animation manifest `events[]`. Missing → plays silently.

See the ownership and degradation rules in
[`../references/cross-skill-contract.md`](../references/cross-skill-contract.md).

# Sensor Safety & Battery Guide

Sensor reactions make the pet feel alive, but naive sensor use drains the
battery and floods the selector. Follow these rules for every `sensor.*`
reaction. Known sensors (from the schema): `accelerometer`, `gyroscope`,
`proximity`, `light`.

## 1. Debounce and rate-limit

- Raw sensor callbacks fire at high frequency (tens–hundreds Hz). **Never** feed
  raw samples into the selector.
- Detect a *gesture* (shake, tilt-hold, pickup) and emit **one** normalized
  `event_id`, then apply a debounce window so the same gesture cannot re-emit
  for `cooldownMs`.
- Rate-limit continuous signals (ambient light, proximity) to **band changes**
  (`bright→dim`), not every sample.

## 2. Register only when useful, and release

- Register a sensor listener **only** when a reaction that needs it is plausible
  (pet visible / interactive), using the slowest delay that still detects the
  gesture (`SENSOR_DELAY_UI` / `SENSOR_DELAY_NORMAL`, not `FASTEST`).
- **Unregister** as soon as it is no longer useful — screen off, pet backgrounded,
  or the reaction is on cooldown for a long window.
- Prefer batching / max-reporting-latency where the platform supports it.

## 3. Graceful fallback when a sensor is unavailable

Every sensor reaction declares `requiresSensor` + `sensorFallback`:

| fallback | behavior when the sensor is missing |
| --- | --- |
| `ignore` | drop the candidate entirely (the reaction simply cannot happen) |
| `degrade` | keep the reaction but mark it **degraded** (e.g. reduced fidelity / same animation, no sensor-driven nuance) |
| `alternate` | keep the reaction using `alternateAnimation` (a sensor-free substitute); if no `alternateAnimation` is defined it drops |

Missing sensors must **never** crash or block behavior. Not every device has a
gyroscope or a proximity sensor — design the fallback deliberately.

## 4. Battery-conscious duty cycling

- Combine related gestures on **one** listener (pickup + tilt + shake all read
  the accelerometer — register it once).
- Duty-cycle expensive sensors: sample in short windows around likely
  interactions rather than continuously.
- Back off when charging is *not* the point — but note that under
  `system.charging_start` you may safely relax duty cycling since power is not a
  concern while charging.
- Coalesce ambient-light / proximity into hysteresis bands to avoid flapping at
  a threshold.

## 5. Priority placement

Sensor reactions live in the **50–69** band (see
[`behavior-priority-rules.md`](behavior-priority-rules.md) and the canonical
[`cross-skill-contract.md`](cross-skill-contract.md)). They out-rank light touch
and emotional beats but yield to system/device (70–89) and critical (90–99)
states. A shake surprise (`sensor.shake`, ~60) should visibly interrupt a happy
wiggle, but a `system.charging_start` (~75) should win over the shake.

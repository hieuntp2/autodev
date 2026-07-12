# Example — Touch Reaction (`interaction.tap` → happy)

The canonical worked example from the cross-skill contract: a single tap makes
the pet happy. This shows the full **ReactionDecision** the behavior skill owns,
plus the **asset handoff requests** it emits to the other two skills.

## User-visible intent

Tapping the pet's face rewards the user with a quick, delightful "happy" beat —
sparkle in the eyes and a short cheerful chirp — that does not spam if the user
taps repeatedly.

## Normalized event

`interaction.tap` (a rapid burst instead normalizes to `interaction.repeated_tap`;
this reaction is throttled by its own `cooldownMs`).

## ReactionDecision

| field | value | why |
| --- | --- | --- |
| `id` | `tap_happy` | unique reaction id |
| `event` | `interaction.tap` | normalized touch event |
| `state` | `happy` | emotional target state |
| `animation_id` | `happy_reward_sparkle` | visual (owned by animation skill) |
| `sound_cue_id` | `happy_reward_chirp` | optional audio (owned by sound skill) |
| `priority` | `35` | strong/emotional band (30–49) |
| `interruptible` | `true` | a sensor/system reaction may take over |
| `cooldownMs` | `1500` | throttles repeat rewards → no chirp spam |
| `minVisibleMs` | `500` | the reward is actually seen before replacement |
| `requiresSensor` | `null` | touch needs no sensor |
| `sensorFallback` | `ignore` | not applicable (no sensor) |
| `memoryDelta` | `{"summary":"tapped","affection":1}` | one compact summary + counter |
| `personalityDelta` | `{"playfulness":0.01}` | slow nudge, well under `maxDeltaPerEvent` |
| `weight` | `1.0` | tie-break weight vs sibling `interaction.tap` reactions |

### Config fragment

```json
{
  "id": "tap_happy",
  "event": "interaction.tap",
  "state": "happy",
  "animation_id": "happy_reward_sparkle",
  "sound_cue_id": "happy_reward_chirp",
  "priority": 35,
  "interruptible": true,
  "cooldownMs": 1500,
  "minVisibleMs": 500,
  "requiresSensor": null,
  "sensorFallback": "ignore",
  "memoryDelta": {"summary": "tapped", "affection": 1},
  "personalityDelta": {"playfulness": 0.01},
  "weight": 1.0
}
```

### Simulate it

```bash
echo '[{"event":"interaction.tap","timeMs":0},{"event":"interaction.tap","timeMs":300},{"event":"interaction.tap","timeMs":2000}]' > events.json
python ../scripts/simulate_behavior.py --config behavior.json --events events.json --seed 7
```

Expected shape: fires at `t=0`; the `t=300` tap is suppressed (still within
`minVisibleMs`, and `tap_happy` is on `cooldownMs`); fires again at `t=2000`
(cooldown elapsed). Memory gains `tapped` entries; `playfulness` drifts up slowly.

## Asset handoffs emitted by this decision

Because this reaction *names* assets it does not own, it emits precise requests:

- **→ `eye-only-pixel-animation-artist`** (REQUIRED — a missing visual is an error):
  > Need `animation_id: happy_reward_sparkle` — happy reward beat for the cyan
  > eyes with a temporary sparkle accent, priority band 30–49, ~500–700 ms,
  > non-looping, `interruptible` friendly. Emit `<id>.animation.json` + sheet.
  > Bind audio via the manifest `events[]`:
  > `{ "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }`.

- **→ `retro-bit-sound-designer`** (OPTIONAL — a missing sound degrades to silent):
  > Need `sound_cue_id: happy_reward_chirp` — short cheerful chirp (~200 ms),
  > bound to the `happy_reward_sparkle` manifest event above. If absent, the
  > animation plays silently (warning, not error).

See the degradation rules in
[`../references/cross-skill-contract.md`](../references/cross-skill-contract.md).

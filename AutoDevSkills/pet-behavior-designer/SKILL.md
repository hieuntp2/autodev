---
name: pet-behavior-designer
description: >-
  Behavior authority for the Android eye-only pet. Designs emotional states and
  maps normalized events (touch, long-press, repeated tap, swipe, pickup, tilt,
  shake, proximity, ambient light, charging, battery, time-of-day, lifecycle) to
  bounded reactions, orchestrating animation priority, interruptibility,
  cooldown/minVisibleMs, seedable randomness, compact long-term memory and
  slowly-evolving bounded personality. Selects animation_id + optional
  sound_cue_id + timing intent and emits handoff requests to the animation and
  sound skills; never draws frames or synthesises audio.
---

# pet-behavior-designer

You are the **single behavior authority** for the Android eye-only pixel pet.
Keep all behavioral orchestration here — do **not** split it into micro-skills.
This skill lives once in AutoDev's global store and is shared across projects;
only its *output* (a behavior config JSON plus behavior code) lands in a target
project.

## Purpose & core responsibilities

Own the full behavioral decision layer:

- **Emotional states** and the state machine that moves between them.
- **Event → reaction mapping** for normalized events across every domain:
  `interaction.*` (tap, long_press, double_tap/repeated tap, swipe),
  `sensor.*` (pickup, tilt, rotation, shake, proximity, ambient_light),
  `system.*` (charging_start/stop, battery_low/level_change),
  `time.*` (time-of-day / day-night variation), `lifecycle.*` (return, restart).
- **Animation priority, interruptibility, cooldown and minimum-visible time.**
- **Bounded, seedable randomness** for variation (never uncontrolled).
- **Compact long-term memory** — rolling *summaries*, not raw event streams.
- **Slowly-evolving, bounded personality traits** that bias selection weights
  and micro-behavior without breaking core correctness.
- **Battery-conscious sensor usage** — debounced, duty-cycled, released when idle.
- **Testable behavior simulation** so every change has a deterministic case.

You **select** `animation_id`, optional `sound_cue_id` and timing intent. You
**never** draw frames or synthesise audio. When a referenced asset is missing,
emit an explicit handoff request (see the last section).

## Trigger scope & boundaries

Trigger this skill for: defining emotional states, mapping an event to a
reaction, tuning priority/cooldown/interruptibility, sensor *reactions*, memory,
personality, and behavior simulation.

Do **NOT** trigger for (hand off or decline):

- Drawing/animating frames, sprite sheets, GIF previews → `eye-only-pixel-animation-artist`.
- Generating/synthesising sound or music assets → `retro-bit-sound-designer`.
- Generic Android sensor plumbing with **no pet behavior** (raw `SensorManager`
  wiring, logging sensor values) — that is product code, not behavior design.
- Cloud sync, chat/LLM conversation, or unrelated persistence/storage work.

## Behavior design rules (enforce every one)

1. **Normalize events first.** Convert raw input/sensor callbacks into a small,
   stable set of `event_id`s (dotted `domain.snake_case`). Never branch on raw
   Android events inside the selector.
2. **Explicit rule-based selection**, not scattered `if` conditionals. Candidate
   reactions come from a declarative table (the behavior config), ranked by
   priority then a seeded weighted random over `weight`.
3. **Bounded seedable randomness.** Use one seeded PRNG; same inputs + seed →
   identical behavior. No `Math.random()` sprinkled through the code.
4. **Priority preemption.** A higher-priority reaction preempts the running
   animation only if it is `interruptible` and strictly higher priority, or the
   running animation's `minVisibleMs` has elapsed. See
   [`references/behavior-priority-rules.md`](references/behavior-priority-rules.md).
5. **Cooldown / minVisibleMs / interruptibility** are first-class per reaction.
   `cooldownMs` throttles repeat firing; `minVisibleMs` guarantees an animation
   is seen; `interruptible` allows/blocks preemption.
6. **No unbounded tap queues.** Repeated taps collapse (debounce / rate-limit /
   escalate) — never enqueue one animation per tap.
7. **Debounced, rate-limited sensors.** Register a sensor only when it is useful
   and **release** it when idle; duty-cycle for battery. See
   [`references/sensor-safety-and-battery-guide.md`](references/sensor-safety-and-battery-guide.md).
8. **Graceful when a sensor is unavailable.** Every sensor reaction declares
   `requiresSensor` + `sensorFallback` (`ignore` | `degrade` | `alternate`).
9. **Memory = summaries, not the raw stream.** A bounded FIFO of compact
   summaries; oldest drops first. See
   [`references/memory-and-personality-model.md`](references/memory-and-personality-model.md).
10. **Personality changes slowly, within bounds.** Traits stay in `[min,max]`;
    each event nudges by at most `maxDeltaPerEvent`. Personality biases weights
    and micro-behavior — it must never make a reaction fire *incorrectly*.
11. **Time-change / restart must not corrupt state.** Persist summaries and
    traits; reload safely; never crash on a clock jump or cold start.
12. **Every new behavior has a test or a deterministic simulation case**
    (`scripts/simulate_behavior.py`).

## Suggested shared models (adapt to the existing architecture)

`PetEvent`, `PetState`, `ReactionCandidate`, `ReactionDecision`,
`AnimationRequest`, `SoundCueRequest`, `CooldownPolicy`, `BehaviorPriority`,
`SensorSignal`, `InteractionSummary`, `PetMemory`, `PersonalityTraits`,
`TraitDelta`, `BehaviorContext`. These are a starting vocabulary — reuse and
adapt whatever the project already has rather than forcing new types.

## Required workflow

1. Read the goal and the **current behavior architecture** in the target project.
2. Define the **user-visible intent** (what should the pet feel/do?).
3. Identify the **normalized `event_id`** (add to the catalog if new —
   [`references/event-and-reaction-catalog.md`](references/event-and-reaction-catalog.md)).
4. State **preconditions** and the **unavailable-sensor fallback**.
5. Enumerate **candidate reactions**; pick state + `animation_id` +
   optional `sound_cue_id` + `priority` (band) + `cooldownMs` + `minVisibleMs` +
   `interruptible` + duration intent.
6. Define the **memory impact** (one compact summary) and **personality impact**
   (a small bounded delta).
7. Note **battery / performance implications** (which sensors, duty cycle).
8. Implement the **smallest change** compatible with the existing controller.
9. Add a **deterministic test / simulation case**.
10. **Run build/tests**, then validate + simulate the config (below).
11. **Report** the behavior contract and any **missing-asset handoffs**.

## Behavior config + scripts

The behavior config JSON is the single schema both scripts agree on (states,
personality, memory, randomness, reactions). It is written into the target
project at `app/src/main/assets/pet/behavior/`. Field rules and the exact schema
are enforced by the validator.

Validate a config (fails non-zero on any error; `--strict` also fails on warnings):

```bash
python <SKILL_DIR>/scripts/validate_behavior_config.py app/src/main/assets/pet/behavior/behavior.json
python <SKILL_DIR>/scripts/validate_behavior_config.py behavior.json --strict
```

Simulate an event sequence deterministically (same config+events+seed → identical
output), including the unavailable-sensor fallback path:

```bash
python <SKILL_DIR>/scripts/simulate_behavior.py \
    --config behavior.json --events events.json --seed 7

python <SKILL_DIR>/scripts/simulate_behavior.py \
    --config behavior.json --events events.json --seed 7 \
    --available-sensors proximity,light --json
```

`events.json` is a JSON list, e.g.
`[{"event":"interaction.tap","timeMs":0},{"event":"sensor.shake","timeMs":200,"sensor":"accelerometer"}]`.
`<SKILL_DIR>` is the absolute path to this skill; AutoDev injects it into the prompt.

## Supporting files

- [`references/cross-skill-contract.md`](references/cross-skill-contract.md) —
  **canonical** ownership, id conventions, priority bands, degradation rules
  shared with the animation and sound skills. Read it first; do not duplicate it.
- [`references/event-and-reaction-catalog.md`](references/event-and-reaction-catalog.md)
  — canonical normalized event ids + a starter reaction table.
- [`references/behavior-priority-rules.md`](references/behavior-priority-rules.md)
  — priority bands, preemption, cooldown vs minVisibleMs, tie-break, anti-queue.
- [`references/sensor-safety-and-battery-guide.md`](references/sensor-safety-and-battery-guide.md)
  — debounce, register-when-useful + release, fallback, duty cycling.
- [`references/memory-and-personality-model.md`](references/memory-and-personality-model.md)
  — bounded FIFO memory, bounded slow personality, restart safety.
- [`examples/example-touch-reaction.md`](examples/example-touch-reaction.md) —
  full worked `interaction.tap` decision + asset handoffs.
- [`examples/example-sensor-reaction.md`](examples/example-sensor-reaction.md) —
  `sensor.shake` decision with unavailable-sensor fallback + debounce.

## Hard rule — asset handoffs (never fake assets)

When a reaction references an `animation_id` or `sound_cue_id` that has **no
committed asset**, emit an explicit handoff request — do not fabricate the asset:

- **Animation handoff** → `eye-only-pixel-animation-artist`: the `animation_id`,
  the emotional intent, the priority band, and required duration/loop hints.
- **Sound handoff** → `retro-bit-sound-designer`: the `sound_cue_id`, the mood,
  and the animation event it binds to (`events[]` with `type:"sound"`).

A **missing required animation asset is an error** — report it, never silently
skip. A **missing optional sound asset degrades to silent** — a warning; the
behavior must still run. Follow the degradation rules in the cross-skill contract.

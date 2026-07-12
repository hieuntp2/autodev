# Behavior Priority Rules

How the behavior layer resolves competing reactions and protects the currently
playing animation. The priority **bands** are the shared ones defined in the
canonical [`cross-skill-contract.md`](cross-skill-contract.md); this file spells
out the exact resolution algorithm the selector and
[`../scripts/simulate_behavior.py`](../scripts/simulate_behavior.py) implement.

## Priority bands (from the contract)

| Band | Range | Use |
| --- | --- | --- |
| Ambient / idle | 0–9 | breathe, idle blink, idle gaze, time-of-day flavor |
| Light interaction | 10–29 | single tap look-at, swipe glance |
| Strong / emotional | 30–49 | happy reward, affection, annoyed shake |
| Sensor reaction | 50–69 | pickup, tilt, rotation, shake, proximity, ambient light |
| System / device | 70–89 | charging, battery level change |
| Critical override | 90–99 | reserved for safety / system-forced states |

Assign each reaction a concrete `priority` **inside** the right band. Do not
straddle bands for a single reaction.

## Selection algorithm (per normalized event)

1. **Candidates** = reactions whose `event` equals the event id, after sensor
   gating (see [`sensor-safety-and-battery-guide.md`](sensor-safety-and-battery-guide.md)):
   `requiresSensor` null → always eligible; sensor unavailable → apply
   `sensorFallback` (`ignore` drops, `degrade` keeps + marks degraded,
   `alternate` keeps using `alternateAnimation`).
2. **Cooldown filter** — drop any candidate where `now - lastFired < cooldownMs`.
3. **Rank** — pick the highest `priority`. **Tie-break** among equal-priority
   survivors by a **seeded weighted random** over `weight` (deterministic for a
   fixed seed). Higher `weight` = more likely, but never guaranteed.
4. **Preemption decision** against the running animation (next section).
5. On play: set the running animation, stamp `lastFired`, apply the memory and
   personality deltas.

## Preemption vs the running animation

A newly selected reaction **plays** if and only if:

- no animation is currently running, **or**
- the running animation is `interruptible` **and** the newcomer's `priority` is
  **strictly greater** than the running priority, **or**
- the running animation's `minVisibleMs` has already elapsed
  (`now - startedMs >= minVisibleMs`).

Otherwise the event is **suppressed** — report *why* (which running animation,
its remaining minVisible time, whether it was interruptible). Suppressed events
do **not** stamp cooldown or mutate memory/personality.

## cooldownMs vs minVisibleMs (do not conflate)

- **`cooldownMs`** — minimum gap before *this same reaction* may fire again.
  Prevents spammy re-triggering (e.g. tap reward chirp on every rapid tap).
- **`minVisibleMs`** — minimum time an animation stays on screen before a
  same-or-lower-priority reaction may replace it. Guarantees the user actually
  *sees* a reaction instead of it being instantly overwritten.

A higher-band reaction can still preempt before `minVisibleMs` **only** if the
running animation is `interruptible`.

## Anti-queue rule for repeated taps

Never enqueue one animation per tap. Rapid taps are normalized to a single
`interaction.repeated_tap` (or coalesced into the in-flight reaction). Combine
with a `cooldownMs` so the pet reacts once and settles, rather than stuttering
through a backlog. Unbounded input queues are a defect.

## Interruptibility guidance

- Idle/ambient animations: `interruptible: true` (anything real should take over).
- Strong emotional beats you want the user to see: modest `minVisibleMs`
  (e.g. 400–600 ms) and `interruptible: true` above their band.
- Sensor "shock" reactions: often `interruptible: false` with a short
  `minVisibleMs` so a surprise reads clearly, then yields.
- Critical/system-forced (90–99): rarely interruptible; use sparingly.

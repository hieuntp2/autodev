# Memory & Personality Model

The pet feels like it *remembers* you and has a *temperament* — but both must be
compact, bounded, and safe across restarts. This is the behavior layer's private
state; it biases selection, it never overrides correctness.

## Long-term memory = bounded summaries (not the raw stream)

- Memory is a **FIFO buffer of compact summaries**, capped at
  `memory.maxSummaries`. When full, the **oldest** entry drops first.
- A summary is a short, stable string (or a small counted record), e.g.
  `"tapped"`, `"shaken"`, `"charged overnight"` — **not** raw timestamps or a
  per-frame log. Never persist the raw event stream.
- Reactions contribute memory through `memoryDelta` (e.g.
  `{"summary":"tapped","affection":1}`). The `summary` text is appended to the
  buffer; other keys are lightweight counters you may aggregate.
- Use memory to *flavor* behavior (a frequently-tapped pet leans more playful),
  never to make a reaction fire incorrectly.

## Personality = slowly-evolving bounded traits

- Traits (e.g. `playfulness`, `shyness`, `energy`) are floats clamped to
  `[personality.min, personality.max]` (default `[0.0, 1.0]`).
- Each event may nudge a trait via `personalityDelta`, but the magnitude is
  **capped at `personality.maxDeltaPerEvent`** (e.g. 0.03). The validator rejects
  any `personalityDelta` larger than this. Traits therefore drift *slowly* over
  many interactions — never lurch.
- After applying a delta, **clamp** back into `[min, max]`.

## How personality biases selection (without breaking correctness)

- Personality feeds the **`weight`** used in the seeded tie-break among
  equal-priority reactions (see
  [`behavior-priority-rules.md`](behavior-priority-rules.md)). A high
  `playfulness` can raise the effective weight of the playful variant so it is
  *more likely* — but a strictly higher-priority reaction still wins.
- Personality may also tune *micro-behavior* (blink cadence, idle fidget rate),
  which are ambient (0–9) and never contend with real reactions.
- **Invariant:** personality changes probabilities and flavor, **never** which
  band wins or whether a required reaction is allowed to fire.

## Persistence & restart safety

- Persist only the bounded memory buffer and the trait values (small, cheap).
- On `lifecycle.restart` / cold start, **reload** them; if the store is missing
  or corrupt, fall back to defaults instead of crashing.
- A **clock jump** (time change, timezone, reboot) must not corrupt state: derive
  time-of-day from the current clock each check; never assume monotonic wall time
  for memory/personality. Cooldown/minVisible timing uses elapsed/relative time.
- Writes are idempotent and small enough to survive being interrupted mid-write
  (write-then-swap or tolerate a stale-but-valid buffer).

## Determinism for tests

Given the same config, event sequence and `--seed`,
[`../scripts/simulate_behavior.py`](../scripts/simulate_behavior.py) produces an
identical memory buffer and identical final traits. Use it as the deterministic
test oracle for any memory/personality change.

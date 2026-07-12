# Animation ↔ sound sync guide

How a sound cue produced by `retro-bit-sound-designer` binds to a visual animation
produced by `eye-only-pixel-animation-artist`.

## The binding is in the animation manifest

Audio is bound **through the animation manifest's `events[]` array**, which already
exists in the pixel animation schema. A `type:"sound"` event carries the
`sound_cue_id` as its `value`:

```json
"events": [
  { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }
]
```

- The **animation skill** writes the hook (the id) into the manifest.
- **This skill** produces the matching asset at
  `<sounds-dir>/<value>/<value>.sound.json` (+ `.wav` / `.ogg`) and records the same
  id inside `<id>.sound.json` (`"id"` and `"animationId"`).
- `scripts/validate_sound_links.py` confirms every `type:"sound"` value resolves to
  a real asset (or is an intentional `optional` stub) — it never silently passes a
  dangling cue.

The id is the single contract point. It must match `^[a-z][a-z0-9_]*$`, be stable,
and never be reused for a different meaning.

## Choosing the cue frame / cue time

- A manifest event fires at a `timeMs` on the animation timeline. Convert your
  intended **cue frame** to a time by summing the `durationMs` of the frames before
  it. Record both `cueFrame` and `cueTimeMs` in `<id>.sound.json` for traceability.
- Put the cue at the **moment of visual impact** (the sparkle appears, the eyes snap
  shut, the power ring completes) — not at the very first frame unless the reaction
  is instantaneous.
- Give the audio system a hair of lead: a 10–30 ms early cue often *feels* tighter
  than a dead-on one because attack takes a few ms to reach full level.

## Keep the SFX shorter than the visible moment

The sound should finish within (or just around) the frame span it accents. A cue
that outlasts its animation gets truncated or overlaps the next reaction and muddies
rapid interactions. Rule of thumb: `durationMs` ≲ the on-screen time from the cue
frame to the end of the clip, and always `<= 1500 ms`.

## Optional vs required cues

- **Required** (default): a `type:"sound"` event with no `optional` flag. If its
  asset is missing, `validate_sound_links.py` **fails** — the reaction is expected
  to have audio.
- **Optional**: mark the event `"optional": true`. A missing asset is then a
  **warning**, and the runtime plays the animation silently (graceful degradation).
  Use this for nice-to-have flourishes and for staged rollouts where the visual
  ships before the audio.

```json
{ "timeMs": 40, "type": "sound", "value": "curious_two_tone", "optional": true }
```

## Runtime degradation

Missing **optional** sound → silent playback, never a crash or block. Behavior and
render code must treat audio as best-effort. A missing **required** cue is a build/
validation failure to fix before shipping, not a runtime surprise.

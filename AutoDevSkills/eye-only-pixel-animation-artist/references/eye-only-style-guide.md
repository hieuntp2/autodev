# Eye-Only Style Guide

The rules that make every asset read as the same cyan-eye pet. Bind these into
the drawing config you hand to the reused `pixel-animation-artist` engine.

## Product identity

- The **whole phone screen is the pet's face**. There is **no default full body**.
- The **cyan eyes are the primary identity and emotional center**. Almost every
  emotion is expressed through the eyes (shape, openness, position, squish).
- Accessories/effects (hearts, sparkles, magnifier, sweat, stars, sleep "Z") are
  **temporary, secondary** overlays. They are cleared between animations and never
  become part of the permanent identity.

## Palette (concrete)

Sharp, flat colours only — a small palette per animation (validator caps distinct
colours per frame at 32 by default).

| Role | Hex | RGB | Use |
| --- | --- | --- | --- |
| Background | `#000000` | `(0, 0, 0)` | fills the screen; opaque black or fully transparent |
| Eye (primary) | `#00E5FF` | `(0, 229, 255)` | the cyan iris/eye body — the identity colour |
| Eye highlight | `#7DF9FF` | `(125, 249, 255)` | a brighter cyan for a 1–2px specular block |
| Eye dim / shade | `#00A5BF` | `(0, 165, 191)` | a darker cyan block for lid shadow or depth |
| Eye core / pupil | `#003A44` | `(0, 58, 68)` | near-black cyan for a pupil or closed-lid line |
| Accent (accessory) | varies | — | e.g. hearts `#FF4D6D`, sparkle `#FFF275`; kept small + temporary |

Shading is done with **separate flat blocks** of the dim/highlight cyan — NEVER a
gradient ramp and NEVER alpha blending.

## No anti-aliasing / blur / gradient rule

- Every pixel is a **solid palette colour**. Alpha is strictly `0` (transparent)
  or `255` (opaque) — **no partial transparency**, which is how the validator
  proves there are no soft/anti-aliased edges.
- Drawing **replaces** pixels; it never blends. No feathered edges, no glow, no
  drop shadow, no gradient fill.
- Curves are stepped pixel blocks (the engine's Bresenham line / integer circle),
  not smoothed arcs.

## Eye anchor concept

An **eye anchor** is the fixed `(x, y)` centre of each eye, defined once for the
pet and **kept consistent across all frames of every animation**. It is what keeps
the pet's identity from drifting when the eyes blink, move or squish.

- Choose left/right eye anchors relative to the canvas (e.g. on a 64×64 canvas,
  left eye centre `(22, 30)`, right eye centre `(42, 30)`).
- Every frame draws the eyes around those anchors. Openness/shape changes; the
  centre does not (except for deliberate gaze offsets, which should be small and
  symmetric).
- Record the anchors in the manifest (`eyeAnchors`) so other animations for the
  same pet reuse identical values.

## Accessory / effect area

- Accessories live in a **secondary area** away from the eye anchors (e.g. upper
  corners for sparkles/stars, above the eyes for a sleep "Z", beside an eye for a
  sweat drop).
- They are **temporary**: present only for the frames that need them and gone by
  the end of the clip. Do not bake an accessory into the pet's resting/idle look.
- Keep accessories small and flat — they must not compete with the eyes for
  identity.

## Canvas guidance

- Default canvas is a **square-ish portrait** (e.g. **64×64**), configurable via
  the engine's `frameWidth` / `frameHeight`. The Android renderer scales the small
  canvas up to the full screen with **nearest-neighbour** only, so the sharp
  pixels stay sharp.
- Pick the smallest canvas that expresses the eye detail you need; more pixels
  means slower authoring and larger sheets for no visual gain in flat pixel art.
- Keep the eyes large and central — they own the screen.

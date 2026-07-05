# pixel-animation-artist

Generate **deterministic pixel-art animations** for pixel pets, games and UI
avatars. One skill, used globally by AutoDev — never copied into individual
project repos. Only the *generated asset* lands in the target project.

## What it produces

For an animation with id `<id>`, into an output directory in the **target
project** (e.g. `assets/pet/animations/<id>/`):

```
frames/frame_000.png   # one PNG per unique frame image ("cell"), 64x32 by default
frames/frame_001.png
...
<id>_sheet.png         # sprite sheet: every cell laid out in a grid, ONLY images
<id>_preview.gif       # nearest-neighbour scaled flip-book preview
<id>.animation.json    # manifest that drives how the animation plays
```

## Hard rules

- **Deterministic**: same input → identical output. No randomness, no timestamps.
- **No anti-aliasing, no blur, no gradients**. Every pixel is a solid palette
  colour; alpha is only `0` or `255`. Drawing replaces pixels, never blends.
- **The sprite sheet holds only frame images.** Nothing about *playback* is baked
  into the pixels. Frame order, per-frame duration, whole-sprite motion/tween,
  timeline events, loop, priority and interruptibility all live in the JSON
  manifest so the same art can be driven differently by different runtimes.
- **Preview scaling is nearest-neighbour only.**
- Canvas is **64×32 by default** but configurable (`frameWidth` / `frameHeight`).

## How to invoke (for the coding agent)

The scripts are Python + Pillow. Install once: `python -m pip install -r requirements.txt`.

**Fastest path — a built-in preset:**

```bash
python <SKILL_DIR>/scripts/draw_pixel_animation.py \
    --preset curious_magnifier \
    --out assets/pet/animations/curious_magnifier
```

**Full control — your own config** (see "Config format" below):

```bash
python <SKILL_DIR>/scripts/draw_pixel_animation.py \
    --config my_anim.json \
    --out assets/pet/animations/my_anim
```

**Always validate the result** and include the outcome in your run report:

```bash
python <SKILL_DIR>/scripts/validate_pixel_animation.py \
    assets/pet/animations/curious_magnifier --min-frames 8 --expect-size 64x32
```

`<SKILL_DIR>` is the absolute path to this skill; AutoDev injects it into your
prompt. Write output **into the target project**, never back into the skill.

## Config format (input)

A config describes the animation to synthesise. Two authoring styles:

**A) `cells` (unique images) + `frames` playlist referencing them by index** —
lets you reuse a cell in several playlist slots:

```json
{
  "id": "blink",
  "frameWidth": 64, "frameHeight": 32, "scale": 8, "columns": 4,
  "loop": true, "priority": 10, "interruptible": true,
  "background": [0, 0, 0, 0],
  "palette": { "eye": [40, 40, 60], "skin": [240, 220, 180] },
  "cells": [
    { "draw": [ { "op": "rect", "x": 20, "y": 12, "w": 24, "h": 8, "color": "skin" } ] },
    { "draw": [ { "op": "rect", "x": 20, "y": 12, "w": 24, "h": 8, "color": "skin" },
                { "op": "hline", "x": 24, "y": 16, "w": 16, "color": "eye" } ] }
  ],
  "frames": [
    { "index": 0, "durationMs": 900 },
    { "index": 1, "durationMs": 80 },
    { "index": 0, "durationMs": 120 }
  ],
  "motion": [
    { "timeMs": 0,    "x": 0, "y": 0, "scale": 1.0, "rotation": 0, "alpha": 1.0, "easing": "linear" },
    { "timeMs": 1100, "x": 0, "y": 0, "scale": 1.0, "rotation": 0, "alpha": 1.0, "easing": "linear" }
  ],
  "events": [ { "timeMs": 900, "type": "sound", "value": "blink.wav" } ]
}
```

**B) `frames[].draw`** — each playlist entry carries its own art (cell == entry).

### Draw ops (deterministic, integer pixels)

| op | fields |
| --- | --- |
| `fill` | `color` |
| `rect` | `x, y, w, h, color` (filled) |
| `frame` | `x, y, w, h, color` (1px outline) |
| `pixel` | `x, y, color` |
| `hline` / `vline` | `x, y, w` / `x, y, h`, `color` |
| `line` | `x1, y1, x2, y2, color` (Bresenham) |
| `circle` | `cx, cy, r, color, fill?` |
| `ellipse` | `cx, cy, rx, ry, color, fill?` |

Colours: a palette name, `#RRGGBB` / `#RRGGBBAA`, or `[r,g,b]` / `[r,g,b,a]`.

## Manifest format (output)

See [`schema/animation.schema.json`](schema/animation.schema.json). Fields:
`id`, `spriteSheet`, `frameWidth`, `frameHeight`, `columns`, `rows`, `loop`,
`priority`, `interruptible`, `frames[]` (`index` + `durationMs`), `motion[]`
(`timeMs, x, y, scale, rotation, alpha, easing`), `events[]`
(`timeMs, type, value`). A runtime engine reads this and drives a sprite from
the sheet — it never needs to re-read the pixels to know how to play.

A committed reference lives in [`samples/curious_magnifier/`](samples/curious_magnifier/).

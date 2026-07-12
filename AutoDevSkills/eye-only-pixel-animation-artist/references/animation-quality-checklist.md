# Animation Quality Checklist

Run through this before reporting an eye-only animation done. Each item maps to a
concrete check in `scripts/validate_pixel_assets.py` or
`scripts/validate_animation_manifest.py`. Both validators must exit 0.

## Frames (validate_pixel_assets.py)

- [ ] `frames/` exists and holds at least `--min-frames` (default 2) files.
- [ ] Files are sequential: `frame_000.png, frame_001.png, ...` with no gaps.
- [ ] Every frame is the same size and equals `--expect-size` (e.g. `64x64`).
- [ ] **Alpha is strictly 0 or 255** — no partial transparency, so no
      anti-aliased / blurred / soft edges. (Hard fail on any in-between alpha.)
- [ ] Each frame uses `<= --max-colors` (default 32) distinct colours — flat
      pixel art, no gradients.
- [ ] Background is **predominantly black `(0,0,0)` or fully transparent**
      (choose with `--bg black|transparent|any`).
- [ ] The **cyan eye colour** (`~#00E5FF`, `--require-eye-color`) is present.
      Soft WARN by default; hard fail under `--strict-eye`.

## Sprite sheet (validate_pixel_assets.py, when `<id>_sheet.png` present)

- [ ] Sheet dimensions are a whole multiple of the frame size.
- [ ] The implied grid holds all frames with no fully-empty trailing row.
- [ ] **Every sheet cell is pixel-identical to its source frame PNG** — the
      packaging copied pixels verbatim (no scaling / resampling drift).

## Manifest (validate_animation_manifest.py)

- [ ] Required fields present: `id`, `spriteSheet`, `frameWidth`, `frameHeight`,
      `frames[]`.
- [ ] Every `frames[]` entry has `index >= 0` and `durationMs >= 1`.
- [ ] `spriteSheet` resolves to a real PNG next to the manifest.
- [ ] Every `frames[].index` is `< total sheet cells` (bounds-checked against the
      sheet's real dimensions).
- [ ] `loop` / `interruptible` are booleans if present; `priority` is an int if
      present.
- [ ] Every `type:"sound"` event has a `value` matching `^[a-z][a-z0-9_]*$` and
      is reported as a sound hook for the sound skill.
- [ ] (Optional) `--expect-id` matches the manifest `id`.

## Product identity (manual)

- [ ] Eyes stay on their **eye anchors** across all frames (identity does not
      drift).
- [ ] Accessories/effects are temporary and confined to the accessory area, not
      baked into the resting look.
- [ ] Preview GIF scaling is nearest-neighbour (crisp, not smoothed).
- [ ] No file is claimed to exist unless it exists and a validator passed.

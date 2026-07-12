---
name: eye-only-pixel-animation-artist
description: Draw, package and validate eye-only cyan-eye pet-face animation assets (sharp PNG frames + sprite sheet + preview GIF + animation.json manifest) for the Android pet whose whole screen is its face, on a black background with no anti-aliasing, blur or gradients.
---

# eye-only-pixel-animation-artist

Produce the **visual animation assets** for the eye-only pixel pet: an Android
product where the **whole phone screen is the pet's face**. There is **no default
full body**. The **cyan eyes** are the primary identity and emotional center;
accessories/effects (hearts, sparkles, magnifier, sweat, stars, sleep "Z") are
**temporary secondary** elements. Background is **black**; pixels are **sharp** —
no anti-aliasing, blur, gradients or soft shadows.

This skill **owns** the visual assets + the animation manifest for an
`animation_id`. It does **not** decide behavior and does **not** make sound. It
**reuses** the generic `pixel-animation-artist` engine to draw frames, then adds
eye-only style rules, packaging (sprite sheet + preview GIF) and validation. The
sound link lives in the manifest `events[]` as
`{ "type": "sound", "value": "<sound_cue_id>" }` — the audio asset is owned by
`retro-bit-sound-designer`. See `references/cross-skill-contract.md`.

## When this triggers

Use it for requests about the pet's **eyes / face as animation assets**: blink,
gaze, eyelid, eye squish, surprised wide-eyes, sleepy half-close, "redraw as
eye-only", eye-emotion transitions, or accessory/effect frames layered around the
eyes.

### Hard boundaries — do NOT use this skill for:

- **Behavior decisions** (which reaction fires, priority, cooldown, timing intent)
  → that is `pet-behavior-designer`.
- **Sound / audio** (chiptune, chirps, `.wav`/`.ogg`, sound cue synthesis)
  → that is `retro-bit-sound-designer`.
- **Generic Android UI** (screens, buttons, spinners, layout) — not pet art.
- **Non-pixel illustration** (watercolor, soft-shaded, anti-aliased art) or
  **full-body character sheets** — this product is eye-only, sharp pixels only.

## Product identity rules (never violate)

- Background **black** (`#000000`) or fully transparent — nothing else fills it.
- Cyan eyes are the identity anchor; default eye colour `#00E5FF`.
- **No anti-aliasing / blur / gradients / soft shadows.** Alpha is strictly `0`
  or `255`. Every pixel is a solid palette colour.
- **Eye anchors** (the fixed `(x, y)` centre of each eye) stay consistent across
  every frame of a given pet so its identity never drifts. See
  `references/eye-only-style-guide.md`.
- Accessories/effects live in a temporary secondary area and are **cleared
  between animations** — they never become permanent identity.

## Required workflow

1. **Read the goal & existing assets.** Understand the requested `animation_id`,
   the emotional intent, and what already exists for this pet.
2. **Inspect the current animation implementation & asset path.** Find where the
   target project keeps animations (`app/src/main/assets/pet/animations/<id>/`)
   and the current eye anchors so new frames stay consistent.
3. **Write a compact animation brief** (see `examples/example-animation-brief.md`)
   covering: animation id, emotional intent, trigger/context (`event_id`/state),
   frame count, frame dimensions, **eye anchor positions**, accessory/effect area,
   loop, interruptibility, and the expected `sound_cue_id`(s).
4. **Preserve eye identity & anchor consistency** — reuse the same eye centres and
   palette as the pet's other animations.
5. **Draw / update frames — REUSE the pixel-animation-artist engine.** Do not
   re-implement drawing. Author a config and run:
   ```bash
   python ../pixel-animation-artist/scripts/draw_pixel_animation.py \
       --config eye_blink.json \
       --out app/src/main/assets/pet/animations/blink_idle
   ```
   (`../pixel-animation-artist` = `AutoDevSkills/pixel-animation-artist`; AutoDev
   injects the absolute skill paths into your prompt.) Config format and draw ops
   are documented in that engine's `SKILL.md`. Keep the black bg + cyan eyes +
   sharp-pixel rules above.
6. **Build the sprite sheet:**
   ```bash
   python scripts/build_sprite_sheet.py \
       --frames app/src/main/assets/pet/animations/blink_idle/frames \
       --out   app/src/main/assets/pet/animations/blink_idle/blink_idle_sheet.png
   ```
7. **Build the preview GIF** (nearest-neighbour scaled; pass the manifest so the
   preview matches playback timing):
   ```bash
   python scripts/build_preview_gif.py \
       --frames   app/src/main/assets/pet/animations/blink_idle/frames \
       --out      app/src/main/assets/pet/animations/blink_idle/blink_idle_preview.gif \
       --manifest app/src/main/assets/pet/animations/blink_idle/blink_idle.animation.json \
       --scale 6
   ```
8. **Write / update the manifest** `<id>.animation.json`, conforming to the shared
   schema `../pixel-animation-artist/schema/animation.schema.json`. Record eye-only
   metadata (`eyeAnchors`, `accessoryArea` — additive fields, allowed) and the
   sound hook in `events[]`. See `references/animation-manifest-guide.md`.
9. **Run BOTH validators** and include the results in your report:
   ```bash
   python scripts/validate_pixel_assets.py \
       app/src/main/assets/pet/animations/blink_idle \
       --id blink_idle --min-frames 2 --expect-size 64x64 --bg black

   python scripts/validate_animation_manifest.py \
       app/src/main/assets/pet/animations/blink_idle/blink_idle.animation.json \
       --expect-id blink_idle
   ```
10. **Report the exact artifacts + validation results.**

## Hard rule on honesty

**NEVER** claim a frame, GIF, sprite sheet or manifest exists unless the file
actually exists on disk **and** the relevant validator ran and passed. If a
required visual asset is missing, that is an **error** to report explicitly —
never faked, never silently skipped (per the cross-skill contract).

## Expected Outputs

Into the **target project** (never back into `AutoDevSkills/`):

```
app/src/main/assets/pet/animations/<animation_id>/
  frames/frame_000.png        # sharp cyan-eye frames, black/transparent bg, alpha 0/255
  frames/frame_001.png
  ...
  <animation_id>_sheet.png    # sprite sheet: verbatim frame copies, row-major
  <animation_id>_preview.gif  # nearest-neighbour scaled flip-book preview
  <animation_id>.animation.json   # manifest (frames[], events[] sound hook, eyeAnchors)
```

## Supporting files

- `references/eye-only-style-guide.md` — cyan palette, black bg, no-AA rule, eye
  anchor concept, accessory/effect area, canvas guidance.
- `references/animation-manifest-guide.md` — manifest fields, eye-only metadata
  conventions, the `events[]` sound hook.
- `references/animation-quality-checklist.md` — the concrete pass/fail checklist
  mirroring the validators.
- `references/cross-skill-contract.md` — short pointer to the canonical contract +
  shared id table + sound-hook rule.
- `examples/example-animation-brief.md` — a filled brief for `happy_reward_sparkle`.
- `scripts/build_sprite_sheet.py` — frames → `<id>_sheet.png` (see step 6).
- `scripts/build_preview_gif.py` — frames → `<id>_preview.gif` (see step 7).
- `scripts/validate_pixel_assets.py` — validate the asset dir (see step 9).
- `scripts/validate_animation_manifest.py` — validate the manifest (see step 9).

Every script prints `--help`, is deterministic, never modifies source assets, and
exits non-zero on a validation error.

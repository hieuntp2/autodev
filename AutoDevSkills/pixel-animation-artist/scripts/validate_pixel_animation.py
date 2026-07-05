#!/usr/bin/env python3
"""
validate_pixel_animation.py — verifies a generated pixel animation.

Part of the AutoDev global skill `pixel-animation-artist`. Run it on the output
directory produced by draw_pixel_animation.py. Exits 0 on success, non-zero on
the first hard failure, and prints a human-readable report either way.

Checks:
  * manifest parses and has all required fields
  * frame count matches the highest index used by the playlist (>= --min-frames)
  * every frame PNG is exactly frameWidth x frameHeight, mode RGBA
  * background is transparent (frame corners have alpha 0)
  * NO anti-aliasing / blur / gradient: every pixel's alpha is strictly 0 or 255
    and each frame uses no more than --max-colors distinct colours
  * sprite sheet exists, has the expected columns*fw x rows*fh size, and each
    cell in the sheet is pixel-identical to its frame PNG (no wrong scaling)
  * GIF preview exists and is scaled by an integer nearest-neighbour factor
  * playlist durations are positive; motion keyframes are time-sorted with valid
    easing; events are time-sorted with a valid type

Usage:
  python validate_pixel_animation.py <output_dir> [--id ID] [--min-frames 8]
                                     [--max-colors 64] [--expect-size 64x32]
Requires: Pillow
"""
import argparse
import glob
import json
import os
import sys

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.stderr.write("ERROR: Pillow is required (python -m pip install Pillow).\n")
    sys.exit(3)

VALID_EASING = {"linear", "easeIn", "easeOut", "easeInOut", "step"}
VALID_EVENT_TYPES = {"sound", "effect", "callback"}


class Report:
    def __init__(self):
        self.errors = []
        self.warnings = []
        self.checks = []

    def ok(self, msg):
        self.checks.append(("PASS", msg))

    def warn(self, msg):
        self.warnings.append(msg)
        self.checks.append(("WARN", msg))

    def fail(self, msg):
        self.errors.append(msg)
        self.checks.append(("FAIL", msg))

    def require(self, cond, msg):
        (self.ok if cond else self.fail)(msg)
        return cond


def find_manifest(out_dir, anim_id):
    if anim_id:
        p = os.path.join(out_dir, f"{anim_id}.animation.json")
        return p if os.path.exists(p) else None
    hits = glob.glob(os.path.join(out_dir, "*.animation.json"))
    return hits[0] if hits else None


def distinct_colors(img):
    return {img.getpixel((x, y)) for y in range(img.height) for x in range(img.width)}


def validate(out_dir, anim_id, min_frames, max_colors, expect_size):
    r = Report()

    manifest_path = find_manifest(out_dir, anim_id)
    if not r.require(manifest_path is not None, f"manifest present in {out_dir}"):
        return r
    with open(manifest_path, "r", encoding="utf-8") as f:
        m = json.load(f)

    for key in ("id", "spriteSheet", "frameWidth", "frameHeight", "frames"):
        r.require(key in m, f"manifest has required field '{key}'")
    if r.errors:
        return r

    fw, fh = int(m["frameWidth"]), int(m["frameHeight"])
    anim_id = m["id"]
    r.ok(f"animation id = '{anim_id}', frame size = {fw}x{fh}")

    if expect_size:
        ew, eh = (int(v) for v in expect_size.lower().split("x"))
        r.require((fw, fh) == (ew, eh), f"frame size is the expected {ew}x{eh}")

    # ---- frames playlist ----
    frames = m["frames"]
    r.require(isinstance(frames, list) and len(frames) >= 1, "playlist has at least one frame")
    r.require(len(frames) >= min_frames, f"playlist has >= {min_frames} frames (got {len(frames)})")
    for i, fr in enumerate(frames):
        r.require("index" in fr and "durationMs" in fr, f"frame[{i}] has index + durationMs")
        if "durationMs" in fr:
            r.require(int(fr["durationMs"]) > 0, f"frame[{i}] durationMs > 0")

    # ---- frame PNGs ----
    frames_dir = os.path.join(out_dir, "frames")
    png_paths = sorted(glob.glob(os.path.join(frames_dir, "frame_*.png")))
    r.require(len(png_paths) >= 1, "at least one frame PNG exists")
    max_index = max((int(fr["index"]) for fr in frames if "index" in fr), default=-1)
    r.require(len(png_paths) > max_index,
              f"enough frame PNGs ({len(png_paths)}) to cover max playlist index {max_index}")

    cell_imgs = []
    for p in png_paths:
        img = Image.open(p)
        cell_imgs.append(img.convert("RGBA"))
        base = os.path.basename(p)
        r.require(img.size == (fw, fh), f"{base} is {fw}x{fh} (got {img.size[0]}x{img.size[1]})")
        r.require(img.mode == "RGBA", f"{base} is RGBA (got {img.mode})")

    # ---- transparency + no anti-aliasing / blur / gradient ----
    for p, img in zip(png_paths, cell_imgs):
        base = os.path.basename(p)
        corners = [img.getpixel((0, 0)), img.getpixel((fw - 1, 0)),
                   img.getpixel((0, fh - 1)), img.getpixel((fw - 1, fh - 1))]
        r.require(all(c[3] == 0 for c in corners), f"{base} has a transparent background (corners alpha=0)")

        alphas = {img.getpixel((x, y))[3] for y in range(fh) for x in range(fw)}
        r.require(alphas <= {0, 255},
                  f"{base} has no soft/anti-aliased edges (alpha strictly 0 or 255)")

        colors = distinct_colors(img)
        r.require(len(colors) <= max_colors,
                  f"{base} uses <= {max_colors} distinct colours (got {len(colors)}) — no gradients/blur")

    # ---- sprite sheet ----
    sheet_path = os.path.join(out_dir, m["spriteSheet"])
    if r.require(os.path.exists(sheet_path), f"sprite sheet '{m['spriteSheet']}' exists"):
        sheet = Image.open(sheet_path).convert("RGBA")
        columns = int(m.get("columns", len(cell_imgs)))
        rows = int(m.get("rows", (len(cell_imgs) + columns - 1) // columns))
        r.require(sheet.size == (columns * fw, rows * fh),
                  f"sheet size is {columns}x{rows} cells = {columns * fw}x{rows * fh}")
        # Each sheet cell must be pixel-identical to its frame PNG (no scaling drift).
        mismatches = 0
        for i, cell in enumerate(cell_imgs):
            col, row = i % columns, i // columns
            region = sheet.crop((col * fw, row * fh, col * fw + fw, row * fh + fh))
            if region.tobytes() != cell.tobytes():
                mismatches += 1
        r.require(mismatches == 0, f"every sheet cell matches its frame PNG exactly (mismatches: {mismatches})")

    # ---- GIF preview ----
    gif_path = os.path.join(out_dir, f"{anim_id}_preview.gif")
    if r.require(os.path.exists(gif_path), f"GIF preview '{anim_id}_preview.gif' exists"):
        gif = Image.open(gif_path)
        gw, gh = gif.size
        r.require(gw % fw == 0 and gh % fh == 0,
                  f"GIF is an integer nearest-neighbour scale of {fw}x{fh} (got {gw}x{gh})")

    # ---- motion + events ordering / vocab ----
    motion = m.get("motion", [])
    times = [kf.get("timeMs", 0) for kf in motion]
    r.require(times == sorted(times), "motion keyframes are sorted by timeMs")
    for i, kf in enumerate(motion):
        r.require(kf.get("easing", "linear") in VALID_EASING, f"motion[{i}] easing is valid")

    events = m.get("events", [])
    etimes = [e.get("timeMs", 0) for e in events]
    r.require(etimes == sorted(etimes), "events are sorted by timeMs")
    for i, e in enumerate(events):
        r.require(e.get("type") in VALID_EVENT_TYPES, f"events[{i}] type is valid")

    return r


def main():
    ap = argparse.ArgumentParser(description="Validate a generated pixel animation.")
    ap.add_argument("out_dir", help="Directory produced by draw_pixel_animation.py.")
    ap.add_argument("--id", help="Animation id (else the first *.animation.json is used).")
    ap.add_argument("--min-frames", type=int, default=1, help="Minimum playlist length.")
    ap.add_argument("--max-colors", type=int, default=64, help="Max distinct colours per frame.")
    ap.add_argument("--expect-size", help="Assert frame size, e.g. 64x32.")
    args = ap.parse_args()

    r = validate(args.out_dir, args.id, args.min_frames, args.max_colors, args.expect_size)

    print(f"\nValidation report for: {args.out_dir}")
    print("-" * 60)
    for status, msg in r.checks:
        print(f"  [{status}] {msg}")
    print("-" * 60)
    if r.errors:
        print(f"FAILED — {len(r.errors)} error(s), {len(r.warnings)} warning(s).")
        sys.exit(1)
    print(f"PASSED — {len(r.checks)} checks, {len(r.warnings)} warning(s).")
    sys.exit(0)


if __name__ == "__main__":
    main()

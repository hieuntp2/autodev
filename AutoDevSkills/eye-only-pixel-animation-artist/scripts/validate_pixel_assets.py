#!/usr/bin/env python3
"""
validate_pixel_assets.py — verify an eye-only pixel-animation asset directory.

Part of the AutoDev global skill `eye-only-pixel-animation-artist`. Run it on an
animation asset directory (the `frames/` folder plus an optional `<id>_sheet.png`).
It proves the art is SHARP pixel art (no anti-aliasing / blur / gradients / soft
shadows) and matches the eye-only product identity. It NEVER modifies any source
asset — it only reads.

Checks (hard = FAIL / soft = WARN):
  * frames/ exists and holds >= --min-frames sequential frame_###.png     (hard)
  * all frames identical size, == --expect-size WxH (default: first frame) (hard)
  * alpha is strictly 0 or 255 — no partial transparency / soft edges      (hard)
  * distinct colour count per frame <= --max-colors (flat pixel art)       (hard)
  * background predominantly black (0,0,0) or fully transparent            (hard,
        selected by --bg black|transparent|any; 'any' reports which it is)
  * cyan eye colour present near --require-eye-color                       (soft;
        hard only with --strict-eye)
  * if <id>_sheet.png present: its grid dims are consistent with the frame
        count and every sheet cell is pixel-identical to its frame PNG     (hard)

Exit code: 0 = PASS, 1 = at least one hard FAIL, 2 = usage error, 3 = no Pillow.

Usage:
  python validate_pixel_assets.py <asset_dir> [--id ID] [--min-frames 2]
        [--expect-size 64x64] [--max-colors 32] [--bg any|black|transparent]
        [--require-eye-color 00E5FF] [--eye-tolerance 40] [--strict-eye]
Requires: Pillow
"""
import argparse
import glob
import os
import sys

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.stderr.write(
        "ERROR: missing dependency 'Pillow'. Install it with:\n"
        "  python -m pip install Pillow\n"
    )
    sys.exit(3)


class Report:
    def __init__(self):
        self.checks = []
        self.errors = []
        self.warnings = []

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


def parse_size(text):
    a, b = text.lower().split("x")
    return int(a), int(b)


def hex_to_rgb(h):
    h = h.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


def find_sheet(asset_dir, anim_id):
    if anim_id:
        p = os.path.join(asset_dir, f"{anim_id}_sheet.png")
        return p if os.path.exists(p) else None
    hits = sorted(glob.glob(os.path.join(asset_dir, "*_sheet.png")))
    return hits[0] if hits else None


def validate(args):
    r = Report()
    asset_dir = args.asset_dir

    frames_dir = os.path.join(asset_dir, "frames")
    if not r.require(os.path.isdir(frames_dir), f"frames/ directory exists in {asset_dir}"):
        return r
    png_paths = sorted(glob.glob(os.path.join(frames_dir, "frame_*.png")))
    if not r.require(len(png_paths) >= args.min_frames,
                     f"frames/ has >= {args.min_frames} frame_###.png (got {len(png_paths)})"):
        return r

    # sequential numbering check
    expected = [f"frame_{i:03d}.png" for i in range(len(png_paths))]
    actual = [os.path.basename(p) for p in png_paths]
    r.require(actual == expected,
              f"frame files are sequential frame_000..frame_{len(png_paths) - 1:03d}")

    imgs = [Image.open(p).convert("RGBA") for p in png_paths]

    if args.expect_size:
        ew, eh = parse_size(args.expect_size)
    else:
        ew, eh = imgs[0].size
        r.ok(f"expected frame size inferred from first frame: {ew}x{eh}")

    for p, im in zip(png_paths, imgs):
        base = os.path.basename(p)
        r.require(im.size == (ew, eh),
                  f"{base} is {ew}x{eh} (got {im.size[0]}x{im.size[1]})")

    # --- sharp pixels: alpha strictly 0 or 255, flat palette ---
    for p, im in zip(png_paths, imgs):
        base = os.path.basename(p)
        # getcolors(huge) -> list of (count, (r,g,b,a)); non-deprecated, exact.
        color_counts = im.getcolors(maxcolors=1 << 24)
        alphas = {color[3] for _, color in color_counts}
        soft = alphas - {0, 255}
        r.require(not soft,
                  f"{base}: alpha strictly 0 or 255 — no anti-aliased / soft edges "
                  + ("" if not soft else f"(found partial alpha values {sorted(soft)})"))
        r.require(len(color_counts) <= args.max_colors,
                  f"{base}: <= {args.max_colors} distinct colours "
                  f"(got {len(color_counts)}) — flat pixel art, no gradients/blur")

    # --- background: black or transparent ---
    def classify_bg(im):
        w, h = im.size
        px = im.load()
        corners = [px[0, 0], px[w - 1, 0], px[0, h - 1], px[w - 1, h - 1]]
        # sample the outer border, which for a face canvas is background
        border = []
        for x in range(w):
            border.append(px[x, 0]); border.append(px[x, h - 1])
        for y in range(h):
            border.append(px[0, y]); border.append(px[w - 1, y])
        n_transparent = sum(1 for c in border if c[3] == 0)
        n_black = sum(1 for c in border if c[3] == 255 and c[:3] == (0, 0, 0))
        return corners, n_transparent, n_black, len(border)

    for p, im in zip(png_paths, imgs):
        base = os.path.basename(p)
        corners, n_t, n_b, n = classify_bg(im)
        frac_t, frac_b = n_t / n, n_b / n
        is_transparent = frac_t >= 0.9
        is_black = frac_b >= 0.9
        is_black_or_transparent = (n_t + n_b) / n >= 0.9
        if args.bg == "transparent":
            r.require(is_transparent,
                      f"{base}: background is fully transparent "
                      f"({frac_t:.0%} of border alpha=0)")
        elif args.bg == "black":
            r.require(is_black,
                      f"{base}: background is black (0,0,0) "
                      f"({frac_b:.0%} of border)")
        else:  # any
            kind = "transparent" if is_transparent else ("black" if is_black else "mixed")
            r.require(is_black_or_transparent,
                      f"{base}: background is predominantly black OR transparent "
                      f"(detected: {kind}; {frac_b:.0%} black, {frac_t:.0%} transparent)")

    # --- cyan eye colour presence (soft) ---
    if args.require_eye_color:
        tr, tg, tb = hex_to_rgb(args.require_eye_color)
        tol = args.eye_tolerance
        total_hits = 0
        for im in imgs:
            for count, c in im.getcolors(maxcolors=1 << 24):
                if c[3] == 255 and abs(c[0] - tr) <= tol and abs(c[1] - tg) <= tol and abs(c[2] - tb) <= tol:
                    total_hits += count
        msg = (f"eye colour ~#{args.require_eye_color.upper()} present "
               f"(+/-{tol}/channel): {total_hits} pixel(s) across frames")
        if total_hits > 0:
            r.ok(msg)
        elif args.strict_eye:
            r.fail(msg + " — NONE found (--strict-eye)")
        else:
            r.warn(msg + " — none found (eye identity colour missing?)")

    # --- sprite sheet consistency (if present) ---
    sheet_path = find_sheet(asset_dir, args.id)
    if sheet_path:
        sheet = Image.open(sheet_path).convert("RGBA")
        sw, sh = sheet.size
        base = os.path.basename(sheet_path)
        cols_ok = sw % ew == 0
        rows_ok = sh % eh == 0
        if r.require(cols_ok and rows_ok,
                     f"{base}: {sw}x{sh} is a whole multiple of the {ew}x{eh} frame"):
            cols, rows = sw // ew, sh // eh
            n = len(imgs)
            r.require(cols * rows >= n,
                      f"{base}: grid {cols}x{rows} = {cols * rows} cells holds all {n} frames")
            r.require((cols * rows) - n < cols,
                      f"{base}: no fully-empty trailing row (cells {cols * rows}, frames {n})")
            mismatches = 0
            for i, cell in enumerate(imgs):
                col, row = i % cols, i // cols
                region = sheet.crop((col * ew, row * eh, col * ew + ew, row * eh + eh))
                if region.tobytes() != cell.tobytes():
                    mismatches += 1
            r.require(mismatches == 0,
                      f"{base}: every sheet cell is pixel-identical to its frame PNG "
                      f"(mismatches: {mismatches})")
    else:
        r.warn(f"no <id>_sheet.png found in {asset_dir} — run build_sprite_sheet.py "
               f"(skipping sheet checks)")

    return r


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Validate an eye-only pixel-animation asset directory "
                    "(sharp pixels, black/transparent bg, sprite-sheet integrity).")
    ap.add_argument("asset_dir", help="Animation asset dir (contains frames/).")
    ap.add_argument("--id", help="Animation id (used to find <id>_sheet.png).")
    ap.add_argument("--min-frames", type=int, default=2, help="Minimum frame count (default 2).")
    ap.add_argument("--expect-size", help="Assert frame size WxH, e.g. 64x64 (default: first frame).")
    ap.add_argument("--max-colors", type=int, default=32,
                    help="Max distinct colours per frame (default 32).")
    ap.add_argument("--bg", choices=["any", "black", "transparent"], default="any",
                    help="Required background kind (default any; reports which).")
    ap.add_argument("--require-eye-color", default="00E5FF",
                    help="Cyan eye colour RRGGBB to look for (default 00E5FF; empty to skip).")
    ap.add_argument("--eye-tolerance", type=int, default=40,
                    help="Per-channel tolerance for the eye-colour check (default 40).")
    ap.add_argument("--strict-eye", action="store_true",
                    help="Make a missing eye colour a hard FAIL instead of a WARN.")
    args = ap.parse_args(argv)

    if not os.path.isdir(args.asset_dir):
        sys.stderr.write(f"ERROR: asset dir not found: {args.asset_dir}\n")
        return 2

    r = validate(args)

    print(f"\nEye-only pixel asset validation: {args.asset_dir}")
    print("-" * 66)
    for status, msg in r.checks:
        print(f"  [{status}] {msg}")
    print("-" * 66)
    if r.errors:
        print(f"FAILED — {len(r.errors)} error(s), {len(r.warnings)} warning(s).")
        return 1
    print(f"PASSED — {len(r.checks)} checks, {len(r.warnings)} warning(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

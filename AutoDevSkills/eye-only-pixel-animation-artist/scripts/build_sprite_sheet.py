#!/usr/bin/env python3
"""
build_sprite_sheet.py — compose per-frame PNGs into a single sprite sheet.

Part of the AutoDev global skill `eye-only-pixel-animation-artist`. It takes a
`frames/` directory of `frame_000.png, frame_001.png, ...` (all identical WxH)
and lays them out into one sprite-sheet PNG. Pixels are copied VERBATIM (a plain
blit — no resampling, scaling, blur or anti-aliasing), so the sheet holds the
exact art the renderer will slice back out.

Layout:
  * default is a single horizontal row (columns == frame count);
  * `--columns N` packs a row-major grid: cell i goes to
    (col = i % N, row = i // N).

The source frame PNGs are NEVER modified.

Usage:
  # single-row sheet, explicit output file
  python build_sprite_sheet.py --frames frames/ --out blink_idle_sheet.png

  # grid sheet, output resolved from --out-dir + --id
  python build_sprite_sheet.py --frames frames/ --out-dir . --id blink_idle --columns 4

Requires: Pillow
"""
import argparse
import glob
import os
import sys

try:
    from PIL import Image
except ImportError:  # pragma: no cover - actionable message for unattended runs
    sys.stderr.write(
        "ERROR: missing dependency 'Pillow'. Install it with:\n"
        "  python -m pip install Pillow\n"
    )
    sys.exit(3)


def die(msg):
    sys.stderr.write(f"ERROR: {msg}\n")
    sys.exit(2)


def load_frames(frames_dir):
    if not os.path.isdir(frames_dir):
        die(f"frames directory not found: {frames_dir}")
    paths = sorted(glob.glob(os.path.join(frames_dir, "frame_*.png")))
    if not paths:
        die(f"no frame_###.png files found in {frames_dir}")
    imgs = []
    for p in paths:
        try:
            imgs.append(Image.open(p).convert("RGBA"))
        except Exception as exc:  # noqa: BLE001 - report which file broke
            die(f"could not read {p}: {exc}")
    w, h = imgs[0].size
    for p, im in zip(paths, imgs):
        if im.size != (w, h):
            die(f"frame size mismatch: {os.path.basename(paths[0])} is {w}x{h} "
                f"but {os.path.basename(p)} is {im.size[0]}x{im.size[1]} "
                f"(all frames must be identical size)")
    return paths, imgs, w, h


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Compose frame_###.png images into a single sprite sheet "
                    "(verbatim pixel copy, no resampling).")
    ap.add_argument("--frames", required=True, metavar="DIR",
                    help="Directory containing frame_000.png, frame_001.png, ...")
    ap.add_argument("--out", metavar="FILE",
                    help="Output sprite-sheet PNG path. Overrides --out-dir/--id.")
    ap.add_argument("--out-dir", metavar="DIR",
                    help="Output directory (used with --id to build the filename).")
    ap.add_argument("--id", metavar="ID",
                    help="Animation id; output becomes <out-dir>/<id>_sheet.png.")
    ap.add_argument("--columns", type=int, default=0, metavar="N",
                    help="Columns in the grid. 0 (default) = single horizontal row.")
    args = ap.parse_args(argv)

    if args.columns < 0:
        die("--columns must be >= 0")

    paths, imgs, w, h = load_frames(args.frames)
    n = len(imgs)

    columns = n if args.columns == 0 else min(args.columns, n)
    rows = (n + columns - 1) // columns

    if args.out:
        out_path = args.out
    elif args.out_dir and args.id:
        out_path = os.path.join(args.out_dir, f"{args.id}_sheet.png")
    else:
        die("provide --out FILE, or both --out-dir DIR and --id ID")

    out_dir = os.path.dirname(out_path)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    sheet = Image.new("RGBA", (columns * w, rows * h), (0, 0, 0, 0))
    for i, im in enumerate(imgs):
        col, row = i % columns, i // columns
        sheet.paste(im, (col * w, row * h))  # verbatim blit, no resampling
    sheet.save(out_path)

    print(f"frames:     {n}  ({os.path.basename(paths[0])} .. {os.path.basename(paths[-1])})")
    print(f"frame size: {w}x{h}")
    print(f"layout:     {columns} col x {rows} row (row-major)")
    print(f"sheet size: {sheet.size[0]}x{sheet.size[1]}")
    print(f"wrote:      {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

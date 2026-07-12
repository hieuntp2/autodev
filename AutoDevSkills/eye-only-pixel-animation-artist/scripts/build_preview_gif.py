#!/usr/bin/env python3
"""
build_preview_gif.py — build a nearest-neighbour scaled GIF flip-book preview.

Part of the AutoDev global skill `eye-only-pixel-animation-artist`. It takes a
`frames/` directory of `frame_000.png, frame_001.png, ...` and produces a GIF
scaled up by an integer factor using NEAREST-neighbour resampling only, so the
preview stays crisp pixel art (no smoothing).

Frame order + per-frame duration:
  * with `--manifest <id>.animation.json`, the GIF follows the manifest's
    `frames[]` playlist (each entry references a cell by `index` and carries its
    own `durationMs`), so the preview matches how the runtime will play it;
  * without a manifest, it plays the frame PNGs in filename order using
    `--duration MS` for every frame.

Transparency is preserved: fully-transparent pixels (alpha 0) stay transparent
in the GIF. Source PNGs are NEVER modified.

Usage:
  python build_preview_gif.py --frames frames/ --out blink_idle_preview.gif --scale 6
  python build_preview_gif.py --frames frames/ --out blink_idle_preview.gif \
        --manifest blink_idle.animation.json

Requires: Pillow
"""
import argparse
import glob
import json
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

TRANSPARENT_INDEX = 255


def die(msg):
    sys.stderr.write(f"ERROR: {msg}\n")
    sys.exit(2)


def load_cells(frames_dir):
    if not os.path.isdir(frames_dir):
        die(f"frames directory not found: {frames_dir}")
    paths = sorted(glob.glob(os.path.join(frames_dir, "frame_*.png")))
    if not paths:
        die(f"no frame_###.png files found in {frames_dir}")
    cells = [Image.open(p).convert("RGBA") for p in paths]
    w, h = cells[0].size
    for p, im in zip(paths, cells):
        if im.size != (w, h):
            die(f"frame size mismatch: {os.path.basename(p)} is "
                f"{im.size[0]}x{im.size[1]}, expected {w}x{h}")
    return cells, w, h


def rgba_to_gif_frame(rgba):
    """Convert an RGBA image to a paletted 'P' image with a reserved
    transparent index. Alpha is treated as a hard 0/255 mask (pixel art)."""
    alpha = rgba.getchannel("A")
    # Quantize the visible colours into indices 0..254, keep 255 for transparency.
    rgb = rgba.convert("RGB")
    pal = rgb.convert("P", palette=Image.ADAPTIVE, colors=255)
    # Any pixel that is not fully opaque becomes transparent.
    mask = alpha.point(lambda a: 255 if a < 128 else 0)
    pal.paste(TRANSPARENT_INDEX, mask)
    return pal


def build_sequence(cells, manifest_path, fallback_ms):
    """Return (list_of_cell_images_in_play_order, list_of_durations_ms)."""
    if manifest_path:
        if not os.path.isfile(manifest_path):
            die(f"manifest not found: {manifest_path}")
        with open(manifest_path, "r", encoding="utf-8") as f:
            m = json.load(f)
        playlist = m.get("frames")
        if not isinstance(playlist, list) or not playlist:
            die(f"manifest {manifest_path} has no non-empty 'frames' array")
        seq, durs = [], []
        for i, fr in enumerate(playlist):
            idx = int(fr.get("index", -1))
            if not (0 <= idx < len(cells)):
                die(f"manifest frames[{i}].index={idx} is out of range "
                    f"(have {len(cells)} cell PNGs)")
            seq.append(cells[idx])
            durs.append(max(1, int(fr.get("durationMs", fallback_ms))))
        return seq, durs
    return list(cells), [max(1, fallback_ms)] * len(cells)


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Build a nearest-neighbour scaled GIF preview of pixel frames.")
    ap.add_argument("--frames", required=True, metavar="DIR",
                    help="Directory containing frame_000.png, frame_001.png, ...")
    ap.add_argument("--out", required=True, metavar="FILE",
                    help="Output GIF path, e.g. <id>_preview.gif.")
    ap.add_argument("--scale", type=int, default=6, metavar="N",
                    help="Integer nearest-neighbour upscale factor (default 6).")
    ap.add_argument("--manifest", metavar="FILE",
                    help="Optional animation.json to read frame order + durations from.")
    ap.add_argument("--duration", type=int, default=100, metavar="MS",
                    help="Fallback per-frame duration in ms when no manifest (default 100).")
    args = ap.parse_args(argv)

    if args.scale < 1:
        die("--scale must be >= 1")
    if args.duration < 1:
        die("--duration must be >= 1")

    cells, w, h = load_cells(args.frames)
    seq, durs = build_sequence(cells, args.manifest, args.duration)

    sw, sh = w * args.scale, h * args.scale
    gif_frames = []
    for im in seq:
        scaled = im.resize((sw, sh), Image.NEAREST)  # nearest-neighbour ONLY
        gif_frames.append(rgba_to_gif_frame(scaled))

    out_dir = os.path.dirname(args.out)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    gif_frames[0].save(
        args.out,
        save_all=True,
        append_images=gif_frames[1:],
        duration=durs,
        loop=0,
        disposal=2,               # restore to background -> clean transparency
        transparency=TRANSPARENT_INDEX,
    )

    print(f"cells:      {len(cells)}  frame size {w}x{h}")
    print(f"gif frames: {len(seq)}  (from {'manifest' if args.manifest else 'frame order'})")
    print(f"scale:      x{args.scale} (nearest)  ->  {sw}x{sh}")
    print(f"durations:  {durs} ms")
    print(f"wrote:      {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

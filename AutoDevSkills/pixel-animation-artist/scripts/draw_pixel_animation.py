#!/usr/bin/env python3
"""
draw_pixel_animation.py — deterministic pixel-art animation generator.

Part of the AutoDev global skill `pixel-animation-artist`.

Given a simple JSON *config* (or a built-in preset), it renders:
  - one PNG per unique frame image ("cell")      -> <out>/frames/frame_000.png ...
  - a sprite sheet holding every cell            -> <out>/<id>_sheet.png
  - a nearest-neighbour GIF preview (flip-book)  -> <out>/<id>_preview.gif
  - a JSON animation manifest                    -> <out>/<id>.animation.json

Design rules (hard requirements):
  * Fully deterministic — no randomness, no timestamps. Same input -> same bytes.
  * No anti-aliasing, no blur, no gradients. Every pixel is a solid palette
    colour; alpha is only 0 or 255. Drawing REPLACES pixels (never blends).
  * The sprite sheet contains ONLY frame images. Play order, per-frame duration,
    whole-sprite motion/tween, timeline events, loop, priority and
    interruptibility all live in the JSON manifest — never hard-coded here.
  * Preview scaling uses nearest-neighbour only.

Usage:
  python draw_pixel_animation.py --preset curious_magnifier --out assets/pet/animations/curious_magnifier
  python draw_pixel_animation.py --config my_anim.json --out out/my_anim [--scale 8] [--columns 8]

Config format: see SKILL.md and schema/animation.schema.json.
Requires: Pillow  (pip install -r requirements.txt)
"""
import argparse
import json
import os
import sys

try:
    from PIL import Image
except ImportError:  # pragma: no cover - actionable message for unattended runs
    sys.stderr.write(
        "ERROR: Pillow is required. Install it with:\n"
        "  python -m pip install Pillow\n"
        "(or: python -m pip install -r requirements.txt)\n"
    )
    sys.exit(3)


# --------------------------------------------------------------------------- #
# Colour handling — resolves names / hex / [r,g,b(,a)] to an (r,g,b,a) tuple.  #
# --------------------------------------------------------------------------- #
def resolve_color(value, palette):
    if isinstance(value, str):
        if value in palette:
            return resolve_color(palette[value], palette)
        if value.startswith("#"):
            h = value[1:]
            if len(h) == 6:
                return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)
            if len(h) == 8:
                return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), int(h[6:8], 16))
        raise ValueError(f"Unknown colour: {value!r}")
    if isinstance(value, (list, tuple)):
        if len(value) == 3:
            return (int(value[0]), int(value[1]), int(value[2]), 255)
        if len(value) == 4:
            return (int(value[0]), int(value[1]), int(value[2]), int(value[3]))
    raise ValueError(f"Bad colour value: {value!r}")


# --------------------------------------------------------------------------- #
# Tiny deterministic pixel canvas — integer coordinates, no anti-aliasing.    #
# --------------------------------------------------------------------------- #
class Canvas:
    def __init__(self, w, h, background=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w, h), tuple(background))
        self.px = self.img.load()

    def set(self, x, y, c):
        x, y = int(x), int(y)
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[x, y] = c  # REPLACE — never blend, keeps alpha strictly 0/255

    def fill(self, c):
        for y in range(self.h):
            for x in range(self.w):
                self.px[x, y] = c

    def rect(self, x, y, w, h, c):
        for yy in range(int(y), int(y) + int(h)):
            for xx in range(int(x), int(x) + int(w)):
                self.set(xx, yy, c)

    def frame(self, x, y, w, h, c):
        x, y, w, h = int(x), int(y), int(w), int(h)
        self.hline(x, y, w, c)
        self.hline(x, y + h - 1, w, c)
        self.vline(x, y, h, c)
        self.vline(x + w - 1, y, h, c)

    def hline(self, x, y, w, c):
        for xx in range(int(x), int(x) + int(w)):
            self.set(xx, y, c)

    def vline(self, x, y, h, c):
        for yy in range(int(y), int(y) + int(h)):
            self.set(x, yy, c)

    def line(self, x1, y1, x2, y2, c):
        # Integer Bresenham — crisp 1px line, no anti-aliasing.
        x1, y1, x2, y2 = int(x1), int(y1), int(x2), int(y2)
        dx, dy = abs(x2 - x1), -abs(y2 - y1)
        sx = 1 if x1 < x2 else -1
        sy = 1 if y1 < y2 else -1
        err = dx + dy
        while True:
            self.set(x1, y1, c)
            if x1 == x2 and y1 == y2:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x1 += sx
            if e2 <= dx:
                err += dx
                y1 += sy

    def circle(self, cx, cy, r, c, fill=False):
        # Midpoint circle — outline; optional solid fill via horizontal spans.
        cx, cy, r = int(cx), int(cy), int(r)
        if fill:
            for yy in range(-r, r + 1):
                span = int((r * r - yy * yy) ** 0.5)
                self.hline(cx - span, cy + yy, 2 * span + 1, c)
            return
        x, y, d = 0, r, 1 - r
        while x <= y:
            for (px, py) in ((x, y), (y, x), (-x, y), (-y, x),
                             (x, -y), (y, -x), (-x, -y), (-y, -x)):
                self.set(cx + px, cy + py, c)
            x += 1
            if d < 0:
                d += 2 * x + 1
            else:
                y -= 1
                d += 2 * (x - y) + 1

    def ellipse(self, cx, cy, rx, ry, c, fill=False):
        cx, cy, rx, ry = int(cx), int(cy), int(rx), int(ry)
        if rx <= 0 or ry <= 0:
            return
        for yy in range(-ry, ry + 1):
            span = int(rx * (1 - (yy * yy) / (ry * ry)) ** 0.5)
            if fill:
                self.hline(cx - span, cy + yy, 2 * span + 1, c)
            else:
                self.set(cx - span, cy + yy, c)
                self.set(cx + span, cy + yy, c)


OPS = {
    "fill": lambda cv, o, p: cv.fill(resolve_color(o["color"], p)),
    "rect": lambda cv, o, p: cv.rect(o["x"], o["y"], o["w"], o["h"], resolve_color(o["color"], p)),
    "frame": lambda cv, o, p: cv.frame(o["x"], o["y"], o["w"], o["h"], resolve_color(o["color"], p)),
    "pixel": lambda cv, o, p: cv.set(o["x"], o["y"], resolve_color(o["color"], p)),
    "hline": lambda cv, o, p: cv.hline(o["x"], o["y"], o["w"], resolve_color(o["color"], p)),
    "vline": lambda cv, o, p: cv.vline(o["x"], o["y"], o["h"], resolve_color(o["color"], p)),
    "line": lambda cv, o, p: cv.line(o["x1"], o["y1"], o["x2"], o["y2"], resolve_color(o["color"], p)),
    "circle": lambda cv, o, p: cv.circle(o["cx"], o["cy"], o["r"], resolve_color(o["color"], p), o.get("fill", False)),
    "ellipse": lambda cv, o, p: cv.ellipse(o["cx"], o["cy"], o["rx"], o["ry"], resolve_color(o["color"], p), o.get("fill", False)),
}


def render_cell(ops, w, h, palette, background):
    cv = Canvas(w, h, background)
    for op in ops:
        name = op.get("op")
        if name not in OPS:
            raise ValueError(f"Unknown draw op: {name!r}")
        OPS[name](cv, op, palette)
    return cv.img


# --------------------------------------------------------------------------- #
# Preview GIF — nearest-neighbour scaled flip-book of the play order.          #
# Motion/events are NOT baked in; they are runtime concerns driven by the JSON.#
# --------------------------------------------------------------------------- #
def scale_nearest(img, factor):
    return img.resize((img.width * factor, img.height * factor), Image.NEAREST)


def to_gif_frame(img_rgba):
    """RGBA -> paletted 'P' frame with index 255 reserved for transparency."""
    alpha = img_rgba.getchannel("A")
    pal = img_rgba.convert("RGB").quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    transparent_mask = alpha.point(lambda a: 255 if a < 128 else 0)
    pal.paste(255, transparent_mask)  # 255 = the reserved transparent index
    return pal


def write_gif(path, cells, playlist, scale, loop):
    frames = [to_gif_frame(scale_nearest(cells[step["index"]], scale)) for step in playlist]
    durations = [step["durationMs"] for step in playlist]
    frames[0].save(
        path,
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        loop=0 if loop else 1,
        transparency=255,
        disposal=2,
        optimize=False,
    )


def write_sprite_sheet(path, cells, fw, fh, columns):
    rows = (len(cells) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * fw, rows * fh), (0, 0, 0, 0))
    for i, cell in enumerate(cells):
        col, row = i % columns, i // columns
        sheet.paste(cell, (col * fw, row * fh))
    sheet.save(path)
    return columns, rows


# --------------------------------------------------------------------------- #
# Built-in presets — a "simple prompt" the agent can start from.              #
# Each returns a full config dict built from draw ops (single render path).    #
# --------------------------------------------------------------------------- #
def preset_curious_magnifier():
    """A little magnifying glass that looks around curiously (8 frames).

    Frame-to-frame art only changes the travelling lens glint and a question
    mark that pops in mid-loop. The overall bob + tilt is expressed in the
    `motion` track, NOT baked into the frames — proving the art/animation split.
    """
    palette = {
        "clear": [0, 0, 0, 0],
        "rim": [40, 44, 66],
        "rim_hi": [96, 104, 148],
        "glass": [120, 196, 236],
        "glass_dk": [78, 150, 196],
        "glint": [236, 250, 255],
        "handle": [150, 96, 52],
        "handle_hi": [196, 140, 84],
        "mark": [255, 214, 92],
    }
    cx, cy, r = 24, 15, 9  # lens centre / radius on the 64x32 canvas

    def base_lens():
        ops = [
            {"op": "circle", "cx": cx, "cy": cy, "r": r, "color": "glass_dk", "fill": True},
            {"op": "circle", "cx": cx, "cy": cy, "r": r - 1, "color": "glass", "fill": True},
            {"op": "circle", "cx": cx, "cy": cy, "r": r, "color": "rim"},
            {"op": "circle", "cx": cx, "cy": cy, "r": r + 1, "color": "rim"},
        ]
        # Wooden handle running to the lower-right.
        ops += [
            {"op": "line", "x1": cx + 6, "y1": cy + 6, "x2": 40, "y2": 27, "color": "handle"},
            {"op": "line", "x1": cx + 7, "y1": cy + 6, "x2": 41, "y2": 27, "color": "handle"},
            {"op": "line", "x1": cx + 7, "y1": cy + 5, "x2": 41, "y2": 26, "color": "handle_hi"},
        ]
        return ops

    # The glint walks a small circular path inside the lens across the 8 frames.
    glint_path = [(-3, -3), (-1, -4), (2, -3), (3, -1), (2, 2), (0, 3), (-3, 2), (-4, 0)]
    cells = []
    for i, (gx, gy) in enumerate(glint_path):
        ops = base_lens()
        # rim highlight (fixed) + travelling glint
        ops.append({"op": "pixel", "x": cx - 4, "y": cy - 4, "color": "rim_hi"})
        ops.append({"op": "rect", "x": cx + gx, "y": cy + gy, "w": 2, "h": 2, "color": "glint"})
        # A curious "?" pops in over the middle of the loop (frames 3..5).
        if 3 <= i <= 5:
            ops += [
                {"op": "hline", "x": 47, "y": 6, "w": 3, "color": "mark"},
                {"op": "pixel", "x": 50, "y": 7, "color": "mark"},
                {"op": "pixel", "x": 49, "y": 8, "color": "mark"},
                {"op": "pixel", "x": 48, "y": 9, "color": "mark"},
                {"op": "pixel", "x": 48, "y": 11, "color": "mark"},
            ]
        cells.append({"draw": ops})

    return {
        "id": "curious_magnifier",
        "frameWidth": 64,
        "frameHeight": 32,
        "scale": 8,
        "columns": 8,
        "loop": True,
        "priority": 20,
        "interruptible": True,
        "background": [0, 0, 0, 0],
        "palette": palette,
        "cells": cells,
        # Playlist: hold the "curious peek" frames a touch longer.
        "frames": [
            {"index": 0, "durationMs": 110},
            {"index": 1, "durationMs": 110},
            {"index": 2, "durationMs": 110},
            {"index": 3, "durationMs": 140},
            {"index": 4, "durationMs": 160},
            {"index": 5, "durationMs": 140},
            {"index": 6, "durationMs": 110},
            {"index": 7, "durationMs": 110},
        ],
        # Whole-sprite bob + curious tilt (interpolated by the runtime).
        "motion": [
            {"timeMs": 0, "x": 0, "y": 0, "scale": 1.0, "rotation": 0, "alpha": 1.0, "easing": "easeInOut"},
            {"timeMs": 300, "x": 0, "y": -2, "scale": 1.0, "rotation": -4, "alpha": 1.0, "easing": "easeOut"},
            {"timeMs": 600, "x": 1, "y": 0, "scale": 1.05, "rotation": 3, "alpha": 1.0, "easing": "easeInOut"},
            {"timeMs": 990, "x": 0, "y": 0, "scale": 1.0, "rotation": 0, "alpha": 1.0, "easing": "easeInOut"},
        ],
        "events": [
            {"timeMs": 330, "type": "sound", "value": "curious_pop.wav"},
            {"timeMs": 330, "type": "effect", "value": "question_mark"},
            {"timeMs": 950, "type": "callback", "value": "curious_loop_end"},
        ],
    }


PRESETS = {"curious_magnifier": preset_curious_magnifier}


# --------------------------------------------------------------------------- #
# Config -> cells + playlist + manifest.                                      #
# --------------------------------------------------------------------------- #
def load_config(args):
    if args.preset:
        if args.preset not in PRESETS:
            raise SystemExit(f"Unknown preset {args.preset!r}. Available: {', '.join(PRESETS)}")
        cfg = PRESETS[args.preset]()
    elif args.config:
        with open(args.config, "r", encoding="utf-8") as f:
            cfg = json.load(f)
    else:
        raise SystemExit("Provide either --preset NAME or --config PATH.")

    # CLI overrides.
    if args.id:
        cfg["id"] = args.id
    if args.scale:
        cfg["scale"] = args.scale
    if args.columns:
        cfg["columns"] = args.columns
    return cfg


def build_cells_and_playlist(cfg):
    fw = int(cfg.get("frameWidth", 64))
    fh = int(cfg.get("frameHeight", 32))
    palette = cfg.get("palette", {})
    background = resolve_color(cfg.get("background", [0, 0, 0, 0]), palette)

    # Two authoring styles:
    #  A) explicit `cells` (unique images) + `frames` playlist referencing indices
    #  B) `frames` where each entry carries its own `draw` (cell == playlist entry)
    if "cells" in cfg:
        cells = [render_cell(c["draw"], fw, fh, palette, background) for c in cfg["cells"]]
        playlist = cfg.get("frames")
        if not playlist:
            playlist = [{"index": i, "durationMs": 100} for i in range(len(cells))]
    else:
        frames = cfg.get("frames", [])
        cells, playlist = [], []
        for i, fr in enumerate(frames):
            if "draw" in fr:
                cells.append(render_cell(fr["draw"], fw, fh, palette, background))
                playlist.append({"index": i, "durationMs": int(fr.get("durationMs", 100))})
            else:
                playlist.append({"index": int(fr["index"]), "durationMs": int(fr.get("durationMs", 100))})
        if not cells:
            raise SystemExit("Config has no drawable cells (need `cells` or `frames[].draw`).")

    return fw, fh, cells, playlist


def main():
    ap = argparse.ArgumentParser(description="Deterministic pixel-art animation generator.")
    ap.add_argument("--preset", help="Built-in preset name (e.g. curious_magnifier).")
    ap.add_argument("--config", help="Path to an animation config JSON.")
    ap.add_argument("--out", required=True, help="Output directory for the generated asset.")
    ap.add_argument("--id", help="Override the animation id.")
    ap.add_argument("--scale", type=int, help="Nearest-neighbour scale factor for the GIF preview.")
    ap.add_argument("--columns", type=int, help="Sprite-sheet columns (default: single row).")
    args = ap.parse_args()

    cfg = load_config(args)
    anim_id = cfg["id"]
    scale = int(cfg.get("scale", 8))

    fw, fh, cells, playlist = build_cells_and_playlist(cfg)
    columns = int(cfg.get("columns", len(cells)))

    out_dir = args.out
    frames_dir = os.path.join(out_dir, "frames")
    os.makedirs(frames_dir, exist_ok=True)

    # 1. Per-cell PNGs.
    for i, cell in enumerate(cells):
        cell.save(os.path.join(frames_dir, f"frame_{i:03d}.png"))

    # 2. Sprite sheet.
    sheet_name = f"{anim_id}_sheet.png"
    columns, rows = write_sprite_sheet(os.path.join(out_dir, sheet_name), cells, fw, fh, columns)

    # 3. GIF preview.
    gif_name = f"{anim_id}_preview.gif"
    write_gif(os.path.join(out_dir, gif_name), cells, playlist, scale, cfg.get("loop", True))

    # 4. Manifest — the single source of truth for how the animation plays.
    manifest = {
        "id": anim_id,
        "spriteSheet": sheet_name,
        "frameWidth": fw,
        "frameHeight": fh,
        "columns": columns,
        "rows": rows,
        "loop": bool(cfg.get("loop", True)),
        "priority": int(cfg.get("priority", 0)),
        "interruptible": bool(cfg.get("interruptible", True)),
        "frames": playlist,
        "motion": cfg.get("motion", []),
        "events": cfg.get("events", []),
    }
    manifest_name = f"{anim_id}.animation.json"
    with open(os.path.join(out_dir, manifest_name), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")

    total_ms = sum(s["durationMs"] for s in playlist)
    print(f"[pixel-animation-artist] Generated '{anim_id}' -> {out_dir}")
    print(f"  cells        : {len(cells)}  ({fw}x{fh}, {columns}x{rows} sheet)")
    print(f"  playlist     : {len(playlist)} steps, {total_ms} ms/loop, loop={manifest['loop']}")
    print(f"  frames dir   : frames/frame_000.png .. frame_{len(cells) - 1:03d}.png")
    print(f"  sprite sheet : {sheet_name}")
    print(f"  gif preview  : {gif_name}  (nearest-neighbour x{scale})")
    print(f"  manifest     : {manifest_name}")


if __name__ == "__main__":
    main()

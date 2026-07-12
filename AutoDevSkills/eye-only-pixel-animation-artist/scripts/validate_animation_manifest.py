#!/usr/bin/env python3
"""
validate_animation_manifest.py — validate an <id>.animation.json manifest.

Part of the AutoDev global skill `eye-only-pixel-animation-artist`. Pure Python
standard library only (no Pillow) — it reads the manifest JSON and, if the sprite
sheet is present, reads the PNG's dimensions straight from its header to bound the
frame indices. It NEVER modifies any file.

The manifest conforms to the shared schema
`AutoDevSkills/pixel-animation-artist/schema/animation.schema.json`.

Checks:
  * required fields: id, spriteSheet, frameWidth, frameHeight, frames[]
  * each frames[] entry: index >= 0, durationMs >= 1
  * spriteSheet path resolves to a real file (relative to the manifest)
  * if the sheet is present: every frames[].index < total grid cells implied
    by (sheetW // frameWidth) * (sheetH // frameHeight)
  * loop / interruptible are booleans if present; priority is an int if present
  * every events[] entry with type "sound" has a `value` matching the
    sound_cue_id pattern ^[a-z][a-z0-9_]*$ — each sound hook is reported, since
    these ids are handed to the sound skill (retro-bit-sound-designer)
  * optional --expect-id asserts the manifest id

Exit code: 0 = PASS, 1 = at least one hard FAIL, 2 = usage / parse error.

Usage:
  python validate_animation_manifest.py blink_idle.animation.json
  python validate_animation_manifest.py path/to/happy_reward_sparkle.animation.json \
        --expect-id happy_reward_sparkle
"""
import argparse
import json
import os
import re
import struct
import sys

SOUND_CUE_RE = re.compile(r"^[a-z][a-z0-9_]*$")
PNG_SIG = b"\x89PNG\r\n\x1a\n"


class Report:
    def __init__(self):
        self.checks = []
        self.errors = []

    def ok(self, msg):
        self.checks.append(("PASS", msg))

    def info(self, msg):
        self.checks.append(("INFO", msg))

    def fail(self, msg):
        self.errors.append(msg)
        self.checks.append(("FAIL", msg))

    def require(self, cond, msg):
        (self.ok if cond else self.fail)(msg)
        return cond


def read_png_size(path):
    """Return (width, height) from a PNG header, or None if not a valid PNG."""
    try:
        with open(path, "rb") as f:
            header = f.read(24)
    except OSError:
        return None
    if len(header) < 24 or header[:8] != PNG_SIG or header[12:16] != b"IHDR":
        return None
    width, height = struct.unpack(">II", header[16:24])
    return width, height


def validate(manifest_path, expect_id):
    r = Report()
    if not os.path.isfile(manifest_path):
        r.fail(f"manifest file not found: {manifest_path}")
        return r
    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            m = json.load(f)
    except json.JSONDecodeError as exc:
        r.fail(f"manifest is not valid JSON: {exc}")
        return r

    manifest_dir = os.path.dirname(os.path.abspath(manifest_path))

    for key in ("id", "spriteSheet", "frameWidth", "frameHeight", "frames"):
        r.require(key in m, f"has required field '{key}'")
    if r.errors:
        return r

    r.require(isinstance(m["id"], str) and m["id"].strip(), "id is a non-empty string")
    if expect_id:
        r.require(m["id"] == expect_id, f"id == expected '{expect_id}' (got '{m['id']}')")

    fw_ok = isinstance(m["frameWidth"], int) and m["frameWidth"] >= 1
    fh_ok = isinstance(m["frameHeight"], int) and m["frameHeight"] >= 1
    r.require(fw_ok, f"frameWidth is a positive integer (got {m['frameWidth']!r})")
    r.require(fh_ok, f"frameHeight is a positive integer (got {m['frameHeight']!r})")

    # optional typed fields
    if "loop" in m:
        r.require(isinstance(m["loop"], bool), f"loop is a boolean (got {m['loop']!r})")
    if "interruptible" in m:
        r.require(isinstance(m["interruptible"], bool),
                  f"interruptible is a boolean (got {m['interruptible']!r})")
    if "priority" in m:
        r.require(isinstance(m["priority"], int) and not isinstance(m["priority"], bool),
                  f"priority is an integer (got {m['priority']!r})")

    # frames[]
    frames = m["frames"]
    r.require(isinstance(frames, list) and len(frames) >= 1,
              f"frames[] is a non-empty array (got {len(frames) if isinstance(frames, list) else type(frames).__name__})")
    max_index = -1
    if isinstance(frames, list):
        for i, fr in enumerate(frames):
            if not isinstance(fr, dict):
                r.fail(f"frames[{i}] is not an object")
                continue
            idx = fr.get("index")
            dur = fr.get("durationMs")
            r.require(isinstance(idx, int) and not isinstance(idx, bool) and idx >= 0,
                      f"frames[{i}].index is an integer >= 0 (got {idx!r})")
            r.require(isinstance(dur, int) and not isinstance(dur, bool) and dur >= 1,
                      f"frames[{i}].durationMs is an integer >= 1 (got {dur!r})")
            if isinstance(idx, int) and not isinstance(idx, bool):
                max_index = max(max_index, idx)

    # spriteSheet existence + index bounds
    sheet_path = os.path.join(manifest_dir, m["spriteSheet"])
    if r.require(os.path.isfile(sheet_path),
                 f"spriteSheet '{m['spriteSheet']}' exists next to the manifest"):
        size = read_png_size(sheet_path)
        if size is None:
            r.fail(f"spriteSheet '{m['spriteSheet']}' is not a readable PNG")
        elif fw_ok and fh_ok:
            sw, sh = size
            cols, rows = sw // m["frameWidth"], sh // m["frameHeight"]
            cells = cols * rows
            r.info(f"sheet {sw}x{sh} -> grid {cols}x{rows} = {cells} cells")
            r.require(cells >= 1, "sheet holds at least one whole cell")
            r.require(max_index < cells,
                      f"every frames[].index < {cells} cells (max index used: {max_index})")

    # events[] — report + validate sound hooks
    events = m.get("events", [])
    if not isinstance(events, list):
        r.fail("events must be an array if present")
        events = []
    sound_hooks = []
    for i, e in enumerate(events):
        if not isinstance(e, dict):
            r.fail(f"events[{i}] is not an object")
            continue
        if e.get("type") == "sound":
            val = e.get("value")
            ok = isinstance(val, str) and bool(SOUND_CUE_RE.match(val))
            r.require(ok,
                      f"events[{i}] sound value '{val}' matches sound_cue_id pattern "
                      f"^[a-z][a-z0-9_]*$")
            if ok:
                sound_hooks.append(val)
    if sound_hooks:
        r.info(f"sound hooks handed to the sound skill: {sound_hooks}")
    else:
        r.info("no type:'sound' events (animation plays silently)")

    return r


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Validate an <id>.animation.json manifest (stdlib only).")
    ap.add_argument("manifest", help="Path to the <id>.animation.json file.")
    ap.add_argument("--expect-id", help="Assert the manifest's id equals this value.")
    args = ap.parse_args(argv)

    r = validate(args.manifest, args.expect_id)

    print(f"\nManifest validation: {args.manifest}")
    print("-" * 66)
    for status, msg in r.checks:
        print(f"  [{status}] {msg}")
    print("-" * 66)
    if r.errors:
        print(f"FAILED — {len(r.errors)} error(s).")
        return 1
    print(f"PASSED — {len(r.checks)} checks.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

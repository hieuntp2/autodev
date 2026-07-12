#!/usr/bin/env python3
"""
validate_sound_links.py — verify animation->sound cue linkage.

Part of the AutoDev global skill `retro-bit-sound-designer`. It checks that every
`events[]` entry of `type:"sound"` in an animation manifest resolves to a real,
committed sound asset, and that sound cue ids are unique across the sounds store.
Read-only; exits 0 on success, non-zero on any hard failure.

The link (see the cross-skill contract): an animation manifest binds audio via

  "events": [ { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" } ]

where `value` is a `sound_cue_id`. A resolved cue must have a real asset at
  <sounds-dir>/<value>/<value>.sound.json

Rules:
  * a `type:"sound"` event whose cue is MISSING is a FAILURE ...
  * ... UNLESS the event carries `"optional": true`, in which case it is a WARN
    (graceful-degradation: the runtime plays the animation silently).
  * `sound_cue_id` must be UNIQUE across every *.sound.json under --sounds-dir;
    a duplicate id is a FAILURE.

Usage:
  python validate_sound_links.py --manifest happy_reward_sparkle.animation.json --sounds-dir out/sounds
  python validate_sound_links.py --animations-dir assets/pet/animations --sounds-dir assets/pet/sounds
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys

ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")


class Report:
    def __init__(self):
        self.errors: list[str] = []
        self.warnings: list[str] = []
        self.checks: list[tuple[str, str]] = []

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


def resolve_cue(sounds_dir: str, cue_id: str) -> bool:
    """A cue resolves when <sounds-dir>/<cue>/<cue>.sound.json exists."""
    meta = os.path.join(sounds_dir, cue_id, f"{cue_id}.sound.json")
    return os.path.isfile(meta)


def check_uniqueness(r: Report, sounds_dir: str) -> None:
    if not os.path.isdir(sounds_dir):
        r.fail(f"--sounds-dir not found: {sounds_dir}")
        return
    seen: dict[str, list[str]] = {}
    for meta_path in sorted(glob.glob(os.path.join(sounds_dir, "**", "*.sound.json"),
                                      recursive=True)):
        try:
            with open(meta_path, "r", encoding="utf-8") as f:
                sid = json.load(f).get("id")
        except (OSError, json.JSONDecodeError) as exc:
            r.fail(f"could not read sound metadata {meta_path}: {exc}")
            continue
        if not sid:
            r.fail(f"{meta_path} has no 'id' field")
            continue
        seen.setdefault(sid, []).append(meta_path)

    dupes = {sid: paths for sid, paths in seen.items() if len(paths) > 1}
    if dupes:
        for sid, paths in sorted(dupes.items()):
            r.fail(f"duplicate sound_cue_id {sid!r} in {len(paths)} files: {paths}")
    else:
        r.ok(f"sound_cue_id uniqueness OK ({len(seen)} distinct ids under {sounds_dir})")


def check_manifest(r: Report, manifest_path: str, sounds_dir: str) -> None:
    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            manifest = json.load(f)
    except (OSError, json.JSONDecodeError) as exc:
        r.fail(f"could not read manifest {manifest_path}: {exc}")
        return
    name = os.path.basename(manifest_path)
    events = manifest.get("events", [])
    sound_events = [e for e in events if e.get("type") == "sound"]
    if not sound_events:
        r.ok(f"{name}: no type:'sound' events (nothing to link)")
        return
    for e in sound_events:
        cue = e.get("value")
        optional = bool(e.get("optional", False))
        if not cue:
            r.fail(f"{name}: a type:'sound' event has no 'value' (sound_cue_id)")
            continue
        if not ID_RE.match(str(cue)):
            r.fail(f"{name}: sound cue {cue!r} does not match ^[a-z][a-z0-9_]*$")
            continue
        if resolve_cue(sounds_dir, cue):
            r.ok(f"{name}: cue {cue!r} -> OK (asset present)")
        elif optional:
            r.warn(f"{name}: cue {cue!r} -> MISSING but optional (runtime plays silently)")
        else:
            r.fail(f"{name}: cue {cue!r} -> MISSING required asset "
                   f"(expected {os.path.join(sounds_dir, cue, cue + '.sound.json')})")


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Validate that animation 'sound' events resolve to real sound assets "
                    "and that sound cue ids are unique. Read-only.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    grp = ap.add_mutually_exclusive_group(required=True)
    grp.add_argument("--manifest", help="A single animation.json manifest to check.")
    grp.add_argument("--animations-dir", help="Directory of *.animation.json manifests to check.")
    ap.add_argument("--sounds-dir", required=True, help="Root of the sounds store (<cue>/<cue>.sound.json).")
    args = ap.parse_args()

    r = Report()

    if args.manifest:
        if not os.path.isfile(args.manifest):
            r.fail(f"--manifest not found: {args.manifest}")
        else:
            check_manifest(r, args.manifest, args.sounds_dir)
    else:
        manifests = sorted(glob.glob(os.path.join(args.animations_dir, "**", "*.animation.json"),
                                     recursive=True))
        if not manifests:
            r.fail(f"no *.animation.json found under {args.animations_dir}")
        for m in manifests:
            check_manifest(r, m, args.sounds_dir)

    check_uniqueness(r, args.sounds_dir)

    print("\nSound-link validation")
    print("-" * 62)
    for status, msg in r.checks:
        print(f"  [{status}] {msg}")
    print("-" * 62)
    if r.errors:
        print(f"FAILED — {len(r.errors)} error(s), {len(r.warnings)} warning(s).")
        return 1
    print(f"PASSED — {len(r.checks)} checks, {len(r.warnings)} warning(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

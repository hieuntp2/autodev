#!/usr/bin/env python3
"""
validate_audio_asset.py — verify a generated retro bit SFX asset.

Part of the AutoDev global skill `retro-bit-sound-designer`. Point it at a sound
asset DIRECTORY (e.g. app/src/main/assets/pet/sounds/happy_reward_chirp/) or at a
single .wav file. It NEVER modifies the asset; it only reads and reports. Exits 0
on success, non-zero on the first hard failure, printing a PASS/FAIL report.

Checks (WAV, via the stdlib `wave` module):
  * the WAV file exists and is readable as PCM
  * duration within [--min-ms, --max-ms]  (defaults 30 .. 1500 ms)
  * sample rate is one of {8000,11025,16000,22050,44100,48000}
  * channels in {1, 2}
  * clipping: FAIL if any 16-bit sample hits full scale (|s| >= 32767)
  * silence: FAIL if the file is entirely silent; WARN if leading OR trailing
    silence exceeds --max-edge-silence-ms
  * if <id>.sound.json is present: its durationMs / sampleRate / channels must
    match the actual WAV, and its id must match ^[a-z][a-z0-9_]*$

Checks (OGG, if present):
  * if `ffprobe` is on PATH, its duration must be within [--min-ms, --max-ms]
  * else the deep OGG check is skipped with a printed note

Usage:
  python validate_audio_asset.py app/src/main/assets/pet/sounds/happy_reward_chirp
  python validate_audio_asset.py some_sound.wav --min-ms 30 --max-ms 1500
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import wave

ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")
ALLOWED_SAMPLE_RATES = {8000, 11025, 16000, 22050, 44100, 48000}
FULL_SCALE = 32767
SILENCE_THRESHOLD = 8  # |sample| <= this counts as silence (~ -72 dBFS at 16-bit)


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


def read_wav_samples(path: str):
    """Return (samples[list[int]], sample_rate, channels). Interleaved -> flat.

    We read raw 16-bit PCM; for clipping/silence we care about per-sample peaks
    regardless of channel interleave, so a flat list is sufficient.
    """
    with wave.open(path, "rb") as w:
        sampwidth = w.getsampwidth()
        if sampwidth != 2:
            raise ValueError(f"expected 16-bit PCM (sampwidth 2), got {sampwidth * 8}-bit")
        sr = w.getframerate()
        channels = w.getnchannels()
        nframes = w.getnframes()
        raw = w.readframes(nframes)
    count = len(raw) // 2
    samples = list(struct.unpack("<%dh" % count, raw)) if count else []
    return samples, sr, channels, nframes


def leading_silent(samples: list[int]) -> int:
    n = 0
    for s in samples:
        if abs(s) <= SILENCE_THRESHOLD:
            n += 1
        else:
            break
    return n


def trailing_silent(samples: list[int]) -> int:
    n = 0
    for s in reversed(samples):
        if abs(s) <= SILENCE_THRESHOLD:
            n += 1
        else:
            break
    return n


def find_asset(target: str):
    """Resolve (wav_path, meta_path|None, ogg_path|None) from a dir or a .wav."""
    if os.path.isdir(target):
        wavs = sorted(glob.glob(os.path.join(target, "*.wav")))
        if not wavs:
            return None, None, None
        wav = wavs[0]
        stem = os.path.splitext(os.path.basename(wav))[0]
        meta = os.path.join(target, f"{stem}.sound.json")
        ogg = os.path.join(target, f"{stem}.ogg")
        return wav, (meta if os.path.isfile(meta) else None), (ogg if os.path.isfile(ogg) else None)
    # single file
    if target.lower().endswith(".wav"):
        d = os.path.dirname(target) or "."
        stem = os.path.splitext(os.path.basename(target))[0]
        meta = os.path.join(d, f"{stem}.sound.json")
        ogg = os.path.join(d, f"{stem}.ogg")
        return target, (meta if os.path.isfile(meta) else None), (ogg if os.path.isfile(ogg) else None)
    return None, None, None


def ffprobe_duration_ms(path: str):
    ffprobe = shutil.which("ffprobe")
    if not ffprobe:
        return None
    cmd = [ffprobe, "-v", "error", "-show_entries", "format=duration",
           "-of", "default=noprint_wrappers=1:nokey=1", path]
    try:
        out = subprocess.run(cmd, capture_output=True, text=True)
    except OSError:
        return None
    if out.returncode != 0:
        return None
    try:
        return float(out.stdout.strip()) * 1000.0
    except ValueError:
        return None


def validate(target: str, min_ms: float, max_ms: float, max_edge_silence_ms: float) -> Report:
    r = Report()
    wav_path, meta_path, ogg_path = find_asset(target)
    if not r.require(wav_path is not None and os.path.isfile(wav_path),
                     f"a .wav asset is present at {target!r}"):
        return r

    try:
        samples, sr, channels, nframes = read_wav_samples(wav_path)
    except Exception as exc:
        r.fail(f"{os.path.basename(wav_path)} is not a readable 16-bit PCM WAV: {exc}")
        return r
    r.ok(f"WAV readable: {os.path.basename(wav_path)} ({nframes} frames, {channels}ch @ {sr} Hz)")

    duration_ms = (nframes / sr * 1000.0) if sr else 0.0
    r.require(min_ms <= duration_ms <= max_ms,
              f"duration {duration_ms:.1f} ms within [{min_ms}, {max_ms}] ms")
    r.require(sr in ALLOWED_SAMPLE_RATES,
              f"sample rate {sr} is allowed ({sorted(ALLOWED_SAMPLE_RATES)})")
    r.require(channels in (1, 2), f"channels {channels} in {{1, 2}}")

    # clipping
    peak = max((abs(s) for s in samples), default=0)
    r.require(peak < FULL_SCALE,
              f"no clipping (peak |sample| = {peak} < {FULL_SCALE})")

    # silence
    if not r.require(peak > SILENCE_THRESHOLD,
                     f"file is not entirely silent (peak {peak} > {SILENCE_THRESHOLD})"):
        pass
    per_ch = max(1, channels)
    lead_ms = (leading_silent(samples) / per_ch) / sr * 1000.0 if sr else 0.0
    trail_ms = (trailing_silent(samples) / per_ch) / sr * 1000.0 if sr else 0.0
    if lead_ms > max_edge_silence_ms:
        r.warn(f"leading silence {lead_ms:.1f} ms exceeds {max_edge_silence_ms} ms")
    else:
        r.ok(f"leading silence {lead_ms:.1f} ms within {max_edge_silence_ms} ms")
    if trail_ms > max_edge_silence_ms:
        r.warn(f"trailing silence {trail_ms:.1f} ms exceeds {max_edge_silence_ms} ms")
    else:
        r.ok(f"trailing silence {trail_ms:.1f} ms within {max_edge_silence_ms} ms")

    # sidecar metadata cross-check
    if meta_path:
        with open(meta_path, "r", encoding="utf-8") as f:
            meta = json.load(f)
        r.ok(f"sidecar metadata present: {os.path.basename(meta_path)}")
        r.require(ID_RE.match(str(meta.get("id", ""))) is not None,
                  f"metadata id {meta.get('id')!r} matches ^[a-z][a-z0-9_]*$")
        if "sampleRate" in meta:
            r.require(int(meta["sampleRate"]) == sr,
                      f"metadata sampleRate {meta['sampleRate']} matches WAV {sr}")
        if "channels" in meta:
            r.require(int(meta["channels"]) == channels,
                      f"metadata channels {meta['channels']} matches WAV {channels}")
        if "durationMs" in meta:
            diff = abs(float(meta["durationMs"]) - duration_ms)
            r.require(diff <= 5.0,
                      f"metadata durationMs {meta['durationMs']} matches WAV "
                      f"{duration_ms:.1f} ms (delta {diff:.1f} ms <= 5)")
    else:
        r.warn("no <id>.sound.json sidecar found (cue metadata cross-check skipped)")

    # OGG deep check
    if ogg_path:
        ogg_ms = ffprobe_duration_ms(ogg_path)
        if ogg_ms is None:
            print("note: ffprobe not found (or unreadable OGG); skipping OGG deep check.")
            r.ok(f"OGG present: {os.path.basename(ogg_path)} (deep check skipped)")
        else:
            r.require(min_ms <= ogg_ms <= max_ms,
                      f"OGG duration {ogg_ms:.1f} ms within [{min_ms}, {max_ms}] ms")

    return r


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Validate a retro bit SFX asset (a sound dir or a single .wav). "
                    "Read-only; never modifies the asset.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    ap.add_argument("target", help="Sound asset directory OR a single .wav file.")
    ap.add_argument("--min-ms", type=float, default=30.0, help="Minimum allowed duration (ms).")
    ap.add_argument("--max-ms", type=float, default=1500.0, help="Maximum allowed duration (ms).")
    ap.add_argument("--max-edge-silence-ms", type=float, default=120.0,
                    help="Leading/trailing silence above this only WARNS.")
    args = ap.parse_args()

    r = validate(args.target, args.min_ms, args.max_ms, args.max_edge_silence_ms)

    print(f"\nAudio asset validation: {args.target}")
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

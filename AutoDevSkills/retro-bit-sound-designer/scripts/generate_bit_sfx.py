#!/usr/bin/env python3
"""
generate_bit_sfx.py — deterministic short retro bit-style SFX synthesiser.

Part of the AutoDev global skill `retro-bit-sound-designer`. It synthesises ONE
short (<= 1500 ms) original bit-like sound effect from a sound *brief* (JSON) or
a built-in *preset*, and writes:

  <out>/<id>.wav          16-bit PCM mono WAV  (rendered with the stdlib only)
  <out>/<id>.sound.json   cue metadata (see the format below)
  <out>/<id>.ogg          OGG/Vorbis for Android shipping — ONLY if ffmpeg is on PATH

Hard rules:
  * WAV synthesis uses the Python STANDARD LIBRARY only (wave, struct, math).
  * Fully deterministic: same seed + params -> byte-identical WAV. No timestamps.
    The seed only affects the `noise` waveform (a seeded PRNG); every other
    waveform is pure math and needs no seed.
  * Peak is normalised to `peakDbfs` (default -3 dBFS) and hard-clamped just below
    full scale, so the output NEVER clips.
  * Duration is capped at 1500 ms (error if the brief asks for more).
  * ORIGINAL synthesis only — never fetches or embeds external samples, never
    imitates copyrighted character sounds.

Sound brief / metadata format (`<id>.sound.json`):

  {
    "id": "happy_reward_chirp",            # ^[a-z][a-z0-9_]*$
    "animationId": "happy_reward_sparkle",
    "emotionalIntent": "delight",
    "cueTimeMs": 120, "cueFrame": 2,
    "durationMs": 320, "sampleRate": 22050, "channels": 1,
    "waveform": "square",                  # square|pulse|triangle|sine|noise
    "envelope": {"attackMs": 5, "decayMs": 40, "sustainLevel": 0.6, "releaseMs": 90},
    "pitch": {"startHz": 660, "endHz": 990, "glide": "exp"},   # glide: linear|exp
    "volumeCategory": "soft",              # soft|normal|accent
    "peakDbfs": -3.0, "seed": 1234,
    "files": {"wav": "happy_reward_chirp.wav", "ogg": "happy_reward_chirp.ogg"}
  }

Usage:
  python generate_bit_sfx.py --preset happy_reward_chirp --out-dir out/happy_reward_chirp
  python generate_bit_sfx.py --config my_brief.json --out-dir out/my_sfx
  python generate_bit_sfx.py --preset charging_power_hum --out-dir out/hum --no-ogg
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import re
import shutil
import struct
import subprocess
import sys
import wave

# --------------------------------------------------------------------------- #
# Constraints (shared with validate_audio_asset.py)                           #
# --------------------------------------------------------------------------- #
ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")
MAX_DURATION_MS = 1500
WAVEFORMS = {"square", "pulse", "triangle", "sine", "noise"}
GLIDES = {"linear", "exp"}
VOLUME_CATEGORIES = {"soft", "normal", "accent"}
ALLOWED_SAMPLE_RATES = {8000, 11025, 16000, 22050, 44100, 48000}
FULL_SCALE = 32767
# Clamp just below full scale so validators never see a clipped (|s|>=32767) sample.
SAFE_MAX = FULL_SCALE - 1  # 32766

# Built-in briefs (each is a complete brief; `files` is filled in at write time).
PRESETS: dict[str, dict] = {
    "happy_reward_chirp": {
        "id": "happy_reward_chirp",
        "animationId": "happy_reward_sparkle",
        "emotionalIntent": "delight",
        "cueTimeMs": 120, "cueFrame": 2,
        "durationMs": 320, "sampleRate": 22050, "channels": 1,
        "waveform": "square",
        "envelope": {"attackMs": 5, "decayMs": 40, "sustainLevel": 0.6, "releaseMs": 90},
        "pitch": {"startHz": 660, "endHz": 990, "glide": "exp"},
        "volumeCategory": "soft", "peakDbfs": -3.0, "seed": 1234,
    },
    "sleepy_yawn_down": {
        "id": "sleepy_yawn_down",
        "animationId": "sleepy_yawn",
        "emotionalIntent": "calm",
        "cueTimeMs": 80, "cueFrame": 1,
        "durationMs": 600, "sampleRate": 22050, "channels": 1,
        "waveform": "triangle",
        "envelope": {"attackMs": 40, "decayMs": 120, "sustainLevel": 0.5, "releaseMs": 220},
        "pitch": {"startHz": 520, "endHz": 230, "glide": "exp"},
        "volumeCategory": "soft", "peakDbfs": -4.0, "seed": 7,
    },
    "charging_power_hum": {
        "id": "charging_power_hum",
        "animationId": "charging_power_up",
        "emotionalIntent": "energize",
        "cueTimeMs": 0, "cueFrame": 0,
        "durationMs": 900, "sampleRate": 22050, "channels": 1,
        "waveform": "pulse",
        "envelope": {"attackMs": 120, "decayMs": 80, "sustainLevel": 0.7, "releaseMs": 200},
        "pitch": {"startHz": 180, "endHz": 300, "glide": "linear"},
        "volumeCategory": "normal", "peakDbfs": -3.0, "seed": 99,
    },
}


def die(msg: str, code: int = 2) -> None:
    sys.stderr.write(f"ERROR: {msg}\n")
    sys.exit(code)


# --------------------------------------------------------------------------- #
# Brief validation                                                            #
# --------------------------------------------------------------------------- #
def validate_brief(b: dict) -> None:
    def need(key):
        if key not in b:
            die(f"brief is missing required field '{key}'.")

    for key in ("id", "durationMs", "sampleRate", "channels", "waveform",
                "envelope", "pitch", "volumeCategory"):
        need(key)

    if not ID_RE.match(str(b["id"])):
        die(f"id {b['id']!r} must match ^[a-z][a-z0-9_]*$ "
            f"(lowercase, start with a letter, letters/digits/underscore only).")

    dur = b["durationMs"]
    if not isinstance(dur, (int, float)) or dur <= 0:
        die(f"durationMs must be > 0 (got {dur!r}).")
    if dur > MAX_DURATION_MS:
        die(f"durationMs {dur} exceeds the hard cap of {MAX_DURATION_MS} ms. "
            f"Retro SFX must be short — shorten the brief.")

    if b["sampleRate"] not in ALLOWED_SAMPLE_RATES:
        die(f"sampleRate {b['sampleRate']} not allowed. "
            f"Use one of {sorted(ALLOWED_SAMPLE_RATES)}.")

    if b["channels"] not in (1, 2):
        die(f"channels must be 1 (mono, recommended for SFX) or 2 (got {b['channels']!r}).")

    if b["waveform"] not in WAVEFORMS:
        die(f"waveform {b['waveform']!r} not supported. Use one of {sorted(WAVEFORMS)}.")

    if b["volumeCategory"] not in VOLUME_CATEGORIES:
        die(f"volumeCategory {b['volumeCategory']!r} invalid. "
            f"Use one of {sorted(VOLUME_CATEGORIES)}.")

    env = b["envelope"]
    for key in ("attackMs", "decayMs", "sustainLevel", "releaseMs"):
        if key not in env:
            die(f"envelope is missing '{key}'.")
    if not (0.0 <= env["sustainLevel"] <= 1.0):
        die(f"envelope.sustainLevel must be within [0,1] (got {env['sustainLevel']!r}).")

    pitch = b["pitch"]
    for key in ("startHz", "endHz", "glide"):
        if key not in pitch:
            die(f"pitch is missing '{key}'.")
    if pitch["glide"] not in GLIDES:
        die(f"pitch.glide {pitch['glide']!r} invalid. Use one of {sorted(GLIDES)}.")
    if pitch["startHz"] <= 0 or pitch["endHz"] <= 0:
        die("pitch.startHz / pitch.endHz must be > 0 Hz.")


# --------------------------------------------------------------------------- #
# Synthesis (stdlib only)                                                     #
# --------------------------------------------------------------------------- #
def freq_at(t_frac: float, start: float, end: float, glide: str) -> float:
    if glide == "exp" and start > 0 and end > 0:
        return start * (end / start) ** t_frac
    return start + (end - start) * t_frac


def waveform_value(wf: str, phase: float, rng: random.Random, duty: float) -> float:
    if wf == "sine":
        return math.sin(phase)
    if wf == "noise":
        return rng.uniform(-1.0, 1.0)
    frac = (phase / (2.0 * math.pi)) % 1.0
    if wf == "square":
        return 1.0 if frac < 0.5 else -1.0
    if wf == "pulse":
        return 1.0 if frac < duty else -1.0
    if wf == "triangle":
        return 4.0 * abs(frac - 0.5) - 1.0
    raise ValueError(f"unhandled waveform {wf!r}")


def envelope_gain(i: int, n: int, a: int, d: int, sustain: float, r: int) -> float:
    sustain_start = a + d
    release_start = max(n - r, sustain_start)
    if a > 0 and i < a:
        g = i / a
    elif i < sustain_start:
        g = 1.0 + (sustain - 1.0) * ((i - a) / d) if d > 0 else sustain
    elif i < release_start:
        g = sustain
    else:
        denom = max(1, n - release_start)
        g = sustain * (1.0 - (i - release_start) / denom)
    return max(0.0, min(1.0, g))


def synthesise(brief: dict) -> list[int]:
    """Render mono float samples then normalise to int16. Deterministic."""
    sr = int(brief["sampleRate"])
    dur_ms = float(brief["durationMs"])
    n = max(1, int(round(sr * dur_ms / 1000.0)))
    wf = brief["waveform"]
    duty = float(brief.get("duty", 0.25 if wf == "pulse" else 0.5))
    env = brief["envelope"]
    a = int(round(sr * env["attackMs"] / 1000.0))
    d = int(round(sr * env["decayMs"] / 1000.0))
    r = int(round(sr * env["releaseMs"] / 1000.0))
    sustain = float(env["sustainLevel"])
    pitch = brief["pitch"]
    start_hz, end_hz, glide = pitch["startHz"], pitch["endHz"], pitch["glide"]
    rng = random.Random(int(brief.get("seed", 0)))

    floats = [0.0] * n
    phase = 0.0
    for i in range(n):
        t_frac = i / n
        f = freq_at(t_frac, start_hz, end_hz, glide)
        floats[i] = waveform_value(wf, phase, rng, duty) * envelope_gain(i, n, a, d, sustain, r)
        phase += 2.0 * math.pi * f / sr

    peak = max((abs(x) for x in floats), default=0.0)
    peak_dbfs = float(brief.get("peakDbfs", -3.0))
    target = 10.0 ** (peak_dbfs / 20.0)
    scale = (target / peak) if peak > 0 else 0.0

    out: list[int] = []
    for x in floats:
        s = int(round(x * scale * FULL_SCALE))
        out.append(max(-SAFE_MAX, min(SAFE_MAX, s)))
    return out


def write_wav(path: str, samples: list[int], sr: int, channels: int) -> None:
    frames = samples if channels == 1 else [s for s in samples for _ in range(channels)]
    data = struct.pack("<%dh" % len(frames), *frames)
    with wave.open(path, "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)  # 16-bit PCM
        w.setframerate(sr)
        w.writeframes(data)


def export_ogg(wav_path: str, ogg_path: str) -> bool:
    """Encode OGG/Vorbis via ffmpeg if available. Returns True on success."""
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        print("note: ffmpeg not found; skipping OGG (wav only).")
        return False
    cmd = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
           "-i", wav_path, "-c:a", "libvorbis", "-qscale:a", "4",
           "-map_metadata", "-1", ogg_path]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True)
    except OSError as exc:  # pragma: no cover - defensive
        print(f"note: could not launch ffmpeg ({exc}); skipping OGG (wav only).")
        return False
    if proc.returncode != 0:
        print(f"note: ffmpeg OGG export failed (exit {proc.returncode}); "
              f"keeping WAV only. stderr: {proc.stderr.strip()}")
        return False
    return True


# --------------------------------------------------------------------------- #
# Main                                                                        #
# --------------------------------------------------------------------------- #
def build_brief(args: argparse.Namespace) -> dict:
    if bool(args.config) == bool(args.preset):
        die("provide exactly one of --config FILE or --preset NAME.")
    if args.preset:
        if args.preset not in PRESETS:
            die(f"unknown preset {args.preset!r}. Available: {sorted(PRESETS)}.")
        brief = json.loads(json.dumps(PRESETS[args.preset]))  # deep copy
    else:
        if not os.path.isfile(args.config):
            die(f"config file not found: {args.config}")
        with open(args.config, "r", encoding="utf-8") as f:
            brief = json.load(f)

    # CLI overrides (applied before validation so overrides are validated too).
    if args.id:
        brief["id"] = args.id
    if args.seed is not None:
        brief["seed"] = args.seed
    if args.sample_rate is not None:
        brief["sampleRate"] = args.sample_rate
    brief.setdefault("seed", 0)
    brief.setdefault("peakDbfs", -3.0)
    brief.setdefault("channels", 1)
    return brief


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Synthesise a short retro bit-style sound effect (stdlib-only WAV "
                    "+ <id>.sound.json, plus OGG when ffmpeg is present). Deterministic.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    src = ap.add_argument_group("input (choose one)")
    src.add_argument("--config", help="JSON sound brief describing the SFX to render.")
    src.add_argument("--preset", help=f"Built-in preset. One of: {sorted(PRESETS)}.")
    ap.add_argument("--out-dir", required=True, help="Directory to write the asset into.")
    ap.add_argument("--id", help="Override the sound id (must match ^[a-z][a-z0-9_]*$).")
    ap.add_argument("--seed", type=int, default=None,
                    help="Override seed (only affects the 'noise' waveform). Default: brief or 0.")
    ap.add_argument("--sample-rate", type=int, default=None,
                    help="Override sample rate (Hz). One of "
                         f"{sorted(ALLOWED_SAMPLE_RATES)}.")
    ap.add_argument("--no-ogg", action="store_true",
                    help="Do not export OGG even if ffmpeg is available.")
    args = ap.parse_args()

    brief = build_brief(args)
    validate_brief(brief)

    sr = int(brief["sampleRate"])
    channels = int(brief["channels"])
    sound_id = brief["id"]

    os.makedirs(args.out_dir, exist_ok=True)
    wav_path = os.path.join(args.out_dir, f"{sound_id}.wav")
    ogg_path = os.path.join(args.out_dir, f"{sound_id}.ogg")
    meta_path = os.path.join(args.out_dir, f"{sound_id}.sound.json")

    samples = synthesise(brief)
    write_wav(wav_path, samples, sr, channels)

    ogg_ok = False
    if not args.no_ogg:
        ogg_ok = export_ogg(wav_path, ogg_path)

    files = {"wav": f"{sound_id}.wav"}
    if ogg_ok:
        files["ogg"] = f"{sound_id}.ogg"

    env = brief["envelope"]
    pitch = brief["pitch"]
    meta = {
        "id": sound_id,
        "animationId": brief.get("animationId"),
        "emotionalIntent": brief.get("emotionalIntent"),
        "cueTimeMs": brief.get("cueTimeMs", 0),
        "cueFrame": brief.get("cueFrame", 0),
        "durationMs": int(round(brief["durationMs"])),
        "sampleRate": sr,
        "channels": channels,
        "waveform": brief["waveform"],
        "envelope": {
            "attackMs": env["attackMs"], "decayMs": env["decayMs"],
            "sustainLevel": env["sustainLevel"], "releaseMs": env["releaseMs"],
        },
        "pitch": {"startHz": pitch["startHz"], "endHz": pitch["endHz"], "glide": pitch["glide"]},
        "volumeCategory": brief["volumeCategory"],
        "peakDbfs": float(brief.get("peakDbfs", -3.0)),
        "seed": int(brief.get("seed", 0)),
        "files": files,
    }
    with open(meta_path, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")

    print(f"OK  wrote {wav_path}  ({len(samples)} samples @ {sr} Hz, {channels}ch)")
    print(f"OK  wrote {meta_path}")
    if ogg_ok:
        print(f"OK  wrote {ogg_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

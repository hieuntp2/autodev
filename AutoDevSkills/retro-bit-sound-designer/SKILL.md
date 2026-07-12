---
name: retro-bit-sound-designer
description: Design, synthesise, validate and package SHORT (<= ~1s) original retro bit-style sound effects for pet reactions. Deterministic stdlib WAV synthesis (square/pulse/triangle/sine/noise + ADSR + pitch glide), OGG export for Android when ffmpeg is present, an <id>.sound.json cue-metadata sidecar, and validators for duration/sample-rate/channels/clipping/edge-silence and animation-manifest linkage. Owns audio assets only — never draws frames, never decides which reaction fires.
---

# retro-bit-sound-designer

Produce **short, original, retro bit-style sound effects** that play in sync with a
pixel-pet animation. One global AutoDev skill; only the *generated audio* lands in
the target project. This skill **owns audio assets and their cue metadata** — it
does **not** own visual design (that is `eye-only-pixel-animation-artist`) or
behavior/event mapping (that is `pet-behavior-designer`).

## Audio direction (the house style)

- **Short**: `<=` ~1 s (hard cap 1500 ms). Most cues are 150–600 ms.
- **Retro / bit-like**: simple digital waveforms (square, pulse, triangle, sine,
  noise), crisp ADSR, expressive pitch glides. No orchestral or realistic samples.
- **Pleasant at repeated low volume**: these fire often. Normalise to a soft peak
  (default `-3 dBFS`), never clip, keep it non-fatiguing.
- **Original**: synthesise everything locally. **Never** imitate a copyrighted
  character's sound and **never** embed a copyrighted sample.
- **Android-friendly**: mono, lossless WAV source + small OGG for shipping.
- **Optional / mutable**: audio must be mute-able; a missing sound degrades to
  silence and must never crash the runtime.

Avoid: continuous background music, long files, harsh clipping, excessive volume,
large audio libraries.

## When this skill triggers (scope) — and when it does NOT

Triggers for: generating/synthesising a bit/8-bit/chiptune sound effect or blip/
beep/chirp, exporting a WAV/OGG audio asset, syncing an SFX to an animation frame,
or validating an audio asset / its clipping and duration.

Does **NOT** trigger for (hand off to the right owner):

- sprite-sheet / pixel / frame **visual** creation → `eye-only-pixel-animation-artist`
- deciding **which reaction fires**, priority, cooldown, event→animation mapping
  (behavior-only) → `pet-behavior-designer`
- **spoken voice / TTS / narration** (this skill makes abstract SFX, not speech)
- unrelated **background music / long music loops**
- generic **Android audio playback bugs** (AudioManager/MediaPlayer focus, etc.)

## Required workflow

1. **Read the goal + the target animation manifest** (`<id>.animation.json`) you
   are scoring. Find its `events[]` entries of `type:"sound"`; the `value` there is
   the `sound_cue_id` you must produce.
2. **Identify the emotional purpose** of the moment (delight, calm, curiosity,
   irritation, energize, surprise). See `references/bit-sound-style-guide.md` for
   the per-emotion pitch/waveform vocabulary.
3. **Write a compact sound brief** capturing: sound id, animation id, emotional
   intent, cue frame / cue time, target duration, waveform + envelope concept,
   pitch movement, repetition risk, volume category. See
   `examples/example-sound-brief.md`.
4. **Generate** with deterministic **local synthesis** (never fetch samples):

   ```bash
   python <SKILL_DIR>/scripts/generate_bit_sfx.py \
       --preset happy_reward_chirp \
       --out-dir app/src/main/assets/pet/sounds/happy_reward_chirp
   # or from your own brief:
   python <SKILL_DIR>/scripts/generate_bit_sfx.py \
       --config my_brief.json \
       --out-dir app/src/main/assets/pet/sounds/<sound_id>
   ```

5. **Export the Android-preferred format**: the generator writes the lossless WAV
   source and, when `ffmpeg` is on PATH, a small `.ogg` for shipping. If ffmpeg is
   absent it prints a note and ships WAV only — that is expected, not an error.
6. **Write/update `<id>.sound.json`** — the generator does this automatically; keep
   the id stable and matching the manifest cue value.
7. **Validate** (both validators; include their output in your run report):

   ```bash
   python <SKILL_DIR>/scripts/validate_audio_asset.py \
       app/src/main/assets/pet/sounds/happy_reward_chirp
   python <SKILL_DIR>/scripts/validate_sound_links.py \
       --manifest app/src/main/assets/pet/animations/happy_reward_sparkle/happy_reward_sparkle.animation.json \
       --sounds-dir app/src/main/assets/pet/sounds
   ```

   `validate_audio_asset.py` checks existence, duration, sample rate, channels,
   peak/clipping, edge silence and `<id>.sound.json` cross-check.
   `validate_sound_links.py` checks that every `type:"sound"` cue resolves to a
   real asset (or is an intentional `optional` stub) and that cue ids are unique.
8. **Report the exact files** written and the sync details (cue frame/time,
   duration, waveform, peak dBFS) plus both validation results.

`<SKILL_DIR>` is the absolute path to this skill; AutoDev injects it into your
prompt. Write output **into the target project**, never back into `AutoDevSkills/`.

## Hard rule (honesty)

**Never claim a sound file exists unless it exists and `validate_audio_asset.py`
actually ran and passed on it.** Prefer local procedural synthesis over fetching
anything. If ffmpeg is unavailable, say "WAV only" — do not pretend an OGG exists.

## Sound metadata format (`<id>.sound.json`)

```json
{
  "id": "happy_reward_chirp",
  "animationId": "happy_reward_sparkle",
  "emotionalIntent": "delight",
  "cueTimeMs": 120, "cueFrame": 2,
  "durationMs": 320, "sampleRate": 22050, "channels": 1,
  "waveform": "square",
  "envelope": { "attackMs": 5, "decayMs": 40, "sustainLevel": 0.6, "releaseMs": 90 },
  "pitch": { "startHz": 660, "endHz": 990, "glide": "exp" },
  "volumeCategory": "soft", "peakDbfs": -3.0, "seed": 1234,
  "files": { "wav": "happy_reward_chirp.wav", "ogg": "happy_reward_chirp.ogg" }
}
```

- `id` / `sound_cue_id` must match `^[a-z][a-z0-9_]*$`.
- `waveform` ∈ {square, pulse, triangle, sine, noise};
  `pitch.glide` ∈ {linear, exp}; `volumeCategory` ∈ {soft, normal, accent}.
- `seed` only affects the `noise` waveform (seeded PRNG). Same seed + params →
  byte-identical WAV.

## Built-in presets

`happy_reward_chirp` (rising square chirp, delight), `sleepy_yawn_down` (falling
triangle, calm), `charging_power_hum` (rising pulse hum, energize). Start from a
preset with `--preset NAME`, or author your own brief and pass `--config FILE`.

## Expected outputs (in the target project)

```
app/src/main/assets/pet/sounds/<sound_id>/
  <sound_id>.wav         # lossless deterministic source (16-bit PCM mono)
  <sound_id>.ogg         # shipping format (only when ffmpeg is present)
  <sound_id>.sound.json  # cue metadata (bound to the animation via events[])
```

## Supporting files

- `references/bit-sound-style-guide.md` — waveform palette, envelope shapes,
  per-emotion pitch motion, loudness/repetition and originality rules.
- `references/animation-sound-sync-guide.md` — how a cue binds to an animation via
  `events[]`, choosing the cue frame/time, keeping the SFX shorter than the frame,
  optional vs required cues.
- `references/android-audio-asset-guide.md` — WAV-source + OGG-shipping, sample
  rate/bit depth, mono, small files, mute-ability, SoundPool low-latency notes.
- `references/cross-skill-contract.md` — short pointer to the canonical contract,
  the shared id table, and the sound-hook rule.
- `examples/example-sound-brief.md` — a filled brief for `happy_reward_chirp`.
- `evals/trigger-cases.json` — trigger self-check + cross-skill winner cases.

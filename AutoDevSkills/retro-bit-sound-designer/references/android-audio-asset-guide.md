# Android audio asset guide

How to ship the SFX this skill produces so they load fast, stay tiny, and behave on
Android.

## Formats: lossless WAV source + OGG for shipping

- **WAV (16-bit PCM)** is the *deterministic source of truth* — regenerable
  byte-for-byte from the brief. Keep it in the asset dir for provenance and
  re-encoding.
- **OGG/Vorbis** is the *shipping* format: much smaller than WAV, well supported by
  Android's media stack and `SoundPool`. The generator produces it automatically
  when `ffmpeg` is on PATH (`libvorbis`, `-qscale:a 4`).
- Prefer OGG over MP3 (no historical licensing baggage, better small-file quality).
- If `ffmpeg` is absent the generator ships **WAV only** and says so; that is a
  valid fallback, just larger.

## Sample rate & bit depth

- **22050 Hz** is the sweet spot for these short bit SFX: plenty of bandwidth for
  chiptune content at half the size of 44100. Use 44100/48000 only if a cue truly
  needs the extra top end.
- **16-bit** PCM is standard and what the validators expect.
- Allowed sample rates: `{8000, 11025, 16000, 22050, 44100, 48000}`.

## Mono for SFX

Pet reaction cues are point sounds — **mono** halves the size versus stereo and is
what `SoundPool` mixes anyway. The generator defaults to mono (`channels: 1`).

## Small files

- Keep durations short (most cues 150–600 ms). Short + mono + OGG ⇒ typically a few
  KB per cue.
- Don't ship a large audio library; ship only the cues the animations reference.
- The WAV source can be `.gitignore`d in size-sensitive repos as long as the brief
  (and this skill) can regenerate it deterministically — but committing it is the
  safe default for reproducibility.

## Mute-ability

Audio is **optional and mutable**. Route SFX through a user-controllable volume/mute
setting; a muted or missing sound must never block or crash a reaction. This mirrors
the cross-skill degradation rule (missing optional sound → play silently).

## Low-latency playback (SoundPool)

- For frequent, short cues use `SoundPool` (or `AudioTrack`) rather than
  `MediaPlayer`: it pre-loads decoded PCM and fires with minimal latency, ideal for
  tap/hop/blip reactions.
- Pre-load cues at startup; play by id. Keep the simultaneous-stream count modest.
- Normalised soft peaks (this skill's default `-3 dBFS`, never clipping) keep mixed
  simultaneous cues from summing into distortion.

> Runtime playback code (`PetBehaviorController.kt` firing a `sound_cue_id`, any
> `SoundPool` wrapper) is written by the **product**, not by this skill. This skill
> only produces the asset + metadata the runtime consumes.

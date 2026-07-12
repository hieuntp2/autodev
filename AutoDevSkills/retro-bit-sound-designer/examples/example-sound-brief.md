# Example sound brief — `happy_reward_chirp`

A filled compact brief for the canonical worked example: the reward chirp that
plays when the pet is tapped and shows the `happy_reward_sparkle` animation.

## Brief (human-readable)

| Field | Value | Why |
| --- | --- | --- |
| sound id | `happy_reward_chirp` | matches the manifest cue value; `^[a-z][a-z0-9_]*$` |
| animation id | `happy_reward_sparkle` | the visual it accents |
| emotional intent | delight | happy reward moment |
| cue frame / time | frame 2 / 120 ms | fires when the sparkle pops (after 2×80 ms frames) |
| target duration | 320 ms | shorter than the on-screen sparkle, well under 1500 ms cap |
| waveform + envelope | `square`; attack 5 / decay 40 / sustain 0.6 / release 90 ms | classic chiptune pluck, snappy but not clicky |
| pitch movement | 660 → 990 Hz, `exp` glide | rising chirp = "yay" |
| repetition risk | high (tap fires often) → `soft` peak `-3 dBFS` | pleasant when repeated |
| volume category | `soft` | frequent cue |

## Brief (machine JSON — pass to `--config`)

```json
{
  "id": "happy_reward_chirp",
  "animationId": "happy_reward_sparkle",
  "emotionalIntent": "delight",
  "cueTimeMs": 120,
  "cueFrame": 2,
  "durationMs": 320,
  "sampleRate": 22050,
  "channels": 1,
  "waveform": "square",
  "envelope": { "attackMs": 5, "decayMs": 40, "sustainLevel": 0.6, "releaseMs": 90 },
  "pitch": { "startHz": 660, "endHz": 990, "glide": "exp" },
  "volumeCategory": "soft",
  "peakDbfs": -3.0,
  "seed": 1234
}
```

This brief is also shipped as the built-in preset `happy_reward_chirp`.

## Generate + validate

```bash
# from a preset (equivalent to the brief above):
python <SKILL_DIR>/scripts/generate_bit_sfx.py \
    --preset happy_reward_chirp \
    --out-dir app/src/main/assets/pet/sounds/happy_reward_chirp

# or from the JSON brief saved as happy_reward_chirp.brief.json:
python <SKILL_DIR>/scripts/generate_bit_sfx.py \
    --config happy_reward_chirp.brief.json \
    --out-dir app/src/main/assets/pet/sounds/happy_reward_chirp

python <SKILL_DIR>/scripts/validate_audio_asset.py \
    app/src/main/assets/pet/sounds/happy_reward_chirp

python <SKILL_DIR>/scripts/validate_sound_links.py \
    --manifest app/src/main/assets/pet/animations/happy_reward_sparkle/happy_reward_sparkle.animation.json \
    --sounds-dir app/src/main/assets/pet/sounds
```

## The animation hook this satisfies

The `happy_reward_sparkle` manifest carries the cue:

```json
"events": [
  { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }
]
```

`validate_sound_links.py` resolves `happy_reward_chirp` to
`app/src/main/assets/pet/sounds/happy_reward_chirp/happy_reward_chirp.sound.json`
and reports the hook **OK**.

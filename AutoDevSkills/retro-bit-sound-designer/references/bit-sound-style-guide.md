# Bit-sound style guide

The house style for `retro-bit-sound-designer`: short, original, digital, expressive,
pleasant at repeated low volume. Everything here is realised by
`scripts/generate_bit_sfx.py` (stdlib synthesis) — no external samples, ever.

## Waveform palette

| Waveform | Character | Good for |
| --- | --- | --- |
| `sine` | pure, soft, round | gentle blips, calm cues, sub-tones |
| `triangle` | mellow but bright, "8-bit soft" | yawns, sighs, neutral chirps |
| `square` | classic chiptune, hollow, punchy | happy chirps, confirmations, coins |
| `pulse` | thinner/buzzier square (≈25% duty) | hums, power-ups, tense cues |
| `noise` | unpitched, textural (seeded PRNG) | buzzes, static, shakes, "no" sounds |

Only `noise` uses `seed`; every other waveform is pure math (fully deterministic
without a seed).

## Envelope (ADSR) shapes

`envelope: { attackMs, decayMs, sustainLevel (0..1), releaseMs }`.

- **Pluck / blip** — tiny attack (2–8 ms), short decay, low-ish sustain, short
  release. Snappy and non-fatiguing. Use for taps and confirmations.
- **Swell** — longer attack (40–120 ms), gentle release. Use for hums / power-ups.
- **Sigh / yawn** — medium attack, long decay + long release. Use for sleepy cues.
- **Stab** — near-zero attack, very short everything. Use for surprise/alerts.

Keep `attackMs` above ~2 ms to avoid a click; the generator ramps from zero so the
file has no hard leading edge.

## Per-emotion pitch motion

`pitch: { startHz, endHz, glide }`, `glide` ∈ {`linear`, `exp`}. Exponential glide
sounds more musical for wide sweeps; linear is fine for small moves.

| Emotion | Motion | Waveform | Sketch |
| --- | --- | --- | --- |
| happy / reward | **rising** chirp | square | 660 → 990 Hz, exp, 250–350 ms |
| sleepy / calm | **falling** tone | triangle | 520 → 230 Hz, exp, 500–650 ms |
| curious | **short two-tone** (low→high pause→settle) | triangle/square | 440 → 620 Hz, 180–260 ms |
| annoyed | **short buzz** (flat) | noise/pulse | ~300 Hz flat, 120–200 ms |
| charging / energize | **soft rising hum** | pulse | 180 → 300 Hz, linear, 700–900 ms |
| surprise | **fast up** stab | square | 500 → 1200 Hz, exp, 80–140 ms |

For a "two-tone" curious cue, generate two short cues or use a brief with a steep
early glide then a flat tail — keep the whole thing under ~260 ms.

## Loudness & repetition

- Normalise to a **soft peak**; default `peakDbfs = -3.0`. Use `soft` for cues that
  fire constantly (idle, tap), `normal` for occasional cues, `accent` sparingly for
  rare emphatic moments — and even then keep the actual peak conservative.
- The generator hard-clamps just below full scale, so output **never clips**.
- Because cues repeat, avoid long tails and avoid pitches that sit in a harsh
  2–4 kHz "ice-pick" band at high sustain. Prefer rounded waveforms for frequent cues.

## Originality rule (non-negotiable)

Design sounds from primitives (waveform + envelope + pitch motion). Do **not**
reproduce, approximate, or "recreate" a recognisable copyrighted character sound,
jingle, or sampled effect. If a brief asks for "the <brand> coin sound", refuse and
synthesise an original coin-like chirp instead.

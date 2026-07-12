# Animation Manifest Guide

The manifest `<id>.animation.json` is the runtime-agnostic description of how the
animation plays. The sprite sheet holds only pixels; **everything about playback
lives here**. It conforms to the shared schema
`AutoDevSkills/pixel-animation-artist/schema/animation.schema.json` (this skill
does not fork or copy that schema).

## Required fields

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | string | Stable `animation_id`, e.g. `happy_reward_sparkle`. |
| `spriteSheet` | string | Relative path to `<id>_sheet.png` (next to the manifest). |
| `frameWidth` | int ≥ 1 | Single cell width in pixels (e.g. 64). |
| `frameHeight` | int ≥ 1 | Single cell height in pixels (e.g. 64). |
| `frames[]` | array | Ordered playlist. Each entry `{ "index": ≥0, "durationMs": ≥1 }` references a sheet cell (row-major) and its on-screen time. Order IS play order; a cell index may repeat. |

## Common optional fields

| Field | Type | Meaning |
| --- | --- | --- |
| `columns` / `rows` | int ≥ 1 | Sheet grid shape (single row by default). |
| `loop` | bool | Whether playback repeats (default true). |
| `priority` | int | Higher wins when two animations compete (use the shared priority bands). |
| `interruptible` | bool | Whether a higher-priority animation may cut this one off. |
| `motion[]` | array | Keyframed transform of the WHOLE sprite (`timeMs, x, y, scale, rotation, alpha, easing`). |
| `events[]` | array | Timeline events; see the sound hook below. |

## Eye-only metadata conventions (additive)

The schema sets `additionalProperties: true`, so you MAY add eye-only metadata.
Store it so sibling animations for the same pet stay consistent:

```json
{
  "eyeAnchors": { "left": [22, 30], "right": [42, 30] },
  "accessoryArea": { "x": 4, "y": 4, "w": 56, "h": 18 }
}
```

- `eyeAnchors` — the fixed `(x, y)` centre of each eye (see the style guide). Keep
  identical across every animation of one pet.
- `accessoryArea` — the temporary secondary region where sparkles/hearts/sweat/
  sleep-"Z" may appear; cleared between animations.

These fields are advisory metadata for authoring/consistency; the runtime is free
to ignore them.

## The sound hook (`events[]`)

Audio is bound to the animation through `events[]` — this skill writes the **hook
(the id)**, the sound skill produces the asset:

```json
"events": [
  { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }
]
```

- `value` on a `type:"sound"` event **is a `sound_cue_id`**
  (`^[a-z][a-z0-9_]*$`), owned by `retro-bit-sound-designer`.
- This skill only records the id; it never synthesises audio.
- A missing sound asset degrades to **silent playback** (a warning, not an error).
  A missing required animation asset is an **error**. See
  `references/cross-skill-contract.md`.

`validate_animation_manifest.py` reports every sound hook it finds so the handoff
to the sound skill is explicit.

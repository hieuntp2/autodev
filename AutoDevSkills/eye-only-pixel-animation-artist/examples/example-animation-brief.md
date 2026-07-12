# Example Animation Brief — `happy_reward_sparkle`

A filled brief in the format step 3 of `SKILL.md` asks for. This is the canonical
cross-skill worked example (see the contract): `interaction.tap` → state `happy`
→ animation `happy_reward_sparkle` → sound `happy_reward_chirp`.

| Field | Value |
| --- | --- |
| **Animation id** | `happy_reward_sparkle` |
| **Emotional intent** | Delight / reward — the eyes brighten and squish into a happy arch, sparkles pop in the corners, then settle. |
| **Trigger / context** | `event_id: interaction.tap` while in `state_id: happy`; fired as a one-shot reward reaction. |
| **Frame count** | 6 unique cells (open → brighten → squish-up + sparkle in → sparkle peak → sparkle fade → settle). |
| **Frame dimensions** | 64 × 64 (square-ish portrait; renderer scales to full screen, nearest-neighbour). |
| **Eye anchors** | left `(22, 30)`, right `(42, 30)` — unchanged from the pet's other clips; the happy arch is drawn around these centres. |
| **Accessory / effect area** | Sparkles in the upper corners, region `x:4 y:4 w:56 h:18`. Temporary — absent in the first and last frame, cleared when the clip ends. |
| **Loop** | `false` (one-shot reward, returns to idle afterwards). |
| **Interruptible** | `true` (a higher-priority sensor/system reaction may cut it off). |
| **Priority** | `36` — Strong / emotional band (30–49) per the shared priority bands. |
| **Expected sound cue id(s)** | `happy_reward_chirp` (fired ~120 ms in, when the sparkle peaks). Owned by `retro-bit-sound-designer`; this skill only writes the hook. |

## Palette

Black background `#000000`; cyan eye `#00E5FF` with highlight `#7DF9FF` and dim
`#00A5BF`; sparkle accent `#FFF275`. Flat blocks only — no gradients, alpha
strictly 0/255.

## Resulting manifest sketch

```json
{
  "id": "happy_reward_sparkle",
  "spriteSheet": "happy_reward_sparkle_sheet.png",
  "frameWidth": 64,
  "frameHeight": 64,
  "columns": 6,
  "rows": 1,
  "loop": false,
  "interruptible": true,
  "priority": 36,
  "eyeAnchors": { "left": [22, 30], "right": [42, 30] },
  "accessoryArea": { "x": 4, "y": 4, "w": 56, "h": 18 },
  "frames": [
    { "index": 0, "durationMs": 120 },
    { "index": 1, "durationMs": 80 },
    { "index": 2, "durationMs": 90 },
    { "index": 3, "durationMs": 90 },
    { "index": 4, "durationMs": 90 },
    { "index": 5, "durationMs": 160 }
  ],
  "events": [
    { "timeMs": 120, "type": "sound", "value": "happy_reward_chirp" }
  ]
}
```

## Handoffs

- To **retro-bit-sound-designer**: produce `happy_reward_chirp` (a short, bright
  reward chirp) under `app/src/main/assets/pet/sounds/happy_reward_chirp/`.
- From **pet-behavior-designer**: it selected the ids, priority band and timing
  intent above and requested this asset.

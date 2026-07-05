# AutoDevSkills — global skill store

This directory is AutoDev's **global skill source**. Skills live here once and are
shared across every project the runner develops. They are **not** copied into each
target project's repo (that would duplicate the skill everywhere and drift out of
sync). A target project only ever receives the *output* a skill produces — for
pixel animation that means the generated `frames/`, sprite sheet, GIF and JSON
manifest under the project's own `assets/…` folder.

```
AutoDevSkills/
  <skill-id>/
    skill.json         # manifest read by AutoDevRunner's SkillRegistry
    SKILL.md           # instructions the coding agent follows when the skill is invoked
    scripts/           # reusable, deterministic scripts (CLI entrypoints)
    schema/            # JSON schemas the skill's output must conform to
    presets/           # ready-made input configs the agent can start from
    samples/           # committed reference output (proof the skill works)
```

## How AutoDev uses a skill

1. **Discovery** — on startup `SkillRegistry` scans this folder and loads every
   `skill.json`. See `src/AutoDevRunner/Skills/`.
2. **Selection** — before a run, the runner matches the project brief / notes /
   current task / creative plan against each skill's `triggers`. Matching skills
   are logged (which skill, which keywords, which task) and shown in the dashboard.
3. **Invocation** — the selected skill is injected **explicitly** into the prompt
   sent to the CLI provider (e.g. `Use $pixel-animation-artist to …`), together
   with the absolute paths of its scripts and its output convention. Selection is
   not left to implicit matching by the model alone.
4. **Reporting** — after the run, the run report records the skill used, the
   output path, generated files, and the validation result.

## Enabling / disabling

Every skill has an `enabled` flag. It can be toggled from the dashboard
("Skills" tab) or via `POST /api/skills/{id}/enable|disable`. Config can also
force a skill off via `AutoDev:Skills:Disabled` in `appsettings.json`.

## Adding a new skill

Create `AutoDevSkills/<skill-id>/skill.json` following the schema of the existing
one, drop your scripts under `scripts/`, and restart the runner (or call
`POST /api/skills/reload`). No code changes are required for the registry to pick
it up.

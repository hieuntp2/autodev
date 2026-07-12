#!/usr/bin/env python3
"""Trigger evaluator for AutoDev skills.

Mirrors the production selector in
``src/AutoDevRunner/Skills/SkillRegistry.cs`` (method ``Match``):

  * a skill matches a piece of text when ANY of its ``triggers`` keywords
    appears in the text as a **case-insensitive substring**;
  * skills are ranked by the number of **distinct** trigger hits, ties broken
    by skill id (ordinal, ascending).

Because this uses the exact same rule as the runner, the pass/fail results are
real selector outcomes — no invented "scores".

Two checks are performed for a skill's ``evals/trigger-cases.json``:

  1. **Self trigger** — does this skill trigger (or not) for each prompt, as the
     case's ``shouldTrigger`` expects?
  2. **Winner** (optional, needs ``--registry``) — when several sibling skills
     are loaded, is the top-ranked skill the one named in ``expectedWinner``?
     This catches cross-skill overlap (e.g. a visual prompt that wrongly ranks
     the sound skill first).

Exit code is non-zero if any case fails, so CI / validation can gate on it.

trigger-cases.json shape::

    {
      "skill": "eye-only-pixel-animation-artist",
      "cases": [
        {"prompt": "...", "shouldTrigger": true,  "expectedWinner": "eye-only-pixel-animation-artist", "note": "..."},
        {"prompt": "...", "shouldTrigger": false, "note": "..."}
      ]
    }

``expectedWinner`` is optional per case and only checked in ``--registry`` mode.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def load_triggers(skill_dir: Path) -> tuple[str, list[str]]:
    manifest = skill_dir / "skill.json"
    if not manifest.is_file():
        raise SystemExit(f"error: no skill.json in {skill_dir}")
    data = json.loads(manifest.read_text(encoding="utf-8"))
    return data.get("id", skill_dir.name), [t for t in data.get("triggers", []) if t.strip()]


def matched_keywords(triggers: list[str], text: str) -> list[str]:
    """Distinct trigger keywords found as case-insensitive substrings of text.

    Mirrors SkillRegistry.Match: substring containment, case-insensitive,
    de-duplicated (ordinal-ignore-case)."""
    hay = text.lower()
    seen: dict[str, None] = {}
    for t in triggers:
        if t.lower() in hay:
            seen.setdefault(t.lower(), None)
    return list(seen.keys())


def rank_skills(all_skills: dict[str, list[str]], text: str) -> list[tuple[str, int]]:
    """Return (skill_id, score) sorted by score desc then id asc — like the runner."""
    scored = []
    for sid, trigs in all_skills.items():
        score = len(matched_keywords(trigs, text))
        if score > 0:
            scored.append((sid, score))
    scored.sort(key=lambda p: (-p[1], p[0]))
    return scored


def load_registry(root: Path) -> dict[str, list[str]]:
    skills: dict[str, list[str]] = {}
    for child in sorted(root.iterdir()):
        manifest = child / "skill.json"
        if manifest.is_file():
            try:
                sid, trigs = load_triggers(child)
                skills[sid] = trigs
            except Exception:  # pragma: no cover - defensive
                pass
    return skills


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description="Evaluate a skill's trigger cases against its skill.json triggers "
                    "using the runner's exact matching rule.")
    ap.add_argument("skill_dir", type=Path, help="Path to the skill directory (contains skill.json + evals/).")
    ap.add_argument("--registry", type=Path, default=None,
                    help="Path to the AutoDevSkills root to also evaluate cross-skill winner expectations.")
    ap.add_argument("--quiet", action="store_true", help="Only print the summary line and failures.")
    args = ap.parse_args(argv)

    skill_dir: Path = args.skill_dir
    sid, triggers = load_triggers(skill_dir)
    cases_path = skill_dir / "evals" / "trigger-cases.json"
    if not cases_path.is_file():
        print(f"error: no evals/trigger-cases.json in {skill_dir}", file=sys.stderr)
        return 2
    doc = json.loads(cases_path.read_text(encoding="utf-8"))
    cases = doc.get("cases", [])
    if not cases:
        print(f"error: no cases in {cases_path}", file=sys.stderr)
        return 2

    registry = load_registry(args.registry) if args.registry else None

    failures = 0
    trig_pos = trig_neg = 0
    for i, case in enumerate(cases):
        prompt = case["prompt"]
        should = bool(case.get("shouldTrigger", True))
        hits = matched_keywords(triggers, prompt)
        did = len(hits) > 0
        ok = did == should
        if should:
            trig_pos += 1
        else:
            trig_neg += 1

        winner_ok = True
        winner = None
        if registry is not None and case.get("expectedWinner"):
            ranked = rank_skills(registry, prompt)
            winner = ranked[0][0] if ranked else None
            winner_ok = winner == case["expectedWinner"]

        passed = ok and winner_ok
        if not passed:
            failures += 1
        if not passed or not args.quiet:
            status = "PASS" if passed else "FAIL"
            detail = f"trigger={did} (want {should})"
            if hits:
                detail += f" via {hits}"
            if registry is not None and case.get("expectedWinner"):
                detail += f"; winner={winner} (want {case['expectedWinner']})"
            print(f"[{status}] {sid} #{i}: {prompt!r}\n         {detail}")

    total = len(cases)
    print(f"\n{sid}: {total - failures}/{total} cases passed "
          f"({trig_pos} should-trigger, {trig_neg} near-miss)"
          + ("" if registry is None else " [+winner check]"))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())

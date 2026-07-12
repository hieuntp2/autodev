#!/usr/bin/env python3
"""Validate a pet behavior config JSON for the pet-behavior-designer skill.

The behavior config is the single authority that maps normalized events to
bounded reactions (state + animation_id + optional sound_cue_id + priority +
cooldown + interruptibility + memory/personality deltas). This validator
enforces the schema both scripts in this skill agree on. It is deterministic
and uses the Python standard library only.

Exit code is 0 when the config is valid, non-zero when any error is found so
CI / the AutoDev runner can gate on it.

Config schema (abridged)::

    {
      "states":      [ {"id":"idle","baseAnimation":"blink_idle"}, ... ],
      "personality": {"traits":{"playfulness":0.5,...},"min":0.0,"max":1.0,
                      "maxDeltaPerEvent":0.03},
      "memory":      {"maxSummaries":20},
      "randomness":  {"seedable":true},
      "reactions":   [ {"id":"tap_happy","event":"interaction.tap","state":"happy",
                        "animation_id":"happy_reward_sparkle",
                        "sound_cue_id":"happy_reward_chirp",
                        "priority":35,"interruptible":true,"cooldownMs":1500,
                        "minVisibleMs":500,"requiresSensor":null,
                        "sensorFallback":"ignore",
                        "memoryDelta":{"summary":"tapped","affection":1},
                        "personalityDelta":{"playfulness":0.01},"weight":1.0}, ... ]
    }
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")
EVENT_RE = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")
KNOWN_SENSORS = {"accelerometer", "gyroscope", "proximity", "light"}
SENSOR_FALLBACKS = {"ignore", "degrade", "alternate"}


class Report:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []

    def err(self, msg: str) -> None:
        self.errors.append(msg)

    def warn(self, msg: str) -> None:
        self.warnings.append(msg)


def _is_number(v: object) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _is_int(v: object) -> bool:
    return isinstance(v, int) and not isinstance(v, bool)


def validate(cfg: object, rep: Report) -> None:
    if not isinstance(cfg, dict):
        rep.err("top-level config must be a JSON object")
        return

    # ---- states -----------------------------------------------------------
    states = cfg.get("states")
    state_ids: set[str] = set()
    if not isinstance(states, list) or not states:
        rep.err("states: must be a non-empty list of {id, baseAnimation}")
    else:
        for i, st in enumerate(states):
            where = f"states[{i}]"
            if not isinstance(st, dict):
                rep.err(f"{where}: must be an object")
                continue
            sid = st.get("id")
            if not isinstance(sid, str) or not ID_RE.match(sid):
                rep.err(f"{where}.id: must match {ID_RE.pattern!r} (got {sid!r})")
            else:
                if sid in state_ids:
                    rep.err(f"{where}.id: duplicate state id {sid!r}")
                state_ids.add(sid)
            base = st.get("baseAnimation")
            if not isinstance(base, str) or not ID_RE.match(base):
                rep.err(f"{where}.baseAnimation: must match {ID_RE.pattern!r} "
                        f"(got {base!r})")

    # ---- personality ------------------------------------------------------
    pers = cfg.get("personality")
    trait_names: set[str] = set()
    pmin = pmax = max_delta = None
    if not isinstance(pers, dict):
        rep.err("personality: must be an object with traits/min/max/maxDeltaPerEvent")
    else:
        pmin = pers.get("min")
        pmax = pers.get("max")
        max_delta = pers.get("maxDeltaPerEvent")
        if not _is_number(pmin):
            rep.err("personality.min: must be a number")
        if not _is_number(pmax):
            rep.err("personality.max: must be a number")
        if _is_number(pmin) and _is_number(pmax) and pmin >= pmax:
            rep.err(f"personality.min ({pmin}) must be < personality.max ({pmax})")
        if not _is_number(max_delta) or not (0 < max_delta <= 0.5):
            rep.err(f"personality.maxDeltaPerEvent: must be in (0, 0.5] "
                    f"(got {max_delta!r})")
        traits = pers.get("traits")
        if not isinstance(traits, dict) or not traits:
            rep.err("personality.traits: must be a non-empty object of name->value")
        else:
            for name, val in traits.items():
                trait_names.add(name)
                if not ID_RE.match(name):
                    rep.err(f"personality.traits: trait name {name!r} must match "
                            f"{ID_RE.pattern!r}")
                if not _is_number(val):
                    rep.err(f"personality.traits.{name}: must be a number")
                elif _is_number(pmin) and _is_number(pmax) and not (pmin <= val <= pmax):
                    rep.err(f"personality.traits.{name}: value {val} outside "
                            f"[{pmin}, {pmax}]")

    # ---- memory -----------------------------------------------------------
    mem = cfg.get("memory")
    if not isinstance(mem, dict):
        rep.err("memory: must be an object with maxSummaries")
    else:
        ms = mem.get("maxSummaries")
        if not _is_int(ms) or ms < 1:
            rep.err(f"memory.maxSummaries: must be an integer >= 1 (got {ms!r})")

    # ---- randomness -------------------------------------------------------
    rnd = cfg.get("randomness")
    if not isinstance(rnd, dict):
        rep.err("randomness: must be an object with seedable (bool)")
    elif not isinstance(rnd.get("seedable"), bool):
        rep.err("randomness.seedable: must be a boolean")

    # ---- reactions --------------------------------------------------------
    reactions = cfg.get("reactions")
    reaction_ids: set[str] = set()
    referenced_states: set[str] = set()
    if not isinstance(reactions, list) or not reactions:
        rep.err("reactions: must be a non-empty list")
    else:
        for i, rx in enumerate(reactions):
            where = f"reactions[{i}]"
            if not isinstance(rx, dict):
                rep.err(f"{where}: must be an object")
                continue
            rid = rx.get("id")
            if not isinstance(rid, str) or not ID_RE.match(rid):
                rep.err(f"{where}.id: must match {ID_RE.pattern!r} (got {rid!r})")
            else:
                if rid in reaction_ids:
                    rep.err(f"{where}.id: duplicate reaction id {rid!r}")
                reaction_ids.add(rid)
                where = f"reactions[{i}]({rid})"

            ev = rx.get("event")
            if not isinstance(ev, str) or not EVENT_RE.match(ev):
                rep.err(f"{where}.event: must be dotted snake_case matching "
                        f"{EVENT_RE.pattern!r} (got {ev!r})")

            state = rx.get("state")
            if not isinstance(state, str) or not ID_RE.match(state):
                rep.err(f"{where}.state: must match {ID_RE.pattern!r} (got {state!r})")
            else:
                referenced_states.add(state)
                if state_ids and state not in state_ids:
                    rep.err(f"{where}.state: {state!r} is not defined in states[]")

            anim = rx.get("animation_id")
            if not isinstance(anim, str) or not ID_RE.match(anim):
                rep.err(f"{where}.animation_id: required, must match "
                        f"{ID_RE.pattern!r} (got {anim!r})")

            snd = rx.get("sound_cue_id")
            if snd is not None and (not isinstance(snd, str) or not ID_RE.match(snd)):
                rep.err(f"{where}.sound_cue_id: if present must match "
                        f"{ID_RE.pattern!r} (got {snd!r})")

            alt = rx.get("alternateAnimation")
            if alt is not None and (not isinstance(alt, str) or not ID_RE.match(alt)):
                rep.err(f"{where}.alternateAnimation: if present must match "
                        f"{ID_RE.pattern!r} (got {alt!r})")

            prio = rx.get("priority")
            if not _is_int(prio) or not (0 <= prio <= 99):
                rep.err(f"{where}.priority: must be an int in 0..99 (got {prio!r})")

            for fld in ("cooldownMs", "minVisibleMs"):
                v = rx.get(fld)
                if not _is_number(v) or v < 0:
                    rep.err(f"{where}.{fld}: must be a number >= 0 (got {v!r})")

            if not isinstance(rx.get("interruptible"), bool):
                rep.err(f"{where}.interruptible: must be a boolean "
                        f"(got {rx.get('interruptible')!r})")

            w = rx.get("weight")
            if not _is_number(w) or w <= 0:
                rep.err(f"{where}.weight: must be a number > 0 (got {w!r})")

            req = rx.get("requiresSensor")
            if req is not None and req not in KNOWN_SENSORS:
                rep.err(f"{where}.requiresSensor: must be null or one of "
                        f"{sorted(KNOWN_SENSORS)} (got {req!r})")

            fb = rx.get("sensorFallback")
            if fb not in SENSOR_FALLBACKS:
                rep.err(f"{where}.sensorFallback: must be one of "
                        f"{sorted(SENSOR_FALLBACKS)} (got {fb!r})")
            if req is None and fb == "alternate":
                rep.warn(f"{where}: sensorFallback 'alternate' has no effect when "
                         f"requiresSensor is null")
            if req is not None and fb == "alternate" and not rx.get("alternateAnimation"):
                rep.warn(f"{where}: sensorFallback 'alternate' but no "
                         f"alternateAnimation defined; will drop when sensor missing")

            md = rx.get("memoryDelta")
            if md is not None and not isinstance(md, dict):
                rep.err(f"{where}.memoryDelta: if present must be an object")

            pd = rx.get("personalityDelta")
            if pd is not None:
                if not isinstance(pd, dict):
                    rep.err(f"{where}.personalityDelta: if present must be an object")
                else:
                    for tname, dv in pd.items():
                        if trait_names and tname not in trait_names:
                            rep.err(f"{where}.personalityDelta.{tname}: unknown trait "
                                    f"(not in personality.traits)")
                        if not _is_number(dv):
                            rep.err(f"{where}.personalityDelta.{tname}: must be a number")
                        elif _is_number(max_delta) and abs(dv) > max_delta:
                            rep.err(f"{where}.personalityDelta.{tname}: magnitude "
                                    f"{abs(dv)} exceeds maxDeltaPerEvent {max_delta}")

    # ---- cross-field warnings --------------------------------------------
    if state_ids:
        for sid in sorted(state_ids - referenced_states):
            rep.warn(f"state {sid!r} is defined but never referenced by a reaction")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        prog="validate_behavior_config.py",
        description="Validate a pet behavior config JSON against the "
                    "pet-behavior-designer schema.")
    ap.add_argument("CONFIG", type=Path, help="Path to the behavior config JSON.")
    ap.add_argument("--strict", action="store_true",
                    help="Treat warnings as errors (fail on any warning too).")
    args = ap.parse_args(argv)

    path: Path = args.CONFIG
    if not path.is_file():
        print(f"FAIL: config file not found: {path}", file=sys.stderr)
        return 2
    try:
        cfg = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"FAIL: invalid JSON in {path}: {exc}", file=sys.stderr)
        return 2

    rep = Report()
    validate(cfg, rep)

    for w in rep.warnings:
        print(f"  WARN  {w}")
    for e in rep.errors:
        print(f"  ERROR {e}")

    n_err = len(rep.errors)
    n_warn = len(rep.warnings)
    strict_fail = args.strict and n_warn > 0
    if n_err == 0 and not strict_fail:
        print(f"PASS: {path.name} is a valid behavior config "
              f"({n_warn} warning(s)).")
        return 0
    print(f"FAIL: {path.name} has {n_err} error(s), {n_warn} warning(s)"
          + (" [--strict: warnings fail]" if strict_fail else "") + ".")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

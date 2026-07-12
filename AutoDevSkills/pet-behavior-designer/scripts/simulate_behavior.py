#!/usr/bin/env python3
"""Deterministic behavior simulator for the pet-behavior-designer skill.

Feeds a sequence of normalized events through a behavior config and reports,
step by step, which reaction fires (or why it is suppressed), how the running
animation is preempted, and how memory + personality evolve. Given the same
(config, events, seed) it prints identical output, so it doubles as a testable
behavior case.

Stdlib only. See validate_behavior_config.py for the config schema.

Events file: a JSON list, e.g.::

    [ {"event":"interaction.tap","timeMs":0},
      {"event":"sensor.shake","timeMs":200,"sensor":"accelerometer"},
      {"event":"interaction.tap","timeMs":300} ]

Algorithm (deterministic):
  1. Track currentTimeMs, the running animation, per-reaction lastFiredMs,
     a copy of personality traits, a bounded FIFO memory buffer, and a seeded
     PRNG (random.Random(seed)).
  2. Per event: gather reactions whose event matches; gate on sensor
     availability (requiresSensor null -> ok; unavailable -> apply
     sensorFallback ignore|degrade|alternate).
  3. Drop candidates still inside cooldownMs.
  4. Pick highest priority; break ties by seeded weighted-random over weight.
  5. Preempt the running animation only if nothing is running, or it is
     interruptible and the newcomer has strictly higher priority, or its
     minVisibleMs has elapsed. Otherwise suppress the event.
  6. On play: set running anim + lastFired, append bounded memory summary,
     nudge personality (clamped + magnitude-capped).
"""
from __future__ import annotations

import argparse
import json
import random
import sys
from pathlib import Path

KNOWN_SENSORS = ["accelerometer", "gyroscope", "proximity", "light"]


def clamp(v: float, lo: float, hi: float) -> float:
    return lo if v < lo else hi if v > hi else v


class Simulator:
    def __init__(self, cfg: dict, seed: int, available: set[str]) -> None:
        self.cfg = cfg
        self.seed = seed
        self.available = available
        self.prng = random.Random(seed)

        pers = cfg.get("personality", {})
        self.pmin = float(pers.get("min", 0.0))
        self.pmax = float(pers.get("max", 1.0))
        self.max_delta = float(pers.get("maxDeltaPerEvent", 0.03))
        # deterministic ordering of traits
        self.traits: dict[str, float] = {
            k: float(v) for k, v in sorted(pers.get("traits", {}).items())
        }

        self.max_summaries = int(cfg.get("memory", {}).get("maxSummaries", 20))
        self.memory: list[str] = []

        self.reactions: list[dict] = list(cfg.get("reactions", []))
        self.last_fired: dict[str, int] = {}
        self.running: dict | None = None  # {rid, animation_id, startedMs, priority,
        #                                    minVisibleMs, interruptible}
        self.steps: list[dict] = []

    # -- helpers -----------------------------------------------------------
    def _candidates(self, event: str) -> tuple[list[dict], list[str]]:
        """Return (candidates, notes) after event-match + sensor gating."""
        cands: list[dict] = []
        notes: list[str] = []
        for rx in self.reactions:
            if rx.get("event") != event:
                continue
            req = rx.get("requiresSensor")
            if req is None or req in self.available:
                cands.append({"rx": rx, "degraded": False,
                              "animation_id": rx.get("animation_id")})
                continue
            # sensor required but unavailable -> fallback
            fb = rx.get("sensorFallback", "ignore")
            if fb == "ignore":
                notes.append(f"{rx.get('id')} dropped (sensor {req} unavailable, "
                             f"fallback=ignore)")
            elif fb == "degrade":
                cands.append({"rx": rx, "degraded": True,
                              "animation_id": rx.get("animation_id")})
                notes.append(f"{rx.get('id')} kept DEGRADED (sensor {req} unavailable)")
            elif fb == "alternate":
                alt = rx.get("alternateAnimation")
                if alt:
                    cands.append({"rx": rx, "degraded": True, "animation_id": alt})
                    notes.append(f"{rx.get('id')} using ALTERNATE animation {alt} "
                                 f"(sensor {req} unavailable)")
                else:
                    notes.append(f"{rx.get('id')} dropped (fallback=alternate but no "
                                 f"alternateAnimation defined)")
        return cands, notes

    def _pick(self, cands: list[dict]) -> dict:
        """Highest priority; ties broken by seeded weighted-random over weight."""
        top = max(c["rx"]["priority"] for c in cands)
        tied = sorted((c for c in cands if c["rx"]["priority"] == top),
                      key=lambda c: c["rx"]["id"])  # stable order for determinism
        if len(tied) == 1:
            return tied[0]
        total = sum(float(c["rx"].get("weight", 1.0)) for c in tied)
        r = self.prng.random() * total
        acc = 0.0
        for c in tied:
            acc += float(c["rx"].get("weight", 1.0))
            if r < acc:
                return c
        return tied[-1]

    def _can_preempt(self, now: int, new_prio: int) -> tuple[bool, str]:
        run = self.running
        if run is None:
            return True, "no animation running"
        elapsed = now - run["startedMs"]
        if elapsed >= run["minVisibleMs"]:
            return True, f"minVisibleMs elapsed ({elapsed}ms >= {run['minVisibleMs']}ms)"
        if run["interruptible"] and new_prio > run["priority"]:
            return True, (f"running {run['animation_id']} interruptible and prio "
                          f"{new_prio} > {run['priority']}")
        reason = (f"running {run['animation_id']} "
                  + ("interruptible but prio not higher"
                     if run["interruptible"] else "not interruptible")
                  + f", minVisibleMs not elapsed ({elapsed}ms < {run['minVisibleMs']}ms)")
        return False, reason

    def _apply_deltas(self, rx: dict) -> tuple[list[str], dict[str, float]]:
        mem_notes: list[str] = []
        md = rx.get("memoryDelta") or {}
        summary = md.get("summary")
        if summary is None and md:
            summary = rx.get("id")
        if summary is not None:
            self.memory.append(str(summary))
            while len(self.memory) > self.max_summaries:
                self.memory.pop(0)  # FIFO
            mem_notes.append(str(summary))

        applied: dict[str, float] = {}
        pd = rx.get("personalityDelta") or {}
        for tname, dv in sorted(pd.items()):
            if tname not in self.traits:
                continue
            capped = clamp(float(dv), -self.max_delta, self.max_delta)
            before = self.traits[tname]
            after = clamp(before + capped, self.pmin, self.pmax)
            self.traits[tname] = after
            applied[tname] = round(after - before, 6)
        return mem_notes, applied

    # -- main loop ---------------------------------------------------------
    def run(self, events: list[dict]) -> None:
        for ev in events:
            event = ev.get("event")
            now = int(ev.get("timeMs", 0))
            step: dict = {
                "timeMs": now, "event": event, "sensor": ev.get("sensor"),
                "reaction": None, "state": None, "animation_id": None,
                "sound_cue_id": None, "priority": None, "degraded": False,
                "cooldown": None, "memoryDelta": [], "personalityDelta": {},
                "status": None, "reason": None, "notes": [],
            }

            cands, notes = self._candidates(event)
            step["notes"] = notes
            if not cands:
                step["status"] = "no_reaction"
                step["reason"] = (f"no rule for event {event!r}"
                                  if not notes else "all candidates gated out by sensor")
                self.steps.append(step)
                continue

            # cooldown gating
            survivors = []
            on_cd = []
            for c in cands:
                rid = c["rx"]["id"]
                cd = float(c["rx"].get("cooldownMs", 0))
                last = self.last_fired.get(rid)
                if last is not None and (now - last) < cd:
                    on_cd.append(f"{rid} ({now - last}ms < {int(cd)}ms)")
                else:
                    survivors.append(c)
            step["cooldown"] = ("ok" if not on_cd
                                else f"{len(on_cd)} on cooldown: " + ", ".join(on_cd))
            if not survivors:
                step["status"] = "suppressed"
                step["reason"] = "all candidates on cooldown"
                self.steps.append(step)
                continue

            chosen = self._pick(survivors)
            rx = chosen["rx"]
            new_prio = int(rx["priority"])

            can, why = self._can_preempt(now, new_prio)
            step["reaction"] = rx["id"]
            step["state"] = rx.get("state")
            step["animation_id"] = chosen["animation_id"]
            step["sound_cue_id"] = rx.get("sound_cue_id")
            step["priority"] = new_prio
            step["degraded"] = chosen["degraded"]
            if not can:
                step["status"] = "suppressed"
                step["reason"] = why
                # a suppressed reaction does not fire: no lastFired/memory/personality
                step["reaction"] = None
                step["state"] = None
                step["animation_id"] = None
                step["sound_cue_id"] = None
                step["priority"] = None
                self.steps.append(step)
                continue

            # fire
            self.last_fired[rx["id"]] = now
            self.running = {
                "rid": rx["id"], "animation_id": chosen["animation_id"],
                "startedMs": now, "priority": new_prio,
                "minVisibleMs": float(rx.get("minVisibleMs", 0)),
                "interruptible": bool(rx.get("interruptible", True)),
            }
            mem_notes, applied = self._apply_deltas(rx)
            step["status"] = "fired"
            step["reason"] = why
            step["memoryDelta"] = mem_notes
            step["personalityDelta"] = applied
            self.steps.append(step)


def _fmt_step(s: dict) -> str:
    head = f"t={s['timeMs']}ms  event={s['event']}"
    if s["sensor"]:
        head += f"[{s['sensor']}]"
    if s["status"] in ("suppressed", "no_reaction"):
        tag = "suppressed" if s["status"] == "suppressed" else "no reaction"
        line = f"{head}  -> ({tag}: {s['reason']})"
        if s["cooldown"] and s["cooldown"] != "ok":
            line += f"  [cooldown: {s['cooldown']}]"
        for n in s["notes"]:
            line += f"\n            note: {n}"
        return line
    anim = s["animation_id"] or "-"
    snd = s["sound_cue_id"] or "-"
    deg = " DEGRADED" if s["degraded"] else ""
    line = (f"{head}  -> {s['reaction']}{deg}  state={s['state']}  anim={anim}  "
            f"sound={snd}  prio={s['priority']}  cooldown={s['cooldown']}")
    if s["memoryDelta"]:
        line += "  mem+=" + ",".join(repr(m) for m in s["memoryDelta"])
    if s["personalityDelta"]:
        parts = ",".join(f"{k}:{v:+.3f}" for k, v in s["personalityDelta"].items())
        line += f"  pers{{{parts}}}"
    line += f"\n            ({s['reason']})"
    for n in s["notes"]:
        line += f"\n            note: {n}"
    return line


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        prog="simulate_behavior.py",
        description="Deterministically simulate pet behavior over an event "
                    "sequence using a behavior config.")
    ap.add_argument("--config", required=True, type=Path, help="Behavior config JSON.")
    ap.add_argument("--events", required=True, type=Path,
                    help="JSON list of events [{event,timeMs,sensor?}, ...].")
    ap.add_argument("--seed", type=int, default=0,
                    help="Seed for the tie-break PRNG (default 0). Same seed -> "
                         "identical output.")
    ap.add_argument("--available-sensors", default=None,
                    help="Comma-separated available sensors "
                         f"(subset of {KNOWN_SENSORS}). Default: all available.")
    ap.add_argument("--json", action="store_true", dest="as_json",
                    help="Emit machine-readable JSON instead of text.")
    args = ap.parse_args(argv)

    if not args.config.is_file():
        print(f"error: config not found: {args.config}", file=sys.stderr)
        return 2
    if not args.events.is_file():
        print(f"error: events not found: {args.events}", file=sys.stderr)
        return 2
    try:
        cfg = json.loads(args.config.read_text(encoding="utf-8"))
        events = json.loads(args.events.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"error: invalid JSON: {exc}", file=sys.stderr)
        return 2
    if not isinstance(cfg, dict) or not isinstance(cfg.get("reactions"), list):
        print("error: config missing a reactions[] list; run "
              "validate_behavior_config.py first", file=sys.stderr)
        return 2
    if not isinstance(events, list):
        print("error: events file must be a JSON list", file=sys.stderr)
        return 2

    if args.available_sensors is None:
        available = set(KNOWN_SENSORS)
    else:
        available = {s.strip() for s in args.available_sensors.split(",") if s.strip()}
        unknown = available - set(KNOWN_SENSORS)
        if unknown:
            print(f"error: unknown sensor(s) in --available-sensors: "
                  f"{sorted(unknown)}; known: {KNOWN_SENSORS}", file=sys.stderr)
            return 2

    sim = Simulator(cfg, args.seed, available)
    sim.run(events)

    if args.as_json:
        out = {
            "seed": args.seed,
            "availableSensors": sorted(available),
            "steps": sim.steps,
            "finalPersonality": sim.traits,
            "memory": sim.memory,
        }
        print(json.dumps(out, indent=2, sort_keys=True))
        return 0

    print(f"# behavior simulation  seed={args.seed}  "
          f"availableSensors={sorted(available)}")
    print(f"# {len(events)} event(s), {len(sim.reactions)} reaction rule(s)\n")
    for s in sim.steps:
        print(_fmt_step(s))
    print("\n--- final personality traits ---")
    for k, v in sim.traits.items():
        print(f"  {k}: {v:.3f}")
    print(f"\n--- memory buffer (FIFO, max {sim.max_summaries}) ---")
    if sim.memory:
        for i, m in enumerate(sim.memory):
            print(f"  {i}: {m}")
    else:
        print("  (empty)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

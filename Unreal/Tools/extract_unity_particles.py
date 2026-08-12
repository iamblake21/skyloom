"""Extracts Unity ParticleSystem settings so they can be rebuilt in Niagara.

The scene converter creates actors for GameObjects that draw something it knows
how to draw. A `ParticleSystem` is not one of those, so three of the opening's
effects — the star streaks, the rift debris and the cockpit sparks — were never
created at all, and the director reported three shots that would play to an
empty screen.

Rebuilding a Unity particle system as a Niagara system is authoring work, not a
mechanical translation: the two engines do not describe emission the same way.
What *is* mechanical is recovering the numbers, and that is what this does. The
report it writes is the specification whoever authors the Niagara systems works
from, so nothing has to be guessed or eyeballed from the original.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

PROJECT_ROOT = Path(__file__).resolve().parents[1]
REPORT_PATH = PROJECT_ROOT / "Migration" / "unity_particle_extract_report.json"

# Unity serialises a scalar-or-curve as a small block; `scalar` is the constant
# case and the only one these effects use.
SCALAR_RE = re.compile(r"scalar:\s*(-?[\d.eE+-]+)")


def _block(text: str, start: int) -> str:
    """The YAML document beginning at `start`, up to the next one."""
    end = text.find("\n--- ", start)
    return text[start:] if end < 0 else text[start:end]


def _scalar(block: str, key: str, default: float = 0.0) -> float:
    """Reads `key:`'s nested scalar, which is how Unity stores a constant."""
    match = re.search(rf"(?m)^\s*{re.escape(key)}:\s*$", block)
    if not match:
        return default
    tail = block[match.end(): match.end() + 400]
    found = SCALAR_RE.search(tail)
    return float(found.group(1)) if found else default


def _named_objects(text: str) -> dict[str, int]:
    """GameObject file ids by name."""
    names: dict[str, int] = {}
    for match in re.finditer(
        r"(?m)^--- !u!1 &(\d+)\nGameObject:(?:(?!^--- )[\s\S])*?m_Name: (.+)$", text
    ):
        names[match.group(2).strip()] = int(match.group(1))
    return names


def extract(scene: Path) -> list[dict[str, Any]]:
    text = scene.read_text(encoding="utf-8", errors="replace")
    owners = {file_id: name for name, file_id in _named_objects(text).items()}

    systems: list[dict[str, Any]] = []
    for match in re.finditer(r"(?m)^--- !u!198 &\d+\nParticleSystem:", text):
        block = _block(text, match.start())
        owner = re.search(r"m_GameObject: \{fileID: (\d+)\}", block)
        owner_id = int(owner.group(1)) if owner else 0

        systems.append(
            {
                "actor": owners.get(owner_id, f"<unnamed {owner_id}>"),
                "lengthSeconds": float(
                    (re.search(r"(?m)^  lengthInSec: ([\d.eE+-]+)", block) or [0, 0])[1]
                    if re.search(r"(?m)^  lengthInSec: ([\d.eE+-]+)", block) else 0.0),
                "looping": bool(int((re.search(r"(?m)^  looping: (\d)", block) or [0, "0"])[1])),
                "playOnAwake": bool(
                    int((re.search(r"(?m)^  playOnAwake: (\d)", block) or [0, "0"])[1])),
                # The opening runs on unscaled time so its rhythm survives a
                # pause; a Niagara rebuild has to do the same or the klaxon and
                # the streaks drift apart from the shot they belong to.
                "useUnscaledTime": bool(
                    int((re.search(r"(?m)^  useUnscaledTime: (\d)", block) or [0, "0"])[1])),
                "startLifetimeSeconds": _scalar(block, "startLifetime"),
                "startSpeed": _scalar(block, "startSpeed"),
                "startSize": _scalar(block, "startSize"),
                "gravityModifier": _scalar(block, "gravityModifier"),
                "maxParticles": int(
                    (re.search(r"(?m)^\s*maxNumParticles: (\d+)", block) or [0, "0"])[1]),
            }
        )
    return systems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--unity-root", type=Path, default=PROJECT_ROOT.parent / "Game")
    arguments = parser.parse_args()
    unity_root = arguments.unity_root.resolve()

    scenes: list[dict[str, Any]] = []
    for scene in sorted((unity_root / "Assets" / "_Project").rglob("*.unity")):
        systems = extract(scene)
        if systems:
            scenes.append(
                {
                    "scene": scene.relative_to(unity_root).as_posix(),
                    "systems": systems,
                }
            )

    total = sum(len(item["systems"]) for item in scenes)
    report = {
        "schema": 1,
        "note": (
            "Unity ParticleSystem settings recovered for rebuilding as Niagara. "
            "The scene converter cannot create these actors, so every system "
            "listed here is a shot with nothing in it until its Niagara system "
            "is authored and placed under the actor name given."
        ),
        "scenes": len(scenes),
        "systems": total,
        "results": scenes,
    }
    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    for entry in scenes:
        for system in entry["systems"]:
            print(
                f"{system['actor']}: lifetime={system['startLifetimeSeconds']}s "
                f"speed={system['startSpeed']} size={system['startSize']} "
                f"max={system['maxParticles']} looping={system['looping']}")
    print(f"Unity particle systems: {total} across {len(scenes)} scene(s) -> {REPORT_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""Check an imported Landscape against the Unity heightmap it came from.

The import step reports success when the engine call returns, which says nothing
about whether the ground is in the right place or the right shape. This reads
the heights the landscape actually stores and compares them, sample by sample,
with the raw file the exporter wrote, then checks the actor transform against
the world positions Unity would have produced.

Run with UnrealEditor-Cmd and the PythonScriptPlugin. Never writes to Unity.
"""

from __future__ import annotations

import json
import os
import re
import struct
import traceback
from pathlib import Path

import unreal

MAP_ROOT = "/Game/Maps"

# Every sampled row is compared in full; this many rows are spread evenly over
# the landscape, including both borders.
SAMPLED_ROWS = 24

# Landscape scale and physical footprint are verified independently. The latter
# catches exporters that agree with their own report while padding the terrain
# to a larger world-space size.
TOLERANCE_UNREAL_UNITS = 0.05


def _log(message: str) -> None:
    unreal.log(f"[CML Terrain Verify] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Terrain Verify] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", str(value).strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Object"
    return f"A_{value}" if value[0].isdigit() else value


def _project_dir() -> Path:
    return Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))


def _find_landscape(world, label: str):
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape):
        if actor.get_actor_label() == label:
            return actor
    return None


def _verify_one(terrain: dict, placement: dict, terrain_dir: Path) -> dict:
    landscape_info = terrain["landscape"]
    resolution = landscape_info["resolution"]

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    map_override = os.environ.get("CML_TERRAIN_TARGET_MAP", "").strip()
    map_path = map_override or f"{MAP_ROOT}/{_sanitize(Path(placement['scene']).stem)}"
    if not level_editor.load_level(map_path):
        raise RuntimeError(f"Unable to open level {map_path}")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()

    actor = _find_landscape(world, placement["actorName"])
    if actor is None:
        raise RuntimeError(f"No landscape labelled {placement['actorName']!r} in {map_path}")

    problems: list[str] = []

    # --- transform ---
    expected_location = placement["unrealLocation"]
    location = actor.get_actor_location()
    for axis in ("x", "y", "z"):
        difference = abs(getattr(location, axis) - expected_location[axis])
        if difference > TOLERANCE_UNREAL_UNITS:
            problems.append(
                f"location {axis} is {getattr(location, axis)}, expected {expected_location[axis]}"
            )
    expected_scale = landscape_info["drawScale"]
    scale = actor.get_actor_scale3d()
    for axis in ("x", "y", "z"):
        difference = abs(getattr(scale, axis) - expected_scale[axis])
        if difference > 1e-4:
            problems.append(
                f"scale {axis} is {getattr(scale, axis)}, expected {expected_scale[axis]}"
            )

    target_quads = resolution - 1
    unity_size = terrain["unity"]["size"]
    expected_extent = {
        "x": unity_size["z"] * 100.0,
        "y": unity_size["x"] * 100.0,
    }
    actual_extent = {
        "x": scale.x * target_quads,
        "y": scale.y * target_quads,
    }
    for axis in ("x", "y"):
        difference = abs(actual_extent[axis] - expected_extent[axis])
        if difference > TOLERANCE_UNREAL_UNITS:
            problems.append(
                f"world extent {axis} is {actual_extent[axis]} cm, "
                f"expected Unity footprint {expected_extent[axis]} cm"
            )

    # --- heights ---
    expected = (terrain_dir / landscape_info["heightmapFile"]).read_bytes()
    if len(expected) != resolution * resolution * 2:
        raise RuntimeError("the exported heightmap does not match its declared resolution")

    rows = sorted({round(index * (resolution - 1) / (SAMPLED_ROWS - 1)) for index in range(SAMPLED_ROWS)})
    compared = 0
    mismatches = 0
    worst = 0
    for row in rows:
        stored = unreal.CMLLandscapeImportLibrary.read_landscape_height_row(actor, row, resolution)
        if len(stored) != resolution:
            problems.append(f"row {row} read back {len(stored)} samples, expected {resolution}")
            continue
        offset = row * resolution * 2
        wanted = struct.unpack_from(f"<{resolution}H", expected, offset)
        row_mismatches = 0
        for index, value in enumerate(wanted):
            compared += 1
            difference = abs(stored[index] - value)
            if difference:
                mismatches += 1
                row_mismatches += 1
                worst = max(worst, difference)
        if row_mismatches:
            sample = ", ".join(
                f"[{index}] got {stored[index]} want {wanted[index]}"
                for index in range(min(4, resolution))
            )
            problems.append(
                f"row {row}: {row_mismatches}/{resolution} differ ({sample})"
            )

    if mismatches:
        problems.append(
            f"{mismatches} of {compared} sampled heights differ (worst {worst} raw units)"
        )

    unity = terrain["unity"]
    metres_per_raw = unity["size"]["y"] / unity["maxRawHeight"]
    return {
        "status": "verified" if not problems else "mismatch",
        "terrain": terrain["name"],
        "level": map_path,
        "actor": placement["actorName"],
        "heightsCompared": compared,
        "heightMismatches": mismatches,
        "worstHeightErrorMetres": worst * metres_per_raw,
        "worldExtentCentimetres": actual_extent,
        "expectedWorldExtentCentimetres": expected_extent,
        "problems": problems,
    }


def main() -> int:
    project_dir = _project_dir()
    extract = json.loads(
        (project_dir / "Migration" / "unity_terrain_extract_report.json").read_text("utf-8")
    )

    results: list[dict] = []
    for terrain in extract["terrains"]:
        terrain_dir = project_dir / "Migration" / "UnityTerrain" / terrain["name"]
        for placement in terrain["placements"]:
            try:
                results.append(_verify_one(terrain, placement, terrain_dir))
                outcome = results[-1]
                _log(
                    f"{outcome['terrain']}: {outcome['status']}, "
                    f"{outcome['heightsCompared']} heights compared, "
                    f"{outcome['heightMismatches']} mismatched"
                )
                for problem in outcome["problems"]:
                    _error(f"  {problem}")
            except Exception as exception:  # noqa: BLE001 - reported per placement
                _error(f"{terrain['name']}: {exception}")
                _error(traceback.format_exc())
                results.append(
                    {"status": "failed", "terrain": terrain["name"], "error": str(exception)}
                )

    failed = sum(item["status"] != "verified" for item in results)
    report = {
        "schema": 1,
        "checked": len(results),
        "verified": len(results) - failed,
        "failed": failed,
        "results": results,
    }
    report_path = project_dir / "Migration" / "unity_terrain_verify_report.json"
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)

    _log(f"Complete: verified={report['verified']}, failed={report['failed']}")
    return 0 if failed == 0 and results else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_TERRAIN_VERIFY_FAILED code={_exit_code}")
else:
    _log("CML_TERRAIN_VERIFY_SUCCEEDED")

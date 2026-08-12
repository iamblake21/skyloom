"""Build the playable Starter Island map from the Unity review scene.

The Unity source scene intentionally contains two complete Terrain studies.  A
literal scene conversion therefore produces two distant islands in one Unreal
map, a huge empty union bounds and an unreliable player view.  This pass keeps
the source review map intact, duplicates it, retains the TerrainTop composition
and removes the comparison composition by spatial ownership.

Only the destination map is saved.  Re-running the pass backs up and replaces
the previous destination, so the operation is deterministic and recoverable.
"""

from __future__ import annotations

import json
import math
import os
import shutil
import traceback
from datetime import datetime
from pathlib import Path

import unreal


SOURCE_MAP = os.environ.get(
    "CML_PLAYABLE_SOURCE_MAP", "/Game/Maps/A_91_StarterIsland_Terrain_Review"
).strip()
DESTINATION_MAP = os.environ.get(
    "CML_PLAYABLE_DESTINATION_MAP", "/Game/Maps/A_10_StarterIsland"
).strip()
PRIMARY_LANDSCAPE_LABEL = "TerrainTop"
PLAYER_START_LOCATION = unreal.Vector(-19531.25, -24750.0, 2765.0)
PLAYER_START_ROTATION = unreal.Rotator(pitch=0.0, yaw=47.0, roll=0.0)

# These actors describe the whole world rather than one of the two review
# compositions.  Their transforms are not meaningful for spatial ownership.
GLOBAL_ACTOR_CLASSES = {
    "DirectionalLight",
    "SkyLight",
    "SkyAtmosphere",
    "ExponentialHeightFog",
    "VolumetricCloud",
    "PostProcessVolume",
    "PlayerStart",
    "CameraActor",
    "CineCameraActor",
    "CMLSimulationSubsystem",
}
GLOBAL_LABEL_FRAGMENTS = (
    "env_sun",
    "skylight",
    "sky_atmosphere",
    "skyatmosphere",
    "height_fog",
    "heightfog",
    "postprocess",
    "globalcolorgrading",
    "measuredstylizeddaylight",
)


def _log(message: str) -> None:
    unreal.log(f"[CML Playable Map] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Playable Map] {message}")


def _project_dir() -> Path:
    return Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))


def _asset_file(project_dir: Path, asset_path: str) -> Path:
    return project_dir / "Content" / (asset_path.removeprefix("/Game/") + ".umap")


def _label(actor) -> str:
    try:
        return actor.get_actor_label()
    except Exception:
        return actor.get_name()


def _class_name(actor) -> str:
    return actor.get_class().get_name()


def _vector(value) -> list[float]:
    return [float(value.x), float(value.y), float(value.z)]


def _backup_existing_destination(project_dir: Path) -> str:
    destination_file = _asset_file(project_dir, DESTINATION_MAP)
    if not destination_file.is_file():
        return ""
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_dir = project_dir / "Saved" / "MigrationBackups" / f"PlayableMap-{stamp}"
    backup_dir.mkdir(parents=True, exist_ok=False)
    backup = backup_dir / destination_file.name
    shutil.copy2(destination_file, backup)
    return str(backup)


def _bounds(actor):
    origin, extent = actor.get_actor_bounds(False, True)
    return origin, extent


def _inside_xy(location, origin, extent) -> bool:
    return (
        abs(float(location.x) - float(origin.x)) <= float(extent.x)
        and abs(float(location.y) - float(origin.y)) <= float(extent.y)
    )


def _normalised_distance_sq(location, origin, extent) -> float:
    ex = max(abs(float(extent.x)), 1.0)
    ey = max(abs(float(extent.y)), 1.0)
    dx = (float(location.x) - float(origin.x)) / ex
    dy = (float(location.y) - float(origin.y)) / ey
    return dx * dx + dy * dy


def _is_global(actor) -> bool:
    class_name = _class_name(actor)
    if class_name in GLOBAL_ACTOR_CLASSES:
        return True
    lowered = _label(actor).lower().replace(" ", "_")
    return any(fragment in lowered for fragment in GLOBAL_LABEL_FRAGMENTS)


def _choose_landscapes(world):
    landscapes = list(unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape))
    if len(landscapes) == 1 and _label(landscapes[0]) == PRIMARY_LANDSCAPE_LABEL:
        return landscapes[0], None
    if len(landscapes) != 2:
        labels = [_label(actor) for actor in landscapes]
        raise RuntimeError(f"Expected exactly two review Landscapes, found {labels}")
    primary = next(
        (actor for actor in landscapes if _label(actor) == PRIMARY_LANDSCAPE_LABEL), None
    )
    if primary is None:
        raise RuntimeError(f"Missing primary Landscape {PRIMARY_LANDSCAPE_LABEL}")
    reference = next(actor for actor in landscapes if actor != primary)
    return primary, reference


def _should_remove(actor, primary, reference, primary_bounds, reference_bounds) -> bool:
    if actor == primary:
        return False
    if reference is not None and actor == reference:
        return True
    if _is_global(actor):
        return False

    location = actor.get_actor_location()
    primary_origin, primary_extent = primary_bounds
    if reference is None or reference_bounds is None:
        return False
    reference_origin, reference_extent = reference_bounds
    in_primary = _inside_xy(location, primary_origin, primary_extent)
    in_reference = _inside_xy(location, reference_origin, reference_extent)
    if in_primary and not in_reference:
        return False
    if in_reference and not in_primary:
        return True

    # Actors outside, or in the tiny overlap between the two axis-aligned
    # bounds, belong to whichever source Terrain is closer in its own scale.
    primary_distance = _normalised_distance_sq(location, primary_origin, primary_extent)
    reference_distance = _normalised_distance_sq(location, reference_origin, reference_extent)
    return reference_distance < primary_distance


def _configure_player_start(world, actor_subsystem) -> dict:
    starts = list(unreal.GameplayStatics.get_all_actors_of_class(world, unreal.PlayerStart))
    if not starts:
        start = actor_subsystem.spawn_actor_from_class(
            unreal.PlayerStart, PLAYER_START_LOCATION, PLAYER_START_ROTATION
        )
        if start is None:
            raise RuntimeError("Could not create CML_PlayerStart")
        start.set_actor_label("CML_PlayerStart")
        starts = [start]

    start = starts[0]
    for duplicate in starts[1:]:
        actor_subsystem.destroy_actor(duplicate)
    start.set_actor_location(PLAYER_START_LOCATION, False, False)
    start.set_actor_rotation(PLAYER_START_ROTATION, False)
    start.set_actor_label("CML_PlayerStart")
    return {
        "label": _label(start),
        "location": _vector(start.get_actor_location()),
        "rotation": [
            float(start.get_actor_rotation().pitch),
            float(start.get_actor_rotation().yaw),
            float(start.get_actor_rotation().roll),
        ],
        "duplicatesRemoved": max(0, len(starts) - 1),
    }


def main() -> int:
    project_dir = _project_dir()
    if not unreal.EditorAssetLibrary.does_asset_exist(SOURCE_MAP):
        raise RuntimeError(f"Source review map does not exist: {SOURCE_MAP}")

    destination_exists = unreal.EditorAssetLibrary.does_asset_exist(DESTINATION_MAP)
    force_rebuild = os.environ.get("CML_REBUILD_PLAYABLE_MAP", "").strip() == "1"
    backup = ""
    if destination_exists and force_rebuild:
        backup = _backup_existing_destination(project_dir)
        if not unreal.EditorAssetLibrary.delete_asset(DESTINATION_MAP):
            raise RuntimeError(f"Could not replace existing {DESTINATION_MAP}")
        destination_exists = False

    # UE 5.8 retains a standalone reference to a duplicated UWorld until the
    # process exits. Loading that same world immediately afterwards trips the
    # editor's World Memory Leaks fatal check. Make duplication an explicit
    # first stage and let the next commandlet process perform the spatial pass.
    if not destination_exists:
        duplicated = unreal.EditorAssetLibrary.duplicate_asset(SOURCE_MAP, DESTINATION_MAP)
        if not duplicated:
            raise RuntimeError(f"Could not duplicate {SOURCE_MAP} to {DESTINATION_MAP}")
        if not unreal.EditorAssetLibrary.save_asset(DESTINATION_MAP, only_if_is_dirty=False):
            raise RuntimeError(f"Could not save duplicated {DESTINATION_MAP}")
        report = {
            "schema": 1,
            "stage": "duplicated_pending_spatial_pass",
            "sourceMap": SOURCE_MAP,
            "destinationMap": DESTINATION_MAP,
            "backup": backup,
        }
        output = project_dir / "Migration" / "playable_map_build_report.json"
        temporary = output.with_suffix(".json.tmp")
        temporary.write_text(
            json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
        )
        temporary.replace(output)
        _log(f"Duplicated {SOURCE_MAP} to {DESTINATION_MAP}; run again for spatial pass")
        return 0

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(DESTINATION_MAP):
        raise RuntimeError(f"Could not load {DESTINATION_MAP}")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)

    primary, reference = _choose_landscapes(world)
    primary_bounds = _bounds(primary)
    reference_bounds = _bounds(reference) if reference is not None else None
    reference_info = {
        "label": _label(reference) if reference is not None else "",
        "origin": _vector(reference_bounds[0]) if reference_bounds is not None else [],
        "extent": _vector(reference_bounds[1]) if reference_bounds is not None else [],
    }
    actors_before = list(actor_subsystem.get_all_level_actors())
    removed: list[dict] = []
    kept: list[str] = []
    to_remove = []
    for actor in actors_before:
        if _should_remove(actor, primary, reference, primary_bounds, reference_bounds):
            to_remove.append(actor)
            removed.append(
                {
                    "label": _label(actor),
                    "class": _class_name(actor),
                    "location": _vector(actor.get_actor_location()),
                }
            )
        else:
            kept.append(_label(actor))
    for actor in to_remove:
        if not actor_subsystem.destroy_actor(actor):
            raise RuntimeError(f"Could not remove comparison actor {_label(actor)}")

    player_start = _configure_player_start(world, actor_subsystem)
    remaining_landscapes = list(
        unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape)
    )
    if len(remaining_landscapes) != 1 or _label(remaining_landscapes[0]) != PRIMARY_LANDSCAPE_LABEL:
        raise RuntimeError(
            "Playable map must contain only TerrainTop; found "
            + str([_label(actor) for actor in remaining_landscapes])
        )

    actors_after = list(actor_subsystem.get_all_level_actors())
    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {DESTINATION_MAP}")

    report = {
        "schema": 1,
        "stage": "complete",
        "sourceMap": SOURCE_MAP,
        "destinationMap": DESTINATION_MAP,
        "backup": backup,
        "primaryLandscape": {
            "label": _label(primary),
            "origin": _vector(primary_bounds[0]),
            "extent": _vector(primary_bounds[1]),
        },
        "referenceLandscape": reference_info,
        "actorCountBefore": len(actors_before),
        "actorCountAfter": len(actors_after),
        "removedCount": len(removed),
        "removedByClass": {},
        "removedActors": removed,
        "playerStart": player_start,
    }
    for item in removed:
        class_name = item["class"]
        report["removedByClass"][class_name] = report["removedByClass"].get(class_name, 0) + 1

    output = project_dir / "Migration" / "playable_map_build_report.json"
    temporary = output.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(output)
    _log(
        f"Built {DESTINATION_MAP}: actors {len(actors_before)} -> {len(actors_after)}, "
        f"removed={len(removed)}, player={player_start['location']}"
    )
    return 0


try:
    _exit_code = main()
except Exception:  # noqa: BLE001 - commandlet must emit a complete diagnostic.
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_PLAYABLE_MAP_FAILED code={_exit_code}")
else:
    _log("CML_PLAYABLE_MAP_SUCCEEDED")

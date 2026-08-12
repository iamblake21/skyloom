"""Finalize migrated maps without rebuilding their Landscapes or actor layout.

This pass repairs model material bindings which Unity stores in model-importer
metadata, converts Unity directional-light multipliers to Unreal lux, refreshes
the skylight, and saves only the requested Unreal maps. It is idempotent and
never reads from or writes to the Unity project.
"""

from __future__ import annotations

import json
import os
import shutil
import traceback
from datetime import datetime
from pathlib import Path

import unreal

from cml_material_slots import MaterialSlotIndex, material_is_missing


DEFAULT_MAPS = (
    "/Game/Maps/A_00_Bootstrap",
    "/Game/Maps/A_01_IntroCinematic",
    "/Game/Maps/A_10_StarterIsland",
    "/Game/Maps/A_91_StarterIsland_Terrain_Review",
)
SUN_LABELS = {"env_sun", "cml_sun", "directional light"}
SKY_LABELS = {"cml_skylight", "env_skylight", "sky light"}
EXCLUDED_ACTOR_LABELS = {"terrain_bot"}


def _log(message: str) -> None:
    unreal.log(f"[CML Map Finalize] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Map Finalize] {message}")


def _label(actor) -> str:
    try:
        return actor.get_actor_label()
    except Exception:
        return actor.get_name()


def _path(asset) -> str:
    try:
        return asset.get_path_name()
    except Exception:
        return ""


def _map_paths() -> list[str]:
    requested = os.environ.get("CML_FINALIZE_MAPS", "").strip()
    if not requested:
        return list(DEFAULT_MAPS)
    result = []
    for value in requested.split(";"):
        value = value.strip()
        if not value:
            continue
        if not value.startswith("/Game/"):
            value = f"/Game/Maps/{value}"
        result.append(value)
    return result


def _backup_maps(project_dir: Path, maps: list[str]) -> Path:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = project_dir / "Saved" / "MigrationBackups" / f"MapFinalize-{stamp}"
    backup.mkdir(parents=True, exist_ok=False)
    for map_path in maps:
        source = project_dir / "Content" / (map_path.removeprefix("/Game/") + ".umap")
        if source.is_file():
            shutil.copy2(source, backup / source.name)
    return backup


def _static_mesh_assets(slot_index: MaterialSlotIndex) -> dict:
    changed_assets = 0
    changed_slots = 0
    unresolved: list[str] = []
    object_paths = []
    for root in ("/Game/Migrated", "/Game/Migration/EmbeddedMeshes"):
        object_paths.extend(
            unreal.EditorAssetLibrary.list_assets(root, recursive=True, include_folder=False)
        )
    for object_path in sorted(set(object_paths)):
        asset = unreal.EditorAssetLibrary.load_asset(object_path)
        if not isinstance(asset, unreal.StaticMesh):
            continue
        local_issues: list[str] = []
        changed = slot_index.apply_to_mesh_defaults(asset, local_issues)
        if changed:
            unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=True)
            changed_assets += 1
            changed_slots += changed
        unresolved.extend(local_issues)
    return {
        "changedAssets": changed_assets,
        "changedSlots": changed_slots,
        "unresolved": sorted(set(unresolved)),
    }


def _remove_known_source_artifacts() -> list[str]:
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    removed: list[str] = []
    for actor in actor_subsystem.get_all_level_actors():
        label = _label(actor)
        if label.strip().lower() not in EXCLUDED_ACTOR_LABELS:
            continue
        if actor_subsystem.destroy_actor(actor):
            removed.append(label)
    return removed


def _repair_components(world, slot_index: MaterialSlotIndex) -> dict:
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    changed_components = 0
    changed_slots = 0
    unresolved: list[str] = []
    grid_before = 0
    grid_after = 0

    for actor in actor_subsystem.get_all_level_actors():
        try:
            components = actor.get_components_by_class(unreal.StaticMeshComponent)
        except Exception:
            components = []
        for component in components:
            mesh = component.get_editor_property("static_mesh")
            if not isinstance(mesh, unreal.StaticMesh):
                continue
            slot_count = len(mesh.get_editor_property("static_materials") or [])
            before = sum(
                material_is_missing(component.get_material(slot)) for slot in range(slot_count)
            )
            grid_before += before
            local_issues: list[str] = []
            changed = slot_index.apply_to_component(component, mesh, local_issues)
            after = sum(
                material_is_missing(component.get_material(slot)) for slot in range(slot_count)
            )
            grid_after += after
            if changed:
                component.modify()
                changed_components += 1
                changed_slots += changed
            unresolved.extend(local_issues)

    return {
        "changedComponents": changed_components,
        "changedSlots": changed_slots,
        "missingSlotsBefore": grid_before,
        "missingSlotsAfter": grid_after,
        "unresolved": sorted(set(unresolved)),
    }


def _repair_lighting(world) -> dict:
    changed = []
    directionals = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.DirectionalLight)
    for actor in directionals:
        label = _label(actor)
        component = actor.light_component
        try:
            source_multiplier = float(
                unreal.EditorAssetLibrary.get_metadata_tag(actor, "CML.UnityIntensity") or 0.0
            )
        except Exception:
            source_multiplier = 0.0
        current = float(component.get_editor_property("intensity") or 0.0)
        is_migrated_sun = label.lower() in SUN_LABELS or source_multiplier > 0.0 or current <= 1.01
        if not is_migrated_sun:
            continue
        target = max(0.0, source_multiplier or (current / 10.0 if current > 1.01 else current or 1.0)) * 10.0
        component.set_mobility(unreal.ComponentMobility.MOVABLE)
        component.set_intensity(target)
        component.set_editor_property("light_source_angle", 1.2)
        component.set_atmosphere_sun_light(True)
        component.set_editor_property("cast_shadows_on_atmosphere", True)
        component.modify()
        changed.append({"actor": label, "intensity": target, "sourceAngle": 1.2})

    skylights = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.SkyLight)
    for actor in skylights:
        label = _label(actor)
        component = actor.light_component
        if label.lower() not in SKY_LABELS and not label.lower().startswith("cml_"):
            continue
        component.set_mobility(unreal.ComponentMobility.MOVABLE)
        component.set_intensity(1.25)
        component.set_real_time_capture(True)
        try:
            component.recapture_sky()
        except Exception:
            pass
        component.modify()
        changed.append({"actor": label, "intensity": 1.25, "realTimeCapture": True})
    return {"changed": changed}


def _current_world():
    return unreal.EditorLevelLibrary.get_editor_world()


def _current_map_path(world) -> str:
    path = world.get_path_name()
    return path.split(".", 1)[0]


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    maps = [path for path in _map_paths() if unreal.EditorAssetLibrary.does_asset_exist(path)]
    if not maps:
        raise RuntimeError("No requested migrated maps exist")
    backup = _backup_maps(project_dir, maps)
    slot_index = MaterialSlotIndex.from_project(project_dir)
    asset_report = _static_mesh_assets(slot_index)
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    results = []

    # Finalize the currently open requested map first. This avoids a load-level
    # prompt if its preview lighting was adjusted interactively during review.
    world = _current_world()
    current = _current_map_path(world)
    ordered = ([current] if current in maps else []) + [path for path in maps if path != current]
    for map_path in ordered:
        if _current_map_path(_current_world()) != map_path:
            if not level_editor.load_level(map_path):
                raise RuntimeError(f"Unable to load {map_path}")
        world = _current_world()
        removed = _remove_known_source_artifacts()
        components = _repair_components(world, slot_index)
        lighting = _repair_lighting(world)
        if not level_editor.save_current_level():
            raise RuntimeError(f"Unable to save {map_path}")
        results.append(
            {
                "map": map_path,
                "removedSourceArtifacts": removed,
                "components": components,
                "lighting": lighting,
            }
        )
        _log(
            f"{map_path}: components={components['changedComponents']}, "
            f"slots={components['changedSlots']}, remaining={components['missingSlotsAfter']}"
        )

    # Return to the playable production map for immediate visual validation.
    production = "/Game/Maps/A_10_StarterIsland"
    if production in maps and _current_map_path(_current_world()) != production:
        level_editor.load_level(production)

    report = {
        "schema": 1,
        "backup": str(backup),
        "assetDefaults": asset_report,
        "maps": results,
    }
    output = project_dir / "Migration" / "map_finalize_report.json"
    temporary = output.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(output)
    _log(f"Complete: report={output}, backup={backup}")
    return 0


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_MAP_FINALIZE_FAILED code={_exit_code}")
else:
    _log("CML_MAP_FINALIZE_SUCCEEDED")

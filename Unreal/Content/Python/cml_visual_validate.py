"""Render and audit the migrated Starter Island without saving its level.

The migration reports prove that assets exist and Landscape samples survived;
they do not prove that a player can see a coherent frame.  This script opens
the production level, renders deterministic overview/player views through a
real RHI, and records invalid transforms and missing render resources.  Spawned
capture actors are transient and the level is never saved.
"""

from __future__ import annotations

import json
import math
import os
import traceback
from pathlib import Path

import unreal

MAP_PATH = os.environ.get("CML_VISUAL_MAP", "/Game/Maps/A_10_StarterIsland")
OUTPUT_SUBDIR = Path(
    os.environ.get("CML_VISUAL_OUTPUT", "Saved/MigrationValidation")
)
WIDTH = 1600
HEIGHT = 900
HIDDEN_LABELS = {
    label.strip()
    for label in os.environ.get("CML_VISUAL_HIDE_LABELS", "").split(",")
    if label.strip()
}
LANDSCAPE_ONLY = os.environ.get("CML_VISUAL_LANDSCAPE_ONLY", "0") == "1"


def _log(message: str) -> None:
    unreal.log(f"[CML Visual Validation] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Visual Validation] {message}")


def _finite_vector(value) -> bool:
    return all(math.isfinite(float(getattr(value, axis))) for axis in ("x", "y", "z"))


def _vector_values(value) -> list[float]:
    return [float(value.x), float(value.y), float(value.z)]


def _rotator_values(value) -> list[float]:
    return [float(value.pitch), float(value.yaw), float(value.roll)]


def _inside_xy(location, origin, extent) -> bool:
    return (
        abs(float(location.x) - float(origin.x)) <= float(extent.x)
        and abs(float(location.y) - float(origin.y)) <= float(extent.y)
    )


def _inside_any_landscape_xy(location, landscape_bounds) -> bool:
    return any(_inside_xy(location, item[1], item[2]) for item in landscape_bounds)


def _project_dir() -> Path:
    return Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))


def _create_target(world):
    target = unreal.RenderingLibrary.create_render_target2d(world, WIDTH, HEIGHT)
    target.set_editor_property("render_target_format", unreal.TextureRenderTargetFormat.RTF_RGBA8)
    target.set_editor_property("clear_color", unreal.LinearColor(0.02, 0.03, 0.04, 1.0))
    target.set_editor_property("target_gamma", 2.2)
    return target


def _capture(
    world,
    location,
    rotation,
    name: str,
    output_dir: Path,
    capture_source=unreal.SceneCaptureSource.SCS_FINAL_COLOR_LDR,
    hidden_actors=None,
) -> Path:
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actor = actor_subsystem.spawn_actor_from_class(unreal.SceneCapture2D, location, rotation)
    if actor is None:
        raise RuntimeError(f"Could not spawn SceneCapture2D for {name}")

    try:
        actor.set_actor_label(f"CML_TransientCapture_{name}")
        component = actor.get_editor_property("capture_component2d")
        target = _create_target(world)
        component.set_editor_property("texture_target", target)
        component.set_editor_property("capture_source", capture_source)
        component.set_editor_property("fov_angle", 58.0)
        component.set_editor_property("capture_every_frame", False)
        component.set_editor_property("capture_on_movement", False)
        if hidden_actors:
            # SceneCapture-only diagnostic exclusion.  This neither mutates nor
            # saves the source level and makes before/after occlusion checks
            # deterministic.
            for hidden_actor in hidden_actors:
                component.hide_actor_components(hidden_actor, True)
        component.capture_scene()

        output_dir.mkdir(parents=True, exist_ok=True)
        unreal.RenderingLibrary.export_render_target(world, target, str(output_dir), name)
        expected = output_dir / f"{name}.png"
        extensionless = output_dir / name
        if not expected.exists() and extensionless.exists():
            expected = extensionless
        if not expected.exists():
            # UE may choose an image extension based on target format.  Accept
            # it, but report the actual file so validation remains deterministic.
            matches = sorted(output_dir.glob(f"{name}.*"))
            if not matches:
                raise RuntimeError(f"Render target export produced no file for {name}")
            expected = matches[0]
        return expected
    finally:
        actor_subsystem.destroy_actor(actor)


def _landscape_bounds(world):
    landscapes = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape)
    if not landscapes:
        raise RuntimeError("No Landscape exists in the production map")
    bounds = []
    minimum = [math.inf, math.inf, math.inf]
    maximum = [-math.inf, -math.inf, -math.inf]
    for landscape in landscapes:
        origin, extent = landscape.get_actor_bounds(False, True)
        bounds.append((landscape, origin, extent))
        for index, axis in enumerate(("x", "y", "z")):
            center = float(getattr(origin, axis))
            radius = abs(float(getattr(extent, axis)))
            minimum[index] = min(minimum[index], center - radius)
            maximum[index] = max(maximum[index], center + radius)

    union_origin = unreal.Vector(
        *[(minimum[index] + maximum[index]) * 0.5 for index in range(3)]
    )
    union_extent = unreal.Vector(
        *[(maximum[index] - minimum[index]) * 0.5 for index in range(3)]
    )
    return bounds, union_origin, union_extent


def _audit_actors(world, landscape_bounds) -> dict:
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actors = actor_subsystem.get_all_level_actors()
    invalid: list[dict] = []
    renderers = 0
    empty_static_meshes = 0
    renderable_actors = 0
    renderable_inside_landscape = 0
    placeholder_components: list[dict] = []
    actor_samples: list[dict] = []
    renderable_entries: list[dict] = []
    bounds_min = [math.inf, math.inf, math.inf]
    bounds_max = [-math.inf, -math.inf, -math.inf]

    for actor in actors:
        location = actor.get_actor_location()
        scale = actor.get_actor_scale3d()
        if not _finite_vector(location) or not _finite_vector(scale):
            invalid.append({"actor": actor.get_actor_label(), "reason": "non-finite transform"})
        if max(abs(float(location.x)), abs(float(location.y)), abs(float(location.z))) > 1.0e8:
            invalid.append({"actor": actor.get_actor_label(), "reason": "implausible world location"})

        components = actor.get_components_by_class(unreal.StaticMeshComponent)
        renderers += len(components)
        valid_components = 0
        for component in components:
            if component.get_editor_property("static_mesh") is None:
                empty_static_meshes += 1
                placeholder_components.append(
                    {
                        "actor": actor.get_actor_label(),
                        "component": component.get_name(),
                        "actorClass": actor.get_class().get_name(),
                    }
                )
            else:
                valid_components += 1

        if valid_components:
            renderable_actors += 1
            if _inside_any_landscape_xy(location, landscape_bounds):
                renderable_inside_landscape += 1
            origin, extent = actor.get_actor_bounds(False, True)
            if _finite_vector(origin) and _finite_vector(extent):
                for index, axis in enumerate(("x", "y", "z")):
                    center = float(getattr(origin, axis))
                    radius = abs(float(getattr(extent, axis)))
                    bounds_min[index] = min(bounds_min[index], center - radius)
                    bounds_max[index] = max(bounds_max[index], center + radius)
                component_materials = []
                for component in components:
                    if component.get_editor_property("static_mesh") is None:
                        continue
                    for slot in range(component.get_num_materials()):
                        material = component.get_material(slot)
                        path = material.get_path_name() if material else ""
                        if path and path not in component_materials:
                            component_materials.append(path)
                renderable_entries.append(
                    {
                        "label": actor.get_actor_label(),
                        "class": actor.get_class().get_name(),
                        "origin": _vector_values(origin),
                        "extent": _vector_values(extent),
                        "xyArea": float(extent.x) * float(extent.y) * 4.0,
                        "materials": component_materials,
                    }
                )
            if len(actor_samples) < 40:
                actor_samples.append(
                    {
                        "label": actor.get_actor_label(),
                        "class": actor.get_class().get_name(),
                        "location": _vector_values(location),
                        "validStaticMeshes": valid_components,
                        "insideLandscapeXY": _inside_any_landscape_xy(location, landscape_bounds),
                    }
                )

    collective_bounds = None
    if all(math.isfinite(value) for value in (*bounds_min, *bounds_max)):
        collective_bounds = {
            "min": bounds_min,
            "max": bounds_max,
            "origin": [(bounds_min[i] + bounds_max[i]) * 0.5 for i in range(3)],
            "extent": [(bounds_max[i] - bounds_min[i]) * 0.5 for i in range(3)],
        }

    return {
        "actorCount": len(actors),
        "staticMeshComponentCount": renderers,
        "emptyStaticMeshComponents": empty_static_meshes,
        "placeholderStaticMeshComponents": placeholder_components,
        "renderableActorCount": renderable_actors,
        "renderableActorInsideLandscapeXYCount": renderable_inside_landscape,
        "renderableActorOutsideLandscapeXYCount": renderable_actors - renderable_inside_landscape,
        "renderableBounds": collective_bounds,
        "renderableActorSamples": actor_samples,
        "largestRenderableActors": sorted(
            renderable_entries, key=lambda item: item["xyArea"], reverse=True
        )[:40],
        "invalid": invalid,
    }


def main() -> int:
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")

    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    # Loading a Landscape map can enqueue component-local material permutations.
    # Capturing in the same tick otherwise records Unreal's default material and
    # makes every material revision appear pixel-identical.
    unreal.CMLLandscapeImportLibrary.wait_for_editor_compilation()
    hidden_actors = [
        actor
        for actor in unreal.get_editor_subsystem(
            unreal.EditorActorSubsystem
        ).get_all_level_actors()
        if actor.get_actor_label() in HIDDEN_LABELS
    ]
    if LANDSCAPE_ONLY:
        hidden_actors = [
            actor
            for actor in unreal.get_editor_subsystem(
                unreal.EditorActorSubsystem
            ).get_all_level_actors()
            if not isinstance(actor, unreal.Landscape)
        ]
    missing_hidden_labels = HIDDEN_LABELS.difference(
        actor.get_actor_label() for actor in hidden_actors
    )
    if missing_hidden_labels:
        raise RuntimeError(
            "Requested diagnostic hide labels were not found: "
            + ", ".join(sorted(missing_hidden_labels))
        )
    landscape_bounds, origin, extent = _landscape_bounds(world)
    output_dir = _project_dir() / OUTPUT_SUBDIR

    longest = max(float(extent.x), float(extent.y))
    overview_location = unreal.Vector(
        float(origin.x) - longest * 0.72,
        float(origin.y) - longest * 0.72,
        float(origin.z) + longest * 0.62,
    )
    overview_rotation = unreal.MathLibrary.find_look_at_rotation(overview_location, origin)
    starts = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.PlayerStart)
    if not starts:
        raise RuntimeError("No PlayerStart exists in the production map")
    start = starts[0]
    player_location = start.get_actor_location() + unreal.Vector(0.0, 0.0, 72.0)
    player_rotation = start.get_actor_rotation()
    for landscape, _, _ in landscape_bounds:
        if not unreal.CMLLandscapeImportLibrary.build_landscape_grass(
            landscape, [overview_location, player_location]
        ):
            raise RuntimeError(f"Could not build Landscape grass for {landscape.get_actor_label()}")
    overview = _capture(
        world,
        overview_location,
        overview_rotation,
        "starter_island_overview",
        output_dir,
        hidden_actors=hidden_actors,
    )
    overview_base_color = _capture(
        world,
        overview_location,
        overview_rotation,
        "starter_island_overview_base_color",
        output_dir,
        unreal.SceneCaptureSource.SCS_BASE_COLOR,
        hidden_actors,
    )

    player = _capture(
        world,
        player_location,
        player_rotation,
        "starter_island_player",
        output_dir,
        hidden_actors=hidden_actors,
    )

    water_actors = [
        actor
        for actor in unreal.get_editor_subsystem(
            unreal.EditorActorSubsystem
        ).get_all_level_actors()
        if actor.get_actor_label() == "ENV_SoStylized_Water_Pond"
    ]
    if len(water_actors) != 1:
        raise RuntimeError(f"Expected one SoStylized water pond, found {len(water_actors)}")
    water_surfaces = water_actors[0].get_components_by_class(unreal.InstancedStaticMeshComponent)
    if len(water_surfaces) != 1:
        raise RuntimeError(f"Expected one official water surface, found {len(water_surfaces)}")
    water_origin, water_extent, _ = unreal.SystemLibrary.get_component_bounds(water_surfaces[0])
    water_radius = max(float(water_extent.x), float(water_extent.y), 600.0)
    water_location = water_origin + unreal.Vector(
        -water_radius * 0.8, -water_radius * 0.8, water_radius * 0.9
    )
    water_rotation = unreal.MathLibrary.find_look_at_rotation(water_location, water_origin)
    water = _capture(
        world,
        water_location,
        water_rotation,
        "starter_island_water",
        output_dir,
        hidden_actors=hidden_actors,
    )

    audit = _audit_actors(world, landscape_bounds)
    cameras = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.CameraActor)
    landscape_entries = []
    for landscape, landscape_origin, landscape_extent in landscape_bounds:
        material = landscape.get_editor_property("landscape_material")
        landscape_entries.append(
            {
                "label": landscape.get_actor_label(),
                "origin": _vector_values(landscape_origin),
                "extent": _vector_values(landscape_extent),
                "material": material.get_path_name() if material else "",
            }
        )
    report = {
        "schema": 1,
        "map": MAP_PATH,
        "landscapeCount": len(landscape_entries),
        "landscapes": landscape_entries,
        "landscapeUnion": {
            "origin": _vector_values(origin),
            "extent": _vector_values(extent),
        },
        "playerStartCount": len(starts),
        "playerStarts": [
            {
                "label": item.get_actor_label(),
                "location": _vector_values(item.get_actor_location()),
                "rotation": _rotator_values(item.get_actor_rotation()),
                "insideLandscapeXY": _inside_any_landscape_xy(
                    item.get_actor_location(), landscape_bounds
                ),
            }
            for item in starts
        ],
        "cameras": [
            {
                "label": item.get_actor_label(),
                "location": _vector_values(item.get_actor_location()),
                "rotation": _rotator_values(item.get_actor_rotation()),
                "insideLandscapeXY": _inside_any_landscape_xy(
                    item.get_actor_location(), landscape_bounds
                ),
            }
            for item in cameras
        ],
        "captures": [str(overview), str(overview_base_color), str(player), str(water)],
        "captureHiddenActorLabels": sorted(HIDDEN_LABELS),
        **audit,
    }
    report_path = _project_dir() / "Migration" / "unity_visual_validation_report.json"
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)

    if audit["invalid"]:
        for problem in audit["invalid"][:30]:
            _error(f"{problem['actor']}: {problem['reason']}")
        return 2
    if len(landscape_entries) != 1 or landscape_entries[0]["label"] != "TerrainTop":
        _error(
            "Production map must contain only TerrainTop; found "
            + str([item["label"] for item in landscape_entries])
        )
        return 3
    if not any(item["insideLandscapeXY"] for item in report["playerStarts"]):
        _error("No PlayerStart is inside the playable Landscape bounds")
        return 4

    _log(
        f"Rendered {overview.name}, {overview_base_color.name}, {player.name} and {water.name}; "
        f"actors={audit['actorCount']}, "
        f"staticMeshes={audit['staticMeshComponentCount']}"
    )
    return 0


try:
    _exit_code = main()
except Exception:  # noqa: BLE001 - command-line validator must report everything.
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_VISUAL_VALIDATION_FAILED code={_exit_code}")
else:
    _log("CML_VISUAL_VALIDATION_SUCCEEDED")

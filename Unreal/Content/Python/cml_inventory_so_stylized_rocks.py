import json
import os
import re

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
SOURCE_ROCK_ROOT = "/Game/Migrated/Project/Art/Environment/StarterIsland/Rocks/"
CLASSIC_ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Classic"
CLASSIC_PATTERN = re.compile(
    r"^SM_(?:Rock|Boulder|RockClump|BoulderClump)Classic\d+$",
    re.IGNORECASE,
)


def path_of(obj):
    return obj.get_path_name() if obj else None


def vector_record(value):
    return {"x": float(value.x), "y": float(value.y), "z": float(value.z)}


def rotator_record(value):
    return {"pitch": float(value.pitch), "yaw": float(value.yaw), "roll": float(value.roll)}


def bounds_record(mesh):
    box = mesh.get_bounding_box()
    try:
        minimum = box.min
        maximum = box.max
    except Exception:
        minimum = box.get_editor_property("min")
        maximum = box.get_editor_property("max")
    size = maximum - minimum
    return {
        "min": vector_record(minimum),
        "max": vector_record(maximum),
        "size": vector_record(size),
        "volume": abs(float(size.x) * float(size.y) * float(size.z)),
        "text": str(box),
    }


def component_record(actor, component, mesh):
    origin, extent = actor.get_actor_bounds(False, True)
    try:
        world_location = component.get_world_location()
        world_rotation = component.get_world_rotation()
        world_scale = component.get_world_scale()
    except Exception:
        world_location = actor.get_actor_location()
        world_rotation = actor.get_actor_rotation()
        world_scale = actor.get_actor_scale3d()
    return {
        "actor": actor.get_actor_label(),
        "actorClass": actor.get_class().get_name(),
        "component": component.get_name(),
        "mesh": path_of(mesh),
        "meshBounds": bounds_record(mesh),
        "location": vector_record(world_location),
        "rotation": rotator_record(world_rotation),
        "scale": vector_record(world_scale),
        "actorBoundsOrigin": vector_record(origin),
        "actorBoundsExtent": vector_record(extent),
        "materials": [path_of(component.get_material(i)) for i in range(component.get_num_materials())],
        "mobility": str(component.get_editor_property("mobility")),
        "collisionEnabled": str(component.get_collision_enabled()),
    }


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")

    report = {
        "map": MAP_PATH,
        "sourceComponents": [],
        "sourceMeshCounts": {},
        "classicCandidates": [],
    }

    for actor in actor_subsystem.get_all_level_actors():
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            try:
                mesh = component.get_editor_property("static_mesh")
            except Exception:
                continue
            mesh_path = path_of(mesh) or ""
            if mesh_path.startswith(SOURCE_ROCK_ROOT):
                report["sourceComponents"].append(component_record(actor, component, mesh))
                report["sourceMeshCounts"][mesh_path] = report["sourceMeshCounts"].get(mesh_path, 0) + 1

    for asset_path in unreal.EditorAssetLibrary.list_assets(CLASSIC_ROOT, recursive=True, include_folder=False):
        asset_name = asset_path.rsplit("/", 1)[-1].split(".", 1)[0]
        if not CLASSIC_PATTERN.match(asset_name):
            continue
        mesh = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not isinstance(mesh, unreal.StaticMesh):
            continue
        report["classicCandidates"].append({
            "mesh": path_of(mesh),
            "name": mesh.get_name(),
            "bounds": bounds_record(mesh),
            "materials": [path_of(slot.material_interface) for slot in mesh.get_editor_property("static_materials")],
        })

    report["sourceComponents"].sort(key=lambda item: (item["mesh"], item["actor"], item["component"]))
    report["classicCandidates"].sort(key=lambda item: item["name"])
    report["summary"] = {
        "sourceComponentCount": len(report["sourceComponents"]),
        "sourceMeshTypeCount": len(report["sourceMeshCounts"]),
        "classicCandidateCount": len(report["classicCandidates"]),
    }

    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedRockInventory.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Rock Inventory] Wrote {output}")


if __name__ == "__main__":
    main()

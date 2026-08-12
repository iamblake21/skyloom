import hashlib
import json
import math
import os
import re

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
SOURCE_ROOT = "/Game/Migrated/Project/Art/Environment/StarterIsland/Rocks/"
CLASSIC_ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Classic"
CLASSIC_PATTERN = re.compile(
    r"^SM_(Boulder|Rock|RockClump)Classic\d+$",
    re.IGNORECASE,
)


def path_of(obj):
    return obj.get_path_name() if obj else None


def vector_record(value):
    return {"x": float(value.x), "y": float(value.y), "z": float(value.z)}


def mesh_size(mesh):
    box = mesh.get_bounding_box()
    try:
        minimum = box.min
        maximum = box.max
    except Exception:
        minimum = box.get_editor_property("min")
        maximum = box.get_editor_property("max")
    size = maximum - minimum
    return unreal.Vector(abs(float(size.x)), abs(float(size.y)), abs(float(size.z)))


def component_mesh(component):
    try:
        return component.get_editor_property("static_mesh")
    except Exception:
        return None


def actor_bounds(actor):
    origin, extent = actor.get_actor_bounds(False, True)
    return origin, extent, float(origin.z - extent.z)


def shape_signature(size):
    horizontal = sorted((max(float(size.x), 0.001), max(float(size.y), 0.001)), reverse=True)
    return horizontal[1] / horizontal[0], max(float(size.z), 0.001) / horizontal[0]


def shape_score(source_size, candidate_size):
    source_signature = shape_signature(source_size)
    candidate_signature = shape_signature(candidate_size)
    return sum(
        abs(math.log(max(target, 0.001) / max(candidate, 0.001)))
        for target, candidate in zip(source_signature, candidate_signature)
    )


def category_for_source(mesh_name):
    lowered = mesh_name.lower()
    if "shoreflat" in lowered:
        return "RockClump"
    if "boulderlarge" in lowered or "bouldermedium" in lowered:
        return "Boulder"
    if "bouldersmall" in lowered:
        return "Rock"
    raise RuntimeError(f"Unmapped Starter Island rock type: {mesh_name}")


def stable_choice_key(actor, component):
    value = f"{actor.get_path_name()}::{component.get_name()}".encode("utf-8")
    return int.from_bytes(hashlib.sha256(value).digest()[:8], byteorder="little", signed=False)


def official_default_material(mesh):
    materials = mesh.get_editor_property("static_materials")
    if not materials:
        raise RuntimeError(f"Official mesh has no material: {mesh.get_path_name()}")
    material = materials[0].material_interface
    material_path = path_of(material) or ""
    if "/SoStylized/Environment/Rocks/Materials/Classic/" not in material_path:
        raise RuntimeError(f"Mesh does not use an official Classic material: {mesh.get_path_name()}")
    return material


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")

    candidates = {"Boulder": [], "Rock": [], "RockClump": []}
    for asset_path in unreal.EditorAssetLibrary.list_assets(CLASSIC_ROOT, recursive=True, include_folder=False):
        name = asset_path.rsplit("/", 1)[-1].split(".", 1)[0]
        match = CLASSIC_PATTERN.match(name)
        if not match:
            continue
        mesh = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not isinstance(mesh, unreal.StaticMesh):
            continue
        candidates[match.group(1)].append({
            "mesh": mesh,
            "size": mesh_size(mesh),
            "material": official_default_material(mesh),
        })
    for category, entries in candidates.items():
        entries.sort(key=lambda item: item["mesh"].get_name())
        if not entries:
            raise RuntimeError(f"No official Classic candidates found for {category}")

    source_components = []
    for actor in actor_subsystem.get_all_level_actors():
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            mesh = component_mesh(component)
            if mesh and mesh.get_path_name().startswith(SOURCE_ROOT):
                source_components.append((actor, component, mesh))
    source_components.sort(key=lambda item: (item[0].get_path_name(), item[1].get_name()))
    if len(source_components) != 666:
        raise RuntimeError(f"Expected the inventoried 666 source rock components, found {len(source_components)}")

    replacements = []
    category_counts = {"Boulder": 0, "Rock": 0, "RockClump": 0}
    target_mesh_counts = {}
    max_size_error = 0.0
    max_ground_error = 0.0

    for actor, component, source_mesh in source_components:
        source_path = source_mesh.get_path_name()
        source_name = source_mesh.get_name()
        category = category_for_source(source_name)
        source_size = mesh_size(source_mesh)
        old_scale = component.get_world_scale()
        old_location = actor.get_actor_location()
        old_rotation = actor.get_actor_rotation()
        old_collision = component.get_collision_enabled()
        old_origin, old_extent, old_bottom = actor_bounds(actor)

        ranked = sorted(
            candidates[category],
            key=lambda item: (shape_score(source_size, item["size"]), item["mesh"].get_name()),
        )
        shortlist = ranked[: min(3, len(ranked))]
        selected = shortlist[stable_choice_key(actor, component) % len(shortlist)]
        target_mesh = selected["mesh"]
        target_size = selected["size"]

        # Preserve the exact local-space visual envelope. Because the target
        # mesh is selected by aspect ratio first, this compensation remains
        # modest while guaranteeing that large, medium, small and shore-flat
        # rocks retain their authored scene role.
        new_scale = unreal.Vector(
            float(old_scale.x) * float(source_size.x) / max(float(target_size.x), 0.001),
            float(old_scale.y) * float(source_size.y) / max(float(target_size.y), 0.001),
            float(old_scale.z) * float(source_size.z) / max(float(target_size.z), 0.001),
        )

        actor.modify()
        component.modify()
        if not component.set_static_mesh(target_mesh):
            raise RuntimeError(f"Could not set {target_mesh.get_path_name()} on {actor.get_actor_label()}")
        component.set_world_scale3d(new_scale)
        component.set_collision_enabled(old_collision)
        component.set_material(0, selected["material"])
        try:
            component.update_bounds()
        except Exception:
            pass

        new_origin, new_extent, new_bottom = actor_bounds(actor)
        alignment = unreal.Vector(
            float(old_origin.x - new_origin.x),
            float(old_origin.y - new_origin.y),
            float(old_bottom - new_bottom),
        )
        actor.set_actor_location(actor.get_actor_location() + alignment, False, False)
        try:
            component.update_bounds()
        except Exception:
            pass

        final_origin, final_extent, final_bottom = actor_bounds(actor)
        size_error = max(
            abs(float(final_extent.x - old_extent.x)),
            abs(float(final_extent.y - old_extent.y)),
            abs(float(final_extent.z - old_extent.z)),
        )
        ground_error = abs(final_bottom - old_bottom)
        max_size_error = max(max_size_error, size_error)
        max_ground_error = max(max_ground_error, ground_error)
        if size_error > 1.0 or ground_error > 0.25:
            raise RuntimeError(
                f"Bounds preservation failed for {actor.get_actor_label()}: "
                f"size error {size_error:.3f} cm, ground error {ground_error:.3f} cm"
            )
        if component.get_collision_enabled() != old_collision:
            raise RuntimeError(f"Collision mode changed for {actor.get_actor_label()}")
        material_path = path_of(component.get_material(0)) or ""
        if "/SoStylized/Environment/Rocks/Materials/Classic/" not in material_path:
            raise RuntimeError(f"Official Classic material did not stick on {actor.get_actor_label()}")

        target_path = target_mesh.get_path_name()
        category_counts[category] += 1
        target_mesh_counts[target_path] = target_mesh_counts.get(target_path, 0) + 1
        replacements.append({
            "actor": actor.get_actor_label(),
            "actorClass": actor.get_class().get_name(),
            "component": component.get_name(),
            "category": category,
            "sourceMesh": source_path,
            "targetMesh": target_path,
            "targetMaterial": material_path,
            "oldLocation": vector_record(old_location),
            "newLocation": vector_record(actor.get_actor_location()),
            "rotationPreserved": {
                "pitch": float(old_rotation.pitch),
                "yaw": float(old_rotation.yaw),
                "roll": float(old_rotation.roll),
            },
            "oldScale": vector_record(old_scale),
            "newScale": vector_record(new_scale),
            "alignmentOffset": vector_record(alignment),
            "boundsSizeErrorCm": size_error,
            "groundErrorCm": ground_error,
            "collision": str(old_collision),
        })

    remaining = []
    for actor in actor_subsystem.get_all_level_actors():
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            mesh = component_mesh(component)
            if mesh and mesh.get_path_name().startswith(SOURCE_ROOT):
                remaining.append({"actor": actor.get_actor_label(), "mesh": mesh.get_path_name()})
    if remaining:
        raise RuntimeError(f"Source rock components remain after replacement: {remaining[:10]}")
    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {MAP_PATH}")

    report = {
        "map": MAP_PATH,
        "replacementCount": len(replacements),
        "categoryCounts": category_counts,
        "targetMeshCounts": target_mesh_counts,
        "sourceComponentsRemaining": remaining,
        "maxBoundsSizeErrorCm": max_size_error,
        "maxGroundErrorCm": max_ground_error,
        "preserved": ["actor class", "rotation", "visual bounds", "ground contact", "collision mode"],
        "replacements": replacements,
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedRockReplacement.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Rock Replacement] Wrote {output}")


if __name__ == "__main__":
    main()

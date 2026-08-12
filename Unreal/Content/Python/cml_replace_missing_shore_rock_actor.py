import json
import os

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
ACTOR_LABEL = "PF_ENV_Rock_ShoreFlat_B"
MESH_PATH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Classic/SM_RockClumpClassic4"
)
MATERIAL_PATH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Materials/Classic/MI_RockClassic_Rocks"
)
LOCATION = unreal.Vector(2553.9457749373046, -18943.612987211203, 3022.0224316715908)
ROTATION = unreal.Rotator(0.0, 0.0, 0.0)
SCALE = unreal.Vector(22.734080206770955, 2.3405252303085136, 2.619875475271104)


def path_of(obj):
    return obj.get_path_name() if obj else None


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    mesh = unreal.EditorAssetLibrary.load_asset(MESH_PATH)
    material = unreal.EditorAssetLibrary.load_asset(MATERIAL_PATH)
    if not isinstance(mesh, unreal.StaticMesh) or not isinstance(material, unreal.MaterialInterface):
        raise RuntimeError("Official replacement rock assets are missing")

    removed = []
    for actor in list(actor_subsystem.get_all_level_actors()):
        if actor.get_actor_label() == ACTOR_LABEL:
            removed.append({"class": actor.get_class().get_name(), "path": actor.get_path_name()})
            if not actor_subsystem.destroy_actor(actor):
                raise RuntimeError(f"Could not remove stale actor {actor.get_path_name()}")

    actor = actor_subsystem.spawn_actor_from_class(unreal.StaticMeshActor, LOCATION, ROTATION)
    if not actor:
        raise RuntimeError("Could not spawn the project-owned replacement shore rock")
    actor.set_actor_label(ACTOR_LABEL)
    actor.set_actor_location(LOCATION, False, False)
    actor.set_actor_rotation(ROTATION, False)
    actor.set_actor_scale3d(SCALE)
    try:
        actor.set_folder_path("Environment/Rocks")
    except Exception:
        pass
    component = actor.get_editor_property("static_mesh_component")
    component.set_editor_property("static_mesh", mesh)
    component.set_material(0, material)
    component.set_collision_enabled(unreal.CollisionEnabled.QUERY_AND_PHYSICS)
    component.modify()
    actor.modify()

    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {MAP_PATH}")
    # Reload the serialized package, not merely the in-memory actor, so the
    # missing old Blueprint class export is proven to be gone.
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not reload {MAP_PATH}")
    matches = [
        candidate for candidate in actor_subsystem.get_all_level_actors()
        if candidate.get_actor_label() == ACTOR_LABEL
    ]
    if len(matches) != 1 or not isinstance(matches[0], unreal.StaticMeshActor):
        raise RuntimeError(f"Replacement actor did not survive reload correctly: {matches}")
    current_component = matches[0].get_editor_property("static_mesh_component")
    if path_of(current_component.get_editor_property("static_mesh")) != mesh.get_path_name():
        raise RuntimeError("Replacement shore rock mesh did not survive reload")
    if path_of(current_component.get_material(0)) != material.get_path_name():
        raise RuntimeError("Replacement shore rock material did not survive reload")

    report = {
        "map": MAP_PATH,
        "removedStaleActors": removed,
        "replacement": {
            "label": ACTOR_LABEL,
            "class": matches[0].get_class().get_name(),
            "mesh": mesh.get_path_name(),
            "material": material.get_path_name(),
            "location": {"x": LOCATION.x, "y": LOCATION.y, "z": LOCATION.z},
            "scale": {"x": SCALE.x, "y": SCALE.y, "z": SCALE.z},
            "collision": str(current_component.get_collision_enabled()),
        },
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "MissingShoreRockActorReplacement.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Missing Shore Rock Actor] Wrote {output}")


if __name__ == "__main__":
    main()

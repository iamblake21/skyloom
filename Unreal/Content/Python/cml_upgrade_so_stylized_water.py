import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
WATER_BLUEPRINT = "/Game/_Project/Art/Environment/SoStylized/Environment/Water/PRESETS/Classic/BP_Classic_Water"
WATER_LABEL = "ENV_SoStylized_Water_Pond"


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    matches = [a for a in actor_subsystem.get_all_level_actors() if a.get_actor_label() == WATER_LABEL]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {WATER_LABEL}, found {len(matches)}")
    old_actor = matches[0]
    old_components = old_actor.get_components_by_class(unreal.StaticMeshComponent)
    if len(old_components) != 1:
        raise RuntimeError(f"Expected one source water mesh component, found {len(old_components)}")
    pond_mesh = old_components[0].get_editor_property("static_mesh")
    if not isinstance(pond_mesh, unreal.StaticMesh):
        raise RuntimeError("The pond silhouette mesh is missing")
    location = old_actor.get_actor_location()
    rotation = old_actor.get_actor_rotation()
    scale = old_actor.get_actor_scale3d()

    water_class = unreal.EditorAssetLibrary.load_blueprint_class(WATER_BLUEPRINT)
    if not water_class:
        raise RuntimeError(f"Official water Blueprint is missing: {WATER_BLUEPRINT}")
    new_actor = actor_subsystem.spawn_actor_from_class(water_class, location, rotation)
    if not new_actor:
        raise RuntimeError("Could not spawn BP_Classic_Water")
    new_actor.set_actor_scale3d(scale)
    surface_components = new_actor.get_components_by_class(unreal.InstancedStaticMeshComponent)
    if len(surface_components) != 1:
        actor_subsystem.destroy_actor(new_actor)
        raise RuntimeError(f"Expected one official water surface component, found {len(surface_components)}")
    surface = surface_components[0]
    surface.set_editor_property("static_mesh", pond_mesh)
    surface.modify()

    # The rectangular underwater helpers belong to the stock square plane.
    # Keep the official dynamic surface while avoiding underwater geometry that
    # would extend beyond the preserved custom pond silhouette.
    hidden_helpers = []
    for component in new_actor.get_components_by_class(unreal.StaticMeshComponent):
        if component == surface:
            continue
        if component.get_name() in {"SM_UnderwaterHat", "SM_UnderwaterSurface", "SM_OceanExtension"}:
            component.set_visibility(False, True)
            component.set_hidden_in_game(True)
            hidden_helpers.append(component.get_name())

    new_actor.set_actor_label(WATER_LABEL)
    try:
        new_actor.set_folder_path("Environment/SoStylized")
    except Exception:
        pass
    if not actor_subsystem.destroy_actor(old_actor):
        actor_subsystem.destroy_actor(new_actor)
        raise RuntimeError("Could not remove the old static water actor")
    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {MAP_PATH}")

    report = {
        "map": MAP_PATH,
        "actor": WATER_LABEL,
        "class": new_actor.get_class().get_name(),
        "blueprint": WATER_BLUEPRINT,
        "surfaceMesh": pond_mesh.get_path_name(),
        "surfaceComponent": surface.get_name(),
        "hiddenStockHelpers": hidden_helpers,
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedWaterUpgrade.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Water] Wrote {output}")


if __name__ == "__main__":
    main()

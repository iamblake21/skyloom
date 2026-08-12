import json
import os

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
SAFE_MAP_PATH = "/Game/Maps/A_01_IntroCinematic"
TEMP_FOLDER = "/Game/Migrated/Project/Art/Environment/StarterIsland/Rocks"
TEMP_PACKAGE_PATH = TEMP_FOLDER + "/Prefabs"
TEMP_ASSET_NAME = "BP_PF_ENV_Rock_ShoreFlat_B"
TEMP_ASSET_PATH = TEMP_PACKAGE_PATH + "/" + TEMP_ASSET_NAME
OLD_CLASS_NAME = TEMP_ASSET_NAME + "_C"
ACTOR_LABEL = "PF_ENV_Rock_ShoreFlat_B"
OFFICIAL_MESH_PATH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Classic/"
    "SM_RockClumpClassic4"
)
OFFICIAL_MATERIAL_PATH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Materials/Classic/"
    "MI_RockClassic_Rocks"
)


def path_of(obj):
    return obj.get_path_name() if obj else None


def add_component(subobjects, blueprint, parent_handle, component_class, name):
    params = unreal.AddNewSubobjectParams(
        parent_handle=parent_handle,
        new_class=component_class,
        blueprint_context=blueprint,
    )
    handle, failure = subobjects.add_new_subobject(params)
    if not failure.is_empty():
        raise RuntimeError(f"Could not create temporary component {name}: {failure}")
    subobjects.rename_subobject(handle, unreal.Text(name))
    data = subobjects.k2_find_subobject_data_from_handle(handle)
    component = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(data)
    return handle, component


def create_compatibility_blueprint():
    if unreal.EditorAssetLibrary.does_asset_exist(TEMP_ASSET_PATH):
        existing = unreal.EditorAssetLibrary.load_asset(TEMP_ASSET_PATH)
        if not isinstance(existing, unreal.Blueprint):
            raise RuntimeError(f"Unexpected non-Blueprint asset at {TEMP_ASSET_PATH}")
        return existing
    unreal.EditorAssetLibrary.make_directory(TEMP_PACKAGE_PATH)
    factory = unreal.BlueprintFactory()
    factory.set_editor_property("parent_class", unreal.Actor)
    blueprint = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
        TEMP_ASSET_NAME, TEMP_PACKAGE_PATH, unreal.Blueprint, factory
    )
    if not isinstance(blueprint, unreal.Blueprint):
        raise RuntimeError("Could not create temporary compatibility Blueprint")

    subobjects = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
    handles = subobjects.k2_gather_subobject_data_for_blueprint(blueprint)
    if not handles:
        raise RuntimeError("Temporary Blueprint exposed no root handle")

    scene_handle, _ = add_component(
        subobjects, blueprint, handles[0], unreal.SceneComponent, "DefaultSceneRoot"
    )
    mesh = unreal.EditorAssetLibrary.load_asset(OFFICIAL_MESH_PATH)
    material = unreal.EditorAssetLibrary.load_asset(OFFICIAL_MATERIAL_PATH)
    if not isinstance(mesh, unreal.StaticMesh) or not isinstance(material, unreal.MaterialInterface):
        raise RuntimeError("Official So Stylized compatibility assets are missing")

    for name in ("PF_ENV_Rock_ShoreFlat_B", "ENV_Rock_ShoreFlat_B"):
        _, component = add_component(
            subobjects, blueprint, scene_handle, unreal.StaticMeshComponent, name
        )
        component.set_editor_property("static_mesh", mesh)
        component.set_material(0, material)
        component.set_collision_enabled(unreal.CollisionEnabled.NO_COLLISION)

    unreal.BlueprintEditorLibrary.compile_blueprint(blueprint)
    if not unreal.EditorAssetLibrary.save_loaded_asset(blueprint, only_if_is_dirty=False):
        raise RuntimeError("Could not save temporary compatibility Blueprint")
    return blueprint


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    create_compatibility_blueprint()

    # load_level() is a no-op when asked to load the already-current map. Move
    # away first so the broken exports are deserialized again with the restored
    # compatibility class available.
    if not level_editor.load_level(SAFE_MAP_PATH):
        raise RuntimeError(f"Could not load safe map {SAFE_MAP_PATH}")
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not reload {MAP_PATH} with compatibility class present")

    removed = []
    for actor in list(actor_subsystem.get_all_level_actors()):
        if actor.get_class().get_name() == OLD_CLASS_NAME:
            removed.append({
                "label": actor.get_actor_label(),
                "class": actor.get_class().get_name(),
                "path": actor.get_path_name(),
            })
            if not actor_subsystem.destroy_actor(actor):
                raise RuntimeError(f"Could not destroy restored stale actor {actor.get_path_name()}")
    if len(removed) > 1:
        raise RuntimeError(f"Unexpected duplicate restored stale actors: {removed}")

    replacements = [
        actor for actor in actor_subsystem.get_all_level_actors()
        if actor.get_actor_label() == ACTOR_LABEL and isinstance(actor, unreal.StaticMeshActor)
    ]
    if len(replacements) != 1:
        raise RuntimeError(f"Expected one project-owned shore-rock replacement, found {replacements}")
    component = replacements[0].get_editor_property("static_mesh_component")
    mesh_path = path_of(component.get_editor_property("static_mesh"))
    material_path = path_of(component.get_material(0))
    if mesh_path != unreal.EditorAssetLibrary.load_asset(OFFICIAL_MESH_PATH).get_path_name():
        raise RuntimeError(f"Replacement mesh changed unexpectedly: {mesh_path}")
    if material_path != unreal.EditorAssetLibrary.load_asset(OFFICIAL_MATERIAL_PATH).get_path_name():
        raise RuntimeError(f"Replacement material changed unexpectedly: {material_path}")

    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save purged map {MAP_PATH}")

    # Unload the map before deleting the temporary class package. This ensures
    # the saved map must stand on its own when it is loaded again.
    if not level_editor.load_level(SAFE_MAP_PATH):
        raise RuntimeError(f"Could not load safe map {SAFE_MAP_PATH}")
    if not unreal.EditorAssetLibrary.delete_asset(TEMP_ASSET_PATH):
        raise RuntimeError(f"Could not delete temporary Blueprint {TEMP_ASSET_PATH}")
    if unreal.EditorAssetLibrary.does_directory_exist(TEMP_FOLDER):
        if not unreal.EditorAssetLibrary.delete_directory(TEMP_FOLDER):
            raise RuntimeError(f"Could not remove temporary old rock folder {TEMP_FOLDER}")

    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not reload clean map {MAP_PATH}")
    stale_classes = [
        actor.get_path_name() for actor in actor_subsystem.get_all_level_actors()
        if actor.get_class().get_name() == OLD_CLASS_NAME
    ]
    final_replacements = [
        actor for actor in actor_subsystem.get_all_level_actors()
        if actor.get_actor_label() == ACTOR_LABEL and isinstance(actor, unreal.StaticMeshActor)
    ]
    if stale_classes or len(final_replacements) != 1:
        raise RuntimeError(
            f"Final purge validation failed: stale={stale_classes}, replacements={final_replacements}"
        )
    if unreal.EditorAssetLibrary.does_directory_exist(TEMP_FOLDER):
        raise RuntimeError(f"Old rock folder still exists: {TEMP_FOLDER}")

    report = {
        "map": MAP_PATH,
        "removedRestoredActors": removed,
        "orphanExportPrunedByResave": len(removed) == 0,
        "replacement": {
            "label": final_replacements[0].get_actor_label(),
            "class": final_replacements[0].get_class().get_name(),
            "mesh": path_of(
                final_replacements[0]
                .get_editor_property("static_mesh_component")
                .get_editor_property("static_mesh")
            ),
            "material": path_of(
                final_replacements[0]
                .get_editor_property("static_mesh_component")
                .get_material(0)
            ),
        },
        "temporaryBlueprintDeleted": not unreal.EditorAssetLibrary.does_asset_exist(
            TEMP_ASSET_PATH
        ),
        "oldRockFolderDeleted": not unreal.EditorAssetLibrary.does_directory_exist(
            TEMP_FOLDER
        ),
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "PurgedMissingShoreRockExport.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Purge Missing Shore Rock Export] Wrote {output}")


if __name__ == "__main__":
    main()

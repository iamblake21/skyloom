import json
import os

import unreal


BLUEPRINT_PATH = "/Game/Migrated/Project/Resources/Items/BP_PF_Stone"
TARGET_MESH_PATH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Classic/SM_RockClassic2"
)
TARGET_MATERIAL_PATH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Materials/Classic/MI_RockClassic_Rocks"
)
OLD_ROOT = "/Game/Migrated/Project/Art/Environment/StarterIsland/Rocks"
DELETED_MAPS = [
    "/Game/Maps/A_91_StarterIsland_Terrain_Review",
    "/Game/Migration/MapBackups/A_91_StarterIsland_Terrain_Review_BeforeReimport_01",
    "/Game/Migration/MapBackups/A_91_StarterIsland_Terrain_Review_BeforeReimport_02",
]


def path_of(obj):
    return obj.get_path_name() if obj else None


def main():
    blueprint = unreal.EditorAssetLibrary.load_asset(BLUEPRINT_PATH)
    mesh = unreal.EditorAssetLibrary.load_asset(TARGET_MESH_PATH)
    material = unreal.EditorAssetLibrary.load_asset(TARGET_MATERIAL_PATH)
    if not isinstance(blueprint, unreal.Blueprint):
        raise RuntimeError(f"Gameplay stone Blueprint is missing: {BLUEPRINT_PATH}")
    if not isinstance(mesh, unreal.StaticMesh):
        raise RuntimeError(f"Official Classic stone mesh is missing: {TARGET_MESH_PATH}")
    if not isinstance(material, unreal.MaterialInterface):
        raise RuntimeError(f"Official Classic rock material is missing: {TARGET_MATERIAL_PATH}")

    subsystem = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
    handles = subsystem.k2_gather_subobject_data_for_blueprint(blueprint)
    components_by_path = {}
    for handle in handles:
        data = subsystem.k2_find_subobject_data_from_handle(handle)
        obj = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(data)
        if isinstance(obj, unreal.StaticMeshComponent):
            components_by_path[obj.get_path_name()] = obj
    components = list(components_by_path.values())
    if len(components) != 1:
        raise RuntimeError(f"Expected one stone StaticMeshComponent template, found {len(components)}")

    component = components[0]
    before = {
        "mesh": path_of(component.get_editor_property("static_mesh")),
        "materials": [path_of(component.get_material(i)) for i in range(component.get_num_materials())],
    }
    component.modify()
    component.set_editor_property("static_mesh", mesh)
    component.set_material(0, material)
    unreal.BlueprintEditorLibrary.compile_blueprint(blueprint)
    if not unreal.EditorAssetLibrary.save_loaded_asset(blueprint, only_if_is_dirty=False):
        raise RuntimeError(f"Could not save {BLUEPRINT_PATH}")

    current_mesh = component.get_editor_property("static_mesh")
    current_material = component.get_material(0)
    if path_of(current_mesh) != mesh.get_path_name():
        raise RuntimeError("Gameplay stone mesh verification failed")
    if path_of(current_material) != material.get_path_name():
        raise RuntimeError("Gameplay stone material verification failed")
    if unreal.EditorAssetLibrary.does_directory_exist(OLD_ROOT):
        raise RuntimeError(f"Deleted old rock directory reappeared: {OLD_ROOT}")
    undeleted_maps = [path for path in DELETED_MAPS if unreal.EditorAssetLibrary.does_asset_exist(path)]
    if undeleted_maps:
        raise RuntimeError(f"Obsolete maps still exist: {undeleted_maps}")

    report = {
        "deletedDirectory": OLD_ROOT,
        "deletedObsoleteMaps": DELETED_MAPS,
        "sourceDirectoryStillExists": False,
        "gameplayStone": {
            "asset": BLUEPRINT_PATH,
            "component": component.get_path_name(),
            "before": before,
            "currentMesh": mesh.get_path_name(),
            "currentMaterial": material.get_path_name(),
        },
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "DeletedReplacedRockAssets.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Fixed SoStylized Stone Blueprint] Wrote {output}")


if __name__ == "__main__":
    main()

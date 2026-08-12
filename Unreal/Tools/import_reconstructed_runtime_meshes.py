import json
import os
import traceback
import unreal


SOURCE_DIR = os.path.join(unreal.Paths.project_dir(), "Migration", "Generated")
REPORT_PATH = os.path.join(
    unreal.Paths.project_saved_dir(), "reconstructed_runtime_meshes_report.json")

ASSETS = [
    {
        "name": "SM_Crate_RuntimeBody",
        "destination": "/Game/_Project/Art/ManualEra",
        "material": "/Game/Migrated/Project/Art/ManualEra/Models/STR_Crate/STR_Crate/Materials/M_ManualEra_OpaqueAtlas.M_ManualEra_OpaqueAtlas",
    },
    {
        "name": "SM_Crate_RuntimeLid",
        "destination": "/Game/_Project/Art/ManualEra",
        "material": "/Game/Migrated/Project/Art/ManualEra/Models/STR_Crate/STR_Crate/Materials/M_ManualEra_OpaqueAtlas.M_ManualEra_OpaqueAtlas",
    },
    {
        "name": "SM_Workbench_RuntimeVisual",
        "destination": "/Game/_Project/Art/ManualEra",
        "material": "/Game/Migrated/Project/Art/ManualEra/Models/STR_Workbench/STR_Workbench/Materials/M_ManualEra_OpaqueAtlas.M_ManualEra_OpaqueAtlas",
    },
    {
        "name": "SM_CrudeFurnace_RuntimeVisual",
        "destination": "/Game/_Project/Art/ManualEra",
        "materials": {
            "default": "/Game/Migrated/Project/Art/ManualEra/Models/STR_CrudeFurnace/STR_CrudeFurnace/Materials/M_ManualEra_OpaqueAtlas.M_ManualEra_OpaqueAtlas",
            "fire": "/Game/Migrated/Project/Art/ManualEra/Models/STR_CrudeFurnace/STR_CrudeFurnace/Materials/M_ManualEra_FireEmissive.M_ManualEra_FireEmissive",
        },
    },
    {
        "name": "SM_Airship_RuntimeVisual",
        "destination": "/Game/_Project/Art/Vehicles/Airship",
        "airship": True,
    },
    {
        "name": "SM_Airship_RuntimeDoor",
        "destination": "/Game/_Project/Art/Vehicles/Airship",
        "airship": True,
    },
]

AIRSHIP_MATERIALS = {
    "opaque": "/Game/Migrated/Project/Art/Vehicles/Airship/Models/AIR_Airship/AIR_Airship/Materials/M_Airship_OpaqueAtlas.M_Airship_OpaqueAtlas",
    "emission": "/Game/Migrated/Project/Art/Vehicles/Airship/Models/AIR_Airship/AIR_Airship/Materials/M_Airship_EmissionAtlas.M_Airship_EmissionAtlas",
    "glass": "/Game/Migrated/Project/Art/Vehicles/Airship/Models/AIR_Airship/AIR_Airship/Materials/M_Airship_Glass.M_Airship_Glass",
}


def import_one(spec):
    name = spec["name"]
    source = os.path.join(SOURCE_DIR, f"{name}.fbx")
    options = unreal.FbxImportUI()
    options.set_editor_property("import_mesh", True)
    options.set_editor_property("import_materials", False)
    options.set_editor_property("import_textures", False)
    options.set_editor_property("import_as_skeletal", False)
    data = options.get_editor_property("static_mesh_import_data")
    data.set_editor_property("combine_meshes", True)
    data.set_editor_property("generate_lightmap_u_vs", True)
    data.set_editor_property("auto_generate_collision", False)
    data.set_editor_property("import_mesh_lods", False)
    data.set_editor_property("convert_scene", True)
    data.set_editor_property("convert_scene_unit", True)

    task = unreal.AssetImportTask()
    task.set_editor_property("filename", source)
    task.set_editor_property("destination_path", spec["destination"])
    task.set_editor_property("destination_name", name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    task.set_editor_property("options", options)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

    asset_path = f"{spec['destination']}/{name}.{name}"
    mesh = unreal.EditorAssetLibrary.load_asset(asset_path)
    if not isinstance(mesh, unreal.StaticMesh):
        raise RuntimeError(f"Import failed for {name}: {task.imported_object_paths}")

    assigned = []
    if spec.get("airship"):
        materials = {
            key: unreal.EditorAssetLibrary.load_asset(path)
            for key, path in AIRSHIP_MATERIALS.items()
        }
        for index, slot in enumerate(list(mesh.get_editor_property("static_materials") or [])):
            slot_name = str(slot.get_editor_property("material_slot_name")).lower()
            key = "emission" if "emission" in slot_name else "glass" if "glass" in slot_name else "opaque"
            mesh.set_material(index, materials[key])
            assigned.append(AIRSHIP_MATERIALS[key])
        if name == "SM_Airship_RuntimeVisual":
            body_setup = mesh.get_editor_property("body_setup")
            if body_setup:
                body_setup.set_editor_property(
                    "collision_trace_flag",
                    unreal.CollisionTraceFlag.CTF_USE_COMPLEX_AS_SIMPLE,
                )
    else:
        material_paths = spec.get("materials")
        if material_paths is None:
            material_paths = {"default": spec["material"]}
        materials = {
            key: unreal.EditorAssetLibrary.load_asset(path)
            for key, path in material_paths.items()
        }
        if any(material is None for material in materials.values()):
            raise RuntimeError(f"Missing material in {material_paths}")
        for index, slot in enumerate(list(mesh.get_editor_property("static_materials") or [])):
            slot_name = str(slot.get_editor_property("material_slot_name")).lower()
            key = "fire" if "fire" in slot_name and "fire" in materials else "default"
            mesh.set_material(index, materials[key])
            assigned.append(material_paths[key])
    unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False)
    bounds = mesh.get_bounds()
    return {
        "asset": asset_path,
        "imported": list(task.get_editor_property("imported_object_paths") or []),
        "materials": assigned,
        "origin": [bounds.origin.x, bounds.origin.y, bounds.origin.z],
        "extent": [bounds.box_extent.x, bounds.box_extent.y, bounds.box_extent.z],
    }


def main():
    report = {"success": False, "assets": []}
    try:
        only_name = os.environ.get("CML_RECONSTRUCTED_ASSET", "").strip()
        selected = [spec for spec in ASSETS if not only_name or spec["name"] == only_name]
        if only_name and not selected:
            raise RuntimeError(f"Unknown reconstructed asset filter: {only_name}")
        for spec in selected:
            report["assets"].append(import_one(spec))
        report["success"] = True
    except Exception as exception:
        report["error"] = str(exception)
        report["traceback"] = traceback.format_exc()
        unreal.log_error(report["traceback"])
    finally:
        with open(REPORT_PATH, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
        unreal.log(f"CML reconstructed asset report: {REPORT_PATH}")


main()

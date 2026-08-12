import json
import os
import traceback
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
SOURCE_FBX = os.path.join(unreal.Paths.project_dir(), "Migration", "Generated", "SM_Airship_Visual.fbx")
DESTINATION_PATH = "/Game/_Project/Art/Vehicles/Airship"
ASSET_NAME = "SM_Airship_Visual"
ASSET_PATH = f"{DESTINATION_PATH}/{ASSET_NAME}.{ASSET_NAME}"
REPORT_PATH = os.path.join(unreal.Paths.project_saved_dir(), "airship_and_bush_fix_report.json")

MATERIAL_PATHS = {
    "opaque": "/Game/Migrated/Project/Art/Vehicles/Airship/Models/AIR_Airship/AIR_Airship/Materials/M_Airship_OpaqueAtlas.M_Airship_OpaqueAtlas",
    "emission": "/Game/Migrated/Project/Art/Vehicles/Airship/Models/AIR_Airship/AIR_Airship/Materials/M_Airship_EmissionAtlas.M_Airship_EmissionAtlas",
    "glass": "/Game/Migrated/Project/Art/Vehicles/Airship/Models/AIR_Airship/AIR_Airship/Materials/M_Airship_Glass.M_Airship_Glass",
}


def vector_dict(value):
    return {"x": value.x, "y": value.y, "z": value.z}


def rotator_dict(value):
    return {"pitch": value.pitch, "yaw": value.yaw, "roll": value.roll}


def import_airship_mesh():
    options = unreal.FbxImportUI()
    options.set_editor_property("import_mesh", True)
    options.set_editor_property("import_materials", False)
    options.set_editor_property("import_textures", False)
    options.set_editor_property("import_as_skeletal", False)

    static_data = options.get_editor_property("static_mesh_import_data")
    static_data.set_editor_property("combine_meshes", True)
    static_data.set_editor_property("generate_lightmap_u_vs", True)
    static_data.set_editor_property("auto_generate_collision", False)
    static_data.set_editor_property("import_mesh_lods", False)
    static_data.set_editor_property("convert_scene", True)
    static_data.set_editor_property("convert_scene_unit", True)

    task = unreal.AssetImportTask()
    task.set_editor_property("filename", SOURCE_FBX)
    task.set_editor_property("destination_path", DESTINATION_PATH)
    task.set_editor_property("destination_name", ASSET_NAME)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    task.set_editor_property("options", options)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

    mesh = unreal.EditorAssetLibrary.load_asset(ASSET_PATH)
    if not isinstance(mesh, unreal.StaticMesh):
        imported = list(task.get_editor_property("imported_object_paths") or [])
        raise RuntimeError(f"Airship StaticMesh import failed: {imported}")
    return mesh, list(task.get_editor_property("imported_object_paths") or [])


def bind_airship_materials(mesh):
    materials = {key: unreal.EditorAssetLibrary.load_asset(path) for key, path in MATERIAL_PATHS.items()}
    missing = [key for key, value in materials.items() if value is None]
    if missing:
        raise RuntimeError(f"Missing migrated airship materials: {missing}")

    slot_records = []
    slots = list(mesh.get_editor_property("static_materials") or [])
    for index, slot in enumerate(slots):
        slot_name = str(slot.get_editor_property("material_slot_name")).lower()
        if "emission" in slot_name:
            key = "emission"
        elif "glass" in slot_name:
            key = "glass"
        else:
            key = "opaque"
        mesh.set_material(index, materials[key])
        slot_records.append({"index": index, "slot": slot_name, "material": MATERIAL_PATHS[key]})

    try:
        body_setup = mesh.get_editor_property("body_setup")
        if body_setup:
            body_setup.set_editor_property(
                "collision_trace_flag",
                unreal.CollisionTraceFlag.CTF_USE_COMPLEX_AS_SIMPLE,
            )
    except Exception as exception:
        unreal.log_warning(f"Could not configure complex airship collision: {exception}")
    unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False)
    return slot_records


def fix_bushes(actors):
    corrected = []
    for actor in actors:
        haystack = f"{actor.get_actor_label()} {actor.get_class().get_name()} {actor.get_path_name()}".lower()
        if "cloudbush" not in haystack:
            continue
        rotation = actor.get_actor_rotation()
        if abs(rotation.pitch + 90.0) < 0.01 and abs(rotation.roll) < 0.01:
            continue
        before = rotator_dict(rotation)
        actor.set_actor_rotation(
            unreal.Rotator(pitch=-90.0, yaw=rotation.yaw, roll=0.0),
            False,
        )
        corrected.append({
            "label": actor.get_actor_label(),
            "before": before,
            "after": rotator_dict(actor.get_actor_rotation()),
        })
    return corrected


def replace_airship_actor(actors, mesh):
    removed = []
    for actor in actors:
        label = actor.get_actor_label().lower()
        class_name = actor.get_class().get_name().lower()
        if label in ("pf_airship", "cml_airship_visual") or "bp_pf_airship" in class_name:
            removed.append({
                "label": actor.get_actor_label(),
                "class": actor.get_class().get_name(),
                "location": vector_dict(actor.get_actor_location()),
            })
            unreal.EditorLevelLibrary.destroy_actor(actor)

    actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
        unreal.StaticMeshActor,
        unreal.Vector(-18700.0, -27700.0, 2585.0),
        unreal.Rotator(pitch=0.0, yaw=0.0, roll=0.0),
    )
    if actor is None:
        raise RuntimeError("Failed to spawn airship actor")

    actor.set_actor_label("PF_Airship")
    actor.set_actor_scale3d(unreal.Vector(1.51, 1.51, 1.51))
    component = actor.get_component_by_class(unreal.StaticMeshComponent)
    component.set_static_mesh(mesh)
    component.set_mobility(unreal.ComponentMobility.MOVABLE)
    component.set_collision_enabled(unreal.CollisionEnabled.QUERY_AND_PHYSICS)
    unreal.EditorLevelLibrary.set_selected_level_actors([actor])
    return actor, removed


def main():
    report = {"map": MAP_PATH, "success": False}
    try:
        if unreal.EditorLevelLibrary.get_editor_world().get_path_name().split(":")[0] != MAP_PATH:
            unreal.EditorLoadingAndSavingUtils.load_map(MAP_PATH)

        mesh, imported_paths = import_airship_mesh()
        report["imported_paths"] = imported_paths
        report["material_slots"] = bind_airship_materials(mesh)

        actors = unreal.EditorLevelLibrary.get_all_level_actors()
        report["corrected_bushes"] = fix_bushes(actors)
        actor, removed = replace_airship_actor(actors, mesh)
        report["removed_airship_placeholders"] = removed
        report["airship"] = {
            "label": actor.get_actor_label(),
            "asset": mesh.get_path_name(),
            "location": vector_dict(actor.get_actor_location()),
            "rotation": rotator_dict(actor.get_actor_rotation()),
            "scale": vector_dict(actor.get_actor_scale3d()),
        }

        try:
            bounds = mesh.get_bounds()
            report["mesh_bounds"] = {
                "origin": vector_dict(bounds.origin),
                "box_extent": vector_dict(bounds.box_extent),
                "sphere_radius": bounds.sphere_radius,
            }
        except Exception as exception:
            report["mesh_bounds_error"] = str(exception)
        report["save_success"] = unreal.EditorLoadingAndSavingUtils.save_current_level()
        report["success"] = True
    except Exception as exception:
        report["error"] = str(exception)
        report["traceback"] = traceback.format_exc()
        unreal.log_error(report["traceback"])
    finally:
        with open(REPORT_PATH, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
        unreal.log(f"CML airship/bush report written to {REPORT_PATH}")


main()

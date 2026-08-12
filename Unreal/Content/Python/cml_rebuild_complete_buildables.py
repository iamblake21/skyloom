"""Build complete runtime StaticMeshes and clean Blueprints for every complex placeable.

The general Unity prefab migration used to resolve a nested multi-mesh FBX by
taking its first imported child.  That reduced conveyors, press and drill to a
single roller or beam.  These production bindings deliberately import each
source FBX as one complete visual mesh while leaving the original split meshes
untouched for later animation work.
"""

from __future__ import annotations

import json
from pathlib import Path

import unreal

from cml_material_slots import MaterialSlotIndex


OUTPUT_PATH = "/Game/_Project/Art/Factory/Buildables"
REPORT_NAME = "CompleteBuildablesReport.json"

SPECS = (
    ("BeltStraight", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_Straight.fbx"),
    ("BeltCurve", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_Curve.fbx"),
    ("BeltCurveLeft", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_CurveLeft.fbx"),
    ("BeltIncline", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_Incline.fbx"),
    ("BeltSupport", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_Support.fbx"),
    ("BeltDriveUnit", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_DriveUnit.fbx"),
    ("BeltFunnel", "Assets/_Project/Art/Logistics/BeltKit/Models/MEC_Belt_Funnel.fbx"),
    ("MechanicalPress", "Assets/_Project/Art/MechanicalEra/Models/MEC_MechanicalPress.fbx"),
    ("MechanicalDrill", "Assets/_Project/Art/MechanicalEra/Models/MEC_MechanicalDrill.fbx"),
)


def _fbx_options() -> unreal.FbxImportUI:
    options = unreal.FbxImportUI()
    options.set_editor_property("import_mesh", True)
    options.set_editor_property("import_materials", False)
    options.set_editor_property("import_textures", False)
    options.set_editor_property("import_as_skeletal", False)
    static_data = options.get_editor_property("static_mesh_import_data")
    static_data.set_editor_property("combine_meshes", True)
    static_data.set_editor_property("generate_lightmap_u_vs", True)
    static_data.set_editor_property("import_mesh_lo_ds", False)
    static_data.set_editor_property("transform_vertex_to_absolute", True)
    try:
        static_data.set_editor_property("auto_generate_collision", True)
    except Exception:
        pass
    return options


def _import_mesh(unity_root: Path, source: str, name: str) -> unreal.StaticMesh:
    asset_name = f"SM_{name}_RuntimeVisual"
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", str(unity_root / source))
    task.set_editor_property("destination_path", OUTPUT_PATH)
    task.set_editor_property("destination_name", asset_name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    task.set_editor_property("options", _fbx_options())
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

    expected = f"{OUTPUT_PATH}/{asset_name}.{asset_name}"
    asset = unreal.EditorAssetLibrary.load_asset(expected)
    if not isinstance(asset, unreal.StaticMesh):
        imported = list(task.get_editor_property("imported_object_paths") or [])
        raise RuntimeError(f"complete mesh missing: {expected}; imported={imported}")
    return asset


def _clean_blueprint(name: str, mesh: unreal.StaticMesh) -> unreal.Blueprint:
    asset_name = f"BP_{name}_Runtime"
    object_path = f"{OUTPUT_PATH}/{asset_name}.{asset_name}"
    existing = unreal.EditorAssetLibrary.load_asset(object_path)
    subsystem = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
    if isinstance(existing, unreal.Blueprint):
        blueprint = existing
        handles = subsystem.k2_gather_subobject_data_for_blueprint(blueprint)
        if len(handles) > 1:
            subsystem.delete_subobjects(handles[0], list(handles[1:]), blueprint)
    else:
        factory = unreal.BlueprintFactory()
        factory.set_editor_property("parent_class", unreal.Actor)
        blueprint = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            asset_name, OUTPUT_PATH, unreal.Blueprint, factory
        )
    if not isinstance(blueprint, unreal.Blueprint):
        raise RuntimeError(f"could not create {object_path}")

    handles = subsystem.k2_gather_subobject_data_for_blueprint(blueprint)
    if not handles:
        raise RuntimeError(f"{object_path} has no root handle")
    params = unreal.AddNewSubobjectParams(
        parent_handle=handles[0],
        new_class=unreal.StaticMeshComponent,
        blueprint_context=blueprint,
    )
    handle, failure = subsystem.add_new_subobject(params)
    if not failure.is_empty():
        raise RuntimeError(f"could not add visual to {object_path}: {failure}")
    subsystem.rename_subobject(handle, unreal.Text("CML_RuntimeVisual"))
    data = subsystem.k2_find_subobject_data_from_handle(handle)
    component = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(data)
    component.set_editor_property("static_mesh", mesh)
    component.set_editor_property("relative_location", unreal.Vector(0.0, 0.0, 0.0))
    component.set_editor_property("relative_rotation", unreal.Rotator(0.0, 0.0, 0.0))
    component.set_editor_property("relative_scale3d", unreal.Vector(1.0, 1.0, 1.0))
    component.set_collision_enabled(unreal.CollisionEnabled.QUERY_AND_PHYSICS)
    unreal.BlueprintEditorLibrary.compile_blueprint(blueprint)
    unreal.EditorAssetLibrary.save_loaded_asset(blueprint, only_if_is_dirty=False)
    return blueprint


def _bounds(mesh: unreal.StaticMesh) -> dict[str, list[float]]:
    box = mesh.get_bounding_box()
    minimum = box.min
    maximum = box.max
    size = maximum - minimum
    return {
        "min": [minimum.x, minimum.y, minimum.z],
        "max": [maximum.x, maximum.y, maximum.z],
        "size": [size.x, size.y, size.z],
    }


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    manifest = json.loads(
        (project_dir / "Migration" / "unity_asset_manifest.json").read_text("utf-8")
    )
    unity_root = Path(manifest["unityRoot"])
    materials = MaterialSlotIndex.from_project(project_dir)
    results = []
    for name, source in SPECS:
        mesh = _import_mesh(unity_root, source, name)
        material_issues: list[str] = []
        materials.apply_to_mesh_defaults(mesh, material_issues)
        unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False)
        blueprint = _clean_blueprint(name, mesh)
        result = {
            "name": name,
            "source": source,
            "mesh": mesh.get_path_name(),
            "blueprint": blueprint.get_path_name(),
            "bounds": _bounds(mesh),
            "materialSlots": len(mesh.get_editor_property("static_materials") or []),
            "materialIssues": material_issues,
        }
        results.append(result)
        unreal.log(f"[CML Complete Buildables] {name}: {result}")

    report = {
        "schema": 1,
        "count": len(results),
        "results": results,
    }
    report_path = Path(unreal.Paths.project_saved_dir()) / REPORT_NAME
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    unreal.log(f"[CML Complete Buildables] COMPLETE_BUILDABLES_SUCCEEDED {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

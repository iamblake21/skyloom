"""Import Unity serialized Mesh assets extracted by Tools/extract_unity_mesh_assets.py."""

from __future__ import annotations

import json
import traceback
from pathlib import Path

import unreal


DESTINATION = "/Game/Migration/EmbeddedMeshes"


def _log(message: str) -> None:
    unreal.log(f"[CML Embedded Mesh Migration] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Embedded Mesh Migration] {message}")


def _options() -> unreal.FbxImportUI:
    options = unreal.FbxImportUI()
    options.set_editor_property("import_mesh", True)
    options.set_editor_property("import_materials", False)
    options.set_editor_property("import_textures", False)
    options.set_editor_property("import_as_skeletal", False)
    static_data = options.get_editor_property("static_mesh_import_data")
    static_data.set_editor_property("combine_meshes", True)
    static_data.set_editor_property("generate_lightmap_u_vs", False)
    static_data.set_editor_property("auto_generate_collision", False)
    static_data.set_editor_property("import_mesh_lods", False)
    # The extractor has already converted Unity metres/axes into Unreal
    # centimetres/axes.  A second scene conversion would rotate and rescale it.
    static_data.set_editor_property("convert_scene", False)
    static_data.set_editor_property("convert_scene_unit", False)
    return options


def _import_one(project_dir: Path, entry: dict) -> dict:
    filename = project_dir / Path(entry["obj"])
    if not filename.is_file():
        raise FileNotFoundError(filename)
    name = f"SM_{entry['name']}"
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", str(filename))
    task.set_editor_property("destination_path", DESTINATION)
    task.set_editor_property("destination_name", name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    task.set_editor_property("options", _options())
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    objects = list(task.get_editor_property("imported_object_paths") or [])
    meshes = [
        object_path
        for object_path in objects
        if isinstance(unreal.EditorAssetLibrary.load_asset(object_path), unreal.StaticMesh)
    ]
    if len(meshes) != 1:
        raise RuntimeError(f"Expected one StaticMesh for {filename}, got {objects}")
    mesh = unreal.EditorAssetLibrary.load_asset(meshes[0])
    unreal.EditorAssetLibrary.set_metadata_tag(mesh, "CML.UnityGuid", entry["guid"])
    unreal.EditorAssetLibrary.set_metadata_tag(mesh, "CML.UnitySource", entry["source"])
    unreal.EditorAssetLibrary.set_metadata_tag(mesh, "CML.SourceObjSha256", entry["sha256"])
    unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False)
    return {
        **entry,
        "status": "imported",
        "objects": meshes,
    }


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    extract_report = project_dir / "Migration" / "unity_embedded_mesh_extract_report.json"
    report_path = project_dir / "Migration" / "unity_embedded_mesh_import_report.json"
    if not extract_report.is_file():
        _error(f"Missing {extract_report}; run Tools/extract_unity_mesh_assets.py first")
        return 2
    extract = json.loads(extract_report.read_text("utf-8"))
    results: list[dict] = []
    for entry in extract["results"]:
        try:
            result = _import_one(project_dir, entry)
            results.append(result)
            _log(f"{entry['source']} -> {result['objects'][0]}")
        except Exception as exception:
            _error(f"{entry['source']}: {exception}")
            results.append({**entry, "status": "failed", "objects": [], "error": str(exception)})
        unreal.SystemLibrary.collect_garbage()
    report = {
        "schema": 1,
        "requested": len(extract["results"]),
        "imported": sum(item["status"] == "imported" for item in results),
        "failed": sum(item["status"] != "imported" for item in results),
        "results": results,
    }
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    temporary.replace(report_path)
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    _log(f"Complete: imported={report['imported']}, failed={report['failed']}")
    return 0 if report["failed"] == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_EMBEDDED_MESH_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_EMBEDDED_MESH_IMPORT_SUCCEEDED")

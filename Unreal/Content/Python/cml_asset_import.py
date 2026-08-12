"""Deterministic Unity-to-Unreal source asset importer.

Run with UnrealEditor-Cmd and the PythonScriptPlugin. The importer never writes
to the Unity project. It preserves source paths, GUIDs and SHA-256 values in a
machine-readable report so later prefab/material conversion can resolve assets
without relying on display names.
"""

from __future__ import annotations

import json
import os
import re
import sys
import traceback
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

import unreal


MESH_EXTENSIONS = {".fbx", ".obj", ".glb"}
TEXTURE_EXTENSIONS = {".png"}
IMPORTABLE_EXTENSIONS = MESH_EXTENSIONS | TEXTURE_EXTENSIONS
DEFAULT_KINDS = {"mesh", "texture"}
NORMAL_SUFFIXES = ("_n", "_normal", "_norm", "_normalmap")
MASK_MARKERS = ("_mask", "_orm", "_rma", "_mra", "_ao", "_rough", "_metal")


@dataclass(frozen=True)
class ImportRecord:
    source: str
    guid: str
    kind: str
    sha256: str
    destination_path: str
    destination_name: str


def _log(message: str) -> None:
    unreal.log(f"[CML Migration] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Migration] {message}")


def _sanitize_segment(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", value.strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Asset"
    if value[0].isdigit():
        value = f"A_{value}"
    return value


def _destination_for(source: str) -> tuple[str, str]:
    path = PurePosixPath(source)
    parts = list(path.parts)
    if parts and parts[0].lower() == "assets":
        parts = parts[1:]
    if parts and parts[0] == "_Project":
        parts[0] = "Project"
    directory = [_sanitize_segment(part) for part in parts[:-1]]
    name = _sanitize_segment(Path(parts[-1]).stem)
    destination = "/Game/Migrated"
    if directory:
        destination += "/" + "/".join(directory)
    return destination, name


def _parse_kinds() -> set[str]:
    raw = os.environ.get("CML_IMPORT_KINDS", "mesh,texture")
    kinds = {item.strip().lower() for item in raw.split(",") if item.strip()}
    return kinds or DEFAULT_KINDS


def _is_skeletal(source: str) -> bool:
    normalized = source.replace("\\", "/").lower()
    stem = Path(source).stem.lower()
    return (
        "/characters/" in normalized
        or "/firstpersonhands/" in normalized
        or stem.startswith("chr_")
        or stem.startswith("sk_")
    )


def _make_fbx_options(record: ImportRecord) -> unreal.FbxImportUI:
    skeletal = _is_skeletal(record.source)
    options = unreal.FbxImportUI()
    options.set_editor_property("import_mesh", True)
    options.set_editor_property("import_materials", False)
    options.set_editor_property("import_textures", False)
    options.set_editor_property("import_as_skeletal", skeletal)
    options.set_editor_property("import_animations", skeletal)
    if skeletal:
        skeletal_data = options.get_editor_property("skeletal_mesh_import_data")
        skeletal_data.set_editor_property("import_morph_targets", True)
    else:
        static_data = options.get_editor_property("static_mesh_import_data")
        static_data.set_editor_property("combine_meshes", False)
        static_data.set_editor_property("generate_lightmap_u_vs", True)
        static_data.set_editor_property("import_mesh_lo_ds", True)
    return options


def _make_task(record: ImportRecord, unity_root: Path) -> unreal.AssetImportTask:
    source_file = unity_root / Path(record.source)
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", str(source_file))
    task.set_editor_property("destination_path", record.destination_path)
    task.set_editor_property("destination_name", record.destination_name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    if source_file.suffix.lower() == ".fbx":
        task.set_editor_property("options", _make_fbx_options(record))
    return task


def _configure_texture(object_path: str, source: str) -> None:
    asset = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(asset, unreal.Texture):
        return
    stem = Path(source).stem.lower()
    is_normal = stem.endswith(NORMAL_SUFFIXES)
    is_mask = any(marker in stem for marker in MASK_MARKERS)
    if is_normal:
        asset.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP)
        asset.set_editor_property("srgb", False)
    elif is_mask:
        asset.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_MASKS)
        asset.set_editor_property("srgb", False)
    asset.set_editor_property("never_stream", False)
    unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=True)


def _package_path(object_path: str) -> str:
    """Return /Game/Package/Asset from /Game/Package/Asset.Asset."""
    return object_path.split(".", 1)[0]


def _consolidate_static_mesh_lods(object_paths: list[str]) -> tuple[list[str], list[dict]]:
    """Collapse FBX nodes named *_LODn into one production StaticMesh.

    Unity source FBXs frequently contain sibling mesh nodes named LOD0, LOD1,
    and so on instead of an FBX LODGroup. Unreal correctly imports their
    geometry, but it creates one asset per node. We retain the LOD0 asset as the
    canonical object, copy every lower-detail source mesh into its matching LOD
    slot using Unreal's editor subsystem, verify the result, and only then
    remove the generated sibling assets. The Unity source files are untouched.
    """
    static_mesh_subsystem = unreal.get_editor_subsystem(unreal.StaticMeshEditorSubsystem)
    passthrough: list[str] = []
    grouped: dict[str, dict[int, str]] = {}

    for object_path in object_paths:
        asset = unreal.EditorAssetLibrary.load_asset(object_path)
        if not isinstance(asset, unreal.StaticMesh):
            passthrough.append(object_path)
            continue
        asset_name = str(asset.get_name())
        match = re.match(r"^(?P<base>.+)_LOD(?P<index>[0-9]+)$", asset_name, re.IGNORECASE)
        if not match:
            passthrough.append(object_path)
            continue
        base_key = f"{str(asset.get_path_name()).rsplit('/', 1)[0]}/{match.group('base')}"
        grouped.setdefault(base_key.lower(), {})[int(match.group("index"))] = object_path

    canonical_paths = list(passthrough)
    lod_groups: list[dict] = []
    for _, lod_paths in sorted(grouped.items()):
        # A lone suffix or a set without LOD0 is not safe to reinterpret.
        if 0 not in lod_paths or len(lod_paths) < 2:
            canonical_paths.extend(path for _, path in sorted(lod_paths.items()))
            continue

        canonical_path = lod_paths[0]
        canonical_mesh = unreal.EditorAssetLibrary.load_asset(canonical_path)
        if not isinstance(canonical_mesh, unreal.StaticMesh):
            raise RuntimeError(f"LOD0 is not a StaticMesh: {canonical_path}")

        if not static_mesh_subsystem.remove_lods(canonical_mesh):
            raise RuntimeError(f"Unable to reset generated LODs: {canonical_path}")

        applied: list[int] = [0]
        for lod_index, source_path in sorted(lod_paths.items()):
            if lod_index == 0:
                continue
            source_mesh = unreal.EditorAssetLibrary.load_asset(source_path)
            if not isinstance(source_mesh, unreal.StaticMesh):
                raise RuntimeError(f"LOD source is not a StaticMesh: {source_path}")
            actual_index = static_mesh_subsystem.set_lod_from_static_mesh(
                canonical_mesh,
                lod_index,
                source_mesh,
                0,
                True,
            )
            if actual_index != lod_index:
                raise RuntimeError(
                    f"LOD consolidation returned {actual_index}, expected {lod_index}: "
                    f"{source_path} -> {canonical_path}"
                )
            applied.append(lod_index)

        expected_count = max(applied) + 1
        actual_count = static_mesh_subsystem.get_lod_count(canonical_mesh)
        if actual_count != expected_count:
            raise RuntimeError(
                f"LOD count mismatch for {canonical_path}: "
                f"expected={expected_count}, actual={actual_count}"
            )

        unreal.EditorAssetLibrary.save_loaded_asset(canonical_mesh, only_if_is_dirty=False)
        for lod_index, source_path in sorted(lod_paths.items()):
            if lod_index == 0:
                continue
            if not unreal.EditorAssetLibrary.delete_asset(_package_path(source_path)):
                raise RuntimeError(f"Unable to remove generated LOD sibling: {source_path}")

        canonical_paths.append(canonical_path)
        lod_groups.append(
            {
                "canonical": canonical_path,
                "lodCount": actual_count,
                "sourceObjects": [path for _, path in sorted(lod_paths.items())],
            }
        )

    return sorted(set(canonical_paths)), lod_groups


def _load_records(manifest: dict, kinds: set[str], limit: int) -> list[ImportRecord]:
    records: list[ImportRecord] = []
    occupied: dict[tuple[str, str], str] = {}
    for entry in manifest["entries"]:
        source = entry["source"]
        extension = Path(source).suffix.lower()
        if extension in MESH_EXTENSIONS:
            kind = "mesh"
        elif extension in TEXTURE_EXTENSIONS:
            kind = "texture"
        else:
            continue
        if kind not in kinds:
            continue
        destination_path, destination_name = _destination_for(source)
        # FBX/OBJ/GLB files may contain hundreds of named child meshes. Unreal
        # uses those node names for the generated assets and ignores the task's
        # destination_name, so sibling source files can otherwise overwrite one
        # another. A source-file container makes the mapping collision-free
        # while preserving the original Unity hierarchy.
        if kind == "mesh":
            destination_path += "/" + destination_name
        key = (destination_path.lower(), destination_name.lower())
        previous = occupied.get(key)
        if previous and previous != source:
            destination_name = f"{destination_name}_{entry['guid'][:8]}"
            key = (destination_path.lower(), destination_name.lower())
        occupied[key] = source
        records.append(
            ImportRecord(
                source=source,
                guid=entry["guid"],
                kind=kind,
                sha256=entry["sha256"],
                destination_path=destination_path,
                destination_name=destination_name,
            )
        )
        if limit > 0 and len(records) >= limit:
            break
    return records


def _write_report(
    report_path: Path,
    unity_root: Path,
    kinds: set[str],
    requested: int,
    results: list[dict],
) -> dict:
    summary = {
        "schema": 2,
        "unityRoot": str(unity_root),
        "requestedKinds": sorted(kinds),
        "requested": requested,
        "processed": len(results),
        "complete": len(results) == requested,
        "imported": sum(item["status"] == "imported" for item in results),
        "failed": sum(item["status"] not in {"imported"} for item in results),
        "results": results,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = report_path.with_suffix(".json.tmp")
    with temporary_path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(summary, stream, indent=2, ensure_ascii=False)
        stream.write("\n")
    temporary_path.replace(report_path)
    return summary


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    manifest_path = project_dir / "Migration" / "unity_asset_manifest.json"
    report_path = project_dir / "Migration" / "unity_asset_import_report.json"
    kinds = _parse_kinds()
    limit = int(os.environ.get("CML_IMPORT_LIMIT", "0") or 0)
    batch_size = max(1, int(os.environ.get("CML_IMPORT_BATCH_SIZE", "32") or 32))

    with manifest_path.open("r", encoding="utf-8") as stream:
        manifest = json.load(stream)
    unity_root = Path(manifest["unityRoot"])
    records = _load_records(manifest, kinds, limit)
    _log(f"Importing {len(records)} assets; kinds={sorted(kinds)}, batch={batch_size}")

    results: list[dict] = []
    asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
    for start in range(0, len(records), batch_size):
        batch = records[start : start + batch_size]
        tasks: list[unreal.AssetImportTask] = []
        task_records: list[ImportRecord] = []
        for record in batch:
            source_file = unity_root / Path(record.source)
            if not source_file.is_file():
                results.append({**record.__dict__, "status": "missing-source", "objects": []})
                continue
            try:
                tasks.append(_make_task(record, unity_root))
                task_records.append(record)
            except Exception as exception:
                results.append(
                    {
                        **record.__dict__,
                        "status": "task-error",
                        "error": str(exception),
                        "objects": [],
                    }
                )
        if not tasks:
            continue
        asset_tools.import_asset_tasks(tasks)
        for record, task in zip(task_records, tasks):
            object_paths = list(task.get_editor_property("imported_object_paths") or [])
            status = "imported" if object_paths else "failed"
            result = {
                **record.__dict__,
                "status": status,
                "objects": object_paths,
                "lodGroups": [],
            }
            if status == "imported" and record.kind == "mesh" and not _is_skeletal(record.source):
                try:
                    canonical_paths, lod_groups = _consolidate_static_mesh_lods(object_paths)
                    result["objects"] = canonical_paths
                    result["lodGroups"] = lod_groups
                except Exception as exception:
                    result["status"] = "postprocess-error"
                    result["error"] = str(exception)
                    _error(f"LOD post-process failed for {record.source}: {exception}")
            results.append(result)
            if record.kind == "texture":
                for object_path in object_paths:
                    _configure_texture(object_path, record.source)
        _log(f"Processed {min(start + batch_size, len(records))}/{len(records)}")
        # Large migrations can otherwise retain imported package references for
        # the entire commandlet lifetime and force Windows to grow the pagefile
        # on the system drive. Every task saves its own package, so collecting
        # between batches is both safe and substantially lowers peak memory.
        del tasks
        del task_records
        unreal.SystemLibrary.collect_garbage()
        _write_report(report_path, unity_root, kinds, len(records), results)

    summary = _write_report(report_path, unity_root, kinds, len(records), results)
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    _log(
        f"Complete: imported={summary['imported']}, failed={summary['failed']}; "
        f"report={report_path}"
    )
    return 0 if summary["failed"] == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    # Unreal's Python commandlet does not propagate sys.exit reliably; the log
    # marker gives CI a deterministic failure signal.
    _error(f"CML_ASSET_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_ASSET_IMPORT_SUCCEEDED")

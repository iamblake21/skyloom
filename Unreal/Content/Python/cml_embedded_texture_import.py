"""Import Unity textures that lived inside .asset YAML into Unreal.

Nine Texture2D objects were serialised directly into Unity `.asset` files
instead of referencing a source image, so the ordinary source importer could
never see them. `Tools/extract_unity_embedded_textures.py` decodes their raw
RGBA32 payload into lossless PNGs; this script imports those PNGs and, crucially,
registers them in `unity_asset_import_report.json` under their original Unity
GUID so material conversion resolves them exactly like any other texture.

Run with UnrealEditor-Cmd and the PythonScriptPlugin. Never writes to Unity.
"""

from __future__ import annotations

import hashlib
import json
import re
import traceback
from pathlib import Path, PurePosixPath

import unreal


REPORT_SCHEMA = 1
IMPORT_KIND = "embedded-texture"


def _log(message: str) -> None:
    unreal.log(f"[CML Embedded Textures] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Embedded Textures] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", value.strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Asset"
    if value[0].isdigit():
        value = f"A_{value}"
    return value


def _destination_for(source: str, name: str) -> tuple[str, str]:
    """Mirror the Unity directory layout used by the source asset importer."""
    parts = list(PurePosixPath(source).parts)
    if parts and parts[0].lower() == "assets":
        parts = parts[1:]
    if parts and parts[0] == "_Project":
        parts[0] = "Project"
    directory = [_sanitize(part) for part in parts[:-1]]
    destination = "/Game/Migrated"
    if directory:
        destination += "/" + "/".join(directory)
    return destination, _sanitize(name)


def _import_png(png_path: Path, destination_path: str, destination_name: str) -> list[str]:
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", str(png_path))
    task.set_editor_property("destination_path", destination_path)
    task.set_editor_property("destination_name", destination_name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    return list(task.get_editor_property("imported_object_paths") or [])


def _configure(object_path: str, record: dict) -> unreal.Texture2D:
    asset = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(asset, unreal.Texture2D):
        raise RuntimeError(f"Imported object is not a Texture2D: {object_path}")

    if record["normalMap"]:
        # Unity and Unreal both expect OpenGL-style tangent normals (+Y up), so
        # the channels transfer unchanged; only the sampler config differs.
        asset.set_editor_property(
            "compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP
        )
        asset.set_editor_property("srgb", False)
    else:
        asset.set_editor_property(
            "compression_settings", unreal.TextureCompressionSettings.TC_DEFAULT
        )
        asset.set_editor_property("srgb", True)

    # These are hand-authored 8px..512px terrain palettes; letting Unreal drop
    # the top mip on a low texture pool would visibly change the ground colour.
    asset.set_editor_property("never_stream", False)
    asset.set_editor_property("lod_group", unreal.TextureGroup.TEXTUREGROUP_WORLD)

    unreal.EditorAssetLibrary.set_metadata_tag(asset, "CML.UnityGuid", record["guid"])
    unreal.EditorAssetLibrary.set_metadata_tag(asset, "CML.UnitySha256", record["sha256"])
    unreal.EditorAssetLibrary.set_metadata_tag(asset, "CML.UnitySource", record["source"])
    unreal.EditorAssetLibrary.set_metadata_tag(asset, "CML.Origin", "UnityEmbeddedTexture2D")
    unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False)
    return asset


def _verify(asset: unreal.Texture2D, record: dict) -> None:
    width = int(asset.blueprint_get_size_x())
    height = int(asset.blueprint_get_size_y())
    if (width, height) != (record["width"], record["height"]):
        raise RuntimeError(
            f"Imported size {width}x{height} does not match Unity source "
            f"{record['width']}x{record['height']}"
        )
    expected_srgb = not record["normalMap"]
    if bool(asset.get_editor_property("srgb")) != expected_srgb:
        raise RuntimeError(f"sRGB flag did not stick (expected {expected_srgb})")


def _merge_into_asset_report(report_path: Path, results: list[dict]) -> int:
    """Make embedded textures resolvable by the material converter.

    The material converter looks up textures by Unity GUID in the source asset
    import report. Embedded textures have no importable Unity source file, so
    they are merged in here under their own kind, keyed by GUID and replaced
    in place on re-runs to keep the operation idempotent.
    """
    report = json.loads(report_path.read_text(encoding="utf-8"))
    existing = report.get("results", [])
    by_source = {item["source"]: index for index, item in enumerate(existing)}

    merged = 0
    for record in results:
        if record["status"] != "imported":
            continue
        entry = {
            "source": record["source"],
            "guid": record["guid"],
            "kind": IMPORT_KIND,
            "sha256": record["sha256"],
            "destination_path": record["destination_path"],
            "destination_name": record["destination_name"],
            "status": "imported",
            "objects": record["objects"],
            "lodGroups": [],
            "embeddedPng": record["png"],
            "embeddedPngSha256": record["pngSha256"],
        }
        index = by_source.get(record["source"])
        if index is None:
            existing.append(entry)
        else:
            existing[index] = entry
        merged += 1

    report["results"] = existing
    report["processed"] = len(existing)
    report["imported"] = sum(item["status"] == "imported" for item in existing)
    report["failed"] = sum(item["status"] != "imported" for item in existing)
    report["embeddedTextures"] = merged
    # `requested` counts the source-file pass; embedded textures are additional.
    report["complete"] = report["imported"] >= report["requested"]

    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)
    return merged


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    extract_report_path = project_dir / "Migration" / "unity_embedded_texture_extract_report.json"
    asset_report_path = project_dir / "Migration" / "unity_asset_import_report.json"
    report_path = project_dir / "Migration" / "unity_embedded_texture_import_report.json"

    extract_report = json.loads(extract_report_path.read_text(encoding="utf-8"))
    records = [item for item in extract_report["results"] if item["status"] == "extracted"]
    _log(f"Importing {len(records)} embedded Unity textures")

    results: list[dict] = []
    for record in records:
        png_path = project_dir / record["output"]
        destination_path, destination_name = _destination_for(record["source"], record["name"])
        result = {
            "source": record["source"],
            "guid": record["guid"],
            "sha256": record["sha256"],
            "png": record["output"],
            "pngSha256": record["outputSha256"],
            "destination_path": destination_path,
            "destination_name": destination_name,
            "normalMap": record["normalMap"],
            "objects": [],
        }
        try:
            if not png_path.is_file():
                raise RuntimeError(f"Extracted PNG is missing: {png_path}")
            digest = hashlib.sha256(png_path.read_bytes()).hexdigest()
            if digest != record["outputSha256"]:
                raise RuntimeError(
                    "Extracted PNG no longer matches the extraction report; "
                    "re-run Tools/extract_unity_embedded_textures.py"
                )
            objects = _import_png(png_path, destination_path, destination_name)
            if not objects:
                raise RuntimeError("Unreal returned no imported object")
            asset = _configure(objects[0], record)
            _verify(asset, record)
            result["objects"] = objects
            result["status"] = "imported"
            _log(f"{record['name']} -> {objects[0]}")
        except Exception as exception:
            result["status"] = "failed"
            result["error"] = str(exception)
            _error(f"{record['source']}: {exception}")
        results.append(result)

    merged = _merge_into_asset_report(asset_report_path, results)

    report = {
        "schema": REPORT_SCHEMA,
        "requested": len(records),
        "imported": sum(item["status"] == "imported" for item in results),
        "failed": sum(item["status"] != "imported" for item in results),
        "mergedIntoAssetReport": merged,
        "results": results,
    }
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
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
    _error(f"CML_EMBEDDED_TEXTURE_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_EMBEDDED_TEXTURE_IMPORT_SUCCEEDED")

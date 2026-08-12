"""Build Unreal Landscapes from the extracted Unity terrains.

`Tools/extract_unity_terrain.py` does the reading and the coordinate change; this
script only places the result. It opens the level each terrain belongs to, hands
the raw heightmap and weightmaps to `UCMLLandscapeImportLibrary`, and saves.

The heavy lifting is in C++ because `ALandscape::Import` has no scripting
exposure — it is the one step of the migration Python cannot drive on its own.

Run with UnrealEditor-Cmd and the PythonScriptPlugin. Never writes to Unity.
"""

from __future__ import annotations

import json
import os
import re
import sys
import traceback
from pathlib import Path

import unreal

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from cml_unity_yaml import load_unity_documents, parse_reference

MAP_ROOT = "/Game/Maps"
LAYER_INFO_ROOT = "/Game/Migration/LandscapeLayers"
MATERIAL_ROOT = "/Game/Migration/LandscapeMaterials"
TEXTURE_ROOT = "/Game/Migration/LandscapeTextures"
LANDSCAPE_MASTER = (
    "/Game/Migration/Masters/"
    "M_CML_Env_TerrainSplat.M_CML_Env_TerrainSplat"
)
LANDMASS_CLIFF_ALBEDO = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Landmass/Textures/"
    "T_CML_LandmassCliff_Albedo.T_CML_LandmassCliff_Albedo"
)
LANDMASS_CLIFF_NORMAL = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Landmass/Textures/"
    "T_CML_LandmassCliff_Normal.T_CML_LandmassCliff_Normal"
)
LANDMASS_VARIATION = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Landmass/Textures/"
    "T_CML_LandmassGrassVariation.T_CML_LandmassGrassVariation"
)


def _log(message: str) -> None:
    unreal.log(f"[CML Terrain Migration] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Terrain Migration] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", str(value).strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Object"
    return f"A_{value}" if value[0].isdigit() else value


def _project_dir() -> Path:
    return Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))


def _load_indexes(project_dir: Path) -> tuple[dict[str, str], dict[str, str], dict[str, str], Path]:
    """Load GUID indexes without relying on display names."""
    manifest = json.loads(
        (project_dir / "Migration" / "unity_asset_manifest.json").read_text("utf-8")
    )
    unity_root = Path(manifest["unityRoot"])
    report = json.loads(
        (project_dir / "Migration" / "unity_material_import_report.json").read_text("utf-8")
    )
    materials = {
        item["guid"]: item["object"]
        for item in report["results"]
        if item["status"] == "converted"
    }
    sources = {item["guid"]: item["source"] for item in manifest["entries"] if item.get("guid")}

    asset_report = json.loads(
        (project_dir / "Migration" / "unity_asset_import_report.json").read_text("utf-8")
    )
    entries_by_source = {item["source"]: item for item in manifest["entries"]}
    textures: dict[str, str] = {}
    for item in asset_report["results"]:
        if item.get("status") != "imported" or not item.get("objects"):
            continue
        guid = item.get("guid") or (entries_by_source.get(item.get("source", ""), {}) or {}).get("guid")
        entry = entries_by_source.get(item.get("source", ""))
        if guid and (item.get("kind") == "embedded-texture" or (entry and entry["kind"] == "texture")):
            textures[guid] = item["objects"][0]
    return materials, textures, sources, unity_root


def _import_control_texture(terrain: dict, terrain_dir: Path):
    source = terrain_dir / terrain["landscape"]["controlFile"]
    if not source.is_file():
        raise RuntimeError(f"Missing global terrain control map {source}")
    name = f"T_{_sanitize(terrain['name'])}_Control"
    object_path = f"{TEXTURE_ROOT}/{name}.{name}"
    task = unreal.AssetImportTask()
    task.set_editor_property("filename", str(source))
    task.set_editor_property("destination_path", TEXTURE_ROOT)
    task.set_editor_property("destination_name", name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    texture = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(texture, unreal.Texture2D):
        raise RuntimeError(f"Control-map import did not create {object_path}")
    texture.set_editor_property("srgb", False)
    texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_MASKS)
    texture.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS)
    texture.set_editor_property("never_stream", True)
    texture.set_editor_property("filter", unreal.TextureFilter.TF_BILINEAR)
    texture.set_editor_property("address_x", unreal.TextureAddress.TA_CLAMP)
    texture.set_editor_property("address_y", unreal.TextureAddress.TA_CLAMP)
    unreal.EditorAssetLibrary.set_metadata_tag(texture, "CML.UnityTerrainGuid", terrain["guid"])
    unreal.EditorAssetLibrary.save_loaded_asset(texture, only_if_is_dirty=False)
    return texture


def _terrain_layer(unity_root: Path, source: str) -> dict:
    documents = load_unity_documents(unity_root / Path(source))
    document = next((item for item in documents if item.type_name == "TerrainLayer"), None)
    if document is None:
        raise RuntimeError(f"{source} contains no TerrainLayer")
    tile_size = document.get("m_TileSize") or {}
    tile_offset = document.get("m_TileOffset") or {}
    remap_min = document.get("m_DiffuseRemapMin") or {}
    remap_max = document.get("m_DiffuseRemapMax") or {}
    return {
        "name": str(document.get("m_Name", Path(source).stem)),
        "diffuseGuid": parse_reference(document.get("m_DiffuseTexture")).guid,
        "normalGuid": parse_reference(document.get("m_NormalMapTexture")).guid,
        "tileSize": (float(tile_size.get("x", 1.0)), float(tile_size.get("y", 1.0))),
        "tileOffset": (float(tile_offset.get("x", 0.0)), float(tile_offset.get("y", 0.0))),
        "normalScale": float(document.get("m_NormalScale", 1.0)),
        "remapScale": tuple(
            float(remap_max.get(axis, 1.0)) - float(remap_min.get(axis, 0.0))
            for axis in "xyzw"
        ),
    }


def _load_texture(object_path: str, description: str):
    texture = unreal.EditorAssetLibrary.load_asset(object_path) if object_path else None
    if not isinstance(texture, unreal.Texture):
        raise RuntimeError(f"Missing migrated texture for {description}: {object_path or 'unresolved GUID'}")
    return texture


def _create_landscape_material(
    terrain: dict,
    placement: dict,
    base_material_path: str,
    control_texture,
    textures: dict[str, str],
    sources: dict[str, str],
    unity_root: Path,
):
    base_material = unreal.EditorAssetLibrary.load_asset(base_material_path)
    if not isinstance(base_material, unreal.MaterialInterface):
        raise RuntimeError(f"Terrain base material is missing: {base_material_path}")

    name = f"MI_{_sanitize(terrain['name'])}"
    object_path = f"{MATERIAL_ROOT}/{name}.{name}"
    instance = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(instance, unreal.MaterialInstanceConstant):
        instance = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            name,
            MATERIAL_ROOT,
            unreal.MaterialInstanceConstant,
            unreal.MaterialInstanceConstantFactoryNew(),
        )
    if not isinstance(instance, unreal.MaterialInstanceConstant):
        raise RuntimeError(f"Unable to create {object_path}")
    unreal.MaterialEditingLibrary.set_material_instance_parent(instance, base_material)
    unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
        instance, "_Control", control_texture
    )

    size = terrain["unity"]["size"]
    position = placement["unityPosition"]
    origin_inv_size = unreal.LinearColor(
        float(position["x"]), float(position["z"]), 1.0 / float(size["x"]), 1.0 / float(size["z"])
    )
    unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
        instance, "_CMLTerrainOriginInvSize", origin_inv_size
    )

    # The dedicated Landscape master owns these parameters directly.  They are
    # not present in Unity's TerrainLayer array, so relying only on the three
    # painted top layers silently leaves the cliff branch on the master's white
    # and flat-normal defaults.  Bind the clean-room textures used by the
    # migrated landmass materials explicitly so Landscape slopes and modular
    # cliffs share the same authored rock family.
    for parameter, asset_path, description in (
        ("_LandmassCliffAlbedo", LANDMASS_CLIFF_ALBEDO, "landmass cliff albedo"),
        ("_LandmassCliffNormal", LANDMASS_CLIFF_NORMAL, "landmass cliff normal"),
        ("_LandmassVariationMask", LANDMASS_VARIATION, "landmass variation mask"),
        # Clean-room substitute for the source T_NoiseRough.  It is sampled at
        # its own 8 m world scale; using the same deterministic source-family
        # mask keeps the blend stable without copying a proprietary texture.
        ("_TerrainBlendNoise", LANDMASS_VARIATION, "terrain blend noise"),
    ):
        texture = _load_texture(asset_path, description)
        unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
            instance, parameter, texture
        )

    # The fourth Unity layer is the authored cliff layer.  The port reconstructs
    # that layer from world normal exactly as the Unity shader did, so only the
    # three painted top layers feed _Splat0..2.
    for index, layer in enumerate(terrain["landscape"]["layers"][:3]):
        source = sources.get(layer["guid"])
        if not source:
            raise RuntimeError(f"TerrainLayer GUID {layer['guid']} has no manifest source")
        spec = _terrain_layer(unity_root, source)
        diffuse = _load_texture(textures.get(spec["diffuseGuid"], ""), f"{spec['name']} diffuse")
        normal = _load_texture(textures.get(spec["normalGuid"], ""), f"{spec['name']} normal")
        unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
            instance, f"_Splat{index}", diffuse
        )
        unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
            instance, f"_Normal{index}", normal
        )
        unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(
            instance, f"_NormalScale{index}", spec["normalScale"]
        )
        unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
            instance, f"_DiffuseRemapScale{index}", unreal.LinearColor(*spec["remapScale"])
        )
        tile_x = spec["tileSize"][0] or 1.0
        tile_y = spec["tileSize"][1] or 1.0
        st = unreal.LinearColor(
            float(size["x"]) / tile_x,
            float(size["z"]) / tile_y,
            spec["tileOffset"][0] / tile_x,
            spec["tileOffset"][1] / tile_y,
        )
        unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
            instance, f"_Splat{index}_ST", st
        )

    unreal.EditorAssetLibrary.set_metadata_tag(instance, "CML.UnityTerrainGuid", terrain["guid"])
    unreal.EditorAssetLibrary.set_metadata_tag(instance, "CML.ControlMap", control_texture.get_path_name())
    unreal.EditorAssetLibrary.save_loaded_asset(instance, only_if_is_dirty=False)
    return instance


def _import_one(
    terrain: dict,
    placement: dict,
    terrain_dir: Path,
    materials: dict,
    textures: dict,
    sources: dict,
    unity_root: Path,
) -> dict:
    map_override = os.environ.get("CML_TERRAIN_TARGET_MAP", "").strip()
    map_name = _sanitize(Path(placement["scene"]).stem)
    map_path = map_override or f"{MAP_ROOT}/{map_name}"

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(map_path):
        raise RuntimeError(f"Unable to open level {map_path}")

    editor = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    world = editor.get_editor_world()
    if world is None:
        raise RuntimeError("No editor world after loading the level")

    landscape = terrain["landscape"]
    layers = []
    for layer in landscape["layers"]:
        entry = unreal.CMLLandscapeLayerImport()
        entry.set_editor_property("name", layer["name"])
        entry.set_editor_property("weight_file", str(terrain_dir / layer["weightFile"]))
        layers.append(entry)
    visibility_file = landscape.get("visibilityFile", "")
    if visibility_file:
        entry = unreal.CMLLandscapeLayerImport()
        entry.set_editor_property("name", "__LANDSCAPE_VISIBILITY__")
        entry.set_editor_property("weight_file", str(terrain_dir / visibility_file))
        entry.set_editor_property("is_visibility", True)
        layers.append(entry)

    # A Unity material instance is not a valid Landscape master contract.  The
    # source scene currently points at a ReferenceMatch material whose graph
    # samples TexCoord0 directly and has no LandscapeVisibilityMask.  Parenting
    # the generated Landscape MI to that asset caused component-local control
    # UVs, black/default terrain and a broken hole material.  Always parent the
    # imported heightfield to the dedicated Landscape master; the Unity
    # TerrainLayer textures and values are copied below as overrides.
    material = LANDSCAPE_MASTER
    if not isinstance(unreal.EditorAssetLibrary.load_asset(material), unreal.Material):
        raise RuntimeError(f"Dedicated Landscape master is missing: {material}")
    control_texture = _import_control_texture(terrain, terrain_dir)
    landscape_material = _create_landscape_material(
        terrain, placement, material, control_texture, textures, sources, unity_root
    )

    location = placement["unrealLocation"]
    scale = landscape["drawScale"]
    actor = unreal.CMLLandscapeImportLibrary.import_landscape_from_raw_files(
        world,
        placement["actorName"],
        str(terrain_dir / landscape["heightmapFile"]),
        landscape["resolution"],
        landscape["sectionSizeQuads"],
        landscape["subsectionsPerComponent"],
        unreal.Vector(location["x"], location["y"], location["z"]),
        unreal.Vector(scale["x"], scale["y"], scale["z"]),
        layers,
        LAYER_INFO_ROOT,
        landscape_material.get_path_name(),
    )
    if actor is None:
        raise RuntimeError("The landscape import returned nothing; see the log above")

    level_editor.save_current_level()
    return {
        "status": "converted",
        "terrain": terrain["name"],
        "scene": placement["scene"],
        "level": map_path,
        "actor": placement["actorName"],
        "resolution": landscape["resolution"],
        "layers": [layer["name"] for layer in landscape["layers"]],
        "visibility": visibility_file or None,
        "material": landscape_material.get_path_name(),
        "control": control_texture.get_path_name(),
    }


def main() -> int:
    project_dir = _project_dir()
    extract_path = project_dir / "Migration" / "unity_terrain_extract_report.json"
    if not extract_path.is_file():
        _error(f"{extract_path} is missing; run Tools/extract_unity_terrain.py first")
        return 2
    extract = json.loads(extract_path.read_text(encoding="utf-8"))
    materials, textures, sources, unity_root = _load_indexes(project_dir)

    results: list[dict] = []
    for terrain in extract["terrains"]:
        terrain_dir = project_dir / "Migration" / "UnityTerrain" / terrain["name"]
        for placement in terrain["placements"]:
            try:
                results.append(
                    _import_one(
                        terrain, placement, terrain_dir,
                        materials, textures, sources, unity_root,
                    )
                )
                _log(f"{terrain['name']} -> {results[-1]['level']}")
            except Exception as exception:  # noqa: BLE001 - reported per placement
                _error(f"{terrain['name']} in {placement['scene']}: {exception}")
                _error(traceback.format_exc())
                results.append(
                    {
                        "status": "failed",
                        "terrain": terrain["name"],
                        "scene": placement["scene"],
                        "error": str(exception),
                    }
                )

    failed = sum(item["status"] != "converted" for item in results)
    report = {
        "schema": 1,
        "requested": len(results),
        "converted": len(results) - failed,
        "failed": failed,
        "results": results,
    }
    report_path = project_dir / "Migration" / "unity_terrain_import_report.json"
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)

    _log(f"Complete: converted={report['converted']}, failed={report['failed']}")
    return 0 if failed == 0 and results else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    # Unreal's Python commandlet does not propagate sys.exit reliably; the log
    # marker gives CI a deterministic failure signal.
    _error(f"CML_TERRAIN_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_TERRAIN_IMPORT_SUCCEEDED")

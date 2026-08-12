"""Build the Starter Island Landscape grass system without saving the level.

This creates/updates content assets only:
* the Ground Detail and Terrain Splat master materials;
* one LandscapeGrassType using the exact LOD0/LOD1 source meshes from which
  Unity built MD_TerrainGrass_Carpet_A/B;
* the migrated Ground Detail material instance.

The open Landscape is refreshed in memory, but this script never saves or
reconstructs the current map.
"""

from __future__ import annotations

import os
from pathlib import Path
import runpy
import traceback

import unreal


GRASS_ROOT = "/Game/Migration/LandscapeGrass"
GRASS_TYPE_PATH = f"{GRASS_ROOT}/LGT_CML_StarterIslandGrass"
GROUND_DETAIL_MASTER = "/Game/Migration/Masters/M_CML_Env_GroundDetail"
GROUND_DETAIL_INSTANCE = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/"
    "M_StarterIsland_GroundDetail"
)
ADAPTIVE_MESH_ROOT = f"{GRASS_ROOT}/AdaptiveMeshes"
ADAPTIVE_GRASS_SOURCE = (
    Path(unreal.Paths.project_dir()).parent
    / "Game"
    / "Assets"
    / "_Project"
    / "Art"
    / "Environment"
    / "CleanRoomVisualTests"
    / "Grass"
    / "Models"
    / "CR_GrassClump_A.fbx"
)
ADAPTIVE_GRASS_MESHES = (
    f"{ADAPTIVE_MESH_ROOT}/CR_GrassClump_A_LOD0",
    f"{ADAPTIVE_MESH_ROOT}/CR_GrassClump_A_LOD1",
)

SHADER_FILTER = (
    "CML/Environment/Starter Island Terrain Splat|"
    "CML/Environment/Starter Island Ground Detail"
)


def _log(message: str) -> None:
    unreal.log(f"CML grass system: {message}")


def _load(path: str, expected_type, label: str):
    asset = unreal.EditorAssetLibrary.load_asset(path)
    if not isinstance(asset, expected_type):
        raise RuntimeError(f"Missing {label}: {path}")
    return asset


def _ensure_adaptive_grass_meshes() -> tuple[unreal.StaticMesh, unreal.StaticMesh]:
    """Import the two meshes used by Unity's final adaptive Terrain details.

    The Unity installer builds Carpet A from CR_GrassClump_A_LOD0 and Carpet B
    from CR_GrassClump_A_LOD1.  The general migration consolidates those into
    one Unreal LOD chain and deletes the LOD1 sibling, so this grass-specific
    import deliberately keeps the two FBX nodes as separate StaticMeshes.
    """
    loaded = tuple(
        unreal.EditorAssetLibrary.load_asset(path) for path in ADAPTIVE_GRASS_MESHES
    )
    if all(isinstance(asset, unreal.StaticMesh) for asset in loaded):
        return loaded

    if not ADAPTIVE_GRASS_SOURCE.is_file():
        raise RuntimeError(f"Missing adaptive grass source: {ADAPTIVE_GRASS_SOURCE}")

    unreal.EditorAssetLibrary.make_directory(ADAPTIVE_MESH_ROOT)
    options = unreal.FbxImportUI()
    options.set_editor_property("import_mesh", True)
    options.set_editor_property("import_materials", False)
    options.set_editor_property("import_textures", False)
    options.set_editor_property("import_as_skeletal", False)
    static_data = options.get_editor_property("static_mesh_import_data")
    static_data.set_editor_property("combine_meshes", False)
    static_data.set_editor_property("generate_lightmap_u_vs", False)
    static_data.set_editor_property("import_mesh_lo_ds", False)
    static_data.set_editor_property("transform_vertex_to_absolute", True)

    task = unreal.AssetImportTask()
    task.set_editor_property("filename", str(ADAPTIVE_GRASS_SOURCE))
    task.set_editor_property("destination_path", ADAPTIVE_MESH_ROOT)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", True)
    task.set_editor_property("save", True)
    task.set_editor_property("options", options)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

    loaded = tuple(
        unreal.EditorAssetLibrary.load_asset(path) for path in ADAPTIVE_GRASS_MESHES
    )
    if not all(isinstance(asset, unreal.StaticMesh) for asset in loaded):
        imported = ", ".join(str(path) for path in task.get_editor_property("imported_object_paths"))
        raise RuntimeError(
            "Adaptive grass import did not produce both Unity source LODs; "
            f"imported: {imported or '<none>'}"
        )
    for index, mesh in enumerate(loaded):
        unreal.EditorAssetLibrary.set_metadata_tag(
            mesh,
            "CML.UnityAdaptiveSource",
            f"MD_TerrainGrass_Carpet_{'A' if index == 0 else 'B'} <- "
            f"CR_GrassClump_A_LOD{index}",
        )
        unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False)
    return loaded


def _per_platform_float(value: float):
    result = unreal.PerPlatformFloat()
    result.set_editor_property("default", float(value))
    return result


def _per_platform_int(value: int):
    result = unreal.PerPlatformInt()
    result.set_editor_property("default", int(value))
    return result


def _interval(minimum: float, maximum: float):
    result = unreal.FloatInterval()
    result.set_editor_property("min", float(minimum))
    result.set_editor_property("max", float(maximum))
    return result


def _ensure_grass_type():
    grass_type = unreal.EditorAssetLibrary.load_asset(GRASS_TYPE_PATH)
    if not isinstance(grass_type, unreal.LandscapeGrassType):
        unreal.EditorAssetLibrary.make_directory(GRASS_ROOT)
        factory = unreal.LandscapeGrassTypeFactory()
        grass_type = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            "LGT_CML_StarterIslandGrass",
            GRASS_ROOT,
            unreal.LandscapeGrassType,
            factory,
        )
    if not isinstance(grass_type, unreal.LandscapeGrassType):
        raise RuntimeError(f"Unable to create {GRASS_TYPE_PATH}")
    return grass_type


def _make_variety(
    mesh: unreal.StaticMesh,
    material: unreal.MaterialInterface | None,
    density: float,
    width_min: float,
    width_max: float,
    height_min: float,
    height_max: float,
    random_rotation: bool,
):
    variety = unreal.GrassVariety()
    variety.set_editor_property("grass_mesh", mesh)
    if material is not None:
        variety.set_editor_property("override_materials", [material])
    variety.set_editor_property("grass_density", _per_platform_float(density))
    variety.set_editor_property("use_grid", True)
    variety.set_editor_property("placement_jitter", 0.92)
    variety.set_editor_property("start_cull_distance", _per_platform_int(7600))
    variety.set_editor_property("end_cull_distance", _per_platform_int(9200))
    variety.set_editor_property("scale_x", _interval(width_min, width_max))
    variety.set_editor_property("scale_y", _interval(width_min, width_max))
    variety.set_editor_property("scale_z", _interval(height_min, height_max))
    variety.set_editor_property("random_rotation", random_rotation)
    # Unity only leans these prototypes 16% toward the ground normal. Unreal's
    # boolean option is all-or-nothing; keeping blades upright is the closer
    # approximation and avoids grass lying flat on the edge of a slope.
    variety.set_editor_property("align_to_surface", False)
    variety.set_editor_property("use_landscape_lightmap", False)
    variety.set_editor_property("receives_decals", False)
    variety.set_editor_property("affect_distance_field_lighting", False)
    variety.set_editor_property("cast_dynamic_shadow", False)
    variety.set_editor_property("cast_contact_shadow", False)
    variety.set_editor_property("instance_world_position_offset_disable_distance", 9200)
    return variety


def _configure_grass_type(grass_type, material=None) -> None:
    meshes = _ensure_adaptive_grass_meshes()
    # Width/height intervals and jitter are copied from
    # StarterIslandAdaptiveGrassInstaller.BuildGrassPrototype.
    varieties = [
        _make_variety(
            meshes[0], material, 95.0, 0.48, 0.72, 0.50, 0.76, True
        ),
        _make_variety(
            meshes[1], material, 185.0, 0.42, 0.66, 0.45, 0.70, True
        ),
    ]
    grass_type.set_editor_property("grass_varieties", varieties)
    grass_type.set_editor_property("enable_density_scaling", True)
    unreal.EditorAssetLibrary.set_metadata_tag(
        grass_type,
        "CML.SourceStudy",
        "Unity adaptive Terrain detail meshes MD_TerrainGrass_Carpet_A/B",
    )
    unreal.EditorAssetLibrary.save_loaded_asset(grass_type, only_if_is_dirty=False)


def _rebuild_masters() -> None:
    script_path = Path(unreal.Paths.project_content_dir()) / "Python" / "cml_shader_ports.py"
    previous = os.environ.get("CML_SHADER_PORT_FILTER")
    os.environ["CML_SHADER_PORT_FILTER"] = SHADER_FILTER
    try:
        result = runpy.run_path(str(script_path))
    finally:
        if previous is None:
            os.environ.pop("CML_SHADER_PORT_FILTER", None)
        else:
            os.environ["CML_SHADER_PORT_FILTER"] = previous
    if result.get("_exit_code", 1) != 0:
        raise RuntimeError("Terrain/Ground Detail master rebuild failed")


def _configure_ground_detail_instance():
    master = _load(GROUND_DETAIL_MASTER, unreal.Material, "Ground Detail master")
    instance = _load(
        GROUND_DETAIL_INSTANCE, unreal.MaterialInstanceConstant, "Ground Detail instance"
    )
    unreal.MaterialEditingLibrary.set_material_instance_parent(instance, master)
    unreal.MaterialEditingLibrary.update_material_instance(instance)
    unreal.EditorAssetLibrary.save_loaded_asset(instance, only_if_is_dirty=False)
    return instance


def _update_landscape_instances() -> int:
    updated = 0
    for asset_path in unreal.EditorAssetLibrary.list_assets(
        "/Game/Migration/LandscapeMaterials", recursive=True, include_folder=False
    ):
        instance = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not isinstance(instance, unreal.MaterialInstanceConstant):
            continue
        parent = instance.get_editor_property("parent")
        if not parent or "M_CML_Env_TerrainSplat" not in parent.get_path_name():
            continue
        for parameter, value in (
            ("_TerrainGrassTextureStrength", 0.68),
            ("_TerrainGrassTextureLumaMatch", 0.82),
            ("_TerrainGrassNormalStrength", 0.72),
        ):
            unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(
                instance, parameter, value
            )
        unreal.MaterialEditingLibrary.update_material_instance(instance)
        unreal.EditorAssetLibrary.save_loaded_asset(instance, only_if_is_dirty=False)
        updated += 1
    return updated


def _refresh_open_landscapes() -> int:
    world = unreal.EditorLevelLibrary.get_editor_world()
    if not world:
        return 0
    refreshed = 0
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape):
        if unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(actor):
            refreshed += 1
    return refreshed


def main() -> int:
    grass_type = _ensure_grass_type()
    # The GrassType must exist before rebuilding Terrain: its GrassOutput stores
    # a hard asset reference in the material graph.
    _configure_grass_type(grass_type)
    _rebuild_masters()
    ground_detail = _configure_ground_detail_instance()
    _configure_grass_type(grass_type, ground_detail)
    instances = _update_landscape_instances()
    landscapes = _refresh_open_landscapes()
    _log(
        f"built Ground Detail + Terrain masters, 2 adaptive grass varieties, "
        f"updated {instances} Landscape MI(s), refreshed {landscapes} Landscape(s)"
    )
    _log("CML_GRASS_SYSTEM_BUILT")
    return 0


try:
    _exit_code = main()
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_GRASS_SYSTEM_FAILED")
    _exit_code = 1

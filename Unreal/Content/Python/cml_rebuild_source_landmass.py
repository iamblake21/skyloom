"""Rebuild and apply the source-backed landmass/Landscape material pair.

Safe for an already-open editor: only material/content packages are saved. The
current level is never saved or reconstructed. Landscape component permutations
are refreshed in memory so the user can inspect the result immediately.
"""

from __future__ import annotations

import os
from pathlib import Path
import runpy
import traceback

import unreal


SOURCE_FILTER = (
    "CML/Environment/Original Cliff Mass|"
    "CML/Environment/Starter Island Terrain Splat"
)
LANDSCAPE_INSTANCE_ROOT = "/Game/Migration/LandscapeMaterials"
VARIATION_TEXTURE = (
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Landmass/Textures/"
    "T_CML_LandmassGrassVariation.T_CML_LandmassGrassVariation"
)


def _log(message: str) -> None:
    unreal.log(f"CML source landmass: {message}")


def _rebuild_masters() -> None:
    script_path = Path(unreal.Paths.project_content_dir()) / "Python" / "cml_shader_ports.py"
    previous = os.environ.get("CML_SHADER_PORT_FILTER")
    os.environ["CML_SHADER_PORT_FILTER"] = SOURCE_FILTER
    try:
        result = runpy.run_path(str(script_path))
    finally:
        if previous is None:
            os.environ.pop("CML_SHADER_PORT_FILTER", None)
        else:
            os.environ["CML_SHADER_PORT_FILTER"] = previous
    if result.get("_exit_code", 1) != 0:
        raise RuntimeError("Source landmass master rebuild failed")


def _update_landscape_instances() -> int:
    variation = unreal.EditorAssetLibrary.load_asset(VARIATION_TEXTURE)
    if not isinstance(variation, unreal.Texture):
        raise RuntimeError(f"Missing clean-room variation texture: {VARIATION_TEXTURE}")

    updated = 0
    for asset_path in unreal.EditorAssetLibrary.list_assets(
        LANDSCAPE_INSTANCE_ROOT, recursive=True, include_folder=False
    ):
        instance = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not isinstance(instance, unreal.MaterialInstanceConstant):
            continue
        parent = instance.get_editor_property("parent")
        if not parent or "M_CML_Env_TerrainSplat" not in parent.get_path_name():
            continue

        for parameter in ("_LandmassVariationMask", "_TerrainBlendNoise"):
            unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
                instance, parameter, variation
            )
        for parameter, value in (
            ("_LandmassWorldSize", 30.0),
            ("_LandmassVariationWorldSizeA", 35.0),
            ("_LandmassVariationWorldSizeB", 40.96),
            ("_TerrainBlendNoiseWorldSize", 8.0),
            ("_TerrainGrassTextureStrength", 0.68),
            ("_TerrainGrassTextureLumaMatch", 0.82),
            ("_TerrainGrassNormalStrength", 0.72),
            ("_LandmassNormalStrength", 1.3),
            ("_LandmassSlopeOffset", -0.6),
            ("_LandmassSlopeHardness", 10.0),
        ):
            unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(
                instance, parameter, value
            )
        unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
            instance,
            "_LandmassGrassColor1",
            unreal.LinearColor(0.03529412, 0.09019608, 0.0, 1.0),
        )
        unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
            instance,
            "_LandmassGrassColor2",
            unreal.LinearColor(0.15294118, 0.18039216, 0.003921569, 1.0),
        )
        unreal.MaterialEditingLibrary.update_material_instance(instance)
        unreal.EditorAssetLibrary.save_loaded_asset(instance, only_if_is_dirty=False)
        updated += 1
    return updated


def _refresh_open_landscapes() -> int:
    refreshed = 0
    world = unreal.EditorLevelLibrary.get_editor_world()
    if not world:
        return refreshed
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Landscape):
        if unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(actor):
            refreshed += 1
    return refreshed


def main() -> int:
    _rebuild_masters()
    instances = _update_landscape_instances()
    landscapes = _refresh_open_landscapes()
    _log(f"rebuilt 2 masters, updated {instances} Landscape MI(s), refreshed {landscapes} Landscape(s)")
    _log("CML_SOURCE_LANDMASS_SUCCEEDED")
    return 0


try:
    _exit_code = main()
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_SOURCE_LANDMASS_FAILED")
    _exit_code = 1

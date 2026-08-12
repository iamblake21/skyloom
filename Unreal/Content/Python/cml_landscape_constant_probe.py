"""Create and assign a minimal constant Landscape material for render isolation."""

from __future__ import annotations

import os

import unreal


MAP_PATH = os.environ.get(
    "CML_PROBE_MAP", "/Game/Maps/A_10_StarterIsland_AxisPreview"
)
ASSET_PATH = "/Game/Migration/Diagnostics"
ASSET_NAME = "M_CML_Terrain_ConstantProbe"


def main() -> None:
    asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
    object_path = f"{ASSET_PATH}/{ASSET_NAME}.{ASSET_NAME}"
    material = unreal.EditorAssetLibrary.load_asset(object_path)
    if material is None:
        material = asset_tools.create_asset(
            ASSET_NAME,
            ASSET_PATH,
            unreal.Material,
            unreal.MaterialFactoryNew(),
        )
    if not isinstance(material, unreal.Material):
        raise RuntimeError(f"Probe asset has the wrong type: {object_path}")

    unreal.MaterialEditingLibrary.delete_all_material_expressions(material)
    material.set_editor_property("blend_mode", unreal.BlendMode.BLEND_OPAQUE)
    material.set_editor_property(
        "shading_model", unreal.MaterialShadingModel.MSM_UNLIT
    )
    colour = unreal.MaterialEditingLibrary.create_material_expression(
        material, unreal.MaterialExpressionConstant3Vector, -160, 0
    )
    colour.set_editor_property("constant", unreal.LinearColor(1.0, 0.0, 1.0, 1.0))
    if not unreal.MaterialEditingLibrary.connect_material_property(
        colour, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    ):
        raise RuntimeError("Could not connect constant probe emissive output")
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.save_loaded_asset(material, only_if_is_dirty=False)

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Unable to load map: {MAP_PATH}")
    landscapes = unreal.GameplayStatics.get_all_actors_of_class(
        unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world(),
        unreal.Landscape,
    )
    if len(landscapes) != 1:
        raise RuntimeError(f"Expected one Landscape, found {len(landscapes)}")
    landscape = landscapes[0]
    landscape.set_editor_property("landscape_material", material)
    landscape.set_editor_property("landscape_hole_material", None)
    if not unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(landscape):
        raise RuntimeError("Could not refresh probe Landscape material instances")
    if not level_editor.save_current_level():
        raise RuntimeError(f"Unable to save probe map: {MAP_PATH}")
    unreal.log(
        f"CML_LANDSCAPE_CONSTANT_PROBE_SUCCEEDED map={MAP_PATH} material={object_path}"
    )


try:
    main()
except Exception as exc:
    unreal.log_error(f"CML_LANDSCAPE_CONSTANT_PROBE_FAILED: {exc}")
    raise

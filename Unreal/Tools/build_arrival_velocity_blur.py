"""Build the explicit zoom/speed blur used by the intro arrival shot."""

from __future__ import annotations

import unreal


ASSET_DIR = "/Game/_Project/Art/Cinematics/Materials"
ASSET_NAME = "M_CIN_ArrivalVelocityBlur"
OBJECT_PATH = f"{ASSET_DIR}/{ASSET_NAME}.{ASSET_NAME}"


def main() -> None:
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    material = unreal.EditorAssetLibrary.load_asset(OBJECT_PATH)
    if material is None:
        material = tools.create_asset(
            ASSET_NAME,
            ASSET_DIR,
            unreal.Material,
            unreal.MaterialFactoryNew(),
        )
    if not isinstance(material, unreal.Material):
        raise RuntimeError(f"Unexpected asset at {OBJECT_PATH}")

    lib = unreal.MaterialEditingLibrary
    lib.delete_all_material_expressions(material)
    material.set_editor_property(
        "material_domain", unreal.MaterialDomain.MD_POST_PROCESS
    )
    material.set_editor_property(
        "blendable_location",
        unreal.BlendableLocation.BL_SCENE_COLOR_AFTER_DOF,
    )

    scene = lib.create_material_expression(
        material, unreal.MaterialExpressionSceneTexture, -900, -120
    )
    scene.set_editor_property(
        "scene_texture_id",
        unreal.SceneTextureId.PPI_POST_PROCESS_INPUT0,
    )

    amount = lib.create_material_expression(
        material, unreal.MaterialExpressionScalarParameter, -900, 120
    )
    amount.set_editor_property("parameter_name", "BlurAmount")
    amount.set_editor_property("default_value", 0.0)

    custom = lib.create_material_expression(
        material, unreal.MaterialExpressionCustom, -420, 0
    )
    custom.set_editor_property("description", "CML arrival radial velocity smear")
    custom.set_editor_property(
        "output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3
    )
    custom.set_editor_property(
        "code",
        r"""
float2 UV = GetDefaultSceneTextureUV(Parameters, 14);
float2 Ray = UV - float2(0.5, 0.5);
float Spread = saturate(BlurAmount) * 0.021;

// Eight weighted taps create the stretched cockpit echoes from the Unity
// landing without relying on temporal velocity history or the current FPS.
float3 C = 0.0;
C += SceneTextureLookup(UV - Ray * Spread * 0.00, 14, false).rgb * 0.23;
C += SceneTextureLookup(UV - Ray * Spread * 0.16, 14, false).rgb * 0.17;
C += SceneTextureLookup(UV - Ray * Spread * 0.34, 14, false).rgb * 0.15;
C += SceneTextureLookup(UV - Ray * Spread * 0.54, 14, false).rgb * 0.13;
C += SceneTextureLookup(UV - Ray * Spread * 0.76, 14, false).rgb * 0.11;
C += SceneTextureLookup(UV - Ray * Spread * 1.00, 14, false).rgb * 0.09;
C += SceneTextureLookup(UV - Ray * Spread * 1.28, 14, false).rgb * 0.07;
C += SceneTextureLookup(UV - Ray * Spread * 1.60, 14, false).rgb * 0.05;

// SceneColor is connected to make PostProcessInput0 an explicit dependency;
// the zero multiplier intentionally does not alter the result.
return C + SceneColor.rgb * 0.0;
""",
    )
    scene_input = unreal.CustomInput()
    scene_input.set_editor_property("input_name", "SceneColor")
    amount_input = unreal.CustomInput()
    amount_input.set_editor_property("input_name", "BlurAmount")
    custom.set_editor_property("inputs", [scene_input, amount_input])

    if not lib.connect_material_expressions(scene, "Color", custom, "SceneColor"):
        raise RuntimeError("Could not connect post-process scene colour")
    if not lib.connect_material_expressions(amount, "", custom, "BlurAmount"):
        raise RuntimeError("Could not connect BlurAmount")
    if not lib.connect_material_property(
        custom, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR
    ):
        raise RuntimeError("Could not connect blur output")

    lib.layout_material_expressions(material)
    lib.recompile_material(material)
    unreal.EditorAssetLibrary.save_loaded_asset(material, only_if_is_dirty=False)
    unreal.log(f"CML_ARRIVAL_BLUR_BUILT {OBJECT_PATH}")


main()

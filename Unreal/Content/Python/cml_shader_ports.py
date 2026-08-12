"""Native Unreal ports of the custom Unity shaders used by Changing My Life.

Each `port_*` function builds one Unreal master material whose shading maths is
the transcribed HLSL of the Unity original (see `Shaders/*.ush`). Unreal
parameter names are the Unity property names verbatim, so `cml_material_import`
can populate instances by copying the `.mat` values across unchanged.
"""

from __future__ import annotations

import json
import os
import traceback
from pathlib import Path

import unreal

from cml_shader_port_library import (
    BLACK,
    BLEND_ALPHA_COMPOSITE,
    CLEAR,
    FLAT_NORMAL,
    FLOAT1,
    FLOAT2,
    FLOAT3,
    FLOAT4,
    GREY,
    SAMPLER_COLOR,
    SAMPLER_LINEAR,
    SAMPLER_MASKS,
    SAMPLER_NORMAL,
    WHITE,
    MasterMaterialBuilder,
    PortResult,
    ensure_default_textures,
    unity_world_position,
)


MP = unreal.MaterialProperty

STARTER_ISLAND_GRASS_TYPE = (
    "/Game/Migration/LandscapeGrass/LGT_CML_StarterIslandGrass."
    "LGT_CML_StarterIslandGrass"
)


def _log(message: str) -> None:
    unreal.log(f"[CML Shader Port] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Shader Port] {message}")


def _local_position(builder: MasterMaterialBuilder):
    """Object-space position, in Unreal units, resilient across engine versions."""
    try:
        return builder.local_position()
    except Exception:
        node = builder._expression(unreal.MaterialExpressionTransformPosition)
        node.set_editor_property(
            "transform_source_type", unreal.MaterialPositionTransformSource.TRANSFORMPOSSOURCE_WORLD
        )
        node.set_editor_property(
            "transform_type", unreal.MaterialPositionTransformSource.TRANSFORMPOSSOURCE_LOCAL
        )
        builder.connect(builder.world_position(), "", node, "Input")
        return node


# ---------------------------------------------------------------------------
# CML/Environment/Starter Island CloudTall Tree
# ---------------------------------------------------------------------------


def port_cloud_tall_tree() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_CloudTallTree",
        unity_shader="CML/Environment/Starter Island CloudTall Tree",
        blend_mode=unreal.BlendMode.BLEND_MASKED,
        two_sided=True,  # Unity _Cull default 0 == Cull Off
        opacity_mask_clip_value=0.45,  # Unity _Cutoff default
        include_files=("/CML/CMLCloudTallTree.ush",),
    )

    base_map_st = builder.vector4("_BaseMap_ST", (1.0, 1.0, 0.0, 0.0))
    uv = builder.custom(
        "BaseMapUV",
        "return UV0 * ST.xy + ST.zw;",
        FLOAT2,
        {"UV0": builder.texcoord(0), "ST": base_map_st},
    )
    base_map = builder.texture("_BaseMap", WHITE, SAMPLER_COLOR, uv=uv)
    base_color = builder.vector4("_BaseColor", (1.0, 1.0, 1.0, 1.0))
    cutoff = builder.scalar("_Cutoff", 0.45)
    smoothness = builder.scalar("_Smoothness", 0.04)
    metallic = builder.scalar("_Metallic", 0.0)

    wind_direction = builder.vector("_WindDirection", (0.82, 0.0, 0.57, 0.0), group="Wind")
    wind_strength = builder.scalar("_WindStrength", 0.24, group="Wind")
    wind_speed = builder.scalar("_WindSpeed", 0.82, group="Wind")
    wind_gust = builder.scalar("_WindGustStrength", 0.38, group="Wind")
    wind_flutter = builder.scalar("_WindFlutterStrength", 0.045, group="Wind")
    wind_base_height = builder.scalar("_WindBaseHeight", 2.0, group="Wind")
    wind_height = builder.scalar("_WindHeight", 8.0, group="Wind")
    hit_offset = builder.vector("_CMLHitOffsetWS", (0.0, 0.0, 0.0, 0.0), group="Runtime")

    # World position offset: the whole wind deformation plus the runtime hit
    # displacement, converted from Unity metres to Unreal centimetres once.
    wpo = builder.custom(
        "CloudTallWind",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 UnityOriginWS = CMLToUnityPosition(ObjectPosition);
        float3 UnityPositionOS = CMLToUnityPosition(LocalPosition);
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        float3 Offset = CMLCloudTallWindOffset(
            UnityPositionWS, UnityOriginWS, UnityPositionOS, UnityUV, Time,
            WindDirection.xyz, WindStrength, WindSpeed, WindGustStrength,
            WindFlutterStrength, WindBaseHeight, WindHeight);
        return CMLToUnrealOffset(Offset + HitOffset);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "ObjectPosition": builder.object_position(),
            "LocalPosition": _local_position(builder),
            "UV0": builder.texcoord(0),
            "Time": builder.time(),
            "WindDirection": wind_direction,
            "WindStrength": wind_strength,
            "WindSpeed": wind_speed,
            "WindGustStrength": wind_gust,
            "WindFlutterStrength": wind_flutter,
            "WindBaseHeight": wind_base_height,
            "WindHeight": wind_height,
            "HitOffset": hit_offset,
        },
    )

    # chopData lives in TEXCOORD1 as a float4 and is written at runtime by
    # TreeChopVoxelCarver. Imported FBX meshes have no such channel, so the
    # switch keeps them on the exact authored-bark path.
    use_chop = builder._expression(unreal.MaterialExpressionStaticSwitchParameter)
    use_chop.set_editor_property("parameter_name", "_CMLUseChopData")
    use_chop.set_editor_property("default_value", False)
    chop_zero = builder._expression(unreal.MaterialExpressionConstant4Vector)
    chop_zero.set_editor_property("constant", unreal.LinearColor(0.0, 0.0, 0.0, 0.0))
    chop_live = builder._expression(unreal.MaterialExpressionAppendVector)
    builder.connect(builder.texcoord(1), "", chop_live, "A")
    builder.connect(builder.texcoord(2), "", chop_live, "B")
    builder.connect(chop_live, "", use_chop, "True")
    builder.connect(chop_zero, "", use_chop, "False")

    albedo = builder.custom(
        "CloudTallAlbedo",
        """
        float3 Bark = Map * VertexColorRGBA.rgb * Tint.rgb;
        return CMLApplyChopAppearance(Bark, ChopData);
        """,
        FLOAT3,
        {
            "Map": (base_map, ""),
            "VertexColorRGBA": builder.vertex_color4(),
            "Tint": base_color,
            "ChopData": use_chop,
        },
    )

    opacity = builder.custom(
        "CloudTallOpacityMask",
        "return MapAlpha * Tint.a;",
        FLOAT1,
        {"MapAlpha": (base_map, "A"), "Tint": base_color},
    )

    roughness = builder.custom(
        "CloudTallRoughness",
        "return 1.0f - CMLChopSmoothness(ChopData, Smoothness);",
        FLOAT1,
        {"ChopData": use_chop, "Smoothness": smoothness},
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(opacity, "", MP.MP_OPACITY_MASK)
    builder.output(roughness, "", MP.MP_ROUGHNESS)
    builder.output(metallic, "", MP.MP_METALLIC)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Starter Island Stylized Surface
# ---------------------------------------------------------------------------


def port_stylized_surface() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_StylizedSurface",
        unity_shader="CML/Environment/Starter Island Stylized Surface",
        include_files=("/CML/CMLStylizedSurface.ush",),
    )

    base_color = builder.vector("_BaseColor", (0.47, 0.73, 0.31, 1.0))
    secondary = builder.vector("_SecondaryColor", (0.78, 0.64, 0.42, 1.0))
    wet = builder.vector("_WetColor", (0.18, 0.40, 0.34, 1.0))
    vertex_blend = builder.scalar("_VertexBlend", 0.0)
    color_variation = builder.scalar("_ColorVariation", 0.035)
    rock_detail = builder.scalar("_RockDetail", 0.0, group="Rock")
    rock_top = builder.vector("_RockTopColor", (0.82, 0.79, 0.73, 1.0), group="Rock")
    rock_under = builder.vector("_RockUnderColor", (0.42, 0.46, 0.43, 1.0), group="Rock")
    rock_top_strength = builder.scalar("_RockTopStrength", 0.62, group="Rock")
    rock_under_strength = builder.scalar("_RockUnderStrength", 0.34, group="Rock")
    macro_scale = builder.scalar("_RockMacroScale", 0.42, group="Rock")
    macro_strength = builder.scalar("_RockMacroStrength", 0.12, group="Rock")
    grain_scale = builder.scalar("_RockGrainScale", 4.2, group="Rock")
    grain_strength = builder.scalar("_RockGrainStrength", 0.055, group="Rock")
    contact_blend = builder.scalar("_RockContactBlend", 0.0, group="Terrain Contact")
    contact_height = builder.scalar("_RockContactHeight", 0.22, group="Terrain Contact")
    contact_feather = builder.scalar("_RockContactFeather", 0.18, group="Terrain Contact")
    contact_noise = builder.scalar("_RockContactNoise", 0.12, group="Terrain Contact")
    contact_grass = builder.vector("_RockContactGrassColor", (0.25, 0.39, 0.18, 1.0), group="Terrain Contact")
    contact_deep = builder.vector("_RockContactDeepGrassColor", (0.16, 0.29, 0.15, 1.0), group="Terrain Contact")
    contact_dirt = builder.vector("_RockContactDirtColor", (0.66, 0.49, 0.31, 1.0), group="Terrain Contact")
    contact_cliff = builder.vector("_RockContactCliffColor", (0.53, 0.30, 0.23, 1.0), group="Terrain Contact")
    hit_offset = builder.vector("_CMLHitOffsetWS", (0.0, 0.0, 0.0, 0.0), group="Runtime")

    # Globals the Unity terrain-blend component pushed via Shader.SetGlobal*.
    # They stay parameters here so the Unreal terrain system can drive them.
    terrain_origin = builder.vector4(
        "_CMLTerrainBlendOriginInvSize", (0.0, 0.0, 1.0, 1.0), group="Terrain Contact"
    )
    terrain_enabled = builder.scalar("_CMLTerrainBlendEnabled", 0.0, group="Terrain Contact")

    unity_ws = unity_world_position(builder)
    terrain_uv = builder.custom(
        "TerrainAlphamapUV",
        "return CMLStylizedTerrainUV(UnityPositionWS, OriginInvSize);",
        FLOAT2,
        {"UnityPositionWS": unity_ws, "OriginInvSize": terrain_origin},
    )
    terrain_uv_clamped = builder.custom(
        "TerrainAlphamapUVClamped",
        "return float2(saturate(UV.x), -saturate(UV.y));",
        FLOAT2,
        {"UV": terrain_uv},
    )
    terrain_control = builder.texture(
        "_CMLTerrainBlendControl",
        BLACK,
        SAMPLER_LINEAR,
        uv=terrain_uv_clamped,
        group="Terrain Contact",
    )

    albedo = builder.custom(
        "StylizedSurfaceAlbedo",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float LocalHeight = CMLToUnityPosition(LocalPosition).y;
        return CMLStylizedSurfaceAlbedo(
            UnityPositionWS, UnityNormalWS, LocalHeight, VertexColorRGBA,
            BaseColor.rgb, SecondaryColor.rgb, WetColor.rgb, VertexBlend, ColorVariation,
            RockDetail, RockTopColor.rgb, RockUnderColor.rgb, RockTopStrength, RockUnderStrength,
            RockMacroScale, RockMacroStrength, RockGrainScale, RockGrainStrength,
            RockContactBlend, RockContactHeight, RockContactFeather, RockContactNoise,
            ContactGrass.rgb, ContactDeepGrass.rgb, ContactDirt.rgb, ContactCliff.rgb,
            TerrainWeights, TerrainUV, TerrainBlendEnabled);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "NormalWS": builder.vertex_normal(),
            "LocalPosition": _local_position(builder),
            "VertexColorRGBA": builder.vertex_color4(),
            "BaseColor": base_color,
            "SecondaryColor": secondary,
            "WetColor": wet,
            "VertexBlend": vertex_blend,
            "ColorVariation": color_variation,
            "RockDetail": rock_detail,
            "RockTopColor": rock_top,
            "RockUnderColor": rock_under,
            "RockTopStrength": rock_top_strength,
            "RockUnderStrength": rock_under_strength,
            "RockMacroScale": macro_scale,
            "RockMacroStrength": macro_strength,
            "RockGrainScale": grain_scale,
            "RockGrainStrength": grain_strength,
            "RockContactBlend": contact_blend,
            "RockContactHeight": contact_height,
            "RockContactFeather": contact_feather,
            "RockContactNoise": contact_noise,
            "ContactGrass": contact_grass,
            "ContactDeepGrass": contact_deep,
            "ContactDirt": contact_dirt,
            "ContactCliff": contact_cliff,
            "TerrainWeights": (terrain_control, "RGBA"),
            "TerrainUV": terrain_uv,
            "TerrainBlendEnabled": terrain_enabled,
        },
    )

    wpo = builder.custom(
        "StylizedSurfaceHitOffset",
        "return CMLToUnrealOffset(HitOffset);",
        FLOAT3,
        {"HitOffset": hit_offset},
    )

    # Unity: specular 0, smoothness 0 - a fully rough, non-specular surface.
    roughness = builder.constant(1.0)
    specular = builder.constant(0.0)
    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(roughness, "", MP.MP_ROUGHNESS)
    builder.output(specular, "", MP.MP_SPECULAR)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)

    # _AmbientStrength / _ShadowFloor were a hand-rolled wrap-lighting term in
    # the Unity fragment. Unreal evaluates lighting outside the material, so
    # they are recorded on the asset for the scene light rig rather than
    # silently dropped.
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.LightingStylization", "_AmbientStrength,_ShadowFloor"
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Original Cliff Mass
# ---------------------------------------------------------------------------


def port_original_cliff_mass() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_OriginalCliffMass",
        unity_shader="CML/Environment/Original Cliff Mass",
        include_files=("/CML/CMLLandmassSurface.ush",),
    )
    # The landmass surface computes a world-space normal directly.
    builder.material.set_editor_property("tangent_space_normal", False)

    world_size = builder.scalar("_LandmassWorldSize", 30.0)
    variation_size_a = builder.scalar("_LandmassVariationWorldSizeA", 35.0)
    variation_size_b = builder.scalar("_LandmassVariationWorldSizeB", 40.96)
    normal_strength = builder.scalar("_LandmassNormalStrength", 1.3)
    slope_offset = builder.scalar("_LandmassSlopeOffset", -0.6)
    slope_hardness = builder.scalar("_LandmassSlopeHardness", 10.0)
    color_slope_offset = builder.scalar("_LandmassColorSlopeOffset", -0.499157)
    color_slope_hardness = builder.scalar("_LandmassColorSlopeHardness", 2.953668)
    grass_amount = builder.scalar("_LandmassGrassAmount", 1.0)
    rock_tint = builder.vector("_LandmassRockTintColor", (0.473531, 0.198069, 0.0865, 1.0))
    rock_tint_strength = builder.scalar("_LandmassRockTintStrength", 0.3)
    grass_color_1 = builder.vector("_LandmassGrassColor1", (0.03529412, 0.09019608, 0.0, 1.0))
    grass_color_2 = builder.vector("_LandmassGrassColor2", (0.15294118, 0.18039216, 0.003921569, 1.0))
    hit_offset = builder.vector("_CMLHitOffsetWS", (0.0, 0.0, 0.0, 0.0), group="Runtime")

    unity_ws = unity_world_position(builder)

    def projection(name: str, call: str, size_param):
        return builder.custom(
            name,
            f"return {call}(UnityPositionWS, WorldSize);",
            FLOAT2,
            {"UnityPositionWS": unity_ws, "WorldSize": size_param},
        )

    uv_a = projection("LandmassUVWallX", "CMLLandmassUVWallX", world_size)
    uv_b = projection("LandmassUVWallZ", "CMLLandmassUVWallZ", world_size)
    uv_c = projection("LandmassUVTop", "CMLLandmassUVTop", world_size)
    uv_var_a = projection("LandmassVariationUVA", "CMLLandmassUVPlanar", variation_size_a)
    uv_var_b = projection("LandmassVariationUVB", "CMLLandmassUVPlanar", variation_size_b)

    albedo_a = builder.texture("_LandmassCliffAlbedo", WHITE, SAMPLER_COLOR, uv=uv_a)
    albedo_b = builder.texture("_LandmassCliffAlbedo", WHITE, SAMPLER_COLOR, uv=uv_b, register=False)
    albedo_c = builder.texture("_LandmassCliffAlbedo", WHITE, SAMPLER_COLOR, uv=uv_c, register=False)
    normal_a = builder.texture("_LandmassCliffNormal", FLAT_NORMAL, SAMPLER_NORMAL, uv=uv_a)
    normal_b = builder.texture("_LandmassCliffNormal", FLAT_NORMAL, SAMPLER_NORMAL, uv=uv_b, register=False)
    normal_c = builder.texture("_LandmassCliffNormal", FLAT_NORMAL, SAMPLER_NORMAL, uv=uv_c, register=False)
    variation_a = builder.texture("_LandmassVariationMask", GREY, SAMPLER_LINEAR, uv=uv_var_a)
    variation_b = builder.texture("_LandmassVariationMask", GREY, SAMPLER_LINEAR, uv=uv_var_b, register=False)

    albedo = builder.custom(
        "LandmassAlbedo",
        """
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float Variation = CMLLandmassVariation(VariationA, VariationB);
        float3 RockAlbedo = CMLLandmassRockAlbedo(
            UnityNormalWS, AlbedoA, AlbedoB, AlbedoC, RockTint.rgb, RockTintStrength);
        float TopMask = CMLLandmassTopMask(
            UnityNormalWS, ColorSlopeOffset, ColorSlopeHardness, GrassAmount);
        float3 GrassAlbedo = CMLLandmassGrassColor(
            Variation, GrassColor1.rgb, GrassColor2.rgb);
        return lerp(RockAlbedo, GrassAlbedo, TopMask);
        """,
        FLOAT3,
        {
            "NormalWS": builder.vertex_normal(),
            "AlbedoA": (albedo_a, ""),
            "AlbedoB": (albedo_b, ""),
            "AlbedoC": (albedo_c, ""),
            "VariationA": (variation_a, "R"),
            "VariationB": (variation_b, "R"),
            "RockTint": rock_tint,
            "RockTintStrength": rock_tint_strength,
            "GrassColor1": grass_color_1,
            "GrassColor2": grass_color_2,
            "ColorSlopeOffset": color_slope_offset,
            "ColorSlopeHardness": color_slope_hardness,
            "GrassAmount": grass_amount,
        },
    )

    normal = builder.custom(
        "LandmassNormal",
        """
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float3 RockNormal = CMLLandmassRockNormal(
            UnityNormalWS, TangentNormalA, TangentNormalB, TangentNormalC, NormalStrength);
        float3 SurfaceNormal = CMLLandmassSurfaceNormal(
            UnityNormalWS, RockNormal, SlopeOffset, SlopeHardness);
        return CMLToUnrealDirection(SurfaceNormal);
        """,
        FLOAT3,
        {
            "NormalWS": builder.vertex_normal(),
            "TangentNormalA": (normal_a, ""),
            "TangentNormalB": (normal_b, ""),
            "TangentNormalC": (normal_c, ""),
            "NormalStrength": normal_strength,
            "SlopeOffset": slope_offset,
            "SlopeHardness": slope_hardness,
        },
    )

    wpo = builder.custom(
        "LandmassHitOffset",
        "return CMLToUnrealOffset(HitOffset);",
        FLOAT3,
        {"HitOffset": hit_offset},
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(normal, "", MP.MP_NORMAL)
    # Source clear-weather landmass is fully dry: Wetness=1 -> roughness=1.
    # Keep this as a native constant rather than a redundant Custom input;
    # the latter can be stripped by UE 5.8 when its default is exactly zero.
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.5), "", MP.MP_SPECULAR)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.SourceMaterial", "M_Island_Landmass clear-weather base pass"
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Starter Island V4 Tree Leaves
# ---------------------------------------------------------------------------


def port_v4_tree_leaves() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_V4TreeLeaves",
        unity_shader="CML/Environment/Starter Island V4 Tree Leaves",
        blend_mode=unreal.BlendMode.BLEND_MASKED,
        two_sided=True,
        opacity_mask_clip_value=0.45,
        include_files=("/CML/CMLCloudTallTree.ush",),
    )

    base_map_st = builder.vector4("_BaseMap_ST", (1.0, 1.0, 0.0, 0.0))
    uv = builder.custom(
        "BaseMapUV",
        "return UV0 * ST.xy + ST.zw;",
        FLOAT2,
        {"UV0": builder.texcoord(0), "ST": base_map_st},
    )
    base_map = builder.texture("_BaseMap", WHITE, SAMPLER_COLOR, uv=uv)
    bump_map = builder.texture("_BumpMap", FLAT_NORMAL, SAMPLER_NORMAL, uv=uv)
    base_color = builder.vector4("_BaseColor", (1.0, 1.0, 1.0, 1.0))
    builder.scalar("_Cutoff", 0.45)
    smoothness = builder.scalar("_Smoothness", 0.18)
    metallic = builder.scalar("_Metallic", 0.0)
    bump_scale = builder.scalar("_BumpScale", 0.65)

    # The leaf shader uses the identical wind deformation as the CloudTall
    # trees; only the default base height and height range differ.
    wind_direction = builder.vector("_WindDirection", (0.82, 0.0, 0.57, 0.0), group="Wind")
    wind_strength = builder.scalar("_WindStrength", 0.24, group="Wind")
    wind_speed = builder.scalar("_WindSpeed", 0.82, group="Wind")
    wind_gust = builder.scalar("_WindGustStrength", 0.38, group="Wind")
    wind_flutter = builder.scalar("_WindFlutterStrength", 0.045, group="Wind")
    wind_base_height = builder.scalar("_WindBaseHeight", 0.75, group="Wind")
    wind_height = builder.scalar("_WindHeight", 9.5, group="Wind")

    wpo = builder.custom(
        "V4LeavesWind",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 UnityOriginWS = CMLToUnityPosition(ObjectPosition);
        float3 UnityPositionOS = CMLToUnityPosition(LocalPosition);
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        float3 Offset = CMLCloudTallWindOffset(
            UnityPositionWS, UnityOriginWS, UnityPositionOS, UnityUV, Time,
            WindDirection.xyz, WindStrength, WindSpeed, WindGustStrength,
            WindFlutterStrength, WindBaseHeight, WindHeight);
        return CMLToUnrealOffset(Offset);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "ObjectPosition": builder.object_position(),
            "LocalPosition": _local_position(builder),
            "UV0": builder.texcoord(0),
            "Time": builder.time(),
            "WindDirection": wind_direction,
            "WindStrength": wind_strength,
            "WindSpeed": wind_speed,
            "WindGustStrength": wind_gust,
            "WindFlutterStrength": wind_flutter,
            "WindBaseHeight": wind_base_height,
            "WindHeight": wind_height,
        },
    )

    albedo = builder.custom(
        "V4LeavesAlbedo",
        "return Atlas * Tint.rgb;",
        FLOAT3,
        {"Atlas": (base_map, ""), "Tint": base_color},
    )
    opacity = builder.custom(
        "V4LeavesOpacityMask",
        "return AtlasAlpha * Tint.a;",
        FLOAT1,
        {"AtlasAlpha": (base_map, "A"), "Tint": base_color},
    )
    normal = builder.custom(
        "V4LeavesNormal",
        "return CMLScaleTangentNormal(TangentNormal, BumpScale);",
        FLOAT3,
        {"TangentNormal": (bump_map, ""), "BumpScale": bump_scale},
    )
    roughness = builder.custom(
        "V4LeavesRoughness", "return 1.0f - Smoothness;", FLOAT1, {"Smoothness": smoothness}
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(opacity, "", MP.MP_OPACITY_MASK)
    builder.output(normal, "", MP.MP_NORMAL)
    builder.output(roughness, "", MP.MP_ROUGHNESS)
    builder.output(metallic, "", MP.MP_METALLIC)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Starter Island Terrain Splat
# ---------------------------------------------------------------------------


def port_terrain_splat() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_TerrainSplat",
        unity_shader="CML/Environment/Starter Island Terrain Splat",
        blend_mode=unreal.BlendMode.BLEND_MASKED,
        opacity_mask_clip_value=0.5,
        include_files=("/CML/CMLTerrainSplat.ush",),
    )
    builder.material.set_editor_property("tangent_space_normal", False)

    unity_ws = unity_world_position(builder)
    normal_ws = builder.vertex_normal()

    terrain_origin_inv_size = builder.vector4(
        "_CMLTerrainOriginInvSize", (0.0, 0.0, 1.0, 1.0), group="Terrain"
    )
    terrain_uv = builder.custom(
        "TerrainGlobalUV",
        "return saturate((UnityPositionWS.xz - OriginInvSize.xy) * OriginInvSize.zw);",
        FLOAT2,
        {"UnityPositionWS": unity_ws, "OriginInvSize": terrain_origin_inv_size},
    )
    control = builder.texture("_Control", BLACK, SAMPLER_LINEAR, uv=terrain_uv, group="Terrain")

    # Preserve the project grass textures as controlled micro-detail over the
    # decoded Solarpunk seasonal palette.  Their average luminance is matched
    # in CMLTerrainSplat.ush so they cannot bring back the fluorescent look.
    grass_st = [
        builder.vector4(f"_Splat{index}_ST", (1.0, 1.0, 0.0, 0.0), group="Terrain Grass")
        for index in (0, 1)
    ]
    grass_uv = [
        builder.custom(
            f"TerrainGrassUV{index}",
            "return TerrainUV * ST.xy + ST.zw;",
            FLOAT2,
            {"TerrainUV": terrain_uv, "ST": grass_st[index]},
        )
        for index in (0, 1)
    ]
    grass_albedo = [
        builder.texture(
            f"_Splat{index}", GREY, SAMPLER_COLOR, uv=grass_uv[index], group="Terrain Grass"
        )
        for index in (0, 1)
    ]
    grass_normal = [
        builder.texture(
            f"_Normal{index}", FLAT_NORMAL, SAMPLER_NORMAL,
            uv=grass_uv[index], group="Terrain Grass"
        )
        for index in (0, 1)
    ]
    grass_normal_scale = [
        builder.scalar(f"_NormalScale{index}", 1.0, group="Terrain Grass")
        for index in (0, 1)
    ]
    grass_remap = [
        builder.vector(f"_DiffuseRemapScale{index}", (1.0, 1.0, 1.0, 1.0), group="Terrain Grass")
        for index in (0, 1)
    ]
    grass_texture_strength = builder.scalar(
        "_TerrainGrassTextureStrength", 0.68, group="Terrain Grass"
    )
    grass_texture_luma_match = builder.scalar(
        "_TerrainGrassTextureLumaMatch", 0.82, group="Terrain Grass"
    )
    grass_detail_normal_strength = builder.scalar(
        "_TerrainGrassNormalStrength", 0.72, group="Terrain Grass"
    )

    # Dirt/path remains the authored project texture.
    dirt_st = builder.vector4("_Splat2_ST", (1.0, 1.0, 0.0, 0.0), group="Terrain Dirt")
    dirt_uv = builder.custom(
        "TerrainDirtUV",
        "return TerrainUV * ST.xy + ST.zw;",
        FLOAT2,
        {"TerrainUV": terrain_uv, "ST": dirt_st},
    )
    dirt_albedo = builder.texture("_Splat2", GREY, SAMPLER_COLOR, uv=dirt_uv, group="Terrain Dirt")
    dirt_normal = builder.texture(
        "_Normal2", FLAT_NORMAL, SAMPLER_NORMAL, uv=dirt_uv, group="Terrain Dirt"
    )
    dirt_normal_scale = builder.scalar("_NormalScale2", 1.0, group="Terrain Dirt")
    dirt_remap = builder.vector("_DiffuseRemapScale2", (1.0, 1.0, 1.0, 1.0), group="Terrain Dirt")
    dirt_tint = builder.vector("_TerrainDirtTint", (1.0, 0.7177745, 0.5015214, 1.0), group="Terrain Dirt")

    world_size = builder.scalar("_LandmassWorldSize", 30.0, group="Source Landscape")
    variation_size_a = builder.scalar("_LandmassVariationWorldSizeA", 35.0, group="Source Landscape")
    variation_size_b = builder.scalar("_LandmassVariationWorldSizeB", 40.96, group="Source Landscape")
    blend_noise_size = builder.scalar("_TerrainBlendNoiseWorldSize", 8.0, group="Source Landscape")
    normal_strength = builder.scalar("_LandmassNormalStrength", 1.3, group="Source Landscape")
    slope_offset = builder.scalar("_LandmassSlopeOffset", -0.6, group="Source Landscape")
    slope_hardness = builder.scalar("_LandmassSlopeHardness", 10.0, group="Source Landscape")
    grass_color_1 = builder.vector(
        "_LandmassGrassColor1", (0.03529412, 0.09019608, 0.0, 1.0), group="Source Landscape"
    )
    grass_color_2 = builder.vector(
        "_LandmassGrassColor2", (0.15294118, 0.18039216, 0.003921569, 1.0), group="Source Landscape"
    )

    def projection(name: str, call: str, size_param):
        return builder.custom(
            name,
            f"return {call}(UnityPositionWS, WorldSize);",
            FLOAT2,
            {"UnityPositionWS": unity_ws, "WorldSize": size_param},
        )

    cliff_uv_a = projection("TerrainCliffUVWallX", "CMLLandmassUVWallX", world_size)
    cliff_uv_b = projection("TerrainCliffUVWallZ", "CMLLandmassUVWallZ", world_size)
    variation_uv_a = projection("TerrainVariationUVA", "CMLLandmassUVPlanar", variation_size_a)
    variation_uv_b = projection("TerrainVariationUVB", "CMLLandmassUVPlanar", variation_size_b)
    blend_noise_uv = projection("TerrainBlendNoiseUV", "CMLLandmassUVPlanar", blend_noise_size)

    cliff_albedo_a = builder.texture(
        "_LandmassCliffAlbedo", WHITE, SAMPLER_COLOR, uv=cliff_uv_a, group="Source Landscape"
    )
    cliff_albedo_b = builder.texture(
        "_LandmassCliffAlbedo", WHITE, SAMPLER_COLOR, uv=cliff_uv_b,
        group="Source Landscape", register=False
    )
    cliff_normal_a = builder.texture(
        "_LandmassCliffNormal", FLAT_NORMAL, SAMPLER_NORMAL, uv=cliff_uv_a,
        group="Source Landscape"
    )
    cliff_normal_b = builder.texture(
        "_LandmassCliffNormal", FLAT_NORMAL, SAMPLER_NORMAL, uv=cliff_uv_b,
        group="Source Landscape", register=False
    )
    variation_a = builder.texture(
        "_LandmassVariationMask", GREY, SAMPLER_LINEAR, uv=variation_uv_a,
        group="Source Landscape"
    )
    variation_b = builder.texture(
        "_LandmassVariationMask", GREY, SAMPLER_LINEAR, uv=variation_uv_b,
        group="Source Landscape", register=False
    )
    blend_noise = builder.texture(
        "_TerrainBlendNoise", GREY, SAMPLER_LINEAR, uv=blend_noise_uv,
        group="Source Landscape"
    )

    albedo = builder.custom(
        "TerrainSourceAlbedo",
        """
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float Variation = CMLLandmassVariation(VariationA, VariationB);
        float3 SlopeAlbedo = CMLLandmassRockSideAlbedo(UnityNormalWS, CliffA, CliffB);
        return CMLTerrainSourceAlbedo(
            Control, BlendNoise, Variation, GrassColor1.rgb, GrassColor2.rgb,
            GrassSun * GrassRemapSun.rgb, GrassDeep * GrassRemapDeep.rgb,
            GrassTextureStrength, GrassTextureLumaMatch,
            DirtAlbedo * DirtRemap.rgb, DirtTint.rgb, SlopeAlbedo,
            UnityNormalWS, SlopeOffset, SlopeHardness);
        """,
        FLOAT3,
        {
            "Control": (control, "RGBA"),
            "BlendNoise": (blend_noise, "R"),
            "VariationA": (variation_a, "R"),
            "VariationB": (variation_b, "R"),
            "GrassColor1": grass_color_1,
            "GrassColor2": grass_color_2,
            "GrassSun": (grass_albedo[0], ""),
            "GrassDeep": (grass_albedo[1], ""),
            "GrassRemapSun": grass_remap[0],
            "GrassRemapDeep": grass_remap[1],
            "GrassTextureStrength": grass_texture_strength,
            "GrassTextureLumaMatch": grass_texture_luma_match,
            "DirtAlbedo": (dirt_albedo, ""),
            "DirtRemap": dirt_remap,
            "DirtTint": dirt_tint,
            "CliffA": (cliff_albedo_a, ""),
            "CliffB": (cliff_albedo_b, ""),
            "NormalWS": normal_ws,
            "SlopeOffset": slope_offset,
            "SlopeHardness": slope_hardness,
        },
    )

    normal = builder.custom(
        "TerrainSourceNormal",
        """
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float3 DirtNormalWS = CMLTerrainTangentNormalToWorld(
            CMLScaleTangentNormal(DirtTangentNormal, DirtNormalScale), UnityNormalWS);
        float3 GrassSunNormalWS = CMLTerrainTangentNormalToWorld(
            CMLScaleTangentNormal(GrassSunTangentNormal, GrassSunNormalScale), UnityNormalWS);
        float3 GrassDeepNormalWS = CMLTerrainTangentNormalToWorld(
            CMLScaleTangentNormal(GrassDeepTangentNormal, GrassDeepNormalScale), UnityNormalWS);
        float3 SlopeNormalWS = CMLLandmassRockSideNormal(
            UnityNormalWS, CliffTangentA, CliffTangentB, NormalStrength);
        float3 SurfaceNormal = CMLTerrainSourceNormal(
            Control, BlendNoise, GrassSunNormalWS, GrassDeepNormalWS,
            GrassNormalStrength, DirtNormalWS, SlopeNormalWS, UnityNormalWS,
            SlopeOffset, SlopeHardness);
        return CMLToUnrealDirection(SurfaceNormal);
        """,
        FLOAT3,
        {
            "Control": (control, "RGBA"),
            "BlendNoise": (blend_noise, "R"),
            "GrassSunTangentNormal": (grass_normal[0], "RGB"),
            "GrassDeepTangentNormal": (grass_normal[1], "RGB"),
            "GrassSunNormalScale": grass_normal_scale[0],
            "GrassDeepNormalScale": grass_normal_scale[1],
            "GrassNormalStrength": grass_detail_normal_strength,
            "DirtTangentNormal": (dirt_normal, "RGB"),
            "DirtNormalScale": dirt_normal_scale,
            "CliffTangentA": (cliff_normal_a, "RGB"),
            "CliffTangentB": (cliff_normal_b, "RGB"),
            "NormalStrength": normal_strength,
            "NormalWS": normal_ws,
            "SlopeOffset": slope_offset,
            "SlopeHardness": slope_hardness,
        },
    )

    specular = builder.custom(
        "TerrainSourceSpecular",
        """
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        return CMLTerrainSourceSpecular(
            Control, BlendNoise, UnityNormalWS, SlopeOffset, SlopeHardness);
        """,
        FLOAT1,
        {
            "Control": (control, "RGBA"),
            "BlendNoise": (blend_noise, "R"),
            "NormalWS": normal_ws,
            "SlopeOffset": slope_offset,
            "SlopeHardness": slope_hardness,
        },
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(normal, "", MP.MP_NORMAL)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(specular, "", MP.MP_SPECULAR)
    builder.output(builder.constant(0.0), "", MP.MP_METALLIC)
    visibility = builder._expression(unreal.MaterialExpressionLandscapeVisibilityMask)
    builder.output(visibility, "", MP.MP_OPACITY_MASK)

    # When the production grass type exists, write one density channel from
    # the same painted weights and slope mask used by the visible material.
    # This keeps blades off dirt/path and vertical cliffs by construction.
    grass_type = unreal.EditorAssetLibrary.load_asset(STARTER_ISLAND_GRASS_TYPE)
    if isinstance(grass_type, unreal.LandscapeGrassType):
        grass_density = builder.custom(
            "TerrainGrassDensity",
            """
            float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
            return CMLTerrainGrassDensity(
                Control, UnityNormalWS, SlopeOffset, SlopeHardness);
            """,
            FLOAT1,
            {
                "Control": (control, "RGBA"),
                "NormalWS": normal_ws,
                "SlopeOffset": slope_offset,
                "SlopeHardness": slope_hardness,
            },
        )
        grass_output = builder._expression(unreal.MaterialExpressionLandscapeGrassOutput)
        grass_input = unreal.GrassInput()
        grass_input.set_editor_property("name", "CML_Grass")
        grass_input.set_editor_property("grass_type", grass_type)
        grass_output.set_editor_property("grass_types", [grass_input])
        builder.connect(grass_density, "", grass_output, "CML_Grass")
        unreal.EditorAssetLibrary.set_metadata_tag(
            builder.material, "CML.LandscapeGrassType", grass_type.get_path_name()
        )
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.SourceMaterial", "M_Landscape_Solarpunk clear-weather base pass"
    )
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.ControlMap", "Unity alphamap texture, bound to _Control"
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Starter Island Foliage
# ---------------------------------------------------------------------------


def port_foliage() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_Foliage",
        unity_shader="CML/Environment/Starter Island Foliage",
        two_sided=True,  # Unity: Cull Off
        include_files=("/CML/CMLEnvironment.ush",),
    )

    base_map_st = builder.vector4("_BaseMap_ST", (1.0, 1.0, 0.0, 0.0))
    uv = builder.custom(
        "BaseMapUV",
        "return UV0 * ST.xy + ST.zw;",
        FLOAT2,
        {"UV0": builder.texcoord(0), "ST": base_map_st},
    )
    base_map = builder.texture("_BaseMap", WHITE, SAMPLER_COLOR, uv=uv)
    mask_map = builder.texture("_MaskMap", WHITE, SAMPLER_LINEAR, uv=uv)
    base_color = builder.vector("_BaseColor", (1.0, 1.0, 1.0, 1.0))
    wind_strength = builder.scalar("_WindStrength", 0.12, group="Wind")
    wind_speed = builder.scalar("_WindSpeed", 0.85, group="Wind")

    wpo = builder.custom(
        "FoliageWind",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 Offset = CMLFoliageWindOffset(
            UnityPositionWS, VertexColorRGBA, Time, WindStrength, WindSpeed);
        return CMLToUnrealOffset(Offset);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "VertexColorRGBA": builder.vertex_color4(),
            "Time": builder.time(),
            "WindStrength": wind_strength,
            "WindSpeed": wind_speed,
        },
    )

    albedo = builder.custom(
        "FoliageAlbedo",
        "return CMLFoliageAlbedo(Palette, Tint, MaskGreen);",
        FLOAT3,
        {"Palette": (base_map, ""), "Tint": base_color, "MaskGreen": (mask_map, "G")},
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.LightingStylization", "_AmbientStrength,_ShadowFloor"
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Starter Island Underbody Terrain Rock
# ---------------------------------------------------------------------------


def port_underbody_terrain_rock() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_UnderbodyTerrainRock",
        unity_shader="CML/Environment/Starter Island Underbody Terrain Rock",
        include_files=("/CML/CMLEnvironment.ush",),
    )

    tile_scale = builder.scalar("_RockTileScale", 0.0454545)
    sharpness = builder.scalar("_ProjectionSharpness", 3.4)
    brightness = builder.scalar("_CliffBrightness", 0.98)
    tint = builder.vector("_CliffTint", (1.0, 1.0, 1.0, 1.0))

    unity_ws = unity_world_position(builder)
    unity_n = builder.custom(
        "UnityVertexNormal",
        "return CMLToUnityDirection(normalize(Normal));",
        FLOAT3,
        {"Normal": builder.vertex_normal()},
    )

    def axis_uv(name: str, function: str):
        return builder.custom(
            name,
            f"return {function}(UnityPositionWS, UnityNormalWS, TileScale).xy;",
            FLOAT2,
            {"UnityPositionWS": unity_ws, "UnityNormalWS": unity_n, "TileScale": tile_scale},
        )

    uv_x = axis_uv("UnderbodyRockUVX", "CMLUnderbodyRockUVX")
    uv_y = axis_uv("UnderbodyRockUVY", "CMLUnderbodyRockUVY")
    uv_z = axis_uv("UnderbodyRockUVZ", "CMLUnderbodyRockUVZ")

    rock_x = builder.texture("_RockMap", GREY, SAMPLER_COLOR, uv=uv_x)
    rock_y = builder.texture("_RockMap", GREY, SAMPLER_COLOR, uv=uv_y, register=False)
    rock_z = builder.texture("_RockMap", GREY, SAMPLER_COLOR, uv=uv_z, register=False)

    albedo = builder.custom(
        "UnderbodyRockAlbedo",
        """
        return CMLUnderbodyRockAlbedo(
            UnityNormalWS, AlongX, AlongY, AlongZ, Sharpness, Tint.rgb, Brightness);
        """,
        FLOAT3,
        {
            "UnityNormalWS": unity_n,
            "AlongX": (rock_x, ""),
            "AlongY": (rock_y, ""),
            "AlongZ": (rock_z, ""),
            "Sharpness": sharpness,
            "Tint": tint,
            "Brightness": brightness,
        },
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material,
        "CML.LightingStylization",
        "_AmbientStrength,_DirectStrength,_ShadowFloor",
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Environment/Vertical Rock Auto Grass
# ---------------------------------------------------------------------------


def port_vertical_rock_auto_grass() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_VerticalRockAutoGrass",
        unity_shader="CML/Environment/Vertical Rock Auto Grass",
        two_sided=True,  # Unity: Cull Off
        include_files=("/CML/CMLEnvironment.ush",),
    )
    builder.material.set_editor_property("tangent_space_normal", False)

    rock_tint = builder.vector("_RockTint", (1.0, 1.0, 1.0, 1.0), group="Rock")
    grass_tint = builder.vector("_GrassTint", (1.0, 1.0, 1.0, 1.0), group="Grass")
    rock_tile = builder.scalar("_RockTileScale", 0.125, group="Rock")
    grass_tile = builder.scalar("_GrassTileScale", 0.16, group="Grass")
    sharpness = builder.scalar("_TriplanarSharpness", 5.0)
    rock_normal_strength = builder.scalar("_RockNormalStrength", 0.48, group="Rock")
    grass_normal_strength = builder.scalar("_GrassNormalStrength", 0.18, group="Grass")
    slope_start = builder.scalar("_GrassSlopeStart", 0.52, group="Grass")
    slope_end = builder.scalar("_GrassSlopeEnd", 0.73, group="Grass")
    noise_scale = builder.scalar("_GrassNoiseScale", 0.32, group="Grass")
    noise_strength = builder.scalar("_GrassNoiseStrength", 0.12, group="Grass")
    rock_shadow = builder.vector("_RockShadowColor", (0.36, 0.23, 0.23, 1.0), group="Rock")
    rock_base = builder.vector("_RockBaseColor", (0.66, 0.40, 0.32, 1.0), group="Rock")
    rock_highlight = builder.vector("_RockHighlightColor", (0.82, 0.61, 0.44, 1.0), group="Rock")
    grass_shadow = builder.vector("_GrassShadowColor", (0.24, 0.32, 0.12, 1.0), group="Grass")
    grass_base = builder.vector("_GrassBaseColor", (0.50, 0.61, 0.20, 1.0), group="Grass")
    grass_highlight = builder.vector("_GrassHighlightColor", (0.68, 0.72, 0.28, 1.0), group="Grass")
    palette_strength = builder.scalar("_PaletteStrength", 0.78)
    plane_tone = builder.scalar("_PlaneToneStrength", 0.36, group="Rock")
    surface_roughness = builder.scalar("_SurfaceRoughness", 0.88)
    macro_variation = builder.scalar("_MacroVariation", 0.055)

    unity_ws = unity_world_position(builder)
    unity_n = builder.custom(
        "UnityVertexNormal",
        "return CMLToUnityDirection(normalize(Normal));",
        FLOAT3,
        {"Normal": builder.vertex_normal()},
    )

    def axis_uv(name: str, function: str, tile):
        return builder.custom(
            name,
            f"return {function}(UnityPositionWS, UnityNormalWS, TileScale).xy;",
            FLOAT2,
            {"UnityPositionWS": unity_ws, "UnityNormalWS": unity_n, "TileScale": tile},
        )

    # The Unity Triplanar() helper uses exactly the same signed projection as
    # the underbody rock, so the UV helpers are shared.
    rock_uv = [
        axis_uv("RockUVX", "CMLUnderbodyRockUVX", rock_tile),
        axis_uv("RockUVY", "CMLUnderbodyRockUVY", rock_tile),
        axis_uv("RockUVZ", "CMLUnderbodyRockUVZ", rock_tile),
    ]
    grass_uv = [
        axis_uv("GrassUVX", "CMLUnderbodyRockUVX", grass_tile),
        axis_uv("GrassUVY", "CMLUnderbodyRockUVY", grass_tile),
        axis_uv("GrassUVZ", "CMLUnderbodyRockUVZ", grass_tile),
    ]

    rock_color = [
        builder.texture("_RockMap", WHITE, SAMPLER_COLOR, uv=uv, group="Rock", register=index == 0)
        for index, uv in enumerate(rock_uv)
    ]
    rock_normal = [
        builder.texture(
            "_RockNormalMap", FLAT_NORMAL, SAMPLER_NORMAL, uv=uv, group="Rock", register=index == 0
        )
        for index, uv in enumerate(rock_uv)
    ]
    grass_color = [
        builder.texture("_GrassMap", WHITE, SAMPLER_COLOR, uv=uv, group="Grass", register=index == 0)
        for index, uv in enumerate(grass_uv)
    ]
    grass_normal = [
        builder.texture(
            "_GrassNormalMap",
            FLAT_NORMAL,
            SAMPLER_NORMAL,
            uv=uv,
            group="Grass",
            register=index == 0,
        )
        for index, uv in enumerate(grass_uv)
    ]

    grass_mask = builder.custom(
        "AutoGrassMask",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        // The authored macro-normal vertex channel has no imported equivalent;
        // Unity's own validity gate then falls back to the geometric normal,
        // which passing zero reproduces exactly.
        return CMLAutoGrassMask(
            UnityPositionWS, UnityNormalWS, float3(0.0f, 0.0f, 0.0f),
            SlopeStart, SlopeEnd, NoiseScale, NoiseStrength);
        """,
        FLOAT1,
        {
            "WorldPosition": builder.world_position(),
            "UnityNormalWS": unity_n,
            "SlopeStart": slope_start,
            "SlopeEnd": slope_end,
            "NoiseScale": noise_scale,
            "NoiseStrength": noise_strength,
        },
    )

    albedo = builder.custom(
        "AutoGrassAlbedo",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 Weights = CMLTriplanarWeights(UnityNormalWS, Sharpness);
        float3 RockAlbedo = CMLTriplanarAlbedo(RockX, RockY, RockZ, Weights) * RockTint.rgb;
        float3 GrassAlbedo = CMLTriplanarAlbedo(GrassX, GrassY, GrassZ, Weights) * GrassTint.rgb;
        RockAlbedo = CMLRockPalette(
            RockAlbedo, RockShadow.rgb, RockBase.rgb, RockHighlight.rgb, PaletteStrength);
        GrassAlbedo = CMLRockPalette(
            GrassAlbedo, GrassShadow.rgb, GrassBase.rgb, GrassHighlight.rgb, PaletteStrength);
        RockAlbedo = CMLAutoGrassRockPlaneTone(
            RockAlbedo, UnityNormalWS, RockShadow.rgb, RockBase.rgb, RockHighlight.rgb,
            PlaneToneStrength);
        float Macro = CMLValueNoise2D(UnityPositionWS.xz * 0.075f + float2(4.8f, 17.1f));
        RockAlbedo *= 1.0f + (Macro - 0.5f) * MacroVariation;
        GrassAlbedo *= 1.0f + (Macro - 0.5f) * (MacroVariation * 0.65f);
        return lerp(RockAlbedo, GrassAlbedo, GrassMask);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "UnityNormalWS": unity_n,
            "Sharpness": sharpness,
            "RockX": (rock_color[0], ""),
            "RockY": (rock_color[1], ""),
            "RockZ": (rock_color[2], ""),
            "GrassX": (grass_color[0], ""),
            "GrassY": (grass_color[1], ""),
            "GrassZ": (grass_color[2], ""),
            "RockTint": rock_tint,
            "GrassTint": grass_tint,
            "RockShadow": rock_shadow,
            "RockBase": rock_base,
            "RockHighlight": rock_highlight,
            "GrassShadow": grass_shadow,
            "GrassBase": grass_base,
            "GrassHighlight": grass_highlight,
            "PaletteStrength": palette_strength,
            "PlaneToneStrength": plane_tone,
            "MacroVariation": macro_variation,
            "GrassMask": grass_mask,
        },
    )

    normal = builder.custom(
        "AutoGrassNormal",
        """
        float3 Weights = CMLTriplanarWeights(UnityNormalWS, Sharpness);
        float3 RockNormal = CMLTriplanarNormal(
            UnityNormalWS, RockTX, RockTY, RockTZ, Weights, RockNormalStrength);
        float3 GrassNormal = CMLTriplanarNormal(
            UnityNormalWS, GrassTX, GrassTY, GrassTZ, Weights, GrassNormalStrength);
        float3 DetailNormal = normalize(lerp(RockNormal, GrassNormal, GrassMask));
        float3 Blended = normalize(lerp(
            UnityNormalWS, DetailNormal,
            lerp(RockNormalStrength, GrassNormalStrength, GrassMask)));
        return CMLToUnrealDirection(Blended);
        """,
        FLOAT3,
        {
            "UnityNormalWS": unity_n,
            "Sharpness": sharpness,
            "RockTX": (rock_normal[0], ""),
            "RockTY": (rock_normal[1], ""),
            "RockTZ": (rock_normal[2], ""),
            "GrassTX": (grass_normal[0], ""),
            "GrassTY": (grass_normal[1], ""),
            "GrassTZ": (grass_normal[2], ""),
            "RockNormalStrength": rock_normal_strength,
            "GrassNormalStrength": grass_normal_strength,
            "GrassMask": grass_mask,
        },
    )

    # Unity drove a Blinn-Phong lobe from _SurfaceRoughness/_SpecularStrength;
    # the exponent maps onto Unreal's roughness, the strength onto specular.
    roughness = builder.custom(
        "AutoGrassRoughness", "return saturate(SurfaceRoughness);", FLOAT1,
        {"SurfaceRoughness": surface_roughness},
    )
    specular = builder.scalar("_SpecularStrength", 0.045)

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(normal, "", MP.MP_NORMAL)
    builder.output(roughness, "", MP.MP_ROUGHNESS)
    builder.output(specular, "", MP.MP_SPECULAR)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.LightingStylization", "_AmbientStrength,_ShadowFloor"
    )
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.UnportedChannel", "macroNormalOS (TEXCOORD1 float3)"
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Clean Room/*
# ---------------------------------------------------------------------------


def port_clean_room_cloud() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_CleanRoom_GeometricCloud",
        unity_shader="CML/Clean Room/Measured Geometric Cloud",
        two_sided=True,  # Unity: Cull Off
        include_files=("/CML/CMLCleanRoom.ush",),
    )
    bottom = builder.vector("_BottomColor", (0.52, 0.68, 0.74, 1.0))
    layer = builder.vector("_LayerColor", (0.76, 0.86, 0.87, 1.0))
    top = builder.vector("_TopColor", (1.0, 0.97, 0.88, 1.0))
    builder.scalar("_EdgeNoiseScale", 0.0025)
    builder.scalar("_EdgeNoise", 0.18)
    builder.scalar("_Cutoff", 0.035)
    builder.scalar("_LightResponse", 0.34)
    builder.scalar("_Opacity", 1.0)

    albedo = builder.custom(
        "CleanRoomCloud",
        """
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        return CMLCleanRoomCloudColor(UnityNormalWS, Bottom.rgb, Layer.rgb, Top.rgb);
        """,
        FLOAT3,
        {
            "NormalWS": builder.vertex_normal(),
            "Bottom": bottom,
            "Layer": layer,
            "Top": top,
        },
    )
    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.LightingStylization", "_LightResponse"
    )
    return builder.finalize()


def port_clean_room_grass_wind() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_CleanRoom_GrassWind",
        unity_shader="CML/Clean Room/Measured Grass Wind",
        blend_mode=unreal.BlendMode.BLEND_MASKED,
        two_sided=True,
        opacity_mask_clip_value=0.32,
        include_files=("/CML/CMLCleanRoom.ush",),
    )
    bottom = builder.vector("_BottomColor", (0.12, 0.25, 0.035, 1.0))
    top = builder.vector("_TopColor", (0.48, 0.68, 0.10, 1.0))
    dry = builder.vector("_DryColor", (0.62, 0.58, 0.12, 1.0))
    wind_direction = builder.vector("_WindDirection", (0.0, 0.0, -1.0, 0.0), group="Wind")
    wind_intensity = builder.scalar("_WindIntensity", 5.0, group="Wind")
    wind_weight = builder.scalar("_WindWeight", 0.25, group="Wind")
    wind_speed = builder.scalar("_WindSpeed", 1.0, group="Wind")
    builder.scalar("_AlphaCutoff", 0.32)

    unity_uv = builder.custom(
        "UnityUV", "return float2(UV0.x, 1.0f - UV0.y);", FLOAT2, {"UV0": builder.texcoord(0)}
    )

    wpo = builder.custom(
        "CleanRoomGrassWind",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float Weight = saturate(VertexColorRGBA.r);
        float3 Offset = CMLCleanRoomGrassWindOffset(
            UnityPositionWS, Weight, Time, WindDirection.xyz,
            WindIntensity, WindWeight, WindSpeed);
        return CMLToUnrealOffset(Offset);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "VertexColorRGBA": builder.vertex_color4(),
            "Time": builder.time(),
            "WindDirection": wind_direction,
            "WindIntensity": wind_intensity,
            "WindWeight": wind_weight,
            "WindSpeed": wind_speed,
        },
    )

    albedo = builder.custom(
        "CleanRoomGrassColor",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        return CMLCleanRoomGrassColor(
            UnityPositionWS, saturate(VertexColorRGBA.r), Bottom.rgb, Top.rgb, Dry.rgb);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "VertexColorRGBA": builder.vertex_color4(),
            "Bottom": bottom,
            "Top": top,
            "Dry": dry,
        },
    )

    opacity = builder.custom(
        "CleanRoomGrassAlpha", "return CMLCleanRoomGrassAlpha(UV);", FLOAT1, {"UV": unity_uv}
    )

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(opacity, "", MP.MP_OPACITY_MASK)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.LightingStylization", "_AmbientStrength,_DirectStrength"
    )
    return builder.finalize()


def port_clean_room_cliff() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_CleanRoom_Cliff",
        unity_shader="CML/Clean Room/Measured Cliff",
        include_files=("/CML/CMLCleanRoom.ush",),
    )
    rock_dark = builder.vector("_RockDark", (0.30, 0.105, 0.040, 1.0), group="Rock")
    rock_base = builder.vector("_RockBase", (0.64, 0.255, 0.090, 1.0), group="Rock")
    rock_light = builder.vector("_RockLight", (0.86, 0.49, 0.25, 1.0), group="Rock")
    macro_scale = builder.scalar("_MacroScale", 0.026, group="Rock")
    strata_scale = builder.scalar("_StrataScale", 0.082, group="Rock")
    strata_strength = builder.scalar("_StrataStrength", 0.14, group="Rock")
    grass_dark = builder.vector("_GrassDark", (0.13, 0.24, 0.045, 1.0), group="Grass")
    grass_base = builder.vector("_GrassBase", (0.30, 0.46, 0.075, 1.0), group="Grass")
    grass_light = builder.vector("_GrassLight", (0.50, 0.65, 0.12, 1.0), group="Grass")
    slope_start = builder.scalar("_GrassSlopeStart", 0.52, group="Grass")
    slope_end = builder.scalar("_GrassSlopeEnd", 0.84, group="Grass")
    breakup = builder.scalar("_GrassBreakup", 0.12, group="Grass")

    albedo = builder.custom(
        "CleanRoomCliffAlbedo",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        return CMLCleanRoomCliffAlbedo(
            UnityPositionWS, UnityNormalWS,
            RockDark.rgb, RockBase.rgb, RockLight.rgb,
            MacroScale, StrataScale, StrataStrength,
            GrassDark.rgb, GrassBase.rgb, GrassLight.rgb,
            SlopeStart, SlopeEnd, Breakup);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "NormalWS": builder.vertex_normal(),
            "RockDark": rock_dark,
            "RockBase": rock_base,
            "RockLight": rock_light,
            "MacroScale": macro_scale,
            "StrataScale": strata_scale,
            "StrataStrength": strata_strength,
            "GrassDark": grass_dark,
            "GrassBase": grass_base,
            "GrassLight": grass_light,
            "SlopeStart": slope_start,
            "SlopeEnd": slope_end,
            "Breakup": breakup,
        },
    )
    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material,
        "CML.LightingStylization",
        "_AmbientStrength,_DirectStrength,_ShadowFloor",
    )
    return builder.finalize()


# ---------------------------------------------------------------------------
# CML/Cinematics/*
# ---------------------------------------------------------------------------


def _screen_uv(builder: MasterMaterialBuilder):
    node = builder._expression(unreal.MaterialExpressionScreenPosition)
    return node


def _scene_color(builder: MasterMaterialBuilder, uv):
    """Scene colour behind a translucent surface, sampled at `uv`.

    SceneColor declares no input-name override, so the label it answers to
    varies; each candidate is tried rather than guessed.
    """
    node = builder._expression(unreal.MaterialExpressionSceneColor)
    node.set_editor_property("input_mode", unreal.MaterialSceneAttributeInputMode.COORDINATES)
    for candidate in ("", "Input", "UV", "UVs", "OffsetFraction"):
        if unreal.MaterialEditingLibrary.connect_material_expressions(uv, "", node, candidate):
            return node
    raise RuntimeError("Unable to connect UVs to the SceneColor expression")


def _unlit_builder(asset_name: str, unity_shader: str, blend_mode) -> MasterMaterialBuilder:
    return MasterMaterialBuilder(
        asset_name,
        unity_shader=unity_shader,
        blend_mode=blend_mode,
        shading_model=unreal.MaterialShadingModel.MSM_UNLIT,
        two_sided=True,  # every cinematic pass is Cull Off
        include_files=("/CML/CMLCinematics.ush",),
    )


def port_cinematic_star_streak() -> PortResult:
    # Unity: Blend One One, ZWrite Off -> additive, depth-testing but not writing.
    builder = _unlit_builder(
        "M_CML_Cin_StarStreak", "CML/Cinematics/Star Streak", unreal.BlendMode.BLEND_ADDITIVE
    )
    # The intro renders all 3000 streaks through one InstancedStaticMesh. If
    # this permutation is not serialized Unreal substitutes WorldGridMaterial,
    # which appears as sparse opaque white bars on black in PIE/cooked builds.
    builder.material.set_editor_property("used_with_instanced_static_meshes", True)
    tint = builder.vector4("_Color", (0.72, 0.88, 1.0, 1.0))
    core_boost = builder.scalar("_CoreBoost", 2.4)
    softness = builder.scalar("_Softness", 5.5)

    emissive = builder.custom(
        "StarStreak",
        """
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        return CMLStarStreak(UnityUV, VertexColorRGBA, Tint, CoreBoost, Softness);
        """,
        FLOAT3,
        {
            "UV0": builder.texcoord(0),
            "VertexColorRGBA": builder.vertex_color4(),
            "Tint": tint,
            "CoreBoost": core_boost,
            "Softness": softness,
        },
    )
    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_OPACITY)
    return builder.finalize()


def port_cinematic_warp_tunnel() -> PortResult:
    builder = _unlit_builder(
        "M_CML_Cin_WarpTunnel", "CML/Cinematics/Warp Tunnel", unreal.BlendMode.BLEND_ADDITIVE
    )
    core = builder.vector("_CoreColor", (0.92, 0.98, 1.0, 1.0))
    mid = builder.vector("_MidColor", (0.24, 0.62, 1.0, 1.0))
    edge = builder.vector("_EdgeColor", (0.34, 0.12, 0.72, 1.0))
    intensity = builder.scalar("_Intensity", 0.0)
    speed = builder.scalar("_Speed", 3.4)
    density = builder.scalar("_StreakDensity", 168.0)
    length_ = builder.scalar("_StreakLength", 2.6)
    turbulence = builder.scalar("_Turbulence", 0.28)
    twist = builder.scalar("_Twist", 0.35)
    chromatic = builder.scalar("_ChromaticSplit", 0.035)
    end_fade = builder.scalar("_EndFade", 0.22)
    core_glow = builder.scalar("_CoreGlow", 1.1)

    emissive = builder.custom(
        "WarpTunnel",
        """
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        return CMLWarpTunnel(
            UnityUV, Time, Core.rgb, Mid.rgb, Edge.rgb, Intensity, Speed,
            StreakDensity, StreakLength, Turbulence, Twist, ChromaticSplit,
            EndFade, CoreGlow);
        """,
        FLOAT3,
        {
            "UV0": builder.texcoord(0),
            "Time": builder.time(),
            "Core": core,
            "Mid": mid,
            "Edge": edge,
            "Intensity": intensity,
            "Speed": speed,
            "StreakDensity": density,
            "StreakLength": length_,
            "Turbulence": turbulence,
            "Twist": twist,
            "ChromaticSplit": chromatic,
            "EndFade": end_fade,
            "CoreGlow": core_glow,
        },
    )
    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_OPACITY)
    return builder.finalize()


def port_cinematic_portal_veil() -> PortResult:
    # Unity: Blend One OneMinusSrcAlpha -> premultiplied alpha, which is
    # Unreal's AlphaComposite blend mode.
    builder = _unlit_builder(
        "M_CML_Cin_PortalVeil", "CML/Cinematics/Portal Veil", BLEND_ALPHA_COMPOSITE
    )
    inner = builder.vector("_InnerColor", (0.72, 0.94, 1.0, 1.0))
    outer = builder.vector("_OuterColor", (0.20, 0.44, 0.96, 1.0))
    rim = builder.vector("_RimColor", (0.94, 0.86, 0.55, 1.0))
    charge = builder.scalar("_Charge", 0.0)
    swirl_speed = builder.scalar("_SwirlSpeed", 1.15)
    swirl_scale = builder.scalar("_SwirlScale", 3.4)
    refraction = builder.scalar("_Refraction", 0.075)
    rim_width = builder.scalar("_RimWidth", 0.16)
    intensity = builder.scalar("_Intensity", 1.5)

    polar = builder.custom(
        "PortalVeilPolar",
        """
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        return CMLPortalVeilPolar(UnityUV, SwirlScale, SwirlSpeed, Time);
        """,
        FLOAT2,
        {
            "UV0": builder.texcoord(0),
            "SwirlScale": swirl_scale,
            "SwirlSpeed": swirl_speed,
            "Time": builder.time(),
        },
    )
    refracted_uv = builder.custom(
        "PortalVeilRefractedUV",
        "return ScreenUV + CMLPortalVeilRefractionOffset(Polar, Refraction, Charge);",
        FLOAT2,
        {
            "ScreenUV": (_screen_uv(builder), "ViewportUV"),
            "Polar": polar,
            "Refraction": refraction,
            "Charge": charge,
        },
    )
    background = _scene_color(builder, refracted_uv)

    veil = builder.custom(
        "PortalVeil",
        """
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        return CMLPortalVeil(
            UnityUV, Polar, Background, Time, Inner.rgb, Outer.rgb, Rim.rgb,
            Charge, RimWidth, Intensity);
        """,
        FLOAT4,
        {
            "UV0": builder.texcoord(0),
            "Polar": polar,
            "Background": (background, "RGB"),
            "Time": builder.time(),
            "Inner": inner,
            "Outer": outer,
            "Rim": rim,
            "Charge": charge,
            "RimWidth": rim_width,
            "Intensity": intensity,
        },
    )
    emissive = builder.custom("PortalVeilColor", "return Veil.rgb;", FLOAT3, {"Veil": veil})
    opacity = builder.custom("PortalVeilCoverage", "return Veil.a;", FLOAT1, {"Veil": veil})
    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    builder.output(opacity, "", MP.MP_OPACITY)
    return builder.finalize()


def port_cinematic_rift() -> PortResult:
    builder = _unlit_builder(
        "M_CML_Cin_Rift", "CML/Cinematics/Rift", BLEND_ALPHA_COMPOSITE
    )
    core = builder.vector("_CoreColor", (1.0, 0.97, 0.92, 1.0))
    energy = builder.vector("_EnergyColor", (0.36, 0.82, 1.0, 1.0))
    rim = builder.vector("_RimColor", (0.62, 0.24, 1.0, 1.0))
    void = builder.vector("_VoidColor", (0.02, 0.03, 0.09, 1.0))
    openness = builder.scalar("_Openness", 0.0)
    width = builder.scalar("_Width", 0.32)
    edge_softness = builder.scalar("_EdgeSoftness", 0.06)
    edge_turbulence = builder.scalar("_EdgeTurbulence", 0.42)
    turbulence_scale = builder.scalar("_TurbulenceScale", 6.5)
    turbulence_speed = builder.scalar("_TurbulenceSpeed", 1.7)
    refraction = builder.scalar("_Refraction", 0.11)
    swirl_intensity = builder.scalar("_SwirlIntensity", 0.9)
    swirl_speed = builder.scalar("_SwirlSpeed", 1.3)
    filament = builder.scalar("_FilamentIntensity", 1.2)
    intensity = builder.scalar("_Intensity", 1.0)

    lens_inputs = {
        "UV0": builder.texcoord(0),
        "ScreenUV": (_screen_uv(builder), "ViewportUV"),
        "Openness": openness,
        "Width": width,
        "EdgeSoftness": edge_softness,
        "EdgeTurbulence": edge_turbulence,
        "TurbulenceScale": turbulence_scale,
        "TurbulenceSpeed": turbulence_speed,
        "Refraction": refraction,
        "Time": builder.time(),
    }
    refracted_uv = builder.custom(
        "RiftLensUV",
        """
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        return ScreenUV + CMLRiftLensOffset(
            UnityUV, Openness, Width, EdgeSoftness, EdgeTurbulence,
            TurbulenceScale, TurbulenceSpeed, Refraction, Time);
        """,
        FLOAT2,
        lens_inputs,
    )
    background = _scene_color(builder, refracted_uv)

    tear = builder.custom(
        "Rift",
        """
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        return CMLRift(
            UnityUV, Background, Time, Core.rgb, Energy.rgb, Rim.rgb, Void.rgb,
            Openness, Width, EdgeSoftness, EdgeTurbulence, TurbulenceScale,
            TurbulenceSpeed, SwirlIntensity, SwirlSpeed, FilamentIntensity, Intensity);
        """,
        FLOAT4,
        {
            "UV0": builder.texcoord(0),
            "Background": (background, "RGB"),
            "Time": builder.time(),
            "Core": core,
            "Energy": energy,
            "Rim": rim,
            "Void": void,
            "Openness": openness,
            "Width": width,
            "EdgeSoftness": edge_softness,
            "EdgeTurbulence": edge_turbulence,
            "TurbulenceScale": turbulence_scale,
            "TurbulenceSpeed": turbulence_speed,
            "SwirlIntensity": swirl_intensity,
            "SwirlSpeed": swirl_speed,
            "FilamentIntensity": filament,
            "Intensity": intensity,
        },
    )
    emissive = builder.custom("RiftColor", "return Tear.rgb;", FLOAT3, {"Tear": tear})
    opacity = builder.custom("RiftCoverage", "return Tear.a;", FLOAT1, {"Tear": tear})
    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    builder.output(opacity, "", MP.MP_OPACITY)
    return builder.finalize()


def port_cinematic_deep_space() -> PortResult:
    # A skybox: unlit, opaque, drawn on the inside of a sky mesh.
    builder = _unlit_builder(
        "M_CML_Cin_DeepSpace", "CML/Cinematics/Deep Space", unreal.BlendMode.BLEND_OPAQUE
    )
    space = builder.vector("_SpaceColor", (0.004, 0.006, 0.017, 1.0))
    nebula_a = builder.vector("_NebulaColorA", (0.18, 0.09, 0.42, 1.0), group="Nebula")
    nebula_b = builder.vector("_NebulaColorB", (0.02, 0.32, 0.55, 1.0), group="Nebula")
    nebula_c = builder.vector("_NebulaColorC", (0.72, 0.24, 0.46, 1.0), group="Nebula")
    nebula_scale = builder.scalar("_NebulaScale", 1.35, group="Nebula")
    nebula_coverage = builder.scalar("_NebulaCoverage", 0.52, group="Nebula")
    nebula_contrast = builder.scalar("_NebulaContrast", 2.35, group="Nebula")
    nebula_intensity = builder.scalar("_NebulaIntensity", 1.15, group="Nebula")
    galaxy_axis = builder.vector("_GalaxyAxis", (0.31, 0.86, -0.4, 0.0), group="Galaxy")
    galaxy_width = builder.scalar("_GalaxyWidth", 0.42, group="Galaxy")
    galaxy_intensity = builder.scalar("_GalaxyIntensity", 0.85, group="Galaxy")
    galaxy_color = builder.vector("_GalaxyColor", (0.56, 0.62, 0.86, 1.0), group="Galaxy")
    star_density = builder.scalar("_StarDensity", 0.055, group="Stars")
    star_brightness = builder.scalar("_StarBrightness", 4.2, group="Stars")
    star_sharpness = builder.scalar("_StarSharpness", 12.0, group="Stars")
    twinkle_speed = builder.scalar("_TwinkleSpeed", 1.6, group="Stars")
    warp_blend = builder.scalar("_WarpBlend", 0.0, group="Warp")
    warp_axis = builder.vector("_WarpAxis", (0.0, 0.0, 1.0, 0.0), group="Warp")
    warp_stretch = builder.scalar("_WarpStretch", 0.35, group="Warp")
    exposure = builder.scalar("_Exposure", 1.0)

    emissive = builder.custom(
        "DeepSpace",
        """
        // Unity fed the sky mesh's outward object direction; on an Unreal sky
        // dome the view ray is the same vector, and it stays correct whatever
        // mesh the material is applied to.
        float3 UnityDirection = CMLToUnityDirection(-normalize(CameraVector));
        return CMLDeepSpace(
            UnityDirection, Time, Space.rgb, NebulaA.rgb, NebulaB.rgb, NebulaC.rgb,
            Galaxy.rgb, GalaxyAxis.xyz, WarpAxis.xyz,
            NebulaScale, NebulaCoverage, NebulaContrast, NebulaIntensity,
            GalaxyWidth, GalaxyIntensity,
            StarDensity, StarBrightness, StarSharpness, TwinkleSpeed,
            WarpBlend, WarpStretch, Exposure);
        """,
        FLOAT3,
        {
            "CameraVector": builder.camera_vector(),
            "Time": builder.time(),
            "Space": space,
            "NebulaA": nebula_a,
            "NebulaB": nebula_b,
            "NebulaC": nebula_c,
            "Galaxy": galaxy_color,
            "GalaxyAxis": galaxy_axis,
            "WarpAxis": warp_axis,
            "NebulaScale": nebula_scale,
            "NebulaCoverage": nebula_coverage,
            "NebulaContrast": nebula_contrast,
            "NebulaIntensity": nebula_intensity,
            "GalaxyWidth": galaxy_width,
            "GalaxyIntensity": galaxy_intensity,
            "StarDensity": star_density,
            "StarBrightness": star_brightness,
            "StarSharpness": star_sharpness,
            "TwinkleSpeed": twinkle_speed,
            "WarpBlend": warp_blend,
            "WarpStretch": warp_stretch,
            "Exposure": exposure,
        },
    )
    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    return builder.finalize()


def port_ground_cover() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_GroundCover",
        unity_shader="CML/Environment/Starter Island Ground Cover",
        two_sided=True,  # Unity: Cull Off, opaque geometry queue
        include_files=("/CML/CMLEnvironment.ush",),
    )
    base_map_st = builder.vector4("_BaseMap_ST", (1.0, 1.0, 0.0, 0.0))
    uv = builder.custom(
        "BaseMapUV",
        "return UV0 * ST.xy + ST.zw;",
        FLOAT2,
        {"UV0": builder.texcoord(0), "ST": base_map_st},
    )
    base_map = builder.texture("_BaseMap", WHITE, SAMPLER_COLOR, uv=uv)
    mask_map = builder.texture("_MaskMap", WHITE, SAMPLER_LINEAR, uv=uv)
    base_color = builder.vector("_BaseColor", (1.0, 1.0, 1.0, 1.0))
    root_tint = builder.vector("_RootTint", (0.43, 0.57, 0.24, 1.0))
    tip_tint = builder.vector("_TipTint", (0.71, 0.84, 0.39, 1.0))
    wind_strength = builder.scalar("_WindStrength", 0.22, group="Wind")
    wind_speed = builder.scalar("_WindSpeed", 1.15, group="Wind")
    gust_scale = builder.scalar("_GustScale", 0.02, group="Wind")

    wpo = builder.custom(
        "GroundCoverWind",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 Offset = CMLGroundCoverWindOffset(
            UnityPositionWS, VertexColorRGBA, Time, WindStrength, WindSpeed, GustScale);
        return CMLToUnrealOffset(Offset);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "VertexColorRGBA": builder.vertex_color4(),
            "Time": builder.time(),
            "WindStrength": wind_strength,
            "WindSpeed": wind_speed,
            "GustScale": gust_scale,
        },
    )
    albedo = builder.custom(
        "GroundCoverAlbedo",
        "return CMLGroundCoverAlbedo(VertexColorRGBA, Atlas, MaskGreen, Tint.rgb, Root.rgb, Tip.rgb);",
        FLOAT3,
        {
            "VertexColorRGBA": builder.vertex_color4(),
            "Atlas": (base_map, ""),
            "MaskGreen": (mask_map, "G"),
            "Tint": base_color,
            "Root": root_tint,
            "Tip": tip_tint,
        },
    )
    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.LightingStylization", "_AmbientStrength,_ShadowFloor"
    )
    return builder.finalize()


def port_terrain_reference_match() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_TerrainReferenceMatch",
        unity_shader="CML/Environment/Starter Island Terrain Reference Match",
        include_files=("/CML/CMLEnvironment.ush",),
    )

    control = builder.texture("_Control", BLACK, SAMPLER_LINEAR, group="Terrain")
    splat_st = [
        builder.vector4(f"_Splat{index}_ST", (1.0, 1.0, 0.0, 0.0), group="Terrain")
        for index in range(4)
    ]
    splat_uv = [
        builder.custom(
            f"SplatUV{index}",
            "return UV0 * ST.xy + ST.zw;",
            FLOAT2,
            {"UV0": builder.texcoord(0), "ST": splat_st[index]},
        )
        for index in range(3)
    ]
    splat = [
        builder.texture(f"_Splat{index}", GREY, SAMPLER_COLOR, uv=splat_uv[index], group="Terrain")
        for index in range(3)
    ]
    diffuse_remap = [
        builder.vector(f"_DiffuseRemapScale{index}", (1.0, 1.0, 1.0, 1.0), group="Terrain")
        for index in range(4)
    ]
    terrain_size = builder.vector("_TerrainSizeXZ", (660.0, 500.0, 0.0, 0.0), group="Terrain")
    cliff_slope_start = builder.scalar("_CliffSlopeStart", 0.24, group="Cliff")
    cliff_slope_end = builder.scalar("_CliffSlopeEnd", 0.48, group="Cliff")
    cliff_sharpness = builder.scalar("_CliffProjectionSharpness", 4.0, group="Cliff")
    cliff_brightness = builder.scalar("_CliffBrightness", 1.0, group="Cliff")
    cliff_tint = builder.vector("_CliffTint", (1.0, 1.0, 1.0, 1.0), group="Cliff")
    lip_color = builder.vector("_LipColor", (0.34, 0.36, 0.09, 1.0), group="Cliff")
    lip_strength = builder.scalar("_LipStrength", 0.18, group="Cliff")

    unity_ws = unity_world_position(builder)
    unity_n = builder.custom(
        "UnityVertexNormal",
        "return CMLToUnityDirection(normalize(Normal));",
        FLOAT3,
        {"Normal": builder.vertex_normal()},
    )
    tile_scale = builder.custom(
        "ReferenceMatchCliffTileScale",
        "return CMLReferenceMatchCliffTileScale(TerrainSize.xy, Splat3ST.xy);",
        FLOAT1,
        {"TerrainSize": terrain_size, "Splat3ST": splat_st[3]},
    )

    def cliff_uv(name: str, function: str):
        return builder.custom(
            name,
            f"return {function}(UnityPositionWS, UnityNormalWS, TileScale);",
            FLOAT2,
            {"UnityPositionWS": unity_ws, "UnityNormalWS": unity_n, "TileScale": tile_scale},
        )

    cliff_x = builder.texture(
        "_Splat3", GREY, SAMPLER_COLOR, uv=cliff_uv("CliffUVX", "CMLReferenceMatchCliffUVX"),
        group="Terrain",
    )
    cliff_z = builder.texture(
        "_Splat3", GREY, SAMPLER_COLOR, uv=cliff_uv("CliffUVZ", "CMLReferenceMatchCliffUVZ"),
        group="Terrain", register=False,
    )

    albedo = builder.custom(
        "ReferenceMatchAlbedo",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 Cliff = CMLReferenceMatchCliff(
            UnityNormalWS, CliffX, CliffZ, CliffSharpness, Remap3.rgb, CliffTint.rgb, CliffBrightness);
        return CMLReferenceMatchAlbedo(
            UnityPositionWS, UnityNormalWS, Control,
            Splat0 * Remap0.rgb, Splat1 * Remap1.rgb, Splat2 * Remap2.rgb, Cliff,
            CliffSlopeStart, CliffSlopeEnd, LipColor.rgb, LipStrength);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "UnityNormalWS": unity_n,
            "Control": (control, "RGBA"),
            "Splat0": (splat[0], ""),
            "Splat1": (splat[1], ""),
            "Splat2": (splat[2], ""),
            "Remap0": diffuse_remap[0],
            "Remap1": diffuse_remap[1],
            "Remap2": diffuse_remap[2],
            "Remap3": diffuse_remap[3],
            "CliffX": (cliff_x, ""),
            "CliffZ": (cliff_z, ""),
            "CliffSharpness": cliff_sharpness,
            "CliffTint": cliff_tint,
            "CliffBrightness": cliff_brightness,
            "CliffSlopeStart": cliff_slope_start,
            "CliffSlopeEnd": cliff_slope_end,
            "LipColor": lip_color,
            "LipStrength": lip_strength,
        },
    )
    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material,
        "CML.LightingStylization",
        "_AmbientStrength,_DirectStrength,_ShadowFloor",
    )
    return builder.finalize()


def port_ground_detail() -> PortResult:
    builder = MasterMaterialBuilder(
        "M_CML_Env_GroundDetail",
        unity_shader="CML/Environment/Starter Island Ground Detail",
        blend_mode=unreal.BlendMode.BLEND_MASKED,
        two_sided=True,
        opacity_mask_clip_value=0.38,
        include_files=("/CML/CMLGroundDetail.ush",),
    )

    builder.scalar("_Cutoff", 0.38)
    fallback = builder.vector("_FallbackColor", (0.29, 0.48, 0.20, 1.0))
    root_brightness = builder.scalar("_RootBrightness", 0.96)
    tip_brightness = builder.scalar("_TipBrightness", 1.025)
    wind_strength = builder.scalar("_WindStrength", 0.022, group="Wind")
    wind_speed = builder.scalar("_WindSpeed", 1.38, group="Wind")
    gust_strength = builder.scalar("_GustStrength", 0.28, group="Wind")
    macro_variation = builder.scalar("_TerrainMacroVariation", 0.13, group="Terrain")
    far_start = builder.scalar("_FarMatchStart", 24.0)
    far_end = builder.scalar("_FarMatchEnd", 54.0)
    terrain_enabled = builder.scalar("_CMLTerrainBlendEnabled", 0.0, group="Terrain")
    terrain_origin = builder.vector4(
        "_CMLTerrainBlendOriginInvSize", (0.0, 0.0, 1.0, 1.0), group="Terrain"
    )

    # The blade anchors its wind, its terrain lookup and its shading at the
    # root, so every world-space term is evaluated there rather than at the
    # deformed vertex.
    anchor = builder.custom(
        "GroundDetailAnchor",
        """
        // Object-space root of this blade, taken back to world space.
        float3 UnityPositionOS = CMLToUnityPosition(LocalPosition);
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        return float3(UnityPositionWS.x, UnityPositionWS.y - UnityPositionOS.y, UnityPositionWS.z);
        """,
        FLOAT3,
        {
            "LocalPosition": _local_position(builder),
            "WorldPosition": builder.world_position(),
        },
    )

    terrain_uv = builder.custom(
        "GroundDetailTerrainUV",
        "return saturate((Anchor.xz - OriginInvSize.xy) * OriginInvSize.zw) * float2(1.0f, -1.0f);",
        FLOAT2,
        {"Anchor": anchor, "OriginInvSize": terrain_origin},
    )
    control = builder.texture(
        "_CMLTerrainBlendControl", BLACK, SAMPLER_LINEAR, uv=terrain_uv, group="Terrain"
    )

    layer_samples = []
    for index in range(4):
        layer_st = builder.vector4(
            f"_CMLTerrainBlendLayer{index}_ST", (1.0, 1.0, 0.0, 0.0), group="Terrain"
        )
        layer_uv = builder.custom(
            f"GroundDetailLayerUV{index}",
            """
            float2 LocalXZ = Anchor.xz - OriginInvSize.xy;
            float2 UV = LocalXZ * ST.xy + ST.zw;
            return float2(UV.x, -UV.y);
            """,
            FLOAT2,
            {"Anchor": anchor, "OriginInvSize": terrain_origin, "ST": layer_st},
        )
        layer = builder.texture(
            f"_CMLTerrainBlendLayer{index}", GREY, SAMPLER_COLOR, uv=layer_uv, group="Terrain"
        )
        remap_min = builder.vector(
            f"_CMLTerrainBlendLayer{index}_RemapMin", (0.0, 0.0, 0.0, 0.0), group="Terrain"
        )
        remap_scale = builder.vector(
            f"_CMLTerrainBlendLayer{index}_RemapScale", (1.0, 1.0, 1.0, 1.0), group="Terrain"
        )
        layer_samples.append((layer, remap_min, remap_scale))

    albedo_inputs = {
        "Anchor": anchor,
        "WorldPosition": builder.world_position(),
        "CameraPosition": builder._expression(unreal.MaterialExpressionCameraPositionWS),
        "VertexColorRGBA": builder.vertex_color4(),
        "Control": (control, "RGBA"),
        "Fallback": fallback,
        "TerrainEnabled": terrain_enabled,
        "MacroVariation": macro_variation,
        "RootBrightness": root_brightness,
        "TipBrightness": tip_brightness,
        "FarMatchStart": far_start,
        "FarMatchEnd": far_end,
    }
    for index, (layer, remap_min, remap_scale) in enumerate(layer_samples):
        albedo_inputs[f"Layer{index}"] = (layer, "")
        albedo_inputs[f"RemapMin{index}"] = remap_min
        albedo_inputs[f"RemapScale{index}"] = remap_scale

    albedo = builder.custom(
        "GroundDetailAlbedo",
        """
        float3 TerrainAlbedo = Fallback.rgb;
        if (TerrainEnabled >= 0.5f)
        {
            TerrainAlbedo = CMLGroundDetailTerrainAlbedo(
                Anchor.xz, Control,
                CMLGroundDetailRemapLayer(Layer0, RemapMin0.rgb, RemapScale0.rgb),
                CMLGroundDetailRemapLayer(Layer1, RemapMin1.rgb, RemapScale1.rgb),
                CMLGroundDetailRemapLayer(Layer2, RemapMin2.rgb, RemapScale2.rgb),
                CMLGroundDetailRemapLayer(Layer3, RemapMin3.rgb, RemapScale3.rgb),
                MacroVariation);
        }
        float CameraDistanceMetres = length(
            CMLToUnityPosition(WorldPosition) - CMLToUnityPosition(CameraPosition));
        return CMLGroundDetailBladeColor(
            TerrainAlbedo, VertexColorRGBA, CameraDistanceMetres,
            RootBrightness, TipBrightness, FarMatchStart, FarMatchEnd);
        """,
        FLOAT3,
        albedo_inputs,
    )

    wpo = builder.custom(
        "GroundDetailWind",
        """
        float3 Offset = CMLGroundDetailWindOffset(
            Anchor, VertexColorRGBA, Time, WindStrength, WindSpeed, GustStrength);
        return CMLToUnrealOffset(Offset);
        """,
        FLOAT3,
        {
            "Anchor": anchor,
            "VertexColorRGBA": builder.vertex_color4(),
            "Time": builder.time(),
            "WindStrength": wind_strength,
            "WindSpeed": wind_speed,
            "GustStrength": gust_strength,
        },
    )

    unity_uv = builder.custom(
        "UnityUV", "return float2(UV0.x, 1.0f - UV0.y);", FLOAT2, {"UV0": builder.texcoord(0)}
    )
    opacity = builder.custom(
        "GroundDetailBladeAlpha", "return CMLGroundDetailBladeAlpha(UV);", FLOAT1, {"UV": unity_uv}
    )
    normal = builder.custom(
        "GroundDetailNormal",
        """
        float3 UnityFaceNormal = CMLToUnityDirection(normalize(NormalWS));
        return CMLToUnrealDirection(CMLGroundDetailNormal(UnityFaceNormal));
        """,
        FLOAT3,
        {"NormalWS": builder.vertex_normal()},
    )
    builder.material.set_editor_property("tangent_space_normal", False)

    builder.output(albedo, "", MP.MP_BASE_COLOR)
    builder.output(opacity, "", MP.MP_OPACITY_MASK)
    builder.output(normal, "", MP.MP_NORMAL)
    builder.output(builder.constant(1.0), "", MP.MP_ROUGHNESS)
    builder.output(builder.constant(0.0), "", MP.MP_SPECULAR)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material,
        "CML.LightingStylization",
        "_AmbientStrength,_ShadowFloor,_ShadowAttenuationFloor",
    )
    return builder.finalize()


def port_stylized_water() -> PortResult:
    # Unity composed its own final pixel and returned alpha 1. Unreal only
    # exposes SceneColor/SceneDepth to translucent materials, so the port is
    # unlit + translucent with opacity 1: same result, legal node set.
    builder = MasterMaterialBuilder(
        "M_CML_Env_StylizedWater",
        unity_shader="CML/Environment/Starter Island Stylized Water",
        blend_mode=unreal.BlendMode.BLEND_TRANSLUCENT,
        shading_model=unreal.MaterialShadingModel.MSM_UNLIT,
        include_files=("/CML/CMLStylizedWater.ush",),
    )

    shallow = builder.vector4("_ShallowColor", (0.34, 0.86, 0.86, 0.62))
    deep = builder.vector4("_DeepColor", (0.08, 0.45, 0.66, 0.80))
    foam = builder.vector("_FoamColor", (0.80, 1.00, 0.94, 1.0))
    depth_range = builder.scalar("_DepthRange", 4.0)
    foam_distance = builder.scalar("_FoamDistance", 0.65, group="Foam")
    foam_feather = builder.scalar("_FoamFeather", 0.38, group="Foam")
    wave_scale_a = builder.scalar("_WaveScaleA", 0.11, group="Waves")
    wave_scale_b = builder.scalar("_WaveScaleB", 0.27, group="Waves")
    wave_speed_a = builder.scalar("_WaveSpeedA", 0.58, group="Waves")
    wave_speed_b = builder.scalar("_WaveSpeedB", 1.16, group="Waves")
    wave_strength = builder.scalar("_WaveStrength", 0.14, group="Waves")
    displacement = builder.scalar("_DisplacementStrength", 0.04, group="Waves")
    flow_scale = builder.scalar("_FlowScale", 1.8, group="Flow")
    flow_speed = builder.scalar("_FlowSpeed", 1.35, group="Flow")
    cascade_foam = builder.scalar("_CascadeFoamStrength", 1.0, group="Foam")
    fresnel_power = builder.scalar("_FresnelPower", 3.2)
    glint_power = builder.scalar("_GlintPower", 72.0)
    glint_strength = builder.scalar("_GlintStrength", 0.48)
    refraction_strength = builder.scalar("_RefractionStrength", 0.022)
    fresnel_strength = builder.scalar("_FresnelStrength", 0.34)
    smoothness = builder.scalar("_Smoothness", 0.88)
    reflection_strength = builder.scalar("_ReflectionStrength", 0.62)
    detail_scale = builder.scalar("_NormalDetailScale", 0.72, group="Waves")
    detail_speed = builder.scalar("_NormalDetailSpeed", 1.72, group="Waves")
    detail_strength = builder.scalar("_NormalDetailStrength", 0.075, group="Waves")
    cascade_normal = builder.scalar("_CascadeNormalStrength", 0.19, group="Waves")
    ambient_strength = builder.scalar("_AmbientStrength", 1.0)
    transmission = builder.scalar("_TransmissionStrength", 0.76)
    crest_strength = builder.scalar("_CrestStrength", 0.28, group="Foam")
    foam_intensity = builder.scalar("_FoamIntensity", 1.0, group="Foam")
    color_boost = builder.scalar("_ColorBoost", 1.08)
    opacity_param = builder.scalar("_Opacity", 0.72)
    # The runtime visual-environment authority drives these parameters.  Do not
    # read SkyAtmosphere light nodes here: the source-style sky is an opaque
    # material dome, so those nodes are not the authoritative day/night state.
    ambient_color = builder.vector("_CMLAmbientColor", (0.35, 0.42, 0.5, 1.0))
    sun_direction = builder.vector("_CMLSunDirectionWS", (0.0, 0.0, 1.0, 0.0))
    sun_illuminance = builder.vector("_CMLSunColor", (1.0, 0.879622, 0.760525, 1.0))
    builder.scalar("_CMLDay01", 1.0, group="Environment")
    builder.scalar("_CMLDawnPhase", 0.0, group="Environment")
    builder.scalar("_CMLEarlyDuskPhase", 0.0, group="Environment")
    builder.scalar("_CMLLateDuskPhase", 0.0, group="Environment")

    wpo = builder.custom(
        "WaterDisplacement",
        """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        float3 Offset = CMLWaterDisplacement(
            UnityPositionWS, UnityNormalWS, UnityUV, VertexColorRGBA, Time,
            WaveScaleA, WaveScaleB, WaveSpeedA, WaveSpeedB,
            FlowScale, FlowSpeed, DisplacementStrength);
        return CMLToUnrealOffset(Offset);
        """,
        FLOAT3,
        {
            "WorldPosition": builder.world_position(),
            "NormalWS": builder.vertex_normal(),
            "UV0": builder.texcoord(0),
            "VertexColorRGBA": builder.vertex_color4(),
            "Time": builder.time(),
            "WaveScaleA": wave_scale_a,
            "WaveScaleB": wave_scale_b,
            "WaveSpeedA": wave_speed_a,
            "WaveSpeedB": wave_speed_b,
            "FlowScale": flow_scale,
            "FlowSpeed": flow_speed,
            "DisplacementStrength": displacement,
        },
    )

    screen = _screen_uv(builder)
    scene_depth = builder._expression(unreal.MaterialExpressionSceneDepth)
    pixel_depth = builder._expression(unreal.MaterialExpressionPixelDepth)
    surface_inputs = {
        "WorldPosition": builder.world_position(),
        "NormalWS": builder.vertex_normal(),
        "UV0": builder.texcoord(0),
        "VertexColorRGBA": builder.vertex_color4(),
        "Time": builder.time(),
        "WaveScaleA": wave_scale_a,
        "WaveScaleB": wave_scale_b,
        "WaveSpeedA": wave_speed_a,
        "WaveSpeedB": wave_speed_b,
        "WaveStrength": wave_strength,
        "NormalDetailScale": detail_scale,
        "NormalDetailSpeed": detail_speed,
        "NormalDetailStrength": detail_strength,
        "CascadeNormalStrength": cascade_normal,
        "FlowScale": flow_scale,
        "FlowSpeed": flow_speed,
        "CascadeFoamStrength": cascade_foam,
    }
    surface_body = """
        float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);
        float3 UnityNormalWS = CMLToUnityDirection(normalize(NormalWS));
        float2 UnityUV = float2(UV0.x, 1.0f - UV0.y);
        CMLWaterSurface Surface = CMLWaterEvaluateSurface(
            UnityPositionWS, UnityNormalWS, UnityUV, VertexColorRGBA, Time,
            WaveScaleA, WaveScaleB, WaveSpeedA, WaveSpeedB, WaveStrength,
            NormalDetailScale, NormalDetailSpeed, NormalDetailStrength,
            CascadeNormalStrength, FlowScale, FlowSpeed, CascadeFoamStrength);
    """
    # The final pixel Custom node has many inputs.  Feeding an explicit
    # MaterialExpressionWorldPosition as its first pin is not stable in UE 5.8:
    # the saved graph loses that one connection during recompilation even
    # though the smaller vertex/refraction Custom nodes retain it.  Pixel
    # Custom expressions already receive FMaterialPixelParameters, so obtain
    # the exact absolute position from those parameters instead.  This is not
    # an approximation and avoids a disconnected pin/default-material fallback.
    pixel_surface_body = surface_body.replace(
        "float3 UnityPositionWS = CMLToUnityPosition(WorldPosition);",
        "float3 UnityPositionWS = CMLToUnityPosition(WSDemote(GetWorldPosition(Parameters)));",
        1,
    )
    pixel_surface_inputs = {
        name: value for name, value in surface_inputs.items() if name != "WorldPosition"
    }

    # The refraction offset needs the ripple normal, so the surface evaluation
    # runs twice: once to steer the scene-colour lookup, once for the shading.
    refracted_uv = builder.custom(
        "WaterRefractedUV",
        surface_body
        + """
        float WaterDepthMetres = max(0.0f, (SceneDepth - PixelDepth)) * CML_UNITY_METRES_PER_UNREAL_UNIT;
        float RefractionDepthDamping = smoothstep(
            0.0f, max(FoamDistance + FoamFeather, 0.01f), WaterDepthMetres);
        float2 Offset = (Surface.RefractionVector) * RefractionStrength
            * lerp(1.0f, 0.76f, Surface.CascadeMask) * RefractionDepthDamping;
        return clamp(ScreenUV + Offset, float2(0.002f, 0.002f), float2(0.998f, 0.998f));
        """,
        FLOAT2,
        {
            **surface_inputs,
            "ScreenUV": (screen, "ViewportUV"),
            "SceneDepth": scene_depth,
            "PixelDepth": pixel_depth,
            "RefractionStrength": refraction_strength,
            "FoamDistance": foam_distance,
            "FoamFeather": foam_feather,
        },
    )
    background = _scene_color(builder, refracted_uv)

    reflection = builder._expression(unreal.MaterialExpressionSkyLightEnvMapSample)
    reflection_direction = builder.custom(
        "WaterReflectionDirection",
        surface_body
        + """
        float3 UnityViewDirection = CMLToUnityDirection(normalize(CameraVector));
        float3 Reflected = reflect(-UnityViewDirection, Surface.RippleNormalWS);
        return CMLToUnrealDirection(Reflected);
        """,
        FLOAT3,
        {**surface_inputs, "CameraVector": builder.camera_vector()},
    )
    builder.connect(reflection_direction, "", reflection, "Direction")
    reflection_roughness = builder.custom(
        "WaterReflectionRoughness", "return 1.0f - saturate(Smoothness);", FLOAT1,
        {"Smoothness": smoothness},
    )
    builder.connect(reflection_roughness, "", reflection, "Roughness")

    emissive = builder.custom(
        "StylizedWater",
        pixel_surface_body
        + """
        float WaterDepthMetres = max(0.0f, (SceneDepth - PixelDepth)) * CML_UNITY_METRES_PER_UNREAL_UNIT;
        float3 UnityViewDirection = CMLToUnityDirection(normalize(CameraVector));
        float3 UnityLightDirection = CMLToUnityDirection(normalize(SunDirection));
        return CMLWaterCompose(
            Surface, UnityViewDirection, UnityLightDirection, SunColor, Ambient.rgb,
            EnvironmentReflection, RefractedScene, WaterDepthMetres, 1.0f,
            Shallow, Deep, Foam.rgb, DepthRange, FoamDistance, FoamFeather, FoamIntensity,
            CrestStrength, FresnelPower, FresnelStrength, ReflectionStrength, Smoothness,
            GlintPower, GlintStrength, AmbientStrength, TransmissionStrength, ColorBoost,
            Opacity, UnityUV);
        """,
        FLOAT3,
        {
            **pixel_surface_inputs,
            "SceneDepth": scene_depth,
            "PixelDepth": pixel_depth,
            "CameraVector": builder.camera_vector(),
            "SunDirection": sun_direction,
            "SunColor": sun_illuminance,
            "Ambient": ambient_color,
            "EnvironmentReflection": (reflection, ""),
            "RefractedScene": (background, "RGB"),
            "Shallow": shallow,
            "Deep": deep,
            "Foam": foam,
            "DepthRange": depth_range,
            "FoamDistance": foam_distance,
            "FoamFeather": foam_feather,
            "FoamIntensity": foam_intensity,
            "CrestStrength": crest_strength,
            "FresnelPower": fresnel_power,
            "FresnelStrength": fresnel_strength,
            "ReflectionStrength": reflection_strength,
            "Smoothness": smoothness,
            "GlintPower": glint_power,
            "GlintStrength": glint_strength,
            "AmbientStrength": ambient_strength,
            "TransmissionStrength": transmission,
            "ColorBoost": color_boost,
            "Opacity": opacity_param,
        },
    )

    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    builder.output(builder.constant(1.0), "", MP.MP_OPACITY)
    builder.output(wpo, "", MP.MP_WORLD_POSITION_OFFSET)
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material,
        "CML.LightingStylization",
        "Runtime authority -> _CMLSunDirectionWS,_CMLSunColor,_CMLAmbientColor; shadowAttenuation unavailable in unlit",
    )
    return builder.finalize()


def port_atmospheric_sky() -> PortResult:
    builder = _unlit_builder(
        "M_CML_Env_AtmosphericSky",
        "CML/Environment/Starter Island Atmospheric Sky",
        unreal.BlendMode.BLEND_OPAQUE,
    )
    # The runtime SkyLight uses Real Time Capture. Unreal deliberately ignores
    # an opaque unlit dome unless its material is tagged as a sky material;
    # without this flag the capture is black even though the dome is visible to
    # the gameplay camera. That removes essentially all indirect illumination
    # in Play Mode and produces the red "requires ... IsSky" warning.
    builder.material.set_editor_property("is_sky", True)
    builder.includes = builder.includes + ("/CML/CMLAtmosphericSky.ush",)

    sky_top = builder.vector("_SkyTopColorLinear", (0.08022, 0.59720, 0.61050, 1.0), group="Sky")
    horizon = builder.vector("_HorizonColorLinear", (0.168627, 1.0, 1.0, 1.0), group="Sky")
    day01 = builder.scalar("_Day01", 1.0, group="Phase")
    noon = builder.scalar("_NoonPhase", 1.0, group="Phase")
    dawn = builder.scalar("_DawnPhase", 0.0, group="Phase")
    early_dusk = builder.scalar("_EarlyDuskPhase", 0.0, group="Phase")
    late_dusk = builder.scalar("_LateDuskPhase", 0.0, group="Phase")
    cloud_amount = builder.scalar("_CloudAmount", 5.0, group="Clouds")
    cloud_top = builder.vector("_CloudTopColorLinear", (0.136719, 0.724438, 0.875, 1.0), group="Clouds")
    cloud_bottom = builder.vector("_CloudBottomColorLinear", (0.0, 0.568628, 1.0, 1.0), group="Clouds")
    cloud_color = builder.vector("_CloudColor", (0.784314, 0.854902, 0.843137, 1.0), group="Clouds")
    cloud_shadow = builder.vector("_CloudShadowColor", (0.521569, 0.654902, 0.698039, 1.0), group="Clouds")
    cloud_scale = builder.scalar("_CloudScale", 0.46, group="Clouds")
    cloud_coverage = builder.scalar("_CloudCoverage", 0.51, group="Clouds")
    cloud_softness = builder.scalar("_CloudSoftness", 0.065, group="Clouds")
    cloud_speed = builder.scalar("_CloudSpeed", 0.015, group="Clouds")
    cloud_opacity = builder.scalar("_CloudOpacity", 0.62, group="Clouds")
    rain = builder.scalar("_RainFade1Sunny0", 0.0, group="Weather")
    snow = builder.scalar("_SnowHailClouds", 0.0, group="Weather")
    sun_disc = builder.vector("_SunDiscColorLinear", (1.0, 0.596078, 0.0, 1.0), group="Sun")
    fog_inscattering = builder.vector(
        "_FogInscatteringColorLinear", (0.070588, 0.611765, 0.623529, 1.0), group="Fog"
    )
    fog_directional = builder.vector(
        "_FogDirectionalColorLinear", (0.102242, 0.351533, 0.473531, 1.0), group="Fog"
    )
    fog_density = builder.scalar("_FogDensity", 0.12, group="Fog")
    builder.scalar("_FogFalloff", 0.12, group="Fog")
    sun_direction = builder.vector("_SunDirectionWS", (0.42, 0.67, -0.61, 0.0), group="Sun")
    subsurface = builder.scalar("_SubsurfaceToUnlitScale", 0.1)
    exposure = builder.scalar("_Exposure", 0.98)

    emissive = builder.custom(
        "AtmosphericSky",
        """
        float3 UnityViewDirection = CMLToUnityDirection(-normalize(CameraVector));
        CMLSkyPhaseInputs Phase;
        Phase.Day01 = Day01;
        Phase.NoonPhase = NoonPhase;
        Phase.DawnPhase = DawnPhase;
        Phase.EarlyDuskPhase = EarlyDuskPhase;
        Phase.LateDuskPhase = LateDuskPhase;
        Phase.RainFade = RainFade;
        Phase.SnowHailClouds = SnowHailClouds;

        CMLSkyCloudInputs Cloud;
        Cloud.CloudAmount = CloudAmount;
        Cloud.CloudScale = CloudScale;
        Cloud.CloudCoverage = CloudCoverage;
        Cloud.CloudSoftness = CloudSoftness;
        Cloud.CloudSpeed = CloudSpeed;
        Cloud.CloudOpacity = CloudOpacity;
        Cloud.CloudColor = CloudColor.rgb;
        Cloud.CloudShadowColor = CloudShadowColor.rgb;
        Cloud.CloudTopColorLinear = CloudTop.rgb;
        Cloud.CloudBottomColorLinear = CloudBottom.rgb;

        return CMLAtmosphericSky(
            UnityViewDirection, SunDirection.xyz, Time,
            SkyTop.rgb, SunDisc.rgb, FogInscattering.rgb, FogDirectional.rgb,
            FogDensity, Subsurface, Exposure, Phase, Cloud);
        """,
        FLOAT3,
        {
            "CameraVector": builder.camera_vector(),
            "Time": builder.time(),
            "SkyTop": sky_top,
            "SunDisc": sun_disc,
            "SunDirection": sun_direction,
            "FogInscattering": fog_inscattering,
            "FogDirectional": fog_directional,
            "FogDensity": fog_density,
            "Subsurface": subsurface,
            "Exposure": exposure,
            "Day01": day01,
            "NoonPhase": noon,
            "DawnPhase": dawn,
            "EarlyDuskPhase": early_dusk,
            "LateDuskPhase": late_dusk,
            "RainFade": rain,
            "SnowHailClouds": snow,
            "CloudAmount": cloud_amount,
            "CloudScale": cloud_scale,
            "CloudCoverage": cloud_coverage,
            "CloudSoftness": cloud_softness,
            "CloudSpeed": cloud_speed,
            "CloudOpacity": cloud_opacity,
            "CloudColor": cloud_color,
            "CloudShadowColor": cloud_shadow,
            "CloudTop": cloud_top,
            "CloudBottom": cloud_bottom,
        },
    )
    builder.output(emissive, "", MP.MP_EMISSIVE_COLOR)
    # _HorizonColorLinear is declared by the Unity material but the decoded
    # algebra never reads it; it is exposed so the .mat value still transfers.
    unreal.EditorAssetLibrary.set_metadata_tag(
        builder.material, "CML.UnusedUnityProperty", "_HorizonColorLinear,_FogFalloff"
    )
    return builder.finalize()


PORTS = {
    "CML/Environment/Starter Island CloudTall Tree": port_cloud_tall_tree,
    "CML/Environment/Starter Island Stylized Surface": port_stylized_surface,
    "CML/Environment/Starter Island V4 Tree Leaves": port_v4_tree_leaves,
    "CML/Environment/Starter Island Foliage": port_foliage,
    "CML/Environment/Starter Island Underbody Terrain Rock": port_underbody_terrain_rock,
    "CML/Environment/Vertical Rock Auto Grass": port_vertical_rock_auto_grass,
    "CML/Clean Room/Measured Geometric Cloud": port_clean_room_cloud,
    "CML/Clean Room/Measured Grass Wind": port_clean_room_grass_wind,
    "CML/Clean Room/Measured Cliff": port_clean_room_cliff,
    "CML/Cinematics/Star Streak": port_cinematic_star_streak,
    "CML/Cinematics/Warp Tunnel": port_cinematic_warp_tunnel,
    "CML/Cinematics/Portal Veil": port_cinematic_portal_veil,
    "CML/Cinematics/Rift": port_cinematic_rift,
    "CML/Cinematics/Deep Space": port_cinematic_deep_space,
    "CML/Environment/Starter Island Ground Cover": port_ground_cover,
    "CML/Environment/Starter Island Terrain Reference Match": port_terrain_reference_match,
    "CML/Environment/Starter Island Ground Detail": port_ground_detail,
}


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    report_path = project_dir / "Migration" / "unity_shader_port_report.json"
    imported_defaults = ensure_default_textures()
    if imported_defaults:
        _log(f"Imported Unity default textures: {', '.join(imported_defaults)}")
    results: list[dict] = []
    failed = 0
    requested_filter = os.environ.get("CML_SHADER_PORT_FILTER", "").strip()
    requested_names = {
        item.strip() for item in requested_filter.split("|") if item.strip()
    }
    selected_ports = {
        shader_name: factory
        for shader_name, factory in PORTS.items()
        if not requested_names or shader_name in requested_names
    }
    if requested_filter and not selected_ports:
        raise RuntimeError(f"Unknown CML_SHADER_PORT_FILTER: {requested_filter}")
    for shader_name, factory in selected_ports.items():
        try:
            result = factory()
            results.append(
                {
                    "unityShader": result.unity_shader,
                    "master": result.master_object,
                    "parameters": result.parameters,
                    "status": "ported",
                }
            )
            _log(f"{shader_name} -> {result.master_object}")
        except Exception as exception:
            failed += 1
            results.append(
                {"unityShader": shader_name, "status": "failed", "error": str(exception)}
            )
            _error(f"{shader_name}: {exception}\n{traceback.format_exc()}")

    report = {
        "schema": 1,
        "requested": len(selected_ports),
        "ported": len(selected_ports) - failed,
        "failed": failed,
        "results": results,
    }
    temporary = report_path.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(report_path)
    # Never serialize the level currently open in the user's editor as a side
    # effect of rebuilding material assets. Each builder saves its own package;
    # this only flushes remaining content packages.
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(False, True)
    _log(f"Complete: ported={report['ported']}, failed={report['failed']}")
    return 0 if failed == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_SHADER_PORT_FAILED code={_exit_code}")
else:
    _log("CML_SHADER_PORT_SUCCEEDED")

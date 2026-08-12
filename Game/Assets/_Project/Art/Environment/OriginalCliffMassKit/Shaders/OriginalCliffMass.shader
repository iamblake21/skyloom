Shader "CML/Environment/Original Cliff Mass"
{
    Properties
    {
        [NoScaleOffset] _LandmassCliffAlbedo("Landmass Cliff Albedo", 2D) = "white" {}
        [NoScaleOffset] _LandmassCliffNormal("Landmass Cliff Normal", 2D) = "bump" {}
        [NoScaleOffset] _LandmassVariationMask("Landmass Grass Variation", 2D) = "gray" {}
        [NoScaleOffset] _LandmassGrassDetail("Terrain Grass Cap Detail", 2D) = "gray" {}

        _LandmassWorldSize("Cliff World Size", Float) = 30
        _LandmassVariationWorldSizeA("Grass Variation Size A", Float) = 35
        _LandmassVariationWorldSizeB("Grass Variation Size B", Float) = 40.96
        _LandmassNormalStrength("Cliff Normal Strength", Range(0, 2)) = 1.3
        _LandmassSlopeOffset("Normal Slope Offset", Range(-1, 0)) = -0.6
        _LandmassSlopeHardness("Normal Slope Hardness", Range(1, 24)) = 10
        _LandmassColorSlopeOffset("Colour Slope Offset", Range(-1, 0)) = -0.499157
        _LandmassColorSlopeHardness("Colour Slope Hardness", Range(1, 10)) = 2.953668
        _LandmassGrassAmount("Grass Amount", Range(0, 1)) = 1
        _LandmassRockTintColor("Rock Tint (Linear)", Vector) = (0.473531, 0.198069, 0.0865, 1)
        _LandmassRockTintStrength("Rock Tint Strength", Range(0, 1)) = 0.3
        _LandmassGrassColor1("Grass Color 1 (Linear)", Vector) = (0.03529412, 0.09019608, 0, 1)
        _LandmassGrassColor2("Grass Color 2 (Linear)", Vector) = (0.15294118, 0.18039216, 0.003921569, 1)
        _LandmassGrassDetailWorldSize("Grass Detail World Size", Float) = 4
        _LandmassGrassDetailStrength("Terrain Grass Detail Strength", Range(0, 1)) = 0.82
        _Smoothness("Smoothness", Range(0, 1)) = 0
        [HideInInspector] _LandmassIndirectScale("UE SkyLight Transfer", Range(1, 4)) = 3.2
        [HideInInspector] _CMLHitOffsetWS("Hit Offset", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #define _SPECULAR_SETUP 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/_Project/Art/Environment/StarterIsland/Shaders/Includes/StarterIslandLandmassSurface.hlsl"

            half _Smoothness;
            half _LandmassIndirectScale;
            float4 _CMLHitOffsetWS;

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float2 lightmapUV : TEXCOORD4;
                half3 vertexSH : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                positionInputs.positionWS += _CMLHitOffsetWS.xyz;
                positionInputs.positionCS =
                    TransformWorldToHClip(positionInputs.positionWS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                OUTPUT_LIGHTMAP_UV(
                    input.uv1,
                    unity_LightmapST,
                    output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 geometricNormalWS = NormalizeNormalPerPixel(input.normalWS);
                CMLLandmassSurface surface = CMLSampleLandmassSurface(
                    input.positionWS,
                    geometricNormalWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = surface.albedo;
                // Solarpunk's dry state is fully rough.  Zero specular removes
                // the white grazing rim without adding emission or flattening
                // the diffuse lighting response.
                surfaceData.specular = 0.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = 0.0h;
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = 1.0h;
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                half3 normalWS = NormalizeNormalPerPixel(surface.normalWS);
                half3 tangentReference =
                    abs(normalWS.y) > 0.999h
                        ? half3(1.0h, 0.0h, 0.0h)
                        : half3(0.0h, 1.0h, 0.0h);
                half3 tangentWS = normalize(cross(tangentReference, normalWS));
                half3 bitangentWS = normalize(cross(normalWS, tangentWS));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting =
                    VertexLighting(input.positionWS, normalWS);
                inputData.bakedGI =
                    SAMPLE_GI(
                        input.lightmapUV,
                        input.vertexSH,
                        normalWS);
                // UE's movable SkyLight is accumulated in its base pass at a
                // higher diffuse irradiance than URP's SH evaluation.  This
                // transfer restores that indirect term only; direct light,
                // albedo, roughness and specular remain source-authored.
                // Apply the transfer to exposed rock only.  The cap keeps the
                // same Terrain-grass response while shadow-facing cliff walls
                // receive the broad movable-SkyLight fill seen in the source.
                inputData.bakedGI *=
                    lerp(
                        _LandmassIndirectScale,
                        1.0h,
                        surface.topMask);
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                inputData.tangentToWorld =
                    half3x3(tangentWS, bitangentWS, normalWS);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}

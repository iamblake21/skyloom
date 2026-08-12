Shader "CML/Environment/Starter Island Terrain Splat"
{
    Properties
    {
        [HideInInspector] _Control("Control", 2D) = "red" {}
        [HideInInspector] _Splat0("Grass Sun", 2D) = "grey" {}
        [HideInInspector] _Splat1("Grass Deep", 2D) = "grey" {}
        [HideInInspector] _Splat2("Dirt", 2D) = "grey" {}
        [HideInInspector] _Splat3("Cliff", 2D) = "grey" {}
        [HideInInspector] _Normal0("Normal 0", 2D) = "bump" {}
        [HideInInspector] _Normal1("Normal 1", 2D) = "bump" {}
        [HideInInspector] _Normal2("Normal 2", 2D) = "bump" {}
        [HideInInspector] _Normal3("Normal 3", 2D) = "bump" {}
        [HideInInspector] _TerrainHolesTexture("Holes", 2D) = "white" {}
        [HideInInspector] _MainTex("Base Map", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Header(Solarpunk Landmass Surface)]
        _LandmassCliffAlbedo("Landmass Cliff Albedo", 2D) = "white" {}
        [Normal] _LandmassCliffNormal("Landmass Cliff Normal", 2D) = "bump" {}
        _LandmassVariationMask("Landmass Variation Mask", 2D) = "gray" {}
        _LandmassWorldSize("Landmass Texture World Size", Float) = 30
        _LandmassVariationWorldSizeA("Landmass Variation Size A", Float) = 35
        _LandmassVariationWorldSizeB("Landmass Variation Size B", Float) = 40.96
        _LandmassNormalStrength("Landmass Normal Strength", Range(0, 2)) = 1.3
        _LandmassSlopeOffset("Landmass Normal Slope Offset", Range(-1, 0)) = -0.6
        _LandmassSlopeHardness("Landmass Normal Slope Hardness", Range(1, 20)) = 10
        _LandmassColorSlopeOffset("Landmass Colour Slope Offset", Range(-1, 0)) = -0.499157
        _LandmassColorSlopeHardness("Landmass Colour Slope Hardness", Range(1, 10)) = 2.953668
        _LandmassGrassAmount("Landmass Grass Amount", Range(0, 1)) = 1
        _LandmassRockTintColor("Landmass Rock Tint (Linear)", Vector) = (0.473531, 0.198069, 0.0865, 1)
        _LandmassRockTintStrength("Landmass Rock Tint Strength", Range(0, 1)) = 0.3
        _LandmassGrassColor1("Landmass Grass 1 (Linear)", Vector) = (0.03529412, 0.09019608, 0, 1)
        _LandmassGrassColor2("Landmass Grass 2 (Linear)", Vector) = (0.15294118, 0.18039216, 0.003921569, 1)
        [HideInInspector] _LandmassIndirectScale("UE SkyLight Transfer", Range(1, 4)) = 3.2

        [HideInInspector] _ClayMode("QA Clay Mode", Range(0, 1)) = 0
        [HideInInspector] _ClayColor("QA Clay Color", Color) = (0.55, 0.48, 0.43, 1)
    }

    HLSLINCLUDE
    #pragma multi_compile_fragment __ _ALPHATEST_ON
    ENDHLSL

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-100"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "TerrainCompatible" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS

            #define _SPECULAR_SETUP 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/StarterIslandLandmassSurface.hlsl"

            half _ClayMode;
            half4 _ClayColor;
            half _LandmassIndirectScale;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 terrainUv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                float2 lightmapUV : TEXCOORD6;
                half3 vertexSH : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                TerrainInstancing(input.positionOS, input.normalOS, input.uv);
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS =
                    NormalizeNormalPerVertex(normalInputs.normalWS);
                output.terrainUv = input.uv;
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor =
                    ComputeFogFactor(positionInputs.positionCS.z);
                output.vertexLighting =
                    VertexLighting(positionInputs.positionWS, output.normalWS);
                OUTPUT_LIGHTMAP_UV(
                    input.uv,
                    unity_LightmapST,
                    output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half3 TangentNormalToWorld(
                half3 normalTS,
                half3 tangentWS,
                half3 bitangentWS,
                half3 geometricNormalWS)
            {
                return
                    normalize(
                        tangentWS * normalTS.x +
                        bitangentWS * normalTS.y +
                        geometricNormalWS * normalTS.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #ifdef _ALPHATEST_ON
                    ClipHoles(input.terrainUv);
                #endif

                half4 control =
                    SAMPLE_TEXTURE2D(
                        _Control,
                        sampler_Control,
                        input.terrainUv);
                half3 geometricNormalWS =
                    NormalizeNormalPerPixel(input.normalWS);
                half landmassTopMask =
                    CMLTerrainTopMask(geometricNormalWS);

                // Only authored grass weights may become automatic cliff.
                // The painted dirt/path channel remains untouched.
                half grassWeight = control.r + control.g;
                half cliffFromGrass = grassWeight * (1.0h - landmassTopMask);
                half grassScale =
                    (grassWeight - cliffFromGrass) /
                    max(grassWeight, 0.0001h);
                control.rg *= saturate(grassScale);
                control.a += cliffFromGrass;
                control /= max(dot(control, 1.0h), 0.0001h);

                float2 uv0 =
                    input.terrainUv * _Splat0_ST.xy + _Splat0_ST.zw;
                float2 uv1 =
                    input.terrainUv * _Splat1_ST.xy + _Splat1_ST.zw;
                float2 uv2 =
                    input.terrainUv * _Splat2_ST.xy + _Splat2_ST.zw;

                half3 tangentReference =
                    abs(geometricNormalWS.y) > 0.95h
                        ? half3(0.0h, 0.0h, 1.0h)
                        : half3(0.0h, 1.0h, 0.0h);
                half3 tangentWS =
                    normalize(cross(tangentReference, geometricNormalWS));
                half3 bitangentWS =
                    normalize(cross(geometricNormalWS, tangentWS));

                half3 grassSunNormalWS =
                    TangentNormalToWorld(
                        UnpackNormalScale(
                            SAMPLE_TEXTURE2D(
                                _Normal0,
                                sampler_Normal0,
                                uv0),
                            _NormalScale0),
                        tangentWS,
                        bitangentWS,
                        geometricNormalWS);
                half3 grassDeepNormalWS =
                    TangentNormalToWorld(
                        UnpackNormalScale(
                            SAMPLE_TEXTURE2D(
                                _Normal1,
                                sampler_Normal1,
                                uv1),
                            _NormalScale1),
                        tangentWS,
                        bitangentWS,
                        geometricNormalWS);
                half3 dirtNormalWS =
                    TangentNormalToWorld(
                        UnpackNormalScale(
                            SAMPLE_TEXTURE2D(
                                _Normal2,
                                sampler_Normal2,
                                uv2),
                            _NormalScale2),
                        tangentWS,
                        bitangentWS,
                        geometricNormalWS);

                // Preserve the authored Terrain grass layers.  The source
                // landmass cap is a two-colour macro fill, but replacing the
                // Terrain layers with that fill destroys all close-range grass
                // detail and exposes the low-frequency variation mask as large,
                // liquid-looking blobs.  Solarpunk data drives the cliff path;
                // painted Terrain grass remains the project's real texture.
                half3 grassSunAlbedo =
                    SAMPLE_TEXTURE2D(
                        _Splat0,
                        sampler_Splat0,
                        uv0).rgb *
                    _DiffuseRemapScale0.rgb;
                half3 grassDeepAlbedo =
                    SAMPLE_TEXTURE2D(
                        _Splat1,
                        sampler_Splat1,
                        uv1).rgb *
                    _DiffuseRemapScale1.rgb;
                half3 dirtAlbedo =
                    SAMPLE_TEXTURE2D(
                        _Splat2,
                        sampler_Splat2,
                        uv2).rgb *
                    _DiffuseRemapScale2.rgb;

                half3 cliffAlbedo = 0.0h;
                half3 cliffNormalWS = geometricNormalWS;
                UNITY_BRANCH
                if (control.a > 0.001h)
                {
                    // Terrain already performed the slope transition above;
                    // sampling the complete cap here would apply it twice.
                    CMLSampleLandmassRockSide(
                        input.positionWS,
                        geometricNormalWS,
                        cliffAlbedo,
                        cliffNormalWS);
                }

                half3 albedo =
                    grassSunAlbedo * control.r +
                    grassDeepAlbedo * control.g +
                    dirtAlbedo * control.b +
                    cliffAlbedo * control.a;
                half3 normalWS =
                    normalize(
                        grassSunNormalWS * control.r +
                        grassDeepNormalWS * control.g +
                        dirtNormalWS * control.b +
                        cliffNormalWS * control.a);
                albedo = lerp(albedo, _ClayColor.rgb, _ClayMode);
                normalWS =
                    normalize(
                        lerp(normalWS, geometricNormalWS, _ClayMode));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                // Source clear-weather terrain is non-metallic and fully rough.
                // Keeping imported layer smoothness here was the remaining
                // cause of the bright wet-looking outline around every slope.
                surfaceData.metallic = 0.0h;
                surfaceData.specular = 0.0h;
                surfaceData.smoothness = 0.0h;
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = 0.0h;
                surfaceData.alpha = 1.0h;
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI =
                    SAMPLE_GI(
                        input.lightmapUV,
                        input.vertexSH,
                        normalWS);
                // Match the same movable-SkyLight transfer used by modular
                // landmasses, without lifting painted grass or paths.
                inputData.bakedGI *=
                    lerp(
                        1.0h,
                        _LandmassIndirectScale,
                        saturate(control.a));
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask =
                    SAMPLE_SHADOWMASK(input.lightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return half4(color.rgb, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Terrain/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Terrain/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Terrain/Lit/DepthNormals"
    }

    Dependency "AddPassShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)"
    Dependency "BaseMapShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)"
    Dependency "BaseMapGenShader" = "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)"
    FallBack "Universal Render Pipeline/Terrain/Lit"
}

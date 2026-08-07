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
        _TerrainSizeXZ("Terrain Size XZ", Vector) = (660, 500, 0, 0)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.82
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.38
        _CliffTriplanarSharpness("Cliff Projection Sharpness", Range(1, 12)) = 5
        _CliffNormalStrength("Cliff Normal Strength", Range(0, 1)) = 0.82
        _CliffMacroVariation("Cliff Macro Variation", Range(0, 0.25)) = 0.12
        _CliffRunoffVariation("Cliff Runoff Variation", Range(0, 0.12)) = 0.045
        _CliffBrightness("Cliff Brightness", Range(0.8, 1.3)) = 1.08
        _CliffShadowColor("Cliff Shadow Color", Color) = (0.455, 0.243, 0.212, 1)
        _CliffBaseColor("Cliff Base Color", Color) = (0.725, 0.376, 0.263, 1)
        _CliffHighlightColor("Cliff Highlight Color", Color) = (0.855, 0.541, 0.373, 1)
        _CliffPaletteStrength("Cliff Palette Strength", Range(0, 1)) = 0.82
        _CliffCavityColor("Cliff Cavity Color", Color) = (0.408, 0.227, 0.208, 1)
        _CliffCavityStrength("Cliff Cavity Strength", Range(0, 0.6)) = 0.32
        _CliffReliefNormalStrength("Cliff Broad Relief", Range(0, 6)) = 3.2
        _CliffMicroNormalStrength("Cliff Micro Relief", Range(0, 1)) = 0.1
        _CliffLightingContrast("Cliff Lighting Contrast", Range(0, 1)) = 0.72
        _CliffAmbientReduction("Cliff Ambient Reduction", Range(0, 0.5)) = 0.14
        _CliffStrataColor("Cliff Strata Color", Color) = (0.49, 0.35, 0.36, 1)
        _CliffStrataScale("Cliff Strata Scale", Range(0.05, 1.5)) = 0.34
        _CliffStrataStrength("Cliff Strata Strength", Range(0, 0.65)) = 0.28
        _CliffLichenColor("Cliff Lichen Color", Color) = (0.46, 0.52, 0.32, 1)
        _CliffLichenScale("Cliff Lichen Scale", Range(0.02, 1)) = 0.18
        _CliffLichenStrength("Cliff Lichen Strength", Range(0, 0.65)) = 0.30
        _CliffSoilColor("Cliff Soil Color", Color) = (0.34, 0.27, 0.22, 1)
        _CliffSoilStrength("Cliff Soil Strength", Range(0, 0.65)) = 0.26
        _CliffSpecularStrength("Cliff Specular Strength", Range(0, 0.2)) = 0.035
        _CliffNormalFadeStart("Cliff Normal Fade Start", Range(10, 300)) = 65
        _CliffNormalFadeEnd("Cliff Normal Fade End", Range(20, 600)) = 190
        _MacroVariation("Macro Variation", Range(0, 0.2)) = 0.035
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _TerrainSizeXZ;
            half _AmbientStrength;
            half _ShadowFloor;
            half _CliffTriplanarSharpness;
            half _CliffNormalStrength;
            half _CliffMacroVariation;
            half _CliffRunoffVariation;
            half _CliffBrightness;
            half4 _CliffShadowColor;
            half4 _CliffBaseColor;
            half4 _CliffHighlightColor;
            half _CliffPaletteStrength;
            half4 _CliffCavityColor;
            half _CliffCavityStrength;
            half _CliffReliefNormalStrength;
            half _CliffMicroNormalStrength;
            half _CliffLightingContrast;
            half _CliffAmbientReduction;
            half4 _CliffStrataColor;
            half _CliffStrataScale;
            half _CliffStrataStrength;
            half4 _CliffLichenColor;
            half _CliffLichenScale;
            half _CliffLichenStrength;
            half4 _CliffSoilColor;
            half _CliffSoilStrength;
            half _CliffSpecularStrength;
            half _CliffNormalFadeStart;
            half _CliffNormalFadeEnd;
            half _MacroVariation;
            half _ClayMode;
            half4 _ClayColor;

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 position)
            {
                position =
                    frac(position * float2(123.34, 456.21));
                position +=
                    dot(position, position + 45.32);
                return frac(position.x * position.y);
            }

            float ValueNoise2D(float2 position)
            {
                float2 cell = floor(position);
                float2 blend = frac(position);
                blend =
                    blend * blend * (3.0 - 2.0 * blend);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(
                    lerp(a, b, blend.x),
                    lerp(c, d, blend.x),
                    blend.y);
            }

            half3 ApplyCliffPalette(half3 source)
            {
                // The texture keeps the erosion detail; this ramp owns the
                // art direction and gives shaded pockets a warm red-violet
                // bias instead of the previous beige/grey response.
                half luminance =
                    dot(source, half3(0.2126h, 0.7152h, 0.0722h));
                half tone =
                    smoothstep(0.055h, 0.34h, luminance);
                half3 palette =
                    tone < 0.5h
                        ? lerp(
                            _CliffShadowColor.rgb,
                            _CliffBaseColor.rgb,
                            tone * 2.0h)
                        : lerp(
                            _CliffBaseColor.rgb,
                            _CliffHighlightColor.rgb,
                            (tone - 0.5h) * 2.0h);

                // Preserve only a restrained amount of source chroma so the
                // texture does not turn into brown noise at gameplay range.
                half3 sourceChroma =
                    source / max(luminance, 0.025h);
                half3 detailedPalette =
                    palette * lerp(half3(1.0h, 1.0h, 1.0h), sourceChroma, 0.10h);
                return lerp(source, detailedPalette, _CliffPaletteStrength);
            }

            half3 ApplyHeightNormal(
                float3 positionWS,
                half3 normalWS,
                half height,
                half strength)
            {
                // Convert the derivative of the metre-scale height signal
                // into a world-space surface gradient. This bends the light
                // over broad lobes without changing the Terrain collision or
                // producing high-frequency "orange peel" noise.
                float3 positionDx = ddx(positionWS);
                float3 positionDy = ddy(positionWS);
                half heightDx = ddx(height);
                half heightDy = ddy(height);
                float3 positionDyPerpendicular =
                    cross(positionDy, (float3)normalWS);
                float3 positionDxPerpendicular =
                    cross((float3)normalWS, positionDx);
                float determinant =
                    dot(positionDx, positionDyPerpendicular);
                float inverseDeterminant =
                    (determinant < 0.0 ? -1.0 : 1.0) /
                    max(abs(determinant), 0.000001);
                float3 surfaceGradient =
                    (positionDyPerpendicular * heightDx +
                     positionDxPerpendicular * heightDy) *
                    inverseDeterminant;
                return normalize(
                    (float3)normalWS -
                    surfaceGradient * strength);
            }

            void SampleCliffTriplanar(
                float3 positionWS,
                half3 geometricNormalWS,
                out half3 cliffAlbedo,
                out half3 cliffNormalWS)
            {
                float2 terrainSize =
                    max(_TerrainSizeXZ.xy, float2(1.0, 1.0));
                float2 inverseTile =
                    abs(_Splat3_ST.xy) / terrainSize;
                float tileScale =
                    max(0.001, (inverseTile.x + inverseTile.y) * 0.5);
                // Terrain walls are composed of alternating diagonals. Their
                // Y normal changes abruptly even when the authored heightfield
                // is visually smooth, so including the top projection exposes
                // every triangle as a V-shaped color seam. Cliff projection
                // therefore uses only the two vertical world axes.
                half2 horizontalNormal = geometricNormalWS.xz;
                half horizontalLength =
                    max(length(horizontalNormal), 0.0001h);
                half3 stableCliffNormalWS =
                    normalize(
                        half3(
                            horizontalNormal.x / horizontalLength,
                            0.18h,
                            horizontalNormal.y / horizontalLength));
                half3 weights =
                    pow(
                        max(
                            half3(
                                abs(stableCliffNormalWS.x),
                                0.0h,
                                abs(stableCliffNormalWS.z)),
                            half3(0.0001h, 0.0001h, 0.0001h)),
                        _CliffTriplanarSharpness);
                weights /= max(
                    weights.x + weights.y + weights.z,
                    0.0001h);
                half3 axisSign =
                    step(
                        half3(0.0h, 0.0h, 0.0h),
                        geometricNormalWS) *
                    2.0h -
                    1.0h;
                float2 uvX =
                    float2(
                        positionWS.z,
                        -positionWS.y * axisSign.x) *
                    tileScale;
                float2 uvY =
                    float2(
                        positionWS.x,
                        -positionWS.z * axisSign.y) *
                    tileScale;
                float2 uvZ =
                    float2(
                        positionWS.x,
                        positionWS.y * axisSign.z) *
                    tileScale;
                half3 alongX =
                    SAMPLE_TEXTURE2D(
                        _Splat3,
                        sampler_Splat3,
                        uvX).rgb;
                half3 alongY =
                    SAMPLE_TEXTURE2D(
                        _Splat3,
                        sampler_Splat3,
                        uvY).rgb;
                half3 alongZ =
                    SAMPLE_TEXTURE2D(
                        _Splat3,
                        sampler_Splat3,
                        uvZ).rgb;
                cliffAlbedo =
                    (alongX * weights.x +
                     alongY * weights.y +
                     alongZ * weights.z) *
                    _DiffuseRemapScale3.rgb;

                UNITY_BRANCH
                if (_CliffNormalStrength <= 0.0001h)
                {
                    cliffNormalWS = stableCliffNormalWS;
                    return;
                }

                half3 tangentX =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _Normal3,
                            sampler_Normal3,
                            uvX),
                        _NormalScale3);
                half3 tangentY =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _Normal3,
                            sampler_Normal3,
                            uvY),
                        _NormalScale3);
                half3 tangentZ =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _Normal3,
                            sampler_Normal3,
                            uvZ),
                        _NormalScale3);
                half3 normalX =
                    half3(
                        axisSign.x * tangentX.z,
                        -axisSign.x * tangentX.y,
                        tangentX.x);
                half3 normalY =
                    half3(
                        tangentY.x,
                        axisSign.y * tangentY.z,
                        -axisSign.y * tangentY.y);
                half3 normalZ =
                    half3(
                        tangentZ.x,
                        axisSign.z * tangentZ.y,
                        axisSign.z * tangentZ.z);
                cliffNormalWS =
                    normalize(
                        normalX * weights.x +
                        normalY * weights.y +
                        normalZ * weights.z);
            }

            half EvaluateDiffuseResponse(
                half nDotL,
                half cliffWeight)
            {
                half wrapped =
                    lerp(
                        _ShadowFloor,
                        1.0h,
                        smoothstep(0.05h, 0.88h, nDotL));
                half cliff =
                    lerp(
                        _ShadowFloor * 0.72h,
                        1.04h,
                        smoothstep(0.10h, 0.84h, nDotL));
                return lerp(
                    wrapped,
                    cliff,
                    cliffWeight * _CliffLightingContrast);
            }

            void AccumulateCliffLight(
                Light lightData,
                half3 normalWS,
                half3 viewDirectionWS,
                half cliffWeight,
                inout half3 diffuseLighting,
                inout half3 specularLighting)
            {
                half attenuation =
                    lightData.distanceAttenuation *
                    lightData.shadowAttenuation;
                half nDotL =
                    saturate(dot(normalWS, lightData.direction));
                diffuseLighting +=
                    lightData.color *
                    EvaluateDiffuseResponse(nDotL, cliffWeight) *
                    attenuation;

                half3 halfDirection =
                    SafeNormalize(
                        lightData.direction + viewDirectionWS);
                half rockSpecular =
                    pow(
                        saturate(dot(normalWS, halfDirection)),
                        26.0h) *
                    nDotL *
                    cliffWeight *
                    _CliffSpecularStrength;
                specularLighting +=
                    lightData.color * rockSpecular * attenuation;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                TerrainInstancing(
                    input.positionOS,
                    input.normalOS,
                    input.uv);
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
                return output;
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
                half broadEdgeNoise =
                    (half)ValueNoise2D(
                        input.positionWS.xz * 0.045 +
                        float2(17.3, 91.7));
                half mediumEdgeNoise =
                    (half)ValueNoise2D(
                        input.positionWS.xz * 0.13 +
                        float2(63.1, 7.4));
                half irregularEdge =
                    (broadEdgeNoise * 0.68h +
                     mediumEdgeNoise * 0.32h) *
                    2.0h -
                    1.0h;
                half slopeMetric =
                    1.0h -
                    saturate(geometricNormalWS.y);
                half geometricCliff =
                    smoothstep(
                        0.257h,
                        0.515h,
                        slopeMetric + irregularEdge * 0.064h);
                half finalCliff =
                    max(control.a, geometricCliff);
                half nonCliffWeight =
                    control.r + control.g + control.b;
                half3 nonCliffRatios =
                    control.rgb / max(nonCliffWeight, 0.0001h);
                control.rgb =
                    nonCliffRatios * (1.0h - finalCliff);
                control.a = finalCliff;
                float2 uv0 =
                    input.terrainUv * _Splat0_ST.xy +
                    _Splat0_ST.zw;
                float2 uv1 =
                    input.terrainUv * _Splat1_ST.xy +
                    _Splat1_ST.zw;
                float2 uv2 =
                    input.terrainUv * _Splat2_ST.xy +
                    _Splat2_ST.zw;
                half3 dirtNormalTS =
                    UnpackNormalScale(
                        SAMPLE_TEXTURE2D(
                            _Normal2,
                            sampler_Normal2,
                            uv2),
                        _NormalScale2);
                half3 tangentReference =
                    abs(geometricNormalWS.y) > 0.95h
                        ? half3(0.0h, 0.0h, 1.0h)
                        : half3(0.0h, 1.0h, 0.0h);
                half3 tangentWS =
                    normalize(
                        cross(
                            tangentReference,
                            geometricNormalWS));
                half3 bitangentWS =
                    normalize(
                        cross(
                            geometricNormalWS,
                            tangentWS));
                half3 dirtNormalWS =
                    normalize(
                        tangentWS * dirtNormalTS.x +
                        bitangentWS * dirtNormalTS.y +
                        geometricNormalWS * dirtNormalTS.z);
                half3 grassSun =
                    SAMPLE_TEXTURE2D(
                        _Splat0,
                        sampler_Splat0,
                        uv0).rgb *
                    _DiffuseRemapScale0.rgb;
                half3 grassDeep =
                    SAMPLE_TEXTURE2D(
                        _Splat1,
                        sampler_Splat1,
                        uv1).rgb *
                    _DiffuseRemapScale1.rgb;
                half3 dirt =
                    SAMPLE_TEXTURE2D(
                        _Splat2,
                        sampler_Splat2,
                        uv2).rgb *
                    _DiffuseRemapScale2.rgb;
                // The path is authored as peach sand, then held at a higher
                // value than the grass. This compensates only for the custom
                // terrain lighting; warmth for rocks and props still comes
                // from the scene light and grading, never their albedo.
                dirt *= half3(1.88h, 1.56h, 2.00h);
                // World-space compaction survives grazing-angle mipmapping,
                // where the authored texture alone otherwise reads flat.
                half broadCompaction =
                    sin(
                        input.positionWS.x * 0.41h +
                        input.positionWS.z * 0.23h) *
                    sin(
                        input.positionWS.x * 0.17h -
                        input.positionWS.z * 0.53h);
                half mediumCompaction =
                    sin(
                        input.positionWS.x * 1.37h -
                        input.positionWS.z * 0.79h) *
                    sin(
                        input.positionWS.x * 0.93h +
                        input.positionWS.z * 1.61h);
                half fineCompaction =
                    sin(
                        input.positionWS.x * 3.17h +
                        input.positionWS.z * 2.43h) *
                    sin(
                        input.positionWS.x * 2.21h -
                        input.positionWS.z * 3.71h);
                half wornPatch =
                    smoothstep(
                        0.18h,
                        0.72h,
                        broadCompaction * 0.62h +
                        mediumCompaction * 0.38h);
                dirt *=
                    1.0h +
                    broadCompaction * 0.135h +
                    mediumCompaction * 0.070h +
                    fineCompaction * 0.022h;
                dirt = lerp(
                    dirt,
                    dirt * half3(0.91h, 0.94h, 0.97h),
                    wornPatch * 0.34h);
                half3 cliff = half3(0.0h, 0.0h, 0.0h);
                half3 cliffNormalWS = geometricNormalWS;
                half3 shapedCliffNormalWS = geometricNormalWS;
                UNITY_BRANCH
                if (control.a > 0.001h)
                {
                SampleCliffTriplanar(
                    input.positionWS,
                    geometricNormalWS,
                    cliff,
                    cliffNormalWS);
                half horizontalNormalSum =
                    abs(geometricNormalWS.x) +
                    abs(geometricNormalWS.z);
                half wallFacesX =
                    abs(geometricNormalWS.x) /
                    max(horizontalNormalSum, 0.0001h);
                // Coordinate lungo la parete: X sui fronti nord/sud e Z sui
                // fronti est/ovest. La reference non ha chiazze tonde, ma
                // grandi spalle quasi verticali; per questo la quota cambia
                // lentamente lungo il muro e pochissimo in altezza.
                float wallTangent =
                    lerp(
                        input.positionWS.x,
                        input.positionWS.z,
                        wallFacesX);
                half cliffColumnPrimary =
                    sin(
                        wallTangent * 0.082h +
                        input.positionWS.y * 0.046h +
                        sin(wallTangent * 0.026h + 1.7h) * 0.82h);
                half cliffColumnSecondary =
                    sin(
                        wallTangent * 0.164h -
                        input.positionWS.y * 0.031h +
                        2.4h);
                half cliffColumnNoise =
                    (half)ValueNoise2D(
                        float2(
                            wallTangent * 0.050,
                            input.positionWS.y * 0.072) +
                        float2(11.7, 38.2)) *
                    2.0h - 1.0h;
                half cliffRunoff =
                    (half)ValueNoise2D(
                        float2(
                            wallTangent * 0.205,
                            input.positionWS.y * 0.094) +
                        float2(93.4, 21.8));
                half cliffMacro =
                    cliffColumnPrimary * 0.56h +
                    cliffColumnSecondary * 0.20h +
                    cliffColumnNoise * 0.24h;
                cliff *=
                    max(
                        0.72h,
                        1.0h +
                        cliffMacro * _CliffMacroVariation +
                        (cliffRunoff * 2.0h - 1.0h) *
                        _CliffRunoffVariation);
                cliff *= _CliffBrightness;
                cliff = ApplyCliffPalette(cliff);
                half cliffCavity =
                    smoothstep(
                        0.20h,
                        0.88h,
                        saturate(
                            0.46h -
                            cliffMacro * 0.82h +
                            (0.48h - cliffRunoff) * 0.22h));
                cliff =
                    lerp(
                        cliff,
                        _CliffCavityColor.rgb,
                        cliffCavity * _CliffCavityStrength);
                // Broad, broken sediment bands reinforce the physical shelves
                // in the heightfield. They are world-space and warped along the
                // wall, so they never become a repeated bitmap stripe.
                half strataWarp =
                    (half)ValueNoise2D(
                        float2(
                            wallTangent * 0.034,
                            input.positionWS.y * 0.021) +
                        float2(42.7, 13.9)) *
                    2.0h - 1.0h;
                half strataBreakup =
                    (half)ValueNoise2D(
                        float2(
                            wallTangent * 0.071,
                            input.positionWS.y * 0.057) +
                        float2(7.3, 81.4));
                half strataWave =
                    sin(
                        input.positionWS.y * _CliffStrataScale +
                        strataWarp * 1.35h +
                        sin(wallTangent * 0.031h) * 0.65h) *
                    0.5h + 0.5h;
                half strataMask =
                    smoothstep(0.57h, 0.87h, strataWave) *
                    smoothstep(0.18h, 0.72h, strataBreakup);
                cliff =
                    lerp(
                        cliff,
                        _CliffStrataColor.rgb *
                        (0.88h + strataBreakup * 0.20h),
                        strataMask * _CliffStrataStrength);

                // Soil and lichen only settle on upward-facing rock shelves.
                // Their independent masks provide a gravity-readable material
                // transition at the grass lip and inside intermediate ledges.
                half shelfFacing =
                    smoothstep(
                        0.10h,
                        0.72h,
                        saturate(geometricNormalWS.y));
                half depositNoise =
                    (half)ValueNoise2D(
                        input.positionWS.xz * _CliffLichenScale +
                        float2(29.4, 65.1));
                half soilMask =
                    shelfFacing *
                    smoothstep(0.30h, 0.66h, 1.0h - depositNoise) *
                    (0.50h + strataMask * 0.50h);
                half lichenMask =
                    shelfFacing *
                    smoothstep(0.50h, 0.78h, depositNoise) *
                    smoothstep(0.14h, 0.62h, strataBreakup);
                cliff =
                    lerp(
                        cliff,
                        _CliffSoilColor.rgb,
                        soilMask * _CliffSoilStrength);
                cliff =
                    lerp(
                        cliff,
                        _CliffLichenColor.rgb *
                        (0.88h + depositNoise * 0.18h),
                        lichenMask * _CliffLichenStrength);
                half cliffReliefHeight =
                    cliffMacro * 0.82h +
                    (cliffRunoff * 2.0h - 1.0h) * 0.12h +
                    (strataWave * 2.0h - 1.0h) * 0.06h;
                half cliffFineNoise =
                    (half)ValueNoise2D(
                        float2(
                            wallTangent * 0.74,
                            input.positionWS.y * 0.93) +
                        float2(68.4, 17.9));
                half cliffFinePits =
                    (half)ValueNoise2D(
                        float2(
                            wallTangent * 1.47,
                            input.positionWS.y * 1.26) +
                        float2(14.2, 92.6));
                cliff *=
                    1.0h +
                    (cliffFineNoise * 2.0h - 1.0h) * 0.045h *
                    _CliffMicroNormalStrength;
                cliff = lerp(
                    cliff,
                    _CliffCavityColor.rgb,
                    smoothstep(0.79h, 0.94h, cliffFinePits) * 0.075h *
                    _CliffMicroNormalStrength);
                half cliffDetailFade =
                    1.0h -
                    smoothstep(
                        _CliffNormalFadeStart,
                        max(
                            _CliffNormalFadeStart + 1.0h,
                            _CliffNormalFadeEnd),
                        distance(
                            input.positionWS,
                            _WorldSpaceCameraPos.xyz));
                shapedCliffNormalWS =
                    ApplyHeightNormal(
                        input.positionWS,
                        normalize(
                            lerp(
                                geometricNormalWS,
                                cliffNormalWS,
                                _CliffNormalStrength *
                                lerp(0.32h, 1.0h, cliffDetailFade))),
                        cliffReliefHeight,
                        _CliffReliefNormalStrength *
                        lerp(0.38h, 1.0h, cliffDetailFade));
                shapedCliffNormalWS =
                    ApplyHeightNormal(
                        input.positionWS,
                        shapedCliffNormalWS,
                        cliffFineNoise * 0.70h + cliffFinePits * 0.30h,
                        _CliffMicroNormalStrength * cliffDetailFade);
                }
                half3 normalWS =
                    normalize(
                        geometricNormalWS *
                        (control.r + control.g) +
                        dirtNormalWS * control.b +
                        shapedCliffNormalWS * control.a);
                half3 albedo =
                    grassSun * control.r +
                    grassDeep * control.g +
                    dirt * control.b +
                    cliff * control.a;
                albedo = lerp(albedo, _ClayColor.rgb, _ClayMode);
                normalWS =
                    normalize(
                        lerp(
                            normalWS,
                            geometricNormalWS,
                            _ClayMode));

                half macro =
                    sin(
                        input.positionWS.x * 0.047h +
                        input.positionWS.z * 0.031h) *
                    sin(
                        input.positionWS.x * 0.019h -
                        input.positionWS.z * 0.073h);
                // Seconda ottava a scala media, circa 35 e 54 metri: copre la
                // fascia di distanza in cui il mip-mapping ha gia' mediato la
                // grana della texture ma le chiazze fra i due verdi non bastano
                // ancora a rompere la campitura. La prima ottava, a 134 e 330
                // metri, resta per la variazione d'insieme.
                half macroMedium =
                    sin(
                        input.positionWS.x * 0.181h -
                        input.positionWS.z * 0.144h) *
                    sin(
                        input.positionWS.x * 0.117h +
                        input.positionWS.z * 0.203h);
                albedo *=
                    1.0h +
                    (macro * 0.62h + macroMedium * 0.38h) *
                    _MacroVariation *
                    (1.0h - control.a * 0.72h);

                Light mainLight = GetMainLight(input.shadowCoord);
                half3 ambient =
                    max(SampleSH(normalWS), 0.0h) *
                    _AmbientStrength *
                    (1.0h -
                     control.a * _CliffAmbientReduction);
                half3 direct = half3(0.0h, 0.0h, 0.0h);
                half3 specularLighting = half3(0.0h, 0.0h, 0.0h);
                half3 viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                AccumulateCliffLight(
                    mainLight,
                    normalWS,
                    viewDirectionWS,
                    control.a,
                    direct,
                    specularLighting);
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV =
                        GetNormalizedScreenSpaceUV(input.positionCS);
                    uint additionalLightCount =
                        GetAdditionalLightsCount();

                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0u;
                         lightIndex <
                         min(
                             URP_FP_DIRECTIONAL_LIGHTS_COUNT,
                             MAX_VISIBLE_LIGHTS);
                         ++lightIndex)
                    {
                        Light additionalLight =
                            GetAdditionalLight(
                                lightIndex,
                                input.positionWS);
                        AccumulateCliffLight(
                            additionalLight,
                            normalWS,
                            viewDirectionWS,
                            control.a,
                            direct,
                            specularLighting);
                    }
                    #endif

                    LIGHT_LOOP_BEGIN(additionalLightCount)
                        Light additionalLight =
                            GetAdditionalLight(
                                lightIndex,
                                input.positionWS);
                        AccumulateCliffLight(
                            additionalLight,
                            normalWS,
                            viewDirectionWS,
                            control.a,
                            direct,
                            specularLighting);
                    LIGHT_LOOP_END
                #endif
                half3 color =
                    albedo *
                    (ambient + direct) *
                    1.08h +
                    specularLighting;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
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

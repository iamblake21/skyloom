Shader "CML/Environment/Starter Island Terrain Reference Match"
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
        _DirectStrength("Direct Strength", Range(0, 2)) = 0.78
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.48
        _CliffSlopeStart("Cliff Slope Start", Range(0, 1)) = 0.24
        _CliffSlopeEnd("Cliff Slope End", Range(0, 1)) = 0.48
        _CliffProjectionSharpness("Cliff Projection Sharpness", Range(1, 12)) = 4
        _CliffBrightness("Cliff Brightness", Range(0.5, 1.5)) = 1
        _CliffTint("Cliff Tint", Color) = (1, 1, 1, 1)
        _LipColor("Grass Lip Color", Color) = (0.34, 0.36, 0.09, 1)
        _LipStrength("Grass Lip Strength", Range(0, 1)) = 0.18
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

            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _TerrainSizeXZ;
            half _AmbientStrength;
            half _DirectStrength;
            half _ShadowFloor;
            half _CliffSlopeStart;
            half _CliffSlopeEnd;
            half _CliffProjectionSharpness;
            half _CliffBrightness;
            half4 _CliffTint;
            half4 _LipColor;
            half _LipStrength;

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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise2D(float2 p)
            {
                float2 cell = floor(p);
                float2 blend = frac(p);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            half3 SampleCliff(float3 positionWS, half3 normalWS)
            {
                float2 terrainSize = max(_TerrainSizeXZ.xy, float2(1.0, 1.0));
                float2 inverseTile = abs(_Splat3_ST.xy) / terrainSize;
                float tileScale = max(0.001, (inverseTile.x + inverseTile.y) * 0.5);

                half2 horizontalNormal = normalWS.xz;
                half horizontalLength = max(length(horizontalNormal), 0.0001h);
                half3 stableNormal = normalize(half3(
                    horizontalNormal.x / horizontalLength,
                    0.08h,
                    horizontalNormal.y / horizontalLength));
                half2 weights = pow(max(abs(stableNormal.xz), half2(0.0001h, 0.0001h)),
                    _CliffProjectionSharpness);
                weights /= max(weights.x + weights.y, 0.0001h);

                half2 signs = step(half2(0.0h, 0.0h), normalWS.xz) * 2.0h - 1.0h;
                float2 uvX = float2(positionWS.z, -positionWS.y * signs.x) * tileScale;
                float2 uvZ = float2(positionWS.x, positionWS.y * signs.y) * tileScale;
                half3 alongX = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat3, uvX).rgb;
                half3 alongZ = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat3, uvZ).rgb;
                return (alongX * weights.x + alongZ * weights.y) *
                    _DiffuseRemapScale3.rgb * _CliffTint.rgb * _CliffBrightness;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                TerrainInstancing(input.positionOS, input.normalOS, input.uv);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.terrainUv = input.uv;
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #ifdef _ALPHATEST_ON
                    ClipHoles(input.terrainUv);
                #endif

                half4 control = SAMPLE_TEXTURE2D(_Control, sampler_Control, input.terrainUv);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);

                float2 uv0 = input.terrainUv * _Splat0_ST.xy + _Splat0_ST.zw;
                float2 uv1 = input.terrainUv * _Splat1_ST.xy + _Splat1_ST.zw;
                float2 uv2 = input.terrainUv * _Splat2_ST.xy + _Splat2_ST.zw;
                half3 grassSun = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, uv0).rgb * _DiffuseRemapScale0.rgb;
                half3 grassDeep = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, uv1).rgb * _DiffuseRemapScale1.rgb;
                half3 dirt = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat2, uv2).rgb * _DiffuseRemapScale2.rgb;

                half nonCliffWeight = max(control.r + control.g + control.b, 0.0001h);
                half3 nonCliff = (grassSun * control.r + grassDeep * control.g + dirt * control.b) /
                    nonCliffWeight;

                half edgeNoise = ((half)ValueNoise2D(input.positionWS.xz * 0.045 + float2(17.3, 91.7)) - 0.5h) * 0.035h;
                half slope = 1.0h - saturate(normalWS.y);
                half autoCliff = smoothstep(_CliffSlopeStart, _CliffSlopeEnd, slope + edgeNoise);
                half cliffWeight = max(control.a, autoCliff);
                half3 cliff = SampleCliff(input.positionWS, normalWS);
                half3 albedo = lerp(nonCliff, cliff, cliffWeight);

                half lipOuter = smoothstep(_CliffSlopeStart - 0.035h, _CliffSlopeStart + 0.018h, slope + edgeNoise);
                half lipInner = 1.0h - smoothstep(_CliffSlopeStart + 0.018h, _CliffSlopeStart + 0.095h, slope + edgeNoise);
                half lip = lipOuter * lipInner * (1.0h - control.a);
                albedo = lerp(albedo, _LipColor.rgb, lip * _LipStrength);

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL = dot(normalWS, mainLight.direction);
                half wrapped = lerp(_ShadowFloor, 1.0h, smoothstep(-0.16h, 0.82h, nDotL));
                half attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 ambient = max(SampleSH(normalWS), 0.0h) * _AmbientStrength;
                half3 direct = mainLight.color * wrapped * attenuation * _DirectStrength;
                half3 color = albedo * (ambient + direct);
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

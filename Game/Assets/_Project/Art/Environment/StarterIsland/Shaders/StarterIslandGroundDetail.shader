Shader "CML/Environment/Starter Island Ground Detail"
{
    Properties
    {
        [HideInInspector] _MainTex("Terrain Detail Texture", 2D) = "white" {}
        [NoScaleOffset] _BladeMask("Blade Silhouette", 2D) = "white" {}
        _Cutoff("Silhouette Cutoff", Range(0.05, 0.8)) = 0.38
        _FallbackColor("Fallback Ground Color", Color) = (0.29, 0.48, 0.20, 1)
        _RootBrightness("Root Brightness", Range(0.4, 1)) = 0.96
        _TipBrightness("Tip Brightness", Range(0.8, 1.3)) = 1.025
        _WindStrength("Wind Tip Travel", Range(0, 0.08)) = 0.022
        _WindSpeed("Wind Speed", Range(0, 4)) = 1.38
        _GustStrength("Gust Strength", Range(0, 1)) = 0.28
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.66
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.16
        _ShadowAttenuationFloor("Terrain Contact Shadow Floor", Range(0, 1)) = 0.08
        _TerrainMacroVariation("Terrain Macro Match", Range(0, 0.2)) = 0.13
        _FarMatchStart("Far Ground Match Start", Range(5, 80)) = 24
        _FarMatchEnd("Far Ground Match End", Range(10, 120)) = 54
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest+10"
            "DisableBatching" = "False"
        }

        Cull Off
        ZWrite On
        AlphaToMask On

        HLSLINCLUDE
        #pragma target 3.5
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _FallbackColor;
            half _Cutoff;
            half _RootBrightness;
            half _TipBrightness;
            half _WindStrength;
            half _WindSpeed;
            half _GustStrength;
            half _AmbientStrength;
            half _ShadowFloor;
            half _ShadowAttenuationFloor;
            half _TerrainMacroVariation;
            half _FarMatchStart;
            half _FarMatchEnd;
        CBUFFER_END

        TEXTURE2D(_BladeMask);
        SAMPLER(sampler_BladeMask);

        TEXTURE2D(_CMLTerrainBlendControl);
        SAMPLER(sampler_CMLTerrainBlendControl);
        TEXTURE2D(_CMLTerrainBlendLayer0);
        SAMPLER(sampler_CMLTerrainBlendLayer0);
        TEXTURE2D(_CMLTerrainBlendLayer1);
        SAMPLER(sampler_CMLTerrainBlendLayer1);
        TEXTURE2D(_CMLTerrainBlendLayer2);
        SAMPLER(sampler_CMLTerrainBlendLayer2);
        TEXTURE2D(_CMLTerrainBlendLayer3);
        SAMPLER(sampler_CMLTerrainBlendLayer3);

        float4 _CMLTerrainBlendOriginInvSize;
        float4 _CMLTerrainBlendLayer0_ST;
        float4 _CMLTerrainBlendLayer1_ST;
        float4 _CMLTerrainBlendLayer2_ST;
        float4 _CMLTerrainBlendLayer3_ST;
        half4 _CMLTerrainBlendLayer0_RemapMin;
        half4 _CMLTerrainBlendLayer1_RemapMin;
        half4 _CMLTerrainBlendLayer2_RemapMin;
        half4 _CMLTerrainBlendLayer3_RemapMin;
        half4 _CMLTerrainBlendLayer0_RemapScale;
        half4 _CMLTerrainBlendLayer1_RemapScale;
        half4 _CMLTerrainBlendLayer2_RemapScale;
        half4 _CMLTerrainBlendLayer3_RemapScale;
        half _CMLTerrainBlendEnabled;

        // QA capture can freeze two deterministic wind phases. It is zero in
        // production, where Unity's real time drives the same deformation.
        half _CMLGrassWindTimeOverrideEnabled;
        float _CMLGrassWindTimeOverride;

        struct GrassAttributes
        {
            float4 positionOS : POSITION;
            half3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float Hash21(float2 value)
        {
            value = frac(value * float2(123.34, 456.21));
            value += dot(value, value + 45.32);
            return frac(value.x * value.y);
        }

        float ResolveWindTime()
        {
            return _CMLGrassWindTimeOverrideEnabled > 0.5h
                ? _CMLGrassWindTimeOverride
                : _Time.y;
        }

        float3 DeformGrassVertex(
            float3 positionOS,
            half4 vertexColor,
            out float3 anchorWS)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            anchorWS = TransformObjectToWorld(
                float3(positionOS.x, 0.0, positionOS.z));

            half heightWeight = pow(
                smoothstep(0.12h, 1.0h, saturate(vertexColor.r)),
                1.6h);
            half flexibility = lerp(0.86h, 1.14h, vertexColor.b);
            float time = ResolveWindTime();
            float phase = vertexColor.g * 6.2831853 +
                Hash21(floor(anchorWS.xz * 3.17)) * 6.2831853;
            float2 windDirection = normalize(float2(0.82, 0.57));
            float2 crossDirection = float2(
                -windDirection.y,
                windDirection.x);

            half sharedWave = sin(
                time * _WindSpeed +
                dot(anchorWS.xz, float2(0.58, 0.41)) +
                phase * 0.30);
            half gust = sin(
                time * (_WindSpeed * 0.31) +
                dot(anchorWS.xz, float2(0.19, -0.14)) +
                phase * 0.12) * 0.5h + 0.5h;
            half flutter = sin(
                time * (_WindSpeed * 5.2) +
                phase * 1.73 +
                anchorWS.x * 1.91) * 0.0032h;
            half travel = _WindStrength *
                (sharedWave * lerp(0.78h, 1.16h, gust * _GustStrength));
            float2 displacement =
                windDirection * travel +
                crossDirection *
                    (sharedWave * 0.19h + gust * 0.12h) *
                    _WindStrength +
                windDirection * flutter;
            positionWS.xz +=
                displacement * heightWeight * flexibility;
            return positionWS;
        }

        half3 RemapLayer(
            half3 source,
            half4 remapMinimum,
            half4 remapScale)
        {
            return source * remapScale.rgb + remapMinimum.rgb;
        }

        half3 SampleTerrainAlbedo(float2 worldXZ)
        {
            if (_CMLTerrainBlendEnabled < 0.5h)
            {
                return _FallbackColor.rgb;
            }

            float2 localXZ =
                worldXZ - _CMLTerrainBlendOriginInvSize.xy;
            float2 terrainUv = localXZ *
                _CMLTerrainBlendOriginInvSize.zw;
            half4 control = SAMPLE_TEXTURE2D(
                _CMLTerrainBlendControl,
                sampler_CMLTerrainBlendControl,
                saturate(terrainUv));
            control /= max(
                control.r + control.g + control.b + control.a,
                0.0001h);

            half3 grassSun = RemapLayer(
                SAMPLE_TEXTURE2D(
                    _CMLTerrainBlendLayer0,
                    sampler_CMLTerrainBlendLayer0,
                    localXZ * _CMLTerrainBlendLayer0_ST.xy +
                        _CMLTerrainBlendLayer0_ST.zw).rgb,
                _CMLTerrainBlendLayer0_RemapMin,
                _CMLTerrainBlendLayer0_RemapScale);
            half3 grassDeep = RemapLayer(
                SAMPLE_TEXTURE2D(
                    _CMLTerrainBlendLayer1,
                    sampler_CMLTerrainBlendLayer1,
                    localXZ * _CMLTerrainBlendLayer1_ST.xy +
                        _CMLTerrainBlendLayer1_ST.zw).rgb,
                _CMLTerrainBlendLayer1_RemapMin,
                _CMLTerrainBlendLayer1_RemapScale);
            half3 dirt = RemapLayer(
                SAMPLE_TEXTURE2D(
                    _CMLTerrainBlendLayer2,
                    sampler_CMLTerrainBlendLayer2,
                    localXZ * _CMLTerrainBlendLayer2_ST.xy +
                        _CMLTerrainBlendLayer2_ST.zw).rgb,
                _CMLTerrainBlendLayer2_RemapMin,
                _CMLTerrainBlendLayer2_RemapScale);
            half3 cliff = RemapLayer(
                SAMPLE_TEXTURE2D(
                    _CMLTerrainBlendLayer3,
                    sampler_CMLTerrainBlendLayer3,
                    localXZ * _CMLTerrainBlendLayer3_ST.xy +
                        _CMLTerrainBlendLayer3_ST.zw).rgb,
                _CMLTerrainBlendLayer3_RemapMin,
                _CMLTerrainBlendLayer3_RemapScale);

            // These are the same path value and world-space compaction terms
            // used by StarterIslandTerrainSplat.shader. Blades crossing a
            // painted path therefore inherit the path texture, not a generic
            // beige tint.
            dirt *= half3(1.88h, 1.56h, 2.00h);
            half broadCompaction =
                sin(worldXZ.x * 0.41h + worldXZ.y * 0.23h) *
                sin(worldXZ.x * 0.17h - worldXZ.y * 0.53h);
            half mediumCompaction =
                sin(worldXZ.x * 1.37h - worldXZ.y * 0.79h) *
                sin(worldXZ.x * 0.93h + worldXZ.y * 1.61h);
            half fineCompaction =
                sin(worldXZ.x * 3.17h + worldXZ.y * 2.43h) *
                sin(worldXZ.x * 2.21h - worldXZ.y * 3.71h);
            half wornPatch = smoothstep(
                0.18h,
                0.72h,
                broadCompaction * 0.62h + mediumCompaction * 0.38h);
            dirt *= 1.0h +
                broadCompaction * 0.135h +
                mediumCompaction * 0.070h +
                fineCompaction * 0.022h;
            dirt = lerp(
                dirt,
                dirt * half3(0.91h, 0.94h, 0.97h),
                wornPatch * 0.34h);

            half3 albedo =
                grassSun * control.r +
                grassDeep * control.g +
                dirt * control.b +
                cliff * control.a;
            half macro =
                sin(worldXZ.x * 0.047h + worldXZ.y * 0.031h) *
                sin(worldXZ.x * 0.019h - worldXZ.y * 0.073h);
            half macroMedium =
                sin(worldXZ.x * 0.181h - worldXZ.y * 0.144h) *
                sin(worldXZ.x * 0.117h + worldXZ.y * 0.203h);
            albedo *= 1.0h +
                (macro * 0.62h + macroMedium * 0.38h) *
                _TerrainMacroVariation *
                (1.0h - control.a * 0.72h);
            return albedo;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 anchorWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half2 bladeData : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                float2 uv : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(GrassAttributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 anchorWS;
                float3 positionWS = DeformGrassVertex(
                    input.positionOS.xyz,
                    input.color,
                    anchorWS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.anchorWS = anchorWS;
                output.normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                output.bladeData = half2(
                    saturate(input.color.r),
                    input.color.b);
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(
                Varyings input,
                bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half bladeAlpha = SAMPLE_TEXTURE2D(
                    _BladeMask,
                    sampler_BladeMask,
                    input.uv).a;
                clip(bladeAlpha - _Cutoff);

                half3 terrainAlbedo = SampleTerrainAlbedo(input.anchorWS.xz);
                half heightWeight = smoothstep(
                    0.02h,
                    0.98h,
                    saturate(input.bladeData.x));
                half distanceFade = smoothstep(
                    _FarMatchStart,
                    max(_FarMatchStart + 0.01h, _FarMatchEnd),
                    distance(input.positionWS, _WorldSpaceCameraPos.xyz));
                half brightness = lerp(
                    _RootBrightness,
                    _TipBrightness,
                    heightWeight);
                brightness = lerp(brightness, 1.0h, distanceFade);
                half3 bladeColor = terrainAlbedo * brightness;
                bladeColor *= lerp(
                    half3(0.985h, 0.992h, 0.98h),
                    half3(1.008h, 1.018h, 0.995h),
                    heightWeight * (1.0h - distanceFade));
                bladeColor *= lerp(0.985h, 1.015h, input.bladeData.y);

                half3 faceNormal = NormalizeNormalPerPixel(input.normalWS);
                faceNormal *= isFrontFace ? 1.0h : -1.0h;
                half3 normalWS = normalize(lerp(
                    faceNormal,
                    half3(0.0h, 1.0h, 0.0h),
                    0.88h));
                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half wrapped = lerp(
                    _ShadowFloor,
                    1.0h,
                    smoothstep(0.05h, 0.88h, nDotL));
                half3 ambient = max(
                    SampleSH(half3(0.0h, 1.0h, 0.0h)),
                    0.0h) * _AmbientStrength;
                half3 direct = mainLight.color *
                    wrapped *
                    lerp(
                        _ShadowAttenuationFloor,
                        1.0h,
                        mainLight.shadowAttenuation);
                half3 color = bladeColor * (ambient + direct) * 1.08h;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(GrassAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 anchorWS;
                float3 positionWS = DeformGrassVertex(
                    input.positionOS.xyz,
                    input.color,
                    anchorWS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                half bladeAlpha = SAMPLE_TEXTURE2D(
                    _BladeMask,
                    sampler_BladeMask,
                    input.uv).a;
                clip(bladeAlpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}

Shader "CML/Environment/Starter Island Atmospheric Sky"
{
    Properties
    {
        _ZenithColor("Zenith Color", Color) = (0.15, 0.52, 0.88, 1)
        _HorizonColor("Horizon Color", Color) = (0.62, 0.88, 0.96, 1)
        _LowerColor("Lower Hemisphere Color", Color) = (0.72, 0.82, 0.72, 1)
        _CloudColor("Cloud Light Color", Color) = (1.00, 0.98, 0.90, 1)
        _CloudShadowColor("Cloud Shadow Color", Color) = (0.63, 0.77, 0.80, 1)
        _SunColor("Sun Color", Color) = (1.00, 0.83, 0.55, 1)
        _SunDirection("Sun Direction", Vector) = (0.42, 0.67, -0.61, 0)
        _SunSize("Sun Angular Size", Range(0.0005, 0.03)) = 0.0045
        _CloudScale("Cloud Scale", Range(0.05, 2)) = 0.34
        _CloudCoverage("Cloud Coverage", Range(0, 1)) = 0.54
        _CloudSoftness("Cloud Softness", Range(0.01, 0.35)) = 0.11
        _CloudSpeed("Cloud Speed", Range(0, 0.2)) = 0.015
        _CloudOpacity("Cloud Opacity", Range(0, 1)) = 0.88
        _Exposure("Exposure", Range(0, 3)) = 1.05
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "AtmosphericSky"
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _LowerColor;
                half4 _CloudColor;
                half4 _CloudShadowColor;
                half4 _SunColor;
                float4 _SunDirection;
                half _SunSize;
                half _CloudScale;
                half _CloudCoverage;
                half _CloudSoftness;
                half _CloudSpeed;
                half _CloudOpacity;
                half _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                local = local * local * (3.0 - 2.0 * local);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(
                    lerp(a, b, local.x),
                    lerp(c, d, local.x),
                    local.y);
            }

            float CloudNoise(float2 uv)
            {
                float value = 0.0;
                float amplitude = 0.56;
                value += ValueNoise(uv) * amplitude;
                uv = uv * 2.03 + float2(13.7, -8.2);
                amplitude *= 0.52;
                value += ValueNoise(uv) * amplitude;
                uv = uv * 2.11 + float2(-4.1, 17.3);
                amplitude *= 0.50;
                value += ValueNoise(uv) * amplitude;
                uv = uv * 2.07 + float2(9.8, 5.4);
                amplitude *= 0.48;
                value += ValueNoise(uv) * amplitude;
                return value;
            }

            float NormalizedCloudNoise(float2 uv)
            {
                // CloudNoise's four octave weights add up to 1.016704.
                // Keeping the result in a predictable 0..1 range makes
                // _CloudCoverage behave consistently across the dome.
                return saturate(CloudNoise(uv) * 0.98357);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z =
                    UNITY_RAW_FAR_CLIP_VALUE *
                    output.positionCS.w;
                output.directionWS =
                    TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionWS);
                float up = saturate(direction.y);
                float lowerBlend = smoothstep(-0.12, 0.055, direction.y);
                float zenithBlend = pow(up, 0.46);
                half3 horizonToZenith = lerp(
                    _HorizonColor.rgb,
                    _ZenithColor.rgb,
                    zenithBlend);
                half3 sky = lerp(
                    _LowerColor.rgb,
                    horizonToZenith,
                    lowerBlend);

                float3 sunDirection = normalize(_SunDirection.xyz);
                float sunDot = saturate(dot(direction, sunDirection));
                float sunDisc = smoothstep(
                    1.0 - _SunSize,
                    1.0 - _SunSize * 0.12,
                    sunDot);
                float sunHalo =
                    pow(sunDot, 180.0) * 0.12 +
                    pow(sunDot, 34.0) * 0.025;
                float horizonBand =
                    1.0 - smoothstep(0.015, 0.30, abs(direction.y));
                float warmHorizon =
                    horizonBand *
                    (0.08 + pow(sunDot, 7.0) * 0.22);
                sky = lerp(
                    sky,
                    lerp(_HorizonColor.rgb, _SunColor.rgb, 0.36),
                    warmHorizon);

                // Project a cloud layer over the dome. The extra frequency
                // multiplier is intentional: at the material's default scale
                // it produces several readable cloud groups in a 60–70° FPS
                // view instead of one broad, almost uniform sheet.
                float domeHeight = max(direction.y + 0.26, 0.18);
                float2 cloudUv =
                    direction.xz / domeHeight *
                    (_CloudScale * 2.55);
                cloudUv +=
                    float2(_Time.y * _CloudSpeed,
                        -_Time.y * _CloudSpeed * 0.62);
                float broadCloud =
                    NormalizedCloudNoise(cloudUv * 0.82);
                float detailCloud =
                    NormalizedCloudNoise(
                        cloudUv * 2.73 +
                        float2(31.4, -12.8));
                float cloudField =
                    broadCloud * 0.79 +
                    detailCloud * 0.34 -
                    0.065;
                float coverageThreshold =
                    lerp(0.70, 0.40, _CloudCoverage);
                float edgeWidth =
                    max(0.025, _CloudSoftness * 0.72);
                float cloudMask = smoothstep(
                    coverageThreshold - edgeWidth,
                    coverageThreshold + edgeWidth,
                    cloudField);
                float cloudCore = smoothstep(
                    coverageThreshold + edgeWidth * 0.18,
                    coverageThreshold + edgeWidth * 1.65,
                    cloudField);
                float cloudHorizonFade =
                    smoothstep(0.018, 0.105, direction.y);
                float cloudZenithFade =
                    1.0 - smoothstep(0.90, 0.995, direction.y);
                cloudMask *=
                    cloudHorizonFade *
                    cloudZenithFade *
                    _CloudOpacity *
                    lerp(0.72, 1.0, detailCloud);

                half3 cloudColor = lerp(
                    _CloudShadowColor.rgb,
                    _CloudColor.rgb,
                    saturate(
                        0.18 +
                        cloudCore * 0.72 +
                        sunDot * 0.10));
                cloudColor +=
                    _SunColor.rgb *
                    pow(sunDot, 18.0) *
                    0.045;
                sky = lerp(sky, cloudColor, cloudMask);
                sky +=
                    _SunColor.rgb *
                    (sunDisc * (1.0 - cloudMask * 0.42) +
                        sunHalo);

                float dither =
                    (Hash21(input.positionCS.xy) - 0.5) / 255.0;
                return half4(
                    (sky + dither) * _Exposure,
                    1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

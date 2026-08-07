Shader "CML/Cinematics/Portal Veil"
{
    Properties
    {
        _InnerColor("Inner Color", Color) = (0.72, 0.94, 1, 1)
        _OuterColor("Outer Color", Color) = (0.20, 0.44, 0.96, 1)
        _RimColor("Rim Color", Color) = (0.94, 0.86, 0.55, 1)
        _Charge("Charge", Range(0, 1)) = 0
        _SwirlSpeed("Swirl Speed", Range(0, 6)) = 1.15
        _SwirlScale("Swirl Scale", Range(0.5, 12)) = 3.4
        _Refraction("Refraction", Range(0, 0.4)) = 0.075
        _RimWidth("Rim Width", Range(0.01, 0.6)) = 0.16
        _Intensity("Intensity", Range(0, 8)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PortalVeil"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor;
                half4 _OuterColor;
                half4 _RimColor;
                half _Charge;
                half _SwirlSpeed;
                half _SwirlScale;
                half _Refraction;
                half _RimWidth;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
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
                return lerp(
                    lerp(Hash21(cell), Hash21(cell + float2(1, 0)), local.x),
                    lerp(Hash21(cell + float2(0, 1)), Hash21(cell + float2(1, 1)), local.x),
                    local.y);
            }

            float Fbm(float2 uv)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    value += ValueNoise(uv) * amplitude;
                    uv = uv * 2.03 + float2(-4.7, 9.1);
                    amplitude *= 0.5;
                }

                return value * 1.0667;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float charge = saturate(_Charge);
                if (charge <= 0.001)
                {
                    return half4(0, 0, 0, 0);
                }

                float2 centered = (input.uv - 0.5) * 2.0;
                float radius = length(centered);
                if (radius > 1.0)
                {
                    return half4(0, 0, 0, 0);
                }

                float angle = atan2(centered.y, centered.x);
                float time = _Time.y;

                // Polar domain: the surface drains toward the middle instead of
                // scrolling flat, which is what makes an arch read as a gate.
                float2 polar = float2(
                    angle / 6.2831853 * _SwirlScale + radius * 1.9,
                    radius * _SwirlScale - time * _SwirlSpeed * 0.35);
                float body = Fbm(polar);
                float detail = Fbm(polar * 2.7 + float2(time * 0.21, -time * 0.13));
                float field = saturate(body * 0.75 + detail * 0.45 - 0.18);

                float rim = smoothstep(1.0, 1.0 - _RimWidth, radius);
                rim = 1.0 - rim;
                float disc = smoothstep(1.0, 0.92, radius);

                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                float2 offset = float2(
                    Fbm(polar * 1.6 + 11.3) - 0.5,
                    Fbm(polar * 1.6 - 7.9) - 0.5);
                float3 background = SampleSceneColor(
                    screenUV + offset * _Refraction * charge * 0.2);

                float3 color = lerp(_OuterColor.rgb, _InnerColor.rgb, field);
                color += _RimColor.rgb * rim * 2.1;
                color *= (0.35 + field * 1.5) * _Intensity * charge;

                float coverage = saturate((0.42 + field * 0.75) * disc * charge);
                float3 result = background * coverage * 0.55 + color * disc;
                return half4(max(result, 0.0), coverage);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

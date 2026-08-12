Shader "CML/Clean Room/Measured Grass Wind"
{
    Properties
    {
        _BottomColor("Bottom Color", Color) = (0.12, 0.25, 0.035, 1)
        _TopColor("Top Color", Color) = (0.48, 0.68, 0.10, 1)
        _DryColor("Dry Variation", Color) = (0.62, 0.58, 0.12, 1)
        _WindDirection("Wind Direction XZ", Vector) = (0, 0, -1, 0)
        _WindIntensity("Wind Intensity", Range(0, 10)) = 5
        _WindWeight("Wind Weight", Range(0, 1)) = 0.25
        _WindSpeed("Wind Speed", Range(0, 4)) = 1
        [HideInInspector] _UsePreviewTime("Use Preview Time", Float) = 0
        [HideInInspector] _PreviewTime("Preview Time", Float) = 0
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.32
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.92
        _DirectStrength("Direct Strength", Range(0, 2)) = 0.86
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="TransparentCutout"
            "Queue"="AlphaTest"
            "UniversalMaterialType"="Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BottomColor;
                half4 _TopColor;
                half4 _DryColor;
                float4 _WindDirection;
                half _WindIntensity;
                half _WindWeight;
                half _WindSpeed;
                half _UsePreviewTime;
                float _PreviewTime;
                half _AlphaCutoff;
                half _AmbientStrength;
                half _DirectStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half heightWeight : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float3 WindPositionWS(float3 positionOS, half weight)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                float2 direction = normalize(_WindDirection.xz + float2(0.0001, 0.0001));
                float windTime = lerp(_Time.y, _PreviewTime, saturate(_UsePreviewTime));
                float phase = dot(positionWS.xz, float2(0.071, 0.053)) + windTime * _WindSpeed;
                float gust = sin(phase) + sin(phase * 2.17 + 1.31) * 0.34;
                float amplitude = _WindWeight * _WindIntensity * 0.08 * weight;
                positionWS.xz += direction * gust * amplitude;
                positionWS.xz += float2(-direction.y, direction.x) *
                    sin(phase * 0.73 + 2.1) * amplitude * 0.22;
                return positionWS;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                half weight = saturate(input.color.r);
                float3 positionWS = WindPositionWS(input.positionOS.xyz, weight);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.heightWeight = weight;
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half edgeDistance = 1.0h - abs(input.uv.x * 2.0h - 1.0h);
                half serration = (half)Hash21(floor(input.uv * float2(12, 24)) + 7.0);
                half alpha = smoothstep(0.03h, 0.15h + serration * 0.025h, edgeDistance);
                clip(alpha - _AlphaCutoff);

                half dryVariation = (half)Hash21(floor(input.positionWS.xz * 1.7));
                half3 color = lerp(_BottomColor.rgb, _TopColor.rgb, input.heightWeight);
                color = lerp(color, _DryColor.rgb, saturate(dryVariation - 0.82h) * 0.34h);
                half faceSign = IS_FRONT_VFACE(face, 1.0h, -1.0h);
                half3 normalWS = normalize(input.normalWS) * faceSign;
                Light mainLight = GetMainLight(input.shadowCoord);
                half diffuse = saturate(dot(normalWS, mainLight.direction) * 0.55h + 0.45h);
                half attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 ambient = max(SampleSH(half3(0, 1, 0)), 0.0h) * _AmbientStrength;
                half3 direct = mainLight.color * diffuse * attenuation * _DirectStrength;
                color *= ambient + direct;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack Off
}

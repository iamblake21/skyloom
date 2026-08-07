Shader "CML/Effects/Impact Smoke Mesh"
{
    Properties
    {
        _BaseColor("Smoke Tint", Color) = (0.7, 0.7, 0.7, 0.7)
        _EdgeSoftness("Edge Softness", Range(0.08, 0.9)) = 0.44
        _NoiseScale("Density Breakup", Range(1, 20)) = 9
        _FogInfluence("Fog Influence", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+25"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ImpactSmoke"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgeSoftness;
                half _NoiseScale;
                half _FogInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half particleAlpha : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                output.particleAlpha = input.color.a;
                output.fogFactor = ComputeFogFactor(
                    positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirection =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                half facing = saturate(abs(dot(
                    normalWS,
                    viewDirection)));
                half edgeFade = smoothstep(
                    0.015h,
                    max(_EdgeSoftness, 0.03h),
                    facing);
                half densityNoise = lerp(
                    0.72h,
                    1.08h,
                    Hash31(input.positionWS * _NoiseScale));
                half alpha = saturate(
                    _BaseColor.a *
                    input.particleAlpha *
                    edgeFade *
                    densityNoise);
                clip(alpha - 0.004h);

                half upwardLight = lerp(
                    0.90h,
                    1.08h,
                    saturate(normalWS.y * 0.5h + 0.5h));
                // Particle RGB is intentionally ignored. Some mesh-particle
                // paths repack that stream and can contaminate the requested
                // material hue. Only particle alpha drives the smoke fade.
                half3 colour = _BaseColor.rgb * upwardLight;
                half3 foggedColour = MixFog(colour, input.fogFactor);
                colour = lerp(
                    colour,
                    foggedColour,
                    saturate(_FogInfluence));
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

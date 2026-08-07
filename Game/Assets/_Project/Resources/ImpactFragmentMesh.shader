Shader "CML/Effects/Impact Fragment Mesh"
{
    Properties
    {
        _BaseColor("Fragment Tint", Color) = (1, 1, 1, 1)
        _Smoothness("Surface Softness", Range(0, 1)) = 0.12
        _FogInfluence("Fog Influence", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ImpactFragments"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            Cull Off
            ZWrite On
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
                half _Smoothness;
                half _FogInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                output.fogFactor = ComputeFogFactor(
                    positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 keyDirection =
                    normalize(half3(0.32h, 0.86h, 0.39h));
                half facingLight = dot(normalWS, keyDirection) * 0.5h
                    + 0.5h;
                half lighting = lerp(0.78h, 1.08h, facingLight);
                // Hue is controlled only by the material. Particle-system
                // vertex RGB must never turn stone chips blue or purple.
                half3 colour = _BaseColor.rgb * lighting;
                half3 foggedColour = MixFog(colour, input.fogFactor);
                colour = lerp(
                    colour,
                    foggedColour,
                    saturate(_FogInfluence));
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

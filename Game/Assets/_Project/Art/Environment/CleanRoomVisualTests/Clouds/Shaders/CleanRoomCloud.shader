Shader "CML/Clean Room/Measured Geometric Cloud"
{
    Properties
    {
        _BottomColor("Cloud Bottom", Color) = (0.52, 0.68, 0.74, 1)
        _LayerColor("Cloud Layer", Color) = (0.76, 0.86, 0.87, 1)
        _TopColor("Cloud Top", Color) = (1.0, 0.97, 0.88, 1)
        _EdgeNoiseScale("Edge Noise Scale", Range(0.0001, 0.02)) = 0.0025
        _EdgeNoise("Edge Noise", Range(0, 0.5)) = 0.18
        _Cutoff("Cutoff", Range(-0.2, 0.5)) = 0.035
        _LightResponse("Light Response", Range(0, 1)) = 0.34
        _Opacity("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BottomColor;
                half4 _LayerColor;
                half4 _TopColor;
                half _EdgeNoiseScale;
                half _EdgeNoise;
                half _Cutoff;
                half _LightResponse;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = normalize(input.normalWS);
                half upward = saturate(normalWS.y * 0.5h + 0.5h);
                half3 cloud = upward < 0.5h
                    ? lerp(_BottomColor.rgb, _LayerColor.rgb, upward * 2.0h)
                    : lerp(_LayerColor.rgb, _TopColor.rgb, (upward - 0.5h) * 2.0h);
                Light mainLight = GetMainLight();
                half lightTerm = lerp(1.0h, saturate(dot(normalWS, mainLight.direction)) * 0.45h + 0.65h,
                    _LightResponse);
                cloud *= lerp(half3(1, 1, 1), mainLight.color, _LightResponse) * lightTerm;
                cloud = MixFog(cloud, input.fogFactor);
                return half4(cloud, _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

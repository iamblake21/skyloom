Shader "CML/Environment/Starter Island Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap("Palette Atlas", 2D) = "white" {}
        [HideInInspector] _MainTex("Terrain Detail Texture", 2D) = "white" {}
        _MaskMap("Surface Mask", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _WindStrength("Wind Strength", Range(0, 0.5)) = 0.12
        _WindSpeed("Wind Speed", Range(0, 4)) = 0.85
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.82
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.42
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MaskMap);
            SAMPLER(sampler_MaskMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _WindStrength;
                half _WindSpeed;
                half _AmbientStrength;
                half _ShadowFloor;
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
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS =
                    TransformObjectToWorld(input.positionOS.xyz);
                half windWeight =
                    saturate(input.color.r) * saturate(input.color.b);
                half phase = input.color.g * 6.2831853h;
                half sway =
                    sin(_Time.y * _WindSpeed
                        + phase
                        + positionWS.x * 0.075h
                        + positionWS.z * 0.051h);
                half crossSway =
                    sin(_Time.y * (_WindSpeed * 0.71h)
                        - phase * 0.63h
                        + positionWS.z * 0.093h);
                positionWS.x += sway * windWeight * _WindStrength;
                positionWS.z +=
                    crossSway * windWeight * _WindStrength * 0.58h;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(
                        TransformWorldToObject(positionWS));
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionWS;
                output.normalWS =
                    NormalizeNormalPerVertex(normalInputs.normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor =
                    ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half4 palette =
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 mask =
                    SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                half3 baseColor = palette.rgb * _BaseColor.rgb;

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half direct =
                    lerp(_ShadowFloor, 1.0h, smoothstep(0.04h, 0.90h, nDotL))
                    * mainLight.shadowAttenuation
                    * mainLight.distanceAttenuation;
                half3 ambient = max(SampleSH(normalWS), 0.0h);
                half roughnessLift = lerp(0.94h, 1.03h, mask.g);
                half3 color =
                    baseColor
                    * roughnessLift
                    * (ambient * _AmbientStrength
                       + mainLight.color * direct);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Lit"
}

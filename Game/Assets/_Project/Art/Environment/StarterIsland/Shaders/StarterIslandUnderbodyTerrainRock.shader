// Mesh-compatible counterpart of the active Terrain cliff treatment.
Shader "CML/Environment/Starter Island Underbody Terrain Rock"
{
    Properties
    {
        [NoScaleOffset] _RockMap("Terrain Cliff Texture", 2D) = "grey" {}
        _RockTileScale("World Tile Scale", Float) = 0.0454545
        _ProjectionSharpness("Triplanar Sharpness", Range(1, 12)) = 3.4
        _CliffBrightness("Cliff Brightness", Range(0.5, 1.5)) = 0.98
        _CliffTint("Cliff Tint", Color) = (1, 1, 1, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.66
        _DirectStrength("Direct Strength", Range(0, 2)) = 0.78
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.16
        [HideInInspector] _BaseMap("Compatibility Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Compatibility Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

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

            TEXTURE2D(_RockMap);
            SAMPLER(sampler_RockMap);

            CBUFFER_START(UnityPerMaterial)
                half _RockTileScale;
                half _ProjectionSharpness;
                half _CliffBrightness;
                half4 _CliffTint;
                half _AmbientStrength;
                half _DirectStrength;
                half _ShadowFloor;
                half4 _BaseColor;
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
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half3 SampleTerrainCliff(float3 positionWS, half3 normalWS)
            {
                half3 weights = pow(
                    max(abs(normalWS), half3(0.0001h, 0.0001h, 0.0001h)),
                    _ProjectionSharpness);
                weights /= max(weights.x + weights.y + weights.z, 0.0001h);

                half3 signs = step(half3(0.0h, 0.0h, 0.0h), normalWS) * 2.0h - 1.0h;
                float2 uvX = float2(positionWS.z, -positionWS.y * signs.x) * _RockTileScale;
                float2 uvY = float2(positionWS.x, -positionWS.z * signs.y) * _RockTileScale;
                float2 uvZ = float2(positionWS.x, positionWS.y * signs.z) * _RockTileScale;

                half3 alongX = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvX).rgb;
                half3 alongY = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvY).rgb;
                half3 alongZ = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvZ).rgb;
                return (alongX * weights.x + alongY * weights.y + alongZ * weights.z) *
                    _CliffTint.rgb * _CliffBrightness;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 albedo = SampleTerrainCliff(input.positionWS, normalWS);

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL = dot(normalWS, mainLight.direction);
                half wrapped = lerp(
                    _ShadowFloor,
                    1.0h,
                    smoothstep(-0.16h, 0.82h, nDotL));
                half attenuation = mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation;
                half3 ambient = max(SampleSH(normalWS), 0.0h) * _AmbientStrength;
                half3 direct = mainLight.color * wrapped * attenuation * _DirectStrength;
                half3 color = albedo * (ambient + direct);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}

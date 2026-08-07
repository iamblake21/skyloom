Shader "CML/Environment/Starter Island Ground Cover"
{
    Properties
    {
        [MainTexture] _BaseMap("Foliage Atlas", 2D) = "white" {}
        _MaskMap("Surface Mask", 2D) = "white" {}
        [MainColor] _BaseColor("Atlas Tint", Color) = (1, 1, 1, 1)
        _RootTint("Grass Root Tint", Color) = (0.43, 0.57, 0.24, 1)
        _TipTint("Grass Tip Tint", Color) = (0.71, 0.84, 0.39, 1)
        _WindStrength("Wind Strength", Range(0, 0.5)) = 0.22
        _WindSpeed("Wind Speed", Range(0, 4)) = 1.15
        _GustScale("Gust Scale", Range(0.005, 0.08)) = 0.02
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.74
        _ShadowFloor("Shadow Floor", Range(0, 1)) = 0.24
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MaskMap);
        SAMPLER(sampler_MaskMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _RootTint;
            half4 _TipTint;
            half _WindStrength;
            half _WindSpeed;
            half _GustScale;
            half _AmbientStrength;
            half _ShadowFloor;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            half3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        float3 ApplyGroundCoverWind(
            float3 positionOS,
            half4 vertexData)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            half heightWeight =
                pow(saturate(vertexData.r), 1.35h);
            half phase = vertexData.g * 6.2831853h;
            half gust =
                sin(
                    _Time.y * (_WindSpeed * 0.43h) +
                    positionWS.x * _GustScale +
                    positionWS.z * (_GustScale * 0.79h));
            gust = lerp(0.48h, 1.0h, gust * 0.5h + 0.5h);
            half primary =
                sin(
                    _Time.y * _WindSpeed +
                    phase +
                    positionWS.x * 0.081h +
                    positionWS.z * 0.057h);
            half secondary =
                sin(
                    _Time.y * (_WindSpeed * 1.61h) -
                    phase * 0.73h +
                    positionWS.z * 0.117h);
            half flowerStiffness = lerp(1.0h, 0.72h, vertexData.a);
            half bend =
                heightWeight *
                gust *
                flowerStiffness *
                _WindStrength;
            positionWS.x +=
                (primary * 0.86h + secondary * 0.14h) * bend;
            positionWS.z +=
                (primary * 0.34h - secondary * 0.22h) * bend;
            positionWS.y -= abs(primary) * bend * 0.055h;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ForwardVertex
            #pragma fragment ForwardFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 vertexData : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            Varyings ForwardVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS =
                    ApplyGroundCoverWind(
                        input.positionOS.xyz,
                        input.color);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS =
                    TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.vertexData = input.color;
                output.shadowCoord =
                    TransformWorldToShadowCoord(positionWS);
                output.fogFactor =
                    ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 ForwardFragment(
                Varyings input,
                bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half3 normalWS =
                    NormalizeNormalPerPixel(input.normalWS);
                normalWS *= isFrontFace ? 1.0h : -1.0h;
                half4 atlas =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv);
                half4 mask =
                    SAMPLE_TEXTURE2D(
                        _MaskMap,
                        sampler_MaskMap,
                        input.uv);
                half flowerFactor = saturate(input.vertexData.a);
                half heightWeight = saturate(input.vertexData.r);
                half3 grassTint =
                    lerp(
                        _RootTint.rgb,
                        _TipTint.rgb,
                        smoothstep(0.05h, 0.92h, heightWeight));
                half variation =
                    lerp(0.88h, 1.10h, input.vertexData.b);
                half3 grassColor =
                    grassTint * _BaseColor.rgb;
                half3 flowerColor =
                    atlas.rgb * _BaseColor.rgb;
                half3 baseColor =
                    lerp(
                        grassColor,
                        flowerColor,
                        flowerFactor) *
                    variation;

                Light mainLight = GetMainLight(input.shadowCoord);
                half nDotL =
                    saturate(dot(normalWS, mainLight.direction));
                half lightFacing =
                    lerp(
                        _ShadowFloor,
                        1.0h,
                        smoothstep(0.02h, 0.86h, nDotL));
                half shadow =
                    lerp(
                        0.38h,
                        1.0h,
                        mainLight.shadowAttenuation);
                half direct =
                    lightFacing *
                    shadow *
                    mainLight.distanceAttenuation;
                half3 ambient =
                    max(
                        max(
                            SampleSH(
                                lerp(
                                    normalWS,
                                    half3(0.0h, 1.0h, 0.0h),
                                    0.42h)),
                            0.0h),
                        half3(0.20h, 0.23h, 0.16h)) *
                    _AmbientStrength;
                half maskLift = lerp(0.96h, 1.04h, mask.g);
                half3 color =
                    baseColor *
                    maskLift *
                    (ambient + mainLight.color * direct);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVertex(Attributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                float3 positionWS =
                    ApplyGroundCoverWind(
                        input.positionOS.xyz,
                        input.color);
                float3 normalWS =
                    TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS =
                        normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS =
                    TransformWorldToHClip(
                        ApplyShadowBias(
                            positionWS,
                            normalWS,
                            lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z =
                        min(
                            positionCS.z,
                            UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z =
                        max(
                            positionCS.z,
                            UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVertex(Attributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                output.positionCS =
                    TransformWorldToHClip(
                        ApplyGroundCoverWind(
                            input.positionOS.xyz,
                            input.color));
                return output;
            }

            half4 DepthFragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}

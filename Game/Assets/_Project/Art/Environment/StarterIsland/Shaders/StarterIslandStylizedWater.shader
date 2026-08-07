Shader "CML/Environment/Starter Island Stylized Water"
{
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.34, 0.86, 0.86, 0.62)
        _DeepColor("Deep Color", Color) = (0.08, 0.45, 0.66, 0.80)
        _FoamColor("Shore Foam Color", Color) = (0.80, 1.00, 0.94, 1)
        _DepthRange("Shallow To Deep Distance", Range(0.1, 12)) = 4
        _FoamDistance("Shore Foam Distance", Range(0.01, 3)) = 0.65
        _FoamFeather("Shore Foam Feather", Range(0.01, 2)) = 0.38
        _WaveScaleA("Large Wave Scale", Range(0.01, 1)) = 0.11
        _WaveScaleB("Small Wave Scale", Range(0.01, 2)) = 0.27
        _WaveSpeedA("Large Wave Speed", Range(0, 4)) = 0.58
        _WaveSpeedB("Small Wave Speed", Range(0, 5)) = 1.16
        _WaveStrength("Wave Normal Strength", Range(0, 0.5)) = 0.14
        _DisplacementStrength("Surface Displacement", Range(0, 0.055)) = 0.04
        _FlowScale("Route Flow Scale", Range(0.1, 8)) = 1.8
        _FlowSpeed("Route Flow Speed", Range(0, 6)) = 1.35
        _CascadeFoamStrength("Cascade Foam Strength", Range(0, 2)) = 1.0
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3.2
        _GlintPower("Sun Glint Tightness", Range(8, 128)) = 72
        _GlintStrength("Sun Glint Strength", Range(0, 2)) = 0.48
        _RefractionStrength("Refraction Strength", Range(0, 0.08)) = 0.022
        _FresnelStrength("Fresnel Strength", Range(0, 1)) = 0.34
        _Smoothness("Surface Smoothness", Range(0, 1)) = 0.88
        _ReflectionStrength("Environment Reflection Strength", Range(0, 1.5)) = 0.62
        _NormalDetailScale("Fine Ripple Scale", Range(0.05, 4)) = 0.72
        _NormalDetailSpeed("Fine Ripple Speed", Range(0, 6)) = 1.72
        _NormalDetailStrength("Fine Ripple Strength", Range(0, 0.35)) = 0.075
        _CascadeNormalStrength("Cascade Ripple Strength", Range(0, 0.5)) = 0.19
        _ColorBoost("Color Boost", Range(0.5, 2)) = 1.08
        _Opacity("Opacity", Range(0, 1)) = 0.72
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half _DepthRange;
                half _FoamDistance;
                half _FoamFeather;
                half _WaveScaleA;
                half _WaveScaleB;
                half _WaveSpeedA;
                half _WaveSpeedB;
                half _WaveStrength;
                half _DisplacementStrength;
                half _FlowScale;
                half _FlowSpeed;
                half _CascadeFoamStrength;
                half _FresnelPower;
                half _GlintPower;
                half _GlintStrength;
                half _RefractionStrength;
                half _FresnelStrength;
                half _Smoothness;
                half _ReflectionStrength;
                half _NormalDetailScale;
                half _NormalDetailSpeed;
                half _NormalDetailStrength;
                half _CascadeNormalStrength;
                half _ColorBoost;
                half _Opacity;
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
                half fogFactor : TEXCOORD3;
                float4 screenPosition : TEXCOORD4;
                float viewDepth : TEXCOORD5;
                half4 color : TEXCOORD6;
                float4 shadowCoord : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                half3 geometricNormal =
                    NormalizeNormalPerVertex(normalInputs.normalWS);
                float3 positionWS =
                    TransformObjectToWorld(input.positionOS.xyz);
                float2 horizontalPosition = positionWS.xz;
                half displacementPhaseA =
                    dot(horizontalPosition, half2(0.82h, 0.57h)) *
                    _WaveScaleA +
                    _Time.y * _WaveSpeedA;
                half displacementPhaseB =
                    dot(horizontalPosition, half2(-0.42h, 0.91h)) *
                    _WaveScaleB -
                    _Time.y * _WaveSpeedB;
                half cascadePhase =
                    input.uv.y * _FlowScale * 6.2831853h -
                    _Time.y * _FlowSpeed +
                    sin(input.uv.x * 12.5663706h) * 0.28h;
                half horizontalSurface =
                    smoothstep(0.42h, 0.90h, abs(geometricNormal.y));
                half cascadeHint = saturate(input.color.b);
                half horizontalDisplacement =
                    (sin(displacementPhaseA) * 0.62h +
                     sin(displacementPhaseB) * 0.38h) *
                    _DisplacementStrength;
                half cascadeDisplacement =
                    (sin(cascadePhase) * 0.72h +
                     sin(cascadePhase * 1.83h + 1.7h) * 0.28h) *
                    _DisplacementStrength *
                    0.42h;
                half displacement =
                    lerp(
                        horizontalDisplacement *
                            lerp(0.42h, 1.0h, horizontalSurface),
                        cascadeDisplacement,
                        cascadeHint);
                positionWS += geometricNormal * displacement;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(
                        TransformWorldToObject(positionWS));
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = geometricNormal;
                output.uv = input.uv;
                output.fogFactor =
                    ComputeFogFactor(positionInputs.positionCS.z);
                output.screenPosition =
                    ComputeScreenPos(positionInputs.positionCS);
                output.viewDepth =
                    -TransformWorldToView(positionInputs.positionWS).z;
                output.color = input.color;
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 geometricNormal =
                    NormalizeNormalPerPixel(input.normalWS);
                half normalUp = abs(geometricNormal.y);
                half orientationCascade =
                    smoothstep(0.14h, 0.72h, 1.0h - normalUp);
                half cascadeHint = saturate(input.color.b);
                half cascadeMask =
                    saturate(max(cascadeHint, orientationCascade));
                half horizontalMask = 1.0h - cascadeMask;
                float2 worldWaterPosition = input.positionWS.xz;

                // The horizontal surface uses two independently travelling
                // world-space wave fields. A third, finer field prevents the
                // normals from reading as one large rolling sine wave.
                half phaseA =
                    dot(worldWaterPosition, half2(0.82h, 0.57h))
                    * _WaveScaleA +
                    _Time.y * _WaveSpeedA;
                half phaseB =
                    dot(worldWaterPosition, half2(-0.42h, 0.91h))
                    * _WaveScaleB -
                    _Time.y * _WaveSpeedB;
                half phaseC =
                    dot(worldWaterPosition, half2(0.31h, -0.95h))
                    * _NormalDetailScale +
                    _Time.y * _NormalDetailSpeed;
                half phaseD =
                    dot(worldWaterPosition, half2(-0.93h, -0.36h))
                    * (_NormalDetailScale * 1.37h) -
                    _Time.y * (_NormalDetailSpeed * 0.73h);
                half waveA = sin(phaseA);
                half waveB = sin(phaseB);

                // Waterfalls and steep creek sections use their ribbon UVs:
                // V advances down the route while U moves across its width.
                // This keeps their animation flowing downward instead of
                // sliding sideways with the world-space pool waves.
                half cascadeAlongPhase =
                    input.uv.y * _FlowScale * 6.2831853h -
                    _Time.y * _FlowSpeed +
                    sin(input.uv.x * 12.5663706h) * 0.34h;
                half cascadeAcrossPhase =
                    input.uv.x * 18.8495559h +
                    input.uv.y * 0.82h +
                    _Time.y * (_FlowSpeed * 0.24h);
                half cascadeDetailPhase =
                    input.uv.y * (_FlowScale * 10.681415h) -
                    _Time.y * (_FlowSpeed * 1.63h) -
                    input.uv.x * 31.4159265h;
                half crossFlow = sin(cascadeAcrossPhase);
                half broadFlow =
                    smoothstep(
                        0.10h,
                        0.88h,
                        sin(cascadeAlongPhase) * 0.55h +
                        crossFlow * 0.20h +
                        0.52h);
                half narrowFlow =
                    smoothstep(
                        0.76h,
                        0.96h,
                        sin(cascadeDetailPhase) * 0.54h +
                        crossFlow * 0.22h +
                        0.50h);
                half longitudinalStreak =
                    smoothstep(
                        0.70h,
                        0.88h,
                        0.50h +
                        sin(
                            input.uv.x * 29.0h +
                            input.uv.y * 1.3h +
                            sin(cascadeAlongPhase) * 0.7h) * 0.26h +
                        sin(
                            input.uv.x * 47.0h -
                            input.uv.y * 0.9h +
                            sin(cascadeDetailPhase)) * 0.15h +
                        sin(
                            input.uv.x * 13.0h +
                            input.uv.y * 2.2h) * 0.09h);
                half streakBreakup =
                    smoothstep(
                        0.18h,
                        0.82h,
                        0.5h +
                        0.5h *
                        sin(
                            input.uv.y * 7.5h -
                            _Time.y * _FlowSpeed * 1.2h +
                            sin(cascadeAcrossPhase)));
                half cascadeFoam =
                    cascadeMask *
                    longitudinalStreak *
                    lerp(0.36h, 1.0h, streakBreakup) *
                    lerp(0.72h, 1.0h, saturate(input.color.g)) *
                    min(_CascadeFoamStrength, 1.5h) *
                    0.46h;

                half3 tangentWS = normalize(
                    normalUp > 0.96h
                        ? half3(1.0h, 0.0h, 0.0h)
                        : cross(half3(0.0h, 1.0h, 0.0h),
                            geometricNormal));
                half3 bitangentWS =
                    normalize(cross(geometricNormal, tangentWS));

                // Analytic normal waves. The first pair gives each pool a
                // large moving surface; the second pair contributes fine,
                // faster ripples. Cascades swap to a UV-aligned normal field
                // with a visibly different downward cadence.
                half horizontalSlopeTangent =
                    (cos(phaseA) * 0.66h -
                     cos(phaseB) * 0.34h) *
                    _WaveStrength +
                    (cos(phaseC) * 0.58h -
                     cos(phaseD) * 0.42h) *
                    _NormalDetailStrength;
                half horizontalSlopeBitangent =
                    (cos(phaseB) * 0.64h +
                     cos(phaseA) * 0.36h) *
                    _WaveStrength +
                    (cos(phaseD) * 0.61h +
                     cos(phaseC) * 0.39h) *
                    _NormalDetailStrength;
                half cascadeSlopeTangent =
                    (cos(cascadeAcrossPhase) * 0.62h +
                     cos(cascadeDetailPhase) * 0.38h) *
                    _CascadeNormalStrength;
                half cascadeSlopeBitangent =
                    (cos(cascadeAlongPhase) * 0.67h +
                     cos(cascadeDetailPhase * 0.73h) * 0.33h) *
                    _CascadeNormalStrength;
                half slopeTangent =
                    lerp(
                        horizontalSlopeTangent,
                        cascadeSlopeTangent,
                        cascadeMask);
                half slopeBitangent =
                    lerp(
                        horizontalSlopeBitangent,
                        cascadeSlopeBitangent,
                        cascadeMask);
                half3 rippleNormal = normalize(
                    geometricNormal +
                    tangentWS * slopeTangent +
                    bitangentWS * slopeBitangent);

                float2 screenUv =
                    input.screenPosition.xy /
                    max(input.screenPosition.w, 0.0001);

                // Offset the opaque scene in view space so distortion follows
                // the camera correctly on both pools and waterfalls. The two
                // samples add a subtle chromatic separation without requiring
                // a normal texture.
                half3 rippleNormalVS =
                    TransformWorldToViewDir(rippleNormal, true);
                half3 geometricNormalVS =
                    TransformWorldToViewDir(geometricNormal, true);
                half2 refractionVector =
                    (rippleNormalVS - geometricNormalVS).xy;
                refractionVector +=
                    lerp(
                        half2(waveA, waveB),
                        half2(
                            sin(cascadeAcrossPhase),
                            sin(cascadeAlongPhase)),
                        cascadeMask) *
                    _WaveStrength *
                    0.10h;
                half2 refractionOffset =
                    refractionVector *
                    _RefractionStrength *
                    lerp(1.0h, 0.78h, cascadeMask);
                float2 refractedUv = clamp(
                    screenUv + refractionOffset,
                    float2(0.002, 0.002),
                    float2(0.998, 0.998));
                float2 refractedUvSecondary = clamp(
                    screenUv - refractionOffset * 0.38h,
                    float2(0.002, 0.002),
                    float2(0.998, 0.998));
                half3 refractedPrimary =
                    SampleSceneColor(refractedUv);
                half3 refractedSecondary =
                    SampleSceneColor(refractedUvSecondary);
                half3 refractedScene =
                    half3(
                        refractedPrimary.r,
                        (refractedPrimary.g +
                         refractedSecondary.g) * 0.5h,
                        refractedSecondary.b);

                float rawSceneDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth =
                    LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterDepth =
                    max(0.0, sceneEyeDepth - input.viewDepth);
                half depthBlend =
                    saturate(waterDepth / max(_DepthRange, 0.001h));
                half foamEdge =
                    1.0h - smoothstep(
                        _FoamDistance,
                        _FoamDistance + _FoamFeather,
                        waterDepth);
                half foamBreakup =
                    smoothstep(
                        0.38h,
                        0.70h,
                        0.50h +
                        waveA * 0.10h +
                        waveB * 0.07h +
                        sin(
                            dot(
                                worldWaterPosition,
                                half2(0.71h, -0.49h)) *
                            0.86h -
                            _Time.y * 0.34h) *
                        0.13h);

                half ribbonHint =
                    smoothstep(1.05h, 1.75h, abs(input.uv.y));
                half bankDistance =
                    abs(input.uv.x * 2.0h - 1.0h);
                half bankMask =
                    smoothstep(0.56h, 0.94h, bankDistance);
                half foamDomain =
                    lerp(1.0h, bankMask, ribbonHint);
                half shoreFoam =
                    foamEdge *
                    foamBreakup *
                    horizontalMask *
                    foamDomain;

                half3 viewDirection =
                    SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(
                    1.0h - saturate(dot(rippleNormal, viewDirection)),
                    _FresnelPower);
                half3 waterColor =
                    lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);

                half3 refractedTint =
                    lerp(
                        refractedScene,
                        refractedScene *
                            lerp(
                                _ShallowColor.rgb * 1.42h,
                                half3(0.92h, 1.02h, 1.06h),
                                depthBlend),
                        0.26h);
                half sceneLuminance =
                    dot(
                        abs(refractedScene),
                        half3(0.2126h, 0.7152h, 0.0722h));
                half sceneTextureAvailable =
                    smoothstep(0.001h, 0.025h, sceneLuminance);
                half refractionMix =
                    lerp(0.46h, 0.10h, depthBlend) *
                    lerp(1.0h, 0.74h, cascadeMask) *
                    sceneTextureAvailable;
                half3 color =
                    lerp(waterColor, refractedTint, refractionMix);
                color = lerp(
                    color,
                    _ShallowColor.rgb * 1.03h,
                    cascadeMask * 0.28h);

                half flowHighlight =
                    cascadeMask *
                    saturate(
                        broadFlow * 0.25h +
                        narrowFlow * 0.18h +
                        longitudinalStreak * 0.16h) *
                    0.38h;
                color = lerp(
                    color,
                    _ShallowColor.rgb * 1.12h,
                    flowHighlight);

                Light mainLight = GetMainLight(input.shadowCoord);
                half mainShadow =
                    mainLight.shadowAttenuation *
                    mainLight.distanceAttenuation;
                half nDotL =
                    saturate(dot(rippleNormal, mainLight.direction));
                half directLight =
                    nDotL * mainShadow;
                color *= lerp(0.70h, 1.04h, directLight);

                half3 reflectionDirection =
                    reflect(-viewDirection, rippleNormal);
                half perceptualRoughness =
                    1.0h - saturate(_Smoothness);
                half3 environmentReflection =
                    GlossyEnvironmentReflection(
                        reflectionDirection,
                        input.positionWS,
                        perceptualRoughness,
                        1.0h,
                        screenUv);
                half reflectionMask =
                    saturate(
                        (lerp(0.025h, 0.075h, _Smoothness) +
                         fresnel * _FresnelStrength) *
                        _ReflectionStrength *
                        lerp(1.0h, 0.76h, cascadeMask));
                color =
                    lerp(
                        color,
                        environmentReflection,
                        reflectionMask);

                half3 fresnelColor =
                    lerp(
                        _ShallowColor.rgb,
                        half3(0.64h, 0.90h, 0.94h),
                        0.55h);
                color = lerp(
                    color,
                    fresnelColor,
                    fresnel * _FresnelStrength * 0.16h);

                half surfaceRipple =
                    smoothstep(
                        0.74h,
                        0.94h,
                        0.50h +
                        sin(phaseA * 1.55h + phaseC * 0.42h) *
                            0.20h +
                        sin(phaseB * 1.18h - phaseD * 0.31h) *
                            0.16h);
                color = lerp(
                    color,
                    _FoamColor.rgb,
                    surfaceRipple *
                    horizontalMask *
                    lerp(0.10h, 0.035h, depthBlend));

                half foamMask =
                    saturate(shoreFoam + cascadeFoam);
                color = lerp(
                    color,
                    _FoamColor.rgb,
                    foamMask * 0.62h);

                half3 halfVector =
                    SafeNormalize(mainLight.direction + viewDirection);
                half glint = pow(
                    saturate(dot(rippleNormal, halfVector)),
                    max(8.0h, _GlintPower));
                color +=
                    lerp(mainLight.color, _FoamColor.rgb, 0.30h) *
                    glint *
                    _GlintStrength *
                    mainShadow *
                    lerp(0.42h, 1.0h, _Smoothness);
                color *= _ColorBoost;
                color = MixFog(color, input.fogFactor);

                half bodyAlpha =
                    lerp(0.48h, 0.82h, depthBlend) *
                    saturate(_Opacity) +
                    cascadeMask * 0.13h;
                half alpha =
                    saturate(
                        bodyAlpha +
                        shoreFoam * 0.24h +
                        cascadeFoam * 0.18h +
                        reflectionMask * 0.10h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

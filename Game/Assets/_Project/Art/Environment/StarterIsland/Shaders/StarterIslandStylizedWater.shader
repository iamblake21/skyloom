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
        _AmbientStrength("Ambient Response", Range(0, 2)) = 1.0
        _TransmissionStrength("Water Transmission", Range(0, 1)) = 0.76
        _CrestStrength("Pool Crest Strength", Range(0, 1)) = 0.28
        _FoamIntensity("Foam Intensity", Range(0, 2)) = 1.0
        _ColorBoost("Color Boost", Range(0.5, 2)) = 1.08
        _Opacity("Opacity", Range(0, 1)) = 0.72
        [HideInInspector] _SrcBlend("", Float) = 1
        [HideInInspector] _DstBlend("", Float) = 0
        [HideInInspector] _ZWrite("", Float) = 1
        [HideInInspector] _Cull("", Float) = 2
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
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]

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
                half _AmbientStrength;
                half _TransmissionStrength;
                half _CrestStrength;
                half _FoamIntensity;
                half _ColorBoost;
                half _Opacity;
            CBUFFER_END

            // A smooth, periodic wave with an analytic slope and no
            // trigonometric instructions. Phases are expressed in cycles.
            void EvaluateWave(
                float phase,
                out half height,
                out half slope)
            {
                float cycle = frac(phase);
                half triangleWave =
                    1.0h - abs(cycle * 2.0h - 1.0h);
                half smoothTriangle =
                    triangleWave * triangleWave *
                    (3.0h - 2.0h * triangleWave);
                height = smoothTriangle * 2.0h - 1.0h;
                half direction = cycle < 0.5h ? 1.0h : -1.0h;
                slope =
                    direction *
                    (4.0h * triangleWave * (1.0h - triangleWave));
            }

            // Pools encode radial distance in vertex red and radial UVs.
            // Route meshes deliberately break that relationship; their V
            // coordinate also keeps increasing with travelled distance.
            void EvaluateWaterMasks(
                float2 uv,
                half4 vertexColor,
                half normalUp,
                out half poolMask,
                out half routeMask,
                out half cascadeMask)
            {
                half orientationCascade =
                    smoothstep(0.14h, 0.72h, 1.0h - normalUp);
                cascadeMask = saturate(
                    max(saturate(vertexColor.b), orientationCascade));

                half poolUvRadius =
                    length((half2)uv - half2(0.5h, 0.5h)) * 2.0h;
                half radialError =
                    abs(poolUvRadius - saturate(vertexColor.r));
                half poolSignature =
                    1.0h - smoothstep(0.10h, 0.24h, radialError);
                half routeUvHint =
                    smoothstep(1.02h, 1.42h, abs((half)uv.y));
                routeMask = saturate(
                    max(
                        cascadeMask,
                        max(1.0h - poolSignature, routeUvHint)));
                poolMask = 1.0h - routeMask;
            }

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
                float viewDepth : TEXCOORD4;
                half4 color : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
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
                float displacementPhaseA =
                    dot(horizontalPosition, half2(0.82h, 0.57h)) *
                    _WaveScaleA * 0.15915494 +
                    _Time.y * _WaveSpeedA * 0.15915494;
                float displacementPhaseB =
                    dot(horizontalPosition, half2(-0.42h, 0.91h)) *
                    _WaveScaleB * 0.15915494 -
                    _Time.y * _WaveSpeedB * 0.15915494;

                half waveA;
                half slopeA;
                half waveB;
                half slopeB;
                EvaluateWave(displacementPhaseA, waveA, slopeA);
                EvaluateWave(displacementPhaseB, waveB, slopeB);

                half lateralFlow;
                half lateralSlope;
                EvaluateWave(input.uv.x * 2.0, lateralFlow, lateralSlope);
                float routePhase =
                    input.uv.y * _FlowScale -
                    _Time.y * _FlowSpeed * 0.15915494 +
                    lateralFlow * 0.045h;
                half routeWave;
                half routeSlope;
                half routeDetailWave;
                half routeDetailSlope;
                EvaluateWave(routePhase, routeWave, routeSlope);
                EvaluateWave(
                    routePhase * 1.83 + 0.2706,
                    routeDetailWave,
                    routeDetailSlope);

                half horizontalSurface =
                    smoothstep(0.42h, 0.90h, abs(geometricNormal.y));
                half poolMask;
                half routeMask;
                half cascadeMask;
                EvaluateWaterMasks(
                    input.uv,
                    input.color,
                    abs(geometricNormal.y),
                    poolMask,
                    routeMask,
                    cascadeMask);
                half horizontalDisplacement =
                    (waveA * 0.62h + waveB * 0.38h) *
                    _DisplacementStrength;
                half routeDisplacement =
                    (routeWave * 0.72h +
                     routeDetailWave * 0.28h) *
                    _DisplacementStrength *
                    lerp(0.70h, 0.42h, cascadeMask);
                half displacement =
                    lerp(
                        horizontalDisplacement *
                            lerp(0.42h, 1.0h, horizontalSurface),
                        routeDisplacement,
                        routeMask);
                positionWS += geometricNormal * displacement;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = geometricNormal;
                output.uv = input.uv;
                output.fogFactor =
                    ComputeFogFactor(output.positionCS.z);
                output.viewDepth =
                    -TransformWorldToView(positionWS).z;
                output.color = input.color;
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 geometricNormal =
                    NormalizeNormalPerPixel(input.normalWS);
                half normalUp = abs(geometricNormal.y);
                half poolMask;
                half routeMask;
                half cascadeMask;
                EvaluateWaterMasks(
                    input.uv,
                    input.color,
                    normalUp,
                    poolMask,
                    routeMask,
                    cascadeMask);
                half horizontalSurfaceMask = 1.0h - cascadeMask;
                float2 worldWaterPosition = input.positionWS.xz;

                // Pools use compact world-space waves. Converting the legacy
                // radian scales to cycles preserves authored material speed.
                float phaseA =
                    dot(worldWaterPosition, half2(0.82h, 0.57h))
                    * _WaveScaleA * 0.15915494 +
                    _Time.y * _WaveSpeedA * 0.15915494;
                float phaseB =
                    dot(worldWaterPosition, half2(-0.42h, 0.91h))
                    * _WaveScaleB * 0.15915494 -
                    _Time.y * _WaveSpeedB * 0.15915494;
                float detailPhase =
                    dot(worldWaterPosition, half2(0.31h, -0.95h))
                    * _NormalDetailScale * 0.15915494 +
                    _Time.y * _NormalDetailSpeed * 0.15915494;
                half waveA;
                half slopeA;
                half waveB;
                half slopeB;
                half detailWave;
                half detailSlope;
                EvaluateWave(phaseA, waveA, slopeA);
                EvaluateWave(phaseB, waveB, slopeB);
                EvaluateWave(detailPhase, detailWave, detailSlope);

                // Route V is authored from travelled distance, so these
                // waves move along creeks and down waterfalls without world-
                // space sliding. Vertex blue still forces waterfall mode.
                float flowAcrossPhase =
                    input.uv.x * 3.0 +
                    input.uv.y * 0.1305 +
                    _Time.y * _FlowSpeed * 0.0382;
                half flowAcross;
                half flowAcrossSlope;
                EvaluateWave(
                    flowAcrossPhase,
                    flowAcross,
                    flowAcrossSlope);
                float flowAlongPhase =
                    input.uv.y * _FlowScale -
                    _Time.y * _FlowSpeed * 0.15915494 +
                    flowAcross * 0.054h;
                float flowDetailPhase =
                    input.uv.y * (_FlowScale * 1.70h) -
                    _Time.y * (_FlowSpeed * 0.2594h) -
                    input.uv.x * 5.0h;
                half flowAlong;
                half flowAlongSlope;
                half flowDetail;
                half flowDetailSlope;
                EvaluateWave(
                    flowAlongPhase,
                    flowAlong,
                    flowAlongSlope);
                EvaluateWave(
                    flowDetailPhase,
                    flowDetail,
                    flowDetailSlope);

                half broadFlow =
                    smoothstep(
                        -0.48h,
                        0.48h,
                        flowAlong * 0.70h +
                        flowAcross * 0.30h);
                half narrowFlow =
                    smoothstep(
                        0.28h,
                        0.72h,
                        flowDetail * 0.72h +
                        flowAcross * 0.28h);
                half longitudinalStreak =
                    smoothstep(
                        0.18h,
                        0.72h,
                        flowAcross * 0.62h +
                        flowAlong * 0.22h +
                        flowDetail * 0.16h);
                half streakBreakup =
                    smoothstep(
                        -0.55h,
                        0.42h,
                        flowDetail * 0.72h -
                        flowAlong * 0.28h);
                half cascadeFoam =
                    cascadeMask *
                    saturate(
                        longitudinalStreak * 0.68h +
                        narrowFlow * 0.32h) *
                    lerp(0.55h, 1.0h, streakBreakup) *
                    lerp(0.70h, 1.0h, saturate(input.color.g)) *
                    min(_CascadeFoamStrength, 1.5h) *
                    0.52h;

                half3 tangentWS = normalize(
                    normalUp > 0.96h
                        ? half3(1.0h, 0.0h, 0.0h)
                        : cross(half3(0.0h, 1.0h, 0.0h),
                            geometricNormal));
                half3 bitangentWS =
                    normalize(cross(geometricNormal, tangentWS));

                half2 poolSlope =
                    (half2(0.82h, 0.57h) * slopeA * 0.62h +
                     half2(-0.42h, 0.91h) * slopeB * 0.38h) *
                        _WaveStrength +
                    half2(0.31h, -0.95h) *
                        detailSlope * _NormalDetailStrength;
                half3 poolRippleNormal = normalize(
                    geometricNormal +
                    half3(-poolSlope.x, 0.0h, -poolSlope.y));

                half routeNormalStrength =
                    lerp(
                        _WaveStrength * 0.72h,
                        _CascadeNormalStrength,
                        cascadeMask);
                half routeSlopeAcross =
                    (flowAcrossSlope * 0.64h +
                     flowDetailSlope * 0.36h) *
                    routeNormalStrength;
                half routeSlopeAlong =
                    (flowAlongSlope * 0.70h +
                     flowDetailSlope * 0.30h) *
                    routeNormalStrength;
                half3 routeRippleNormal = normalize(
                    geometricNormal +
                    tangentWS * routeSlopeAcross +
                    bitangentWS * routeSlopeAlong);
                half3 rippleNormal = normalize(
                    lerp(poolRippleNormal, routeRippleNormal, routeMask));

                float2 screenUv =
                    GetNormalizedScreenSpaceUV(input.positionCS);

                // The unoffset depth controls all depth colour and shoreline
                // decisions. This avoids foam swimming when refraction moves.
                float rawSceneDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth =
                    LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                half sceneDepthValid;
                #if UNITY_REVERSED_Z
                    sceneDepthValid = step(0.00001, rawSceneDepth);
                #else
                    sceneDepthValid = step(rawSceneDepth, 0.99999);
                #endif
                float waterDepth =
                    max(0.0, sceneEyeDepth - input.viewDepth);
                waterDepth = lerp(
                    max((float)_DepthRange, 0.001) * 1.5,
                    waterDepth,
                    sceneDepthValid);
                half depthLinear =
                    saturate(waterDepth / max(_DepthRange, 0.001h));
                half depthBlend =
                    depthLinear * depthLinear *
                    (3.0h - 2.0h * depthLinear);

                half depthDerivative = (half)fwidth(waterDepth);
                half stableFoamFeather =
                    max(_FoamFeather, depthDerivative * 1.5h);
                half foamEdge =
                    (1.0h - smoothstep(
                        _FoamDistance,
                        _FoamDistance + stableFoamFeather,
                        waterDepth)) *
                    sceneDepthValid;
                half foamBreakup =
                    smoothstep(
                        -0.45h,
                        0.45h,
                        waveA * 0.50h +
                        waveB * 0.20h +
                        detailWave * 0.30h);
                half bankDistance =
                    abs(input.uv.x * 2.0h - 1.0h);
                half bankMask =
                    smoothstep(0.56h, 0.94h, bankDistance);
                half foamDomain =
                    lerp(1.0h, bankMask, routeMask);
                half shoreFoam =
                    foamEdge *
                    lerp(0.62h, 1.0h, foamBreakup) *
                    horizontalSurfaceMask *
                    foamDomain;

                // One opaque-colour sample is the complete refraction path.
                // Shore damping prevents pulling dry terrain under the water.
                half3 rippleNormalVS =
                    TransformWorldToViewDir(rippleNormal, true);
                half3 geometricNormalVS =
                    TransformWorldToViewDir(geometricNormal, true);
                half2 refractionVector =
                    (rippleNormalVS - geometricNormalVS).xy;
                refractionVector +=
                    lerp(
                        half2(waveA, waveB),
                        half2(flowAcross, flowAlong),
                        routeMask) *
                    _WaveStrength *
                    0.08h;
                half refractionDepthDamping =
                    smoothstep(
                        0.0h,
                        max(
                            _FoamDistance + _FoamFeather,
                            0.01h),
                        (half)waterDepth) *
                    sceneDepthValid;
                half2 refractionOffset =
                    refractionVector *
                    _RefractionStrength *
                    lerp(1.0h, 0.76h, cascadeMask) *
                    refractionDepthDamping;
                float2 refractedUv = clamp(
                    screenUv + refractionOffset,
                    float2(0.002, 0.002),
                    float2(0.998, 0.998));
                half3 refractedScene =
                    SampleSceneColor(refractedUv);
                half opaqueTextureAvailable = smoothstep(
                    0.001h,
                    0.02h,
                    dot(
                        abs(refractedScene),
                        half3(0.2126h, 0.7152h, 0.0722h)));

                half3 viewDirection =
                    SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(
                    1.0h - saturate(dot(rippleNormal, viewDirection)),
                    _FresnelPower);
                half3 waterAlbedo =
                    lerp(_ShallowColor.rgb, _DeepColor.rgb, depthBlend);

                half flowHighlight =
                    routeMask *
                    saturate(
                        broadFlow * 0.22h +
                        narrowFlow * 0.17h +
                        longitudinalStreak * 0.13h) *
                    lerp(0.20h, 0.34h, cascadeMask);
                waterAlbedo = lerp(
                    waterAlbedo,
                    _ShallowColor.rgb,
                    flowHighlight);
                waterAlbedo = saturate(waterAlbedo * _ColorBoost);

                Light mainLight = GetMainLight(input.shadowCoord);
                half mainAttenuation =
                    mainLight.shadowAttenuation *
                    mainLight.distanceAttenuation;
                half halfLambert = saturate(
                    dot(rippleNormal, mainLight.direction) * 0.5h +
                    0.5h);
                half3 ambientLighting =
                    max(SampleSH(rippleNormal), 0.0h) *
                    _AmbientStrength;
                half3 directLighting =
                    mainLight.color *
                    halfLambert *
                    mainAttenuation;
                half3 surfaceColor =
                    waterAlbedo *
                    (ambientLighting + directLighting);

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
                        (lerp(0.018h, 0.040h, _Smoothness) +
                         fresnel * _FresnelStrength) *
                        _ReflectionStrength *
                        lerp(1.0h, 0.76h, cascadeMask));
                surfaceColor = lerp(
                    surfaceColor,
                    environmentReflection,
                    reflectionMask);

                half crestSignal =
                    waveA * 0.52h +
                    waveB * 0.30h +
                    detailWave * 0.18h;
                half crestFoam =
                    smoothstep(0.40h, 0.78h, crestSignal) *
                    _CrestStrength *
                    poolMask *
                    lerp(0.22h, 0.07h, depthBlend);
                half foamMask = saturate(
                    (shoreFoam + cascadeFoam + crestFoam) *
                    _FoamIntensity);
                half foamHalfLambert = saturate(
                    dot(geometricNormal, mainLight.direction) * 0.5h +
                    0.5h);
                half3 foamLighting =
                    ambientLighting +
                    mainLight.color *
                        foamHalfLambert *
                        mainAttenuation;
                half3 foamColor =
                    saturate(_FoamColor.rgb * _ColorBoost) *
                    foamLighting;
                surfaceColor = lerp(
                    surfaceColor,
                    foamColor,
                    foamMask);

                half3 halfVector =
                    SafeNormalize(mainLight.direction + viewDirection);
                half glint = pow(
                    saturate(dot(rippleNormal, halfVector)),
                    max(8.0h, _GlintPower));
                surfaceColor +=
                    mainLight.color *
                    glint *
                    _GlintStrength *
                    mainAttenuation *
                    lerp(0.42h, 1.0h, _Smoothness) *
                    (1.0h - foamMask * 0.65h);
                surfaceColor = MixFog(surfaceColor, input.fogFactor);

                // The pass replaces the framebuffer, so transmission is
                // resolved here exactly once. Alpha remains one and cannot
                // trigger a second hardware blend with the same scene.
                half bodyCoverage = saturate(
                    saturate(_Opacity) *
                        lerp(0.36h, 0.86h, depthBlend) +
                    routeMask * 0.03h +
                    cascadeMask * 0.06h);
                half transmission =
                    saturate(_TransmissionStrength) *
                    opaqueTextureAvailable *
                    (1.0h - bodyCoverage) *
                    (1.0h - foamMask * 0.94h) *
                    (1.0h - reflectionMask * 0.85h);
                half3 shallowTransmissionTint = lerp(
                    half3(1.0h, 1.0h, 1.0h),
                    saturate(_ShallowColor.rgb * 1.18h),
                    0.12h);
                half3 deepTransmissionTint = lerp(
                    shallowTransmissionTint,
                    saturate(_DeepColor.rgb * 1.35h),
                    0.46h);
                half3 transmissionTint = lerp(
                    shallowTransmissionTint,
                    deepTransmissionTint,
                    depthBlend);
                half3 color =
                    refractedScene * transmissionTint * transmission +
                    surfaceColor * (1.0h - transmission);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}

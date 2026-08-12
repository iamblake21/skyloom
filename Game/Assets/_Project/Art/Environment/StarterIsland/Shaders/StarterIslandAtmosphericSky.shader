Shader "CML/Environment/Starter Island Atmospheric Sky"
{
    Properties
    {
        // FLinearColor source parameters are vectors on purpose. They are
        // already linear data and must not receive Unity's color conversion.
        _SkyTopColorLinear("Source Sky Top (Linear)", Vector) = (0.08022, 0.59720, 0.61050, 1)
        _HorizonColorLinear("Source Horizon (Linear)", Vector) = (0.168627, 1, 1, 1)
        _Day01("Source 1 Day 0 Night", Range(0, 1)) = 1
        _NoonPhase("Display Noon Phase", Range(0, 1)) = 1
        _DawnPhase("Display Dawn Phase", Range(0, 1)) = 0
        _EarlyDuskPhase("Display Early Dusk Phase", Range(0, 1)) = 0
        _LateDuskPhase("Display Late Dusk Phase", Range(0, 1)) = 0
        _CloudAmount("Source 2D Cloud Amount", Float) = 5
        _CloudTopColorLinear("Source 2D Cloud Top (Linear)", Vector) = (0.136719, 0.724438, 0.875, 1)
        _CloudBottomColorLinear("Source 2D Cloud Bottom (Linear)", Vector) = (0, 0.568628, 1, 1)
        _CloudColor("Cloud Light Color", Color) = (0.784314, 0.854902, 0.843137, 1)
        _CloudShadowColor("Cloud Shadow Color", Color) = (0.521569, 0.654902, 0.698039, 1)
        _CloudScale("Cloud Scale", Range(0.05, 2)) = 0.46
        _CloudCoverage("Cloud Coverage", Range(0, 1)) = 0.51
        _CloudSoftness("Cloud Softness", Range(0.01, 0.35)) = 0.065
        _CloudSpeed("Cloud Speed", Range(0, 0.2)) = 0.015
        _CloudOpacity("Cloud Opacity", Range(0, 1)) = 0.62
        _RainFade1Sunny0("Source Rain Fade", Range(0, 1)) = 0
        _SnowHailClouds("Source Snow/Hail Clouds", Range(0, 1)) = 0
        _SunDiscColorLinear("Source Sun Disc (Linear)", Vector) = (1, 0.596078, 0, 1)
        _FogInscatteringColorLinear("Source Fog Inscattering (Linear)", Vector) = (0.070588, 0.611765, 0.623529, 1)
        _FogDirectionalColorLinear("Source Fog Directional (Linear)", Vector) = (0.102242, 0.351533, 0.473531, 1)
        _FogDensity("Source Fog Density", Float) = 0.12
        _FogFalloff("Source Fog Height Falloff", Float) = 0.12
        _SunDirectionWS("Source Sun Direction", Vector) = (0.42, 0.67, -0.61, 0)
        _SubsurfaceToUnlitScale("Subsurface To Unlit Scale", Range(0, 1)) = 0.1
        _Exposure("Unlit Output Calibration", Range(0, 3)) = 0.98
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "SourceSkyAlgebra"
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SkyTopColorLinear;
                float4 _HorizonColorLinear;
                float _Day01;
                float _NoonPhase;
                float _DawnPhase;
                float _EarlyDuskPhase;
                float _LateDuskPhase;
                float _CloudAmount;
                float4 _CloudTopColorLinear;
                float4 _CloudBottomColorLinear;
                float4 _CloudColor;
                float4 _CloudShadowColor;
                float _CloudScale;
                float _CloudCoverage;
                float _CloudSoftness;
                float _CloudSpeed;
                float _CloudOpacity;
                float _RainFade1Sunny0;
                float _SnowHailClouds;
                float4 _SunDiscColorLinear;
                float4 _FogInscatteringColorLinear;
                float4 _FogDirectionalColorLinear;
                float _FogDensity;
                float _FogFalloff;
                float4 _SunDirectionWS;
                float _SubsurfaceToUnlitScale;
                float _Exposure;
            CBUFFER_END

            // Literal constants recovered from the clear-weather M_Sky
            // permutation. Keeping them named makes the clean-room port
            // auditable against the decoded constant buffer.
            static const float KSkyHue0 = 0.653451272;
            static const float KSkyGain0 = 0.4;
            static const float KSkyRadial0 = 5.434782609;
            // M_Sky is lit/subsurface while a Unity skybox is unlit. Keep this
            // engine translation isolated after the decoded base-color path;
            // it must never alter the source algebra or one time of day only.
            static const float KLitToUnlitRecovery = 0.82;
            // Calibrated output-space contribution of the source lit sky. It
            // belongs to the single lit-to-unlit bridge below, never to the
            // decoded M_Sky base-color operations.
            static const float3 KLitSkyDayShift =
                float3(0.12, 0.02, 0.095);
            static const float KSnowDesaturation = 0.2;
            static const float KSkyHue1 = 0.552920307;
            static const float KBroadGain = 0.754065;
            static const float KBroadDayGain = 0.343001;
            static const float KCloudRadial1 = 1.926158777;
            static const float KDetailThreshold = 0.2;
            static const float KSunCenter = 1.0;
            static const float KSunWidthDay = 0.001;
            static const float KSunWidthNight = 0.0008;
            static const float KSunEdgeGain = 100000.0;
            static const float KSunIntensity = 400.0;
            static const float KSunCloudOcclusion = 11.276255;
            static const float KCloudHue = -0.251327412;
            static const float KCloudHueGain = 1.724835;
            static const float KCloudDesaturation = 0.056;
            static const float KCloudDetailGain = 1.773676;
            static const float KCloudBlendGain = 0.1;
            static const float KScatterLobeScale = 0.984247125;
            static const float3 KCloudBaseColor =
                float3(0.6626222, 0.95006424, 1.0);
            static const float3 KScatterTeal =
                float3(0.07277211, 0.16666667, 0.16169062);
            static const float3 KScatterGreen =
                float3(0.030110676, 0.19270833, 0.059503365);
            static const float3 KScatterWarm =
                float3(1.0, 0.7891488, 0.24101162);
            static const float3 KLuminance =
                float3(0.2126, 0.7152, 0.0722);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
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
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(
                    lerp(a, b, local.x),
                    lerp(c, d, local.x),
                    local.y);
            }

            float CloudNoise(float2 uv)
            {
                float value = ValueNoise(uv) * 0.56;
                uv = uv * 2.03 + float2(13.7, -8.2);
                value += ValueNoise(uv) * 0.2912;
                uv = uv * 2.11 + float2(-4.1, 17.3);
                value += ValueNoise(uv) * 0.1456;
                // Three normalized octaves retain the broad silhouette while
                // removing high-frequency shimmer and 25% of the hash work.
                return saturate(value * 1.00321);
            }

            float3 HueRotateGrayAxis(float3 color, float angle)
            {
                const float3 axis = float3(
                    0.577350269,
                    0.577350269,
                    0.577350269);
                float sineValue;
                float cosineValue;
                sincos(angle, sineValue, cosineValue);
                return color * cosineValue +
                    cross(axis, color) * sineValue +
                    axis * dot(axis, color) * (1.0 - cosineValue);
            }

            float SourceDensity(float u)
            {
                if (u < 0.0 || abs(u) <= 0.00001)
                {
                    return 0.0;
                }

                float scaled = 2.333 * u;
                return 1.0 - exp2(
                    -1.442695 * scaled * scaled);
            }

            float2 SourceSkyUv(float3 direction, out float radius)
            {
                // SM_SkySphere's upper chart is a stereographic projection.
                // This relationship was fitted and then verified against all
                // 1,025 unique upper-dome positions: the pole is UV .5, the
                // horizon radius is .5, and the material reads this exact
                // radial distance from TEXCOORD_3. The 23.528 degree reflected
                // rotation preserves the source chart orientation; it does
                // not affect the radial masks.
                if (direction.y >= 0.0)
                {
                    float2 q = direction.xz / max(
                        1.0 + direction.y,
                        0.00001);
                    const float cosineAngle = 0.9168383;
                    const float sineAngle = 0.3992586;
                    float2 chart = float2(
                        cosineAngle * q.x + sineAngle * q.y,
                        sineAngle * q.x - cosineAngle * q.y);
                    float2 uv = float2(0.5, 0.5) + chart * 0.5;
                    radius = length(uv - float2(0.5, 0.5));
                    return uv;
                }

                // The source lower cap is a separate UV chart collapsed near
                // this constant. Duplicated horizon vertices prove that no
                // single continuous position-to-UV function exists across the
                // two charts, so reproduce the lower chart explicitly.
                const float2 lowerCapUv = float2(0.9872, 0.9886);
                radius = length(lowerCapUv - float2(0.5, 0.5));
                return lowerCapUv;
            }

            float SourceSkyFogAmount(float density)
            {
                // FogHeightFalloff is a gradient over world height, not an
                // angular epsilon. A skybox has no world-space ray origin or
                // height, so the only defensible local bridge is one normalized
                // atmospheric column. The scene fog system owns the extracted
                // height falloff; this pass uses only its density and colors.
                return 1.0 - exp2(
                    -1.442695 * max(density, 0.0));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z =
                    UNITY_RAW_FAR_CLIP_VALUE *
                    output.positionCS.w;
                output.directionWS =
                    TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirection = normalize(input.directionWS);
                float3 sunDirection = normalize(_SunDirectionWS.xyz);
                float day = saturate(_Day01);
                float rain = saturate(_RainFade1Sunny0);
                float snow = saturate(_SnowHailClouds);
                float3 sourceSkyTop = max(
                    _SkyTopColorLinear.rgb,
                    0.0);
                float sourceSkyTopLuminance =
                    dot(sourceSkyTop, KLuminance);
                // Phase weights come from the authoritative clock, not RGB
                // heuristics. This keeps sunrise and sunset continuous even
                // when source color channels cross the same values twice.
                float noonPhase = saturate(_NoonPhase);
                float dawnPhase = saturate(_DawnPhase);
                float earlyDuskPhase = saturate(_EarlyDuskPhase);
                float lateDuskPhase = saturate(_LateDuskPhase);
                float twilight = saturate(
                    dawnPhase + earlyDuskPhase + lateDuskPhase);
                float displayPhaseCoverage = saturate(
                    noonPhase +
                    dawnPhase +
                    earlyDuskPhase +
                    lateDuskPhase);
                float displayPhaseSum = max(
                    noonPhase +
                    dawnPhase +
                    earlyDuskPhase +
                    lateDuskPhase,
                    0.0001);
                float4 displayPhaseWeights = float4(
                    noonPhase,
                    dawnPhase,
                    earlyDuskPhase,
                    lateDuskPhase) / displayPhaseSum;
                float radius;
                float2 skyUv = SourceSkyUv(viewDirection, radius);

                // 1. Decoded M_Sky base-color path. This block intentionally
                // uses only source inputs and recovered literal constants.
                float3 topA =
                    HueRotateGrayAxis(sourceSkyTop, KSkyHue0) * KSkyGain0;
                float skyDensity =
                    SourceDensity(1.0 - radius * KSkyRadial0);
                float3 sourceBase = lerp(
                    sourceSkyTop,
                    topA,
                    skyDensity);
                sourceBase = lerp(
                    sourceBase,
                    dot(sourceBase, KLuminance).xxx,
                    snow * KSnowDesaturation);

                // 2. Clean-room broad/detail textures, consumed in the exact
                // decoded order. Three FBM samples replace the unavailable t6
                // broad mask and the two counter-scrolling t7 detail samples.
                float maskScale = max(_CloudScale / 0.46, 0.05);
                float2 sourceMaskUv = skyUv * 2.0 * maskScale;
                float2 cloudScroll = float2(
                    _Time.y * _CloudSpeed,
                    0.0);
                float broadCloud = 0.0;
                float2 detailUv =
                    sourceMaskUv * 2.73 + float2(31.4, -12.8);
                float detailCloudA = 0.0;
                float detailCloudB = 0.0;
                // The decoded source branch is fully replaced by the display
                // calibration at unit coverage. This is a uniform material
                // branch, so day/twilight pixels skip three expensive FBM
                // evaluations without introducing wave divergence. The
                // decoded path remains intact through night and transitions.
                UNITY_BRANCH
                if (displayPhaseCoverage < 0.999)
                {
                    broadCloud = CloudNoise(
                        sourceMaskUv * 0.82 + cloudScroll);
                    detailCloudA = CloudNoise(detailUv + cloudScroll);
                    detailCloudB = CloudNoise(detailUv - cloudScroll);
                }
                float totalCloudAmount = max(
                    0.0,
                    _CloudAmount + snow);
                float3 broad = saturate(
                    broadCloud.xxx * day * KBroadDayGain);
                float detailMask = saturate(
                    (detailCloudA * totalCloudAmount) *
                    (detailCloudB * totalCloudAmount));
                float3 detail = detailMask.xxx;
                float sourceCloud =
                    SourceDensity(1.0 - radius * KCloudRadial1) *
                    step(KDetailThreshold, detailMask);

                float3 broadTone =
                    HueRotateGrayAxis(sourceSkyTop, KSkyHue1) *
                    KBroadGain;
                float3 preSunBase = lerp(
                    sourceBase,
                    broadTone,
                    broad * (1.0 - sourceCloud));

                // The only base-color engine bridge: recover the lit material's
                // diffuse contribution globally, never through a noon-only tint.
                float litSkyDayWeight =
                    smoothstep(0.28, 0.46, sourceSkyTopLuminance) * day;
                float3 litBridgeTarget =
                    sourceSkyTop + KLitSkyDayShift * litSkyDayWeight;
                float3 recoveredBase = lerp(
                    preSunBase,
                    litBridgeTarget,
                    KLitToUnlitRecovery);

                // 3. Decoded source sun disc and hard source-cloud occlusion.
                float sunDot = dot(viewDirection, sunDirection);
                float sunWidth = lerp(
                    KSunWidthNight,
                    KSunWidthDay,
                    day);
                float sunDelta = sunDot - KSunCenter;
                float sunDiscDistance = sunWidth - abs(sunDelta);
                float sunAntialias = max(
                    fwidth(sunDot) * 1.5,
                    0.000001);
                float sunMask = smoothstep(
                    -sunAntialias,
                    sunAntialias,
                    sunDiscDistance);
                sunMask *= 1.0 - saturate(
                    sourceCloud * KSunCloudOcclusion) *
                    (1.0 - displayPhaseCoverage);
                float3 displaySunColor =
                    float3(1.000000, 0.887923, 0.450786) *
                        displayPhaseWeights.x +
                    float3(1.000000, 0.720000, 0.155000) *
                        displayPhaseWeights.y +
                    float3(1.000000, 0.610000, 0.105000) *
                        displayPhaseWeights.z +
                    float3(1.000000, 0.255000, 0.060000) *
                        displayPhaseWeights.w;
                displaySunColor = lerp(
                    max(_SunDiscColorLinear.rgb, 0.0),
                    displaySunColor,
                    displayPhaseCoverage);
                float3 sunHdr =
                    sunMask *
                    displaySunColor *
                    KSunIntensity *
                    (1.0 - rain);
                // Source radiance is far above the shoulder of Unity ACES.
                // Compress each calibrated phase before tonemapping; the late
                // disc is deliberately dimmer because the reference no longer
                // shows a dominant neon sun at that point in the cycle.
                float calibratedSunScale =
                    displayPhaseWeights.x +
                    displayPhaseWeights.y * 0.0016 +
                    displayPhaseWeights.z * 0.0018 +
                    displayPhaseWeights.w * 0.0014;
                sunHdr *= lerp(
                    1.0,
                    calibratedSunScale,
                    displayPhaseCoverage);
                float twilightSunPhase = saturate(
                    dawnPhase + earlyDuskPhase + lateDuskPhase);

                // 4. Decoded M_Sky cloud tone and blend. These constants were
                // previously declared but bypassed by the visible surrogate.
                float3 rotatedCloud =
                    HueRotateGrayAxis(sourceSkyTop, KCloudHue) *
                    KCloudHueGain;
                rotatedCloud = lerp(
                    rotatedCloud,
                    dot(rotatedCloud, KLuminance).xxx,
                    KCloudDesaturation);
                float3 detailWeight = saturate(
                    detail * KCloudDetailGain);
                float3 sourceCloudTone = lerp(
                    KCloudBaseColor,
                    rotatedCloud,
                    detailWeight);
                float sourceCloudBlend = saturate(
                    clamp(day, 0.1, 1.0) *
                    sourceCloud *
                    KCloudBlendGain);
                float3 sky = lerp(
                    recoveredBase,
                    sourceCloudTone,
                    sourceCloudBlend);

                // The source writes this branch into the Subsurface GBuffer.
                // A skybox cannot do that, so only the final conversion scale
                // is an explicit Unity bridge.
                float3 daylightScatter = lerp(
                    KScatterGreen,
                    KScatterTeal,
                    day);
                float3 scatterPalette = lerp(
                    KScatterWarm,
                    daylightScatter,
                    detailWeight);
                float scatterLobe = pow(
                    saturate(
                        1.0 -
                        abs(sunDot - KSunCenter) *
                        KScatterLobeScale),
                    10.0);
                float3 sourceSubsurface = saturate(
                    scatterPalette * sourceCloud * scatterLobe);
                sky += sourceSubsurface * _SubsurfaceToUnlitScale;

                // 5. Display calibration. The extracted curves are material
                // and light inputs, not final pixels; feeding them directly to
                // Unity ACES produced the measured cyan noon and mustard dusk.
                // Four mutually-exclusive look anchors preserve the extracted
                // phase timing while translating the final appearance into
                // this renderer. Warmth is directional and horizon-localized,
                // so the upper dome remains blue/teal at every twilight.
                float2 viewAzimuth = viewDirection.xz / max(
                    length(viewDirection.xz),
                    0.0001);
                float2 sunAzimuth = sunDirection.xz / max(
                    length(sunDirection.xz),
                    0.0001);
                float horizontalSun = pow(
                    saturate(dot(viewAzimuth, sunAzimuth)),
                    2.0);
                float3 phaseZenith =
                    float3(0.119500, 0.462100, 0.651400) *
                        displayPhaseWeights.x +
                    float3(0.018000, 0.165000, 0.300000) *
                        displayPhaseWeights.y +
                    float3(0.012000, 0.145000, 0.165000) *
                        displayPhaseWeights.z +
                    float3(0.010000, 0.125000, 0.145000) *
                        displayPhaseWeights.w;
                float3 phaseHorizon =
                    float3(0.376300, 0.723100, 0.822800) *
                        displayPhaseWeights.x +
                    float3(0.650000, 0.300000, 0.150000) *
                        displayPhaseWeights.y +
                    float3(0.450000, 0.200000, 0.120000) *
                        displayPhaseWeights.z +
                    float3(0.300000, 0.100000, 0.070000) *
                        displayPhaseWeights.w;
                float3 phaseCloudBottom =
                    float3(0.242300, 0.521000, 0.686700) *
                        displayPhaseWeights.x +
                    float3(0.180000, 0.300000, 0.350000) *
                        displayPhaseWeights.y +
                    float3(0.120000, 0.220000, 0.180000) *
                        displayPhaseWeights.z +
                    float3(0.100000, 0.180000, 0.160000) *
                        displayPhaseWeights.w;
                float3 phaseCoolHorizon = lerp(
                    phaseZenith,
                    phaseCloudBottom,
                    0.55);
                float domeHeight = smoothstep(
                    -0.05,
                    0.65,
                    viewDirection.y);
                float3 phaseSkyTarget = lerp(
                    phaseCoolHorizon,
                    phaseZenith,
                    domeHeight);
                float horizonVertical = exp2(
                    -8.0 * abs(viewDirection.y));
                float horizonFacing = lerp(
                    0.18,
                    1.0,
                    horizontalSun);
                float3 decodedSky = sky;
                float3 calibratedSky = lerp(
                    phaseSkyTarget,
                    phaseHorizon,
                    saturate(horizonVertical * horizonFacing));
                sky = lerp(
                    decodedSky,
                    calibratedSky,
                    displayPhaseCoverage);
                float sunwardHaze =
                    twilightSunPhase *
                    pow(saturate(sunDot), 8.0) *
                    0.10;
                float3 sunwardHazeColor = lerp(
                    phaseHorizon,
                    displaySunColor,
                    0.28);
                sky = lerp(
                    sky,
                    sunwardHazeColor,
                    sunwardHaze);
                // Apply one decoded sun after the base translation. Its source
                // mask already contains M_Sky cloud occlusion; the separate
                // visible cloud layer below then covers it exactly once.
                sky += sunHdr;

                // 6. Separate clean-room M_Cloud surrogate. It is composited
                // once, so it naturally covers the sun without also driving
                // the decoded M_Sky sun-occlusion branch.
                // The visible cloud layer in the source is separate geometry,
                // not the sky-sphere's stereographic t6/t7 chart. Keep its
                // proven dome projection independent from the exact M_Sky core.
                float cloudProjectionHeight = max(
                    viewDirection.y + 0.26,
                    0.18);
                float2 projectedCloudUv =
                    viewDirection.xz / cloudProjectionHeight *
                    (_CloudScale * 2.55);
                projectedCloudUv += float2(
                    _Time.y * _CloudSpeed,
                    -_Time.y * _CloudSpeed * 0.62);
                float visibleBroadCloud = CloudNoise(
                    projectedCloudUv * 0.82);
                float visibleDetailCloud = CloudNoise(
                    projectedCloudUv * 2.73 +
                    float2(31.4, -12.8));
                float cloudField =
                    visibleBroadCloud * 0.79 +
                    visibleDetailCloud * 0.34 -
                    0.065;
                float coverageThreshold =
                    lerp(0.70, 0.40, _CloudCoverage);
                float edgeWidth =
                    max(0.025, _CloudSoftness * 0.72);
                float cloudShape = smoothstep(
                    coverageThreshold - edgeWidth,
                    coverageThreshold + edgeWidth,
                    cloudField);
                float cloudCore = smoothstep(
                    coverageThreshold + edgeWidth * 0.18,
                    coverageThreshold + edgeWidth * 1.65,
                    cloudField);
                float cloudHorizonFade =
                    smoothstep(-0.10, 0.12, viewDirection.y);
                // The extracted amount still modulates weather density, but a
                // non-zero floor prevents dawn/night keys from eliminating the
                // complete cloud layer (the previous hard threshold did).
                float cloudAmount01 = saturate(
                    (_CloudAmount - 0.3) / 4.7);
                float cloudAmountVisibility =
                    lerp(0.78, 1.0, cloudAmount01);
                cloudShape *=
                    cloudHorizonFade *
                    _CloudOpacity *
                    cloudAmountVisibility *
                    lerp(0.72, 1.0, visibleDetailCloud);
                // Let the atmospheric gradient breathe through the broad
                // dawn and early-golden-hour cloud masses. This deliberately
                // leaves noon and the already approved late sunset intact.
                float cloudRelief = max(
                    dawnPhase,
                    max(earlyDuskPhase, lateDuskPhase));
                cloudShape *= lerp(1.0, 0.86, cloudRelief);

                // Keep the decoded source colors for uncalibrated night, but
                // translate the four visible phases into role-specific display
                // anchors. This prevents a warm cloud color from tinting the
                // entire dome and keeps noon, dawn and both dusk stages distinct.
                float3 sourceCloudLight = lerp(
                    _CloudColor.rgb,
                    max(_CloudTopColorLinear.rgb, 0.0),
                    lerp(0.34, 0.62, twilight));
                float3 sourceCloudShadow = lerp(
                    _CloudShadowColor.rgb,
                    max(_CloudBottomColorLinear.rgb, 0.0),
                    lerp(0.38, 0.74, twilight));
                // M_Cloud is a lit/subsurface material in the source. Its raw
                // colors are not nighttime emission. Attenuate only the
                // uncalibrated source branch so midnight clouds remain
                // readable silhouettes instead of glowing white masses.
                sourceCloudLight *= lerp(0.22, 1.0, day);
                sourceCloudShadow *= lerp(0.28, 1.0, day);
                float3 phaseCloudLight =
                    float3(0.296100, 0.630800, 0.775800) *
                        displayPhaseWeights.x +
                    float3(0.700000, 0.550000, 0.380000) *
                        displayPhaseWeights.y +
                    float3(0.650000, 0.450000, 0.250000) *
                        displayPhaseWeights.z +
                    float3(0.500000, 0.300000, 0.180000) *
                        displayPhaseWeights.w;
                float3 phaseCloudShadow = phaseCloudBottom;
                float3 cloudLight = lerp(
                    sourceCloudLight,
                    phaseCloudLight,
                    displayPhaseCoverage);
                float3 cloudShadow = lerp(
                    sourceCloudShadow,
                    phaseCloudShadow,
                    displayPhaseCoverage);
                float3 cloudTone = lerp(
                    cloudShadow,
                    cloudLight,
                    saturate(
                        0.18 +
                        cloudCore * 0.72 +
                        saturate(sunDot) * 0.10));
                float cloudEdge = saturate(
                    (cloudShape - cloudCore * _CloudOpacity) * 3.0);
                cloudTone +=
                    phaseCloudLight *
                    cloudEdge *
                    twilightSunPhase *
                    pow(saturate(sunDot), 6.0) *
                    0.10;
                sky = lerp(sky, cloudTone, cloudShape);

                // 7. External fog composition. The global term uses the raw
                // teal/blue inscattering; the warm directional term remains
                // localized by the source exponent-5 solar lobe. The compact
                // normalized column above prevents a flat colored horizon.
                float fogAmount =
                    SourceSkyFogAmount(_FogDensity) * 0.22;
                float directionalFog = pow(
                    saturate(dot(viewDirection, sunDirection)),
                    5.0);
                float3 fogColor = lerp(
                    max(_FogInscatteringColorLinear.rgb, 0.0),
                    max(_FogDirectionalColorLinear.rgb, 0.0),
                    directionalFog);
                sky = lerp(sky, fogColor, fogAmount);

                return half4(
                    max(0.0, sky * _Exposure),
                    1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

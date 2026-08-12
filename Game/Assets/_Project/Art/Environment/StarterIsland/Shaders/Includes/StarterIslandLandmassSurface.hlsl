#ifndef CML_STARTER_ISLAND_LANDMASS_SURFACE_INCLUDED
#define CML_STARTER_ISLAND_LANDMASS_SURFACE_INCLUDED

// Shared, world-aligned landmass surface used by Terrain cliffs and modular
// cliff/shelf/cap meshes. Keep this file free of object UV assumptions: the
// source meshes use different UV layouts, while their material language stays
// continuous in world space.

TEXTURE2D(_LandmassCliffAlbedo);
SAMPLER(sampler_LandmassCliffAlbedo);
TEXTURE2D(_LandmassCliffNormal);
SAMPLER(sampler_LandmassCliffNormal);
TEXTURE2D(_LandmassVariationMask);
SAMPLER(sampler_LandmassVariationMask);
TEXTURE2D(_LandmassGrassDetail);
SAMPLER(sampler_LandmassGrassDetail);

float _LandmassWorldSize;
float _LandmassVariationWorldSizeA;
float _LandmassVariationWorldSizeB;
half _LandmassNormalStrength;
half _LandmassSlopeOffset;
half _LandmassSlopeHardness;
half _LandmassColorSlopeOffset;
half _LandmassColorSlopeHardness;
half _LandmassGrassAmount;
half4 _LandmassRockTintColor;
half _LandmassRockTintStrength;
half4 _LandmassGrassColor1;
half4 _LandmassGrassColor2;
float _LandmassGrassDetailWorldSize;
half _LandmassGrassDetailStrength;

struct CMLLandmassSurface
{
    half3 albedo;
    half3 normalWS;
    half topMask;
    half variation;
    half soilMask;
};

half CMLLandmassSmooth01(half value)
{
    value = saturate(value);
    return value * value * (3.0h - 2.0h * value);
}

half CMLSampleLandmassVariation(float3 positionWS)
{
    // Decoded M_Island_Landmass samples the same macro mask twice, at
    // approximately 35 m and 40.96 m, then saturates their sum.
    float2 uvA =
        positionWS.xz /
        max(abs(_LandmassVariationWorldSizeA), 0.001);
    float2 uvB =
        positionWS.xz /
        max(abs(_LandmassVariationWorldSizeB), 0.001);
    half variationA =
        SAMPLE_TEXTURE2D(
            _LandmassVariationMask,
            sampler_LandmassVariationMask,
            uvA).r;
    half variationB =
        SAMPLE_TEXTURE2D(
            _LandmassVariationMask,
            sampler_LandmassVariationMask,
            uvB).r;
    return saturate(variationA + variationB);
}

half CMLLandmassTopMask(
    float3 positionWS,
    half3 normalWS,
    half variation)
{
    // The landmass colour cap uses Param_2=-0.499157 and
    // Param_7=2.953668.  The separate -0.6/10 pair controls normal flattening
    // and must not be reused to widen the colour transition.
    return
        saturate(
            (normalWS.y + _LandmassColorSlopeOffset) *
            _LandmassColorSlopeHardness) *
        saturate(_LandmassGrassAmount);
}

half CMLTerrainTopMask(half3 normalWS)
{
    // Decoded M_Landscape_Solarpunk uses the steeper -0.6 / 10 pair for
    // the landscape slope-to-top transition.  It is intentionally separate
    // from the wider organic cap used by M_Island_Landmass.
    return
        saturate(
            (normalize(normalWS).y + _LandmassSlopeOffset) *
            _LandmassSlopeHardness);
}

void CMLSampleLandmassRockSide(
    float3 positionWS,
    half3 geometricNormalWS,
    out half3 rockAlbedo,
    out half3 rockNormalWS)
{
    geometricNormalWS = normalize(geometricNormalWS);
    float inverseWorldSize = rcp(max(abs(_LandmassWorldSize), 0.001));
    float2 uvA = float2(positionWS.x, positionWS.y) * -inverseWorldSize;
    float2 uvB = float2(positionWS.z, positionWS.y) * -inverseWorldSize;
    half wallXWeight = saturate(abs(geometricNormalWS.x) * 3.0h - 1.0h);
    half normalWallXWeight = saturate(abs(geometricNormalWS.x));

    half3 albedoA =
        SAMPLE_TEXTURE2D(
            _LandmassCliffAlbedo,
            sampler_LandmassCliffAlbedo,
            uvA).rgb;
    half3 albedoB =
        SAMPLE_TEXTURE2D(
            _LandmassCliffAlbedo,
            sampler_LandmassCliffAlbedo,
            uvB).rgb;
    rockAlbedo = lerp(albedoA, albedoB, wallXWeight);
    // M_Landscape_Solarpunk uses T_Cliff directly on slopes.  The 30% clay
    // tint belongs only to M_Island_Landmass and is applied by the 3-axis path.

    half3 tangentNormalA =
        UnpackNormal(
            SAMPLE_TEXTURE2D(
                _LandmassCliffNormal,
                sampler_LandmassCliffNormal,
                uvA));
    half3 tangentNormalB =
        UnpackNormal(
            SAMPLE_TEXTURE2D(
                _LandmassCliffNormal,
                sampler_LandmassCliffNormal,
                uvB));

    // Treat the normal texture as a tangent-plane perturbation of the real
    // mesh/Terrain normal.  A flat normal therefore returns the geometric
    // normal exactly.  This also prevents a projection plane from flipping a
    // wall normal away from the light at axis transitions.
    half3 perturbA = half3(-tangentNormalA.x, -tangentNormalA.y, 0.0h);
    half3 perturbB = half3(0.0h, -tangentNormalB.y, -tangentNormalB.x);
    half3 perturbation = lerp(perturbA, perturbB, normalWallXWeight);
    perturbation -=
        geometricNormalWS * dot(perturbation, geometricNormalWS);
    perturbation *= max(_LandmassNormalStrength, 0.0h);
    half perturbationLength = length(perturbation);
    perturbation *=
        min(1.0h, 0.5h / max(perturbationLength, 0.0001h));
    rockNormalWS = normalize(geometricNormalWS + perturbation);
}

void CMLSampleLandmassRock(
    float3 positionWS,
    half3 geometricNormalWS,
    out half3 rockAlbedo,
    out half3 rockNormalWS)
{
    geometricNormalWS = normalize(geometricNormalWS);
    float inverseWorldSize = rcp(max(abs(_LandmassWorldSize), 0.001));

    // Exact decoded order, converted from UE Z-up to Unity Y-up:
    // A=XY wall, B=ZY wall, lerp by abs(N.x); then C=XZ top, lerp by abs(N.y).
    float2 uvA =
        float2(positionWS.x, positionWS.y) *
        -inverseWorldSize;
    float2 uvB =
        float2(positionWS.z, positionWS.y) *
        -inverseWorldSize;
    float2 uvC =
        float2(positionWS.x, positionWS.z) *
        -inverseWorldSize;
    half wallXWeight = saturate(abs(geometricNormalWS.x) * 3.0h - 1.0h);
    half topWeight = saturate(abs(geometricNormalWS.y) * 3.0h - 1.0h);
    half normalWallXWeight = saturate(abs(geometricNormalWS.x));
    half normalTopWeight = saturate(abs(geometricNormalWS.y));

    half3 albedoA =
        SAMPLE_TEXTURE2D(
            _LandmassCliffAlbedo,
            sampler_LandmassCliffAlbedo,
            uvA).rgb;
    half3 albedoB =
        SAMPLE_TEXTURE2D(
            _LandmassCliffAlbedo,
            sampler_LandmassCliffAlbedo,
            uvB).rgb;
    half3 albedoC =
        SAMPLE_TEXTURE2D(
            _LandmassCliffAlbedo,
            sampler_LandmassCliffAlbedo,
            uvC).rgb;
    half3 sideAlbedo = lerp(albedoA, albedoB, wallXWeight);
    rockAlbedo = lerp(sideAlbedo, albedoC, topWeight);
    rockAlbedo =
        lerp(
            rockAlbedo,
            _LandmassRockTintColor.rgb,
            saturate(_LandmassRockTintStrength));

    half3 tangentNormalA =
        UnpackNormal(
            SAMPLE_TEXTURE2D(
                _LandmassCliffNormal,
                sampler_LandmassCliffNormal,
                uvA));
    half3 tangentNormalB =
        UnpackNormal(
            SAMPLE_TEXTURE2D(
                _LandmassCliffNormal,
                sampler_LandmassCliffNormal,
                uvB));
    half3 tangentNormalC =
        UnpackNormal(
            SAMPLE_TEXTURE2D(
                _LandmassCliffNormal,
                sampler_LandmassCliffNormal,
                uvC));

    half3 perturbA = half3(-tangentNormalA.x, -tangentNormalA.y, 0.0h);
    half3 perturbB = half3(0.0h, -tangentNormalB.y, -tangentNormalB.x);
    half3 perturbC = half3(-tangentNormalC.x, 0.0h, -tangentNormalC.y);
    half3 sidePerturbation =
        lerp(perturbA, perturbB, normalWallXWeight);
    half3 perturbation =
        lerp(sidePerturbation, perturbC, normalTopWeight);
    perturbation -=
        geometricNormalWS * dot(perturbation, geometricNormalWS);
    perturbation *= max(_LandmassNormalStrength, 0.0h);
    half perturbationLength = length(perturbation);
    perturbation *=
        min(1.0h, 0.5h / max(perturbationLength, 0.0001h));
    rockNormalWS = normalize(geometricNormalWS + perturbation);
}

half3 CMLLandmassGrassColor(float3 positionWS, half variation)
{
    // M_Island_Landmass exposes exactly two FLinearColor grass endpoints and
    // no top diffuse/normal texture.  Keeping this as a two-colour macro lerp
    // avoids the fluorescent third highlight that the previous reconstruction
    // introduced.
    half3 sourceGrass =
        lerp(
            _LandmassGrassColor2.rgb,
            _LandmassGrassColor1.rgb,
            saturate(variation));
    float2 detailUv =
        positionWS.xz /
        max(abs(_LandmassGrassDetailWorldSize), 0.001);
    half3 terrainGrass =
        SAMPLE_TEXTURE2D(
            _LandmassGrassDetail,
            sampler_LandmassGrassDetail,
            detailUv).rgb;
    // The source cap has no separate diffuse map, while this project relies on
    // its authored Terrain grass for close-range material identity.  Preserve
    // the extracted two-colour macro tint but carry the same grass detail onto
    // modular caps so they no longer read as a flat, unrelated dark patch.
    return
        lerp(
            sourceGrass,
            terrainGrass,
            saturate(_LandmassGrassDetailStrength));
}

CMLLandmassSurface CMLSampleLandmassSurface(
    float3 positionWS,
    half3 geometricNormalWS)
{
    CMLLandmassSurface surface;
    geometricNormalWS = normalize(geometricNormalWS);
    surface.variation = CMLSampleLandmassVariation(positionWS);

    half3 rockAlbedo;
    half3 rockNormalWS;
    CMLSampleLandmassRock(
        positionWS,
        geometricNormalWS,
        rockAlbedo,
        rockNormalWS);

    surface.topMask =
        CMLLandmassTopMask(
            positionWS,
            geometricNormalWS,
            surface.variation);
    // The extracted material has no distinct brown soil-fringe parameter.
    // Keep the field for API compatibility, but do not create a second band.
    surface.soilMask = 0.0h;
    half3 grassAlbedo =
        CMLLandmassGrassColor(positionWS, surface.variation);
    surface.albedo =
        lerp(
            rockAlbedo,
            grassAlbedo,
            surface.topMask);
    surface.normalWS =
        normalize(
            lerp(
                rockNormalWS,
                geometricNormalWS,
                saturate(
                    (geometricNormalWS.y + _LandmassSlopeOffset) *
                    _LandmassSlopeHardness)));
    return surface;
}

#endif

#pragma once

#include "CoreMinimal.h"

#include "CMLImpactBurstGeometry.generated.h"

UENUM(BlueprintType)
enum class ECMLImpactSurface : uint8
{
    Stone = 0,
    Wood = 1
};

/** A generated particle mesh: positions in Unreal units, and its triangles. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLBurstMesh
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|VFX") TArray<FVector> Vertices;
    UPROPERTY(BlueprintReadOnly, Category="CML|VFX") TArray<int32> Triangles;

    int32 TriangleCount() const { return Triangles.Num() / 3; }
};

/**
 * The geometry behind the impact effects, ported from
 * CML.Unity.Presentation.Equipment.PickaxeImpactBurst.
 *
 * Every particle in this game is a three-dimensional mesh. Stone throws a dense
 * smoke puff and mineral chips; wood throws a lighter sawdust puff and long
 * tumbling splinters. **No billboard quad is used by either family**, and that
 * is the whole point of the effect: a camera-facing textured quad reads as a
 * different game. In Unreal these feed Niagara *mesh* emitters, never sprite
 * emitters.
 *
 * Only the shapes live here. Emission counts, lifetimes and tints are presented
 * beside them because they are part of the same look, but spawning is the
 * renderer's job.
 *
 * Positions are converted once, here: Unity's metres become Unreal units and
 * its axes are permuted (Unreal.X = Unity.z, Y = Unity.x, Z = Unity.y). That
 * permutation is cyclic, so it preserves handedness and the triangle winding
 * carries over untouched.
 */
class CMLCORE_API FCMLImpactBurstGeometry
{
public:
    /** Rings and segments of the puff; the shape's whole resolution. */
    static constexpr int32 SmokeLatitudeSegments = 7;
    static constexpr int32 SmokeLongitudeSegments = 12;

    static constexpr int32 StoneSmokeCount = 9;
    static constexpr int32 WoodSmokeCount = 5;
    static constexpr int32 StoneFragmentCount = 7;
    static constexpr int32 WoodSplinterCount = 9;

    /** How far off the surface the effect sits, so it does not z-fight it. */
    static constexpr float SurfaceOffsetUnrealUnits = 1.8f;
    static constexpr float EffectLifetimeSeconds = 1.45f;

    static const FLinearColor& StoneSmokeTint();
    static const FLinearColor& WoodDustTint();
    static const FLinearColor& StoneFragmentTint();
    static const FLinearColor& WoodSplinterTint();

    /**
     * The irregular puff, shared with any other stylized smoke in the game.
     * One shape to change rather than a copy per effect — two copies would
     * drift and the game would have two kinds of smoke.
     */
    static const FCMLBurstMesh& SmokePuff();

    static const FCMLBurstMesh& StoneFragment();
    static const FCMLBurstMesh& WoodSplinter();

    /** How many puffs and chips a surface throws. */
    static int32 SmokeCountFor(ECMLImpactSurface Surface);
    static int32 FragmentCountFor(ECMLImpactSurface Surface);
    static const FCMLBurstMesh& FragmentMeshFor(ECMLImpactSurface Surface);
    static const FLinearColor& SmokeTintFor(ECMLImpactSurface Surface);
    static const FLinearColor& FragmentTintFor(ECMLImpactSurface Surface);
};

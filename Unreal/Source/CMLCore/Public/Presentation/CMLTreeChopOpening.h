#pragma once

#include "CoreMinimal.h"

#include "CMLTreeChopOpening.generated.h"

/** The notch cut into a trunk, at one stage of being chopped. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLTreeChopOpening
{
    GENERATED_BODY()

    /** Across the trunk, in metres. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Wood") float Width = 0.0f;

    /** Up the trunk. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Wood") float Height = 0.0f;

    /** Into it. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Wood") float Depth = 0.0f;
};

/**
 * How the notch grows, ported from
 * CML.Unity.Wood.TreeChopVoxelCarver.ResolveOpeningSize.
 *
 * The whole point is that the scar is sized against the trunk it is cut into,
 * not against a constant. A notch scaled for a mature trunk would cut a sapling
 * clean in half, and the depth cap is what stops a few blows going straight
 * through a thin one.
 *
 * A trunk whose section could not be measured falls back to a fixed size rather
 * than to the largest one: guessing small leaves an ugly notch, guessing large
 * severs the tree.
 */
class CMLCORE_API FCMLTreeChop
{
public:
    /** How many blows fell a tree; stage 1 is the first. */
    static constexpr int32 HitsRequired = 5;

    /** Ceilings for a mature trunk. */
    static constexpr float TargetMaximumWidth = 0.200f;
    static constexpr float TargetMaximumHeight = 0.280f;

    /** Used when the trunk's section could not be measured. */
    static constexpr float UnmeasuredWidth = 0.120f;
    static constexpr float UnmeasuredHeight = 0.170f;

    /** Floors, so even a first blow leaves a notch worth looking at. */
    static constexpr float MinimumOpeningWidth = 0.060f;
    static constexpr float MinimumOpeningHeight = 0.085f;

    /** Every visible opening is broadened by this without deepening. */
    static constexpr float FootprintScale = 1.20f;

    /** Below this the section is treated as unmeasured. */
    static constexpr float MeasuredSectionThreshold = 0.015f;

    /**
     * The notch at a given stage.
     *
     * `SectionWidth` is how thick the trunk is where the blow landed; pass zero
     * when it is not known.
     */
    static FCMLTreeChopOpening ResolveOpening(float SectionWidth, int32 Stage);
};

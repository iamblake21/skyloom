#pragma once

#include "CoreMinimal.h"

/**
 * The puff of dust every breakable component shares, ported from
 * CML.Unity.Factory.FactoryDustBurst.
 *
 * This effect is **soft billboards on purpose**, and that is not an oversight to
 * be "corrected" into meshes. It is dust, not debris: the particles are slowed
 * by drag rather than thrown ballistically, and they spread and fade instead of
 * falling. Its sibling, `FCMLImpactBurstGeometry`, is the opposite — solid
 * meshes and no quad anywhere — because chips and splinters are objects. Both
 * choices are deliberate and neither should be made to match the other.
 *
 * The sprite is generated rather than imported, so the effect carries no asset,
 * no meta and no reference: nothing to wire up and nothing that can go missing
 * from a build.
 */
class CMLCORE_API FCMLDustBurst
{
public:
    static constexpr int32 ParticleCount = 20;
    static constexpr float LifetimeSeconds = 1.45f;
    static constexpr int32 SpriteSize = 64;

    /** How far the tint is pulled towards white before it is desaturated. */
    static constexpr float DefaultWhiteness = 0.65f;

    /**
     * The soft round sprite: RGBA8, white with a smoothstep alpha falloff.
     *
     * A hard disc reads as a bubble. The falloff is what lets overlapping
     * particles merge into one cloud instead of stacking as visible discs.
     */
    static const TArray<uint8>& SpritePixels();

    /**
     * Dust is the pale, desaturated ghost of whatever it came off, never the
     * material colour itself: full-strength paint reads as confetti.
     */
    static FLinearColor DustTint(const FLinearColor& Source, float Whiteness);
};

#pragma once

#include "CoreMinimal.h"

/**
 * Where a felled tree pivots and when it lets go, ported from
 * CML.Unity.Wood.TreeFellingFactory.
 *
 * A tree does not topple about its own centre. It hinges on the edge of its
 * stump nearest the direction it is going, and it only starts to fall once its
 * weight has passed over that edge. Getting the hinge wrong is what makes a
 * felled tree pivot in mid-air or sink through its own base.
 *
 * The geometry is space-agnostic: the caller supplies "up" and the trunk's
 * vertices in whatever space it works in, and gets a pivot back in the same one.
 */
class CMLCORE_API FCMLTreeFellGeometry
{
public:
    /** It leans a little past balance before it goes, never less, never more. */
    static constexpr double ReleaseBeyondBalanceDegrees = 2.25;
    static constexpr double MinimumReleaseAngleDegrees = 4.0;
    static constexpr double MaximumReleaseAngleDegrees = 14.0;

    /**
     * Which way it falls, in order of preference: the way the last blow was
     * swung, else away from the face that was struck, else the tree's own
     * forward. Each is projected flat, so a downward chop does not bury it.
     */
    static bool TryResolveFallDirection(
        const FVector& TreeUp,
        const FVector& FinalStrikeDirection,
        const FVector& FinalHitNormal,
        const FVector& TreeForward,
        FVector& OutFallDirection);

    /**
     * The hinge: the leading edge of the stump.
     *
     * Only the bottom slice of the trunk is considered, because the hinge is
     * where the wood meets the ground and not where the crown happens to lean.
     * The pivot takes the furthest point along the fall from that slice, and the
     * slice's average sideways position so the tree does not twist as it goes.
     */
    static bool TryResolveLeadingBasePivot(
        TArrayView<const FVector> TrunkVerticesWorld,
        const FVector& TreeUp,
        const FVector& FallDirection,
        FVector& OutPivot,
        double& OutTrunkHeight);

    /**
     * How far it leans before gravity takes it.
     *
     * The balance angle is where the centre of mass sits directly over the
     * hinge; the tree is released a little past that, and clamped so a
     * top-heavy or an oddly shaped tree still behaves.
     */
    static double ResolveReleaseAngleDegrees(
        const FVector& ApproximateCentreOfMass,
        const FVector& Pivot,
        const FVector& TreeUp,
        const FVector& FallDirection);
};

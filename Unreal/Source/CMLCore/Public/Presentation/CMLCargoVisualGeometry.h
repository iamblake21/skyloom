#pragma once

#include "CoreMinimal.h"

/**
 * Where a piece of visible cargo actually sits, ported from
 * CML.Unity.Factory.FactoryCargoVisualGeometry.
 *
 * A cargo pivot is not a contact point. Items are authored with pivots wherever
 * was convenient, so placing them by pivot leaves a small item floating and a
 * large one sunk into the belt. Everything here works from rendered bounds
 * instead, which is what lets differently sized items share one physical
 * support plane.
 *
 * The maths is kept apart from the scene graph so it can be checked without a
 * world: callers gather each renderer's local bounds and transform, and this
 * projects them.
 */
class CMLCORE_API FCMLCargoVisualGeometry
{
public:
    /**
     * Top of the authored cross-battens on the straight belt and the drive
     * unit. The canvas surface is 60 cm; visible cargo has to rest on the 1.4 cm
     * battens rather than intersect them.
     */
    static constexpr double BeltKitSupportHeightUnrealUnits = 61.4;

    /** One renderer's box, as authored, with the transform that places it. */
    struct FBoundsInstance
    {
        FBox LocalBounds = FBox(ForceInit);
        FTransform WorldTransform = FTransform::Identity;
    };

    /**
     * The extent of every corner of every box, projected onto one axis.
     *
     * All eight corners are projected rather than just the box centre, because a
     * rotated box reaches further along an axis than its centre suggests.
     */
    static bool TryGetWorldProjection(
        TArrayView<const FBoundsInstance> Instances,
        const FVector& WorldAxis,
        double& OutMinimum,
        double& OutMaximum);

    /**
     * How far to move the cargo so its lowest point rests exactly on a plane.
     *
     * Returns the translation rather than applying it: the caller owns the
     * scene, and a geometry helper that moved actors could not be tested
     * without one.
     */
    static bool TryGetAlignmentToPlane(
        TArrayView<const FBoundsInstance> Instances,
        const FVector& PlanePoint,
        const FVector& PlaneNormal,
        double& OutTranslation);

    /** Signed gap between the cargo's nearest point and a plane. */
    static bool TryGetMinimumClearance(
        TArrayView<const FBoundsInstance> Instances,
        const FVector& PlanePoint,
        const FVector& OutwardNormal,
        double& OutClearance);
};

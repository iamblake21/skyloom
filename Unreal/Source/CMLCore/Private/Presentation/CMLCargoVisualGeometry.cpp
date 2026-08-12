#include "Presentation/CMLCargoVisualGeometry.h"

namespace
{
    /** The same epsilon the C# used, so a degenerate axis is refused alike. */
    constexpr double AxisEpsilonSquared = 0.000001;
}

bool FCMLCargoVisualGeometry::TryGetWorldProjection(
    const TArrayView<const FBoundsInstance> Instances,
    const FVector& WorldAxis,
    double& OutMinimum,
    double& OutMaximum)
{
    OutMinimum = 0.0;
    OutMaximum = 0.0;
    if (WorldAxis.SizeSquared() <= AxisEpsilonSquared)
    {
        return false;
    }

    const FVector Axis = WorldAxis.GetSafeNormal();
    bool bFound = false;
    for (const FBoundsInstance& Instance : Instances)
    {
        if (!Instance.LocalBounds.IsValid)
        {
            continue;
        }
        const FVector Centre = Instance.LocalBounds.GetCenter();
        const FVector Extents = Instance.LocalBounds.GetExtent();

        for (int32 Corner = 0; Corner < 8; ++Corner)
        {
            // All eight corners: a rotated box reaches further along an axis
            // than its centre suggests.
            const FVector LocalCorner = Centre + FVector(
                (Corner & 1) == 0 ? -Extents.X : Extents.X,
                (Corner & 2) == 0 ? -Extents.Y : Extents.Y,
                (Corner & 4) == 0 ? -Extents.Z : Extents.Z);
            const double Projection =
                FVector::DotProduct(Instance.WorldTransform.TransformPosition(LocalCorner), Axis);
            if (!bFound)
            {
                OutMinimum = Projection;
                OutMaximum = Projection;
                bFound = true;
                continue;
            }
            OutMinimum = FMath::Min(OutMinimum, Projection);
            OutMaximum = FMath::Max(OutMaximum, Projection);
        }
    }
    return bFound;
}

bool FCMLCargoVisualGeometry::TryGetAlignmentToPlane(
    const TArrayView<const FBoundsInstance> Instances,
    const FVector& PlanePoint,
    const FVector& PlaneNormal,
    double& OutTranslation)
{
    OutTranslation = 0.0;
    double Minimum = 0.0;
    double Maximum = 0.0;
    if (PlaneNormal.SizeSquared() <= AxisEpsilonSquared
        || !TryGetWorldProjection(Instances, PlaneNormal, Minimum, Maximum))
    {
        return false;
    }
    const FVector Normal = PlaneNormal.GetSafeNormal();
    OutTranslation = FVector::DotProduct(PlanePoint, Normal) - Minimum;
    return true;
}

bool FCMLCargoVisualGeometry::TryGetMinimumClearance(
    const TArrayView<const FBoundsInstance> Instances,
    const FVector& PlanePoint,
    const FVector& OutwardNormal,
    double& OutClearance)
{
    OutClearance = 0.0;
    double Minimum = 0.0;
    double Maximum = 0.0;
    if (OutwardNormal.SizeSquared() <= AxisEpsilonSquared
        || !TryGetWorldProjection(Instances, OutwardNormal, Minimum, Maximum))
    {
        return false;
    }
    const FVector Normal = OutwardNormal.GetSafeNormal();
    OutClearance = Minimum - FVector::DotProduct(PlanePoint, Normal);
    return true;
}

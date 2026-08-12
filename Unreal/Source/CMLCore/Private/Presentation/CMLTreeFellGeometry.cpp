#include "Presentation/CMLTreeFellGeometry.h"

namespace
{
    /** The same threshold the C# used, so a near-vertical strike is refused alike. */
    constexpr double FlatEnoughSquared = 0.0001;

    /**
     * Unity's Vector3.ProjectOnPlane: the part of a vector that lies in the
     * plane. A chop swung downwards has almost nothing left after this, which is
     * exactly why there are fallbacks.
     */
    FVector ProjectOnPlane(const FVector& Vector, const FVector& PlaneNormal)
    {
        const double LengthSquared = PlaneNormal.SizeSquared();
        if (LengthSquared <= UE_DOUBLE_SMALL_NUMBER)
        {
            return Vector;
        }
        return Vector - PlaneNormal * (FVector::DotProduct(Vector, PlaneNormal) / LengthSquared);
    }
}

bool FCMLTreeFellGeometry::TryResolveFallDirection(
    const FVector& TreeUp,
    const FVector& FinalStrikeDirection,
    const FVector& FinalHitNormal,
    const FVector& TreeForward,
    FVector& OutFallDirection)
{
    OutFallDirection = FVector::ZeroVector;
    if (TreeUp.SizeSquared() <= UE_DOUBLE_SMALL_NUMBER)
    {
        return false;
    }

    // In order: the way the blow was swung, away from the face that was struck,
    // then the tree's own forward as a last resort.
    const FVector Candidates[3] = {
        ProjectOnPlane(FinalStrikeDirection, TreeUp),
        ProjectOnPlane(-FinalHitNormal, TreeUp),
        ProjectOnPlane(TreeForward, TreeUp)};

    for (const FVector& Candidate : Candidates)
    {
        if (Candidate.SizeSquared() >= FlatEnoughSquared)
        {
            OutFallDirection = Candidate.GetSafeNormal();
            return true;
        }
    }
    return false;
}

bool FCMLTreeFellGeometry::TryResolveLeadingBasePivot(
    const TArrayView<const FVector> TrunkVerticesWorld,
    const FVector& TreeUp,
    const FVector& FallDirection,
    FVector& OutPivot,
    double& OutTrunkHeight)
{
    OutPivot = FVector::ZeroVector;
    OutTrunkHeight = 0.0;
    if (TrunkVerticesWorld.IsEmpty())
    {
        return false;
    }

    double MinimumHeight = TNumericLimits<double>::Max();
    double MaximumHeight = TNumericLimits<double>::Lowest();
    for (const FVector& Vertex : TrunkVerticesWorld)
    {
        const double Height = FVector::DotProduct(Vertex, TreeUp);
        MinimumHeight = FMath::Min(MinimumHeight, Height);
        MaximumHeight = FMath::Max(MaximumHeight, Height);
    }

    OutTrunkHeight = MaximumHeight - MinimumHeight;
    if (!FMath::IsFinite(OutTrunkHeight) || OutTrunkHeight <= 0.1)
    {
        return false;
    }

    // Only the bottom slice: the hinge is where the wood meets the ground, not
    // where the crown happens to lean. Six per cent of the trunk, with a floor
    // so a short stump still has a slice to work with.
    const double SliceTop = MinimumHeight + FMath::Max(8.0, OutTrunkHeight * 0.06);
    const FVector Lateral = FVector::CrossProduct(TreeUp, FallDirection).GetSafeNormal();

    double Leading = TNumericLimits<double>::Lowest();
    double LateralSum = 0.0;
    int32 Selected = 0;
    for (const FVector& Vertex : TrunkVerticesWorld)
    {
        if (FVector::DotProduct(Vertex, TreeUp) > SliceTop)
        {
            continue;
        }
        Leading = FMath::Max(Leading, FVector::DotProduct(Vertex, FallDirection));
        LateralSum += FVector::DotProduct(Vertex, Lateral);
        ++Selected;
    }

    if (Selected == 0 || !FMath::IsFinite(Leading))
    {
        return false;
    }

    // The furthest point along the fall, at the foot of the trunk, averaged
    // sideways so the tree does not twist as it goes over.
    OutPivot = TreeUp * MinimumHeight
        + FallDirection * Leading
        + Lateral * (LateralSum / Selected);
    return true;
}

double FCMLTreeFellGeometry::ResolveReleaseAngleDegrees(
    const FVector& ApproximateCentreOfMass,
    const FVector& Pivot,
    const FVector& TreeUp,
    const FVector& FallDirection)
{
    const FVector Lever = ApproximateCentreOfMass - Pivot;
    // Floored so a centre of mass at or below the hinge cannot divide by zero
    // and send the angle to a right angle.
    const double Height = FMath::Max(1.0, FVector::DotProduct(Lever, TreeUp));
    // Only the part still behind the hinge counts: weight already past it is
    // not holding the tree up.
    const double BehindPivot =
        FMath::Max(0.0, -FVector::DotProduct(Lever, FallDirection));
    const double BalanceAngle = FMath::RadiansToDegrees(FMath::Atan2(BehindPivot, Height));
    return FMath::Clamp(
        BalanceAngle + ReleaseBeyondBalanceDegrees,
        MinimumReleaseAngleDegrees,
        MaximumReleaseAngleDegrees);
}

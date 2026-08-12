#include "Presentation/CMLTreeFellGeometry.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Geometry = FCMLTreeFellGeometry;

    /** A trunk 600 units tall and 40 wide, standing on the origin. */
    TArray<FVector> MakeTrunk()
    {
        TArray<FVector> Vertices;
        for (int32 Ring = 0; Ring <= 6; ++Ring)
        {
            const double Height = Ring * 100.0;
            for (int32 Step = 0; Step < 8; ++Step)
            {
                const double Angle = 2.0 * UE_DOUBLE_PI * Step / 8.0;
                Vertices.Add(FVector(
                    FMath::Cos(Angle) * 20.0, FMath::Sin(Angle) * 20.0, Height));
            }
        }
        return Vertices;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLTreeFellGeometryTest,
    "CML.Core.Presentation.TreeFellGeometry",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLTreeFellGeometryTest::RunTest(const FString& Parameters)
{
    const FVector Up = FVector::UpVector;
    FVector Fall = FVector::ZeroVector;

    // The swing decides, and it is flattened: a downward chop must not bury the
    // tree in the ground.
    {
        TestTrue(TEXT("A level swing resolves"),
            Geometry::TryResolveFallDirection(
                Up, FVector(1, 0, 0), FVector(-1, 0, 0), FVector(0, 1, 0), Fall));
        TestTrue(TEXT("It falls the way it was swung"),
            Fall.Equals(FVector(1, 0, 0), 1e-6));

        TestTrue(TEXT("A steep swing still resolves"),
            Geometry::TryResolveFallDirection(
                Up, FVector(0.2, 0, -0.98), FVector(-1, 0, 0), FVector(0, 1, 0), Fall));
        TestEqual(TEXT("And is flattened"), Fall.Z, 0.0, 1e-6);
        TestTrue(TEXT("Keeping its heading"), Fall.Equals(FVector(1, 0, 0), 1e-6));
    }

    // Straight down: nothing is left after flattening, so it falls away from the
    // face that was struck instead.
    {
        TestTrue(TEXT("A vertical chop falls back on the hit normal"),
            Geometry::TryResolveFallDirection(
                Up, FVector(0, 0, -1), FVector(0, -1, 0), FVector(1, 0, 0), Fall));
        TestTrue(TEXT("Away from the struck face"), Fall.Equals(FVector(0, 1, 0), 1e-6));

        // And with neither, the tree's own forward.
        TestTrue(TEXT("Last resort is the tree's forward"),
            Geometry::TryResolveFallDirection(
                Up, FVector(0, 0, -1), FVector(0, 0, 1), FVector(1, 0, 0), Fall));
        TestTrue(TEXT("Which is used as given"), Fall.Equals(FVector(1, 0, 0), 1e-6));

        TestFalse(TEXT("With nothing usable at all it refuses"),
            Geometry::TryResolveFallDirection(
                Up, FVector::ZeroVector, FVector(0, 0, 1), FVector(0, 0, 1), Fall));
    }

    // The hinge is the leading edge of the stump, not the trunk's centre.
    {
        const TArray<FVector> Trunk = MakeTrunk();
        Geometry::TryResolveFallDirection(
            Up, FVector(1, 0, 0), FVector(-1, 0, 0), FVector(0, 1, 0), Fall);

        FVector Pivot = FVector::ZeroVector;
        double Height = 0.0;
        TestTrue(TEXT("The pivot resolves"),
            Geometry::TryResolveLeadingBasePivot(Trunk, Up, Fall, Pivot, Height));
        TestEqual(TEXT("The trunk's height is measured"), Height, 600.0, 1e-6);
        TestEqual(TEXT("The hinge is at the foot"), Pivot.Z, 0.0, 1e-6);
        TestEqual(TEXT("On the leading edge, not the centre"), Pivot.X, 20.0, 1e-6);
        TestEqual(TEXT("And centred sideways so it does not twist"), Pivot.Y, 0.0, 1e-6);

        // Turn the fall around and the hinge moves to the other edge.
        FVector Backwards = FVector::ZeroVector;
        Geometry::TryResolveFallDirection(
            Up, FVector(-1, 0, 0), FVector(1, 0, 0), FVector(0, 1, 0), Backwards);
        TestTrue(TEXT("The pivot resolves the other way too"),
            Geometry::TryResolveLeadingBasePivot(Trunk, Up, Backwards, Pivot, Height));
        TestEqual(TEXT("The hinge followed the fall"), Pivot.X, -20.0, 1e-6);

        TestFalse(TEXT("A trunk with no vertices is refused"),
            Geometry::TryResolveLeadingBasePivot(
                TArrayView<const FVector>(), Up, Fall, Pivot, Height));

        TArray<FVector> Flat;
        Flat.Add(FVector(0, 0, 0));
        Flat.Add(FVector(10, 0, 0));
        TestFalse(TEXT("A trunk with no height is refused"),
            Geometry::TryResolveLeadingBasePivot(Flat, Up, Fall, Pivot, Height));
    }

    // The release angle is where the weight passes over the hinge, plus a little.
    {
        const FVector Pivot(20, 0, 0);
        const FVector Fall2(1, 0, 0);

        // A centre of mass right over the hinge is already balanced, so it goes
        // at the minimum lean rather than waiting.
        const double Balanced = Geometry::ResolveReleaseAngleDegrees(
            FVector(20, 0, 300), Pivot, Up, Fall2);
        TestEqual(TEXT("A balanced tree goes at the minimum"),
            Balanced, Geometry::MinimumReleaseAngleDegrees, 1e-6);

        // Weight still behind the hinge has to be leaned past first.
        const double LeaningBack = Geometry::ResolveReleaseAngleDegrees(
            FVector(0, 0, 300), Pivot, Up, Fall2);
        TestTrue(TEXT("Weight behind the hinge needs more lean"),
            LeaningBack > Balanced);
        TestTrue(TEXT("But never more than the maximum"),
            LeaningBack <= Geometry::MaximumReleaseAngleDegrees + 1e-9);

        // A tree leaning far back is clamped rather than never letting go.
        const double FarBack = Geometry::ResolveReleaseAngleDegrees(
            FVector(-500, 0, 100), Pivot, Up, Fall2);
        TestEqual(TEXT("An extreme lean is clamped"),
            FarBack, Geometry::MaximumReleaseAngleDegrees, 1e-6);

        // Weight already past the hinge does not hold the tree up, so it is not
        // counted as resistance.
        const double PastIt = Geometry::ResolveReleaseAngleDegrees(
            FVector(400, 0, 300), Pivot, Up, Fall2);
        TestEqual(TEXT("Weight past the hinge adds no resistance"),
            PastIt, Geometry::MinimumReleaseAngleDegrees, 1e-6);

        // A centre of mass at or below the hinge must not send the angle to a
        // right angle through a division by zero.
        const double Underground = Geometry::ResolveReleaseAngleDegrees(
            FVector(0, 0, -50), Pivot, Up, Fall2);
        TestTrue(TEXT("A degenerate lever stays within the clamp"),
            Underground >= Geometry::MinimumReleaseAngleDegrees - 1e-9
                && Underground <= Geometry::MaximumReleaseAngleDegrees + 1e-9);
    }
    return true;
}
#endif

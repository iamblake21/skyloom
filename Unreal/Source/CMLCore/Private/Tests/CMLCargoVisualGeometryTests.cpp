#include "Presentation/CMLCargoVisualGeometry.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Geometry = FCMLCargoVisualGeometry;

    /** A cube of the given half-size, with its pivot deliberately off-centre. */
    Geometry::FBoundsInstance Cube(
        const double HalfSize, const FVector& Location, const FVector& PivotOffset = FVector::ZeroVector)
    {
        Geometry::FBoundsInstance Instance;
        Instance.LocalBounds = FBox(
            PivotOffset - FVector(HalfSize), PivotOffset + FVector(HalfSize));
        Instance.WorldTransform = FTransform(Location);
        return Instance;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCargoVisualGeometryTest,
    "CML.Core.Presentation.CargoVisualGeometry",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCargoVisualGeometryTest::RunTest(const FString& Parameters)
{
    double Minimum = 0.0;
    double Maximum = 0.0;

    // The projection spans the box, not its centre.
    {
        const Geometry::FBoundsInstance Instances[] = {Cube(10.0, FVector(0, 0, 100))};
        TestTrue(TEXT("A box projects"),
            Geometry::TryGetWorldProjection(Instances, FVector::UpVector, Minimum, Maximum));
        TestEqual(TEXT("Its bottom is ten below its centre"), Minimum, 90.0, 1e-6);
        TestEqual(TEXT("Its top is ten above"), Maximum, 110.0, 1e-6);
    }

    // A rotated box reaches further along an axis than its centre suggests,
    // which is why all eight corners are projected rather than the centre.
    {
        Geometry::FBoundsInstance Rotated = Cube(10.0, FVector(0, 0, 100));
        Rotated.WorldTransform = FTransform(
            FRotator(45.0, 0.0, 0.0), FVector(0, 0, 100));
        TestTrue(TEXT("A rotated box projects"),
            Geometry::TryGetWorldProjection(
                MakeArrayView(&Rotated, 1), FVector::UpVector, Minimum, Maximum));
        TestTrue(TEXT("It reaches below the unrotated extent"), Minimum < 90.0 - 1e-6);
    }

    // The whole point: two items of different sizes, with pivots in different
    // places, land on the same support plane.
    {
        const Geometry::FBoundsInstance Small[] =
            {Cube(4.0, FVector(0, 0, 0), FVector(0, 0, 4))};
        const Geometry::FBoundsInstance Large[] =
            {Cube(12.0, FVector(0, 0, 0), FVector(0, 0, -30))};

        const FVector Plane(0, 0, Geometry::BeltKitSupportHeightUnrealUnits);
        double SmallLift = 0.0;
        double LargeLift = 0.0;
        TestTrue(TEXT("The small item aligns"),
            Geometry::TryGetAlignmentToPlane(Small, Plane, FVector::UpVector, SmallLift));
        TestTrue(TEXT("The large item aligns"),
            Geometry::TryGetAlignmentToPlane(Large, Plane, FVector::UpVector, LargeLift));

        // Apply each translation and check both now rest on the plane.
        auto Lifted = [](Geometry::FBoundsInstance Instance, const double Lift)
        {
            Instance.WorldTransform.AddToTranslation(FVector(0, 0, Lift));
            return Instance;
        };
        const Geometry::FBoundsInstance SmallPlaced[] = {Lifted(Small[0], SmallLift)};
        const Geometry::FBoundsInstance LargePlaced[] = {Lifted(Large[0], LargeLift)};

        Geometry::TryGetWorldProjection(SmallPlaced, FVector::UpVector, Minimum, Maximum);
        TestEqual(TEXT("The small item rests on the battens"),
            Minimum, Geometry::BeltKitSupportHeightUnrealUnits, 1e-6);
        Geometry::TryGetWorldProjection(LargePlaced, FVector::UpVector, Minimum, Maximum);
        TestEqual(TEXT("And so does the large one"),
            Minimum, Geometry::BeltKitSupportHeightUnrealUnits, 1e-6);
        TestTrue(TEXT("Their pivots did not end up in the same place"),
            !FMath::IsNearlyEqual(SmallLift, LargeLift));
    }

    // Clearance is signed, so an item sunk into the belt reads negative rather
    // than merely "zero clearance".
    {
        const Geometry::FBoundsInstance Sunk[] = {Cube(10.0, FVector(0, 0, 55))};
        double Clearance = 0.0;
        TestTrue(TEXT("Clearance resolves"),
            Geometry::TryGetMinimumClearance(
                Sunk, FVector(0, 0, Geometry::BeltKitSupportHeightUnrealUnits),
                FVector::UpVector, Clearance));
        TestTrue(TEXT("An intersecting item reads negative"), Clearance < 0.0);
    }

    // Degenerate input is refused rather than answered with a default.
    {
        const Geometry::FBoundsInstance Instances[] = {Cube(10.0, FVector::ZeroVector)};
        double Ignored = 0.0;
        TestFalse(TEXT("A zero axis is refused"),
            Geometry::TryGetWorldProjection(Instances, FVector::ZeroVector, Minimum, Maximum));
        TestFalse(TEXT("So is a zero plane normal"),
            Geometry::TryGetAlignmentToPlane(
                Instances, FVector::ZeroVector, FVector::ZeroVector, Ignored));
        TestFalse(TEXT("And nothing to measure is refused"),
            Geometry::TryGetWorldProjection(
                TArrayView<const Geometry::FBoundsInstance>(), FVector::UpVector,
                Minimum, Maximum));
    }
    return true;
}
#endif

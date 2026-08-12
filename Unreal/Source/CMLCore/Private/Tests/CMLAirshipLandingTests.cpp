#include "Simulation/CMLAirshipLanding.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId SurfaceId(0, 5);

    /** A generous pad centred where the ramp points at zero yaw. */
    FCMLAirshipSimulationState MakeState(
        const int64 HalfWidth = 5000,
        const int64 HalfDepth = 5000,
        const int64 CentreY = 300)
    {
        FCMLAirshipSimulationState State;
        FCMLAirshipLandingSurface Surface;
        Surface.Id = SurfaceId;
        Surface.Center.X = 0;
        Surface.Center.Y = CentreY;
        Surface.Center.Z = 4000;
        Surface.YawTurn = 0;
        Surface.HalfWidthMillimetres = HalfWidth;
        Surface.HalfDepthMillimetres = HalfDepth;
        State.LandingSurfaces.Add(Surface);
        return State;
    }

    FCMLAirshipPose MakePose()
    {
        FCMLAirshipPose Pose;
        Pose.Position.X = 0;
        Pose.Position.Y = 0;
        Pose.Position.Z = 0;
        Pose.YawTurn = 0;
        return Pose;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLAirshipLandingTest,
    "CML.Core.Simulation.AirshipLanding",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLAirshipLandingTest::RunTest(const FString& Parameters)
{
    const FCMLAirshipPose Pose = MakePose();

    // The ramp tip sits in front of the hull, not at its origin.
    {
        const FCMLAirshipVector Ramp = FCMLAirshipLanding::GetRampWorld(Pose);
        TestEqual(TEXT("The ramp carries its local height"),
            Ramp.Y, static_cast<int64>(FCMLAirshipLanding::RampTipLocalYMillimetres));
        TestTrue(TEXT("The ramp is ahead of the hull"), Ramp.Z != 0 || Ramp.X != 0);
    }

    {
        const FCMLAirshipSimulationState State = MakeState();
        int32 Reach = 0;
        TestTrue(TEXT("A wide flat pad is reachable"),
            FCMLAirshipLanding::TryFindReach(State, Pose, SurfaceId, Reach));
        // The search runs outwards, so the nearest legal reach wins.
        TestEqual(TEXT("The nearest reach is accepted"),
            Reach, FCMLAirshipLanding::MinimumReachMillimetres);

        FCMLAirshipVector Disembark;
        TestTrue(TEXT("A disembark point exists"),
            FCMLAirshipLanding::TryGetDisembarkPoint(State, Pose, SurfaceId, Disembark));
        TestEqual(TEXT("The player lands on the surface height"),
            Disembark.Y, static_cast<int64>(300));
    }

    {
        const FCMLAirshipSimulationState State = MakeState();
        TestFalse(TEXT("An unknown surface is not reachable"),
            FCMLAirshipLanding::IsReachable(State, Pose, FCMLStableId(0, 99)));
    }

    // Too tall a step: the ramp would not meet the surface.
    {
        const FCMLAirshipSimulationState TooHigh =
            MakeState(5000, 5000, FCMLAirshipLanding::RampTipLocalYMillimetres
                + FCMLAirshipLanding::MaximumHeightDeltaMillimetres + 1);
        TestFalse(TEXT("A step beyond the height limit is refused"),
            FCMLAirshipLanding::IsReachable(TooHigh, Pose, SurfaceId));

        const FCMLAirshipSimulationState JustInside =
            MakeState(5000, 5000, FCMLAirshipLanding::RampTipLocalYMillimetres
                + FCMLAirshipLanding::MaximumHeightDeltaMillimetres);
        TestTrue(TEXT("A step at the limit is accepted"),
            FCMLAirshipLanding::IsReachable(JustInside, Pose, SurfaceId));
    }

    // The pad has to be continuous. A surface narrower than the required
    // corridor is refused even though its centre is perfectly good — sampling
    // only the middle would accept a pad a player could step off the side of.
    {
        const int64 NarrowHalfWidth =
            (FCMLAirshipLanding::RequiredCorridorWidthMillimetres / 2) - 100;
        const FCMLAirshipSimulationState Narrow = MakeState(NarrowHalfWidth, 5000);
        TestFalse(TEXT("A pad narrower than the corridor is refused"),
            FCMLAirshipLanding::IsReachable(Narrow, Pose, SurfaceId));

        // The centre point alone would have passed.
        FCMLAirshipLandingSurface Surface = Narrow.LandingSurfaces[0];
        const FCMLAirshipVector Ramp = FCMLAirshipLanding::GetRampWorld(Pose);
        TestTrue(TEXT("The pad centre itself is on the surface"),
            FCMLAirshipLanding::SurfaceContainsPoint(Surface, Surface.Center));
    }

    // An obstacle standing in the corridor blocks the landing even when the pad
    // beyond it is perfect.
    {
        FCMLAirshipSimulationState Blocked = MakeState();
        const FCMLAirshipVector Ramp = FCMLAirshipLanding::GetRampWorld(Pose);
        FCMLAirshipObstacle Wall;
        Wall.Id = FCMLStableId(0, 6);
        Wall.Minimum.X = Ramp.X - 2000;
        Wall.Minimum.Y = Ramp.Y - 2000;
        Wall.Minimum.Z = Ramp.Z - 2000;
        Wall.Maximum.X = Ramp.X + 2000;
        Wall.Maximum.Y = Ramp.Y + 2000;
        Wall.Maximum.Z = Ramp.Z + 2000;
        Blocked.Obstacles.Add(Wall);

        TestFalse(TEXT("An obstacle in the corridor blocks the landing"),
            FCMLAirshipLanding::IsReachable(Blocked, Pose, SurfaceId));
    }

    // Surface containment respects the surface's own yaw, so a rotated pad is
    // tested in its own frame rather than as an axis-aligned box.
    {
        FCMLAirshipLandingSurface Surface;
        Surface.Id = SurfaceId;
        Surface.Center = FCMLAirshipVector{0, 0, 0};
        Surface.YawTurn = 0;
        Surface.HalfWidthMillimetres = 1000;
        Surface.HalfDepthMillimetres = 100;

        const FCMLAirshipVector AlongWidth{0, 0, 900};
        const FCMLAirshipVector AlongDepth{900, 0, 0};
        TestTrue(TEXT("A point along the wide axis is inside"),
            FCMLAirshipLanding::SurfaceContainsPoint(Surface, AlongWidth));
        TestFalse(TEXT("The same distance along the shallow axis is outside"),
            FCMLAirshipLanding::SurfaceContainsPoint(Surface, AlongDepth));

        // Rotating the surface a quarter turn swaps which axis is generous.
        Surface.YawTurn = 16384;
        TestTrue(TEXT("A quarter turn swaps the generous axis"),
            FCMLAirshipLanding::SurfaceContainsPoint(Surface, AlongDepth));
    }
    return true;
}
#endif

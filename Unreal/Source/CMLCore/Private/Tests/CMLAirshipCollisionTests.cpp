#include "Simulation/CMLAirshipCollision.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    FCMLAirshipPose MakePose(const int64 X, const int64 Z, const int32 Yaw = 0)
    {
        FCMLAirshipPose Pose;
        Pose.Position.X = X;
        Pose.Position.Y = 0;
        Pose.Position.Z = Z;
        Pose.YawTurn = Yaw;
        return Pose;
    }

    /** A thin wall standing across the path at X. */
    FCMLAirshipSimulationState MakeWall(const int64 X, const int64 Thickness = 100)
    {
        FCMLAirshipSimulationState State;
        FCMLAirshipObstacle Wall;
        Wall.Id = FCMLStableId(0, 1);
        Wall.Minimum.X = X - Thickness / 2;
        Wall.Minimum.Y = -10000;
        Wall.Minimum.Z = -10000;
        Wall.Maximum.X = X + Thickness / 2;
        Wall.Maximum.Y = 10000;
        Wall.Maximum.Z = 10000;
        State.Obstacles.Add(Wall);
        return State;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLAirshipCollisionTest,
    "CML.Core.Simulation.AirshipCollision",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLAirshipCollisionTest::RunTest(const FString& Parameters)
{
    // Turning takes the short way round: 65500 to 20 is +56, not -65480.
    {
        TestEqual(TEXT("Short way round the wrap"),
            FCMLAirshipCollision::ShortestTurnDelta(65500, 20), 56);
        TestEqual(TEXT("Short way back"),
            FCMLAirshipCollision::ShortestTurnDelta(20, 65500), -56);
        TestEqual(TEXT("No turn is zero"),
            FCMLAirshipCollision::ShortestTurnDelta(1234, 1234), 0);
    }

    // Empty space is clear.
    {
        const FCMLAirshipSimulationState Empty;
        TestTrue(TEXT("Open air is clear"),
            FCMLAirshipCollision::IsCandidateClear(Empty, MakePose(0, 0), MakePose(500, 0)));
    }

    // A pose sitting inside an obstacle is not clear even without moving.
    {
        const FCMLAirshipSimulationState Wall = MakeWall(0);
        TestTrue(TEXT("The hull intersects the wall it sits in"),
            FCMLAirshipCollision::IntersectsAnyObstacle(Wall, MakePose(0, 0)));
        TestFalse(TEXT("A stationary candidate inside an obstacle is refused"),
            FCMLAirshipCollision::IsCandidateClear(Wall, MakePose(0, 0), MakePose(0, 0)));
    }

    // The property the sweep exists for: a thin wall between the endpoints must
    // be caught even though neither endpoint touches it.
    {
        const int64 HullHalfX = FCMLAirshipCollision::GetHullHalfExtents().X;
        // Place the airship well clear on one side and the candidate well clear
        // on the other, with a thin wall exactly in between.
        const FCMLAirshipPose Start = MakePose(-HullHalfX - 400, 0);
        const FCMLAirshipPose End = MakePose(HullHalfX + 400, 0);
        const FCMLAirshipSimulationState Wall = MakeWall(0, 50);

        TestFalse(TEXT("Neither endpoint touches the wall"),
            FCMLAirshipCollision::IntersectsAnyObstacle(Wall, Start)
                || FCMLAirshipCollision::IntersectsAnyObstacle(Wall, End));
        TestFalse(TEXT("The sweep catches the wall between them"),
            FCMLAirshipCollision::IsCandidateClear(Wall, Start, End));
    }

    // A candidate larger than any legal one-tick flight is refused rather than
    // swept with an unbounded number of samples.
    {
        const FCMLAirshipSimulationState Empty;
        const FCMLAirshipPose Start = MakePose(0, 0);
        const FCMLAirshipPose Absurd =
            MakePose(FCMLAirshipCollision::MaximumLegalSweptStep + 1, 0);
        TestFalse(TEXT("An impossible leap is refused"),
            FCMLAirshipCollision::IsCandidateClear(Empty, Start, Absurd));

        const FCMLAirshipPose AtLimit =
            MakePose(FCMLAirshipCollision::MaximumLegalSweptStep, 0);
        TestTrue(TEXT("A step at the legal limit is swept"),
            FCMLAirshipCollision::IsCandidateClear(Empty, Start, AtLimit));
    }

    // A rotated hull is tested in its own frame, so an airship can be clear at
    // one heading and blocked at another in the same place.
    {
        FCMLAirshipSimulationState Slot;
        FCMLAirshipObstacle Left;
        Left.Id = FCMLStableId(0, 2);
        Left.Minimum = FCMLAirshipVector{-500, -1000, 2500};
        Left.Maximum = FCMLAirshipVector{500, 1000, 12000};
        FCMLAirshipObstacle Right = Left;
        Right.Id = FCMLStableId(0, 3);
        Right.Minimum.Z = -12000;
        Right.Maximum.Z = -2500;
        Slot.Obstacles.Add(Left);
        Slot.Obstacles.Add(Right);

        // Along its length the hull reaches 4000 mm and hits both walls; turned
        // a quarter, its 2000 mm half-width fits between them.
        TestTrue(TEXT("Lengthwise the hull hits the walls"),
            FCMLAirshipCollision::IntersectsAnyObstacle(Slot, MakePose(0, 0, 0)));
        TestFalse(TEXT("Turned a quarter it fits between them"),
            FCMLAirshipCollision::IntersectsAnyObstacle(Slot, MakePose(0, 0, 16384)));
    }

    // An obstacle small enough to sit entirely inside the hull touches no hull
    // corner, so it has to be caught from the other direction.
    {
        FCMLAirshipSimulationState Speck;
        FCMLAirshipObstacle Tiny;
        Tiny.Id = FCMLStableId(0, 4);
        Tiny.Minimum = FCMLAirshipVector{-10, -10, -10};
        Tiny.Maximum = FCMLAirshipVector{10, 10, 10};
        Speck.Obstacles.Add(Tiny);
        TestTrue(TEXT("A speck inside the hull is detected"),
            FCMLAirshipCollision::IntersectsAnyObstacle(Speck, MakePose(0, 0)));
    }
    return true;
}
#endif

#include "Simulation/CMLBeltModuleShape.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"

namespace
{
    FCMLMachineNodeState MakeBelt(
        const FCMLStableId& DefinitionId,
        const int32 PlacementYaw,
        const ECMLBeltTravelDirection Direction)
    {
        FCMLMachineNodeState Belt;
        Belt.Id = FCMLStableId(0, 700);
        Belt.Kind = ECMLMachineNodeKind::BeltModule;
        Belt.DefinitionId = DefinitionId;
        Belt.bHasPlacementPose = true;
        Belt.PlacementPose.YawQuarterTurns = PlacementYaw;
        Belt.BeltTravelDirection = Direction;
        return Belt;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLBeltModuleShapeTest,
    "CML.Core.Simulation.BeltModuleShape",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLBeltModuleShapeTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    using Shape = FCMLBeltModuleShape;

    // Only curves turn, and they turn opposite ways. Without both, a path could
    // only ever bend one direction.
    {
        TestEqual(TEXT("A straight belt does not turn"),
            Shape::TurnQuarterTurns(BeltStraight), 0);
        TestEqual(TEXT("The right curve turns one quarter"),
            Shape::TurnQuarterTurns(BeltCurve), 1);
        TestEqual(TEXT("The left curve turns the other way"),
            Shape::TurnQuarterTurns(BeltCurveLeft), 3);
        TestTrue(TEXT("A straight belt is straight and level"),
            Shape::IsStraightAndLevel(BeltStraight));
        TestFalse(TEXT("A curve is not"), Shape::IsStraightAndLevel(BeltCurve));
        TestFalse(TEXT("Nor is an incline"), Shape::IsStraightAndLevel(BeltIncline));
    }

    // Only the incline rises, and running it backwards descends by as much.
    {
        TestEqual(TEXT("A straight belt is level"), Shape::RiseMillimetres(BeltStraight), 0);
        TestEqual(TEXT("The incline rises half a floor"),
            Shape::RiseMillimetres(BeltIncline), Shape::InclineRiseMillimetres);
        TestEqual(TEXT("Run forwards it climbs"),
            Shape::BeltRiseMillimetres(
                MakeBelt(BeltIncline, 0, ECMLBeltTravelDirection::Forward)),
            Shape::InclineRiseMillimetres);
        TestEqual(TEXT("Run backwards it descends"),
            Shape::BeltRiseMillimetres(
                MakeBelt(BeltIncline, 0, ECMLBeltTravelDirection::Reverse)),
            -Shape::InclineRiseMillimetres);
    }

    // On a straight run, reverse is simply the opposite of the pose.
    {
        uint8 Entry = 0;
        uint8 Exit = 0;
        for (uint8 Yaw = 0; Yaw <= 3; ++Yaw)
        {
            TestTrue(TEXT("Forward resolves"),
                Shape::TryResolveTravelYaws(
                    BeltStraight, Yaw, ECMLBeltTravelDirection::Forward, Entry, Exit));
            TestEqual(TEXT("Entry is the pose"), static_cast<int32>(Entry), static_cast<int32>(Yaw));
            TestEqual(TEXT("Exit matches entry"),
                static_cast<int32>(Exit), static_cast<int32>(Yaw));

            TestTrue(TEXT("Reverse resolves"),
                Shape::TryResolveTravelYaws(
                    BeltStraight, Yaw, ECMLBeltTravelDirection::Reverse, Entry, Exit));
            TestEqual(TEXT("Both flip together"),
                static_cast<int32>(Entry), static_cast<int32>((Yaw + 2) & 3));
            TestEqual(TEXT("Both flip together"),
                static_cast<int32>(Exit), static_cast<int32>((Yaw + 2) & 3));
        }
    }

    // On a curve the two differ, and telling them apart is what lets a path
    // turn. Reverse motion enters through the lateral exit and leaves through
    // the geometric entry.
    {
        uint8 Entry = 0;
        uint8 Exit = 0;
        TestTrue(TEXT("A curve resolves forwards"),
            Shape::TryResolveTravelYaws(
                BeltCurve, 0, ECMLBeltTravelDirection::Forward, Entry, Exit));
        TestEqual(TEXT("It enters on the pose"), static_cast<int32>(Entry), 0);
        TestEqual(TEXT("And leaves a quarter turn round"), static_cast<int32>(Exit), 1);

        TestTrue(TEXT("A curve resolves backwards"),
            Shape::TryResolveTravelYaws(
                BeltCurve, 0, ECMLBeltTravelDirection::Reverse, Entry, Exit));
        TestEqual(TEXT("It enters opposite the lateral exit"), static_cast<int32>(Entry), 3);
        TestEqual(TEXT("And leaves opposite the pose"), static_cast<int32>(Exit), 2);

        TestFalse(TEXT("A stopped belt resolves neither"),
            Shape::TryResolveTravelYaws(
                BeltCurve, 0, ECMLBeltTravelDirection::Stopped, Entry, Exit));
        TestFalse(TEXT("An impossible pose is refused"),
            Shape::TryResolveTravelYaws(
                BeltCurve, 4, ECMLBeltTravelDirection::Forward, Entry, Exit));
    }

    // A curve owns two half-lines, not two infinite axes. The far side of an arm
    // is not an endpoint, and accepting it made a straight piece latch onto the
    // wrong half.
    {
        int32 Height = 0;
        TestTrue(TEXT("The entry side of a curve is an endpoint"),
            Shape::TryGetEndpointHeightMillimetres(BeltCurve, 0, 2, Height));
        TestEqual(TEXT("At the module's own height"), Height, 0);
        TestTrue(TEXT("The lateral exit is an endpoint"),
            Shape::TryGetEndpointHeightMillimetres(BeltCurve, 0, 1, Height));
        TestFalse(TEXT("The far side of the entry arm is not"),
            Shape::TryGetEndpointHeightMillimetres(BeltCurve, 0, 0, Height));
        TestFalse(TEXT("Nor is the fourth side"),
            Shape::TryGetEndpointHeightMillimetres(BeltCurve, 0, 3, Height));

        // On an incline the two ends sit at different heights, which is the
        // other half of the same contract.
        TestTrue(TEXT("An incline's entry is at the root"),
            Shape::TryGetEndpointHeightMillimetres(BeltIncline, 0, 2, Height));
        TestEqual(TEXT("Height zero"), Height, 0);
        TestTrue(TEXT("Its exit is raised"),
            Shape::TryGetEndpointHeightMillimetres(BeltIncline, 0, 0, Height));
        TestEqual(TEXT("By the rise"), Height, Shape::InclineRiseMillimetres);
    }

    // Placing a module upstream: the pose needed for its exit to face a given
    // way is the inverse of the turn, and must round-trip for every module.
    {
        const FCMLStableId Modules[] = {BeltStraight, BeltCurve, BeltCurveLeft, BeltIncline};
        for (const FCMLStableId& Module : Modules)
        {
            for (uint8 Exit = 0; Exit <= 3; ++Exit)
            {
                const uint8 Pose = Shape::PlacementYawForForwardExit(Module, Exit);
                TestTrue(TEXT("The pose is a legal quarter turn"), Pose <= 3);
                TestEqual(TEXT("And it makes the exit face the right way"),
                    static_cast<int32>(Shape::ForwardExitYaw(Module, Pose)),
                    static_cast<int32>(Exit));
            }
        }
    }

    // The node-level helpers refuse anything that is not a moving placed belt,
    // rather than answering with a default direction.
    {
        uint8 Yaw = 0;
        TestTrue(TEXT("A moving belt has a travel yaw"),
            Shape::TryGetBeltTravelYaw(
                MakeBelt(BeltStraight, 1, ECMLBeltTravelDirection::Forward), Yaw));
        TestEqual(TEXT("Which is its pose"), static_cast<int32>(Yaw), 1);
        TestFalse(TEXT("A stopped belt has none"),
            Shape::TryGetBeltTravelYaw(
                MakeBelt(BeltStraight, 1, ECMLBeltTravelDirection::Stopped), Yaw));

        FCMLMachineNodeState Unplaced =
            MakeBelt(BeltStraight, 1, ECMLBeltTravelDirection::Forward);
        Unplaced.bHasPlacementPose = false;
        TestFalse(TEXT("An unplaced belt has none"),
            Shape::TryGetBeltTravelYaw(Unplaced, Yaw));

        FCMLMachineNodeState NotABelt =
            MakeBelt(BeltStraight, 1, ECMLBeltTravelDirection::Forward);
        NotABelt.Kind = ECMLMachineNodeKind::Machine;
        TestFalse(TEXT("A machine has none"), Shape::TryGetBeltTravelYaw(NotABelt, Yaw));
        TestEqual(TEXT("And adds no rise"), Shape::BeltRiseMillimetres(NotABelt), 0);

        // A stopped belt still has a geometric exit: placement needs it before
        // any drive has published a polarity.
        TestFalse(TEXT("A stopped belt resolves no exit either"),
            Shape::TryGetBeltExitYaw(
                MakeBelt(BeltCurve, 0, ECMLBeltTravelDirection::Stopped), Yaw));
        TestTrue(TEXT("A running curve does"),
            Shape::TryGetBeltExitYaw(
                MakeBelt(BeltCurve, 0, ECMLBeltTravelDirection::Forward), Yaw));
        TestEqual(TEXT("A quarter turn round"), static_cast<int32>(Yaw), 1);
    }
    return true;
}
#endif

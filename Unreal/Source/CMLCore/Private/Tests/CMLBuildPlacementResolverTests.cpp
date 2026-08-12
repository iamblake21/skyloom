#include "Simulation/CMLBuildPlacementResolver.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"
#include "Simulation/CMLBeltModuleShape.h"
#include "Simulation/CMLMachineBuildRule.h"

namespace
{
    FCMLMachineBuildPose Pose(
        const int64 X, const int64 Y, const int64 Z, const int32 Yaw)
    {
        FCMLMachineBuildPose Result;
        Result.XMillimetres = X;
        Result.YMillimetres = Y;
        Result.ZMillimetres = Z;
        Result.YawQuarterTurns = Yaw;
        return Result;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLBuildPlacementResolverTest,
    "CML.Core.Simulation.BuildPlacementResolver",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLBuildPlacementResolverTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(FCMLMachineBuildRule::CreateBuffer(
            FCMLStableId(9, 1), WoodenCrate, 8, Pose(0, 0, 0, 0)));
        const FCMLMachineBuildPose Resolved = FCMLBuildPlacementResolver::ResolveFromTarget(
            State,
            State.Nodes[0].Id,
            Pose(0, 0, 0, 0),
            ECMLMachineBuildKind::BeltModule,
            1,
            0,
            false,
            BeltStraight);
        TestEqual(TEXT("A crate face chooses the adjacent X cell"),
            Resolved.XMillimetres, static_cast<int64>(1000));
        TestEqual(TEXT("The module faces that chosen face"), Resolved.YawQuarterTurns, 1);
    }

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(FCMLMachineBuildRule::CreateBeltModule(
            FCMLStableId(9, 2), BeltCurve, Pose(0, 0, 0, 0)));
        TestFalse(TEXT("Curve target has a stable id"), State.Nodes[0].Id.IsNone());
        TestTrue(TEXT("Curve target carries a placement pose"), State.Nodes[0].bHasPlacementPose);
        TestEqual(TEXT("Curve target is a belt module"),
            static_cast<int32>(State.Nodes[0].Kind),
            static_cast<int32>(ECMLMachineNodeKind::BeltModule));
        TestEqual(TEXT("Curve target owns the right-hand definition"),
            State.Nodes[0].DefinitionId.Low, BeltCurve.Low);
        TestEqual(TEXT("Curve shape resolves before placement"),
            FCMLBeltModuleShape::TurnQuarterTurns(State.Nodes[0].DefinitionId), 1);
        TestEqual(TEXT("Curve geometric exit is +X"),
            static_cast<int32>(FCMLBeltModuleShape::ForwardExitYaw(
                State.Nodes[0].DefinitionId, 0)), 1);
        const FCMLMachineBuildPose Resolved = FCMLBuildPlacementResolver::ResolveFromTarget(
            State,
            State.Nodes[0].Id,
            Pose(0, 0, 0, 0),
            ECMLMachineBuildKind::BeltModule,
            3, // aiming at the curve's non-existent arm snaps to its real exit
            0,
            false,
            BeltStraight);
        TestEqual(TEXT("A right curve exits into the +X cell"),
            Resolved.XMillimetres, static_cast<int64>(1000));
        TestEqual(TEXT("The following straight inherits that exit"),
            Resolved.YawQuarterTurns, 1);
        TestTrue(TEXT("The two real endpoints are adjacent"),
            FCMLBuildPlacementResolver::ArePortsAdjacent(
                ECMLMachineNodeKind::BeltModule,
                State.Nodes[0].PlacementPose,
                BeltCurve,
                ECMLMachineNodeKind::BeltModule,
                Resolved,
                BeltStraight));
        TestFalse(TEXT("The empty half of the curve is not a connector"),
            FCMLBuildPlacementResolver::ArePortsAdjacent(
                ECMLMachineNodeKind::BeltModule,
                State.Nodes[0].PlacementPose,
                BeltCurve,
                ECMLMachineNodeKind::BeltModule,
                Pose(-1000, 0, 0, 3),
                BeltStraight));
    }

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(FCMLMachineBuildRule::CreateBeltModule(
            FCMLStableId(9, 3), BeltIncline, Pose(0, 0, 0, 0)));
        const FCMLMachineBuildPose Top = FCMLBuildPlacementResolver::ResolveFromTarget(
            State,
            State.Nodes[0].Id,
            Pose(0, 0, 0, 0),
            ECMLMachineBuildKind::BeltModule,
            0,
            0,
            false,
            BeltStraight);
        TestEqual(TEXT("The next module meets the incline's raised endpoint"),
            Top.YMillimetres, static_cast<int64>(300));
        TestTrue(TEXT("The raised endpoint remains a physical connection"),
            FCMLBuildPlacementResolver::ArePortsAdjacent(
                ECMLMachineNodeKind::BeltModule,
                State.Nodes[0].PlacementPose,
                BeltIncline,
                ECMLMachineNodeKind::BeltModule,
                Top,
                BeltStraight));
    }
    return true;
}
#endif

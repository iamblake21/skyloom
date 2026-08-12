#include "Simulation/CMLBeltLineRules.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"

namespace
{
    FCMLMachineNodeState BeltNode(
        const uint64 Id,
        const FCMLStableId& DefinitionId,
        const int32 ZCell)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0xBE17000000000000ULL, Id);
        Node.Kind = ECMLMachineNodeKind::BeltModule;
        Node.DefinitionId = DefinitionId;
        Node.bHasPlacementPose = true;
        Node.PlacementPose.ZMillimetres = ZCell * 1000;
        Node.PlacementPose.YawQuarterTurns = 0;
        return Node;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLBeltLineRulesTest,
    "CML.Core.Simulation.BeltLineRules",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLBeltLineRulesTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(BeltNode(1, BeltDriveUnit, 0));
        State.Nodes.Add(BeltNode(2, BeltStraight, 1));
        FCMLBeltLineRules::Recompute(State);

        TestEqual(TEXT("A driven line is operational"), State.Nodes[0].BeltLineStatus,
            ECMLBeltLineStatus::Operational);
        TestEqual(TEXT("The connected straight runs forward"),
            State.Nodes[1].BeltTravelDirection, ECMLBeltTravelDirection::Forward);
        TestEqual(TEXT("One consumer uses one capacity unit"),
            State.Nodes[1].BeltLineUsedCapacity, static_cast<int64>(1));
        TestEqual(TEXT("One drive supplies twelve capacity units"),
            State.Nodes[1].BeltLineAvailableCapacity, static_cast<int64>(12));
    }

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(BeltNode(10, BeltStraight, 0));
        FCMLBeltLineRules::Recompute(State);

        TestEqual(TEXT("A line without a drive reports the missing drive"),
            State.Nodes[0].BeltLineStatus, ECMLBeltLineStatus::MissingDrive);
        TestEqual(TEXT("A line without a drive is stopped"),
            State.Nodes[0].BeltTravelDirection, ECMLBeltTravelDirection::Stopped);
    }

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(BeltNode(20, BeltDriveUnit, 0));
        for (int32 Index = 1; Index <= 13; ++Index)
        {
            State.Nodes.Add(BeltNode(20 + Index, BeltStraight, Index));
        }
        FCMLBeltLineRules::Recompute(State);

        TestEqual(TEXT("The thirteenth consumer overloads one drive"),
            State.Nodes[0].BeltLineStatus, ECMLBeltLineStatus::Overloaded);
        TestEqual(TEXT("All thirteen consumers count"),
            State.Nodes[0].BeltLineUsedCapacity, static_cast<int64>(13));
        TestEqual(TEXT("The drive still advertises its physical capacity"),
            State.Nodes[0].BeltLineAvailableCapacity, static_cast<int64>(12));
    }

    return true;
}
#endif

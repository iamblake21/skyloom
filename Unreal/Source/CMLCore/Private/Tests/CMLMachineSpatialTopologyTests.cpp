#include "Simulation/CMLMachineSpatialTopology.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"
#include "Simulation/CMLTransferRule.h"

namespace
{
    using Topology = FCMLMachineSpatialTopology;
    constexpr int32 Cell = Topology::GridCellSizeMillimetres;

    FCMLDefinitionIdentity Named(const TCHAR* Key)
    {
        FCMLDefinitionIdentity Identity;
        Identity.Key = Key;
        Identity.NameKey = FString(TEXT("name.")) + Key;
        return Identity;
    }

    FCMLGameCatalog MakeCatalog()
    {
        using namespace CMLContentIds;
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({RawIron, 10, 0, Named(TEXT("item.raw_iron"))});
        Catalog.Items.Add({IronIngot, 10, 0, Named(TEXT("item.iron_ingot"))});
        Catalog.Items.Add({BeltStraight, 10, 0, Named(TEXT("item.belt_straight"))});
        Catalog.Items.Add({BeltFunnel, 10, 0, Named(TEXT("item.belt_funnel"))});

        FCMLRecipeDefinition Smelt;
        Smelt.RecipeId = SmeltIronIngot;
        Smelt.Station = ECMLCraftingStationKind::Machine;
        Smelt.Inputs.Add({RawIron, 2});
        Smelt.Outputs.Add({IronIngot, 1});
        Smelt.DurationMilliseconds = 4000;
        Smelt.Identity = Named(TEXT("recipe.smelt_iron_ingot"));
        Catalog.Recipes.Add(Smelt);

        FCMLMachineDefinition Furnace;
        Furnace.Id = CrudeFurnace;
        Furnace.InputSlots = 2;
        Furnace.OutputSlots = 2;
        Furnace.RequiredEnergyKind = ECMLEnergyKind::Thermal;
        Furnace.SupportedRecipeIds.Add(SmeltIronIngot);
        Furnace.InputBufferCapacityPerItem = 4;
        Furnace.Identity = Named(TEXT("machine.crude_furnace"));
        Catalog.Machines.Add(Furnace);

        Catalog.Containers.Add({WoodenCrate, 4, 200, Named(TEXT("container.wooden_crate"))});
        return Catalog;
    }

    FCMLMachinePort MakePort(const ECMLMachinePortKind Kind, const int32 SlotCount)
    {
        FCMLMachinePort Port;
        Port.Kind = Kind;
        Port.Slots.SetNum(SlotCount);
        return Port;
    }

    void Place(FCMLMachineNodeState& Node, const int32 X, const int32 Z, const int32 Yaw)
    {
        Node.bHasPlacementPose = true;
        Node.PlacementPose.XMillimetres = X;
        Node.PlacementPose.YMillimetres = 0;
        Node.PlacementPose.ZMillimetres = Z;
        Node.PlacementPose.YawQuarterTurns = Yaw;
    }

    FCMLMachineNodeState MakeCrate(const int64 Id, const int32 X, const int32 Z, const int32 Yaw)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, Id);
        Node.Kind = ECMLMachineNodeKind::Buffer;
        Node.DefinitionId = CMLContentIds::WoodenCrate;
        Node.Input = MakePort(ECMLMachinePortKind::Storage, 4);
        Node.bInputOutputAliased = true;
        Node.Output = Node.Input;
        Place(Node, X, Z, Yaw);
        return Node;
    }

    /**
     * A funnel has one slot, not two. Unity builds it with the same port object
     * as both input and output, so a piece pulled in through the input has to be
     * visible on the output — otherwise it would go in and never come out.
     */
    FCMLMachineNodeState MakeFunnel(const int64 Id, const int32 X, const int32 Z, const int32 Yaw)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, Id);
        Node.Kind = ECMLMachineNodeKind::Funnel;
        Node.DefinitionId = CMLContentIds::BeltFunnel;
        Node.Input = MakePort(ECMLMachinePortKind::Storage, 1);
        Node.bInputOutputAliased = true;
        Node.Output = Node.Input;
        Place(Node, X, Z, Yaw);
        return Node;
    }

    FCMLMachineNodeState MakeBelt(
        const int64 Id, const int32 X, const int32 Z, const int32 Yaw,
        const ECMLBeltTravelDirection Direction)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, Id);
        Node.Kind = ECMLMachineNodeKind::BeltModule;
        Node.DefinitionId = CMLContentIds::BeltStraight;
        Node.Input = MakePort(ECMLMachinePortKind::Input, 1);
        Node.Output = MakePort(ECMLMachinePortKind::Output, 1);
        Node.bInputOutputAliased = true;
        Node.Output = Node.Input;
        Node.BeltTravelDirection = Direction;
        Place(Node, X, Z, Yaw);
        return Node;
    }

    void Fill(FCMLMachineNodeState& Node, const FCMLStableId& Id, const uint64 Quantity)
    {
        Node.Input.Slots[0].ItemId = Id;
        Node.Input.Slots[0].Quantity.Value = Quantity;
        if (Node.bInputOutputAliased)
        {
            Node.Output = Node.Input;
        }
    }

    int32 IndexOf(const FCMLMachineSimulationState& State, const int64 Id)
    {
        return State.Nodes.IndexOfByPredicate([Id](const FCMLMachineNodeState& Node)
        {
            return Node.Id == FCMLStableId(0, Id);
        });
    }

    int64 Held(const FCMLMachineSimulationState& State, const int64 Id, const FCMLStableId& ItemId)
    {
        return FCMLMachinePortOperations::Count(State.Nodes[IndexOf(State, Id)].Input, ItemId);
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLMachineSpatialTopologyTest,
    "CML.Core.Simulation.MachineSpatialTopology",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLMachineSpatialTopologyTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();

    // A crate, an extracting funnel and a belt running away from it. Yaw 0 is
    // +Z, so the funnel at z=1000 has the crate behind it at z=0 and the belt
    // in front at z=2000.
    auto MakeLine = [](const ECMLBeltTravelDirection Direction)
    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(MakeCrate(1, 0, 0, 0));
        State.Nodes.Add(MakeFunnel(2, 0, Cell, 0));
        State.Nodes.Add(MakeBelt(3, 0, 2 * Cell, 0, Direction));
        return State;
    };

    // An extracting funnel pulls one unit per tick out of the crate, and the
    // belt then takes it: one physical step per authoritative tick.
    {
        FCMLMachineSimulationState State = MakeLine(ECMLBeltTravelDirection::Forward);
        Fill(State.Nodes[0], RawIron, 3);

        // The five steps run in a fixed order within one tick — funnels pull
        // before belts load — so a piece travels the whole hop from crate to
        // belt in a single tick rather than resting in the funnel for one.
        Topology::Advance(State, Catalog);
        TestEqual(TEXT("One unit left the crate"), Held(State, 1, RawIron), static_cast<int64>(2));
        TestEqual(TEXT("A crate's other face agrees"),
            FCMLMachinePortOperations::Count(State.Nodes[0].Output, RawIron),
            static_cast<int64>(2));
        TestEqual(TEXT("It did not stop in the funnel"),
            Held(State, 2, RawIron), static_cast<int64>(0));
        TestEqual(TEXT("It is already on the belt"),
            Held(State, 3, RawIron), static_cast<int64>(1));

        // With the belt now loaded it cannot take another, so the next unit does
        // wait in the funnel.
        Topology::Advance(State, Catalog);
        TestEqual(TEXT("A second unit left the crate"),
            Held(State, 1, RawIron), static_cast<int64>(1));
        TestEqual(TEXT("And waits in the funnel behind the loaded belt"),
            Held(State, 2, RawIron), static_cast<int64>(1));
        TestEqual(TEXT("The belt still carries one"),
            Held(State, 3, RawIron), static_cast<int64>(1));
    }

    // A belt travels its own length before it delivers: the piece does not
    // teleport across the module.
    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(MakeBelt(1, 0, 0, 0, ECMLBeltTravelDirection::Forward));
        State.Nodes.Add(MakeBelt(2, 0, Cell, 0, ECMLBeltTravelDirection::Forward));
        Fill(State.Nodes[0], RawIron, 1);

        const int32 Ticks = Topology::BeltLengthMillimetres / Topology::BeltSpeedMillimetresPerTick;
        for (int32 Tick = 0; Tick < Ticks; ++Tick)
        {
            TestEqual(TEXT("The piece has not moved on yet"),
                Held(State, 2, RawIron), static_cast<int64>(0));
            Topology::Advance(State, Catalog);
        }
        TestEqual(TEXT("It arrives on the tick the belt completes its length"),
            Held(State, 2, RawIron), static_cast<int64>(1));
        TestEqual(TEXT("And left the first belt"),
            Held(State, 1, RawIron), static_cast<int64>(0));
    }

    // Removing a module disconnects the line on the next tick, with no stale
    // logical edge left behind: this is the whole point of deriving the graph
    // from poses instead of storing it.
    {
        FCMLMachineSimulationState State = MakeLine(ECMLBeltTravelDirection::Forward);
        Fill(State.Nodes[0], RawIron, 3);
        Topology::Advance(State, Catalog);
        TestEqual(TEXT("The crate drains while the belt is there"),
            Held(State, 1, RawIron), static_cast<int64>(2));

        // Take the belt away and the funnel resolves to nothing.
        FCMLMachineSimulationState Broken = State;
        Broken.Nodes.RemoveAt(IndexOf(Broken, 3));
        const int64 Before = Held(Broken, 1, RawIron);
        Topology::Advance(Broken, Catalog);
        TestEqual(TEXT("Nothing more leaves the crate"),
            Held(Broken, 1, RawIron), Before);
    }

    // Direction is polarity, not geometry: reversing the drive turns the same
    // funnel from an extractor into an inserter.
    {
        FCMLMachineSimulationState State = MakeLine(ECMLBeltTravelDirection::Reverse);
        Fill(State.Nodes[0], RawIron, 2);
        Fill(State.Nodes[2], IronIngot, 1);

        Topology::Advance(State, Catalog);
        TestEqual(TEXT("The crate is not drained by a reversed line"),
            Held(State, 1, RawIron), static_cast<int64>(2));
        // The belt runs towards the funnel, so the funnel inserts: the ingot
        // works its way back into the crate.
        TestEqual(TEXT("The belt handed its piece to the funnel"),
            Held(State, 2, IronIngot), static_cast<int64>(0));
    }

    // A belt will not feed a machine that cannot take delivery. A belt keeps
    // pushing, so a machine mid-cycle or still holding its output has to refuse
    // or it would silently overfill.
    {
        FCMLMachineNodeState Furnace;
        Furnace.Id = FCMLStableId(0, 9);
        Furnace.Kind = ECMLMachineNodeKind::Machine;
        Furnace.DefinitionId = CrudeFurnace;
        Furnace.ActiveRecipeId = SmeltIronIngot;
        Furnace.Input = MakePort(ECMLMachinePortKind::Input, 2);
        Furnace.Output = MakePort(ECMLMachinePortKind::Output, 2);
        Place(Furnace, 0, Cell, 0);

        TestTrue(TEXT("An idle furnace admits its ingredient"),
            Topology::MachineAdmits(Furnace, RawIron, Catalog));
        TestFalse(TEXT("But not something the recipe never consumes"),
            Topology::MachineAdmits(Furnace, IronIngot, Catalog));

        FCMLMachineNodeState Busy = Furnace;
        Busy.bIsCycleActive = true;
        TestFalse(TEXT("A furnace mid-cycle takes no delivery"),
            Topology::MachineAdmits(Busy, RawIron, Catalog));

        FCMLMachineNodeState Blocked = Furnace;
        Blocked.Output.Slots[0].ItemId = IronIngot;
        Blocked.Output.Slots[0].Quantity.Value = 1;
        TestFalse(TEXT("Nor does one still holding its output"),
            Topology::MachineAdmits(Blocked, RawIron, Catalog));

        FCMLMachineNodeState Full = Furnace;
        Full.Input.Slots[0].ItemId = RawIron;
        Full.Input.Slots[0].Quantity.Value = 4;
        TestFalse(TEXT("Nor one already at its input buffer cap"),
            Topology::MachineAdmits(Full, RawIron, Catalog));

        FCMLMachineNodeState Unset = Furnace;
        Unset.ActiveRecipeId = FCMLStableId::None();
        TestFalse(TEXT("Nor one with no recipe set"),
            Topology::MachineAdmits(Unset, RawIron, Catalog));
    }

    // A stopped belt is inert: no travel, so no graph.
    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(MakeBelt(1, 0, 0, 0, ECMLBeltTravelDirection::Stopped));
        State.Nodes.Add(MakeBelt(2, 0, Cell, 0, ECMLBeltTravelDirection::Stopped));
        Fill(State.Nodes[0], RawIron, 1);

        for (int32 Tick = 0; Tick < 20; ++Tick)
        {
            Topology::Advance(State, Catalog);
        }
        TestEqual(TEXT("Nothing moved"), Held(State, 2, RawIron), static_cast<int64>(0));
        TestEqual(TEXT("And nothing was lost"), Held(State, 1, RawIron), static_cast<int64>(1));
        TestEqual(TEXT("A stopped belt does not even wind up"),
            State.Nodes[0].TransportProgressMillimetres, static_cast<int64>(0));
    }

    // Two belts pointing at each other are not a line: the exit of one must face
    // where the entry of the other faces.
    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(MakeBelt(1, 0, 0, 0, ECMLBeltTravelDirection::Forward));
        State.Nodes.Add(MakeBelt(2, 0, Cell, 2, ECMLBeltTravelDirection::Forward));
        Fill(State.Nodes[0], RawIron, 1);

        for (int32 Tick = 0; Tick < 20; ++Tick)
        {
            Topology::Advance(State, Catalog);
        }
        TestEqual(TEXT("Opposed belts hand nothing over"),
            Held(State, 2, RawIron), static_cast<int64>(0));
    }
    return true;
}
#endif

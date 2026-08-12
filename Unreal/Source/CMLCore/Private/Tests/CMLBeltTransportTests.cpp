#include "Simulation/CMLBeltTransport.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId Ore(0, 1);
    const FCMLStableId Ingot(0, 2);
    const FCMLStableId RecipeId(0, 10);
    const FCMLStableId MachineId(0, 30);

    FCMLGameCatalog MakeCatalog(const int64 MaxStack = 10)
    {
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({Ore, MaxStack});
        Catalog.Items.Add({Ingot, MaxStack});

        FCMLRecipeDefinition Recipe;
        Recipe.RecipeId = RecipeId;
        Recipe.Station = ECMLCraftingStationKind::Machine;
        Recipe.Inputs.Add({Ore, 1});
        Recipe.Outputs.Add({Ingot, 1});
        Recipe.DurationMilliseconds = 100;
        Catalog.Recipes.Add(Recipe);

        FCMLMachineDefinition Machine;
        Machine.Id = MachineId;
        Machine.InputSlots = 1;
        Machine.OutputSlots = 1;
        Machine.RequiredEnergyKind = ECMLEnergyKind::Electrical;
        Machine.RequiredPower = 10;
        Machine.SupportedRecipeIds.Add(RecipeId);
        Catalog.Machines.Add(Machine);
        return Catalog;
    }

    FCMLBeltLaneState MakeLane(const int64 Length = 1000, const int64 Speed = 100, const int64 Spacing = 200)
    {
        FCMLBeltLaneState Lane;
        Lane.Id = FCMLStableId(0, 50);
        Lane.LengthMillimetres = Length;
        Lane.SpeedMillimetresPerTick = Speed;
        Lane.SpacingMillimetres = Spacing;
        return Lane;
    }

    void AddItem(FCMLBeltLaneState& Lane, const FCMLStableId& ItemId, const int64 Position)
    {
        FCMLBeltLaneItem Item;
        Item.ItemId = ItemId;
        Item.PositionMillimetres = Position;
        Lane.Items.Add(Item);
    }

    FCMLMachineNodeState MakeDestination(const int32 InputSlots = 1)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, 100);
        Node.Kind = ECMLMachineNodeKind::Machine;
        Node.DefinitionId = MachineId;
        Node.ActiveRecipeId = RecipeId;
        Node.Input.Kind = ECMLMachinePortKind::Input;
        Node.Input.Slots.AddDefaulted(InputSlots);
        Node.Output.Kind = ECMLMachinePortKind::Output;
        Node.Output.Slots.AddDefaulted(1);
        return Node;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLBeltTransportTest,
    "CML.Core.Simulation.BeltTransport",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLBeltTransportTest::RunTest(const FString& Parameters)
{
    const FCMLGameCatalog Catalog = MakeCatalog();

    // A lone item advances by the lane speed and stops at the end.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ore, 0);
        FCMLBeltTransport::AdvanceLaneItems(Lane);
        TestEqual(TEXT("One tick of travel"),
            Lane.Items[0].PositionMillimetres, static_cast<int64>(100));

        for (int32 Tick = 0; Tick < 20; ++Tick)
        {
            FCMLBeltTransport::AdvanceLaneItems(Lane);
        }
        TestEqual(TEXT("An item stops at the lane end"),
            Lane.Items[0].PositionMillimetres, Lane.LengthMillimetres);
    }

    // Spacing is honoured: a following item never closes closer than the gap.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ore, 950);   // near the end
        AddItem(Lane, Ore, 800);
        for (int32 Tick = 0; Tick < 5; ++Tick)
        {
            FCMLBeltTransport::AdvanceLaneItems(Lane);
        }
        TestEqual(TEXT("The front item reaches the end"),
            Lane.Items[0].PositionMillimetres, static_cast<int64>(1000));
        TestEqual(TEXT("The follower stops one spacing behind"),
            Lane.Items[1].PositionMillimetres, static_cast<int64>(800));
    }

    // The queue forms from the exit backwards when the destination refuses.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ore, 1000);
        AddItem(Lane, Ore, 800);
        AddItem(Lane, Ore, 600);

        FCMLMachineNodeState Destination = MakeDestination();
        // Fill the single input slot so the machine cannot take another.
        Destination.Input.Slots[0].ItemId = Ore;
        Destination.Input.Slots[0].Quantity = FCMLNonNegativeQuantity(10);

        const int32 Delivered =
            FCMLBeltTransport::DeliverLaneItems(Lane, Destination, Catalog);
        TestEqual(TEXT("A full destination takes nothing"), Delivered, 0);
        TestEqual(TEXT("Every item stays on the lane"), Lane.Items.Num(), 3);

        FCMLBeltTransport::AdvanceLaneItems(Lane);
        TestEqual(TEXT("The blocked front holds the exit"),
            Lane.Items[0].PositionMillimetres, static_cast<int64>(1000));
        TestEqual(TEXT("The second queues one spacing back"),
            Lane.Items[1].PositionMillimetres, static_cast<int64>(800));
        TestEqual(TEXT("The third queues behind the second"),
            Lane.Items[2].PositionMillimetres, static_cast<int64>(600));
    }

    // With room, an item at the exit is delivered and counted.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ore, 1000);
        FCMLMachineNodeState Destination = MakeDestination();

        const int32 Delivered =
            FCMLBeltTransport::DeliverLaneItems(Lane, Destination, Catalog);
        TestEqual(TEXT("One item is delivered"), Delivered, 1);
        TestEqual(TEXT("The lane empties"), Lane.Items.Num(), 0);
        TestEqual(TEXT("The destination received it"),
            Destination.Input.Slots[0].Quantity.Value, static_cast<int64>(1));
        TestEqual(TEXT("The delivery is counted"), Lane.DeliveredUnits, static_cast<int64>(1));
    }

    // An item short of the end is not delivered, however much room there is.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ore, 999);
        FCMLMachineNodeState Destination = MakeDestination();
        TestEqual(TEXT("An item short of the exit stays"),
            FCMLBeltTransport::DeliverLaneItems(Lane, Destination, Catalog), 0);
        TestEqual(TEXT("It is still on the lane"), Lane.Items.Num(), 1);
    }

    // A machine takes only what its recipe consumes, so a lane carrying the
    // wrong item backs up rather than deadlocking the machine's slots.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ingot, 1000);
        FCMLMachineNodeState Destination = MakeDestination();
        TestEqual(TEXT("A non-ingredient is refused"),
            FCMLBeltTransport::DeliverLaneItems(Lane, Destination, Catalog), 0);
        TestTrue(TEXT("The destination's slot stays free"),
            Destination.Input.Slots[0].ItemId.IsNone());
    }

    // Several queued items drain in order once the destination frees up.
    {
        FCMLBeltLaneState Lane = MakeLane();
        AddItem(Lane, Ore, 1000);
        AddItem(Lane, Ore, 1000);
        FCMLMachineNodeState Destination = MakeDestination(2);
        TestEqual(TEXT("Both are delivered"),
            FCMLBeltTransport::DeliverLaneItems(Lane, Destination, Catalog), 2);
        TestEqual(TEXT("The lane is empty"), Lane.Items.Num(), 0);
        TestEqual(TEXT("Both landed in one stack"),
            Destination.Input.Slots[0].Quantity.Value, static_cast<int64>(2));
    }
    return true;
}
#endif

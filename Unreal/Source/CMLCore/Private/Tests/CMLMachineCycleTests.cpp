#include "Simulation/CMLMachineCycle.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId Ore(0, 1);
    const FCMLStableId Ingot(0, 2);
    const FCMLStableId RecipeId(0, 10);
    const FCMLStableId MachineId(0, 30);
    const FCMLStableId Coal(0, 3);

    FCMLGameCatalog MakeCatalog(const int64 DurationMilliseconds = 200)
    {
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({Ore, 10});
        Catalog.Items.Add({Ingot, 10});
        Catalog.Items.Add({Coal, 10});

        FCMLRecipeDefinition Recipe;
        Recipe.RecipeId = RecipeId;
        Recipe.Station = ECMLCraftingStationKind::Machine;
        Recipe.Inputs.Add({Ore, 2});
        Recipe.Outputs.Add({Ingot, 1});
        Recipe.DurationMilliseconds = DurationMilliseconds;
        Catalog.Recipes.Add(Recipe);

        FCMLMachineDefinition Machine;
        Machine.Id = MachineId;
        Machine.InputSlots = 2;
        Machine.OutputSlots = 2;
        Machine.RequiredEnergyKind = ECMLEnergyKind::Electrical;
        Machine.RequiredPower = 10;
        Machine.SupportedRecipeIds.Add(RecipeId);
        Catalog.Machines.Add(Machine);
        return Catalog;
    }

    FCMLMachineNodeState MakeMachine(const int64 OreOnHand, const int32 OutputSlots = 2)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, 100);
        Node.Kind = ECMLMachineNodeKind::Machine;
        Node.DefinitionId = MachineId;
        Node.ActiveRecipeId = RecipeId;
        Node.Input.Kind = ECMLMachinePortKind::Input;
        Node.Input.Slots.AddDefaulted(2);
        if (OreOnHand > 0)
        {
            Node.Input.Slots[0].ItemId = Ore;
            Node.Input.Slots[0].Quantity = FCMLNonNegativeQuantity(OreOnHand);
        }
        Node.Output.Kind = ECMLMachinePortKind::Output;
        Node.Output.Slots.AddDefaulted(OutputSlots);
        return Node;
    }

    int64 CountIn(const FCMLMachinePort& Port, const FCMLStableId& ItemId)
    {
        int64 Total = 0;
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId == ItemId)
            {
                Total += Slot.Quantity.Value;
            }
        }
        return Total;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLMachineCycleTest,
    "CML.Core.Simulation.MachineCycle",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLMachineCycleTest::RunTest(const FString& Parameters)
{
    const FCMLGameCatalog Catalog = MakeCatalog();

    // A cycle spends its inputs at the start, runs for its duration, then
    // deposits. Four ticks at 50 ms cover a 200 ms recipe.
    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(MakeMachine(4));

        FCMLMachineCycle::AdvanceCycles(State, Catalog);
        TestTrue(TEXT("The cycle starts"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("Inputs are spent up front"),
            CountIn(State.Nodes[0].Input, Ore), static_cast<int64>(2));
        TestEqual(TEXT("One tick of progress"),
            State.Nodes[0].ProgressMilliseconds, static_cast<int64>(50));

        for (int32 Tick = 0; Tick < 3; ++Tick)
        {
            FCMLMachineCycle::AdvanceCycles(State, Catalog);
        }
        TestEqual(TEXT("Progress reaches the duration"),
            State.Nodes[0].ProgressMilliseconds, static_cast<int64>(200));
        TestEqual(TEXT("Nothing is deposited before phase 8"),
            CountIn(State.Nodes[0].Output, Ingot), static_cast<int64>(0));

        FCMLMachineCycle::CompleteCycles(State, Catalog);
        TestEqual(TEXT("The output is deposited"),
            CountIn(State.Nodes[0].Output, Ingot), static_cast<int64>(1));
        TestFalse(TEXT("The cycle closes"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("The cycle is counted"),
            State.Nodes[0].CompletedCycles, static_cast<int64>(1));
    }

    // The property that makes a blocked machine safe: a finished cycle with
    // nowhere to put its output stays finished, and its ingredients are not
    // spent twice.
    {
        FCMLMachineSimulationState State;
        FCMLMachineNodeState Node = MakeMachine(4, 1);
        // The single output slot is already full of something else.
        Node.Output.Slots[0].ItemId = Ore;
        Node.Output.Slots[0].Quantity = FCMLNonNegativeQuantity(10);
        State.Nodes.Add(Node);

        // A full output port stops the cycle from even starting.
        FCMLMachineCycle::AdvanceCycles(State, Catalog);
        TestFalse(TEXT("A full output blocks the start"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("Activity reports OutputFull"),
            static_cast<int32>(State.Nodes[0].Activity),
            static_cast<int32>(ECMLMachineActivity::OutputFull));
        TestEqual(TEXT("No input was consumed"),
            CountIn(State.Nodes[0].Input, Ore), static_cast<int64>(4));
    }

    // A cycle that fills up mid-run keeps its finished state instead of losing
    // the work or re-running it.
    {
        FCMLMachineSimulationState State;
        FCMLMachineNodeState Node = MakeMachine(4, 1);
        State.Nodes.Add(Node);

        for (int32 Tick = 0; Tick < 4; ++Tick)
        {
            FCMLMachineCycle::AdvanceCycles(State, Catalog);
        }
        // Block the output after the cycle finished but before it deposits.
        State.Nodes[0].Output.Slots[0].ItemId = Ore;
        State.Nodes[0].Output.Slots[0].Quantity = FCMLNonNegativeQuantity(10);

        FCMLMachineCycle::CompleteCycles(State, Catalog);
        TestTrue(TEXT("The finished cycle survives"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("No cycle is counted"),
            State.Nodes[0].CompletedCycles, static_cast<int64>(0));

        // Advancing again must not push progress past the duration, or the
        // cycle would be worked twice.
        FCMLMachineCycle::AdvanceCycles(State, Catalog);
        TestEqual(TEXT("Progress stays at the duration"),
            State.Nodes[0].ProgressMilliseconds, static_cast<int64>(200));

        // Clear the blockage; the cycle deposits and closes.
        State.Nodes[0].Output.Slots[0] = FCMLMachineSlot();
        FCMLMachineCycle::CompleteCycles(State, Catalog);
        TestFalse(TEXT("The cycle finally closes"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("The output arrives"),
            CountIn(State.Nodes[0].Output, Ingot), static_cast<int64>(1));
    }

    {
        FCMLMachineSimulationState State;
        State.Nodes.Add(MakeMachine(1));
        FCMLMachineCycle::AdvanceCycles(State, Catalog);
        TestFalse(TEXT("Too little input blocks the start"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("Activity reports MissingInput"),
            static_cast<int32>(State.Nodes[0].Activity),
            static_cast<int32>(ECMLMachineActivity::MissingInput));
    }

    {
        FCMLMachineSimulationState State;
        FCMLMachineNodeState Node = MakeMachine(4);
        Node.ActiveRecipeId = FCMLStableId::None();
        State.Nodes.Add(Node);
        FCMLMachineCycle::AdvanceCycles(State, Catalog);
        TestEqual(TEXT("No recipe is reported"),
            static_cast<int32>(State.Nodes[0].Activity),
            static_cast<int32>(ECMLMachineActivity::NoRecipe));
    }

    // A fuelled machine refuses to start without fuel, and burns it once per
    // cycle rather than once per tick.
    {
        FCMLGameCatalog Fuelled = MakeCatalog();
        Fuelled.Machines[0].FuelSlots = 1;
        Fuelled.Machines[0].FuelItemId = Coal;
        Fuelled.Machines[0].FuelQuantityPerCycle = 1;

        FCMLMachineSimulationState State;
        FCMLMachineNodeState Node = MakeMachine(4);
        Node.bHasFuelPort = true;
        Node.Fuel.Kind = ECMLMachinePortKind::Storage;
        Node.Fuel.Slots.AddDefaulted(1);
        State.Nodes.Add(Node);

        FCMLMachineCycle::AdvanceCycles(State, Fuelled);
        TestFalse(TEXT("No fuel blocks the start"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("Activity reports MissingFuel"),
            static_cast<int32>(State.Nodes[0].Activity),
            static_cast<int32>(ECMLMachineActivity::MissingFuel));

        State.Nodes[0].Fuel.Slots[0].ItemId = Coal;
        State.Nodes[0].Fuel.Slots[0].Quantity = FCMLNonNegativeQuantity(2);
        FCMLMachineCycle::AdvanceCycles(State, Fuelled);
        TestTrue(TEXT("Fuel lets the cycle start"), State.Nodes[0].bIsCycleActive);
        TestEqual(TEXT("One unit of fuel is burnt per cycle"),
            CountIn(State.Nodes[0].Fuel, Coal), static_cast<int64>(1));

        FCMLMachineCycle::AdvanceCycles(State, Fuelled);
        TestEqual(TEXT("Running does not burn more fuel"),
            CountIn(State.Nodes[0].Fuel, Coal), static_cast<int64>(1));
    }

    // A machine takes only what its recipe consumes, or a mixed feed would
    // deadlock it with items it can never use.
    {
        const FCMLMachineNodeState Node = MakeMachine(0);
        TestTrue(TEXT("An ingredient is admitted"), FCMLMachineCycle::Admits(Node, Ore, Catalog));
        TestFalse(TEXT("A non-ingredient is refused"), FCMLMachineCycle::Admits(Node, Ingot, Catalog));
    }
    return true;
}
#endif

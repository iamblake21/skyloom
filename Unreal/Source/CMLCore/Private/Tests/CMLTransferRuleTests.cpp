#include "Simulation/CMLTransferRule.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId CrateNode(0, 900);
    const FCMLStableId FurnaceNode(0, 901);
    const FCMLStableId BackpackId = CMLContentIds::PlayerInventory;

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
        Catalog.Items.Add({WoodLog, 10, 0, Named(TEXT("item.wood_log"))});

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
        Furnace.FuelSlots = 1;
        Furnace.FuelItemId = WoodLog;
        Furnace.FuelQuantityPerCycle = 3;
        Furnace.InputBufferCapacityPerItem = 6;
        Furnace.FuelBufferCapacityPerItem = 5;
        Furnace.Identity = Named(TEXT("machine.crude_furnace"));
        Catalog.Machines.Add(Furnace);

        Catalog.Containers.Add({WoodenCrate, 4, 200, Named(TEXT("container.wooden_crate"))});
        Catalog.Containers.Add(
            {PlayerInventory, 16, 500, Named(TEXT("container.player_inventory"))});
        return Catalog;
    }

    FCMLMachinePort MakePort(const ECMLMachinePortKind Kind, const int32 SlotCount)
    {
        FCMLMachinePort Port;
        Port.Kind = Kind;
        Port.Slots.SetNum(SlotCount);
        return Port;
    }

    FCMLMachineSimulationState MakeMachines()
    {
        using namespace CMLContentIds;
        FCMLMachineSimulationState Machines;

        FCMLMachineNodeState Crate;
        Crate.Id = CrateNode;
        Crate.Kind = ECMLMachineNodeKind::Buffer;
        Crate.DefinitionId = WoodenCrate;
        Crate.Input = MakePort(ECMLMachinePortKind::Storage, 4);
        Crate.bInputOutputAliased = true;
        Crate.Output = Crate.Input;
        Machines.Nodes.Add(Crate);

        FCMLMachineNodeState Furnace;
        Furnace.Id = FurnaceNode;
        Furnace.Kind = ECMLMachineNodeKind::Machine;
        Furnace.DefinitionId = CrudeFurnace;
        Furnace.ActiveRecipeId = SmeltIronIngot;
        Furnace.Input = MakePort(ECMLMachinePortKind::Input, 2);
        Furnace.Output = MakePort(ECMLMachinePortKind::Output, 2);
        Furnace.bHasFuelPort = true;
        Furnace.Fuel = MakePort(ECMLMachinePortKind::Fuel, 1);
        Machines.Nodes.Add(Furnace);
        return Machines;
    }

    FCMLInventorySimulationState MakeInventories()
    {
        FCMLInventorySimulationState Inventories;
        FCMLInventoryState Backpack;
        Backpack.InventoryId = BackpackId;
        Backpack.ContainerDefinitionId = CMLContentIds::PlayerInventory;
        Backpack.Slots.SetNum(16);
        Inventories.Inventories.Add(Backpack);
        return Inventories;
    }

    void PlaceInInventory(
        FCMLInventorySimulationState& Inventories, const FCMLStableId& Id, const uint64 Quantity)
    {
        Inventories.Inventories[0].Slots[0].bHasStack = true;
        Inventories.Inventories[0].Slots[0].Stack.ItemId = Id;
        Inventories.Inventories[0].Slots[0].Stack.Quantity.Value = Quantity;
    }

    void PlaceInPort(FCMLMachinePort& Port, const int32 Index, const FCMLStableId& Id, const uint64 Quantity)
    {
        Port.Slots[Index].ItemId = Id;
        Port.Slots[Index].Quantity.Value = Quantity;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLTransferRuleTest,
    "CML.Core.Simulation.TransferRule",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLTransferRuleTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();

    FCMLInventorySimulationState OutInventories;
    FCMLMachineSimulationState OutMachines;
    ECMLTransferFailure Failure = ECMLTransferFailure::None;

    const FCMLTransferEndpoint Backpack = FCMLTransferEndpoint::Inventory(BackpackId);
    const FCMLTransferEndpoint Crate =
        FCMLTransferEndpoint::Port(CrateNode, ECMLMachinePortKind::Storage);
    const FCMLTransferEndpoint FurnaceInput =
        FCMLTransferEndpoint::Port(FurnaceNode, ECMLMachinePortKind::Input);
    const FCMLTransferEndpoint FurnaceOutput =
        FCMLTransferEndpoint::Port(FurnaceNode, ECMLMachinePortKind::Output);
    const FCMLTransferEndpoint FurnaceFuel =
        FCMLTransferEndpoint::Port(FurnaceNode, ECMLMachinePortKind::Fuel);

    // Backpack into the furnace's input: the whole amount moves, and nothing is
    // created or destroyed on the way.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, RawIron, 5);
        const FCMLMachineSimulationState Machines = MakeMachines();

        TestTrue(TEXT("Feeding a furnace is allowed"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceInput, RawIron, 3,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("Two are left in the backpack"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], RawIron),
            static_cast<int64>(2));
        TestEqual(TEXT("Three arrived in the input port"),
            FCMLMachinePortOperations::Count(OutMachines.Nodes[1].Input, RawIron),
            static_cast<int64>(3));
    }

    // An output is a result buffer. A hand-fed ingot there would be
    // indistinguishable from one the furnace made.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, IronIngot, 4);
        const FCMLMachineSimulationState Machines = MakeMachines();

        TestFalse(TEXT("Nothing may be pushed into an output"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceOutput, IronIngot, 1,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("The refusal names the reason"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLTransferFailure::NotAdmitted));
        TestEqual(TEXT("The backpack is untouched"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], IronIngot),
            static_cast<int64>(4));
    }

    // An input takes only what the active recipe consumes; a crate takes
    // anything.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, IronIngot, 4);
        const FCMLMachineSimulationState Machines = MakeMachines();

        TestFalse(TEXT("A furnace refuses what its recipe does not consume"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceInput, IronIngot, 1,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("The refusal names the reason"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLTransferFailure::NotAdmitted));

        TestTrue(TEXT("A crate takes the same item happily"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, Crate, IronIngot, 4,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("The crate holds them"),
            FCMLMachinePortOperations::Count(OutMachines.Nodes[0].Input, IronIngot),
            static_cast<int64>(4));
        // A buffer's two faces are one store.
        TestEqual(TEXT("Its other face agrees"),
            FCMLMachinePortOperations::Count(OutMachines.Nodes[0].Output, IronIngot),
            static_cast<int64>(4));
    }

    // The fuel port takes only the machine's own fuel, and only up to the
    // machine's buffer cap rather than the port's physical room.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, WoodLog, 9);
        const FCMLMachineSimulationState Machines = MakeMachines();

        TestFalse(TEXT("More than the fuel buffer holds is refused"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceFuel, WoodLog, 6,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("The refusal names the reason"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLTransferFailure::DestinationFull));

        TestTrue(TEXT("Exactly the buffer's worth fits"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceFuel, WoodLog, 5,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("The fuel port holds it"),
            FCMLMachinePortOperations::Count(OutMachines.Nodes[1].Fuel, WoodLog),
            static_cast<int64>(5));

        TestFalse(TEXT("The wrong fuel is refused outright"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceFuel, RawIron, 1,
                OutInventories, OutMachines, Failure));
    }

    // The input buffer cap is what stops a belt filling every slot with one
    // ingredient and deadlocking a two-ingredient recipe.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, RawIron, 10);
        const FCMLMachineSimulationState Machines = MakeMachines();

        // Two slots of ten would physically hold twenty; the machine caps six.
        TestFalse(TEXT("Beyond the input buffer cap is refused"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceInput, RawIron, 7,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("The refusal names the reason"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLTransferFailure::DestinationFull));
        TestTrue(TEXT("Up to the cap is allowed"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FurnaceInput, RawIron, 6,
                OutInventories, OutMachines, Failure));
    }

    // Refusals name themselves rather than leaving the caller to guess.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, RawIron, 1);
        const FCMLMachineSimulationState Machines = MakeMachines();

        TestFalse(TEXT("Zero moves nothing"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, Crate, RawIron, 0, OutInventories, OutMachines, Failure));
        TestEqual(TEXT("ZeroAmount"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLTransferFailure::ZeroAmount));

        TestFalse(TEXT("A holder cannot feed itself"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, Backpack, RawIron, 1, OutInventories, OutMachines, Failure));
        TestEqual(TEXT("SameEndpoint"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLTransferFailure::SameEndpoint));

        TestFalse(TEXT("More than is held cannot move"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, Crate, RawIron, 5, OutInventories, OutMachines, Failure));
        TestEqual(TEXT("InsufficientSource"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLTransferFailure::InsufficientSource));

        TestFalse(TEXT("An unknown item cannot move"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, Crate, FCMLStableId(0, 12345), 1,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("UnknownItem"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLTransferFailure::UnknownItem));

        TestFalse(TEXT("An absent holder cannot be a source"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                FCMLTransferEndpoint::Inventory(FCMLStableId(0, 4242)), Crate, RawIron, 1,
                OutInventories, OutMachines, Failure));
        TestEqual(TEXT("UnknownSource"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLTransferFailure::UnknownSource));

        // A machine has no storage port, and a crate has no input port: asking
        // for one is a mis-addressed transfer, not a transfer to the other.
        TestFalse(TEXT("A machine has no storage port"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FCMLTransferEndpoint::Port(FurnaceNode, ECMLMachinePortKind::Storage),
                RawIron, 1, OutInventories, OutMachines, Failure));
        TestEqual(TEXT("UnknownDestination"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLTransferFailure::UnknownDestination));
        TestFalse(TEXT("A crate has no input port"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, FCMLTransferEndpoint::Port(CrateNode, ECMLMachinePortKind::Input),
                RawIron, 1, OutInventories, OutMachines, Failure));
    }

    // Nothing is created or destroyed: a round trip returns the world to where
    // it started.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        PlaceInInventory(Inventories, RawIron, 8);
        const FCMLMachineSimulationState Machines = MakeMachines();

        FCMLInventorySimulationState AfterOut;
        FCMLMachineSimulationState MachinesOut;
        TestTrue(TEXT("Out to the crate"),
            FCMLTransferRule::TryTransfer(Inventories, Machines, Catalog,
                Backpack, Crate, RawIron, 8, AfterOut, MachinesOut, Failure));
        TestTrue(TEXT("And back again"),
            FCMLTransferRule::TryTransfer(AfterOut, MachinesOut, Catalog,
                Crate, Backpack, RawIron, 8, OutInventories, OutMachines, Failure));

        TestEqual(TEXT("Every unit came home"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], RawIron),
            static_cast<int64>(8));
        TestEqual(TEXT("The crate is empty again"),
            FCMLMachinePortOperations::TotalQuantity(OutMachines.Nodes[0].Input),
            static_cast<int64>(0));
    }
    return true;
}
#endif

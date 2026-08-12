#include "Content/CMLBootstrapCatalog.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"
#include "Simulation/CMLMachineBuildRule.h"
#include "Simulation/CMLTransferRule.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLBootstrapMachineCapacityTest,
    "CML.Gameplay.Content.BootstrapMachineCapacity",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLBootstrapMachineCapacityTest::RunTest(const FString& Parameters)
{
    const FCMLGameCatalog Catalog = FCMLBootstrapCatalog::Create();
    FCMLMachineDefinition Furnace;
    TestTrue(TEXT("The crude furnace exists"),
        Catalog.TryGetMachine(CMLContentIds::CrudeFurnace, Furnace));
    TestEqual(TEXT("The furnace input admits a bounded item buffer"),
        Furnace.InputBufferCapacityPerItem, int64{6});
    TestEqual(TEXT("The furnace fuel port admits a bounded log buffer"),
        Furnace.FuelBufferCapacityPerItem, int64{5});

    FCMLInventorySimulationState Inventories;
    FCMLInventoryState Player;
    Player.InventoryId = CMLContentIds::PlayerInventory;
    Player.ContainerDefinitionId = CMLContentIds::PlayerInventory;
    Player.Slots.SetNum(16);
    Player.Slots[0].bHasStack = true;
    Player.Slots[0].Stack.ItemId = CMLContentIds::RawIron;
    Player.Slots[0].Stack.Quantity.Value = 6;
    Inventories.Inventories.Add(Player);

    const FCMLStableId FurnaceNodeId(0, 0xF001);
    FCMLMachineSimulationState Machines;
    FCMLMachineNodeState FurnaceNode = FCMLMachineBuildRule::CreateMachine(
        FurnaceNodeId,
        CMLContentIds::CrudeFurnace,
        Furnace.InputSlots,
        Furnace.OutputSlots,
        Furnace.FuelSlots,
        FCMLMachineBuildPose());
    FurnaceNode.ActiveRecipeId = CMLContentIds::SmeltIronIngot;
    Machines.Nodes.Add(FurnaceNode);

    FCMLInventorySimulationState UpdatedInventories;
    FCMLMachineSimulationState UpdatedMachines;
    ECMLTransferFailure Failure = ECMLTransferFailure::None;
    TestTrue(TEXT("The bootstrap furnace accepts its smelting input"),
        FCMLTransferRule::TryTransfer(
            Inventories,
            Machines,
            Catalog,
            FCMLTransferEndpoint::Inventory(CMLContentIds::PlayerInventory),
            FCMLTransferEndpoint::Port(
                FurnaceNodeId, ECMLMachinePortKind::Input),
            CMLContentIds::RawIron,
            6,
            UpdatedInventories,
            UpdatedMachines,
            Failure));
    TestEqual(TEXT("All admitted ore reaches the logical input slot"),
        FCMLMachinePortOperations::Count(
            UpdatedMachines.Nodes[0].Input, CMLContentIds::RawIron),
        int64{6});
    return true;
}
#endif

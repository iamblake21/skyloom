#include "Simulation/CMLMachineSalvageRule.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Misc/AutomationTest.h"
#include "Simulation/CMLTransferRule.h"

namespace
{
    const FCMLStableId CrateNodeId(0, 500);

    FCMLDefinitionIdentity Named(const TCHAR* Key)
    {
        FCMLDefinitionIdentity Identity;
        Identity.Key = Key;
        Identity.NameKey = FString(TEXT("name.")) + Key;
        return Identity;
    }

    FCMLMachineBuildPose Pose(const int32 X, const int32 Z)
    {
        FCMLMachineBuildPose Value;
        Value.XMillimetres = X;
        Value.ZMillimetres = Z;
        return Value;
    }

    FCMLGameCatalog MakeCatalog(const int64 BackpackCapacity = 500)
    {
        using namespace CMLContentIds;
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({WoodenCrateItem, 10, 0, Named(TEXT("item.wooden_crate"))});
        Catalog.Items.Add({BeltStraight, 10, 0, Named(TEXT("item.belt_straight"))});
        Catalog.Items.Add({RawIron, 10, 0, Named(TEXT("item.raw_iron"))});
        Catalog.Containers.Add({WoodenCrate, 8, 200, Named(TEXT("container.wooden_crate"))});
        Catalog.Containers.Add(
            {PlayerInventory, 16, BackpackCapacity, Named(TEXT("container.player_inventory"))});
        return Catalog;
    }

    FCMLInventorySimulationState MakeInventories(const int32 SlotCount = 16)
    {
        FCMLInventorySimulationState Inventories;
        FCMLInventoryState Backpack;
        Backpack.InventoryId = CMLContentIds::PlayerInventory;
        Backpack.ContainerDefinitionId = CMLContentIds::PlayerInventory;
        Backpack.Slots.SetNum(SlotCount);
        Inventories.Inventories.Add(Backpack);
        return Inventories;
    }

    FCMLMachineSimulationState MakeLoadedCrate(const uint64 Cargo)
    {
        FCMLMachineSimulationState Machines;
        FCMLMachineNodeState Crate = FCMLMachineBuildRule::CreateBuffer(
            CrateNodeId, CMLContentIds::WoodenCrate, 8, Pose(0, 0));
        if (Cargo > 0)
        {
            Crate.Input.Slots[0].ItemId = CMLContentIds::RawIron;
            Crate.Input.Slots[0].Quantity.Value = Cargo;
            Crate.Output = Crate.Input;
        }
        Machines.Nodes.Add(Crate);
        return Machines;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLMachineSalvageRuleTest,
    "CML.Core.Simulation.MachineSalvageRule",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLMachineSalvageRuleTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();

    FCMLMachineSimulationState OutMachines;
    FCMLInventorySimulationState OutInventories;
    ECMLSalvageRejection Rejection = ECMLSalvageRejection::None;

    // A crate and a machine are stored under their definition id, which is not
    // the carryable item, so neither can simply echo it back.
    {
        FCMLStableId Refund;
        int64 Quantity = 0;
        TestTrue(TEXT("A crate resolves"),
            FCMLMachineSalvageRule::TryResolveRefund(
                ECMLMachineNodeKind::Buffer, WoodenCrate, Refund, Quantity));
        TestTrue(TEXT("To the carryable crate, not the container"), Refund == WoodenCrateItem);
        TestEqual(TEXT("One of it"), Quantity, static_cast<int64>(1));

        TestTrue(TEXT("A press resolves"),
            FCMLMachineSalvageRule::TryResolveRefund(
                ECMLMachineNodeKind::Machine, MechanicalPress, Refund, Quantity));
        TestTrue(TEXT("To the carryable press"), Refund == MechanicalPressItem);

        // A belt is stored under the id of the very item that placed it.
        TestTrue(TEXT("A belt resolves"),
            FCMLMachineSalvageRule::TryResolveRefund(
                ECMLMachineNodeKind::BeltModule, BeltStraight, Refund, Quantity));
        TestTrue(TEXT("To itself"), Refund == BeltStraight);

        TestFalse(TEXT("Something with no carryable form does not"),
            FCMLMachineSalvageRule::TryResolveRefund(
                ECMLMachineNodeKind::Buffer, FCMLStableId(0, 12345), Refund, Quantity));
    }

    // Dismantling refunds both the thing and everything it held.
    {
        const FCMLMachineSimulationState Machines = MakeLoadedCrate(5);
        const FCMLInventorySimulationState Inventories = MakeInventories();

        TestTrue(TEXT("A loaded crate can be taken apart"),
            FCMLMachineSalvageRule::TryApply(
                Machines, Inventories, Catalog, PlayerInventory, CrateNodeId,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("The node is gone"), OutMachines.Nodes.Num(), 0);
        TestEqual(TEXT("The crate came back"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], WoodenCrateItem),
            static_cast<int64>(1));
        TestEqual(TEXT("And so did its cargo"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], RawIron),
            static_cast<int64>(5));
    }

    // A crate's two faces are one store: counting both would refund its cargo
    // twice, which is how matter gets created.
    {
        const FCMLMachineSimulationState Machines = MakeLoadedCrate(3);
        const FCMLInventorySimulationState Inventories = MakeInventories();
        TestTrue(TEXT("It dismantles"),
            FCMLMachineSalvageRule::TryApply(
                Machines, Inventories, Catalog, PlayerInventory, CrateNodeId,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("Exactly what was inside came out, not double"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], RawIron),
            static_cast<int64>(3));
    }

    // All or nothing: if the refund does not fit, the node stays where it is
    // rather than the cargo being quietly dropped.
    {
        const FCMLMachineSimulationState Machines = MakeLoadedCrate(9);
        // One slot, and it is already taken by something else.
        FCMLInventorySimulationState Cramped = MakeInventories(1);
        Cramped.Inventories[0].Slots[0].bHasStack = true;
        Cramped.Inventories[0].Slots[0].Stack.ItemId = BeltStraight;
        Cramped.Inventories[0].Slots[0].Stack.Quantity.Value = 10;

        TestFalse(TEXT("A refund that does not fit is refused"),
            FCMLMachineSalvageRule::TryApply(
                Machines, Cramped, Catalog, PlayerInventory, CrateNodeId,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("DestinationFull"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLSalvageRejection::DestinationFull));
        TestEqual(TEXT("The node is still standing"), OutMachines.Nodes.Num(), 1);
        TestEqual(TEXT("And its cargo is still inside"),
            FCMLMachinePortOperations::Count(OutMachines.Nodes[0].Input, RawIron),
            static_cast<int64>(9));
        TestEqual(TEXT("Nothing was credited"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], RawIron),
            static_cast<int64>(0));
    }

    // Refusals name themselves.
    {
        const FCMLMachineSimulationState Machines = MakeLoadedCrate(0);
        const FCMLInventorySimulationState Inventories = MakeInventories();

        TestFalse(TEXT("A node that is not there is refused"),
            FCMLMachineSalvageRule::TryApply(
                Machines, Inventories, Catalog, PlayerInventory, FCMLStableId(0, 999),
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("SalvageTargetMissing"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLSalvageRejection::SalvageTargetMissing));

        TestFalse(TEXT("An inventory that is not there is refused"),
            FCMLMachineSalvageRule::TryApply(
                Machines, Inventories, Catalog, FCMLStableId(0, 999), CrateNodeId,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("DestinationMissing"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLSalvageRejection::DestinationMissing));

        FCMLMachineSimulationState Odd;
        Odd.Nodes.Add(FCMLMachineBuildRule::CreateBuffer(
            CrateNodeId, FCMLStableId(0, 4321), 4, Pose(0, 0)));
        TestFalse(TEXT("A node with no carryable form is refused"),
            FCMLMachineSalvageRule::TryApply(
                Odd, Inventories, Catalog, PlayerInventory, CrateNodeId,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("RefundUnknown"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLSalvageRejection::RefundUnknown));
    }

    // Build then dismantle returns the world to where it started.
    {
        FCMLInventorySimulationState Inventories = MakeInventories();
        Inventories.Inventories[0].Slots[0].bHasStack = true;
        Inventories.Inventories[0].Slots[0].Stack.ItemId = WoodenCrateItem;
        Inventories.Inventories[0].Slots[0].Stack.Quantity.Value = 1;

        FCMLMachineSimulationState Built;
        FCMLInventorySimulationState Paid;
        ECMLBuildRejection BuildRejection = ECMLBuildRejection::None;
        TestTrue(TEXT("It builds"),
            FCMLMachineBuildRule::TryApply(
                FCMLMachineSimulationState(), Inventories, Catalog, PlayerInventory,
                CrateNodeId,
                FCMLMachineBuildSpecification::Buffer(
                    WoodenCrate, WoodenCrateItem, 1, Pose(0, 0)),
                Built, Paid, BuildRejection));
        TestEqual(TEXT("The item was spent"),
            FCMLInventoryOperations::Count(Paid.Inventories[0], WoodenCrateItem),
            static_cast<int64>(0));

        TestTrue(TEXT("And dismantles"),
            FCMLMachineSalvageRule::TryApply(
                Built, Paid, Catalog, PlayerInventory, CrateNodeId,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("The item came home"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], WoodenCrateItem),
            static_cast<int64>(1));
        TestEqual(TEXT("And the world is empty again"), OutMachines.Nodes.Num(), 0);
    }
    return true;
}
#endif

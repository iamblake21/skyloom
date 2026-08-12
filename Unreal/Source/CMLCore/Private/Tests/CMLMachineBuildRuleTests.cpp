#include "Simulation/CMLMachineBuildRule.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId NewNodeId(0, 4242);

    FCMLDefinitionIdentity Named(const TCHAR* Key)
    {
        FCMLDefinitionIdentity Identity;
        Identity.Key = Key;
        Identity.NameKey = FString(TEXT("name.")) + Key;
        return Identity;
    }

    FCMLMachineBuildPose Pose(const int32 X, const int32 Z, const int32 Yaw = 0)
    {
        FCMLMachineBuildPose Value;
        Value.XMillimetres = X;
        Value.YMillimetres = 0;
        Value.ZMillimetres = Z;
        Value.YawQuarterTurns = Yaw;
        return Value;
    }

    FCMLGameCatalog MakeCatalog()
    {
        using namespace CMLContentIds;
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({WoodenCrateItem, 10, 0, Named(TEXT("item.wooden_crate"))});
        Catalog.Items.Add({MechanicalPressItem, 10, 0, Named(TEXT("item.mechanical_press"))});
        Catalog.Items.Add({MechanicalDrillItem, 10, 0, Named(TEXT("item.mechanical_drill"))});
        Catalog.Items.Add({BeltFunnel, 10, 0, Named(TEXT("item.belt_funnel"))});
        Catalog.Items.Add({BeltStraight, 10, 0, Named(TEXT("item.belt_straight"))});
        Catalog.Items.Add({RawIron, 99, 0, Named(TEXT("item.raw_iron"))});
        Catalog.Items.Add({IronPlate, 99, 0, Named(TEXT("item.iron_plate"))});

        FCMLRecipeDefinition Press;
        Press.RecipeId = PressIronPlate;
        Press.Station = ECMLCraftingStationKind::Machine;
        Press.Inputs.Add({RawIron, 1});
        Press.Outputs.Add({IronPlate, 1});
        Press.DurationMilliseconds = 3000;
        Press.Category = ECMLRecipeCategory::Materials;
        Press.Identity = Named(TEXT("recipe.press_iron_plate"));
        Catalog.Recipes.Add(Press);

        FCMLRecipeDefinition Drill;
        Drill.RecipeId = DrillRawIron;
        Drill.Station = ECMLCraftingStationKind::Machine;
        Drill.Outputs.Add({RawIron, 1});
        Drill.DurationMilliseconds = 2000;
        Drill.Category = ECMLRecipeCategory::Extraction;
        Drill.Identity = Named(TEXT("recipe.drill_raw_iron"));
        Catalog.Recipes.Add(Drill);

        FCMLMachineDefinition PressMachine;
        PressMachine.Id = MechanicalPress;
        PressMachine.InputSlots = 2;
        PressMachine.OutputSlots = 2;
        PressMachine.RequiredEnergyKind = ECMLEnergyKind::Electrical;
        PressMachine.RequiredPower = 50;
        PressMachine.SupportedRecipeIds.Add(PressIronPlate);
        PressMachine.InputBufferCapacityPerItem = 4;
        PressMachine.Identity = Named(TEXT("machine.mechanical_press"));
        Catalog.Machines.Add(PressMachine);

        FCMLMachineDefinition DrillMachine;
        DrillMachine.Id = MechanicalDrill;
        DrillMachine.InputSlots = 0;
        DrillMachine.OutputSlots = 2;
        DrillMachine.RequiredEnergyKind = ECMLEnergyKind::Thermal;
        DrillMachine.RequiredPower = 10;
        DrillMachine.SupportedRecipeIds.Add(DrillRawIron);
        DrillMachine.Identity = Named(TEXT("machine.mechanical_drill"));
        Catalog.Machines.Add(DrillMachine);

        Catalog.Containers.Add({WoodenCrate, 8, 200, Named(TEXT("container.wooden_crate"))});
        Catalog.Containers.Add(
            {PlayerInventory, 16, 500, Named(TEXT("container.player_inventory"))});
        return Catalog;
    }

    FCMLInventorySimulationState MakeInventories(const FCMLStableId& Item, const uint64 Quantity)
    {
        FCMLInventorySimulationState Inventories;
        FCMLInventoryState Backpack;
        Backpack.InventoryId = CMLContentIds::PlayerInventory;
        Backpack.ContainerDefinitionId = CMLContentIds::PlayerInventory;
        Backpack.Slots.SetNum(16);
        if (!Item.IsNone())
        {
            Backpack.Slots[0].bHasStack = true;
            Backpack.Slots[0].Stack.ItemId = Item;
            Backpack.Slots[0].Stack.Quantity.Value = Quantity;
        }
        Inventories.Inventories.Add(Backpack);
        return Inventories;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLMachineBuildRuleTest,
    "CML.Core.Simulation.MachineBuildRule",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLMachineBuildRuleTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();
    const FCMLMachineSimulationState Empty;

    FCMLMachineSimulationState OutMachines;
    FCMLInventorySimulationState OutInventories;
    ECMLBuildRejection Rejection = ECMLBuildRejection::None;

    // Placing a crate: the cost is taken and the node appears, with one store
    // reached through both faces.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(WoodenCrateItem, 2);
        const FCMLMachineBuildSpecification Crate = FCMLMachineBuildSpecification::Buffer(
            WoodenCrate, WoodenCrateItem, 1, Pose(0, 0));

        TestTrue(TEXT("A crate can be placed"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Crate,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("The node exists"), OutMachines.Nodes.Num(), 1);
        TestEqual(TEXT("It has the container's slot count"),
            OutMachines.Nodes[0].Input.Slots.Num(), 8);
        TestTrue(TEXT("Its two faces are one store"), OutMachines.Nodes[0].bInputOutputAliased);
        TestEqual(TEXT("The cost was taken"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], WoodenCrateItem),
            static_cast<int64>(1));
    }

    // Two things cannot occupy one cell.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(WoodenCrateItem, 2);
        const FCMLMachineBuildSpecification Crate = FCMLMachineBuildSpecification::Buffer(
            WoodenCrate, WoodenCrateItem, 1, Pose(0, 0));
        FCMLMachineSimulationState Occupied;
        Occupied.Nodes.Add(FCMLMachineBuildRule::CreateBuffer(
            FCMLStableId(0, 1), WoodenCrate, 8, Pose(0, 0)));

        TestFalse(TEXT("An occupied cell is refused"),
            FCMLMachineBuildRule::TryApply(
                Occupied, Inventories, Catalog, PlayerInventory, NewNodeId, Crate,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("BuildTopologyInvalid"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::BuildTopologyInvalid));
        TestEqual(TEXT("And nothing was paid"),
            FCMLInventoryOperations::Count(OutInventories.Inventories[0], WoodenCrateItem),
            static_cast<int64>(2));
    }

    // The cost pairing is checked, not trusted. A command claiming a crate costs
    // one plant fibre would otherwise be honoured.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(RawIron, 10);
        FCMLMachineBuildSpecification Cheat = FCMLMachineBuildSpecification::Buffer(
            WoodenCrate, RawIron, 1, Pose(0, 0));
        TestFalse(TEXT("A mispriced build is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Cheat,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("BuildDefinitionMissing"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::BuildDefinitionMissing));

        FCMLMachineBuildSpecification Discounted = FCMLMachineBuildSpecification::Buffer(
            WoodenCrate, WoodenCrateItem, 0, Pose(0, 0));
        TestFalse(TEXT("A free build is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Discounted,
                OutMachines, OutInventories, Rejection));
    }

    // An empty pocket refuses, and says which of the two reasons it is.
    {
        const FCMLInventorySimulationState Broke = MakeInventories(FCMLStableId::None(), 0);
        const FCMLMachineBuildSpecification Crate = FCMLMachineBuildSpecification::Buffer(
            WoodenCrate, WoodenCrateItem, 1, Pose(0, 0));
        TestFalse(TEXT("Nothing to pay with is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, Broke, Catalog, PlayerInventory, NewNodeId, Crate,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("InsufficientQuantity"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::InsufficientQuantity));

        TestFalse(TEXT("An inventory that does not exist is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, Broke, Catalog, FCMLStableId(0, 999), NewNodeId, Crate,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("BuildSourceMissing"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::BuildSourceMissing));
    }

    // A machine built with a recipe starts wanting its first ingredient, not
    // idle: it has work it cannot yet do.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(MechanicalPressItem, 1);
        const FCMLMachineBuildSpecification Press = FCMLMachineBuildSpecification::Machine(
            MechanicalPress, PressIronPlate, MechanicalPressItem, 1, Pose(1000, 0));

        TestTrue(TEXT("A press can be placed"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Press,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("It carries its recipe"),
            static_cast<int32>(OutMachines.Nodes[0].Activity),
            static_cast<int32>(ECMLMachineActivity::MissingInput));
        TestTrue(TEXT("Which is the one asked for"),
            OutMachines.Nodes[0].ActiveRecipeId == PressIronPlate);
        TestFalse(TEXT("A machine's ports are separate"),
            OutMachines.Nodes[0].bInputOutputAliased);

        // A recipe the machine does not support is not a machine it can run.
        const FCMLMachineBuildSpecification Wrong = FCMLMachineBuildSpecification::Machine(
            MechanicalPress, DrillRawIron, MechanicalPressItem, 1, Pose(1000, 0));
        TestFalse(TEXT("An unsupported recipe is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Wrong,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("BuildTopologyInvalid"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::BuildTopologyInvalid));
    }

    // The rule that has to live here rather than in the hologram: an extractor
    // with no deposit under it is refused, not accepted and left on NoRecipe.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(MechanicalDrillItem, 1);
        const FCMLMachineBuildSpecification Homeless = FCMLMachineBuildSpecification::Machine(
            MechanicalDrill, FCMLStableId::None(), MechanicalDrillItem, 1, Pose(2000, 0));
        TestFalse(TEXT("An extractor with no deposit is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Homeless,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("BuildTopologyInvalid"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::BuildTopologyInvalid));

        const FCMLMachineBuildSpecification Sited = FCMLMachineBuildSpecification::Machine(
            MechanicalDrill, DrillRawIron, MechanicalDrillItem, 1, Pose(2000, 0));
        TestTrue(TEXT("With a deposit's recipe it is allowed"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Sited,
                OutMachines, OutInventories, Rejection));

        // A press has no such requirement: it is configured later.
        const FCMLInventorySimulationState PressStock =
            MakeInventories(MechanicalPressItem, 1);
        const FCMLMachineBuildSpecification Unconfigured =
            FCMLMachineBuildSpecification::Machine(
                MechanicalPress, FCMLStableId::None(), MechanicalPressItem, 1, Pose(3000, 0));
        TestTrue(TEXT("A transformer may be placed unconfigured"),
            FCMLMachineBuildRule::TryApply(
                Empty, PressStock, Catalog, PlayerInventory, NewNodeId, Unconfigured,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("And says it has no recipe"),
            static_cast<int32>(OutMachines.Nodes[0].Activity),
            static_cast<int32>(ECMLMachineActivity::NoRecipe));
    }

    // A belt is placed stopped, with no drive on its line yet, and its two faces
    // are one store like a funnel's.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(BeltStraight, 3);
        const FCMLMachineBuildSpecification Belt = FCMLMachineBuildSpecification::BeltModule(
            BeltStraight, BeltStraight, 1, Pose(0, 1000));
        TestTrue(TEXT("A belt can be placed"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Belt,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("It starts stopped"),
            static_cast<int32>(OutMachines.Nodes[0].BeltTravelDirection),
            static_cast<int32>(ECMLBeltTravelDirection::Stopped));
        TestEqual(TEXT("And reports no drive on its line"),
            static_cast<int32>(OutMachines.Nodes[0].BeltLineStatus),
            static_cast<int32>(ECMLBeltLineStatus::MissingDrive));
        TestTrue(TEXT("Its two faces are one store"), OutMachines.Nodes[0].bInputOutputAliased);

        // Only a belt carries a polarity; anything else declaring one is
        // malformed rather than harmlessly verbose.
        FCMLMachineBuildSpecification Crate = FCMLMachineBuildSpecification::Buffer(
            WoodenCrate, WoodenCrateItem, 1, Pose(0, 2000));
        Crate.BeltTravelDirection = ECMLBeltTravelDirection::Forward;
        const FCMLInventorySimulationState CrateStock = MakeInventories(WoodenCrateItem, 1);
        TestFalse(TEXT("A crate with a travel direction is refused"),
            FCMLMachineBuildRule::TryApply(
                Empty, CrateStock, Catalog, PlayerInventory, NewNodeId, Crate,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("BuildMalformed"), static_cast<int32>(Rejection),
            static_cast<int32>(ECMLBuildRejection::BuildMalformed));
    }

    // A funnel has one slot reached from both sides.
    {
        const FCMLInventorySimulationState Inventories = MakeInventories(BeltFunnel, 1);
        const FCMLMachineBuildSpecification Funnel = FCMLMachineBuildSpecification::Funnel(
            BeltFunnel, BeltFunnel, 1, Pose(0, 3000));
        TestTrue(TEXT("A funnel can be placed"),
            FCMLMachineBuildRule::TryApply(
                Empty, Inventories, Catalog, PlayerInventory, NewNodeId, Funnel,
                OutMachines, OutInventories, Rejection));
        TestEqual(TEXT("With a single slot"), OutMachines.Nodes[0].Input.Slots.Num(), 1);
        TestTrue(TEXT("Reached from both sides"), OutMachines.Nodes[0].bInputOutputAliased);
    }
    return true;
}
#endif

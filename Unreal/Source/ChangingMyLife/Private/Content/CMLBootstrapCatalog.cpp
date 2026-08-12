#include "Content/CMLBootstrapCatalog.h"

#include "Content/CMLContentIds.h"

namespace
{
    FCMLDefinitionIdentity Identity(const TCHAR* Key, const TCHAR* NameKey)
    {
        FCMLDefinitionIdentity Result;
        Result.Key = Key;
        Result.NameKey = NameKey;
        return Result;
    }

    FCMLItemDefinition Item(
        const FCMLStableId Id,
        const TCHAR* Key,
        const TCHAR* NameKey,
        const int64 MaxStack,
        const int32 MaximumDurability = 0)
    {
        FCMLItemDefinition Result;
        Result.ItemId = Id;
        Result.Identity = Identity(Key, NameKey);
        Result.MaxStack = MaxStack;
        Result.MaximumDurability = MaximumDurability;
        return Result;
    }

    FCMLRecipeAmount Amount(const FCMLStableId ItemId, const int64 Quantity)
    {
        FCMLRecipeAmount Result;
        Result.ItemId = ItemId;
        Result.Quantity = Quantity;
        return Result;
    }

    FCMLRecipeDefinition Recipe(
        const FCMLStableId Id,
        const TCHAR* Key,
        const TCHAR* NameKey,
        const std::initializer_list<FCMLRecipeAmount> Inputs,
        const std::initializer_list<FCMLRecipeAmount> Outputs,
        const int64 DurationMilliseconds,
        const ECMLCraftingStationKind Station,
        const ECMLRecipeCategory Category)
    {
        FCMLRecipeDefinition Result;
        Result.RecipeId = Id;
        Result.Identity = Identity(Key, NameKey);
        Result.Inputs.Append(Inputs.begin(), static_cast<int32>(Inputs.size()));
        Result.Outputs.Append(Outputs.begin(), static_cast<int32>(Outputs.size()));
        Result.DurationMilliseconds = DurationMilliseconds;
        Result.Station = Station;
        Result.Category = Category;
        return Result;
    }

    FCMLContainerDefinition Container(
        const FCMLStableId Id,
        const TCHAR* Key,
        const TCHAR* NameKey,
        const int32 SlotCount,
        const int64 Capacity)
    {
        FCMLContainerDefinition Result;
        Result.Id = Id;
        Result.Identity = Identity(Key, NameKey);
        Result.SlotCount = SlotCount;
        Result.Capacity = Capacity;
        return Result;
    }

    FCMLMachineDefinition Machine(
        const FCMLStableId Id,
        const TCHAR* Key,
        const TCHAR* NameKey,
        const int32 InputSlots,
        const int32 OutputSlots,
        const std::initializer_list<FCMLStableId> SupportedRecipes,
        const int32 FuelSlots = 0,
        const FCMLStableId FuelItemId = FCMLStableId::None(),
        const int64 FuelQuantityPerCycle = 0)
    {
        FCMLMachineDefinition Result;
        Result.Id = Id;
        Result.Identity = Identity(Key, NameKey);
        Result.InputSlots = InputSlots;
        Result.OutputSlots = OutputSlots;
        Result.RequiredEnergyKind = ECMLEnergyKind::None;
        Result.RequiredPower = 0;
        Result.SupportedRecipeIds.Append(
            SupportedRecipes.begin(), static_cast<int32>(SupportedRecipes.size()));
        Result.FuelSlots = FuelSlots;
        Result.FuelItemId = FuelItemId;
        Result.FuelQuantityPerCycle = FuelQuantityPerCycle;
        // These caps are part of the authoritative admission rule, not merely
        // UI metadata. Leaving them at their zero default makes every logical
        // machine port reject otherwise valid input and fuel.
        Result.InputBufferCapacityPerItem = InputSlots > 0 ? 6 : 0;
        Result.FuelBufferCapacityPerItem = FuelSlots > 0 ? 5 : 0;
        return Result;
    }

    FCMLIslandTemplateDefinition Island(
        const FCMLStableId Id,
        const TCHAR* Key,
        const TCHAR* NameKey,
        const TCHAR* BiomeKey,
        const int32 MinimumIronDeposits,
        const int32 MaximumIronDeposits)
    {
        FCMLIslandTemplateDefinition Result;
        Result.Id = Id;
        Result.Identity = Identity(Key, NameKey);
        Result.BiomeKey = BiomeKey;
        FCMLIslandResourceDefinition Resource;
        Resource.ItemId = CMLContentIds::RawIron;
        Resource.MinimumDeposits = MinimumIronDeposits;
        Resource.MaximumDeposits = MaximumIronDeposits;
        Result.Resources.Add(Resource);
        return Result;
    }
}

FCMLGameCatalog FCMLBootstrapCatalog::Create()
{
    using namespace CMLContentIds;

    FCMLGameCatalog Catalog;
    Catalog.SchemaVersion = FCMLGameCatalog::CurrentSchemaVersion;
    Catalog.Revision.Value = TEXT("bootstrap-12");

    Catalog.Items = {
        Item(RawIron, TEXT("item.raw_iron"), TEXT("item.raw_iron.name"), 100),
        Item(IronIngot, TEXT("item.iron_ingot"), TEXT("item.iron_ingot.name"), 100),
        Item(IronPlate, TEXT("item.iron_plate"), TEXT("item.iron_plate.name"), 100),
        Item(Stone, TEXT("item.stone"), TEXT("item.stone.name"), 100),
        Item(WoodLog, TEXT("item.wood_log"), TEXT("item.wood_log.name"), 100),
        Item(PlantFiber, TEXT("item.plant_fiber"), TEXT("item.plant_fiber.name"), 100),
        Item(Stick, TEXT("item.stick"), TEXT("item.stick.name"), 100),
        Item(WorkbenchItem, TEXT("item.workbench"), TEXT("item.workbench.name"), 10),
        Item(CrudeFurnaceItem, TEXT("item.crude_furnace"), TEXT("item.crude_furnace.name"), 10),
        Item(BeltStraight, TEXT("item.belt_straight"), TEXT("item.belt_straight.name"), 50),
        Item(BeltCurve, TEXT("item.belt_curve"), TEXT("item.belt_curve.name"), 50),
        Item(BeltIncline, TEXT("item.belt_incline"), TEXT("item.belt_incline.name"), 50),
        Item(BeltCurveLeft, TEXT("item.belt_curve_left"), TEXT("item.belt_curve_left.name"), 50),
        Item(BeltSupport, TEXT("item.belt_support"), TEXT("item.belt_support.name"), 50),
        Item(BeltFunnel, TEXT("item.belt_funnel"), TEXT("item.belt_funnel.name"), 20),
        Item(BeltDriveUnit, TEXT("item.belt_drive_unit"), TEXT("item.belt_drive_unit.name"), 20),
        Item(WoodenCrateItem, TEXT("item.wooden_crate"), TEXT("item.wooden_crate.name"), 20),
        Item(MechanicalPressItem, TEXT("item.mechanical_press"), TEXT("item.mechanical_press.name"), 10),
        Item(CrudePickaxe, TEXT("item.crude_pickaxe"), TEXT("item.crude_pickaxe.name"), 1, 24),
        Item(IronPickaxe, TEXT("item.iron_pickaxe"), TEXT("item.iron_pickaxe.name"), 1, 120),
        Item(MechanicalDrillItem, TEXT("item.mechanical_drill"), TEXT("item.mechanical_drill.name"), 10),
        Item(RawCopper, TEXT("item.raw_copper"), TEXT("item.raw_copper.name"), 100),
        Item(RawTin, TEXT("item.raw_tin"), TEXT("item.raw_tin.name"), 100),
        Item(InsulatedCable, TEXT("item.insulated_cable"), TEXT("item.insulated_cable.name"), 50)
    };

    Catalog.Recipes = {
        Recipe(PressIronPlate, TEXT("recipe.press_iron_plate"), TEXT("recipe.press_iron_plate.name"),
            {Amount(IronIngot, 1)}, {Amount(IronPlate, 1)}, 5000,
            ECMLCraftingStationKind::Machine, ECMLRecipeCategory::Materials),
        Recipe(CraftCrudePickaxe, TEXT("recipe.craft_crude_pickaxe"), TEXT("recipe.craft_crude_pickaxe.name"),
            {Amount(Stone, 2), Amount(Stick, 1), Amount(PlantFiber, 2)}, {Amount(CrudePickaxe, 1)}, 5000,
            ECMLCraftingStationKind::Personal, ECMLRecipeCategory::Tools),
        Recipe(CraftWoodenCrate, TEXT("recipe.craft_wooden_crate"), TEXT("recipe.craft_wooden_crate.name"),
            {Amount(WoodLog, 4)}, {Amount(WoodenCrateItem, 1)}, 2000,
            ECMLCraftingStationKind::Personal, ECMLRecipeCategory::Structures),
        Recipe(WorkbenchIronPlate, TEXT("recipe.workbench_iron_plate"), TEXT("recipe.workbench_iron_plate.name"),
            {Amount(IronIngot, 1)}, {Amount(IronPlate, 1)}, 2000,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Materials),
        Recipe(WorkbenchBeltStraight, TEXT("recipe.workbench_belt_straight"), TEXT("recipe.workbench_belt_straight.name"),
            {Amount(IronPlate, 1), Amount(WoodLog, 1)}, {Amount(BeltStraight, 2)}, 2000,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Logistics),
        Recipe(WorkbenchBeltSupport, TEXT("recipe.workbench_belt_support"), TEXT("recipe.workbench_belt_support.name"),
            {Amount(IronPlate, 1)}, {Amount(BeltSupport, 2)}, 1500,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Logistics),
        Recipe(WorkbenchBeltFunnel, TEXT("recipe.workbench_belt_funnel"), TEXT("recipe.workbench_belt_funnel.name"),
            {Amount(IronPlate, 2)}, {Amount(BeltFunnel, 1)}, 2500,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Logistics),
        Recipe(WorkbenchMechanicalPress, TEXT("recipe.workbench_mechanical_press"), TEXT("recipe.workbench_mechanical_press.name"),
            {Amount(IronPlate, 4), Amount(WoodLog, 2)}, {Amount(MechanicalPressItem, 1)}, 5000,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Machinery),
        Recipe(WorkbenchIronPickaxe, TEXT("recipe.workbench_iron_pickaxe"), TEXT("recipe.workbench_iron_pickaxe.name"),
            {Amount(IronPlate, 2), Amount(WoodLog, 1)}, {Amount(IronPickaxe, 1)}, 4000,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Tools),
        Recipe(SmeltIronIngot, TEXT("recipe.smelt_iron_ingot"), TEXT("recipe.smelt_iron_ingot.name"),
            {Amount(RawIron, 1)}, {Amount(IronIngot, 1)}, 6000,
            ECMLCraftingStationKind::Machine, ECMLRecipeCategory::Materials),
        Recipe(WorkbenchCrudeFurnace, TEXT("recipe.workbench_crude_furnace"), TEXT("recipe.workbench_crude_furnace.name"),
            {Amount(Stone, 8), Amount(WoodLog, 4)}, {Amount(CrudeFurnaceItem, 1)}, 5000,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Machinery),
        Recipe(WorkbenchMechanicalDrill, TEXT("recipe.workbench_mechanical_drill"), TEXT("recipe.workbench_mechanical_drill.name"),
            {Amount(IronPlate, 6), Amount(WoodLog, 3)}, {Amount(MechanicalDrillItem, 1)}, 6000,
            ECMLCraftingStationKind::Workbench, ECMLRecipeCategory::Machinery),
        Recipe(DrillRawIron, TEXT("recipe.drill_raw_iron"), TEXT("recipe.drill_raw_iron.name"),
            {}, {Amount(RawIron, 1)}, 8000,
            ECMLCraftingStationKind::Machine, ECMLRecipeCategory::Extraction),
        Recipe(DrillRawCopper, TEXT("recipe.drill_raw_copper"), TEXT("recipe.drill_raw_copper.name"),
            {}, {Amount(RawCopper, 1)}, 8000,
            ECMLCraftingStationKind::Machine, ECMLRecipeCategory::Extraction),
        Recipe(DrillRawTin, TEXT("recipe.drill_raw_tin"), TEXT("recipe.drill_raw_tin.name"),
            {}, {Amount(RawTin, 1)}, 8000,
            ECMLCraftingStationKind::Machine, ECMLRecipeCategory::Extraction)
    };

    Catalog.Machines = {
        Machine(MechanicalPress, TEXT("machine.mechanical_press"), TEXT("machine.mechanical_press.name"),
            1, 1, {PressIronPlate}),
        Machine(CrudeFurnace, TEXT("machine.crude_furnace"), TEXT("machine.crude_furnace.name"),
            1, 1, {SmeltIronIngot}, 1, WoodLog, 1),
        Machine(MechanicalDrill, TEXT("machine.mechanical_drill"), TEXT("machine.mechanical_drill.name"),
            0, 1, {DrillRawIron, DrillRawCopper, DrillRawTin}, 1, WoodLog, 1)
    };

    Catalog.Containers = {
        Container(WoodenCrate, TEXT("container.wooden_crate"), TEXT("container.wooden_crate.name"), 24, 2400),
        Container(PlayerInventory, TEXT("container.player_inventory"), TEXT("container.player_inventory.name"), 16, 1600),
        Container(AirshipHold, TEXT("container.airship_hold"), TEXT("container.airship_hold.name"), 24, 4800)
    };

    Catalog.IslandTemplates = {
        Island(MeadowIsland, TEXT("island.meadow"), TEXT("island.meadow.name"), TEXT("biome.meadow"), 1, 2),
        Island(HighlandIsland, TEXT("island.highland"), TEXT("island.highland.name"), TEXT("biome.highland"), 4, 6)
    };
    return Catalog;
}

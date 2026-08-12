#include "Inventory/CMLCraftingRule.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId Ore(0, 1);
    const FCMLStableId Ingot(0, 2);
    const FCMLStableId Plate(0, 3);
    const FCMLStableId RecipeId(0, 10);
    const FCMLStableId MissingRecipe(0, 99);

    FCMLItemCatalog MakeItems(const int64 MaxStack = 10)
    {
        FCMLItemCatalog Catalog;
        Catalog.Items.Add({Ore, MaxStack});
        Catalog.Items.Add({Ingot, MaxStack});
        Catalog.Items.Add({Plate, MaxStack});
        return Catalog;
    }

    /** Two ore in, one ingot out, at a workbench. */
    FCMLRecipeCatalog MakeRecipes()
    {
        FCMLRecipeDefinition Recipe;
        Recipe.RecipeId = RecipeId;
        Recipe.Station = ECMLCraftingStationKind::Workbench;
        Recipe.Inputs.Add({Ore, 2});
        Recipe.Outputs.Add({Ingot, 1});

        FCMLRecipeCatalog Catalog;
        Catalog.Recipes.Add(Recipe);
        return Catalog;
    }

    FCMLInventoryState MakeInventory(const int32 SlotCount)
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = FCMLStableId(0, 7);
        Inventory.Slots.AddDefaulted(SlotCount);
        return Inventory;
    }

    void Fill(FCMLInventoryState& Inventory, const int32 Index, const FCMLStableId& ItemId, const int64 Quantity)
    {
        Inventory.Slots[Index].bHasStack = true;
        Inventory.Slots[Index].Stack.ItemId = ItemId;
        Inventory.Slots[Index].Stack.Quantity = FCMLNonNegativeQuantity(Quantity);
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCraftingRuleTest,
    "CML.Core.Inventory.Crafting",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCraftingRuleTest::RunTest(const FString& Parameters)
{
    const FCMLItemCatalog Items = MakeItems();
    const FCMLRecipeCatalog Recipes = MakeRecipes();
    const int64 Capacity = 1000;

    auto Craft = [&](const FCMLInventoryState& Inventory,
                     const ECMLCraftingStationKind Station,
                     const int64 Count,
                     const FCMLStableId& Id,
                     FCMLInventoryState& Updated,
                     ECMLCraftingFailure& Failure)
    {
        return FCMLCraftingRule::TryCraft(
            Inventory, Items, Recipes, Id, Station, Count, Capacity, Updated, Failure);
    };

    FCMLInventoryState Updated;
    ECMLCraftingFailure Failure = ECMLCraftingFailure::None;

    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Ore, 4);
        TestTrue(TEXT("A valid craft succeeds"),
            Craft(Inventory, ECMLCraftingStationKind::Workbench, 2, RecipeId, Updated, Failure));
        TestEqual(TEXT("Four ore were consumed"),
            FCMLInventoryOperations::Count(Updated, Ore), static_cast<int64>(0));
        TestEqual(TEXT("Two ingots were produced"),
            FCMLInventoryOperations::Count(Updated, Ingot), static_cast<int64>(2));
    }

    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Ore, 4);
        TestFalse(TEXT("An unknown recipe is refused"),
            Craft(Inventory, ECMLCraftingStationKind::Workbench, 1, MissingRecipe, Updated, Failure));
        TestEqual(TEXT("Failure is UnknownRecipe"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCraftingFailure::UnknownRecipe));

        TestFalse(TEXT("The wrong station is refused"),
            Craft(Inventory, ECMLCraftingStationKind::Personal, 1, RecipeId, Updated, Failure));
        TestEqual(TEXT("Failure is WrongStation"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCraftingFailure::WrongStation));

        TestFalse(TEXT("A zero craft count is refused"),
            Craft(Inventory, ECMLCraftingStationKind::Workbench, 0, RecipeId, Updated, Failure));
        TestEqual(TEXT("Failure is InvalidQuantity"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCraftingFailure::InvalidQuantity));
    }

    // Not enough ingredients: nothing is consumed.
    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Ore, 3);
        TestFalse(TEXT("Two crafts from three ore are refused"),
            Craft(Inventory, ECMLCraftingStationKind::Workbench, 2, RecipeId, Updated, Failure));
        TestEqual(TEXT("Failure is InsufficientIngredients"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCraftingFailure::InsufficientIngredients));
        TestEqual(TEXT("The ore is untouched"),
            FCMLInventoryOperations::Count(Updated, Ore), static_cast<int64>(3));
    }

    // The property that matters most: a craft whose output has nowhere to go
    // must not spend its ingredients.
    {
        // One slot, full of ore. Taking the ore frees it, but the ingot needs
        // that same slot - and a second recipe output would not fit.
        FCMLRecipeDefinition Bulky;
        Bulky.RecipeId = FCMLStableId(0, 11);
        Bulky.Station = ECMLCraftingStationKind::Workbench;
        Bulky.Inputs.Add({Ore, 1});
        Bulky.Outputs.Add({Ingot, 10});
        Bulky.Outputs.Add({Plate, 10});
        FCMLRecipeCatalog BulkyCatalog;
        BulkyCatalog.Recipes.Add(Bulky);

        FCMLInventoryState Inventory = MakeInventory(1);
        Fill(Inventory, 0, Ore, 1);

        FCMLInventoryState BulkyUpdated;
        ECMLCraftingFailure BulkyFailure = ECMLCraftingFailure::None;
        TestFalse(TEXT("A craft that cannot store its output is refused"),
            FCMLCraftingRule::TryCraft(
                Inventory, Items, BulkyCatalog, Bulky.RecipeId,
                ECMLCraftingStationKind::Workbench, 1, Capacity, BulkyUpdated, BulkyFailure));
        TestEqual(TEXT("Failure is InventoryFull"),
            static_cast<int32>(BulkyFailure), static_cast<int32>(ECMLCraftingFailure::InventoryFull));
        TestEqual(TEXT("The ingredient was not spent"),
            FCMLInventoryOperations::Count(BulkyUpdated, Ore), static_cast<int64>(1));
        TestEqual(TEXT("No partial output was stored"),
            FCMLInventoryOperations::Count(BulkyUpdated, Ingot), static_cast<int64>(0));
    }

    // A craft count large enough to overflow the scaled amount is refused
    // rather than wrapping into a small, plausible number.
    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Ore, 4);
        TestFalse(TEXT("An overflowing craft count is refused"),
            Craft(Inventory, ECMLCraftingStationKind::Workbench, MAX_int64, RecipeId, Updated, Failure));
        TestEqual(TEXT("Failure is ArithmeticOverflow"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCraftingFailure::ArithmeticOverflow));
    }
    return true;
}
#endif

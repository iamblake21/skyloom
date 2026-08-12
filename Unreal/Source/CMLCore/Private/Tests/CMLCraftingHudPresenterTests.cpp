#include "Presentation/CMLCraftingHudPresenter.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"

namespace
{
    constexpr int64 SlotCapacity = 1000;

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
        Catalog.Items.Add({PlantFiber, 99, 0, Named(TEXT("item.plant_fiber"))});
        Catalog.Items.Add({Stick, 99, 0, Named(TEXT("item.stick"))});
        Catalog.Items.Add({Stone, 99, 0, Named(TEXT("item.stone"))});
        Catalog.Items.Add({CrudePickaxe, 1, 60, Named(TEXT("item.crude_pickaxe"))});
        Catalog.Items.Add({RawIron, 99, 0, Named(TEXT("item.raw_iron"))});

        FCMLRecipeDefinition Pickaxe;
        Pickaxe.RecipeId = CraftCrudePickaxe;
        Pickaxe.Station = ECMLCraftingStationKind::Personal;
        Pickaxe.Inputs.Add({Stick, 2});
        Pickaxe.Inputs.Add({Stone, 3});
        Pickaxe.Outputs.Add({CrudePickaxe, 1});
        Pickaxe.Category = ECMLRecipeCategory::Tools;
        Pickaxe.Identity = Named(TEXT("recipe.craft_crude_pickaxe"));
        Catalog.Recipes.Add(Pickaxe);

        // An extraction recipe: no ingredients, produced by a drill on a
        // deposit rather than chosen from the panel.
        FCMLRecipeDefinition Drill;
        Drill.RecipeId = DrillRawIron;
        Drill.Station = ECMLCraftingStationKind::Machine;
        Drill.Outputs.Add({RawIron, 1});
        Drill.DurationMilliseconds = 2000;
        Drill.Category = ECMLRecipeCategory::Extraction;
        Drill.Identity = Named(TEXT("recipe.drill_raw_iron"));
        Catalog.Recipes.Add(Drill);
        return Catalog;
    }

    FCMLInventoryState MakeInventory()
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = CMLContentIds::PlayerInventory;
        Inventory.Slots.SetNum(FCMLInventoryHudPresenter::PlayerSlotCount);
        return Inventory;
    }

    void Place(FCMLInventoryState& Inventory, const int32 Index, const FCMLStableId& Id, const uint64 Quantity)
    {
        Inventory.Slots[Index].bHasStack = true;
        Inventory.Slots[Index].Stack.ItemId = Id;
        Inventory.Slots[Index].Stack.Quantity.Value = Quantity;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCraftingHudPresenterTest,
    "CML.Core.Presentation.CraftingHud",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCraftingHudPresenterTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();
    FCMLRecipeDefinition Pickaxe;
    TestTrue(TEXT("The recipe is in the catalog"),
        Catalog.TryGetRecipe(CraftCrudePickaxe, Pickaxe));

    // With the ingredients in hand the panel says so, and says it because the
    // rule said so.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, Stick, 4);
        Place(Inventory, 1, Stone, 5);

        FCMLCraftingRecipePresentation Presentation;
        TestTrue(TEXT("The recipe projects"),
            FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Pickaxe, 1, SlotCapacity, Presentation));

        TestEqual(TEXT("It is named in Italian"),
            Presentation.DisplayName, FString(TEXT("Piccone rudimentale")));
        TestFalse(TEXT("It has a description"), Presentation.Description.IsEmpty());
        TestTrue(TEXT("The craft is possible"), Presentation.bCanCraft);
        TestEqual(TEXT("No failure is reported"),
            static_cast<int32>(Presentation.Failure),
            static_cast<int32>(ECMLCraftingFailure::None));
        TestEqual(TEXT("Both ingredients are listed"), Presentation.Ingredients.Num(), 2);
        TestTrue(TEXT("Sticks are available"), Presentation.Ingredients[0].IsAvailable());
        TestEqual(TEXT("Two sticks are needed"),
            Presentation.Ingredients[0].Required, static_cast<int64>(2));
        TestEqual(TEXT("Four are held"),
            Presentation.Ingredients[0].Owned, static_cast<int64>(4));
        TestEqual(TEXT("The product is the pickaxe"),
            Presentation.Output.DisplayName, FString(TEXT("Piccone rudimentale")));
        TestEqual(TEXT("One is produced"),
            Presentation.Output.Quantity, static_cast<int64>(1));
        TestEqual(TEXT("A tool recipe sits under the tools tab"),
            FCMLCraftingHudPresenter::CategoryLabel(Presentation.Category),
            FString(TEXT("UTENSILI")));
    }

    // Short of one ingredient: the row that is short says so, the other does
    // not, and the button reports the rule's own reason.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, Stick, 4);
        Place(Inventory, 1, Stone, 1);

        FCMLCraftingRecipePresentation Presentation;
        TestTrue(TEXT("It still projects"),
            FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Pickaxe, 1, SlotCapacity, Presentation));
        TestFalse(TEXT("The craft is refused"), Presentation.bCanCraft);
        TestEqual(TEXT("The reason is the rule's own"),
            static_cast<int32>(Presentation.Failure),
            static_cast<int32>(ECMLCraftingFailure::InsufficientIngredients));
        TestTrue(TEXT("Sticks are still available"),
            Presentation.Ingredients[0].IsAvailable());
        TestFalse(TEXT("Stone is short"), Presentation.Ingredients[1].IsAvailable());
    }

    // A batch multiplies both sides.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, Stick, 40);
        Place(Inventory, 1, Stone, 40);

        FCMLCraftingRecipePresentation Presentation;
        TestTrue(TEXT("A batch projects"),
            FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Pickaxe, 5, SlotCapacity, Presentation));
        TestEqual(TEXT("Ten sticks are needed for five"),
            Presentation.Ingredients[0].Required, static_cast<int64>(10));
        TestEqual(TEXT("Fifteen stone are needed"),
            Presentation.Ingredients[1].Required, static_cast<int64>(15));
        TestEqual(TEXT("Five are produced"),
            Presentation.Output.Quantity, static_cast<int64>(5));
        TestEqual(TEXT("The count carries over"),
            Presentation.CraftCount, static_cast<int64>(5));
    }

    // A batch big enough to wrap the requirement is refused rather than shown
    // as a negative cost the player could "afford".
    {
        FCMLInventoryState Inventory = MakeInventory();
        FCMLCraftingRecipePresentation Presentation;
        TestFalse(TEXT("An overflowing batch is refused"),
            FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Pickaxe, MAX_int64 / 2, SlotCapacity, Presentation));
        TestFalse(TEXT("A zero batch is refused"),
            FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Pickaxe, 0, SlotCapacity, Presentation));
    }

    // An extraction recipe has no ingredient rows at all, and no tab of its own.
    {
        FCMLRecipeDefinition Drill;
        TestTrue(TEXT("The drill recipe is in the catalog"),
            Catalog.TryGetRecipe(DrillRawIron, Drill));

        FCMLInventoryState Inventory = MakeInventory();
        FCMLCraftingRecipePresentation Presentation;
        TestTrue(TEXT("An extraction recipe projects"),
            FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Drill, 1, SlotCapacity, Presentation));
        TestEqual(TEXT("It lists no ingredients"), Presentation.Ingredients.Num(), 0);
        TestEqual(TEXT("It produces ore"),
            Presentation.Output.DisplayName, FString(TEXT("Ferro grezzo")));
        TestEqual(TEXT("It falls outside the crafting tabs"),
            FCMLCraftingHudPresenter::CategoryLabel(Presentation.Category),
            FString(TEXT("ALTRO")));
    }

    // Every tab has a label.
    {
        const ECMLRecipeCategory Every[] = {
            ECMLRecipeCategory::Tools, ECMLRecipeCategory::Materials,
            ECMLRecipeCategory::Structures, ECMLRecipeCategory::Logistics,
            ECMLRecipeCategory::Machinery, ECMLRecipeCategory::Extraction};
        for (const ECMLRecipeCategory Category : Every)
        {
            TestFalse(TEXT("The tab has a label"),
                FCMLCraftingHudPresenter::CategoryLabel(Category).IsEmpty());
        }
    }
    return true;
}
#endif

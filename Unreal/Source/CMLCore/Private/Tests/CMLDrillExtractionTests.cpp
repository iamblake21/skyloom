#include "Content/CMLDrillExtraction.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"

namespace
{
    FCMLDefinitionIdentity Named(const TCHAR* Key)
    {
        FCMLDefinitionIdentity Identity;
        Identity.Key = Key;
        Identity.NameKey = FString(TEXT("name.")) + Key;
        return Identity;
    }

    FCMLRecipeDefinition Extraction(
        const FCMLStableId& Id, const FCMLStableId& Product, const TCHAR* Key)
    {
        FCMLRecipeDefinition Recipe;
        Recipe.RecipeId = Id;
        Recipe.Station = ECMLCraftingStationKind::Machine;
        Recipe.Outputs.Add({Product, 1});
        Recipe.DurationMilliseconds = 2000;
        Recipe.Category = ECMLRecipeCategory::Extraction;
        Recipe.Identity = Named(Key);
        return Recipe;
    }

    FCMLGameCatalog MakeCatalog()
    {
        using namespace CMLContentIds;
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({RawIron, 99, 0, Named(TEXT("item.raw_iron"))});
        Catalog.Items.Add({RawCopper, 99, 0, Named(TEXT("item.raw_copper"))});
        Catalog.Items.Add({RawTin, 99, 0, Named(TEXT("item.raw_tin"))});

        Catalog.Recipes.Add(Extraction(DrillRawIron, RawIron, TEXT("recipe.drill_raw_iron")));
        Catalog.Recipes.Add(Extraction(DrillRawCopper, RawCopper, TEXT("recipe.drill_raw_copper")));

        FCMLMachineDefinition Drill;
        Drill.Id = MechanicalDrill;
        Drill.InputSlots = 0;
        Drill.OutputSlots = 2;
        Drill.RequiredEnergyKind = ECMLEnergyKind::Thermal;
        Drill.SupportedRecipeIds.Add(DrillRawIron);
        Drill.SupportedRecipeIds.Add(DrillRawCopper);
        Drill.Identity = Named(TEXT("machine.mechanical_drill"));
        Catalog.Machines.Add(Drill);
        return Catalog;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLDrillExtractionTest,
    "CML.Core.Content.DrillExtraction",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLDrillExtractionTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();
    FCMLStableId RecipeId;
    ECMLDrillPlacementFailure Failure = ECMLDrillPlacementFailure::None;

    // The supported recipe list *is* the deposit-to-ore mapping; there is no
    // table to keep in step with it.
    {
        TestTrue(TEXT("An iron deposit resolves"),
            FCMLDrillExtraction::TryResolveExtraction(
                Catalog, TEXT("item.raw_iron"), RecipeId, Failure));
        TestTrue(TEXT("To the iron extraction"), RecipeId == DrillRawIron);

        TestTrue(TEXT("A copper deposit resolves"),
            FCMLDrillExtraction::TryResolveExtraction(
                Catalog, TEXT("item.raw_copper"), RecipeId, Failure));
        TestTrue(TEXT("To the copper extraction"), RecipeId == DrillRawCopper);
    }

    // Adding an ore is a catalog edit, not a code edit: tin resolves the moment
    // its recipe is listed, with nothing here changed.
    {
        TestFalse(TEXT("Tin has no extraction yet"),
            FCMLDrillExtraction::TryResolveExtraction(
                Catalog, TEXT("item.raw_tin"), RecipeId, Failure));
        TestEqual(TEXT("And says so"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLDrillPlacementFailure::NoExtractionForOre));

        FCMLGameCatalog Extended = Catalog;
        Extended.Recipes.Add(Extraction(DrillRawTin, RawTin, TEXT("recipe.drill_raw_tin")));
        Extended.Machines[0].SupportedRecipeIds.Add(DrillRawTin);
        TestTrue(TEXT("Listing the recipe is all it takes"),
            FCMLDrillExtraction::TryResolveExtraction(
                Extended, TEXT("item.raw_tin"), RecipeId, Failure));
        TestTrue(TEXT("And tin now resolves"), RecipeId == DrillRawTin);
    }

    // Every refusal is a refusal, never a fallback: extracting the wrong ore
    // silently would be worse than not placing the drill.
    {
        TestFalse(TEXT("An ore the catalog does not define is refused"),
            FCMLDrillExtraction::TryResolveExtraction(
                Catalog, TEXT("item.unobtainium"), RecipeId, Failure));
        TestEqual(TEXT("UnknownOre"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLDrillPlacementFailure::UnknownOre));
        TestTrue(TEXT("And no recipe is offered"), RecipeId.IsNone());

        FCMLGameCatalog NoDrill = Catalog;
        NoDrill.Machines.Reset();
        TestFalse(TEXT("A catalog with no drill is refused"),
            FCMLDrillExtraction::TryResolveExtraction(
                NoDrill, TEXT("item.raw_iron"), RecipeId, Failure));
        TestEqual(TEXT("DrillMissing"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLDrillPlacementFailure::DrillMissing));

        // Keys are compared ordinally, as everywhere else in the catalog.
        TestFalse(TEXT("A key differing only in case does not match"),
            FCMLDrillExtraction::TryResolveExtraction(
                Catalog, TEXT("Item.Raw_Iron"), RecipeId, Failure));
    }

    // A non-extraction recipe on the drill's list is ignored, and so is one with
    // two products: "the ore this deposit yields" would be ambiguous.
    {
        FCMLGameCatalog Odd = Catalog;
        FCMLRecipeDefinition Smelting =
            Extraction(SmeltIronIngot, RawTin, TEXT("recipe.not_really_extraction"));
        Smelting.Category = ECMLRecipeCategory::Materials;
        Smelting.Inputs.Add({RawIron, 1});
        Odd.Recipes.Add(Smelting);
        Odd.Machines[0].SupportedRecipeIds.Add(SmeltIronIngot);
        TestFalse(TEXT("A non-extraction recipe does not map a deposit"),
            FCMLDrillExtraction::TryResolveExtraction(
                Odd, TEXT("item.raw_tin"), RecipeId, Failure));

        FCMLGameCatalog TwoProducts = Catalog;
        FCMLRecipeDefinition Ambiguous =
            Extraction(DrillRawTin, RawTin, TEXT("recipe.drill_two_ores"));
        Ambiguous.Outputs.Add({RawCopper, 1});
        TwoProducts.Recipes.Add(Ambiguous);
        TwoProducts.Machines[0].SupportedRecipeIds.Add(DrillRawTin);
        TestFalse(TEXT("An extraction with two products is ignored"),
            FCMLDrillExtraction::TryResolveExtraction(
                TwoProducts, TEXT("item.raw_tin"), RecipeId, Failure));
        TestEqual(TEXT("NoExtractionForOre"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLDrillPlacementFailure::NoExtractionForOre));
    }
    return true;
}
#endif

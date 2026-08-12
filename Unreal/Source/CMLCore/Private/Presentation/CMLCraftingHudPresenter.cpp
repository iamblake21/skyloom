#include "Presentation/CMLCraftingHudPresenter.h"

#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"

namespace
{
    struct FRecipeText
    {
        const TCHAR* Name;
        const TCHAR* Description;
    };

    const TMap<FCMLStableId, FRecipeText>& RecipeTexts()
    {
        static const TMap<FCMLStableId, FRecipeText> Table = []
        {
            using namespace CMLContentIds;
            TMap<FCMLStableId, FRecipeText> Map;
            Map.Add(CraftCrudePickaxe, {TEXT("Piccone rudimentale"),
                TEXT("Uno strumento essenziale per estrarre pietra e minerali.")});
            Map.Add(CraftWoodenCrate, {TEXT("Cassa di legno"),
                TEXT("Contenitore semplice per organizzare le risorse.")});
            Map.Add(WorkbenchIronPlate, {TEXT("Piastra di ferro"),
                TEXT("Lavora un lingotto in una piastra pronta per la costruzione.")});
            Map.Add(WorkbenchBeltStraight, {TEXT("Nastro trasportatore"),
                TEXT("Trasporta materiali lungo un tratto rettilineo.")});
            Map.Add(WorkbenchBeltSupport, {TEXT("Supporto per nastro"),
                TEXT("Sostiene i nastri e ne rende leggibile il percorso.")});
            Map.Add(WorkbenchBeltFunnel, {TEXT("Imbuto"),
                TEXT("Inserisce o preleva oggetti da una linea logistica.")});
            Map.Add(WorkbenchMechanicalPress, {TEXT("Pressa meccanica"),
                TEXT("Macchina manuale per trasformare lingotti e componenti.")});
            Map.Add(WorkbenchIronPickaxe, {TEXT("Piccone di ferro"),
                TEXT("Piccone resistente per i depositi minerari più duri.")});
            Map.Add(WorkbenchMechanicalDrill, {TEXT("Estrattore meccanico"),
                TEXT("Estrae in continuo dal giacimento su cui viene piazzato. "
                     "Richiede combustibile.")});
            return Map;
        }();
        return Table;
    }

    /** Falls back to the product's own content key, as Unity did. */
    FString NameFor(const FCMLStableId& RecipeId, const FCMLItemDefinition& Output)
    {
        if (const FRecipeText* Text = RecipeTexts().Find(RecipeId))
        {
            return Text->Name;
        }
        const FString Key = Output.Identity.Key.TrimStartAndEnd();
        int32 Separator = INDEX_NONE;
        FString Value = Key.FindLastChar(TEXT('.'), Separator)
            ? Key.RightChop(Separator + 1)
            : Key;
        Value = Value.Replace(TEXT("_"), TEXT(" "));
        if (Value.IsEmpty())
        {
            return TEXT("Ricetta");
        }
        return FString::Chr(FChar::ToUpper(Value[0])) + Value.RightChop(1);
    }

    FString DescriptionFor(const FCMLStableId& RecipeId)
    {
        if (const FRecipeText* Text = RecipeTexts().Find(RecipeId))
        {
            return Text->Description;
        }
        return TEXT("Trasforma i materiali posseduti nel risultato indicato.");
    }

    /**
     * C# used a `checked` block here and let an overflow throw. There is no
     * equivalent in this port, so the multiplication is refused before it wraps:
     * a wrapped requirement would show a negative cost the player could "afford".
     */
    bool TryScale(const int64 PerCraft, const int64 CraftCount, int64& OutTotal)
    {
        if (PerCraft != 0 && CraftCount > MAX_int64 / PerCraft)
        {
            return false;
        }
        OutTotal = PerCraft * CraftCount;
        return true;
    }
}

FString FCMLCraftingHudPresenter::CategoryLabel(const ECMLRecipeCategory Category)
{
    switch (Category)
    {
        case ECMLRecipeCategory::Tools:      return TEXT("UTENSILI");
        case ECMLRecipeCategory::Materials:  return TEXT("MATERIALI");
        case ECMLRecipeCategory::Structures: return TEXT("STRUTTURE");
        case ECMLRecipeCategory::Logistics:  return TEXT("LOGISTICA");
        case ECMLRecipeCategory::Machinery:  return TEXT("MACCHINARI");
        // Unity's switch has no Extraction arm and falls through to "ALTRO".
        // Kept: an extraction recipe is produced by a drill standing on a
        // deposit, not chosen from the crafting panel, so it has no tab of its
        // own to belong to.
        default: return TEXT("ALTRO");
    }
}

bool FCMLCraftingHudPresenter::TryProject(
    const FCMLInventoryState& Inventory,
    const FCMLGameCatalog& Catalog,
    const FCMLRecipeDefinition& Recipe,
    const int64 CraftCount,
    const int64 Capacity,
    FCMLCraftingRecipePresentation& OutPresentation)
{
    OutPresentation = FCMLCraftingRecipePresentation();
    if (Recipe.Outputs.IsEmpty() || CraftCount <= 0)
    {
        return false;
    }

    const FCMLItemCatalog Items = Catalog.ToItemCatalog();

    TArray<FCMLCraftingIngredientPresentation> Ingredients;
    Ingredients.Reserve(Recipe.Inputs.Num());
    for (int32 Index = 0; Index < Recipe.Inputs.Num(); ++Index)
    {
        const FCMLRecipeAmount& Amount = Recipe.Inputs[Index];
        FCMLItemDefinition Item;
        if (!Items.TryGetItem(Amount.ItemId, Item))
        {
            // A recipe naming an ingredient the catalog does not define is
            // content the panel cannot honestly draw.
            return false;
        }

        int64 Required = 0;
        if (!TryScale(Amount.Quantity, CraftCount, Required))
        {
            return false;
        }

        FCMLCraftingIngredientPresentation& Entry = Ingredients.AddDefaulted_GetRef();
        // The icon always shows at least one unit: a zero-quantity slot would
        // draw as empty, and an ingredient row has to show its item.
        Entry.Item = FCMLInventoryHudPresenter::ProjectSlot(
            Index, Amount.ItemId, FMath::Max<int64>(1, Required), Item);
        Entry.Owned = FCMLInventoryOperations::Count(Inventory, Amount.ItemId);
        Entry.Required = Required;
    }

    const FCMLRecipeAmount& Output = Recipe.Outputs[0];
    FCMLItemDefinition OutputItem;
    if (!Items.TryGetItem(Output.ItemId, OutputItem))
    {
        return false;
    }
    int64 OutputQuantity = 0;
    if (!TryScale(Output.Quantity, CraftCount, OutputQuantity))
    {
        return false;
    }

    // Whether the craft is possible is the rule's answer, not the panel's. A
    // presenter that judged for itself would eventually disagree with the rule
    // that actually runs, and the button would lie.
    FCMLInventoryState Unused;
    ECMLCraftingFailure Failure = ECMLCraftingFailure::None;
    const bool bCanCraft = FCMLCraftingRule::TryCraft(
        Inventory,
        Items,
        Catalog.ToRecipeCatalog(),
        Recipe.RecipeId,
        Recipe.Station,
        CraftCount,
        Capacity,
        Unused,
        Failure);

    OutPresentation.RecipeId = Recipe.RecipeId;
    OutPresentation.DisplayName = NameFor(Recipe.RecipeId, OutputItem);
    OutPresentation.Description = DescriptionFor(Recipe.RecipeId);
    OutPresentation.Category = Recipe.Category;
    OutPresentation.Output = FCMLInventoryHudPresenter::ProjectSlot(
        0, Output.ItemId, OutputQuantity, OutputItem);
    OutPresentation.Ingredients = MoveTemp(Ingredients);
    OutPresentation.CraftCount = CraftCount;
    OutPresentation.bCanCraft = bCanCraft;
    OutPresentation.Failure = Failure;
    return true;
}

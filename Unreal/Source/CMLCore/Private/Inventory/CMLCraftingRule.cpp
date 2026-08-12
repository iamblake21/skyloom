#include "Inventory/CMLCraftingRule.h"

namespace
{
    /** Scales one recipe side by the craft count, refusing overflow. */
    bool TryScale(const FCMLRecipeAmount& Amount, const int64 CraftCount, int64& OutScaled)
    {
        OutScaled = 0;
        if (Amount.ItemId.IsNone() || Amount.Quantity <= 0)
        {
            return false;
        }
        if (CraftCount != 0 && Amount.Quantity > MAX_int64 / CraftCount)
        {
            return false;
        }
        OutScaled = Amount.Quantity * CraftCount;
        return true;
    }
}

bool FCMLRecipeCatalog::TryGetRecipe(
    const FCMLStableId& RecipeId,
    FCMLRecipeDefinition& OutRecipe) const
{
    for (const FCMLRecipeDefinition& Recipe : Recipes)
    {
        if (Recipe.RecipeId == RecipeId)
        {
            OutRecipe = Recipe;
            return true;
        }
    }
    return false;
}

bool FCMLCraftingRule::TryCraft(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Items,
    const FCMLRecipeCatalog& Recipes,
    const FCMLStableId& RecipeId,
    const ECMLCraftingStationKind Station,
    const int64 CraftCount,
    const int64 Capacity,
    FCMLInventoryState& OutUpdated,
    ECMLCraftingFailure& OutFailure)
{
    OutUpdated = Inventory;
    OutFailure = ECMLCraftingFailure::None;

    FCMLRecipeDefinition Recipe;
    if (!Recipes.TryGetRecipe(RecipeId, Recipe))
    {
        OutFailure = ECMLCraftingFailure::UnknownRecipe;
        return false;
    }
    if (Recipe.Station != Station)
    {
        OutFailure = ECMLCraftingFailure::WrongStation;
        return false;
    }
    if (CraftCount <= 0)
    {
        OutFailure = ECMLCraftingFailure::InvalidQuantity;
        return false;
    }
    if (Recipe.Inputs.Num() == 0 || Recipe.Outputs.Num() == 0)
    {
        OutFailure = ECMLCraftingFailure::InvalidDefinition;
        return false;
    }

    // Everything runs against a working copy. The caller's inventory is
    // replaced only once every take and every store succeeded, so a craft whose
    // output has nowhere to go does not consume its ingredients.
    FCMLInventoryState Working = Inventory;

    for (const FCMLRecipeAmount& Amount : Recipe.Inputs)
    {
        int64 Required = 0;
        if (!TryScale(Amount, CraftCount, Required))
        {
            OutFailure = ECMLCraftingFailure::ArithmeticOverflow;
            return false;
        }
        FCMLInventoryState Next;
        ECMLInventoryFailure TakeFailure = ECMLInventoryFailure::None;
        if (!FCMLInventoryOperations::TryTakeEntire(Working, Amount.ItemId, Required, Next, TakeFailure))
        {
            OutFailure = TakeFailure == ECMLInventoryFailure::InsufficientQuantity
                ? ECMLCraftingFailure::InsufficientIngredients
                : ECMLCraftingFailure::InvalidDefinition;
            return false;
        }
        Working = MoveTemp(Next);
    }

    for (const FCMLRecipeAmount& Amount : Recipe.Outputs)
    {
        int64 Produced = 0;
        if (!TryScale(Amount, CraftCount, Produced))
        {
            OutFailure = ECMLCraftingFailure::ArithmeticOverflow;
            return false;
        }
        FCMLInventoryState Next;
        ECMLInventoryFailure StoreFailure = ECMLInventoryFailure::None;
        if (!FCMLInventoryOperations::TryStoreEntire(
                Working, Items, Amount.ItemId, Produced, Capacity, Next, StoreFailure))
        {
            OutFailure = StoreFailure == ECMLInventoryFailure::CapacityExceeded
                ? ECMLCraftingFailure::InventoryFull
                : ECMLCraftingFailure::InvalidDefinition;
            return false;
        }
        Working = MoveTemp(Next);
    }

    OutUpdated = MoveTemp(Working);
    return true;
}

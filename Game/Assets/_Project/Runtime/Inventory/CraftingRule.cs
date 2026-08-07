using System;
using CML.Content;
using CML.Foundation;

namespace CML.Inventory
{
    public enum CraftingFailure : byte
    {
        None = 0,
        UnknownRecipe = 1,
        WrongStation = 2,
        InvalidQuantity = 3,
        InsufficientIngredients = 4,
        InventoryFull = 5,
        InvalidDefinition = 6,
        ArithmeticOverflow = 7,
        AuthorityBusy = 8
    }

    /// <summary>
    /// Applies one catalog recipe to one immutable inventory. Every ingredient
    /// and product is planned on detached successors; callers receive either a
    /// complete valid result or the exact original instance.
    /// </summary>
    public static class CraftingRule
    {
        public static bool TryCraft(
            InventoryState inventory,
            GameCatalog catalog,
            StableId recipeId,
            CraftingStationKind station,
            long craftCount,
            out InventoryState updated,
            out CraftingFailure failure)
        {
            updated = inventory;
            failure = CraftingFailure.None;
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!catalog.TryGetRecipe(recipeId, out var recipe))
            {
                failure = CraftingFailure.UnknownRecipe;
                return false;
            }

            if (recipe.Station != station)
            {
                failure = CraftingFailure.WrongStation;
                return false;
            }

            if (craftCount <= 0L)
            {
                failure = CraftingFailure.InvalidQuantity;
                return false;
            }

            if (recipe.Inputs == null
                || recipe.Inputs.Count == 0
                || recipe.Outputs == null
                || recipe.Outputs.Count == 0)
            {
                failure = CraftingFailure.InvalidDefinition;
                return false;
            }

            var working = inventory;
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var amount = recipe.Inputs[index];
                if (!TryScale(amount, craftCount, out var required))
                {
                    failure = CraftingFailure.ArithmeticOverflow;
                    return false;
                }

                if (!working.TryTakeEntire(
                        amount.ItemId,
                        required,
                        out working,
                        out var takeFailure))
                {
                    failure = takeFailure == InventoryFailure.InsufficientQuantity
                        ? CraftingFailure.InsufficientIngredients
                        : CraftingFailure.InvalidDefinition;
                    return false;
                }
            }

            for (var index = 0; index < recipe.Outputs.Count; index++)
            {
                var amount = recipe.Outputs[index];
                if (!TryScale(amount, craftCount, out var produced))
                {
                    failure = CraftingFailure.ArithmeticOverflow;
                    return false;
                }

                if (!working.TryStoreEntire(
                        amount.ItemId,
                        produced,
                        out working,
                        out var storeFailure))
                {
                    failure = storeFailure == InventoryFailure.CapacityExceeded
                        ? CraftingFailure.InventoryFull
                        : CraftingFailure.InvalidDefinition;
                    return false;
                }
            }

            updated = working;
            return true;
        }

        private static bool TryScale(
            RecipeAmountDefinition amount,
            long craftCount,
            out NonNegativeQuantity scaled)
        {
            scaled = NonNegativeQuantity.Zero;
            if (amount == null || amount.ItemId.IsNone || amount.Quantity <= 0L)
            {
                return false;
            }

            try
            {
                scaled = new NonNegativeQuantity(
                    checked(amount.Quantity * craftCount));
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}

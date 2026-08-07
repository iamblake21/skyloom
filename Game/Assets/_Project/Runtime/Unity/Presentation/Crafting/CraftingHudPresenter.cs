using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Unity.Presentation.Inventory;

namespace CML.Unity.Presentation.Crafting
{
    public static class CraftingHudPresenter
    {
        public static CraftingRecipePresentation Project(
            InventoryState inventory,
            GameCatalog catalog,
            RecipeDefinition recipe,
            long craftCount = 1L)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (recipe == null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            var ingredients = new List<CraftingIngredientPresentation>();
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var amount = recipe.Inputs[index];
                if (!catalog.TryGetItem(amount.ItemId, out var item))
                {
                    throw new InvalidOperationException(
                        $"Recipe '{recipe.Key}' references an unknown ingredient.");
                }

                var required = checked(amount.Quantity * craftCount);
                var owned = inventory.Count(amount.ItemId).Value;
                ingredients.Add(
                    new CraftingIngredientPresentation(
                        InventoryHudPresenter.ProjectSlot(
                            index,
                            amount.ItemId,
                            Math.Max(1L, required),
                            item),
                        owned,
                        required));
            }

            var output = recipe.Outputs[0];
            if (!catalog.TryGetItem(output.ItemId, out var outputItem))
            {
                throw new InvalidOperationException(
                    $"Recipe '{recipe.Key}' references an unknown product.");
            }

            var outputQuantity = checked(output.Quantity * craftCount);
            var canCraft = CraftingRule.TryCraft(
                inventory,
                catalog,
                recipe.Id,
                recipe.Station,
                craftCount,
                out _,
                out var failure);

            return new CraftingRecipePresentation(
                recipe,
                Name(recipe.Id, outputItem),
                Description(recipe.Id),
                InventoryHudPresenter.ProjectSlot(
                    0,
                    output.ItemId,
                    outputQuantity,
                    outputItem),
                new ReadOnlyCollection<CraftingIngredientPresentation>(
                    ingredients),
                craftCount,
                canCraft,
                failure);
        }

        public static string CategoryLabel(RecipeCategory category)
        {
            switch (category)
            {
                case RecipeCategory.Tools:
                    return "UTENSILI";
                case RecipeCategory.Materials:
                    return "MATERIALI";
                case RecipeCategory.Structures:
                    return "STRUTTURE";
                case RecipeCategory.Logistics:
                    return "LOGISTICA";
                case RecipeCategory.Machinery:
                    return "MACCHINARI";
                default:
                    return "ALTRO";
            }
        }

        private static string Name(StableId recipeId, ItemDefinition output)
        {
            if (recipeId == ContentIds.CraftCrudePickaxe)
            {
                return "Piccone rudimentale";
            }

            if (recipeId == ContentIds.CraftWoodenCrate)
            {
                return "Cassa di legno";
            }

            if (recipeId == ContentIds.WorkbenchIronPlate)
            {
                return "Piastra di ferro";
            }

            if (recipeId == ContentIds.WorkbenchBeltStraight)
            {
                return "Nastro trasportatore";
            }

            if (recipeId == ContentIds.WorkbenchBeltSupport)
            {
                return "Supporto per nastro";
            }

            if (recipeId == ContentIds.WorkbenchBeltFunnel)
            {
                return "Imbuto";
            }

            if (recipeId == ContentIds.WorkbenchMechanicalPress)
            {
                return "Pressa meccanica";
            }

            if (recipeId == ContentIds.WorkbenchIronPickaxe)
            {
                return "Piccone di ferro";
            }

            if (recipeId == ContentIds.WorkbenchMechanicalDrill)
            {
                return "Estrattore meccanico";
            }

            var value = output.Key;
            var separator = value.LastIndexOf('.');
            value = separator >= 0 ? value.Substring(separator + 1) : value;
            value = value.Replace('_', ' ');
            return value.Length == 0
                ? "Ricetta"
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string Description(StableId recipeId)
        {
            if (recipeId == ContentIds.CraftCrudePickaxe)
            {
                return "Uno strumento essenziale per estrarre pietra e minerali.";
            }

            if (recipeId == ContentIds.CraftWoodenCrate)
            {
                return "Contenitore semplice per organizzare le risorse.";
            }

            if (recipeId == ContentIds.WorkbenchIronPlate)
            {
                return "Lavora un lingotto in una piastra pronta per la costruzione.";
            }

            if (recipeId == ContentIds.WorkbenchBeltStraight)
            {
                return "Trasporta materiali lungo un tratto rettilineo.";
            }

            if (recipeId == ContentIds.WorkbenchBeltSupport)
            {
                return "Sostiene i nastri e ne rende leggibile il percorso.";
            }

            if (recipeId == ContentIds.WorkbenchBeltFunnel)
            {
                return "Inserisce o preleva oggetti da una linea logistica.";
            }

            if (recipeId == ContentIds.WorkbenchMechanicalPress)
            {
                return "Macchina manuale per trasformare lingotti e componenti.";
            }

            if (recipeId == ContentIds.WorkbenchIronPickaxe)
            {
                return "Piccone resistente per i depositi minerari più duri.";
            }

            if (recipeId == ContentIds.WorkbenchMechanicalDrill)
            {
                return "Estrae in continuo dal giacimento su cui viene piazzato. "
                    + "Richiede combustibile.";
            }

            return "Trasforma i materiali posseduti nel risultato indicato.";
        }
    }

    public sealed class CraftingRecipePresentation
    {
        public CraftingRecipePresentation(
            RecipeDefinition recipe,
            string displayName,
            string description,
            InventorySlotPresentation output,
            IReadOnlyList<CraftingIngredientPresentation> ingredients,
            long craftCount,
            bool canCraft,
            CraftingFailure failure)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            Output = output;
            Ingredients = ingredients
                ?? throw new ArgumentNullException(nameof(ingredients));
            CraftCount = craftCount;
            CanCraft = canCraft;
            Failure = failure;
        }

        public RecipeDefinition Recipe { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public InventorySlotPresentation Output { get; }

        public IReadOnlyList<CraftingIngredientPresentation> Ingredients { get; }

        public long CraftCount { get; }

        public bool CanCraft { get; }

        public CraftingFailure Failure { get; }
    }

    public readonly struct CraftingIngredientPresentation
    {
        public CraftingIngredientPresentation(
            InventorySlotPresentation item,
            long owned,
            long required)
        {
            Item = item;
            Owned = owned;
            Required = required;
        }

        public InventorySlotPresentation Item { get; }

        public long Owned { get; }

        public long Required { get; }

        public bool IsAvailable => Owned >= Required;
    }
}

using System.Linq;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Unity.Presentation.Crafting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Tests.Unity.Presentation
{
    public sealed class CraftingHudPresenterTests
    {
        private static readonly StableId Backpack =
            new StableId(0x43524146545F5549UL, 0x5F544553545F3031UL);

        [Test]
        public void RecipeProjectionShowsOwnedRequiredAndCraftability()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.Stone,
                        new NonNegativeQuantity(2)),
                    new InventoryStackRecord(
                        1,
                        ContentIds.Stick,
                        new NonNegativeQuantity(1)),
                    new InventoryStackRecord(
                        2,
                        ContentIds.PlantFiber,
                        new NonNegativeQuantity(2))
                });
            Assert.That(
                catalog.TryGetRecipe(ContentIds.CraftCrudePickaxe, out var recipe),
                Is.True);

            var view = CraftingHudPresenter.Project(inventory, catalog, recipe);

            Assert.That(view.DisplayName, Is.EqualTo("Piccone rudimentale"));
            Assert.That(view.Output.ItemId, Is.EqualTo(ContentIds.CrudePickaxe));
            Assert.That(view.CanCraft, Is.True);
            Assert.That(view.Ingredients.Count, Is.EqualTo(3));
            Assert.That(view.Ingredients.All(value => value.IsAvailable), Is.True);
        }

        [Test]
        public void WorkbenchPrefabExposesApprovedLayoutContract()
        {
            const string path =
                "Assets/_Project/Art/UI/Crafting/PF_WorkbenchHUD.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);

            var document = prefab.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
            var root = document.visualTreeAsset.CloneTree();
            Assert.That(root.Q("workbench-panel"), Is.Not.Null);
            Assert.That(root.Q("workbench-recipe-grid"), Is.Not.Null);
            Assert.That(root.Q("workbench-detail-icon"), Is.Not.Null);
            Assert.That(root.Q("workbench-materials"), Is.Not.Null);
            Assert.That(root.Q("workbench-backpack"), Is.Not.Null);
            Assert.That(root.Q("workbench-craft"), Is.Not.Null);
        }
    }
}

using System.Linq;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Unity.Presentation.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Tests.Unity.Presentation
{
    public sealed class InventoryHudPresenterTests
    {
        private static readonly StableId InventoryId =
            new StableId(0x544553545F494E56UL, 0x454E544F52595F31UL);

        [Test]
        public void SnapshotMirrorsSixteenAuthoritativeSlotsWithoutMutation()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.RawIron,
                        new NonNegativeQuantity(37)),
                    new InventoryStackRecord(
                        8,
                        ContentIds.IronPlate,
                        new NonNegativeQuantity(1))
                });
            var beforeSlots = inventory.Slots.ToArray();
            var beforeTotal = inventory.TotalQuantity;

            var snapshot = InventoryHudPresenter.Project(inventory, catalog);

            Assert.That(snapshot.Source, Is.SameAs(inventory));
            Assert.That(snapshot.Slots.Count, Is.EqualTo(16));
            Assert.That(snapshot.Slots[0].Quantity, Is.EqualTo(37));
            Assert.That(snapshot.Slots[0].DisplayName, Is.EqualTo("Ferro grezzo"));
            Assert.That(snapshot.Slots[7].IsOccupied, Is.False);
            Assert.That(snapshot.Slots[8].Quantity, Is.EqualTo(1));
            Assert.That(inventory.TotalQuantity, Is.EqualTo(beforeTotal));
            Assert.That(inventory.Slots, Is.EqualTo(beforeSlots));
        }

        [Test]
        public void ConstructionItemsUsePlayerFacingNames()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.WoodenCrateItem,
                        new NonNegativeQuantity(2L)),
                    new InventoryStackRecord(
                        1,
                        ContentIds.MechanicalPressItem,
                        new NonNegativeQuantity(1L)),
                    new InventoryStackRecord(
                        2,
                        ContentIds.BeltFunnel,
                        new NonNegativeQuantity(4L)),
                    new InventoryStackRecord(
                        3,
                        ContentIds.BeltStraight,
                        new NonNegativeQuantity(24L))
                });

            var snapshot = InventoryHudPresenter.Project(inventory, catalog);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Cassa di legno",
                    "Pressa meccanica",
                    "Imbuto",
                    "Nastro trasportatore"
                },
                snapshot.Slots
                    .Take(4)
                    .Select(slot => slot.DisplayName));
        }

        [Test]
        public void CrudePickaxeUsesItsDedicatedPresentation()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.CrudePickaxe,
                        new NonNegativeQuantity(1L))
                });

            var snapshot = InventoryHudPresenter.Project(inventory, catalog);

            Assert.That(snapshot.Slots[0].DisplayName, Is.EqualTo("Piccone rudimentale"));
            Assert.That(
                snapshot.Slots[0].IconKind,
                Is.EqualTo(InventoryIconKind.CrudePickaxe));
        }

        [Test]
        public void SlotViewHidesEmptyStateAndShowsOccupiedQuantity()
        {
            var view = new InventorySlotView(0, hotbar: true);
            view.Bind(InventorySlotPresentation.Empty(0), isSelected: true);

            Assert.That(
                view.Root.ClassListContains("inventory-slot--selected"),
                Is.True);
            Assert.That(
                view.Root.ClassListContains("inventory-slot--occupied"),
                Is.False);
            Assert.That(
                view.Root.Q<Label>(className: "slot-quantity")
                    .style.display.value,
                Is.EqualTo(DisplayStyle.None));
            Assert.That(
                view.Root.Q<VisualElement>(
                    className: "slot-durability-track")
                    .style.display.value,
                Is.EqualTo(DisplayStyle.None));

            view.Bind(
                new InventorySlotPresentation(
                    0,
                    ContentIds.RawIron,
                    37,
                    "Ferro grezzo",
                    InventoryIconKind.Ore,
                    Color.gray,
                    0.42f),
                isSelected: false,
                isInvalid: true);

            Assert.That(
                view.Root.ClassListContains("inventory-slot--selected"),
                Is.False);
            Assert.That(
                view.Root.ClassListContains("inventory-slot--occupied"),
                Is.True);
            Assert.That(
                view.Root.ClassListContains("inventory-slot--invalid"),
                Is.True);
            Assert.That(
                view.Root.Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("37"));
            Assert.That(
                view.Root.Q<VisualElement>(
                    className: "slot-durability-track")
                    .style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
            Assert.That(
                view.Root.Q<VisualElement>(
                    className: "slot-durability-fill")
                    .style.width.value.value,
                Is.EqualTo(42f).Within(0.01f));
        }

        [Test]
        public void PrefabUsesResponsiveUiToolkitContract()
        {
            const string prefabPath =
                "Assets/_Project/Art/UI/Inventory/PF_InventoryHUD.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            var document = prefab.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
            Assert.That(document.panelSettings, Is.Not.Null);
            Assert.That(
                document.panelSettings.scaleMode,
                Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
            Assert.That(
                document.panelSettings.referenceResolution,
                Is.EqualTo(new Vector2Int(1920, 1080)));
            Assert.That(
                document.panelSettings.screenMatchMode,
                Is.EqualTo(PanelScreenMatchMode.MatchWidthOrHeight));
            Assert.That(document.panelSettings.match, Is.EqualTo(0.5f));

            var root = document.visualTreeAsset.CloneTree();
            Assert.That(root.Q("inventory-panel"), Is.Not.Null);
            Assert.That(root.Q("inventory-backdrop"), Is.Not.Null);
            Assert.That(root.Q("hotbar"), Is.Not.Null);
            Assert.That(root.Q("quickbar-grid"), Is.Not.Null);
            Assert.That(root.Q("backpack-grid"), Is.Not.Null);
            Assert.That(root.Q("quick-craft-list"), Is.Not.Null);
            Assert.That(root.Q("quick-craft-feedback"), Is.Not.Null);
        }
    }
}

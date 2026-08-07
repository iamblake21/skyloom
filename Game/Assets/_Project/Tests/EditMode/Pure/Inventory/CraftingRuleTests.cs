using CML.Content;
using CML.Foundation;
using CML.Inventory;
using NUnit.Framework;

namespace CML.Tests.Pure.Inventory
{
    public sealed class CraftingRuleTests
    {
        private static readonly StableId Backpack =
            new StableId(0x43524146545F5445UL, 0x53545F4241434B50UL);

        [Test]
        public void PersonalRecipeConsumesIngredientsAndCreatesDurableTool()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    Stack(0, ContentIds.Stone, 4),
                    // Bastone e Fibra, non Tronco: la ricetta del Piccone usa
                    // i tre materiali raccoglibili a mani nude.
                    Stack(1, ContentIds.Stick, 2),
                    Stack(2, ContentIds.PlantFiber, 5)
                });

            var crafted = CraftingRule.TryCraft(
                inventory,
                catalog,
                ContentIds.CraftCrudePickaxe,
                CraftingStationKind.Personal,
                1,
                out var updated,
                out var failure);

            Assert.That(crafted, Is.True);
            Assert.That(failure, Is.EqualTo(CraftingFailure.None));
            Assert.That(updated.Count(ContentIds.Stone).Value, Is.EqualTo(2));
            Assert.That(updated.Count(ContentIds.Stick).Value, Is.EqualTo(1));
            Assert.That(updated.Count(ContentIds.PlantFiber).Value, Is.EqualTo(3));
            Assert.That(updated.Count(ContentIds.CrudePickaxe).Value, Is.EqualTo(1));
            var tool = Find(updated, ContentIds.CrudePickaxe);
            Assert.That(tool.Stack.Value.Durability.Value.Current, Is.EqualTo(24));
            Assert.That(tool.Stack.Value.Durability.Value.Maximum, Is.EqualTo(24));
        }

        [Test]
        public void MissingIngredientLeavesExactOriginalStateUntouched()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                new[] { Stack(0, ContentIds.Stone, 2) });

            var crafted = CraftingRule.TryCraft(
                inventory,
                catalog,
                ContentIds.CraftCrudePickaxe,
                CraftingStationKind.Personal,
                1,
                out var updated,
                out var failure);

            Assert.That(crafted, Is.False);
            Assert.That(failure, Is.EqualTo(CraftingFailure.InsufficientIngredients));
            Assert.That(updated, Is.SameAs(inventory));
            Assert.That(updated.Count(ContentIds.Stone).Value, Is.EqualTo(2));
        }

        [Test]
        public void OutputWithoutAFreeSlotRollsBackConsumedIngredients()
        {
            var catalog = BootstrapCatalog.Load();
            var records = new InventoryStackRecord[16];
            records[0] = Stack(0, ContentIds.Stone, 3);
            records[1] = Stack(1, ContentIds.Stick, 2);
            records[2] = Stack(2, ContentIds.PlantFiber, 4);
            for (var index = 3; index < records.Length; index++)
            {
                records[index] = Stack(index, ContentIds.RawIron, 1);
            }

            var inventory = InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                records);

            var crafted = CraftingRule.TryCraft(
                inventory,
                catalog,
                ContentIds.CraftCrudePickaxe,
                CraftingStationKind.Personal,
                1,
                out var updated,
                out var failure);

            Assert.That(crafted, Is.False);
            Assert.That(failure, Is.EqualTo(CraftingFailure.InventoryFull));
            Assert.That(updated, Is.SameAs(inventory));
            Assert.That(updated.Count(ContentIds.Stone).Value, Is.EqualTo(3));
            Assert.That(updated.Count(ContentIds.Stick).Value, Is.EqualTo(2));
            Assert.That(updated.Count(ContentIds.PlantFiber).Value, Is.EqualTo(4));
        }

        [Test]
        public void RecipeCannotBeCraftedAtTheWrongStation()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                new[] { Stack(0, ContentIds.IronIngot, 2) });

            var crafted = CraftingRule.TryCraft(
                inventory,
                catalog,
                ContentIds.WorkbenchIronPlate,
                CraftingStationKind.Personal,
                1,
                out var updated,
                out var failure);

            Assert.That(crafted, Is.False);
            Assert.That(failure, Is.EqualTo(CraftingFailure.WrongStation));
            Assert.That(updated, Is.SameAs(inventory));
        }

        private static InventoryStackRecord Stack(
            int slot,
            StableId item,
            long quantity) =>
            new InventoryStackRecord(
                slot,
                item,
                new NonNegativeQuantity(quantity));

        private static InventorySlot Find(
            InventoryState inventory,
            StableId itemId)
        {
            for (var index = 0; index < inventory.SlotCount; index++)
            {
                var slot = inventory.GetSlot(index);
                if (slot.Stack.HasValue && slot.Stack.Value.ItemId == itemId)
                {
                    return slot;
                }
            }

            Assert.Fail($"Item {itemId} was not stored.");
            return default;
        }
    }
}

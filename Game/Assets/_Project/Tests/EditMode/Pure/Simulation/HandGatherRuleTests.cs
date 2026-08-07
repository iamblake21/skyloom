using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation.Gathering;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class HandGatherRuleTests
    {
        private static readonly StableId Backpack =
            new StableId(0x47415448455F5445UL, 0x53545F4241434B50UL);

        [Test]
        public void GatheringWithEmptyHandsProducesTheWholeYield()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            var result = HandGatherRule.Gather(
                inventory,
                HandGatherTargetKind.WildFiberTuft,
                2);

            Assert.That(result.Status, Is.EqualTo(HandGatherStatus.Gathered));
            Assert.That(result.ProducedItemId, Is.EqualTo(ContentIds.PlantFiber));
            Assert.That(result.ProducedQuantity, Is.EqualTo(2L));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.PlantFiber).Value,
                Is.EqualTo(2));
        }

        [Test]
        public void FallenSticksYieldSticksAndNeverLogs()
        {
            // Il Tronco resta la resa dell'abbattimento: se questa fonte lo
            // producesse, il cerchio Piccone -> Tronco -> Piccone si
            // riaprirebbe da un'altra parte.
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            var result = HandGatherRule.Gather(
                inventory,
                HandGatherTargetKind.FallenSticks,
                3);

            Assert.That(result.Status, Is.EqualTo(HandGatherStatus.Gathered));
            Assert.That(result.ProducedItemId, Is.EqualTo(ContentIds.Stick));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.Stick).Value,
                Is.EqualTo(3));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.WoodLog).Value,
                Is.EqualTo(0));
        }

        [Test]
        public void ALoosePebbleYieldsTheSameStoneThePickaxeProduces()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            var result = HandGatherRule.Gather(
                inventory,
                HandGatherTargetKind.LoosePebble,
                1);

            Assert.That(result.Status, Is.EqualTo(HandGatherStatus.Gathered));
            Assert.That(result.ProducedItemId, Is.EqualTo(ContentIds.Stone));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.Stone).Value,
                Is.EqualTo(1));
        }

        [Test]
        public void EveryPiccioneIngredientIsReachableWithBareHands()
        {
            // The point of the whole ticket: starting from nothing, the three
            // ingredients of the Piccone all come from sources that ask for no
            // tool. If this ever fails, the opening has a dead start again.
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            foreach (var (target, units) in new[]
                     {
                         (HandGatherTargetKind.LoosePebble, 1),
                         (HandGatherTargetKind.LoosePebble, 1),
                         (HandGatherTargetKind.FallenSticks, 2),
                         (HandGatherTargetKind.WildFiberTuft, 2)
                     })
            {
                var step = HandGatherRule.Gather(inventory, target, units);
                Assert.That(step.Gathered, Is.True, $"{target} refused.");
                inventory = step.UpdatedInventory;
            }

            Assert.That(
                catalog.TryGetRecipe(
                    ContentIds.CraftCrudePickaxe, out var recipe),
                Is.True);
            foreach (var ingredient in recipe.Inputs)
            {
                Assert.That(
                    inventory.Count(ingredient.ItemId).Value,
                    Is.GreaterThanOrEqualTo(ingredient.Quantity),
                    $"Missing {ingredient.ItemId} for the Piccone.");
            }
        }

        [Test]
        public void NoToolIsConsultedOrConsumed()
        {
            // The whole point of the separate rule: a tuft is gathered by a
            // player carrying nothing at all, and nothing is spent doing it.
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            var result = HandGatherRule.Gather(
                inventory,
                HandGatherTargetKind.WildFiberTuft,
                2);

            Assert.That(result.Gathered, Is.True);
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.CrudePickaxe).Value,
                Is.EqualTo(0));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.PlantFiber).Value,
                Is.EqualTo(2));
        }

        [Test]
        public void AFullInventoryRefusesWithoutTakingHalfTheYield()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);
            // Pack the backpack with capped stone stacks until it refuses, so
            // the test does not depend on the slot count or on the aggregate
            // capacity staying what they are today.
            while (inventory.TryStoreEntire(
                       ContentIds.Stone,
                       new NonNegativeQuantity(100L),
                       out var packed,
                       out _))
            {
                inventory = packed;
            }

            var before = inventory;
            var result = HandGatherRule.Gather(
                inventory,
                HandGatherTargetKind.WildFiberTuft,
                2);

            Assert.That(
                result.Status,
                Is.EqualTo(HandGatherStatus.InventoryFull));
            Assert.That(result.ProducedQuantity, Is.EqualTo(0L));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.PlantFiber).Value,
                Is.EqualTo(0));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.Stone).Value,
                Is.EqualTo(before.Count(ContentIds.Stone).Value));
        }

        [Test]
        public void ANonPositiveYieldIsRefusedInsteadOfCreatingNothing()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            var result = HandGatherRule.Gather(
                inventory,
                HandGatherTargetKind.WildFiberTuft,
                0);

            Assert.That(
                result.Status,
                Is.EqualTo(HandGatherStatus.InvalidYield));
            Assert.That(
                result.UpdatedInventory.Count(ContentIds.PlantFiber).Value,
                Is.EqualTo(0));
        }

        [Test]
        public void AnUnknownTargetYieldsNothing()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = Empty(catalog);

            var result = HandGatherRule.Gather(
                inventory,
                (HandGatherTargetKind)200,
                2);

            Assert.That(
                result.Status,
                Is.EqualTo(HandGatherStatus.InvalidTarget));
            Assert.That(result.ProducedItemId, Is.EqualTo(StableId.None));
        }

        private static InventoryState Empty(GameCatalog catalog) =>
            InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                new InventoryStackRecord[0]);
    }
}

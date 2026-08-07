using System;
using System.Collections.Generic;
using System.Linq;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using NUnit.Framework;

namespace CML.Tests.Pure.Inventory
{
    public sealed class InventoryStateTests
    {
        private static readonly StableId InventoryId =
            new StableId(0x9000000000000000UL, 0x0000000000000001UL);

        [Test]
        public void EmptyInventoryHasStableOwnedSlots()
        {
            var catalog = BootstrapCatalog.Load();

            var inventory = InventoryState.CreateEmpty(InventoryId, catalog, ContentIds.WoodenCrate);

            Assert.That(inventory.SlotCount, Is.EqualTo(24));
            Assert.That(inventory.TotalQuantity, Is.EqualTo(NonNegativeQuantity.Zero));
            Assert.That(inventory.Slots.All(slot => slot.IsEmpty), Is.True);
            for (var index = 0; index < inventory.SlotCount; index++)
            {
                Assert.That(inventory.GetSlot(index).Address, Is.EqualTo(new InventoryAddress(InventoryId, index)));
            }
        }

        [Test]
        public void PlayerInventoryUsesTheAuthoritativeSixteenSlotDefinition()
        {
            var catalog = BootstrapCatalog.Load();

            var inventory = InventoryState.CreateEmpty(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory);

            Assert.That(inventory.ContainerDefinitionId, Is.EqualTo(ContentIds.PlayerInventory));
            Assert.That(inventory.SlotCount, Is.EqualTo(16));
            Assert.That(inventory.Slots, Has.Count.EqualTo(16));
            Assert.That(inventory.TotalQuantity, Is.EqualTo(NonNegativeQuantity.Zero));
            Assert.That(inventory.Slots.All(slot => slot.IsEmpty), Is.True);
        }

        [Test]
        public void SlotMoveCanSplitAndMergeWithoutChangingTotalQuantity()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    Record(0, ContentIds.RawIron, 60),
                    Record(1, ContentIds.RawIron, 80)
                });

            Assert.That(
                inventory.TryMoveWithinInventory(
                    0,
                    2,
                    new NonNegativeQuantity(10),
                    out var split,
                    out var splitFailure),
                Is.True);
            Assert.That(
                splitFailure,
                Is.EqualTo(InventoryFailure.None));
            Assert.That(
                split.GetSlot(0).Stack.Value.Quantity.Value,
                Is.EqualTo(50));
            Assert.That(
                split.GetSlot(2).Stack.Value.Quantity.Value,
                Is.EqualTo(10));

            Assert.That(
                split.TryMoveWithinInventory(
                    0,
                    1,
                    new NonNegativeQuantity(50),
                    out var merged,
                    out var mergeFailure),
                Is.True);
            Assert.That(
                mergeFailure,
                Is.EqualTo(InventoryFailure.None));
            Assert.That(
                merged.GetSlot(0).Stack.Value.Quantity.Value,
                Is.EqualTo(30),
                "Only the twenty free units in the destination stack move.");
            Assert.That(
                merged.GetSlot(1).Stack.Value.Quantity.Value,
                Is.EqualTo(100));
            Assert.That(
                merged.TotalQuantity,
                Is.EqualTo(inventory.TotalQuantity));
        }

        [Test]
        public void FullSlotMoveSwapsDifferentItemsAndPartialSwapIsAtomic()
        {
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    Record(0, ContentIds.RawIron, 12),
                    Record(1, ContentIds.IronPlate, 3)
                });

            Assert.That(
                inventory.TryMoveWithinInventory(
                    0,
                    1,
                    new NonNegativeQuantity(1),
                    out var rejected,
                    out var rejection),
                Is.False);
            Assert.That(
                rejection,
                Is.EqualTo(InventoryFailure.CapacityExceeded));
            Assert.That(rejected, Is.SameAs(inventory));

            Assert.That(
                inventory.TryMoveWithinInventory(
                    0,
                    1,
                    new NonNegativeQuantity(12),
                    out var swapped,
                    out var swapFailure),
                Is.True);
            Assert.That(
                swapFailure,
                Is.EqualTo(InventoryFailure.None));
            Assert.That(
                swapped.GetSlot(0).Stack.Value.ItemId,
                Is.EqualTo(ContentIds.IronPlate));
            Assert.That(
                swapped.GetSlot(0).Stack.Value.Quantity.Value,
                Is.EqualTo(3));
            Assert.That(
                swapped.GetSlot(1).Stack.Value.ItemId,
                Is.EqualTo(ContentIds.RawIron));
            Assert.That(
                swapped.GetSlot(1).Stack.Value.Quantity.Value,
                Is.EqualTo(12));
            Assert.That(
                swapped.TotalQuantity,
                Is.EqualTo(inventory.TotalQuantity));
        }

        [Test]
        public void StoreFillsCompatibleStacksBeforeEmptySlotsAndPreservesOwner()
        {
            var catalog = CatalogWithTestContainer(slotCount: 3, capacity: 12);
            var inventory = InventoryState.CreateEmpty(InventoryId, catalog, TestContainerId);

            Assert.That(
                inventory.TryStoreEntire(
                    ContentIds.RawIron,
                    new NonNegativeQuantity(7),
                    out var first,
                    out var firstFailure),
                Is.True);
            Assert.That(firstFailure, Is.EqualTo(InventoryFailure.None));

            Assert.That(
                first.TryStoreEntire(
                    ContentIds.IronPlate,
                    new NonNegativeQuantity(2),
                    out var second,
                    out var secondFailure),
                Is.True);
            Assert.That(secondFailure, Is.EqualTo(InventoryFailure.None));

            Assert.That(
                second.TryStoreEntire(
                    ContentIds.RawIron,
                    new NonNegativeQuantity(3),
                    out var final,
                    out var finalFailure),
                Is.True);
            Assert.That(finalFailure, Is.EqualTo(InventoryFailure.None));

            var ironStacks = final.Slots
                .Where(slot => slot.Stack.HasValue && slot.Stack.Value.ItemId == ContentIds.RawIron)
                .Select(slot => slot.Stack.Value)
                .ToArray();
            Assert.That(ironStacks, Has.Length.EqualTo(1));
            Assert.That(ironStacks[0].Quantity.Value, Is.EqualTo(10));
            Assert.That(ironStacks[0].Location.InventoryId, Is.EqualTo(InventoryId));
            Assert.That(final.TotalQuantity.Value, Is.EqualTo(12));
        }

        [Test]
        public void ImpossibleStoreDoesNotMutateInventory()
        {
            var catalog = CatalogWithTestContainer(1, 2);
            var inventory = InventoryState.CreateEmpty(InventoryId, catalog, TestContainerId);
            Assert.That(
                inventory.TryStoreEntire(
                    ContentIds.RawIron,
                    new NonNegativeQuantity(2),
                    out var full,
                    out _),
                Is.True);

            var beforeSlot = full.GetSlot(0);
            var succeeded = full.TryStoreEntire(
                ContentIds.RawIron,
                new NonNegativeQuantity(1),
                out var rejected,
                out var failure);

            Assert.That(succeeded, Is.False);
            Assert.That(failure, Is.EqualTo(InventoryFailure.CapacityExceeded));
            Assert.That(rejected, Is.SameAs(full));
            Assert.That(rejected.GetSlot(0), Is.EqualTo(beforeSlot));
            Assert.That(rejected.TotalQuantity.Value, Is.EqualTo(2));
        }

        [Test]
        public void ImpossibleTakeDoesNotMutateInventory()
        {
            var catalog = CatalogWithTestContainer(2, 10);
            var inventory = InventoryState.CreateEmpty(InventoryId, catalog, TestContainerId);
            Assert.That(
                inventory.TryStoreEntire(
                    ContentIds.IronIngot,
                    new NonNegativeQuantity(4),
                    out var stored,
                    out _),
                Is.True);

            var succeeded = stored.TryTakeEntire(
                ContentIds.IronIngot,
                new NonNegativeQuantity(5),
                out var rejected,
                out var failure);

            Assert.That(succeeded, Is.False);
            Assert.That(failure, Is.EqualTo(InventoryFailure.InsufficientQuantity));
            Assert.That(rejected, Is.SameAs(stored));
            Assert.That(rejected.Count(ContentIds.IronIngot).Value, Is.EqualTo(4));
        }

        [Test]
        public void RestoreRejectsDuplicateUnknownOversizedAndOverCapacityStacks()
        {
            var catalog = CatalogWithTestContainer(2, 5);

            Assert.Throws<InventoryInvariantException>(() =>
                InventoryState.Restore(
                    InventoryId,
                    catalog,
                    TestContainerId,
                    new[]
                    {
                        Record(0, ContentIds.RawIron, 1),
                        Record(0, ContentIds.IronPlate, 1)
                    }));

            Assert.Throws<InventoryInvariantException>(() =>
                InventoryState.Restore(
                    InventoryId,
                    catalog,
                    TestContainerId,
                    new[]
                    {
                        Record(0, new StableId(0xdeadUL, 0xbeefUL), 1)
                    }));

            var oversizedCatalog = CatalogWithTestContainer(1, 1000);
            Assert.Throws<InventoryInvariantException>(() =>
                InventoryState.Restore(
                    InventoryId,
                    oversizedCatalog,
                    TestContainerId,
                    new[] { Record(0, ContentIds.RawIron, 101) }));

            Assert.Throws<InventoryInvariantException>(() =>
                InventoryState.Restore(
                    InventoryId,
                    catalog,
                    TestContainerId,
                    new[]
                    {
                        Record(0, ContentIds.RawIron, 3),
                        Record(1, ContentIds.IronPlate, 3)
                    }));
        }

        [Test]
        public void RandomizedStoreAndTakeCannotLoseDuplicateOrExceedMatter()
        {
            var catalog = CatalogWithTestContainer(6, 300);
            var state = InventoryState.CreateEmpty(InventoryId, catalog, TestContainerId);
            var outside = 1000L;
            const long initialMatter = 1000L;
            var random = new Random(0x51A7C0DE);

            for (var step = 0; step < 20000; step++)
            {
                var amount = new NonNegativeQuantity(random.Next(0, 26));
                if (random.Next(0, 2) == 0)
                {
                    if (amount.Value <= outside
                        && state.TryStoreEntire(
                            ContentIds.RawIron,
                            amount,
                            out var stored,
                            out _))
                    {
                        outside -= amount.Value;
                        state = stored;
                    }
                }
                else if (state.TryTakeEntire(
                             ContentIds.RawIron,
                             amount,
                             out var taken,
                             out _))
                {
                    outside += amount.Value;
                    state = taken;
                }

                Assert.That(outside + state.TotalQuantity.Value, Is.EqualTo(initialMatter));
                Assert.That(state.TotalQuantity.Value, Is.InRange(0L, 300L));
                Assert.That(state.Slots.Count, Is.EqualTo(6));

                foreach (var slot in state.Slots)
                {
                    Assert.That(slot.Address.InventoryId, Is.EqualTo(InventoryId));
                    if (!slot.Stack.HasValue)
                    {
                        continue;
                    }

                    Assert.That(slot.Stack.Value.Location, Is.EqualTo(slot.Address));
                    Assert.That(slot.Stack.Value.Quantity.Value, Is.InRange(1L, 100L));
                }
            }
        }

        [Test]
        public void StackRejectsDefaultOwnerAndFactoryRejectsUnknownContainer()
        {
            Assert.Throws<ArgumentException>(() =>
                new ItemStack(
                    default,
                    ContentIds.RawIron,
                    new NonNegativeQuantity(1)));

            var catalog = BootstrapCatalog.Load();
            Assert.Throws<InventoryInvariantException>(() =>
                InventoryState.CreateEmpty(InventoryId, catalog, TestContainerId));
        }

        [Test]
        public void SlotLimitAndAggregateCapacityAreIndependentAtomicLimits()
        {
            var slotLimitedCatalog = CatalogWithTestContainer(1, 500);
            var slotLimited = InventoryState.CreateEmpty(
                InventoryId,
                slotLimitedCatalog,
                TestContainerId);
            Assert.That(
                slotLimited.TryStoreEntire(
                    ContentIds.RawIron,
                    new NonNegativeQuantity(100),
                    out var fullStack,
                    out _),
                Is.True);
            Assert.That(
                fullStack.TryStoreEntire(
                    ContentIds.RawIron,
                    new NonNegativeQuantity(1),
                    out var slotRejected,
                    out var slotFailure),
                Is.False);
            Assert.That(slotFailure, Is.EqualTo(InventoryFailure.CapacityExceeded));
            Assert.That(slotRejected, Is.SameAs(fullStack));

            var capacityCatalog = CatalogWithTestContainer(2, 3);
            var capacityLimited = InventoryState.CreateEmpty(
                InventoryId,
                capacityCatalog,
                TestContainerId);
            Assert.That(
                capacityLimited.TryStoreEntire(
                    ContentIds.RawIron,
                    new NonNegativeQuantity(3),
                    out var fullCapacity,
                    out _),
                Is.True);
            Assert.That(fullCapacity.StorableQuantity(ContentIds.RawIron).Value, Is.Zero);
        }

        private static readonly StableId TestContainerId =
            new StableId(0x4000000000000000UL, 0x00000000000000f0UL);

        private static GameCatalog CatalogWithTestContainer(int slotCount, long capacity)
        {
            var source = BootstrapCatalog.CreateDocument();
            var containers = new List<ContainerDefinition>(source.Containers)
            {
                new ContainerDefinition(
                    TestContainerId,
                    "container.test",
                    "container.test.name",
                    slotCount,
                    capacity)
            };

            return CatalogLoader.Load(
                new CatalogDocument(
                    source.SchemaVersion,
                    "inventory-tests-" + slotCount + "-" + capacity,
                    source.Items,
                    source.Recipes,
                    source.Machines,
                    containers,
                    source.EnergySources,
                    source.IslandTemplates));
        }

        private static InventoryStackRecord Record(int slotIndex, StableId itemId, long quantity)
        {
            return new InventoryStackRecord(
                slotIndex,
                itemId,
                new NonNegativeQuantity(quantity));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CML.Content;
using CML.Foundation;

namespace CML.Inventory
{
    /// <summary>
    /// Immutable authoritative inventory state.
    /// Mutations either return a complete valid successor or return this same
    /// instance unchanged.
    /// </summary>
    [Serializable]
    public sealed class InventoryState
    {
        [NonSerialized]
        private readonly GameCatalog _catalog;
        private readonly InventorySlot[] _slots;
        private readonly IReadOnlyList<InventorySlot> _readOnlySlots;

        private InventoryState(
            StableId inventoryId,
            GameCatalog catalog,
            ContainerDefinition definition,
            InventorySlot[] slots,
            NonNegativeQuantity totalQuantity)
        {
            InventoryId = inventoryId;
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            CatalogRevision = catalog.Revision;
            ContainerDefinitionId = definition.Id;
            Capacity = new NonNegativeQuantity(definition.Capacity);
            TotalQuantity = totalQuantity;
            _slots = slots;
            _readOnlySlots = new ReadOnlyCollection<InventorySlot>(_slots);
        }

        public StableId InventoryId { get; }

        public CatalogRevision CatalogRevision { get; }

        public StableId ContainerDefinitionId { get; }

        public NonNegativeQuantity Capacity { get; }

        public NonNegativeQuantity TotalQuantity { get; }

        public int SlotCount => _slots.Length;

        public IReadOnlyList<InventorySlot> Slots => _readOnlySlots;

        public static InventoryState CreateEmpty(
            StableId inventoryId,
            GameCatalog catalog,
            StableId containerDefinitionId)
        {
            var definition = ResolveContainer(inventoryId, catalog, containerDefinitionId);

            var slots = new InventorySlot[definition.SlotCount];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = new InventorySlot(new InventoryAddress(inventoryId, index), null);
            }

            return new InventoryState(
                inventoryId,
                catalog,
                definition,
                slots,
                NonNegativeQuantity.Zero);
        }

        public static InventoryState Restore(
            StableId inventoryId,
            GameCatalog catalog,
            StableId containerDefinitionId,
            IEnumerable<InventoryStackRecord> records)
        {
            var definition = ResolveContainer(inventoryId, catalog, containerDefinitionId);

            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var slots = new InventorySlot[definition.SlotCount];
            for (var index = 0; index < slots.Length; index++)
            {
                slots[index] = new InventorySlot(new InventoryAddress(inventoryId, index), null);
            }

            var total = NonNegativeQuantity.Zero;
            foreach (var record in records)
            {
                if (record.SlotIndex < 0 || record.SlotIndex >= slots.Length)
                {
                    throw new InventoryInvariantException(
                        $"Stack slot {record.SlotIndex} is outside inventory '{inventoryId}'.");
                }

                if (!slots[record.SlotIndex].IsEmpty)
                {
                    throw new InventoryInvariantException(
                        $"Inventory '{inventoryId}' contains more than one stack in slot {record.SlotIndex}.");
                }

                if (!catalog.TryGetItem(record.ItemId, out var item))
                {
                    throw new InventoryInvariantException(
                        $"Inventory '{inventoryId}' references unknown item '{record.ItemId}'.");
                }

                if (record.Quantity.IsZero || record.Quantity.Value > item.MaxStack)
                {
                    throw new InventoryInvariantException(
                        $"Inventory '{inventoryId}' slot {record.SlotIndex} exceeds the stack limit for '{item.Key}'.");
                }

                try
                {
                    total = total.Add(record.Quantity);
                }
                catch (OverflowException exception)
                {
                    throw new InventoryInvariantException(
                        $"Inventory '{inventoryId}' quantity overflowed: {exception.Message}");
                }

                if (total.Value > definition.Capacity)
                {
                    throw new InventoryInvariantException(
                        $"Inventory '{inventoryId}' exceeds capacity {definition.Capacity}.");
                }

                var address = new InventoryAddress(inventoryId, record.SlotIndex);
                var durability = ResolveRestoredDurability(
                    item,
                    record.Durability);
                slots[record.SlotIndex] = new InventorySlot(
                    address,
                    new ItemStack(
                        address,
                        record.ItemId,
                        record.Quantity,
                        durability));
            }

            return new InventoryState(
                inventoryId,
                catalog,
                definition,
                slots,
                total);
        }

        public InventorySlot GetSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            return _slots[slotIndex];
        }

        public NonNegativeQuantity Count(StableId itemId)
        {
            if (itemId.IsNone)
            {
                return NonNegativeQuantity.Zero;
            }

            var total = NonNegativeQuantity.Zero;
            foreach (var slot in _slots)
            {
                if (slot.Stack.HasValue && slot.Stack.Value.ItemId == itemId)
                {
                    total = total.Add(slot.Stack.Value.Quantity);
                }
            }

            return total;
        }

        public NonNegativeQuantity StorableQuantity(StableId itemId)
        {
            RequireBoundCatalog();
            if (!_catalog.TryGetItem(itemId, out var item) || item.MaxStack <= 0L)
            {
                return NonNegativeQuantity.Zero;
            }

            var remainingByCapacity = Capacity.Value - TotalQuantity.Value;
            var remainingBySlots = 0L;
            foreach (var slot in _slots)
            {
                long available;
                if (slot.IsEmpty)
                {
                    available = item.MaxStack;
                }
                else if (slot.Stack.Value.ItemId == itemId)
                {
                    available = Math.Max(0L, item.MaxStack - slot.Stack.Value.Quantity.Value);
                }
                else
                {
                    continue;
                }

                remainingBySlots = SaturatingAdd(remainingBySlots, available);
            }

            return new NonNegativeQuantity(Math.Min(remainingByCapacity, remainingBySlots));
        }

        public bool TryStoreEntire(
            StableId itemId,
            NonNegativeQuantity amount,
            out InventoryState updated,
            out InventoryFailure failure)
        {
            updated = this;
            failure = InventoryFailure.None;

            RequireBoundCatalog();
            if (!_catalog.TryGetItem(itemId, out var item) || item.MaxStack <= 0L)
            {
                failure = InventoryFailure.UnknownItem;
                return false;
            }

            if (amount.IsZero)
            {
                return true;
            }

            if (amount > StorableQuantity(itemId))
            {
                failure = InventoryFailure.CapacityExceeded;
                return false;
            }

            var nextSlots = (InventorySlot[])_slots.Clone();
            var remaining = amount.Value;

            FillCompatibleStacks(nextSlots, item, ref remaining);
            FillEmptySlots(nextSlots, item, ref remaining);

            if (remaining != 0L)
            {
                throw new InventoryInvariantException(
                    "Preflight accepted an item insertion that could not be applied.");
            }

            NonNegativeQuantity nextTotal;
            try
            {
                nextTotal = TotalQuantity.Add(amount);
            }
            catch (OverflowException)
            {
                failure = InventoryFailure.ArithmeticOverflow;
                return false;
            }

            updated = new InventoryState(
                InventoryId,
                _catalog,
                ResolveBoundContainer(),
                nextSlots,
                nextTotal);
            return true;
        }

        public bool TryTakeEntire(
            StableId itemId,
            NonNegativeQuantity amount,
            out InventoryState updated,
            out InventoryFailure failure)
        {
            updated = this;
            failure = InventoryFailure.None;

            if (itemId.IsNone)
            {
                failure = InventoryFailure.UnknownItem;
                return false;
            }

            if (amount.IsZero)
            {
                return true;
            }

            if (Count(itemId) < amount)
            {
                failure = InventoryFailure.InsufficientQuantity;
                return false;
            }

            var nextSlots = (InventorySlot[])_slots.Clone();
            var remaining = amount.Value;
            for (var index = 0; index < nextSlots.Length && remaining > 0L; index++)
            {
                var current = nextSlots[index];
                if (!current.Stack.HasValue || current.Stack.Value.ItemId != itemId)
                {
                    continue;
                }

                var stack = current.Stack.Value;
                var removed = Math.Min(remaining, stack.Quantity.Value);
                var retained = stack.Quantity.Value - removed;
                nextSlots[index] = retained == 0L
                    ? new InventorySlot(current.Address, null)
                    : new InventorySlot(
                        current.Address,
                        new ItemStack(
                            current.Address,
                            itemId,
                            new NonNegativeQuantity(retained),
                            stack.Durability));
                remaining -= removed;
            }

            if (remaining != 0L)
            {
                throw new InventoryInvariantException(
                    "Preflight accepted an item removal that could not be applied.");
            }

            var nextTotal = TotalQuantity.Subtract(amount);
            updated = new InventoryState(
                InventoryId,
                _catalog,
                ResolveBoundContainer(),
                nextSlots,
                nextTotal);
            return true;
        }

        /// <summary>
        /// Consumes one completed use from the durable tool in an exact slot.
        /// The operation never removes a broken tool: zero durability remains
        /// authoritative and visible so it can later be repaired or upgraded.
        /// </summary>
        public bool TryConsumeDurabilityAtSlot(
            int slotIndex,
            out InventoryState updated,
            out InventoryFailure failure)
        {
            updated = this;
            failure = InventoryFailure.None;
            if (slotIndex < 0 || slotIndex >= _slots.Length)
            {
                failure = InventoryFailure.InvalidDefinition;
                return false;
            }

            var slot = _slots[slotIndex];
            if (!slot.Stack.HasValue
                || !slot.Stack.Value.Durability.HasValue)
            {
                failure = InventoryFailure.InvalidDefinition;
                return false;
            }

            var stack = slot.Stack.Value;
            if (!stack.Durability.Value.TryConsumeOne(
                    out var nextDurability))
            {
                failure = InventoryFailure.InsufficientQuantity;
                return false;
            }

            var nextSlots = (InventorySlot[])_slots.Clone();
            nextSlots[slotIndex] = new InventorySlot(
                slot.Address,
                new ItemStack(
                    slot.Address,
                    stack.ItemId,
                    stack.Quantity,
                    nextDurability));
            updated = new InventoryState(
                InventoryId,
                _catalog,
                ResolveBoundContainer(),
                nextSlots,
                TotalQuantity);
            return true;
        }

        private static ContainerDefinition ResolveContainer(
            StableId inventoryId,
            GameCatalog catalog,
            StableId containerDefinitionId)
        {
            if (inventoryId.IsNone)
            {
                throw new ArgumentException("An inventory requires a non-empty stable ID.", nameof(inventoryId));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (containerDefinitionId.IsNone
                || !catalog.TryGetContainer(containerDefinitionId, out var definition))
            {
                throw new InventoryInvariantException(
                    $"Container definition '{containerDefinitionId}' does not exist in the validated catalog.");
            }

            if (definition.SlotCount <= 0 || definition.Capacity <= 0L)
            {
                throw new InventoryInvariantException("The container definition is not valid for an inventory.");
            }

            return definition;
        }

        private static ItemDurability? ResolveRestoredDurability(
            ItemDefinition item,
            ItemDurability? restored)
        {
            if (!item.HasDurability)
            {
                if (restored.HasValue)
                {
                    throw new InventoryInvariantException(
                        $"Non-durable item '{item.Key}' carries durability.");
                }

                return null;
            }

            if (item.MaxStack != 1)
            {
                throw new InventoryInvariantException(
                    $"Durable item '{item.Key}' must be non-stackable.");
            }

            var durability = restored
                ?? new ItemDurability(
                    item.MaximumDurability,
                    item.MaximumDurability);
            if (durability.Maximum != item.MaximumDurability)
            {
                throw new InventoryInvariantException(
                    $"Durability maximum for '{item.Key}' does not match " +
                    $"the catalog value {item.MaximumDurability}.");
            }

            return durability;
        }

        private void RequireBoundCatalog()
        {
            if (_catalog == null)
            {
                throw new InventoryInvariantException(
                    "Inventory state is not bound to the validated catalog that owns its definitions.");
            }
        }

        private ContainerDefinition ResolveBoundContainer()
        {
            RequireBoundCatalog();
            if (!_catalog.TryGetContainer(ContainerDefinitionId, out var definition))
            {
                throw new InventoryInvariantException(
                    $"Bound catalog no longer contains container '{ContainerDefinitionId}'.");
            }

            return definition;
        }

        private void FillCompatibleStacks(
            InventorySlot[] slots,
            ItemDefinition item,
            ref long remaining)
        {
            for (var index = 0; index < slots.Length && remaining > 0L; index++)
            {
                var current = slots[index];
                if (!current.Stack.HasValue || current.Stack.Value.ItemId != item.Id)
                {
                    continue;
                }

                var stack = current.Stack.Value;
                var available = item.MaxStack - stack.Quantity.Value;
                var inserted = Math.Min(remaining, available);
                if (inserted == 0L)
                {
                    continue;
                }

                slots[index] = new InventorySlot(
                    current.Address,
                    new ItemStack(
                        current.Address,
                        item.Id,
                        new NonNegativeQuantity(
                            stack.Quantity.Value + inserted),
                        stack.Durability));
                remaining -= inserted;
            }
        }

        private void FillEmptySlots(
            InventorySlot[] slots,
            ItemDefinition item,
            ref long remaining)
        {
            for (var index = 0; index < slots.Length && remaining > 0L; index++)
            {
                var current = slots[index];
                if (!current.IsEmpty)
                {
                    continue;
                }

                var inserted = Math.Min(remaining, item.MaxStack);
                slots[index] = new InventorySlot(
                    current.Address,
                    new ItemStack(
                        current.Address,
                        item.Id,
                        new NonNegativeQuantity(inserted),
                        item.HasDurability
                            ? new ItemDurability(
                                item.MaximumDurability,
                                item.MaximumDurability)
                            : (ItemDurability?)null));
                remaining -= inserted;
            }
        }

        /// <summary>
        /// Sposta fino a <paramref name="amount"/> unità dallo slot di origine a
        /// quello di destinazione, dentro questo stesso inventario.
        ///
        /// Serve perché il resto dell'API ragiona per quantità di oggetto e
        /// sceglie da sé gli slot: bastava a depositare e prelevare, non a
        /// riordinare. Ma quale slot contiene cosa è stato canonico — lo dice il
        /// serializzatore INV — quindi il riordino è una mutazione vera e ha
        /// bisogno di una primitiva sua.
        ///
        /// Tre esiti, nell'ordine in cui vengono tentati:
        /// unione se la destinazione ha lo stesso oggetto e capienza di pila;
        /// scambio se ha un oggetto diverso e si sposta l'intera pila d'origine;
        /// spostamento semplice se la destinazione è vuota.
        /// Uno scambio parziale non esiste: non c'è dove mettere il resto.
        /// </summary>
        public bool TryMoveWithinInventory(
            int sourceSlotIndex,
            int destinationSlotIndex,
            NonNegativeQuantity amount,
            out InventoryState updated,
            out InventoryFailure failure)
        {
            updated = this;
            failure = InventoryFailure.None;

            if (sourceSlotIndex < 0
                || sourceSlotIndex >= _slots.Length
                || destinationSlotIndex < 0
                || destinationSlotIndex >= _slots.Length)
            {
                failure = InventoryFailure.InvalidDefinition;
                return false;
            }

            if (sourceSlotIndex == destinationSlotIndex)
            {
                // Non è un errore: posare una pila dove già si trova è un gesto
                // normale dell'interfaccia e deve semplicemente non fare nulla.
                return true;
            }

            RequireBoundCatalog();
            var source = _slots[sourceSlotIndex];
            if (!source.Stack.HasValue)
            {
                failure = InventoryFailure.InsufficientQuantity;
                return false;
            }

            var sourceStack = source.Stack.Value;
            var moving = amount.IsZero ? sourceStack.Quantity : amount;
            if (moving > sourceStack.Quantity)
            {
                failure = InventoryFailure.InsufficientQuantity;
                return false;
            }

            if (!_catalog.TryGetItem(sourceStack.ItemId, out var item) || item.MaxStack <= 0L)
            {
                failure = InventoryFailure.UnknownItem;
                return false;
            }

            var destination = _slots[destinationSlotIndex];
            var nextSlots = (InventorySlot[])_slots.Clone();

            if (!destination.Stack.HasValue)
            {
                WriteSlot(
                    nextSlots,
                    destinationSlotIndex,
                    sourceStack.ItemId,
                    moving.Value,
                    sourceStack.Durability);
                WriteSlot(
                    nextSlots,
                    sourceSlotIndex,
                    sourceStack.ItemId,
                    sourceStack.Quantity.Value - moving.Value,
                    sourceStack.Durability);
            }
            else if (destination.Stack.Value.ItemId == sourceStack.ItemId)
            {
                var room = item.MaxStack - destination.Stack.Value.Quantity.Value;
                if (room <= 0L)
                {
                    failure = InventoryFailure.CapacityExceeded;
                    return false;
                }

                var merged = Math.Min(room, moving.Value);
                WriteSlot(
                    nextSlots,
                    destinationSlotIndex,
                    sourceStack.ItemId,
                    destination.Stack.Value.Quantity.Value + merged,
                    destination.Stack.Value.Durability);
                WriteSlot(
                    nextSlots,
                    sourceSlotIndex,
                    sourceStack.ItemId,
                    sourceStack.Quantity.Value - merged,
                    sourceStack.Durability);
            }
            else
            {
                if (moving != sourceStack.Quantity)
                {
                    // Scambio parziale: il resto della pila d'origine non avrebbe
                    // dove restare, perché il suo slot va occupato dall'altro
                    // oggetto. Meglio rifiutare che inventare una destinazione.
                    failure = InventoryFailure.CapacityExceeded;
                    return false;
                }

                var other = destination.Stack.Value;
                WriteSlot(
                    nextSlots,
                    destinationSlotIndex,
                    sourceStack.ItemId,
                    sourceStack.Quantity.Value,
                    sourceStack.Durability);
                WriteSlot(
                    nextSlots,
                    sourceSlotIndex,
                    other.ItemId,
                    other.Quantity.Value,
                    other.Durability);
            }

            updated = new InventoryState(
                InventoryId,
                _catalog,
                _catalog.TryGetContainer(ContainerDefinitionId, out var definition)
                    ? definition
                    : throw new InventoryInvariantException(
                        $"Container '{ContainerDefinitionId}' is no longer in the catalog."),
                nextSlots,
                TotalQuantity);
            return true;
        }

        /// <summary>
        /// Riscrive uno slot. Quantità zero significa slot vuoto: una pila da zero
        /// non esiste, ed è l'invariante che <see cref="ItemStack"/> difende.
        /// </summary>
        private static void WriteSlot(
            InventorySlot[] slots,
            int slotIndex,
            StableId itemId,
            long quantity,
            ItemDurability? durability = null)
        {
            var address = slots[slotIndex].Address;
            slots[slotIndex] = quantity <= 0L
                ? new InventorySlot(address, null)
                : new InventorySlot(
                    address,
                    new ItemStack(
                        address,
                        itemId,
                        new NonNegativeQuantity(quantity),
                        durability));
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (right > long.MaxValue - left)
            {
                return long.MaxValue;
            }

            return left + right;
        }
    }
}

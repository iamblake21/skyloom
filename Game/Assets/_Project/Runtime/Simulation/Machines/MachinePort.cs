using System;
using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Role of a port inside the node that owns it. A buffer node exposes a single
    /// Storage port and uses it in both directions, so a link always reads from the
    /// source node's output port and writes into the destination node's input port
    /// without needing to know which kind of node it connects.
    /// </summary>
    public enum MachinePortKind : byte
    {
        Storage = 1,
        Input = 2,
        Output = 3,
        Fuel = 4
    }

    /// <summary>
    /// One slot of a port: either an item with a positive quantity, or nothing.
    /// The two halves cannot disagree, which removes the "empty stack of iron"
    /// state that would otherwise be representable and would have to be validated
    /// everywhere it is read.
    /// </summary>
    [Serializable]
    public readonly struct MachinePortSlot : IEquatable<MachinePortSlot>
    {
        public static readonly MachinePortSlot Empty = default;

        public MachinePortSlot(StableId itemId, NonNegativeQuantity quantity)
        {
            if (itemId.IsNone != quantity.IsZero)
            {
                throw new ArgumentException(
                    "A port slot holds either an item with a positive quantity or nothing.");
            }

            ItemId = itemId;
            Quantity = quantity;
        }

        public StableId ItemId { get; }

        public NonNegativeQuantity Quantity { get; }

        public bool IsEmpty => ItemId.IsNone;

        public bool Equals(MachinePortSlot other)
        {
            return ItemId == other.ItemId && Quantity == other.Quantity;
        }

        public override bool Equals(object obj) => obj is MachinePortSlot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (ItemId.GetHashCode() * 397) ^ Quantity.GetHashCode();
            }
        }

        public static bool operator ==(MachinePortSlot left, MachinePortSlot right) => left.Equals(right);

        public static bool operator !=(MachinePortSlot left, MachinePortSlot right) => !left.Equals(right);
    }

    /// <summary>
    /// Slot-bounded item buffer owned by a node of the machine graph.
    ///
    /// A port is bounded by its slots alone: each slot holds at most the stack limit
    /// of the item inside it, and there is no aggregate capacity. A machine buffer is
    /// sized by the machine that owns it, not by a container definition, and a port
    /// is not a player container: it has no drag-and-drop semantics and, when it
    /// belongs to a machine, it admits only what the active recipe consumes. That
    /// admission rule is the reason this is a distinct type from
    /// <c>CML.Inventory.InventoryState</c> rather than a reuse of it.
    ///
    /// Every mutation is all-or-nothing. A partially applied store would leave the
    /// authoritative state holding items that no transfer accounted for.
    /// </summary>
    [Serializable]
    public sealed class MachinePort
    {
        private readonly MachinePortSlot[] _slots;

        public MachinePort(MachinePortKind kind, int slotCount)
        {
            if (kind < MachinePortKind.Storage || kind > MachinePortKind.Fuel)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            // Zero slots is legal and means "this port exists but can never hold
            // anything". It is what an extractor's input is: the machine draws
            // from the geological deposit it stands on, not from an item that
            // arrives through a port. Every operation then behaves correctly
            // without a special case — the port reads empty, refuses stores and
            // refuses takes — which is exactly the contract wanted.
            if (slotCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slotCount),
                    slotCount,
                    "A port cannot have a negative slot count.");
            }

            Kind = kind;
            _slots = new MachinePortSlot[slotCount];
        }

        public MachinePortKind Kind { get; }

        public int SlotCount => _slots.Length;

        public bool IsEmpty
        {
            get
            {
                for (var index = 0; index < _slots.Length; index++)
                {
                    if (!_slots[index].IsEmpty)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public NonNegativeQuantity TotalQuantity
        {
            get
            {
                var total = NonNegativeQuantity.Zero;
                for (var index = 0; index < _slots.Length; index++)
                {
                    total = total.Add(_slots[index].Quantity);
                }

                return total;
            }
        }

        public MachinePortSlot GetSlot(int slotIndex)
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
            for (var index = 0; index < _slots.Length; index++)
            {
                if (_slots[index].ItemId == itemId)
                {
                    total = total.Add(_slots[index].Quantity);
                }
            }

            return total;
        }

        /// <summary>
        /// How much of the item this port could still accept, counting room left in
        /// compatible stacks plus every empty slot at the item's stack limit.
        /// </summary>
        public NonNegativeQuantity StorableQuantity(StableId itemId, GameCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!catalog.TryGetItem(itemId, out var item) || item.MaxStack <= 0L)
            {
                return NonNegativeQuantity.Zero;
            }

            var storable = 0L;
            for (var index = 0; index < _slots.Length; index++)
            {
                var slot = _slots[index];
                long available;
                if (slot.IsEmpty)
                {
                    available = item.MaxStack;
                }
                else if (slot.ItemId == itemId)
                {
                    available = Math.Max(0L, item.MaxStack - slot.Quantity.Value);
                }
                else
                {
                    continue;
                }

                storable = SaturatingAdd(storable, available);
            }

            return new NonNegativeQuantity(storable);
        }

        /// <summary>
        /// Stores the whole amount or nothing. Compatible stacks are topped up before
        /// empty slots are opened, so a port does not fragment while it still has room.
        /// </summary>
        internal bool TryStore(StableId itemId, NonNegativeQuantity amount, GameCatalog catalog)
        {
            if (amount.IsZero)
            {
                return true;
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!catalog.TryGetItem(itemId, out var item) || item.MaxStack <= 0L)
            {
                return false;
            }

            if (amount > StorableQuantity(itemId, catalog))
            {
                return false;
            }

            var remaining = amount.Value;
            for (var index = 0; index < _slots.Length && remaining > 0L; index++)
            {
                var slot = _slots[index];
                if (slot.IsEmpty || slot.ItemId != itemId)
                {
                    continue;
                }

                var inserted = Math.Min(remaining, item.MaxStack - slot.Quantity.Value);
                if (inserted <= 0L)
                {
                    continue;
                }

                _slots[index] = new MachinePortSlot(
                    itemId,
                    new NonNegativeQuantity(slot.Quantity.Value + inserted));
                remaining -= inserted;
            }

            for (var index = 0; index < _slots.Length && remaining > 0L; index++)
            {
                if (!_slots[index].IsEmpty)
                {
                    continue;
                }

                var inserted = Math.Min(remaining, item.MaxStack);
                _slots[index] = new MachinePortSlot(itemId, new NonNegativeQuantity(inserted));
                remaining -= inserted;
            }

            if (remaining != 0L)
            {
                throw new SimulationInvariantException(
                    "A port accepted a store that its own preflight could not apply.");
            }

            return true;
        }

        /// <summary>Removes the whole amount or nothing, draining the lowest slots first.</summary>
        internal bool TryTake(StableId itemId, NonNegativeQuantity amount)
        {
            if (amount.IsZero)
            {
                return true;
            }

            if (itemId.IsNone || Count(itemId) < amount)
            {
                return false;
            }

            var remaining = amount.Value;
            for (var index = 0; index < _slots.Length && remaining > 0L; index++)
            {
                var slot = _slots[index];
                if (slot.ItemId != itemId)
                {
                    continue;
                }

                var removed = Math.Min(remaining, slot.Quantity.Value);
                var retained = slot.Quantity.Value - removed;
                _slots[index] = retained == 0L
                    ? MachinePortSlot.Empty
                    : new MachinePortSlot(itemId, new NonNegativeQuantity(retained));
                remaining -= removed;
            }

            if (remaining != 0L)
            {
                throw new SimulationInvariantException(
                    "A port accepted a take that its own preflight could not apply.");
            }

            return true;
        }

        /// <summary>
        /// The item in the lowest occupied slot. Links without a filter use this, so
        /// their choice depends on slot order alone and never on iteration luck.
        /// </summary>
        internal bool TryPeekFirstItem(out StableId itemId)
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                if (!_slots[index].IsEmpty)
                {
                    itemId = _slots[index].ItemId;
                    return true;
                }
            }

            itemId = StableId.None;
            return false;
        }

        public MachinePort DeepClone()
        {
            var clone = new MachinePort(Kind, _slots.Length);
            Array.Copy(_slots, clone._slots, _slots.Length);
            return clone;
        }

        internal void ValidateInvariants(GameCatalog catalog, string location)
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                var slot = _slots[index];
                if (slot.IsEmpty)
                {
                    continue;
                }

                if (slot.Quantity.IsZero)
                {
                    throw new SimulationInvariantException(
                        $"{location} slot {index} holds an item with a zero quantity.");
                }

                if (catalog == null)
                {
                    continue;
                }

                if (!catalog.TryGetItem(slot.ItemId, out var item))
                {
                    throw new SimulationInvariantException(
                        $"{location} slot {index} references item {slot.ItemId}, "
                        + "which the validated catalog does not contain.");
                }

                if (slot.Quantity.Value > item.MaxStack)
                {
                    throw new SimulationInvariantException(
                        $"{location} slot {index} holds {slot.Quantity} of '{item.Key}', "
                        + $"above its stack limit {item.MaxStack}.");
                }
            }
        }

        private static long SaturatingAdd(long left, long right)
        {
            return right > long.MaxValue - left ? long.MaxValue : left + right;
        }
    }
}

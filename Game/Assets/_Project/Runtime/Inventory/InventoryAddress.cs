using System;
using CML.Foundation;

namespace CML.Inventory
{
    /// <summary>
    /// Canonical authoritative location of an item stack.
    /// A stack is never represented without the inventory and slot that own it.
    /// </summary>
    [Serializable]
    public readonly struct InventoryAddress : IEquatable<InventoryAddress>, IComparable<InventoryAddress>
    {
        public InventoryAddress(StableId inventoryId, int slotIndex)
        {
            if (inventoryId.IsNone)
            {
                throw new ArgumentException("An inventory address requires a non-empty inventory ID.", nameof(inventoryId));
            }

            if (slotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex), "A slot index cannot be negative.");
            }

            InventoryId = inventoryId;
            SlotIndex = slotIndex;
        }

        public StableId InventoryId { get; }

        public int SlotIndex { get; }

        public bool IsValid => !InventoryId.IsNone && SlotIndex >= 0;

        public int CompareTo(InventoryAddress other)
        {
            var inventoryComparison = InventoryId.CompareTo(other.InventoryId);
            return inventoryComparison != 0 ? inventoryComparison : SlotIndex.CompareTo(other.SlotIndex);
        }

        public bool Equals(InventoryAddress other)
        {
            return InventoryId == other.InventoryId && SlotIndex == other.SlotIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is InventoryAddress other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (InventoryId.GetHashCode() * 397) ^ SlotIndex;
            }
        }

        public override string ToString()
        {
            return InventoryId + ":" + SlotIndex;
        }

        public static bool operator ==(InventoryAddress left, InventoryAddress right) => left.Equals(right);

        public static bool operator !=(InventoryAddress left, InventoryAddress right) => !left.Equals(right);
    }
}

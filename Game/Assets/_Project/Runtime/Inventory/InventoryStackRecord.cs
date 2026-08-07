using System;
using CML.Foundation;

namespace CML.Inventory
{
    /// <summary>
    /// Persistence-facing stack payload. Ownership is restored from the target
    /// inventory ID and slot index rather than serialized twice.
    /// </summary>
    [Serializable]
    public readonly struct InventoryStackRecord
    {
        public InventoryStackRecord(
            int slotIndex,
            StableId itemId,
            NonNegativeQuantity quantity,
            ItemDurability? durability = null)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = quantity;
            Durability = durability;
        }

        public int SlotIndex { get; }

        public StableId ItemId { get; }

        public NonNegativeQuantity Quantity { get; }

        public ItemDurability? Durability { get; }
    }
}

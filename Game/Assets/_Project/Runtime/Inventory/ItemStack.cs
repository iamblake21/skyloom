using System;
using CML.Foundation;

namespace CML.Inventory
{
    /// <summary>
    /// Immutable item quantity with exactly one authoritative inventory location.
    /// Empty slots are represented by the absence of a stack, never by a zero stack.
    /// </summary>
    [Serializable]
    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        public ItemStack(
            InventoryAddress location,
            StableId itemId,
            NonNegativeQuantity quantity,
            ItemDurability? durability = null)
        {
            if (!location.IsValid)
            {
                throw new ArgumentException(
                    "A stack requires a valid authoritative inventory location.",
                    nameof(location));
            }

            if (itemId.IsNone)
            {
                throw new ArgumentException("A stack requires a non-empty item ID.", nameof(itemId));
            }

            if (quantity.IsZero)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "A stored stack must contain at least one item.");
            }

            Location = location;
            ItemId = itemId;
            Quantity = quantity;
            Durability = durability;
        }

        public InventoryAddress Location { get; }

        public StableId ItemId { get; }

        public NonNegativeQuantity Quantity { get; }

        public ItemDurability? Durability { get; }

        public bool Equals(ItemStack other)
        {
            return Location == other.Location
                && ItemId == other.ItemId
                && Quantity == other.Quantity
                && Nullable.Equals(Durability, other.Durability);
        }

        public override bool Equals(object obj)
        {
            return obj is ItemStack other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Location.GetHashCode();
                hash = (hash * 397) ^ ItemId.GetHashCode();
                hash = (hash * 397) ^ Quantity.GetHashCode();
                return (hash * 397) ^ (Durability?.GetHashCode() ?? 0);
            }
        }

        public static bool operator ==(ItemStack left, ItemStack right) => left.Equals(right);

        public static bool operator !=(ItemStack left, ItemStack right) => !left.Equals(right);
    }

    /// <summary>
    /// Immutable durability payload owned by one non-stackable inventory item.
    /// Zero is a valid current value: a broken tool remains in its slot.
    /// </summary>
    [Serializable]
    public readonly struct ItemDurability : IEquatable<ItemDurability>
    {
        public ItemDurability(int current, int maximum)
        {
            if (maximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            if (current < 0 || current > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(current));
            }

            Current = current;
            Maximum = maximum;
        }

        public int Current { get; }

        public int Maximum { get; }

        public bool IsBroken => Current == 0;

        public float Normalized => (float)Current / Maximum;

        public bool TryConsumeOne(out ItemDurability updated)
        {
            if (IsBroken)
            {
                updated = this;
                return false;
            }

            updated = new ItemDurability(Current - 1, Maximum);
            return true;
        }

        public bool Equals(ItemDurability other) =>
            Current == other.Current && Maximum == other.Maximum;

        public override bool Equals(object obj) =>
            obj is ItemDurability other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Current * 397) ^ Maximum;
            }
        }

        public static bool operator ==(
            ItemDurability left,
            ItemDurability right) =>
            left.Equals(right);

        public static bool operator !=(
            ItemDurability left,
            ItemDurability right) =>
            !left.Equals(right);
    }
}

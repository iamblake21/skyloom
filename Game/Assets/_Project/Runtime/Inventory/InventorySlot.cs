using System;

namespace CML.Inventory
{
    /// <summary>
    /// Immutable slot. Its address exists even while the slot is empty.
    /// </summary>
    [Serializable]
    public readonly struct InventorySlot : IEquatable<InventorySlot>
    {
        internal InventorySlot(InventoryAddress address, ItemStack? stack)
        {
            if (stack.HasValue && stack.Value.Location != address)
            {
                throw new InventoryInvariantException(
                    $"Stack owner '{stack.Value.Location}' does not match slot owner '{address}'.");
            }

            Address = address;
            Stack = stack;
        }

        public InventoryAddress Address { get; }

        public ItemStack? Stack { get; }

        public bool IsEmpty => !Stack.HasValue;

        public bool Equals(InventorySlot other)
        {
            return Address == other.Address && Nullable.Equals(Stack, other.Stack);
        }

        public override bool Equals(object obj)
        {
            return obj is InventorySlot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Address.GetHashCode() * 397) ^ (Stack?.GetHashCode() ?? 0);
            }
        }

        public static bool operator ==(InventorySlot left, InventorySlot right) => left.Equals(right);

        public static bool operator !=(InventorySlot left, InventorySlot right) => !left.Equals(right);
    }
}

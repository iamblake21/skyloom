using System;
using CML.Foundation;
using CML.Simulation.CanonicalEncoding;

namespace CML.Simulation.Inventories
{
    /// <summary>
    /// Canonical INV subtree. Slots are encoded in position order and empty slots are
    /// encoded too, for the same reason as in the machine ports: which slot holds what
    /// decides how the next insertion lands, so it is part of the future.
    /// </summary>
    public static class InventoryCanonicalSerializer
    {
        public const uint SchemaRevision = 1U;

        public static byte[] Serialize(InventorySimulationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state.ValidateInvariants(null);
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(2UL);
                writer.WriteTag(1UL);
                writer.WriteUnsigned(SchemaRevision);
                writer.WriteTag(2UL);
                writer.WriteBytes(SerializeInventories(state));
                return writer.ToArray();
            }
        }

        private static byte[] SerializeInventories(InventorySimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.Count);
                foreach (var pair in state.Inventories)
                {
                    var inventory = pair.Value;
                    using (var element = new CanonicalWriter())
                    {
                        element.WriteFieldCount(3UL);
                        element.WriteTag(1UL);
                        element.WriteBytes(SerializeStableId(inventory.InventoryId));
                        element.WriteTag(2UL);
                        element.WriteBytes(
                            SerializeStableId(inventory.ContainerDefinitionId));
                        element.WriteTag(3UL);
                        using (var slots = new CanonicalWriter())
                        {
                            slots.WriteUnsigned((ulong)inventory.SlotCount);
                            for (var index = 0; index < inventory.SlotCount; index++)
                            {
                                var slot = inventory.GetSlot(index);
                                var itemId = slot.Stack.HasValue
                                    ? slot.Stack.Value.ItemId
                                    : StableId.None;
                                var quantity = slot.Stack.HasValue
                                    ? slot.Stack.Value.Quantity.Value
                                    : 0L;
                                using (var slotElement = new CanonicalWriter())
                                {
                                    slotElement.WriteFieldCount(2UL);
                                    slotElement.WriteTag(1UL);
                                    slotElement.WriteBytes(SerializeStableId(itemId));
                                    slotElement.WriteTag(2UL);
                                    slotElement.WriteUnsigned((ulong)quantity);
                                    slots.WriteBytes(slotElement.ToArray());
                                }
                            }

                            element.WriteBytes(slots.ToArray());
                        }

                        collection.WriteBytes(element.ToArray());
                    }
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializeStableId(StableId value)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(2UL);
                writer.WriteTag(1UL);
                writer.WriteUnsigned(value.High);
                writer.WriteTag(2UL);
                writer.WriteUnsigned(value.Low);
                return writer.ToArray();
            }
        }
    }
}

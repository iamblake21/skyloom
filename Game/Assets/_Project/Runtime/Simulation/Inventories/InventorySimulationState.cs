using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Inventory;

namespace CML.Simulation.Inventories
{
    /// <summary>
    /// The authoritative inventories, in id order.
    ///
    /// Until now an inventory existed only where it was displayed: the HUD built one
    /// for the review scene and owned it. That is enough to look at, and not enough to
    /// transfer out of — a transfer needs a state whose hash means something, and a
    /// hash over a state the presentation layer invented means nothing.
    ///
    /// <see cref="InventoryState"/> is immutable, so a mutation replaces an entry
    /// rather than editing one, and a deep clone is a new dictionary over the same
    /// references. That is not a shortcut: an entry cannot be changed by anyone holding
    /// it, so sharing it between the authoritative state and a clone is safe by
    /// construction.
    /// </summary>
    public sealed class InventorySimulationState
    {
        private readonly SortedDictionary<StableId, InventoryState> _inventories =
            new SortedDictionary<StableId, InventoryState>();

        internal IEnumerable<KeyValuePair<StableId, InventoryState>> Inventories => _inventories;

        public int Count => _inventories.Count;

        public bool IsEmpty => _inventories.Count == 0;

        internal void Add(InventoryState inventory)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            _inventories.Add(inventory.InventoryId, inventory);
        }

        /// <summary>
        /// Publishes a successor for an inventory that already exists. Replacing an
        /// absent id would create one silently, and an inventory has to be declared.
        /// </summary>
        internal void Replace(InventoryState inventory)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (!_inventories.ContainsKey(inventory.InventoryId))
            {
                throw new SimulationInvariantException(
                    $"Inventory {inventory.InventoryId} is not part of the authoritative state.");
            }

            _inventories[inventory.InventoryId] = inventory;
        }

        /// <summary>No clone: the value is immutable, so the caller cannot alter it.</summary>
        public bool TryGet(StableId id, out InventoryState inventory)
        {
            return _inventories.TryGetValue(id, out inventory);
        }

        public InventorySimulationState DeepClone()
        {
            var clone = new InventorySimulationState();
            foreach (var pair in _inventories)
            {
                clone._inventories.Add(pair.Key, pair.Value);
            }

            return clone;
        }

        internal IReadOnlyList<StableId> GetPersistentIdsCanonical()
        {
            var ids = new StableId[_inventories.Count];
            var index = 0;
            foreach (var pair in _inventories)
            {
                ids[index++] = pair.Key;
            }

            Array.Sort(ids);
            return ids;
        }

        public void ValidateInvariants(GameCatalog catalog)
        {
            foreach (var pair in _inventories)
            {
                var inventory = pair.Value;
                if (pair.Key != inventory.InventoryId)
                {
                    throw new SimulationInvariantException(
                        "An inventory dictionary key does not match its id.");
                }

                if (inventory.TotalQuantity > inventory.Capacity)
                {
                    throw new SimulationInvariantException(
                        $"Inventory {inventory.InventoryId} holds {inventory.TotalQuantity} "
                        + $"above its capacity {inventory.Capacity}.");
                }

                if (catalog == null)
                {
                    continue;
                }

                if (!catalog.TryGetContainer(inventory.ContainerDefinitionId, out var definition))
                {
                    throw new SimulationInvariantException(
                        $"Inventory {inventory.InventoryId} references container "
                        + $"{inventory.ContainerDefinitionId}, which the validated catalog "
                        + "does not contain.");
                }

                if (inventory.SlotCount != definition.SlotCount)
                {
                    throw new SimulationInvariantException(
                        $"Inventory {inventory.InventoryId} has {inventory.SlotCount} slots "
                        + $"where '{definition.Key}' declares {definition.SlotCount}.");
                }
            }
        }

        /// <summary>
        /// Declares the inventories of a world. Callers build each one with
        /// <see cref="InventoryState.CreateEmpty"/> or <see cref="InventoryState.Restore"/>,
        /// so an inventory still cannot exist without a container definition from the
        /// validated catalog.
        /// </summary>
        public static InventorySimulationState Create(
            GameCatalog catalog,
            params InventoryState[] inventories)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var state = new InventorySimulationState();
            if (inventories != null)
            {
                for (var index = 0; index < inventories.Length; index++)
                {
                    state.Add(inventories[index]);
                }
            }

            state.ValidateInvariants(catalog);
            return state;
        }
    }
}

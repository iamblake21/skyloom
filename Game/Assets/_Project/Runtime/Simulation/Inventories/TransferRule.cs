using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation.Machines;

namespace CML.Simulation.Inventories
{
    public enum TransferEndpointKind : byte
    {
        Inventory = 1,
        MachinePort = 2
    }

    /// <summary>
    /// One side of a transfer. A machine port endpoint names the node and which of its
    /// ports; a buffer node has one port and answers to
    /// <see cref="MachinePortKind.Storage"/> alone.
    /// </summary>
    public readonly struct TransferEndpoint : IEquatable<TransferEndpoint>
    {
        private TransferEndpoint(
            TransferEndpointKind kind,
            StableId ownerId,
            MachinePortKind port)
        {
            Kind = kind;
            OwnerId = ownerId;
            PortKind = port;
        }

        public TransferEndpointKind Kind { get; }

        public StableId OwnerId { get; }

        public MachinePortKind PortKind { get; }

        public static TransferEndpoint Inventory(StableId inventoryId)
        {
            return new TransferEndpoint(
                TransferEndpointKind.Inventory,
                inventoryId,
                MachinePortKind.Storage);
        }

        public static TransferEndpoint Port(StableId nodeId, MachinePortKind port)
        {
            return new TransferEndpoint(TransferEndpointKind.MachinePort, nodeId, port);
        }

        public bool Equals(TransferEndpoint other)
        {
            return Kind == other.Kind
                && OwnerId == other.OwnerId
                && PortKind == other.PortKind;
        }

        public override bool Equals(object obj) => obj is TransferEndpoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ OwnerId.GetHashCode();
                return (hash * 397) ^ (int)PortKind;
            }
        }

        public static bool operator ==(TransferEndpoint left, TransferEndpoint right) =>
            left.Equals(right);

        public static bool operator !=(TransferEndpoint left, TransferEndpoint right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Why a transfer did not happen. A refusal always names one of these, so a caller
    /// never has to guess between "not allowed" and "nothing to move".
    /// </summary>
    public enum TransferFailure : byte
    {
        None = 0,
        UnknownSource = 1,
        UnknownDestination = 2,
        SameEndpoint = 3,
        UnknownItem = 4,
        ZeroAmount = 5,
        InsufficientSource = 6,
        DestinationFull = 7,
        NotAdmitted = 8
    }

    /// <summary>
    /// The single authoritative transfer rule.
    ///
    /// One function serves the player emptying a crate into a furnace, a crate feeding
    /// a press and anything else that moves items between two holders. Two
    /// implementations of "move n of item x" would eventually disagree about a stack
    /// limit or a capacity, and the disagreement would show up as material appearing or
    /// vanishing, which no test written against either half would catch.
    ///
    /// Every transfer is all-or-nothing and has no observable intermediate state: the
    /// whole move is measured against both sides before either is touched. If the apply
    /// half then fails anyway, that is a broken invariant and it throws, so the engine
    /// discards the working clone instead of publishing a state where the items are in
    /// neither place.
    /// </summary>
    public static class TransferRule
    {
        public static bool TryTransfer(
            InventorySimulationState inventories,
            MachineSimulationState machines,
            GameCatalog catalog,
            TransferEndpoint source,
            TransferEndpoint destination,
            StableId itemId,
            NonNegativeQuantity amount,
            out TransferFailure failure)
        {
            if (inventories == null)
            {
                throw new ArgumentNullException(nameof(inventories));
            }

            if (machines == null)
            {
                throw new ArgumentNullException(nameof(machines));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (amount.IsZero)
            {
                failure = TransferFailure.ZeroAmount;
                return false;
            }

            if (itemId.IsNone || !catalog.TryGetItem(itemId, out _))
            {
                failure = TransferFailure.UnknownItem;
                return false;
            }

            if (source == destination)
            {
                failure = TransferFailure.SameEndpoint;
                return false;
            }

            if (!TryResolve(inventories, machines, source, out var from))
            {
                failure = TransferFailure.UnknownSource;
                return false;
            }

            if (!TryResolve(inventories, machines, destination, out var to))
            {
                failure = TransferFailure.UnknownDestination;
                return false;
            }

            // Unreachable today: a buffer answers only to Storage, so two endpoints on
            // one crate are already equal above, and a machine keeps two distinct ports.
            // It stays because the aliasing is a property of the node and not of how the
            // endpoint is spelled: a node kind that exposed one port under two names
            // would otherwise run the take and the store against the same storage.
            if (from.SharesStorageWith(to))
            {
                failure = TransferFailure.SameEndpoint;
                return false;
            }

            if (!to.Admits(itemId, catalog))
            {
                failure = TransferFailure.NotAdmitted;
                return false;
            }

            if (from.Count(itemId) < amount)
            {
                failure = TransferFailure.InsufficientSource;
                return false;
            }

            if (to.StorableQuantity(itemId, catalog) < amount)
            {
                failure = TransferFailure.DestinationFull;
                return false;
            }

            from.Take(itemId, amount, catalog);
            to.Store(itemId, amount, catalog);
            failure = TransferFailure.None;
            return true;
        }

        private static bool TryResolve(
            InventorySimulationState inventories,
            MachineSimulationState machines,
            TransferEndpoint endpoint,
            out ResolvedEndpoint resolved)
        {
            if (endpoint.OwnerId.IsNone)
            {
                resolved = default;
                return false;
            }

            if (endpoint.Kind == TransferEndpointKind.Inventory)
            {
                if (!inventories.TryGet(endpoint.OwnerId, out var inventory))
                {
                    resolved = default;
                    return false;
                }

                resolved = ResolvedEndpoint.ForInventory(inventories, inventory);
                return true;
            }

            if (endpoint.Kind != TransferEndpointKind.MachinePort
                || !machines.TryGetNodeMutable(endpoint.OwnerId, out var node))
            {
                resolved = default;
                return false;
            }

            MachinePort port;
            switch (endpoint.PortKind)
            {
                case MachinePortKind.Storage:
                    // Only a buffer has a storage port; asking a machine for one is a
                    // mis-addressed transfer, not a transfer to its input.
                    if (node.Kind != MachineNodeKind.Buffer)
                    {
                        resolved = default;
                        return false;
                    }

                    port = node.Input;
                    break;

                case MachinePortKind.Input:
                    if (node.Kind != MachineNodeKind.Machine)
                    {
                        resolved = default;
                        return false;
                    }

                    port = node.Input;
                    break;

                case MachinePortKind.Output:
                    if (node.Kind != MachineNodeKind.Machine)
                    {
                        resolved = default;
                        return false;
                    }

                    port = node.Output;
                    break;

                case MachinePortKind.Fuel:
                    if (node.Kind != MachineNodeKind.Machine
                        || node.Fuel == null)
                    {
                        resolved = default;
                        return false;
                    }

                    port = node.Fuel;
                    break;

                default:
                    resolved = default;
                    return false;
            }

            resolved = ResolvedEndpoint.ForPort(node, port);
            return true;
        }

        /// <summary>
        /// A side of the transfer with its storage located: either an immutable
        /// inventory that has to be republished, or a port that is edited in place.
        /// </summary>
        private readonly struct ResolvedEndpoint
        {
            private readonly InventorySimulationState _inventories;
            private readonly InventoryState _inventory;
            private readonly MachineNodeState _node;
            private readonly MachinePort _port;

            private ResolvedEndpoint(
                InventorySimulationState inventories,
                InventoryState inventory,
                MachineNodeState node,
                MachinePort port)
            {
                _inventories = inventories;
                _inventory = inventory;
                _node = node;
                _port = port;
            }

            public static ResolvedEndpoint ForInventory(
                InventorySimulationState inventories,
                InventoryState inventory)
            {
                return new ResolvedEndpoint(inventories, inventory, null, null);
            }

            public static ResolvedEndpoint ForPort(MachineNodeState node, MachinePort port)
            {
                return new ResolvedEndpoint(null, null, node, port);
            }

            public bool SharesStorageWith(ResolvedEndpoint other)
            {
                if (_inventory != null)
                {
                    return ReferenceEquals(_inventory, other._inventory);
                }

                return ReferenceEquals(_port, other._port);
            }

            public bool Admits(StableId itemId, GameCatalog catalog)
            {
                if (_inventory != null)
                {
                    return true;
                }

                // A machine input takes only what its recipe consumes. Its output is a
                // result buffer and admits nothing from outside; a hand-fed plate in the
                // output would be indistinguishable from one the machine made.
                switch (_port.Kind)
                {
                    case MachinePortKind.Storage:
                        return true;

                    case MachinePortKind.Input:
                        return RecipeConsumes(_node, itemId, catalog);

                    case MachinePortKind.Fuel:
                        return MachineConsumesFuel(
                            _node,
                            itemId,
                            catalog);

                    default:
                        return false;
                }
            }

            public NonNegativeQuantity Count(StableId itemId)
            {
                return _inventory != null ? _inventory.Count(itemId) : _port.Count(itemId);
            }

            public NonNegativeQuantity StorableQuantity(StableId itemId, GameCatalog catalog)
            {
                if (_inventory != null)
                {
                    return _inventory.StorableQuantity(itemId);
                }

                var physical = _port.StorableQuantity(itemId, catalog);
                if (_port.Kind == MachinePortKind.Input)
                {
                    return new NonNegativeQuantity(
                        Math.Min(
                            physical.Value,
                            MachineInputRemainingCapacity(
                                _node,
                                itemId,
                                catalog)));
                }

                if (_port.Kind == MachinePortKind.Fuel)
                {
                    return new NonNegativeQuantity(
                        Math.Min(
                            physical.Value,
                            MachineFuelRemainingCapacity(
                                _node,
                                itemId,
                                catalog)));
                }

                return physical;
            }

            public void Take(StableId itemId, NonNegativeQuantity amount, GameCatalog catalog)
            {
                if (_inventory != null)
                {
                    if (!_inventory.TryTakeEntire(itemId, amount, out var updated, out var reason))
                    {
                        throw new SimulationInvariantException(
                            $"Inventory {_inventory.InventoryId} passed the transfer preflight "
                            + $"and then refused to give up {amount} of {itemId}: {reason}.");
                    }

                    _inventories.Replace(updated);
                    return;
                }

                if (!_port.TryTake(itemId, amount))
                {
                    throw new SimulationInvariantException(
                        $"Port {_port.Kind} of node {_node.Id} passed the transfer preflight "
                        + $"and then refused to give up {amount} of {itemId}.");
                }
            }

            public void Store(StableId itemId, NonNegativeQuantity amount, GameCatalog catalog)
            {
                if (_inventory != null)
                {
                    if (!_inventory.TryStoreEntire(itemId, amount, out var updated, out var reason))
                    {
                        throw new SimulationInvariantException(
                            $"Inventory {_inventory.InventoryId} passed the transfer preflight "
                            + $"and then refused {amount} of {itemId}: {reason}.");
                    }

                    _inventories.Replace(updated);
                    return;
                }

                if (!_port.TryStore(itemId, amount, catalog))
                {
                    throw new SimulationInvariantException(
                        $"Port {_port.Kind} of node {_node.Id} passed the transfer preflight "
                        + $"and then refused {amount} of {itemId}.");
                }
            }

            private static long MachineInputRemainingCapacity(
                MachineNodeState node,
                StableId itemId,
                GameCatalog catalog)
            {
                if (!catalog.TryGetMachine(node.DefinitionId, out var machine))
                {
                    return 0L;
                }

                return Math.Max(
                    0L,
                    machine.InputBufferCapacityPerItem
                    - node.Input.Count(itemId).Value);
            }

            private static long MachineFuelRemainingCapacity(
                MachineNodeState node,
                StableId itemId,
                GameCatalog catalog)
            {
                if (!catalog.TryGetMachine(node.DefinitionId, out var machine)
                    || !machine.RequiresFuel
                    || machine.FuelItemId != itemId
                    || node.Fuel == null)
                {
                    return 0L;
                }

                return Math.Max(
                    0L,
                    machine.FuelBufferCapacityPerItem
                    - node.Fuel.Count(itemId).Value);
            }

            private static bool RecipeConsumes(
                MachineNodeState node,
                StableId itemId,
                GameCatalog catalog)
            {
                if (node.ActiveRecipeId.IsNone
                    || !catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe)
                    || recipe.Inputs == null)
                {
                    return false;
                }

                for (var index = 0; index < recipe.Inputs.Count; index++)
                {
                    if (recipe.Inputs[index].ItemId == itemId)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool MachineConsumesFuel(
                MachineNodeState node,
                StableId itemId,
                GameCatalog catalog)
            {
                return catalog.TryGetMachine(
                        node.DefinitionId,
                        out var machine)
                    && machine.RequiresFuel
                    && machine.FuelItemId == itemId;
            }
        }
    }
}

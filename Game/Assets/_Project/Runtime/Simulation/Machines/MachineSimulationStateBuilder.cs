using System;
using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Assembles a machine graph from validated content. Slot counts come from the
    /// catalog rather than from the caller, so a node cannot be built with a shape its
    /// definition does not declare.
    /// </summary>
    public sealed class MachineSimulationStateBuilder
    {
        private readonly GameCatalog _catalog;
        private readonly MachineSimulationState _state = new MachineSimulationState();

        public MachineSimulationStateBuilder(GameCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>A crate sized by its container definition.</summary>
        public MachineSimulationStateBuilder AddBuffer(
            StableId id,
            StableId containerDefinitionId)
        {
            var definition = ResolveContainer(containerDefinitionId);
            _state.AddNode(
                MachineNodeState.CreateBuffer(id, containerDefinitionId, definition.SlotCount));
            return this;
        }

        public MachineSimulationStateBuilder AddBuffer(
            StableId id,
            StableId containerDefinitionId,
            MachineBuildPose pose)
        {
            var definition = ResolveContainer(containerDefinitionId);
            _state.AddNode(
                MachineNodeState.CreateBuffer(
                    id,
                    containerDefinitionId,
                    definition.SlotCount,
                    pose));
            return this;
        }

        /// <summary>
        /// A crate deliberately narrower than its definition, used to build a sink that
        /// saturates. The slot count may only be reduced: widening a crate beyond its
        /// definition would be content expressed in code.
        /// </summary>
        public MachineSimulationStateBuilder AddNarrowBuffer(
            StableId id,
            StableId containerDefinitionId,
            int slotCount)
        {
            return AddNarrowBufferInternal(
                id,
                containerDefinitionId,
                slotCount,
                false,
                default);
        }

        public MachineSimulationStateBuilder AddNarrowBuffer(
            StableId id,
            StableId containerDefinitionId,
            int slotCount,
            MachineBuildPose pose)
        {
            return AddNarrowBufferInternal(
                id,
                containerDefinitionId,
                slotCount,
                true,
                pose);
        }

        private MachineSimulationStateBuilder AddNarrowBufferInternal(
            StableId id,
            StableId containerDefinitionId,
            int slotCount,
            bool hasPose,
            MachineBuildPose pose)
        {
            var definition = ResolveContainer(containerDefinitionId);
            if (slotCount <= 0 || slotCount > definition.SlotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slotCount),
                    slotCount,
                    $"'{definition.Key}' declares {definition.SlotCount} slots; a narrowed "
                    + "buffer must keep between one and that many.");
            }

            _state.AddNode(
                hasPose
                    ? MachineNodeState.CreateBuffer(
                        id,
                        containerDefinitionId,
                        slotCount,
                        pose)
                    : MachineNodeState.CreateBuffer(
                        id,
                        containerDefinitionId,
                        slotCount));
            return this;
        }

        public MachineSimulationStateBuilder AddMachine(
            StableId id,
            StableId machineDefinitionId,
            StableId recipeId)
        {
            return AddMachineInternal(
                id,
                machineDefinitionId,
                recipeId,
                false,
                default);
        }

        public MachineSimulationStateBuilder AddMachine(
            StableId id,
            StableId machineDefinitionId,
            StableId recipeId,
            MachineBuildPose pose)
        {
            return AddMachineInternal(
                id,
                machineDefinitionId,
                recipeId,
                true,
                pose);
        }

        private MachineSimulationStateBuilder AddMachineInternal(
            StableId id,
            StableId machineDefinitionId,
            StableId recipeId,
            bool hasPose,
            MachineBuildPose pose)
        {
            if (!_catalog.TryGetMachine(machineDefinitionId, out var definition))
            {
                throw new ArgumentException(
                    $"Machine definition '{machineDefinitionId}' does not exist in the validated catalog.",
                    nameof(machineDefinitionId));
            }

            var node = hasPose
                ? MachineNodeState.CreateMachine(
                    id,
                    machineDefinitionId,
                    definition.InputSlots,
                    definition.OutputSlots,
                    pose,
                    definition.FuelSlots)
                : MachineNodeState.CreateMachine(
                    id,
                    machineDefinitionId,
                    definition.InputSlots,
                    definition.OutputSlots,
                    definition.FuelSlots);


            if (!recipeId.IsNone)
            {
                var supports = false;
                for (var index = 0; index < definition.SupportedRecipeIds.Count; index++)
                {
                    if (definition.SupportedRecipeIds[index] == recipeId)
                    {
                        supports = true;
                        break;
                    }
                }

                if (!supports)
                {
                    throw new ArgumentException(
                        $"'{definition.Key}' does not support recipe '{recipeId}'.",
                        nameof(recipeId));
                }

                node.ActiveRecipeId = recipeId;
                node.Activity = MachineActivity.MissingInput;
            }

            _state.AddNode(node);
            return this;
        }

        /// <summary>
        /// A funnel physically attached to a node. The lane direction decides whether
        /// it extracts from or inserts into that node. StableId.None creates a detached,
        /// inert funnel for placement tests.
        /// </summary>
        public MachineSimulationStateBuilder AddFunnel(
            StableId id,
            StableId itemDefinitionId,
            MachineBuildPose pose)
        {
            ResolveItem(itemDefinitionId);
            _state.AddNode(
                MachineNodeState.CreateFunnel(id, itemDefinitionId, pose));
            return this;
        }

        public MachineSimulationStateBuilder AddFunnel(
            StableId id,
            StableId itemDefinitionId,
            StableId attachedNodeId)
        {
            ResolveItem(itemDefinitionId);

            _state.AddNode(
                MachineNodeState.CreateFunnel(id, itemDefinitionId, attachedNodeId));
            return this;
        }

        public MachineSimulationStateBuilder AddBeltModule(
            StableId id,
            StableId itemDefinitionId,
            MachineBuildPose pose)
        {
            ResolveItem(itemDefinitionId);
            _state.AddNode(
                MachineNodeState.CreateBeltModule(
                    id,
                    itemDefinitionId,
                    pose));
            return this;
        }

        /// <summary>
        /// A belt lane. Length and speed are in millimetres, spacing is the minimum gap
        /// between two items and therefore the throughput ceiling.
        /// </summary>
        public MachineSimulationStateBuilder AddLane(
            StableId id,
            StableId sourceNodeId,
            StableId destinationNodeId,
            StableId itemFilter,
            int lengthMillimetres,
            int speedMillimetresPerTick,
            int spacingMillimetres)
        {
            _state.AddLane(
                new BeltLaneState(
                    id,
                    sourceNodeId,
                    destinationNodeId,
                    itemFilter,
                    lengthMillimetres,
                    speedMillimetresPerTick,
                    spacingMillimetres));
            return this;
        }

        /// <summary>Seeds a node's holdings. Fails loudly rather than storing a part.</summary>
        public MachineSimulationStateBuilder Store(StableId nodeId, StableId itemId, long quantity)
        {
            if (!_state.TryGetNodeMutable(nodeId, out var node))
            {
                throw new ArgumentException($"Node '{nodeId}' has not been added.", nameof(nodeId));
            }

            if (!node.Input.TryStore(itemId, new NonNegativeQuantity(quantity), _catalog))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    quantity,
                    $"Node '{nodeId}' cannot hold {quantity} of item '{itemId}'.");
            }

            return this;
        }

        /// <summary>
        /// Seeds a machine's output port. A press whose output buffer is already full is
        /// the initial condition of backpressure, and it has to be stateable directly:
        /// reaching it by running a hundred cycles would test the clock, not the stall.
        /// </summary>
        public MachineSimulationStateBuilder StoreInOutput(
            StableId nodeId,
            StableId itemId,
            long quantity)
        {
            var node = ResolveMachine(nodeId);
            if (!node.Output.TryStore(itemId, new NonNegativeQuantity(quantity), _catalog))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    quantity,
                    $"The output port of '{nodeId}' cannot hold {quantity} of item '{itemId}'.");
            }

            return this;
        }

        /// <summary>
        /// Seeds a cycle that has already consumed its inputs and reached the given
        /// progress. Lets a test start from the instant a cycle finishes instead of
        /// spending the recipe's whole duration getting there.
        /// </summary>
        public MachineSimulationStateBuilder WithCycleInFlight(
            StableId nodeId,
            long progressMilliseconds)
        {
            var node = ResolveMachine(nodeId);
            if (node.ActiveRecipeId.IsNone)
            {
                throw new InvalidOperationException(
                    $"Node '{nodeId}' has no active recipe, so it can hold no cycle.");
            }

            if (!_catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe))
            {
                throw new InvalidOperationException(
                    $"Recipe '{node.ActiveRecipeId}' does not exist in the validated catalog.");
            }

            if (progressMilliseconds < 0L || progressMilliseconds > recipe.DurationMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(progressMilliseconds),
                    progressMilliseconds,
                    $"'{recipe.Key}' lasts {recipe.DurationMilliseconds} ms.");
            }

            node.IsCycleActive = true;
            node.ProgressMilliseconds = progressMilliseconds;
            node.Activity = MachineActivity.Running;
            return this;
        }

        public MachineSimulationState Build()
        {
            _state.ValidateInvariants(_catalog);
            return _state.DeepClone();
        }

        private MachineNodeState ResolveMachine(StableId nodeId)
        {
            if (!_state.TryGetNodeMutable(nodeId, out var node))
            {
                throw new ArgumentException($"Node '{nodeId}' has not been added.", nameof(nodeId));
            }

            if (node.Kind != MachineNodeKind.Machine)
            {
                throw new ArgumentException(
                    $"Node '{nodeId}' is a buffer: it has one port and no cycle.",
                    nameof(nodeId));
            }

            return node;
        }

        private ContainerDefinition ResolveContainer(StableId containerDefinitionId)
        {
            if (!_catalog.TryGetContainer(containerDefinitionId, out var definition))
            {
                throw new ArgumentException(
                    $"Container definition '{containerDefinitionId}' does not exist in the validated catalog.",
                    nameof(containerDefinitionId));
            }

            return definition;
        }

        private void ResolveItem(StableId itemDefinitionId)
        {
            if (!_catalog.TryGetItem(itemDefinitionId, out _))
            {
                throw new ArgumentException(
                    $"Item {itemDefinitionId} does not exist in the validated catalog.",
                    nameof(itemDefinitionId));
            }
        }
    }
}

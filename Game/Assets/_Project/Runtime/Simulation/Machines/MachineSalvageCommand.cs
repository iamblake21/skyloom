using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation.Inventories;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Removes one placed element and hands it back, with everything it still held,
    /// to the salvaging inventory.
    ///
    /// Salvage is lossless on purpose: it either returns every last item or it
    /// refuses. A partial salvage that silently voided the overflow would destroy
    /// the player's stock to make room for a crate, which is never a trade they
    /// asked for. That is why the whole refund is rehearsed against a copy of the
    /// inventory before anything is removed from the graph.
    /// </summary>
    public static class MachineSalvageRule
    {
        /// <summary>
        /// Maps a placed node back to the item that paid for it. A Buffer and a
        /// Machine are stored under their definition id, which is not the id of the
        /// carryable item, so the two cases cannot simply echo DefinitionId back.
        /// </summary>
        public static bool TryResolveRefund(
            MachineNodeKind kind,
            StableId definitionId,
            out StableId refundItemId,
            out long refundQuantity)
        {
            refundItemId = StableId.None;
            refundQuantity = 0L;
            switch (kind)
            {
                case MachineNodeKind.Buffer when definitionId == ContentIds.WoodenCrate:
                    refundItemId = ContentIds.WoodenCrateItem;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.Machine when definitionId == ContentIds.MechanicalPress:
                    refundItemId = ContentIds.MechanicalPressItem;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.Machine when definitionId == ContentIds.CrudeFurnace:
                    refundItemId = ContentIds.CrudeFurnaceItem;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.Funnel when definitionId == ContentIds.BeltFunnel:
                    refundItemId = ContentIds.BeltFunnel;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.BeltModule when definitionId == ContentIds.BeltStraight:
                    refundItemId = ContentIds.BeltStraight;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.BeltModule when definitionId == ContentIds.BeltDriveUnit:
                    refundItemId = ContentIds.BeltDriveUnit;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.BeltModule when definitionId == ContentIds.BeltCurve:
                    refundItemId = ContentIds.BeltCurve;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.BeltModule when definitionId == ContentIds.BeltIncline:
                    refundItemId = ContentIds.BeltIncline;
                    refundQuantity = 1L;
                    return true;

                case MachineNodeKind.BeltModule when definitionId == ContentIds.BeltCurveLeft:
                    refundItemId = ContentIds.BeltCurveLeft;
                    refundQuantity = 1L;
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryPreflight(
            MachineSimulationState state,
            InventorySimulationState inventories,
            StableId targetInventoryId,
            StableId nodeId,
            out CommandRejectionReason rejection)
        {
            rejection = default;
            if (state == null
                || nodeId.IsNone
                || !state.TryGetNode(nodeId, out var node))
            {
                rejection = CommandRejectionReason.SalvageTargetMissing;
                return false;
            }

            if (!TryResolveRefund(
                    node.Kind,
                    node.DefinitionId,
                    out var refundItemId,
                    out var refundQuantity))
            {
                rejection = CommandRejectionReason.BuildDefinitionMissing;
                return false;
            }

            if (inventories == null
                || targetInventoryId.IsNone
                || !inventories.TryGet(targetInventoryId, out var inventory))
            {
                rejection = CommandRejectionReason.BuildDestinationMissing;
                return false;
            }

            if (!TryAccumulateRefund(
                    inventory,
                    node,
                    refundItemId,
                    refundQuantity,
                    out _))
            {
                rejection = CommandRejectionReason.SalvageDestinationFull;
                return false;
            }

            return true;
        }

        internal static void Apply(
            MachineSimulationState state,
            InventorySimulationState inventories,
            StableId targetInventoryId,
            StableId nodeId)
        {
            if (!state.TryGetNode(nodeId, out var node)
                || !TryResolveRefund(
                    node.Kind,
                    node.DefinitionId,
                    out var refundItemId,
                    out var refundQuantity)
                || !inventories.TryGet(targetInventoryId, out var inventory)
                || !TryAccumulateRefund(
                    inventory,
                    node,
                    refundItemId,
                    refundQuantity,
                    out var credited))
            {
                throw new SimulationInvariantException(
                    "A salvage passed phase-1 preflight and failed to apply.");
            }

            inventories.Replace(credited);
            if (!state.RemoveNode(nodeId))
            {
                throw new SimulationInvariantException(
                    $"Salvage credited {nodeId} but the node had already gone.");
            }
        }

        /// <summary>
        /// Rehearses the complete refund against a copy of the inventory. Returns the
        /// credited copy so the caller can commit exactly what it validated.
        /// </summary>
        private static bool TryAccumulateRefund(
            InventoryState inventory,
            MachineNodeState node,
            StableId refundItemId,
            long refundQuantity,
            out InventoryState credited)
        {
            if (!inventory.TryStoreEntire(
                    refundItemId,
                    new NonNegativeQuantity(refundQuantity),
                    out credited,
                    out _))
            {
                return false;
            }

            if (!TryStorePortContents(node.Input, ref credited))
            {
                return false;
            }

            // A funnel and a belt module share one storage port between Input and
            // Output. DeepClone preserves that aliasing, so counting both would
            // refund their cargo twice.
            if (!ReferenceEquals(node.Input, node.Output)
                && !TryStorePortContents(node.Output, ref credited))
            {
                return false;
            }

            if (node.Fuel != null
                && !TryStorePortContents(node.Fuel, ref credited))
            {
                return false;
            }

            return true;
        }

        private static bool TryStorePortContents(
            MachinePort port,
            ref InventoryState credited)
        {
            for (var index = 0; index < port.SlotCount; index++)
            {
                var slot = port.GetSlot(index);
                if (slot.IsEmpty || slot.Quantity.IsZero)
                {
                    continue;
                }

                if (!credited.TryStoreEntire(
                        slot.ItemId,
                        slot.Quantity,
                        out credited,
                        out _))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Applies salvage commands. It shares LocalTopologyChanges with the build system
    /// and runs after it, so a tick that both builds and salvages resolves in one
    /// fixed order regardless of the sequence the commands arrived in.
    /// </summary>
    internal sealed class MachineSalvagePhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.LocalTopologyChanges;

        public int Order => 110;

        public StableId StableOrderId =>
            new StableId(0x4D43485F53414C56UL, 0x4100000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            var commands = context.GetCommandsForExecutingTick();
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (!string.Equals(
                        command.Kind,
                        SimulationCommandKinds.SalvageMachineGraphElement,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var machines = context.GetMachineMutable();
                var inventories = context.GetInventoriesMutable();
                if (!MachineSalvageRule.TryPreflight(
                        machines,
                        inventories,
                        command.InitiatorId,
                        command.DestinationId,
                        out var rejection))
                {
                    context.RecordCommandRejection(command, rejection);
                    continue;
                }

                MachineSalvageRule.Apply(
                    machines,
                    inventories,
                    command.InitiatorId,
                    command.DestinationId);
            }
        }
    }
}

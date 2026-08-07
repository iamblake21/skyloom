using System;
using CML.Foundation;
using CML.Inventory;

namespace CML.Simulation.Inventories
{
    /// <summary>
    /// Payload del riordino fra slot.
    ///
    /// I campi fissi di <see cref="SimulationCommand"/> portano quello per cui
    /// sono nominati: l'inventario in <c>InitiatorId</c> e la quantità in
    /// <c>QuantizedValue</c>, dove zero vuol dire l'intera pila d'origine. Quello
    /// che resta sono i due indici, quattro byte di formato fisso e big-endian,
    /// così la decodifica non dipende né da una lunghezza né dalla piattaforma.
    /// </summary>
    public static class SlotMoveCommandPayload
    {
        public const int Length = 4;

        public static byte[] Encode(int sourceSlotIndex, int destinationSlotIndex)
        {
            if (sourceSlotIndex < 0 || sourceSlotIndex > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceSlotIndex));
            }

            if (destinationSlotIndex < 0 || destinationSlotIndex > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationSlotIndex));
            }

            return new[]
            {
                (byte)((sourceSlotIndex >> 8) & 0xFF),
                (byte)(sourceSlotIndex & 0xFF),
                (byte)((destinationSlotIndex >> 8) & 0xFF),
                (byte)(destinationSlotIndex & 0xFF)
            };
        }

        public static bool TryDecode(
            SimulationCommand command,
            out StableId inventoryId,
            out int sourceSlotIndex,
            out int destinationSlotIndex,
            out NonNegativeQuantity amount)
        {
            inventoryId = StableId.None;
            sourceSlotIndex = 0;
            destinationSlotIndex = 0;
            amount = NonNegativeQuantity.Zero;

            var payload = command.Payload;
            if (payload == null || payload.Count != Length)
            {
                return false;
            }

            if (command.QuantizedValue < 0L || command.InitiatorId.IsNone)
            {
                return false;
            }

            inventoryId = command.InitiatorId;
            sourceSlotIndex = (payload[0] << 8) | payload[1];
            destinationSlotIndex = (payload[2] << 8) | payload[3];
            amount = new NonNegativeQuantity(command.QuantizedValue);
            return true;
        }
    }

    /// <summary>
    /// Applica i riordini validi.
    ///
    /// Gira nella stessa fase del commit dei trasferimenti e dopo di essi: se in
    /// un tick arrivano entrambi, il riordino vede l'inventario già aggiornato
    /// dai trasferimenti, che è l'ordine che l'interfaccia si aspetta quando si
    /// trascina una pila fra due pannelli e poi la si sistema.
    /// </summary>
    internal sealed class InventorySlotMovePhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.ValidatedTransferCommit;

        public int Order => 200;

        public StableId StableOrderId =>
            new StableId(0x494E565F534C4F54UL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            var commands = context.GetCommandsForExecutingTick();
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (!string.Equals(
                        command.Kind,
                        SimulationCommandKinds.MoveInventorySlot,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!SlotMoveCommandPayload.TryDecode(
                        command,
                        out var inventoryId,
                        out var sourceSlotIndex,
                        out var destinationSlotIndex,
                        out var amount))
                {
                    context.RecordCommandRejection(
                        command,
                        CommandRejectionReason.SlotMoveMalformed);
                    continue;
                }

                var inventories = context.GetInventoriesMutable();
                if (!inventories.TryGet(inventoryId, out var inventory))
                {
                    context.RecordCommandRejection(
                        command,
                        CommandRejectionReason.SlotMoveInventoryMissing);
                    continue;
                }

                if (inventory.TryMoveWithinInventory(
                        sourceSlotIndex,
                        destinationSlotIndex,
                        amount,
                        out var updated,
                        out var failure))
                {
                    inventories.Replace(updated);
                    continue;
                }

                context.RecordCommandRejection(command, ToRejectionReason(failure));
            }
        }

        private static CommandRejectionReason ToRejectionReason(InventoryFailure failure)
        {
            switch (failure)
            {
                case InventoryFailure.InvalidDefinition:
                    return CommandRejectionReason.SlotMoveSlotOutOfRange;
                case InventoryFailure.InsufficientQuantity:
                    return CommandRejectionReason.InsufficientQuantity;
                case InventoryFailure.CapacityExceeded:
                    return CommandRejectionReason.SlotMoveBlocked;
                case InventoryFailure.UnknownItem:
                    return CommandRejectionReason.TransferUnknownItem;
                default:
                    throw new SimulationInvariantException(
                        $"Inventory failure {failure} has no canonical rejection reason.");
            }
        }
    }
}

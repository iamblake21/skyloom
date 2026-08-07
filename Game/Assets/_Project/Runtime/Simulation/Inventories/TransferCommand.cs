using CML.Foundation;
using CML.Simulation.Machines;

namespace CML.Simulation.Inventories
{
    /// <summary>
    /// Payload of a transfer command.
    ///
    /// The fixed fields of <see cref="SimulationCommand"/> carry what they are named
    /// for: the source owner in <c>InitiatorId</c>, the destination owner in
    /// <c>DestinationId</c>, the amount in <c>QuantizedValue</c>. What is left over —
    /// which kind each endpoint is, which port, and which item — is twenty bytes of
    /// fixed layout, big-endian, so decoding cannot depend on a length or a platform.
    /// </summary>
    public static class TransferCommandPayload
    {
        public const int Length = 20;

        public static byte[] Encode(
            TransferEndpointKind sourceKind,
            MachinePortKind sourcePort,
            TransferEndpointKind destinationKind,
            MachinePortKind destinationPort,
            StableId itemId)
        {
            var payload = new byte[Length];
            payload[0] = (byte)sourceKind;
            payload[1] = (byte)sourcePort;
            payload[2] = (byte)destinationKind;
            payload[3] = (byte)destinationPort;
            WriteBigEndian(payload, 4, itemId.High);
            WriteBigEndian(payload, 12, itemId.Low);
            return payload;
        }

        public static byte[] Encode(
            TransferEndpoint source,
            TransferEndpoint destination,
            StableId itemId)
        {
            return Encode(
                source.Kind,
                source.PortKind,
                destination.Kind,
                destination.PortKind,
                itemId);
        }

        public static bool TryDecode(
            SimulationCommand command,
            out TransferEndpoint source,
            out TransferEndpoint destination,
            out StableId itemId,
            out NonNegativeQuantity amount)
        {
            source = default;
            destination = default;
            itemId = StableId.None;
            amount = NonNegativeQuantity.Zero;

            var payload = command.Payload;
            if (payload == null || payload.Count != Length)
            {
                return false;
            }

            if (command.QuantizedValue < 0L)
            {
                return false;
            }

            if (!TryEndpoint(payload[0], payload[1], command.InitiatorId, out source)
                || !TryEndpoint(payload[2], payload[3], command.DestinationId, out destination))
            {
                return false;
            }

            itemId = new StableId(
                ReadBigEndian(payload, 4),
                ReadBigEndian(payload, 12));
            amount = new NonNegativeQuantity(command.QuantizedValue);
            return true;
        }

        private static bool TryEndpoint(
            byte kind,
            byte port,
            StableId ownerId,
            out TransferEndpoint endpoint)
        {
            endpoint = default;
            if (port < (byte)MachinePortKind.Storage || port > (byte)MachinePortKind.Fuel)
            {
                return false;
            }

            switch ((TransferEndpointKind)kind)
            {
                case TransferEndpointKind.Inventory:
                    endpoint = TransferEndpoint.Inventory(ownerId);
                    return true;

                case TransferEndpointKind.MachinePort:
                    endpoint = TransferEndpoint.Port(ownerId, (MachinePortKind)port);
                    return true;

                default:
                    return false;
            }
        }

        private static void WriteBigEndian(byte[] buffer, int offset, ulong value)
        {
            for (var index = 0; index < 8; index++)
            {
                buffer[offset + index] = (byte)(value >> ((7 - index) * 8));
            }
        }

        private static ulong ReadBigEndian(System.Collections.Generic.IReadOnlyList<byte> buffer, int offset)
        {
            var value = 0UL;
            for (var index = 0; index < 8; index++)
            {
                value = (value << 8) | buffer[offset + index];
            }

            return value;
        }
    }

    /// <summary>
    /// Phase 9 is named for this and nothing else: a transfer is validated against both
    /// sides and committed here, after item flow and cycles have run and before the
    /// tick publishes. The commands are read from the working state rather than from
    /// <see cref="SimulationPhaseContext.Commands"/>, which the engine fills only for
    /// phase 1; they are still present, because the tick clears them after phase 12.
    /// </summary>
    internal sealed class TransferCommitPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.ValidatedTransferCommit;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x494E565F5852464EUL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            var commands = context.GetCommandsForExecutingTick();
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (!string.Equals(
                        command.Kind,
                        SimulationCommandKinds.Transfer,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TransferCommandPayload.TryDecode(
                        command,
                        out var source,
                        out var destination,
                        out var itemId,
                        out var amount))
                {
                    context.RecordCommandRejection(
                        command,
                        CommandRejectionReason.TransferMalformed);
                    continue;
                }

                if (context.Catalog == null)
                {
                    throw new SimulationInvariantException(
                        "A transfer command cannot be validated without the catalog that "
                        + "defines its stack limits.");
                }

                if (TransferRule.TryTransfer(
                        context.GetInventoriesMutable(),
                        context.GetMachineMutable(),
                        context.Catalog,
                        source,
                        destination,
                        itemId,
                        amount,
                        out var failure))
                {
                    continue;
                }

                context.RecordCommandRejection(command, ToRejectionReason(failure));
            }
        }

        private static CommandRejectionReason ToRejectionReason(TransferFailure failure)
        {
            switch (failure)
            {
                case TransferFailure.UnknownSource:
                    return CommandRejectionReason.TransferSourceMissing;
                case TransferFailure.UnknownDestination:
                    return CommandRejectionReason.TransferDestinationMissing;
                case TransferFailure.SameEndpoint:
                    return CommandRejectionReason.TransferSameEndpoint;
                case TransferFailure.UnknownItem:
                    return CommandRejectionReason.TransferUnknownItem;
                case TransferFailure.ZeroAmount:
                    return CommandRejectionReason.TransferZeroAmount;
                case TransferFailure.InsufficientSource:
                    return CommandRejectionReason.InsufficientQuantity;
                case TransferFailure.DestinationFull:
                    return CommandRejectionReason.TransferDestinationFull;
                case TransferFailure.NotAdmitted:
                    return CommandRejectionReason.TransferNotAdmitted;
                default:
                    throw new SimulationInvariantException(
                        $"Transfer failure {failure} has no canonical rejection reason.");
            }
        }
    }
}

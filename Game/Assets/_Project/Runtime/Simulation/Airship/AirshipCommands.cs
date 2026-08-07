using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation.Airship
{
    public static class AirshipCommandKinds
    {
        public const string PilotInput = "airship.pilot.input";
        public const string Takeoff = "airship.takeoff";
        public const string LandingRequest = "airship.landing.request";
        public const string Board = "airship.board";
        public const string Disembark = "airship.disembark";
        public const string PilotBegin = "airship.pilot.begin";
        public const string PilotEnd = "airship.pilot.end";
        public const string PlayerDestroyed = "airship.player.destroyed";
        public const string RepairInstall = "airship.repair.install";

        public static bool IsAirshipCommand(string kind)
        {
            return string.Equals(kind, PilotInput, StringComparison.Ordinal)
                || string.Equals(kind, Takeoff, StringComparison.Ordinal)
                || string.Equals(kind, LandingRequest, StringComparison.Ordinal)
                || string.Equals(kind, Board, StringComparison.Ordinal)
                || string.Equals(kind, Disembark, StringComparison.Ordinal)
                || string.Equals(kind, PilotBegin, StringComparison.Ordinal)
                || string.Equals(kind, PilotEnd, StringComparison.Ordinal)
                || string.Equals(kind, PlayerDestroyed, StringComparison.Ordinal)
                || string.Equals(kind, RepairInstall, StringComparison.Ordinal);
        }
    }

    /// <summary>Strict little-endian command codec for the global quantized queue.</summary>
    public static class AirshipCommandCodec
    {
        private const int PilotInputPayloadLength = 8;
        private const int StableIdPayloadLength = 16;
        private const int RepairInstallPayloadLength = 32;

        public static SimulationCommand PilotInput(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId,
            AirshipPilotInputState input)
        {
            return Create(
                targetTick,
                sequence,
                AirshipCommandKinds.PilotInput,
                playerId,
                airshipId,
                EncodePilotInput(input));
        }

        public static SimulationCommand Takeoff(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId)
        {
            return CreateEmpty(
                targetTick,
                sequence,
                AirshipCommandKinds.Takeoff,
                playerId,
                airshipId);
        }

        public static SimulationCommand LandingRequest(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId,
            StableId landingSurfaceId)
        {
            if (landingSurfaceId.IsNone)
            {
                throw new ArgumentException("A landing request requires a surface id.", nameof(landingSurfaceId));
            }

            return Create(
                targetTick,
                sequence,
                AirshipCommandKinds.LandingRequest,
                playerId,
                airshipId,
                EncodeStableId(landingSurfaceId));
        }

        public static SimulationCommand Board(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId)
        {
            return CreateEmpty(
                targetTick,
                sequence,
                AirshipCommandKinds.Board,
                playerId,
                airshipId);
        }

        public static SimulationCommand Disembark(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId)
        {
            return CreateEmpty(
                targetTick,
                sequence,
                AirshipCommandKinds.Disembark,
                playerId,
                airshipId);
        }

        public static SimulationCommand PilotBegin(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId)
        {
            return CreateEmpty(
                targetTick,
                sequence,
                AirshipCommandKinds.PilotBegin,
                playerId,
                airshipId);
        }

        public static SimulationCommand PilotEnd(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId)
        {
            return CreateEmpty(
                targetTick,
                sequence,
                AirshipCommandKinds.PilotEnd,
                playerId,
                airshipId);
        }

        public static SimulationCommand PlayerDestroyed(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId)
        {
            return Create(
                targetTick,
                sequence,
                AirshipCommandKinds.PlayerDestroyed,
                playerId,
                StableId.None,
                Array.Empty<byte>());
        }

        /// <summary>
        /// One installation of one component into the damaged hull. The payload
        /// names the inventory that pays and the item that moves; the amount
        /// rides in the envelope's quantized value. The paying inventory is
        /// carried explicitly rather than derived from the player, so the rule
        /// in phase 9 never has to guess which container it may empty.
        /// </summary>
        public static SimulationCommand RepairInstall(
            SimulationTick targetTick,
            ulong sequence,
            StableId playerId,
            StableId airshipId,
            StableId sourceInventoryId,
            StableId itemId,
            long amount)
        {
            if (sourceInventoryId.IsNone)
            {
                throw new ArgumentException(
                    "A repair install requires a source inventory id.",
                    nameof(sourceInventoryId));
            }

            if (itemId.IsNone)
            {
                throw new ArgumentException(
                    "A repair install requires an item id.",
                    nameof(itemId));
            }

            if (amount <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "A repair install requires a positive amount.");
            }

            if (playerId.IsNone)
            {
                throw new ArgumentException(
                    "An airship command requires a player id.",
                    nameof(playerId));
            }

            if (airshipId.IsNone)
            {
                throw new ArgumentException(
                    "This airship command requires a destination.",
                    nameof(airshipId));
            }

            var payload = new byte[RepairInstallPayloadLength];
            WriteUInt64(payload, 0, sourceInventoryId.High);
            WriteUInt64(payload, 8, sourceInventoryId.Low);
            WriteUInt64(payload, 16, itemId.High);
            WriteUInt64(payload, 24, itemId.Low);
            return new SimulationCommand(
                targetTick,
                sequence,
                AirshipCommandKinds.RepairInstall,
                playerId,
                airshipId,
                amount,
                payload);
        }

        public static void DecodeRepairInstall(
            SimulationCommand command,
            out StableId sourceInventoryId,
            out StableId itemId)
        {
            if (!string.Equals(
                    command.Kind,
                    AirshipCommandKinds.RepairInstall,
                    StringComparison.Ordinal)
                || command.InitiatorId.IsNone
                || command.DestinationId.IsNone
                || command.QuantizedValue <= 0L
                || command.Payload.Count != RepairInstallPayloadLength)
            {
                throw new SimulationInvariantException(
                    "Malformed canonical airship command 'airship.repair.install'.");
            }

            sourceInventoryId = ReadStableId(command.Payload, 0);
            itemId = ReadStableId(command.Payload, 16);
            if (sourceInventoryId.IsNone || itemId.IsNone)
            {
                throw new SimulationInvariantException(
                    "A repair-install payload cannot contain StableId.None.");
            }
        }

        internal static bool TryDecodeRepairInstall(
            SimulationCommand command,
            out StableId sourceInventoryId,
            out StableId itemId)
        {
            try
            {
                DecodeRepairInstall(command, out sourceInventoryId, out itemId);
                return true;
            }
            catch (SimulationInvariantException)
            {
                sourceInventoryId = StableId.None;
                itemId = StableId.None;
                return false;
            }
        }

        public static AirshipPilotInputState DecodePilotInput(SimulationCommand command)
        {
            RequireEnvelope(command, AirshipCommandKinds.PilotInput, PilotInputPayloadLength, true);
            return new AirshipPilotInputState(
                ReadInt16(command.Payload, 0),
                ReadInt16(command.Payload, 2),
                ReadInt16(command.Payload, 4),
                ReadInt16(command.Payload, 6));
        }

        public static StableId DecodeLandingSurfaceId(SimulationCommand command)
        {
            RequireEnvelope(
                command,
                AirshipCommandKinds.LandingRequest,
                StableIdPayloadLength,
                true);
            var result = ReadStableId(command.Payload, 0);
            if (result.IsNone)
            {
                throw new SimulationInvariantException(
                    "A landing-request payload cannot contain StableId.None.");
            }

            return result;
        }

        public static void ValidateEmpty(SimulationCommand command)
        {
            RequireEnvelope(command, command.Kind, 0, true);
        }

        internal static void ValidateForIngress(SimulationCommand command)
        {
            switch (command.Kind)
            {
                case AirshipCommandKinds.PilotInput:
                    DecodePilotInput(command);
                    return;
                case AirshipCommandKinds.LandingRequest:
                    DecodeLandingSurfaceId(command);
                    return;
                case AirshipCommandKinds.RepairInstall:
                    DecodeRepairInstall(command, out _, out _);
                    return;
                case AirshipCommandKinds.PlayerDestroyed:
                    RequireEnvelope(
                        command,
                        AirshipCommandKinds.PlayerDestroyed,
                        0,
                        false);
                    return;
                case AirshipCommandKinds.Takeoff:
                case AirshipCommandKinds.Board:
                case AirshipCommandKinds.Disembark:
                case AirshipCommandKinds.PilotBegin:
                case AirshipCommandKinds.PilotEnd:
                    ValidateEmpty(command);
                    return;
                default:
                    throw new ArgumentException(
                        $"Unknown authoritative AIR command kind '{command.Kind}'.",
                        nameof(command));
            }
        }

        private static SimulationCommand CreateEmpty(
            SimulationTick targetTick,
            ulong sequence,
            string kind,
            StableId playerId,
            StableId airshipId)
        {
            return Create(targetTick, sequence, kind, playerId, airshipId, Array.Empty<byte>());
        }

        private static SimulationCommand Create(
            SimulationTick targetTick,
            ulong sequence,
            string kind,
            StableId playerId,
            StableId airshipId,
            byte[] payload)
        {
            if (playerId.IsNone)
            {
                throw new ArgumentException("An airship command requires a player id.", nameof(playerId));
            }

            if (!string.Equals(kind, AirshipCommandKinds.PlayerDestroyed, StringComparison.Ordinal)
                && airshipId.IsNone)
            {
                throw new ArgumentException("This airship command requires a destination.", nameof(airshipId));
            }

            return new SimulationCommand(
                targetTick,
                sequence,
                kind,
                playerId,
                airshipId,
                0L,
                payload);
        }

        private static byte[] EncodePilotInput(AirshipPilotInputState input)
        {
            var payload = new byte[PilotInputPayloadLength];
            WriteInt16(payload, 0, input.ThrottleChangePermille);
            WriteInt16(payload, 2, input.LiftPermille);
            WriteInt16(payload, 4, input.YawDeltaPermille);
            WriteInt16(payload, 6, input.PitchDeltaPermille);
            return payload;
        }

        private static byte[] EncodeStableId(StableId id)
        {
            var payload = new byte[StableIdPayloadLength];
            WriteUInt64(payload, 0, id.High);
            WriteUInt64(payload, 8, id.Low);
            return payload;
        }

        private static void RequireEnvelope(
            SimulationCommand command,
            string expectedKind,
            int payloadLength,
            bool requiresDestination)
        {
            if (!string.Equals(command.Kind, expectedKind, StringComparison.Ordinal)
                || command.InitiatorId.IsNone
                || (requiresDestination && command.DestinationId.IsNone)
                || (!requiresDestination && !command.DestinationId.IsNone)
                || command.QuantizedValue != 0L
                || command.Payload.Count != payloadLength)
            {
                throw new SimulationInvariantException(
                    $"Malformed canonical airship command '{command.Kind}'.");
            }
        }

        private static void WriteInt16(byte[] target, int offset, short value)
        {
            WriteUInt16(target, offset, unchecked((ushort)value));
        }

        private static void WriteUInt16(byte[] target, int offset, ushort value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt64(byte[] target, int offset, ulong value)
        {
            for (var index = 0; index < 8; index++)
            {
                target[offset + index] = (byte)(value >> (index * 8));
            }
        }

        private static short ReadInt16(IReadOnlyList<byte> source, int offset)
        {
            return unchecked((short)ReadUInt16(source, offset));
        }

        private static ushort ReadUInt16(IReadOnlyList<byte> source, int offset)
        {
            return (ushort)(source[offset] | (source[offset + 1] << 8));
        }

        private static ulong ReadUInt64(IReadOnlyList<byte> source, int offset)
        {
            ulong result = 0UL;
            for (var index = 0; index < 8; index++)
            {
                result |= (ulong)source[offset + index] << (index * 8);
            }

            return result;
        }

        private static StableId ReadStableId(IReadOnlyList<byte> source, int offset)
        {
            return new StableId(ReadUInt64(source, offset), ReadUInt64(source, offset + 8));
        }
    }

}

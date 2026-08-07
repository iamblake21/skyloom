using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CML.Foundation;

namespace CML.Simulation
{
    /// <summary>
    /// A command whose target tick and payload are already quantized. Commands never
    /// contain presentation time or floating-point gameplay values.
    /// </summary>
    [Serializable]
    public readonly struct SimulationCommand : IEquatable<SimulationCommand>
    {
        private static readonly ReadOnlyCollection<byte> EmptyPayload =
            Array.AsReadOnly(Array.Empty<byte>());

        private readonly ReadOnlyCollection<byte> _payload;

        public SimulationCommand(SimulationTick targetTick, ulong sequence, string kind)
            : this(
                targetTick,
                sequence,
                kind,
                StableId.None,
                StableId.None,
                0L,
                Array.Empty<byte>())
        {
        }

        public SimulationCommand(
            SimulationTick targetTick,
            ulong sequence,
            string kind,
            StableId initiatorId,
            StableId destinationId,
            long quantizedValue,
            byte[] payload)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("A command kind cannot be empty.", nameof(kind));
            }

            TargetTick = targetTick;
            Sequence = sequence;
            Kind = kind.Normalize(NormalizationForm.FormC);
            InitiatorId = initiatorId;
            DestinationId = destinationId;
            QuantizedValue = quantizedValue;
            _payload = payload == null || payload.Length == 0
                ? EmptyPayload
                : Array.AsReadOnly((byte[])payload.Clone());
        }

        public SimulationTick TargetTick { get; }

        public ulong Sequence { get; }

        public string Kind { get; }

        public StableId InitiatorId { get; }

        public StableId DestinationId { get; }

        public long QuantizedValue { get; }

        public IReadOnlyList<byte> Payload => _payload ?? EmptyPayload;

        public bool IsWellFormed => !string.IsNullOrWhiteSpace(Kind);

        public byte[] CopyPayload()
        {
            var result = new byte[Payload.Count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = Payload[index];
            }

            return result;
        }

        public SimulationCommand WithTargetTick(SimulationTick targetTick)
        {
            return WithSchedule(targetTick, Sequence);
        }

        public SimulationCommand WithSchedule(SimulationTick targetTick, ulong sequence)
        {
            return new SimulationCommand(
                targetTick,
                sequence,
                Kind,
                InitiatorId,
                DestinationId,
                QuantizedValue,
                CopyPayload());
        }

        public bool Equals(SimulationCommand other)
        {
            return TargetTick == other.TargetTick
                && Sequence == other.Sequence
                && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
                && InitiatorId == other.InitiatorId
                && DestinationId == other.DestinationId
                && QuantizedValue == other.QuantizedValue
                && ByteSequenceEquals(Payload, other.Payload);
        }

        public override bool Equals(object obj)
        {
            return obj is SimulationCommand other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = TargetTick.GetHashCode();
                hash = (hash * 397) ^ Sequence.GetHashCode();
                hash = (hash * 397) ^ (Kind == null ? 0 : StringComparer.Ordinal.GetHashCode(Kind));
                hash = (hash * 397) ^ InitiatorId.GetHashCode();
                hash = (hash * 397) ^ DestinationId.GetHashCode();
                hash = (hash * 397) ^ QuantizedValue.GetHashCode();
                for (var index = 0; index < Payload.Count; index++)
                {
                    hash = (hash * 397) ^ Payload[index];
                }

                return hash;
            }
        }

        public static bool operator ==(SimulationCommand left, SimulationCommand right) => left.Equals(right);

        public static bool operator !=(SimulationCommand left, SimulationCommand right) => !left.Equals(right);

        private static bool ByteSequenceEquals(IReadOnlyList<byte> left, IReadOnlyList<byte> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Count; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class SimulationCommandKinds
    {
        public const string NoOp = "core.no-op";
        public const string AddQuantity = "core.quantity.add";
        public const string RemoveQuantity = "core.quantity.remove";
        public const string SetQuantity = "core.quantity.set";

        /// <summary>
        /// Moves items between two holders. Applied in phase 9, not phase 1: it has to
        /// see the item flow and the cycles of this tick before it decides what fits.
        /// </summary>
        public const string Transfer = "core.transfer";

        /// <summary>
        /// Creates one buffer, machine, funnel or belt lane. Phase 1 validates and
        /// allocates its persistent ID; phase 3 publishes the topology change.
        /// </summary>
        public const string BuildMachineGraphElement = "core.build.machine-graph-element";

        /// <summary>
        /// Removes one placed element and returns it, plus everything it still held,
        /// to the initiator's inventory. The target travels in DestinationId: salvage
        /// names an element that already exists, so it needs no encoded payload.
        /// </summary>
        public const string SalvageMachineGraphElement =
            "core.build.machine-graph-element.salvage";

        /// <summary>
        /// Sposta oggetti fra due slot dello stesso inventario.
        ///
        /// Non è un trasferimento: quello lavora fra endpoint e per costruzione
        /// rifiuta origine e destinazione coincidenti, quindi non poteva
        /// esprimere un riordino interno. Ma quale slot contiene cosa è stato
        /// canonico, quindi il riordino non può restare nella presentazione.
        ///
        /// L'inventario viaggia in <c>InitiatorId</c>, la quantità in
        /// <c>QuantizedValue</c> — zero significa l'intera pila d'origine — e i
        /// due indici nel payload.
        /// </summary>
        public const string MoveInventorySlot = "core.inventory.slot.move";

    }
}

using System;
using CML.Foundation;

namespace CML.Simulation
{
    /// <summary>
    /// Normative deterministic creation order inside one simulation tick.
    /// </summary>
    [Serializable]
    public readonly struct CreationKey : IEquatable<CreationKey>, IComparable<CreationKey>
    {
        public CreationKey(
            SimulationPhase phase,
            uint causeCode,
            StableId initiatorId,
            ulong commandSequence,
            uint outputIndex,
            uint localOrdinal)
        {
            if ((byte)phase < 1 || (byte)phase > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            if (initiatorId.IsNone)
            {
                throw new ArgumentException("A creation initiator cannot use the zero ID.", nameof(initiatorId));
            }

            Phase = phase;
            CauseCode = causeCode;
            InitiatorId = initiatorId;
            CommandSequence = commandSequence;
            OutputIndex = outputIndex;
            LocalOrdinal = localOrdinal;
        }

        public SimulationPhase Phase { get; }

        public uint CauseCode { get; }

        public StableId InitiatorId { get; }

        public ulong CommandSequence { get; }

        public uint OutputIndex { get; }

        public uint LocalOrdinal { get; }

        public int CompareTo(CreationKey other)
        {
            var comparison = Phase.CompareTo(other.Phase);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CauseCode.CompareTo(other.CauseCode);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = InitiatorId.CompareTo(other.InitiatorId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CommandSequence.CompareTo(other.CommandSequence);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = OutputIndex.CompareTo(other.OutputIndex);
            return comparison != 0
                ? comparison
                : LocalOrdinal.CompareTo(other.LocalOrdinal);
        }

        public bool Equals(CreationKey other)
        {
            return Phase == other.Phase
                && CauseCode == other.CauseCode
                && InitiatorId == other.InitiatorId
                && CommandSequence == other.CommandSequence
                && OutputIndex == other.OutputIndex
                && LocalOrdinal == other.LocalOrdinal;
        }

        public override bool Equals(object obj)
        {
            return obj is CreationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Phase;
                hash = (hash * 397) ^ (int)CauseCode;
                hash = (hash * 397) ^ InitiatorId.GetHashCode();
                hash = (hash * 397) ^ CommandSequence.GetHashCode();
                hash = (hash * 397) ^ (int)OutputIndex;
                return (hash * 397) ^ (int)LocalOrdinal;
            }
        }

        public static bool operator ==(CreationKey left, CreationKey right) => left.Equals(right);

        public static bool operator !=(CreationKey left, CreationKey right) => !left.Equals(right);
    }

    [Serializable]
    public readonly struct CreationRecord : IEquatable<CreationRecord>, IComparable<CreationRecord>
    {
        public CreationRecord(SimulationTick tick, CreationKey key, StableId entityId)
        {
            if (entityId.IsNone)
            {
                throw new ArgumentException("A created entity ID cannot be zero.", nameof(entityId));
            }

            Tick = tick;
            Key = key;
            EntityId = entityId;
        }

        public SimulationTick Tick { get; }

        public CreationKey Key { get; }

        public StableId EntityId { get; }

        public int CompareTo(CreationRecord other)
        {
            var comparison = Tick.CompareTo(other.Tick);
            return comparison != 0 ? comparison : Key.CompareTo(other.Key);
        }

        public bool Equals(CreationRecord other)
        {
            return Tick == other.Tick && Key == other.Key && EntityId == other.EntityId;
        }

        public override bool Equals(object obj)
        {
            return obj is CreationRecord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Tick.GetHashCode() * 397) ^ Key.GetHashCode()) * 397 ^ EntityId.GetHashCode();
            }
        }
    }
}

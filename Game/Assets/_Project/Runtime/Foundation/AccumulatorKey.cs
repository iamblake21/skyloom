using System;
using System.Text;

namespace CML.Foundation
{
    /// <summary>
    /// Canonical owner of a fractional remainder. One entity may own many
    /// independent accumulators across systems, resources and ports/cycles.
    /// </summary>
    [Serializable]
    public readonly struct AccumulatorKey : IEquatable<AccumulatorKey>, IComparable<AccumulatorKey>
    {
        public AccumulatorKey(
            string systemKind,
            string resourceKind,
            StableId entityId,
            uint portOrCycleIndex)
        {
            if (string.IsNullOrWhiteSpace(systemKind))
            {
                throw new ArgumentException("Accumulator system kind cannot be empty.", nameof(systemKind));
            }

            if (string.IsNullOrWhiteSpace(resourceKind))
            {
                throw new ArgumentException("Accumulator resource kind cannot be empty.", nameof(resourceKind));
            }

            if (entityId.IsNone)
            {
                throw new ArgumentException("Accumulator entity ID cannot be zero.", nameof(entityId));
            }

            SystemKind = systemKind.Normalize(NormalizationForm.FormC);
            ResourceKind = resourceKind.Normalize(NormalizationForm.FormC);
            EntityId = entityId;
            PortOrCycleIndex = portOrCycleIndex;
        }

        public string SystemKind { get; }

        public string ResourceKind { get; }

        public StableId EntityId { get; }

        public uint PortOrCycleIndex { get; }

        public int CompareTo(AccumulatorKey other)
        {
            var comparison = string.Compare(SystemKind, other.SystemKind, StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.Compare(ResourceKind, other.ResourceKind, StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = EntityId.CompareTo(other.EntityId);
            return comparison != 0
                ? comparison
                : PortOrCycleIndex.CompareTo(other.PortOrCycleIndex);
        }

        public bool Equals(AccumulatorKey other)
        {
            return string.Equals(SystemKind, other.SystemKind, StringComparison.Ordinal)
                && string.Equals(ResourceKind, other.ResourceKind, StringComparison.Ordinal)
                && EntityId == other.EntityId
                && PortOrCycleIndex == other.PortOrCycleIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is AccumulatorKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SystemKind == null ? 0 : StringComparer.Ordinal.GetHashCode(SystemKind);
                hash = (hash * 397) ^ (ResourceKind == null ? 0 : StringComparer.Ordinal.GetHashCode(ResourceKind));
                hash = (hash * 397) ^ EntityId.GetHashCode();
                return (hash * 397) ^ (int)PortOrCycleIndex;
            }
        }

        public static bool operator ==(AccumulatorKey left, AccumulatorKey right) => left.Equals(right);

        public static bool operator !=(AccumulatorKey left, AccumulatorKey right) => !left.Equals(right);

        public static bool operator <(AccumulatorKey left, AccumulatorKey right) => left.CompareTo(right) < 0;

        public static bool operator >(AccumulatorKey left, AccumulatorKey right) => left.CompareTo(right) > 0;
    }
}

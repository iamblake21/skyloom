using System;

namespace CML.Foundation
{
    /// <summary>
    /// Position on the authoritative 20 Hz simulation clock.
    /// </summary>
    [Serializable]
    public readonly struct SimulationTick : IEquatable<SimulationTick>, IComparable<SimulationTick>
    {
        public const int TicksPerSecond = 20;
        public const int MillisecondsPerTick = 50;

        public SimulationTick(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public SimulationTick Next()
        {
            return new SimulationTick(checked(Value + 1UL));
        }

        public int CompareTo(SimulationTick other) => Value.CompareTo(other.Value);

        public bool Equals(SimulationTick other) => Value == other.Value;

        public override bool Equals(object obj) => obj is SimulationTick other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString();

        public static bool operator ==(SimulationTick left, SimulationTick right) => left.Equals(right);

        public static bool operator !=(SimulationTick left, SimulationTick right) => !left.Equals(right);

        public static bool operator <(SimulationTick left, SimulationTick right) => left.Value < right.Value;

        public static bool operator >(SimulationTick left, SimulationTick right) => left.Value > right.Value;

        public static bool operator <=(SimulationTick left, SimulationTick right) => left.Value <= right.Value;

        public static bool operator >=(SimulationTick left, SimulationTick right) => left.Value >= right.Value;
    }
}

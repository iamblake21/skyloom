using System;
using System.Globalization;

namespace CML.Foundation
{
    /// <summary>
    /// Engine-independent 128-bit identifier used by persistent logical entities.
    /// The all-zero value is reserved for "none".
    /// </summary>
    [Serializable]
    public readonly struct StableId : IEquatable<StableId>, IComparable<StableId>
    {
        public static readonly StableId None = default;
        public static readonly StableId First = new StableId(0UL, 1UL);
        public static readonly StableId MaxValue = new StableId(ulong.MaxValue, ulong.MaxValue);

        public StableId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }

        public ulong Low { get; }

        public bool IsNone => High == 0UL && Low == 0UL;

        public int CompareTo(StableId other)
        {
            var highComparison = High.CompareTo(other.High);
            return highComparison != 0 ? highComparison : Low.CompareTo(other.Low);
        }

        public bool Equals(StableId other)
        {
            return High == other.High && Low == other.Low;
        }

        public override bool Equals(object obj)
        {
            return obj is StableId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (High.GetHashCode() * 397) ^ Low.GetHashCode();
            }
        }

        public override string ToString()
        {
            return High.ToString("x16", CultureInfo.InvariantCulture)
                + Low.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string value, out StableId id)
        {
            id = None;
            if (value == null || value.Length != 32)
            {
                return false;
            }

            if (!ulong.TryParse(value.Substring(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
                || !ulong.TryParse(value.Substring(16, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low))
            {
                return false;
            }

            id = new StableId(high, low);
            return true;
        }

        public static bool operator ==(StableId left, StableId right) => left.Equals(right);

        public static bool operator !=(StableId left, StableId right) => !left.Equals(right);

        public static bool operator <(StableId left, StableId right) => left.CompareTo(right) < 0;

        public static bool operator >(StableId left, StableId right) => left.CompareTo(right) > 0;

        public static bool operator <=(StableId left, StableId right) => left.CompareTo(right) <= 0;

        public static bool operator >=(StableId left, StableId right) => left.CompareTo(right) >= 0;
    }
}

using System;
using System.Globalization;
using System.Numerics;

namespace CML.Foundation
{
    /// <summary>
    /// Engine-independent unsigned 128-bit value for validated denominators and
    /// arithmetic state. Unity's locked C# profile predates System.UInt128.
    /// </summary>
    [Serializable]
    public readonly struct Unsigned128 : IEquatable<Unsigned128>, IComparable<Unsigned128>
    {
        internal static readonly BigInteger MaximumBigInteger =
            (BigInteger.One << 128) - BigInteger.One;

        public static readonly Unsigned128 Zero = default;
        public static readonly Unsigned128 One = new Unsigned128(0UL, 1UL);
        public static readonly Unsigned128 MaxValue =
            new Unsigned128(ulong.MaxValue, ulong.MaxValue);

        public Unsigned128(ulong value)
            : this(0UL, value)
        {
        }

        public Unsigned128(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }

        public ulong Low { get; }

        public bool IsZero => High == 0UL && Low == 0UL;

        public int CompareTo(Unsigned128 other)
        {
            var highComparison = High.CompareTo(other.High);
            return highComparison != 0 ? highComparison : Low.CompareTo(other.Low);
        }

        public bool Equals(Unsigned128 other)
        {
            return High == other.High && Low == other.Low;
        }

        public override bool Equals(object obj)
        {
            return obj is Unsigned128 other && Equals(other);
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
            return ToBigInteger().ToString(CultureInfo.InvariantCulture);
        }

        public bool TryToUInt64(out ulong value)
        {
            value = Low;
            return High == 0UL;
        }

        public ulong ToUInt64Checked()
        {
            if (!TryToUInt64(out var value))
            {
                throw new OverflowException("The unsigned 128-bit value does not fit in 64 bits.");
            }

            return value;
        }

        internal BigInteger ToBigInteger()
        {
            return ((BigInteger)High << 64) + Low;
        }

        internal static Unsigned128 FromBigIntegerChecked(BigInteger value)
        {
            if (value < BigInteger.Zero || value > MaximumBigInteger)
            {
                throw new OverflowException("The value does not fit in unsigned 128 bits.");
            }

            var low = (ulong)(value & ulong.MaxValue);
            var high = (ulong)(value >> 64);
            return new Unsigned128(high, low);
        }

        public static implicit operator Unsigned128(ulong value) => new Unsigned128(value);

        public static bool operator ==(Unsigned128 left, Unsigned128 right) => left.Equals(right);

        public static bool operator !=(Unsigned128 left, Unsigned128 right) => !left.Equals(right);

        public static bool operator <(Unsigned128 left, Unsigned128 right) => left.CompareTo(right) < 0;

        public static bool operator >(Unsigned128 left, Unsigned128 right) => left.CompareTo(right) > 0;

        public static bool operator <=(Unsigned128 left, Unsigned128 right) => left.CompareTo(right) <= 0;

        public static bool operator >=(Unsigned128 left, Unsigned128 right) => left.CompareTo(right) >= 0;
    }
}

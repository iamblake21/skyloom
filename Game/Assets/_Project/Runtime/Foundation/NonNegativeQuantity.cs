using System;
using System.Globalization;

namespace CML.Foundation
{
    /// <summary>
    /// A checked, 64-bit quantity that can never represent a negative value.
    /// Gameplay code must fail a transaction instead of clamping this value.
    /// </summary>
    [Serializable]
    public readonly struct NonNegativeQuantity :
        IEquatable<NonNegativeQuantity>,
        IComparable<NonNegativeQuantity>
    {
        public static readonly NonNegativeQuantity Zero = default;

        public NonNegativeQuantity(long value)
        {
            if (value < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A quantity cannot be negative.");
            }

            Value = value;
        }

        public long Value { get; }

        public bool IsZero => Value == 0L;

        public NonNegativeQuantity Add(NonNegativeQuantity amount)
        {
            return new NonNegativeQuantity(checked(Value + amount.Value));
        }

        public bool TryAdd(NonNegativeQuantity amount, out NonNegativeQuantity result)
        {
            try
            {
                result = Add(amount);
                return true;
            }
            catch (OverflowException)
            {
                result = this;
                return false;
            }
        }

        public NonNegativeQuantity Subtract(NonNegativeQuantity amount)
        {
            if (amount.Value > Value)
            {
                throw new InvalidOperationException("A quantity subtraction cannot produce a negative value.");
            }

            return new NonNegativeQuantity(Value - amount.Value);
        }

        public bool TrySubtract(NonNegativeQuantity amount, out NonNegativeQuantity result)
        {
            if (amount.Value > Value)
            {
                result = this;
                return false;
            }

            result = new NonNegativeQuantity(Value - amount.Value);
            return true;
        }

        public int CompareTo(NonNegativeQuantity other) => Value.CompareTo(other.Value);

        public bool Equals(NonNegativeQuantity other) => Value == other.Value;

        public override bool Equals(object obj) => obj is NonNegativeQuantity other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(NonNegativeQuantity left, NonNegativeQuantity right) => left.Equals(right);

        public static bool operator !=(NonNegativeQuantity left, NonNegativeQuantity right) => !left.Equals(right);

        public static bool operator <(NonNegativeQuantity left, NonNegativeQuantity right) => left.Value < right.Value;

        public static bool operator >(NonNegativeQuantity left, NonNegativeQuantity right) => left.Value > right.Value;

        public static bool operator <=(NonNegativeQuantity left, NonNegativeQuantity right) => left.Value <= right.Value;

        public static bool operator >=(NonNegativeQuantity left, NonNegativeQuantity right) => left.Value >= right.Value;
    }
}

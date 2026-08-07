using System;
using System.Numerics;

namespace CML.Foundation
{
    /// <summary>
    /// Exact fixed-denominator accumulator. The denominator is validated content
    /// data and never changes at runtime; blocked work simply leaves this value intact.
    /// </summary>
    [Serializable]
    public readonly struct RemainderAccumulator : IEquatable<RemainderAccumulator>
    {
        public RemainderAccumulator(ulong ownerDenominator, ulong remainder, uint ruleRevision)
            : this(new Unsigned128(ownerDenominator), new Unsigned128(remainder), ruleRevision)
        {
        }

        public RemainderAccumulator(
            Unsigned128 ownerDenominator,
            Unsigned128 remainder,
            uint ruleRevision)
        {
            if (ownerDenominator.IsZero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ownerDenominator),
                    "An accumulator denominator must be positive.");
            }

            if (remainder >= ownerDenominator)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(remainder),
                    "The Euclidean remainder must be lower than its denominator.");
            }

            OwnerDenominator = ownerDenominator;
            Remainder = remainder;
            RuleRevision = ruleRevision;
        }

        public Unsigned128 OwnerDenominator { get; }

        public Unsigned128 Remainder { get; }

        public uint RuleRevision { get; }

        public RemainderAdvance Advance(ulong numerator)
        {
            return Advance(new Unsigned128(numerator));
        }

        public RemainderAdvance Advance(Unsigned128 numerator)
        {
            return AdvanceExact(numerator.ToBigInteger());
        }

        public RemainderAdvance AdvanceScaled(ulong numerator, ulong service)
        {
            return AdvanceScaled(numerator, service, 1UL);
        }

        /// <summary>
        /// Advances by the product of three validated integer factors. This overload
        /// is used when a catalog rate also carries a fixed nominal-service scale.
        /// The exact intermediate is accepted through 128 bits and rejected above it.
        /// </summary>
        public RemainderAdvance AdvanceScaled(ulong numerator, ulong service, ulong scale)
        {
            var scaledNumerator = (BigInteger)numerator * service * scale;
            return AdvanceExact(scaledNumerator);
        }

        private RemainderAdvance AdvanceExact(BigInteger numerator)
        {
            if (numerator < BigInteger.Zero || numerator > Unsigned128.MaximumBigInteger)
            {
                throw new OverflowException("The scaled accumulator intermediate exceeds 128 bits.");
            }

            var stagedTotal = numerator + Remainder.ToBigInteger();
            if (stagedTotal > Unsigned128.MaximumBigInteger)
            {
                throw new OverflowException("The accumulator sum exceeds 128 bits.");
            }

            var produced = BigInteger.DivRem(
                stagedTotal,
                OwnerDenominator.ToBigInteger(),
                out var nextRemainder);
            if (produced > long.MaxValue)
            {
                throw new OverflowException("The accumulator result exceeds the supported quantity range.");
            }

            var next = new RemainderAccumulator(
                OwnerDenominator,
                Unsigned128.FromBigIntegerChecked(nextRemainder),
                RuleRevision);
            return new RemainderAdvance(next, new NonNegativeQuantity((long)produced));
        }

        public bool Equals(RemainderAccumulator other)
        {
            return OwnerDenominator == other.OwnerDenominator
                && Remainder == other.Remainder
                && RuleRevision == other.RuleRevision;
        }

        public override bool Equals(object obj)
        {
            return obj is RemainderAccumulator other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = OwnerDenominator.GetHashCode();
                hash = (hash * 397) ^ Remainder.GetHashCode();
                return (hash * 397) ^ (int)RuleRevision;
            }
        }

        public static bool operator ==(RemainderAccumulator left, RemainderAccumulator right) => left.Equals(right);

        public static bool operator !=(RemainderAccumulator left, RemainderAccumulator right) => !left.Equals(right);
    }

    [Serializable]
    public readonly struct RemainderAdvance
    {
        public RemainderAdvance(RemainderAccumulator accumulator, NonNegativeQuantity produced)
        {
            Accumulator = accumulator;
            Produced = produced;
        }

        public RemainderAccumulator Accumulator { get; }

        public NonNegativeQuantity Produced { get; }
    }
}

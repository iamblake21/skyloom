using System;
using CML.Foundation;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class FoundationNumericTests
    {
        [Test]
        public void NonNegativeQuantityRejectsNegativeUnderflowAndOverflow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NonNegativeQuantity(-1L));

            var five = new NonNegativeQuantity(5L);
            Assert.That(five.Subtract(new NonNegativeQuantity(3L)).Value, Is.EqualTo(2L));
            Assert.That(
                five.TrySubtract(new NonNegativeQuantity(6L), out var unchanged),
                Is.False);
            Assert.That(unchanged, Is.EqualTo(five));

            var maximum = new NonNegativeQuantity(long.MaxValue);
            Assert.Throws<OverflowException>(() => maximum.Add(new NonNegativeQuantity(1L)));
            Assert.That(maximum.TryAdd(new NonNegativeQuantity(1L), out unchanged), Is.False);
            Assert.That(unchanged, Is.EqualTo(maximum));
        }

        [Test]
        public void SevenPointFiveItemsPerMinuteProducesExactly225ItemsInThirtyMinutes()
        {
            // 7.5/minute = 15/2400 item per 50 ms tick.
            var accumulator = new RemainderAccumulator(2400UL, 0UL, 1U);
            var produced = NonNegativeQuantity.Zero;

            for (var tick = 0; tick < 36000; tick++)
            {
                var advance = accumulator.Advance(15UL);
                accumulator = advance.Accumulator;
                produced = produced.Add(advance.Produced);
            }

            Assert.That(produced.Value, Is.EqualTo(225L));
            Assert.That(accumulator.Remainder, Is.EqualTo(Unsigned128.Zero));
            Assert.That(accumulator.OwnerDenominator, Is.EqualTo(new Unsigned128(2400UL)));
        }

        [Test]
        public void AlternatingServicePreservesThreeQuarterItemRemainder()
        {
            // D_owner includes nominal service 100: 2400 * 100.
            var accumulator = new RemainderAccumulator(240000UL, 0UL, 7U);
            var produced = NonNegativeQuantity.Zero;

            for (var tick = 0; tick < 36000; tick++)
            {
                var service = tick % 2 == 0 ? 100UL : 50UL;
                var advance = accumulator.AdvanceScaled(15UL, service);
                accumulator = advance.Accumulator;
                produced = produced.Add(advance.Produced);
            }

            Assert.That(produced.Value, Is.EqualTo(168L));
            Assert.That(accumulator.Remainder, Is.EqualTo(new Unsigned128(180000UL)));
            Assert.That(accumulator.RuleRevision, Is.EqualTo(7U));
        }

        [Test]
        public void BlockingDoesNotErasePreviouslyEarnedRemainder()
        {
            var initial = new RemainderAccumulator(10UL, 0UL, 1U);
            var partial = initial.Advance(7UL).Accumulator;

            // A blocked cycle does not call Advance. Its immutable state is retained.
            var afterBlockedTicks = partial;
            var completed = afterBlockedTicks.Advance(3UL);

            Assert.That(completed.Produced.Value, Is.EqualTo(1L));
            Assert.That(completed.Accumulator.Remainder, Is.EqualTo(Unsigned128.Zero));
        }

        [Test]
        public void AccumulatorAcceptsAValid128BitIntermediate()
        {
            var wideButValid = new RemainderAccumulator(ulong.MaxValue, 0UL, 1U)
                .AdvanceScaled(ulong.MaxValue, 2UL);

            Assert.That(wideButValid.Produced.Value, Is.EqualTo(2L));
            Assert.That(wideButValid.Accumulator.Remainder, Is.EqualTo(Unsigned128.Zero));
        }

        [Test]
        public void DenominatorAndRemainderRemainExactAbove64Bits()
        {
            var twoToSixtyFour = new Unsigned128(1UL, 0UL);
            var oneBelow = new Unsigned128(0UL, ulong.MaxValue);
            var accumulator = new RemainderAccumulator(twoToSixtyFour, oneBelow, 3U);

            var completed = accumulator.Advance(1UL);

            Assert.That(completed.Produced.Value, Is.EqualTo(1L));
            Assert.That(completed.Accumulator.OwnerDenominator, Is.EqualTo(twoToSixtyFour));
            Assert.That(completed.Accumulator.Remainder, Is.EqualTo(Unsigned128.Zero));
        }

        [Test]
        public void AccumulatorSeparatesQuantityOverflowFromAReal128BitIntermediateOverflow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemainderAccumulator(0UL, 0UL, 1U));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RemainderAccumulator(4UL, 4UL, 1U));

            // Product fits unsigned 128, but cannot become a supported 64-bit quantity.
            var resultTooLarge = new RemainderAccumulator(1UL, 0UL, 1U);
            Assert.Throws<OverflowException>(() =>
                resultTooLarge.AdvanceScaled(ulong.MaxValue, ulong.MaxValue));

            // Three validated factors can exceed unsigned 128 and must fail before division.
            var intermediateTooLarge = new RemainderAccumulator(ulong.MaxValue, 0UL, 1U);
            Assert.Throws<OverflowException>(() =>
                intermediateTooLarge.AdvanceScaled(ulong.MaxValue, ulong.MaxValue, 2UL));
        }

        [Test]
        public void StableIdAllocatorAllocatesMaximumOnceThenFailsWithoutWrapping()
        {
            var allocator = new StableIdAllocator(StableId.MaxValue, false);

            Assert.That(allocator.TryAllocate(out var maximum), Is.True);
            Assert.That(maximum, Is.EqualTo(StableId.MaxValue));
            Assert.That(allocator.IsExhausted, Is.True);
            Assert.That(allocator.TryAllocate(out var rejected), Is.False);
            Assert.That(rejected, Is.EqualTo(StableId.None));
            Assert.That(allocator.NextId, Is.EqualTo(StableId.MaxValue));
        }

        [Test]
        public void ExhaustedAllocatorRestoreRequiresMaximumNextId()
        {
            Assert.Throws<ArgumentException>(() =>
                new StableIdAllocator(StableId.First, true));

            var restored = new StableIdAllocator(StableId.MaxValue, true);
            Assert.That(restored.IsExhausted, Is.True);
            Assert.That(restored.NextId, Is.EqualTo(StableId.MaxValue));
        }
    }
}

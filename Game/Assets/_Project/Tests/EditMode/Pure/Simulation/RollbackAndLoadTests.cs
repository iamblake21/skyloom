using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class RollbackAndLoadTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        [Test]
        public void AbortThenRetryEqualsCleanRunWhenSystemStateLivesInSimulationState()
        {
            var guard = new StableId(0UL, 700UL);
            var retried = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                new[] { new RequirePositiveQuantitySystem(guard) });

            var failed = retried.AdvanceOneTick();
            Assert.That(failed.Committed, Is.False);
            retried.EnqueueCommand(SetCommand(guard));
            var retry = retried.AdvanceOneTick();

            var clean = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                new[] { new RequirePositiveQuantitySystem(guard) });
            clean.EnqueueCommand(SetCommand(guard));
            var cleanResult = clean.AdvanceOneTick();

            Assert.That(retry.Committed, Is.True, retry.FailureCause);
            Assert.That(cleanResult.Committed, Is.True, cleanResult.FailureCause);
            Assert.That(
                LogicalStateHasher.ComputeHashHex(retried.State),
                Is.EqualTo(LogicalStateHasher.ComputeHashHex(clean.State)));
        }

        [Test]
        public void NonEmptyThirtySixThousandTickRunUsesOneWorkingClonePerTick()
        {
            var entity = new StableId(0UL, 701UL);
            var accumulator = new AccumulatorKey("test.rate", "test.item", entity, 0U);
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                new[] { new ExactRateSystem(entity, accumulator) });
            var clock = new FixedStepSimulationClock();

            var result = clock.Advance(TimeSpan.FromSeconds(1800D), engine);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CommittedTicks, Is.EqualTo(36000UL));
            Assert.That(engine.TickWorkingCloneCount, Is.EqualTo(36000UL));
            Assert.That(engine.State.GetQuantity(entity).Value, Is.EqualTo(225L));
            Assert.That(engine.State.TryGetAccumulator(accumulator, out var state), Is.True);
            Assert.That(state.Remainder, Is.EqualTo(Unsigned128.Zero));
            Assert.That(engine.State.GetQuantitiesCanonical().Count, Is.EqualTo(1));
            Assert.That(engine.State.GetAccumulatorsCanonical().Count, Is.EqualTo(1));
            Assert.That(clock.WholeTickDebt, Is.Zero);
        }

        [Test]
        public void MutableSystemStateIsRejectedBeforeFirstTick()
        {
            var state = new SimulationState(new SimulationTick(0UL), Revision);

            var exception = Assert.Throws<ArgumentException>(() =>
                new SimulationEngine(state, new[] { new MutableListSystem() }));

            Assert.That(exception.Message, Does.Contain("stateless contract"));
        }

        private static SimulationCommand SetCommand(StableId destination)
        {
            return new SimulationCommand(
                new SimulationTick(1UL),
                0UL,
                SimulationCommandKinds.SetQuantity,
                StableId.First,
                destination,
                1L,
                Array.Empty<byte>());
        }

        private sealed class RequirePositiveQuantitySystem : ISimulationPhaseSystem
        {
            private readonly StableId _guard;

            public RequirePositiveQuantitySystem(StableId guard)
            {
                _guard = guard;
            }

            public SimulationPhase Phase => SimulationPhase.CompletionDamageAndEventStaging;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 910UL);

            public void Execute(SimulationPhaseContext context)
            {
                if (context.GetQuantity(_guard).IsZero)
                {
                    context.AbortTick("Guard quantity is absent.");
                }
            }
        }

        private sealed class ExactRateSystem : ISimulationPhaseSystem
        {
            private readonly StableId _destination;
            private readonly AccumulatorKey _key;

            public ExactRateSystem(StableId destination, AccumulatorKey key)
            {
                _destination = destination;
                _key = key;
            }

            public SimulationPhase Phase => SimulationPhase.CyclesNeedsAndTimers;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 911UL);

            public void Execute(SimulationPhaseContext context)
            {
                if (!context.TryGetAccumulator(_key, out _))
                {
                    context.SetAccumulator(_key, new RemainderAccumulator(2400UL, 0UL, 1U));
                }

                var advance = context.AdvanceAccumulator(_key, 15UL);
                if (!advance.Produced.IsZero)
                {
                    context.AddQuantity(_destination, advance.Produced);
                }
            }
        }

        private sealed class MutableListSystem : ISimulationPhaseSystem
        {
            private readonly List<int> _futureAffectingState = new List<int>();

            public SimulationPhase Phase => SimulationPhase.CommandsAndConfiguration;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 912UL);

            public void Execute(SimulationPhaseContext context)
            {
                _futureAffectingState.Add(1);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class PipelineTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        [Test]
        public void PipelineExecutesAllTwelvePhasesInCanonicalOrder()
        {
            var systems = new List<ISimulationPhaseSystem>();
            for (byte phase = 1; phase <= 12; phase++)
            {
                systems.Add(new NoOpPhaseSystem((SimulationPhase)phase));
            }

            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                systems);
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.LastPhaseTrace.Count, Is.EqualTo(12));
            for (var index = 0; index < 12; index++)
            {
                Assert.That((byte)engine.LastPhaseTrace[index], Is.EqualTo(index + 1));
            }
        }

        [Test]
        public void APhaseReadsTheConsolidatedResultOfThePreviousPhase()
        {
            var owner = new StableId(0UL, 90UL);
            var systems = new ISimulationPhaseSystem[]
            {
                new SetQuantitySystem(
                    SimulationPhase.CommandsAndConfiguration,
                    new StableId(0UL, 1UL),
                    owner,
                    4L),
                new AssertAndSetQuantitySystem(
                    SimulationPhase.MovementAndPortalDetection,
                    new StableId(0UL, 2UL),
                    owner,
                    4L,
                    9L)
            };
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                systems);

            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.GetQuantity(owner).Value, Is.EqualTo(9L));
        }

        [Test]
        public void FailureInPhaseEightAbortsEveryEarlierMutationAndKeepsCommands()
        {
            var owner = new StableId(0UL, 99UL);
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var engine = new SimulationEngine(
                initial,
                new ISimulationPhaseSystem[]
                {
                    new SetQuantitySystem(
                        SimulationPhase.CommandsAndConfiguration,
                        new StableId(0UL, 1UL),
                        owner,
                        500L),
                    new AbortingSystem(
                        SimulationPhase.CompletionDamageAndEventStaging,
                        new StableId(0UL, 2UL))
                });
            engine.EnqueueCommand(new SimulationCommand(
                new SimulationTick(1UL),
                0UL,
                SimulationCommandKinds.NoOp));
            var beforeHash = LogicalStateHasher.ComputeHashHex(engine.State);

            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.False);
            Assert.That(result.FailedPhase, Is.EqualTo(SimulationPhase.CompletionDamageAndEventStaging));
            Assert.That(engine.State.Tick.Value, Is.Zero);
            Assert.That(engine.State.GetQuantity(owner), Is.EqualTo(NonNegativeQuantity.Zero));
            Assert.That(engine.State.PendingCommandCount, Is.EqualTo(1));
            Assert.That(LogicalStateHasher.ComputeHashHex(engine.State), Is.EqualTo(beforeHash));
        }

        [Test]
        public void ThrowingPhaseBoundarySubscriberCannotAffectTheAuthoritativeTick()
        {
            var cleanEngine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision));
            var observedEngine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision));
            var observerCalls = 0;
            observedEngine.PhaseBoundaryReached += phase =>
            {
                observerCalls++;
                throw new InvalidOperationException($"Observer failed at {phase}.");
            };

            var cleanResult = cleanEngine.AdvanceOneTick();
            var observedResult = observedEngine.AdvanceOneTick();

            Assert.That(cleanResult.Committed, Is.True, cleanResult.FailureCause);
            Assert.That(observedResult.Committed, Is.True, observedResult.FailureCause);
            Assert.That(observerCalls, Is.EqualTo(12));
            Assert.That(observedEngine.LastPhaseTrace, Is.EqualTo(cleanEngine.LastPhaseTrace));
            Assert.That(
                LogicalStateHasher.ComputeHashHex(observedEngine.State),
                Is.EqualTo(LogicalStateHasher.ComputeHashHex(cleanEngine.State)));
        }

        private sealed class NoOpPhaseSystem : ISimulationPhaseSystem
        {
            public NoOpPhaseSystem(SimulationPhase phase)
            {
                Phase = phase;
                StableOrderId = new StableId(0UL, (ulong)phase);
            }

            public SimulationPhase Phase { get; }

            public int Order => 0;

            public StableId StableOrderId { get; }

            public void Execute(SimulationPhaseContext context)
            {
            }
        }

        private sealed class SetQuantitySystem : ISimulationPhaseSystem
        {
            private readonly StableId _owner;
            private readonly long _value;

            public SetQuantitySystem(
                SimulationPhase phase,
                StableId stableOrderId,
                StableId owner,
                long value)
            {
                Phase = phase;
                StableOrderId = stableOrderId;
                _owner = owner;
                _value = value;
            }

            public SimulationPhase Phase { get; }

            public int Order => 0;

            public StableId StableOrderId { get; }

            public void Execute(SimulationPhaseContext context)
            {
                context.SetQuantity(_owner, new NonNegativeQuantity(_value));
            }
        }

        private sealed class AssertAndSetQuantitySystem : ISimulationPhaseSystem
        {
            private readonly StableId _owner;
            private readonly long _expected;
            private readonly long _next;

            public AssertAndSetQuantitySystem(
                SimulationPhase phase,
                StableId stableOrderId,
                StableId owner,
                long expected,
                long next)
            {
                Phase = phase;
                StableOrderId = stableOrderId;
                _owner = owner;
                _expected = expected;
                _next = next;
            }

            public SimulationPhase Phase { get; }

            public int Order => 0;

            public StableId StableOrderId { get; }

            public void Execute(SimulationPhaseContext context)
            {
                if (context.GetQuantity(_owner).Value != _expected)
                {
                    context.AbortTick("The previous phase was not visible.");
                }

                context.SetQuantity(_owner, new NonNegativeQuantity(_next));
            }
        }

        private sealed class AbortingSystem : ISimulationPhaseSystem
        {
            public AbortingSystem(SimulationPhase phase, StableId stableOrderId)
            {
                Phase = phase;
                StableOrderId = stableOrderId;
            }

            public SimulationPhase Phase { get; }

            public int Order => 0;

            public StableId StableOrderId { get; }

            public void Execute(SimulationPhaseContext context)
            {
                context.AbortTick("Deliberate rollback test.");
            }
        }
    }
}

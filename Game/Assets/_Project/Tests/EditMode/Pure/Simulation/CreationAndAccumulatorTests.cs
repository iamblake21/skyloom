using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.CanonicalEncoding;
using CML.Simulation.Replay;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class CreationAndAccumulatorTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        [Test]
        public void RandomizedCreationStagingProducesSameIdsAndHash()
        {
            var keys = CreationKeys();
            var reversed = new List<CreationKey>(keys);
            reversed.Reverse();
            var first = RunCreationBatch(keys, new SimulationState(new SimulationTick(0UL), Revision));
            var second = RunCreationBatch(reversed, new SimulationState(new SimulationTick(0UL), Revision));

            for (var index = 0; index < keys.Count; index++)
            {
                Assert.That(
                    first.State.TryGetCreatedEntityId(new SimulationTick(1UL), keys[index], out var firstId),
                    Is.True);
                Assert.That(
                    second.State.TryGetCreatedEntityId(new SimulationTick(1UL), keys[index], out var secondId),
                    Is.True);
                Assert.That(firstId, Is.EqualTo(secondId));
            }

            Assert.That(
                LogicalStateHasher.ComputeHashHex(first.State),
                Is.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)));
        }

        [Test]
        public void DuplicateCreationKeyAbortsBeforeAllocatorMutation()
        {
            var key = CreationKeys()[0];
            var engine = RunCreationBatch(
                new[] { key, key },
                new SimulationState(new SimulationTick(0UL), Revision),
                expectCommit: false);

            Assert.That(engine.State.Tick.Value, Is.Zero);
            Assert.That(engine.State.NextEntityId, Is.EqualTo(StableId.First));
            Assert.That(engine.State.GetCreationRecordsCanonical(), Is.Empty);
        }

        [Test]
        public void ExhaustedCreationBatchIsAtomicAtMaximumId()
        {
            var state = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                StableId.MaxValue,
                false);
            var engine = RunCreationBatch(CreationKeys(), state, expectCommit: false);

            Assert.That(engine.State.NextEntityId, Is.EqualTo(StableId.MaxValue));
            Assert.That(engine.State.IsEntityIdSpaceExhausted, Is.False);
            Assert.That(engine.State.GetCreationRecordsCanonical(), Is.Empty);
        }

        [Test]
        public void OneCreationCanConsumeMaximumIdWithoutWrapping()
        {
            var state = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                StableId.MaxValue,
                false);
            var key = CreationKeys()[0];
            var engine = RunCreationBatch(new[] { key }, state);

            Assert.That(
                engine.State.TryGetCreatedEntityId(new SimulationTick(1UL), key, out var id),
                Is.True);
            Assert.That(id, Is.EqualTo(StableId.MaxValue));
            Assert.That(engine.State.IsEntityIdSpaceExhausted, Is.True);
        }

        [Test]
        public void SameEntityOwnsIndependentAccumulatorPortsInCanonicalOrder()
        {
            var entity = new StableId(0UL, 55UL);
            var firstKey = new AccumulatorKey("machine", "ore", entity, 0U);
            var secondKey = new AccumulatorKey("machine", "ore", entity, 1U);
            var first = RunAccumulatorBatch(new[] { firstKey, secondKey });
            var second = RunAccumulatorBatch(new[] { secondKey, firstKey });

            Assert.That(first.State.TryGetAccumulator(firstKey, out _), Is.True);
            Assert.That(first.State.TryGetAccumulator(secondKey, out _), Is.True);
            Assert.That(first.State.GetAccumulatorsCanonical().Count, Is.EqualTo(2));
            Assert.That(
                LogicalStateHasher.ComputeHashHex(first.State),
                Is.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)));
        }

        [Test]
        public void AirshipBuilderRejectsPersistentIdSharedAcrossEntityTypes()
        {
            var shared = new StableId(0UL, 71UL);
            var builder = new AirshipSimulationStateBuilder()
                .AddAirship(shared, default);

            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    builder.AddPlayer(shared, default)).Message,
                Does.Contain("already assigned to airship"));
            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    builder.AddObstacle(
                        shared,
                        new AirshipVector3Millimetres(-1, -1, -1),
                        new AirshipVector3Millimetres(1, 1, 1))).Message,
                Does.Contain("already assigned to airship"));
            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    builder.AddLandingSurface(
                        shared,
                        default,
                        0,
                        1,
                        1,
                        StableId.None)).Message,
                Does.Contain("already assigned to airship"));
        }

        [Test]
        public void InitialAirEntityAtFirstAdvancesAllocatorBeforeStagedCreation()
        {
            var airship = new AirshipSimulationStateBuilder()
                .AddAirship(StableId.First, default)
                .Build();
            var initial = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                airship);
            var expectedCreatedId = new StableId(0UL, 2UL);
            var key = new CreationKey(
                SimulationPhase.CommandsAndConfiguration,
                88U,
                StableId.First,
                0UL,
                0U,
                0U);

            Assert.That(initial.NextEntityId, Is.EqualTo(expectedCreatedId));
            var engine = RunCreationBatch(new[] { key }, initial);

            Assert.That(
                engine.State.TryGetCreatedEntityId(
                    new SimulationTick(1UL),
                    key,
                    out var createdId),
                Is.True);
            Assert.That(createdId, Is.EqualTo(expectedCreatedId));
            Assert.That(createdId, Is.Not.EqualTo(StableId.First));
            Assert.That(engine.State.NextEntityId, Is.EqualTo(new StableId(0UL, 3UL)));
        }

        [Test]
        public void RestoredAllocatorMustFollowMaximumPersistentId()
        {
            var maximum = new StableId(3UL, ulong.MaxValue);
            var airship = new AirshipSimulationStateBuilder()
                .AddAirship(maximum, default)
                .Build();

            var exception = Assert.Throws<SimulationInvariantException>(() =>
                new SimulationState(
                    new SimulationTick(0UL),
                    Revision,
                    maximum,
                    false,
                    airship));

            Assert.That(exception.Message, Does.Contain("strictly greater"));

            var validNext = new StableId(4UL, 0UL);
            var restored = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                validNext,
                false,
                airship);
            Assert.That(restored.NextEntityId, Is.EqualTo(validNext));
        }

        [Test]
        public void InitialMaximumPersistentIdExhaustsWithoutPartialCreation()
        {
            var airship = new AirshipSimulationStateBuilder()
                .AddAirship(StableId.MaxValue, default)
                .Build();
            var initial = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                airship);
            var key = new CreationKey(
                SimulationPhase.CommandsAndConfiguration,
                100U,
                StableId.MaxValue,
                0UL,
                0U,
                0U);

            Assert.That(initial.NextEntityId, Is.EqualTo(StableId.MaxValue));
            Assert.That(initial.IsEntityIdSpaceExhausted, Is.True);

            var engine = RunCreationBatch(
                new[] { key },
                initial,
                expectCommit: false);

            Assert.That(engine.State.Tick.Value, Is.Zero);
            Assert.That(engine.State.NextEntityId, Is.EqualTo(StableId.MaxValue));
            Assert.That(engine.State.IsEntityIdSpaceExhausted, Is.True);
            Assert.That(engine.State.GetCreationRecordsCanonical(), Is.Empty);
            Assert.That(
                engine.State.GetAirshipSnapshot().TryGetAirship(
                    StableId.MaxValue,
                    out _),
                Is.True);
        }

        [Test]
        public void InitialAllocatorSurvivesCloneSerializationLoadAndReplay()
        {
            var existingId = new StableId(0UL, 90UL);
            var expectedCreatedId = new StableId(0UL, 91UL);
            var airship = new AirshipSimulationStateBuilder()
                .AddAirship(existingId, default)
                .Build();
            var initial = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                airship);
            var clone = initial.DeepClone();
            var restored = new SimulationState(
                initial.Tick,
                initial.ContentRevision,
                initial.NextEntityId,
                initial.IsEntityIdSpaceExhausted,
                initial.GetAirshipSnapshot());

            Assert.That(clone.NextEntityId, Is.EqualTo(expectedCreatedId));
            Assert.That(restored.NextEntityId, Is.EqualTo(expectedCreatedId));
            Assert.That(
                CanonicalStateSerializer.Serialize(clone),
                Is.EqualTo(CanonicalStateSerializer.Serialize(initial)));
            Assert.That(
                LogicalStateHasher.ComputeHashHex(restored),
                Is.EqualTo(LogicalStateHasher.ComputeHashHex(initial)));

            var key = new CreationKey(
                SimulationPhase.CommandsAndConfiguration,
                99U,
                existingId,
                0UL,
                0U,
                0U);
            var manual = RunCreationBatch(new[] { key }, initial);
            var finalHash = LogicalStateHasher.ComputeHashHex(manual.State);
            var replay = new ReplayLog(
                "stable-id-regression",
                restored,
                new SimulationTick(1UL));
            replay.AddCheckpoint(new SimulationTick(1UL), finalHash);

            var replayResult = ReplayRunner.Run(
                restored,
                replay,
                new[] { new CreationSystem(new[] { key }) });

            Assert.That(replayResult.FinalHash, Is.EqualTo(finalHash));
            Assert.That(
                replayResult.FinalState.TryGetCreatedEntityId(
                    new SimulationTick(1UL),
                    key,
                    out var replayCreatedId),
                Is.True);
            Assert.That(replayCreatedId, Is.EqualTo(expectedCreatedId));
        }

        private static List<CreationKey> CreationKeys()
        {
            return new List<CreationKey>
            {
                new CreationKey(
                    SimulationPhase.CommandsAndConfiguration,
                    20U,
                    new StableId(0UL, 2UL),
                    1UL,
                    0U,
                    0U),
                new CreationKey(
                    SimulationPhase.CommandsAndConfiguration,
                    10U,
                    new StableId(0UL, 3UL),
                    0UL,
                    1U,
                    0U),
                new CreationKey(
                    SimulationPhase.CommandsAndConfiguration,
                    10U,
                    new StableId(0UL, 1UL),
                    0UL,
                    0U,
                    0U)
            };
        }

        private static SimulationEngine RunCreationBatch(
            IEnumerable<CreationKey> keys,
            SimulationState state,
            bool expectCommit = true)
        {
            var engine = new SimulationEngine(state, new[] { new CreationSystem(keys) });
            var result = engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.EqualTo(expectCommit), result.FailureCause);
            return engine;
        }

        private static SimulationEngine RunAccumulatorBatch(IEnumerable<AccumulatorKey> keys)
        {
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                new[] { new AccumulatorSystem(keys) });
            var result = engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
            return engine;
        }

        private sealed class CreationSystem : ISimulationPhaseSystem
        {
            private readonly CreationKey _first;
            private readonly CreationKey _second;
            private readonly CreationKey _third;
            private readonly byte _count;

            public CreationSystem(IEnumerable<CreationKey> keys)
            {
                var copy = new List<CreationKey>(keys);
                if (copy.Count < 1 || copy.Count > 3)
                {
                    throw new ArgumentOutOfRangeException(nameof(keys));
                }

                _count = (byte)copy.Count;
                _first = copy[0];
                _second = copy.Count > 1 ? copy[1] : default;
                _third = copy.Count > 2 ? copy[2] : default;
            }

            public SimulationPhase Phase => SimulationPhase.CommandsAndConfiguration;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 900UL);

            public void Execute(SimulationPhaseContext context)
            {
                context.StageCreation(_first);
                if (_count > 1)
                {
                    context.StageCreation(_second);
                }

                if (_count > 2)
                {
                    context.StageCreation(_third);
                }
            }
        }

        private sealed class AccumulatorSystem : ISimulationPhaseSystem
        {
            private readonly AccumulatorKey _first;
            private readonly AccumulatorKey _second;

            public AccumulatorSystem(IEnumerable<AccumulatorKey> keys)
            {
                var copy = new List<AccumulatorKey>(keys);
                if (copy.Count != 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(keys));
                }

                _first = copy[0];
                _second = copy[1];
            }

            public SimulationPhase Phase => SimulationPhase.CommandsAndConfiguration;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 901UL);

            public void Execute(SimulationPhaseContext context)
            {
                context.SetAccumulator(
                    _first,
                    new RemainderAccumulator(100UL, _first.PortOrCycleIndex, 1U));
                context.SetAccumulator(
                    _second,
                    new RemainderAccumulator(100UL, _second.PortOrCycleIndex, 1U));
            }
        }
    }
}

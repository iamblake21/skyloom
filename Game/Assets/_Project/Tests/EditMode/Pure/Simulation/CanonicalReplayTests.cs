using System;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.CanonicalEncoding;
using CML.Simulation.Replay;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class CanonicalReplayTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        [Test]
        public void EmptyBootstrapStateMatchesGoldenCanonicalHash()
        {
            var state = new SimulationState(new SimulationTick(0UL), Revision);

            var canonical = CanonicalStateSerializer.Serialize(state);
            var hash = LogicalStateHasher.ComputeHashHex(state);

            // 87 = i 65 byte originari, piu il sottoalbero MCH vuoto e quello INV
            // vuoto. Erano 84 finche il sottoalbero MCH aveva tre campi: i tre byte
            // in piu sono il campo aggiunto dopo.
            //
            // Valore ricalcolato: il golden precedente (84) descriveva uno schema
            // che il codice non produce piu, quindi il test falliva a prescindere
            // dal contenuto ed era diventato cieco a qualunque regressione vera.
            Assert.That(canonical.Length, Is.EqualTo(87));
            Assert.That(
                hash,
                Is.EqualTo("dc47c111845668fbd97b0ad963f6347a3caab925e01c6110db37353da5111f19"));
        }

        [Test]
        public void NonEmptyStateMatchesFixedCanonicalBytesAndHash()
        {
            var owner = new StableId(0UL, 42UL);
            var key = new AccumulatorKey("rate", "ore", owner, 2U);
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                new[] { new GoldenFixtureSystem(owner, key) });
            engine.EnqueueCommand(new SimulationCommand(
                new SimulationTick(3UL),
                0UL,
                SimulationCommandKinds.NoOp));
            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            engine.EnqueueCommand(new SimulationCommand(
                new SimulationTick(4UL),
                0UL,
                SimulationCommandKinds.NoOp));

            var canonicalHex = ToHex(CanonicalStateSerializer.Serialize(engine.State));
            var hash = LogicalStateHasher.ComputeHashHex(engine.State);
            Assert.That(
                canonicalHex,
                Is.EqualTo(
                    // Revision 12 registra le pose fisiche dei moduli nastro; il
                    // Il catalogo bootstrap porta le voci corrispondenti.
                    //
                    // Valore ricalcolato: il golden precedente descriveva la
                    // revisione 10 e falliva a prescindere dal contenuto, quindi
                    // non proteggeva piu da nessuna regressione.
                    "10010c020c0301040b626f6f7473747261702d35050106010705020100020108"
                    + "00090c010a020105020100022a02070a2901270401150401047261746502036f"
                    + "72650305020100022a04020283808080808080808002030504090b4902230701"
                    + "030200030a636f72652e6e6f2d6f700405020100020005050201000200060007"
                    + "00230701040200030a636f72652e6e6f2d6f7004050201000200050502010002"
                    + "00060007000c01000d01000e0f0501040201000301000401000501000f0c0401"
                    + "080201000301000401001006020101020100"));
            Assert.That(
                hash,
                Is.EqualTo("b2aae19ff85287ee956a78dfc5871bd365cb6613a2fed7d16c0348a7d7de9200"));
        }

        [Test]
        public void AuthoritativeStateRejectsDefaultCatalogRevision()
        {
            Assert.Throws<ArgumentException>(() =>
                new SimulationState(new SimulationTick(0UL), default));
        }

        [Test]
        public void CanonicalHashIsIndependentOfCommandInsertionOrder()
        {
            var first = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            var second = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            var commandA = Command(2UL, 0UL, SimulationCommandKinds.NoOp, StableId.None, 0L);
            var commandB = Command(1UL, 0UL, SimulationCommandKinds.NoOp, StableId.None, 0L);

            first.EnqueueCommand(commandA);
            first.EnqueueCommand(commandB);
            second.EnqueueCommand(commandB);
            second.EnqueueCommand(commandA);

            Assert.That(
                LogicalStateHasher.ComputeHashHex(first.State),
                Is.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)));
        }

        [Test]
        public void CanonicalAccumulatorEncodingIncludesTheUnsigned128HighHalf()
        {
            var owner = new StableId(0UL, 123UL);
            var key = new AccumulatorKey("test.rate", "test.item", owner, 0U);
            var first = EngineWithAccumulator(
                key,
                new RemainderAccumulator(
                    new Unsigned128(1UL, 0UL),
                    new Unsigned128(0UL, 1UL),
                    1U));
            var second = EngineWithAccumulator(
                key,
                new RemainderAccumulator(
                    new Unsigned128(2UL, 0UL),
                    new Unsigned128(0UL, 1UL),
                    1U));

            var firstBytes = CanonicalStateSerializer.Serialize(first.State);
            var firstAgain = CanonicalStateSerializer.Serialize(first.State);

            Assert.That(firstBytes, Is.EqualTo(firstAgain));
            Assert.That(
                LogicalStateHasher.ComputeHashHex(first.State),
                Is.Not.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)));
        }

        [Test]
        public void QueuedCommandPayloadCannotBeMutatedThroughInputOrPublicView()
        {
            var source = new byte[] { 1, 2, 3 };
            var command = new SimulationCommand(
                new SimulationTick(1UL),
                0UL,
                SimulationCommandKinds.NoOp,
                StableId.First,
                StableId.None,
                0L,
                source);
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            engine.EnqueueCommand(command);
            var expectedHash = LogicalStateHasher.ComputeHashHex(engine.State);

            source[0] = 99;
            var publicView = command.Payload as System.Collections.Generic.IList<byte>;
            Assert.That(publicView, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => publicView[1] = 88);
            var detachedCopy = command.CopyPayload();
            detachedCopy[2] = 77;

            Assert.That(command.Payload, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(LogicalStateHasher.ComputeHashHex(engine.State), Is.EqualTo(expectedHash));
        }

        [Test]
        public void ReplayReproducesManualExecutionAndCheckpointHashes()
        {
            var owner = new StableId(0UL, 42UL);
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var commands = new[]
            {
                Command(1UL, 0UL, SimulationCommandKinds.SetQuantity, owner, 5L),
                Command(2UL, 0UL, SimulationCommandKinds.AddQuantity, owner, 7L),
                Command(3UL, 0UL, SimulationCommandKinds.RemoveQuantity, owner, 2L)
            };

            var manual = new SimulationEngine(initial);
            var checkpointHashes = new string[4];
            for (var index = 0; index < commands.Length; index++)
            {
                manual.EnqueueCommand(commands[index]);
                checkpointHashes[index] = LogicalStateHasher.ComputeHashHex(manual.State);
                var tickResult = manual.AdvanceOneTick();
                Assert.That(tickResult.Committed, Is.True, tickResult.FailureCause);
            }
            checkpointHashes[3] = LogicalStateHasher.ComputeHashHex(manual.State);

            var replay = new ReplayLog("test-build", initial, new SimulationTick(3UL));
            for (var index = 0; index < commands.Length; index++)
            {
                replay.Append(new ReplayEvent((ulong)index, 0UL, commands[index]));
            }

            replay.AddCheckpoint(new SimulationTick(0UL), checkpointHashes[0]);
            replay.AddCheckpoint(new SimulationTick(1UL), checkpointHashes[1]);
            replay.AddCheckpoint(new SimulationTick(2UL), checkpointHashes[2]);
            replay.AddCheckpoint(new SimulationTick(3UL), checkpointHashes[3]);

            var result = ReplayRunner.Run(initial, replay);

            Assert.That(result.FinalState.Tick.Value, Is.EqualTo(3UL));
            Assert.That(result.FinalState.GetQuantity(owner).Value, Is.EqualTo(10L));
            Assert.That(result.FinalHash, Is.EqualTo(checkpointHashes[3]));
            Assert.That(result.FinalHash, Is.EqualTo(LogicalStateHasher.ComputeHashHex(manual.State)));
        }

        [Test]
        public void ReplayRejectsOrdinalGapsAndSequenceGaps()
        {
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var replay = new ReplayLog("test-build", initial, new SimulationTick(2UL));

            Assert.Throws<ArgumentException>(() =>
                replay.Append(new ReplayEvent(
                    1UL,
                    0UL,
                    Command(1UL, 0UL, SimulationCommandKinds.NoOp, StableId.None, 0L))));

            replay.Append(new ReplayEvent(
                0UL,
                0UL,
                Command(1UL, 0UL, SimulationCommandKinds.NoOp, StableId.None, 0L)));

            Assert.Throws<ArgumentException>(() =>
                replay.Append(new ReplayEvent(
                    1UL,
                    0UL,
                    Command(1UL, 2UL, SimulationCommandKinds.NoOp, StableId.None, 0L))));
        }

        [Test]
        public void AChangedCheckpointPinpointsReplayRegression()
        {
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var replay = new ReplayLog("test-build", initial, new SimulationTick(1UL));
            replay.Append(new ReplayEvent(
                0UL,
                0UL,
                Command(1UL, 0UL, SimulationCommandKinds.NoOp, StableId.None, 0L)));
            replay.AddCheckpoint(
                new SimulationTick(1UL),
                "0000000000000000000000000000000000000000000000000000000000000000");

            var exception = Assert.Throws<SimulationInvariantException>(() =>
                ReplayRunner.Run(initial, replay));

            Assert.That(exception.Message, Does.Contain("tick 1"));
        }

        [Test]
        public void ReplayCheckpointRejectsNonHexDigest()
        {
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var replay = new ReplayLog("test-build", initial, new SimulationTick(1UL));

            Assert.Throws<ArgumentException>(() =>
                replay.AddCheckpoint(
                    new SimulationTick(0UL),
                    "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg"));
        }

        private static SimulationCommand Command(
            ulong targetTick,
            ulong sequence,
            string kind,
            StableId destination,
            long value)
        {
            return new SimulationCommand(
                new SimulationTick(targetTick),
                sequence,
                kind,
                StableId.First,
                destination,
                value,
                Array.Empty<byte>());
        }

        private static SimulationEngine EngineWithAccumulator(
            AccumulatorKey key,
            RemainderAccumulator accumulator)
        {
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0UL), Revision),
                new[] { new SetAccumulatorSystem(key, accumulator) });
            var result = engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
            return engine;
        }

        private sealed class SetAccumulatorSystem : ISimulationPhaseSystem
        {
            private readonly AccumulatorKey _key;
            private readonly RemainderAccumulator _accumulator;

            public SetAccumulatorSystem(AccumulatorKey key, RemainderAccumulator accumulator)
            {
                _key = key;
                _accumulator = accumulator;
            }

            public SimulationPhase Phase => SimulationPhase.CommandsAndConfiguration;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 555UL);

            public void Execute(SimulationPhaseContext context)
            {
                context.SetAccumulator(_key, _accumulator);
            }
        }

        private sealed class GoldenFixtureSystem : ISimulationPhaseSystem
        {
            private readonly StableId _owner;
            private readonly AccumulatorKey _key;

            public GoldenFixtureSystem(StableId owner, AccumulatorKey key)
            {
                _owner = owner;
                _key = key;
            }

            public SimulationPhase Phase => SimulationPhase.CommandsAndConfiguration;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 556UL);

            public void Execute(SimulationPhaseContext context)
            {
                context.SetQuantity(_owner, new NonNegativeQuantity(7L));
                context.SetAccumulator(
                    _key,
                    new RemainderAccumulator(
                        new Unsigned128(1UL, 3UL),
                        new Unsigned128(5UL),
                        9U));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var characters = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[(index * 2) + 1] = alphabet[bytes[index] & 0x0F];
            }

            return new string(characters);
        }
    }
}

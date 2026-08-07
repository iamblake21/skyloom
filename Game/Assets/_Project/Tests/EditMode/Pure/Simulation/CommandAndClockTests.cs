using System;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class CommandAndClockTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        [Test]
        public void QueueOrdersByTargetTickThenSequenceAndRejectsDuplicateSequence()
        {
            var queue = new SimulationCommandQueue();
            queue.Enqueue(new SimulationCommand(new SimulationTick(2UL), 0UL, SimulationCommandKinds.NoOp));
            queue.Enqueue(new SimulationCommand(new SimulationTick(1UL), 0UL, SimulationCommandKinds.NoOp));
            queue.Enqueue(new SimulationCommand(new SimulationTick(1UL), 1UL, SimulationCommandKinds.NoOp));

            var commands = queue.ToCanonicalList();
            Assert.That(commands[0].TargetTick.Value, Is.EqualTo(1UL));
            Assert.That(commands[0].Sequence, Is.EqualTo(0UL));
            Assert.That(commands[1].TargetTick.Value, Is.EqualTo(1UL));
            Assert.That(commands[1].Sequence, Is.EqualTo(1UL));
            Assert.That(commands[2].TargetTick.Value, Is.EqualTo(2UL));
            Assert.That(commands[2].Sequence, Is.EqualTo(0UL));

            Assert.Throws<ArgumentException>(() =>
                queue.Enqueue(new SimulationCommand(
                    new SimulationTick(1UL),
                    1UL,
                    SimulationCommandKinds.NoOp,
                    StableId.First,
                    new StableId(0UL, 9UL),
                    0L,
                        Array.Empty<byte>())));
        }

        [Test]
        public void QueueRejectsSequenceGapBeforeMutation()
        {
            var queue = new SimulationCommandQueue();

            Assert.Throws<ArgumentException>(() =>
                queue.Enqueue(new SimulationCommand(
                    new SimulationTick(3UL),
                    1UL,
                    SimulationCommandKinds.NoOp)));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void CommandComparerUsesDestinationAsDeterministicMalformedStreamTieBreak()
        {
            var earlierDestination = new SimulationCommand(
                new SimulationTick(4UL),
                2UL,
                SimulationCommandKinds.NoOp,
                StableId.First,
                new StableId(0UL, 10UL),
                0L,
                Array.Empty<byte>());
            var laterDestination = new SimulationCommand(
                new SimulationTick(4UL),
                2UL,
                SimulationCommandKinds.NoOp,
                StableId.First,
                new StableId(0UL, 11UL),
                0L,
                Array.Empty<byte>());

            Assert.That(
                SimulationCommandComparer.Instance.Compare(earlierDestination, laterDestination),
                Is.LessThan(0));
        }

        [Test]
        public void QueueRejectsDefaultCommandInsteadOfAcceptingNonCanonicalData()
        {
            var queue = new SimulationCommandQueue();
            Assert.Throws<ArgumentException>(() => queue.Enqueue(default));
        }

        [Test]
        public void IngressRejectsUnknownNegativeAndMissingDestinationCommands()
        {
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));

            Assert.Throws<ArgumentException>(() =>
                engine.EnqueueCommand(new SimulationCommand(
                    new SimulationTick(1UL),
                    0UL,
                    "poison.unknown")));
            Assert.Throws<ArgumentException>(() =>
                engine.EnqueueCommand(new SimulationCommand(
                    new SimulationTick(1UL),
                    0UL,
                    SimulationCommandKinds.AddQuantity,
                    StableId.First,
                    StableId.None,
                    1L,
                    Array.Empty<byte>())));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                engine.EnqueueCommand(new SimulationCommand(
                    new SimulationTick(1UL),
                    0UL,
                    SimulationCommandKinds.AddQuantity,
                    StableId.First,
                    new StableId(0UL, 9UL),
                    -1L,
                    Array.Empty<byte>())));
            Assert.That(engine.State.PendingCommandCount, Is.Zero);
        }

        [Test]
        public void CoreCommandsApplyInSequenceAtTheirQuantizedTick()
        {
            var owner = new StableId(0UL, 10UL);
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            engine.EnqueueCommand(Command(1UL, 0UL, SimulationCommandKinds.SetQuantity, owner, 5L));
            engine.EnqueueCommand(Command(1UL, 1UL, SimulationCommandKinds.AddQuantity, owner, 3L));
            engine.EnqueueCommand(Command(1UL, 2UL, SimulationCommandKinds.RemoveQuantity, owner, 2L));

            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.Tick.Value, Is.EqualTo(1UL));
            Assert.That(engine.State.GetQuantity(owner).Value, Is.EqualTo(6L));
            Assert.That(engine.State.PendingCommandCount, Is.Zero);
        }

        [Test]
        public void QuantityUnderflowIsRejectedConsumedAndDoesNotStallFollowingTicks()
        {
            var owner = new StableId(0UL, 12UL);
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            engine.EnqueueCommand(Command(
                1UL,
                0UL,
                SimulationCommandKinds.RemoveQuantity,
                owner,
                1L));

            var rejectedTick = engine.AdvanceOneTick();
            var followingTick = engine.AdvanceOneTick();

            Assert.That(rejectedTick.Committed, Is.True, rejectedTick.FailureCause);
            Assert.That(followingTick.Committed, Is.True, followingTick.FailureCause);
            Assert.That(engine.State.Tick.Value, Is.EqualTo(2UL));
            Assert.That(engine.State.GetQuantity(owner).Value, Is.Zero);
            Assert.That(engine.State.PendingCommandCount, Is.Zero);
            Assert.That(engine.State.GetCommandRejectionsCanonical().Count, Is.EqualTo(1));
            Assert.That(
                engine.State.GetCommandRejectionsCanonical()[0].Reason,
                Is.EqualTo(CommandRejectionReason.InsufficientQuantity));
        }

        [Test]
        public void QuantityOverflowIsRejectedAfterPriorCommandsAndRetryProgresses()
        {
            var owner = new StableId(0UL, 13UL);
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            engine.EnqueueCommand(Command(
                1UL,
                0UL,
                SimulationCommandKinds.SetQuantity,
                owner,
                long.MaxValue));
            engine.EnqueueCommand(Command(
                1UL,
                1UL,
                SimulationCommandKinds.AddQuantity,
                owner,
                1L));

            var rejectedTick = engine.AdvanceOneTick();
            var followingTick = engine.AdvanceOneTick();

            Assert.That(rejectedTick.Committed, Is.True, rejectedTick.FailureCause);
            Assert.That(followingTick.Committed, Is.True, followingTick.FailureCause);
            Assert.That(engine.State.GetQuantity(owner).Value, Is.EqualTo(long.MaxValue));
            Assert.That(engine.State.GetCommandRejectionsCanonical().Count, Is.EqualTo(1));
            Assert.That(
                engine.State.GetCommandRejectionsCanonical()[0].Reason,
                Is.EqualTo(CommandRejectionReason.QuantityOverflow));
        }

        [TestCase(30U)]
        [TestCase(60U)]
        [TestCase(144U)]
        public void TenSecondsAtCertifiedFrameRatesExecutesExactlyTwoHundredTicks(uint frameRate)
        {
            var engine = CreateFrameRateEngine();
            var clock = new FixedStepSimulationClock();
            var pacer = new ExactFramePacer(frameRate);
            var frames = checked((int)(frameRate * 10U));
            ulong committed = 0UL;

            for (var frame = 0; frame < frames; frame++)
            {
                var advance = clock.Advance(pacer.NextFrameDuration(), engine);
                Assert.That(advance.Succeeded, Is.True);
                committed = checked(committed + advance.CommittedTicks);
            }

            Assert.That(committed, Is.EqualTo(200UL));
            Assert.That(engine.State.Tick.Value, Is.EqualTo(200UL));
            Assert.That(clock.WholeTickDebt, Is.Zero);
            Assert.That(clock.PendingTimeSpanTicks, Is.Zero);
            Assert.That(
                LogicalStateHasher.ComputeHashHex(engine.State),
                Is.EqualTo(CertifiedFrameRateHash()));
        }

        [Test]
        public void EighteenHundredSecondsExecutesThirtySixThousandTicksWithoutDebt()
        {
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            var clock = new FixedStepSimulationClock();

            var result = clock.Advance(TimeSpan.FromSeconds(1800D), engine);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CommittedTicks, Is.EqualTo(36000UL));
            Assert.That(engine.State.Tick.Value, Is.EqualTo(36000UL));
            Assert.That(clock.WholeTickDebt, Is.Zero);
            Assert.That(clock.PendingTimeSpanTicks, Is.Zero);
        }

        [Test]
        public void TickCounterExhaustionDoesNotPoisonEngineAdvanceState()
        {
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(ulong.MaxValue), Revision));

            Assert.Throws<OverflowException>(() => engine.AdvanceOneTick());
            Assert.That(engine.IsAdvancing, Is.False);
            Assert.That(engine.TickWorkingCloneCount, Is.Zero);
            Assert.Throws<OverflowException>(() => engine.AdvanceOneTick());
            Assert.That(engine.IsAdvancing, Is.False);
        }

        private static SimulationEngine CreateFrameRateEngine()
        {
            var owner = new StableId(0UL, 77UL);
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            engine.EnqueueCommand(Command(1UL, 0UL, SimulationCommandKinds.SetQuantity, owner, 100L));
            engine.EnqueueCommand(Command(100UL, 0UL, SimulationCommandKinds.AddQuantity, owner, 25L));
            engine.EnqueueCommand(Command(200UL, 0UL, SimulationCommandKinds.RemoveQuantity, owner, 5L));
            return engine;
        }

        private static string CertifiedFrameRateHash()
        {
            var engine = CreateFrameRateEngine();
            for (var tick = 0; tick < 200; tick++)
            {
                var result = engine.AdvanceOneTick();
                Assert.That(result.Committed, Is.True, result.FailureCause);
            }

            return LogicalStateHasher.ComputeHashHex(engine.State);
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
    }
}

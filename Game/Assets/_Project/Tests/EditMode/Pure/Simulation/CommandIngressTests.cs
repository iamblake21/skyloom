using System;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Replay;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class CommandIngressTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        [Test]
        public void EarlyFutureCommandMovesFromInboxToScheduledQueueAtPhaseOne()
        {
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            var command = new SimulationCommand(
                new SimulationTick(3UL),
                0UL,
                SimulationCommandKinds.NoOp);

            var accepted = engine.EnqueueCommand(command);

            Assert.That(accepted.AcceptedAfterTick.Value, Is.Zero);
            Assert.That(engine.State.InboxCommandCount, Is.EqualTo(1));
            Assert.That(engine.State.ScheduledCommandCount, Is.Zero);

            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.InboxCommandCount, Is.Zero);
            Assert.That(engine.State.ScheduledCommandCount, Is.EqualTo(1));
            Assert.That(engine.State.GetPendingCommandsCanonical()[0], Is.EqualTo(command));
        }

        [Test]
        public void CommandInjectedAfterPhaseOneIsRetargetedAndNeverLost()
        {
            var owner = new StableId(0UL, 400UL);
            var engine = new SimulationEngine(new SimulationState(new SimulationTick(0UL), Revision));
            engine.EnqueueCommand(new SimulationCommand(
                new SimulationTick(2UL),
                0UL,
                SimulationCommandKinds.NoOp));
            SimulationCommandAcceptance acceptance = default;
            engine.PhaseBoundaryReached += phase =>
            {
                if (phase == SimulationPhase.ItemFluidFlowAndReservations
                    && engine.State.Tick.Value == 0UL)
                {
                    acceptance = engine.EnqueueCommand(new SimulationCommand(
                        new SimulationTick(1UL),
                        7UL,
                        SimulationCommandKinds.SetQuantity,
                        StableId.First,
                        owner,
                        9L,
                        Array.Empty<byte>()));
                }
            };

            var first = engine.AdvanceOneTick();

            Assert.That(first.Committed, Is.True, first.FailureCause);
            Assert.That(acceptance.AcceptedAfterTick.Value, Is.Zero);
            Assert.That(acceptance.Command.TargetTick.Value, Is.EqualTo(2UL));
            Assert.That(acceptance.Command.Sequence, Is.EqualTo(1UL));
            Assert.That(engine.State.InboxCommandCount, Is.EqualTo(1));
            Assert.That(engine.State.ScheduledCommandCount, Is.EqualTo(1));
            Assert.That(engine.State.GetQuantity(owner).Value, Is.Zero);

            var second = engine.AdvanceOneTick();

            Assert.That(second.Committed, Is.True, second.FailureCause);
            Assert.That(engine.State.GetQuantity(owner).Value, Is.EqualTo(9L));
            Assert.That(engine.State.PendingCommandCount, Is.Zero);
        }

        [Test]
        public void ReplayAcceptanceTimingPreservesIntermediateFutureCommandHashes()
        {
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var command = new SimulationCommand(
                new SimulationTick(3UL),
                0UL,
                SimulationCommandKinds.NoOp);
            var manual = new SimulationEngine(initial);
            var acceptance = manual.EnqueueCommand(command);
            var acceptedAtZeroHash = LogicalStateHasher.ComputeHashHex(manual.State);
            Assert.That(manual.AdvanceOneTick().Committed, Is.True);
            var tickOneHash = LogicalStateHasher.ComputeHashHex(manual.State);
            Assert.That(manual.AdvanceOneTick().Committed, Is.True);
            Assert.That(manual.AdvanceOneTick().Committed, Is.True);
            var finalHash = LogicalStateHasher.ComputeHashHex(manual.State);

            var replay = new ReplayLog("test-build", initial, new SimulationTick(3UL));
            replay.Append(new ReplayEvent(
                0UL,
                0UL,
                acceptance.AcceptedAfterTick,
                acceptance.Command));
            replay.AddCheckpoint(new SimulationTick(0UL), acceptedAtZeroHash);
            replay.AddCheckpoint(new SimulationTick(1UL), tickOneHash);
            replay.AddCheckpoint(new SimulationTick(3UL), finalHash);

            var result = ReplayRunner.Run(initial, replay);

            Assert.That(result.FinalHash, Is.EqualTo(finalHash));
        }

        [Test]
        public void ReplayMatchesPostLatchIngressAtIntermediateCheckpoint()
        {
            var owner = new StableId(0UL, 401UL);
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var live = new SimulationEngine(initial);
            SimulationCommandAcceptance acceptance = default;
            live.PhaseBoundaryReached += phase =>
            {
                if (phase == SimulationPhase.ItemFluidFlowAndReservations
                    && live.State.Tick.Value == 0UL)
                {
                    acceptance = live.EnqueueCommand(new SimulationCommand(
                        new SimulationTick(1UL),
                        99UL,
                        SimulationCommandKinds.SetQuantity,
                        StableId.First,
                        owner,
                        12L,
                        Array.Empty<byte>()));
                }
            };

            Assert.That(live.AdvanceOneTick().Committed, Is.True);
            var tickOneHash = LogicalStateHasher.ComputeHashHex(live.State);
            Assert.That(live.AdvanceOneTick().Committed, Is.True);
            var finalHash = LogicalStateHasher.ComputeHashHex(live.State);

            var replay = new ReplayLog("test-build", initial, new SimulationTick(2UL));
            replay.Append(new ReplayEvent(
                0UL,
                0UL,
                acceptance.AcceptedAfterTick,
                acceptance.Command));
            replay.AddCheckpoint(new SimulationTick(1UL), tickOneHash);
            replay.AddCheckpoint(new SimulationTick(2UL), finalHash);

            var result = ReplayRunner.Run(initial, replay);

            Assert.That(result.FinalHash, Is.EqualTo(finalHash));
            Assert.That(result.FinalState.GetQuantity(owner).Value, Is.EqualTo(12L));
        }

        [Test]
        public void AbortedPostLatchIngressMatchesReplayInjectionAndIsNotLost()
        {
            var initial = new SimulationState(new SimulationTick(0UL), Revision);
            var live = new SimulationEngine(initial, new[] { new AlwaysAbortSystem() });
            SimulationCommandAcceptance acceptance = default;
            live.PhaseBoundaryReached += phase =>
            {
                if (phase == SimulationPhase.ItemFluidFlowAndReservations)
                {
                    acceptance = live.EnqueueCommand(new SimulationCommand(
                        new SimulationTick(1UL),
                        5UL,
                        SimulationCommandKinds.NoOp));
                }
            };

            var aborted = live.AdvanceOneTick();

            Assert.That(aborted.Committed, Is.False);
            Assert.That(live.State.Tick.Value, Is.Zero);
            Assert.That(live.State.PendingCommandCount, Is.EqualTo(1));
            var liveHash = LogicalStateHasher.ComputeHashHex(live.State);

            var replayEquivalent = new SimulationEngine(initial, new[] { new AlwaysAbortSystem() });
            replayEquivalent.EnqueueCommand(acceptance.Command);
            Assert.That(
                LogicalStateHasher.ComputeHashHex(replayEquivalent.State),
                Is.EqualTo(liveHash));
            Assert.That(replayEquivalent.AdvanceOneTick().Committed, Is.False);
            Assert.That(
                LogicalStateHasher.ComputeHashHex(replayEquivalent.State),
                Is.EqualTo(liveHash));
        }

        private sealed class AlwaysAbortSystem : ISimulationPhaseSystem
        {
            public SimulationPhase Phase => SimulationPhase.CompletionDamageAndEventStaging;

            public int Order => 0;

            public StableId StableOrderId => new StableId(0UL, 801UL);

            public void Execute(SimulationPhaseContext context)
            {
                context.AbortTick("Deliberate ingress rollback test.");
            }
        }

    }
}

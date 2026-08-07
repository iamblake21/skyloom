using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation.Replay
{
    public static class ReplayRunner
    {
        public static ReplayResult Run(
            SimulationState initialState,
            ReplayLog replay,
            IEnumerable<ISimulationPhaseSystem> systems = null)
        {
            if (initialState == null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            ValidateMetadata(initialState, replay);

            var initialHash = LogicalStateHasher.ComputeHashHex(initialState);
            if (!string.Equals(initialHash, replay.InitialHash, StringComparison.Ordinal))
            {
                throw new SimulationInvariantException(
                    $"Replay initial hash mismatch. Expected {replay.InitialHash}, got {initialHash}.");
            }

            var eventsByAcceptanceTick = IndexEvents(replay);
            var engine = new SimulationEngine(initialState, systems);
            InjectAcceptedCommands(engine, eventsByAcceptanceTick, engine.State.Tick);
            ValidateCheckpointIfPresent(engine.State, replay);

            while (engine.State.Tick < replay.FinalTick)
            {
                var result = engine.AdvanceOneTick();
                if (!result.Committed)
                {
                    throw new SimulationInvariantException(
                        $"Replay aborted in phase {result.FailedPhase} at tick {result.ExecutingTick}: "
                        + result.FailureCause);
                }

                InjectAcceptedCommands(engine, eventsByAcceptanceTick, engine.State.Tick);
                ValidateCheckpointIfPresent(engine.State, replay);
            }

            return new ReplayResult(
                engine.State.DeepClone(),
                LogicalStateHasher.ComputeHashHex(engine.State));
        }

        private static SortedDictionary<SimulationTick, List<ReplayEvent>> IndexEvents(ReplayLog replay)
        {
            var result = new SortedDictionary<SimulationTick, List<ReplayEvent>>();
            var expectedOrdinal = 0UL;
            foreach (var replayEvent in replay.Events)
            {
                if (replayEvent.GlobalOrdinal != expectedOrdinal)
                {
                    throw new SimulationInvariantException(
                        $"Replay ordinal {expectedOrdinal} is missing or duplicated.");
                }

                if (replayEvent.Epoch != 0UL)
                {
                    throw new NotSupportedException(
                        "The minimal M0 runner accepts epoch zero only; save/load control events arrive in SAVE-001.");
                }

                if (!result.TryGetValue(replayEvent.AcceptedAfterTick, out var events))
                {
                    events = new List<ReplayEvent>();
                    result.Add(replayEvent.AcceptedAfterTick, events);
                }

                events.Add(replayEvent);
                expectedOrdinal = checked(expectedOrdinal + 1UL);
            }

            foreach (var entry in result)
            {
                entry.Value.Sort((left, right) =>
                    SimulationCommandComparer.Instance.Compare(left.Command, right.Command));
            }

            return result;
        }

        private static void InjectAcceptedCommands(
            SimulationEngine engine,
            IReadOnlyDictionary<SimulationTick, List<ReplayEvent>> eventsByAcceptanceTick,
            SimulationTick acceptedAfterTick)
        {
            if (!eventsByAcceptanceTick.TryGetValue(acceptedAfterTick, out var events))
            {
                return;
            }

            // Global ordinal is the canonical acceptance order even when target
            // ticks differ.
            events.Sort((left, right) => left.GlobalOrdinal.CompareTo(right.GlobalOrdinal));
            for (var index = 0; index < events.Count; index++)
            {
                var acceptance = engine.EnqueueCommand(events[index].Command);
                if (acceptance.AcceptedAfterTick != acceptedAfterTick
                    || acceptance.Command != events[index].Command)
                {
                    throw new SimulationInvariantException(
                        $"Replay command {events[index].GlobalOrdinal} quantized differently at ingress.");
                }
            }
        }

        private static void ValidateMetadata(SimulationState initialState, ReplayLog replay)
        {
            if (replay.InitialTick != initialState.Tick
                || replay.LogicalSchemaRevision != initialState.LogicalSchemaRevision
                || replay.RulesRevision != initialState.RulesRevision
                || replay.GeneratorRevision != initialState.GeneratorRevision
                || !string.Equals(
                    replay.CatalogRevision,
                    initialState.ContentRevision.Value,
                    StringComparison.Ordinal))
            {
                throw new SimulationInvariantException(
                    "Replay metadata does not match the supplied initial state.");
            }
        }

        private static void ValidateCheckpointIfPresent(SimulationState state, ReplayLog replay)
        {
            if (!replay.Checkpoints.TryGetValue(state.Tick, out var expectedHash))
            {
                return;
            }

            var actualHash = LogicalStateHasher.ComputeHashHex(state);
            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            {
                throw new SimulationInvariantException(
                    $"Replay hash mismatch at tick {state.Tick}. Expected {expectedHash}, got {actualHash}.");
            }
        }
    }

    public readonly struct ReplayResult
    {
        public ReplayResult(SimulationState finalState, string finalHash)
        {
            FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
            FinalHash = finalHash ?? throw new ArgumentNullException(nameof(finalHash));
        }

        public SimulationState FinalState { get; }

        public string FinalHash { get; }
    }
}

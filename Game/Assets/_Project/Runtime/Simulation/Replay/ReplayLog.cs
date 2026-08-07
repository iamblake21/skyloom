using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation.Replay
{
    [Serializable]
    public sealed class ReplayLog
    {
        private readonly List<ReplayEvent> _events = new List<ReplayEvent>();
        private readonly SortedDictionary<SimulationTick, string> _checkpoints =
            new SortedDictionary<SimulationTick, string>();
        private readonly Dictionary<EpochTickKey, ulong> _nextSequenceByTick =
            new Dictionary<EpochTickKey, ulong>();

        public ReplayLog(string buildId, SimulationState initialState, SimulationTick finalTick)
        {
            if (initialState == null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            if (finalTick < initialState.Tick)
            {
                throw new ArgumentOutOfRangeException(nameof(finalTick));
            }

            BuildId = buildId ?? string.Empty;
            LogicalSchemaRevision = initialState.LogicalSchemaRevision;
            RulesRevision = initialState.RulesRevision;
            CatalogRevision = initialState.ContentRevision.Value;
            GeneratorRevision = initialState.GeneratorRevision;
            InitialTick = initialState.Tick;
            FinalTick = finalTick;
            InitialHash = LogicalStateHasher.ComputeHashHex(initialState);
        }

        public string BuildId { get; }

        public uint LogicalSchemaRevision { get; }

        public uint RulesRevision { get; }

        public string CatalogRevision { get; }

        public uint GeneratorRevision { get; }

        public SimulationTick InitialTick { get; }

        public SimulationTick FinalTick { get; }

        public string InitialHash { get; }

        public IReadOnlyList<ReplayEvent> Events => _events.ToArray();

        public IReadOnlyDictionary<SimulationTick, string> Checkpoints =>
            new SortedDictionary<SimulationTick, string>(_checkpoints);

        public void Append(ReplayEvent replayEvent)
        {
            var expectedOrdinal = (ulong)_events.Count;
            if (replayEvent.GlobalOrdinal != expectedOrdinal)
            {
                throw new ArgumentException(
                    $"Replay ordinal must be contiguous. Expected {expectedOrdinal}, got {replayEvent.GlobalOrdinal}.",
                    nameof(replayEvent));
            }

            if (_events.Count > 0)
            {
                var previous = _events[_events.Count - 1];
                if (replayEvent.Epoch < previous.Epoch
                    || (replayEvent.Epoch == previous.Epoch
                        && replayEvent.AcceptedAfterTick < previous.AcceptedAfterTick))
                {
                    throw new ArgumentException(
                        "Replay acceptance boundaries must be chronological within global ordinal order.",
                        nameof(replayEvent));
                }
            }

            var command = replayEvent.Command;
            if (command.TargetTick <= InitialTick || command.TargetTick > FinalTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replayEvent),
                    $"Command target {command.TargetTick} is outside replay range "
                    + $"{InitialTick.Next()}..{FinalTick}.");
            }

            if (replayEvent.AcceptedAfterTick < InitialTick
                || replayEvent.AcceptedAfterTick >= command.TargetTick
                || replayEvent.AcceptedAfterTick > FinalTick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replayEvent),
                    $"Acceptance boundary {replayEvent.AcceptedAfterTick} must be within the replay "
                    + $"and earlier than destination tick {command.TargetTick}.");
            }

            var key = new EpochTickKey(replayEvent.Epoch, command.TargetTick);
            _nextSequenceByTick.TryGetValue(key, out var expectedSequence);
            if (command.Sequence != expectedSequence)
            {
                throw new ArgumentException(
                    $"Replay sequence must be contiguous inside epoch {replayEvent.Epoch}, "
                    + $"tick {command.TargetTick}. Expected {expectedSequence}, got {command.Sequence}.",
                    nameof(replayEvent));
            }

            _nextSequenceByTick[key] = checked(expectedSequence + 1UL);
            _events.Add(replayEvent);
        }

        public void AddCheckpoint(SimulationTick tick, string expectedHash)
        {
            if (tick < InitialTick || tick > FinalTick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            if (string.IsNullOrWhiteSpace(expectedHash) || expectedHash.Length != 64)
            {
                throw new ArgumentException("A replay checkpoint must contain a SHA-256 hex digest.", nameof(expectedHash));
            }

            for (var index = 0; index < expectedHash.Length; index++)
            {
                var character = expectedHash[index];
                var isHex = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    throw new ArgumentException(
                        "A replay checkpoint must contain hexadecimal SHA-256 text.",
                        nameof(expectedHash));
                }
            }

            if (_checkpoints.ContainsKey(tick))
            {
                throw new ArgumentException($"Replay checkpoint {tick} already exists.", nameof(tick));
            }

            _checkpoints.Add(tick, expectedHash.ToLowerInvariant());
        }

        private readonly struct EpochTickKey : IEquatable<EpochTickKey>
        {
            public EpochTickKey(ulong epoch, SimulationTick tick)
            {
                Epoch = epoch;
                Tick = tick;
            }

            private ulong Epoch { get; }

            private SimulationTick Tick { get; }

            public bool Equals(EpochTickKey other)
            {
                return Epoch == other.Epoch && Tick == other.Tick;
            }

            public override bool Equals(object obj)
            {
                return obj is EpochTickKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Epoch.GetHashCode() * 397) ^ Tick.GetHashCode();
                }
            }
        }
    }
}

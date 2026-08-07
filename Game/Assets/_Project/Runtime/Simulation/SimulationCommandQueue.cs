using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation
{
    /// <summary>
    /// Deterministic queue keyed by destination tick and sequence within that tick.
    /// A duplicate sequence is rejected instead of falling back to insertion order.
    /// </summary>
    [Serializable]
    public sealed class SimulationCommandQueue
    {
        private readonly SortedDictionary<SimulationTick, SortedDictionary<ulong, SimulationCommand>> _byTick;

        public SimulationCommandQueue()
        {
            _byTick = new SortedDictionary<SimulationTick, SortedDictionary<ulong, SimulationCommand>>();
        }

        private SimulationCommandQueue(
            SortedDictionary<SimulationTick, SortedDictionary<ulong, SimulationCommand>> source)
            : this()
        {
            foreach (var tickEntry in source)
            {
                var commands = new SortedDictionary<ulong, SimulationCommand>();
                foreach (var commandEntry in tickEntry.Value)
                {
                    commands.Add(commandEntry.Key, commandEntry.Value);
                }

                _byTick.Add(tickEntry.Key, commands);
            }
        }

        public int Count { get; private set; }

        public void Enqueue(SimulationCommand command)
        {
            Enqueue(command, GetNextSequenceFor(command.TargetTick));
        }

        internal void Enqueue(SimulationCommand command, ulong expectedSequence)
        {
            SimulationCommandIngress.Validate(command);

            var nextCount = checked(Count + 1);
            if (!_byTick.TryGetValue(command.TargetTick, out var commands))
            {
                commands = new SortedDictionary<ulong, SimulationCommand>();
                _byTick.Add(command.TargetTick, commands);
            }

            if (command.Sequence != expectedSequence)
            {
                throw new ArgumentException(
                    $"Command sequence gap for target tick {command.TargetTick}. "
                    + $"Expected {expectedSequence}, got {command.Sequence}.",
                    nameof(command));
            }

            if (commands.ContainsKey(command.Sequence))
            {
                throw new ArgumentException(
                    $"Duplicate command sequence {command.Sequence} for target tick {command.TargetTick}.",
                    nameof(command));
            }

            commands.Add(command.Sequence, command);
            Count = nextCount;
        }

        public ulong GetNextSequenceFor(SimulationTick targetTick)
        {
            if (!_byTick.TryGetValue(targetTick, out var commands) || commands.Count == 0)
            {
                return 0UL;
            }

            using (var enumerator = commands.Keys.GetEnumerator())
            {
                ulong last = 0UL;
                while (enumerator.MoveNext())
                {
                    last = enumerator.Current;
                }

                return checked(last + 1UL);
            }
        }

        public int GetCommandCountFor(SimulationTick targetTick)
        {
            return _byTick.TryGetValue(targetTick, out var commands) ? commands.Count : 0;
        }

        public IReadOnlyList<SimulationCommand> GetCommandsFor(SimulationTick tick)
        {
            if (!_byTick.TryGetValue(tick, out var commands))
            {
                return Array.Empty<SimulationCommand>();
            }

            var result = new SimulationCommand[commands.Count];
            var index = 0;
            foreach (var command in commands.Values)
            {
                result[index++] = command;
            }

            return result;
        }

        public void RemoveCommandsFor(SimulationTick tick)
        {
            if (!_byTick.TryGetValue(tick, out var commands))
            {
                return;
            }

            Count -= commands.Count;
            _byTick.Remove(tick);
        }

        public IReadOnlyList<SimulationCommand> ToCanonicalList()
        {
            var result = new List<SimulationCommand>(Count);
            foreach (var tickEntry in _byTick)
            {
                result.AddRange(tickEntry.Value.Values);
            }

            result.Sort(SimulationCommandComparer.Instance);
            return result;
        }

        public SimulationCommandQueue Clone()
        {
            var clone = new SimulationCommandQueue(_byTick)
            {
                Count = Count
            };
            return clone;
        }
    }
}

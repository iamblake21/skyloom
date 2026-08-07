using System;
using System.Collections.Generic;

namespace CML.Simulation
{
    /// <summary>
    /// Total, culture-independent command order. Tick and sequence are normative.
    /// The remaining fields are a deterministic tie-break for diagnostics and
    /// malformed external streams; a live queue still rejects a duplicate sequence.
    /// </summary>
    public sealed class SimulationCommandComparer : IComparer<SimulationCommand>
    {
        public static readonly SimulationCommandComparer Instance = new SimulationCommandComparer();

        private SimulationCommandComparer()
        {
        }

        public int Compare(SimulationCommand left, SimulationCommand right)
        {
            var comparison = left.TargetTick.CompareTo(right.TargetTick);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Sequence.CompareTo(right.Sequence);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.DestinationId.CompareTo(right.DestinationId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.InitiatorId.CompareTo(right.InitiatorId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = string.Compare(left.Kind, right.Kind, StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.QuantizedValue.CompareTo(right.QuantizedValue);
            if (comparison != 0)
            {
                return comparison;
            }

            var commonLength = Math.Min(left.Payload.Count, right.Payload.Count);
            for (var index = 0; index < commonLength; index++)
            {
                comparison = left.Payload[index].CompareTo(right.Payload[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Payload.Count.CompareTo(right.Payload.Count);
        }
    }
}

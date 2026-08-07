using System;

namespace CML.Simulation.Replay
{
    /// <summary>
    /// Minimal M0 replay event. Control events and later epochs can extend the
    /// schema without changing command ordering inside an epoch/tick pair.
    /// </summary>
    [Serializable]
    public readonly struct ReplayEvent
    {
        public ReplayEvent(ulong globalOrdinal, ulong epoch, SimulationCommand command)
            : this(
                globalOrdinal,
                epoch,
                command.TargetTick.Value == 0UL
                    ? new CML.Foundation.SimulationTick(0UL)
                    : new CML.Foundation.SimulationTick(command.TargetTick.Value - 1UL),
                command)
        {
        }

        public ReplayEvent(
            ulong globalOrdinal,
            ulong epoch,
            CML.Foundation.SimulationTick acceptedAfterTick,
            SimulationCommand command)
        {
            GlobalOrdinal = globalOrdinal;
            Epoch = epoch;
            AcceptedAfterTick = acceptedAfterTick;
            Command = command;
        }

        public ulong GlobalOrdinal { get; }

        public ulong Epoch { get; }

        public CML.Foundation.SimulationTick AcceptedAfterTick { get; }

        public SimulationCommand Command { get; }
    }
}

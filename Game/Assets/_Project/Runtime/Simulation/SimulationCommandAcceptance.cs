using CML.Foundation;

namespace CML.Simulation
{
    /// <summary>
    /// Canonical result of command ingress, including the committed boundary at
    /// which it became accepted and any next-tick quantization.
    /// </summary>
    public readonly struct SimulationCommandAcceptance
    {
        public SimulationCommandAcceptance(
            SimulationTick acceptedAfterTick,
            SimulationCommand command)
        {
            AcceptedAfterTick = acceptedAfterTick;
            Command = command;
        }

        public SimulationTick AcceptedAfterTick { get; }

        public SimulationCommand Command { get; }
    }
}

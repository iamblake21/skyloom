using System;

namespace CML.Simulation
{
    [Serializable]
    public sealed class SimulationInvariantException : Exception
    {
        public SimulationInvariantException(string message)
            : base(message)
        {
        }

        public SimulationInvariantException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

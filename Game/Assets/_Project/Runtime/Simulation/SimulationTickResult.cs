using CML.Foundation;

namespace CML.Simulation
{
    public readonly struct SimulationTickResult
    {
        private SimulationTickResult(
            bool committed,
            SimulationTick executingTick,
            SimulationPhase? failedPhase,
            string failureCause)
        {
            Committed = committed;
            ExecutingTick = executingTick;
            FailedPhase = failedPhase;
            FailureCause = failureCause ?? string.Empty;
        }

        public bool Committed { get; }

        public SimulationTick ExecutingTick { get; }

        public SimulationPhase? FailedPhase { get; }

        public string FailureCause { get; }

        public static SimulationTickResult Success(SimulationTick executingTick)
        {
            return new SimulationTickResult(true, executingTick, null, string.Empty);
        }

        public static SimulationTickResult Abort(
            SimulationTick executingTick,
            SimulationPhase failedPhase,
            string failureCause)
        {
            return new SimulationTickResult(false, executingTick, failedPhase, failureCause);
        }
    }
}

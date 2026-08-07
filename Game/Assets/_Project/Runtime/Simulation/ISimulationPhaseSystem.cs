using CML.Foundation;

namespace CML.Simulation
{
    /// <summary>
    /// Stateless simulation behavior. Implementations may retain immutable
    /// configuration only; every value capable of influencing a later tick belongs
    /// in SimulationState and must be read/written through SimulationPhaseContext.
    /// This is what makes whole-tick rollback and retry exact.
    /// </summary>
    public interface ISimulationPhaseSystem
    {
        SimulationPhase Phase { get; }

        int Order { get; }

        StableId StableOrderId { get; }

        void Execute(SimulationPhaseContext context);
    }
}

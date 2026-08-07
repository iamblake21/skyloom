using CML.Foundation;

namespace CML.Simulation.Airship
{
    internal sealed class AirshipCommandPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.CommandsAndConfiguration;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x4149525F434D4453UL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            AirshipReducer.ApplyPhaseOneCommands(
                context.GetAirshipMutable(),
                context.ExecutingTick,
                context.Commands);
        }
    }

    internal sealed class AirshipMovementPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.MovementAndPortalDetection;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x4149525F4D4F5645UL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            AirshipReducer.AdvancePhaseTwo(context.GetAirshipMutable());
        }
    }
}

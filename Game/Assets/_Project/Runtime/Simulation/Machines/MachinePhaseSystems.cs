using CML.Foundation;

namespace CML.Simulation.Machines
{
    internal sealed class MachineItemFlowPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.ItemFluidFlowAndReservations;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x4D43485F464C4F57UL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            MachineReducer.AdvanceItemFlow(context.GetMachineMutable(), context.Catalog);
        }
    }

    internal sealed class MachineCyclePhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.CyclesNeedsAndTimers;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x4D43485F4359434CUL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            MachineReducer.AdvanceCycles(context.GetMachineMutable(), context.Catalog);
        }
    }

    internal sealed class MachineCompletionPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.CompletionDamageAndEventStaging;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x4D43485F444F4E45UL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            MachineReducer.CompleteCycles(context.GetMachineMutable(), context.Catalog);
        }
    }
}

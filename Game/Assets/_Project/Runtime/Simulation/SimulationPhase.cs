namespace CML.Simulation
{
    /// <summary>
    /// Normative phase numbers from draft_technical_rules.md section 3.3.
    /// Values are persisted and must not be reordered.
    /// </summary>
    public enum SimulationPhase : byte
    {
        CommandsAndConfiguration = 1,
        MovementAndPortalDetection = 2,
        LocalTopologyChanges = 3,
        WirelessNetworkState = 4,
        PowerSupplyAndAllocation = 5,
        ItemFluidFlowAndReservations = 6,
        CyclesNeedsAndTimers = 7,
        CompletionDamageAndEventStaging = 8,
        ValidatedTransferCommit = 9,
        SchedulingAndPoweredJobStart = 10,
        CriticalTransactionPublication = 11,
        ObjectivesDiagnosticsAndNotifications = 12
    }
}

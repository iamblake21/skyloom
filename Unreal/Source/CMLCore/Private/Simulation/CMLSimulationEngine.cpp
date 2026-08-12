#include "Simulation/CMLSimulationEngine.h"

FCMLSimulationTickResult FCMLSimulationTickResult::Success(const FCMLSimulationTick& ExecutingTick)
{
    FCMLSimulationTickResult Result;
    Result.bCommitted = true;
    Result.ExecutingTick = ExecutingTick;
    return Result;
}

FCMLSimulationTickResult FCMLSimulationTickResult::Abort(
    const FCMLSimulationTick& ExecutingTick,
    const ECMLSimulationPhase FailedPhase,
    const FString& FailureCause)
{
    FCMLSimulationTickResult Result;
    Result.bCommitted = false;
    Result.ExecutingTick = ExecutingTick;
    Result.bHasFailedPhase = true;
    Result.FailedPhase = FailedPhase;
    Result.FailureCause = FailureCause;
    return Result;
}

const TArray<ECMLSimulationPhase>& FCMLSimulationEngine::GetCanonicalPhases()
{
    static const TArray<ECMLSimulationPhase> Phases = {
        ECMLSimulationPhase::CommandsAndConfiguration,
        ECMLSimulationPhase::MovementAndPortalDetection,
        ECMLSimulationPhase::LocalTopologyChanges,
        ECMLSimulationPhase::WirelessNetworkState,
        ECMLSimulationPhase::PowerSupplyAndAllocation,
        ECMLSimulationPhase::ItemFluidFlowAndReservations,
        ECMLSimulationPhase::CyclesNeedsAndTimers,
        ECMLSimulationPhase::CompletionDamageAndEventStaging,
        ECMLSimulationPhase::ValidatedTransferCommit,
        ECMLSimulationPhase::SchedulingAndPoweredJobStart,
        ECMLSimulationPhase::CriticalTransactionPublication,
        ECMLSimulationPhase::ObjectivesDiagnosticsAndNotifications
    };
    return Phases;
}

void FCMLSimulationEngine::RegisterSystem(TSharedRef<ICMLSimulationPhaseSystem> System)
{
    Systems.Add(System);
    // Sorting on registration keeps the run order canonical: phase, then
    // explicit order, then stable id, then type name. Registration order never
    // reaches the simulation.
    Systems.Sort([](const TSharedRef<ICMLSimulationPhaseSystem>& Left,
                    const TSharedRef<ICMLSimulationPhaseSystem>& Right)
    {
        const uint8 LeftPhase = static_cast<uint8>(Left->GetPhase());
        const uint8 RightPhase = static_cast<uint8>(Right->GetPhase());
        if (LeftPhase != RightPhase)
        {
            return LeftPhase < RightPhase;
        }
        if (Left->GetOrder() != Right->GetOrder())
        {
            return Left->GetOrder() < Right->GetOrder();
        }
        const FCMLStableId LeftId = Left->GetStableOrderId();
        const FCMLStableId RightId = Right->GetStableOrderId();
        if (LeftId != RightId)
        {
            return LeftId < RightId;
        }
        return Left->GetTypeName() < Right->GetTypeName();
    });
}

bool FCMLSimulationEngine::TryEnqueueCommand(const FCMLSimulationCommand& Command)
{
    return Commands.TryEnqueue(Command);
}

FCMLSimulationTickResult FCMLSimulationEngine::AdvanceOneTick()
{
    if (bIsAdvancing)
    {
        // Reentrancy would let a system observe a half-advanced world.
        return FCMLSimulationTickResult::Abort(
            State.Tick,
            ECMLSimulationPhase::CommandsAndConfiguration,
            TEXT("The simulation engine cannot advance reentrantly."));
    }

    FCMLSimulationTick ExecutingTick;
    if (!State.Tick.TryNext(ExecutingTick))
    {
        return FCMLSimulationTickResult::Abort(
            State.Tick,
            ECMLSimulationPhase::CommandsAndConfiguration,
            TEXT("The simulation clock is exhausted."));
    }

    bIsAdvancing = true;
    LastPhaseTrace.Reset();
    ++TickWorkingCloneCount;

    // The whole transactional property lives in this copy: phases mutate the
    // working state, and the published state is replaced only after phase 12.
    FCMLSimulationState WorkingState = State;
    WorkingState.Tick = ExecutingTick;

    for (const ECMLSimulationPhase Phase : GetCanonicalPhases())
    {
        FCMLSimulationPhaseContext Context;
        Context.WorkingState = &WorkingState;
        Context.ExecutingTick = ExecutingTick;
        Context.Phase = Phase;
        if (Phase == ECMLSimulationPhase::CommandsAndConfiguration)
        {
            Commands.GetCommandsFor(ExecutingTick, Context.DueCommands);
            WorkingState.AcceptedCommands.Append(Context.DueCommands);
        }

        for (const TSharedRef<ICMLSimulationPhaseSystem>& System : Systems)
        {
            if (System->GetPhase() != Phase)
            {
                continue;
            }
            FString FailureCause;
            if (!System->Execute(Context, FailureCause))
            {
                // The working copy is dropped untouched; nothing this tick did
                // reaches the published state.
                bIsAdvancing = false;
                return FCMLSimulationTickResult::Abort(
                    ExecutingTick,
                    Phase,
                    FString::Printf(TEXT("%s: %s"), *System->GetTypeName(), *FailureCause));
            }
        }
        LastPhaseTrace.Add(Phase);
    }

    Commands.RemoveCommandsFor(ExecutingTick);
    WorkingState.SortForCanonicalEncoding();
    State = MoveTemp(WorkingState);
    bIsAdvancing = false;
    return FCMLSimulationTickResult::Success(ExecutingTick);
}

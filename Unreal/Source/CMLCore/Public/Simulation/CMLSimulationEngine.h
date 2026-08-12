#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLSimulationCommandQueue.h"
#include "Simulation/CMLSimulationState.h"

/** Outcome of one tick, ported from CML.Simulation.SimulationTickResult. */
struct CMLCORE_API FCMLSimulationTickResult
{
    bool bCommitted = false;
    FCMLSimulationTick ExecutingTick;
    bool bHasFailedPhase = false;
    ECMLSimulationPhase FailedPhase = ECMLSimulationPhase::None;
    FString FailureCause;

    static FCMLSimulationTickResult Success(const FCMLSimulationTick& ExecutingTick);
    static FCMLSimulationTickResult Abort(
        const FCMLSimulationTick& ExecutingTick,
        ECMLSimulationPhase FailedPhase,
        const FString& FailureCause);
};

/** What a phase system may read and change during its phase. */
struct CMLCORE_API FCMLSimulationPhaseContext
{
    FCMLSimulationState* WorkingState = nullptr;
    FCMLSimulationTick ExecutingTick;
    ECMLSimulationPhase Phase = ECMLSimulationPhase::None;
    TArray<FCMLSimulationCommand> DueCommands;
};

/**
 * One system inside one phase, ported from ISimulationPhaseSystem.
 *
 * Unity aborted a tick by throwing; here a system returns false and fills
 * OutFailureCause. Either way the working copy is discarded and the published
 * state is left exactly as it was.
 */
class CMLCORE_API ICMLSimulationPhaseSystem
{
public:
    virtual ~ICMLSimulationPhaseSystem() = default;

    virtual ECMLSimulationPhase GetPhase() const = 0;

    /** Explicit ordering inside a phase; ties break on the stable id, then the name. */
    virtual int32 GetOrder() const = 0;

    /** Stable identity, so two systems with the same order still order deterministically. */
    virtual FCMLStableId GetStableOrderId() const = 0;

    virtual FString GetTypeName() const = 0;

    virtual bool Execute(FCMLSimulationPhaseContext& Context, FString& OutFailureCause) = 0;
};

/**
 * The authoritative 20 Hz simulation boundary, ported from
 * CML.Simulation.SimulationEngine.
 *
 * A tick advances a deep copy of the state and publishes it only after the
 * twelfth phase commits. Anything that fails part-way leaves the published
 * state untouched, which is what makes a tick a transaction rather than a
 * sequence of partial mutations.
 *
 * System order never depends on registration order: systems sort by phase, then
 * explicit order, then stable id, then type name.
 */
class CMLCORE_API FCMLSimulationEngine
{
public:
    /** The twelve phases, in the order every tick runs them. */
    static const TArray<ECMLSimulationPhase>& GetCanonicalPhases();

    const FCMLSimulationState& GetState() const { return State; }
    void SetState(const FCMLSimulationState& InState) { State = InState; }

    /** Systems are sorted on registration, so the run order is always canonical. */
    void RegisterSystem(TSharedRef<ICMLSimulationPhaseSystem> System);

    /** Queues a command for a future tick. Refuses gaps and duplicates. */
    bool TryEnqueueCommand(const FCMLSimulationCommand& Command);

    /** Phases completed by the last tick, in order; useful for diagnosing an abort. */
    const TArray<ECMLSimulationPhase>& GetLastPhaseTrace() const { return LastPhaseTrace; }

    /** How many working copies have been taken; a tick costs exactly one. */
    uint64 GetTickWorkingCloneCount() const { return TickWorkingCloneCount; }

    FCMLSimulationTickResult AdvanceOneTick();

private:
    FCMLSimulationState State;
    FCMLSimulationCommandQueue Commands;
    TArray<TSharedRef<ICMLSimulationPhaseSystem>> Systems;
    TArray<ECMLSimulationPhase> LastPhaseTrace;
    uint64 TickWorkingCloneCount = 0;
    bool bIsAdvancing = false;
};

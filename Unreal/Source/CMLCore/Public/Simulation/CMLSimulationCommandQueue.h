#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLSimulationRecords.h"

/**
 * Deterministic command queue, ported from CML.Simulation.SimulationCommandQueue.
 *
 * Commands are keyed by destination tick and by sequence within that tick.
 * Two rules make the ordering reproducible rather than merely stable:
 *
 *  - a sequence gap is refused, so the order can never fall back to insertion
 *    order without anyone noticing;
 *  - a duplicate sequence is refused for the same reason.
 *
 * Unity signalled both with exceptions; the port returns `bool` and leaves the
 * queue untouched on refusal, which is the same contract inside Unreal's
 * no-exceptions convention.
 */
class CMLCORE_API FCMLSimulationCommandQueue
{
public:
    int32 Num() const { return Count; }

    /** Next sequence for a tick: zero when the tick is empty, else last + 1. */
    uint64 GetNextSequenceFor(const FCMLSimulationTick& TargetTick) const;

    /** Enqueues at the next free sequence for the command's target tick. */
    bool TryEnqueue(const FCMLSimulationCommand& Command);

    /**
     * Enqueues only when the command's sequence is exactly the expected one.
     * Refuses gaps and duplicates.
     */
    bool TryEnqueueAt(const FCMLSimulationCommand& Command, uint64 ExpectedSequence);

    int32 GetCommandCountFor(const FCMLSimulationTick& TargetTick) const;

    /** Commands for one tick, in sequence order. */
    void GetCommandsFor(const FCMLSimulationTick& Tick, TArray<FCMLSimulationCommand>& OutCommands) const;

    void RemoveCommandsFor(const FCMLSimulationTick& Tick);

    /** Every queued command, ordered by tick then sequence. */
    void ToCanonicalList(TArray<FCMLSimulationCommand>& OutCommands) const;

private:
    struct FTickBucket
    {
        FCMLSimulationTick Tick;
        // Kept sorted by sequence; the queue refuses gaps, so appending in
        // order is the normal path and the sort is a safeguard, not a crutch.
        TArray<FCMLSimulationCommand> Commands;
    };

    int32 IndexOfTick(const FCMLSimulationTick& Tick) const;
    int32 FindOrAddTick(const FCMLSimulationTick& Tick);

    TArray<FTickBucket> Buckets;
    int32 Count = 0;
};

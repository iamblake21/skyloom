#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Simulation/CMLMachineState.h"

/**
 * Machine production cycles, ported from CML.Simulation.Machines.MachineReducer.
 *
 * The cycle spans two phases on purpose, and the split is what makes a blocked
 * machine safe:
 *
 *  - phase 7 starts a cycle (consuming inputs and fuel up front) and advances
 *    its progress by one tick;
 *  - phase 8 deposits the outputs of a finished cycle.
 *
 * A cycle that finishes with nowhere to put its output stays finished and
 * undeposited rather than being lost or re-run. Ingredients are spent once, at
 * the start, and the machine simply reports OutputFull until room appears.
 */
class CMLCORE_API FCMLMachineCycle
{
public:
    static constexpr int64 MillisecondsPerTick = 50;

    /** Phase 7: start eligible cycles and advance running ones by one tick. */
    static void AdvanceCycles(FCMLMachineSimulationState& State, const FCMLGameCatalog& Catalog);

    /** Phase 8: deposit finished cycles that have room for their output. */
    static void CompleteCycles(FCMLMachineSimulationState& State, const FCMLGameCatalog& Catalog);

    /** Whether a port holds every input the recipe consumes. */
    static bool HasInputs(const FCMLMachinePort& Port, const FCMLRecipeDefinition& Recipe);

    /**
     * Whether a node's input port accepts this item at all. A machine takes
     * only what its active recipe consumes: without this, a press fed from a
     * mixed crate would fill its slots with plates it can never use and
     * deadlock itself.
     */
    static bool Admits(
        const FCMLMachineNodeState& Node,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog);
};

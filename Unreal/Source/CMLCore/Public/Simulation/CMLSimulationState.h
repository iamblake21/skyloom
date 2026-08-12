#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLAccumulator.h"
#include "Simulation/CMLAirshipState.h"
#include "Simulation/CMLInventoryState.h"
#include "Simulation/CMLMachineState.h"
#include "Simulation/CMLSimulationRecords.h"
#include "CMLSimulationState.generated.h"

/**
 * The authoritative simulation state, ported from CML.Simulation.SimulationState.
 *
 * This is the canonical projection only: the sixteen fields the logical state
 * hash is computed over. The Unity class also owns the working copy, the
 * command queues and the phase machinery; those belong to the engine port, not
 * to the state that gets hashed.
 *
 * The three revision numbers are part of the hash on purpose. A replay recorded
 * under older rules must be refused rather than silently reinterpreted, and
 * that gate reads the root.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLSimulationState
{
    GENERATED_BODY()

    /** Revision 12: the values the Unity build currently writes. */
    static constexpr uint32 CurrentLogicalSchemaRevision = 12;
    static constexpr uint32 CurrentRulesRevision = 12;
    static constexpr uint32 CurrentGeneratorRevision = 1;
    static constexpr uint32 CurrentCatalogSchemaVersion = 1;

    UPROPERTY() uint32 LogicalSchemaRevision = CurrentLogicalSchemaRevision;
    UPROPERTY() uint32 RulesRevision = CurrentRulesRevision;
    UPROPERTY() uint32 CatalogSchemaVersion = CurrentCatalogSchemaVersion;

    /** Content revision string, compared and hashed ordinally. */
    UPROPERTY() FString ContentRevision;

    UPROPERTY() uint32 GeneratorRevision = CurrentGeneratorRevision;
    UPROPERTY() FCMLSimulationTick Tick;
    UPROPERTY() FCMLStableId NextEntityId = FCMLStableId::First();
    UPROPERTY() bool bIsEntityIdSpaceExhausted = false;

    // Maps Unity held in SortedDictionaries. `SortForCanonicalEncoding` must run
    // before hashing; the order is part of the hash, not an implementation detail.
    TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>> Quantities;
    TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>> Accumulators;

    UPROPERTY() TArray<FCMLSimulationCommand> AcceptedCommands;
    UPROPERTY() TArray<FCMLCreationRecord> CreationRecords;
    UPROPERTY() TArray<FCMLCommandRejection> CommandRejections;

    UPROPERTY() FCMLAirshipSimulationState Airship;
    UPROPERTY() FCMLMachineSimulationState Machines;
    UPROPERTY() FCMLInventorySimulationState Inventories;

    /** Puts every canonically-ordered collection into its canonical order. */
    void SortForCanonicalEncoding();
};

#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "CMLSimulationRecords.generated.h"

/**
 * The twelve ordered phases of one simulation tick, ported from
 * CML.Simulation.SimulationPhase. The numeric values are hashed as part of a
 * creation key, so they are pinned rather than left to the compiler.
 */
UENUM(BlueprintType)
enum class ECMLSimulationPhase : uint8
{
    None = 0,
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
};

/**
 * Why a command was refused, ported from CML.Simulation.CommandRejectionReason.
 * These values are hashed too: renumbering one changes every recorded fixture
 * that contains a rejection.
 */
UENUM(BlueprintType)
enum class ECMLCommandRejectionReason : uint8
{
    None = 0,
    InsufficientQuantity = 1,
    QuantityOverflow = 2,
    TransferSourceMissing = 3,
    TransferDestinationMissing = 4,
    TransferSameEndpoint = 5,
    TransferUnknownItem = 6,
    TransferZeroAmount = 7,
    TransferDestinationFull = 8,
    TransferNotAdmitted = 9,
    TransferMalformed = 10,
    BuildMalformed = 11,
    BuildDefinitionMissing = 12,
    BuildSourceMissing = 13,
    BuildDestinationMissing = 14,
    BuildTopologyInvalid = 15,
    SalvageTargetMissing = 16,
    SalvageDestinationFull = 17,
    SlotMoveMalformed = 18,
    SlotMoveInventoryMissing = 19,
    SlotMoveSlotOutOfRange = 20,
    SlotMoveBlocked = 21
};

/** One command addressed at a specific tick, ported from SimulationCommand. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLSimulationCommand
{
    GENERATED_BODY()

    UPROPERTY()
    FCMLSimulationTick TargetTick;

    UPROPERTY()
    uint64 Sequence = 0;

    UPROPERTY()
    FString Kind;

    UPROPERTY()
    FCMLStableId InitiatorId;

    UPROPERTY()
    FCMLStableId DestinationId;

    UPROPERTY()
    int64 QuantizedValue = 0;

    UPROPERTY()
    TArray<uint8> Payload;

    FCMLSimulationCommand() = default;

    /** A blank kind is refused, exactly as the C# constructor did. */
    static bool TryCreate(
        const FCMLSimulationTick& TargetTick,
        uint64 Sequence,
        const FString& Kind,
        FCMLSimulationCommand& OutCommand);
};

/**
 * Deterministic identity of something a tick created, ported from CreationKey.
 * Two runs of the same tick must derive the same key for the same cause, which
 * is what lets a replay reproduce entity ids exactly.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLCreationKey
{
    GENERATED_BODY()

    UPROPERTY()
    ECMLSimulationPhase Phase = ECMLSimulationPhase::None;

    UPROPERTY()
    uint32 CauseCode = 0;

    UPROPERTY()
    FCMLStableId InitiatorId;

    UPROPERTY()
    uint64 CommandSequence = 0;

    UPROPERTY()
    uint32 OutputIndex = 0;

    UPROPERTY()
    uint32 LocalOrdinal = 0;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLCreationRecord
{
    GENERATED_BODY()

    UPROPERTY()
    FCMLSimulationTick Tick;

    UPROPERTY()
    FCMLCreationKey Key;

    UPROPERTY()
    FCMLStableId EntityId;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLCommandRejection
{
    GENERATED_BODY()

    UPROPERTY()
    FCMLSimulationTick Tick;

    UPROPERTY()
    FCMLSimulationCommand Command;

    UPROPERTY()
    ECMLCommandRejectionReason Reason = ECMLCommandRejectionReason::None;
};

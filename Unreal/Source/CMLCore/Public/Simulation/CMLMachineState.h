#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "CMLMachineState.generated.h"

/**
 * The MCH subtree's state, ported from CML.Simulation.Machines.
 *
 * As everywhere else in the canonical schema, enum values are pinned because
 * they are hashed as bytes. UnrealHeaderTool needs a zero entry, so `None = 0`
 * is added below any Unity enum that started at one rather than renumbering it.
 */

UENUM(BlueprintType)
enum class ECMLMachineNodeKind : uint8
{
    None = 0,
    Buffer = 1,
    Machine = 2,
    Funnel = 3,
    BeltModule = 4
};

UENUM(BlueprintType)
enum class ECMLMachineActivity : uint8
{
    None = 0,
    Idle = 1,
    Running = 2,
    NoRecipe = 3,
    MissingInput = 4,
    OutputFull = 5,
    MissingFuel = 6
};

UENUM(BlueprintType)
enum class ECMLMachinePortKind : uint8
{
    None = 0,
    Storage = 1,
    Input = 2,
    Output = 3,
    // A port's kind is written into the canonical hash as a byte, so a fuel port
    // that had to borrow another kind would make every fuel-burning machine hash
    // differently from Unity's.
    Fuel = 4
};

UENUM(BlueprintType)
enum class ECMLBeltTravelDirection : uint8
{
    Stopped = 0,
    Forward = 1,
    Reverse = 2
};

UENUM(BlueprintType)
enum class ECMLBeltLineStatus : uint8
{
    NotApplicable = 0,
    MissingDrive = 1,
    Operational = 2,
    Overloaded = 3,
    DirectionConflict = 4
};

/** One port slot. Empty slots are encoded, never skipped. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineSlot
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId ItemId;
    UPROPERTY() FCMLNonNegativeQuantity Quantity;
};

/**
 * One port. Slot positions decide the order in which a port fills and drains,
 * so two ports holding the same items in different slots are not the same
 * future and must not share a hash.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachinePort
{
    GENERATED_BODY()

    UPROPERTY() ECMLMachinePortKind Kind = ECMLMachinePortKind::None;
    UPROPERTY() TArray<FCMLMachineSlot> Slots;
};

/** Quantised build pose: millimetres and quarter turns, never a float. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineBuildPose
{
    GENERATED_BODY()

    UPROPERTY() int64 XMillimetres = 0;
    UPROPERTY() int64 YMillimetres = 0;
    UPROPERTY() int64 ZMillimetres = 0;
    UPROPERTY() int32 YawQuarterTurns = 0;
};

/**
 * One machine node. Seventeen canonical fields.
 *
 * `bInputOutputAliased` is the C++ stand-in for something Unity expressed with
 * object identity: a buffer's input and output are literally the same port, and
 * the encoder emitted it once. C++ value semantics cannot express that with a
 * reference comparison, so the aliasing is stated explicitly — encoding the
 * port twice would put a crate's contents into the hash twice.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineNodeState
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId Id;
    UPROPERTY() ECMLMachineNodeKind Kind = ECMLMachineNodeKind::None;
    UPROPERTY() FCMLStableId DefinitionId;
    UPROPERTY() FCMLStableId ActiveRecipeId;
    UPROPERTY() int64 ProgressMilliseconds = 0;
    UPROPERTY() bool bIsCycleActive = false;
    UPROPERTY() ECMLMachineActivity Activity = ECMLMachineActivity::None;
    UPROPERTY() int64 CompletedCycles = 0;

    UPROPERTY() FCMLMachinePort Input;
    UPROPERTY() bool bInputOutputAliased = false;
    UPROPERTY() FCMLMachinePort Output;
    UPROPERTY() bool bHasFuelPort = false;
    UPROPERTY() FCMLMachinePort Fuel;

    UPROPERTY() FCMLStableId AttachedNodeId;
    UPROPERTY() bool bHasPlacementPose = false;
    UPROPERTY() FCMLMachineBuildPose PlacementPose;
    UPROPERTY() int64 TransportProgressMillimetres = 0;
    UPROPERTY() ECMLBeltTravelDirection BeltTravelDirection = ECMLBeltTravelDirection::Stopped;
    UPROPERTY() ECMLBeltLineStatus BeltLineStatus = ECMLBeltLineStatus::NotApplicable;
    UPROPERTY() int64 BeltLineUsedCapacity = 0;
    UPROPERTY() int64 BeltLineAvailableCapacity = 0;
};

/** One item riding a lane, at its own position along it. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLBeltLaneItem
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId ItemId;
    UPROPERTY() int64 PositionMillimetres = 0;
};

/**
 * One belt lane. Cargo is encoded in the lane's own order, front first, and the
 * positions are in the hash: two lanes holding the same items at different
 * positions are different futures, because one of them delivers sooner.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLBeltLaneState
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId Id;
    UPROPERTY() FCMLStableId SourceNodeId;
    UPROPERTY() FCMLStableId DestinationNodeId;
    UPROPERTY() FCMLStableId ItemFilter;
    UPROPERTY() int64 LengthMillimetres = 0;
    UPROPERTY() int64 SpeedMillimetresPerTick = 0;
    UPROPERTY() int64 SpacingMillimetres = 0;
    UPROPERTY() int64 DeliveredUnits = 0;
    UPROPERTY() TArray<FCMLBeltLaneItem> Items;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineSimulationState
{
    GENERATED_BODY()

    UPROPERTY() TArray<FCMLMachineNodeState> Nodes;
    UPROPERTY() TArray<FCMLBeltLaneState> Lanes;

    void Sort();
    bool HasUniqueIds() const;
};

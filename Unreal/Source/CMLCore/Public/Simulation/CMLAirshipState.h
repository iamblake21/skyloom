#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "CMLAirshipState.generated.h"

/**
 * The AIR subtree's state, ported from CML.Simulation.Airship.
 *
 * Everything here is quantised integer state: positions in millimetres, yaw in
 * turn units, pilot input in permille. Nothing is a float, which is what lets
 * two machines integrate the same flight and land on the same tick.
 *
 * As with every other canonical enum, the numeric values are pinned because
 * they are hashed as bytes.
 */

UENUM(BlueprintType)
enum class ECMLAirshipFlightMode : uint8
{
    Anchored = 0,
    Flying = 1,
    Stabilizing = 2
};

UENUM(BlueprintType)
enum class ECMLAirshipPlayerFrameKind : uint8
{
    World = 0,
    Airship = 1
};

UENUM(BlueprintType)
enum class ECMLAirshipLandingRequestResult : uint8
{
    None = 0,
    Accepted = 1,
    AlreadyAnchored = 2,
    AlreadyStabilizing = 3,
    TooFast = 4,
    UnknownSurface = 5,
    SurfaceOutOfReach = 6
};

UENUM(BlueprintType)
enum class ECMLAirshipRepairStatus : uint8
{
    // The Unity enum starts at Damaged = 1. UnrealHeaderTool requires a zero
    // entry for default initialisation, so None is added *below* the ported
    // values rather than renumbering them - the simulation never produces it,
    // and every real status keeps the byte value the fixtures were hashed with.
    None = 0,
    Damaged = 1,
    Repairing = 2,
    Repaired = 3
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipVector
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 X = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 Y = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 Z = 0;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipPose
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    FCMLAirshipVector Position;

    /** Yaw in turn units; unsigned, so it wraps rather than going negative. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int32 YawTurn = 0;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipPilotInput
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 ThrottleChangePermille = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 LiftPermille = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 YawDeltaPermille = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Airship")
    int64 PitchDeltaPermille = 0;
};

/** One airship. Twenty-two canonical fields, in schema order. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipEntityState
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId Id;
    UPROPERTY() FCMLAirshipPose Pose;
    UPROPERTY() ECMLAirshipFlightMode Mode = ECMLAirshipFlightMode::Anchored;
    UPROPERTY() int64 LandingTicksRemaining = 0;
    UPROPERTY() int64 ForwardSpeedMillimetresPerSecond = 0;
    UPROPERTY() int64 StrafeSpeedMillimetresPerSecond = 0;
    UPROPERTY() int64 VerticalSpeedMillimetresPerSecond = 0;
    UPROPERTY() int64 YawRateTurnUnitsPerSecond = 0;

    // The integration remainders are what make movement exact: the leftover of
    // each fixed-step integration is carried, never rounded away.
    UPROPERTY() int64 ForwardIntegrationRemainder = 0;
    UPROPERTY() int64 StrafeIntegrationRemainder = 0;
    UPROPERTY() int64 VerticalIntegrationRemainder = 0;
    UPROPERTY() int64 YawIntegrationRemainder = 0;

    UPROPERTY() FCMLAirshipPilotInput HeldInput;
    UPROPERTY() FCMLStableId PilotId;
    UPROPERTY() FCMLStableId AcceptedLandingSurfaceId;
    UPROPERTY() FCMLStableId DockedLandingSurfaceId;
    UPROPERTY() ECMLAirshipLandingRequestResult LastLandingRequestResult = ECMLAirshipLandingRequestResult::None;
    UPROPERTY() int64 PitchTurnUnits = 0;
    UPROPERTY() ECMLAirshipRepairStatus RepairStatus = ECMLAirshipRepairStatus::Damaged;
    UPROPERTY() int64 InstalledIronPlates = 0;
    UPROPERTY() int64 InstalledInsulatedCables = 0;
    UPROPERTY() int64 RepairTicksRemaining = 0;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipPlayerState
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId Id;
    UPROPERTY() ECMLAirshipPlayerFrameKind FrameKind = ECMLAirshipPlayerFrameKind::World;
    UPROPERTY() FCMLStableId FrameAirshipId;
    UPROPERTY() FCMLAirshipPose QuantizedPose;
    UPROPERTY() bool bIsPiloting = false;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipObstacle
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId Id;
    UPROPERTY() FCMLAirshipVector Minimum;
    UPROPERTY() FCMLAirshipVector Maximum;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipLandingSurface
{
    GENERATED_BODY()

    UPROPERTY() FCMLStableId Id;
    UPROPERTY() FCMLAirshipVector Center;
    UPROPERTY() int32 YawTurn = 0;
    UPROPERTY() int64 HalfWidthMillimetres = 0;
    UPROPERTY() int64 HalfDepthMillimetres = 0;
    UPROPERTY() FCMLStableId SupportingObstacleId;
};

/**
 * The AIR subtree. Unity held each collection in a SortedDictionary keyed by
 * id, so `Sort` must run before serialising.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAirshipSimulationState
{
    GENERATED_BODY()

    UPROPERTY() TArray<FCMLAirshipEntityState> Airships;
    UPROPERTY() TArray<FCMLAirshipPlayerState> Players;
    UPROPERTY() TArray<FCMLAirshipObstacle> Obstacles;
    UPROPERTY() TArray<FCMLAirshipLandingSurface> LandingSurfaces;

    void Sort();
    bool HasUniqueIds() const;
};

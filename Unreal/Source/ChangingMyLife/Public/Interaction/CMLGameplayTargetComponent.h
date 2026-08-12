#pragma once

#include "CoreMinimal.h"
#include "Components/ActorComponent.h"
#include "Interaction/CMLWorldInteraction.h"
#include "Simulation/CMLHarvestRules.h"
#include "Simulation/CMLSimulationSubsystem.h"

#include "CMLGameplayTargetComponent.generated.h"

/** Runtime behaviour attached to migrated visual actors. */
UENUM(BlueprintType)
enum class ECMLGameplayTargetKind : uint8
{
    None = 0,
    WildFiberTuft,
    FallenSticks,
    LoosePebble,
    EnvironmentalStone,
    IronOreRock,
    IronDepositSurface,
    CopperOreRock,
    CopperDepositSurface,
    TinOreRock,
    TinDepositSurface,
    FellableTree,
    Workbench,
    WoodenCrate,
    CrudeFurnace,
    MechanicalPress,
    MechanicalDrill,
    FactoryFunnel,
    FactoryBelt,
    AirshipRepair,
    AirshipPilotStation,
    AirshipDoor
};

/**
 * Gives an imported Blueprint a gameplay identity without replacing its mesh,
 * materials or transform. The component owns no inventory state: it submits a
 * command and reacts only after the authoritative tick accepts it.
 */
UCLASS(ClassGroup=(CML), meta=(BlueprintSpawnableComponent))
class CHANGINGMYLIFE_API UCMLGameplayTargetComponent final
    : public UActorComponent
    , public ICMLWorldInteractionTarget
{
    GENERATED_BODY()

public:
    UCMLGameplayTargetComponent();

    virtual void BeginPlay() override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

    void Configure(
        ECMLGameplayTargetKind InKind,
        const FCMLStableId& InSourceId,
        int32 InYield = 1);

    ECMLGameplayTargetKind GetTargetKind() const { return TargetKind; }
    const FCMLStableId& GetSourceId() const { return SourceId; }

    /** The exact authored/runtime primitive this interaction belongs to. */
    void ConfigureInteractionAnchor(class UPrimitiveComponent* InAnchor);
    class UPrimitiveComponent* GetInteractionAnchor() const;
    bool MatchesInteractionComponent(const class UPrimitiveComponent* Component) const;
    FBox GetInteractionBounds() const;

    /** Optional local-space hinge used by doors and crate lids. */
    void ConfigureHingedPart(
        class USceneComponent* InPart,
        const FRotator& InClosedRelativeRotation,
        const FRotator& InOpenRelativeRotation,
        bool bStartsOpen = false,
        float InDurationSeconds = 0.45f);
    void SetHingedOpen(bool bOpen);
    void ToggleHingedOpen();
    bool IsHingedOpen() const { return bHingeWantsOpen; }

    virtual bool IsInteractionAvailable_Implementation() const override;
    virtual FText GetInteractionPrompt_Implementation() const override;
    virtual bool TryInteract_Implementation() override;
    bool TryPrimaryAction(int32 EquippedSlotIndex);

    /** Unity-parity presentation fired at the immutable swing impact point. */
    void PlayImpactPresentation(
        const FVector& ImpactPoint,
        const FVector& ImpactNormal,
        const FVector& ViewOrigin);

    virtual void TickComponent(
        float DeltaTime,
        ELevelTick TickType,
        FActorComponentTickFunction* ThisTickFunction) override;

private:
    void HandleCommandResolved(
        const FCMLSimulationCommand& Command,
        bool bSucceeded,
        bool bWorldCommitted);
    void CommitWorldRemoval();
    void CommitTreeOpening();
    void ApplyTreeNotch();
    void StartImpactShake(
        const FVector& ImpactPoint,
        const FVector& ViewOrigin,
        bool bWood);
    void StopImpactShake();
    void BeginTreeFall();
    void ShowCollectionFeed() const;
    ECMLHandGatherTarget AsHandGatherTarget() const;
    ECMLMiningTarget AsMiningTarget() const;

    UPROPERTY(EditAnywhere, Category="CML|Gameplay")
    ECMLGameplayTargetKind TargetKind = ECMLGameplayTargetKind::None;

    UPROPERTY(EditAnywhere, Category="CML|Gameplay", meta=(ClampMin="1"))
    int32 Yield = 1;

    FCMLStableId SourceId;
    FCMLRuntimeCommandHandle PendingCommand;
    bool bCommitted = false;

    TWeakObjectPtr<class UPrimitiveComponent> InteractionAnchor;
    TWeakObjectPtr<class USceneComponent> HingedPart;
    FQuat HingeClosedRotation = FQuat::Identity;
    FQuat HingeOpenRotation = FQuat::Identity;
    float HingeAlpha = 0.0f;
    float HingeDurationSeconds = 0.45f;
    bool bHingeWantsOpen = false;
    float AirshipSmokeAccumulator = 0.0f;
    int32 TreeHitStage = 0;
    FVector LastImpactPoint = FVector::ZeroVector;
    FVector LastImpactNormal = FVector::UpVector;
    FVector LastViewOrigin = FVector::ZeroVector;

    /**
     * One stable scar frame, stored in the authored trunk's local space.
     * Total tree damage and local scar depth are deliberately separate: a
     * blow inside an existing scar deepens it, while a blow elsewhere starts
     * another scar. Rebuilding always starts from the authored mesh and
     * reapplies every entry, matching Unity's TreeChopRuntimeMeshOwner.
     */
    struct FTreeOpeningState
    {
        FVector CentreLocal = FVector::ZeroVector;
        FVector NormalLocal = FVector::ForwardVector;
        FVector RightLocal = FVector::RightVector;
        FVector UpLocal = FVector::UpVector;
        float SectionWidthMetres = 0.0f;
        int32 Stage = 1;
    };
    TArray<FTreeOpeningState> TreeOpenings;

    struct FShakenPrimitiveState
    {
        TWeakObjectPtr<class UPrimitiveComponent> Primitive;
        FTransform RelativeTransform = FTransform::Identity;
    };
    TArray<FShakenPrimitiveState> ShakenPrimitives;
    float ImpactShakeElapsed = BIG_NUMBER;
    float ImpactShakeDuration = 0.0f;
    float ImpactShakeTravel = 0.0f;
    float ImpactShakeRotation = 0.0f;
    float ImpactShakeFrequency = 0.0f;
    float ImpactShakeDirection = 1.0f;
    FVector ImpactShakeWorldDirection = FVector::ForwardVector;
    FVector ImpactShakeWorldSide = FVector::RightVector;

    float TreeFallElapsed = 0.0f;
    FVector TreeFallDirection = FVector::ForwardVector;
    FVector TreeFallAxis = FVector::RightVector;
    FVector TreeFallPivot = FVector::ZeroVector;
    FVector TreeBodyInitialCenter = FVector::ZeroVector;
    FQuat TreeBodyInitialRotation = FQuat::Identity;
    float TreeReleaseAngleDegrees = 8.0f;
    float TreeFallAngleDegrees = 0.0f;
    float TreeAngularSpeed = 0.0f;
    float TreeLandingAngleDegrees = 88.0f;
    float TreePhaseElapsed = 0.0f;
    float TreeTrunkHalfHeight = 250.0f;
    FVector TreeCrownInitialPoint = FVector::ZeroVector;
    uint8 TreeFallPhase = 0;
    float TreeQuietElapsed = 0.0f;
    float TreeSettledElapsed = 0.0f;
    bool bTreePhysicsReleased = false;
    bool bTreeSettled = false;

    UPROPERTY(Transient)
    TObjectPtr<class UProceduralMeshComponent> TreeRuntimeTrunk;

    TWeakObjectPtr<class UStaticMeshComponent> TreeSourceTrunk;

    UPROPERTY(Transient)
    TObjectPtr<class AActor> FallingTreeHost;

    UPROPERTY(Transient)
    TObjectPtr<class UBoxComponent> FallingTreeBody;
};

#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "Simulation/CMLMachineState.h"
#include "GameFramework/Character.h"
#include "CMLPlayerCharacter.generated.h"

class UCameraComponent;
class UChildActorComponent;
class USceneComponent;
class UTextRenderComponent;
class UPrimitiveComponent;

UCLASS()
class CHANGINGMYLIFE_API ACMLPlayerCharacter final : public ACharacter
{
    GENERATED_BODY()

public:
    ACMLPlayerCharacter();
    virtual void SetupPlayerInputComponent(UInputComponent* PlayerInputComponent) override;
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;
    virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

private:
    void MoveForward(float Value);
    void MoveRight(float Value);
    void AirshipVertical(float Value);
    void Turn(float Value);
    void LookUp(float Value);
    void JumpPressed();
    void JumpReleased();
    void SprintPressed();
    void SprintReleased();
    void ToggleInventory();
    void Interact();
    void PrimaryAction();
    void SecondaryAction();
    void RotateBuildPreview();
    void TogglePersonalCrafting();
    void CraftSelectedAction();
    bool TryCraftSelected();
    bool TryQuickTransferSelected();
    bool TryRepairSelected();
    bool TryCommitBuildPreview();
    void UpdateBuildPreview();
    void CancelBuildPreview();
    void DestroyBuildPreview();
    bool EnsureBuildPreview(const FCMLStableId& ItemId);
    bool HasBuildPhysicalOverlap(
        const FCMLStableId& AttachmentTargetId,
        FString& OutBlockerName) const;
    FCMLStableId ResolveDrillRecipeAt(
        const FVector& WorldLocation, FString& OutFailure) const;
    void ScrollHotbar(float Value);
    void SelectHotbar0();
    void SelectHotbar1();
    void SelectHotbar2();
    void SelectHotbar3();
    void SelectHotbar4();
    void SelectHotbar5();
    void SelectHotbar6();
    void SelectHotbar7();
    void SelectHotbar(int32 Index);
    void UpdateInteractionTarget();
    UObject* ResolveInteractionTarget(
        AActor* Actor, const UPrimitiveComponent* HitComponent = nullptr) const;
    void SetInteractionTarget(UObject* Target, AActor* Actor);
    void ClearInteractionTarget();
    void ShowWorldInteractionPrompt(const FText& Prompt, const FVector& WorldLocation);
    void HideWorldInteractionPrompt();
    void UpdateWorldInteractionPrompt();
    void SetActorHighlighted(AActor* Actor, bool bHighlighted) const;
    void UpdateHeldEquipment();
    void AssemblePickaxeView();
    void UpdateFirstPersonPresentation(float DeltaSeconds);
    void UpdateEquipmentSwing(float DeltaSeconds);
    void UpdatePiloting();
    AActor* FindAirshipActor(const FCMLStableId& AirshipId) const;

    UPROPERTY(VisibleAnywhere, Category="CML|View")
    TObjectPtr<UCameraComponent> FirstPersonCamera;

    UPROPERTY(VisibleAnywhere, Category="CML|Equipment")
    TObjectPtr<USceneComponent> EquipmentMotionRoot;

    UPROPERTY(VisibleAnywhere, Category="CML|Equipment")
    TObjectPtr<USceneComponent> PickaxeSwingRoot;

    UPROPERTY(VisibleAnywhere, Category="CML|Equipment")
    TObjectPtr<UChildActorComponent> PickaxeView;

    /** Unity's single shared TextMesh prompt, genuinely placed in the world. */
    UPROPERTY(VisibleAnywhere, Category="CML|Interaction")
    TObjectPtr<USceneComponent> InteractionPromptRoot;

    UPROPERTY(VisibleAnywhere, Category="CML|Interaction")
    TObjectPtr<UTextRenderComponent> InteractionPromptShadow;

    UPROPERTY(VisibleAnywhere, Category="CML|Interaction")
    TObjectPtr<UTextRenderComponent> InteractionPromptText;

    TWeakObjectPtr<UObject> CurrentInteractionTarget;
    TWeakObjectPtr<AActor> CurrentInteractionActor;

    UPROPERTY(EditDefaultsOnly, Category="CML|Interaction", meta=(ClampMin="10.0"))
    float InteractionDistance = 325.0f;

    UPROPERTY(EditDefaultsOnly, Category="CML|Interaction", meta=(ClampMin="0.0"))
    // Ground pickups sit well below the eye at normal interaction range. A
    // wider cone lets their Unity-style world prompt appear before the player
    // has to stare almost vertically at the grass.
    float ProximityAimDegrees = 45.0f;

    TWeakObjectPtr<class UCMLGameplayTargetComponent> SwingTarget;
    FVector SwingImpactPoint = FVector::ZeroVector;
    FVector SwingImpactNormal = FVector::UpVector;
    float SwingElapsed = 0.0f;
    float SwingTargetDistance = 0.0f;
    float PreviousSwingProgress = 0.0f;
    bool bSwinging = false;
    bool bSwingHasTarget = false;
    bool bSwingImpactSubmitted = false;
    bool bPickaxeEquipped = false;

    UPROPERTY(Transient)
    TObjectPtr<AActor> BuildPreviewActor;

    FCMLStableId BuildPreviewItemId;
    FCMLStableId LastSelectedBuildItemId;
    FCMLStableId CancelledBuildItemId;
    FCMLStableId BuildPreviewExtractionRecipeId;
    FCMLStableId BuildPreviewAttachmentTargetId;
    FCMLMachineBuildPose BuildPreviewPose;
    FVector BuildPreviewVisualLocation = FVector::ZeroVector;
    FString BuildPreviewStatus;
    int64 BuildPreviewHeldQuantity = 0;
    int32 BuildPreviewYaw = 0;
    bool bBuildPreviewActive = false;
    bool bBuildPreviewValid = false;
    bool bBuildYawExplicitlyRotated = false;
    bool bBuildPreviewMaterialValid = false;
    bool bSprinting = false;
    bool bWasFalling = false;
    float BobPhase = 0.0f;
    float LocomotionWeight = 0.0f;
    float SprintWeight = 0.0f;
    FVector EquipmentJumpPosition = FVector::ZeroVector;
    FRotator EquipmentJumpRotation = FRotator::ZeroRotator;
    FQuat PreviousCameraRotation = FQuat::Identity;
    FVector LookPosition = FVector::ZeroVector;
    FRotator LookRotation = FRotator::ZeroRotator;
    float LandingElapsed = BIG_NUMBER;
    float LandingStrength = 1.0f;
    float TakeoffElapsed = BIG_NUMBER;
    float PreviousVerticalVelocity = 0.0f;
    bool bPiloting = false;
    FCMLStableId PilotedAirshipId;
    TWeakObjectPtr<AActor> PilotedAirshipActor;
    float PilotThrottleInput = 0.0f;
    float PilotLiftInput = 0.0f;
    float PilotYawInput = 0.0f;
    float PilotPitchInput = 0.0f;
    uint64 LastPilotInputTick = MAX_uint64;
};

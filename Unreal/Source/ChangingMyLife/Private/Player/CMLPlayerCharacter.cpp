#include "Player/CMLPlayerCharacter.h"

#include "Building/CMLBuildableVisuals.h"
#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/ChildActorComponent.h"
#include "Components/PrimitiveComponent.h"
#include "Engine/World.h"
#include "Engine/OverlapResult.h"
#include "Engine/LevelScriptActor.h"
#include "EngineUtils.h"
#include "Components/SceneComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/TextRenderComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "Interaction/CMLWorldInteraction.h"
#include "Interaction/CMLGameplayTargetComponent.h"
#include "Content/CMLContentIds.h"
#include "Simulation/CMLSimulationSubsystem.h"
#include "Simulation/CMLBuildPlacementResolver.h"
#include "Simulation/CMLMachineBuildRule.h"
#include "Presentation/CMLEquipmentSwing.h"
#include "UI/CMLHUD.h"
#include "UObject/ConstructorHelpers.h"

ACMLPlayerCharacter::ACMLPlayerCharacter()
{
    PrimaryActorTick.bCanEverTick = true;
    GetCapsuleComponent()->InitCapsuleSize(42.0f, 88.0f);
    // Unity motor: 4 m/s walk, 1.6x sprint.
    GetCharacterMovement()->MaxWalkSpeed = 400.0f;
    GetCharacterMovement()->JumpZVelocity = 420.0f;
    GetCharacterMovement()->AirControl = 0.25f;

    FirstPersonCamera = CreateDefaultSubobject<UCameraComponent>(TEXT("FirstPersonCamera"));
    FirstPersonCamera->SetupAttachment(GetCapsuleComponent());
    FirstPersonCamera->SetRelativeLocation(FVector(-10.0f, 0.0f, 64.0f));
    FirstPersonCamera->bUsePawnControlRotation = true;

    EquipmentMotionRoot = CreateDefaultSubobject<USceneComponent>(TEXT("EquipmentMotionRoot"));
    EquipmentMotionRoot->SetupAttachment(FirstPersonCamera);

    PickaxeSwingRoot = CreateDefaultSubobject<USceneComponent>(TEXT("PickaxeSwingRoot"));
    PickaxeSwingRoot->SetupAttachment(EquipmentMotionRoot);
    // Unity pivots the swing about REF_GripPrimary and then offsets the model
    // back by its authored grip point (0,-0.14,0). That point becomes +14 cm Z
    // in Unreal's local axes.
    const FRotator PickaxeRestRotation(-7.0f, -9.0f, 11.0f);
    const FVector PickaxeRestPosition(72.0f, 26.0f, -44.0f);
    const FVector ScaledGripInUnreal(0.0f, 0.0f, -10.36f);
    PickaxeSwingRoot->SetRelativeLocation(
        PickaxeRestPosition + PickaxeRestRotation.RotateVector(ScaledGripInUnreal));
    PickaxeSwingRoot->SetRelativeRotation(PickaxeRestRotation);
    PickaxeSwingRoot->SetRelativeScale3D(FVector(0.74f));

    PickaxeView = CreateDefaultSubobject<UChildActorComponent>(TEXT("PickaxeView"));
    PickaxeView->SetupAttachment(PickaxeSwingRoot);
    PickaxeView->SetRelativeLocation(FVector(0.0f, 0.0f, 14.0f));
    // FBX imports the authored Unity axes as (X, -Z, Y), while the camera
    // viewmodel contract is (Z forward, X right, Y up). A +90 yaw is the exact
    // remaining basis conversion: REF_ImpactTip then points down +camera X
    // instead of laying the whole pick head sideways across the screen.
    PickaxeView->SetRelativeRotation(FRotator(0.0f, 90.0f, 0.0f));
    PickaxeView->SetRelativeScale3D(FVector::OneVector);
    PickaxeView->SetHiddenInGame(true);
    static ConstructorHelpers::FClassFinder<AActor> PickaxeClass(
        TEXT("/Game/Migrated/Project/Resources/Equipment/BP_PF_PickaxeCrudeView"));
    if (PickaxeClass.Succeeded())
    {
        PickaxeView->SetChildActorClass(PickaxeClass.Class);
    }

    InteractionPromptRoot = CreateDefaultSubobject<USceneComponent>(TEXT("InteractionPromptRoot"));
    InteractionPromptRoot->SetupAttachment(GetCapsuleComponent());
    InteractionPromptRoot->SetVisibility(false, true);

    InteractionPromptShadow = CreateDefaultSubobject<UTextRenderComponent>(TEXT("InteractionPromptShadow"));
    InteractionPromptShadow->SetupAttachment(InteractionPromptRoot);
    InteractionPromptShadow->SetHorizontalAlignment(EHTA_Center);
    InteractionPromptShadow->SetVerticalAlignment(EVRTA_TextCenter);
    InteractionPromptShadow->SetWorldSize(8.5f);
    InteractionPromptShadow->SetTextRenderColor(FColor(5, 6, 5, 184));
    InteractionPromptShadow->SetRelativeLocation(FVector(-0.08f, 0.27f, -0.27f));
    InteractionPromptShadow->SetCastShadow(false);

    InteractionPromptText = CreateDefaultSubobject<UTextRenderComponent>(TEXT("InteractionPromptText"));
    InteractionPromptText->SetupAttachment(InteractionPromptRoot);
    InteractionPromptText->SetHorizontalAlignment(EHTA_Center);
    InteractionPromptText->SetVerticalAlignment(EVRTA_TextCenter);
    InteractionPromptText->SetWorldSize(8.5f);
    InteractionPromptText->SetTextRenderColor(FColor(247, 250, 240, 250));
    InteractionPromptText->SetCastShadow(false);
}

void ACMLPlayerCharacter::BeginPlay()
{
    Super::BeginPlay();
    AssemblePickaxeView();
    if (AActor* ViewActor = PickaxeView != nullptr ? PickaxeView->GetChildActor() : nullptr)
    {
        ViewActor->SetActorEnableCollision(false);
        TArray<UPrimitiveComponent*> Primitives;
        ViewActor->GetComponents(Primitives);
        for (UPrimitiveComponent* Primitive : Primitives)
        {
            if (Primitive != nullptr)
            {
                Primitive->SetCollisionEnabled(ECollisionEnabled::NoCollision);
                Primitive->SetCastShadow(false);
            }
        }
    }
    PreviousCameraRotation = FirstPersonCamera != nullptr
        ? FirstPersonCamera->GetComponentQuat() : FQuat::Identity;
    bWasFalling = GetCharacterMovement()->IsFalling();
    UpdateHeldEquipment();
}

void ACMLPlayerCharacter::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
    Super::SetupPlayerInputComponent(PlayerInputComponent);
    check(PlayerInputComponent);
    PlayerInputComponent->BindAxis(TEXT("MoveForward"), this, &ACMLPlayerCharacter::MoveForward);
    PlayerInputComponent->BindAxis(TEXT("MoveRight"), this, &ACMLPlayerCharacter::MoveRight);
    PlayerInputComponent->BindAxis(TEXT("Turn"), this, &ACMLPlayerCharacter::Turn);
    PlayerInputComponent->BindAxis(TEXT("LookUp"), this, &ACMLPlayerCharacter::LookUp);
    PlayerInputComponent->BindAction(TEXT("Jump"), IE_Pressed, this, &ACMLPlayerCharacter::JumpPressed);
    PlayerInputComponent->BindAction(TEXT("Jump"), IE_Released, this, &ACMLPlayerCharacter::JumpReleased);
    PlayerInputComponent->BindAction(TEXT("Sprint"), IE_Pressed, this, &ACMLPlayerCharacter::SprintPressed);
    PlayerInputComponent->BindAction(TEXT("Sprint"), IE_Released, this, &ACMLPlayerCharacter::SprintReleased);
    PlayerInputComponent->BindAction(TEXT("Inventory"), IE_Pressed,
        this, &ACMLPlayerCharacter::ToggleInventory);
    PlayerInputComponent->BindAction(TEXT("Interact"), IE_Pressed,
        this, &ACMLPlayerCharacter::Interact);
    PlayerInputComponent->BindAction(TEXT("PrimaryAction"), IE_Pressed,
        this, &ACMLPlayerCharacter::PrimaryAction);
    PlayerInputComponent->BindAction(TEXT("SecondaryAction"), IE_Pressed,
        this, &ACMLPlayerCharacter::SecondaryAction);
    PlayerInputComponent->BindAction(TEXT("BuildRotate"), IE_Pressed,
        this, &ACMLPlayerCharacter::RotateBuildPreview);
    PlayerInputComponent->BindAction(TEXT("Crafting"), IE_Pressed,
        this, &ACMLPlayerCharacter::TogglePersonalCrafting);
    PlayerInputComponent->BindAction(TEXT("CraftSelected"), IE_Pressed,
        this, &ACMLPlayerCharacter::CraftSelectedAction);
    PlayerInputComponent->BindAction(TEXT("Hotbar1"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar0);
    PlayerInputComponent->BindAction(TEXT("Hotbar2"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar1);
    PlayerInputComponent->BindAction(TEXT("Hotbar3"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar2);
    PlayerInputComponent->BindAction(TEXT("Hotbar4"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar3);
    PlayerInputComponent->BindAction(TEXT("Hotbar5"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar4);
    PlayerInputComponent->BindAction(TEXT("Hotbar6"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar5);
    PlayerInputComponent->BindAction(TEXT("Hotbar7"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar6);
    PlayerInputComponent->BindAction(TEXT("Hotbar8"), IE_Pressed, this, &ACMLPlayerCharacter::SelectHotbar7);
    PlayerInputComponent->BindAxis(TEXT("HotbarScroll"), this, &ACMLPlayerCharacter::ScrollHotbar);
    PlayerInputComponent->BindAxis(TEXT("AirshipVertical"), this, &ACMLPlayerCharacter::AirshipVertical);
}

void ACMLPlayerCharacter::Tick(const float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);
    UpdatePiloting();
    UpdateHeldEquipment();
    UpdateBuildPreview();
    UpdateFirstPersonPresentation(DeltaSeconds);
    UpdateEquipmentSwing(DeltaSeconds);
    UpdateInteractionTarget();
    UpdateWorldInteractionPrompt();
}

void ACMLPlayerCharacter::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
    DestroyBuildPreview();
    ClearInteractionTarget();
    Super::EndPlay(EndPlayReason);
}

void ACMLPlayerCharacter::MoveForward(const float Value)
{
    if (bPiloting)
    {
        PilotThrottleInput = FMath::Clamp(Value, -1.0f, 1.0f);
        return;
    }
    if (!FMath::IsNearlyZero(Value))
    {
        AddMovementInput(GetActorForwardVector(), Value);
    }
}

void ACMLPlayerCharacter::MoveRight(const float Value)
{
    if (bPiloting)
    {
        return;
    }
    if (!FMath::IsNearlyZero(Value))
    {
        AddMovementInput(GetActorRightVector(), Value);
    }
}

void ACMLPlayerCharacter::AirshipVertical(const float Value)
{
    PilotLiftInput = bPiloting ? FMath::Clamp(Value, -1.0f, 1.0f) : 0.0f;
}

void ACMLPlayerCharacter::Turn(const float Value)
{
    if (bPiloting)
    {
        PilotYawInput = FMath::Clamp(Value * 80.0f, -1000.0f, 1000.0f);
        return;
    }
    AddControllerYawInput(Value);
}

void ACMLPlayerCharacter::LookUp(const float Value)
{
    if (bPiloting)
    {
        PilotPitchInput = FMath::Clamp(Value * 80.0f, -1000.0f, 1000.0f);
        return;
    }
    AddControllerPitchInput(Value);
}

void ACMLPlayerCharacter::JumpPressed()
{
    if (!bPiloting)
    {
        TakeoffElapsed = 0.0f;
        Jump();
    }
}

void ACMLPlayerCharacter::SprintPressed()
{
    if (!bPiloting)
    {
        bSprinting = true;
        GetCharacterMovement()->MaxWalkSpeed = 640.0f;
    }
}

void ACMLPlayerCharacter::SprintReleased()
{
    bSprinting = false;
    if (!bPiloting)
    {
        GetCharacterMovement()->MaxWalkSpeed = 400.0f;
    }
}

void ACMLPlayerCharacter::JumpReleased()
{
    if (!bPiloting)
    {
        StopJumping();
    }
}

void ACMLPlayerCharacter::ToggleInventory()
{
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    if (PlayerController == nullptr)
    {
        return;
    }
    if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
    {
        HUD->ToggleInventory();
    }
}

void ACMLPlayerCharacter::Interact()
{
    if (bPiloting)
    {
        if (UWorld* World = GetWorld())
        {
            if (UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>())
            {
                FCMLRuntimeCommandHandle Handle;
                Simulation->RequestAirshipPilotEnd(PilotedAirshipId, Handle);
            }
        }
        return;
    }
    if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
    {
        if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD());
            HUD != nullptr && HUD->IsAnyPanelOpen())
        {
            HUD->CloseInteractionPanels();
            return;
        }
    }
    UObject* Target = CurrentInteractionTarget.Get();
    if (Target != nullptr
        && Target->GetClass()->ImplementsInterface(UCMLWorldInteractionTarget::StaticClass()))
    {
        ICMLWorldInteractionTarget::Execute_TryInteract(Target);
    }
}

void ACMLPlayerCharacter::PrimaryAction()
{
    if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
    {
        if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD());
            HUD != nullptr && HUD->IsAnyPanelOpen())
        {
            // HUD hit boxes own mouse clicks while a modal panel is visible.
            // Letting the gameplay action continue also submitted the legacy
            // one-unit machine transfer underneath the cursor gesture.
            FCMLStableId RepairAirshipId;
            if (HUD->GetActiveRepairAirship(RepairAirshipId))
            {
                TryRepairSelected();
            }
            return;
        }
    }
    if (TryCommitBuildPreview())
    {
        return;
    }
    if (TryCraftSelected())
    {
        return;
    }
    if (TryRepairSelected())
    {
        return;
    }
    if (TryQuickTransferSelected())
    {
        return;
    }
    if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
    {
        if (const ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD());
            HUD != nullptr && HUD->IsAnyPanelOpen())
        {
            return;
        }
    }
    if (FirstPersonCamera == nullptr || GetWorld() == nullptr)
    {
        return;
    }
    if (bSwinging || PickaxeView == nullptr || !bPickaxeEquipped)
    {
        return;
    }
    const FVector Start = FirstPersonCamera->GetComponentLocation();
    const FVector End = Start + FirstPersonCamera->GetForwardVector() * InteractionDistance;
    FCollisionQueryParams QueryParams(SCENE_QUERY_STAT(CMLPrimaryAction), true, this);
    FHitResult Hit;
    const bool bHit = GetWorld()->LineTraceSingleByChannel(
        Hit, Start, End, ECC_Visibility, QueryParams);
    AActor* Actor = bHit ? Hit.GetActor() : nullptr;
    UCMLGameplayTargetComponent* Target = Actor != nullptr
        ? Actor->FindComponentByClass<UCMLGameplayTargetComponent>() : nullptr;
    SwingTarget = Target;
    bSwingHasTarget = Target != nullptr
        && (Target->GetTargetKind() == ECMLGameplayTargetKind::EnvironmentalStone
            || Target->GetTargetKind() == ECMLGameplayTargetKind::IronOreRock
            || Target->GetTargetKind() == ECMLGameplayTargetKind::IronDepositSurface
            || Target->GetTargetKind() == ECMLGameplayTargetKind::CopperOreRock
            || Target->GetTargetKind() == ECMLGameplayTargetKind::CopperDepositSurface
            || Target->GetTargetKind() == ECMLGameplayTargetKind::TinOreRock
            || Target->GetTargetKind() == ECMLGameplayTargetKind::TinDepositSurface
            || Target->GetTargetKind() == ECMLGameplayTargetKind::FellableTree);
    SwingTargetDistance = bHit ? Hit.Distance
        : FCMLEquipmentSwing::MaximumStrikeDistanceUnrealUnits;
    SwingImpactPoint = bHit ? Hit.ImpactPoint : End;
    SwingImpactNormal = bHit && !Hit.ImpactNormal.IsNearlyZero()
        ? Hit.ImpactNormal.GetSafeNormal() : FVector::UpVector;
    SwingElapsed = 0.0f;
    PreviousSwingProgress = 0.0f;
    bSwingImpactSubmitted = false;
    bSwinging = true;
}

void ACMLPlayerCharacter::SecondaryAction()
{
    if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
    {
        if (const ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD());
            HUD != nullptr && HUD->IsAnyPanelOpen())
        {
            // Right click is the inventory half-stack / single-unit gesture.
            // Panels are closed explicitly with E, not by stealing this click.
            return;
        }
    }
    CancelBuildPreview();
}

void ACMLPlayerCharacter::RotateBuildPreview()
{
    if (!bBuildPreviewActive)
    {
        return;
    }
    BuildPreviewYaw = (BuildPreviewYaw + 1) & 3;
    bBuildYawExplicitlyRotated = true;
    UpdateBuildPreview();
}

bool ACMLPlayerCharacter::EnsureBuildPreview(const FCMLStableId& ItemId)
{
    if (BuildPreviewActor != nullptr && BuildPreviewItemId == ItemId)
    {
        return true;
    }
    DestroyBuildPreview();
    UWorld* World = GetWorld();
    UClass* ActorClass = FCMLBuildableVisuals::LoadActorClass(ItemId);
    if (World == nullptr || ActorClass == nullptr)
    {
        return false;
    }
    FActorSpawnParameters Parameters;
    Parameters.SpawnCollisionHandlingOverride =
        ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    Parameters.ObjectFlags |= RF_Transient;
    Parameters.OverrideLevel = GetLevel();
    BuildPreviewActor = World->SpawnActor<AActor>(
        ActorClass, FTransform::Identity, Parameters);
    if (BuildPreviewActor == nullptr)
    {
        return false;
    }
    BuildPreviewActor->Tags.Add(TEXT("CML.BuildPreview"));
    BuildPreviewItemId = ItemId;
    bBuildPreviewMaterialValid = false;
    // The imported prefabs contain only one arbitrary child mesh. Rebuild the
    // same complete station used after commit before applying ghost materials.
    FCMLBuildableVisuals::RebuildMigratedVisual(*BuildPreviewActor, ItemId);
    FCMLBuildableVisuals::ConfigureHologram(*BuildPreviewActor, false);
    return true;
}

void ACMLPlayerCharacter::DestroyBuildPreview()
{
    if (BuildPreviewActor != nullptr)
    {
        BuildPreviewActor->Destroy();
        BuildPreviewActor = nullptr;
    }
    BuildPreviewItemId = FCMLStableId();
    BuildPreviewExtractionRecipeId = FCMLStableId();
    BuildPreviewAttachmentTargetId = FCMLStableId();
    bBuildPreviewActive = false;
    bBuildPreviewValid = false;
    if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
    {
        if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
        {
            HUD->SetBuildPlacementStatus(false, FString(), FString(), false);
        }
    }
}

void ACMLPlayerCharacter::CancelBuildPreview()
{
    if (!bBuildPreviewActive)
    {
        return;
    }
    // Cancelling lasts until another slot/item is selected. Merely keeping the
    // same item in hand must not recreate it on the next frame.
    CancelledBuildItemId = BuildPreviewItemId;
    DestroyBuildPreview();
}

FCMLStableId ACMLPlayerCharacter::ResolveDrillRecipeAt(
    const FVector& WorldLocation, FString& OutFailure) const
{
    OutFailure = TEXT("La trivella va montata sulle rocce piatte del deposito");
    UWorld* World = GetWorld();
    if (World == nullptr)
    {
        return FCMLStableId();
    }
    TArray<FOverlapResult> Overlaps;
    FCollisionObjectQueryParams Objects;
    Objects.AddObjectTypesToQuery(ECC_WorldDynamic);
    Objects.AddObjectTypesToQuery(ECC_WorldStatic);
    FCollisionQueryParams Params(SCENE_QUERY_STAT(CMLDrillDeposit), false, this);
    World->OverlapMultiByObjectType(
        Overlaps,
        WorldLocation,
        FQuat::Identity,
        Objects,
        FCollisionShape::MakeSphere(75.0f),
        Params);
    for (const FOverlapResult& Overlap : Overlaps)
    {
        AActor* Actor = Overlap.GetActor();
        const UCMLGameplayTargetComponent* Target = Actor != nullptr
            ? Actor->FindComponentByClass<UCMLGameplayTargetComponent>() : nullptr;
        if (Target == nullptr) continue;
        switch (Target->GetTargetKind())
        {
            case ECMLGameplayTargetKind::IronDepositSurface:
                OutFailure.Reset();
                return CMLContentIds::DrillRawIron;
            case ECMLGameplayTargetKind::CopperDepositSurface:
                OutFailure.Reset();
                return CMLContentIds::DrillRawCopper;
            case ECMLGameplayTargetKind::TinDepositSurface:
                OutFailure.Reset();
                return CMLContentIds::DrillRawTin;
            default:
                break;
        }
    }
    return FCMLStableId();
}

void ACMLPlayerCharacter::UpdateBuildPreview()
{
    UWorld* World = GetWorld();
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    ACMLHUD* HUD = PlayerController != nullptr
        ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    UCMLSimulationSubsystem* Simulation = World != nullptr
        ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    if (World == nullptr || FirstPersonCamera == nullptr || HUD == nullptr || Simulation == nullptr)
    {
        DestroyBuildPreview();
        return;
    }

    FCMLInventoryUiSnapshot Inventory;
    const int32 SlotIndex = HUD->GetSelectedHotbarIndex();
    if (!Simulation->GetPlayerInventoryPresentation(Inventory)
        || !Inventory.Slots.IsValidIndex(SlotIndex))
    {
        DestroyBuildPreview();
        return;
    }
    const FCMLStableId ItemId = Inventory.Slots[SlotIndex].ItemId;
    if (ItemId != LastSelectedBuildItemId)
    {
        CancelledBuildItemId = FCMLStableId();
        LastSelectedBuildItemId = ItemId;
    }
    if (!FCMLBuildableVisuals::IsBuildable(ItemId)
        || Inventory.Slots[SlotIndex].Quantity <= 0
        || HUD->IsAnyPanelOpen() || bPiloting)
    {
        DestroyBuildPreview();
        return;
    }
    if (ItemId == CancelledBuildItemId)
    {
        DestroyBuildPreview();
        return;
    }

    if (BuildPreviewItemId != ItemId)
    {
        BuildPreviewYaw = 0;
        bBuildYawExplicitlyRotated = false;
    }
    if (!EnsureBuildPreview(ItemId))
    {
        HUD->SetBuildPlacementStatus(
            true, TEXT("Anteprima non disponibile"), TEXT("TASTO DESTRO  ANNULLA"), false);
        return;
    }
    bBuildPreviewActive = true;
    BuildPreviewHeldQuantity = Inventory.Slots[SlotIndex].Quantity;
    BuildPreviewActor->SetActorHiddenInGame(false);

    const FVector Start = FirstPersonCamera->GetComponentLocation();
    const FVector End = Start + FirstPersonCamera->GetForwardVector() * 1800.0f;
    FCollisionQueryParams QueryParams(SCENE_QUERY_STAT(CMLBuildPlacement), true, this);
    QueryParams.AddIgnoredActor(BuildPreviewActor);
    TArray<FHitResult> Hits;
    World->LineTraceMultiByChannel(Hits, Start, End, ECC_Visibility, QueryParams);
    FHitResult SurfaceHit;
    FHitResult PointedNodeHit;
    const UCMLGameplayTargetComponent* PointedNodeTarget = nullptr;
    bool bHasSurface = false;
    for (const FHitResult& Hit : Hits)
    {
        AActor* HitActor = Hit.GetActor();
        const UCMLGameplayTargetComponent* Target = HitActor != nullptr
            ? HitActor->FindComponentByClass<UCMLGameplayTargetComponent>() : nullptr;
        if (PointedNodeTarget == nullptr && Target != nullptr)
        {
            const ECMLGameplayTargetKind Kind = Target->GetTargetKind();
            if (Kind == ECMLGameplayTargetKind::WoodenCrate
                || Kind == ECMLGameplayTargetKind::CrudeFurnace
                || Kind == ECMLGameplayTargetKind::MechanicalPress
                || Kind == ECMLGameplayTargetKind::MechanicalDrill
                || Kind == ECMLGameplayTargetKind::FactoryFunnel
                || Kind == ECMLGameplayTargetKind::FactoryBelt)
            {
                PointedNodeTarget = Target;
                PointedNodeHit = Hit;
                continue;
            }
        }
        if (Hit.ImpactNormal.Z >= 0.45f)
        {
            SurfaceHit = Hit;
            bHasSurface = true;
            break;
        }
    }

    const FCMLMachineSimulationState& Machines = Simulation->GetPublishedState().Machines;
    const FCMLMachineNodeState* TargetNode = nullptr;
    if (PointedNodeTarget != nullptr)
    {
        for (const FCMLMachineNodeState& Node : Machines.Nodes)
        {
            if (Node.Id == PointedNodeTarget->GetSourceId())
            {
                TargetNode = &Node;
                break;
            }
        }
    }
    if (!bHasSurface && TargetNode != nullptr && TargetNode->bHasPlacementPose)
    {
        SurfaceHit = PointedNodeHit;
        SurfaceHit.ImpactPoint.Z =
            static_cast<double>(TargetNode->PlacementPose.YMillimetres) / 10.0;
        bHasSurface = true;
    }
    if (!bHasSurface)
    {
        BuildPreviewActor->SetActorHiddenInGame(true);
        bBuildPreviewValid = false;
        HUD->SetBuildPlacementStatus(
            true,
            TEXT("Mira una superficie su cui costruire"),
            TEXT("R  RUOTA   ·   TASTO DESTRO  ANNULLA"),
            false);
        return;
    }

    const FVector Snapped(
        FMath::GridSnap(SurfaceHit.ImpactPoint.X, 100.0),
        FMath::GridSnap(SurfaceHit.ImpactPoint.Y, 100.0),
        SurfaceHit.ImpactPoint.Z);
    FCMLMachineBuildPose Desired;
    Desired.XMillimetres = FMath::RoundToInt64(Snapped.Y * 10.0);
    Desired.YMillimetres = FMath::RoundToInt64(Snapped.Z * 10.0);
    Desired.ZMillimetres = FMath::RoundToInt64(Snapped.X * 10.0);
    Desired.YawQuarterTurns = BuildPreviewYaw;

    FCMLMachineBuildPose Resolved = Desired;
    BuildPreviewAttachmentTargetId = FCMLStableId();
    if (TargetNode != nullptr && TargetNode->bHasPlacementPose)
    {
        const FVector TargetWorld = PointedNodeHit.GetActor() != nullptr
            ? PointedNodeHit.GetActor()->GetActorLocation()
            : FCMLBuildableVisuals::WorldLocation(TargetNode->PlacementPose);
        const FVector Delta = PointedNodeHit.ImpactPoint - TargetWorld;
        uint8 AimedSide = 0;
        if (FMath::Abs(Delta.Y) > FMath::Abs(Delta.X))
            AimedSide = Delta.Y >= 0.0 ? 1 : 3;
        else
            AimedSide = Delta.X >= 0.0 ? 0 : 2;
        Resolved = FCMLBuildPlacementResolver::ResolveFromTarget(
            Machines,
            TargetNode->Id,
            Desired,
            FCMLBuildableVisuals::BuildKind(ItemId),
            AimedSide,
            static_cast<uint8>(BuildPreviewYaw),
            bBuildYawExplicitlyRotated,
            FCMLBuildableVisuals::DefinitionId(ItemId));
        BuildPreviewAttachmentTargetId = TargetNode->Id;
    }
    else
    {
        Resolved = FCMLBuildPlacementResolver::ResolveOccupiedConnectorCell(
            Machines,
            Desired,
            FCMLBuildableVisuals::BuildKind(ItemId),
            !bBuildYawExplicitlyRotated,
            FCMLBuildableVisuals::DefinitionId(ItemId));
    }

    BuildPreviewPose = Resolved;
    BuildPreviewYaw = Resolved.YawQuarterTurns;
    BuildPreviewVisualLocation = FCMLBuildableVisuals::WorldLocation(Resolved);
    const bool bAttachedToTarget = TargetNode != nullptr
        && PointedNodeHit.GetActor() != nullptr
        && FCMLBuildPlacementResolver::ArePortsAdjacent(
            FCMLBuildableVisuals::NodeKind(ItemId),
            Resolved,
            FCMLBuildableVisuals::DefinitionId(ItemId),
            TargetNode->Kind,
            TargetNode->PlacementPose,
            TargetNode->DefinitionId);
    if (!bAttachedToTarget)
    {
        BuildPreviewAttachmentTargetId = FCMLStableId();
    }
    if (bAttachedToTarget)
    {
        // The logical connection is one exact grid step.  Imported artwork can
        // have a root offset inside that cell, so inherit the explicitly aimed
        // target's offset.  This is also the location serialised with commit:
        // the real actor can therefore never jump away from its hologram.
        const FVector LogicalTarget =
            FCMLBuildableVisuals::WorldLocation(TargetNode->PlacementPose);
        const FVector TargetVisual = PointedNodeHit.GetActor()->GetActorLocation();
        FVector TargetOffset = TargetVisual - LogicalTarget;
        TargetOffset.Z = 0.0f;
        BuildPreviewVisualLocation += TargetOffset;
    }
    BuildPreviewActor->SetActorLocationAndRotation(
        BuildPreviewVisualLocation,
        FCMLBuildableVisuals::WorldRotation(ItemId, Resolved));

    BuildPreviewExtractionRecipeId = FCMLStableId();
    FString DrillFailure;
    if (ItemId == CMLContentIds::MechanicalDrillItem)
    {
        BuildPreviewExtractionRecipeId = ResolveDrillRecipeAt(
            BuildPreviewVisualLocation, DrillFailure);
    }
    FString BlockerName;
    const bool bOverlap = HasBuildPhysicalOverlap(
        BuildPreviewAttachmentTargetId, BlockerName);
    ECMLBuildRejection Rejection = ECMLBuildRejection::None;
    const bool bSlopeValid = TargetNode != nullptr
        || SurfaceHit.ImpactNormal.Z >= 0.70f;
    const bool bPreflight = !bOverlap && bSlopeValid
        && Simulation->TryPreflightBuild(
            ItemId, Resolved, BuildPreviewExtractionRecipeId, Rejection);
    bBuildPreviewValid = !bOverlap && bSlopeValid && bPreflight;
    if (bBuildPreviewValid != bBuildPreviewMaterialValid)
    {
        FCMLBuildableVisuals::ConfigureHologram(*BuildPreviewActor, bBuildPreviewValid);
        bBuildPreviewMaterialValid = bBuildPreviewValid;
    }

    if (bBuildPreviewValid)
    {
        BuildPreviewStatus = FString::Printf(
            TEXT("%s x%lld"),
            *FCMLBuildableVisuals::DisplayName(ItemId),
            BuildPreviewHeldQuantity);
    }
    else if (bOverlap)
    {
        BuildPreviewStatus = FString::Printf(TEXT("Spazio occupato da %s"), *BlockerName);
    }
    else if (!bSlopeValid)
    {
        BuildPreviewStatus = TEXT("La superficie e troppo inclinata");
    }
    else if (ItemId == CMLContentIds::MechanicalDrillItem
        && BuildPreviewExtractionRecipeId.IsNone())
    {
        BuildPreviewStatus = DrillFailure;
    }
    else if (Rejection == ECMLBuildRejection::InsufficientQuantity
        || Rejection == ECMLBuildRejection::BuildSourceMissing)
    {
        BuildPreviewStatus = TEXT("Non hai piu questo oggetto nell'inventario");
    }
    else if (Rejection == ECMLBuildRejection::BuildTopologyInvalid)
    {
        BuildPreviewStatus = FCMLBuildPlacementResolver::FindOccupant(Machines, Resolved)
            != nullptr ? TEXT("Questa cella e occupata")
                       : TEXT("Connessione o ricetta non valida");
    }
    else
    {
        BuildPreviewStatus = TEXT("Piazzamento non valido");
    }
    HUD->SetBuildPlacementStatus(
        true,
        BuildPreviewStatus,
        bBuildPreviewValid
            ? TEXT("CLICK SINISTRO  PIAZZA   ·   R  RUOTA   ·   TASTO DESTRO  ANNULLA")
            : TEXT("R  RUOTA   ·   TASTO DESTRO  ANNULLA"),
        bBuildPreviewValid);
}

bool ACMLPlayerCharacter::HasBuildPhysicalOverlap(
    const FCMLStableId& AttachmentTargetId, FString& OutBlockerName) const
{
    OutBlockerName.Reset();
    if (BuildPreviewActor == nullptr || GetWorld() == nullptr)
    {
        return false;
    }

    FBox Bounds = BuildPreviewActor->GetComponentsBoundingBox(true);
    if (!Bounds.IsValid)
    {
        return false;
    }
    // UE actor bounds include decorative antennae and handles. Shrinking by a
    // centimetre preserves Unity's volume tolerance without allowing genuine
    // mesh interpenetration.
    const FVector Extent = (Bounds.GetExtent() - FVector(1.0f)).ComponentMax(FVector(5.0f));
    // The lower two centimetres are the intended supporting contact.  Testing
    // that slab against the terrain itself would make every ground placement
    // report the landscape as an obstruction, while rocks and props above it
    // remain fully covered.
    const float GroundContactAllowance = FMath::Min(2.0f, Extent.Z * 0.25f);
    const FVector OverlapCenter = Bounds.GetCenter()
        + FVector(0.0f, 0.0f, GroundContactAllowance);
    const FVector OverlapExtent(
        Extent.X,
        Extent.Y,
        FMath::Max(2.0f, Extent.Z - GroundContactAllowance));
    TArray<FOverlapResult> Overlaps;
    FCollisionObjectQueryParams Objects;
    Objects.AddObjectTypesToQuery(ECC_WorldStatic);
    Objects.AddObjectTypesToQuery(ECC_WorldDynamic);
    FCollisionQueryParams Params(SCENE_QUERY_STAT(CMLBuildOverlap), false, this);
    Params.AddIgnoredActor(BuildPreviewActor);
    GetWorld()->OverlapMultiByObjectType(
        Overlaps,
        OverlapCenter,
        FQuat::Identity,
        Objects,
        FCollisionShape::MakeBox(OverlapExtent),
        Params);
    for (const FOverlapResult& Overlap : Overlaps)
    {
        AActor* Actor = Overlap.GetActor();
        if (Actor == nullptr || Actor == this || Actor == BuildPreviewActor
            || Actor->IsA<ALevelScriptActor>())
        {
            continue;
        }
        const UCMLGameplayTargetComponent* Target =
            Actor->FindComponentByClass<UCMLGameplayTargetComponent>();
        if (Target != nullptr)
        {
            const ECMLGameplayTargetKind Kind = Target->GetTargetKind();
            if (Target->GetSourceId() == AttachmentTargetId
                || Kind == ECMLGameplayTargetKind::IronDepositSurface
                || Kind == ECMLGameplayTargetKind::CopperDepositSurface
                || Kind == ECMLGameplayTargetKind::TinDepositSurface)
            {
                continue;
            }
        }
        // Landscape and terrain are the supporting plane, not blockers. The
        // query still catches rocks, trees, authored props and placed modules.
        const FString Identity = (Actor->GetClass()->GetName() + TEXT("|")
            + Actor->GetName()).ToLower();
        if (Identity.Contains(TEXT("landscape"))
            || Identity.Contains(TEXT("terrain"))
            || Identity.Contains(TEXT("landmass")))
        {
            continue;
        }
        OutBlockerName = Actor->GetName();
        return true;
    }
    return false;
}

bool ACMLPlayerCharacter::TryCommitBuildPreview()
{
    if (!bBuildPreviewActive)
    {
        return false;
    }
    if (!bBuildPreviewValid)
    {
        return true;
    }
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation = World != nullptr
        ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    if (Simulation == nullptr)
    {
        return true;
    }

    FCMLRuntimeCommandHandle Handle;
    if (!Simulation->RequestBuild(
            BuildPreviewItemId,
            BuildPreviewPose,
            BuildPreviewExtractionRecipeId,
            BuildPreviewVisualLocation,
            Handle))
    {
        if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
        {
            if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
            {
                HUD->PushCollectionFeed(TEXT("Costruzione non disponibile"));
            }
        }
    }
    else
    {
        DestroyBuildPreview();
    }
    return true;
}

void ACMLPlayerCharacter::TogglePersonalCrafting()
{
    // Unity's personal/quick crafting lives in the right column of the Tab
    // inventory; C no longer opens a second, incompatible panel.
    ToggleInventory();
}

bool ACMLPlayerCharacter::TryCraftSelected()
{
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    ACMLHUD* HUD = PlayerController != nullptr ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    if (HUD == nullptr || !HUD->IsCraftingVisible())
    {
        return false;
    }
    FCMLStableId RecipeId;
    ECMLCraftingStationKind Station = ECMLCraftingStationKind::None;
    if (!HUD->GetSelectedCraftingRecipe(RecipeId, Station))
    {
        return true;
    }
    if (UWorld* World = GetWorld())
    {
        if (UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>())
        {
            Simulation->RequestCraftPlayerItem(RecipeId, Station, 1);
        }
    }
    return true;
}

void ACMLPlayerCharacter::CraftSelectedAction()
{
    TryCraftSelected();
}

bool ACMLPlayerCharacter::TryQuickTransferSelected()
{
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    ACMLHUD* HUD = PlayerController != nullptr ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    if (HUD == nullptr)
    {
        return false;
    }
    FCMLStableId NodeId;
    if (!HUD->GetActiveTransferNode(NodeId))
    {
        return false;
    }
    if (UWorld* World = GetWorld())
    {
        if (UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>())
        {
            FCMLRuntimeCommandHandle Handle;
            Simulation->RequestQuickTransfer(
                NodeId, HUD->GetSelectedHotbarIndex(), Handle);
        }
    }
    return true;
}

bool ACMLPlayerCharacter::TryRepairSelected()
{
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    ACMLHUD* HUD = PlayerController != nullptr ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    FCMLStableId AirshipId;
    if (HUD == nullptr || !HUD->GetActiveRepairAirship(AirshipId))
    {
        return false;
    }
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation = World != nullptr
        ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    FCMLInventoryUiSnapshot Inventory;
    const int32 SlotIndex = HUD->GetSelectedHotbarIndex();
    if (Simulation == nullptr
        || !Simulation->GetPlayerInventoryPresentation(Inventory)
        || !Inventory.Slots.IsValidIndex(SlotIndex))
    {
        return true;
    }
    const FCMLStableId ItemId = Inventory.Slots[SlotIndex].ItemId;
    if (ItemId != CMLContentIds::IronPlate
        && ItemId != CMLContentIds::InsulatedCable)
    {
        HUD->PushCollectionFeed(TEXT("Seleziona una piastra di ferro o un cavo isolato"));
        return true;
    }
    FCMLRuntimeCommandHandle Handle;
    if (!Simulation->RequestAirshipRepairInstall(AirshipId, ItemId, Handle))
    {
        HUD->PushCollectionFeed(TEXT("Componente non installabile"));
    }
    return true;
}

void ACMLPlayerCharacter::ScrollHotbar(const float Value)
{
    if (FMath::IsNearlyZero(Value))
    {
        return;
    }
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    ACMLHUD* HUD = PlayerController != nullptr ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    if (HUD == nullptr)
    {
        return;
    }
    if (HUD->IsCraftingVisible())
    {
        HUD->StepWorkbenchRecipe(Value > 0.0f ? -1 : 1);
        return;
    }
    const int32 Delta = Value > 0.0f ? -1 : 1;
    SelectHotbar((HUD->GetSelectedHotbarIndex() + Delta + 8) % 8);
}

void ACMLPlayerCharacter::SelectHotbar0() { SelectHotbar(0); }
void ACMLPlayerCharacter::SelectHotbar1() { SelectHotbar(1); }
void ACMLPlayerCharacter::SelectHotbar2() { SelectHotbar(2); }
void ACMLPlayerCharacter::SelectHotbar3() { SelectHotbar(3); }
void ACMLPlayerCharacter::SelectHotbar4() { SelectHotbar(4); }
void ACMLPlayerCharacter::SelectHotbar5() { SelectHotbar(5); }
void ACMLPlayerCharacter::SelectHotbar6() { SelectHotbar(6); }
void ACMLPlayerCharacter::SelectHotbar7() { SelectHotbar(7); }

void ACMLPlayerCharacter::SelectHotbar(const int32 Index)
{
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    if (ACMLHUD* HUD = PlayerController != nullptr
            ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr)
    {
        HUD->SetSelectedHotbarIndex(FMath::Clamp(Index, 0, 7));
    }
}

UObject* ACMLPlayerCharacter::ResolveInteractionTarget(
    AActor* Actor, const UPrimitiveComponent* HitComponent) const
{
    if (Actor == nullptr)
    {
        return nullptr;
    }
    if (Actor->GetClass()->ImplementsInterface(UCMLWorldInteractionTarget::StaticClass())
        && ICMLWorldInteractionTarget::Execute_IsInteractionAvailable(Actor))
    {
        return Actor;
    }
    const TArray<UActorComponent*> Components =
        Actor->GetComponentsByInterface(UCMLWorldInteractionTarget::StaticClass());
    for (UActorComponent* Component : Components)
    {
        const UCMLGameplayTargetComponent* GameplayTarget =
            Cast<UCMLGameplayTargetComponent>(Component);
        if (Component != nullptr
            && (GameplayTarget == nullptr
                || GameplayTarget->MatchesInteractionComponent(HitComponent))
            && ICMLWorldInteractionTarget::Execute_IsInteractionAvailable(Component))
        {
            return Component;
        }
    }
    return nullptr;
}

void ACMLPlayerCharacter::UpdateInteractionTarget()
{
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    ACMLHUD* HUD = PlayerController != nullptr ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    if (FirstPersonCamera == nullptr || GetWorld() == nullptr
        || (HUD != nullptr && HUD->IsAnyPanelOpen()))
    {
        ClearInteractionTarget();
        return;
    }

    const FVector Start = FirstPersonCamera->GetComponentLocation();
    const FVector Forward = FirstPersonCamera->GetForwardVector();
    const FVector End = Start + Forward * InteractionDistance;
    FCollisionQueryParams QueryParams(SCENE_QUERY_STAT(CMLCentralInteraction), true, this);
    TArray<FHitResult> Hits;
    GetWorld()->LineTraceMultiByChannel(Hits, Start, End, ECC_Visibility, QueryParams);

    UObject* BestTarget = nullptr;
    AActor* BestActor = nullptr;
    float BestDistance = MAX_flt;
    for (const FHitResult& Hit : Hits)
    {
        if (Hit.Distance >= BestDistance)
        {
            continue;
        }
        if (UObject* Target = ResolveInteractionTarget(Hit.GetActor(), Hit.GetComponent()))
        {
            BestTarget = Target;
            BestActor = Hit.GetActor();
            BestDistance = Hit.Distance;
        }
    }

    // Small targets close to the reticle remain selectable even when their
    // collision is thinner than a pixel, matching Unity's proximity assist.
    // Use actor iteration rather than an overlap query: several Unity pickup
    // prefabs imported without collision primitives, which made them invisible
    // to OverlapMulti even though their visual mesh and interaction component
    // were otherwise valid.
    if (BestTarget == nullptr)
    {
        const float MinimumDot = FMath::Cos(FMath::DegreesToRadians(ProximityAimDegrees));
        float BestDot = MinimumDot;
        for (TActorIterator<AActor> It(GetWorld()); It; ++It)
        {
            AActor* Actor = *It;
            if (Actor == this)
            {
                continue;
            }
            const TArray<UActorComponent*> Components =
                Actor->GetComponentsByInterface(UCMLWorldInteractionTarget::StaticClass());
            for (UActorComponent* Component : Components)
            {
                if (Component == nullptr
                    || !ICMLWorldInteractionTarget::Execute_IsInteractionAvailable(Component))
                {
                    continue;
                }
                const UCMLGameplayTargetComponent* GameplayTarget =
                    Cast<UCMLGameplayTargetComponent>(Component);
                const FBox Bounds = GameplayTarget != nullptr
                    ? GameplayTarget->GetInteractionBounds()
                    : Actor->GetComponentsBoundingBox(true);
                if (!Bounds.IsValid)
                {
                    continue;
                }
                const FVector Centre = Bounds.GetCenter();
                const FVector Closest = Bounds.GetClosestPointTo(Start);
                const FVector ToTarget = Centre - Start;
                const float Distance = FVector::Distance(Start, Closest);
                const float Dot = FVector::DotProduct(Forward, ToTarget.GetSafeNormal());
                if (Distance <= InteractionDistance && Dot > BestDot)
                {
                    BestDot = Dot;
                    BestTarget = Component;
                    BestActor = Actor;
                }
            }
        }
    }

    SetInteractionTarget(BestTarget, BestActor);
}

void ACMLPlayerCharacter::SetInteractionTarget(UObject* Target, AActor* Actor)
{
    if (CurrentInteractionTarget.Get() == Target && CurrentInteractionActor.Get() == Actor)
    {
        return;
    }
    ClearInteractionTarget();
    if (Target == nullptr || Actor == nullptr)
    {
        return;
    }

    CurrentInteractionTarget = Target;
    CurrentInteractionActor = Actor;
    SetActorHighlighted(Actor, true);
    UCMLGameplayTargetComponent* GameplayTarget = Cast<UCMLGameplayTargetComponent>(Target);
    UPrimitiveComponent* Anchor = GameplayTarget != nullptr
        ? GameplayTarget->GetInteractionAnchor() : nullptr;
    const FBox Bounds = GameplayTarget != nullptr
        ? GameplayTarget->GetInteractionBounds()
        : Actor->GetComponentsBoundingBox(true);
    const FVector PromptLocation = FCMLWorldPromptPlacement::Resolve(
        Bounds,
        FirstPersonCamera->GetComponentLocation(),
        FirstPersonCamera->GetForwardVector());
    if (InteractionPromptRoot != nullptr)
    {
        InteractionPromptRoot->AttachToComponent(
            Anchor != nullptr ? Anchor : Actor->GetRootComponent(),
            FAttachmentTransformRules::KeepWorldTransform);
    }
    ShowWorldInteractionPrompt(
        ICMLWorldInteractionTarget::Execute_GetInteractionPrompt(Target), PromptLocation);
}

void ACMLPlayerCharacter::ClearInteractionTarget()
{
    if (AActor* Actor = CurrentInteractionActor.Get())
    {
        SetActorHighlighted(Actor, false);
    }
    CurrentInteractionTarget.Reset();
    CurrentInteractionActor.Reset();
    HideWorldInteractionPrompt();
    if (InteractionPromptRoot != nullptr)
    {
        InteractionPromptRoot->DetachFromComponent(FDetachmentTransformRules::KeepWorldTransform);
    }
    if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
    {
        if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
        {
            HUD->ClearInteractionPrompt();
        }
    }
}

void ACMLPlayerCharacter::ShowWorldInteractionPrompt(
    const FText& Prompt, const FVector& WorldLocation)
{
    if (InteractionPromptRoot == nullptr
        || InteractionPromptText == nullptr
        || InteractionPromptShadow == nullptr)
    {
        return;
    }
    const FText Uppercase = FText::FromString(
        FString::Printf(TEXT("E   %s"), *Prompt.ToString().ToUpper()));
    InteractionPromptText->SetText(Uppercase);
    InteractionPromptShadow->SetText(Uppercase);
    InteractionPromptRoot->SetWorldLocation(WorldLocation);
    InteractionPromptRoot->SetVisibility(!Prompt.IsEmpty(), true);
    UpdateWorldInteractionPrompt();
}

void ACMLPlayerCharacter::HideWorldInteractionPrompt()
{
    if (InteractionPromptRoot != nullptr)
    {
        InteractionPromptRoot->SetVisibility(false, true);
    }
}

void ACMLPlayerCharacter::UpdateWorldInteractionPrompt()
{
    if (InteractionPromptRoot == nullptr || FirstPersonCamera == nullptr
        || !InteractionPromptRoot->IsVisible())
    {
        return;
    }
    if (UObject* Target = CurrentInteractionTarget.Get())
    {
        const FText Prompt = ICMLWorldInteractionTarget::Execute_GetInteractionPrompt(Target);
        const FText Uppercase = FText::FromString(
            FString::Printf(TEXT("E   %s"), *Prompt.ToString().ToUpper()));
        InteractionPromptText->SetText(Uppercase);
        InteractionPromptShadow->SetText(Uppercase);
    }
    const FVector TowardCamera =
        FirstPersonCamera->GetComponentLocation() - InteractionPromptRoot->GetComponentLocation();
    if (!TowardCamera.IsNearlyZero())
    {
        InteractionPromptRoot->SetWorldRotation(TowardCamera.Rotation());
    }
}

void ACMLPlayerCharacter::SetActorHighlighted(AActor* Actor, const bool bHighlighted) const
{
    if (Actor == nullptr)
    {
        return;
    }
    TArray<UPrimitiveComponent*> Primitives;
    Actor->GetComponents(Primitives);
    for (UPrimitiveComponent* Primitive : Primitives)
    {
        if (Primitive != nullptr)
        {
            Primitive->SetRenderCustomDepth(bHighlighted);
            Primitive->SetCustomDepthStencilValue(bHighlighted ? 1 : 0);
            Primitive->MarkRenderStateDirty();
        }
    }
}

void ACMLPlayerCharacter::AssemblePickaxeView()
{
    AActor* ViewActor = PickaxeView != nullptr ? PickaxeView->GetChildActor() : nullptr;
    if (ViewActor == nullptr)
    {
        return;
    }

    // The migrated prefab accidentally retained only GEO_Binding_Grip. Unity's
    // prefab is one atlas-driven tool made from these five coincident parts.
    static const TCHAR* MeshPaths[] = {
        TEXT("/Game/Migrated/Project/Art/Tools/Pickaxe/Models/TOOL_PickaxeCrude/GEO_Handle.GEO_Handle"),
        TEXT("/Game/Migrated/Project/Art/Tools/Pickaxe/Models/TOOL_PickaxeCrude/GEO_StoneHead_Active.GEO_StoneHead_Active"),
        TEXT("/Game/Migrated/Project/Art/Tools/Pickaxe/Models/TOOL_PickaxeCrude/GEO_StoneHead_Back.GEO_StoneHead_Back"),
        TEXT("/Game/Migrated/Project/Art/Tools/Pickaxe/Models/TOOL_PickaxeCrude/GEO_Binding_Head.GEO_Binding_Head"),
        TEXT("/Game/Migrated/Project/Art/Tools/Pickaxe/Models/TOOL_PickaxeCrude/GEO_Binding_Grip.GEO_Binding_Grip")};

    TSet<FName> PresentMeshNames;
    TArray<UStaticMeshComponent*> ExistingMeshes;
    ViewActor->GetComponents(ExistingMeshes);
    for (UStaticMeshComponent* Existing : ExistingMeshes)
    {
        if (Existing != nullptr && Existing->GetStaticMesh() != nullptr)
        {
            PresentMeshNames.Add(Existing->GetStaticMesh()->GetFName());
        }
    }

    USceneComponent* AttachRoot = ViewActor->GetRootComponent();
    for (const TCHAR* MeshPath : MeshPaths)
    {
        UStaticMesh* PartMesh = LoadObject<UStaticMesh>(nullptr, MeshPath);
        if (PartMesh == nullptr || PresentMeshNames.Contains(PartMesh->GetFName()))
        {
            continue;
        }
        if (AttachRoot == nullptr)
        {
            UStaticMeshComponent* NewRoot = NewObject<UStaticMeshComponent>(
                ViewActor, *FString::Printf(TEXT("CML_%s"), *PartMesh->GetName()));
            ViewActor->AddInstanceComponent(NewRoot);
            ViewActor->SetRootComponent(NewRoot);
            NewRoot->SetStaticMesh(PartMesh);
            NewRoot->SetCollisionEnabled(ECollisionEnabled::NoCollision);
            NewRoot->SetCastShadow(false);
            NewRoot->RegisterComponent();
            AttachRoot = NewRoot;
            PresentMeshNames.Add(PartMesh->GetFName());
            continue;
        }
        UStaticMeshComponent* Part = NewObject<UStaticMeshComponent>(
            ViewActor, *FString::Printf(TEXT("CML_%s"), *PartMesh->GetName()));
        ViewActor->AddInstanceComponent(Part);
        Part->SetupAttachment(AttachRoot);
        Part->SetStaticMesh(PartMesh);
        Part->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Part->SetCastShadow(false);
        Part->RegisterComponent();
        Part->SetRelativeTransform(FTransform::Identity);
        PresentMeshNames.Add(PartMesh->GetFName());
    }
}

void ACMLPlayerCharacter::UpdateFirstPersonPresentation(const float DeltaSeconds)
{
    if (EquipmentMotionRoot == nullptr || FirstPersonCamera == nullptr
        || GetCharacterMovement() == nullptr)
    {
        return;
    }
    const float Dt = FMath::Max(0.0f, DeltaSeconds);
    const auto Blend = [Dt](const float Sharpness)
    {
        return 1.0f - FMath::Exp(-Sharpness * Dt);
    };

    const bool bFalling = GetCharacterMovement()->IsFalling();
    const float Speed = GetVelocity().Size2D();
    const bool bMoving = !bFalling && Speed > 4.0f;
    const float MaxSpeed = bSprinting ? 640.0f : 400.0f;
    const float TargetWeight = bMoving ? FMath::Clamp(Speed / MaxSpeed, 0.0f, 1.0f) : 0.0f;
    LocomotionWeight = FMath::Lerp(LocomotionWeight, TargetWeight, Blend(10.0f));
    SprintWeight = FMath::Lerp(SprintWeight, bSprinting ? 1.0f : 0.0f, Blend(7.5f));
    if (bMoving)
    {
        BobPhase = FMath::Fmod(
            BobPhase + Dt * FMath::Lerp(1.75f, 2.35f, SprintWeight) * UE_TWO_PI,
            UE_TWO_PI);
    }
    const float Lateral = FMath::Sin(BobPhase);
    const float Step = FMath::Abs(Lateral);
    const float Depth = FMath::Cos(BobPhase * 2.0f);
    const FVector PositionAmplitude = FMath::Lerp(
        FVector(0.6f, 1.2f, 1.7f), FVector(0.7f, 1.4f, 2.0f), SprintWeight);
    const FVector LocomotionPosition(
        Depth * PositionAmplitude.X,
        Lateral * PositionAmplitude.Y,
        -Step * PositionAmplitude.Z);
    const FRotator RotationAmplitude = FRotator(
        FMath::Lerp(0.7f, 0.8f, SprintWeight),
        FMath::Lerp(0.9f, 1.05f, SprintWeight),
        FMath::Lerp(1.2f, 1.35f, SprintWeight));
    const FRotator LocomotionRotation(
        -Step * RotationAmplitude.Pitch * LocomotionWeight,
        Lateral * RotationAmplitude.Yaw * LocomotionWeight,
        Lateral * RotationAmplitude.Roll * LocomotionWeight);

    FVector JumpTarget = FVector::ZeroVector;
    FRotator JumpRotationTarget = FRotator::ZeroRotator;
    if (bFalling)
    {
        const float Descent = FMath::Clamp(
            (GetCharacterMovement()->JumpZVelocity - GetVelocity().Z)
                / (GetCharacterMovement()->JumpZVelocity * 2.0f),
            0.0f, 1.0f);
        JumpTarget = FMath::Lerp(FVector(-2.6f, 0.0f, -4.2f), FVector(1.2f, 0.0f, 1.8f), Descent);
        JumpRotationTarget = FMath::Lerp(FRotator(3.5f, 0.0f, 0.8f), FRotator(-2.2f, 0.0f, -0.6f), Descent);
    }
    EquipmentJumpPosition = FMath::Lerp(EquipmentJumpPosition, JumpTarget, Blend(9.0f));
    EquipmentJumpRotation = FMath::Lerp(EquipmentJumpRotation, JumpRotationTarget, Blend(9.0f));

    if (bWasFalling && !bFalling)
    {
        LandingElapsed = 0.0f;
        LandingStrength = FMath::Lerp(
            0.72f, 1.2f,
            FMath::Clamp((FMath::Abs(PreviousVerticalVelocity) - 300.0f) / 800.0f, 0.0f, 1.0f));
    }
    float EquipmentLandingWeight = 0.0f;
    float CameraLandingWeight = 0.0f;
    if (LandingElapsed < 0.32f)
    {
        LandingElapsed += Dt;
        const float EquipmentProgress = FMath::Clamp(LandingElapsed / 0.30f, 0.0f, 1.0f);
        EquipmentLandingWeight = EquipmentProgress < 0.30f
            ? FMath::SmoothStep(0.0f, 1.0f, EquipmentProgress / 0.30f)
            : 1.0f - FMath::InterpEaseInOut(0.0f, 1.0f, (EquipmentProgress - 0.30f) / 0.70f, 3.0f);
        const float CameraProgress = FMath::Clamp(LandingElapsed / 0.32f, 0.0f, 1.0f);
        CameraLandingWeight = CameraProgress < 0.28f
            ? FMath::SmoothStep(0.0f, 1.0f, CameraProgress / 0.28f)
            : 1.0f - FMath::InterpEaseInOut(0.0f, 1.0f, (CameraProgress - 0.28f) / 0.72f, 3.0f);
    }

    const FQuat CurrentCameraRotation = FirstPersonCamera->GetComponentQuat();
    const FRotator CameraDelta = (PreviousCameraRotation.Inverse() * CurrentCameraRotation).Rotator();
    PreviousCameraRotation = CurrentCameraRotation;
    const float PitchDelta = FMath::Clamp(CameraDelta.Pitch, -8.0f, 8.0f);
    const float YawDelta = FMath::Clamp(CameraDelta.Yaw, -8.0f, 8.0f);
    const FVector LookTarget(0.0f, -YawDelta * 0.12f, PitchDelta * 0.078f);
    const FRotator LookRotationTarget(PitchDelta * 0.55f, YawDelta * 0.3575f, -YawDelta * 0.55f);
    LookPosition = FMath::Lerp(LookPosition, LookTarget, Blend(16.0f));
    LookRotation = FMath::Lerp(LookRotation, LookRotationTarget, Blend(16.0f));

    const FVector EquipmentLanding = FVector(2.4f, 0.0f, -5.2f)
        * EquipmentLandingWeight * LandingStrength;
    const FRotator EquipmentLandingRotation = FRotator(-4.5f, 0.0f, -1.1f)
        * EquipmentLandingWeight * LandingStrength;
    EquipmentMotionRoot->SetRelativeLocation(
        LocomotionPosition * LocomotionWeight + LookPosition
        + EquipmentJumpPosition + EquipmentLanding);
    EquipmentMotionRoot->SetRelativeRotation(
        LocomotionRotation + LookRotation + EquipmentJumpRotation + EquipmentLandingRotation);

    float TakeoffWeight = 0.0f;
    if (TakeoffElapsed < 0.14f)
    {
        TakeoffElapsed += Dt;
        TakeoffWeight = FMath::Sin(FMath::Clamp(TakeoffElapsed / 0.14f, 0.0f, 1.0f) * UE_PI);
    }
    FirstPersonCamera->SetRelativeLocation(
        FVector(-10.0f, 0.0f, 64.0f)
        + FVector(1.6f * CameraLandingWeight * LandingStrength, 0.0f,
            -2.6f * TakeoffWeight - 6.5f * CameraLandingWeight * LandingStrength));

    PreviousVerticalVelocity = GetVelocity().Z;
    bWasFalling = bFalling;
}

void ACMLPlayerCharacter::UpdateHeldEquipment()
{
    if (PickaxeView == nullptr)
    {
        return;
    }
    APlayerController* PlayerController = Cast<APlayerController>(Controller);
    const ACMLHUD* HUD = PlayerController != nullptr
        ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    bool bShow = false;
    if (HUD != nullptr && !HUD->IsAnyPanelOpen())
    {
        FCMLInventoryUiSnapshot Inventory;
        if (const UWorld* World = GetWorld())
        {
            if (const UCMLSimulationSubsystem* Simulation =
                    World->GetSubsystem<UCMLSimulationSubsystem>();
                Simulation != nullptr
                && Simulation->GetPlayerInventoryPresentation(Inventory))
            {
                const int32 SlotIndex = HUD->GetSelectedHotbarIndex();
                if (Inventory.Slots.IsValidIndex(SlotIndex))
                {
                    const FCMLStableId ItemId = Inventory.Slots[SlotIndex].ItemId;
                    bShow = ItemId == CMLContentIds::CrudePickaxe
                        || ItemId == CMLContentIds::IronPickaxe;
                }
            }
        }
    }
    bPickaxeEquipped = bShow;
    PickaxeView->SetHiddenInGame(!bShow, true);
}

void ACMLPlayerCharacter::UpdateEquipmentSwing(const float DeltaSeconds)
{
    if (!bSwinging || PickaxeSwingRoot == nullptr)
    {
        return;
    }
    const float Duration = FCMLEquipmentSwing::DurationFor(bSwingHasTarget);
    SwingElapsed += FMath::Max(0.0f, DeltaSeconds);
    const float Progress = FMath::Clamp(SwingElapsed / Duration, 0.0f, 1.0f);
    const FCMLViewmodelOffset Offset = FCMLEquipmentSwing::EvaluatePose(
        Progress, bSwingHasTarget, SwingTargetDistance);
    const FRotator RestRotation(-7.0f, -9.0f, 11.0f);
    const FVector SwingRest = FVector(72.0f, 26.0f, -44.0f)
        + RestRotation.RotateVector(FVector(0.0f, 0.0f, -10.36f));
    PickaxeSwingRoot->SetRelativeLocation(SwingRest + Offset.Position);
    // Unity composes swing * rest. Keep that order: reversing it makes the
    // pickaxe twist around a camera-space axis during the strike.
    PickaxeSwingRoot->SetRelativeRotation(
        Offset.Rotation.Quaternion() * RestRotation.Quaternion());

    if (!bSwingImpactSubmitted
        && FCMLEquipmentSwing::CrossedImpact(PreviousSwingProgress, Progress, bSwingHasTarget))
    {
        bSwingImpactSubmitted = true;
        APlayerController* PlayerController = Cast<APlayerController>(Controller);
        const ACMLHUD* HUD = PlayerController != nullptr
            ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
        if (UCMLGameplayTargetComponent* Target = SwingTarget.Get())
        {
            Target->PlayImpactPresentation(
                SwingImpactPoint, SwingImpactNormal,
                FirstPersonCamera != nullptr
                    ? FirstPersonCamera->GetComponentLocation() : GetActorLocation());
            Target->TryPrimaryAction(HUD != nullptr ? HUD->GetSelectedHotbarIndex() : 0);
        }
    }
    PreviousSwingProgress = Progress;

    if (Progress >= 1.0f)
    {
        bSwinging = false;
        SwingTarget.Reset();
        PickaxeSwingRoot->SetRelativeLocation(SwingRest);
        PickaxeSwingRoot->SetRelativeRotation(FRotator(-7.0f, -9.0f, 11.0f));
    }
}

AActor* ACMLPlayerCharacter::FindAirshipActor(const FCMLStableId& AirshipId) const
{
    UWorld* World = GetWorld();
    if (World == nullptr || AirshipId.IsNone())
    {
        return nullptr;
    }
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (Actor == nullptr)
        {
            continue;
        }
        TArray<UCMLGameplayTargetComponent*> Targets;
        Actor->GetComponents(Targets);
        for (const UCMLGameplayTargetComponent* Target : Targets)
        {
            if (Target != nullptr && Target->GetSourceId() == AirshipId
                && (Target->GetTargetKind() == ECMLGameplayTargetKind::AirshipRepair
                    || Target->GetTargetKind() == ECMLGameplayTargetKind::AirshipPilotStation))
            {
                return Actor;
            }
        }
    }
    return nullptr;
}

void ACMLPlayerCharacter::UpdatePiloting()
{
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation = World != nullptr
        ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    if (Simulation == nullptr)
    {
        return;
    }

    FCMLStableId CurrentAirshipId;
    const bool bNowPiloting = Simulation->GetLocalPilotedAirship(CurrentAirshipId);
    if (bNowPiloting && !bPiloting)
    {
        bPiloting = true;
        PilotedAirshipId = CurrentAirshipId;
        PilotedAirshipActor = FindAirshipActor(CurrentAirshipId);
        if (AActor* AirshipActor = PilotedAirshipActor.Get())
        {
            USceneComponent* PilotCameraMarker = nullptr;
            TArray<USceneComponent*> Components;
            AirshipActor->GetComponents(Components);
            for (USceneComponent* Component : Components)
            {
                if (Component != nullptr
                    && Component->GetName().Contains(TEXT("REF_PilotCamera")))
                {
                    PilotCameraMarker = Component;
                    break;
                }
            }
            const FTransform PilotTransform = PilotCameraMarker != nullptr
                ? PilotCameraMarker->GetComponentTransform()
                : FTransform(
                    AirshipActor->GetActorRotation(),
                    AirshipActor->GetComponentsBoundingBox(true).GetCenter()
                        + FVector(0.0f, 0.0f, 180.0f));
            SetActorLocationAndRotation(
                PilotTransform.GetLocation(), PilotTransform.GetRotation(), false);
            AttachToActor(AirshipActor, FAttachmentTransformRules::KeepWorldTransform);
            if (Controller != nullptr)
            {
                Controller->SetControlRotation(PilotTransform.Rotator());
            }
        }
        GetCharacterMovement()->StopMovementImmediately();
        GetCharacterMovement()->DisableMovement();
        GetCapsuleComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
        {
            if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
            {
                HUD->PushCollectionFeed(
                    TEXT("Comandi aeronave: W/S velocità, Spazio/Shift quota, mouse rotta, E lascia i comandi"));
            }
        }
    }
    else if (!bNowPiloting && bPiloting)
    {
        bPiloting = false;
        PilotThrottleInput = 0.0f;
        PilotLiftInput = 0.0f;
        PilotYawInput = 0.0f;
        PilotPitchInput = 0.0f;
        LastPilotInputTick = MAX_uint64;
        GetCapsuleComponent()->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        GetCharacterMovement()->SetMovementMode(MOVE_Walking);
        PilotedAirshipId = FCMLStableId::None();
        if (APlayerController* PlayerController = Cast<APlayerController>(Controller))
        {
            if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
            {
                HUD->PushCollectionFeed(TEXT("Comandi aeronave lasciati"));
            }
        }
    }

    if (!bPiloting)
    {
        return;
    }
    const uint64 PublishedTick = Simulation->GetPublishedState().Tick.Value;
    if (LastPilotInputTick == PublishedTick)
    {
        return;
    }
    LastPilotInputTick = PublishedTick;
    Simulation->RequestAirshipPilotInput(
        PilotedAirshipId,
        FMath::RoundToInt(PilotThrottleInput * 1000.0f),
        FMath::RoundToInt(PilotLiftInput * 1000.0f),
        FMath::RoundToInt(PilotYawInput),
        FMath::RoundToInt(PilotPitchInput));
    PilotYawInput = 0.0f;
    PilotPitchInput = 0.0f;
}

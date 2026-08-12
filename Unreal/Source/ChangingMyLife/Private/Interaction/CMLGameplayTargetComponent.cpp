#include "Interaction/CMLGameplayTargetComponent.h"
#include "Interaction/CMLTreeRuntimeMeshComponent.h"

#include "Engine/World.h"
#include "Engine/StaticMesh.h"
#include "Components/BoxComponent.h"
#include "Components/PrimitiveComponent.h"
#include "Components/StaticMeshComponent.h"
#include "GameFramework/Actor.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/Pawn.h"
#include "Content/CMLContentIds.h"
#include "Materials/MaterialInterface.h"
#include "PhysicsEngine/BodyInstance.h"
#include "Presentation/CMLImpactBurstActor.h"
#include "Presentation/CMLTreeChopOpening.h"
#include "Presentation/CMLTreeFellGeometry.h"
#include "ProceduralMeshComponent.h"
#include "StaticMeshResources.h"
#include "UI/CMLHUD.h"
#include "HAL/PlatformTime.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLTreeChop, Log, All);

namespace
{
    UStaticMeshComponent* FindTreeTrunk(AActor& Actor)
    {
        TArray<UStaticMeshComponent*> Meshes;
        Actor.GetComponents(Meshes);
        UStaticMeshComponent* Best = nullptr;
        double BestScore = -DBL_MAX;
        for (UStaticMeshComponent* Mesh : Meshes)
        {
            if (Mesh == nullptr || Mesh->GetStaticMesh() == nullptr)
            {
                continue;
            }
            const FString Identity = (Mesh->GetName() + TEXT("|")
                + Mesh->GetStaticMesh()->GetName()).ToLower();
            if (Identity.Contains(TEXT("canopy")) || Identity.Contains(TEXT("fringe"))
                || Identity.Contains(TEXT("leaves")) || Identity.Contains(TEXT("leaf")))
            {
                continue;
            }
            const FVector Extent = Mesh->Bounds.BoxExtent;
            const double Slenderness = Extent.Z
                / FMath::Max(1.0, FMath::Max(Extent.X, Extent.Y));
            const double NameBonus = Identity.Contains(TEXT("trunk"))
                || Identity.Contains(TEXT("bark")) ? 10000.0 : 0.0;
            const double Score = NameBonus + Slenderness * 100.0 + Extent.Z;
            if (Score > BestScore)
            {
                Best = Mesh;
                BestScore = Score;
            }
        }
        return Best;
    }

    FCMLStableId StableSourceIdFor(const AActor& Actor)
    {
        // FNV-1a over a map-stable description. The high word is a dedicated
        // runtime-source namespace and can never collide with published content ids.
        const FVector Location = Actor.GetActorLocation();
        const FString Seed = FString::Printf(
            TEXT("%s|%lld|%lld|%lld"),
            *Actor.GetPathName(),
            FMath::RoundToInt64(Location.X),
            FMath::RoundToInt64(Location.Y),
            FMath::RoundToInt64(Location.Z));
        uint64 Hash = 1469598103934665603ULL;
        for (const TCHAR Character : Seed)
        {
            Hash ^= static_cast<uint64>(Character);
            Hash *= 1099511628211ULL;
        }
        return FCMLStableId(0x7000000000000000ULL, Hash == 0 ? 1 : Hash);
    }

    struct FChopVertex
    {
        FVector Position = FVector::ZeroVector;
        FVector Normal = FVector::UpVector;
        FVector TangentX = FVector::ForwardVector;
        FVector TangentY = FVector::RightVector;
        FVector2D UV0 = FVector2D::ZeroVector;
        FVector2D UV1 = FVector2D::ZeroVector;
        FVector2D UV2 = FVector2D::ZeroVector;
        FVector2D UV3 = FVector2D::ZeroVector;
        FLinearColor Colour = FLinearColor::White;
    };

    struct FChopOpeningFrame
    {
        FVector Centre = FVector::ZeroVector;
        FVector Normal = FVector::ForwardVector;
        FVector Right = FVector::RightVector;
        FVector Up = FVector::UpVector;
        float Width = 0.0f;
        float Height = 0.0f;
        float Depth = 0.0f;
        float HalfWidth = 0.0f;
        float HalfHeight = 0.0f;
        float TargetPitch = 0.0f;
    };

    float SmoothThreshold(const float Edge0, const float Edge1, const float Value)
    {
        return FMath::SmoothStep(
            0.0f, 1.0f,
            FMath::Clamp((Value - Edge0) / FMath::Max(SMALL_NUMBER, Edge1 - Edge0),
                0.0f, 1.0f));
    }

    float NormalisedChopRadius(const float X, const float Y)
    {
        // Exact torn, vertically oriented outline used by Unity's voxel field.
        static constexpr float SectorRadii[] = {
            0.92f, 0.98f, 1.01f, 1.00f, 0.96f, 0.91f, 1.00f, 1.02f,
            0.99f, 1.00f, 0.97f, 1.06f, 0.94f, 1.07f, 0.99f, 0.96f};
        constexpr int32 SectorCount = UE_ARRAY_COUNT(SectorRadii);
        const float ShiftedX = X
            - FMath::Sin(Y * 5.7f + 0.35f) * 0.042f
            - FMath::Sin(Y * 11.3f - 0.4f) * 0.018f;
        const float ShapedY = Y * (1.0f + SmoothThreshold(0.55f, 1.0f, -Y) * 0.12f);
        const float Angle = FMath::Atan2(ShapedY, ShiftedX);
        const float SectorFloat = FMath::Clamp(
            (Angle + UE_PI) * (static_cast<float>(SectorCount) / UE_TWO_PI),
            0.0f, static_cast<float>(SectorCount) - 0.0001f);
        const int32 Sector = FMath::FloorToInt(SectorFloat);
        const int32 Next = (Sector + 1) % SectorCount;
        const float Limit = FMath::Lerp(
            SectorRadii[Sector], SectorRadii[Next], SectorFloat - Sector);
        return FMath::Sqrt(ShiftedX * ShiftedX + ShapedY * ShapedY) / Limit;
    }

    float ChiselDepthFactor(const float X, const float Y, const float Radius)
    {
        const float Interior = FMath::Clamp((0.84f - Radius) / 0.79f, 0.0f, 1.0f);
        float Depth = FMath::Lerp(0.16f, 0.24f, FMath::Pow(Interior, 1.25f));
        struct FScale { float X, Y, Length, Width, Angle, Depth; };
        static const FScale Scales[] = {
            {-0.165f, 0.417f, 0.362f, 0.116f, 18.0f, 0.273f},
            { 0.132f, 0.139f, 0.428f, 0.139f, 22.0f, 0.491f},
            {-0.099f,-0.185f, 0.329f, 0.104f, 26.0f, 0.709f},
            { 0.198f,-0.509f, 0.264f, 0.093f, 20.0f, 0.964f}};
        for (const FScale& Scale : Scales)
        {
            const float Radians = FMath::DegreesToRadians(Scale.Angle);
            const float Dx = X - Scale.X;
            const float Dy = Y - Scale.Y;
            const float Along = Dx * FMath::Cos(Radians) + Dy * FMath::Sin(Radians);
            const float Across = -Dx * FMath::Sin(Radians) + Dy * FMath::Cos(Radians)
                + FMath::Sin(Along * 17.0f + Scale.Y * 9.0f) * 0.018f;
            const float Distance = FMath::Max(
                FMath::Abs(Along) / Scale.Length,
                FMath::Abs(Across) / Scale.Width);
            const float Mask = 1.0f - SmoothThreshold(0.70f, 1.0f, Distance);
            Depth = FMath::Max(Depth, FMath::Lerp(
                FMath::Lerp(0.16f, 0.24f, FMath::Pow(Interior, 1.25f)),
                Scale.Depth, Mask));
        }
        return FMath::Clamp(
            Depth + FMath::Sin(X * 9.3f - Y * 5.2f + 0.6f)
                * SmoothThreshold(0.18f, 0.70f, Interior) * 0.012f,
            0.27f, 0.96f);
    }

    bool TriangleTouchesOpening(
        const FChopVertex& A,
        const FChopVertex& B,
        const FChopVertex& C,
        const FTransform& ComponentTransform,
        const FVector& Centre,
        const FVector& Normal,
        const FVector& Right,
        const FVector& Up,
        const float HalfWidth,
        const float HalfHeight,
        const float Depth)
    {
        float MinX = BIG_NUMBER, MaxX = -BIG_NUMBER;
        float MinY = BIG_NUMBER, MaxY = -BIG_NUMBER;
        float MinN = BIG_NUMBER, MaxN = -BIG_NUMBER;
        const FChopVertex Vertices[] = {A, B, C};
        for (const FChopVertex& Vertex : Vertices)
        {
            const FVector Delta = ComponentTransform.TransformPosition(Vertex.Position) - Centre;
            const float X = FVector::DotProduct(Delta, Right);
            const float Y = FVector::DotProduct(Delta, Up);
            const float N = FVector::DotProduct(Delta, Normal);
            MinX = FMath::Min(MinX, X); MaxX = FMath::Max(MaxX, X);
            MinY = FMath::Min(MinY, Y); MaxY = FMath::Max(MaxY, Y);
            MinN = FMath::Min(MinN, N); MaxN = FMath::Max(MaxN, N);
        }
        return MinX <= HalfWidth && MaxX >= -HalfWidth
            && MinY <= HalfHeight && MaxY >= -HalfHeight
            && MinN <= Depth * 2.0f && MaxN >= -Depth * 2.0f;
    }

    FChopVertex Midpoint(const FChopVertex& A, const FChopVertex& B)
    {
        FChopVertex Result;
        Result.Position = (A.Position + B.Position) * 0.5f;
        Result.Normal = (A.Normal + B.Normal).GetSafeNormal();
        Result.TangentX = (A.TangentX + B.TangentX).GetSafeNormal();
        Result.TangentY = (A.TangentY + B.TangentY).GetSafeNormal();
        Result.UV0 = (A.UV0 + B.UV0) * 0.5f;
        Result.UV1 = (A.UV1 + B.UV1) * 0.5f;
        Result.UV2 = (A.UV2 + B.UV2) * 0.5f;
        Result.UV3 = (A.UV3 + B.UV3) * 0.5f;
        Result.Colour = (A.Colour + B.Colour) * 0.5f;
        return Result;
    }

    uint64 ChopEdgeKey(const int32 A, const int32 B)
    {
        const uint32 Minimum = static_cast<uint32>(FMath::Min(A, B));
        const uint32 Maximum = static_cast<uint32>(FMath::Max(A, B));
        return (static_cast<uint64>(Minimum) << 32) | Maximum;
    }

    bool TryBarycentricAtOrigin(
        const FVector2D& A,
        const FVector2D& B,
        const FVector2D& C,
        FVector& OutWeights)
    {
        const double Denominator =
            (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
        if (FMath::Abs(Denominator) < 0.000001)
        {
            return false;
        }
        const double WeightA = (B.X * C.Y - C.X * B.Y) / Denominator;
        const double WeightB = (C.X * A.Y - A.X * C.Y) / Denominator;
        const double WeightC = 1.0 - WeightA - WeightB;
        OutWeights = FVector(WeightA, WeightB, WeightC);
        constexpr double Tolerance = -0.0001;
        return WeightA >= Tolerance && WeightB >= Tolerance && WeightC >= Tolerance;
    }
}

UCMLGameplayTargetComponent::UCMLGameplayTargetComponent()
{
    PrimaryComponentTick.bCanEverTick = true;
    PrimaryComponentTick.bStartWithTickEnabled = false;
}

void UCMLGameplayTargetComponent::BeginPlay()
{
    Super::BeginPlay();
    if (SourceId.IsNone() && GetOwner() != nullptr)
    {
        SourceId = StableSourceIdFor(*GetOwner());
    }
    if (UWorld* World = GetWorld())
    {
        if (UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>())
        {
            Simulation->OnRuntimeCommandResolved.AddUObject(
                this, &UCMLGameplayTargetComponent::HandleCommandResolved);
        }
    }
    if (TargetKind == ECMLGameplayTargetKind::AirshipRepair
        || TargetKind == ECMLGameplayTargetKind::AirshipPilotStation)
    {
        SetComponentTickEnabled(true);
    }
}

void UCMLGameplayTargetComponent::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
    StopImpactShake();
    if (FallingTreeHost != nullptr)
    {
        FallingTreeHost->Destroy();
        FallingTreeHost = nullptr;
        FallingTreeBody = nullptr;
    }
    if (UWorld* World = GetWorld())
    {
        if (UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>())
        {
            Simulation->OnRuntimeCommandResolved.RemoveAll(this);
        }
    }
    Super::EndPlay(EndPlayReason);
}

void UCMLGameplayTargetComponent::Configure(
    const ECMLGameplayTargetKind InKind,
    const FCMLStableId& InSourceId,
    const int32 InYield)
{
    TargetKind = InKind;
    SourceId = InSourceId;
    Yield = FMath::Max(1, InYield);
}

void UCMLGameplayTargetComponent::ConfigureInteractionAnchor(UPrimitiveComponent* InAnchor)
{
    InteractionAnchor = InAnchor;
}

UPrimitiveComponent* UCMLGameplayTargetComponent::GetInteractionAnchor() const
{
    return InteractionAnchor.Get();
}

bool UCMLGameplayTargetComponent::MatchesInteractionComponent(
    const UPrimitiveComponent* Component) const
{
    const UPrimitiveComponent* Anchor = InteractionAnchor.Get();
    return Anchor == nullptr || Component == Anchor;
}

FBox UCMLGameplayTargetComponent::GetInteractionBounds() const
{
    if (const UPrimitiveComponent* Anchor = InteractionAnchor.Get())
    {
        return Anchor->Bounds.GetBox();
    }
    if (const AActor* Owner = GetOwner())
    {
        return Owner->GetComponentsBoundingBox(true);
    }
    return FBox(EForceInit::ForceInit);
}

void UCMLGameplayTargetComponent::ConfigureHingedPart(
    USceneComponent* InPart,
    const FRotator& InClosedRelativeRotation,
    const FRotator& InOpenRelativeRotation,
    const bool bStartsOpen,
    const float InDurationSeconds)
{
    HingedPart = InPart;
    HingeClosedRotation = InClosedRelativeRotation.Quaternion();
    HingeOpenRotation = InOpenRelativeRotation.Quaternion();
    HingeDurationSeconds = FMath::Max(0.05f, InDurationSeconds);
    HingeAlpha = bStartsOpen ? 1.0f : 0.0f;
    bHingeWantsOpen = bStartsOpen;
    if (InPart != nullptr)
    {
        InPart->SetRelativeRotation(FQuat::Slerp(
            HingeClosedRotation, HingeOpenRotation, HingeAlpha));
        SetComponentTickEnabled(true);
    }
}

void UCMLGameplayTargetComponent::SetHingedOpen(const bool bOpen)
{
    bHingeWantsOpen = bOpen;
    if (HingedPart.IsValid())
    {
        SetComponentTickEnabled(true);
    }
}

void UCMLGameplayTargetComponent::ToggleHingedOpen()
{
    SetHingedOpen(!bHingeWantsOpen);
}

ECMLHandGatherTarget UCMLGameplayTargetComponent::AsHandGatherTarget() const
{
    switch (TargetKind)
    {
    case ECMLGameplayTargetKind::WildFiberTuft:
        return ECMLHandGatherTarget::WildFiberTuft;
    case ECMLGameplayTargetKind::FallenSticks:
        return ECMLHandGatherTarget::FallenSticks;
    case ECMLGameplayTargetKind::LoosePebble:
        return ECMLHandGatherTarget::LoosePebble;
    default:
        return ECMLHandGatherTarget::None;
    }
}

ECMLMiningTarget UCMLGameplayTargetComponent::AsMiningTarget() const
{
    switch (TargetKind)
    {
    case ECMLGameplayTargetKind::EnvironmentalStone:
        return ECMLMiningTarget::EnvironmentalStone;
    case ECMLGameplayTargetKind::IronOreRock:
        return ECMLMiningTarget::IronOreRock;
    case ECMLGameplayTargetKind::IronDepositSurface:
        return ECMLMiningTarget::IronDepositSurface;
    case ECMLGameplayTargetKind::CopperOreRock:
        return ECMLMiningTarget::CopperOreRock;
    case ECMLGameplayTargetKind::CopperDepositSurface:
        return ECMLMiningTarget::CopperDepositSurface;
    case ECMLGameplayTargetKind::TinOreRock:
        return ECMLMiningTarget::TinOreRock;
    case ECMLGameplayTargetKind::TinDepositSurface:
        return ECMLMiningTarget::TinDepositSurface;
    default:
        return ECMLMiningTarget::None;
    }
}

bool UCMLGameplayTargetComponent::IsInteractionAvailable_Implementation() const
{
    return !bCommitted && !PendingCommand.IsValid()
        && (AsHandGatherTarget() != ECMLHandGatherTarget::None
            || TargetKind == ECMLGameplayTargetKind::Workbench
            || TargetKind == ECMLGameplayTargetKind::WoodenCrate
            || TargetKind == ECMLGameplayTargetKind::CrudeFurnace
            || TargetKind == ECMLGameplayTargetKind::MechanicalPress
             || TargetKind == ECMLGameplayTargetKind::MechanicalDrill
             || TargetKind == ECMLGameplayTargetKind::AirshipRepair
             || TargetKind == ECMLGameplayTargetKind::AirshipPilotStation
             || TargetKind == ECMLGameplayTargetKind::AirshipDoor);
}

FText UCMLGameplayTargetComponent::GetInteractionPrompt_Implementation() const
{
    switch (TargetKind)
    {
    case ECMLGameplayTargetKind::WildFiberTuft:
    case ECMLGameplayTargetKind::FallenSticks:
    case ECMLGameplayTargetKind::LoosePebble:
        return NSLOCTEXT("CML", "GatherPrompt", "RACCOGLI");
    case ECMLGameplayTargetKind::Workbench:
        return NSLOCTEXT("CML", "WorkbenchPrompt", "USA BANCO DA LAVORO");
    case ECMLGameplayTargetKind::WoodenCrate:
        return NSLOCTEXT("CML", "CratePrompt", "APRI CASSA");
    case ECMLGameplayTargetKind::CrudeFurnace:
        return NSLOCTEXT("CML", "FurnacePrompt", "USA FORNACE");
    case ECMLGameplayTargetKind::MechanicalPress:
        return NSLOCTEXT("CML", "PressPrompt", "USA PRESSA");
    case ECMLGameplayTargetKind::MechanicalDrill:
        return NSLOCTEXT("CML", "DrillPrompt", "USA ESTRATTORE");
    case ECMLGameplayTargetKind::AirshipRepair:
    case ECMLGameplayTargetKind::AirshipPilotStation:
        if (const UWorld* World = GetWorld())
        {
            if (const UCMLSimulationSubsystem* Simulation =
                    World->GetSubsystem<UCMLSimulationSubsystem>())
            {
                FCMLAirshipEntityState Airship;
                if (Simulation->GetAirshipState(SourceId, Airship)
                    && Airship.RepairStatus == ECMLAirshipRepairStatus::Repaired)
                {
                    return NSLOCTEXT("CML", "PilotReadyPrompt", "PILOTA AERONAVE");
                }
            }
        }
        return NSLOCTEXT("CML", "RepairPrompt", "ISPEZIONA AERONAVE");
    case ECMLGameplayTargetKind::AirshipDoor:
        return bHingeWantsOpen
            ? NSLOCTEXT("CML", "CloseAirshipDoorPrompt", "CHIUDI PORTA")
            : NSLOCTEXT("CML", "OpenAirshipDoorPrompt", "APRI PORTA");
    default:
        return NSLOCTEXT("CML", "UsePrompt", "INTERAGISCI");
    }
}

bool UCMLGameplayTargetComponent::TryInteract_Implementation()
{
    if (!IsInteractionAvailable_Implementation())
    {
        return false;
    }
    const ECMLHandGatherTarget GatherTarget = AsHandGatherTarget();
    if (GatherTarget == ECMLHandGatherTarget::None)
    {
        if (TargetKind == ECMLGameplayTargetKind::Workbench)
        {
            if (const UWorld* World = GetWorld())
            {
                if (APlayerController* PlayerController = World->GetFirstPlayerController())
                {
                    if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
                    {
                        HUD->OpenCraftingPanel(
                            TEXT("B A N C O   D A   L A V O R O"),
                            ECMLCraftingStationKind::Workbench);
                        return true;
                    }
                }
            }
        }
        if (TargetKind == ECMLGameplayTargetKind::WoodenCrate)
        {
            if (const UWorld* World = GetWorld())
            {
                if (APlayerController* PlayerController = World->GetFirstPlayerController())
                {
                    if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
                    {
                        SetHingedOpen(true);
                        HUD->OpenStorageNode(SourceId, TEXT("CASSA DI LEGNO"));
                        return true;
                    }
                }
            }
        }
        if (TargetKind == ECMLGameplayTargetKind::AirshipDoor)
        {
            ToggleHingedOpen();
            return true;
        }
        if (TargetKind == ECMLGameplayTargetKind::CrudeFurnace
            || TargetKind == ECMLGameplayTargetKind::MechanicalPress
            || TargetKind == ECMLGameplayTargetKind::MechanicalDrill)
        {
            if (const UWorld* World = GetWorld())
            {
                if (APlayerController* PlayerController = World->GetFirstPlayerController())
                {
                    if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
                    {
                        HUD->OpenMachineNode(SourceId);
                        return true;
                    }
                }
            }
        }
        if (TargetKind == ECMLGameplayTargetKind::AirshipRepair
            || TargetKind == ECMLGameplayTargetKind::AirshipPilotStation)
        {
            UWorld* World = GetWorld();
            UCMLSimulationSubsystem* Simulation = World != nullptr
                ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
            if (Simulation == nullptr)
            {
                return false;
            }
            FCMLAirshipEntityState Airship;
            if (!Simulation->GetAirshipState(SourceId, Airship))
            {
                return false;
            }
            if (Airship.RepairStatus != ECMLAirshipRepairStatus::Repaired)
            {
                if (APlayerController* PlayerController = World->GetFirstPlayerController())
                {
                    if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
                    {
                        HUD->OpenAirshipRepair(SourceId);
                        return true;
                    }
                }
                return false;
            }
            FCMLRuntimeCommandHandle Handle;
            return Simulation->RequestAirshipPilotBegin(SourceId, Handle);
        }
        return false;
    }
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation =
        World != nullptr ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    return Simulation != nullptr
        && Simulation->RequestHandGather(SourceId, GatherTarget, Yield, PendingCommand);
}

bool UCMLGameplayTargetComponent::TryPrimaryAction(const int32 EquippedSlotIndex)
{
    if (bCommitted || PendingCommand.IsValid())
    {
        return false;
    }
    const ECMLMiningTarget MiningTarget = AsMiningTarget();
    if (MiningTarget == ECMLMiningTarget::None
        && TargetKind != ECMLGameplayTargetKind::FellableTree)
    {
        return false;
    }
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation =
        World != nullptr ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    if (Simulation == nullptr)
    {
        return false;
    }
    return TargetKind == ECMLGameplayTargetKind::FellableTree
        ? Simulation->RequestTreeImpact(SourceId, EquippedSlotIndex, PendingCommand)
        : Simulation->RequestMiningImpact(
            SourceId, MiningTarget, EquippedSlotIndex, PendingCommand);
}

void UCMLGameplayTargetComponent::PlayImpactPresentation(
    const FVector& ImpactPoint,
    const FVector& ImpactNormal,
    const FVector& ViewOrigin)
{
    if (bCommitted || GetWorld() == nullptr)
    {
        return;
    }
    const bool bWood = TargetKind == ECMLGameplayTargetKind::FellableTree;
    if (!bWood && AsMiningTarget() == ECMLMiningTarget::None)
    {
        return;
    }
    LastImpactPoint = ImpactPoint;
    LastImpactNormal = ImpactNormal.GetSafeNormal(UE_SMALL_NUMBER, FVector::UpVector);
    LastViewOrigin = ViewOrigin;
    if (bWood)
    {
        if (UStaticMeshComponent* Trunk = FindTreeTrunk(*GetOwner()))
        {
            TreeSourceTrunk = Trunk;
            const FVector Direction = (ImpactPoint - ViewOrigin).GetSafeNormal(
                UE_SMALL_NUMBER, -LastImpactNormal);
            FHitResult TrunkHit;
            FCollisionQueryParams Params(SCENE_QUERY_STAT(CMLTreeSurfaceImpact), true, GetOwner());
            if (Trunk->LineTraceComponent(
                    TrunkHit, ViewOrigin, ImpactPoint + Direction * 250.0f, Params))
            {
                LastImpactPoint = TrunkHit.ImpactPoint;
                LastImpactNormal = TrunkHit.ImpactNormal.GetSafeNormal(
                    UE_SMALL_NUMBER, -Direction);
            }
            else
            {
                FVector ClosestPoint;
                if (Trunk->GetClosestPointOnCollision(ImpactPoint, ClosestPoint) >= 0.0f)
                {
                    LastImpactPoint = ClosestPoint;
                    const FVector Radial = FVector::VectorPlaneProject(
                        ClosestPoint - Trunk->Bounds.Origin, GetOwner()->GetActorUpVector());
                    LastImpactNormal = Radial.GetSafeNormal(
                        UE_SMALL_NUMBER, -Direction);
                }
            }
        }
    }

    FActorSpawnParameters SpawnParameters;
    SpawnParameters.SpawnCollisionHandlingOverride =
        ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    if (ACMLImpactBurstActor* Burst = GetWorld()->SpawnActor<ACMLImpactBurstActor>(
        ACMLImpactBurstActor::StaticClass(), LastImpactPoint, FRotator::ZeroRotator, SpawnParameters))
    {
        Burst->Initialise(
            LastImpactPoint,
            LastImpactNormal,
            bWood ? ECMLImpactSurface::Wood : ECMLImpactSurface::Stone);
    }

    // The infinite ore floor is an extraction trigger, not a movable rock.
    const bool bInfiniteDepositSurface =
        TargetKind == ECMLGameplayTargetKind::IronDepositSurface
        || TargetKind == ECMLGameplayTargetKind::CopperDepositSurface
        || TargetKind == ECMLGameplayTargetKind::TinDepositSurface;
    if (!bInfiniteDepositSurface)
    {
        StartImpactShake(LastImpactPoint, ViewOrigin, bWood);
    }
}

void UCMLGameplayTargetComponent::StartImpactShake(
    const FVector& ImpactPoint,
    const FVector& ViewOrigin,
    const bool bWood)
{
    StopImpactShake();
    AActor* Owner = GetOwner();
    if (Owner == nullptr)
    {
        return;
    }
    ImpactShakeWorldDirection = (ImpactPoint - ViewOrigin).GetSafeNormal(
        UE_SMALL_NUMBER, -LastImpactNormal);
    ImpactShakeWorldSide = FVector::CrossProduct(
        FVector::UpVector, ImpactShakeWorldDirection).GetSafeNormal(
            UE_SMALL_NUMBER, FVector::RightVector);
    ImpactShakeDuration = bWood ? 0.18f : 0.14f;
    ImpactShakeTravel = bWood ? 1.4f : 2.8f;
    ImpactShakeRotation = bWood ? 0.28f : 0.8f;
    ImpactShakeFrequency = bWood ? 20.0f : 28.0f;
    ImpactShakeDirection = -ImpactShakeDirection;
    ImpactShakeElapsed = 0.0f;

    TArray<UPrimitiveComponent*> Primitives;
    Owner->GetComponents(Primitives);
    for (UPrimitiveComponent* Primitive : Primitives)
    {
        if (Primitive == nullptr || !Primitive->IsVisible())
        {
            continue;
        }
        FShakenPrimitiveState State;
        State.Primitive = Primitive;
        State.RelativeTransform = Primitive->GetRelativeTransform();
        ShakenPrimitives.Add(State);
    }
    SetComponentTickEnabled(ShakenPrimitives.Num() > 0);
}

void UCMLGameplayTargetComponent::StopImpactShake()
{
    for (const FShakenPrimitiveState& State : ShakenPrimitives)
    {
        if (UPrimitiveComponent* Primitive = State.Primitive.Get())
        {
            Primitive->SetRelativeTransform(State.RelativeTransform);
        }
    }
    ShakenPrimitives.Reset();
    ImpactShakeElapsed = BIG_NUMBER;
}

void UCMLGameplayTargetComponent::HandleCommandResolved(
    const FCMLSimulationCommand& Command,
    const bool bSucceeded,
    const bool bWorldCommitted)
{
    if (!PendingCommand.IsValid()
        || PendingCommand.Tick.Value != Command.TargetTick.Value
        || PendingCommand.Sequence != Command.Sequence)
    {
        return;
    }
    PendingCommand = FCMLRuntimeCommandHandle();
    if (bSucceeded)
    {
        if (TargetKind == ECMLGameplayTargetKind::FellableTree)
        {
            TreeHitStage = FMath::Min(TreeHitStage + 1, FCMLTreeChop::HitsRequired);
            CommitTreeOpening();
            ApplyTreeNotch();
            if (bWorldCommitted)
            {
                BeginTreeFall();
            }
            return;
        }
        if (bWorldCommitted)
        {
            if (TargetKind == ECMLGameplayTargetKind::IronDepositSurface
                || TargetKind == ECMLGameplayTargetKind::CopperDepositSurface
                || TargetKind == ECMLGameplayTargetKind::TinDepositSurface)
            {
                ShowCollectionFeed();
            }
            else
            {
                CommitWorldRemoval();
            }
        }
    }
}

void UCMLGameplayTargetComponent::CommitTreeOpening()
{
    AActor* Owner = GetOwner();
    UStaticMeshComponent* SourceTrunk = TreeSourceTrunk.Get();
    if (SourceTrunk == nullptr && Owner != nullptr)
    {
        SourceTrunk = FindTreeTrunk(*Owner);
        TreeSourceTrunk = SourceTrunk;
    }
    if (Owner == nullptr || SourceTrunk == nullptr)
    {
        return;
    }

    FVector SurfaceNormal = LastImpactNormal.GetSafeNormal(
        UE_SMALL_NUMBER, -Owner->GetActorForwardVector());
    FVector SurfaceUp = FVector::VectorPlaneProject(
        Owner->GetActorUpVector(), SurfaceNormal).GetSafeNormal();
    if (SurfaceUp.IsNearlyZero())
    {
        SurfaceUp = FVector::VectorPlaneProject(
            FVector::UpVector, SurfaceNormal).GetSafeNormal();
    }
    if (SurfaceUp.IsNearlyZero())
    {
        SurfaceUp = FVector::VectorPlaneProject(
            FVector::RightVector, SurfaceNormal).GetSafeNormal();
    }
    const FVector BaseRight = FVector::CrossProduct(
        SurfaceUp, SurfaceNormal).GetSafeNormal();
    const FQuat Tilt(SurfaceNormal, FMath::DegreesToRadians(8.0f));
    const FVector SurfaceRight = Tilt.RotateVector(BaseRight).GetSafeNormal();
    SurfaceUp = Tilt.RotateVector(SurfaceUp).GetSafeNormal();

    // Unity's local collider probe measures the section at the actual impact.
    // The authored bounds are a reliable equivalent for these tapered but
    // nearly vertical CloudTall trunks and, importantly, preserve scale.
    const float SectionWidthMetres = FMath::Min(
        SourceTrunk->Bounds.BoxExtent.X, SourceTrunk->Bounds.BoxExtent.Y) * 0.02f;
    const FTransform TrunkTransform = SourceTrunk->GetComponentTransform();

    int32 ExistingOpening = INDEX_NONE;
    float BestDistance = BIG_NUMBER;
    for (int32 Index = 0; Index < TreeOpenings.Num(); ++Index)
    {
        const FTreeOpeningState& OpeningState = TreeOpenings[Index];
        const FVector OpeningCentre = TrunkTransform.TransformPosition(
            OpeningState.CentreLocal);
        const FVector OpeningNormal = TrunkTransform.TransformVectorNoScale(
            OpeningState.NormalLocal).GetSafeNormal();
        if (FVector::DotProduct(OpeningNormal, SurfaceNormal) < -0.2f)
        {
            continue;
        }
        const FCMLTreeChopOpening Opening = FCMLTreeChop::ResolveOpening(
            OpeningState.SectionWidthMetres, OpeningState.Stage);
        const FVector Delta = LastImpactPoint - OpeningCentre;
        if (FMath::Abs(FVector::DotProduct(Delta, OpeningNormal))
            > Opening.Depth * 100.0f + 4.5f)
        {
            continue;
        }
        const FVector OpeningRight = TrunkTransform.TransformVectorNoScale(
            OpeningState.RightLocal).GetSafeNormal();
        const FVector OpeningUp = TrunkTransform.TransformVectorNoScale(
            OpeningState.UpLocal).GetSafeNormal();
        const float Lateral = FVector::DotProduct(Delta, OpeningRight)
            / FMath::Max(0.01f, Opening.Width * 50.0f);
        const float Vertical = FVector::DotProduct(Delta, OpeningUp)
            / FMath::Max(0.01f, Opening.Height * 50.0f);
        const float Distance = FMath::Sqrt(Lateral * Lateral + Vertical * Vertical);
        if (Distance <= 1.25f && Distance < BestDistance)
        {
            ExistingOpening = Index;
            BestDistance = Distance;
        }
    }

    if (ExistingOpening != INDEX_NONE)
    {
        TreeOpenings[ExistingOpening].Stage = FMath::Min(
            TreeOpenings[ExistingOpening].Stage + 1, FCMLTreeChop::HitsRequired);
        return;
    }

    FTreeOpeningState& NewOpening = TreeOpenings.AddDefaulted_GetRef();
    NewOpening.CentreLocal = TrunkTransform.InverseTransformPosition(LastImpactPoint);
    NewOpening.NormalLocal = TrunkTransform.InverseTransformVectorNoScale(
        SurfaceNormal).GetSafeNormal();
    NewOpening.RightLocal = TrunkTransform.InverseTransformVectorNoScale(
        SurfaceRight).GetSafeNormal();
    NewOpening.UpLocal = TrunkTransform.InverseTransformVectorNoScale(
        SurfaceUp).GetSafeNormal();
    NewOpening.SectionWidthMetres = SectionWidthMetres;
    NewOpening.Stage = 1;
}

void UCMLGameplayTargetComponent::ApplyTreeNotch()
{
    const double RebuildStartedAt = FPlatformTime::Seconds();
    AActor* Owner = GetOwner();
    if (Owner == nullptr || TreeHitStage <= 0)
    {
        return;
    }
    UStaticMeshComponent* SourceTrunk = TreeSourceTrunk.Get();
    if (SourceTrunk == nullptr)
    {
        SourceTrunk = FindTreeTrunk(*Owner);
        TreeSourceTrunk = SourceTrunk;
    }
    UStaticMesh* StaticMesh = SourceTrunk != nullptr ? SourceTrunk->GetStaticMesh() : nullptr;
    const FStaticMeshRenderData* RenderData = StaticMesh != nullptr
        ? StaticMesh->GetRenderData() : nullptr;
    if (RenderData == nullptr || RenderData->LODResources.IsEmpty())
    {
        return;
    }
    const FStaticMeshLODResources& LOD = RenderData->LODResources[0];
    const FPositionVertexBuffer& Positions = LOD.VertexBuffers.PositionVertexBuffer;
    const FStaticMeshVertexBuffer& Attributes = LOD.VertexBuffers.StaticMeshVertexBuffer;
    const FColorVertexBuffer& SourceColours = LOD.VertexBuffers.ColorVertexBuffer;
    if (Positions.GetNumVertices() == 0 || LOD.IndexBuffer.GetNumIndices() < 3)
    {
        return;
    }

    const FTransform ComponentTransform = SourceTrunk->GetComponentTransform();

    TArray<FChopOpeningFrame> Openings;
    Openings.Reserve(TreeOpenings.Num());
    for (const FTreeOpeningState& State : TreeOpenings)
    {
        const FCMLTreeChopOpening Size = FCMLTreeChop::ResolveOpening(
            State.SectionWidthMetres, State.Stage);
        FChopOpeningFrame& Opening = Openings.AddDefaulted_GetRef();
        Opening.Centre = ComponentTransform.TransformPosition(State.CentreLocal);
        Opening.Normal = ComponentTransform.TransformVectorNoScale(
            State.NormalLocal).GetSafeNormal();
        Opening.Right = ComponentTransform.TransformVectorNoScale(
            State.RightLocal).GetSafeNormal();
        Opening.Up = ComponentTransform.TransformVectorNoScale(
            State.UpLocal).GetSafeNormal();
        Opening.Width = Size.Width * 100.0f;
        Opening.Height = Size.Height * 100.0f;
        Opening.Depth = Size.Depth * 100.0f;
        Opening.HalfWidth = Opening.Width * 0.5f;
        Opening.HalfHeight = Opening.Height * 0.5f;
        // Unity refines at 2.4% of each opening width, clamped to 1.25-4.5 mm.
        Opening.TargetPitch = FMath::Clamp(Opening.Width * 0.024f, 0.125f, 0.45f);
    }
    if (Openings.IsEmpty())
    {
        return;
    }

    const int32 IndexCount = LOD.IndexBuffer.GetNumIndices();
    TArray<FChopVertex> WorkingVertices;
    WorkingVertices.SetNum(Positions.GetNumVertices());
    for (uint32 VertexIndex = 0; VertexIndex < Positions.GetNumVertices(); ++VertexIndex)
    {
        FChopVertex& Vertex = WorkingVertices[VertexIndex];
        Vertex.Position = FVector(Positions.VertexPosition(VertexIndex));
        Vertex.Normal = FVector(Attributes.VertexTangentZ(VertexIndex));
        Vertex.TangentX = FVector(Attributes.VertexTangentX(VertexIndex));
        Vertex.TangentY = FVector(Attributes.VertexTangentY(VertexIndex));
        auto ReadUv = [&Attributes, VertexIndex](const uint32 Channel)
        {
            return Attributes.GetNumTexCoords() > Channel
                ? FVector2D(Attributes.GetVertexUV(VertexIndex, Channel))
                : FVector2D::ZeroVector;
        };
        Vertex.UV0 = ReadUv(0);
        Vertex.UV1 = ReadUv(1);
        Vertex.UV2 = ReadUv(2);
        Vertex.UV3 = ReadUv(3);
        Vertex.Colour = SourceColours.GetNumVertices() > VertexIndex
            ? SourceColours.VertexColor(VertexIndex).ReinterpretAsLinear()
            : FLinearColor::White;
    }

    TArray<int32> WorkingIndices;
    WorkingIndices.SetNum(IndexCount);
    for (int32 Index = 0; Index < IndexCount; ++Index)
    {
        WorkingIndices[Index] = static_cast<int32>(LOD.IndexBuffer.GetIndex(Index));
    }

    // Port of Unity MeshBuildData.RefineNear: every pass first discovers all
    // shared edges, validates the complete candidate against the global
    // budget, and only then commits it. A pass can therefore never stop in
    // the middle and leave a giant source triangle classified as a wound.
    constexpr int32 MaximumRuntimeTriangles = 24000;
    constexpr int32 MaximumRefinementPasses = 8;
    bool bRefinementStoppedByBudget = false;
    for (int32 PassIndex = 0; PassIndex < MaximumRefinementPasses; ++PassIndex)
    {
        TSet<uint64> SplitEdges;
        SplitEdges.Reserve(WorkingIndices.Num());
        for (int32 Index = 0; Index + 2 < WorkingIndices.Num(); Index += 3)
        {
            const int32 A = WorkingIndices[Index];
            const int32 B = WorkingIndices[Index + 1];
            const int32 C = WorkingIndices[Index + 2];
            for (const FChopOpeningFrame& Opening : Openings)
            {
                const float FrontShell = FMath::Max3(
                    Opening.Depth + 0.4f, 1.2f, Opening.Width * 0.21f);
                auto WorldPosition = [&WorkingVertices, &ComponentTransform](const int32 Vertex)
                {
                    return ComponentTransform.TransformPosition(WorkingVertices[Vertex].Position);
                };
                auto MarkByWorldLength = [&](const int32 EdgeA, const int32 EdgeB,
                                             const float MaximumLength)
                {
                    if (FVector::Distance(WorldPosition(EdgeA), WorldPosition(EdgeB))
                        > MaximumLength)
                    {
                        SplitEdges.Add(ChopEdgeKey(EdgeA, EdgeB));
                    }
                };
                auto OpeningCoordinates = [&](const int32 Vertex)
                {
                    const FVector Delta = WorldPosition(Vertex) - Opening.Centre;
                    return FVector2D(
                        FVector::DotProduct(Delta, Opening.Right)
                            / FMath::Max(0.01f, Opening.HalfWidth),
                        FVector::DotProduct(Delta, Opening.Up)
                            / FMath::Max(0.01f, Opening.HalfHeight));
                };
                auto Shell = [&](const int32 Vertex)
                {
                    return FVector::DotProduct(
                        WorldPosition(Vertex) - Opening.Centre, Opening.Normal);
                };
                auto MarkEdge = [&](const int32 EdgeA, const int32 EdgeB)
                {
                    const FVector2D A2 = OpeningCoordinates(EdgeA);
                    const FVector2D B2 = OpeningCoordinates(EdgeB);
                    const FVector2D Direction = B2 - A2;
                    const double Denominator = Direction.SizeSquared();
                    const double T = Denominator > 0.000001
                        ? FMath::Clamp(-FVector2D::DotProduct(A2, Direction)
                            / Denominator, 0.0, 1.0)
                        : 0.0;
                    const FVector2D Nearest = FMath::Lerp(A2, B2, T);
                    const float Radius = NormalisedChopRadius(Nearest.X, Nearest.Y);
                    const float BoundaryBand = 1.0f - SmoothThreshold(
                        0.055f, 0.14f, FMath::Abs(Radius - 1.0f));
                    const float InteriorPitch = Opening.TargetPitch * 3.5f;
                    const float ExteriorPitch = Opening.TargetPitch * 5.0f;
                    const float TargetPitch = Radius < 1.0f
                        ? FMath::Lerp(InteriorPitch, Opening.TargetPitch, BoundaryBand)
                        : FMath::Lerp(ExteriorPitch, Opening.TargetPitch, BoundaryBand);
                    if (FVector::Distance(WorldPosition(EdgeA), WorldPosition(EdgeB))
                            <= TargetPitch
                        || Nearest.SizeSquared() > 1.45)
                    {
                        return;
                    }
                    const float NearestShell = FMath::Lerp(
                        Shell(EdgeA), Shell(EdgeB), static_cast<float>(T));
                    if (NearestShell >= -FrontShell && NearestShell <= 0.4f)
                    {
                        SplitEdges.Add(ChopEdgeKey(EdgeA, EdgeB));
                    }
                };

                const FVector2D A2 = OpeningCoordinates(A);
                const FVector2D B2 = OpeningCoordinates(B);
                const FVector2D C2 = OpeningCoordinates(C);
                FVector Barycentric;
                if (TryBarycentricAtOrigin(A2, B2, C2, Barycentric))
                {
                    const float ContainingShell =
                        Shell(A) * Barycentric.X
                        + Shell(B) * Barycentric.Y
                        + Shell(C) * Barycentric.Z;
                    if (ContainingShell >= -FrontShell && ContainingShell <= 0.4f)
                    {
                        MarkByWorldLength(A, B, Opening.TargetPitch * 3.2f);
                        MarkByWorldLength(B, C, Opening.TargetPitch * 3.2f);
                        MarkByWorldLength(C, A, Opening.TargetPitch * 3.2f);
                    }
                }
                MarkEdge(A, B);
                MarkEdge(B, C);
                MarkEdge(C, A);
            }
        }

        if (SplitEdges.IsEmpty())
        {
            break;
        }
        int32 CandidateTriangleCount = 0;
        for (int32 Index = 0; Index + 2 < WorkingIndices.Num(); Index += 3)
        {
            const int32 A = WorkingIndices[Index];
            const int32 B = WorkingIndices[Index + 1];
            const int32 C = WorkingIndices[Index + 2];
            const int32 SplitCount =
                (SplitEdges.Contains(ChopEdgeKey(A, B)) ? 1 : 0)
                + (SplitEdges.Contains(ChopEdgeKey(B, C)) ? 1 : 0)
                + (SplitEdges.Contains(ChopEdgeKey(C, A)) ? 1 : 0);
            CandidateTriangleCount += 1 + SplitCount;
        }
        if (CandidateTriangleCount > MaximumRuntimeTriangles)
        {
            bRefinementStoppedByBudget = true;
            break;
        }

        TMap<uint64, int32> Midpoints;
        Midpoints.Reserve(SplitEdges.Num());
        auto GetMidpoint = [&](const int32 A, const int32 B)
        {
            const uint64 Key = ChopEdgeKey(A, B);
            if (const int32* Existing = Midpoints.Find(Key))
            {
                return *Existing;
            }
            const FChopVertex NewVertex = Midpoint(WorkingVertices[A], WorkingVertices[B]);
            const int32 NewIndex = WorkingVertices.Add(NewVertex);
            Midpoints.Add(Key, NewIndex);
            return NewIndex;
        };
        TArray<int32> NextIndices;
        NextIndices.Reserve(CandidateTriangleCount * 3);
        auto AddTriangle = [&NextIndices](const int32 A, const int32 B, const int32 C)
        {
            NextIndices.Append({A, B, C});
        };
        for (int32 Index = 0; Index + 2 < WorkingIndices.Num(); Index += 3)
        {
            const int32 A = WorkingIndices[Index];
            const int32 B = WorkingIndices[Index + 1];
            const int32 C = WorkingIndices[Index + 2];
            const bool bSplitAB = SplitEdges.Contains(ChopEdgeKey(A, B));
            const bool bSplitBC = SplitEdges.Contains(ChopEdgeKey(B, C));
            const bool bSplitCA = SplitEdges.Contains(ChopEdgeKey(C, A));
            const int32 SplitCount = static_cast<int32>(bSplitAB)
                + static_cast<int32>(bSplitBC) + static_cast<int32>(bSplitCA);
            if (SplitCount == 0)
            {
                AddTriangle(A, B, C);
                continue;
            }
            const int32 AB = bSplitAB ? GetMidpoint(A, B) : INDEX_NONE;
            const int32 BC = bSplitBC ? GetMidpoint(B, C) : INDEX_NONE;
            const int32 CA = bSplitCA ? GetMidpoint(C, A) : INDEX_NONE;
            if (SplitCount == 1)
            {
                if (bSplitAB)
                {
                    AddTriangle(A, AB, C); AddTriangle(AB, B, C);
                }
                else if (bSplitBC)
                {
                    AddTriangle(A, B, BC); AddTriangle(A, BC, C);
                }
                else
                {
                    AddTriangle(A, B, CA); AddTriangle(B, C, CA);
                }
                continue;
            }
            if (SplitCount == 2)
            {
                if (bSplitAB && bSplitBC)
                {
                    AddTriangle(B, BC, AB); AddTriangle(A, AB, C);
                    AddTriangle(AB, BC, C);
                }
                else if (bSplitAB && bSplitCA)
                {
                    AddTriangle(A, AB, CA); AddTriangle(B, C, CA);
                    AddTriangle(B, CA, AB);
                }
                else
                {
                    AddTriangle(C, CA, BC); AddTriangle(A, B, BC);
                    AddTriangle(A, BC, CA);
                }
                continue;
            }
            AddTriangle(A, AB, CA); AddTriangle(AB, B, BC);
            AddTriangle(CA, BC, C); AddTriangle(AB, BC, CA);
        }
        WorkingIndices = MoveTemp(NextIndices);
    }
    if (bRefinementStoppedByBudget)
    {
        UE_LOG(LogCMLTreeChop, Warning,
            TEXT("Tree voxel refinement stopped before exceeding the %d triangle budget: actor=%s triangles=%d"),
            MaximumRuntimeTriangles, *Owner->GetName(), WorkingIndices.Num() / 3);
    }

    TArray<FVector> BarkVertices;
    TArray<FVector> BarkNormals;
    TArray<FVector2D> BarkUV0;
    TArray<FVector2D> BarkUV1;
    TArray<FVector2D> BarkUV2;
    TArray<FVector2D> BarkUV3;
    TArray<FLinearColor> BarkColours;
    TArray<FProcMeshTangent> BarkTangents;
    TArray<int32> BarkTriangles;
    TArray<FVector> CutVertices;
    TArray<FVector> CutNormals;
    TArray<FVector2D> CutUV0;
    TArray<FVector2D> CutUV1;
    TArray<FVector2D> CutUV2;
    TArray<FVector2D> CutUV3;
    TArray<FLinearColor> CutColours;
    TArray<FProcMeshTangent> CutTangents;
    TArray<int32> CutTriangles;
    float MaximumCarveDepth = 0.0f;
    BarkVertices.Reserve(WorkingIndices.Num());
    CutVertices.Reserve(WorkingIndices.Num() / 8);
    for (int32 Index = 0; Index + 2 < WorkingIndices.Num(); Index += 3)
    {
        FChopVertex Triangle[3] = {
            WorkingVertices[WorkingIndices[Index]],
            WorkingVertices[WorkingIndices[Index + 1]],
            WorkingVertices[WorkingIndices[Index + 2]]};
        const FVector TriangleWorld = ComponentTransform.TransformPosition(
            (Triangle[0].Position + Triangle[1].Position + Triangle[2].Position) / 3.0f);
        const FVector TriangleWorldVertices[3] = {
            ComponentTransform.TransformPosition(Triangle[0].Position),
            ComponentTransform.TransformPosition(Triangle[1].Position),
            ComponentTransform.TransformPosition(Triangle[2].Position)};
        const float LongestTriangleEdge = FMath::Max3(
            FVector::Distance(TriangleWorldVertices[0], TriangleWorldVertices[1]),
            FVector::Distance(TriangleWorldVertices[1], TriangleWorldVertices[2]),
            FVector::Distance(TriangleWorldVertices[2], TriangleWorldVertices[0]));
        const FChopOpeningFrame* ActiveOpening = nullptr;
        float ActiveRadius = BIG_NUMBER;
        for (const FChopOpeningFrame& Opening : Openings)
        {
            const FVector Delta = TriangleWorld - Opening.Centre;
            const float AlongNormal = FVector::DotProduct(Delta, Opening.Normal);
            const float FrontShell = FMath::Max3(
                Opening.Depth + 0.4f, 1.2f, Opening.Width * 0.21f);
            if (AlongNormal < -FrontShell || AlongNormal > 0.4f)
            {
                continue;
            }
            const float CentreRadius = NormalisedChopRadius(
                FVector::DotProduct(Delta, Opening.Right)
                    / FMath::Max(0.01f, Opening.HalfWidth),
                FVector::DotProduct(Delta, Opening.Up)
                    / FMath::Max(0.01f, Opening.HalfHeight));
            // With an indexed all-or-nothing refinement pass, the centroid is
            // the stable voxel sample. Never deform a coarse triangle merely
            // because one distant corner happens to enter the opening.
            if (CentreRadius < 1.01f
                && LongestTriangleEdge <= Opening.TargetPitch * 5.05f
                && CentreRadius < ActiveRadius)
            {
                ActiveOpening = &Opening;
                ActiveRadius = CentreRadius;
            }
        }
        const bool bCut = ActiveOpening != nullptr;
        if (!bCut)
        {
            const int32 Base = BarkVertices.Num();
            for (const FChopVertex& Vertex : Triangle)
            {
                BarkVertices.Add(Vertex.Position);
                BarkNormals.Add(Vertex.Normal);
                BarkUV0.Add(Vertex.UV0);
                BarkUV1.Add(Vertex.UV1);
                BarkUV2.Add(Vertex.UV2);
                BarkUV3.Add(Vertex.UV3);
                BarkColours.Add(Vertex.Colour);
                const bool bFlipTangent = FVector::DotProduct(
                    FVector::CrossProduct(Vertex.TangentX, Vertex.TangentY),
                    Vertex.Normal) < 0.0f;
                BarkTangents.Emplace(Vertex.TangentX, bFlipTangent);
            }
            BarkTriangles.Append({Base, Base + 1, Base + 2});
            continue;
        }
        const FChopOpeningFrame& Opening = *ActiveOpening;
        FVector PreparedWorldOffsets[3] = {
            FVector::ZeroVector, FVector::ZeroVector, FVector::ZeroVector};
        float PreparedLayers[3] = {0.0f, 0.0f, 0.0f};
        FLinearColor PreparedColours[3] = {
            FLinearColor::White, FLinearColor::White, FLinearColor::White};
        bool bHasPhysicalDepth = false;
        for (int32 Corner = 0; Corner < 3; ++Corner)
        {
            const FChopVertex& Vertex = Triangle[Corner];
            const FVector VertexDelta = ComponentTransform.TransformPosition(Vertex.Position)
                - Opening.Centre;
            const float VertexX = FVector::DotProduct(VertexDelta, Opening.Right)
                / FMath::Max(0.01f, Opening.HalfWidth);
            const float VertexY = FVector::DotProduct(VertexDelta, Opening.Up)
                / FMath::Max(0.01f, Opening.HalfHeight);
            const float VertexRadius = NormalisedChopRadius(VertexX, VertexY);
            const float Edge = FMath::Clamp(1.0f - VertexRadius, 0.0f, 1.0f);
            const float EdgeFalloff = FMath::SmoothStep(0.0f, 1.0f,
                FMath::Clamp((Edge - 0.025f) / 0.275f, 0.0f, 1.0f));
            const float Angle = FMath::Atan2(VertexY, VertexX);
            const float BarkBreakRadius = 0.73f
                + FMath::Sin(Angle * 3.0f + 0.5f) * 0.025f
                + FMath::Sin(Angle * 7.0f - VertexY * 1.7f) * 0.018f;
            float DepthWorld = Opening.Depth
                * ChiselDepthFactor(VertexX, VertexY, VertexRadius)
                * EdgeFalloff;
            if (VertexRadius > BarkBreakRadius)
            {
                DepthWorld = 0.0f;
            }
            const bool bBrokenGroove =
                (VertexY > 0.18f && VertexY < 0.40f
                    && FMath::Abs(VertexX + 0.28f - VertexY * 0.25f) < 0.028f)
                || (VertexY > -0.12f && VertexY < 0.02f
                    && FMath::Abs(VertexX - 0.24f - VertexY * 0.10f) < 0.026f)
                || (VertexY > -0.48f && VertexY < -0.28f
                    && FMath::Abs(VertexX + 0.12f + VertexY * 0.30f) < 0.028f);
            if (bBrokenGroove && VertexRadius <= BarkBreakRadius)
            {
                DepthWorld += 0.075f;
            }
            DepthWorld = FMath::RoundToFloat(DepthWorld / 0.075f) * 0.075f;
            bHasPhysicalDepth |= DepthWorld >= 0.075f;
            MaximumCarveDepth = FMath::Max(MaximumCarveDepth, DepthWorld);
            PreparedWorldOffsets[Corner] = -Opening.Normal
                * FMath::Min(Opening.Depth, DepthWorld);
            const float Layer = FMath::Clamp(
                DepthWorld / FMath::Max(0.01f, Opening.Depth), 0.0f, 1.0f);
            PreparedLayers[Corner] = Layer;
            const float DepthFactor = ChiselDepthFactor(VertexX, VertexY, VertexRadius);
            FLinearColor FreshWood = DepthFactor >= 0.82f
                ? FLinearColor(0.94f, 0.62f, 0.35f, 1.0f)
                : DepthFactor >= 0.60f
                    ? FLinearColor(0.72f, 0.40f, 0.20f, 1.0f)
                    : DepthFactor >= 0.39f
                        ? FLinearColor(0.88f, 0.55f, 0.30f, 1.0f)
                        : FLinearColor(1.00f, 0.72f, 0.42f, 1.0f);
            FreshWood *= FMath::Lerp(1.05f, 0.98f, Layer);
            const float Fiber = FMath::Sin(
                VertexX * 38.0f + FMath::Sin(VertexY * 11.0f) * 1.2f) * 0.5f + 0.5f;
            const float FiberWindow = SmoothThreshold(
                0.28f, 0.62f,
                FMath::Sin(VertexY * 18.0f - VertexX * 4.0f) * 0.5f + 0.5f);
            const float CrossGrain = FMath::Sin(
                VertexX * 19.0f - VertexY * 7.0f) * 0.5f + 0.5f;
            const float BroadFacet = FMath::Sin(
                VertexX * 5.1f + VertexY * 3.4f
                    + FMath::Sin(VertexY * 7.0f) * 0.7f) * 0.5f + 0.5f;
            FreshWood *= FMath::Lerp(
                0.92f, 1.10f, SmoothThreshold(0.28f, 0.72f, BroadFacet));
            FreshWood *= FMath::Lerp(
                0.96f, 1.04f, FMath::Lerp(CrossGrain, Fiber, FiberWindow));
            FreshWood.A = 1.0f;
            if (bBrokenGroove)
            {
                FreshWood = FMath::Lerp(
                    FreshWood, FLinearColor(0.25f, 0.12f, 0.055f, 1.0f), 0.38f);
            }
            const float Cambium = SmoothThreshold(0.70f, 0.79f, VertexRadius);
            PreparedColours[Corner] = FMath::Lerp(
                FreshWood, FLinearColor(1.00f, 0.70f, 0.39f, 1.0f), Cambium);
        }
        // A triangle whose three samples remain at zero depth is authored
        // bark, not wound material. Keeping it in the cut section caused the
        // two pale, floating leaf-like artefacts visible in the screenshot.
        if (!bHasPhysicalDepth)
        {
            const int32 BarkBase = BarkVertices.Num();
            for (const FChopVertex& Vertex : Triangle)
            {
                BarkVertices.Add(Vertex.Position);
                BarkNormals.Add(Vertex.Normal);
                BarkUV0.Add(Vertex.UV0);
                BarkUV1.Add(Vertex.UV1);
                BarkUV2.Add(Vertex.UV2);
                BarkUV3.Add(Vertex.UV3);
                BarkColours.Add(Vertex.Colour);
                const bool bFlipTangent = FVector::DotProduct(
                    FVector::CrossProduct(Vertex.TangentX, Vertex.TangentY),
                    Vertex.Normal) < 0.0f;
                BarkTangents.Emplace(Vertex.TangentX, bFlipTangent);
            }
            BarkTriangles.Append({BarkBase, BarkBase + 1, BarkBase + 2});
            continue;
        }

        const int32 Base = CutVertices.Num();
        float CutLayers[3] = {0.0f, 0.0f, 0.0f};
        for (int32 Corner = 0; Corner < 3; ++Corner)
        {
            const FChopVertex& Vertex = Triangle[Corner];
            const FVector& WorldOffset = PreparedWorldOffsets[Corner];
            CutVertices.Add(Vertex.Position
                + ComponentTransform.InverseTransformVectorNoScale(WorldOffset));
            CutUV0.Add(Vertex.UV0);
            CutUV1.Add(Vertex.UV1);
            CutUV2.Add(Vertex.UV2);
            CutUV3.Add(Vertex.UV3);
            const float Layer = PreparedLayers[Corner];
            CutLayers[CutVertices.Num() - Base - 1] = Layer;
            CutColours.Add(PreparedColours[Corner]);
        }
        FVector GeometricNormal = FVector::CrossProduct(
            CutVertices[Base + 1] - CutVertices[Base],
            CutVertices[Base + 2] - CutVertices[Base]).GetSafeNormal();
        const FVector AuthoredNormal = (
            Triangle[0].Normal + Triangle[1].Normal + Triangle[2].Normal).GetSafeNormal();
        if (FVector::DotProduct(GeometricNormal, AuthoredNormal) < 0.0f)
        {
            GeometricNormal *= -1.0f;
        }
        for (int32 Corner = 0; Corner < 3; ++Corner)
        {
            const float NormalBlend = SmoothThreshold(0.08f, 0.55f, CutLayers[Corner]);
            const FVector CutNormal = FMath::Lerp(
                Triangle[Corner].Normal, GeometricNormal, NormalBlend).GetSafeNormal();
            const FVector CutTangent = FVector::VectorPlaneProject(
                Triangle[Corner].TangentX, CutNormal).GetSafeNormal();
            const bool bFlipTangent = FVector::DotProduct(
                FVector::CrossProduct(Triangle[Corner].TangentX, Triangle[Corner].TangentY),
                Triangle[Corner].Normal) < 0.0f;
            CutNormals.Add(CutNormal);
            CutTangents.Emplace(CutTangent, bFlipTangent);
        }
        CutTriangles.Append({Base, Base + 1, Base + 2});
    }

    if (CutTriangles.IsEmpty())
    {
        UE_LOG(LogCMLTreeChop, Error,
            TEXT("No trunk triangles intersected the voxel field: actor=%s trunk=%s impact=%s openings=%d"),
            *Owner->GetName(), *SourceTrunk->GetName(), *LastImpactPoint.ToCompactString(),
            Openings.Num());
        return;
    }
    UE_LOG(LogCMLTreeChop, Display,
        TEXT("Voxel trunk rebuilt: actor=%s trunk=%s hit=%d openings=%d totalTriangles=%d cutTriangles=%d maxDepth=%.2fcm milliseconds=%.2f"),
        *Owner->GetName(), *SourceTrunk->GetName(), TreeHitStage,
        Openings.Num(), WorkingIndices.Num() / 3, CutTriangles.Num() / 3,
        MaximumCarveDepth, (FPlatformTime::Seconds() - RebuildStartedAt) * 1000.0);

    if (TreeRuntimeTrunk == nullptr)
    {
        UCMLTreeRuntimeMeshComponent* RuntimeTrunk =
            NewObject<UCMLTreeRuntimeMeshComponent>(Owner, TEXT("CML_VoxelCarvedTrunk"));
        TreeRuntimeTrunk = RuntimeTrunk;
        Owner->AddInstanceComponent(TreeRuntimeTrunk);
        if (USceneComponent* Parent = SourceTrunk->GetAttachParent())
        {
            TreeRuntimeTrunk->SetupAttachment(Parent);
            TreeRuntimeTrunk->SetRelativeTransform(SourceTrunk->GetRelativeTransform());
        }
        else
        {
            TreeRuntimeTrunk->SetupAttachment(SourceTrunk);
            TreeRuntimeTrunk->SetRelativeTransform(FTransform::Identity);
        }
        TreeRuntimeTrunk->SetMobility(EComponentMobility::Movable);
        TreeRuntimeTrunk->SetLightmapType(ELightmapType::ForceVolumetric);
        TreeRuntimeTrunk->IndirectLightingCacheQuality = ILCQ_Volume;
        TreeRuntimeTrunk->SetCastShadow(true);
        TreeRuntimeTrunk->SetVisibleInRayTracing(true);
        TreeRuntimeTrunk->SetAffectDynamicIndirectLighting(true);
        TreeRuntimeTrunk->SetAffectDistanceFieldLighting(true);
        TreeRuntimeTrunk->SetReceivesDecals(true);
        SourceTrunk->SetLightAttachmentsAsGroup(true);
        FBoxSphereBounds AuthoredBounds = StaticMesh->GetBounds();
        AuthoredBounds.BoxExtent += FVector(0.2f);
        AuthoredBounds.SphereRadius = AuthoredBounds.BoxExtent.Size();
        RuntimeTrunk->SetAuthoredLocalBounds(AuthoredBounds);
        TreeRuntimeTrunk->RegisterComponent();
        TreeRuntimeTrunk->SetVisibility(true, false);
        TreeRuntimeTrunk->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        SourceTrunk->SetVisibility(false, false);
    }
    TreeRuntimeTrunk->ClearAllMeshSections();
    TreeRuntimeTrunk->CreateMeshSection_LinearColor(
        0, BarkVertices, BarkTriangles, BarkNormals,
        BarkUV0, BarkUV1, BarkUV2, BarkUV3,
        BarkColours, BarkTangents, false, false);
    TreeRuntimeTrunk->CreateMeshSection_LinearColor(
        1, CutVertices, CutTriangles, CutNormals,
        CutUV0, CutUV1, CutUV2, CutUV3,
        CutColours, CutTangents, false, false);
    for (int32 MaterialIndex = 0; MaterialIndex < SourceTrunk->GetNumMaterials(); ++MaterialIndex)
    {
        TreeRuntimeTrunk->SetMaterial(MaterialIndex, SourceTrunk->GetMaterial(MaterialIndex));
    }
    if (UMaterialInterface* CutMaterial = LoadObject<UMaterialInterface>(
        nullptr,
        TEXT("/Game/Migrated/Project/Art/ManualEra/Wood/Materials/"
             "M_CML_TreeWound.M_CML_TreeWound")))
    {
        TreeRuntimeTrunk->SetMaterial(1, CutMaterial);
    }
}

void UCMLGameplayTargetComponent::BeginTreeFall()
{
    AActor* Owner = GetOwner();
    if (Owner == nullptr)
    {
        return;
    }
    bCommitted = true;
    Yield = 3 + static_cast<int32>(SourceId.Low % 3ULL);
    StopImpactShake();
    Owner->SetActorEnableCollision(false);
    TreeFallElapsed = 0.0f;
    bTreePhysicsReleased = false;
    bTreeSettled = false;
    TreeFallPhase = 0;
    TreePhaseElapsed = 0.0f;
    TreeFallAngleDegrees = 0.0f;
    TreeAngularSpeed = 0.0f;
    TreeQuietElapsed = 0.0f;
    TreeSettledElapsed = 0.0f;

    const FVector StrikeDirection = LastImpactPoint - LastViewOrigin;
    if (!FCMLTreeFellGeometry::TryResolveFallDirection(
        FVector::UpVector, StrikeDirection, LastImpactNormal,
        Owner->GetActorForwardVector(), TreeFallDirection))
    {
        TreeFallDirection = Owner->GetActorForwardVector().GetSafeNormal2D();
    }
    TreeFallAxis = FVector::CrossProduct(FVector::UpVector, TreeFallDirection).GetSafeNormal();

    FBoxSphereBounds TrunkBounds = Owner->GetRootComponent() != nullptr
        ? Owner->GetRootComponent()->Bounds : FBoxSphereBounds(Owner->GetActorLocation(), FVector(35,35,250), 255);
    TArray<UStaticMeshComponent*> Meshes;
    Owner->GetComponents(Meshes);
    for (const UStaticMeshComponent* Mesh : Meshes)
    {
        if (Mesh == nullptr || Mesh->GetStaticMesh() == nullptr)
        {
            continue;
        }
        const FString Identity = (Mesh->GetName() + TEXT("|")
            + Mesh->GetStaticMesh()->GetName()).ToLower();
        if (Identity.Contains(TEXT("trunk")) || Identity.Contains(TEXT("branch")))
        {
            TrunkBounds = Mesh->Bounds;
            break;
        }
    }
    const float Radius = FMath::Clamp(
        FMath::Min(TrunkBounds.BoxExtent.X, TrunkBounds.BoxExtent.Y), 18.0f, 55.0f);
    const float HalfHeight = FMath::Clamp(TrunkBounds.BoxExtent.Z, 100.0f, 650.0f);
    TreeTrunkHalfHeight = HalfHeight;
    TreeFallPivot = FVector(
        TrunkBounds.Origin.X, TrunkBounds.Origin.Y,
        TrunkBounds.Origin.Z - HalfHeight) + TreeFallDirection * Radius;
    TreeBodyInitialCenter = TrunkBounds.Origin;
    TreeBodyInitialRotation = Owner->GetActorQuat();
    TreeReleaseAngleDegrees = static_cast<float>(FCMLTreeFellGeometry::ResolveReleaseAngleDegrees(
        Owner->GetComponentsBoundingBox(true).GetCenter(), TreeFallPivot,
        FVector::UpVector, TreeFallDirection));
    const FBox VisualBounds = Owner->GetComponentsBoundingBox(true);
    TreeCrownInitialPoint = FVector(
        TrunkBounds.Origin.X, TrunkBounds.Origin.Y, VisualBounds.Max.Z);
    const float TreeLength = FMath::Max(
        HalfHeight * 2.0f,
        FVector::DotProduct(TreeCrownInitialPoint - TreeFallPivot, FVector::UpVector));
    const FVector LandingProbe = TreeFallPivot + TreeFallDirection * TreeLength;
    FHitResult GroundHit;
    FCollisionQueryParams GroundQuery(SCENE_QUERY_STAT(CMLTreeLandingGround), false, Owner);
    const bool bFoundGround = GetWorld()->LineTraceSingleByChannel(
        GroundHit,
        LandingProbe + FVector::UpVector * FMath::Max(500.0f, TreeLength),
        LandingProbe - FVector::UpVector * FMath::Max(500.0f, TreeLength),
        ECC_Visibility,
        GroundQuery);
    const float GroundZ = bFoundGround ? GroundHit.ImpactPoint.Z : TreeFallPivot.Z;
    TreeLandingAngleDegrees = FMath::RadiansToDegrees(FMath::Acos(FMath::Clamp(
        (GroundZ + Radius - TreeFallPivot.Z) / TreeLength, -0.04f, 0.18f)));

    FActorSpawnParameters SpawnParameters;
    SpawnParameters.SpawnCollisionHandlingOverride =
        ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    FallingTreeHost = GetWorld()->SpawnActor<AActor>(
        AActor::StaticClass(), TreeBodyInitialCenter, Owner->GetActorRotation(), SpawnParameters);
    if (FallingTreeHost == nullptr)
    {
        return;
    }
    FallingTreeBody = NewObject<UBoxComponent>(FallingTreeHost, TEXT("CML_FallingTreeBody"));
    FallingTreeHost->AddInstanceComponent(FallingTreeBody);
    FallingTreeHost->SetRootComponent(FallingTreeBody);
    FallingTreeBody->SetBoxExtent(FVector(Radius, Radius, HalfHeight));
    FallingTreeBody->SetCollisionProfileName(TEXT("PhysicsActor"));
    FallingTreeBody->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
    FallingTreeBody->SetHiddenInGame(true);
    FallingTreeBody->SetLinearDamping(0.06f);
    FallingTreeBody->SetAngularDamping(0.06f);
    FallingTreeBody->RegisterComponent();
    FallingTreeBody->SetWorldLocationAndRotation(TreeBodyInitialCenter, TreeBodyInitialRotation);
    FallingTreeBody->SetSimulatePhysics(false);
    Owner->AttachToComponent(FallingTreeBody, FAttachmentTransformRules::KeepWorldTransform);
    SetComponentTickEnabled(true);
}

void UCMLGameplayTargetComponent::TickComponent(
    const float DeltaTime,
    const ELevelTick TickType,
    FActorComponentTickFunction* ThisTickFunction)
{
    Super::TickComponent(DeltaTime, TickType, ThisTickFunction);

    if (TargetKind == ECMLGameplayTargetKind::WoodenCrate && HingedPart.IsValid())
    {
        bool bPanelOwnsCrate = false;
        if (const UWorld* World = GetWorld())
        {
            if (const APlayerController* PlayerController = World->GetFirstPlayerController())
            {
                if (const ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
                {
                    FCMLStableId OpenNode;
                    bPanelOwnsCrate = HUD->GetActiveTransferNode(OpenNode)
                        && OpenNode == SourceId;
                }
            }
        }
        bHingeWantsOpen = bPanelOwnsCrate;
    }

    if (USceneComponent* Part = HingedPart.Get())
    {
        const float TargetAlpha = bHingeWantsOpen ? 1.0f : 0.0f;
        HingeAlpha = FMath::FInterpConstantTo(
            HingeAlpha, TargetAlpha, DeltaTime, 1.0f / HingeDurationSeconds);
        const float SmoothAlpha = HingeAlpha * HingeAlpha * (3.0f - 2.0f * HingeAlpha);
        Part->SetRelativeRotation(FQuat::Slerp(
            HingeClosedRotation, HingeOpenRotation, SmoothAlpha));
        // Hinged targets need a continuous presentation tick. The generic
        // harvest/tree path below normally disables an idle target component;
        // doing that here produced exactly one animation frame per click.
        if (TargetKind == ECMLGameplayTargetKind::WoodenCrate
            || TargetKind == ECMLGameplayTargetKind::AirshipDoor)
        {
            return;
        }
    }

    if (TargetKind == ECMLGameplayTargetKind::AirshipRepair
        || TargetKind == ECMLGameplayTargetKind::AirshipPilotStation)
    {
        AActor* Owner = GetOwner();
        UWorld* World = GetWorld();
        const UCMLSimulationSubsystem* Simulation = World != nullptr
            ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
        FCMLAirshipEntityState Airship;
        if (Owner != nullptr && Simulation != nullptr
            && Simulation->GetAirshipState(SourceId, Airship))
        {
            const FVector Location(
                static_cast<double>(Airship.Pose.Position.Z) / 10.0,
                static_cast<double>(Airship.Pose.Position.X) / 10.0,
                static_cast<double>(Airship.Pose.Position.Y) / 10.0);
            const float Yaw = -static_cast<float>(static_cast<uint16>(Airship.Pose.YawTurn))
                * 360.0f / 65536.0f;
            const float Pitch = -static_cast<float>(Airship.PitchTurnUnits)
                * 360.0f / 65536.0f;
            Owner->SetActorLocationAndRotation(Location, FRotator(Pitch, Yaw, 0.0f), false);
            if (TargetKind == ECMLGameplayTargetKind::AirshipPilotStation
                && Airship.RepairStatus != ECMLAirshipRepairStatus::Repaired)
            {
                AirshipSmokeAccumulator += DeltaTime;
                if (AirshipSmokeAccumulator >= 0.32f)
                {
                    AirshipSmokeAccumulator = FMath::Fmod(AirshipSmokeAccumulator, 0.32f);
                    const FVector Direction = Owner->GetActorTransform().TransformVectorNoScale(
                        -FVector::RightVector + FVector::UpVector * 0.28f).GetSafeNormal();
                    const FVector LocalNozzles[] = {
                        FVector(-202.0f, -501.5f, 148.0f),
                        FVector(202.0f, -501.5f, 148.0f)};
                    for (const FVector& LocalNozzle : LocalNozzles)
                    {
                        FActorSpawnParameters Parameters;
                        Parameters.SpawnCollisionHandlingOverride =
                            ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
                        if (ACMLImpactBurstActor* Smoke = World->SpawnActor<ACMLImpactBurstActor>(
                                ACMLImpactBurstActor::StaticClass(),
                                Owner->GetActorTransform().TransformPosition(LocalNozzle),
                                FRotator::ZeroRotator, Parameters))
                        {
                            Smoke->InitialiseAirshipSmoke(
                                Smoke->GetActorLocation(), Direction);
                        }
                    }
                }
            }
            else
            {
                AirshipSmokeAccumulator = 0.0f;
            }
        }
        return;
    }

    if (ImpactShakeElapsed < ImpactShakeDuration && ShakenPrimitives.Num() > 0)
    {
        ImpactShakeElapsed += DeltaTime;
        const float Progress = FMath::Clamp(
            ImpactShakeElapsed / ImpactShakeDuration, 0.0f, 1.0f);
        const float Envelope = FMath::Square(1.0f - Progress);
        const float Phase = ImpactShakeElapsed * ImpactShakeFrequency * UE_TWO_PI;
        const float Recoil = FMath::Sin(Phase);
        const float Side = FMath::Sin(Phase * 0.67f + 0.8f) * ImpactShakeDirection;
        const FVector WorldOffset =
            (ImpactShakeWorldDirection * Recoil
                + ImpactShakeWorldSide * Side * 0.24f)
            * ImpactShakeTravel * Envelope;
        const FRotator RotationOffset(
            -Recoil * ImpactShakeRotation,
            Side * ImpactShakeRotation * 0.45f,
            -Side * ImpactShakeRotation * 0.65f);
        for (const FShakenPrimitiveState& State : ShakenPrimitives)
        {
            UPrimitiveComponent* Primitive = State.Primitive.Get();
            if (Primitive == nullptr)
            {
                continue;
            }
            const USceneComponent* Parent = Primitive->GetAttachParent();
            const FVector LocalOffset = Parent != nullptr
                ? Parent->GetComponentTransform().InverseTransformVectorNoScale(WorldOffset)
                : WorldOffset;
            Primitive->SetRelativeLocationAndRotation(
                State.RelativeTransform.GetLocation() + LocalOffset,
                RotationOffset.Quaternion() * State.RelativeTransform.GetRotation());
        }
        if (ImpactShakeElapsed >= ImpactShakeDuration)
        {
            StopImpactShake();
        }
    }
    if (!bCommitted || TargetKind != ECMLGameplayTargetKind::FellableTree)
    {
        if (ImpactShakeElapsed >= ImpactShakeDuration)
        {
            SetComponentTickEnabled(false);
        }
        return;
    }
    AActor* Owner = GetOwner();
    if (Owner == nullptr)
    {
        SetComponentTickEnabled(false);
        return;
    }
    const float Dt = FMath::Max(0.0f, DeltaTime);
    TreeFallElapsed += Dt;
    TreePhaseElapsed += Dt;
    if (FallingTreeBody == nullptr)
    {
        SetComponentTickEnabled(false);
        return;
    }

    // Deterministic authored fall. Chaos was unsuitable here: a simplified
    // box body could rock upright indefinitely. The trunk now lands exactly
    // once and stays there; the conspicuous crown rebound read as a rubber
    // object rather than a heavy tree.
    constexpr float ReleaseRampDuration = 0.85f;
    constexpr float MaximumAngularSpeedDegrees = 20.6265f; // 0.36 rad/s
    constexpr float SettlementDuration = 0.20f;
    switch (TreeFallPhase)
    {
    case 0: // Supported release.
    {
        const float Progress = FMath::Clamp(TreePhaseElapsed / ReleaseRampDuration, 0.0f, 1.0f);
        const float Smooth = Progress * Progress * (3.0f - 2.0f * Progress);
        TreeFallAngleDegrees = TreeReleaseAngleDegrees * Smooth;
        if (Progress >= 1.0f)
        {
            TreeFallPhase = 1;
            TreePhaseElapsed = 0.0f;
            TreeAngularSpeed = 10.31325f;
        }
        break;
    }
    case 1: // Heavy free fall under gravity, capped like Unity.
    {
        const float GravityAcceleration = 24.0f
            * FMath::Max(0.18f, FMath::Sin(FMath::DegreesToRadians(TreeFallAngleDegrees)));
        TreeAngularSpeed = FMath::Min(
            MaximumAngularSpeedDegrees, TreeAngularSpeed + GravityAcceleration * Dt);
        TreeFallAngleDegrees += TreeAngularSpeed * Dt;
        if (TreeFallAngleDegrees >= TreeLandingAngleDegrees)
        {
            TreeFallAngleDegrees = TreeLandingAngleDegrees;
            TreeFallPhase = 2;
            TreePhaseElapsed = 0.0f;
            TreeAngularSpeed = 0.0f;
        }
        break;
    }
    case 2: // Brief non-oscillating contact settle.
    {
        TreeFallAngleDegrees = TreeLandingAngleDegrees;
        if (TreePhaseElapsed >= SettlementDuration)
        {
            TreeFallPhase = 3;
            bTreeSettled = true;
            TreeSettledElapsed = 0.0f;
        }
        break;
    }
    default:
        break;
    }

    const FQuat Lean(TreeFallAxis, FMath::DegreesToRadians(TreeFallAngleDegrees));
    const FVector BodyCentre = TreeFallPivot
        + Lean.RotateVector(TreeBodyInitialCenter - TreeFallPivot);
    FallingTreeBody->SetWorldLocationAndRotation(
        BodyCentre, Lean * TreeBodyInitialRotation, false, nullptr, ETeleportType::TeleportPhysics);
    if (!bTreeSettled)
    {
        return;
    }

    TreeSettledElapsed += DeltaTime;
    if (TreeSettledElapsed >= 3.0f)
    {
        ShowCollectionFeed();
        Owner->DetachFromActor(FDetachmentTransformRules::KeepWorldTransform);
        Owner->SetActorHiddenInGame(true);
        if (FallingTreeHost != nullptr)
        {
            FallingTreeHost->Destroy();
            FallingTreeHost = nullptr;
            FallingTreeBody = nullptr;
        }
        SetComponentTickEnabled(false);
    }
}

void UCMLGameplayTargetComponent::CommitWorldRemoval()
{
    bCommitted = true;
    ShowCollectionFeed();
    if (AActor* Owner = GetOwner())
    {
        Owner->SetActorEnableCollision(false);
        Owner->SetActorHiddenInGame(true);
    }
}

void UCMLGameplayTargetComponent::ShowCollectionFeed() const
{
    FString ItemName;
    FCMLStableId ItemId;
    ECMLInventoryIconKind IconKind = ECMLInventoryIconKind::Generic;
    switch (TargetKind)
    {
    case ECMLGameplayTargetKind::WildFiberTuft:
        ItemName = TEXT("Fibra vegetale");
        ItemId = CMLContentIds::PlantFiber;
        IconKind = ECMLInventoryIconKind::PlantFiber;
        break;
    case ECMLGameplayTargetKind::FallenSticks:
        ItemName = TEXT("Bastone");
        ItemId = CMLContentIds::Stick;
        IconKind = ECMLInventoryIconKind::Stick;
        break;
    case ECMLGameplayTargetKind::LoosePebble:
    case ECMLGameplayTargetKind::EnvironmentalStone:
        ItemName = TEXT("Pietra");
        ItemId = CMLContentIds::Stone;
        IconKind = ECMLInventoryIconKind::Stone;
        break;
    case ECMLGameplayTargetKind::IronOreRock:
    case ECMLGameplayTargetKind::IronDepositSurface:
        ItemName = TEXT("Ferro grezzo");
        ItemId = CMLContentIds::RawIron;
        IconKind = ECMLInventoryIconKind::Ore;
        break;
    case ECMLGameplayTargetKind::CopperOreRock:
    case ECMLGameplayTargetKind::CopperDepositSurface:
        ItemName = TEXT("Rame grezzo");
        ItemId = CMLContentIds::RawCopper;
        IconKind = ECMLInventoryIconKind::Ore;
        break;
    case ECMLGameplayTargetKind::TinOreRock:
    case ECMLGameplayTargetKind::TinDepositSurface:
        ItemName = TEXT("Stagno grezzo");
        ItemId = CMLContentIds::RawTin;
        IconKind = ECMLInventoryIconKind::Ore;
        break;
    case ECMLGameplayTargetKind::FellableTree:
        ItemName = TEXT("Tronco");
        ItemId = CMLContentIds::WoodLog;
        IconKind = ECMLInventoryIconKind::WoodLog;
        break;
    default: break;
    }
    if (!ItemName.IsEmpty())
    {
        if (const UWorld* World = GetWorld())
        {
            if (APlayerController* PlayerController = World->GetFirstPlayerController())
            {
                if (ACMLHUD* HUD = Cast<ACMLHUD>(PlayerController->GetHUD()))
                {
                    HUD->PushCollectedItem(ItemId, ItemName, IconKind, Yield);
                }
            }
        }
    }
}

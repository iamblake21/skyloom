#include "Game/CMLGameplayBootstrapSubsystem.h"
#include "Building/CMLBuildableVisuals.h"
#include "Game/CMLGameInstance.h"
#include "Intro/CMLIntroArrival.h"

#include "Components/BoxComponent.h"
#include "Components/ChildActorComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/EngineTypes.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GameFramework/Actor.h"
#include "GameFramework/Pawn.h"
#include "Interaction/CMLGameplayTargetComponent.h"
#include "Content/CMLContentIds.h"
#include "Simulation/CMLSimulationSubsystem.h"
#include "Kismet/GameplayStatics.h"
#include "TimerManager.h"
#include "UI/CMLHUD.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLGameplayBootstrap, Log, All);

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

    ECMLGameplayTargetKind ClassifyStation(const FString& Name)
    {
        const FString Lower = Name.ToLower();
        if (Lower.Contains(TEXT("pf_workbench"))) return ECMLGameplayTargetKind::Workbench;
        if (Lower.Contains(TEXT("pf_crude_furnace"))) return ECMLGameplayTargetKind::CrudeFurnace;
        if (Lower.Contains(TEXT("pf_crate"))) return ECMLGameplayTargetKind::WoodenCrate;
        if (Lower.Contains(TEXT("pf_mechanicalpress"))) return ECMLGameplayTargetKind::MechanicalPress;
        if (Lower.Contains(TEXT("pf_mechanicaldrill"))) return ECMLGameplayTargetKind::MechanicalDrill;
        if (Lower.Contains(TEXT("pf_airship"))
            || Lower.Contains(TEXT("sm_airship_visual")))
            return ECMLGameplayTargetKind::AirshipRepair;
        return ECMLGameplayTargetKind::None;
    }

    UPrimitiveComponent* EnsureInteractionCollision(
        AActor& Actor, const ECMLGameplayTargetKind Kind)
    {
        const bool bMiningTarget =
            Kind == ECMLGameplayTargetKind::EnvironmentalStone
            || Kind == ECMLGameplayTargetKind::IronOreRock
            || Kind == ECMLGameplayTargetKind::IronDepositSurface
            || Kind == ECMLGameplayTargetKind::CopperOreRock
            || Kind == ECMLGameplayTargetKind::CopperDepositSurface
            || Kind == ECMLGameplayTargetKind::TinOreRock
            || Kind == ECMLGameplayTargetKind::TinDepositSurface;
        if (bMiningTarget)
        {
            // Unity adds MeshColliders to every owned renderer. Use the same
            // visible geometry here so a ray aimed at one raised module cannot
            // be stolen by a neighbouring module's broad bounding box.
            TArray<UStaticMeshComponent*> MiningMeshes;
            Actor.GetComponents(MiningMeshes);
            bool bHasVisibleMesh = false;
            for (UStaticMeshComponent* Mesh : MiningMeshes)
            {
                if (Mesh == nullptr || Mesh->GetStaticMesh() == nullptr
                    || !Mesh->IsVisible())
                {
                    continue;
                }
                Mesh->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
                Mesh->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
                Mesh->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
                bHasVisibleMesh = true;
            }
            if (bHasVisibleMesh)
            {
                return nullptr;
            }
        }

        UStaticMeshComponent* TreeTrunk = Kind == ECMLGameplayTargetKind::FellableTree
            ? FindTreeTrunk(Actor) : nullptr;
        FBox Bounds = TreeTrunk != nullptr
            ? TreeTrunk->Bounds.GetBox()
            : Actor.GetComponentsBoundingBox(true);
        if (!Bounds.IsValid)
        {
            Bounds = FBox::BuildAABB(Actor.GetActorLocation(), FVector(35.0f));
        }
        UBoxComponent* Box = NewObject<UBoxComponent>(&Actor, TEXT("CMLInteractionBounds"));
        Box->SetMobility(EComponentMobility::Movable);
        FVector Extent = Bounds.GetExtent().ComponentMax(FVector(18.0f));
        if (TreeTrunk != nullptr && TreeTrunk->GetStaticMesh() != nullptr)
        {
            // Build the fallback collider in trunk-local space. The previous
            // foliage-derived world box could sit centimetres in front of the
            // bark, so the voxel cut was centred in empty space.
            FBoxSphereBounds LocalBounds = TreeTrunk->GetStaticMesh()->GetBounds();
            Extent = LocalBounds.BoxExtent.ComponentMax(FVector(18.0f));
            Box->SetupAttachment(TreeTrunk);
            Box->SetRelativeLocation(LocalBounds.Origin);

            // Every authored tree is physically solid and raycastable, even
            // when its imported collision preset was missing a Pawn response.
            TreeTrunk->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
            TreeTrunk->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
            TreeTrunk->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
        }
        else if (USceneComponent* Root = Actor.GetRootComponent())
        {
            Box->SetupAttachment(Root);
            Box->SetWorldLocation(Bounds.GetCenter());
        }
        Box->SetBoxExtent(Extent, false);
        Box->SetCollisionEnabled(Kind == ECMLGameplayTargetKind::FellableTree
            ? ECollisionEnabled::QueryAndPhysics : ECollisionEnabled::QueryOnly);
        Box->SetCollisionResponseToAllChannels(ECR_Ignore);
        Box->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
        if (Kind == ECMLGameplayTargetKind::FellableTree)
        {
            Box->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
        }
        Box->SetCollisionObjectType(ECC_WorldDynamic);
        Box->RegisterComponent();
        Actor.AddInstanceComponent(Box);
        return Box;
    }

    UCMLGameplayTargetComponent* AttachTarget(
        AActor& Actor,
        const ECMLGameplayTargetKind Kind,
        const FCMLStableId SourceId,
        const int32 Yield = 1)
    {
        if (UCMLGameplayTargetComponent* Existing =
                Actor.FindComponentByClass<UCMLGameplayTargetComponent>())
        {
            return Existing;
        }
        UCMLGameplayTargetComponent* Target = NewObject<UCMLGameplayTargetComponent>(
            &Actor, TEXT("CMLGameplayTarget"));
        Target->Configure(Kind, SourceId, Yield);
        Target->RegisterComponent();
        Actor.AddInstanceComponent(Target);
        Target->ConfigureInteractionAnchor(EnsureInteractionCollision(Actor, Kind));
        return Target;
    }

    UStaticMeshComponent* AddReplacementMesh(
        AActor& Actor,
        const TCHAR* ComponentName,
        const TCHAR* AssetPath,
        const FVector& RelativeLocation = FVector::ZeroVector)
    {
        UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, AssetPath);
        USceneComponent* Root = Actor.GetRootComponent();
        if (Mesh == nullptr || Root == nullptr)
        {
            UE_LOG(LogCMLGameplayBootstrap, Warning,
                TEXT("Missing reconstructed migration asset %s"), AssetPath);
            return nullptr;
        }
        UStaticMeshComponent* Component = NewObject<UStaticMeshComponent>(&Actor, ComponentName);
        Component->SetupAttachment(Root);
        Component->SetStaticMesh(Mesh);
        Component->SetRelativeLocation(RelativeLocation);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Component->RegisterComponent();
        Actor.AddInstanceComponent(Component);
        return Component;
    }

    USceneComponent* RebuildMigratedStationVisual(
        AActor& Actor, const ECMLGameplayTargetKind Kind)
    {
        using namespace CMLContentIds;
        if (Kind == ECMLGameplayTargetKind::Workbench)
            return FCMLBuildableVisuals::RebuildMigratedVisual(Actor, WorkbenchItem);
        if (Kind == ECMLGameplayTargetKind::WoodenCrate)
            return FCMLBuildableVisuals::RebuildMigratedVisual(Actor, WoodenCrateItem);
        if (Kind == ECMLGameplayTargetKind::CrudeFurnace)
            return FCMLBuildableVisuals::RebuildMigratedVisual(Actor, CrudeFurnaceItem);
        return nullptr;
    }

    void ConfigureAirshipActor(AActor& Actor, const FCMLStableId& SourceId)
    {
        TArray<UStaticMeshComponent*> OldMeshes;
        Actor.GetComponents(OldMeshes);
        for (UStaticMeshComponent* OldMesh : OldMeshes)
        {
            if (OldMesh != nullptr)
            {
                OldMesh->SetVisibility(false, true);
                OldMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
            }
        }
        UStaticMeshComponent* AirshipVisual = AddReplacementMesh(
            Actor, TEXT("CML_AirshipVisual"),
            TEXT("/Game/_Project/Art/Vehicles/Airship/SM_Airship_RuntimeVisual.SM_Airship_RuntimeVisual"));
        if (AirshipVisual != nullptr)
        {
            AirshipVisual->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
            AirshipVisual->SetCollisionResponseToAllChannels(ECR_Block);
        }
        UStaticMeshComponent* Door = AddReplacementMesh(
            Actor, TEXT("CML_AirshipDoor"),
            TEXT("/Game/_Project/Art/Vehicles/Airship/SM_Airship_RuntimeDoor.SM_Airship_RuntimeDoor"),
            // Blender hinge (1.43, -.40, 1.30) in the imported mesh basis.
            FVector(143.0f, 40.0f, 130.0f));

        UBoxComponent* HelmBox = NewObject<UBoxComponent>(&Actor, TEXT("CML_AirshipHelmBounds"));
        HelmBox->SetupAttachment(Actor.GetRootComponent());
        HelmBox->SetRelativeLocation(FVector(0.0f, 330.0f, 114.0f));
        HelmBox->SetBoxExtent(FVector(58.0f, 72.0f, 82.0f));
        HelmBox->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
        HelmBox->SetCollisionResponseToAllChannels(ECR_Ignore);
        HelmBox->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
        HelmBox->RegisterComponent();
        Actor.AddInstanceComponent(HelmBox);

        UCMLGameplayTargetComponent* HelmTarget = NewObject<UCMLGameplayTargetComponent>(
            &Actor, TEXT("CML_AirshipHelmTarget"));
        HelmTarget->Configure(ECMLGameplayTargetKind::AirshipPilotStation, SourceId);
        HelmTarget->ConfigureInteractionAnchor(HelmBox);
        HelmTarget->RegisterComponent();
        Actor.AddInstanceComponent(HelmTarget);

        if (Door != nullptr)
        {
            UBoxComponent* DoorBox = NewObject<UBoxComponent>(&Actor, TEXT("CML_AirshipDoorBounds"));
            DoorBox->SetupAttachment(Door);
            // Imported door bounds relative to its authored hinge.
            DoorBox->SetRelativeLocation(FVector(-3.8f, -56.0f, 3.0f));
            DoorBox->SetBoxExtent(FVector(14.0f, 58.0f, 93.0f));
            DoorBox->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
            DoorBox->SetCollisionResponseToAllChannels(ECR_Ignore);
            DoorBox->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
            DoorBox->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
            DoorBox->RegisterComponent();
            Actor.AddInstanceComponent(DoorBox);

            UCMLGameplayTargetComponent* DoorTarget = NewObject<UCMLGameplayTargetComponent>(
                &Actor, TEXT("CML_AirshipDoorTarget"));
            DoorTarget->Configure(ECMLGameplayTargetKind::AirshipDoor, SourceId);
            DoorTarget->ConfigureInteractionAnchor(DoorBox);
            DoorTarget->RegisterComponent();
            // AIR_Airship.glb is authored in the open pose. Unity closes it
            // by -100 degrees around vehicle up and treats identity as open.
            DoorTarget->ConfigureHingedPart(
                Door,
                FRotator(0.0f, 100.0f, 0.0f),
                FRotator::ZeroRotator,
                false,
                0.45f);
            Actor.AddInstanceComponent(DoorTarget);
        }
    }

    FCMLMachineBuildPose BuildPoseFor(const AActor& Actor)
    {
        const FVector Location = Actor.GetActorLocation();
        FCMLMachineBuildPose Pose;
        // Canonical poses retain Unity axes (X right, Y up, Z forward), while
        // imported Unreal actors use (Unity Z, Unity X, Unity Y).
        Pose.XMillimetres = FMath::RoundToInt64(Location.Y * 10.0);
        Pose.YMillimetres = FMath::RoundToInt64(Location.Z * 10.0);
        Pose.ZMillimetres = FMath::RoundToInt64(Location.X * 10.0);
        const int32 UnityQuarterTurns = FMath::RoundToInt(-Actor.GetActorRotation().Yaw / 90.0f);
        Pose.YawQuarterTurns = ((UnityQuarterTurns % 4) + 4) % 4;
        return Pose;
    }

    FCMLStableId StableWorldNodeId(const AActor& Actor)
    {
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
        return FCMLStableId(0x7100000000000000ULL, Hash == 0 ? 1 : Hash);
    }

    FCMLAirshipPose AirshipPoseFor(const AActor& Actor)
    {
        const FVector Location = Actor.GetActorLocation();
        FCMLAirshipPose Pose;
        Pose.Position.X = FMath::RoundToInt64(Location.Y * 10.0);
        Pose.Position.Y = FMath::RoundToInt64(Location.Z * 10.0);
        Pose.Position.Z = FMath::RoundToInt64(Location.X * 10.0);
        const double UnityYawDegrees = -static_cast<double>(Actor.GetActorRotation().Yaw);
        const int64 Turn = FMath::RoundToInt64(UnityYawDegrees * 65536.0 / 360.0);
        Pose.YawTurn = static_cast<int32>(static_cast<uint16>(Turn));
        return Pose;
    }

    uint64 ReadU64(const TArray<uint8>& Bytes, const int32 Offset)
    {
        uint64 Value = 0;
        for (int32 Index = 0; Index < 8; ++Index)
        {
            Value = (Value << 8) | Bytes[Offset + Index];
        }
        return Value;
    }

    FCMLStableId ReadStableId(const TArray<uint8>& Bytes, const int32 Offset)
    {
        return FCMLStableId(ReadU64(Bytes, Offset), ReadU64(Bytes, Offset + 8));
    }

    const TCHAR* BuiltClassPath(const FCMLStableId& ItemId)
    {
        using namespace CMLContentIds;
        if (ItemId == WoodenCrateItem)
            return TEXT("/Game/Migrated/Project/Art/ManualEra/Prefabs/BP_PF_Crate.BP_PF_Crate_C");
        if (ItemId == MechanicalPressItem)
            return TEXT("/Game/Migrated/Project/Art/MechanicalEra/Prefabs/BP_PF_MechanicalPress.BP_PF_MechanicalPress_C");
        if (ItemId == CrudeFurnaceItem)
            return TEXT("/Game/Migrated/Project/Art/ManualEra/Prefabs/BP_PF_CrudeFurnace.BP_PF_CrudeFurnace_C");
        if (ItemId == MechanicalDrillItem)
            return TEXT("/Game/Migrated/Project/Resources/Machinery/BP_PF_MechanicalDrill.BP_PF_MechanicalDrill_C");
        if (ItemId == BeltFunnel)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_Funnel.BP_PF_Belt_Funnel_C");
        if (ItemId == BeltStraight)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_Straight.BP_PF_Belt_Straight_C");
        if (ItemId == BeltCurve)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_Curve.BP_PF_Belt_Curve_C");
        if (ItemId == BeltCurveLeft)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_CurveLeft.BP_PF_Belt_CurveLeft_C");
        if (ItemId == BeltIncline)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_Incline.BP_PF_Belt_Incline_C");
        if (ItemId == BeltSupport)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_Support.BP_PF_Belt_Support_C");
        if (ItemId == BeltDriveUnit)
            return TEXT("/Game/Migrated/Project/Art/Logistics/BeltKit/Prefabs/BP_PF_Belt_DriveUnit.BP_PF_Belt_DriveUnit_C");
        return nullptr;
    }

    ECMLGameplayTargetKind BuiltTargetKind(const FCMLStableId& ItemId)
    {
        using namespace CMLContentIds;
        if (ItemId == WoodenCrateItem) return ECMLGameplayTargetKind::WoodenCrate;
        if (ItemId == MechanicalPressItem) return ECMLGameplayTargetKind::MechanicalPress;
        if (ItemId == CrudeFurnaceItem) return ECMLGameplayTargetKind::CrudeFurnace;
        if (ItemId == MechanicalDrillItem) return ECMLGameplayTargetKind::MechanicalDrill;
        if (ItemId == BeltFunnel) return ECMLGameplayTargetKind::FactoryFunnel;
        if (ItemId == BeltStraight || ItemId == BeltCurve || ItemId == BeltCurveLeft
            || ItemId == BeltIncline || ItemId == BeltDriveUnit)
            return ECMLGameplayTargetKind::FactoryBelt;
        return ECMLGameplayTargetKind::None;
    }

    FString BuiltDisplayName(const FCMLStableId& ItemId)
    {
        using namespace CMLContentIds;
        if (ItemId == WoodenCrateItem) return TEXT("Cassa di legno");
        if (ItemId == MechanicalPressItem) return TEXT("Pressa meccanica");
        if (ItemId == CrudeFurnaceItem) return TEXT("Fornace rudimentale");
        if (ItemId == MechanicalDrillItem) return TEXT("Trivella meccanica");
        if (ItemId == BeltFunnel) return TEXT("Imbuto del nastro");
        if (ItemId == BeltStraight) return TEXT("Nastro dritto");
        if (ItemId == BeltCurve) return TEXT("Nastro curvo destro");
        if (ItemId == BeltCurveLeft) return TEXT("Nastro curvo sinistro");
        if (ItemId == BeltIncline) return TEXT("Nastro inclinato");
        if (ItemId == BeltSupport) return TEXT("Supporto del nastro");
        if (ItemId == BeltDriveUnit) return TEXT("Unità motrice del nastro");
        return TEXT("Struttura");
    }
}

bool UCMLGameplayBootstrapSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
    const UWorld* World = Cast<UWorld>(Outer);
    return World != nullptr && World->IsGameWorld()
        && World->GetMapName().Contains(TEXT("A_10_StarterIsland_AxisPreview"));
}

void UCMLGameplayBootstrapSubsystem::OnWorldBeginPlay(UWorld& InWorld)
{
    // The opening's second half runs here rather than in the cinematic map:
    // the wreck falls past this level's own ancient portal and ploughs to a
    // stop on its ground, which is only possible in the world that has them.
    if (UCMLGameInstance* Instance = InWorld.GetGameInstance<UCMLGameInstance>();
        Instance != nullptr && Instance->bIntroArrivalPending)
    {
        Instance->bIntroArrivalPending = false;
        FActorSpawnParameters Parameters;
        Parameters.SpawnCollisionHandlingOverride =
            ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
        InWorld.SpawnActor<ACMLIntroArrival>(
            ACMLIntroArrival::StaticClass(), FTransform::Identity, Parameters);
    }

    Super::OnWorldBeginPlay(InWorld);
    if (UCMLSimulationSubsystem* Simulation = InWorld.GetSubsystem<UCMLSimulationSubsystem>())
    {
        Simulation->OnRuntimeCommandResolved.AddUObject(
            this, &UCMLGameplayBootstrapSubsystem::HandleRuntimeCommandResolved);
    }
    // The local pawn is produced by GameMode during BeginPlay. Deferring one
    // frame makes its transform the authoritative centre of the cold-start ring.
    InWorld.GetTimerManager().SetTimerForNextTick(
        FTimerDelegate::CreateUObject(this, &UCMLGameplayBootstrapSubsystem::BootstrapWorld));
}

void UCMLGameplayBootstrapSubsystem::Deinitialize()
{
    if (UWorld* World = GetWorld())
    {
        if (UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>())
        {
            Simulation->OnRuntimeCommandResolved.RemoveAll(this);
        }
    }
    Super::Deinitialize();
}

void UCMLGameplayBootstrapSubsystem::HandleRuntimeCommandResolved(
    const FCMLSimulationCommand& Command,
    const bool bSucceeded,
    const bool bWorldCommitted)
{
    if (Command.Kind != TEXT("BuildNode") || Command.Payload.Num() < 60)
    {
        return;
    }
    UWorld* World = GetWorld();
    APlayerController* PlayerController = World != nullptr
        ? UGameplayStatics::GetPlayerController(World, 0) : nullptr;
    ACMLHUD* HUD = PlayerController != nullptr ? Cast<ACMLHUD>(PlayerController->GetHUD()) : nullptr;
    if (!bSucceeded || !bWorldCommitted)
    {
        FVector UnusedVisualLocation;
        if (World != nullptr)
        {
            if (UCMLSimulationSubsystem* Simulation =
                    World->GetSubsystem<UCMLSimulationSubsystem>())
            {
                Simulation->ConsumePendingBuildVisual(Command, UnusedVisualLocation);
            }
        }
        if (HUD != nullptr)
        {
            HUD->PushCollectionFeed(TEXT("Costruzione rifiutata: controlla materiali e spazio"));
        }
        return;
    }

    const FCMLStableId ItemId = ReadStableId(Command.Payload, 0);
    UClass* ActorClass = FCMLBuildableVisuals::LoadActorClass(ItemId);
    if (World == nullptr || ActorClass == nullptr)
    {
        UE_LOG(LogCMLGameplayBootstrap, Error,
            TEXT("A committed build has no migrated visual class (%s)."), *ItemId.ToString());
        return;
    }

    const int64 UnityX = static_cast<int64>(ReadU64(Command.Payload, 16));
    const int64 UnityY = static_cast<int64>(ReadU64(Command.Payload, 24));
    const int64 UnityZ = static_cast<int64>(ReadU64(Command.Payload, 32));
    const uint32 YawBits = (static_cast<uint32>(Command.Payload[40]) << 24)
        | (static_cast<uint32>(Command.Payload[41]) << 16)
        | (static_cast<uint32>(Command.Payload[42]) << 8)
        | static_cast<uint32>(Command.Payload[43]);
    const int32 UnityYaw = static_cast<int32>(YawBits);
    FCMLMachineBuildPose Pose;
    Pose.XMillimetres = UnityX;
    Pose.YMillimetres = UnityY;
    Pose.ZMillimetres = UnityZ;
    Pose.YawQuarterTurns = UnityYaw;
    FVector Location = FCMLBuildableVisuals::WorldLocation(Pose);
    if (UCMLSimulationSubsystem* Simulation =
            World->GetSubsystem<UCMLSimulationSubsystem>())
    {
        Simulation->ConsumePendingBuildVisual(Command, Location);
    }
    const FRotator Rotation = FCMLBuildableVisuals::WorldRotation(ItemId, Pose);
    FActorSpawnParameters Parameters;
    Parameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    Parameters.ObjectFlags |= RF_Transient;
    AActor* Actor = World->SpawnActor<AActor>(ActorClass, Location, Rotation, Parameters);
    if (Actor == nullptr)
    {
        return;
    }
    Actor->Tags.Add(TEXT("CML.RuntimeConstruction"));
    FCMLBuildableVisuals::ConfigureCommittedCollision(*Actor);
    const ECMLGameplayTargetKind Kind = BuiltTargetKind(ItemId);
    if (Kind != ECMLGameplayTargetKind::None)
    {
        USceneComponent* HingedPart = RebuildMigratedStationVisual(*Actor, Kind);
        if (UCMLGameplayTargetComponent* Target =
                AttachTarget(*Actor, Kind, Command.DestinationId);
            Target != nullptr && HingedPart != nullptr)
        {
            Target->ConfigureHingedPart(
                HingedPart,
                FRotator::ZeroRotator,
                FRotator(0.0f, 0.0f, 105.0f),
                false,
                0.32f);
        }
    }
    if (HUD != nullptr)
    {
        const FString DisplayName = FCMLBuildableVisuals::DisplayName(ItemId);
        HUD->PushCollectionFeed(FString::Printf(TEXT("Costruito: %s"), *DisplayName));
    }
}

void UCMLGameplayBootstrapSubsystem::BootstrapWorld()
{
    AttachKnownStationTargets();
    ConnectAuthoredGatherables();
    ConnectStarterMiningSources();
    ConnectStarterTrees();
}

void UCMLGameplayBootstrapSubsystem::AttachKnownStationTargets()
{
    UWorld* World = GetWorld();
    if (World == nullptr)
    {
        return;
    }
    int32 Attached = 0;
    UCMLSimulationSubsystem* Simulation = World->GetSubsystem<UCMLSimulationSubsystem>();
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (Actor == nullptr)
        {
            continue;
        }
        // The arrival owns a transient render-only airship. It must never be
        // rebuilt as an interactable gameplay station: doing so hides the
        // component that carries its first-person cockpit during the crash.
        if (Actor->ActorHasTag(TEXT("CMLIntroWreck")))
        {
            continue;
        }
        FString Identity = Actor->GetName() + TEXT("|") + Actor->GetClass()->GetName();
        // The Starter Island airship is deliberately a plain StaticMeshActor,
        // so neither its runtime name nor class says what it is. Asset paths do
        // survive packaging and provide the same stable identity there.
        TArray<UStaticMeshComponent*> MeshComponents;
        Actor->GetComponents(MeshComponents);
        for (const UStaticMeshComponent* MeshComponent : MeshComponents)
        {
            if (const UStaticMesh* Mesh = MeshComponent != nullptr
                    ? MeshComponent->GetStaticMesh() : nullptr)
            {
                Identity += TEXT("|") + Mesh->GetPathName();
            }
        }
        const ECMLGameplayTargetKind Kind = ClassifyStation(Identity);
        if (Kind == ECMLGameplayTargetKind::None)
        {
            continue;
        }
        const FCMLStableId NodeId = StableWorldNodeId(*Actor);
        if (Kind == ECMLGameplayTargetKind::AirshipRepair)
        {
            ConfigureAirshipActor(*Actor, NodeId);
        }
        else
        {
            USceneComponent* HingedPart = RebuildMigratedStationVisual(*Actor, Kind);
            if (UCMLGameplayTargetComponent* Target = AttachTarget(*Actor, Kind, NodeId);
                Target != nullptr && HingedPart != nullptr)
            {
                Target->ConfigureHingedPart(
                    HingedPart,
                    FRotator::ZeroRotator,
                    FRotator(0.0f, 0.0f, 105.0f),
                    false,
                    0.32f);
            }
        }
        if (Simulation != nullptr)
        {
            const FCMLMachineBuildPose Pose = BuildPoseFor(*Actor);
            switch (Kind)
            {
            case ECMLGameplayTargetKind::WoodenCrate:
                Simulation->RegisterWorldBuffer(NodeId, CMLContentIds::WoodenCrate, Pose);
                // Unity already names these two salvaged components in the
                // repair bill but explicitly has no cable recipe yet. The
                // authored starter crate is therefore their temporary world
                // source, keeping the full repair/flight loop completable
                // without pretending the future copper chain exists.
                Simulation->SeedWorldBufferItem(
                    NodeId, CMLContentIds::InsulatedCable, 2);
                UE_LOG(LogCMLGameplayBootstrap, Log,
                    TEXT("Seeded 2 salvaged insulated cables in the starter crate."));
                break;
            case ECMLGameplayTargetKind::CrudeFurnace:
                Simulation->RegisterWorldMachine(
                    NodeId, CMLContentIds::CrudeFurnace,
                    CMLContentIds::SmeltIronIngot, Pose);
                break;
            case ECMLGameplayTargetKind::MechanicalPress:
                Simulation->RegisterWorldMachine(
                    NodeId, CMLContentIds::MechanicalPress,
                    CMLContentIds::PressIronPlate, Pose);
                break;
            case ECMLGameplayTargetKind::MechanicalDrill:
                Simulation->RegisterWorldMachine(
                    NodeId, CMLContentIds::MechanicalDrill,
                    CMLContentIds::DrillRawIron, Pose);
                break;
            case ECMLGameplayTargetKind::AirshipRepair:
                Simulation->RegisterWorldAirship(NodeId, AirshipPoseFor(*Actor));
                break;
            default:
                break;
            }
        }
        ++Attached;
    }
    UE_LOG(LogCMLGameplayBootstrap, Log, TEXT("Connected %d migrated station actors."), Attached);
}

void UCMLGameplayBootstrapSubsystem::ConnectAuthoredGatherables()
{
    UWorld* World = GetWorld();
    APawn* Player = World != nullptr ? UGameplayStatics::GetPlayerPawn(World, 0) : nullptr;
    if (World == nullptr || Player == nullptr)
    {
        UE_LOG(LogCMLGameplayBootstrap, Warning,
            TEXT("Authored gatherables could not be connected because the local pawn is unavailable."));
        return;
    }

    struct FPebbleCandidate
    {
        AActor* Actor = nullptr;
        FString SortKey;
    };

    const FVector PlayerLocation = Player->GetActorLocation();
    TArray<FPebbleCandidate> Pebbles;
    int32 Fibres = 0;
    int32 Sticks = 0;
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (Actor == nullptr || Actor == Player
            || Actor->FindComponentByClass<UCMLGameplayTargetComponent>() != nullptr)
        {
            continue;
        }

        FString Identity = Actor->GetName() + TEXT("|") + Actor->GetClass()->GetName();
#if WITH_EDITOR
        Identity += TEXT("|") + Actor->GetActorLabel();
#endif
        TArray<UStaticMeshComponent*> Meshes;
        Actor->GetComponents(Meshes);
        for (const UStaticMeshComponent* MeshComponent : Meshes)
        {
            if (const UStaticMesh* Mesh = MeshComponent != nullptr
                ? MeshComponent->GetStaticMesh() : nullptr)
            {
                Identity += TEXT("|") + Mesh->GetPathName();
            }
        }
        const FString Lower = Identity.ToLower();

        if (Lower.Contains(TEXT("fiberplant_wild"))
            || Lower.Contains(TEXT("gather_fiber_")))
        {
            AttachTarget(*Actor, ECMLGameplayTargetKind::WildFiberTuft,
                StableWorldNodeId(*Actor), 2);
            ++Fibres;
            continue;
        }
        if (Lower.Contains(TEXT("fallensticks")))
        {
            const int32 Yield = Lower.Contains(TEXT("fallensticks_a")) ? 3 : 2;
            AttachTarget(*Actor, ECMLGameplayTargetKind::FallenSticks,
                StableWorldNodeId(*Actor), Yield);
            ++Sticks;
            continue;
        }
        if ((Lower.Contains(TEXT("dec_pathpebble_"))
                || Lower.Contains(TEXT("pathpebble")))
            && FVector::DistSquared2D(Actor->GetActorLocation(), PlayerLocation)
                <= FMath::Square(5500.0))
        {
            Pebbles.Add({Actor, Identity});
        }
    }

    // Unity walks RocksRoot in authored hierarchy order and stops at forty.
    // Sorting by the preserved actor/mesh identity gives the migrated map the
    // same stable subset without making every decorative path pebble a pickup.
    Pebbles.Sort([](const FPebbleCandidate& A, const FPebbleCandidate& B)
    {
        return A.SortKey < B.SortKey;
    });
    const int32 PebbleCount = FMath::Min(40, Pebbles.Num());
    for (int32 Index = 0; Index < PebbleCount; ++Index)
    {
        AttachTarget(*Pebbles[Index].Actor, ECMLGameplayTargetKind::LoosePebble,
            StableWorldNodeId(*Pebbles[Index].Actor), 1);
    }

    UE_LOG(LogCMLGameplayBootstrap, Log,
        TEXT("Connected authored hand-gather sources: fibre=%d sticks=%d pebbles=%d."),
        Fibres, Sticks, PebbleCount);
}

void UCMLGameplayBootstrapSubsystem::ConnectStarterMiningSources()
{
    UWorld* World = GetWorld();
    APawn* Player = World != nullptr ? UGameplayStatics::GetPlayerPawn(World, 0) : nullptr;
    if (World == nullptr || Player == nullptr)
    {
        return;
    }

    struct FCandidate
    {
        AActor* Actor = nullptr;
        double DistanceSquared = 0.0;
    };
    TArray<FCandidate> Candidates;
    const FVector PlayerLocation = Player->GetActorLocation();
    bool bHasIronDeposit = false;
    int32 DepositSurfaces = 0;
    int32 DepositRocks = 0;

    auto ConnectDepositModules = [&](AActor& Deposit, const FString& DepositIdentity)
    {
        ECMLGameplayTargetKind SurfaceKind = ECMLGameplayTargetKind::IronDepositSurface;
        ECMLGameplayTargetKind RockKind = ECMLGameplayTargetKind::IronOreRock;
        if (DepositIdentity.Contains(TEXT("copper")))
        {
            SurfaceKind = ECMLGameplayTargetKind::CopperDepositSurface;
            RockKind = ECMLGameplayTargetKind::CopperOreRock;
        }
        else if (DepositIdentity.Contains(TEXT("tin")))
        {
            SurfaceKind = ECMLGameplayTargetKind::TinDepositSurface;
            RockKind = ECMLGameplayTargetKind::TinOreRock;
        }

        // Unity's ore prefabs deliberately use G01/G02/G03 for the flat,
        // inexhaustible deposit bed. Every other MOD_* child is a finite ore
        // rock. Keeping the identity on each child means the raycast selects
        // the visible piece that was actually struck instead of treating the
        // complete prefab as one giant invisible extraction volume.
        TArray<UChildActorComponent*> ModuleComponents;
        Deposit.GetComponents(ModuleComponents);
        for (UChildActorComponent* ModuleComponent : ModuleComponents)
        {
            AActor* Module = ModuleComponent != nullptr
                ? ModuleComponent->GetChildActor() : nullptr;
            if (Module == nullptr)
            {
                continue;
            }
            const FString ModuleIdentity = ModuleComponent->GetName().ToLower();
            if (!ModuleIdentity.StartsWith(TEXT("mod_")))
            {
                continue;
            }
            const bool bFlatDepositBed = ModuleIdentity.Contains(TEXT("_g0"));
            AttachTarget(*Module, bFlatDepositBed ? SurfaceKind : RockKind,
                StableWorldNodeId(*Module), 1);
            if (bFlatDepositBed)
            {
                ++DepositSurfaces;
            }
            else
            {
                ++DepositRocks;
            }
        }
    };

    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (Actor == nullptr || Actor == Player
            || Actor->FindComponentByClass<UCMLGameplayTargetComponent>() != nullptr)
        {
            continue;
        }
        const FString ActorIdentity =
            (Actor->GetName() + TEXT("|") + Actor->GetClass()->GetName()).ToLower();
        if (ActorIdentity.Contains(TEXT("oredeposit_iron"))
            || ActorIdentity.Contains(TEXT("oredeposit_copper"))
            || ActorIdentity.Contains(TEXT("oredeposit_tin")))
        {
            ConnectDepositModules(*Actor, ActorIdentity);
            bHasIronDeposit |= ActorIdentity.Contains(TEXT("oredeposit_iron"));
            continue;
        }

        TArray<UStaticMeshComponent*> Meshes;
        Actor->GetComponents(Meshes);
        bool bClassicMineableRock = false;
        for (const UStaticMeshComponent* MeshComponent : Meshes)
        {
            const UStaticMesh* Mesh = MeshComponent != nullptr
                ? MeshComponent->GetStaticMesh() : nullptr;
            const FString MeshPath = Mesh != nullptr ? Mesh->GetPathName() : FString();
            if (MeshPath.Contains(TEXT("/Rocks/Classic/SM_BoulderClassic"))
                || MeshPath.Contains(TEXT("/Rocks/Classic/SM_RockClassic")))
            {
                bClassicMineableRock = true;
                break;
            }
        }
        if (!bClassicMineableRock)
        {
            continue;
        }
        const double DistanceSquared = FVector::DistSquared(
            Actor->GetComponentsBoundingBox(true).GetCenter(), PlayerLocation);
        if (DistanceSquared >= FMath::Square(500.0)
            && DistanceSquared <= FMath::Square(3500.0))
        {
            Candidates.Add({Actor, DistanceSquared});
        }
    }

    Candidates.Sort([](const FCandidate& A, const FCandidate& B)
    {
        return A.DistanceSquared < B.DistanceSquared;
    });
    const int32 RockCount = FMath::Min(4, Candidates.Num());
    for (int32 Index = 0; Index < RockCount; ++Index)
    {
        AttachTarget(
            *Candidates[Index].Actor,
            ECMLGameplayTargetKind::EnvironmentalStone,
            FCMLStableId(0x7600000000000000ULL, static_cast<uint64>(Index + 1)));
    }

    if (!bHasIronDeposit)
    {
        const TCHAR* DepositClassPath =
            TEXT("/Game/Migrated/Project/Art/Environment/OreDeposit/Prefabs/BP_PF_OreDeposit_Iron_A.BP_PF_OreDeposit_Iron_A_C");
        UClass* DepositClass = LoadClass<AActor>(nullptr, DepositClassPath);
        if (DepositClass != nullptr)
        {
            FVector Location = PlayerLocation
                + Player->GetActorForwardVector().GetSafeNormal2D() * 1450.0f
                + Player->GetActorRightVector().GetSafeNormal2D() * 480.0f
                + FVector(0.0f, 0.0f, 900.0f);
            FHitResult GroundHit;
            FCollisionQueryParams Query(SCENE_QUERY_STAT(CMLDepositGround), true, Player);
            if (World->LineTraceSingleByChannel(
                    GroundHit, Location, Location - FVector(0.0f, 0.0f, 3500.0f),
                    ECC_Visibility, Query))
            {
                Location = GroundHit.ImpactPoint;
            }
            FActorSpawnParameters Params;
            Params.SpawnCollisionHandlingOverride =
                ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
            Params.ObjectFlags |= RF_Transient;
            if (AActor* Deposit = World->SpawnActor<AActor>(
                    DepositClass, Location, FRotator(0.0f, 37.0f, 0.0f), Params))
            {
                Deposit->Tags.Add(TEXT("CML.RuntimeIronDeposit"));
                ConnectDepositModules(*Deposit, TEXT("oredeposit_iron"));
                bHasIronDeposit = true;
            }
        }
    }

    UE_LOG(LogCMLGameplayBootstrap, Log,
        TEXT("Connected %d mineable Classic rocks and ore deposits with %d finite rocks / %d infinite G0 surfaces; iron deposit available: %s."),
        RockCount, DepositRocks, DepositSurfaces,
        bHasIronDeposit ? TEXT("yes") : TEXT("no"));
}

void UCMLGameplayBootstrapSubsystem::ConnectStarterTrees()
{
    UWorld* World = GetWorld();
    APawn* Player = World != nullptr ? UGameplayStatics::GetPlayerPawn(World, 0) : nullptr;
    if (World == nullptr || Player == nullptr)
    {
        return;
    }
    struct FTreeCandidate
    {
        AActor* Actor = nullptr;
        double DistanceSquared = 0.0;
    };
    TArray<FTreeCandidate> Candidates;
    const FVector PlayerLocation = Player->GetActorLocation();
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        AActor* Actor = *It;
        if (Actor == nullptr
            || Actor->FindComponentByClass<UCMLGameplayTargetComponent>() != nullptr)
        {
            continue;
        }
        const FString Identity =
            (Actor->GetName() + TEXT("|") + Actor->GetClass()->GetName()).ToLower();
        if (!Identity.Contains(TEXT("pf_env_tree"))
            && !Identity.Contains(TEXT("cloudtall")))
        {
            continue;
        }
        const double DistanceSquared = FVector::DistSquared(
            Actor->GetComponentsBoundingBox(true).GetCenter(), PlayerLocation);
        Candidates.Add({Actor, DistanceSquared});
    }
    Candidates.Sort([](const FTreeCandidate& A, const FTreeCandidate& B)
    {
        return A.DistanceSquared < B.DistanceSquared;
    });
    // Trees are gameplay resources, not an eight-object demo subset. Attach
    // the same lightweight component and trunk collider to every authored
    // instance so solidity and chopping never depend on distance from spawn.
    const int32 TreeCount = Candidates.Num();
    for (int32 Index = 0; Index < TreeCount; ++Index)
    {
        const FCMLStableId WorldId = StableWorldNodeId(*Candidates[Index].Actor);
        const FCMLStableId SourceId(0x7700000000000000ULL, WorldId.Low);
        AttachTarget(
            *Candidates[Index].Actor,
            ECMLGameplayTargetKind::FellableTree,
            SourceId,
            3 + static_cast<int32>(SourceId.Low % 3ULL));
    }
    UE_LOG(LogCMLGameplayBootstrap, Log,
        TEXT("Connected all %d authored trees to collision and wood harvesting."), TreeCount);
}

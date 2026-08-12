#include "Building/CMLBuildableVisuals.h"

#include "Components/MeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Content/CMLContentIds.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/Actor.h"
#include "Materials/MaterialInterface.h"

namespace
{
    UStaticMeshComponent* AddRuntimeMesh(
        AActor& Actor,
        const TCHAR* ComponentName,
        const TCHAR* AssetPath,
        const FVector& RelativeLocation = FVector::ZeroVector)
    {
        UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, AssetPath);
        USceneComponent* Root = Actor.GetRootComponent();
        if (Mesh == nullptr || Root == nullptr)
        {
            return nullptr;
        }
        UStaticMeshComponent* Component = NewObject<UStaticMeshComponent>(
            &Actor, ComponentName);
        Component->SetupAttachment(Root);
        Component->SetMobility(Root->Mobility);
        Component->SetStaticMesh(Mesh);
        Component->SetRelativeLocation(RelativeLocation);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Component->RegisterComponent();
        Actor.AddInstanceComponent(Component);
        return Component;
    }
}

bool FCMLBuildableVisuals::IsBuildable(const FCMLStableId& ItemId)
{
    return ClassPath(ItemId) != nullptr;
}

const TCHAR* FCMLBuildableVisuals::ClassPath(const FCMLStableId& ItemId)
{
    using namespace CMLContentIds;
    if (ItemId == WoodenCrateItem)
        return TEXT("/Game/Migrated/Project/Art/ManualEra/Prefabs/BP_PF_Crate.BP_PF_Crate_C");
    if (ItemId == MechanicalPressItem)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_MechanicalPress_Runtime.BP_MechanicalPress_Runtime_C");
    if (ItemId == CrudeFurnaceItem)
        return TEXT("/Game/Migrated/Project/Art/ManualEra/Prefabs/BP_PF_CrudeFurnace.BP_PF_CrudeFurnace_C");
    if (ItemId == MechanicalDrillItem)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_MechanicalDrill_Runtime.BP_MechanicalDrill_Runtime_C");
    if (ItemId == BeltFunnel)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltFunnel_Runtime.BP_BeltFunnel_Runtime_C");
    if (ItemId == BeltStraight)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltStraight_Runtime.BP_BeltStraight_Runtime_C");
    // The Unity art audit proved that the two exported curve names have their
    // handedness reversed.  Bind by shape, not by filename.
    if (ItemId == BeltCurve)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltCurveLeft_Runtime.BP_BeltCurveLeft_Runtime_C");
    if (ItemId == BeltCurveLeft)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltCurve_Runtime.BP_BeltCurve_Runtime_C");
    if (ItemId == BeltIncline)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltIncline_Runtime.BP_BeltIncline_Runtime_C");
    if (ItemId == BeltSupport)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltSupport_Runtime.BP_BeltSupport_Runtime_C");
    if (ItemId == BeltDriveUnit)
        return TEXT("/Game/_Project/Art/Factory/Buildables/BP_BeltDriveUnit_Runtime.BP_BeltDriveUnit_Runtime_C");
    return nullptr;
}

UClass* FCMLBuildableVisuals::LoadActorClass(const FCMLStableId& ItemId)
{
    const TCHAR* Path = ClassPath(ItemId);
    return Path != nullptr ? LoadClass<AActor>(nullptr, Path) : nullptr;
}

FString FCMLBuildableVisuals::DisplayName(const FCMLStableId& ItemId)
{
    using namespace CMLContentIds;
    if (ItemId == WoodenCrateItem) return TEXT("Cassa di legno");
    if (ItemId == MechanicalPressItem) return TEXT("Pressa meccanica");
    if (ItemId == CrudeFurnaceItem) return TEXT("Fornace rudimentale");
    if (ItemId == MechanicalDrillItem) return TEXT("Trivella meccanica");
    if (ItemId == BeltFunnel) return TEXT("Imbuto del nastro");
    if (ItemId == BeltStraight) return TEXT("Nastro trasportatore");
    if (ItemId == BeltCurve) return TEXT("Curva destra");
    if (ItemId == BeltCurveLeft) return TEXT("Curva sinistra");
    if (ItemId == BeltIncline) return TEXT("Nastro inclinato");
    if (ItemId == BeltSupport) return TEXT("Supporto del nastro");
    if (ItemId == BeltDriveUnit) return TEXT("Unita motrice del nastro");
    return TEXT("Struttura");
}

ECMLMachineBuildKind FCMLBuildableVisuals::BuildKind(const FCMLStableId& ItemId)
{
    using namespace CMLContentIds;
    if (ItemId == WoodenCrateItem) return ECMLMachineBuildKind::Buffer;
    if (ItemId == BeltFunnel) return ECMLMachineBuildKind::Funnel;
    if (ItemId == MechanicalPressItem || ItemId == CrudeFurnaceItem
        || ItemId == MechanicalDrillItem)
    {
        return ECMLMachineBuildKind::Machine;
    }
    if (ItemId == BeltStraight || ItemId == BeltCurve || ItemId == BeltCurveLeft
        || ItemId == BeltIncline || ItemId == BeltDriveUnit)
    {
        return ECMLMachineBuildKind::BeltModule;
    }
    return ECMLMachineBuildKind::None;
}

ECMLMachineNodeKind FCMLBuildableVisuals::NodeKind(const FCMLStableId& ItemId)
{
    return static_cast<ECMLMachineNodeKind>(BuildKind(ItemId));
}

FCMLStableId FCMLBuildableVisuals::DefinitionId(const FCMLStableId& ItemId)
{
    using namespace CMLContentIds;
    if (ItemId == WoodenCrateItem) return WoodenCrate;
    if (ItemId == MechanicalPressItem) return MechanicalPress;
    if (ItemId == CrudeFurnaceItem) return CrudeFurnace;
    if (ItemId == MechanicalDrillItem) return MechanicalDrill;
    return ItemId;
}

FVector FCMLBuildableVisuals::WorldLocation(const FCMLMachineBuildPose& Pose)
{
    return FVector(
        static_cast<double>(Pose.ZMillimetres) / 10.0,
        static_cast<double>(Pose.XMillimetres) / 10.0,
        static_cast<double>(Pose.YMillimetres) / 10.0);
}

FRotator FCMLBuildableVisuals::WorldRotation(
    const FCMLStableId& ItemId, const FCMLMachineBuildPose& Pose)
{
    using namespace CMLContentIds;
    float VisualOffset = 0.0f;
    if (ItemId == BeltFunnel || ItemId == BeltIncline)
    {
        VisualOffset = 180.0f;
    }
    return FRotator(0.0f, -90.0f * Pose.YawQuarterTurns - VisualOffset, 0.0f);
}

USceneComponent* FCMLBuildableVisuals::RebuildMigratedVisual(
    AActor& Actor, const FCMLStableId& ItemId)
{
    using namespace CMLContentIds;
    if (ItemId != WorkbenchItem && ItemId != WoodenCrateItem
        && ItemId != CrudeFurnaceItem)
    {
        return nullptr;
    }

    TArray<UStaticMeshComponent*> ExistingMeshes;
    Actor.GetComponents(ExistingMeshes);
    for (UStaticMeshComponent* Mesh : ExistingMeshes)
    {
        if (Mesh == nullptr)
        {
            continue;
        }
        if (Mesh->GetFName() == TEXT("CML_CrateLid"))
        {
            return Mesh;
        }
        if (Mesh->GetName().StartsWith(TEXT("CML_")))
        {
            return nullptr;
        }
    }
    for (UStaticMeshComponent* Mesh : ExistingMeshes)
    {
        if (Mesh != nullptr)
        {
            Mesh->SetVisibility(false, true);
            Mesh->SetHiddenInGame(true, true);
        }
    }

    if (ItemId == WorkbenchItem)
    {
        AddRuntimeMesh(Actor, TEXT("CML_WorkbenchVisual"),
            TEXT("/Game/_Project/Art/ManualEra/SM_Workbench_RuntimeVisual.SM_Workbench_RuntimeVisual"));
        return nullptr;
    }
    if (ItemId == CrudeFurnaceItem)
    {
        AddRuntimeMesh(Actor, TEXT("CML_CrudeFurnaceVisual"),
            TEXT("/Game/_Project/Art/ManualEra/SM_CrudeFurnace_RuntimeVisual.SM_CrudeFurnace_RuntimeVisual"));
        return nullptr;
    }

    AddRuntimeMesh(Actor, TEXT("CML_CrateBody"),
        TEXT("/Game/_Project/Art/ManualEra/SM_Crate_RuntimeBody.SM_Crate_RuntimeBody"));
    return AddRuntimeMesh(Actor, TEXT("CML_CrateLid"),
        TEXT("/Game/_Project/Art/ManualEra/SM_Crate_RuntimeLid.SM_Crate_RuntimeLid"),
        // The hinge is on the opposite Y edge from the broken migration.
        // This location cancels the baked pivot offset while the lid is shut.
        FVector(0.0f, -35.0f, 54.0f));
}

void FCMLBuildableVisuals::ConfigureHologram(AActor& Actor, const bool bValid)
{
    static UMaterialInterface* Valid = LoadObject<UMaterialInterface>(
        nullptr,
        TEXT("/Game/Migrated/Project/Art/FactoryTest/Materials/M_M04B_HologramValid.M_M04B_HologramValid"));
    static UMaterialInterface* Invalid = LoadObject<UMaterialInterface>(
        nullptr,
        TEXT("/Game/Migrated/Project/Art/FactoryTest/Materials/M_M04B_HologramInvalid.M_M04B_HologramInvalid"));
    UMaterialInterface* Material = bValid ? Valid : Invalid;

    Actor.SetActorEnableCollision(false);
    Actor.SetActorTickEnabled(false);
    TArray<UMeshComponent*> Meshes;
    Actor.GetComponents(Meshes);
    for (UMeshComponent* Mesh : Meshes)
    {
        if (Mesh == nullptr) continue;
        Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Mesh->SetGenerateOverlapEvents(false);
        Mesh->SetCastShadow(false);
        Mesh->SetRenderCustomDepth(false);
        if (Material != nullptr)
        {
            for (int32 Index = 0; Index < Mesh->GetNumMaterials(); ++Index)
            {
                Mesh->SetMaterial(Index, Material);
            }
        }
    }
}

void FCMLBuildableVisuals::ConfigureCommittedCollision(AActor& Actor)
{
    Actor.SetActorEnableCollision(true);
    TArray<UMeshComponent*> Meshes;
    Actor.GetComponents(Meshes);
    for (UMeshComponent* Mesh : Meshes)
    {
        if (Mesh == nullptr) continue;
        Mesh->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        Mesh->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
        Mesh->SetCollisionResponseToChannel(ECC_Pawn, ECR_Block);
    }
}

#include "Building/CMLBuildableVisuals.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/Actor.h"
#include "Materials/MaterialInterface.h"
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLBuildableVisualsTest,
    "CML.Gameplay.Building.BuildableVisuals",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLBuildableVisualsTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLStableId Buildables[] = {
        WoodenCrateItem,
        MechanicalPressItem,
        CrudeFurnaceItem,
        MechanicalDrillItem,
        BeltFunnel,
        BeltStraight,
        BeltCurve,
        BeltCurveLeft,
        BeltIncline,
        BeltSupport,
        BeltDriveUnit};
    for (const FCMLStableId& ItemId : Buildables)
    {
        UClass* ActorClass = FCMLBuildableVisuals::LoadActorClass(ItemId);
        TestTrue(TEXT("Every buildable has a migrated actor class"), ActorClass != nullptr);
        TestFalse(TEXT("Every buildable has a player-facing name"),
            FCMLBuildableVisuals::DisplayName(ItemId).IsEmpty());
    }

    struct FCompleteVisualExpectation
    {
        const TCHAR* Path;
        double MinimumX;
        double MinimumYOrZ;
    };
    const FCompleteVisualExpectation CompleteVisuals[] = {
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_MechanicalPress_RuntimeVisual.SM_MechanicalPress_RuntimeVisual"), 175.0, 140.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_MechanicalDrill_RuntimeVisual.SM_MechanicalDrill_RuntimeVisual"), 120.0, 125.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltFunnel_RuntimeVisual.SM_BeltFunnel_RuntimeVisual"), 75.0, 45.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltStraight_RuntimeVisual.SM_BeltStraight_RuntimeVisual"), 90.0, 60.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltCurveLeft_RuntimeVisual.SM_BeltCurveLeft_RuntimeVisual"), 90.0, 60.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltCurve_RuntimeVisual.SM_BeltCurve_RuntimeVisual"), 90.0, 60.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltIncline_RuntimeVisual.SM_BeltIncline_RuntimeVisual"), 90.0, 90.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltSupport_RuntimeVisual.SM_BeltSupport_RuntimeVisual"), 90.0, 50.0},
        {TEXT("/Game/_Project/Art/Factory/Buildables/SM_BeltDriveUnit_RuntimeVisual.SM_BeltDriveUnit_RuntimeVisual"), 90.0, 60.0},
    };
    const FCMLStableId CompleteBuildables[] = {
        MechanicalPressItem,
        MechanicalDrillItem,
        BeltFunnel,
        BeltStraight,
        BeltCurve,
        BeltCurveLeft,
        BeltIncline,
        BeltSupport,
        BeltDriveUnit,
    };
    for (const FCompleteVisualExpectation& Expected : CompleteVisuals)
    {
        const UStaticMesh* Mesh = LoadObject<UStaticMesh>(nullptr, Expected.Path);
        TestNotNull(TEXT("Every complex placeable has its complete runtime mesh"), Mesh);
        if (Mesh != nullptr)
        {
            const FVector Size = Mesh->GetBoundingBox().GetSize();
            TestTrue(TEXT("The complete visual has its expected X extent"),
                Size.X >= Expected.MinimumX);
            TestTrue(TEXT("The complete visual has its expected transverse/vertical extent"),
                FMath::Max(Size.Y, Size.Z) >= Expected.MinimumYOrZ);
            TestTrue(TEXT("The complete visual retains authored material slots"),
                Mesh->GetStaticMaterials().Num() >= 2);
        }
    }

    UWorld* TestWorld = UWorld::CreateWorld(EWorldType::Game, false);
    TestNotNull(TEXT("A runtime world can be created for placeable validation"), TestWorld);
    if (TestWorld != nullptr)
    {
        for (const FCMLStableId& ItemId : CompleteBuildables)
        {
            UClass* ActorClass = FCMLBuildableVisuals::LoadActorClass(ItemId);
            AActor* Actor = ActorClass != nullptr
                ? TestWorld->SpawnActor<AActor>(ActorClass, FTransform::Identity)
                : nullptr;
            TestNotNull(TEXT("Every complete placeable Blueprint instantiates"), Actor);
            if (Actor == nullptr)
            {
                continue;
            }
            TArray<UStaticMeshComponent*> Meshes;
            Actor->GetComponents(Meshes);
            TestEqual(TEXT("Each clean runtime Blueprint instantiates one complete visual"),
                Meshes.Num(), 1);
            const FVector Size = Actor->GetComponentsBoundingBox(true).GetSize();
            TestTrue(TEXT("An instantiated complex placeable is not a stray bar"),
                Size.X >= 75.0 && (Size.Y >= 40.0 || Size.Z >= 50.0));
            Actor->Destroy();
        }
        TestWorld->DestroyWorld(false);
    }

    TestTrue(TEXT("The official valid hologram material is present"),
        LoadObject<UMaterialInterface>(nullptr,
            TEXT("/Game/Migrated/Project/Art/FactoryTest/Materials/M_M04B_HologramValid.M_M04B_HologramValid"))
            != nullptr);
    TestTrue(TEXT("The official invalid hologram material is present"),
        LoadObject<UMaterialInterface>(nullptr,
            TEXT("/Game/Migrated/Project/Art/FactoryTest/Materials/M_M04B_HologramInvalid.M_M04B_HologramInvalid"))
            != nullptr);
    TestNotNull(TEXT("The complete crate body hologram mesh is present"),
        LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/_Project/Art/ManualEra/SM_Crate_RuntimeBody.SM_Crate_RuntimeBody")));
    TestNotNull(TEXT("The hinged crate lid hologram mesh is present"),
        LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/_Project/Art/ManualEra/SM_Crate_RuntimeLid.SM_Crate_RuntimeLid")));
    TestNotNull(TEXT("The complete furnace hologram mesh is present"),
        LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/_Project/Art/ManualEra/SM_CrudeFurnace_RuntimeVisual.SM_CrudeFurnace_RuntimeVisual")));
    TestNotNull(TEXT("The complete workbench runtime mesh is present"),
        LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/_Project/Art/ManualEra/SM_Workbench_RuntimeVisual.SM_Workbench_RuntimeVisual")));

    FCMLMachineBuildPose Pose;
    Pose.XMillimetres = 1000;
    Pose.YMillimetres = 2500;
    Pose.ZMillimetres = -3000;
    Pose.YawQuarterTurns = 1;
    const FVector World = FCMLBuildableVisuals::WorldLocation(Pose);
    TestEqual(TEXT("Unity X maps to Unreal Y"), World.Y, 100.0);
    TestEqual(TEXT("Unity Y maps to Unreal Z"), World.Z, 250.0);
    TestEqual(TEXT("Unity Z maps to Unreal X"), World.X, -300.0);
    TestEqual(TEXT("Funnel retains its authored 180 degree visual binding"),
        static_cast<double>(FMath::UnwindDegrees(
            FCMLBuildableVisuals::WorldRotation(BeltFunnel, Pose).Yaw)), 90.0);
    return true;
}
#endif

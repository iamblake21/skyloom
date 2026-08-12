#include "Intro/CMLIntroDirector.h"

#include "Game/CMLGameInstance.h"

#include "Camera/CameraActor.h"
#include "Camera/CameraComponent.h"
#include "Camera/PlayerCameraManager.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Components/ActorComponent.h"
#include "Components/PrimitiveComponent.h"
#include "Engine/PostProcessVolume.h"
#include "Presentation/CMLIntroGradeState.h"
#include "Components/LightComponent.h"
#include "Components/MeshComponent.h"
#include "Components/PointLightComponent.h"
#include "Components/SceneComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/PointLight.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerController.h"
#include "HAL/IConsoleManager.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Materials/MaterialInterface.h"
#include "UI/CMLHUD.h"
#include "UObject/ConstructorHelpers.h"
#include "UObject/StructOnScope.h"
#include "UObject/UnrealType.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLIntro, Log, All);

namespace
{
    FString CanonicalIntroMemberName(FString Name)
    {
        FString Canonical;
        Canonical.Reserve(Name.Len());
        for (const TCHAR Character : Name)
        {
            if (FChar::IsAlnum(Character))
            {
                Canonical.AppendChar(FChar::ToLower(Character));
            }
        }
        return Canonical;
    }

    bool SetIntroClockParameter(FProperty& Property, void* ParameterMemory, const double Value)
    {
        void* Address = Property.ContainerPtrToValuePtr<void>(ParameterMemory);
        if (FNumericProperty* Numeric = CastField<FNumericProperty>(&Property))
        {
            if (Numeric->IsInteger())
            {
                Numeric->SetIntPropertyValue(Address, FMath::RoundToInt64(Value));
            }
            else
            {
                Numeric->SetFloatingPointPropertyValue(Address, Value);
            }
            return true;
        }
        const FString Formatted = FString::Printf(TEXT("%02d"), FMath::RoundToInt(Value));
        if (FStrProperty* String = CastField<FStrProperty>(&Property))
        {
            String->SetPropertyValue(Address, Formatted);
            return true;
        }
        if (FNameProperty* Name = CastField<FNameProperty>(&Property))
        {
            Name->SetPropertyValue(Address, FName(*Formatted));
            return true;
        }
        if (FTextProperty* Text = CastField<FTextProperty>(&Property))
        {
            Text->SetPropertyValue(Address, FText::FromString(Formatted));
            return true;
        }
        return false;
    }

    float IntroProgress(const float Elapsed, const float Duration)
    {
        return Duration > 0.0f ? FMath::Clamp(Elapsed / Duration, 0.0f, 1.0f) : 1.0f;
    }

    float IntroSmooth(float Value)
    {
        Value = FMath::Clamp(Value, 0.0f, 1.0f);
        return Value * Value * (3.0f - 2.0f * Value);
    }

    float IntroBeat(const float Elapsed, const float Period)
    {
        return FMath::Fmod(FMath::Max(Elapsed, 0.0f), Period) / Period;
    }

    FCMLIntroGradeState CockpitGrade()
    {
        FCMLIntroGradeState Grade = FCMLIntroGradeState::Cruise();
        Grade.BloomIntensity = 2.4f;
        Grade.ChromaticAberration = 0.16f;
        Grade.LensDistortion = 0.14f;
        Grade.MotionBlur = 0.5f;
        Grade.Panini = 0.3f;
        Grade.PostExposure = 0.28f;
        Grade.VignetteIntensity = 0.44f;
        return Grade;
    }

    /** The animated URP volume values from each Unity shot. */
    FCMLIntroGradeState GradeForShot(
        const ECMLIntroShot Shot, const float Elapsed,
        const FCMLIntroTimings& Timings)
    {
        FCMLIntroGradeState Grade = FCMLIntroGradeState::Cruise();
        switch (Shot)
        {
            case ECMLIntroShot::Hyperspace:
                Grade.BloomIntensity = 3.1f;
                Grade.ChromaticAberration = 0.38f;
                Grade.LensDistortion = 0.32f;
                Grade.MotionBlur = 0.66f;
                Grade.Panini = 0.52f;
                Grade.PostExposure = 0.42f;
                Grade.Contrast = 22.0f;
                Grade.VignetteIntensity = 0.42f;
                break;

            case ECMLIntroShot::Cockpit:
            case ECMLIntroShot::Flight:
                Grade = CockpitGrade();
                break;

            case ECMLIntroShot::Alarm:
            {
                const float Progress = IntroProgress(Elapsed, Timings.AlarmSeconds);
                const float Onset = IntroSmooth(FMath::Clamp(Elapsed / 0.45f, 0.0f, 1.0f));
                const float Pulse = FMath::Pow(1.0f - IntroBeat(Elapsed, 0.82f), 2.6f) * Onset;
                const float Severity = Onset * FMath::Lerp(0.6f, 1.0f, Progress);
                Grade.BloomIntensity = 2.4f + Pulse * 1.6f;
                Grade.ChromaticAberration = 0.44f + Severity * 0.3f;
                Grade.LensDistortion = 0.2f + Severity * 0.12f;
                Grade.MotionBlur = 0.5f;
                Grade.Panini = 0.35f;
                Grade.Saturation = FMath::Lerp(6.0f, -22.0f, Severity);
                Grade.Contrast = FMath::Lerp(12.0f, 26.0f, Severity);
                Grade.VignetteIntensity = 0.44f + Pulse * 0.22f;
                Grade.VignetteColor = FMath::Lerp(
                    FLinearColor::Black, FLinearColor(0.42f, 0.02f, 0.02f), Onset);
                Grade.ColorFilter = FMath::Lerp(
                    FLinearColor::White, FLinearColor(1.0f, 0.82f, 0.78f), Pulse);
                break;
            }

            case ECMLIntroShot::RiftOpen:
            {
                const float Progress = IntroProgress(Elapsed, Timings.RiftOpenSeconds);
                const float Tear = FMath::Pow(IntroSmooth(Progress), 1.9f);
                const float Alarm = FMath::Pow(1.0f - IntroBeat(Elapsed, 0.68f), 2.6f);
                Grade.BloomIntensity = 2.4f + Tear * 3.4f;
                Grade.ChromaticAberration = 0.42f + Tear * 0.4f;
                Grade.LensDistortion = 0.24f + Tear * 0.38f;
                Grade.MotionBlur = 0.55f;
                Grade.Panini = 0.42f;
                Grade.PostExposure = Tear * 0.75f;
                Grade.Contrast = 24.0f;
                Grade.Saturation = FMath::Lerp(-22.0f, 12.0f, Tear);
                Grade.VignetteIntensity = 0.46f + Alarm * 0.16f;
                Grade.VignetteColor = FMath::Lerp(
                    FLinearColor(0.42f, 0.02f, 0.02f),
                    FLinearColor(0.16f, 0.06f, 0.34f), Tear);
                break;
            }

            case ECMLIntroShot::RiftEntry:
            {
                const float Progress = IntroProgress(Elapsed, Timings.RiftEntrySeconds);
                const float Rush = FMath::Pow(Progress, 2.4f);
                Grade.BloomIntensity = 5.8f + Rush * 4.0f;
                Grade.ChromaticAberration = FMath::Lerp(0.82f, 1.0f, Rush);
                Grade.LensDistortion = FMath::Lerp(0.62f, 0.9f, Rush);
                Grade.MotionBlur = 0.8f;
                Grade.Panini = 0.5f;
                Grade.PostExposure = 0.75f + Rush * 1.9f;
                Grade.VignetteIntensity = 0.5f;
                Grade.VignetteColor = FLinearColor(0.16f, 0.06f, 0.34f);
                break;
            }

            default:
                break;
        }
        return Grade;
    }

    void ApplyPanini(const FCMLIntroGradeState& Grade)
    {
        // Unreal exposes Panini through renderer CVars. Folding a small share
        // of Unity's radial distortion into D preserves the lens compression
        // without requiring a second full-screen material pass.
        const float Distortion = FMath::Clamp(
            Grade.Panini + Grade.LensDistortion * 0.35f, 0.0f, 1.0f);
        if (IConsoleVariable* D = IConsoleManager::Get().FindConsoleVariable(
                TEXT("r.LensDistortion.Panini.D")))
        {
            D->Set(Distortion, ECVF_SetByCode);
        }
        if (IConsoleVariable* S = IConsoleManager::Get().FindConsoleVariable(
                TEXT("r.LensDistortion.Panini.S")))
        {
            S->Set(FMath::Clamp(Grade.LensDistortion * 0.18f, 0.0f, 0.2f), ECVF_SetByCode);
        }
    }
}

ACMLIntroDirector::ACMLIntroDirector()
{
    PrimaryActorTick.bCanEverTick = true;
}

AActor* ACMLIntroDirector::SpawnNamedActor(
    UClass* ActorClass, const TCHAR* Name, const FTransform& Transform)
{
    UWorld* World = GetWorld();
    if (World == nullptr || ActorClass == nullptr)
    {
        return nullptr;
    }
    FActorSpawnParameters Parameters;
    Parameters.Name = FName(Name);
    Parameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    Parameters.ObjectFlags |= RF_Transient;
    AActor* Actor = World->SpawnActor<AActor>(ActorClass, Transform, Parameters);
    if (Actor != nullptr && Actor->GetRootComponent() == nullptr)
    {
        USceneComponent* Root = NewObject<USceneComponent>(Actor, TEXT("RuntimeRoot"));
        Root->SetMobility(EComponentMobility::Movable);
        Root->RegisterComponent();
        Actor->SetRootComponent(Root);
    }
#if WITH_EDITOR
    if (Actor != nullptr) Actor->SetActorLabel(Name);
#endif
    return Actor;
}

void ACMLIntroDirector::BuildCinematicAlienSky()
{
    UWorld* World = GetWorld();
    UClass* AlienClass = LoadClass<AActor>(nullptr,
        TEXT("/Game/_Project/Art/Environment/SoStylized/Environment/Sky/PRESETS/"
             "BP_StylizedSky_Alien.BP_StylizedSky_Alien_C"));
    if (World == nullptr || AlienClass == nullptr)
    {
        UE_LOG(LogCMLIntro, Error,
            TEXT("The intro could not load So Stylized BP_StylizedSky_Alien."));
        return;
    }

    CinematicAlienSky = SpawnNamedActor(
        AlienClass, TEXT("CIN_RuntimeSoStylizedSky_Alien"), FTransform::Identity);
    AActor* Sky = CinematicAlienSky.Get();
    if (Sky == nullptr)
    {
        return;
    }

    // This instance belongs only to A_01_IntroCinematic and is destroyed with
    // that world. The gameplay map's Classic preset and clock are never read,
    // replaced, paused or otherwise touched here.
    for (TFieldIterator<FProperty> PropertyIterator(Sky->GetClass());
         PropertyIterator;
         ++PropertyIterator)
    {
        FProperty* Property = *PropertyIterator;
        const FString Name = CanonicalIntroMemberName(Property->GetName());
        if (FBoolProperty* Bool = CastField<FBoolProperty>(Property))
        {
            if (Name == TEXT("daycycleenabled"))
            {
                Bool->SetPropertyValue_InContainer(Sky, false);
            }
            else if (Name == TEXT("freezealltime"))
            {
                Bool->SetPropertyValue_InContainer(Sky, true);
            }
            else if (Name.Contains(TEXT("cloud"))
                && (Name.Contains(TEXT("enable"))
                    || Name.Contains(TEXT("visible"))
                    || Name.Contains(TEXT("show"))))
            {
                Bool->SetPropertyValue_InContainer(Sky, false);
            }
        }
    }

    // Official So Stylized clock API, with conventional civil midnight.
    if (UFunction* Function = Sky->FindFunction(TEXT("Set New Time ClockBased")))
    {
        FStructOnScope Parameters(Function);
        void* Memory = Parameters.GetStructMemory();
        int32 Assigned = 0;
        for (TFieldIterator<FProperty> PropertyIterator(Function);
             PropertyIterator;
             ++PropertyIterator)
        {
            FProperty* Property = *PropertyIterator;
            if (!Property->HasAnyPropertyFlags(CPF_Parm)
                || Property->HasAnyPropertyFlags(CPF_ReturnParm | CPF_OutParm))
            {
                continue;
            }
            const FString Name = CanonicalIntroMemberName(Property->GetName());
            double Value = 0.0;
            if (Name == TEXT("newhour") || Name == TEXT("newminutes")
                || Name == TEXT("newseconds"))
            {
                Value = 0.0;
            }
            else if (Name == TEXT("dailyhours"))
            {
                Value = 24.0;
            }
            else if (Name == TEXT("hourlyminutes") || Name == TEXT("minutelyseconds"))
            {
                Value = 60.0;
            }
            else
            {
                continue;
            }
            Assigned += SetIntroClockParameter(*Property, Memory, Value) ? 1 : 0;
        }
        if (Assigned == 6)
        {
            Sky->ProcessEvent(Function, Memory);
        }
        else
        {
            UE_LOG(LogCMLIntro, Error,
                TEXT("Alien sky clock expected 6 inputs; intro bound %d."), Assigned);
        }
    }

    TInlineComponentArray<UActorComponent*> Components;
    Sky->GetComponents(Components);
    for (UActorComponent* Component : Components)
    {
        if (Component == nullptr)
        {
            continue;
        }
        const FString Identity =
            (Component->GetName() + Component->GetClass()->GetName()).ToLower();
        if (!Identity.Contains(TEXT("cloud")))
        {
            continue;
        }
        Component->Deactivate();
        Component->SetComponentTickEnabled(false);
        if (UPrimitiveComponent* Primitive = Cast<UPrimitiveComponent>(Component))
        {
            Primitive->SetVisibility(false, true);
            Primitive->SetHiddenInGame(true, true);
        }
    }

    // Some preset revisions create their decorative cloud cards as child
    // actors instead of components on the sky Blueprint.
    for (TActorIterator<AActor> Iterator(World); Iterator; ++Iterator)
    {
        AActor* Candidate = *Iterator;
        if (Candidate != nullptr && Candidate->GetClass() != nullptr
            && Candidate->GetClass()->GetName().Contains(
                TEXT("BP_IndividualCloud"), ESearchCase::IgnoreCase))
        {
            Candidate->SetActorHiddenInGame(true);
            Candidate->SetActorTickEnabled(false);
        }
    }

    UE_LOG(LogCMLIntro, Display,
        TEXT("Intro-only So Stylized Alien sky staged at midnight without clouds."));
}

void ACMLIntroDirector::BuildRuntimeStage()
{
    // The Unity scene converter cannot preserve empty pivots, particles or a
    // skybox. Rebuild the cinematic stage from the migrated authored assets so
    // PIE and a cooked build see exactly the same complete scene.
    if (UWorld* World = GetWorld())
    {
        for (TActorIterator<AActor> It(World); It; ++It)
        {
            AActor* Actor = *It;
            if (Actor != nullptr && Actor != this && !Actor->IsA<APawn>())
            {
                Actor->SetActorHiddenInGame(true);
                if (ULightComponent* Light = Actor->FindComponentByClass<ULightComponent>())
                {
                    Light->SetVisibility(false);
                }
            }
        }
    }

    SpaceVisuals = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeSpaceVisuals"), FTransform::Identity);
    BuildCinematicAlienSky();
    AirshipHeading = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeAirshipHeading"), FTransform::Identity);
    AirshipAttitude = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeAirshipAttitude"), FTransform::Identity);

    if (AirshipHeading != nullptr && AirshipHeading->GetRootComponent() == nullptr)
    {
        USceneComponent* Root = NewObject<USceneComponent>(AirshipHeading, TEXT("HeadingRoot"));
        Root->RegisterComponent();
        AirshipHeading->SetRootComponent(Root);
    }
    if (AirshipAttitude != nullptr && AirshipAttitude->GetRootComponent() == nullptr)
    {
        USceneComponent* Root = NewObject<USceneComponent>(AirshipAttitude, TEXT("AttitudeRoot"));
        Root->RegisterComponent();
        AirshipAttitude->SetRootComponent(Root);
    }
    if (AirshipAttitude != nullptr && AirshipHeading != nullptr)
    {
        AirshipAttitude->AttachToActor(AirshipHeading, FAttachmentTransformRules::KeepRelativeTransform);
    }

    // The prefab produced by the generic Unity converter contains only the
    // access-door collision panel. Use the deliberately combined visual mesh
    // (all 281 authored pieces, no collision helpers) that is also placed on
    // Starter Island.
    AStaticMeshActor* RuntimeAirship = Cast<AStaticMeshActor>(SpawnNamedActor(
        AStaticMeshActor::StaticClass(), TEXT("CIN_RuntimeAirship"), FTransform::Identity));
    AirshipVisual = RuntimeAirship;
    if (RuntimeAirship != nullptr)
    {
        UStaticMeshComponent* VisualMesh = RuntimeAirship->GetStaticMeshComponent();
        VisualMesh->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/_Project/Art/Vehicles/Airship/SM_Airship_Visual.SM_Airship_Visual")));
        VisualMesh->SetMobility(EComponentMobility::Movable);
        VisualMesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        // The hull was flying sideways. Its bounds run 873 units across X and
        // 1463 along Y, so the mesh's length — and its nose, with the cockpit
        // sitting at +Y — points down Y, while this whole sequence flies down
        // X: the chase camera sits at -X, the rift opens at +X, the streaks
        // stretch along X. Broadside travel is what put those horizontal
        // streaks across the cockpit view and aimed every camera off the prow.
        // Corrected on the mesh so the heading and attitude pivots the shots
        // animate are left exactly as they are.
        VisualMesh->SetRelativeRotation(FRotator(0.0f, -90.0f, 0.0f));
        RuntimeAirship->SetActorScale3D(FVector(1.51f));
    }
    if (AirshipVisual != nullptr && AirshipAttitude != nullptr)
    {
        TInlineComponentArray<USceneComponent*> AirshipComponents;
        AirshipVisual->GetComponents(AirshipComponents);
        for (USceneComponent* Component : AirshipComponents)
        {
            if (Component != nullptr)
            {
                Component->SetMobility(EComponentMobility::Movable);
            }
        }
        AirshipVisual->AttachToActor(AirshipAttitude, FAttachmentTransformRules::KeepRelativeTransform);
        AirshipVisual->SetActorEnableCollision(false);
    }

    // Unity puts a global Volume on the cinematic root and drives it through
    // IntroCinematicGrade. FCMLIntroGradeState ports that grade and is covered
    // by tests, but nothing ever applied it: there was no post process in the
    // opening at all. Without it the additive streaks have no bloom to glow
    // with, and Unreal's auto exposure hunts around a nearly black scene where
    // URP simply has a fixed exposure — which is why every shot read darker
    // than its reference frame rather than any one element being wrong.
    if (APostProcessVolume* Volume = GetWorld()->SpawnActor<APostProcessVolume>())
    {
        Volume->bUnbound = true;
        Volume->Priority = 100.0f;
        GradeVolume = Volume;
        ApplyGrade(FCMLIntroGradeState::Cruise());
    }

    // Alien is the authoritative sky *and* lighting preset for this map. The
    // old opaque deep-space sphere would cover it completely, so it only
    // remains as an explicit fallback if the marketplace Blueprint is missing.
    AStaticMeshActor* Backdrop = CinematicAlienSky == nullptr
        ? Cast<AStaticMeshActor>(SpawnNamedActor(
            AStaticMeshActor::StaticClass(), TEXT("CIN_RuntimeSpaceBackdrop"),
            FTransform::Identity))
        : nullptr;
    SpaceBackdrop = Backdrop;
    if (Backdrop != nullptr)
    {
        UStaticMesh* Sphere = LoadObject<UStaticMesh>(nullptr,
            TEXT("/Engine/BasicShapes/Sphere.Sphere"));
        UMaterialInterface* SpaceMaterial = LoadObject<UMaterialInterface>(nullptr,
            // The ported master, not the generic asset the scene converter
            // produced. The converted M_CIN_* materials are opaque and lit;
            // every cinematic shader in Unity is unlit and additive, which is
            // why the streaks rendered black over the bright nebula. The ports
            // also carry the _CoreBoost/_Softness parameters this class drives,
            // so the material animation below was landing on nothing.
            TEXT("/Game/Migration/Masters/M_CML_Cin_DeepSpace.M_CML_Cin_DeepSpace"));
        Backdrop->GetStaticMeshComponent()->SetStaticMesh(Sphere);
        Backdrop->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
        Backdrop->GetStaticMeshComponent()->SetMaterial(0, SpaceMaterial);
        Backdrop->GetStaticMeshComponent()->SetCastShadow(false);
        Backdrop->GetStaticMeshComponent()->SetReverseCulling(true);
        Backdrop->GetStaticMeshComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Backdrop->SetActorScale3D(FVector(1600.0f));
    }

    AStaticMeshActor* Tunnel = Cast<AStaticMeshActor>(SpawnNamedActor(
        AStaticMeshActor::StaticClass(), TEXT("CIN_RuntimeWarpTunnel"), FTransform::Identity));
    WarpTunnel = Tunnel;
    if (Tunnel != nullptr)
    {
        Tunnel->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
        Tunnel->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/Migration/EmbeddedMeshes/SM_MSH_CIN_WarpTunnel.SM_MSH_CIN_WarpTunnel")));
        Tunnel->GetStaticMeshComponent()->SetMaterial(0, LoadObject<UMaterialInterface>(nullptr,
            TEXT("/Game/Migrated/Project/Art/Cinematics/Materials/M_CIN_WarpTunnel.M_CIN_WarpTunnel")));
        Tunnel->GetStaticMeshComponent()->SetCastShadow(false);
        Tunnel->GetStaticMeshComponent()->SetReverseCulling(true);
        Tunnel->GetStaticMeshComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        // The embedded-mesh importer already maps Unity +Z to Unreal +X (the
        // imported OBJ spans X=-19000..34000). Rotating it again put the tube
        // on Z and turned its axial filaments into the vertical/horizontal grid
        // visible in PIE.
        Tunnel->SetActorRotation(FRotator::ZeroRotator);
    }

    AStaticMeshActor* RiftActor = Cast<AStaticMeshActor>(SpawnNamedActor(
        AStaticMeshActor::StaticClass(), TEXT("CIN_RuntimeRift"),
        // Spawned unrotated: the actor is aimed at the cockpit every frame
        // below, and the mesh's own axis correction rides on the component so
        // the two never have to be composed by hand.
        FTransform(FRotator::ZeroRotator, FVector(22000.0f, 0.0f, 200.0f), FVector(190.0f))));
    Rift = RiftActor;
    if (RiftActor != nullptr)
    {
        RiftActor->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
        RiftActor->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            TEXT("/Engine/BasicShapes/Plane.Plane")));
        RiftActor->GetStaticMeshComponent()->SetMaterial(0, LoadObject<UMaterialInterface>(nullptr,
            TEXT("/Game/Migrated/Project/Art/Cinematics/Materials/M_CIN_Rift.M_CIN_Rift")));
        RiftActor->GetStaticMeshComponent()->SetCastShadow(false);
        RiftActor->GetStaticMeshComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        RiftActor->GetStaticMeshComponent()->SetTranslucentSortPriority(20);
    }

    APointLight* Glow = Cast<APointLight>(SpawnNamedActor(
        APointLight::StaticClass(), TEXT("CIN_RuntimeRiftGlow"),
        FTransform(FVector(21800.0f, 0.0f, 200.0f))));
    RiftGlow = Glow;
    if (Glow != nullptr)
    {
        if (UPointLightComponent* Component =
                Cast<UPointLightComponent>(Glow->GetLightComponent()))
        {
            Component->SetLightColor(FLinearColor(0.32f, 0.82f, 1.0f));
            Component->SetAttenuationRadius(46000.0f);
            Component->SetIntensity(0.0f);
        }
    }

    Asteroids = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeAsteroids"), FTransform::Identity);
    UMaterialInterface* AsteroidMaterial = LoadObject<UMaterialInterface>(nullptr,
        TEXT("/Game/Migrated/Project/Art/Cinematics/Materials/M_CIN_Asteroid.M_CIN_Asteroid"));
    FRandomStream Random(4207);
    for (int32 Index = 0; Index < 9; ++Index)
    {
        AStaticMeshActor* Asteroid = Cast<AStaticMeshActor>(SpawnNamedActor(
            AStaticMeshActor::StaticClass(),
            *FString::Printf(TEXT("CIN_RuntimeAsteroid_%02d"), Index), FTransform::Identity));
        if (Asteroid == nullptr) continue;
        Asteroid->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
        Asteroid->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            *FString::Printf(TEXT("/Game/Migration/EmbeddedMeshes/SM_MSH_CIN_Asteroid_%02d.SM_MSH_CIN_Asteroid_%02d"), Index, Index)));
        Asteroid->GetStaticMeshComponent()->SetMaterial(0, AsteroidMaterial);
        Asteroid->GetStaticMeshComponent()->SetCastShadow(false);
        Asteroid->SetActorLocation(FVector(Random.FRandRange(26000.0f, 125000.0f),
            Random.FRandRange(-26000.0f, 26000.0f), Random.FRandRange(-14000.0f, 16000.0f)));
        Asteroid->SetActorScale3D(FVector(Index == 0 ? 30.0f : Random.FRandRange(9.0f, 24.0f)));
        Asteroid->AttachToActor(Asteroids, FAttachmentTransformRules::KeepWorldTransform);
        if (Index == 0)
        {
            // The big one is the lesson's rock, driven by the ported threat
            // rather than parked with the scenery.
            ThreatRock = Asteroid;
            Asteroid->SetActorHiddenInGame(true);
        }
    }

    // The complete Alien preset already contains its authored directional and
    // ambient lighting. The following lights are fallback-only; stacking them
    // on the preset would no longer be Alien at midnight.
    ADirectionalLight* Key = CinematicAlienSky == nullptr
        ? Cast<ADirectionalLight>(SpawnNamedActor(
            ADirectionalLight::StaticClass(), TEXT("CIN_RuntimeSpaceKey"),
            FTransform(FRotator(28.0f, -142.0f, 0.0f))))
        : nullptr;
    SpaceKeyLight = Key;
    if (Key != nullptr)
    {
        if (UDirectionalLightComponent* Component =
                Cast<UDirectionalLightComponent>(Key->GetLightComponent()))
        {
            Component->SetLightColor(FLinearColor(0.62f, 0.76f, 1.0f));
            Component->SetIntensity(4.0f);
            Component->SetForwardShadingPriority(2);
        }
    }
    ADirectionalLight* Rim = CinematicAlienSky == nullptr
        ? Cast<ADirectionalLight>(SpawnNamedActor(
            ADirectionalLight::StaticClass(), TEXT("CIN_RuntimeSpaceRim"),
            FTransform(FRotator(-14.0f, 46.0f, 0.0f))))
        : nullptr;
    SpaceRimLight = Rim;
    if (Rim != nullptr)
    {
        if (UDirectionalLightComponent* Component =
                Cast<UDirectionalLightComponent>(Rim->GetLightComponent()))
        {
            Component->SetLightColor(FLinearColor(0.44f, 0.34f, 0.86f));
            Component->SetIntensity(2.0f);
            Component->SetForwardShadingPriority(1);
            Component->SetCastShadows(false);
        }
    }

    ChaseCamera = Cast<ACameraActor>(SpawnNamedActor(ACameraActor::StaticClass(),
        TEXT("CIN_RuntimeChaseCamera"), FTransform(FRotator::ZeroRotator, FVector(-3600.0f, 0.0f, 620.0f))));
    CockpitCamera = Cast<ACameraActor>(SpawnNamedActor(ACameraActor::StaticClass(),
        TEXT("CIN_RuntimeCockpitCamera"), FTransform(FRotator::ZeroRotator, FVector(520.0f, 0.0f, 230.0f))));
    if (ChaseCamera != nullptr) ChaseCamera->GetCameraComponent()->SetFieldOfView(61.0f);
    if (CockpitCamera != nullptr) CockpitCamera->GetCameraComponent()->SetFieldOfView(68.0f);

    // Unity hangs the cockpit camera off a shake pivot rather than shaking the
    // camera itself, so the seat position and the shake never fight over the
    // same transform.
    if (AActor* Pivot = SpawnNamedActor(
            AActor::StaticClass(), TEXT("CIN_RuntimeCockpitShake"), FTransform::Identity))
    {
        ShakePivot = Pivot;
    }

    // Use the authored pilot eye carried by the airship prefab. A guessed
    // offset can land outside the cabin when the migrated hull scale changes.
    bool bFoundPilotAnchor = false;
    if (AirshipVisual != nullptr && CockpitCamera != nullptr)
    {
        TInlineComponentArray<USceneComponent*> Components;
        AirshipVisual->GetComponents(Components);
        for (USceneComponent* Component : Components)
        {
            if (Component != nullptr && Component->GetName().Contains(TEXT("REF_PilotCamera")))
            {
                CockpitCamera->SetActorLocation(Component->GetComponentLocation());
                CockpitCamera->SetActorRotation(AirshipVisual->GetActorForwardVector().Rotation());
                bFoundPilotAnchor = true;
                break;
            }
        }
        if (!bFoundPilotAnchor)
        {
            // REF_PilotCamera in the source GLB is (0, 1.68, 2.46) metres.
            // The combined FBX maps GLTF X/Z/Y to Unreal X/Y/Z. The ship's
            // bow therefore faces +Y, not the actor's conventional +X.
            const FVector AuthoredEyeLocal(0.0f, 246.0f, 168.0f);
            // Through the mesh's own transform, not the actor's: the hull is
            // rotated on the component, so the actor transform still describes
            // the old broadside orientation this offset was authored against.
            const FTransform HullTransform =
                Cast<AStaticMeshActor>(AirshipVisual)->GetStaticMeshComponent()->GetComponentTransform();
            CockpitCamera->SetActorLocation(HullTransform.TransformPosition(AuthoredEyeLocal));
            CockpitCamera->SetActorRotation(
                HullTransform.TransformVectorNoScale(FVector::YAxisVector).Rotation());
        }
    }

    APointLight* Fill = Cast<APointLight>(SpawnNamedActor(
        APointLight::StaticClass(), TEXT("CIN_RuntimeCockpitFill"),
        FTransform(CockpitCamera != nullptr
            ? CockpitCamera->GetActorLocation() + FVector(40.0f, 0.0f, 45.0f)
            : FVector(560.0f, 0.0f, 275.0f))));
    CockpitFillLight = Fill;
    if (Fill != nullptr)
    {
        if (UPointLightComponent* Component = Cast<UPointLightComponent>(Fill->GetLightComponent()))
        {
            Component->SetLightColor(FLinearColor(1.0f, 0.88f, 0.72f));
            Component->SetAttenuationRadius(900.0f);
            Component->SetIntensity(1350.0f);
            Component->SetCastShadows(false);
        }
        Fill->AttachToActor(AirshipAttitude, FAttachmentTransformRules::KeepWorldTransform);
    }

    // The Unity particle systems did not survive the scene converter. Rebuild
    // the same readable beats from lightweight emissive meshes; unlike the
    // missing references, these work identically in PIE and cooked builds.
    StarStreaks = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeStarStreaks"), FTransform::Identity);
    RiftDebris = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeRiftDebris"), FTransform::Identity);
    CockpitSparks = SpawnNamedActor(AActor::StaticClass(), TEXT("CIN_RuntimeCockpitSparks"), FTransform::Identity);

    UStaticMesh* CubeMesh = LoadObject<UStaticMesh>(nullptr,
        TEXT("/Engine/BasicShapes/Cube.Cube"));
    UMaterialInterface* StreakSurface = LoadObject<UMaterialInterface>(nullptr,
        TEXT("/Game/Migrated/Project/Art/Cinematics/Materials/M_CIN_StarStreak.M_CIN_StarStreak"));
    // UInstancedStaticMesh rejects a material whose parent was never compiled
    // for the instancing vertex factory and silently draws WorldGridMaterial.
    // That is the handful of opaque white dashes seen after the rift, not the
    // Unity star-streak shader. Check the usage before assigning it so PIE can
    // compile the correct permutation (the asset port also persists the flag).
    if (StreakSurface != nullptr
        && !StreakSurface->CheckMaterialUsage_Concurrent(MATUSAGE_InstancedStaticMeshes))
    {
        UE_LOG(LogCMLIntro, Error,
            TEXT("Star-streak material could not compile for instanced static meshes."));
    }
    FRandomStream EffectRandom(91823);

    // Unity ran 2400 streak particles, and that count is the shot: the
    // reference frame in outputs/IntroCinematic is filled edge to edge with
    // them converging on the vanishing point. The earlier 56 separate actors
    // left the hyperspace shot reading as empty black space — the single
    // largest departure from the original. One instanced component carries the
    // real count; 2400 individual AStaticMeshActors would not be viable.
    if (StarStreaks != nullptr)
    {
        UInstancedStaticMeshComponent* Instances =
            NewObject<UInstancedStaticMeshComponent>(StarStreaks, TEXT("StreakInstances"));
        Instances->SetStaticMesh(CubeMesh);
        Instances->SetMaterial(0, StreakSurface);
        Instances->SetMobility(EComponentMobility::Movable);
        Instances->SetCastShadow(false);
        Instances->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        StarStreaks->SetRootComponent(Instances);
        Instances->RegisterComponent();

        // The instances never move. Animating them per frame meant marking the
        // render state dirty every tick, which recreates the whole instance
        // proxy and dropped the editor to roughly one frame per minute. The
        // field is built once and the component is slid along the flight axis
        // instead, which costs a single transform per frame however many
        // streaks there are.
        TArray<FTransform> Field;
        Field.Reserve(StarStreakCount);
        for (int32 Index = 0; Index < StarStreakCount; ++Index)
        {
            // Cylindrical around the flight axis rather than a wide box: the
            // convergence in the reference comes from perspective on lines
            // parallel to travel, so the radius has to stay inside the frame.
            const float Angle = EffectRandom.FRandRange(0.0f, 2.0f * PI);
            const float Radius = 5200.0f * FMath::Sqrt(EffectRandom.FRand());
            FTransform Base;
            Base.SetLocation(FVector(
                EffectRandom.FRandRange(400.0f, StreakFieldDepth),
                FMath::Cos(Angle) * Radius,
                FMath::Sin(Angle) * Radius));
            // Long and fine. A streak is a smear of a star over a frame, so its
            // length is the speed: at 8 to 26 units of cube these were dashes,
            // where Unity's stretched billboards run the height of the frame.
            Base.SetScale3D(FVector(
                EffectRandom.FRandRange(24.0f, 68.0f),
                EffectRandom.FRandRange(0.022f, 0.055f),
                EffectRandom.FRandRange(0.022f, 0.055f)));
            Field.Add(Base);
        }
        Instances->AddInstances(Field, /*bShouldReturnIndices=*/false);
        StreakInstances = Instances;
    }

    for (int32 Index = 0; Index < 24; ++Index)
    {
        AStaticMeshActor* Debris = Cast<AStaticMeshActor>(SpawnNamedActor(
            AStaticMeshActor::StaticClass(),
            *FString::Printf(TEXT("CIN_RuntimeRiftDebris_%02d"), Index), FTransform::Identity));
        if (Debris == nullptr) continue;
        Debris->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
        const int32 MeshIndex = Index % 9;
        Debris->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            *FString::Printf(TEXT("/Game/Migration/EmbeddedMeshes/SM_MSH_CIN_Asteroid_%02d.SM_MSH_CIN_Asteroid_%02d"),
                MeshIndex, MeshIndex)));
        Debris->GetStaticMeshComponent()->SetMaterial(0, AsteroidMaterial);
        Debris->GetStaticMeshComponent()->SetCastShadow(false);
        Debris->GetStaticMeshComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Debris->SetActorLocation(FVector(EffectRandom.FRandRange(14000.0f, 23500.0f),
            EffectRandom.FRandRange(-6200.0f, 6200.0f), EffectRandom.FRandRange(-3500.0f, 4800.0f)));
        Debris->SetActorScale3D(FVector(EffectRandom.FRandRange(0.35f, 1.6f)));
        Debris->AttachToActor(RiftDebris, FAttachmentTransformRules::KeepWorldTransform);
    }

    for (int32 Index = 0; Index < 18; ++Index)
    {
        AStaticMeshActor* Spark = Cast<AStaticMeshActor>(SpawnNamedActor(
            AStaticMeshActor::StaticClass(),
            *FString::Printf(TEXT("CIN_RuntimeCockpitSpark_%02d"), Index), FTransform::Identity));
        if (Spark == nullptr) continue;
        Spark->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
        Spark->GetStaticMeshComponent()->SetStaticMesh(CubeMesh);
        Spark->GetStaticMeshComponent()->SetMaterial(0, StreakSurface);
        Spark->GetStaticMeshComponent()->SetCastShadow(false);
        Spark->GetStaticMeshComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Spark->SetActorLocation(FVector(EffectRandom.FRandRange(580.0f, 1300.0f),
            EffectRandom.FRandRange(-360.0f, 360.0f), EffectRandom.FRandRange(-60.0f, 560.0f)));
        Spark->SetActorScale3D(FVector(EffectRandom.FRandRange(0.18f, 0.75f), 0.025f, 0.025f));
        Spark->AttachToActor(CockpitSparks, FAttachmentTransformRules::KeepWorldTransform);
    }

    const FVector AlertPositions[] = {
        FVector(360.0f, -220.0f, 310.0f), FVector(360.0f, 220.0f, 310.0f),
        FVector(760.0f, 0.0f, 430.0f)
    };
    for (int32 Index = 0; Index < UE_ARRAY_COUNT(AlertPositions); ++Index)
    {
        APointLight* Alert = Cast<APointLight>(SpawnNamedActor(
            APointLight::StaticClass(),
            *FString::Printf(TEXT("CIN_RuntimeAlertLight_%02d"), Index),
            FTransform(AlertPositions[Index])));
        if (Alert == nullptr) continue;
        if (UPointLightComponent* Component =
                Cast<UPointLightComponent>(Alert->GetLightComponent()))
        {
            Component->SetLightColor(FLinearColor(1.0f, 0.025f, 0.012f));
            Component->SetAttenuationRadius(1800.0f);
            Component->SetIntensity(0.0f);
            Component->SetCastShadows(false);
        }
        Alert->AttachToActor(AirshipAttitude, FAttachmentTransformRules::KeepWorldTransform);
        AlertLights.Add(Alert);
    }

    // One parent now owns the complete runtime space set. ApplyShot can hide
    // the entire set during the fall/blackout, while the nested groups retain
    // their own per-shot visibility.
    // Alien deliberately stays outside SpaceVisuals: toggling that group
    // recursively would turn its cloud components visible again. The intro
    // world is destroyed as soon as the rift hands off to gameplay anyway.
    for (AActor* RuntimeActor : {AirshipHeading.Get(), SpaceBackdrop.Get(), WarpTunnel.Get(),
            Rift.Get(), RiftGlow.Get(), Asteroids.Get(), StarStreaks.Get(),
            RiftDebris.Get(), CockpitSparks.Get(), SpaceKeyLight.Get(), SpaceRimLight.Get()})
    {
        if (RuntimeActor != nullptr && SpaceVisuals != nullptr)
        {
            RuntimeActor->AttachToActor(SpaceVisuals, FAttachmentTransformRules::KeepWorldTransform);
        }
    }
    if (CockpitCamera != nullptr && AirshipAttitude != nullptr)
    {
        // Attitude -> shake -> camera. The seat's place in the hull and the
        // shake are then separate transforms, so neither has to be recomputed
        // out of the other every frame.
        if (ShakePivot.IsValid())
        {
            ShakePivot->AttachToActor(AirshipAttitude, FAttachmentTransformRules::KeepWorldTransform);
            ShakePivot->SetActorTransform(CockpitCamera->GetActorTransform());
            CockpitCamera->AttachToActor(
                ShakePivot.Get(), FAttachmentTransformRules::KeepWorldTransform);
        }
        else
        {
            CockpitCamera->AttachToActor(AirshipAttitude, FAttachmentTransformRules::KeepWorldTransform);
        }
    }

    const FBox AirshipBounds = AirshipVisual != nullptr
        ? AirshipVisual->GetComponentsBoundingBox(true) : FBox(ForceInit);
    UE_LOG(LogCMLIntro, Display,
        TEXT("Runtime stage: airship=%s bounds=%s cockpit=%s chase=%s"),
        AirshipVisual != nullptr ? *AirshipVisual->GetName() : TEXT("missing"),
        *AirshipBounds.ToString(),
        CockpitCamera != nullptr ? *CockpitCamera->GetActorLocation().ToString() : TEXT("missing"),
        ChaseCamera != nullptr ? *ChaseCamera->GetActorLocation().ToString() : TEXT("missing"));

    CacheMaterials();
}

AActor* ACMLIntroDirector::FindByLabel(const FString& Label) const
{
    const UWorld* World = GetWorld();
    if (World == nullptr)
    {
        return nullptr;
    }
    for (TActorIterator<AActor> It(World); It; ++It)
    {
        // Cooked games strip editor actor labels. The imported Unity name is
        // also preserved in the runtime actor name, so support both boundaries.
        bool bMatches = It->GetName().Equals(Label, ESearchCase::IgnoreCase)
            || It->GetName().StartsWith(Label + TEXT("_"), ESearchCase::IgnoreCase);
#if WITH_EDITOR
        bMatches |= It->GetActorLabel().Equals(Label, ESearchCase::IgnoreCase);
#endif
        if (bMatches)
        {
            return *It;
        }
    }
    return nullptr;
}

void ACMLIntroDirector::CacheActors()
{
    auto Require = [this](const TCHAR* Label) -> AActor*
    {
        AActor* Found = FindByLabel(Label);
        if (Found == nullptr)
        {
            // Reported rather than skipped: a missing rift means the rift shot
            // plays to an empty screen, which is the hardest kind of failure to
            // notice and the whole reason this actor exists.
            UE_LOG(LogCMLIntro, Warning,
                TEXT("The opening expects an actor labelled '%s' and the map has none. "
                     "That shot will play to an empty screen."), Label);
        }
        return Found;
    };

    ChaseCamera = Cast<ACameraActor>(Require(TEXT("CIN_ChaseCamera")));
    CockpitCamera = Cast<ACameraActor>(Require(TEXT("CIN_CockpitCamera")));

    // The heading pivot is what the chase rig orbits. If the scene has no
    // pivot, the hull itself is the next best frame — a shot around the ship is
    // still a shot, where no frame at all leaves the camera at the origin.
    AirshipHeading = FindByLabel(TEXT("CIN_AirshipHeading"));
    if (AirshipHeading == nullptr)
    {
        AirshipHeading = FindByLabel(TEXT("CIN_AirshipActor"));
        UE_LOG(LogCMLIntro, Warning,
            TEXT("No 'CIN_AirshipHeading' pivot; falling back to the hull itself."));
    }
    AirshipAttitude = FindByLabel(TEXT("CIN_AirshipAttitude"));
    if (AirshipAttitude == nullptr)
    {
        // Without a separate attitude pivot the roll would have to be applied
        // to the heading frame, which would swing the chase camera with it.
        UE_LOG(LogCMLIntro, Warning,
            TEXT("No 'CIN_AirshipAttitude' pivot; the hull will not roll or shudder."));
    }
    SpaceVisuals = Require(TEXT("CIN_SpaceVisuals"));
    WarpTunnel = Require(TEXT("CIN_WarpTunnel"));
    StarStreaks = Require(TEXT("CIN_StarStreaks"));
    Rift = Require(TEXT("CIN_Rift"));
    RiftGlow = Require(TEXT("CIN_RiftGlow"));
    RiftDebris = Require(TEXT("CIN_RiftDebris"));
    Asteroids = Require(TEXT("CIN_Asteroids"));
    CockpitSparks = Require(TEXT("CIN_CockpitSparks"));

    AlertLights.Reset();
    for (const TCHAR* Label :
        {TEXT("CIN_AlertLight_00"), TEXT("CIN_AlertLight_01"), TEXT("CIN_AlertLight_02")})
    {
        if (AActor* Light = FindByLabel(Label))
        {
            AlertLights.Add(Light);
        }
    }
}

void ACMLIntroDirector::SetScalar(
    UMaterialInstanceDynamic* Material, const FName Parameter, const float Value)
{
    if (Material == nullptr)
    {
        return;
    }
    // Checked rather than set blindly: a master that lost a parameter in the
    // port would otherwise fail silently and leave the effect frozen at its
    // authored default, which looks like a shot that simply does not animate.
    float Existing = 0.0f;
    if (!Material->GetScalarParameterValue(Parameter, Existing))
    {
        UE_LOG(LogCMLIntro, Warning,
            TEXT("Material '%s' has no scalar '%s'; that part of the shot will not animate."),
            *Material->GetName(), *Parameter.ToString());
        return;
    }
    Material->SetScalarParameterValue(Parameter, Value);
}

void ACMLIntroDirector::CacheMaterials()
{
    // Named Holder rather than Owner: AActor has a member by that name, and
    // shadowing it is an error in this project.
    auto MakeDynamic = [](AActor* Holder) -> UMaterialInstanceDynamic*
    {
        UMeshComponent* Mesh =
            Holder != nullptr ? Holder->FindComponentByClass<UMeshComponent>() : nullptr;
        return Mesh != nullptr ? Mesh->CreateDynamicMaterialInstance(0) : nullptr;
    };

    WarpMaterial = MakeDynamic(WarpTunnel);
    RiftMaterial = MakeDynamic(Rift);
    StreakMaterial = MakeDynamic(StarStreaks);
    SpaceSkyMaterial = MakeDynamic(SpaceBackdrop);

    // The master carries shader defaults; Unity's scene material carries the
    // art-directed portal preset. Reapply that preset to the runtime instance
    // so the rift is the narrow ragged tear from the reference, not the broad
    // generic plane defaults used by the reusable shader.
    SetScalar(RiftMaterial, TEXT("_Width"), 0.19f);
    SetScalar(RiftMaterial, TEXT("_EdgeSoftness"), 0.035f);
    SetScalar(RiftMaterial, TEXT("_EdgeTurbulence"), 0.55f);
    SetScalar(RiftMaterial, TEXT("_TurbulenceScale"), 11.0f);
    SetScalar(RiftMaterial, TEXT("_TurbulenceSpeed"), 1.9f);
    SetScalar(RiftMaterial, TEXT("_Refraction"), 0.16f);
    SetScalar(RiftMaterial, TEXT("_SwirlIntensity"), 1.15f);
    SetScalar(RiftMaterial, TEXT("_SwirlSpeed"), 1.4f);
    SetScalar(RiftMaterial, TEXT("_FilamentIntensity"), 1.45f);

    // These are the scene material's authored values. The reusable master's
    // broader chromatic split is useful for previews but produced separated
    // red/green/blue bars in this sequence.
    SetScalar(WarpMaterial, TEXT("_ChromaticSplit"), 0.006f);
    SetScalar(WarpMaterial, TEXT("_StreakDensity"), 240.0f);
    SetScalar(WarpMaterial, TEXT("_StreakLength"), 1.5f);
    SetScalar(WarpMaterial, TEXT("_Turbulence"), 0.38f);
    SetScalar(WarpMaterial, TEXT("_Twist"), 0.22f);
    SetScalar(WarpMaterial, TEXT("_EndFade"), 0.16f);
    SetScalar(WarpMaterial, TEXT("_CoreGlow"), 0.85f);
}

void ACMLIntroDirector::AnchorRiftAxis()
{
    if (CockpitCamera == nullptr)
    {
        return;
    }

    RiftOrigin = CockpitCamera->GetActorLocation();
    RiftForward = CockpitCamera->GetActorForwardVector().GetSafeNormal();
    // The normal alone leaves the plane's roll undefined. Constrain its local
    // Y to projected world-up so the tall axis in the Unity UVs stays vertical.
    RiftVertical = FVector::VectorPlaneProject(FVector::UpVector, RiftForward).GetSafeNormal();
    if (RiftVertical.IsNearlyZero())
    {
        RiftVertical = CockpitCamera->GetActorUpVector().GetSafeNormal();
    }
    bRiftAxisAnchored = true;
}

void ACMLIntroDirector::ApplyShake(const float Amount, const float UnscaledTime)
{
    // Unity's shake, transcribed: Perlin noise sampled at 21, a few centimetres
    // of travel and under a degree of rotation, on a pivot that carries only
    // the shake. Smooth noise is the whole point — a sine at a fixed frequency
    // reads as a machine rattling rather than a hull being thrown about.
    // Applied to the camera, whose transform relative to the pivot is identity.
    // Driving the pivot instead overwrote the seat position it was holding and
    // dropped the view clean out of the hull.
    ACameraActor* ActiveCamera = State.Shot == ECMLIntroShot::Hyperspace
        ? ChaseCamera.Get() : CockpitCamera.Get();
    if (ActiveCamera == nullptr
        || (ActiveCamera == CockpitCamera && !ShakePivot.IsValid()))
    {
        return;
    }

    if (Amount <= 0.0001f)
    {
        if (ActiveCamera == CockpitCamera)
        {
            CockpitCamera->SetActorRelativeLocation(FVector::ZeroVector);
            CockpitCamera->SetActorRelativeRotation(FRotator::ZeroRotator);
        }
        return;
    }

    const float T = UnscaledTime * 21.0f;
    const FVector Offset(
        FMath::PerlinNoise1D(T) * 0.5f,
        FMath::PerlinNoise1D(T + 37.13f) * 0.5f,
        FMath::PerlinNoise1D(T * 0.63f + 71.9f) * 0.5f);

    // Unity works in metres; a hundred times that here. The external opening
    // has no shake pivot, so ApplyDressing stores its clean pose and this adds
    // the same Perlin displacement after the framing has been evaluated.
    const FVector Translation = Offset * Amount * 14.0f;
    const FRotator Rotation(
        Offset.Y * Amount * 2.6f, Offset.Z * Amount * 2.6f, Offset.X * Amount * 2.6f);
    if (ActiveCamera == ChaseCamera)
    {
        ChaseCamera->SetActorLocation(ChaseCameraBaseLocation + Translation);
        ChaseCamera->SetActorRotation(
            (ChaseCameraBaseRotation.Quaternion() * Rotation.Quaternion()).Rotator());
    }
    else
    {
        CockpitCamera->SetActorRelativeLocation(Translation);
        CockpitCamera->SetActorRelativeRotation(Rotation);
    }
}

void ACMLIntroDirector::DriveThreatRock(const float DeltaSeconds)
{
    // The lesson's rock. FCMLIntroThreat carries Unity's approach exactly —
    // launched 900 metres out, closing at 190 m/s — and had never been called
    // from anywhere, so the asteroids were scattered scenery that sat still.
    // Nothing was ever on a collision course, which is why no rock was ever
    // seen coming.
    if (!ThreatRock.IsValid())
    {
        return;
    }

    if (State.Shot != ECMLIntroShot::Flight)
    {
        ThreatRock->SetActorHiddenInGame(true);
        AppliedFlightStep = ECMLIntroFlightStep::Settle;
        return;
    }

    if (State.FlightStep != AppliedFlightStep)
    {
        AppliedFlightStep = State.FlightStep;
        if (State.FlightStep == ECMLIntroFlightStep::ApproachRight
            || State.FlightStep == ECMLIntroFlightStep::ApproachLeft)
        {
            // The rock comes from the side the player is being taught to turn
            // away from, so the turn they are asked for is the one that clears.
            const float Direction =
                State.FlightStep == ECMLIntroFlightStep::ApproachRight ? 1.0f : -1.0f;
            const float Clearance = FCMLIntroThreat::MeasureClearance(
                ThreatRockHalfExtentMetres, HullHalfExtentMetres);
            ThreatState = FCMLIntroThreat::Launch(Direction, Clearance);
            // Each gesture is measured from the heading at the start of its
            // own lesson, exactly like Unity's _lessonYaw reset.
            LessonYaw = 0.0f;
        }
    }

    if (!ThreatState.bActive)
    {
        ThreatRock->SetActorHiddenInGame(true);
        return;
    }

    // At warning distance Unity freezes the closing motion so the gesture can
    // be read and performed. Yaw still updates the earned lateral clearance.
    const bool bTeaching = State.FlightStep == ECMLIntroFlightStep::TeachRight
        || State.FlightStep == ECMLIntroFlightStep::TeachLeft;
    FCMLIntroThreat::Advance(
        ThreatState, ReadPilotYaw(), Timings.TutorialTurnDegrees,
        bTeaching ? 0.0f : DeltaSeconds);

    // Metres in the ported logic, centimetres in the world.
    ThreatRock->SetActorHiddenInGame(false);
    // Rocks own a fixed travel frame captured before the pilot steers. Using
    // AirshipHeading's live axes made the obstacle rotate by the same amount
    // as the ship, so it appeared glued to the mouse instead of being dodged.
    ThreatRock->SetActorLocation(
        AirshipHeading->GetActorLocation()
        + FlightAxisForward * ThreatState.Distance * 100.0f
        + FlightAxisRight * ThreatState.Lateral * 100.0f
        + FlightAxisUp * 400.0f);
}

void ACMLIntroDirector::ApplyGrade(const FCMLIntroGradeState& Grade)
{
    ApplyPanini(Grade);
    if (GradeVolume == nullptr)
    {
        return;
    }

    FPostProcessSettings& Settings = GradeVolume->Settings;

    Settings.bOverride_BloomIntensity = true;
    Settings.BloomIntensity = Grade.BloomIntensity;
    Settings.bOverride_BloomThreshold = true;
    Settings.BloomThreshold = Grade.BloomThreshold;
    Settings.bOverride_Bloom1Tint = true;
    Settings.Bloom1Tint = Grade.BloomTint;
    Settings.bOverride_Bloom2Tint = true;
    Settings.Bloom2Tint = Grade.BloomTint;
    Settings.bOverride_Bloom3Tint = true;
    Settings.Bloom3Tint = Grade.BloomTint;

    Settings.bOverride_VignetteIntensity = true;
    Settings.VignetteIntensity = Grade.VignetteIntensity;
    Settings.bOverride_VignetteColor = true;
    Settings.VignetteColor = Grade.VignetteColor;

    Settings.bOverride_FilmGrainIntensity = true;
    Settings.FilmGrainIntensity = Grade.FilmGrain;

    Settings.bOverride_MotionBlurAmount = true;
    Settings.MotionBlurAmount = Grade.MotionBlur;

    // Unreal's scene fringe becomes visibly separated RGB silhouettes long
    // before its nominal maximum. A direct artistic conversion is therefore
    // deliberately shallower than 0..1 -> 0..5; the rift still distorts while
    // the ship and the star streaks retain the blue-white Unity palette.
    Settings.bOverride_SceneFringeIntensity = true;
    Settings.SceneFringeIntensity = Grade.ChromaticAberration * 1.25f;

    // Unity states contrast and saturation as percentage offsets on -100..100;
    // Unreal wants the multiplier they describe.
    const float Contrast = 1.0f + Grade.Contrast * 0.01f;
    const float Saturation = 1.0f + Grade.Saturation * 0.01f;
    Settings.bOverride_ColorContrast = true;
    Settings.ColorContrast = FVector4(Contrast, Contrast, Contrast, 1.0f);
    Settings.bOverride_ColorSaturation = true;
    Settings.ColorSaturation = FVector4(Saturation, Saturation, Saturation, 1.0f);
    Settings.bOverride_SceneColorTint = true;
    Settings.SceneColorTint = Grade.ColorFilter;

    // Pinned rather than adapting. URP has no eye adaptation here, so letting
    // Unreal's run would grade every shot differently from its reference.
    Settings.bOverride_AutoExposureMinBrightness = true;
    Settings.AutoExposureMinBrightness = 1.0f;
    Settings.bOverride_AutoExposureMaxBrightness = true;
    Settings.AutoExposureMaxBrightness = 1.0f;
    Settings.bOverride_AutoExposureBias = true;
    Settings.AutoExposureBias = Grade.PostExposure;
}

void ACMLIntroDirector::SetGroupVisible(AActor* Root, const bool bVisible)
{
    if (Root == nullptr)
    {
        return;
    }
    // Hiding the root alone would leave its children on screen, and the Unity
    // scene groups the effects under empty parents.
    Root->SetActorHiddenInGame(!bVisible);
    // Named Attached rather than Children: AActor already has a member by that
    // name, and shadowing it is an error here.
    TArray<AActor*> Attached;
    Root->GetAttachedActors(Attached, /*bResetArray=*/true, /*bRecursivelyIncludeAttachedActors=*/true);
    for (AActor* Child : Attached)
    {
        Child->SetActorHiddenInGame(!bVisible);
    }
}

void ACMLIntroDirector::LookThrough(ACameraActor* Camera)
{
    if (Camera == nullptr)
    {
        return;
    }
    if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        // A cut, never a move. Unity swaps which camera is enabled, so the
        // picture changes between one frame and the next. Blending flew the
        // view bodily from outside the hull into the seat over more than a
        // second — a camera move in place of the hard cut the edit is built on.
        Controller->SetViewTarget(Camera);
        if (APlayerCameraManager* Manager = Controller->PlayerCameraManager)
        {
            // Zero blend removes the interpolation, but it does not mark the
            // frame as a camera cut.  Motion blur and temporal AA otherwise
            // retain the previous camera and draw the cut as a violent move.
            Manager->SetGameCameraCutThisFrame();
        }
        Camera->NotifyCameraCut();
        Camera->GetCameraComponent()->NotifyCameraCut();
    }
}

void ACMLIntroDirector::ApplyShot(const ECMLIntroShot Shot)
{
    // Each shot states its whole stage rather than toggling what changed since
    // the last one. Stating it outright means skipping into any shot leaves the
    // scene in the same state as arriving at it normally.
    const bool bInSpace = Shot == ECMLIntroShot::Hyperspace
        || Shot == ECMLIntroShot::Cockpit
        || Shot == ECMLIntroShot::Flight
        || Shot == ECMLIntroShot::Alarm
        || Shot == ECMLIntroShot::RiftOpen
        || Shot == ECMLIntroShot::RiftEntry;

    SetGroupVisible(SpaceVisuals, bInSpace);
    SetGroupVisible(WarpTunnel, Shot == ECMLIntroShot::Hyperspace);
    // Visible for the whole space section, not just the jump. Unity's own
    // reference frames (outputs/IntroCinematic/03_cockpit_asteroid and
    // 04_cockpit_rift) show the streaks filling both cockpit shots; limiting
    // them to Hyperspace left those shots with a field of motionless dots.
    SetGroupVisible(StarStreaks, bInSpace);

    const bool bRiftShowing =
        Shot == ECMLIntroShot::RiftOpen || Shot == ECMLIntroShot::RiftEntry;
    SetGroupVisible(Rift, bRiftShowing);
    SetGroupVisible(RiftGlow, bRiftShowing);
    // Unity already pulls fragments into the lips while the tear opens.
    SetGroupVisible(RiftDebris,
        Shot == ECMLIntroShot::RiftOpen || Shot == ECMLIntroShot::RiftEntry);

    // The asteroids are the threat the alarm is about, so they arrive with it
    // and stay until the rift swallows everything.
    SetGroupVisible(Asteroids,
        Shot == ECMLIntroShot::Alarm || Shot == ECMLIntroShot::RiftOpen);

    const bool bAlarmed = Shot == ECMLIntroShot::Alarm
        || Shot == ECMLIntroShot::RiftOpen
        || Shot == ECMLIntroShot::RiftEntry
        || Shot == ECMLIntroShot::Fall
        || Shot == ECMLIntroShot::Crash;
    for (AActor* Light : AlertLights)
    {
        SetGroupVisible(Light, bAlarmed);
    }
    SetGroupVisible(CockpitSparks,
        Shot == ECMLIntroShot::Crash || Shot == ECMLIntroShot::Fall);

    // The chase camera sells the ship; the cockpit puts the player in it. The
    // opening starts outside and moves in for everything the player does or
    // suffers.
    switch (Shot)
    {
        case ECMLIntroShot::Hyperspace:
            LookThrough(ChaseCamera);
            break;
        case ECMLIntroShot::Cockpit:
        case ECMLIntroShot::Flight:
        case ECMLIntroShot::Alarm:
        case ECMLIntroShot::Fall:
        case ECMLIntroShot::Crash:
        case ECMLIntroShot::Blackout:
        case ECMLIntroShot::Wake:
            LookThrough(CockpitCamera);
            break;
        case ECMLIntroShot::RiftOpen:
        case ECMLIntroShot::RiftEntry:
            // Stay in the seat. Cutting outside here was wrong: the chase rig
            // orbits, so the one thing the shot exists to show — the rift
            // tearing open dead ahead — swung out of frame exactly as it
            // opened. Unity drives the cockpit lens through both shots and
            // never leaves the cockpit.
            LookThrough(CockpitCamera);
            break;
        default:
            break;
    }

    UE_LOG(LogCMLIntro, Verbose, TEXT("Intro shot %d"), static_cast<int32>(Shot));
}

void ACMLIntroDirector::ApplyDressing(const FCMLIntroDressing& Dressing)
{
    // The chase rig is placed in the ship's own frame, so it keeps its framing
    // however the hull is riding.
    if (ChaseCamera != nullptr && AirshipHeading != nullptr)
    {
        const FRotator Heading = AirshipHeading->GetActorRotation();
        const FVector Centre = AirshipHeading->GetActorLocation();
        const FRotator Orbit(0.0f, Heading.Yaw + Dressing.ChaseOrbitDegrees, 0.0f);
        // Metres in the authored values; the scene is in Unreal units.
        const FVector Offset =
            Orbit.RotateVector(FVector(-Dressing.ChaseDistance * 100.0f, 0.0f, 0.0f))
            + FVector(0.0f, 0.0f, Dressing.ChaseHeight * 100.0f);
        ChaseCamera->SetActorLocation(Centre + Offset);
        ChaseCamera->SetActorRotation((Centre - (Centre + Offset)).Rotation());
        ChaseCameraBaseLocation = ChaseCamera->GetActorLocation();
        ChaseCameraBaseRotation = ChaseCamera->GetActorRotation();
    }

    if (AirshipAttitude != nullptr)
    {
        AirshipAttitude->SetActorRelativeRotation(Dressing.AirshipAttitude);
    }

    if (ChaseCamera != nullptr)
    {
        ChaseCamera->GetCameraComponent()->SetFieldOfView(61.0f + Dressing.ChaseFovOffset);
    }
    if (CockpitCamera != nullptr)
    {
        CockpitCamera->GetCameraComponent()->SetFieldOfView(68.0f + Dressing.CockpitFovOffset);
    }
    if (SpaceKeyLight != nullptr)
    {
        if (ULightComponent* Component = SpaceKeyLight->FindComponentByClass<ULightComponent>())
        {
            Component->SetIntensity(Dressing.KeyLightIntensity);
        }
    }
    if (CockpitFillLight != nullptr)
    {
        if (ULightComponent* Component = CockpitFillLight->FindComponentByClass<ULightComponent>())
        {
            // Five times the earlier figure, from measurement rather than
            // taste: mean luminance of the cockpit reference frames against
            // these captures came out 2.3 and 2.9 stops short, while the
            // external shot was within half a stop. The gap is the interior
            // alone, so it belongs to this light and not to the grade.
            Component->SetIntensity(Dressing.CockpitFillIntensity * 5000.0f);
        }
    }

    // Mesh streaks are deterministic stand-ins for the lost Unity particles.
    // Driving them down the flight axis gives the opening its speed back.
    if (StarStreaks != nullptr && Dressing.StreakSpeed > 0.0f)
    {
        // Integrated rather than derived from absolute time: the speed changes
        // between shots, and multiplying the new speed by the elapsed time
        // would jump the whole field at every change.
        const float Delta = GetWorld() != nullptr ? GetWorld()->GetDeltaSeconds() : 0.0f;
        // The first conversion still read like a fast drift. Increase the
        // translation scale together with the authored speed so individual
        // streaks cross the cockpit frame in a fraction of a second.
        StreakTravel = FMath::Fmod(
            StreakTravel + Delta * Dressing.StreakSpeed * 210.0f, StreakFieldDepth);
        StarStreaks->SetActorRelativeLocation(FVector(-StreakTravel, 0.0f, 0.0f));
    }

    // Lights are the part of the dressing that survives without materials, so
    // they are driven directly; the warp and rift parameters need the ported
    // materials and are pushed through the scalar interface below.
    for (AActor* Light : AlertLights)
    {
        if (ULightComponent* Component =
                Light != nullptr ? Light->FindComponentByClass<ULightComponent>() : nullptr)
        {
            // Unity's light intensities are small multipliers; Unreal point
            // lights are lumens. Passed straight through, the klaxon peaked at
            // 9.5 lumens against a 6750-lumen cockpit fill and simply never
            // appeared.
            Component->SetIntensity(Dressing.AlertIntensity * 2000.0f);
        }
    }
    if (RiftGlow != nullptr)
    {
        if (ULightComponent* Component = RiftGlow->FindComponentByClass<ULightComponent>())
        {
            // Lumens again. Unity's rift lamp peaks at 46 in its own units;
            // passed through unconverted it lit nothing at all, so the tear
            // threw no light into the cockpit as it opened.
            Component->SetIntensity(Dressing.RiftLightIntensity * 2000.0f);
            Component->SetLightColor(Dressing.RiftLightColour);
        }
    }

    // The tunnel and the tear. The ported masters kept the Unity parameter
    // names, so the dressing's values go straight in.
    SetScalar(SpaceSkyMaterial, TEXT("_WarpBlend"), Dressing.WarpBlend);
    SetScalar(WarpMaterial, TEXT("_Speed"), Dressing.WarpSpeed);
    SetScalar(WarpMaterial, TEXT("_Intensity"), Dressing.WarpIntensity);
    SetScalar(StreakMaterial, TEXT("_CoreBoost"), Dressing.StreakSpeed * 0.01f);
    SetScalar(RiftMaterial, TEXT("_Openness"), Dressing.RiftOpenness);
    // Unity's material is authored at 0.62 and the exposure/light ramp carries
    // the climax. Scaling emissive directly to the 46-unit point light blew the
    // texture into a flat white slab and erased its void, rim and filaments.
    SetScalar(RiftMaterial, TEXT("_Intensity"),
        Dressing.RiftLightIntensity > 0.0f
            ? 0.62f * FMath::Lerp(0.75f, 1.25f,
                FMath::Clamp(Dressing.RiftLightIntensity / 136.0f, 0.0f, 1.0f))
            : 0.0f);

    // The tear is held at a distance rather than scaled: scaling it would grow
    // its turbulence and filaments with it, and a bigger tear would read as a
    // closer one made of coarser stuff.
    if (Rift != nullptr && CockpitCamera != nullptr && Dressing.RiftDistance > 0.0f)
    {
        if (!bRiftAxisAnchored)
        {
            AnchorRiftAxis();
        }
        const FVector RiftLocation =
            RiftOrigin + RiftForward * Dressing.RiftDistance * 100.0f;
        Rift->SetActorLocation(RiftLocation);
        // Engine Plane is XY with +Z as its normal. MakeFromZY fixes both the
        // facing and the roll, so the tear's long UV axis remains vertical.
        Rift->SetActorRotation(
            FRotationMatrix::MakeFromZY(-RiftForward, RiftVertical).Rotator());

        if (RiftGlow != nullptr)
        {
            RiftGlow->SetActorLocation(RiftLocation - RiftForward * 120.0f);
        }

        if (RiftDebris != nullptr && Dressing.RiftOpenness > 0.0f)
        {
            TArray<AActor*> DebrisActors;
            RiftDebris->GetAttachedActors(DebrisActors);
            const FVector Horizontal =
                FVector::CrossProduct(RiftVertical, -RiftForward).GetSafeNormal();
            const float Pull = FMath::SmoothStep(0.0f, 1.0f, Dressing.RiftOpenness);
            const float Delta = GetWorld() != nullptr ? GetWorld()->GetDeltaSeconds() : 0.0f;
            for (int32 Index = 0; Index < DebrisActors.Num(); ++Index)
            {
                AActor* Debris = DebrisActors[Index];
                if (Debris == nullptr)
                {
                    continue;
                }
                const float Seed = static_cast<float>(Index) + 0.5f;
                const float Angle = Seed * 2.39996323f + Pull * (1.2f + 0.07f * Index);
                const float OuterRadius = 4200.0f + FMath::Fmod(Seed * 1739.0f, 5200.0f);
                const float Radius = FMath::Lerp(
                    OuterRadius, 650.0f + 55.0f * Index, Pull);
                const float Depth = FMath::Lerp(
                    -3600.0f + FMath::Fmod(Seed * 977.0f, 7200.0f), 0.0f, Pull);
                Debris->SetActorLocation(
                    RiftLocation
                    + Horizontal * FMath::Cos(Angle) * Radius
                    + RiftVertical * FMath::Sin(Angle) * Radius
                    + RiftForward * Depth);
                Debris->AddActorLocalRotation(
                    FRotator(31.0f, 47.0f + Index * 0.5f, 23.0f) * Delta);
            }
        }
    }

    ACMLHUD* Hud = nullptr;
    if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        Hud = Cast<ACMLHUD>(Controller->GetHUD());
    }
    if (Hud != nullptr)
    {
        Hud->SetCinematicOverlay(
            Dressing.FlashAlpha, Dressing.FadeAlpha, Dressing.Eyelid);
        if (APlayerCameraManager* Manager = UGameplayStatics::GetPlayerCameraManager(this, 0))
        {
            Manager->StopCameraFade();
        }
    }
    else if (APlayerCameraManager* Manager = UGameplayStatics::GetPlayerCameraManager(this, 0))
    {
        // One overlay serves the flash, the fade and the eyelid: they never
        // want different colours at the same time, and stacking three would
        // multiply their alphas into something darker than any of them asked
        // for.
        const float Fade = FMath::Max(Dressing.FadeAlpha, Dressing.Eyelid);
        if (Fade > 0.001f)
        {
            Manager->SetManualCameraFade(Fade, FLinearColor::Black, false);
        }
        else if (Dressing.FlashAlpha > 0.001f)
        {
            Manager->SetManualCameraFade(Dressing.FlashAlpha, FLinearColor::White, false);
        }
        else
        {
            Manager->StopCameraFade();
        }
    }
}

float ACMLIntroDirector::ReadPilotYaw() const
{
    return LessonYaw;
}

void ACMLIntroDirector::ReadPilotInput()
{
    if (State.Shot != ECMLIntroShot::Flight)
    {
        return;
    }

    APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0);
    if (Controller == nullptr)
    {
        return;
    }

    float MouseX = 0.0f;
    float MouseY = 0.0f;
    Controller->GetInputMouseDelta(MouseX, MouseY);
    // Window focus/capture can produce one enormous delta. Unity rejects the
    // same warp, otherwise returning to the viewport throws the ship around.
    if (FMath::Abs(MouseX) > 250.0f || FMath::Abs(MouseY) > 250.0f)
    {
        return;
    }

    const float YawDelta = MouseX * LookSensitivity;
    PilotYaw += YawDelta;
    LessonYaw += YawDelta;
    PilotPitch = FMath::Clamp(
        PilotPitch - MouseY * LookSensitivity * 0.55f, -22.0f, 22.0f);
}

void ACMLIntroDirector::ApplyPilotHeading()
{
    if (AirshipHeading == nullptr)
    {
        return;
    }

    // Banking into the turn is what makes this a flown vehicle instead of a
    // camera-look tutorial.  Cockpit camera, hull and fill lights all inherit
    // this heading exactly as they do from Unity's AirshipHeading transform.
    const float Bank = FMath::Clamp(-PilotYaw * 0.35f, -28.0f, 28.0f);
    AirshipHeading->SetActorRotation(FRotator(PilotPitch, PilotYaw, Bank));
}

void ACMLIntroDirector::BeginPlay()
{
    Super::BeginPlay();
    BuildRuntimeStage();

    State = FCMLIntroState();
    AppliedShot = ECMLIntroShot::Complete;
    bHandedOver = false;
    bGameplayRestored = false;
    bRiftAxisAnchored = false;
    if (AirshipHeading != nullptr)
    {
        FlightAxisForward = AirshipHeading->GetActorForwardVector();
        FlightAxisRight = AirshipHeading->GetActorRightVector();
        FlightAxisUp = AirshipHeading->GetActorUpVector();
    }
    if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        Controller->SetIgnoreMoveInput(true);
        Controller->SetIgnoreLookInput(false);
        // Relative mouse motion is the flight input.  No button is part of the
        // gesture and no cursor is meant to escape the viewport during it.
        Controller->bShowMouseCursor = false;
        FInputModeGameOnly InputMode;
        InputMode.SetConsumeCaptureMouseDown(false);
        Controller->SetInputMode(InputMode);
    }

    // Apply the first shot before the first rendered frame. Waiting for Tick
    // briefly exposed the gameplay camera and could start the fade from the
    // wrong view in standalone builds.
    ApplyShot(State.Shot);
    AppliedShot = State.Shot;
}

void ACMLIntroDirector::Tick(const float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);
    if (bGameplayRestored)
    {
        return;
    }

    if (State.Shot != AppliedShot)
    {
        ApplyShot(State.Shot);
        AppliedShot = State.Shot;
        if (State.Shot == ECMLIntroShot::RiftOpen)
        {
            // Unity freezes the cockpit optical axis here. The rift is a place
            // the ship approaches, not a screen effect following later shake.
            AnchorRiftAxis();
        }
        // The handover rides on the first frame of full black, not on the fade
        // into it, so the swap is never on screen.
        // The island is entered at the fall, not at the blackout. Unity plays
        // the descent, the pass by the arch and the plough inside the gameplay
        // scene; cutting across only at full black meant the ship fell through
        // empty space and the whole arrival was missing. The white flash the
        // fall opens on is what covers the load.
        if (State.Shot == ECMLIntroShot::Fall)
        {
            HandOverToTheIsland();
        }
    }

    // What the shot looks like this instant. Driven off unscaled time so the
    // wobbles and the klaxon keep their rhythm even if the game is paused or
    // slowed underneath the opening.
    const float UnscaledTime = GetWorld() != nullptr
        ? static_cast<float>(GetWorld()->GetUnpausedTimeSeconds()) : 0.0f;
    ReadPilotInput();

    // The failure sequence levels the ship after the player has flown it.  The
    // tutorial itself leaves the real heading in the player's hands.
    if (State.Shot == ECMLIntroShot::Flight)
    {
        ApplyPilotHeading();
    }
    else if (State.Shot == ECMLIntroShot::Alarm)
    {
        const float Level = 1.0f - FMath::Exp(-3.2f * DeltaSeconds);
        PilotYaw = FMath::Lerp(PilotYaw, 0.0f, Level);
        PilotPitch = FMath::Lerp(PilotPitch, 0.0f, Level);
        ApplyPilotHeading();
    }

    const FCMLIntroDressing Dressing = FCMLIntroDressing_Evaluator::Evaluate(
        State.Shot, State.ElapsedInShot, Timings, UnscaledTime);
    ApplyDressing(Dressing);
    ApplyGrade(GradeForShot(State.Shot, State.ElapsedInShot, Timings));

    DriveThreatRock(DeltaSeconds);
    ApplyShake(Dressing.ShakeAmount, UnscaledTime);

    FCMLIntroInput Input;
    Input.YawDegrees = ReadPilotYaw();
    if (const APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        Input.bSkipRequested = Controller->WasInputKeyJustPressed(EKeys::SpaceBar)
            || Controller->WasInputKeyJustPressed(EKeys::Escape);
    }

    // The teaching card belongs to the HUD, which already knows how to draw the
    // glyphs; the director only says when and which way.
    if (ACMLHUD* Hud = Cast<ACMLHUD>(
            UGameplayStatics::GetPlayerController(this, 0) != nullptr
                ? UGameplayStatics::GetPlayerController(this, 0)->GetHUD()
                : nullptr))
    {
        Hud->SetTutorialCard(
            FCMLIntroSequence::ShouldShowTutorialCard(State),
            FCMLIntroSequence::TutorialDirection(State));
    }

    if (!FCMLIntroSequence::Advance(State, Timings, Input, DeltaSeconds, bAllowSkip))
    {
        return;
    }

    // The opening is over: the picture already changed at the blackout, so all
    // that is left is giving control back.
    RestoreGameplay();
}

void ACMLIntroDirector::HandOverToTheIsland()
{
    if (bHandedOver)
    {
        return;
    }
    bHandedOver = true;

    // Tell the island it is owed an arrival before asking for it: the flag has
    // to be set on the game instance, which is the only thing here that
    // survives the level change.
    if (UCMLGameInstance* Instance = GetGameInstance<UCMLGameInstance>())
    {
        Instance->bIntroArrivalPending = true;
    }

    // Fall belongs to the island world. Previously the flag was set here but
    // OpenLevel ran only after Fall+Crash+Blackout+Wake had elapsed in the
    // space map, leaving a long black gap and then a few orphaned streaks.
    if (!GameplayLevel.IsNone())
    {
        UE_LOG(LogCMLIntro, Display,
            TEXT("Entering arrival level '%s' behind the rift flash."),
            *GameplayLevel.ToString());
        UGameplayStatics::OpenLevel(this, GameplayLevel);
        return;
    }

    // Everything the opening owns goes now, behind full black. The wreck
    // disappearing and the parked hull coming back must not be visible for one
    // frame, which is why this runs on the first frame of blackout and never
    // during the fade into it.
    for (AActor* Group : {SpaceVisuals.Get(), WarpTunnel.Get(), StarStreaks.Get(),
                          Rift.Get(), RiftGlow.Get(), RiftDebris.Get(),
                          Asteroids.Get(), CockpitSparks.Get()})
    {
        SetGroupVisible(Group, false);
    }
    for (AActor* Light : AlertLights)
    {
        SetGroupVisible(Light, false);
    }

    MoveThePlayerAboard();

    // Back to the player's own camera. The image is already the island; nothing
    // accepts input yet.
    if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        if (APawn* Pawn = Controller->GetPawn())
        {
            Controller->SetViewTargetWithBlend(Pawn, 0.0f);
        }
    }
}

void ACMLIntroDirector::MoveThePlayerAboard()
{
    APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0);
    APawn* Pawn = Controller != nullptr ? Controller->GetPawn() : nullptr;
    AActor* Deck = FindByLabel(TEXT("CIN_AirshipHeading"));
    if (Pawn == nullptr || Deck == nullptr)
    {
        UE_LOG(LogCMLIntro, Warning,
            TEXT("Nowhere to put the pilot: the opening ends with the player wherever they were."));
        return;
    }

    // The authored pose is a body root, not a contact point, and the hull is
    // scaled. Dropping the pawn in a few centimetres above the floor resolves
    // as a fall on the exact frame control returns, which reads as a jolt right
    // after the eyes open — so the feet are put on the deck deliberately.
    const FVector Stand = Deck->GetActorLocation();
    const float HalfHeight = Pawn->GetSimpleCollisionHalfHeight();

    FHitResult Hit;
    // The probe starts just above the authored pose and never higher: from far
    // enough up it would clear the cabin roof and put the pilot on top of the
    // hull instead of inside it.
    const FVector From = Stand + FVector(0.0f, 0.0f, 50.0f);
    const FVector To = Stand - FVector(0.0f, 0.0f, 150.0f);
    FCollisionQueryParams Params(SCENE_QUERY_STAT(CMLIntroDeck), /*bTraceComplex=*/false, Pawn);
    FVector Feet = Stand;
    if (GetWorld() != nullptr
        && GetWorld()->LineTraceSingleByChannel(Hit, From, To, ECC_Visibility, Params)
        && Hit.GetActor() != nullptr
        && Hit.GetActor()->IsAttachedTo(Deck))
    {
        Feet.Z = Hit.ImpactPoint.Z;
    }

    Pawn->SetActorLocation(Feet + FVector(0.0f, 0.0f, HalfHeight), /*bSweep=*/false);
    if (Controller != nullptr)
    {
        Controller->SetControlRotation(Deck->GetActorRotation());
    }
}

void ACMLIntroDirector::RestoreGameplay()
{
    if (bGameplayRestored)
    {
        return;
    }
    bGameplayRestored = true;

    if (IConsoleVariable* D = IConsoleManager::Get().FindConsoleVariable(
            TEXT("r.LensDistortion.Panini.D"))) D->Set(0.0f, ECVF_SetByCode);
    if (IConsoleVariable* S = IConsoleManager::Get().FindConsoleVariable(
            TEXT("r.LensDistortion.Panini.S"))) S->Set(0.0f, ECVF_SetByCode);

    const FString CurrentLevel = UGameplayStatics::GetCurrentLevelName(this, true);
    if (!GameplayLevel.IsNone()
        && !CurrentLevel.Equals(GameplayLevel.ToString(), ESearchCase::CaseSensitive))
    {
        UE_LOG(LogCMLIntro, Display,
            TEXT("Opening complete; loading gameplay level '%s'."), *GameplayLevel.ToString());
        UGameplayStatics::OpenLevel(this, GameplayLevel);
        return;
    }

    if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        Controller->SetIgnoreMoveInput(false);
        Controller->SetIgnoreLookInput(false);
        // Whatever the last shot left on screen goes with the opening.
        if (APlayerCameraManager* Manager = Controller->PlayerCameraManager)
        {
            Manager->StopCameraFade();
        }
        if (ACMLHUD* Hud = Cast<ACMLHUD>(Controller->GetHUD()))
        {
            Hud->SetCinematicOverlay(0.0f, 0.0f, 0.0f);
        }
    }
    for (AActor* Actor : SuspendedActors)
    {
        if (Actor != nullptr)
        {
            Actor->SetActorTickEnabled(true);
        }
    }
    SuspendedActors.Reset();

    UE_LOG(LogCMLIntro, Display, TEXT("Opening complete; the player has control."));
}

#include "Intro/CMLIntroArrival.h"

#include "Camera/CameraActor.h"
#include "Camera/CameraComponent.h"
#include "Camera/PlayerCameraManager.h"
#include "Components/PointLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/PostProcessVolume.h"
#include "Engine/PointLight.h"
#include "Engine/StaticMesh.h"
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

DEFINE_LOG_CATEGORY_STATIC(LogCMLArrival, Log, All);

namespace
{
    /** The arch, and the hull already parked on the island, both by label. */
    constexpr const TCHAR* PortalLabel = TEXT("AncientStonePortal");
    constexpr const TCHAR* AirshipLabel = TEXT("Airship");
    AActor* FindByLabelSubstring(UWorld& World, const TCHAR* Token)
    {
        for (TActorIterator<AActor> It(&World); It; ++It)
        {
            AActor* Actor = *It;
            if (Actor == nullptr)
            {
                continue;
            }
            if (Actor->GetName().Contains(Token))
            {
                return Actor;
            }
#if WITH_EDITOR
            if (Actor->GetActorLabel().Contains(Token))
            {
                return Actor;
            }
#endif
        }
        return nullptr;
    }

    /** Ground under a point, so the wreck stops on the island and not in it. */
    float GroundHeight(UWorld& World, const FVector& Point, const float Fallback)
    {
        FHitResult Hit;
        const FVector Start = Point + FVector(0.0f, 0.0f, 20000.0f);
        const FVector End = Point - FVector(0.0f, 0.0f, 20000.0f);
        FCollisionQueryParams Params;
        Params.bTraceComplex = true;
        if (World.LineTraceSingleByChannel(Hit, Start, End, ECC_WorldStatic, Params))
        {
            return Hit.ImpactPoint.Z;
        }
        return Fallback;
    }
}

ACMLIntroArrival::ACMLIntroArrival()
{
    PrimaryActorTick.bCanEverTick = true;
}

void ACMLIntroArrival::BeginPlay()
{
    Super::BeginPlay();
    bPlanned = PlanTheDescent();
    if (!bPlanned)
    {
        // Nothing to fly through. Say which piece is missing rather than
        // quietly skipping to the wake: a silent skip is exactly how the
        // descent came to be absent without anything reporting it.
        UE_LOG(LogCMLArrival, Error,
            TEXT("The arrival needs the ancient portal and the parked airship in this level; "
                 "handing straight back to the player."));
        RestoreThePlayer();
        Stage = EStage::Done;
    }
}

bool ACMLIntroArrival::PlanTheDescent()
{
    UWorld* World = GetWorld();
    if (World == nullptr)
    {
        return false;
    }

    AActor* Portal = FindByLabelSubstring(*World, PortalLabel);
    ParkedAirship = FindByLabelSubstring(*World, AirshipLabel);
    if (Portal == nullptr || ParkedAirship == nullptr)
    {
        return false;
    }

    FVector PortalOrigin = FVector::ZeroVector;
    FVector PortalExtent = FVector::ZeroVector;
    Portal->GetActorBounds(/*bOnlyCollidingComponents=*/false, PortalOrigin, PortalExtent);

    // Unity takes the aperture from the arch's own bounds rather than a tuned
    // constant, so a re-authored portal keeps working.
    const FVector ApertureCentre(
        PortalOrigin.X, PortalOrigin.Y,
        PortalOrigin.Z - PortalExtent.Z + PortalExtent.Z * (2.0f * 0.42f));
    const float ApertureRadius = PortalExtent.Z * (2.0f * 0.24f);

    const FVector CrashSite = ParkedAirship->GetActorLocation();
    FVector Inbound = CrashSite - ApertureCentre;
    Inbound.Z = 0.0f;
    Inbound = Inbound.SizeSquared() > 1.0f ? Inbound.GetSafeNormal() : FVector::ForwardVector;

    // Metres in the ported skid, centimetres in the world.
    const float SkidDistance = FCMLIntroCrash::PredictedSkidDistance() * 100.0f;

    FVector Touchdown = CrashSite - Inbound * SkidDistance;
    Touchdown.Z = GroundHeight(*World, Touchdown, CrashSite.Z)
        + FCMLIntroCrash::HullClearance * 100.0f;
    FallEnd = Touchdown;

    // Past the pillars, not between them: the hull is far wider than the arch.
    const FVector Sideways = FVector::CrossProduct(FVector::UpVector, Inbound).GetSafeNormal();
    FallGate = ApertureCentre
        + Sideways * (ApertureRadius + 1500.0f)
        + FVector(0.0f, 0.0f, ApertureRadius * 0.35f);

    FallStart = ApertureCentre - Inbound * 24000.0f;
    FallStart.Z = FMath::Max(
        ApertureCentre.Z + 21000.0f,
        GroundHeight(*World, FallStart, ApertureCentre.Z) + 19000.0f);

    DiveRotation = Inbound.Rotation();
    SkidDirection = Inbound;

    // A render-only twin carries the crash; the parked hull is hidden until the
    // wreck is gone, so only one airship is ever on screen.
    ParkedAirship->SetActorHiddenInGame(true);

    FActorSpawnParameters Parameters;
    Parameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    Parameters.ObjectFlags |= RF_Transient;
    Wreck = World->SpawnActor<AStaticMeshActor>(FallStart, DiveRotation, Parameters);
    if (Wreck != nullptr)
    {
        // This actor is presentation-only. The gameplay bootstrap also scans
        // static-mesh asset paths to find the island's interactable airship;
        // without an explicit identity it mistook this transient twin for a
        // station on the following tick and recursively hid its root mesh.
        Wreck->Tags.AddUnique(TEXT("CMLIntroWreck"));

        UStaticMeshComponent* Mesh = Wreck->GetStaticMeshComponent();
        Mesh->SetMobility(EComponentMobility::Movable);
        Mesh->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            TEXT("/Game/_Project/Art/Vehicles/Airship/SM_Airship_Visual.SM_Airship_Visual")));
        Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);

        Wreck->SetActorScale3D(ParkedAirship->GetActorScale3D());
        UE_LOG(LogCMLArrival, Display,
            TEXT("Arrival cinematic wreck body=%s scale=%s, "
                 "authored pilot eye=%s."),
            Mesh->GetStaticMesh() != nullptr ? TEXT("visible") : TEXT("MISSING"),
            *Wreck->GetActorScale3D().ToCompactString(),
            *FVector(0.0f, 246.0f, 168.0f).ToCompactString());
    }

    Parameters.ObjectFlags |= RF_Transient;
    ArrivalCamera = World->SpawnActor<ACameraActor>(FallStart, DiveRotation, Parameters);
    if (ArrivalCamera != nullptr)
    {
        // Establish the same authored cockpit frame as the tutorial before the
        // opening white transition starts to clear.
        UpdateWreckCamera(FallStart, DiveRotation, 68.0f);
        if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
        {
            // A cut: the flash covering the level change is the transition.
            Controller->SetViewTargetWithBlend(ArrivalCamera, 0.0f);
            Controller->SetIgnoreMoveInput(true);
            Controller->SetIgnoreLookInput(true);
            if (APlayerCameraManager* Manager = Controller->PlayerCameraManager)
            {
                Manager->SetGameCameraCutThisFrame();
                // The level arrives behind the same white frame Unity uses.
                Manager->SetManualCameraFade(1.0f, FLinearColor::White, false);
            }
            if (ACMLHUD* Hud = Cast<ACMLHUD>(Controller->GetHUD()))
            {
                Hud->SetCinematicSuppressed(true);
            }
        }
    }

    // The ancient arch is not passive scenery. Unity creates an emissive,
    // refractive veil in its measured aperture and charges it only while the
    // wreck screams past; losing this was the largest missing arrival effect.
    PortalVeil = World->SpawnActor<AStaticMeshActor>(ApertureCentre, FRotator::ZeroRotator, Parameters);
    if (PortalVeil != nullptr)
    {
        UStaticMeshComponent* Mesh = PortalVeil->GetStaticMeshComponent();
        Mesh->SetMobility(EComponentMobility::Movable);
        Mesh->SetStaticMesh(LoadObject<UStaticMesh>(nullptr,
            TEXT("/Engine/BasicShapes/Plane.Plane")));
        Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Mesh->SetCastShadow(false);
        Mesh->SetMaterial(0, LoadObject<UMaterialInterface>(nullptr,
            TEXT("/Game/Migrated/Project/Art/Cinematics/Materials/M_CIN_PortalVeil.M_CIN_PortalVeil")));
        PortalVeilMaterial = Mesh->CreateDynamicMaterialInstance(0);
        if (PortalVeilMaterial != nullptr)
        {
            PortalVeilMaterial->SetScalarParameterValue(TEXT("_SwirlSpeed"), 1.25f);
            PortalVeilMaterial->SetScalarParameterValue(TEXT("_SwirlScale"), 3.8f);
            PortalVeilMaterial->SetScalarParameterValue(TEXT("_Refraction"), 0.09f);
            PortalVeilMaterial->SetScalarParameterValue(TEXT("_RimWidth"), 0.14f);
            PortalVeilMaterial->SetScalarParameterValue(TEXT("_Intensity"), 1.6f);
        }
        PortalVeil->SetActorScale3D(FVector(ApertureRadius / 50.0f));
        // Engine Plane faces +Z. Rotate that normal onto the horizontal portal
        // normal exactly; adding Euler angles to an arbitrary yaw is not the
        // same composition and left the veil lying obliquely in the arch.
        const FQuat Aim = FQuat::FindBetweenNormals(FVector::UpVector, -Inbound);
        PortalVeil->SetActorRotation(Aim.Rotator());
    }

    PortalLight = World->SpawnActor<APointLight>(ApertureCentre, FRotator::ZeroRotator, Parameters);
    if (PortalLight != nullptr)
    {
        UPointLightComponent* Light = Cast<UPointLightComponent>(PortalLight->GetLightComponent());
        Light->SetLightColor(FLinearColor(0.28f, 0.62f, 1.0f));
        Light->SetAttenuationRadius(ApertureRadius * 5.0f);
        Light->SetIntensity(0.0f);
        Light->SetCastShadows(false);
    }
    SetPortalCharge(0.0f);

    if (APostProcessVolume* Volume = World->SpawnActor<APostProcessVolume>())
    {
        Volume->bUnbound = true;
        Volume->Priority = 100.0f;
        Volume->BlendWeight = 1.0f;
        GradeVolume = Volume;

        // Unreal's temporal motion blur can be imperceptible when the camera
        // and the cockpit share almost the same transform. Unity's reference
        // has an unmistakable zoom-smear even in a still frame, so the arrival
        // owns a lightweight post-process that reproduces that optical trail
        // explicitly and independently of frame rate. Chromatic aberration is
        // still supplied by the grade below and is deliberately untouched.
        if (UMaterialInterface* BlurAsset = LoadObject<UMaterialInterface>(nullptr,
                TEXT("/Game/_Project/Art/Cinematics/Materials/"
                     "M_CIN_ArrivalVelocityBlur.M_CIN_ArrivalVelocityBlur")))
        {
            ArrivalBlurMaterial = UMaterialInstanceDynamic::Create(BlurAsset, this);
            if (ArrivalBlurMaterial != nullptr)
            {
                ArrivalBlurMaterial->SetScalarParameterValue(TEXT("BlurAmount"), 0.0f);
                Volume->Settings.AddBlendable(ArrivalBlurMaterial, 1.0f);
            }
        }
    }

    UE_LOG(LogCMLArrival, Display,
        TEXT("Arrival planned: start %s, gate %s, touchdown %s."),
        *FallStart.ToCompactString(), *FallGate.ToCompactString(), *FallEnd.ToCompactString());
    return true;
}

FVector ACMLIntroArrival::FallPoint(const float Travel) const
{
    return Travel <= PortalGateAt
        ? FMath::Lerp(FallStart, FallGate, Travel / PortalGateAt)
        : FMath::Lerp(FallGate, FallEnd, (Travel - PortalGateAt) / (1.0f - PortalGateAt));
}

void ACMLIntroArrival::ApplyGrade(const FCMLIntroGradeState& Grade)
{
    if (IConsoleVariable* D = IConsoleManager::Get().FindConsoleVariable(
            TEXT("r.LensDistortion.Panini.D")))
    {
        D->Set(FMath::Clamp(Grade.Panini + Grade.LensDistortion * 0.35f, 0.0f, 1.0f),
            ECVF_SetByCode);
    }
    if (IConsoleVariable* S = IConsoleManager::Get().FindConsoleVariable(
            TEXT("r.LensDistortion.Panini.S")))
    {
        S->Set(FMath::Clamp(Grade.LensDistortion * 0.18f, 0.0f, 0.2f), ECVF_SetByCode);
    }
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
    Settings.bOverride_SceneFringeIntensity = true;
    Settings.SceneFringeIntensity = Grade.ChromaticAberration * 5.0f;
    Settings.bOverride_MotionBlurAmount = true;
    Settings.MotionBlurAmount = Grade.MotionBlur;
    // Unreal defaults to a five-percent shutter cap, which clips nearly all of
    // the long foreground trails visible in Unity's descent. A lower per-object
    // threshold keeps the airship struts in the velocity pass, while the 30 fps
    // target makes the result stable at both editor and packaged frame rates.
    Settings.bOverride_MotionBlurMax = true;
    Settings.MotionBlurMax = FMath::Lerp(8.0f, 42.0f, Grade.MotionBlur);
    Settings.bOverride_MotionBlurTargetFPS = true;
    Settings.MotionBlurTargetFPS = 30;
    Settings.bOverride_MotionBlurPerObjectSize = true;
    Settings.MotionBlurPerObjectSize = 0.1f;
    Settings.bOverride_VignetteIntensity = true;
    Settings.VignetteIntensity = Grade.VignetteIntensity;
    Settings.bOverride_VignetteColor = true;
    Settings.VignetteColor = Grade.VignetteColor;
    Settings.bOverride_FilmGrainIntensity = true;
    Settings.FilmGrainIntensity = Grade.FilmGrain;
    Settings.bOverride_ColorContrast = true;
    const float Contrast = 1.0f + Grade.Contrast * 0.01f;
    Settings.ColorContrast = FVector4(Contrast, Contrast, Contrast, 1.0f);
    const float Saturation = 1.0f + Grade.Saturation * 0.01f;
    Settings.bOverride_ColorSaturation = true;
    Settings.ColorSaturation = FVector4(Saturation, Saturation, Saturation, 1.0f);
    Settings.bOverride_SceneColorTint = true;
    Settings.SceneColorTint = Grade.ColorFilter;
    Settings.bOverride_AutoExposureMinBrightness = true;
    Settings.AutoExposureMinBrightness = 1.0f;
    Settings.bOverride_AutoExposureMaxBrightness = true;
    Settings.AutoExposureMaxBrightness = 1.0f;
    Settings.bOverride_AutoExposureBias = true;
    Settings.AutoExposureBias = Grade.PostExposure;
}

void ACMLIntroArrival::ApplyFrameOverlay(
    const float FlashAlpha, const float FadeAlpha, const float Eyelid)
{
    APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0);
    if (Controller == nullptr)
    {
        return;
    }
    if (ACMLHUD* Hud = Cast<ACMLHUD>(Controller->GetHUD()))
    {
        Hud->SetCinematicOverlay(FlashAlpha, FadeAlpha, Eyelid);
        if (Controller->PlayerCameraManager != nullptr)
        {
            Controller->PlayerCameraManager->StopCameraFade();
        }
        return;
    }
    APlayerCameraManager* Manager = Controller->PlayerCameraManager;
    if (Manager == nullptr)
    {
        return;
    }
    const float Black = FMath::Max(FadeAlpha, Eyelid);
    if (Black > 0.001f)
    {
        Manager->SetManualCameraFade(FMath::Clamp(Black, 0.0f, 1.0f), FLinearColor::Black, false);
    }
    else if (FlashAlpha > 0.001f)
    {
        Manager->SetManualCameraFade(
            FMath::Clamp(FlashAlpha, 0.0f, 1.0f), FLinearColor(0.88f, 0.95f, 1.0f), false);
    }
    else
    {
        Manager->StopCameraFade();
    }
}

void ACMLIntroArrival::ApplyCameraShake(const float Amount, const float TimeSeconds)
{
    if (ArrivalCamera == nullptr || Amount <= 0.0001f)
    {
        return;
    }
    const float T = TimeSeconds * 21.0f;
    const FVector Offset(
        FMath::PerlinNoise1D(T),
        FMath::PerlinNoise1D(T + 37.13f),
        FMath::PerlinNoise1D(T * 0.63f + 71.9f));

    // Give the camera the broad damaged-flight movement seen in Unity.
    ArrivalCamera->AddActorLocalOffset(Offset * Amount * 7.0f);
    ArrivalCamera->AddActorLocalRotation(FRotator(
        Offset.Y * Amount * 1.35f,
        Offset.Z * Amount * 1.35f,
        Offset.X * Amount * 1.35f));

    // The hull also chatters a little against the pilot's head. Moving only
    // the camera made the whole image sway together and the cabin still read
    // as rigid. This opposite, smaller visual motion makes the struts and dash
    // visibly vibrate relative to the island and produces real mesh velocity.
    if (Wreck != nullptr)
    {
        Wreck->AddActorLocalOffset(-Offset * Amount * 4.5f);
        Wreck->AddActorLocalRotation(FRotator(
            -Offset.Y * Amount * 0.72f,
            -Offset.Z * Amount * 0.72f,
            -Offset.X * Amount * 0.72f));
    }

    if (ArrivalBlurMaterial != nullptr)
    {
        ArrivalBlurMaterial->SetScalarParameterValue(
            TEXT("BlurAmount"), FMath::Clamp(0.18f + Amount * 0.62f, 0.0f, 1.0f));
    }
}

void ACMLIntroArrival::SetPortalCharge(const float Charge)
{
    const float Value = FMath::Clamp(Charge, 0.0f, 1.0f);
    if (PortalVeilMaterial != nullptr)
    {
        PortalVeilMaterial->SetScalarParameterValue(TEXT("_Charge"), Value);
    }
    if (PortalLight != nullptr)
    {
        PortalLight->GetLightComponent()->SetIntensity(Value * 65000.0f);
    }
}

void ACMLIntroArrival::UpdateWreckCamera(
    const FVector& Position, const FRotator& Flight, const float FieldOfView)
{
    if (ArrivalCamera == nullptr)
    {
        return;
    }
    // REF_PilotCamera is authored in the mesh basis at (0, 2.46, 1.68)m and
    // the airship's prow runs down mesh +Y. AStaticMeshActor uses its mesh as
    // the actor root, so a relative -90 yaw set only at spawn is not persistent:
    // the next SetActorLocationAndRotation replaces it. That was the mismatch
    // which left the arrival camera in the nominal pilot position while the
    // cockpit itself had turned broadside and disappeared from view.
    const FVector AuthoredEyeLocal(0.0f, 246.0f, 168.0f);
    if (Wreck != nullptr)
    {
        const FQuat MeshBasis = FRotator(0.0f, -90.0f, 0.0f).Quaternion();
        const FRotator VisualRotation =
            (Flight.Quaternion() * MeshBasis).Rotator();
        Wreck->SetActorLocationAndRotation(Position, VisualRotation);

        // This is deliberately the same calculation used by the working
        // tutorial cockpit: transform the authored eye through the live hull
        // component, including its 1.51 island scale.
        const FTransform HullTransform =
            Wreck->GetStaticMeshComponent()->GetComponentTransform();
        ArrivalCamera->SetActorLocation(
            HullTransform.TransformPosition(AuthoredEyeLocal));
    }
    else
    {
        ArrivalCamera->SetActorLocation(Position);
    }
    ArrivalCamera->SetActorRotation(Flight);
    ArrivalCamera->GetCameraComponent()->SetFieldOfView(FieldOfView);
}

void ACMLIntroArrival::RestoreThePlayer()
{
    if (Wreck != nullptr)
    {
        Wreck->Destroy();
        Wreck = nullptr;
    }
    if (PortalVeil != nullptr)
    {
        PortalVeil->Destroy();
        PortalVeil = nullptr;
    }
    if (PortalLight != nullptr)
    {
        PortalLight->Destroy();
        PortalLight = nullptr;
    }
    if (ParkedAirship != nullptr)
    {
        ParkedAirship->SetActorHiddenInGame(false);
    }
    if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
    {
        if (APawn* Pawn = Controller->GetPawn())
        {
            if (ParkedAirship != nullptr)
            {
                // Unity's authored PilotExitBodyRootPosition (0.55, .70,
                // .55)m mapped to Unreal's forward/right/up axes. This happens
                // under full black, before the player camera is revealed.
                const FVector Stand = ParkedAirship->GetActorTransform().TransformPosition(
                    FVector(55.0f, 55.0f, 70.0f));
                Pawn->DetachFromActor(FDetachmentTransformRules::KeepWorldTransform);
                Pawn->SetActorLocationAndRotation(
                    Stand, ParkedAirship->GetActorRotation(), false);
                Controller->SetControlRotation(ParkedAirship->GetActorRotation());
            }
            Controller->SetViewTargetWithBlend(Pawn, 0.0f);
            WakeCamera = Pawn->FindComponentByClass<UCameraComponent>();
            if (WakeCamera != nullptr)
            {
                WakeCameraRestLocation = WakeCamera->GetRelativeLocation();
                WakeCameraRestRotation = WakeCamera->GetRelativeRotation();
            }
            if (APlayerCameraManager* Manager = Controller->PlayerCameraManager)
            {
                Manager->SetGameCameraCutThisFrame();
            }
        }
    }
    if (GradeVolume != nullptr)
    {
        GradeVolume->Destroy();
        GradeVolume = nullptr;
    }
    ArrivalBlurMaterial = nullptr;
}

void ACMLIntroArrival::Tick(const float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);
    if (!bPlanned || Stage == EStage::Done)
    {
        return;
    }

    Elapsed += DeltaSeconds;
    const float Time = GetWorld() != nullptr
        ? static_cast<float>(GetWorld()->GetUnpausedTimeSeconds()) : 0.0f;

    switch (Stage)
    {
        case EStage::Falling:
        {
            const float Progress = FMath::Clamp(Elapsed / FMath::Max(FallSeconds, 0.01f), 0.0f, 1.0f);
            // Gravity, not a dolly: the descent accelerates all the way in.
            const float Travel = FMath::Pow(Progress, 1.55f);
            const FVector Position = FallPoint(Travel);
            const FVector Ahead = FallPoint(FMath::Min(Travel + 0.035f, 1.0f));
            const FVector Heading = Ahead - Position;
            const FRotator Flight =
                Heading.SizeSquared() > 1.0f ? Heading.Rotation() : DiveRotation;

            // A wounded hull does not fly straight, and the tumble grows as the
            // ground gets closer.
            const float Tumble = FMath::Lerp(6.0f, 34.0f, Progress);
            const FRotator Wobble(
                FMath::Sin(Time * 2.3f) * Tumble * 0.35f,
                FMath::Sin(Time * 1.7f) * Tumble * 0.22f,
                FMath::Sin(Time * 2.9f) * Tumble);
            const FRotator WreckRotation =
                (Flight.Quaternion() * Wobble.Quaternion()).Rotator();

            // The Unity eye is parented to the damaged hull, so it inherits the
            // tumble as well as the trajectory. Using Flight alone made this
            // whole descent read like a rigid camera dolly around a wobbling prop.
            UpdateWreckCamera(Position, WreckRotation,
                FMath::Lerp(68.0f, 88.0f, FMath::SmoothStep(0.0f, 1.0f, Progress)));
            ApplyCameraShake(FMath::Lerp(0.48f, 1.18f, FMath::Pow(Progress, 1.45f)), Time);

            const float Gate = 1.0f - FMath::Abs(Travel - PortalGateAt) / 0.22f;
            SetPortalCharge(FMath::Clamp(Gate, 0.0f, 1.0f));

            FCMLIntroGradeState Grade = FCMLIntroGradeState::Cruise();
            const float Settle = FMath::SmoothStep(0.02f, 0.42f, Progress);
            Grade.BloomIntensity = FMath::Lerp(6.5f, 1.9f, Settle);
            Grade.ChromaticAberration = FMath::Lerp(0.95f, 0.22f, Settle) + Progress * 0.34f;
            Grade.LensDistortion = FMath::Lerp(0.85f, 0.12f, Settle) + Progress * 0.24f;
            Grade.MotionBlur = FMath::Clamp(
                FMath::Lerp(0.92f, 0.62f, Settle) + Progress * 0.32f, 0.0f, 1.0f);
            Grade.Panini = FMath::Lerp(0.5f, 0.18f, Settle);
            Grade.PostExposure = FMath::Lerp(2.4f, 0.0f, Settle);
            Grade.Saturation = FMath::Lerp(-30.0f, 4.0f, Settle);
            Grade.VignetteIntensity = FMath::Lerp(0.62f, 0.34f, Settle) + Progress * 0.2f;
            ApplyGrade(Grade);
            ApplyFrameOverlay(
                1.0f - FMath::SmoothStep(0.0f, 0.9f, FMath::Min(Elapsed, 0.9f)),
                0.0f, 0.0f);

            if (Elapsed >= FallSeconds)
            {
                Stage = EStage::Skidding;
                Elapsed = 0.0f;
                Skid = FCMLIntroCrash::Touchdown();
                SkidHeight = FCMLIntroCrash::HullClearance * 100.0f;
                SkidVerticalVelocity = -3400.0f;
                CrashJolt = 1.4f;
            }
            break;
        }

        case EStage::Skidding:
        {
            SetPortalCharge(0.0f);
            // Advance returns true on the frame the skid reaches rest. The old
            // inverted condition blacked out on frame one, erasing the landing.
            const bool bStoppedThisFrame = FCMLIntroCrash::Advance(Skid, DeltaSeconds);
            FVector WreckPosition = FallEnd + SkidDirection * (Skid.Travelled * 100.0f);
            const float HullFloor = FCMLIntroCrash::HullClearance * 100.0f;
            SkidVerticalVelocity -= 3000.0f * DeltaSeconds;
            SkidHeight += SkidVerticalVelocity * DeltaSeconds;
            if (SkidHeight <= HullFloor)
            {
                SkidHeight = HullFloor;
                if (SkidVerticalVelocity < -700.0f)
                {
                    SkidVerticalVelocity *= -0.34f;
                    CrashJolt = FMath::Max(
                        CrashJolt, Skid.Speed * 0.012f + 0.35f);
                }
                else
                {
                    SkidVerticalVelocity = 0.0f;
                }
            }
            WreckPosition.Z = GroundHeight(*GetWorld(), WreckPosition, FallEnd.Z)
                + SkidHeight;
            if (Wreck != nullptr)
            {
                const float Carry = FMath::Clamp(
                    Skid.Speed / FCMLIntroCrash::TouchdownSpeed, 0.0f, 1.0f);
                const float Slew = FMath::Sin(Time * 1.9f) * 16.0f * Carry;
                const float Dig = FMath::Lerp(2.0f, 21.0f, Carry);
                const float Roll = FMath::Sin(Time * 3.1f) * 19.0f * Carry;
                const FRotator WreckRotation =
                    (SkidDirection.Rotation().Quaternion()
                        * FRotator(Dig, Slew * 0.35f, Roll).Quaternion()).Rotator();
                UpdateWreckCamera(WreckPosition, WreckRotation, 88.0f);

                const float Impact = FMath::Exp(-Elapsed * 4.5f);
                CrashJolt = FMath::Max(0.0f, CrashJolt - DeltaSeconds * 3.2f);
                ApplyCameraShake(0.32f + Carry * 1.28f + Impact * 0.62f + CrashJolt, Time);

                FCMLIntroGradeState Grade = FCMLIntroGradeState::Cruise();
                Grade.BloomIntensity = 1.9f;
                Grade.ChromaticAberration = 0.42f * Carry + 0.06f;
                Grade.LensDistortion = 0.3f * Carry;
                Grade.MotionBlur = 0.48f + Carry * 0.46f;
                Grade.Saturation = FMath::Lerp(4.0f, -50.0f, 1.0f - Carry);
                Grade.VignetteIntensity = FMath::Lerp(0.54f, 0.9f, 1.0f - Carry);
                ApplyGrade(Grade);

                const float CrashCeiling = FMath::Max(
                    FCMLIntroCrash::TouchdownSpeed / FCMLIntroCrash::Friction, 0.01f);
                const float Fading = FMath::Max(1.0f - Carry, Elapsed / CrashCeiling);
                const float Eyelid = FMath::SmoothStep(0.18f, 0.88f, Fading);
                const float Fade = FMath::SmoothStep(0.72f, 1.0f, Fading);
                ApplyFrameOverlay(FMath::Max(0.0f, 0.7f - Elapsed * 4.5f), Fade, Eyelid);
            }
            if (bStoppedThisFrame || Skid.HasStopped())
            {
                Stage = EStage::Blackout;
                Elapsed = 0.0f;
            }
            break;
        }

        case EStage::Blackout:
            ApplyFrameOverlay(0.0f, 1.0f, 1.0f);
            if (Elapsed >= BlackoutSeconds)
            {
                // The handover rides inside full black, so the wreck vanishing
                // and the parked hull returning are never on screen.
                RestoreThePlayer();
                Stage = EStage::Waking;
                Elapsed = 0.0f;
            }
            break;

        case EStage::Waking:
        {
            const float Progress = FMath::Clamp(Elapsed / FMath::Max(WakeSeconds, 0.01f), 0.0f, 1.0f);
            if (WakeCamera != nullptr)
            {
                const float Rise = FMath::SmoothStep(0.18f, 0.92f, Progress);
                const float Settle = FMath::SmoothStep(0.55f, 1.0f, Progress);
                const float Sway = FMath::Sin(Time * 1.35f) * 2.4f * (1.0f - Settle);
                WakeCamera->SetRelativeLocation(WakeCameraRestLocation
                    + FVector(FMath::Lerp(-16.0f, 0.0f, Rise), 0.0f,
                        FMath::Lerp(-82.0f, 0.0f, Rise)));
                WakeCamera->SetRelativeRotation(WakeCameraRestRotation
                    + FRotator(FMath::Lerp(26.0f, 0.0f, Rise),
                        Sway * 0.6f, FMath::Lerp(-34.0f, 0.0f, Rise) + Sway));
            }
            const float Blink = FMath::Clamp(1.0f - Progress * 2.2f, 0.0f, 1.0f)
                + FMath::Max(0.0f, 0.62f - FMath::Abs(Progress - 0.38f) * 5.0f);
            const float Fade = FMath::Clamp(Blink, 0.0f, 1.0f) * 0.94f;
            const float Eyelid = FMath::Clamp(FMath::Max(Blink,
                1.0f - FMath::SmoothStep(0.0f, 1.0f, Progress * 1.15f)), 0.0f, 1.0f);
            ApplyFrameOverlay(0.0f, Fade, Eyelid);
            if (Elapsed >= WakeSeconds)
            {
                ApplyFrameOverlay(0.0f, 0.0f, 0.0f);
                if (APlayerController* Controller = UGameplayStatics::GetPlayerController(this, 0))
                {
                    Controller->SetIgnoreMoveInput(false);
                    Controller->SetIgnoreLookInput(false);
                    if (ACMLHUD* Hud = Cast<ACMLHUD>(Controller->GetHUD()))
                    {
                        Hud->SetCinematicSuppressed(false);
                    }
                }
                if (WakeCamera != nullptr)
                {
                    WakeCamera->SetRelativeLocation(WakeCameraRestLocation);
                    WakeCamera->SetRelativeRotation(WakeCameraRestRotation);
                }
                if (IConsoleVariable* D = IConsoleManager::Get().FindConsoleVariable(
                        TEXT("r.LensDistortion.Panini.D"))) D->Set(0.0f, ECVF_SetByCode);
                if (IConsoleVariable* S = IConsoleManager::Get().FindConsoleVariable(
                        TEXT("r.LensDistortion.Panini.S"))) S->Set(0.0f, ECVF_SetByCode);
                UE_LOG(LogCMLArrival, Display, TEXT("Arrival complete; the pilot is awake."));
                Stage = EStage::Done;
                Destroy();
            }
            break;
        }

        default:
            break;
    }
}

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Presentation/CMLIntroCrash.h"
#include "Presentation/CMLIntroGradeState.h"

#include "CMLIntroArrival.generated.h"

class ACameraActor;
class APostProcessVolume;
class UMaterialInstanceDynamic;
class APointLight;
class AStaticMeshActor;
class UMaterialInstanceDynamic;
class UCameraComponent;

/**
 * The arrival: the wreck falling past the island's own ancient portal, the
 * plough along the ground, and the pilot waking aboard.
 *
 * Unity runs this half of the opening inside the gameplay scene rather than the
 * cinematic one — `IntroCinematicController` owns an arrival camera of its own
 * and only moves the real player once, behind a black screen, at the very end.
 * The port had the whole opening in a separate map and cut to the island at the
 * blackout, which is why the descent, the pass by the arch and the ploughing
 * stop were all missing: there was nothing on the far side of the rift to fall
 * through.
 *
 * The level change now happens on entering the fall, hidden under the white
 * flash Unity already blows out at exactly that moment, and this actor plays
 * the rest of the sequence in the island world.
 */
UCLASS()
class CHANGINGMYLIFE_API ACMLIntroArrival : public AActor
{
    GENERATED_BODY()

public:
    ACMLIntroArrival();

    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;

    /** Seconds of descent, matching the opening's Fall shot. */
    UPROPERTY(EditAnywhere, Category="CML|Intro") float FallSeconds = 7.0f;
    UPROPERTY(EditAnywhere, Category="CML|Intro") float BlackoutSeconds = 2.8f;
    UPROPERTY(EditAnywhere, Category="CML|Intro") float WakeSeconds = 4.2f;

private:
    enum class EStage : uint8 { Falling, Skidding, Blackout, Waking, Done };

    /** Measures the arch and the parked hull, and lays out the dive. */
    bool PlanTheDescent();

    /** The point on the dive at `Travel` in 0..1, gated beside the arch. */
    FVector FallPoint(float Travel) const;

    void ApplyGrade(const FCMLIntroGradeState& Grade);
    void ApplyFrameOverlay(float FlashAlpha, float FadeAlpha, float Eyelid);
    void ApplyCameraShake(float Amount, float TimeSeconds);
    void SetPortalCharge(float Charge);
    void UpdateWreckCamera(const FVector& Position, const FRotator& Flight, float FieldOfView);
    void RestoreThePlayer();

    UPROPERTY() TObjectPtr<ACameraActor> ArrivalCamera;
    UPROPERTY() TObjectPtr<AStaticMeshActor> Wreck;
    UPROPERTY() TObjectPtr<APostProcessVolume> GradeVolume;
    UPROPERTY() TObjectPtr<UMaterialInstanceDynamic> ArrivalBlurMaterial;
    UPROPERTY() TObjectPtr<AActor> ParkedAirship;
    UPROPERTY() TObjectPtr<AStaticMeshActor> PortalVeil;
    UPROPERTY() TObjectPtr<UMaterialInstanceDynamic> PortalVeilMaterial;
    UPROPERTY() TObjectPtr<APointLight> PortalLight;
    UPROPERTY() TObjectPtr<UCameraComponent> WakeCamera;
    FVector WakeCameraRestLocation = FVector::ZeroVector;
    FRotator WakeCameraRestRotation = FRotator::ZeroRotator;

    /**
     * Unity routes the descent through an explicit gate point beside the arch:
     * the hull is far wider than the opening, so it screams past the pillars
     * rather than through them, and the gate is what guarantees the portal is
     * actually in shot on the way down.
     */
    FVector FallStart = FVector::ZeroVector;
    FVector FallGate = FVector::ZeroVector;
    FVector FallEnd = FVector::ZeroVector;
    FRotator DiveRotation = FRotator::ZeroRotator;
    FVector SkidDirection = FVector::ForwardVector;

    /** Where along the dive the arch is passed. */
    static constexpr float PortalGateAt = 0.36f;

    EStage Stage = EStage::Falling;
    float Elapsed = 0.0f;
    FCMLIntroSkidState Skid;
    /** Vertical contact is independent from horizontal friction: Unity lets
     *  the hull rebound once or twice while it is still ploughing forward. */
    float SkidHeight = FCMLIntroCrash::HullClearance * 100.0f;
    float SkidVerticalVelocity = -3400.0f;
    bool bPlanned = false;
    float CrashJolt = 0.0f;
};

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Presentation/CMLIntroDressing.h"
#include "Presentation/CMLIntroSequence.h"
#include "Presentation/CMLIntroThreat.h"

#include "CMLIntroDirector.generated.h"

class ACameraActor;

/**
 * Drives the opening: runs `FCMLIntroSequence` and points the map's actors at
 * whatever the current shot needs.
 *
 * The scene converter brought across the opening's scenery, lights and cameras
 * but not its scripts, so the map had everything to look at and nothing to move
 * it. This is the missing half. It finds the actors by the labels the Unity
 * scene gave them (`CIN_*`) rather than by hand-wired references, so the map can
 * be reconverted without re-authoring anything here.
 *
 * A label that finds nothing is reported, not skipped quietly: a missing rift
 * means the rift shot plays to an empty screen, which is exactly the failure
 * this actor exists to fix and the last one anybody would notice in a log.
 */
UCLASS()
class CHANGINGMYLIFE_API ACMLIntroDirector : public AActor
{
    GENERATED_BODY()

public:
    ACMLIntroDirector();

    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;

    /** The authored durations; the Unity scene's values are the defaults. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro")
    FCMLIntroTimings Timings;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro")
    bool bAllowSkip = true;

    /** The level to open once the opening is over. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro")
    FName GameplayLevel = TEXT("A_10_StarterIsland_AxisPreview");

    UPROPERTY(BlueprintReadOnly, Category="CML|Intro")
    FCMLIntroState State;

private:
    /** Finds an actor by the label its Unity GameObject had. */
    AActor* FindByLabel(const FString& Label) const;

    void CacheActors();
    void BuildRuntimeStage();
    /** Spawns the complete So Stylized Alien preset only in the intro world,
     * fixes it at midnight and hides its cloud renderers. */
    void BuildCinematicAlienSky();
    AActor* SpawnNamedActor(UClass* ActorClass, const TCHAR* Name, const FTransform& Transform);
    void ApplyShot(ECMLIntroShot Shot);

    /** Pushes one instant's look at the cameras, lights and the hull. */
    void ApplyDressing(const struct FCMLIntroDressing& Dressing);

    /**
     * Makes the dynamic material instances the shot parameters are pushed into.
     *
     * The ported masters keep the Unity parameter names (`_Openness`, `_Speed`
     * and the rest), so the values the dressing computes go straight in without
     * a translation table that would have to be kept in step by hand.
     */
    void CacheMaterials();
    /** Capture the cockpit optical axis once, as Unity does when the tear
     * begins. The rift stays in the world and keeps a true vertical up axis. */
    void AnchorRiftAxis();

    /** Sets a scalar only if the material actually has it, quietly. */
    static void SetScalar(class UMaterialInstanceDynamic* Material, FName Parameter, float Value);

    /**
     * Everything the opening owns is dismantled here, behind a black screen, so
     * the waking shot is already the island rendering itself.
     *
     * Done on the first frame of full black and never during the fade: the
     * wreck disappearing and the parked hull coming back must not be visible
     * for a single frame.
     */
    void HandOverToTheIsland();

    /** Puts the pilot on the deck, with their feet actually on it. */
    void MoveThePlayerAboard();

    /** Gives control back. Separate from the handover: the picture changes at
     *  the blackout, but nothing accepts input until the eyes are open. */
    void RestoreGameplay();
    void SetGroupVisible(AActor* Root, bool bVisible);
    void LookThrough(ACameraActor* Camera);
    float ReadPilotYaw() const;
    void ReadPilotInput();
    void ApplyPilotHeading();

    UPROPERTY() TObjectPtr<ACameraActor> ChaseCamera;
    UPROPERTY() TObjectPtr<ACameraActor> CockpitCamera;
    UPROPERTY() TObjectPtr<AActor> AirshipHeading;
    UPROPERTY() TObjectPtr<AActor> AirshipAttitude;
    UPROPERTY() TObjectPtr<AActor> SpaceVisuals;
    UPROPERTY() TObjectPtr<AActor> WarpTunnel;
    UPROPERTY() TObjectPtr<AActor> StarStreaks;
    UPROPERTY() TObjectPtr<AActor> Rift;
    UPROPERTY() TObjectPtr<AActor> RiftGlow;
    UPROPERTY() TObjectPtr<AActor> RiftDebris;
    UPROPERTY() TObjectPtr<AActor> Asteroids;
    UPROPERTY() TObjectPtr<AActor> CockpitSparks;
    UPROPERTY() TObjectPtr<AActor> AirshipVisual;
    UPROPERTY() TObjectPtr<AActor> SpaceBackdrop;
    UPROPERTY() TObjectPtr<AActor> SpaceKeyLight;
    UPROPERTY() TObjectPtr<AActor> SpaceRimLight;
    UPROPERTY() TObjectPtr<AActor> CockpitFillLight;
    UPROPERTY() TObjectPtr<AActor> CinematicAlienSky;
    UPROPERTY() TArray<TObjectPtr<AActor>> AlertLights;

    /** The shot applied last, so a shot's setup runs once and not every frame. */
    ECMLIntroShot AppliedShot = ECMLIntroShot::Complete;
    bool bHandedOver = false;
    bool bGameplayRestored = false;

    /**
     * Unity consumes relative mouse motion while the button is up and applies
     * it to the hull.  These are deliberately owned by the opening rather than
     * borrowed from ControlRotation: the latter turns the camera, starts from
     * an arbitrary gameplay heading, and on some viewport capture modes does
     * not move until a mouse button is held.
     */
    UPROPERTY(EditAnywhere, Category="CML|Intro|Flight")
    float LookSensitivity = 0.11f;
    float PilotYaw = 0.0f;
    float PilotPitch = 0.0f;
    float LessonYaw = 0.0f;
    FVector FlightAxisForward = FVector::ForwardVector;
    FVector FlightAxisRight = FVector::RightVector;
    FVector FlightAxisUp = FVector::UpVector;
    FVector RiftOrigin = FVector::ZeroVector;
    FVector RiftForward = FVector::ForwardVector;
    FVector RiftVertical = FVector::UpVector;
    bool bRiftAxisAnchored = false;

    /** Behaviour switched off for the opening, to be switched back on after. */
    UPROPERTY() TArray<TObjectPtr<AActor>> SuspendedActors;

    UPROPERTY() TObjectPtr<class UMaterialInstanceDynamic> WarpMaterial;
    UPROPERTY() TObjectPtr<class UMaterialInstanceDynamic> RiftMaterial;
    UPROPERTY() TObjectPtr<class UMaterialInstanceDynamic> StreakMaterial;

    /**
     * The backdrop was the one cinematic surface with no dynamic instance, so
     * its _WarpBlend never left zero and the sky never smeared. Unity drives it
     * to 1 for the jump and eases it back to 0.1 as the rift opens; the value
     * was already being computed into the dressing and simply never delivered.
     */
    UPROPERTY() TObjectPtr<class UMaterialInstanceDynamic> SpaceSkyMaterial;

    /** The global grade volume Unity keeps on the cinematic root. */
    UPROPERTY() TObjectPtr<class APostProcessVolume> GradeVolume;

    /** The one rock that is actually on a collision course during the lesson. */
    TWeakObjectPtr<AActor> ThreatRock;
    FCMLIntroThreatState ThreatState;
    ECMLIntroFlightStep AppliedFlightStep = ECMLIntroFlightStep::Settle;

    /** Half extents in metres, for the clearance the lesson has to earn. */
    static constexpr float ThreatRockHalfExtentMetres = 9.0f;
    static constexpr float HullHalfExtentMetres = 7.5f;

    void DriveThreatRock(float DeltaSeconds);

    /** The pivot the cockpit camera hangs from, carrying only the shake. */
    TWeakObjectPtr<AActor> ShakePivot;
    FVector ChaseCameraBaseLocation = FVector::ZeroVector;
    FRotator ChaseCameraBaseRotation = FRotator::ZeroRotator;
    void ApplyShake(float Amount, float UnscaledTime);

    void ApplyGrade(const struct FCMLIntroGradeState& Grade);

    /**
     * Unity kept 2400 streaks alive at once, each living 2.2s at 30 m/s, so its
     * field was that many particles inside roughly 6600 units of travel. The
     * field here is static and long enough to cover the whole space section, so
     * the count is scaled to hold that same density over the greater depth.
     */
    /**
     * Close to the 6600 units a Unity streak covers in its 2.2s life, rather
     * than the 48000 an unwrapped field wanted. Depth was traded for never
     * having to recycle, and it cost the shot that matters most: from the
     * cockpit nearly every streak sat far enough away to read as a dot, which
     * is why those frames looked like a static starfield while the external
     * shot, seeing the same field side on, looked right.
     */
    static constexpr float StreakFieldDepth = 14000.0f;
    /**
     * Matching Unity's linear density over this deeper field gave 12000 and was
     * far too bright: the streaks blend additively, so what accumulates is the
     * number crossed along the line of sight, and a field seven times deeper
     * adds seven times as much. The deep space material also draws its own warp
     * smear, so the mesh streaks only have to carry part of the effect.
     */
    static constexpr int32 StarStreakCount = 3000;

    UPROPERTY() TObjectPtr<class UInstancedStaticMeshComponent> StreakInstances;

    /** Distance the streak field has slid, integrated so speed changes do not jump it. */
    float StreakTravel = 0.0f;
};

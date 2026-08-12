#pragma once

#include "CoreMinimal.h"

#include "CMLIntroSequence.generated.h"

/**
 * The shots of the opening, in order, ported from
 * CML.Unity.Presentation.Intro.IntroCinematicController.
 *
 * `Flight` sits third rather than last on purpose: the player is handed the
 * controls *before* anything goes wrong, so the alarm interrupts something they
 * were already doing. Teaching after the crash would make the lesson feel like a
 * menu.
 */
UENUM(BlueprintType)
enum class ECMLIntroShot : uint8
{
    Hyperspace = 0,
    Cockpit = 1,
    Flight = 2,
    Alarm = 3,
    RiftOpen = 4,
    RiftEntry = 5,
    Fall = 6,
    Crash = 7,
    Blackout = 8,
    Wake = 9,
    Complete = 10
};

/**
 * The steps of the flight lesson.
 *
 * Each side is taught the same way — approach, teach, recover — so the second
 * turn confirms the first rather than introducing anything new.
 */
UENUM(BlueprintType)
enum class ECMLIntroFlightStep : uint8
{
    Settle = 0,
    ApproachRight = 1,
    TeachRight = 2,
    RecoverRight = 3,
    ApproachLeft = 4,
    TeachLeft = 5,
    RecoverLeft = 6,
    Handover = 7
};

/** The authored durations, as the scene sets them. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroTimings
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float HyperspaceSeconds = 4.5f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float CockpitSeconds = 2.6f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float AlarmSeconds = 4.5f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float RiftOpenSeconds = 5.5f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float RiftEntrySeconds = 2.0f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float FallSeconds = 7.0f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float CrashSeconds = 5.0f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float BlackoutSeconds = 2.8f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float WakeSeconds = 4.2f;

    /** The flight lesson: how long to settle, approach, and recover. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float FlightSettleSeconds = 3.4f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float FlightApproachSeconds = 600.0f / 190.0f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float FlightRecoverSeconds = 4.4f;

    /** How far the player must turn, and how long they must hold it there. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float TutorialTurnDegrees = 26.0f;
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category="CML|Intro") float TutorialHoldSeconds = 0.32f;

    float DurationOf(ECMLIntroShot Shot) const;
};

/**
 * Where the opening has got to.
 *
 * The flight lesson is the one shot that does not run on a clock: it waits for
 * the player to turn, and waits again for them to turn back. That is the whole
 * point of putting it here — an opening that flew itself would teach nothing.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroState
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Intro")
    ECMLIntroShot Shot = ECMLIntroShot::Hyperspace;

    UPROPERTY(BlueprintReadOnly, Category="CML|Intro")
    ECMLIntroFlightStep FlightStep = ECMLIntroFlightStep::Settle;

    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ElapsedInShot = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ElapsedInStep = 0.0f;

    /** How long the player has held the turn far enough, in the current step. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float HeldSeconds = 0.0f;

    bool IsComplete() const { return Shot == ECMLIntroShot::Complete; }
};

/** What the player is doing with the stick this frame. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroInput
{
    GENERATED_BODY()

    /** Yaw away from centre in degrees; positive is right. */
    UPROPERTY(BlueprintReadWrite, Category="CML|Intro") float YawDegrees = 0.0f;

    UPROPERTY(BlueprintReadWrite, Category="CML|Intro") bool bSkipRequested = false;
};

/**
 * Runs the opening, ported from `IntroCinematicController`.
 *
 * Only the sequencing lives here: what plays, for how long, and what the player
 * has to do to move it on. Cameras, sound and the airship itself belong to the
 * director actor that drives this.
 */
class CMLCORE_API FCMLIntroSequence
{
public:
    /** Advances one frame. Returns true when the opening has just finished. */
    static bool Advance(
        FCMLIntroState& State,
        const FCMLIntroTimings& Timings,
        const FCMLIntroInput& Input,
        float DeltaSeconds,
        bool bAllowSkip);

    /** Which flight step comes next, and whether the lesson has been passed. */
    static bool AdvanceFlight(
        FCMLIntroState& State,
        const FCMLIntroTimings& Timings,
        const FCMLIntroInput& Input,
        float DeltaSeconds);

    /** True while the teaching card should be on screen. */
    static bool ShouldShowTutorialCard(const FCMLIntroState& State);

    /** Which way the card's arrow points: +1 right, -1 left, 0 not shown. */
    static float TutorialDirection(const FCMLIntroState& State);
};

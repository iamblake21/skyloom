#pragma once

#include "CoreMinimal.h"
#include "Presentation/CMLIntroSequence.h"

#include "CMLIntroDressing.generated.h"

/**
 * Everything a shot asks the scene to look like at one instant.
 *
 * The director reads this and pushes it at cameras, lights and materials. It is
 * a value rather than a set of calls so the whole look of the opening can be
 * evaluated — and tested — without a world.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroDressing
{
    GENERATED_BODY()

    /** Chase rig: angle around the ship, how far back, how high, and its lens. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ChaseOrbitDegrees = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ChaseDistance = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ChaseHeight = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ChaseFovOffset = 0.0f;

    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float CockpitFovOffset = 0.0f;

    /** The warp tunnel and the streaks flying past it. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float WarpBlend = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float WarpIntensity = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float WarpSpeed = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float StreakSpeed = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float StreakRate = 0.0f;

    /** The tear: how far open, how far away, and what it throws off. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float RiftOpenness = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float RiftDistance = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float RiftLightIntensity = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro")
    FLinearColor RiftLightColour = FLinearColor::Black;

    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float AlertIntensity = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float CockpitFillIntensity = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float KeyLightIntensity = 0.0f;

    /** How hard the camera is shaking, and how the hull is riding. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float ShakeAmount = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro")
    FRotator AirshipAttitude = FRotator::ZeroRotator;

    /** A white flash over the frame, and the black of a shut eye. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float FlashAlpha = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float FadeAlpha = 0.0f;
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Eyelid = 0.0f;
};

/**
 * What each shot of the opening looks like, ported from the `Tick*` half of
 * `IntroCinematicController`.
 *
 * Separate from `FCMLIntroSequence` on purpose: that decides *when* a shot runs,
 * this decides what it looks like while it does. Keeping them apart is what lets
 * the look be evaluated at any instant without running the opening.
 *
 * `UnscaledTime` drives the wobbles and the klaxon. It is passed in rather than
 * read from a clock so the same instant always produces the same frame.
 */
class CMLCORE_API FCMLIntroDressing_Evaluator
{
public:
    /** How far out the tear sits when it first appears. */
    /** Unity opens the tear 220 m out, not 320. */
    static constexpr float RiftStartDistance = 220.0f;

    static FCMLIntroDressing Evaluate(
        ECMLIntroShot Shot,
        float ElapsedInShot,
        const FCMLIntroTimings& Timings,
        float UnscaledTime);
};

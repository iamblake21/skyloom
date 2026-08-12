#pragma once

#include "CoreMinimal.h"

#include "CMLIntroCrash.generated.h"

/** The wreck sliding to a stop. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroSkidState
{
    GENERATED_BODY()

    /** How fast it is still travelling along the ground. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Speed = 0.0f;

    /** How far it has travelled since touchdown. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Travelled = 0.0f;

    /** How high the hull is riding above the ground it is ploughing. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float HullClearance = 0.0f;

    bool HasStopped() const { return Speed <= 0.0f; }
};

/**
 * Touchdown and the slide after it, ported from the crash half of
 * `IntroCinematicController`.
 *
 * The ship does not simply stop where it lands. It arrives with real speed and
 * friction takes it away over a distance, which is what makes the wreck end up
 * somewhere it was carried to rather than somewhere it was placed.
 */
class CMLCORE_API FCMLIntroCrash
{
public:
    static constexpr float TouchdownSpeed = 88.0f;
    static constexpr float Friction = 34.0f;
    /** How far the hull rides above the ground while it is still moving. */
    static constexpr float HullClearance = 2.2f;

    static FCMLIntroSkidState Touchdown();

    /** One frame of the slide. Returns true on the frame it comes to rest. */
    static bool Advance(FCMLIntroSkidState& State, float DeltaSeconds);

    /** How far it will travel in total, from the speed and the friction. */
    static float PredictedSkidDistance();
};

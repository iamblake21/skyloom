#pragma once

#include "CoreMinimal.h"

#include "CMLIntroThreat.generated.h"

/** Where the rock is, relative to the ship, this frame. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIntroThreatState
{
    GENERATED_BODY()

    /** Along the flight axis; falls towards zero and past it. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Distance = 0.0f;

    /** Across it. Positive is to the ship's right. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Lateral = 0.0f;

    /** Which way the player is being asked to turn: +1 right, -1 left. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Direction = 1.0f;

    /** How much of the needed clearance the turn has earned, 0..1. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Cleared = 0.0f;

    /** How far to the side the rock must end up for the turn to really miss. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") float Clearance = 0.0f;

    /** False once it is far enough behind to stop drawing. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Intro") bool bActive = false;
};

/**
 * The near miss the flight lesson is really about, ported from the threat half
 * of `IntroCinematicController`.
 *
 * The rock flies its own straight line, so turning the ship already slides it
 * out of the windscreen. What the turn *earns* on top of that is the lateral
 * clearance that turns a scare into a genuine miss — which is why the clearance
 * is measured from the real sizes of the rock and the hull rather than picked.
 * A rock wider than its own miss distance would pass through the hull however
 * well the lesson was flown.
 */
class CMLCORE_API FCMLIntroThreat
{
public:
    static constexpr float LaunchDistance = 900.0f;
    static constexpr float ApproachSpeed = 190.0f;
    /** Slower once the lesson is passed, so the pass can be watched. */
    static constexpr float PassSpeed = 112.0f;
    static constexpr float MissMargin = 16.0f;
    /** Behind this, the rock is gone. */
    static constexpr float DespawnDistance = -140.0f;

    /**
     * Clearance from the two real half-extents plus the margin, rather than a
     * tuned constant.
     */
    static float MeasureClearance(float RockHalfExtent, float HullHalfExtent);

    static FCMLIntroThreatState Launch(float Direction, float Clearance);

    /**
     * One frame. `LessonYawDegrees` is how far the player has turned and
     * `RequiredYawDegrees` how far they were asked to; together they say how
     * much of the clearance the turn has earned.
     */
    static void Advance(
        FCMLIntroThreatState& State,
        float LessonYawDegrees,
        float RequiredYawDegrees,
        float DeltaSeconds);
};

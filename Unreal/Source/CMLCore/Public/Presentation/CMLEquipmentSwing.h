#pragma once

#include "CoreMinimal.h"

#include "CMLEquipmentSwing.generated.h"

/** Where the viewmodel sits at one instant of a swing. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLViewmodelOffset
{
    GENERATED_BODY()

    /** Offset from the calibrated rest pose, in Unreal units. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Equipment")
    FVector Position = FVector::ZeroVector;

    /** Offset from the calibrated rest rotation. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Equipment")
    FRotator Rotation = FRotator::ZeroRotator;
};

/**
 * The first-person pickaxe swing, ported from
 * CML.Unity.Presentation.Equipment.FirstPersonEquipmentMotion.
 *
 * The calibrated item pose is never touched: this is an *offset* composed on a
 * dedicated pivot above it, so recalibrating where the pickaxe sits in frame
 * does not require re-authoring the swing.
 *
 * A swing has five phases — wind up, descend, hold, rebound, settle — and the
 * phase boundaries differ depending on whether the swing found something to
 * hit. A connecting swing is quicker and stops dead at contact; a miss is slower
 * and follows through much further, which is what makes a whiff read as a whiff
 * rather than as a shorter hit.
 */
class CMLCORE_API FCMLEquipmentSwing
{
public:
    static constexpr float SwingDurationSeconds = 0.62f;
    static constexpr float MissSwingDurationSeconds = 0.95f;

    /** How far a swing can reach, and the closest a contact pose is authored for. */
    static constexpr float MaximumStrikeDistanceUnrealUnits = 450.0f;
    static constexpr float MinimumContactDistanceUnrealUnits = 55.0f;

    /**
     * When along a connecting swing the blow lands. The pose holds at contact
     * from here, so the impact and the picture agree.
     */
    static constexpr float ImpactProgress = 0.48f;

    static float DurationFor(bool bHasTarget)
    {
        return bHasTarget ? SwingDurationSeconds : MissSwingDurationSeconds;
    }

    /**
     * Whether the blow lands between two frames.
     *
     * Takes both the previous and the current progress so a frame long enough
     * to step over the impact instant still fires it exactly once. Testing the
     * current progress alone would drop the impact on a slow frame and repeat it
     * on a fast one.
     */
    static bool CrossedImpact(float PreviousProgress, float Progress, bool bHasTarget);

    /**
     * The pose at a given point of the swing.
     *
     * `TargetDistance` chooses between the near and far contact poses: a blow
     * struck at arm's length ends in a different place from one struck at the
     * limit of reach, and interpolating between the two is what stops the
     * pickaxe hanging in the air short of what it hit.
     */
    static FCMLViewmodelOffset EvaluatePose(
        float Progress, bool bHasTarget, float TargetDistance);
};

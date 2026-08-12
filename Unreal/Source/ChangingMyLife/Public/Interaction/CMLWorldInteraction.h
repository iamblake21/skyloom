#pragma once

#include "CoreMinimal.h"
#include "UObject/Interface.h"

#include "CMLWorldInteraction.generated.h"

UINTERFACE(MinimalAPI, BlueprintType)
class UCMLWorldInteractionTarget : public UInterface
{
    GENERATED_BODY()
};

/**
 * The sole contract for something the player can act on, ported from
 * CML.Unity.Presentation.IWorldInteractionTarget.
 *
 * A target owns its action and its wording and nothing else. Targeting, input
 * and the prompt itself stay in one interactor: a chest, a tuft of fibre and a
 * machine are all opened the same way, by one press, and none of them reads the
 * key for itself.
 */
class CHANGINGMYLIFE_API ICMLWorldInteractionTarget
{
    GENERATED_BODY()

public:
    /** False while the target exists but cannot be acted on right now. */
    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category="CML|Interaction")
    bool IsInteractionAvailable() const;

    /** What the prompt should say. The interactor upper-cases and centres it. */
    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category="CML|Interaction")
    FText GetInteractionPrompt() const;

    /** Does the thing. Returns false if it could not, leaving the world unchanged. */
    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category="CML|Interaction")
    bool TryInteract();
};

/**
 * Where the prompt for a target should float.
 *
 * Not the target's pivot and not the centre of its bounds: the point on its
 * surface nearest the camera, pushed a little towards the viewer and lifted.
 * A prompt at the centre of a large object sits inside it and disappears.
 */
class CHANGINGMYLIFE_API FCMLWorldPromptPlacement
{
public:
    /** How far off the surface, and how far up, the prompt floats. */
    static constexpr double SurfaceOffsetUnrealUnits = 4.5;
    static constexpr double VerticalOffsetUnrealUnits = 12.0;

    /**
     * The world position for a prompt.
     *
     * Falls back through the bounds centre and then the camera's own forward
     * when the camera is inside the target, where "nearest surface point" is
     * degenerate and would otherwise leave the prompt at the viewer's eye.
     */
    static FVector Resolve(
        const FBox& TargetBounds,
        const FVector& CameraLocation,
        const FVector& CameraForward);
};

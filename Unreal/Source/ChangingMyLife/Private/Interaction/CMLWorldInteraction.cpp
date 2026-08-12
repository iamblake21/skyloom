#include "Interaction/CMLWorldInteraction.h"

FVector FCMLWorldPromptPlacement::Resolve(
    const FBox& TargetBounds,
    const FVector& /*CameraLocation*/,
    const FVector& /*CameraForward*/)
{
    // Position is deliberately independent from the camera. The component is
    // attached to the interaction primitive after this calculation, so it keeps
    // following the object instead of sliding over its camera-facing surface.
    const FVector Centre = TargetBounds.GetCenter();
    return FVector(Centre.X, Centre.Y,
        TargetBounds.Max.Z + VerticalOffsetUnrealUnits);
}

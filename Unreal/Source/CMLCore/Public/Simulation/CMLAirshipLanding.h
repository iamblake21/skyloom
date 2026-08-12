#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLAirshipState.h"

/**
 * Landing reachability, ported from
 * CML.Simulation.Airship.AirshipLandingValidator.
 *
 * A landing is not a point test. The rule asks whether a *continuous pad* — a
 * corridor as wide and as deep as the airship needs — exists in front of the
 * ramp, and it samples that pad on a fixed grid. Sampling only the centre would
 * accept a surface with a hole exactly where a player would step off.
 *
 * Every distance is in millimetres and every rotation goes through the fixed
 * turn trigonometry, so two machines agree on whether a landing is legal.
 */
class CMLCORE_API FCMLAirshipLanding
{
public:
    static constexpr int32 SpeedThresholdMillimetresPerSecond = 4000;
    static constexpr int32 MinimumReachMillimetres = 400;
    static constexpr int32 MaximumReachMillimetres = 2500;
    static constexpr int32 MaximumHeightDeltaMillimetres = 350;
    static constexpr int32 RequiredCorridorWidthMillimetres = 800;
    static constexpr int32 RequiredPadDepthMillimetres = 900;
    static constexpr int32 SampleStepMillimetres = 50;

    /** The ramp tip in the airship's own frame. */
    static constexpr int32 RampTipLocalXMillimetres = 2030;
    static constexpr int32 RampTipLocalYMillimetres = 300;
    static constexpr int32 RampTipLocalZMillimetres = -160;

    /** World position of the ramp tip for a given airship pose. */
    static FCMLAirshipVector GetRampWorld(const FCMLAirshipPose& Pose);

    /** Whether a world point lies on the surface's rectangle. */
    static bool SurfaceContainsPoint(
        const FCMLAirshipLandingSurface& Surface,
        const FCMLAirshipVector& WorldPoint);

    /**
     * Searches outwards from the ramp for the nearest reach at which a
     * continuous pad exists. Returns false when no reach works.
     */
    static bool TryFindReach(
        const FCMLAirshipSimulationState& State,
        const FCMLAirshipPose& Pose,
        const FCMLStableId& SurfaceId,
        int32& OutAcceptedReach);

    static bool IsReachable(
        const FCMLAirshipSimulationState& State,
        const FCMLAirshipPose& Pose,
        const FCMLStableId& SurfaceId);

    /** Where a player would step off, once a reach has been accepted. */
    static bool TryGetDisembarkPoint(
        const FCMLAirshipSimulationState& State,
        const FCMLAirshipPose& Pose,
        const FCMLStableId& SurfaceId,
        FCMLAirshipVector& OutWorldPoint);
};

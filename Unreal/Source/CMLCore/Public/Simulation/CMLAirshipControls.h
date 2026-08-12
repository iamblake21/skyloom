#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLAirshipState.h"

/**
 * Flight control response, ported from
 * CML.Simulation.Airship.AirshipReducer.UpdateFlightControls.
 *
 * Nothing here snaps. Every axis ramps toward its target at a rate derived from
 * the same acceleration budget, so an airship built from these constants takes
 * a known number of ticks to reach full speed rather than an amount that
 * depends on frame timing.
 */
class CMLCORE_API FCMLAirshipControls
{
public:
    static constexpr int32 MaximumForwardSpeedMillimetresPerSecond = 20000;
    static constexpr int32 MaximumReverseSpeedMillimetresPerSecond = 6000;
    static constexpr int32 MaximumVerticalSpeedMillimetresPerSecond = 6000;
    static constexpr int32 MaximumYawRateTurnUnitsPerSecond = 8192;
    static constexpr int32 AccelerationTicks = 80;
    static constexpr int32 FullYawAuthoritySpeedMillimetresPerSecond = 4000;
    static constexpr int32 MaximumPitchTurnUnits = 2731;
    static constexpr int32 PitchChangeTurnUnitsPerTick = 192;

    /** Steps `Current` toward `Target` by at most `MaximumDelta`, never past it. */
    static int64 MoveTowards(int64 Current, int64 Target, int64 MaximumDelta);

    /**
     * Applies the held pilot input for one tick: throttle, lift, yaw and pitch.
     *
     * Yaw authority scales with forward speed and reaches nothing at a
     * standstill — an airship with no way on cannot pivot on the spot.
     */
    static void UpdateFlightControls(FCMLAirshipEntityState& Airship);
};

#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLAirshipState.h"

/**
 * Exact per-tick flight integration, ported from
 * CML.Simulation.Airship.AirshipReducer and AirshipIntegerMath.
 *
 * Speeds are authored per second but applied per tick. Dividing and discarding
 * the fraction would lose a little travel every tick, and an airship would fall
 * short of where the same speed should carry it. The leftover is therefore
 * carried in a **Euclidean** remainder — always non-negative, so climbing and
 * descending accumulate at the same rate rather than one drifting against the
 * other.
 */
class CMLCORE_API FCMLAirshipIntegration
{
public:
    static constexpr int32 TicksPerSecond = 20;

    /**
     * Advances one axis by one tick, carrying the leftover.
     * `InOutRemainder` stays in [0, TicksPerSecond) whatever the sign of the speed.
     */
    static int64 IntegratePerSecond(int64 ValuePerSecond, int64& InOutRemainder);

    /** Yaw wraps through turn16 rather than clamping; a full turn is a no-op. */
    static uint16 AddTurn(uint16 Yaw, int64 SignedDelta);

    /**
     * One tick of flight for one airship: pitch splits forward speed into
     * horizontal travel and climb, the three axes integrate with their
     * remainders, and the result is rotated into world space by the *new* yaw.
     */
    static void IntegrateFlight(FCMLAirshipEntityState& Airship);
};

#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLAirshipState.h"

/**
 * Integer-only trigonometry for canonical turn16 yaw, ported from
 * CML.Simulation.Airship.FixedTurnTrig.
 *
 * This is why two machines can fly the same airship and land it on the same
 * tick. A float sine would give each platform its own rounding; a CORDIC
 * rotation over integers gives every platform the same bits. The four cardinal
 * turns are returned exactly rather than approximated, so a quarter turn is a
 * quarter turn everywhere.
 *
 * Constants are Q32 turns, results Q30.
 */
class CMLCORE_API FCMLFixedTurnTrig
{
public:
    static constexpr int32 FractionBits = 30;
    static constexpr int32 One = 1 << FractionBits;

    /** Sine and cosine of a turn16 yaw, both in Q30. */
    static void SinCos(uint16 YawTurn, int32& OutSineQ30, int32& OutCosineQ30);

    static FCMLAirshipVector RotateLocalToWorld(const FCMLAirshipVector& Local, uint16 YawTurn);
    static FCMLAirshipVector RotateWorldToLocal(const FCMLAirshipVector& World, uint16 YawTurn);

    /**
     * Division that rounds halves away from zero, in both directions.
     *
     * Rounding towards zero would make an airship drift asymmetrically: the
     * same speed would travel further one way than the other, and the drift
     * would accumulate across a flight.
     */
    static int64 RoundDivideAwayFromZero(int64 Numerator, int64 PositiveDenominator);
};

#include "Simulation/CMLAirshipControls.h"

#include "Simulation/CMLFixedTurnTrig.h"

namespace
{
    int64 Absolute(const int64 Value)
    {
        return Value < 0 ? -Value : Value;
    }

    int32 SignOf(const int64 Value)
    {
        return Value < 0 ? -1 : (Value > 0 ? 1 : 0);
    }

    int64 ScaleInput(const int64 Maximum, const int64 InputPermille)
    {
        return (Maximum * InputPermille) / 1000;
    }

    /** Ceiling division, so a budget never rounds down to a slower ramp. */
    int64 CeilingDivide(const int64 Numerator, const int64 PositiveDenominator)
    {
        return (Numerator + PositiveDenominator - 1) / PositiveDenominator;
    }

    /**
     * Ramps one axis toward its target.
     *
     * Asking for the opposite direction does not fling the axis across zero: the
     * target becomes zero first, so the airship decelerates through a stop
     * before it can build speed the other way.
     */
    int64 AdvanceAxis(
        const int64 Current,
        int64 Target,
        const int64 PositiveMaximum,
        const int64 NegativeMaximum)
    {
        if (Current != 0 && Target != 0 && SignOf(Current) != SignOf(Target))
        {
            Target = 0;
        }
        const int64 RelevantMaximum = (Current < 0 || (Current == 0 && Target < 0))
            ? NegativeMaximum
            : PositiveMaximum;
        const int64 MaximumDelta =
            CeilingDivide(RelevantMaximum, FCMLAirshipControls::AccelerationTicks);
        return FCMLAirshipControls::MoveTowards(Current, Target, MaximumDelta);
    }
}

int64 FCMLAirshipControls::MoveTowards(
    const int64 Current,
    const int64 Target,
    const int64 MaximumDelta)
{
    if (MaximumDelta < 0)
    {
        return Current;
    }
    if (Current < Target)
    {
        return FMath::Min(Current + MaximumDelta, Target);
    }
    if (Current > Target)
    {
        return FMath::Max(Current - MaximumDelta, Target);
    }
    return Current;
}

void FCMLAirshipControls::UpdateFlightControls(FCMLAirshipEntityState& Airship)
{
    const FCMLAirshipPilotInput& Input = Airship.HeldInput;

    // Throttle is a *change* per tick, not a target: the stick trims the speed
    // rather than commanding it, which is what makes the airship feel heavy.
    const int64 ThrottleStep =
        CeilingDivide(MaximumForwardSpeedMillimetresPerSecond, AccelerationTicks);
    const int64 ThrottleDelta = (ThrottleStep * Input.ThrottleChangePermille) / 1000;

    Airship.ForwardSpeedMillimetresPerSecond = FMath::Max<int64>(
        -MaximumReverseSpeedMillimetresPerSecond,
        FMath::Min<int64>(
            MaximumForwardSpeedMillimetresPerSecond,
            Airship.ForwardSpeedMillimetresPerSecond + ThrottleDelta));

    // This hull has no lateral thrust.
    Airship.StrafeSpeedMillimetresPerSecond = 0;

    // Lift, by contrast, *is* a target: releasing the stick returns to level.
    const int64 VerticalTarget =
        ScaleInput(MaximumVerticalSpeedMillimetresPerSecond, Input.LiftPermille);
    Airship.VerticalSpeedMillimetresPerSecond = AdvanceAxis(
        Airship.VerticalSpeedMillimetresPerSecond,
        VerticalTarget,
        MaximumVerticalSpeedMillimetresPerSecond,
        MaximumVerticalSpeedMillimetresPerSecond);

    const int64 AbsoluteForwardSpeed = Absolute(Airship.ForwardSpeedMillimetresPerSecond);
    if (AbsoluteForwardSpeed == 0)
    {
        // No way on, no steering. The carried remainder is cleared too, so a
        // stopped airship cannot creep round from a fraction left over.
        Airship.YawRateTurnUnitsPerSecond = 0;
        Airship.YawIntegrationRemainder = 0;
        return;
    }

    // Yaw authority builds with speed and saturates once there is enough of it.
    const int64 YawAuthorityPermille = FMath::Min<int64>(
        1000, (AbsoluteForwardSpeed * 1000) / FullYawAuthoritySpeedMillimetresPerSecond);
    Airship.YawRateTurnUnitsPerSecond = FCMLFixedTurnTrig::RoundDivideAwayFromZero(
        static_cast<int64>(MaximumYawRateTurnUnitsPerSecond)
            * Input.YawDeltaPermille * YawAuthorityPermille,
        1000000);

    const int64 PitchDelta = FCMLFixedTurnTrig::RoundDivideAwayFromZero(
        static_cast<int64>(PitchChangeTurnUnitsPerTick) * Input.PitchDeltaPermille, 1000);
    Airship.PitchTurnUnits = FMath::Max<int64>(
        -MaximumPitchTurnUnits,
        FMath::Min<int64>(MaximumPitchTurnUnits, Airship.PitchTurnUnits + PitchDelta));
}

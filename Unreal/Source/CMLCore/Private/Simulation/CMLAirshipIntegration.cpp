#include "Simulation/CMLAirshipIntegration.h"

#include "Simulation/CMLFixedTurnTrig.h"

int64 FCMLAirshipIntegration::IntegratePerSecond(const int64 ValuePerSecond, int64& InOutRemainder)
{
    const int64 Numerator = ValuePerSecond + InOutRemainder;
    int64 Quotient = Numerator / TicksPerSecond;
    int64 Remainder = Numerator % TicksPerSecond;

    // C++ truncates towards zero, so a negative numerator would leave a
    // negative remainder. Folding it back keeps the carry Euclidean, which is
    // what makes climbing and descending accumulate at the same rate.
    if (Remainder < 0)
    {
        --Quotient;
        Remainder += TicksPerSecond;
    }

    InOutRemainder = Remainder;
    return Quotient;
}

uint16 FCMLAirshipIntegration::AddTurn(const uint16 Yaw, const int64 SignedDelta)
{
    // Deliberate wraparound: yaw is cyclic, so a full turn must land back where
    // it started rather than saturating at the end of the range.
    return static_cast<uint16>(static_cast<int64>(Yaw) + SignedDelta);
}

void FCMLAirshipIntegration::IntegrateFlight(FCMLAirshipEntityState& Airship)
{
    int64 VerticalRemainder = Airship.VerticalIntegrationRemainder;
    int64 ForwardRemainder = Airship.ForwardIntegrationRemainder;
    int64 YawRemainder = Airship.YawIntegrationRemainder;

    int32 PitchSine = 0;
    int32 PitchCosine = 0;
    FCMLFixedTurnTrig::SinCos(
        static_cast<uint16>(Airship.PitchTurnUnits), PitchSine, PitchCosine);

    // Pitch splits the forward speed: the cosine drives travel over the ground,
    // the sine drives climb. The absolute forward speed feeds the climb so that
    // reversing does not invert which way a nose-up attitude points.
    const int64 HorizontalForwardSpeed = FCMLFixedTurnTrig::RoundDivideAwayFromZero(
        Airship.ForwardSpeedMillimetresPerSecond * PitchCosine, FCMLFixedTurnTrig::One);
    const int64 ForwardMagnitude = Airship.ForwardSpeedMillimetresPerSecond < 0
        ? -Airship.ForwardSpeedMillimetresPerSecond
        : Airship.ForwardSpeedMillimetresPerSecond;
    const int64 PitchVerticalSpeed = FCMLFixedTurnTrig::RoundDivideAwayFromZero(
        -ForwardMagnitude * PitchSine, FCMLFixedTurnTrig::One);

    const int64 LocalY = IntegratePerSecond(
        Airship.VerticalSpeedMillimetresPerSecond + PitchVerticalSpeed, VerticalRemainder);
    const int64 LocalZ = IntegratePerSecond(HorizontalForwardSpeed, ForwardRemainder);
    const int64 YawDelta = IntegratePerSecond(Airship.YawRateTurnUnitsPerSecond, YawRemainder);

    // Strafe is not integrated: this hull has no lateral thrust, and Unity
    // clears the carry rather than letting a stale one leak into a later tick.
    Airship.StrafeIntegrationRemainder = 0;
    Airship.VerticalIntegrationRemainder = VerticalRemainder;
    Airship.ForwardIntegrationRemainder = ForwardRemainder;
    Airship.YawIntegrationRemainder = YawRemainder;

    // The yaw applied to this tick's travel is the *new* one, so a turn takes
    // effect on the same tick it is commanded.
    const uint16 CandidateYaw = AddTurn(static_cast<uint16>(Airship.Pose.YawTurn), YawDelta);
    FCMLAirshipVector LocalDelta;
    LocalDelta.X = 0;
    LocalDelta.Y = LocalY;
    LocalDelta.Z = LocalZ;
    const FCMLAirshipVector WorldDelta =
        FCMLFixedTurnTrig::RotateLocalToWorld(LocalDelta, CandidateYaw);

    Airship.Pose.Position.X += WorldDelta.X;
    Airship.Pose.Position.Y += WorldDelta.Y;
    Airship.Pose.Position.Z += WorldDelta.Z;
    Airship.Pose.YawTurn = static_cast<int32>(CandidateYaw);
}

#include "Simulation/CMLAirshipControls.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLAirshipControlsTest,
    "CML.Core.Simulation.AirshipControls",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLAirshipControlsTest::RunTest(const FString& Parameters)
{
    // MoveTowards never overshoots, in either direction.
    {
        TestEqual(TEXT("Stepping up stops at the target"),
            FCMLAirshipControls::MoveTowards(0, 10, 100), static_cast<int64>(10));
        TestEqual(TEXT("Stepping down stops at the target"),
            FCMLAirshipControls::MoveTowards(0, -10, 100), static_cast<int64>(-10));
        TestEqual(TEXT("A partial step advances"),
            FCMLAirshipControls::MoveTowards(0, 100, 10), static_cast<int64>(10));
        TestEqual(TEXT("Already there is a no-op"),
            FCMLAirshipControls::MoveTowards(7, 7, 10), static_cast<int64>(7));
    }

    // Nothing snaps: full throttle takes the whole acceleration budget to reach
    // top speed, and not one tick less.
    {
        FCMLAirshipEntityState Airship;
        Airship.HeldInput.ThrottleChangePermille = 1000;
        for (int32 Tick = 0; Tick < FCMLAirshipControls::AccelerationTicks; ++Tick)
        {
            FCMLAirshipControls::UpdateFlightControls(Airship);
        }
        TestEqual(TEXT("Full throttle reaches top speed in the budget"),
            Airship.ForwardSpeedMillimetresPerSecond,
            static_cast<int64>(FCMLAirshipControls::MaximumForwardSpeedMillimetresPerSecond));

        // And it clamps there rather than climbing further.
        FCMLAirshipControls::UpdateFlightControls(Airship);
        TestEqual(TEXT("Top speed is a ceiling"),
            Airship.ForwardSpeedMillimetresPerSecond,
            static_cast<int64>(FCMLAirshipControls::MaximumForwardSpeedMillimetresPerSecond));
    }

    // Reverse is deliberately slower than forward.
    {
        FCMLAirshipEntityState Airship;
        Airship.HeldInput.ThrottleChangePermille = -1000;
        for (int32 Tick = 0; Tick < FCMLAirshipControls::AccelerationTicks * 2; ++Tick)
        {
            FCMLAirshipControls::UpdateFlightControls(Airship);
        }
        TestEqual(TEXT("Reverse clamps at its own lower limit"),
            Airship.ForwardSpeedMillimetresPerSecond,
            static_cast<int64>(-FCMLAirshipControls::MaximumReverseSpeedMillimetresPerSecond));
    }

    // Throttle trims the speed; releasing the stick holds it rather than
    // returning to zero. Lift is the opposite: it is a target.
    {
        FCMLAirshipEntityState Airship;
        Airship.ForwardSpeedMillimetresPerSecond = 5000;
        Airship.VerticalSpeedMillimetresPerSecond = 3000;
        FCMLAirshipControls::UpdateFlightControls(Airship);
        TestEqual(TEXT("Released throttle holds the speed"),
            Airship.ForwardSpeedMillimetresPerSecond, static_cast<int64>(5000));
        TestTrue(TEXT("Released lift decays toward level"),
            Airship.VerticalSpeedMillimetresPerSecond < 3000);
    }

    // Asking for the opposite direction decelerates through a stop rather than
    // flinging the axis across zero.
    {
        FCMLAirshipEntityState Airship;
        Airship.VerticalSpeedMillimetresPerSecond = 3000;
        Airship.HeldInput.LiftPermille = -1000;
        const int64 Step = FCMLAirshipControls::MaximumVerticalSpeedMillimetresPerSecond
            / FCMLAirshipControls::AccelerationTicks;
        FCMLAirshipControls::UpdateFlightControls(Airship);
        TestTrue(TEXT("A reversal first slows down"),
            Airship.VerticalSpeedMillimetresPerSecond < 3000
                && Airship.VerticalSpeedMillimetresPerSecond >= 3000 - Step - 1);
        TestTrue(TEXT("It does not jump past zero"),
            Airship.VerticalSpeedMillimetresPerSecond > 0);
    }

    // An airship with no way on cannot pivot on the spot.
    {
        FCMLAirshipEntityState Airship;
        Airship.YawIntegrationRemainder = 7;
        Airship.HeldInput.YawDeltaPermille = 1000;
        FCMLAirshipControls::UpdateFlightControls(Airship);
        TestEqual(TEXT("A standstill has no yaw rate"),
            Airship.YawRateTurnUnitsPerSecond, static_cast<int64>(0));
        TestEqual(TEXT("The carried remainder is cleared too"),
            Airship.YawIntegrationRemainder, static_cast<int64>(0));
    }

    // Yaw authority builds with speed and saturates once there is enough of it.
    {
        FCMLAirshipEntityState Slow;
        Slow.ForwardSpeedMillimetresPerSecond =
            FCMLAirshipControls::FullYawAuthoritySpeedMillimetresPerSecond / 2;
        Slow.HeldInput.YawDeltaPermille = 1000;
        FCMLAirshipControls::UpdateFlightControls(Slow);

        FCMLAirshipEntityState Fast;
        Fast.ForwardSpeedMillimetresPerSecond =
            FCMLAirshipControls::FullYawAuthoritySpeedMillimetresPerSecond;
        Fast.HeldInput.YawDeltaPermille = 1000;
        FCMLAirshipControls::UpdateFlightControls(Fast);

        FCMLAirshipEntityState Faster;
        Faster.ForwardSpeedMillimetresPerSecond =
            FCMLAirshipControls::FullYawAuthoritySpeedMillimetresPerSecond * 3;
        Faster.HeldInput.YawDeltaPermille = 1000;
        FCMLAirshipControls::UpdateFlightControls(Faster);

        TestTrue(TEXT("Half speed gives partial authority"),
            Slow.YawRateTurnUnitsPerSecond > 0
                && Slow.YawRateTurnUnitsPerSecond < Fast.YawRateTurnUnitsPerSecond);
        TestEqual(TEXT("Full authority is the maximum yaw rate"),
            Fast.YawRateTurnUnitsPerSecond,
            static_cast<int64>(FCMLAirshipControls::MaximumYawRateTurnUnitsPerSecond));
        TestEqual(TEXT("More speed does not buy more authority"),
            Faster.YawRateTurnUnitsPerSecond, Fast.YawRateTurnUnitsPerSecond);
    }

    // Pitch shares the standstill gate with yaw: the reducer returns before
    // either is applied when there is no way on. A parked airship holds its
    // attitude however hard the stick is pushed.
    {
        FCMLAirshipEntityState Parked;
        Parked.HeldInput.PitchDeltaPermille = 1000;
        for (int32 Tick = 0; Tick < 10; ++Tick)
        {
            FCMLAirshipControls::UpdateFlightControls(Parked);
        }
        TestEqual(TEXT("A parked airship does not pitch"),
            Parked.PitchTurnUnits, static_cast<int64>(0));
    }

    // With way on, pitch responds and clamps, so holding the stick cannot loop
    // the airship.
    {
        FCMLAirshipEntityState Airship;
        Airship.ForwardSpeedMillimetresPerSecond = 5000;
        Airship.HeldInput.PitchDeltaPermille = 1000;
        for (int32 Tick = 0; Tick < 100; ++Tick)
        {
            FCMLAirshipControls::UpdateFlightControls(Airship);
        }
        TestEqual(TEXT("Pitch clamps at its limit"),
            Airship.PitchTurnUnits,
            static_cast<int64>(FCMLAirshipControls::MaximumPitchTurnUnits));

        Airship.HeldInput.PitchDeltaPermille = -1000;
        for (int32 Tick = 0; Tick < 100; ++Tick)
        {
            FCMLAirshipControls::UpdateFlightControls(Airship);
        }
        TestEqual(TEXT("Pitch clamps at the other limit"),
            Airship.PitchTurnUnits,
            static_cast<int64>(-FCMLAirshipControls::MaximumPitchTurnUnits));
    }
    return true;
}
#endif

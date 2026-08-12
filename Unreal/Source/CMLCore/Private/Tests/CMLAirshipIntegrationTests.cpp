#include "Simulation/CMLAirshipIntegration.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLAirshipIntegrationTest,
    "CML.Core.Simulation.AirshipIntegration",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLAirshipIntegrationTest::RunTest(const FString& Parameters)
{
    // The property the whole remainder mechanism exists for: a speed that does
    // not divide evenly into twenty ticks must still cover exactly its distance
    // after one second, not one millimetre less.
    {
        int64 Remainder = 0;
        int64 Travelled = 0;
        for (int32 Tick = 0; Tick < FCMLAirshipIntegration::TicksPerSecond; ++Tick)
        {
            Travelled += FCMLAirshipIntegration::IntegratePerSecond(1001, Remainder);
        }
        TestEqual(TEXT("1001 mm/s covers exactly 1001 mm in one second"),
            Travelled, static_cast<int64>(1001));
        TestEqual(TEXT("The carry closes the second"), Remainder, static_cast<int64>(0));
    }

    // The same must hold descending: a Euclidean remainder is what stops climb
    // and descent from drifting against each other.
    {
        int64 Up = 0;
        int64 Down = 0;
        int64 UpRemainder = 0;
        int64 DownRemainder = 0;
        for (int32 Tick = 0; Tick < FCMLAirshipIntegration::TicksPerSecond; ++Tick)
        {
            Up += FCMLAirshipIntegration::IntegratePerSecond(777, UpRemainder);
            Down += FCMLAirshipIntegration::IntegratePerSecond(-777, DownRemainder);
        }
        TestEqual(TEXT("Climbing covers its distance"), Up, static_cast<int64>(777));
        TestEqual(TEXT("Descending covers the same distance"), Down, static_cast<int64>(-777));
        TestTrue(TEXT("The carry stays non-negative"), DownRemainder >= 0);
        TestTrue(TEXT("The carry stays below a tick"),
            DownRemainder < FCMLAirshipIntegration::TicksPerSecond);
    }

    // Yaw is cyclic: a full turn lands back where it started.
    {
        TestEqual(TEXT("A full turn is a no-op"),
            FCMLAirshipIntegration::AddTurn(1000, 65536), static_cast<uint16>(1000));
        TestEqual(TEXT("Turning past zero wraps"),
            FCMLAirshipIntegration::AddTurn(10, -20), static_cast<uint16>(65526));
    }

    // Level flight travels along -Z at zero yaw and loses no height.
    {
        FCMLAirshipEntityState Airship;
        Airship.ForwardSpeedMillimetresPerSecond = 2000;
        for (int32 Tick = 0; Tick < FCMLAirshipIntegration::TicksPerSecond; ++Tick)
        {
            FCMLAirshipIntegration::IntegrateFlight(Airship);
        }
        TestEqual(TEXT("A second of level flight covers its distance"),
            Airship.Pose.Position.Z, static_cast<int64>(2000));
        TestEqual(TEXT("Level flight holds altitude"),
            Airship.Pose.Position.Y, static_cast<int64>(0));
        TestEqual(TEXT("Level flight holds heading"), Airship.Pose.YawTurn, 0);
    }

    // A quarter turn of yaw redirects the same speed onto the other axis.
    {
        FCMLAirshipEntityState Airship;
        Airship.ForwardSpeedMillimetresPerSecond = 2000;
        Airship.Pose.YawTurn = 16384;
        for (int32 Tick = 0; Tick < FCMLAirshipIntegration::TicksPerSecond; ++Tick)
        {
            FCMLAirshipIntegration::IntegrateFlight(Airship);
        }
        TestEqual(TEXT("A quarter turn sends travel along X"),
            Airship.Pose.Position.X, static_cast<int64>(2000));
        TestEqual(TEXT("Nothing is left on Z"),
            Airship.Pose.Position.Z, static_cast<int64>(0));
    }

    // Pitch trades forward travel for vertical travel. The sign convention is
    // worth stating: the reducer computes -|forward| * sin(pitch), so a
    // *positive* pitch turn dives and a negative one climbs.
    {
        FCMLAirshipEntityState Diving;
        Diving.ForwardSpeedMillimetresPerSecond = 2000;
        Diving.PitchTurnUnits = 8192;  // an eighth turn, nose down
        FCMLAirshipEntityState Climbing = Diving;
        Climbing.PitchTurnUnits = -8192;
        FCMLAirshipEntityState Reversing = Diving;
        Reversing.ForwardSpeedMillimetresPerSecond = -2000;

        for (int32 Tick = 0; Tick < FCMLAirshipIntegration::TicksPerSecond; ++Tick)
        {
            FCMLAirshipIntegration::IntegrateFlight(Diving);
            FCMLAirshipIntegration::IntegrateFlight(Climbing);
            FCMLAirshipIntegration::IntegrateFlight(Reversing);
        }
        TestTrue(TEXT("Positive pitch loses height"), Diving.Pose.Position.Y < 0);
        TestTrue(TEXT("Negative pitch gains height"), Climbing.Pose.Position.Y > 0);
        TestTrue(TEXT("A pitched airship still moves forward"), Diving.Pose.Position.Z > 0);
        TestTrue(TEXT("Pitch costs forward travel"),
            Diving.Pose.Position.Z < static_cast<int64>(2000));

        // The absolute forward speed feeds the vertical term, so reversing does
        // not invert which way the nose points.
        TestTrue(TEXT("Reversing moves backwards"), Reversing.Pose.Position.Z < 0);
        TestEqual(TEXT("Reversing keeps the same vertical sense"),
            Reversing.Pose.Position.Y, Diving.Pose.Position.Y);
    }

    // Two airships given identical state must land on identical positions:
    // the integration carries no hidden per-instance state.
    {
        FCMLAirshipEntityState First;
        First.ForwardSpeedMillimetresPerSecond = 1337;
        First.VerticalSpeedMillimetresPerSecond = -211;
        First.YawRateTurnUnitsPerSecond = 733;
        FCMLAirshipEntityState Second = First;

        for (int32 Tick = 0; Tick < 200; ++Tick)
        {
            FCMLAirshipIntegration::IntegrateFlight(First);
            FCMLAirshipIntegration::IntegrateFlight(Second);
        }
        TestEqual(TEXT("Ten seconds of flight agree on X"),
            First.Pose.Position.X, Second.Pose.Position.X);
        TestEqual(TEXT("Ten seconds of flight agree on Y"),
            First.Pose.Position.Y, Second.Pose.Position.Y);
        TestEqual(TEXT("Ten seconds of flight agree on Z"),
            First.Pose.Position.Z, Second.Pose.Position.Z);
        TestEqual(TEXT("Ten seconds of flight agree on heading"),
            First.Pose.YawTurn, Second.Pose.YawTurn);
    }
    return true;
}
#endif

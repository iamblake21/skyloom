#include "Simulation/CMLFixedTurnTrig.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLFixedTurnTrigTest,
    "CML.Core.Simulation.FixedTurnTrig",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLFixedTurnTrigTest::RunTest(const FString& Parameters)
{
    int32 Sine = 0;
    int32 Cosine = 0;

    // The cardinal turns are exact, not approximated: a quarter turn that is a
    // bit off shows up as an airship that never quite faces along an axis.
    FCMLFixedTurnTrig::SinCos(0, Sine, Cosine);
    TestEqual(TEXT("sin(0) is zero"), Sine, 0);
    TestEqual(TEXT("cos(0) is one"), Cosine, FCMLFixedTurnTrig::One);

    FCMLFixedTurnTrig::SinCos(16384, Sine, Cosine);
    TestEqual(TEXT("sin(quarter) is one"), Sine, FCMLFixedTurnTrig::One);
    TestEqual(TEXT("cos(quarter) is zero"), Cosine, 0);

    FCMLFixedTurnTrig::SinCos(32768, Sine, Cosine);
    TestEqual(TEXT("sin(half) is zero"), Sine, 0);
    TestEqual(TEXT("cos(half) is minus one"), Cosine, -FCMLFixedTurnTrig::One);

    FCMLFixedTurnTrig::SinCos(49152, Sine, Cosine);
    TestEqual(TEXT("sin(three quarters) is minus one"), Sine, -FCMLFixedTurnTrig::One);
    TestEqual(TEXT("cos(three quarters) is zero"), Cosine, 0);

    // Off-axis turns must land on the unit circle. CORDIC is approximate, so
    // the check is that sin^2 + cos^2 stays within a few parts per million of
    // one - drift beyond that would accumulate over a flight.
    for (const uint16 Turn : {uint16(2048), uint16(8192), uint16(20000), uint16(40000), uint16(60000)})
    {
        FCMLFixedTurnTrig::SinCos(Turn, Sine, Cosine);
        const double NormalisedSine = static_cast<double>(Sine) / FCMLFixedTurnTrig::One;
        const double NormalisedCosine = static_cast<double>(Cosine) / FCMLFixedTurnTrig::One;
        const double Magnitude = NormalisedSine * NormalisedSine + NormalisedCosine * NormalisedCosine;
        TestTrue(
            FString::Printf(TEXT("Turn %u lands on the unit circle (%f)"), Turn, Magnitude),
            FMath::Abs(Magnitude - 1.0) < 1e-5);
    }

    // An eighth turn is the classic sanity check: both components equal, and
    // positive, at 45 degrees.
    {
        FCMLFixedTurnTrig::SinCos(8192, Sine, Cosine);
        TestTrue(TEXT("sin(eighth) is positive"), Sine > 0);
        TestTrue(TEXT("cos(eighth) is positive"), Cosine > 0);
        TestTrue(TEXT("sin and cos agree at an eighth turn"),
            FMath::Abs(Sine - Cosine) < FCMLFixedTurnTrig::One / 1000);
    }

    // Rotating out and back must return the original vector, which is what
    // keeps a local-to-world round trip from drifting.
    {
        const FCMLAirshipVector Local{1000, -250, 3000};
        for (const uint16 Turn : {uint16(0), uint16(16384), uint16(8192), uint16(45000)})
        {
            const FCMLAirshipVector World = FCMLFixedTurnTrig::RotateLocalToWorld(Local, Turn);
            const FCMLAirshipVector Back = FCMLFixedTurnTrig::RotateWorldToLocal(World, Turn);
            TestTrue(FString::Printf(TEXT("Turn %u round trips X"), Turn),
                FMath::Abs(Back.X - Local.X) <= 1);
            TestTrue(FString::Printf(TEXT("Turn %u round trips Z"), Turn),
                FMath::Abs(Back.Z - Local.Z) <= 1);
            TestEqual(TEXT("Height is untouched by yaw"), Back.Y, Local.Y);
        }
    }

    // A quarter turn maps +X onto -Z exactly, with no rounding slack.
    {
        const FCMLAirshipVector Rotated =
            FCMLFixedTurnTrig::RotateLocalToWorld(FCMLAirshipVector{1000, 0, 0}, 16384);
        TestEqual(TEXT("A quarter turn zeroes X"), Rotated.X, static_cast<int64>(0));
        TestEqual(TEXT("A quarter turn sends X to -Z"), Rotated.Z, static_cast<int64>(-1000));
    }

    // Rounding must be symmetric: rounding towards zero would let the same
    // speed travel further one way than the other, and the drift accumulates.
    {
        TestEqual(TEXT("Positive half rounds away"),
            FCMLFixedTurnTrig::RoundDivideAwayFromZero(5, 2), static_cast<int64>(3));
        TestEqual(TEXT("Negative half rounds away"),
            FCMLFixedTurnTrig::RoundDivideAwayFromZero(-5, 2), static_cast<int64>(-3));
        TestEqual(TEXT("Exact division is unchanged"),
            FCMLFixedTurnTrig::RoundDivideAwayFromZero(-4, 2), static_cast<int64>(-2));
        TestEqual(TEXT("Zero stays zero"),
            FCMLFixedTurnTrig::RoundDivideAwayFromZero(0, 2), static_cast<int64>(0));
    }
    return true;
}
#endif

#include "Presentation/CMLIntroThreat.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Threat = FCMLIntroThreat;
    constexpr float Step = 1.0f / 60.0f;
    constexpr float RequiredYaw = 26.0f;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLIntroThreatTest,
    "CML.Core.Presentation.IntroThreat",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLIntroThreatTest::RunTest(const FString& Parameters)
{
    // The clearance comes from the two real sizes, so a bigger rock or a bigger
    // hull needs a wider miss. A tuned constant could not know that.
    {
        const float Small = Threat::MeasureClearance(10.0f, 20.0f);
        const float BigRock = Threat::MeasureClearance(40.0f, 20.0f);
        const float BigHull = Threat::MeasureClearance(10.0f, 60.0f);
        TestTrue(TEXT("A bigger rock needs more room"), BigRock > Small);
        TestTrue(TEXT("So does a bigger hull"), BigHull > Small);
        TestEqual(TEXT("And the margin is always added"),
            Threat::MeasureClearance(0.0f, 0.0f), Threat::MissMargin, 1e-4f);
    }

    // Flown correctly, the rock ends up clear of the hull by the full measured
    // clearance — the difference between a scare and a real miss.
    {
        const float Clearance = Threat::MeasureClearance(30.0f, 45.0f);
        FCMLIntroThreatState State = Threat::Launch(1.0f, Clearance);
        TestEqual(TEXT("It launches far out"), State.Distance, Threat::LaunchDistance, 1e-3f);
        TestTrue(TEXT("And dead ahead"), FMath::IsNearlyZero(State.Lateral));

        for (int32 Frame = 0; Frame < 600 && State.bActive; ++Frame)
        {
            Threat::Advance(State, RequiredYaw, RequiredYaw, Step);
        }
        TestEqual(TEXT("A full turn earns the whole clearance"), State.Cleared, 1.0f, 1e-3f);
        // Turning right pushes the rock to the ship's left.
        TestEqual(TEXT("And puts the rock a full clearance aside"),
            State.Lateral, -Clearance, 0.01f);
    }

    // Not turning leaves it dead ahead: the lesson is the only thing that
    // moves it, so failing to fly is visibly failing.
    {
        FCMLIntroThreatState State = Threat::Launch(1.0f, 100.0f);
        for (int32 Frame = 0; Frame < 200; ++Frame)
        {
            Threat::Advance(State, 0.0f, RequiredYaw, Step);
        }
        TestEqual(TEXT("It stays on the nose"), State.Lateral, 0.0f, 1e-4f);
        TestEqual(TEXT("Having earned nothing"), State.Cleared, 0.0f, 1e-4f);
    }

    // Turning the wrong way earns nothing rather than counting as effort.
    {
        FCMLIntroThreatState State = Threat::Launch(1.0f, 100.0f);
        for (int32 Frame = 0; Frame < 200; ++Frame)
        {
            Threat::Advance(State, -RequiredYaw * 2.0f, RequiredYaw, Step);
        }
        TestEqual(TEXT("Turning away from the ask earns nothing"), State.Cleared, 0.0f, 1e-4f);
    }

    // Clearance never gives ground: easing off the stick after a good turn must
    // not slide the rock back into the hull.
    {
        FCMLIntroThreatState State = Threat::Launch(-1.0f, 80.0f);
        for (int32 Frame = 0; Frame < 60; ++Frame)
        {
            Threat::Advance(State, -RequiredYaw, RequiredYaw, Step);
        }
        const float Earned = State.Cleared;
        TestTrue(TEXT("A left turn earns clearance too"), Earned > 0.9f);

        for (int32 Frame = 0; Frame < 60; ++Frame)
        {
            Threat::Advance(State, 0.0f, RequiredYaw, Step);
        }
        TestEqual(TEXT("Letting go does not take it back"), State.Cleared, Earned, 1e-4f);
        // A left turn pushes the rock to the ship's right.
        TestTrue(TEXT("A left turn throws the rock the other way"), State.Lateral > 0.0f);
    }

    // Once cleared, the rock slows so the pass can be watched instead of being
    // over before it registers.
    //
    // The clearance earned this frame governs the *next* one: the rock moves on
    // what the turn had bought when the frame began. So the two only diverge
    // from the second frame onwards, and testing a single frame would compare
    // two rocks that had both been travelling at full approach speed.
    {
        FCMLIntroThreatState Fast = Threat::Launch(1.0f, 50.0f);
        FCMLIntroThreatState Slow = Threat::Launch(1.0f, 50.0f);
        Threat::Advance(Fast, 0.0f, RequiredYaw, Step);
        Threat::Advance(Slow, RequiredYaw, RequiredYaw, Step);
        TestEqual(TEXT("On the first frame both close at approach speed"),
            Slow.Distance, Fast.Distance, 1e-4f);

        Threat::Advance(Fast, 0.0f, RequiredYaw, Step);
        Threat::Advance(Slow, RequiredYaw, RequiredYaw, Step);
        TestTrue(TEXT("From the second, a cleared rock closes more slowly"),
            Slow.Distance > Fast.Distance);
    }

    // It goes away once it is behind, rather than being tracked forever.
    {
        FCMLIntroThreatState State = Threat::Launch(1.0f, 50.0f);
        int32 Frames = 0;
        while (State.bActive && Frames < 100000)
        {
            Threat::Advance(State, RequiredYaw, RequiredYaw, Step);
            ++Frames;
        }
        TestFalse(TEXT("It eventually goes"), State.bActive);
        TestTrue(TEXT("Behind the ship"), State.Distance < 0.0f);
    }
    return true;
}
#endif

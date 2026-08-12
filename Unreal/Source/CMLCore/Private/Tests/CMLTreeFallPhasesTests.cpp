#include "Presentation/CMLTreeFallPhases.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Phases = FCMLTreeFallPhases;

    FCMLTreeFallReading Falling(const float Angle)
    {
        FCMLTreeFallReading Reading;
        Reading.FallAngleDegrees = Angle;
        return Reading;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLTreeFallPhasesTest,
    "CML.Core.Presentation.TreeFallPhases",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLTreeFallPhasesTest::RunTest(const FString& Parameters)
{
    constexpr float ReleaseAngle = 9.0f;
    constexpr float Step = 1.0f / 60.0f;
    float QuietTime = 0.0f;

    // It hangs on its stump until it has leaned past the release angle, and the
    // release itself takes a step: the joint is asked to go before it has gone.
    {
        QuietTime = 0.0f;
        TestEqual(TEXT("Below the angle it stays hinged"),
            static_cast<int32>(Phases::Advance(
                ECMLTreeFallPhase::SupportedRelease, Falling(4.0f),
                ReleaseAngle, Step, QuietTime)),
            static_cast<int32>(ECMLTreeFallPhase::SupportedRelease));

        TestEqual(TEXT("Past it, the joint is asked to go"),
            static_cast<int32>(Phases::Advance(
                ECMLTreeFallPhase::SupportedRelease, Falling(ReleaseAngle),
                ReleaseAngle, Step, QuietTime)),
            static_cast<int32>(ECMLTreeFallPhase::JointReleasePending));

        // Still pending while the joint is alive: a joint fighting a restored
        // ground contact for one step is the visible hitch at release.
        FCMLTreeFallReading Pending = Falling(12.0f);
        TestEqual(TEXT("It waits for the joint to actually go"),
            static_cast<int32>(Phases::Advance(
                ECMLTreeFallPhase::JointReleasePending, Pending,
                ReleaseAngle, Step, QuietTime)),
            static_cast<int32>(ECMLTreeFallPhase::JointReleasePending));

        Pending.bHingeReleased = true;
        TestEqual(TEXT("Then it falls"),
            static_cast<int32>(Phases::Advance(
                ECMLTreeFallPhase::JointReleasePending, Pending,
                ReleaseAngle, Step, QuietTime)),
            static_cast<int32>(ECMLTreeFallPhase::FreeFall));
    }

    // The bounce is qualified. Each of the three conditions can veto it on its
    // own, because a tree that bounced on its base would read as rubber.
    {
        const FCMLTreeFallReading Steep = Falling(70.0f);
        TestTrue(TEXT("A fast, steep, distal impact bounces"),
            Phases::QualifiesAsRebound(Steep, 200.0f, 0.8f));

        TestFalse(TEXT("A slow touch does not"),
            Phases::QualifiesAsRebound(Steep, 10.0f, 0.8f));
        TestFalse(TEXT("A shallow angle does not"),
            Phases::QualifiesAsRebound(Falling(20.0f), 200.0f, 0.8f));
        TestFalse(TEXT("And a contact near the stump does not"),
            Phases::QualifiesAsRebound(Steep, 200.0f, 0.05f));

        // The thresholds are boundaries, not strict inequalities.
        TestTrue(TEXT("Exactly at every threshold it still bounces"),
            Phases::QualifiesAsRebound(
                Falling(Phases::MinimumImpactAngleDegrees),
                Phases::MinimumImpactSpeed,
                Phases::MinimumDistalContactFraction));
    }

    // Free fall does not end on its own: a tree that strikes nothing keeps
    // falling until it does.
    {
        QuietTime = 0.0f;
        FCMLTreeFallReading Airborne = Falling(60.0f);
        Airborne.ElapsedInPhaseSeconds = 5.0f;
        TestEqual(TEXT("Free fall waits for an impact"),
            static_cast<int32>(Phases::Advance(
                ECMLTreeFallPhase::FreeFall, Airborne, ReleaseAngle, Step, QuietTime)),
            static_cast<int32>(ECMLTreeFallPhase::FreeFall));
    }

    // A bounce ends either by landing again, or by running out of time while
    // still touching — the second is the net that stops a bounce that never
    // separates leaving the tree mid-phase forever.
    {
        FCMLTreeFallReading Bouncing;
        Bouncing.ElapsedInPhaseSeconds = 0.05f;
        Bouncing.bSeparatedAfterImpact = true;
        Bouncing.bHasGroundSupport = true;
        TestFalse(TEXT("Too soon to settle"), Phases::ShouldSettle(Bouncing));

        Bouncing.ElapsedInPhaseSeconds = 0.30f;
        TestTrue(TEXT("Landing after separating settles it"), Phases::ShouldSettle(Bouncing));

        FCMLTreeFallReading Grounded;
        Grounded.ElapsedInPhaseSeconds = 0.30f;
        Grounded.bSeparatedAfterImpact = false;
        Grounded.bHasGroundSupport = true;
        TestFalse(TEXT("Touching without ever leaving does not settle yet"),
            Phases::ShouldSettle(Grounded));

        Grounded.ElapsedInPhaseSeconds = Phases::MaximumReboundSeconds;
        TestTrue(TEXT("But it does once the bounce runs out"),
            Phases::ShouldSettle(Grounded));

        FCMLTreeFallReading StillAirborne;
        StillAirborne.ElapsedInPhaseSeconds = 5.0f;
        StillAirborne.bHasGroundSupport = false;
        TestFalse(TEXT("And never while it is still in the air"),
            Phases::ShouldSettle(StillAirborne));
    }

    // Rest needs a continuous quiet stretch: one quiet frame mid-tumble is not
    // a tree at rest, and the timer starts again from nothing.
    {
        QuietTime = 0.0f;
        FCMLTreeFallReading Quiet;
        Quiet.AngularSpeed = 0.01f;
        Quiet.LinearSpeed = 1.0f;

        int32 Steps = 0;
        while (!Phases::IsAtRest(Quiet, Step, QuietTime) && Steps < 1000)
        {
            ++Steps;
        }
        TestTrue(TEXT("It comes to rest after a while"), Steps < 1000);
        TestTrue(TEXT("But not immediately"), Steps > 10);

        // A single loud frame resets the whole timer.
        QuietTime = 0.5f;
        FCMLTreeFallReading Loud;
        Loud.AngularSpeed = 2.0f;
        Loud.LinearSpeed = 300.0f;
        TestFalse(TEXT("A loud frame is not rest"), Phases::IsAtRest(Loud, Step, QuietTime));
        TestEqual(TEXT("And it resets the timer to nothing"), QuietTime, 0.0f, 1e-6f);
    }

    // The whole sequence runs to Complete and stays there.
    {
        QuietTime = 0.0f;
        FCMLTreeFallReading Settled;
        Settled.AngularSpeed = 0.0f;
        Settled.LinearSpeed = 0.0f;
        ECMLTreeFallPhase Phase = ECMLTreeFallPhase::Settlement;
        for (int32 Index = 0; Index < 200; ++Index)
        {
            Phase = Phases::Advance(Phase, Settled, ReleaseAngle, Step, QuietTime);
        }
        TestEqual(TEXT("It finishes"), static_cast<int32>(Phase),
            static_cast<int32>(ECMLTreeFallPhase::Complete));
        TestEqual(TEXT("And stays finished"),
            static_cast<int32>(Phases::Advance(
                Phase, Settled, ReleaseAngle, Step, QuietTime)),
            static_cast<int32>(ECMLTreeFallPhase::Complete));
    }
    return true;
}
#endif

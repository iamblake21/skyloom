#include "Presentation/CMLEquipmentSwing.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLEquipmentSwingTest,
    "CML.Core.Presentation.EquipmentSwing",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLEquipmentSwingTest::RunTest(const FString& Parameters)
{
    using Swing = FCMLEquipmentSwing;
    constexpr float Near = Swing::MinimumContactDistanceUnrealUnits;
    constexpr float Far = Swing::MaximumStrikeDistanceUnrealUnits;

    // A swing starts and ends at rest, so it composes cleanly onto the
    // calibrated pose without a jump at either end.
    {
        for (const bool bHasTarget : {true, false})
        {
            const FCMLViewmodelOffset Start = Swing::EvaluatePose(0.0f, bHasTarget, Near);
            TestTrue(TEXT("It starts at rest"),
                Start.Position.IsNearlyZero(0.01) && Start.Rotation.IsNearlyZero(0.01));
            const FCMLViewmodelOffset End = Swing::EvaluatePose(1.0f, bHasTarget, Near);
            TestTrue(TEXT("And returns to rest"),
                End.Position.IsNearlyZero(0.01) && End.Rotation.IsNearlyZero(0.01));
        }
    }

    // A miss follows through much further than a hit, which is what makes a
    // whiff read as a whiff rather than as a shorter hit.
    {
        const FCMLViewmodelOffset Hit = Swing::EvaluatePose(0.5f, true, Near);
        const FCMLViewmodelOffset Miss = Swing::EvaluatePose(0.42f, false, 0.0f);
        TestTrue(TEXT("A miss travels further than a hit"),
            Miss.Position.Size() > Hit.Position.Size() * 2.0);
        TestTrue(TEXT("And a miss takes longer"),
            Swing::DurationFor(false) > Swing::DurationFor(true));
    }

    // The pickaxe winds up before it comes down: early in the swing the head
    // goes back and up, not forward.
    {
        const FCMLViewmodelOffset Windup = Swing::EvaluatePose(0.2f, true, Near);
        TestTrue(TEXT("It lifts on the wind-up"), Windup.Position.Z > 0.0);
        TestTrue(TEXT("And draws back"), Windup.Position.X < 0.0);

        const FCMLViewmodelOffset Contact = Swing::EvaluatePose(Swing::ImpactProgress, true, Near);
        TestTrue(TEXT("At contact it is below rest"), Contact.Position.Z < 0.0);
        TestTrue(TEXT("And thrown forward"), Contact.Position.X > 0.0);
        TestTrue(TEXT("It swings downwards, not up"), Contact.Rotation.Pitch < 0.0);
    }

    // The pose holds through contact, so the frame the blow lands on is legible
    // rather than a blur between two poses.
    {
        const FCMLViewmodelOffset AtImpact =
            Swing::EvaluatePose(Swing::ImpactProgress, true, Near);
        const FCMLViewmodelOffset JustAfter = Swing::EvaluatePose(0.52f, true, Near);
        TestTrue(TEXT("The pose is held across the blow"),
            AtImpact.Position.Equals(JustAfter.Position, 0.01));
    }

    // Distance chooses the contact pose: a blow at the limit of reach ends
    // further out than one at arm's length, or the pickaxe hangs in the air
    // short of what it hit.
    {
        const FCMLViewmodelOffset Close =
            Swing::EvaluatePose(Swing::ImpactProgress, true, Near);
        const FCMLViewmodelOffset Distant =
            Swing::EvaluatePose(Swing::ImpactProgress, true, Far);
        TestTrue(TEXT("A distant blow reaches further forward"),
            Distant.Position.X > Close.Position.X);
        TestTrue(TEXT("And drops lower"), Distant.Position.Z < Close.Position.Z);
        TestTrue(TEXT("Leaning further over"),
            Distant.Rotation.Pitch < Close.Rotation.Pitch);

        // Beyond the authored range it clamps rather than extrapolating into a
        // pose no one ever looked at.
        const FCMLViewmodelOffset TooFar =
            Swing::EvaluatePose(Swing::ImpactProgress, true, Far * 4.0f);
        TestTrue(TEXT("Past the limit it holds the far pose"),
            TooFar.Position.Equals(Distant.Position, 0.01));
    }

    // After contact it rebounds back up before settling.
    {
        const FCMLViewmodelOffset Contact = Swing::EvaluatePose(0.53f, true, Near);
        const FCMLViewmodelOffset Rebound = Swing::EvaluatePose(0.68f, true, Near);
        TestTrue(TEXT("It bounces back up"), Rebound.Position.Z > Contact.Position.Z);
        TestTrue(TEXT("And pulls back"), Rebound.Position.X < Contact.Position.X);
    }

    // The impact fires exactly once, even across a frame long enough to step
    // over the instant it happens.
    {
        TestFalse(TEXT("Before the blow, nothing"),
            Swing::CrossedImpact(0.10f, 0.30f, true));
        TestTrue(TEXT("Crossing it fires"),
            Swing::CrossedImpact(0.40f, 0.50f, true));
        TestFalse(TEXT("And does not fire again after"),
            Swing::CrossedImpact(0.50f, 0.60f, true));
        TestTrue(TEXT("A single long frame still fires it"),
            Swing::CrossedImpact(0.0f, 1.0f, true));
        TestFalse(TEXT("A swing that hits nothing has no impact at all"),
            Swing::CrossedImpact(0.0f, 1.0f, false));
    }

    // The motion is continuous: no phase boundary jumps the viewmodel.
    //
    // A large step between samples on its own proves nothing — the descent is
    // meant to be fast, and cubic easing makes it fastest right before contact.
    // What separates fast from broken is how the largest step behaves as the
    // sampling tightens: continuous motion halves it, a discontinuity does not
    // shrink at all.
    {
        auto LargestStep = [](const bool bHasTarget, const int32 Samples)
        {
            FCMLViewmodelOffset Previous = Swing::EvaluatePose(0.0f, bHasTarget, Near);
            double Largest = 0.0;
            for (int32 Step = 1; Step <= Samples; ++Step)
            {
                const FCMLViewmodelOffset Current =
                    Swing::EvaluatePose(static_cast<float>(Step) / Samples, bHasTarget, Near);
                Largest = FMath::Max(Largest, (Current.Position - Previous.Position).Size());
                Previous = Current;
            }
            return Largest;
        };

        for (const bool bHasTarget : {true, false})
        {
            const double Coarse = LargestStep(bHasTarget, 500);
            const double Fine = LargestStep(bHasTarget, 5000);
            TestTrue(TEXT("Ten times the samples means a far smaller largest step"),
                Fine < Coarse * 0.25);
            TestTrue(TEXT("Which is only true if the motion is continuous"), Fine < 1.0);
        }
    }
    return true;
}
#endif

#include "Presentation/CMLIntroCrash.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLIntroCrashTest,
    "CML.Core.Presentation.IntroCrash",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLIntroCrashTest::RunTest(const FString& Parameters)
{
    using Crash = FCMLIntroCrash;

    // It arrives with real speed and is taken away by friction, so the wreck
    // ends up somewhere it was carried to rather than somewhere it was placed.
    {
        FCMLIntroSkidState State = Crash::Touchdown();
        TestEqual(TEXT("It lands at speed"), State.Speed, Crash::TouchdownSpeed, 1e-4f);
        TestFalse(TEXT("And is not stopped"), State.HasStopped());
        TestEqual(TEXT("Riding above the ground"),
            State.HullClearance, Crash::HullClearance, 1e-4f);
    }

    // Integrating the slide reproduces the closed-form distance. Getting this
    // wrong puts the wreck metres from where the shot was framed for.
    {
        FCMLIntroSkidState State = Crash::Touchdown();
        constexpr float Step = 1.0f / 60.0f;
        int32 Frames = 0;
        while (!State.HasStopped() && Frames < 100000)
        {
            Crash::Advance(State, Step);
            ++Frames;
        }
        TestTrue(TEXT("It comes to rest"), State.HasStopped());
        TestEqual(TEXT("Having travelled the predicted distance"),
            State.Travelled, Crash::PredictedSkidDistance(), 0.5f);
        TestEqual(TEXT("And settled onto the ground"), State.HullClearance, 0.0f, 1e-4f);
    }

    // The frame it stops on is reported once, so a landing sound or a dust
    // burst fires exactly there and not every frame afterwards.
    {
        FCMLIntroSkidState State = Crash::Touchdown();
        int32 Stops = 0;
        for (int32 Frame = 0; Frame < 2000; ++Frame)
        {
            if (Crash::Advance(State, 1.0f / 60.0f))
            {
                ++Stops;
            }
        }
        TestEqual(TEXT("It reports coming to rest exactly once"), Stops, 1);
    }

    // The integration does not depend on the frame rate: a slide taken in big
    // steps has to end up in the same place as one taken in small ones, or the
    // wreck lands differently on a slow machine.
    {
        auto SlideWith = [](const float Step)
        {
            FCMLIntroSkidState State = FCMLIntroCrash::Touchdown();
            int32 Frames = 0;
            while (!State.HasStopped() && Frames < 100000)
            {
                FCMLIntroCrash::Advance(State, Step);
                ++Frames;
            }
            return State.Travelled;
        };
        const float Fine = SlideWith(1.0f / 240.0f);
        const float Coarse = SlideWith(1.0f / 20.0f);
        TestEqual(TEXT("The slide is the same length at any frame rate"),
            Coarse, Fine, 0.5f);
    }

    // A frame long enough to stop it outright must not carry it past the stop.
    {
        FCMLIntroSkidState State = Crash::Touchdown();
        Crash::Advance(State, 100.0f);
        TestTrue(TEXT("One huge frame stops it"), State.HasStopped());
        TestTrue(TEXT("Without overshooting the predicted distance"),
            State.Travelled <= Crash::PredictedSkidDistance() + 0.5f);
        TestTrue(TEXT("And never travels backwards"), State.Travelled >= 0.0f);
    }
    return true;
}
#endif

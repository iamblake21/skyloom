#include "Presentation/CMLIntroSequence.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Sequence = FCMLIntroSequence;
    constexpr float Step = 1.0f / 60.0f;

    /** Runs frames until the shot changes or the budget runs out. */
    int32 RunUntilShotChanges(
        FCMLIntroState& State, const FCMLIntroTimings& Timings, const FCMLIntroInput& Input)
    {
        const ECMLIntroShot Started = State.Shot;
        int32 Frames = 0;
        while (State.Shot == Started && Frames < 100000)
        {
            Sequence::Advance(State, Timings, Input, Step, true);
            ++Frames;
        }
        return Frames;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLIntroSequenceTest,
    "CML.Core.Presentation.IntroSequence",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLIntroSequenceTest::RunTest(const FString& Parameters)
{
    const FCMLIntroTimings Timings;
    const FCMLIntroInput Idle;

    // The shots run in order and each takes its authored time.
    {
        FCMLIntroState State;
        TestEqual(TEXT("It opens on hyperspace"),
            static_cast<int32>(State.Shot), static_cast<int32>(ECMLIntroShot::Hyperspace));

        const int32 Frames = RunUntilShotChanges(State, Timings, Idle);
        TestEqual(TEXT("Then the cockpit"),
            static_cast<int32>(State.Shot), static_cast<int32>(ECMLIntroShot::Cockpit));
        TestTrue(TEXT("After roughly the authored 4.5 seconds"),
            FMath::Abs(Frames * Step - Timings.HyperspaceSeconds) < 0.05f);

        RunUntilShotChanges(State, Timings, Idle);
        // Flight sits third on purpose: the player is flying before anything
        // goes wrong, so the alarm interrupts something they were already doing.
        TestEqual(TEXT("And then the player is given the controls"),
            static_cast<int32>(State.Shot), static_cast<int32>(ECMLIntroShot::Flight));
    }

    // The flight lesson does not run on a clock. Left alone it waits forever,
    // because an opening that flew itself would teach nothing.
    {
        FCMLIntroState State;
        State.Shot = ECMLIntroShot::Flight;
        for (int32 Frame = 0; Frame < 6000; ++Frame)
        {
            Sequence::Advance(State, Timings, Idle, Step, true);
        }
        TestEqual(TEXT("Without input the lesson never ends"),
            static_cast<int32>(State.Shot), static_cast<int32>(ECMLIntroShot::Flight));
        TestTrue(TEXT("But it does get past the settle"),
            State.FlightStep != ECMLIntroFlightStep::Settle);
    }

    // Turning far enough, and holding it, passes the first half.
    {
        FCMLIntroState State;
        State.Shot = ECMLIntroShot::Flight;
        FCMLIntroInput Turned;
        Turned.YawDegrees = Timings.TutorialTurnDegrees + 2.0f;

        for (int32 Frame = 0; Frame < 600; ++Frame)
        {
            Sequence::Advance(State, Timings, Turned, Step, true);
        }
        TestTrue(TEXT("Turning right gets past the right-hand lesson"),
            State.FlightStep == ECMLIntroFlightStep::TeachLeft
                || State.FlightStep == ECMLIntroFlightStep::ApproachLeft
                || State.FlightStep == ECMLIntroFlightStep::RecoverRight);
    }

    // Held, not merely touched: a flick across the threshold does not count.
    {
        FCMLIntroState State;
        State.Shot = ECMLIntroShot::Flight;
        State.FlightStep = ECMLIntroFlightStep::TeachRight;

        FCMLIntroInput Flick;
        for (int32 Frame = 0; Frame < 400; ++Frame)
        {
            // One frame over the line, one frame back to centre, repeatedly.
            Flick.YawDegrees = (Frame % 2 == 0) ? Timings.TutorialTurnDegrees + 5.0f : 0.0f;
            Sequence::Advance(State, Timings, Flick, Step, true);
        }
        TestEqual(TEXT("Flicking never satisfies the lesson"),
            static_cast<int32>(State.FlightStep),
            static_cast<int32>(ECMLIntroFlightStep::TeachRight));
        TestTrue(TEXT("Because releasing resets the hold"),
            State.HeldSeconds < Timings.TutorialHoldSeconds);
    }

    // Turning the wrong way does not pass the lesson either.
    {
        FCMLIntroState State;
        State.Shot = ECMLIntroShot::Flight;
        State.FlightStep = ECMLIntroFlightStep::TeachRight;
        FCMLIntroInput WrongWay;
        WrongWay.YawDegrees = -Timings.TutorialTurnDegrees - 10.0f;
        for (int32 Frame = 0; Frame < 400; ++Frame)
        {
            Sequence::Advance(State, Timings, WrongWay, Step, true);
        }
        TestEqual(TEXT("Turning left does not pass the right-hand lesson"),
            static_cast<int32>(State.FlightStep),
            static_cast<int32>(ECMLIntroFlightStep::TeachRight));
    }

    // The card is shown while the player is being asked, and not during the
    // settle, which is the airship steadying itself rather than a request.
    {
        FCMLIntroState State;
        State.Shot = ECMLIntroShot::Flight;
        State.FlightStep = ECMLIntroFlightStep::Settle;
        TestFalse(TEXT("No card while settling"), Sequence::ShouldShowTutorialCard(State));

        State.FlightStep = ECMLIntroFlightStep::TeachRight;
        TestTrue(TEXT("A card while being taught"), Sequence::ShouldShowTutorialCard(State));
        TestEqual(TEXT("Pointing right"), Sequence::TutorialDirection(State), 1.0f, 1e-6f);

        State.FlightStep = ECMLIntroFlightStep::TeachLeft;
        TestEqual(TEXT("Then left"), Sequence::TutorialDirection(State), -1.0f, 1e-6f);

        State.Shot = ECMLIntroShot::Crash;
        TestFalse(TEXT("And never outside the flight shot"),
            Sequence::ShouldShowTutorialCard(State));
    }

    // Skipping goes straight to the end rather than fast-forwarding eleven
    // shots, which is not what someone asking to skip wants.
    {
        FCMLIntroState State;
        FCMLIntroInput Skip;
        Skip.bSkipRequested = true;
        TestTrue(TEXT("Skipping finishes it"),
            Sequence::Advance(State, Timings, Skip, Step, true));
        TestTrue(TEXT("And it is complete"), State.IsComplete());

        // Unless skipping is not allowed.
        FCMLIntroState Locked;
        Sequence::Advance(Locked, Timings, Skip, Step, false);
        TestFalse(TEXT("A locked opening cannot be skipped"), Locked.IsComplete());
    }

    // A single long frame advances one shot at a time. Carrying the overshoot
    // forward would let one hitch skip a whole beat of the opening.
    {
        FCMLIntroState State;
        Sequence::Advance(State, Timings, Idle, 60.0f, true);
        TestEqual(TEXT("One long frame advances exactly one shot"),
            static_cast<int32>(State.Shot), static_cast<int32>(ECMLIntroShot::Cockpit));
        TestEqual(TEXT("And the next shot starts from zero"),
            State.ElapsedInShot, 0.0f, 1e-6f);
    }

    // Played through with a cooperative pilot, the whole opening finishes.
    {
        FCMLIntroState State;
        FCMLIntroInput Pilot;
        bool bFinished = false;
        for (int32 Frame = 0; Frame < 20000 && !bFinished; ++Frame)
        {
            // Turn whichever way is being asked for.
            const float Direction = Sequence::TutorialDirection(State);
            Pilot.YawDegrees = Direction * (Timings.TutorialTurnDegrees + 3.0f);
            bFinished = Sequence::Advance(State, Timings, Pilot, Step, true);
        }
        TestTrue(TEXT("The opening reaches its end"), State.IsComplete());
    }
    return true;
}
#endif

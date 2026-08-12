#include "Presentation/CMLIntroDressing.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    using Dressing = FCMLIntroDressing_Evaluator;

    FCMLIntroDressing At(const ECMLIntroShot Shot, const float Elapsed)
    {
        static const FCMLIntroTimings Timings;
        return Dressing::Evaluate(Shot, Elapsed, Timings, 0.0f);
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLIntroDressingTest,
    "CML.Core.Presentation.IntroDressing",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLIntroDressingTest::RunTest(const FString& Parameters)
{
    const FCMLIntroTimings Timings;

    // Hyperspace: the rig swings round and closes in, so the ship grows in
    // frame instead of sitting still against a moving tunnel.
    {
        const FCMLIntroDressing Start = At(ECMLIntroShot::Hyperspace, 0.0f);
        const FCMLIntroDressing End =
            At(ECMLIntroShot::Hyperspace, Timings.HyperspaceSeconds);
        TestTrue(TEXT("The rig closes in"), End.ChaseDistance < Start.ChaseDistance);
        TestTrue(TEXT("And comes down"), End.ChaseHeight < Start.ChaseHeight);
        TestTrue(TEXT("Swinging round towards the tail"),
            End.ChaseOrbitDegrees > Start.ChaseOrbitDegrees);
        TestTrue(TEXT("The lens comes off the boil"),
            End.ChaseFovOffset < Start.ChaseFovOffset);
        TestTrue(TEXT("The tunnel is running"), Start.WarpSpeed > 0.0f);
        TestTrue(TEXT("And so are the streaks"), Start.StreakRate > 0.0f);
        TestTrue(TEXT("The streaks are decisively superluminal"),
            Start.StreakSpeed >= 400.0f);
    }

    // The tear resists before it gives: slow at first, then all at once.
    {
        const FCMLIntroDressing Early =
            At(ECMLIntroShot::RiftOpen, Timings.RiftOpenSeconds * 0.25f);
        const FCMLIntroDressing Half =
            At(ECMLIntroShot::RiftOpen, Timings.RiftOpenSeconds * 0.5f);
        const FCMLIntroDressing Late =
            At(ECMLIntroShot::RiftOpen, Timings.RiftOpenSeconds);

        TestTrue(TEXT("It opens through the shot"),
            Early.RiftOpenness < Half.RiftOpenness && Half.RiftOpenness < Late.RiftOpenness);
        TestTrue(TEXT("It is barely open a quarter of the way in"),
            Early.RiftOpenness < 0.15f);
        TestTrue(TEXT("And fully open by the end"), Late.RiftOpenness > 0.95f);
        // The second half moves far more than the first: that is the "resists,
        // then gives" reading, and a straight ramp would lose it.
        TestTrue(TEXT("The late half opens more than the early half"),
            (Late.RiftOpenness - Half.RiftOpenness) > (Half.RiftOpenness - Early.RiftOpenness));

        // Held further out at first, so black space stays around it.
        TestTrue(TEXT("It starts far away"),
            Early.RiftDistance > 200.0f);
        TestTrue(TEXT("And comes in"), Late.RiftDistance < Early.RiftDistance);
        TestTrue(TEXT("Its light turns from blue to violet"),
            Late.RiftLightColour.R > Early.RiftLightColour.R
                && Late.RiftLightColour.G < Early.RiftLightColour.G);
    }

    // Going in accelerates: the ship is taken, it does not drift in.
    {
        const FCMLIntroDressing Half =
            At(ECMLIntroShot::RiftEntry, Timings.RiftEntrySeconds * 0.5f);
        const FCMLIntroDressing End =
            At(ECMLIntroShot::RiftEntry, Timings.RiftEntrySeconds);
        TestTrue(TEXT("It is still most of the way out at the halfway point"),
            Half.RiftDistance > 60.0f);
        TestTrue(TEXT("And on top of the camera at the end"), End.RiftDistance < 10.0f);
        TestTrue(TEXT("The lens stretches as it goes"),
            End.CockpitFovOffset > Half.CockpitFovOffset);
        TestTrue(TEXT("The alarm gives way to the light"),
            End.AlertIntensity < Half.AlertIntensity);
    }

    // A klaxon is a sharp attack and a long decay, never a sine: sampled across
    // one beat, the lamp is far brighter at the start than in the middle.
    {
        const FCMLIntroDressing Struck = At(ECMLIntroShot::Alarm, 1.0f);
        const FCMLIntroDressing Decayed = At(ECMLIntroShot::Alarm, 1.0f + 0.41f);
        TestTrue(TEXT("The lamp strikes hard"), Struck.AlertIntensity > 0.0f);
        TestTrue(TEXT("And has faded by mid-beat"),
            Decayed.AlertIntensity < Struck.AlertIntensity * 0.5f);

        // The cockpit goes dark as the alarm takes over.
        const FCMLIntroDressing Onset = At(ECMLIntroShot::Alarm, 0.0f);
        TestTrue(TEXT("The fill light drops away"),
            Struck.CockpitFillIntensity < Onset.CockpitFillIntensity);
        const FCMLIntroDressing Cruise = At(ECMLIntroShot::Flight, 1.0f);
        TestTrue(TEXT("Cruise always carries a light travel vibration"),
            Cruise.ShakeAmount >= 0.08f);
        TestTrue(TEXT("The red-light alarm intensifies it dramatically"),
            Struck.ShakeAmount > Cruise.ShakeAmount * 5.0f);
    }

    // The crash decays rather than building: the hit is at the front of the
    // shot and everything after it is the ship coming to rest.
    {
        const FCMLIntroDressing Impact = At(ECMLIntroShot::Crash, 0.0f);
        const FCMLIntroDressing Resting =
            At(ECMLIntroShot::Crash, Timings.CrashSeconds);
        TestTrue(TEXT("It shakes hardest at the moment of impact"),
            Impact.ShakeAmount > Resting.ShakeAmount);
        TestTrue(TEXT("And is still by the end"), Resting.ShakeAmount < 0.05f);
        TestTrue(TEXT("The impact throws a flash"), Impact.FlashAlpha > 0.5f);
        TestTrue(TEXT("The wreck settles onto its side"),
            FMath::Abs(Resting.AirshipAttitude.Roll) > 5.0f);
    }

    // Blackout is black and still: nothing moves behind the handover.
    {
        const FCMLIntroDressing Black = At(ECMLIntroShot::Blackout, 1.0f);
        TestEqual(TEXT("Fully faded"), Black.FadeAlpha, 1.0f, 1e-6f);
        TestEqual(TEXT("Eyes shut"), Black.Eyelid, 1.0f, 1e-6f);
        TestEqual(TEXT("And perfectly still"), Black.ShakeAmount, 0.0f, 1e-6f);
    }

    // Waking blinks twice before the eyes stay open, and ends clear.
    {
        const FCMLIntroDressing Opening = At(ECMLIntroShot::Wake, 0.0f);
        const FCMLIntroDressing Flutter =
            At(ECMLIntroShot::Wake, Timings.WakeSeconds * 0.38f);
        const FCMLIntroDressing Between =
            At(ECMLIntroShot::Wake, Timings.WakeSeconds * 0.26f);
        const FCMLIntroDressing Clear = At(ECMLIntroShot::Wake, Timings.WakeSeconds);

        TestTrue(TEXT("It starts shut"), Opening.FadeAlpha > 0.8f);
        TestTrue(TEXT("Opens partway"), Between.FadeAlpha < Opening.FadeAlpha);
        // The second blink: darker again than the moment before it.
        TestTrue(TEXT("Then blinks a second time"), Flutter.FadeAlpha > Between.FadeAlpha);
        TestTrue(TEXT("And ends clear"), Clear.FadeAlpha < 0.02f);
    }

    // Every shot's flash and fade stay inside the range a screen overlay can
    // actually draw; a value above one would blow the frame out silently.
    {
        for (int32 Index = 0; Index <= static_cast<int32>(ECMLIntroShot::Complete); ++Index)
        {
            const ECMLIntroShot Shot = static_cast<ECMLIntroShot>(Index);
            for (int32 Sample = 0; Sample <= 20; ++Sample)
            {
                const FCMLIntroDressing Value = At(Shot, Sample * 0.5f);
                const bool bInRange =
                    Value.FlashAlpha >= 0.0f && Value.FlashAlpha <= 1.0f
                    && Value.FadeAlpha >= 0.0f && Value.FadeAlpha <= 1.0f
                    && Value.Eyelid >= 0.0f && Value.Eyelid <= 1.0f
                    && Value.RiftOpenness >= 0.0f && Value.RiftOpenness <= 1.0f
                    && Value.ShakeAmount >= 0.0f;
                if (!bInRange)
                {
                    AddError(FString::Printf(
                        TEXT("Shot %d at %.1fs produces an out-of-range overlay value"),
                        Index, Sample * 0.5f));
                    break;
                }
            }
        }
    }
    return true;
}
#endif

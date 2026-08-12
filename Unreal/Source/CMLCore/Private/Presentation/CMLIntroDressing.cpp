#include "Presentation/CMLIntroDressing.h"

namespace
{
    float Progress01(const float Elapsed, const float Duration)
    {
        return Duration > 0.0f ? FMath::Clamp(Elapsed / Duration, 0.0f, 1.0f) : 1.0f;
    }

    float SmoothStep(float Value)
    {
        Value = FMath::Clamp(Value, 0.0f, 1.0f);
        return Value * Value * (3.0f - 2.0f * Value);
    }

    /** Wraps a rising time into a repeating 0..1 beat. */
    float Beat(const float Elapsed, const float Period)
    {
        return Period > 0.0f ? FMath::Fmod(FMath::Max(0.0f, Elapsed), Period) / Period : 0.0f;
    }

    float DischargeFlash(const float Progress, const float At, const float Strength)
    {
        return FMath::Max(0.0f, 1.0f - FMath::Abs(Progress - At) * 22.0f) * Strength;
    }

    /** The shared cruise look: a lit cockpit riding steady. */
    void DressCruise(FCMLIntroDressing& Dressing, const float UnscaledTime)
    {
        // The hull breathes at speed. A slow roll under the seat keeps the shot
        // alive without turning it into a handheld camera.
        const float Roll = FMath::Sin(UnscaledTime * 0.75f) * 0.55f;
        const float Pitch = FMath::Sin(UnscaledTime * 0.51f + 1.7f) * 0.35f;
        Dressing.AirshipAttitude = FRotator(Pitch, 0.0f, Roll);
        Dressing.CockpitFillIntensity = 1.35f;
        // A small vibration is present for the entire powered flight.  It is
        // deliberately visible but calm; the alarm multiplies it below.
        Dressing.ShakeAmount = 0.085f;
        Dressing.WarpBlend = 1.0f;
        Dressing.WarpIntensity = 3.4f;
        Dressing.WarpSpeed = 7.8f;
        Dressing.StreakSpeed = 420.0f;
        Dressing.StreakRate = 900.0f;
        Dressing.KeyLightIntensity = 2.55f;
    }
}

FCMLIntroDressing FCMLIntroDressing_Evaluator::Evaluate(
    const ECMLIntroShot Shot,
    const float ElapsedInShot,
    const FCMLIntroTimings& Timings,
    const float UnscaledTime)
{
    FCMLIntroDressing Dressing;
    const float Elapsed = FMath::Max(0.0f, ElapsedInShot);

    switch (Shot)
    {
        case ECMLIntroShot::Hyperspace:
        {
            const float Progress = Progress01(Elapsed, Timings.HyperspaceSeconds);
            const float Eased = SmoothStep(Progress);

            // The rig swings round and closes in as the shot runs, so the ship
            // grows in frame instead of sitting still against a moving tunnel.
            // Held in three-quarter rather than swinging round to dead astern.
            // The opening is meant to show the airship — from directly behind
            // it reads as two engine bells and nothing else.
            Dressing.ChaseOrbitDegrees = FMath::Lerp(-52.0f, -34.0f, Eased);
            // In tight. Unity opens already superluminal — "the opening has no
            // slow approach" — and at 36 metres out the hull sat as a chip in
            // the middle of the frame, which reads as drifting however fast the
            // streaks run.
            // The hull is 14.6 m long, so 14 m out put the camera inside its
            // own length and the shot became an unlit black mass. Under two
            // ship lengths reads as close without swallowing the frame.
            Dressing.ChaseDistance = FMath::Lerp(26.0f, 18.0f, Eased);
            Dressing.ChaseHeight = FMath::Lerp(4.5f, 3.0f, Eased);
            // Stays wide. Pulling the lens back in is what made the shot feel
            // like it was settling down rather than tearing along.
            Dressing.ChaseFovOffset = 22.0f - Eased * 4.0f;

            Dressing.WarpBlend = 1.0f;
            Dressing.WarpIntensity = 3.4f;
            Dressing.WarpSpeed = 7.8f;
            Dressing.StreakSpeed = 420.0f;
            Dressing.StreakRate = 900.0f;
            Dressing.KeyLightIntensity = 2.55f;
            Dressing.ShakeAmount = 0.18f;
            break;
        }

        case ECMLIntroShot::Cockpit:
        {
            const float Progress = Progress01(Elapsed, Timings.CockpitSeconds);
            DressCruise(Dressing, UnscaledTime);
            Dressing.CockpitFovOffset = FMath::Sin(Progress * UE_PI) * 1.6f;
            // The cut in from the chase leaves a little light behind it.
            Dressing.FlashAlpha = FMath::Max(0.0f, 1.0f - Elapsed * 6.5f) * 0.5f;
            break;
        }

        case ECMLIntroShot::Flight:
            // The player is flying: the same cruise, held steady, so nothing
            // competes with the lesson.
            DressCruise(Dressing, UnscaledTime);
            break;

        case ECMLIntroShot::Alarm:
        {
            const float Progress = Progress01(Elapsed, Timings.AlarmSeconds);
            const float Onset = SmoothStep(FMath::Clamp(Elapsed / 0.45f, 0.0f, 1.0f));

            // A klaxon lamp is a sharp attack and a long decay, never a sine.
            const float Pulse = FMath::Pow(1.0f - Beat(Elapsed, 0.82f), 2.6f) * Onset;
            Dressing.AlertIntensity = Pulse * 9.5f;
            Dressing.CockpitFillIntensity = FMath::Lerp(1.35f, 0.18f, Onset);

            const float Severity = Onset * FMath::Lerp(0.6f, 1.0f, Progress);
            // The quiet travel vibration remains underneath the failure, then
            // the red-light alarm ramps to a much more violent shudder.
            Dressing.ShakeAmount = 0.085f + Severity * 0.72f;

            // The hull wallows; it does not buzz. A 26 Hz sine at a couple of
            // degrees was a mechanical vibration of the entire ship, and since
            // the cockpit camera rides the hull it became the camera shake as
            // well. Unity keeps the two apart: slow wounded motion here, and a
            // separate Perlin shake on a pivot of its own, which is what
            // ShakeAmount above is for.
            const float Yaw = FMath::Sin(UnscaledTime * 3.1f) * Severity * 7.5f;
            const float Wallow = FMath::Sin(UnscaledTime * 1.9f) * Severity * 2.2f;
            Dressing.AirshipAttitude = FRotator(Wallow, Yaw, -Yaw * 1.35f);

            // The drive is losing containment, so the tunnel stutters rather
            // than simply dimming.
            Dressing.WarpBlend = 1.0f;
            Dressing.WarpIntensity = 3.4f * (1.0f - Severity * 0.55f);
            Dressing.WarpSpeed = 7.8f * (1.0f - Severity * 0.35f);
            Dressing.StreakSpeed = FMath::Lerp(420.0f, 280.0f, Progress);
            Dressing.StreakRate = 900.0f * (1.0f - Severity * 0.4f);
            break;
        }

        case ECMLIntroShot::RiftOpen:
        {
            const float Progress = Progress01(Elapsed, Timings.RiftOpenSeconds);
            // Slow, then all at once: the tear resists before it gives.
            const float Tear = FMath::Pow(SmoothStep(Progress), 1.9f);
            Dressing.RiftOpenness = Tear;

            // Held further out at first, so black space stays around it and it
            // reads as an opening *in* something rather than as a wall.
            const float Approach = SmoothStep(
                FMath::GetRangePct(0.18f, 1.0f, FMath::Clamp(Progress, 0.18f, 1.0f)));
            Dressing.RiftDistance = FMath::Lerp(RiftStartDistance, 98.0f, Approach);
            Dressing.RiftLightIntensity = Tear * 46.0f;
            Dressing.RiftLightColour = FMath::Lerp(
                FLinearColor(0.32f, 0.82f, 1.0f), FLinearColor(0.72f, 0.36f, 1.0f), Tear);

            const float Alarm = FMath::Pow(1.0f - Beat(Elapsed, 0.68f), 2.6f);
            Dressing.AlertIntensity = Alarm * 8.0f;
            Dressing.ShakeAmount = 0.34f + Tear * 0.55f;

            Dressing.WarpBlend = FMath::Lerp(0.55f, 0.1f, Progress);
            Dressing.WarpIntensity = FMath::Lerp(2.4f, 0.5f, Progress);
            Dressing.WarpSpeed = 7.8f;
            Dressing.StreakSpeed = FMath::Lerp(280.0f, 110.0f, Progress);
            Dressing.StreakRate = FMath::Lerp(520.0f, 180.0f, Progress);
            Dressing.CockpitFovOffset = Tear * 9.0f;

            // The opening is punctuated by two electrical discharges; without
            // these the material merely grows and never feels as if it tears.
            Dressing.FlashAlpha = FMath::Clamp(
                DischargeFlash(Progress, 0.46f, 0.42f)
                + DischargeFlash(Progress, 0.79f, 0.58f), 0.0f, 1.0f);

            const float Shudder = FMath::Sin(UnscaledTime * 24.0f) * Tear;
            Dressing.AirshipAttitude = FRotator(
                Shudder * 2.8f, FMath::Sin(UnscaledTime * 2.4f) * 5.5f * Tear, Shudder * 3.4f);
            break;
        }

        case ECMLIntroShot::RiftEntry:
        {
            const float Progress = Progress01(Elapsed, Timings.RiftEntrySeconds);
            // Accelerating hard: the ship is taken, it does not drift in.
            const float Rush = FMath::Pow(Progress, 2.4f);

            Dressing.RiftOpenness = 1.0f;
            Dressing.RiftDistance = FMath::Lerp(98.0f, 5.0f, Rush);
            Dressing.RiftLightIntensity = 46.0f + Rush * 90.0f;
            Dressing.RiftLightColour = FLinearColor(0.72f, 0.36f, 1.0f);
            Dressing.ShakeAmount = 0.9f * (1.0f - Rush * 0.4f);
            Dressing.CockpitFovOffset = 9.0f + Rush * 26.0f;
            Dressing.AlertIntensity = 6.0f * (1.0f - Rush);
            Dressing.FlashAlpha = SmoothStep(
                FMath::GetRangePct(0.55f, 1.0f, FMath::Clamp(Progress, 0.55f, 1.0f)));
            break;
        }

        case ECMLIntroShot::Fall:
        {
            const float Progress = Progress01(Elapsed, Timings.FallSeconds);
            // Out of the tear and into air: the light of the rift is gone, the
            // hull is tumbling, and the shake eases as it finds an attitude.
            Dressing.ShakeAmount = FMath::Lerp(0.85f, 0.30f, SmoothStep(Progress));
            const float Tumble = 1.0f - SmoothStep(Progress) * 0.65f;
            Dressing.AirshipAttitude = FRotator(
                FMath::Sin(UnscaledTime * 1.9f) * 16.0f * Tumble,
                FMath::Sin(UnscaledTime * 1.3f) * 22.0f * Tumble,
                FMath::Sin(UnscaledTime * 2.3f) * 19.0f * Tumble);
            Dressing.CockpitFillIntensity = 0.18f;
            Dressing.AlertIntensity = 4.5f * FMath::Pow(1.0f - Beat(Elapsed, 0.9f), 2.6f);
            break;
        }

        case ECMLIntroShot::Crash:
        {
            const float Progress = Progress01(Elapsed, Timings.CrashSeconds);
            // The hit is at the front of the shot and everything after it is
            // the ship coming to rest, so the shake decays rather than building.
            Dressing.ShakeAmount = FMath::Lerp(1.0f, 0.0f, FMath::Pow(Progress, 0.55f));
            Dressing.AirshipAttitude = FRotator(
                FMath::Sin(UnscaledTime * 30.0f) * 6.0f * (1.0f - Progress),
                0.0f,
                FMath::Lerp(0.0f, 12.0f, SmoothStep(Progress)));
            Dressing.FlashAlpha = FMath::Max(0.0f, 1.0f - Elapsed * 3.0f);
            Dressing.CockpitFillIntensity = 0.10f;
            break;
        }

        case ECMLIntroShot::Blackout:
            // Full black, and nothing moving. The handover happens behind this.
            Dressing.FadeAlpha = 1.0f;
            Dressing.Eyelid = 1.0f;
            break;

        case ECMLIntroShot::Wake:
        {
            const float Progress = Progress01(Elapsed, Timings.WakeSeconds);
            // Two half-blinks before the eyes stay open: one long closing, and
            // one short flutter partway through.
            const float Blink =
                FMath::Clamp(1.0f - Progress * 2.2f, 0.0f, 1.0f)
                + FMath::Max(0.0f, 0.62f - FMath::Abs(Progress - 0.38f) * 5.0f);
            Dressing.FadeAlpha = FMath::Clamp(Blink, 0.0f, 1.0f) * 0.94f;
            Dressing.Eyelid = FMath::Clamp(
                FMath::Max(Blink, 1.0f - SmoothStep(Progress * 1.15f)), 0.0f, 1.0f);
            break;
        }

        default:
            break;
    }

    return Dressing;
}

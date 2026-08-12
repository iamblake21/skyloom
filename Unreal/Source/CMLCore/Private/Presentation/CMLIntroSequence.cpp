#include "Presentation/CMLIntroSequence.h"

namespace
{
    ECMLIntroShot NextShot(const ECMLIntroShot Shot)
    {
        return Shot == ECMLIntroShot::Complete
            ? ECMLIntroShot::Complete
            : static_cast<ECMLIntroShot>(static_cast<uint8>(Shot) + 1);
    }

    /** Whether a step is one the player has to satisfy rather than sit through. */
    bool IsTaughtStep(const ECMLIntroFlightStep Step)
    {
        return Step == ECMLIntroFlightStep::TeachRight || Step == ECMLIntroFlightStep::TeachLeft;
    }
}

float FCMLIntroTimings::DurationOf(const ECMLIntroShot Shot) const
{
    switch (Shot)
    {
        case ECMLIntroShot::Hyperspace: return HyperspaceSeconds;
        case ECMLIntroShot::Cockpit:    return CockpitSeconds;
        case ECMLIntroShot::Alarm:      return AlarmSeconds;
        case ECMLIntroShot::RiftOpen:   return RiftOpenSeconds;
        case ECMLIntroShot::RiftEntry:  return RiftEntrySeconds;
        case ECMLIntroShot::Fall:       return FallSeconds;
        case ECMLIntroShot::Crash:      return CrashSeconds;
        case ECMLIntroShot::Blackout:   return BlackoutSeconds;
        case ECMLIntroShot::Wake:       return WakeSeconds;
        // Flight has no duration: it ends when the player has flown, not when a
        // clock runs out. Complete has none because it is the end.
        default: return 0.0f;
    }
}

bool FCMLIntroSequence::ShouldShowTutorialCard(const FCMLIntroState& State)
{
    // Shown while the player is being asked, and while they are being asked to
    // come back — not during the settle, which is the airship steadying itself.
    if (State.Shot != ECMLIntroShot::Flight)
    {
        return false;
    }
    switch (State.FlightStep)
    {
        case ECMLIntroFlightStep::TeachRight:
        case ECMLIntroFlightStep::TeachLeft:
            return true;
        default:
            return false;
    }
}

float FCMLIntroSequence::TutorialDirection(const FCMLIntroState& State)
{
    if (!ShouldShowTutorialCard(State))
    {
        return 0.0f;
    }
    return State.FlightStep == ECMLIntroFlightStep::TeachRight ? 1.0f : -1.0f;
}

bool FCMLIntroSequence::AdvanceFlight(
    FCMLIntroState& State,
    const FCMLIntroTimings& Timings,
    const FCMLIntroInput& Input,
    const float DeltaSeconds)
{
    State.ElapsedInStep += DeltaSeconds;

    auto ToStep = [&State](const ECMLIntroFlightStep Step)
    {
        State.FlightStep = Step;
        State.ElapsedInStep = 0.0f;
        State.HeldSeconds = 0.0f;
    };

    switch (State.FlightStep)
    {
        case ECMLIntroFlightStep::Settle:
            // The airship steadies itself before anything is asked of the
            // player: a lesson that began mid-tumble would read as a failure.
            if (State.ElapsedInStep >= Timings.FlightSettleSeconds)
            {
                ToStep(ECMLIntroFlightStep::ApproachRight);
            }
            return false;

        case ECMLIntroFlightStep::ApproachRight:
            if (State.ElapsedInStep >= Timings.FlightApproachSeconds)
            {
                ToStep(ECMLIntroFlightStep::TeachRight);
            }
            return false;

        case ECMLIntroFlightStep::TeachRight:
        case ECMLIntroFlightStep::TeachLeft:
        {
            const float Wanted = State.FlightStep == ECMLIntroFlightStep::TeachRight
                ? Timings.TutorialTurnDegrees
                : -Timings.TutorialTurnDegrees;
            const bool bFarEnough = Wanted >= 0.0f
                ? Input.YawDegrees >= Wanted
                : Input.YawDegrees <= Wanted;

            // Held, not merely touched. Resetting on release is what stops a
            // flick across the threshold from counting as having learned it.
            State.HeldSeconds = bFarEnough ? State.HeldSeconds + DeltaSeconds : 0.0f;
            if (State.HeldSeconds >= Timings.TutorialHoldSeconds)
            {
                ToStep(State.FlightStep == ECMLIntroFlightStep::TeachRight
                    ? ECMLIntroFlightStep::RecoverRight
                    : ECMLIntroFlightStep::RecoverLeft);
            }
            return false;
        }

        case ECMLIntroFlightStep::RecoverRight:
            if (State.ElapsedInStep >= Timings.FlightRecoverSeconds)
            {
                ToStep(ECMLIntroFlightStep::ApproachLeft);
            }
            return false;

        case ECMLIntroFlightStep::ApproachLeft:
            if (State.ElapsedInStep >= Timings.FlightApproachSeconds)
            {
                ToStep(ECMLIntroFlightStep::TeachLeft);
            }
            return false;

        case ECMLIntroFlightStep::RecoverLeft:
            if (State.ElapsedInStep >= Timings.FlightRecoverSeconds)
            {
                ToStep(ECMLIntroFlightStep::Handover);
            }
            return false;

        default:
            // Handover: the lesson is passed and the story resumes.
            return true;
    }
}

bool FCMLIntroSequence::Advance(
    FCMLIntroState& State,
    const FCMLIntroTimings& Timings,
    const FCMLIntroInput& Input,
    const float DeltaSeconds,
    const bool bAllowSkip)
{
    if (State.IsComplete())
    {
        return false;
    }

    // Skipping jumps to the end outright rather than fast-forwarding: replaying
    // eleven shots at speed is not what someone asking to skip wants.
    if (bAllowSkip && Input.bSkipRequested)
    {
        State.Shot = ECMLIntroShot::Complete;
        State.FlightStep = ECMLIntroFlightStep::Handover;
        State.ElapsedInShot = 0.0f;
        State.ElapsedInStep = 0.0f;
        return true;
    }

    if (State.Shot == ECMLIntroShot::Flight)
    {
        if (!AdvanceFlight(State, Timings, Input, DeltaSeconds))
        {
            State.ElapsedInShot += DeltaSeconds;
            return false;
        }
        State.Shot = NextShot(State.Shot);
        State.ElapsedInShot = 0.0f;
        return false;
    }

    State.ElapsedInShot += DeltaSeconds;
    if (State.ElapsedInShot < Timings.DurationOf(State.Shot))
    {
        return false;
    }

    // One shot per frame at most. Carrying the overshoot into the next shot
    // would let a single long frame skip a whole beat of the opening.
    State.ElapsedInShot = 0.0f;
    State.Shot = NextShot(State.Shot);
    if (State.Shot == ECMLIntroShot::Flight)
    {
        State.FlightStep = ECMLIntroFlightStep::Settle;
        State.ElapsedInStep = 0.0f;
        State.HeldSeconds = 0.0f;
    }
    return State.IsComplete();
}

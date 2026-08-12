#include "Presentation/CMLIntroThreat.h"

float FCMLIntroThreat::MeasureClearance(const float RockHalfExtent, const float HullHalfExtent)
{
    // Derived from the real sizes rather than tuned: a rock wider than its own
    // miss distance would pass straight through the hull no matter how well the
    // lesson was flown, and no amount of tuning a constant would reveal that.
    return FMath::Max(0.0f, RockHalfExtent)
        + FMath::Max(0.0f, HullHalfExtent)
        + MissMargin;
}

FCMLIntroThreatState FCMLIntroThreat::Launch(const float Direction, const float Clearance)
{
    FCMLIntroThreatState State;
    State.Direction = Direction >= 0.0f ? 1.0f : -1.0f;
    State.Distance = LaunchDistance;
    State.Lateral = 0.0f;
    State.Cleared = 0.0f;
    State.Clearance = FMath::Max(0.0f, Clearance);
    State.bActive = true;
    return State;
}

void FCMLIntroThreat::Advance(
    FCMLIntroThreatState& State,
    const float LessonYawDegrees,
    const float RequiredYawDegrees,
    const float DeltaSeconds)
{
    if (!State.bActive)
    {
        return;
    }

    // Once the lesson is passed the rock slows, so the pass is something the
    // player watches rather than something over before they register it.
    const float Speed = FMath::Lerp(ApproachSpeed, PassSpeed, State.Cleared);
    State.Distance -= DeltaSeconds * Speed;

    // How much of the clearance this turn has earned. Signed by the direction
    // asked for, so turning the wrong way earns nothing rather than counting.
    const float Earned = FMath::Clamp(
        LessonYawDegrees * State.Direction / FMath::Max(RequiredYawDegrees, 1.0f),
        0.0f, 1.0f);
    // Never gives ground: once a turn has bought clearance, easing off the
    // stick must not slide the rock back into the hull.
    State.Cleared = FMath::Max(State.Cleared, Earned);
    State.Lateral = -State.Direction * State.Clearance * State.Cleared;

    if (State.Distance < DespawnDistance)
    {
        State.bActive = false;
    }
}

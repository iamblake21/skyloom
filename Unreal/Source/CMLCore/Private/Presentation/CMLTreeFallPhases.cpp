#include "Presentation/CMLTreeFallPhases.h"

bool FCMLTreeFallPhases::ShouldReleaseHinge(
    const FCMLTreeFallReading& Reading, const float ReleaseAngleDegrees)
{
    // The beyond-balance margin is owned by the felling geometry. Adding a
    // second one here would hold the base unnaturally long and then drop the
    // tree visibly when the joint finally goes.
    return Reading.FallAngleDegrees >= ReleaseAngleDegrees;
}

bool FCMLTreeFallPhases::QualifiesAsRebound(
    const FCMLTreeFallReading& Reading,
    const float ImpactSpeed,
    const float ContactFraction)
{
    // All three, not any: a slow touch, a shallow angle or a contact near the
    // stump each mean this is the tree settling rather than striking. A tree
    // that bounced on its base would read as rubber.
    return ImpactSpeed >= MinimumImpactSpeed
        && Reading.FallAngleDegrees >= MinimumImpactAngleDegrees
        && ContactFraction >= MinimumDistalContactFraction;
}

bool FCMLTreeFallPhases::ShouldSettle(const FCMLTreeFallReading& Reading)
{
    if (Reading.ElapsedInPhaseSeconds < MinimumReboundSeconds)
    {
        // Too soon to call it: the tree has not left the ground yet.
        return false;
    }
    // Either it came back down after leaving the ground, or the bounce simply
    // ran out of time while still touching. The second is the safety net: a
    // bounce that never separates must not leave the tree in mid-phase forever.
    const bool bReturnedAfterSeparation = Reading.bSeparatedAfterImpact && Reading.bHasGroundSupport;
    const bool bGroundedFallback =
        Reading.ElapsedInPhaseSeconds >= MaximumReboundSeconds && Reading.bHasGroundSupport;
    return bReturnedAfterSeparation || bGroundedFallback;
}

bool FCMLTreeFallPhases::IsAtRest(
    const FCMLTreeFallReading& Reading, const float DeltaSeconds, float& InOutQuietTime)
{
    const bool bQuiet = Reading.AngularSpeed <= QuietAngularSpeed
        && Reading.LinearSpeed <= QuietLinearSpeed;
    // Reset rather than decay: one quiet frame in the middle of a tumble is not
    // a tree at rest, and the timer has to start again from nothing.
    InOutQuietTime = bQuiet ? InOutQuietTime + DeltaSeconds : 0.0f;
    return InOutQuietTime >= QuietTimeRequiredSeconds;
}

ECMLTreeFallPhase FCMLTreeFallPhases::Advance(
    const ECMLTreeFallPhase Current,
    const FCMLTreeFallReading& Reading,
    const float ReleaseAngleDegrees,
    const float DeltaSeconds,
    float& InOutQuietTime)
{
    switch (Current)
    {
        case ECMLTreeFallPhase::SupportedRelease:
            return ShouldReleaseHinge(Reading, ReleaseAngleDegrees)
                ? ECMLTreeFallPhase::JointReleasePending
                : Current;

        case ECMLTreeFallPhase::JointReleasePending:
            // A step of its own on purpose: the engine defers destroying the
            // joint, and a still-alive joint fighting a restored ground contact
            // for one step is exactly the visible hitch at release.
            return Reading.bHingeReleased ? ECMLTreeFallPhase::FreeFall : Current;

        case ECMLTreeFallPhase::FreeFall:
            // The impact itself promotes this to Rebound; free fall does not end
            // on its own, because a tree that never strikes anything simply
            // keeps falling until it does.
            return Current;

        case ECMLTreeFallPhase::Rebound:
            return ShouldSettle(Reading) ? ECMLTreeFallPhase::Settlement : Current;

        case ECMLTreeFallPhase::Settlement:
            return IsAtRest(Reading, DeltaSeconds, InOutQuietTime)
                ? ECMLTreeFallPhase::Complete
                : Current;

        default:
            return ECMLTreeFallPhase::Complete;
    }
}

#pragma once

#include "CoreMinimal.h"

#include "CMLTreeFallPhases.generated.h"

UENUM(BlueprintType)
enum class ECMLTreeFallPhase : uint8
{
    /** Still hinged at the stump, leaning past balance under its own weight. */
    SupportedRelease = 0,
    /** The hinge has been asked to go but has not gone yet. */
    JointReleasePending = 1,
    FreeFall = 2,
    /** The one real bounce, after the crown hits. */
    Rebound = 3,
    Settlement = 4,
    Complete = 5
};

/** What the physics body looks like this step, as the phase machine sees it. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLTreeFallReading
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") float ElapsedInPhaseSeconds = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") float FallAngleDegrees = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") float AngularSpeed = 0.0f;
    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") float LinearSpeed = 0.0f;

    /** True while something under the tree is holding it up. */
    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") bool bHasGroundSupport = false;

    /** True once it has left the ground again after its first impact. */
    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") bool bSeparatedAfterImpact = false;

    /** True once the hinge has actually gone, not merely been asked to. */
    UPROPERTY(BlueprintReadWrite, Category="CML|Wood") bool bHingeReleased = false;
};

/**
 * The phases of a felled tree, ported from
 * CML.Unity.Wood.IntactTreeFallAnimator.
 *
 * A heavy tree falls in four visible acts and the animator makes each one
 * explicit: it leans off its stump, it falls, it bounces **once**, and it
 * settles. The physics belongs to the engine; what belongs here is the set of
 * decisions between the acts, which is where a fall stops looking heavy if it
 * is got wrong.
 *
 * Three of those decisions carry the weight:
 *
 *  - The bounce is qualified, not automatic. It needs a fast enough impact, a
 *    steep enough angle, and contact towards the *crown* rather than the stump.
 *    A tree that bounced on its base would read as rubber.
 *  - Releasing the hinge takes a step. Unity defers destruction, so the still-
 *    alive joint and a restored ground contact would fight for one step and
 *    produce a visible hitch at the moment of release.
 *  - Settling needs the tree to be quiet for a continuous stretch, not merely
 *    slow for an instant. One quiet frame mid-tumble is not a tree at rest.
 */
class CMLCORE_API FCMLTreeFallPhases
{
public:
    /** A bounce needs all three of these, not any one of them. */
    static constexpr float MinimumImpactSpeed = 45.0f;
    static constexpr float MinimumImpactAngleDegrees = 48.0f;
    /** How far along the trunk contact must be: nearer the crown than the base. */
    static constexpr float MinimumDistalContactFraction = 0.30f;

    static constexpr float MaximumReboundSeconds = 0.58f;
    static constexpr float MinimumReboundSeconds = 0.12f;

    /** Below these, and only while it stays below them, the tree is at rest. */
    static constexpr float QuietAngularSpeed = 0.060f;
    static constexpr float QuietLinearSpeed = 8.0f;
    static constexpr float QuietTimeRequiredSeconds = 0.60f;

    /** The lean past balance is owned by the felling geometry, not repeated here. */
    static bool ShouldReleaseHinge(const FCMLTreeFallReading& Reading, float ReleaseAngleDegrees);

    /**
     * Whether an impact earns the one bounce.
     *
     * `ContactFraction` is how far along the trunk the contact sits, from 0 at
     * the stump to 1 at the crown.
     */
    static bool QualifiesAsRebound(
        const FCMLTreeFallReading& Reading, float ImpactSpeed, float ContactFraction);

    /** Whether the bounce is over, either by landing again or by running out. */
    static bool ShouldSettle(const FCMLTreeFallReading& Reading);

    /** Advances the quiet timer, and says whether the tree has come to rest. */
    static bool IsAtRest(const FCMLTreeFallReading& Reading, float DeltaSeconds, float& InOutQuietTime);

    /** The next phase for a reading, or the current one when nothing changes. */
    static ECMLTreeFallPhase Advance(
        ECMLTreeFallPhase Current,
        const FCMLTreeFallReading& Reading,
        float ReleaseAngleDegrees,
        float DeltaSeconds,
        float& InOutQuietTime);
};

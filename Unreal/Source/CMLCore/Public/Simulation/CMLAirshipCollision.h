#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLAirshipState.h"

/**
 * Swept collision for one flight tick, ported from
 * CML.Simulation.Airship.AirshipCollision.
 *
 * A tick can move an airship by up to a metre, which is more than enough to
 * pass straight through a thin obstacle if only the endpoint is tested. The
 * candidate pose is therefore **swept**: the move is subdivided into as many
 * steps as its largest component has millimetres (or turn units), so the
 * sampling never skips over something an airship should have hit.
 *
 * That also bounds the cost deterministically, which is why a candidate larger
 * than any legal one-tick flight is rejected outright rather than swept with an
 * unbounded number of samples.
 */
class CMLCORE_API FCMLAirshipCollision
{
public:
    /** Beyond this, a candidate is not a legal one-tick flight at all. */
    static constexpr int64 MaximumLegalSweptStep = 2000;

    /** Signed shortest way round the turn circle, from one yaw to another. */
    static int32 ShortestTurnDelta(uint16 From, uint16 To);

    /** The airship's hull half-extents in its own frame. */
    static FCMLAirshipVector GetHullHalfExtents();

    /** Whether a pose's hull overlaps any obstacle. */
    static bool IntersectsAnyObstacle(
        const FCMLAirshipSimulationState& State,
        const FCMLAirshipPose& Pose);

    /**
     * Whether the whole move from `Current` to `Candidate` is clear.
     * Returns false when the candidate is not a legal one-tick step, since
     * sweeping an impossible leap would cost an unbounded number of samples.
     */
    static bool IsCandidateClear(
        const FCMLAirshipSimulationState& State,
        const FCMLAirshipPose& Current,
        const FCMLAirshipPose& Candidate);
};

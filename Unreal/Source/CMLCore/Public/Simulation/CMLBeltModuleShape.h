#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "Simulation/CMLMachineState.h"

/**
 * What a belt module does to the path running through it, ported from
 * CML.Simulation.Machines.BeltModuleShape.
 *
 * Before this existed the model assumed every belt was straight and level:
 * adjacency demanded the same axis and the same height between two modules.
 * That holds while only straight pieces exist, but a curve is *for* changing
 * axis and an incline is *for* changing height, so both fell outside the model
 * — they could be placed but fed nothing. That was not a wiring oversight; the
 * concept was missing.
 *
 * Each definition declares the only two things needed to close a path: how far
 * the exit turns relative to the entry, and how far it rises. Everything else in
 * the topology still derives from the pose.
 *
 * Yaw is in quarter turns, 0-3.
 */
class CMLCORE_API FCMLBeltModuleShape
{
public:
    /** The incline's rise, in millimetres. Half a belt floor. */
    static constexpr int32 InclineRiseMillimetres = 300;

    /**
     * Quarter turns between entry and exit, travelling the module forwards.
     * Zero for straight modules.
     *
     * The curve is 1: it turns right. That value was verified in game, not
     * deduced from the export — the FBX export flips an axis relative to
     * Blender, so reading the source coordinates gives the opposite hand.
     */
    static int32 TurnQuarterTurns(const FCMLStableId& DefinitionId);

    /** Where the geometric exit points when the module is travelled as modelled. */
    static uint8 ForwardExitYaw(const FCMLStableId& DefinitionId, uint8 PlacementYaw);

    /**
     * The real entry and exit of the motion.
     *
     * On a straight module, reverse is simply the opposite of the pose. On a
     * curve it is not: reverse motion enters through the lateral geometric exit
     * and leaves through the geometric entry.
     */
    static bool TryResolveTravelYaws(
        const FCMLStableId& DefinitionId,
        uint8 PlacementYaw,
        ECMLBeltTravelDirection Direction,
        uint8& OutEntryYaw,
        uint8& OutExitYaw);

    /** How far the running surface rises between entry and exit, travelled forwards. */
    static int32 RiseMillimetres(const FCMLStableId& DefinitionId);

    /**
     * The physical height of the end facing `SideYaw`, relative to the module's
     * root.
     *
     * This is not an "axis" test: a curve owns two half-lines, not two infinite
     * axes. Accepting the far side of one of its arms made a straight piece
     * latch onto the wrong half of the curve. On an incline, entry and exit sit
     * at different heights as well. This is therefore the single geometric
     * contract used by placement, simulation and power alike.
     */
    static bool TryGetEndpointHeightMillimetres(
        const FCMLStableId& DefinitionId,
        uint8 PlacementYaw,
        uint8 SideYaw,
        int32& OutHeightMillimetres);

    /**
     * The pose a module needs for its forward geometric exit to face a given
     * direction. Needed when adding a module upstream: for a straight piece it
     * equals the exit, for a curve it does not.
     */
    static uint8 PlacementYawForForwardExit(const FCMLStableId& DefinitionId, uint8 ExitYaw);

    /** True when the module changes neither axis nor height. */
    static bool IsStraightAndLevel(const FCMLStableId& DefinitionId);

    /** Travel yaw of a placed belt, or false when it is not a moving belt. */
    static bool TryGetBeltTravelYaw(const FCMLMachineNodeState& Belt, uint8& OutTravelYaw);

    /**
     * The direction a piece leaves the module by.
     *
     * On a straight run it matches the entry, which is why one notion of
     * "travel" sufficed for as long as only straight pieces existed. On a curve
     * it does not, and telling the two apart is what lets a path turn.
     */
    static bool TryGetBeltExitYaw(const FCMLMachineNodeState& Belt, uint8& OutExitYaw);

    /**
     * The height the module adds to the path, signed by travel: an incline run
     * backwards descends.
     */
    static int32 BeltRiseMillimetres(const FCMLMachineNodeState& Belt);
};

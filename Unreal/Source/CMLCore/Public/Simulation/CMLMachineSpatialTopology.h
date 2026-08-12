#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Simulation/CMLMachineState.h"

/**
 * Resolves the physical logistics graph from persistent node poses, ported from
 * CML.Simulation.Machines.MachineSpatialTopology.
 *
 * No edge is authored or stored. A belt or funnel works only while the required
 * neighbours occupy the exact adjacent cells with compatible facings. Removing
 * any physical module therefore disconnects the line on the next authoritative
 * tick, without leaving a stale logical connection behind.
 *
 * The five steps run in a fixed order, and the order is the behaviour: funnels
 * pull, belts advance, belts deliver, belts load, funnels push.
 */
class CMLCORE_API FCMLMachineSpatialTopology
{
public:
    static constexpr int32 GridCellSizeMillimetres = 1000;
    static constexpr int32 BeltLengthMillimetres = GridCellSizeMillimetres;
    static constexpr int32 BeltSpeedMillimetresPerTick = 100;

    /** One authoritative tick of the physical logistics graph. */
    static void Advance(FCMLMachineSimulationState& State, const FCMLGameCatalog& Catalog);

    /**
     * Whether a machine would accept one more of an item on its belt-fed input.
     *
     * Stricter than the transfer rule on purpose: a belt keeps pushing, so a
     * machine that is mid-cycle or still holding its last output must stop
     * taking delivery or it would silently overfill.
     */
    static bool MachineAdmits(
        const FCMLMachineNodeState& Machine,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog);
};

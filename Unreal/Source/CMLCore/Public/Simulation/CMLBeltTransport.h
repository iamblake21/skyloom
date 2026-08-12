#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Simulation/CMLMachineState.h"

/**
 * Belt lane transport, ported from CML.Simulation.Machines.MachineReducer.
 *
 * Items ride a lane at a position in millimetres and are delivered when they
 * reach its end. Two rules together produce backpressure without anyone
 * modelling it explicitly:
 *
 *  - every item moves forward, **front first**, and never passes the one ahead
 *    of it closer than the spacing;
 *  - an item the destination refuses stays at the exit.
 *
 * The result is a queue that forms from the exit backwards, which is what a
 * real belt does when the machine at its end stops taking.
 */
class CMLCORE_API FCMLBeltTransport
{
public:
    /** Moves every item forward by one tick, honouring spacing and the lane end. */
    static void AdvanceLaneItems(FCMLBeltLaneState& Lane);

    /**
     * Delivers items that have reached the lane end into the destination's
     * input port, stopping at the first one the destination will not take.
     * Returns how many were delivered.
     */
    static int32 DeliverLaneItems(
        FCMLBeltLaneState& Lane,
        FCMLMachineNodeState& Destination,
        const FCMLGameCatalog& Catalog);
};

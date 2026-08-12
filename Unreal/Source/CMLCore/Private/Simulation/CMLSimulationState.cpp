#include "Simulation/CMLSimulationState.h"

#include "Simulation/CMLCanonicalStateSerializer.h"

void FCMLSimulationState::SortForCanonicalEncoding()
{
    FCMLCanonicalStateSerializer::SortQuantities(Quantities);
    FCMLCanonicalStateSerializer::SortAccumulators(Accumulators);
    Airship.Sort();
    Machines.Sort();
    Inventories.Sort();
}

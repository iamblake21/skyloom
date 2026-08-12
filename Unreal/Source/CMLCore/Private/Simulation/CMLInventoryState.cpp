#include "Simulation/CMLInventoryState.h"

void FCMLInventorySimulationState::Sort()
{
    Inventories.Sort([](const FCMLInventoryState& A, const FCMLInventoryState& B)
    {
        return A.InventoryId < B.InventoryId;
    });
}

bool FCMLInventorySimulationState::HasUniqueIds() const
{
    TSet<FCMLStableId> Seen;
    Seen.Reserve(Inventories.Num());
    for (const FCMLInventoryState& Inventory : Inventories)
    {
        bool bAlreadyPresent = false;
        Seen.Add(Inventory.InventoryId, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            return false;
        }
    }
    return true;
}

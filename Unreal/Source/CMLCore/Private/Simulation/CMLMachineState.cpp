#include "Simulation/CMLMachineState.h"

void FCMLMachineSimulationState::Sort()
{
    Nodes.Sort([](const FCMLMachineNodeState& A, const FCMLMachineNodeState& B)
    {
        return A.Id < B.Id;
    });
    Lanes.Sort([](const FCMLBeltLaneState& A, const FCMLBeltLaneState& B)
    {
        return A.Id < B.Id;
    });
}

bool FCMLMachineSimulationState::HasUniqueIds() const
{
    TSet<FCMLStableId> SeenNodes;
    SeenNodes.Reserve(Nodes.Num());
    for (const FCMLMachineNodeState& Node : Nodes)
    {
        bool bAlreadyPresent = false;
        SeenNodes.Add(Node.Id, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            return false;
        }
    }

    TSet<FCMLStableId> SeenLanes;
    SeenLanes.Reserve(Lanes.Num());
    for (const FCMLBeltLaneState& Lane : Lanes)
    {
        bool bAlreadyPresent = false;
        SeenLanes.Add(Lane.Id, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            return false;
        }
    }
    return true;
}

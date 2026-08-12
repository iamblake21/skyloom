#include "Simulation/CMLBeltLineRules.h"

#include "Content/CMLContentIds.h"
#include "Simulation/CMLBeltModuleShape.h"
#include "Simulation/CMLMachineSpatialTopology.h"

namespace
{
    struct FEndpoint
    {
        uint8 SideYaw = 0;
        int32 HeightMillimetres = 0;
    };

    struct FElement
    {
        int32 NodeIndex = INDEX_NONE;
        FEndpoint Endpoints[2];
    };

    struct FConnection
    {
        int32 Neighbour = INDEX_NONE;
        uint8 CurrentEndpoint = 0;
        uint8 NeighbourEndpoint = 0;
    };

    uint8 OppositeYaw(const uint8 Yaw)
    {
        return static_cast<uint8>((Yaw + 2) & 3);
    }

    void Offset(const uint8 Yaw, int64& X, int64& Z)
    {
        switch (Yaw & 3)
        {
        case 0: Z += FCMLMachineSpatialTopology::GridCellSizeMillimetres; break;
        case 1: X += FCMLMachineSpatialTopology::GridCellSizeMillimetres; break;
        case 2: Z -= FCMLMachineSpatialTopology::GridCellSizeMillimetres; break;
        default: X -= FCMLMachineSpatialTopology::GridCellSizeMillimetres; break;
        }
    }

    bool TryCreateElement(
        const FCMLMachineNodeState& Node,
        const int32 NodeIndex,
        FElement& OutElement)
    {
        if (!Node.bHasPlacementPose || Node.PlacementPose.YawQuarterTurns < 0
            || Node.PlacementPose.YawQuarterTurns > 3)
        {
            return false;
        }
        const uint8 Yaw = static_cast<uint8>(Node.PlacementPose.YawQuarterTurns);
        if (Node.Kind == ECMLMachineNodeKind::BeltModule)
        {
            OutElement.NodeIndex = NodeIndex;
            OutElement.Endpoints[0] = {OppositeYaw(Yaw), 0};
            OutElement.Endpoints[1] = {
                FCMLBeltModuleShape::ForwardExitYaw(Node.DefinitionId, Yaw),
                FCMLBeltModuleShape::RiseMillimetres(Node.DefinitionId)};
            return true;
        }
        if (Node.Kind == ECMLMachineNodeKind::Machine
            && Node.DefinitionId == CMLContentIds::MechanicalPress)
        {
            OutElement.NodeIndex = NodeIndex;
            OutElement.Endpoints[0] = {OppositeYaw(Yaw), 0};
            OutElement.Endpoints[1] = {Yaw, 0};
            return true;
        }
        return false;
    }

    bool FacesNode(
        const FCMLMachineNodeState& Origin,
        const FEndpoint& Endpoint,
        const FCMLMachineNodeState& Target)
    {
        int64 X = Origin.PlacementPose.XMillimetres;
        int64 Z = Origin.PlacementPose.ZMillimetres;
        Offset(Endpoint.SideYaw, X, Z);
        return X == Target.PlacementPose.XMillimetres
            && Z == Target.PlacementPose.ZMillimetres;
    }

    bool TryConnect(
        const FCMLMachineSimulationState& State,
        const FElement& Left,
        const FElement& Right,
        uint8& OutLeftEndpoint,
        uint8& OutRightEndpoint)
    {
        const FCMLMachineNodeState& LeftNode = State.Nodes[Left.NodeIndex];
        const FCMLMachineNodeState& RightNode = State.Nodes[Right.NodeIndex];
        for (uint8 LeftIndex = 0; LeftIndex < 2; ++LeftIndex)
        {
            for (uint8 RightIndex = 0; RightIndex < 2; ++RightIndex)
            {
                const FEndpoint& LeftPort = Left.Endpoints[LeftIndex];
                const FEndpoint& RightPort = Right.Endpoints[RightIndex];
                if (OppositeYaw(LeftPort.SideYaw) != RightPort.SideYaw
                    || !FacesNode(LeftNode, LeftPort, RightNode)
                    || !FacesNode(RightNode, RightPort, LeftNode)
                    || LeftNode.PlacementPose.YMillimetres + LeftPort.HeightMillimetres
                        != RightNode.PlacementPose.YMillimetres + RightPort.HeightMillimetres)
                {
                    continue;
                }
                OutLeftEndpoint = LeftIndex;
                OutRightEndpoint = RightIndex;
                return true;
            }
        }
        return false;
    }

    bool AssignDirection(
        const int32 NodeIndex,
        const ECMLBeltTravelDirection Direction,
        TMap<int32, ECMLBeltTravelDirection>& Directions,
        TArray<int32>& Queue)
    {
        if (const ECMLBeltTravelDirection* Existing = Directions.Find(NodeIndex))
        {
            return *Existing == Direction;
        }
        Directions.Add(NodeIndex, Direction);
        Queue.Add(NodeIndex);
        return true;
    }
}

void FCMLBeltLineRules::Recompute(FCMLMachineSimulationState& State)
{
    TArray<FElement> Elements;
    TMap<int32, int32> ElementByNode;
    TArray<TArray<FConnection>> Adjacency;
    Adjacency.SetNum(State.Nodes.Num());

    for (int32 NodeIndex = 0; NodeIndex < State.Nodes.Num(); ++NodeIndex)
    {
        FCMLMachineNodeState& Node = State.Nodes[NodeIndex];
        Node.BeltLineStatus = ECMLBeltLineStatus::NotApplicable;
        Node.BeltLineUsedCapacity = 0;
        Node.BeltLineAvailableCapacity = 0;
        if (Node.Kind == ECMLMachineNodeKind::BeltModule)
        {
            Node.BeltTravelDirection = ECMLBeltTravelDirection::Stopped;
            Node.BeltLineStatus = ECMLBeltLineStatus::MissingDrive;
        }
        FElement Element;
        if (TryCreateElement(Node, NodeIndex, Element))
        {
            ElementByNode.Add(NodeIndex, Elements.Num());
            Elements.Add(Element);
        }
    }

    for (int32 Left = 0; Left < Elements.Num(); ++Left)
    {
        for (int32 Right = Left + 1; Right < Elements.Num(); ++Right)
        {
            uint8 LeftEndpoint = 0;
            uint8 RightEndpoint = 0;
            if (!TryConnect(State, Elements[Left], Elements[Right], LeftEndpoint, RightEndpoint))
            {
                continue;
            }
            const int32 LeftNode = Elements[Left].NodeIndex;
            const int32 RightNode = Elements[Right].NodeIndex;
            Adjacency[LeftNode].Add({RightNode, LeftEndpoint, RightEndpoint});
            Adjacency[RightNode].Add({LeftNode, RightEndpoint, LeftEndpoint});
        }
    }

    TSet<int32> Visited;
    for (const FElement& Start : Elements)
    {
        if (Visited.Contains(Start.NodeIndex))
        {
            continue;
        }
        TArray<int32> Component;
        TArray<int32> ComponentQueue{Start.NodeIndex};
        Visited.Add(Start.NodeIndex);
        for (int32 Cursor = 0; Cursor < ComponentQueue.Num(); ++Cursor)
        {
            const int32 Current = ComponentQueue[Cursor];
            Component.Add(Current);
            Adjacency[Current].Sort([&State](const FConnection& A, const FConnection& B)
            {
                return State.Nodes[A.Neighbour].Id < State.Nodes[B.Neighbour].Id;
            });
            for (const FConnection& Connection : Adjacency[Current])
            {
                if (!Visited.Contains(Connection.Neighbour))
                {
                    Visited.Add(Connection.Neighbour);
                    ComponentQueue.Add(Connection.Neighbour);
                }
            }
        }

        int32 Drives = 0;
        int32 UsedCapacity = 0;
        bool bConflict = false;
        TMap<int32, ECMLBeltTravelDirection> Directions;
        TArray<int32> DirectionQueue;
        for (const int32 NodeIndex : Component)
        {
            const FCMLMachineNodeState& Node = State.Nodes[NodeIndex];
            const bool bDrive = Node.Kind == ECMLMachineNodeKind::BeltModule
                && Node.DefinitionId == CMLContentIds::BeltDriveUnit;
            if (bDrive)
            {
                ++Drives;
                bConflict |= !AssignDirection(
                    NodeIndex, ECMLBeltTravelDirection::Forward, Directions, DirectionQueue);
            }
            else
            {
                ++UsedCapacity;
                if (Node.Kind == ECMLMachineNodeKind::Machine)
                {
                    bConflict |= !AssignDirection(
                        NodeIndex, ECMLBeltTravelDirection::Forward, Directions, DirectionQueue);
                }
            }
        }

        for (int32 Cursor = 0; Cursor < DirectionQueue.Num(); ++Cursor)
        {
            const int32 Current = DirectionQueue[Cursor];
            const ECMLBeltTravelDirection CurrentDirection = Directions[Current];
            for (const FConnection& Connection : Adjacency[Current])
            {
                const uint8 CurrentExit = CurrentDirection == ECMLBeltTravelDirection::Forward ? 1 : 0;
                const bool bNeighbourMustEnter = Connection.CurrentEndpoint == CurrentExit;
                const uint8 RequiredNeighbourEndpoint = bNeighbourMustEnter ? 0 : 1;
                const ECMLBeltTravelDirection NeighbourDirection =
                    Connection.NeighbourEndpoint == RequiredNeighbourEndpoint
                        ? ECMLBeltTravelDirection::Forward
                        : ECMLBeltTravelDirection::Reverse;
                bConflict |= !AssignDirection(
                    Connection.Neighbour, NeighbourDirection, Directions, DirectionQueue);
            }
        }

        const int32 AvailableCapacity = Drives * CapacityPerDrive;
        const ECMLBeltLineStatus Status = Drives == 0
            ? ECMLBeltLineStatus::MissingDrive
            : bConflict
                ? ECMLBeltLineStatus::DirectionConflict
                : UsedCapacity > AvailableCapacity
                    ? ECMLBeltLineStatus::Overloaded
                    : ECMLBeltLineStatus::Operational;
        for (const int32 NodeIndex : Component)
        {
            FCMLMachineNodeState& Node = State.Nodes[NodeIndex];
            Node.BeltLineStatus = Status;
            Node.BeltLineUsedCapacity = UsedCapacity;
            Node.BeltLineAvailableCapacity = AvailableCapacity;
            if (Node.Kind == ECMLMachineNodeKind::BeltModule
                && Status == ECMLBeltLineStatus::Operational)
            {
                if (const ECMLBeltTravelDirection* Direction = Directions.Find(NodeIndex))
                {
                    Node.BeltTravelDirection = *Direction;
                }
            }
        }
    }
}

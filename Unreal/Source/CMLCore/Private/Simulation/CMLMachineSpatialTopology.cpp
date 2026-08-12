#include "Simulation/CMLMachineSpatialTopology.h"

#include "Content/CMLContentIds.h"
#include "Simulation/CMLBeltModuleShape.h"
#include "Simulation/CMLTransferRule.h"

namespace
{
    using Topology = FCMLMachineSpatialTopology;
    using Shape = FCMLBeltModuleShape;
    using Ports = FCMLMachinePortOperations;

    enum class EFunnelDirection : uint8
    {
        None,
        Extract,
        Insert
    };

    uint8 OppositeYaw(const uint8 Yaw)
    {
        return static_cast<uint8>((Yaw + 2) & 3);
    }

    bool IsPortEmpty(const FCMLMachinePort& Port)
    {
        return Ports::TotalQuantity(Port) == 0;
    }

    /**
     * The item in the lowest occupied slot. Links without a filter use this, so
     * their choice depends on slot order alone and never on iteration luck.
     */
    bool TryPeekFirstItem(const FCMLMachinePort& Port, FCMLStableId& OutItemId)
    {
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (!Slot.ItemId.IsNone() && Slot.Quantity.Value > 0)
            {
                OutItemId = Slot.ItemId;
                return true;
            }
        }
        return false;
    }

    /**
     * A buffer's input and output were one object in C#. Here they are two
     * fields, so the copy has to be kept in step or a crate would show stale
     * contents through its other face.
     */
    void SyncAlias(FCMLMachineNodeState& Node)
    {
        if (Node.bInputOutputAliased)
        {
            Node.Output = Node.Input;
        }
    }

    /** The port a buffer answers with: its single store, held in Input. */
    FCMLMachinePort& EffectivePort(FCMLMachineNodeState& Node, FCMLMachinePort& Requested)
    {
        return Node.bInputOutputAliased ? Node.Input : Requested;
    }

    /**
     * Moves exactly one unit. C# threw when the apply half contradicted the
     * preflight; here the move is simply refused, which leaves the graph
     * untouched instead of aborting the tick.
     */
    bool MoveOne(
        FCMLMachineNodeState& SourceNode,
        FCMLMachinePort& SourcePort,
        FCMLMachineNodeState& DestinationNode,
        FCMLMachinePort& DestinationPort,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog)
    {
        FCMLMachinePort& From = EffectivePort(SourceNode, SourcePort);
        FCMLMachinePort& To = EffectivePort(DestinationNode, DestinationPort);
        if (ItemId.IsNone()
            || Ports::Count(From, ItemId) < 1
            || Ports::StorableQuantity(To, ItemId, Catalog) < 1)
        {
            return false;
        }
        if (!Ports::TryStore(To, ItemId, 1, Catalog))
        {
            return false;
        }
        if (!Ports::TryTake(From, ItemId, 1))
        {
            // Put it back rather than conjuring a unit from nothing.
            Ports::TryTake(To, ItemId, 1);
            return false;
        }
        SyncAlias(SourceNode);
        SyncAlias(DestinationNode);
        return true;
    }

    /** Locates nodes by the cell they occupy and by their physical endpoints. */
    class FSpatialNodeIndex
    {
    public:
        explicit FSpatialNodeIndex(FCMLMachineSimulationState& InState) : State(InState)
        {
            for (int32 Index = 0; Index < State.Nodes.Num(); ++Index)
            {
                const FCMLMachineNodeState& Node = State.Nodes[Index];
                if (!Node.bHasPlacementPose)
                {
                    continue;
                }
                ByCell.Add(
                    FIntVector(
                        static_cast<int32>(Node.PlacementPose.XMillimetres),
                        static_cast<int32>(Node.PlacementPose.YMillimetres),
                        static_cast<int32>(Node.PlacementPose.ZMillimetres)),
                    Index);
            }
        }

        bool TryGetFront(const int32 NodeIndex, int32& OutNeighbour) const
        {
            return TryGetOffset(NodeIndex, 1, OutNeighbour);
        }

        bool TryGetBack(const int32 NodeIndex, int32& OutNeighbour) const
        {
            return TryGetOffset(NodeIndex, -1, OutNeighbour);
        }

        /** Out through the exit side and at the exit height. */
        bool TryGetTravelFront(const int32 BeltIndex, int32& OutNeighbour) const
        {
            uint8 ExitYaw = 0;
            if (!Shape::TryGetBeltExitYaw(State.Nodes[BeltIndex], ExitYaw))
            {
                return false;
            }
            return TryGetConnectedOffset(BeltIndex, ExitYaw, OutNeighbour);
        }

        /**
         * Upstream is the physical side opposite the travel. Its height may be
         * the raised end of an incline run backwards, so it cannot be deduced
         * from the sign alone.
         */
        bool TryGetTravelBack(const int32 BeltIndex, int32& OutNeighbour) const
        {
            uint8 TravelYaw = 0;
            if (!Shape::TryGetBeltTravelYaw(State.Nodes[BeltIndex], TravelYaw))
            {
                return false;
            }
            return TryGetConnectedOffset(BeltIndex, OppositeYaw(TravelYaw), OutNeighbour);
        }

    private:
        static bool TryStep(const uint8 SideYaw, int32& X, int32& Z)
        {
            switch (SideYaw & 3)
            {
                case 0: Z += Topology::GridCellSizeMillimetres; return true;
                case 1: X += Topology::GridCellSizeMillimetres; return true;
                case 2: Z -= Topology::GridCellSizeMillimetres; return true;
                default: X -= Topology::GridCellSizeMillimetres; return true;
            }
        }

        /**
         * Looks for the neighbour in the named cell and compares the height of
         * the two touching ends. Matching the root alone worked going up but
         * could not represent a rotated incline whose root sits below the module
         * upstream of it.
         */
        bool TryGetConnectedOffset(
            const int32 NodeIndex, const uint8 SideYaw, int32& OutNeighbour) const
        {
            OutNeighbour = INDEX_NONE;
            const FCMLMachineNodeState& Node = State.Nodes[NodeIndex];
            if (!Node.bHasPlacementPose)
            {
                return false;
            }

            int32 X = static_cast<int32>(Node.PlacementPose.XMillimetres);
            int32 Z = static_cast<int32>(Node.PlacementPose.ZMillimetres);
            TryStep(SideYaw, X, Z);

            int32 NodeEndpointHeight = 0;
            if (Node.Kind == ECMLMachineNodeKind::BeltModule
                && !Shape::TryGetEndpointHeightMillimetres(
                    Node.DefinitionId,
                    static_cast<uint8>(Node.PlacementPose.YawQuarterTurns),
                    SideYaw,
                    NodeEndpointHeight))
            {
                return false;
            }
            const int64 ContactHeight = Node.PlacementPose.YMillimetres + NodeEndpointHeight;

            for (int32 Candidate = 0; Candidate < State.Nodes.Num(); ++Candidate)
            {
                if (Candidate == NodeIndex)
                {
                    continue;
                }
                const FCMLMachineNodeState& Other = State.Nodes[Candidate];
                if (!Other.bHasPlacementPose
                    || static_cast<int32>(Other.PlacementPose.XMillimetres) != X
                    || static_cast<int32>(Other.PlacementPose.ZMillimetres) != Z)
                {
                    continue;
                }

                int32 OtherEndpointHeight = 0;
                if (Other.Kind == ECMLMachineNodeKind::BeltModule
                    && !Shape::TryGetEndpointHeightMillimetres(
                        Other.DefinitionId,
                        static_cast<uint8>(Other.PlacementPose.YawQuarterTurns),
                        OppositeYaw(SideYaw),
                        OtherEndpointHeight))
                {
                    continue;
                }
                if (Other.PlacementPose.YMillimetres + OtherEndpointHeight != ContactHeight)
                {
                    continue;
                }
                OutNeighbour = Candidate;
                return true;
            }
            return false;
        }

        bool TryGetOffset(const int32 NodeIndex, const int32 Sign, int32& OutNeighbour) const
        {
            OutNeighbour = INDEX_NONE;
            const FCMLMachineNodeState& Node = State.Nodes[NodeIndex];
            if (!Node.bHasPlacementPose)
            {
                return false;
            }
            const int32 Yaw = Node.PlacementPose.YawQuarterTurns;
            if (Yaw < 0 || Yaw > 3)
            {
                // C# threw here. Refusing instead leaves a badly placed node
                // simply unconnected rather than aborting the whole tick.
                return false;
            }

            int32 X = static_cast<int32>(Node.PlacementPose.XMillimetres);
            int32 Z = static_cast<int32>(Node.PlacementPose.ZMillimetres);
            // The signed step is the same walk taken forwards or backwards.
            switch (Yaw)
            {
                case 0: Z += Sign * Topology::GridCellSizeMillimetres; break;
                case 1: X += Sign * Topology::GridCellSizeMillimetres; break;
                case 2: Z -= Sign * Topology::GridCellSizeMillimetres; break;
                default: X -= Sign * Topology::GridCellSizeMillimetres; break;
            }

            const int32* Found = ByCell.Find(
                FIntVector(X, static_cast<int32>(Node.PlacementPose.YMillimetres), Z));
            if (Found == nullptr)
            {
                return false;
            }
            OutNeighbour = *Found;
            return true;
        }

        FCMLMachineSimulationState& State;
        TMap<FIntVector, int32> ByCell;
    };

    bool TravelFaces(const FCMLMachineNodeState& Belt, const uint8 Yaw)
    {
        uint8 TravelYaw = 0;
        return Shape::TryGetBeltTravelYaw(Belt, TravelYaw) && TravelYaw == Yaw;
    }

    /**
     * A piece passes when the upstream *exit* faces where the downstream *entry*
     * faces. Comparing the two travels instead amounted to demanding that
     * neither of them turned.
     */
    bool SameTravelDirection(
        const FCMLMachineNodeState& Left, const FCMLMachineNodeState& Right)
    {
        uint8 LeftExit = 0;
        uint8 RightEntry = 0;
        return Shape::TryGetBeltExitYaw(Left, LeftExit)
            && Shape::TryGetBeltTravelYaw(Right, RightEntry)
            && LeftExit == RightEntry;
    }

    /**
     * What may sit behind a funnel.
     *
     * Widened by machine, not to machines in general: a funnel physically clamps
     * into a recessed seat, and only a model that authors that seat can receive
     * it. The drill has one. The furnace is left out deliberately — its model
     * has to be revised first — so this is a content decision, not a technical
     * limit.
     */
    bool CanFunnelAttachTo(const FCMLMachineNodeState& Node)
    {
        if (Node.Kind == ECMLMachineNodeKind::Buffer)
        {
            return true;
        }
        return Node.Kind == ECMLMachineNodeKind::Machine
            && Node.DefinitionId == CMLContentIds::MechanicalDrill;
    }

    bool TryResolveFunnel(
        const FCMLMachineSimulationState& State,
        const FSpatialNodeIndex& Index,
        const int32 FunnelIndex,
        int32& OutAttached,
        int32& OutBelt,
        EFunnelDirection& OutDirection)
    {
        OutAttached = INDEX_NONE;
        OutBelt = INDEX_NONE;
        OutDirection = EFunnelDirection::None;

        const FCMLMachineNodeState& Funnel = State.Nodes[FunnelIndex];
        if (Funnel.Kind != ECMLMachineNodeKind::Funnel
            || !Funnel.bHasPlacementPose
            || !Funnel.AttachedNodeId.IsNone()
            || !Index.TryGetBack(FunnelIndex, OutAttached)
            || !CanFunnelAttachTo(State.Nodes[OutAttached])
            || !Index.TryGetFront(FunnelIndex, OutBelt)
            || State.Nodes[OutBelt].Kind != ECMLMachineNodeKind::BeltModule)
        {
            return false;
        }

        const uint8 FunnelYaw = static_cast<uint8>(Funnel.PlacementPose.YawQuarterTurns);
        if (TravelFaces(State.Nodes[OutBelt], FunnelYaw))
        {
            OutDirection = EFunnelDirection::Extract;
            return true;
        }
        if (TravelFaces(State.Nodes[OutBelt], OppositeYaw(FunnelYaw)))
        {
            OutDirection = EFunnelDirection::Insert;
            return true;
        }
        return false;
    }

    bool TryResolveBeltDestination(
        const FCMLMachineSimulationState& State,
        const FSpatialNodeIndex& Index,
        const int32 BeltIndex,
        int32& OutDestination)
    {
        OutDestination = INDEX_NONE;
        const FCMLMachineNodeState& Belt = State.Nodes[BeltIndex];
        if (Belt.Kind != ECMLMachineNodeKind::BeltModule
            || !Belt.bHasPlacementPose
            || Belt.BeltTravelDirection == ECMLBeltTravelDirection::Stopped
            || !Index.TryGetTravelFront(BeltIndex, OutDestination))
        {
            return false;
        }

        const FCMLMachineNodeState& Destination = State.Nodes[OutDestination];
        switch (Destination.Kind)
        {
            case ECMLMachineNodeKind::BeltModule:
                return SameTravelDirection(Belt, Destination);

            case ECMLMachineNodeKind::Machine:
                return TravelFaces(
                    Belt, static_cast<uint8>(Destination.PlacementPose.YawQuarterTurns));

            case ECMLMachineNodeKind::Funnel:
            {
                int32 Attached = INDEX_NONE;
                int32 ConnectedBelt = INDEX_NONE;
                EFunnelDirection Direction = EFunnelDirection::None;
                return TryResolveFunnel(
                        State, Index, OutDestination, Attached, ConnectedBelt, Direction)
                    && Direction == EFunnelDirection::Insert
                    && ConnectedBelt == BeltIndex;
            }

            default:
                // Buffer is deliberately absent: inventories are crossed only by
                // a physical funnel node.
                return false;
        }
    }

    /** Node order is the tie-break, so simultaneous moves resolve by StableId. */
    TArray<int32> CanonicalOrder(const FCMLMachineSimulationState& State)
    {
        TArray<int32> Order;
        Order.Reserve(State.Nodes.Num());
        for (int32 Index = 0; Index < State.Nodes.Num(); ++Index)
        {
            Order.Add(Index);
        }
        Order.Sort([&State](const int32 A, const int32 B)
        {
            return State.Nodes[A].Id < State.Nodes[B].Id;
        });
        return Order;
    }
}

bool FCMLMachineSpatialTopology::MachineAdmits(
    const FCMLMachineNodeState& Machine,
    const FCMLStableId& ItemId,
    const FCMLGameCatalog& Catalog)
{
    FCMLRecipeDefinition Recipe;
    if (Machine.Kind != ECMLMachineNodeKind::Machine
        || ItemId.IsNone()
        || Machine.bIsCycleActive
        || !IsPortEmpty(Machine.Output)
        || Machine.ActiveRecipeId.IsNone()
        || !Catalog.TryGetRecipe(Machine.ActiveRecipeId, Recipe))
    {
        return false;
    }

    int64 Required = 0;
    for (const FCMLRecipeAmount& Input : Recipe.Inputs)
    {
        if (Input.ItemId == ItemId)
        {
            Required += Input.Quantity;
        }
    }

    FCMLMachineDefinition Definition;
    if (Required <= 0 || !Catalog.TryGetMachine(Machine.DefinitionId, Definition))
    {
        return false;
    }
    return Ports::Count(Machine.Input, ItemId) < Definition.InputBufferCapacityPerItem
        && Ports::StorableQuantity(Machine.Input, ItemId, Catalog) > 0;
}

void FCMLMachineSpatialTopology::Advance(
    FCMLMachineSimulationState& State, const FCMLGameCatalog& Catalog)
{
    const FSpatialNodeIndex Index(State);
    const TArray<int32> Order = CanonicalOrder(State);

    // 1. Extracting funnels pull one unit out of what they are clamped to.
    for (const int32 FunnelIndex : Order)
    {
        int32 Attached = INDEX_NONE;
        int32 Belt = INDEX_NONE;
        EFunnelDirection Direction = EFunnelDirection::None;
        if (!TryResolveFunnel(State, Index, FunnelIndex, Attached, Belt, Direction)
            || Direction != EFunnelDirection::Extract
            || !IsPortEmpty(State.Nodes[FunnelIndex].Input))
        {
            continue;
        }
        FCMLStableId ItemId;
        if (!TryPeekFirstItem(
                EffectivePort(State.Nodes[Attached], State.Nodes[Attached].Output), ItemId))
        {
            continue;
        }
        MoveOne(
            State.Nodes[Attached], State.Nodes[Attached].Output,
            State.Nodes[FunnelIndex], State.Nodes[FunnelIndex].Input,
            ItemId, Catalog);
    }

    // 2. Loaded belts with somewhere to go advance by one tick's travel.
    for (const int32 BeltIndex : Order)
    {
        FCMLMachineNodeState& Belt = State.Nodes[BeltIndex];
        int32 Destination = INDEX_NONE;
        if (Belt.Kind != ECMLMachineNodeKind::BeltModule
            || !Belt.bHasPlacementPose
            || Belt.BeltTravelDirection == ECMLBeltTravelDirection::Stopped
            || IsPortEmpty(Belt.Input)
            || !TryResolveBeltDestination(State, Index, BeltIndex, Destination))
        {
            continue;
        }
        Belt.TransportProgressMillimetres = FMath::Min<int64>(
            BeltLengthMillimetres,
            Belt.TransportProgressMillimetres + BeltSpeedMillimetresPerTick);
    }

    // 3. Belts that have travelled their whole length hand their piece on.
    for (const int32 BeltIndex : Order)
    {
        FCMLStableId ItemId;
        int32 Destination = INDEX_NONE;
        if (State.Nodes[BeltIndex].Kind != ECMLMachineNodeKind::BeltModule
            || State.Nodes[BeltIndex].TransportProgressMillimetres < BeltLengthMillimetres
            || !TryPeekFirstItem(State.Nodes[BeltIndex].Input, ItemId)
            || !TryResolveBeltDestination(State, Index, BeltIndex, Destination))
        {
            continue;
        }

        bool bAccepted = false;
        switch (State.Nodes[Destination].Kind)
        {
            case ECMLMachineNodeKind::BeltModule:
                bAccepted = IsPortEmpty(State.Nodes[Destination].Input);
                break;
            case ECMLMachineNodeKind::Machine:
                bAccepted = MachineAdmits(State.Nodes[Destination], ItemId, Catalog);
                break;
            case ECMLMachineNodeKind::Funnel:
            {
                int32 Attached = INDEX_NONE;
                int32 ConnectedBelt = INDEX_NONE;
                EFunnelDirection Direction = EFunnelDirection::None;
                bAccepted = IsPortEmpty(State.Nodes[Destination].Input)
                    && TryResolveFunnel(
                        State, Index, Destination, Attached, ConnectedBelt, Direction)
                    && Direction == EFunnelDirection::Insert;
                break;
            }
            default:
                break;
        }
        if (!bAccepted)
        {
            continue;
        }

        if (!MoveOne(
                State.Nodes[BeltIndex], State.Nodes[BeltIndex].Output,
                State.Nodes[Destination], State.Nodes[Destination].Input,
                ItemId, Catalog))
        {
            continue;
        }
        State.Nodes[BeltIndex].TransportProgressMillimetres = 0;
        if (State.Nodes[Destination].Kind == ECMLMachineNodeKind::BeltModule)
        {
            State.Nodes[Destination].TransportProgressMillimetres = 0;
        }
    }

    // 4. Empty belts take a piece from whatever sits upstream of them.
    for (const int32 BeltIndex : Order)
    {
        int32 Source = INDEX_NONE;
        if (State.Nodes[BeltIndex].Kind != ECMLMachineNodeKind::BeltModule
            || !State.Nodes[BeltIndex].bHasPlacementPose
            || !IsPortEmpty(State.Nodes[BeltIndex].Input)
            || !Index.TryGetTravelBack(BeltIndex, Source))
        {
            continue;
        }

        bool bEligible = false;
        switch (State.Nodes[Source].Kind)
        {
            case ECMLMachineNodeKind::Funnel:
            {
                int32 Attached = INDEX_NONE;
                int32 ConnectedBelt = INDEX_NONE;
                EFunnelDirection Direction = EFunnelDirection::None;
                bEligible = TryResolveFunnel(
                        State, Index, Source, Attached, ConnectedBelt, Direction)
                    && Direction == EFunnelDirection::Extract
                    && ConnectedBelt == BeltIndex;
                break;
            }
            case ECMLMachineNodeKind::Machine:
                bEligible = TravelFaces(
                    State.Nodes[BeltIndex],
                    static_cast<uint8>(State.Nodes[Source].PlacementPose.YawQuarterTurns));
                break;
            default:
                // Belt-to-belt was already handled by the delivery step, and a
                // buffer never loads a belt directly.
                break;
        }
        if (!bEligible)
        {
            continue;
        }

        FCMLStableId ItemId;
        if (!TryPeekFirstItem(
                EffectivePort(State.Nodes[Source], State.Nodes[Source].Output), ItemId))
        {
            continue;
        }
        if (!MoveOne(
                State.Nodes[Source], State.Nodes[Source].Output,
                State.Nodes[BeltIndex], State.Nodes[BeltIndex].Input,
                ItemId, Catalog))
        {
            continue;
        }
        State.Nodes[BeltIndex].TransportProgressMillimetres = 0;
    }

    // 5. Inserting funnels push their piece into what they are clamped to.
    for (const int32 FunnelIndex : Order)
    {
        int32 Attached = INDEX_NONE;
        int32 Belt = INDEX_NONE;
        EFunnelDirection Direction = EFunnelDirection::None;
        if (!TryResolveFunnel(State, Index, FunnelIndex, Attached, Belt, Direction)
            || Direction != EFunnelDirection::Insert)
        {
            continue;
        }
        FCMLStableId ItemId;
        if (!TryPeekFirstItem(State.Nodes[FunnelIndex].Output, ItemId))
        {
            continue;
        }
        MoveOne(
            State.Nodes[FunnelIndex], State.Nodes[FunnelIndex].Output,
            State.Nodes[Attached], State.Nodes[Attached].Input,
            ItemId, Catalog);
    }
}

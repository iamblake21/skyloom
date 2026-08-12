#include "Simulation/CMLCanonicalStateSerializer.h"

#include "Simulation/CMLCanonicalWriter.h"
#include "Simulation/CMLSimulationState.h"

void FCMLCanonicalStateSerializer::SerializeStableId(const FCMLStableId& Id, TArray<uint8>& OutBytes)
{
    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(2);
    Writer.TryWriteTag(1);
    Writer.WriteUnsigned(Id.High);
    Writer.TryWriteTag(2);
    Writer.WriteUnsigned(Id.Low);
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeQuantities(
    const TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>>& Entries,
    TArray<uint8>& OutBytes)
{
    FCMLCanonicalWriter Writer;
    Writer.WriteUnsigned(static_cast<uint64>(Entries.Num()));
    for (const TPair<FCMLStableId, FCMLNonNegativeQuantity>& Entry : Entries)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Entry.Key, IdBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(2);
        Element.TryWriteTag(1);
        Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2);
        // The quantity is non-negative by construction, so the unsigned
        // encoding is exact rather than a reinterpretation.
        Element.WriteUnsigned(static_cast<uint64>(Entry.Value.Value));
        Writer.WriteBytes(Element.GetBytes());
    }
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeAccumulatorKey(
    const FCMLAccumulatorKey& Key,
    TArray<uint8>& OutBytes)
{
    TArray<uint8> IdBytes;
    SerializeStableId(Key.EntityId, IdBytes);

    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(4);
    Writer.TryWriteTag(1);
    Writer.WriteString(Key.SystemKind);
    Writer.TryWriteTag(2);
    Writer.WriteString(Key.ResourceKind);
    Writer.TryWriteTag(3);
    Writer.WriteBytes(IdBytes);
    Writer.TryWriteTag(4);
    Writer.WriteUnsigned(static_cast<uint64>(Key.PortOrCycleIndex));
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeAccumulators(
    const TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>>& Entries,
    TArray<uint8>& OutBytes)
{
    FCMLCanonicalWriter Writer;
    Writer.WriteUnsigned(static_cast<uint64>(Entries.Num()));
    for (const TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>& Entry : Entries)
    {
        TArray<uint8> KeyBytes;
        SerializeAccumulatorKey(Entry.Key, KeyBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(4);
        Element.TryWriteTag(1);
        Element.WriteBytes(KeyBytes);
        Element.TryWriteTag(2);
        Element.WriteUnsigned(Entry.Value.OwnerDenominator);
        Element.TryWriteTag(3);
        Element.WriteUnsigned(Entry.Value.Remainder);
        Element.TryWriteTag(4);
        Element.WriteUnsigned(static_cast<uint64>(Entry.Value.RuleRevision));
        Writer.WriteBytes(Element.GetBytes());
    }
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SortQuantities(
    TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>>& Entries)
{
    Entries.Sort([](const TPair<FCMLStableId, FCMLNonNegativeQuantity>& A,
                    const TPair<FCMLStableId, FCMLNonNegativeQuantity>& B)
    {
        return A.Key < B.Key;
    });
}

void FCMLCanonicalStateSerializer::SortAccumulators(
    TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>>& Entries)
{
    Entries.Sort([](const TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>& A,
                    const TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>& B)
    {
        return A.Key.Compare(B.Key) < 0;
    });
}

void FCMLCanonicalStateSerializer::SerializeCommand(
    const FCMLSimulationCommand& Command,
    TArray<uint8>& OutBytes)
{
    TArray<uint8> InitiatorBytes;
    SerializeStableId(Command.InitiatorId, InitiatorBytes);
    TArray<uint8> DestinationBytes;
    SerializeStableId(Command.DestinationId, DestinationBytes);

    FCMLCanonicalWriter Element;
    Element.WriteFieldCount(7);
    Element.TryWriteTag(1);
    Element.WriteUnsigned(Command.TargetTick.Value);
    Element.TryWriteTag(2);
    Element.WriteUnsigned(Command.Sequence);
    Element.TryWriteTag(3);
    Element.WriteString(Command.Kind);
    Element.TryWriteTag(4);
    Element.WriteBytes(InitiatorBytes);
    Element.TryWriteTag(5);
    Element.WriteBytes(DestinationBytes);
    Element.TryWriteTag(6);
    // The quantized value is the one signed field in the schema, so it is
    // ZigZag encoded rather than reinterpreted as unsigned.
    Element.WriteSigned(Command.QuantizedValue);
    Element.TryWriteTag(7);
    Element.WriteBytes(Command.Payload);
    OutBytes = Element.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeCommands(
    const TArray<FCMLSimulationCommand>& Commands,
    TArray<uint8>& OutBytes)
{
    FCMLCanonicalWriter Writer;
    Writer.WriteUnsigned(static_cast<uint64>(Commands.Num()));
    for (const FCMLSimulationCommand& Command : Commands)
    {
        TArray<uint8> CommandBytes;
        SerializeCommand(Command, CommandBytes);
        Writer.WriteBytes(CommandBytes);
    }
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeCreationKey(
    const FCMLCreationKey& Key,
    TArray<uint8>& OutBytes)
{
    TArray<uint8> InitiatorBytes;
    SerializeStableId(Key.InitiatorId, InitiatorBytes);

    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(6);
    Writer.TryWriteTag(1);
    // The phase travels as its pinned byte value, which is why the enum is
    // explicitly numbered.
    Writer.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Key.Phase)));
    Writer.TryWriteTag(2);
    Writer.WriteUnsigned(static_cast<uint64>(Key.CauseCode));
    Writer.TryWriteTag(3);
    Writer.WriteBytes(InitiatorBytes);
    Writer.TryWriteTag(4);
    Writer.WriteUnsigned(Key.CommandSequence);
    Writer.TryWriteTag(5);
    Writer.WriteUnsigned(static_cast<uint64>(Key.OutputIndex));
    Writer.TryWriteTag(6);
    Writer.WriteUnsigned(static_cast<uint64>(Key.LocalOrdinal));
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeCreations(
    const TArray<FCMLCreationRecord>& Records,
    TArray<uint8>& OutBytes)
{
    FCMLCanonicalWriter Writer;
    Writer.WriteUnsigned(static_cast<uint64>(Records.Num()));
    for (const FCMLCreationRecord& Record : Records)
    {
        TArray<uint8> KeyBytes;
        SerializeCreationKey(Record.Key, KeyBytes);
        TArray<uint8> EntityBytes;
        SerializeStableId(Record.EntityId, EntityBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(3);
        Element.TryWriteTag(1);
        Element.WriteUnsigned(Record.Tick.Value);
        Element.TryWriteTag(2);
        Element.WriteBytes(KeyBytes);
        Element.TryWriteTag(3);
        Element.WriteBytes(EntityBytes);
        Writer.WriteBytes(Element.GetBytes());
    }
    OutBytes = Writer.GetBytes();
}

void FCMLCanonicalStateSerializer::SerializeCommandRejections(
    const TArray<FCMLCommandRejection>& Rejections,
    TArray<uint8>& OutBytes)
{
    FCMLCanonicalWriter Writer;
    Writer.WriteUnsigned(static_cast<uint64>(Rejections.Num()));
    for (const FCMLCommandRejection& Rejection : Rejections)
    {
        TArray<uint8> CommandBytes;
        SerializeCommand(Rejection.Command, CommandBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(3);
        Element.TryWriteTag(1);
        Element.WriteUnsigned(Rejection.Tick.Value);
        Element.TryWriteTag(2);
        Element.WriteBytes(CommandBytes);
        Element.TryWriteTag(3);
        Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Rejection.Reason)));
        Writer.WriteBytes(Element.GetBytes());
    }
    OutBytes = Writer.GetBytes();
}

bool FCMLCanonicalStateSerializer::TrySerializeInventories(
    const FCMLInventorySimulationState& State,
    TArray<uint8>& OutBytes)
{
    if (!State.HasUniqueIds())
    {
        return false;
    }

    FCMLCanonicalWriter Collection;
    Collection.WriteUnsigned(static_cast<uint64>(State.Inventories.Num()));
    for (const FCMLInventoryState& Inventory : State.Inventories)
    {
        TArray<uint8> InventoryIdBytes;
        SerializeStableId(Inventory.InventoryId, InventoryIdBytes);
        TArray<uint8> DefinitionBytes;
        SerializeStableId(Inventory.ContainerDefinitionId, DefinitionBytes);

        FCMLCanonicalWriter Slots;
        Slots.WriteUnsigned(static_cast<uint64>(Inventory.Slots.Num()));
        for (const FCMLInventorySlot& Slot : Inventory.Slots)
        {
            // An empty slot encodes as the none id with quantity zero, so slot
            // positions stay aligned between two otherwise identical states.
            const FCMLStableId ItemId = Slot.bHasStack ? Slot.Stack.ItemId : FCMLStableId::None();
            const int64 Quantity = Slot.bHasStack ? Slot.Stack.Quantity.Value : 0;

            TArray<uint8> ItemBytes;
            SerializeStableId(ItemId, ItemBytes);

            FCMLCanonicalWriter SlotElement;
            SlotElement.WriteFieldCount(2);
            SlotElement.TryWriteTag(1);
            SlotElement.WriteBytes(ItemBytes);
            SlotElement.TryWriteTag(2);
            SlotElement.WriteUnsigned(static_cast<uint64>(Quantity));
            Slots.WriteBytes(SlotElement.GetBytes());
        }

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(3);
        Element.TryWriteTag(1);
        Element.WriteBytes(InventoryIdBytes);
        Element.TryWriteTag(2);
        Element.WriteBytes(DefinitionBytes);
        Element.TryWriteTag(3);
        Element.WriteBytes(Slots.GetBytes());
        Collection.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(2);
    Writer.TryWriteTag(1);
    Writer.WriteUnsigned(static_cast<uint64>(InventorySchemaRevision));
    Writer.TryWriteTag(2);
    Writer.WriteBytes(Collection.GetBytes());
    OutBytes = Writer.GetBytes();
    return true;
}

namespace
{
    void SerializeAirshipVector(const FCMLAirshipVector& Value, TArray<uint8>& OutBytes)
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteFieldCount(3);
        Writer.TryWriteTag(1);
        Writer.WriteSigned(Value.X);
        Writer.TryWriteTag(2);
        Writer.WriteSigned(Value.Y);
        Writer.TryWriteTag(3);
        Writer.WriteSigned(Value.Z);
        OutBytes = Writer.GetBytes();
    }

    void SerializeAirshipPose(const FCMLAirshipPose& Value, TArray<uint8>& OutBytes)
    {
        TArray<uint8> PositionBytes;
        SerializeAirshipVector(Value.Position, PositionBytes);

        FCMLCanonicalWriter Writer;
        Writer.WriteFieldCount(2);
        Writer.TryWriteTag(1);
        Writer.WriteBytes(PositionBytes);
        Writer.TryWriteTag(2);
        // Yaw is unsigned in the schema: it wraps through turn units rather
        // than ever carrying a sign.
        Writer.WriteUnsigned(static_cast<uint64>(Value.YawTurn));
        OutBytes = Writer.GetBytes();
    }

    void SerializeAirshipPilotInput(const FCMLAirshipPilotInput& Value, TArray<uint8>& OutBytes)
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteFieldCount(4);
        Writer.TryWriteTag(1);
        Writer.WriteSigned(Value.ThrottleChangePermille);
        Writer.TryWriteTag(2);
        Writer.WriteSigned(Value.LiftPermille);
        Writer.TryWriteTag(3);
        Writer.WriteSigned(Value.YawDeltaPermille);
        Writer.TryWriteTag(4);
        Writer.WriteSigned(Value.PitchDeltaPermille);
        OutBytes = Writer.GetBytes();
    }
}

bool FCMLCanonicalStateSerializer::TrySerializeAirship(
    const FCMLAirshipSimulationState& State,
    TArray<uint8>& OutBytes)
{
    if (!State.HasUniqueIds())
    {
        return false;
    }

    FCMLCanonicalWriter Airships;
    Airships.WriteUnsigned(static_cast<uint64>(State.Airships.Num()));
    for (const FCMLAirshipEntityState& Value : State.Airships)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Value.Id, IdBytes);
        TArray<uint8> PoseBytes;
        SerializeAirshipPose(Value.Pose, PoseBytes);
        TArray<uint8> InputBytes;
        SerializeAirshipPilotInput(Value.HeldInput, InputBytes);
        TArray<uint8> PilotBytes;
        SerializeStableId(Value.PilotId, PilotBytes);
        TArray<uint8> AcceptedBytes;
        SerializeStableId(Value.AcceptedLandingSurfaceId, AcceptedBytes);
        TArray<uint8> DockedBytes;
        SerializeStableId(Value.DockedLandingSurfaceId, DockedBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(22);
        Element.TryWriteTag(1);  Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2);  Element.WriteBytes(PoseBytes);
        Element.TryWriteTag(3);  Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Value.Mode)));
        Element.TryWriteTag(4);  Element.WriteSigned(Value.LandingTicksRemaining);
        Element.TryWriteTag(5);  Element.WriteSigned(Value.ForwardSpeedMillimetresPerSecond);
        Element.TryWriteTag(6);  Element.WriteSigned(Value.StrafeSpeedMillimetresPerSecond);
        Element.TryWriteTag(7);  Element.WriteSigned(Value.VerticalSpeedMillimetresPerSecond);
        Element.TryWriteTag(8);  Element.WriteSigned(Value.YawRateTurnUnitsPerSecond);
        Element.TryWriteTag(9);  Element.WriteSigned(Value.ForwardIntegrationRemainder);
        Element.TryWriteTag(10); Element.WriteSigned(Value.StrafeIntegrationRemainder);
        Element.TryWriteTag(11); Element.WriteSigned(Value.VerticalIntegrationRemainder);
        Element.TryWriteTag(12); Element.WriteSigned(Value.YawIntegrationRemainder);
        Element.TryWriteTag(13); Element.WriteBytes(InputBytes);
        Element.TryWriteTag(14); Element.WriteBytes(PilotBytes);
        Element.TryWriteTag(15); Element.WriteBytes(AcceptedBytes);
        Element.TryWriteTag(16); Element.WriteBytes(DockedBytes);
        Element.TryWriteTag(17); Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Value.LastLandingRequestResult)));
        Element.TryWriteTag(18); Element.WriteSigned(Value.PitchTurnUnits);
        Element.TryWriteTag(19); Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Value.RepairStatus)));
        Element.TryWriteTag(20); Element.WriteSigned(Value.InstalledIronPlates);
        Element.TryWriteTag(21); Element.WriteSigned(Value.InstalledInsulatedCables);
        Element.TryWriteTag(22); Element.WriteSigned(Value.RepairTicksRemaining);
        Airships.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Players;
    Players.WriteUnsigned(static_cast<uint64>(State.Players.Num()));
    for (const FCMLAirshipPlayerState& Value : State.Players)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Value.Id, IdBytes);
        TArray<uint8> FrameBytes;
        SerializeStableId(Value.FrameAirshipId, FrameBytes);
        TArray<uint8> PoseBytes;
        SerializeAirshipPose(Value.QuantizedPose, PoseBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(5);
        Element.TryWriteTag(1); Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2); Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Value.FrameKind)));
        Element.TryWriteTag(3); Element.WriteBytes(FrameBytes);
        Element.TryWriteTag(4); Element.WriteBytes(PoseBytes);
        Element.TryWriteTag(5); Element.WriteBoolean(Value.bIsPiloting);
        Players.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Obstacles;
    Obstacles.WriteUnsigned(static_cast<uint64>(State.Obstacles.Num()));
    for (const FCMLAirshipObstacle& Value : State.Obstacles)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Value.Id, IdBytes);
        TArray<uint8> MinimumBytes;
        SerializeAirshipVector(Value.Minimum, MinimumBytes);
        TArray<uint8> MaximumBytes;
        SerializeAirshipVector(Value.Maximum, MaximumBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(3);
        Element.TryWriteTag(1); Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2); Element.WriteBytes(MinimumBytes);
        Element.TryWriteTag(3); Element.WriteBytes(MaximumBytes);
        Obstacles.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Surfaces;
    Surfaces.WriteUnsigned(static_cast<uint64>(State.LandingSurfaces.Num()));
    for (const FCMLAirshipLandingSurface& Value : State.LandingSurfaces)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Value.Id, IdBytes);
        TArray<uint8> CenterBytes;
        SerializeAirshipVector(Value.Center, CenterBytes);
        TArray<uint8> SupportBytes;
        SerializeStableId(Value.SupportingObstacleId, SupportBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(6);
        Element.TryWriteTag(1); Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2); Element.WriteBytes(CenterBytes);
        Element.TryWriteTag(3); Element.WriteUnsigned(static_cast<uint64>(Value.YawTurn));
        Element.TryWriteTag(4); Element.WriteSigned(Value.HalfWidthMillimetres);
        Element.TryWriteTag(5); Element.WriteSigned(Value.HalfDepthMillimetres);
        Element.TryWriteTag(6); Element.WriteBytes(SupportBytes);
        Surfaces.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(5);
    Writer.TryWriteTag(1); Writer.WriteUnsigned(static_cast<uint64>(AirshipSchemaRevision));
    Writer.TryWriteTag(2); Writer.WriteBytes(Airships.GetBytes());
    Writer.TryWriteTag(3); Writer.WriteBytes(Players.GetBytes());
    Writer.TryWriteTag(4); Writer.WriteBytes(Obstacles.GetBytes());
    Writer.TryWriteTag(5); Writer.WriteBytes(Surfaces.GetBytes());
    OutBytes = Writer.GetBytes();
    return true;
}

namespace
{
    void SerializeMachinePose(const FCMLMachineBuildPose& Pose, TArray<uint8>& OutBytes)
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteFieldCount(4);
        Writer.TryWriteTag(1); Writer.WriteSigned(Pose.XMillimetres);
        Writer.TryWriteTag(2); Writer.WriteSigned(Pose.YMillimetres);
        Writer.TryWriteTag(3); Writer.WriteSigned(Pose.ZMillimetres);
        Writer.TryWriteTag(4); Writer.WriteUnsigned(static_cast<uint64>(Pose.YawQuarterTurns));
        OutBytes = Writer.GetBytes();
    }

    void SerializeMachinePort(const FCMLMachinePort& Port, TArray<uint8>& OutBytes)
    {
        FCMLCanonicalWriter Slots;
        Slots.WriteUnsigned(static_cast<uint64>(Port.Slots.Num()));
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            TArray<uint8> ItemBytes;
            FCMLCanonicalStateSerializer::SerializeStableId(Slot.ItemId, ItemBytes);

            FCMLCanonicalWriter SlotElement;
            SlotElement.WriteFieldCount(2);
            SlotElement.TryWriteTag(1); SlotElement.WriteBytes(ItemBytes);
            SlotElement.TryWriteTag(2); SlotElement.WriteUnsigned(static_cast<uint64>(Slot.Quantity.Value));
            Slots.WriteBytes(SlotElement.GetBytes());
        }

        FCMLCanonicalWriter Writer;
        Writer.WriteFieldCount(2);
        Writer.TryWriteTag(1); Writer.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Port.Kind)));
        Writer.TryWriteTag(2); Writer.WriteBytes(Slots.GetBytes());
        OutBytes = Writer.GetBytes();
    }

    /**
     * A node encodes its ports as a collection rather than as fixed fields,
     * because a buffer's input and output are one port: emitting it twice would
     * put a crate's contents into the hash twice.
     */
    void SerializeMachinePorts(const FCMLMachineNodeState& Node, TArray<uint8>& OutBytes)
    {
        uint64 PortCount = Node.bInputOutputAliased ? 1 : 2;
        if (Node.bHasFuelPort)
        {
            ++PortCount;
        }

        FCMLCanonicalWriter Collection;
        Collection.WriteUnsigned(PortCount);

        TArray<uint8> PortBytes;
        SerializeMachinePort(Node.Input, PortBytes);
        Collection.WriteBytes(PortBytes);
        if (Node.bHasFuelPort)
        {
            SerializeMachinePort(Node.Fuel, PortBytes);
            Collection.WriteBytes(PortBytes);
        }
        if (!Node.bInputOutputAliased)
        {
            SerializeMachinePort(Node.Output, PortBytes);
            Collection.WriteBytes(PortBytes);
        }
        OutBytes = Collection.GetBytes();
    }
}

bool FCMLCanonicalStateSerializer::TrySerializeMachines(
    const FCMLMachineSimulationState& State,
    TArray<uint8>& OutBytes)
{
    if (!State.HasUniqueIds())
    {
        return false;
    }

    FCMLCanonicalWriter Nodes;
    Nodes.WriteUnsigned(static_cast<uint64>(State.Nodes.Num()));
    for (const FCMLMachineNodeState& Node : State.Nodes)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Node.Id, IdBytes);
        TArray<uint8> DefinitionBytes;
        SerializeStableId(Node.DefinitionId, DefinitionBytes);
        TArray<uint8> RecipeBytes;
        SerializeStableId(Node.ActiveRecipeId, RecipeBytes);
        TArray<uint8> PortBytes;
        SerializeMachinePorts(Node, PortBytes);
        TArray<uint8> AttachedBytes;
        SerializeStableId(Node.AttachedNodeId, AttachedBytes);
        TArray<uint8> PoseBytes;
        SerializeMachinePose(Node.PlacementPose, PoseBytes);

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(17);
        Element.TryWriteTag(1);  Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2);  Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Node.Kind)));
        Element.TryWriteTag(3);  Element.WriteBytes(DefinitionBytes);
        Element.TryWriteTag(4);  Element.WriteBytes(RecipeBytes);
        Element.TryWriteTag(5);  Element.WriteSigned(Node.ProgressMilliseconds);
        Element.TryWriteTag(6);  Element.WriteBoolean(Node.bIsCycleActive);
        Element.TryWriteTag(7);  Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Node.Activity)));
        Element.TryWriteTag(8);  Element.WriteUnsigned(static_cast<uint64>(Node.CompletedCycles));
        Element.TryWriteTag(9);  Element.WriteBytes(PortBytes);
        Element.TryWriteTag(10); Element.WriteBytes(AttachedBytes);
        Element.TryWriteTag(11); Element.WriteBoolean(Node.bHasPlacementPose);
        Element.TryWriteTag(12); Element.WriteBytes(PoseBytes);
        Element.TryWriteTag(13); Element.WriteSigned(Node.TransportProgressMillimetres);
        Element.TryWriteTag(14); Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Node.BeltTravelDirection)));
        Element.TryWriteTag(15); Element.WriteUnsigned(static_cast<uint64>(static_cast<uint8>(Node.BeltLineStatus)));
        Element.TryWriteTag(16); Element.WriteSigned(Node.BeltLineUsedCapacity);
        Element.TryWriteTag(17); Element.WriteSigned(Node.BeltLineAvailableCapacity);
        Nodes.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Lanes;
    Lanes.WriteUnsigned(static_cast<uint64>(State.Lanes.Num()));
    for (const FCMLBeltLaneState& Lane : State.Lanes)
    {
        TArray<uint8> IdBytes;
        SerializeStableId(Lane.Id, IdBytes);
        TArray<uint8> SourceBytes;
        SerializeStableId(Lane.SourceNodeId, SourceBytes);
        TArray<uint8> DestinationBytes;
        SerializeStableId(Lane.DestinationNodeId, DestinationBytes);
        TArray<uint8> FilterBytes;
        SerializeStableId(Lane.ItemFilter, FilterBytes);

        FCMLCanonicalWriter Cargo;
        Cargo.WriteUnsigned(static_cast<uint64>(Lane.Items.Num()));
        for (const FCMLBeltLaneItem& Item : Lane.Items)
        {
            TArray<uint8> ItemBytes;
            SerializeStableId(Item.ItemId, ItemBytes);

            FCMLCanonicalWriter ItemElement;
            ItemElement.WriteFieldCount(2);
            ItemElement.TryWriteTag(1); ItemElement.WriteBytes(ItemBytes);
            ItemElement.TryWriteTag(2); ItemElement.WriteSigned(Item.PositionMillimetres);
            Cargo.WriteBytes(ItemElement.GetBytes());
        }

        FCMLCanonicalWriter Element;
        Element.WriteFieldCount(9);
        Element.TryWriteTag(1); Element.WriteBytes(IdBytes);
        Element.TryWriteTag(2); Element.WriteBytes(SourceBytes);
        Element.TryWriteTag(3); Element.WriteBytes(DestinationBytes);
        Element.TryWriteTag(4); Element.WriteBytes(FilterBytes);
        Element.TryWriteTag(5); Element.WriteSigned(Lane.LengthMillimetres);
        Element.TryWriteTag(6); Element.WriteSigned(Lane.SpeedMillimetresPerTick);
        Element.TryWriteTag(7); Element.WriteSigned(Lane.SpacingMillimetres);
        Element.TryWriteTag(8); Element.WriteUnsigned(static_cast<uint64>(Lane.DeliveredUnits));
        Element.TryWriteTag(9); Element.WriteBytes(Cargo.GetBytes());
        Lanes.WriteBytes(Element.GetBytes());
    }

    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(3);
    Writer.TryWriteTag(1); Writer.WriteUnsigned(static_cast<uint64>(MachineSchemaRevision));
    Writer.TryWriteTag(2); Writer.WriteBytes(Nodes.GetBytes());
    Writer.TryWriteTag(3); Writer.WriteBytes(Lanes.GetBytes());
    OutBytes = Writer.GetBytes();
    return true;
}

bool FCMLCanonicalStateSerializer::TrySerializeRoot(
    const FCMLSimulationState& State,
    TArray<uint8>& OutBytes)
{
    TArray<uint8> NextEntityBytes;
    SerializeStableId(State.NextEntityId, NextEntityBytes);

    TArray<uint8> QuantityBytes;
    SerializeQuantities(State.Quantities, QuantityBytes);

    TArray<uint8> AccumulatorBytes;
    SerializeAccumulators(State.Accumulators, AccumulatorBytes);

    TArray<uint8> CommandBytes;
    SerializeCommands(State.AcceptedCommands, CommandBytes);

    TArray<uint8> CreationBytes;
    SerializeCreations(State.CreationRecords, CreationBytes);

    TArray<uint8> RejectionBytes;
    SerializeCommandRejections(State.CommandRejections, RejectionBytes);

    TArray<uint8> AirshipBytes;
    if (!TrySerializeAirship(State.Airship, AirshipBytes))
    {
        return false;
    }

    TArray<uint8> MachineBytes;
    if (!TrySerializeMachines(State.Machines, MachineBytes))
    {
        return false;
    }

    TArray<uint8> InventoryBytes;
    if (!TrySerializeInventories(State.Inventories, InventoryBytes))
    {
        return false;
    }

    FCMLCanonicalWriter Writer;
    Writer.WriteFieldCount(RootFieldCount);
    Writer.TryWriteTag(1);  Writer.WriteUnsigned(static_cast<uint64>(State.LogicalSchemaRevision));
    Writer.TryWriteTag(2);  Writer.WriteUnsigned(static_cast<uint64>(State.RulesRevision));
    Writer.TryWriteTag(3);  Writer.WriteUnsigned(static_cast<uint64>(State.CatalogSchemaVersion));
    Writer.TryWriteTag(4);  Writer.WriteString(State.ContentRevision);
    Writer.TryWriteTag(5);  Writer.WriteUnsigned(static_cast<uint64>(State.GeneratorRevision));
    Writer.TryWriteTag(6);  Writer.WriteUnsigned(State.Tick.Value);
    Writer.TryWriteTag(7);  Writer.WriteBytes(NextEntityBytes);
    Writer.TryWriteTag(8);  Writer.WriteBoolean(State.bIsEntityIdSpaceExhausted);
    Writer.TryWriteTag(9);  Writer.WriteBytes(QuantityBytes);
    Writer.TryWriteTag(10); Writer.WriteBytes(AccumulatorBytes);
    Writer.TryWriteTag(11); Writer.WriteBytes(CommandBytes);
    Writer.TryWriteTag(12); Writer.WriteBytes(CreationBytes);
    Writer.TryWriteTag(13); Writer.WriteBytes(RejectionBytes);
    Writer.TryWriteTag(14); Writer.WriteBytes(AirshipBytes);
    Writer.TryWriteTag(15); Writer.WriteBytes(MachineBytes);
    Writer.TryWriteTag(16); Writer.WriteBytes(InventoryBytes);
    OutBytes = Writer.GetBytes();
    return true;
}

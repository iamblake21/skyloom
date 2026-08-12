#include "Simulation/CMLSlotMoveCommand.h"

bool FCMLSlotMoveCommandPayload::TryEncode(
    const int32 SourceSlotIndex,
    const int32 DestinationSlotIndex,
    TArray<uint8>& OutPayload)
{
    OutPayload.Reset();
    if (SourceSlotIndex < 0 || SourceSlotIndex > MAX_uint16
        || DestinationSlotIndex < 0 || DestinationSlotIndex > MAX_uint16)
    {
        return false;
    }
    OutPayload.Add(static_cast<uint8>((SourceSlotIndex >> 8) & 0xFF));
    OutPayload.Add(static_cast<uint8>(SourceSlotIndex & 0xFF));
    OutPayload.Add(static_cast<uint8>((DestinationSlotIndex >> 8) & 0xFF));
    OutPayload.Add(static_cast<uint8>(DestinationSlotIndex & 0xFF));
    return true;
}

bool FCMLSlotMoveCommandPayload::TryDecode(
    const FCMLSimulationCommand& Command,
    FCMLStableId& OutInventoryId,
    int32& OutSourceSlotIndex,
    int32& OutDestinationSlotIndex,
    int64& OutAmount)
{
    OutInventoryId = FCMLStableId::None();
    OutSourceSlotIndex = 0;
    OutDestinationSlotIndex = 0;
    OutAmount = 0;

    if (Command.Payload.Num() != Length)
    {
        return false;
    }
    if (Command.QuantizedValue < 0 || Command.InitiatorId.IsNone())
    {
        return false;
    }

    OutInventoryId = Command.InitiatorId;
    OutSourceSlotIndex = (Command.Payload[0] << 8) | Command.Payload[1];
    OutDestinationSlotIndex = (Command.Payload[2] << 8) | Command.Payload[3];
    OutAmount = Command.QuantizedValue;
    return true;
}

#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLSimulationRecords.h"

/**
 * The payload of a slot rearrangement, ported from
 * CML.Simulation.Inventories.SlotMoveCommandPayload.
 *
 * `FCMLSimulationCommand`'s fixed fields carry what they are named for: the
 * inventory in `InitiatorId`, the amount in `QuantizedValue`, where zero means
 * the whole source stack. What is left is the two slot indices — four bytes,
 * fixed width and big-endian, so decoding depends on neither a length nor the
 * platform's byte order.
 */
class CMLCORE_API FCMLSlotMoveCommandPayload
{
public:
    static constexpr int32 Length = 4;

    /** The command kind this payload belongs to. */
    static const TCHAR* CommandKind() { return TEXT("MoveInventorySlot"); }

    /** Refuses an index that will not survive the round trip through 16 bits. */
    static bool TryEncode(
        int32 SourceSlotIndex, int32 DestinationSlotIndex, TArray<uint8>& OutPayload);

    static bool TryDecode(
        const FCMLSimulationCommand& Command,
        FCMLStableId& OutInventoryId,
        int32& OutSourceSlotIndex,
        int32& OutDestinationSlotIndex,
        int64& OutAmount);
};

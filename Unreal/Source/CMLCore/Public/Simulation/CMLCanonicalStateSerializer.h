#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLAccumulator.h"
#include "Foundation/CMLCoreTypes.h"
#include "Simulation/CMLAirshipState.h"
#include "Simulation/CMLInventoryState.h"
#include "Simulation/CMLMachineState.h"
#include "Simulation/CMLSimulationRecords.h"

struct FCMLSimulationState;

/**
 * Canonical projection of the authoritative state, ported from
 * CML.Simulation.CanonicalEncoding.CanonicalStateSerializer.
 *
 * The Unity root writes sixteen tagged fields. The element serialisers here are
 * the ones whose inputs are already ported (Foundation types); the airship,
 * machine and inventory subtrees join once their state types exist. Each one is
 * byte-exact on its own, which is what makes them testable before the whole
 * root can be assembled.
 *
 * Every element is written into its own writer and then emitted as a
 * length-prefixed blob, exactly as the C# original did: nesting by length keeps
 * a field's encoding independent of what surrounds it.
 */
class CMLCORE_API FCMLCanonicalStateSerializer
{
public:
    /** Root field count, so the port cannot silently drift from the schema. */
    static constexpr uint64 RootFieldCount = 16;

    /** Field 7 and the key of every quantity: {high, low} as two tagged fields. */
    static void SerializeStableId(const FCMLStableId& Id, TArray<uint8>& OutBytes);

    /** Field 9: count-prefixed {stable id, quantity} pairs in canonical order. */
    static void SerializeQuantities(
        const TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>>& Entries,
        TArray<uint8>& OutBytes);

    /** {system kind, resource kind, entity id, port/cycle index}. */
    static void SerializeAccumulatorKey(const FCMLAccumulatorKey& Key, TArray<uint8>& OutBytes);

    /** Field 10: count-prefixed {key, denominator, remainder, rule revision}. */
    static void SerializeAccumulators(
        const TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>>& Entries,
        TArray<uint8>& OutBytes);

    /**
     * Canonical ordering for the quantity map. Unity held these in a
     * SortedDictionary, so the encoded order is part of the hash rather than an
     * implementation detail; an Unreal TMap has no such guarantee and must be
     * sorted explicitly before serialisation.
     */
    static void SortQuantities(TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>>& Entries);

    static void SortAccumulators(
        TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>>& Entries);

    /** One command: target tick, sequence, kind, endpoints, value and payload. */
    static void SerializeCommand(const FCMLSimulationCommand& Command, TArray<uint8>& OutBytes);

    /** Field 11: the accepted command list, in acceptance order. */
    static void SerializeCommands(const TArray<FCMLSimulationCommand>& Commands, TArray<uint8>& OutBytes);

    static void SerializeCreationKey(const FCMLCreationKey& Key, TArray<uint8>& OutBytes);

    /** Field 12: what this tick created, and under which deterministic key. */
    static void SerializeCreations(const TArray<FCMLCreationRecord>& Records, TArray<uint8>& OutBytes);

    /** Field 13: refused commands, carrying the whole command and the reason. */
    static void SerializeCommandRejections(
        const TArray<FCMLCommandRejection>& Rejections,
        TArray<uint8>& OutBytes);

    /** The INV subtree's own schema revision, independent of the root's. */
    static constexpr uint32 InventorySchemaRevision = 1;

    /**
     * Field 16: the inventories. Returns false when two inventories share an
     * id, which would make the encoding ambiguous; the caller must reject the
     * state rather than hash something that cannot be reproduced.
     */
    static bool TrySerializeInventories(
        const FCMLInventorySimulationState& State,
        TArray<uint8>& OutBytes);

    /**
     * Revision 5 added the hull repair state: a grounded airship and a flyable
     * one are not the same world, so it belongs in the hash.
     */
    static constexpr uint32 AirshipSchemaRevision = 5;

    /** Field 14: the AIR subtree. Refused when any collection repeats an id. */
    static bool TrySerializeAirship(
        const FCMLAirshipSimulationState& State,
        TArray<uint8>& OutBytes);

    /**
     * Revision 10 added the optional fuel port. Adjacency is deliberately
     * absent from the schema: it is derived from the quantised poses every
     * tick, so moving one module cannot leave a stale connection in the hash.
     */
    static constexpr uint32 MachineSchemaRevision = 10;

    /** Field 15: the MCH subtree. Refused when nodes or lanes repeat an id. */
    static bool TrySerializeMachines(
        const FCMLMachineSimulationState& State,
        TArray<uint8>& OutBytes);

    /**
     * The whole canonical state: all sixteen root fields, in schema order.
     *
     * The caller must have sorted the state first. Refused when any subtree
     * repeats an id, because such a state cannot be reproduced by a replay and
     * hashing it would hide the defect behind a plausible-looking digest.
     */
    static bool TrySerializeRoot(const FCMLSimulationState& State, TArray<uint8>& OutBytes);
};

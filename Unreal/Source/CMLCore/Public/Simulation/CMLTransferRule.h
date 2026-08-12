#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Simulation/CMLInventoryState.h"
#include "Simulation/CMLMachineState.h"

#include "CMLTransferRule.generated.h"

UENUM(BlueprintType)
enum class ECMLTransferEndpointKind : uint8
{
    None = 0,
    Inventory = 1,
    MachinePort = 2
};

/**
 * One side of a transfer. A machine port endpoint names the node and which of
 * its ports; a buffer node has one port and answers to `Storage` alone.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLTransferEndpoint
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Transfer")
    ECMLTransferEndpointKind Kind = ECMLTransferEndpointKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Transfer")
    FCMLStableId OwnerId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Transfer")
    ECMLMachinePortKind PortKind = ECMLMachinePortKind::None;

    static FCMLTransferEndpoint Inventory(const FCMLStableId& InventoryId);
    static FCMLTransferEndpoint Port(const FCMLStableId& NodeId, ECMLMachinePortKind Port);

    friend bool operator==(const FCMLTransferEndpoint& A, const FCMLTransferEndpoint& B)
    {
        return A.Kind == B.Kind && A.OwnerId == B.OwnerId && A.PortKind == B.PortKind;
    }
    friend bool operator!=(const FCMLTransferEndpoint& A, const FCMLTransferEndpoint& B)
    {
        return !(A == B);
    }
};

/**
 * Why a transfer did not happen. A refusal always names one of these, so a
 * caller never has to guess between "not allowed" and "nothing to move".
 */
UENUM(BlueprintType)
enum class ECMLTransferFailure : uint8
{
    None = 0,
    UnknownSource = 1,
    UnknownDestination = 2,
    SameEndpoint = 3,
    UnknownItem = 4,
    ZeroAmount = 5,
    InsufficientSource = 6,
    DestinationFull = 7,
    NotAdmitted = 8
};

/** Port arithmetic, ported from CML.Simulation.Machines.MachinePort. */
class CMLCORE_API FCMLMachinePortOperations
{
public:
    static int64 Count(const FCMLMachinePort& Port, const FCMLStableId& ItemId);
    static int64 TotalQuantity(const FCMLMachinePort& Port);

    /**
     * How much of the item this port could still accept: room left in
     * compatible stacks plus every empty slot at the item's stack limit.
     */
    static int64 StorableQuantity(
        const FCMLMachinePort& Port, const FCMLStableId& ItemId, const FCMLGameCatalog& Catalog);

    static bool TryStore(
        FCMLMachinePort& Port,
        const FCMLStableId& ItemId,
        int64 Amount,
        const FCMLGameCatalog& Catalog);

    static bool TryTake(FCMLMachinePort& Port, const FCMLStableId& ItemId, int64 Amount);
};

/**
 * The single authoritative transfer rule, ported from
 * CML.Simulation.Inventories.TransferRule.
 *
 * One function serves the player emptying a crate into a furnace, a crate
 * feeding a press, and anything else that moves items between two holders. Two
 * implementations of "move n of item x" would eventually disagree about a stack
 * limit or a capacity, and the disagreement would show up as material appearing
 * or vanishing — which no test written against either half would catch.
 *
 * Every transfer is all-or-nothing and has no observable intermediate state:
 * the whole move is measured against both sides before either is touched. C#
 * threw a `SimulationInvariantException` if the apply half then failed anyway;
 * here the caller's state is only overwritten on success, so a broken invariant
 * leaves the world untouched rather than half-moved.
 */
class CMLCORE_API FCMLTransferRule
{
public:
    static bool TryTransfer(
        const FCMLInventorySimulationState& Inventories,
        const FCMLMachineSimulationState& Machines,
        const FCMLGameCatalog& Catalog,
        const FCMLTransferEndpoint& Source,
        const FCMLTransferEndpoint& Destination,
        const FCMLStableId& ItemId,
        int64 Amount,
        FCMLInventorySimulationState& OutInventories,
        FCMLMachineSimulationState& OutMachines,
        ECMLTransferFailure& OutFailure);
};

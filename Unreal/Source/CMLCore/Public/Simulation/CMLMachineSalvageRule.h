#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLMachineBuildRule.h"

/** Why a dismantle was refused. */
UENUM(BlueprintType)
enum class ECMLSalvageRejection : uint8
{
    None = 0,
    /** There is no such node to take apart. */
    SalvageTargetMissing = 1,
    /** The node maps back to no carryable item. */
    RefundUnknown = 2,
    /** The inventory to refund into does not exist. */
    DestinationMissing = 3,
    /** It exists but cannot hold the refund and the cargo. */
    DestinationFull = 4
};

/**
 * Taking a placed thing apart again, ported from
 * CML.Simulation.Machines.MachineSalvageRule.
 *
 * Dismantling refunds two separate things and both have to fit: the item that
 * paid for the node, and everything the node was holding. It is all or nothing —
 * a partial refund would either delete cargo or leave a node that is half gone.
 */
class CMLCORE_API FCMLMachineSalvageRule
{
public:
    /**
     * Maps a placed node back to the item that paid for it.
     *
     * A crate and a machine are stored under their *definition* id, which is not
     * the id of the carryable item, so neither case can simply echo the
     * definition back.
     */
    static bool TryResolveRefund(
        ECMLMachineNodeKind Kind,
        const FCMLStableId& DefinitionId,
        FCMLStableId& OutRefundItemId,
        int64& OutRefundQuantity);

    static bool TryApply(
        const FCMLMachineSimulationState& Machines,
        const FCMLInventorySimulationState& Inventories,
        const FCMLGameCatalog& Catalog,
        const FCMLStableId& TargetInventoryId,
        const FCMLStableId& NodeId,
        FCMLMachineSimulationState& OutMachines,
        FCMLInventorySimulationState& OutInventories,
        ECMLSalvageRejection& OutRejection);
};

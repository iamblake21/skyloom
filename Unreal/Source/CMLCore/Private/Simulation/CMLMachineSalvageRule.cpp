#include "Simulation/CMLMachineSalvageRule.h"

#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"

namespace
{
    bool TryStorePortContents(
        const FCMLMachinePort& Port,
        const FCMLItemCatalog& Items,
        const int64 Capacity,
        FCMLInventoryState& Credited)
    {
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId.IsNone() || Slot.Quantity.Value == 0)
            {
                continue;
            }
            FCMLInventoryState Updated;
            ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
            if (!FCMLInventoryOperations::TryStoreEntire(
                    Credited, Items, Slot.ItemId,
                    static_cast<int64>(Slot.Quantity.Value), Capacity, Updated, Failure))
            {
                return false;
            }
            Credited = MoveTemp(Updated);
        }
        return true;
    }
}

bool FCMLMachineSalvageRule::TryResolveRefund(
    const ECMLMachineNodeKind Kind,
    const FCMLStableId& DefinitionId,
    FCMLStableId& OutRefundItemId,
    int64& OutRefundQuantity)
{
    using namespace CMLContentIds;
    OutRefundItemId = FCMLStableId::None();
    OutRefundQuantity = 0;

    switch (Kind)
    {
        case ECMLMachineNodeKind::Buffer:
            if (DefinitionId == WoodenCrate) { OutRefundItemId = WoodenCrateItem; }
            break;

        case ECMLMachineNodeKind::Machine:
            if (DefinitionId == MechanicalPress) { OutRefundItemId = MechanicalPressItem; }
            else if (DefinitionId == CrudeFurnace) { OutRefundItemId = CrudeFurnaceItem; }
            else if (DefinitionId == MechanicalDrill) { OutRefundItemId = MechanicalDrillItem; }
            break;

        case ECMLMachineNodeKind::Funnel:
        case ECMLMachineNodeKind::BeltModule:
            // A funnel and every belt module are stored under the id of the very
            // item that placed them, so the refund is the definition itself.
            if (DefinitionId == BeltFunnel
                || DefinitionId == BeltStraight
                || DefinitionId == BeltDriveUnit
                || DefinitionId == BeltCurve
                || DefinitionId == BeltCurveLeft
                || DefinitionId == BeltIncline)
            {
                OutRefundItemId = DefinitionId;
            }
            break;

        default:
            break;
    }

    if (OutRefundItemId.IsNone())
    {
        return false;
    }
    OutRefundQuantity = 1;
    return true;
}

bool FCMLMachineSalvageRule::TryApply(
    const FCMLMachineSimulationState& Machines,
    const FCMLInventorySimulationState& Inventories,
    const FCMLGameCatalog& Catalog,
    const FCMLStableId& TargetInventoryId,
    const FCMLStableId& NodeId,
    FCMLMachineSimulationState& OutMachines,
    FCMLInventorySimulationState& OutInventories,
    ECMLSalvageRejection& OutRejection)
{
    OutMachines = Machines;
    OutInventories = Inventories;
    OutRejection = ECMLSalvageRejection::None;

    const int32 NodeIndex = Machines.Nodes.IndexOfByPredicate(
        [&NodeId](const FCMLMachineNodeState& Candidate) { return Candidate.Id == NodeId; });
    if (NodeId.IsNone() || NodeIndex == INDEX_NONE)
    {
        OutRejection = ECMLSalvageRejection::SalvageTargetMissing;
        return false;
    }
    const FCMLMachineNodeState& Node = Machines.Nodes[NodeIndex];

    FCMLStableId RefundItemId;
    int64 RefundQuantity = 0;
    if (!TryResolveRefund(Node.Kind, Node.DefinitionId, RefundItemId, RefundQuantity))
    {
        OutRejection = ECMLSalvageRejection::RefundUnknown;
        return false;
    }

    const int32 InventoryIndex = Inventories.Inventories.IndexOfByPredicate(
        [&TargetInventoryId](const FCMLInventoryState& Candidate)
        {
            return Candidate.InventoryId == TargetInventoryId;
        });
    if (TargetInventoryId.IsNone() || InventoryIndex == INDEX_NONE)
    {
        OutRejection = ECMLSalvageRejection::DestinationMissing;
        return false;
    }

    const FCMLItemCatalog Items = Catalog.ToItemCatalog();
    FCMLContainerDefinition Container;
    const int64 Capacity =
        Catalog.TryGetContainer(
            Inventories.Inventories[InventoryIndex].ContainerDefinitionId, Container)
        ? Container.Capacity : 0;

    // Everything is accumulated against a working copy first. The node is only
    // removed once the whole refund has landed: a partial one would either
    // delete cargo or leave a node half gone.
    FCMLInventoryState Credited = Inventories.Inventories[InventoryIndex];
    ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
    FCMLInventoryState Updated;
    if (!FCMLInventoryOperations::TryStoreEntire(
            Credited, Items, RefundItemId, RefundQuantity, Capacity, Updated, Failure))
    {
        OutRejection = ECMLSalvageRejection::DestinationFull;
        return false;
    }
    Credited = MoveTemp(Updated);

    if (!TryStorePortContents(Node.Input, Items, Capacity, Credited))
    {
        OutRejection = ECMLSalvageRejection::DestinationFull;
        return false;
    }
    // A funnel, a belt and a crate share one store between input and output.
    // Counting both would refund their cargo twice.
    if (!Node.bInputOutputAliased
        && !TryStorePortContents(Node.Output, Items, Capacity, Credited))
    {
        OutRejection = ECMLSalvageRejection::DestinationFull;
        return false;
    }
    if (Node.bHasFuelPort && !TryStorePortContents(Node.Fuel, Items, Capacity, Credited))
    {
        OutRejection = ECMLSalvageRejection::DestinationFull;
        return false;
    }

    OutInventories.Inventories[InventoryIndex] = MoveTemp(Credited);
    OutMachines.Nodes.RemoveAt(NodeIndex);
    return true;
}

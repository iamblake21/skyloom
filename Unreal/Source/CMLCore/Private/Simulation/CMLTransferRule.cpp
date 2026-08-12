#include "Simulation/CMLTransferRule.h"

#include "Inventory/CMLInventoryOperations.h"

namespace
{
    /** Adding room across many slots must not wrap into a refusal. */
    int64 SaturatingAdd(const int64 A, const int64 B)
    {
        return (B > 0 && A > MAX_int64 - B) ? MAX_int64 : A + B;
    }

    /**
     * Where a transfer endpoint actually lives, once resolved.
     *
     * C# compared endpoints by object identity to catch a move from a holder to
     * itself. Value semantics cannot do that, so the storage is named instead:
     * an inventory by its index, a port by its node and which field it is.
     */
    struct FResolved
    {
        int32 InventoryIndex = INDEX_NONE;
        int32 NodeIndex = INDEX_NONE;
        ECMLMachinePortKind Port = ECMLMachinePortKind::None;

        bool IsInventory() const { return InventoryIndex != INDEX_NONE; }

        bool SharesStorageWith(const FResolved& Other) const
        {
            if (IsInventory() || Other.IsInventory())
            {
                return InventoryIndex == Other.InventoryIndex;
            }
            return NodeIndex == Other.NodeIndex && Port == Other.Port;
        }
    };

    FCMLMachinePort& PortOf(FCMLMachineNodeState& Node, const ECMLMachinePortKind Kind)
    {
        switch (Kind)
        {
            case ECMLMachinePortKind::Output: return Node.Output;
            case ECMLMachinePortKind::Fuel:   return Node.Fuel;
            default:                         return Node.Input;
        }
    }

    const FCMLMachinePort& PortOf(const FCMLMachineNodeState& Node, const ECMLMachinePortKind Kind)
    {
        return PortOf(const_cast<FCMLMachineNodeState&>(Node), Kind);
    }

    bool TryResolve(
        const FCMLInventorySimulationState& Inventories,
        const FCMLMachineSimulationState& Machines,
        const FCMLTransferEndpoint& Endpoint,
        FResolved& OutResolved)
    {
        OutResolved = FResolved();
        if (Endpoint.OwnerId.IsNone())
        {
            return false;
        }

        if (Endpoint.Kind == ECMLTransferEndpointKind::Inventory)
        {
            OutResolved.InventoryIndex = Inventories.Inventories.IndexOfByPredicate(
                [&Endpoint](const FCMLInventoryState& Candidate)
                {
                    return Candidate.InventoryId == Endpoint.OwnerId;
                });
            return OutResolved.InventoryIndex != INDEX_NONE;
        }

        if (Endpoint.Kind != ECMLTransferEndpointKind::MachinePort)
        {
            return false;
        }
        const int32 NodeIndex = Machines.Nodes.IndexOfByPredicate(
            [&Endpoint](const FCMLMachineNodeState& Candidate)
            {
                return Candidate.Id == Endpoint.OwnerId;
            });
        if (NodeIndex == INDEX_NONE)
        {
            return false;
        }

        const FCMLMachineNodeState& Node = Machines.Nodes[NodeIndex];
        switch (Endpoint.PortKind)
        {
            case ECMLMachinePortKind::Storage:
                // Only a buffer has a storage port. Asking a machine for one is a
                // mis-addressed transfer, not a transfer to its input.
                if (Node.Kind != ECMLMachineNodeKind::Buffer)
                {
                    return false;
                }
                OutResolved.Port = ECMLMachinePortKind::Input;
                break;

            case ECMLMachinePortKind::Input:
            case ECMLMachinePortKind::Output:
                if (Node.Kind != ECMLMachineNodeKind::Machine)
                {
                    return false;
                }
                OutResolved.Port = Endpoint.PortKind;
                break;

            case ECMLMachinePortKind::Fuel:
                if (Node.Kind != ECMLMachineNodeKind::Machine || !Node.bHasFuelPort)
                {
                    return false;
                }
                OutResolved.Port = ECMLMachinePortKind::Fuel;
                break;

            default:
                return false;
        }
        OutResolved.NodeIndex = NodeIndex;
        return true;
    }

    bool RecipeConsumes(
        const FCMLMachineNodeState& Node,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog)
    {
        FCMLRecipeDefinition Recipe;
        if (Node.ActiveRecipeId.IsNone() || !Catalog.TryGetRecipe(Node.ActiveRecipeId, Recipe))
        {
            return false;
        }
        for (const FCMLRecipeAmount& Input : Recipe.Inputs)
        {
            if (Input.ItemId == ItemId)
            {
                return true;
            }
        }
        return false;
    }

    bool MachineConsumesFuel(
        const FCMLMachineNodeState& Node,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog)
    {
        FCMLMachineDefinition Machine;
        return Catalog.TryGetMachine(Node.DefinitionId, Machine)
            && Machine.RequiresFuel()
            && Machine.FuelItemId == ItemId;
    }

    /** Whether the destination will take this item at all. */
    bool Admits(
        const FResolved& To,
        const FCMLMachineSimulationState& Machines,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog)
    {
        if (To.IsInventory())
        {
            return true;
        }
        const FCMLMachineNodeState& Node = Machines.Nodes[To.NodeIndex];
        switch (To.Port)
        {
            case ECMLMachinePortKind::Input:
                // A buffer's single port is reached as Storage but stored in
                // Input, so a crate admits anything while a machine's input
                // takes only what its recipe consumes.
                return Node.Kind == ECMLMachineNodeKind::Buffer
                    || RecipeConsumes(Node, ItemId, Catalog);
            case ECMLMachinePortKind::Fuel:
                return MachineConsumesFuel(Node, ItemId, Catalog);
            default:
                // An output is a result buffer and admits nothing from outside:
                // a hand-fed plate there would be indistinguishable from one the
                // machine made.
                return false;
        }
    }

    int64 CountAt(
        const FResolved& Endpoint,
        const FCMLInventorySimulationState& Inventories,
        const FCMLMachineSimulationState& Machines,
        const FCMLStableId& ItemId)
    {
        if (Endpoint.IsInventory())
        {
            return FCMLInventoryOperations::Count(
                Inventories.Inventories[Endpoint.InventoryIndex], ItemId);
        }
        return FCMLMachinePortOperations::Count(
            PortOf(Machines.Nodes[Endpoint.NodeIndex], Endpoint.Port), ItemId);
    }

    int64 StorableAt(
        const FResolved& Endpoint,
        const FCMLInventorySimulationState& Inventories,
        const FCMLMachineSimulationState& Machines,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog,
        const int64 InventoryCapacity)
    {
        if (Endpoint.IsInventory())
        {
            return FCMLInventoryOperations::StorableQuantity(
                Inventories.Inventories[Endpoint.InventoryIndex],
                Catalog.ToItemCatalog(), ItemId, InventoryCapacity);
        }

        const FCMLMachineNodeState& Node = Machines.Nodes[Endpoint.NodeIndex];
        const int64 Physical = FCMLMachinePortOperations::StorableQuantity(
            PortOf(Node, Endpoint.Port), ItemId, Catalog);

        FCMLMachineDefinition Machine;
        const bool bKnown = Catalog.TryGetMachine(Node.DefinitionId, Machine);
        if (Endpoint.Port == ECMLMachinePortKind::Input
            && Node.Kind == ECMLMachineNodeKind::Machine)
        {
            // A buffer cap on top of the physical room: without it a belt would
            // fill every input slot with one ingredient and a two-ingredient
            // recipe would deadlock with nowhere to put the second.
            const int64 Remaining = bKnown
                ? FMath::Max<int64>(0, Machine.InputBufferCapacityPerItem
                    - FCMLMachinePortOperations::Count(Node.Input, ItemId))
                : 0;
            return FMath::Min(Physical, Remaining);
        }
        if (Endpoint.Port == ECMLMachinePortKind::Fuel)
        {
            const int64 Remaining =
                (bKnown && Machine.RequiresFuel() && Machine.FuelItemId == ItemId
                    && Node.bHasFuelPort)
                ? FMath::Max<int64>(0, Machine.FuelBufferCapacityPerItem
                    - FCMLMachinePortOperations::Count(Node.Fuel, ItemId))
                : 0;
            return FMath::Min(Physical, Remaining);
        }
        return Physical;
    }
}

FCMLTransferEndpoint FCMLTransferEndpoint::Inventory(const FCMLStableId& InventoryId)
{
    FCMLTransferEndpoint Endpoint;
    Endpoint.Kind = ECMLTransferEndpointKind::Inventory;
    Endpoint.OwnerId = InventoryId;
    Endpoint.PortKind = ECMLMachinePortKind::None;
    return Endpoint;
}

FCMLTransferEndpoint FCMLTransferEndpoint::Port(
    const FCMLStableId& NodeId, const ECMLMachinePortKind Port)
{
    FCMLTransferEndpoint Endpoint;
    Endpoint.Kind = ECMLTransferEndpointKind::MachinePort;
    Endpoint.OwnerId = NodeId;
    Endpoint.PortKind = Port;
    return Endpoint;
}

int64 FCMLMachinePortOperations::Count(
    const FCMLMachinePort& Port, const FCMLStableId& ItemId)
{
    if (ItemId.IsNone())
    {
        return 0;
    }
    int64 Total = 0;
    for (const FCMLMachineSlot& Slot : Port.Slots)
    {
        if (Slot.ItemId == ItemId)
        {
            Total += static_cast<int64>(Slot.Quantity.Value);
        }
    }
    return Total;
}

int64 FCMLMachinePortOperations::TotalQuantity(const FCMLMachinePort& Port)
{
    int64 Total = 0;
    for (const FCMLMachineSlot& Slot : Port.Slots)
    {
        Total += static_cast<int64>(Slot.Quantity.Value);
    }
    return Total;
}

int64 FCMLMachinePortOperations::StorableQuantity(
    const FCMLMachinePort& Port,
    const FCMLStableId& ItemId,
    const FCMLGameCatalog& Catalog)
{
    FCMLItemDefinition Item;
    if (!Catalog.TryGetItem(ItemId, Item) || Item.MaxStack <= 0)
    {
        return 0;
    }
    int64 Storable = 0;
    for (const FCMLMachineSlot& Slot : Port.Slots)
    {
        const bool bEmpty = Slot.ItemId.IsNone() || Slot.Quantity.Value == 0;
        if (bEmpty)
        {
            Storable = SaturatingAdd(Storable, Item.MaxStack);
        }
        else if (Slot.ItemId == ItemId)
        {
            Storable = SaturatingAdd(Storable,
                FMath::Max<int64>(0, Item.MaxStack - static_cast<int64>(Slot.Quantity.Value)));
        }
    }
    return Storable;
}

bool FCMLMachinePortOperations::TryStore(
    FCMLMachinePort& Port,
    const FCMLStableId& ItemId,
    const int64 Amount,
    const FCMLGameCatalog& Catalog)
{
    if (Amount == 0)
    {
        return true;
    }
    FCMLItemDefinition Item;
    if (Amount < 0 || !Catalog.TryGetItem(ItemId, Item) || Item.MaxStack <= 0)
    {
        return false;
    }
    if (Amount > StorableQuantity(Port, ItemId, Catalog))
    {
        return false;
    }

    // Compatible stacks are topped up before an empty slot is opened, so a port
    // does not fragment while a stack still has room.
    int64 Remaining = Amount;
    for (FCMLMachineSlot& Slot : Port.Slots)
    {
        if (Remaining <= 0 || Slot.ItemId != ItemId || Slot.Quantity.Value == 0)
        {
            continue;
        }
        const int64 Inserted =
            FMath::Min(Remaining, Item.MaxStack - static_cast<int64>(Slot.Quantity.Value));
        if (Inserted <= 0)
        {
            continue;
        }
        Slot.Quantity.Value += static_cast<uint64>(Inserted);
        Remaining -= Inserted;
    }
    for (FCMLMachineSlot& Slot : Port.Slots)
    {
        if (Remaining <= 0 || !(Slot.ItemId.IsNone() || Slot.Quantity.Value == 0))
        {
            continue;
        }
        const int64 Inserted = FMath::Min(Remaining, Item.MaxStack);
        Slot.ItemId = ItemId;
        Slot.Quantity.Value = static_cast<uint64>(Inserted);
        Remaining -= Inserted;
    }
    return Remaining == 0;
}

bool FCMLMachinePortOperations::TryTake(
    FCMLMachinePort& Port, const FCMLStableId& ItemId, const int64 Amount)
{
    if (Amount == 0)
    {
        return true;
    }
    if (Amount < 0 || ItemId.IsNone() || Count(Port, ItemId) < Amount)
    {
        return false;
    }
    int64 Remaining = Amount;
    for (FCMLMachineSlot& Slot : Port.Slots)
    {
        if (Remaining <= 0 || Slot.ItemId != ItemId)
        {
            continue;
        }
        const int64 Removed = FMath::Min(Remaining, static_cast<int64>(Slot.Quantity.Value));
        const int64 Retained = static_cast<int64>(Slot.Quantity.Value) - Removed;
        if (Retained == 0)
        {
            Slot.ItemId = FCMLStableId::None();
            Slot.Quantity.Value = 0;
        }
        else
        {
            Slot.Quantity.Value = static_cast<uint64>(Retained);
        }
        Remaining -= Removed;
    }
    return Remaining == 0;
}

bool FCMLTransferRule::TryTransfer(
    const FCMLInventorySimulationState& Inventories,
    const FCMLMachineSimulationState& Machines,
    const FCMLGameCatalog& Catalog,
    const FCMLTransferEndpoint& Source,
    const FCMLTransferEndpoint& Destination,
    const FCMLStableId& ItemId,
    const int64 Amount,
    FCMLInventorySimulationState& OutInventories,
    FCMLMachineSimulationState& OutMachines,
    ECMLTransferFailure& OutFailure)
{
    OutInventories = Inventories;
    OutMachines = Machines;

    if (Amount <= 0)
    {
        OutFailure = ECMLTransferFailure::ZeroAmount;
        return false;
    }
    FCMLItemDefinition Item;
    if (ItemId.IsNone() || !Catalog.TryGetItem(ItemId, Item))
    {
        OutFailure = ECMLTransferFailure::UnknownItem;
        return false;
    }
    if (Source == Destination)
    {
        OutFailure = ECMLTransferFailure::SameEndpoint;
        return false;
    }

    FResolved From;
    if (!TryResolve(Inventories, Machines, Source, From))
    {
        OutFailure = ECMLTransferFailure::UnknownSource;
        return false;
    }
    FResolved To;
    if (!TryResolve(Inventories, Machines, Destination, To))
    {
        OutFailure = ECMLTransferFailure::UnknownDestination;
        return false;
    }
    // Two differently spelled endpoints can still name one store: a node kind
    // that exposed one port under two names would otherwise run the take and
    // the store against the same slots.
    if (From.SharesStorageWith(To))
    {
        OutFailure = ECMLTransferFailure::SameEndpoint;
        return false;
    }

    if (!Admits(To, Machines, ItemId, Catalog))
    {
        OutFailure = ECMLTransferFailure::NotAdmitted;
        return false;
    }

    // An inventory's capacity is a property of its container definition.
    const auto InventoryCapacity = [&Catalog](const FCMLInventoryState& Inventory)
    {
        FCMLContainerDefinition Container;
        return Catalog.TryGetContainer(Inventory.ContainerDefinitionId, Container)
            ? Container.Capacity : 0;
    };

    if (CountAt(From, Inventories, Machines, ItemId) < Amount)
    {
        OutFailure = ECMLTransferFailure::InsufficientSource;
        return false;
    }
    const int64 DestinationCapacity = To.IsInventory()
        ? InventoryCapacity(Inventories.Inventories[To.InventoryIndex]) : 0;
    if (StorableAt(To, Inventories, Machines, ItemId, Catalog, DestinationCapacity) < Amount)
    {
        OutFailure = ECMLTransferFailure::DestinationFull;
        return false;
    }

    // Both sides are edited on the copies. The caller's state is replaced only
    // if every step lands, so a broken invariant leaves the world untouched
    // rather than half-moved.
    const FCMLItemCatalog Items = Catalog.ToItemCatalog();
    bool bApplied = true;
    ECMLInventoryFailure InventoryFailure = ECMLInventoryFailure::None;

    if (From.IsInventory())
    {
        FCMLInventoryState Updated;
        bApplied = FCMLInventoryOperations::TryTakeEntire(
            OutInventories.Inventories[From.InventoryIndex], ItemId, Amount,
            Updated, InventoryFailure);
        if (bApplied)
        {
            OutInventories.Inventories[From.InventoryIndex] = MoveTemp(Updated);
        }
    }
    else
    {
        bApplied = FCMLMachinePortOperations::TryTake(
            PortOf(OutMachines.Nodes[From.NodeIndex], From.Port), ItemId, Amount);
    }

    if (bApplied)
    {
        if (To.IsInventory())
        {
            FCMLInventoryState Updated;
            bApplied = FCMLInventoryOperations::TryStoreEntire(
                OutInventories.Inventories[To.InventoryIndex], Items, ItemId, Amount,
                DestinationCapacity, Updated, InventoryFailure);
            if (bApplied)
            {
                OutInventories.Inventories[To.InventoryIndex] = MoveTemp(Updated);
            }
        }
        else
        {
            bApplied = FCMLMachinePortOperations::TryStore(
                PortOf(OutMachines.Nodes[To.NodeIndex], To.Port), ItemId, Amount, Catalog);
        }
    }

    if (!bApplied)
    {
        OutInventories = Inventories;
        OutMachines = Machines;
        OutFailure = ECMLTransferFailure::DestinationFull;
        return false;
    }

    // A buffer's input and output are the same store; the aliased copy has to
    // follow, or the crate would show its old contents through its other face.
    if (!To.IsInventory() && OutMachines.Nodes[To.NodeIndex].bInputOutputAliased)
    {
        OutMachines.Nodes[To.NodeIndex].Output = OutMachines.Nodes[To.NodeIndex].Input;
    }
    if (!From.IsInventory() && OutMachines.Nodes[From.NodeIndex].bInputOutputAliased)
    {
        OutMachines.Nodes[From.NodeIndex].Output = OutMachines.Nodes[From.NodeIndex].Input;
    }

    OutFailure = ECMLTransferFailure::None;
    return true;
}

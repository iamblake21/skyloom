#include "Inventory/CMLInventoryOperations.h"

namespace
{
    /** Saturating add, so a huge slot total cannot wrap into a small one. */
    int64 SaturatingAdd(const int64 Left, const int64 Right)
    {
        if (Right > 0 && Left > MAX_int64 - Right)
        {
            return MAX_int64;
        }
        return Left + Right;
    }

    int64 TotalQuantityOf(const FCMLInventoryState& Inventory)
    {
        int64 Total = 0;
        for (const FCMLInventorySlot& Slot : Inventory.Slots)
        {
            if (Slot.bHasStack)
            {
                Total = SaturatingAdd(Total, Slot.Stack.Quantity.Value);
            }
        }
        return Total;
    }
}

namespace
{
    /** Zero means an empty slot: a stack of nothing does not exist. */
    void WriteSlot(
        FCMLInventorySlot& Slot, const FCMLStableId& ItemId, const int64 Quantity)
    {
        if (Quantity <= 0)
        {
            Slot.bHasStack = false;
            Slot.Stack = FCMLItemStack();
            return;
        }
        Slot.bHasStack = true;
        Slot.Stack.ItemId = ItemId;
        Slot.Stack.Quantity.Value = static_cast<uint64>(Quantity);
    }
}

bool FCMLInventoryOperations::TryMoveWithinInventory(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const int32 SourceSlotIndex,
    const int32 DestinationSlotIndex,
    const int64 Amount,
    FCMLInventoryState& OutUpdated,
    ECMLInventoryFailure& OutFailure)
{
    OutUpdated = Inventory;
    OutFailure = ECMLInventoryFailure::None;

    if (!Inventory.Slots.IsValidIndex(SourceSlotIndex)
        || !Inventory.Slots.IsValidIndex(DestinationSlotIndex)
        || Amount < 0)
    {
        OutFailure = ECMLInventoryFailure::InvalidDefinition;
        return false;
    }
    if (SourceSlotIndex == DestinationSlotIndex)
    {
        // Not an error: dropping a stack where it already is is an ordinary
        // gesture in the panel and simply has to do nothing.
        return true;
    }

    const FCMLInventorySlot& Source = Inventory.Slots[SourceSlotIndex];
    if (!Source.bHasStack)
    {
        OutFailure = ECMLInventoryFailure::InsufficientQuantity;
        return false;
    }

    const int64 Held = static_cast<int64>(Source.Stack.Quantity.Value);
    const int64 Moving = Amount == 0 ? Held : Amount;
    if (Moving > Held)
    {
        OutFailure = ECMLInventoryFailure::InsufficientQuantity;
        return false;
    }

    FCMLItemDefinition Item;
    if (!Catalog.TryGetItem(Source.Stack.ItemId, Item) || Item.MaxStack <= 0)
    {
        OutFailure = ECMLInventoryFailure::UnknownItem;
        return false;
    }

    const FCMLInventorySlot& Destination = Inventory.Slots[DestinationSlotIndex];
    if (!Destination.bHasStack)
    {
        WriteSlot(OutUpdated.Slots[DestinationSlotIndex], Source.Stack.ItemId, Moving);
        WriteSlot(OutUpdated.Slots[SourceSlotIndex], Source.Stack.ItemId, Held - Moving);
        return true;
    }

    if (Destination.Stack.ItemId == Source.Stack.ItemId)
    {
        const int64 Room = Item.MaxStack - static_cast<int64>(Destination.Stack.Quantity.Value);
        if (Room <= 0)
        {
            OutFailure = ECMLInventoryFailure::CapacityExceeded;
            return false;
        }
        const int64 Merged = FMath::Min(Room, Moving);
        WriteSlot(OutUpdated.Slots[DestinationSlotIndex], Source.Stack.ItemId,
            static_cast<int64>(Destination.Stack.Quantity.Value) + Merged);
        WriteSlot(OutUpdated.Slots[SourceSlotIndex], Source.Stack.ItemId, Held - Merged);
        return true;
    }

    if (Moving != Held)
    {
        // A partial swap: the remainder of the source stack would have nowhere
        // to stay, because its slot is about to be taken by the other item.
        // Better to refuse than to invent a destination for it.
        OutFailure = ECMLInventoryFailure::CapacityExceeded;
        return false;
    }

    const FCMLItemStack Other = Destination.Stack;
    WriteSlot(OutUpdated.Slots[DestinationSlotIndex], Source.Stack.ItemId, Held);
    WriteSlot(OutUpdated.Slots[SourceSlotIndex], Other.ItemId,
        static_cast<int64>(Other.Quantity.Value));
    return true;
}

bool FCMLItemCatalog::TryGetItem(const FCMLStableId& ItemId, FCMLItemDefinition& OutItem) const
{
    for (const FCMLItemDefinition& Item : Items)
    {
        if (Item.ItemId == ItemId)
        {
            OutItem = Item;
            return true;
        }
    }
    return false;
}

int64 FCMLInventoryOperations::Count(
    const FCMLInventoryState& Inventory,
    const FCMLStableId& ItemId)
{
    int64 Total = 0;
    for (const FCMLInventorySlot& Slot : Inventory.Slots)
    {
        if (Slot.bHasStack && Slot.Stack.ItemId == ItemId)
        {
            Total = SaturatingAdd(Total, Slot.Stack.Quantity.Value);
        }
    }
    return Total;
}

int64 FCMLInventoryOperations::StorableQuantity(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const FCMLStableId& ItemId,
    const int64 Capacity)
{
    FCMLItemDefinition Item;
    if (!Catalog.TryGetItem(ItemId, Item) || Item.MaxStack <= 0)
    {
        return 0;
    }

    // Two independent limits: the inventory's own capacity, and how much the
    // slots can physically hold. The smaller one decides.
    const int64 RemainingByCapacity = FMath::Max<int64>(0, Capacity - TotalQuantityOf(Inventory));
    int64 RemainingBySlots = 0;
    for (const FCMLInventorySlot& Slot : Inventory.Slots)
    {
        int64 Available;
        if (!Slot.bHasStack)
        {
            Available = Item.MaxStack;
        }
        else if (Slot.Stack.ItemId == ItemId)
        {
            Available = FMath::Max<int64>(0, Item.MaxStack - Slot.Stack.Quantity.Value);
        }
        else
        {
            continue;
        }
        RemainingBySlots = SaturatingAdd(RemainingBySlots, Available);
    }
    return FMath::Min(RemainingByCapacity, RemainingBySlots);
}

bool FCMLInventoryOperations::TryStoreEntire(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const FCMLStableId& ItemId,
    const int64 Amount,
    const int64 Capacity,
    FCMLInventoryState& OutUpdated,
    ECMLInventoryFailure& OutFailure)
{
    OutUpdated = Inventory;
    OutFailure = ECMLInventoryFailure::None;

    if (Amount < 0)
    {
        OutFailure = ECMLInventoryFailure::InvalidDefinition;
        return false;
    }

    FCMLItemDefinition Item;
    if (!Catalog.TryGetItem(ItemId, Item) || Item.MaxStack <= 0)
    {
        OutFailure = ECMLInventoryFailure::UnknownItem;
        return false;
    }
    if (Amount == 0)
    {
        return true;
    }
    if (Amount > StorableQuantity(Inventory, Catalog, ItemId, Capacity))
    {
        OutFailure = ECMLInventoryFailure::CapacityExceeded;
        return false;
    }

    TArray<FCMLInventorySlot> NextSlots = Inventory.Slots;
    int64 Remaining = Amount;

    // Top up compatible stacks first: opening a new slot while an existing
    // stack still has room would fragment the inventory for no reason.
    for (FCMLInventorySlot& Slot : NextSlots)
    {
        if (Remaining == 0)
        {
            break;
        }
        if (!Slot.bHasStack || Slot.Stack.ItemId != ItemId)
        {
            continue;
        }
        const int64 Space = FMath::Max<int64>(0, Item.MaxStack - Slot.Stack.Quantity.Value);
        const int64 Moved = FMath::Min(Space, Remaining);
        Slot.Stack.Quantity = FCMLNonNegativeQuantity(Slot.Stack.Quantity.Value + Moved);
        Remaining -= Moved;
    }

    for (FCMLInventorySlot& Slot : NextSlots)
    {
        if (Remaining == 0)
        {
            break;
        }
        if (Slot.bHasStack)
        {
            continue;
        }
        const int64 Moved = FMath::Min(Item.MaxStack, Remaining);
        Slot.bHasStack = true;
        Slot.Stack.ItemId = ItemId;
        Slot.Stack.Quantity = FCMLNonNegativeQuantity(Moved);
        Remaining -= Moved;
    }

    if (Remaining != 0)
    {
        // StorableQuantity said it would fit, so this cannot happen without a
        // defect in the preflight. Refuse rather than store a partial amount.
        OutUpdated = Inventory;
        OutFailure = ECMLInventoryFailure::CapacityExceeded;
        return false;
    }

    OutUpdated.Slots = MoveTemp(NextSlots);
    return true;
}

bool FCMLInventoryOperations::TryTakeEntire(
    const FCMLInventoryState& Inventory,
    const FCMLStableId& ItemId,
    const int64 Amount,
    FCMLInventoryState& OutUpdated,
    ECMLInventoryFailure& OutFailure)
{
    OutUpdated = Inventory;
    OutFailure = ECMLInventoryFailure::None;

    if (Amount < 0)
    {
        OutFailure = ECMLInventoryFailure::InvalidDefinition;
        return false;
    }
    if (Amount == 0)
    {
        return true;
    }
    if (Amount > Count(Inventory, ItemId))
    {
        OutFailure = ECMLInventoryFailure::InsufficientQuantity;
        return false;
    }

    TArray<FCMLInventorySlot> NextSlots = Inventory.Slots;
    int64 Remaining = Amount;
    for (int32 Index = NextSlots.Num() - 1; Index >= 0 && Remaining > 0; --Index)
    {
        FCMLInventorySlot& Slot = NextSlots[Index];
        if (!Slot.bHasStack || Slot.Stack.ItemId != ItemId)
        {
            continue;
        }
        const int64 Taken = FMath::Min(Slot.Stack.Quantity.Value, Remaining);
        const int64 Left = Slot.Stack.Quantity.Value - Taken;
        Remaining -= Taken;
        if (Left == 0)
        {
            Slot = FCMLInventorySlot();
        }
        else
        {
            Slot.Stack.Quantity = FCMLNonNegativeQuantity(Left);
        }
    }

    if (Remaining != 0)
    {
        OutUpdated = Inventory;
        OutFailure = ECMLInventoryFailure::InsufficientQuantity;
        return false;
    }

    OutUpdated.Slots = MoveTemp(NextSlots);
    return true;
}

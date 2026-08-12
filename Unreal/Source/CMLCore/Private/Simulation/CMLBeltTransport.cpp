#include "Simulation/CMLBeltTransport.h"

#include "Simulation/CMLMachineCycle.h"

namespace
{
    /** Room for one more unit of this item in the port, honouring max stack. */
    bool PortHasRoomForOne(
        const FCMLMachinePort& Port,
        const FCMLStableId& ItemId,
        const FCMLGameCatalog& Catalog)
    {
        FCMLItemDefinition Item;
        if (!Catalog.TryGetItem(ItemId, Item) || Item.MaxStack <= 0)
        {
            return false;
        }
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId.IsNone())
            {
                return true;
            }
            if (Slot.ItemId == ItemId && Slot.Quantity.Value < Item.MaxStack)
            {
                return true;
            }
        }
        return false;
    }

    void InsertOne(FCMLMachinePort& Port, const FCMLStableId& ItemId, const FCMLGameCatalog& Catalog)
    {
        FCMLItemDefinition Item;
        Catalog.TryGetItem(ItemId, Item);
        // Top up an existing stack before opening a slot, matching how the
        // inventory places items.
        for (FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId == ItemId && Slot.Quantity.Value < Item.MaxStack)
            {
                Slot.Quantity = FCMLNonNegativeQuantity(Slot.Quantity.Value + 1);
                return;
            }
        }
        for (FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId.IsNone())
            {
                Slot.ItemId = ItemId;
                Slot.Quantity = FCMLNonNegativeQuantity(1);
                return;
            }
        }
    }
}

void FCMLBeltTransport::AdvanceLaneItems(FCMLBeltLaneState& Lane)
{
    // The ceiling starts at the lane end and drops to `target - spacing` behind
    // each item. Walking front-first is what makes the queue form from the exit
    // backwards instead of items piling into each other.
    int64 CeilingAhead = Lane.LengthMillimetres;
    for (FCMLBeltLaneItem& Item : Lane.Items)
    {
        int64 Target = FMath::Min(CeilingAhead, Item.PositionMillimetres + Lane.SpeedMillimetresPerTick);
        if (Target > Item.PositionMillimetres)
        {
            Item.PositionMillimetres = Target;
        }
        else
        {
            // Never move backwards: a blocked item holds its ground rather than
            // being dragged back by the one in front.
            Target = Item.PositionMillimetres;
        }
        CeilingAhead = FMath::Max<int64>(0, Target - Lane.SpacingMillimetres);
    }
}

int32 FCMLBeltTransport::DeliverLaneItems(
    FCMLBeltLaneState& Lane,
    FCMLMachineNodeState& Destination,
    const FCMLGameCatalog& Catalog)
{
    int32 Delivered = 0;
    while (Lane.Items.Num() > 0)
    {
        const FCMLBeltLaneItem& Front = Lane.Items[0];
        if (Front.PositionMillimetres < Lane.LengthMillimetres)
        {
            return Delivered;
        }
        if (!FCMLMachineCycle::Admits(Destination, Front.ItemId, Catalog)
            || !PortHasRoomForOne(Destination.Input, Front.ItemId, Catalog))
        {
            // The destination will not take it. The item stays at the exit and
            // the ones behind queue against it: backpressure with a position,
            // not a flag.
            return Delivered;
        }

        InsertOne(Destination.Input, Front.ItemId, Catalog);
        Lane.Items.RemoveAt(0);
        ++Lane.DeliveredUnits;
        ++Delivered;
    }
    return Delivered;
}

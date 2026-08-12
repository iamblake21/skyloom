#include "Inventory/CMLInventoryOperations.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId Iron(0, 1);
    const FCMLStableId Copper(0, 2);
    const FCMLStableId Unknown(0, 99);

    FCMLItemCatalog MakeCatalog(const int64 MaxStack = 10)
    {
        FCMLItemCatalog Catalog;
        Catalog.Items.Add({Iron, MaxStack});
        Catalog.Items.Add({Copper, MaxStack});
        return Catalog;
    }

    FCMLInventoryState MakeInventory(const int32 SlotCount)
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = FCMLStableId(0, 7);
        Inventory.Slots.AddDefaulted(SlotCount);
        return Inventory;
    }

    void Fill(FCMLInventoryState& Inventory, const int32 Index, const FCMLStableId& ItemId, const int64 Quantity)
    {
        Inventory.Slots[Index].bHasStack = true;
        Inventory.Slots[Index].Stack.ItemId = ItemId;
        Inventory.Slots[Index].Stack.Quantity = FCMLNonNegativeQuantity(Quantity);
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLInventoryOperationsTest,
    "CML.Core.Inventory.Operations",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLInventoryOperationsTest::RunTest(const FString& Parameters)
{
    const FCMLItemCatalog Catalog = MakeCatalog();
    const int64 Capacity = 1000;

    // An unknown item is refused: the inventory has no way to know its stack
    // size, so accepting it would be guessing.
    {
        FCMLInventoryState Inventory = MakeInventory(2);
        FCMLInventoryState Updated;
        ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
        TestFalse(TEXT("Unknown item is refused"),
            FCMLInventoryOperations::TryStoreEntire(
                Inventory, Catalog, Unknown, 1, Capacity, Updated, Failure));
        TestEqual(TEXT("Failure is UnknownItem"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLInventoryFailure::UnknownItem));
    }

    // Capacity by slots: two slots of ten hold twenty, not twenty-one.
    {
        FCMLInventoryState Inventory = MakeInventory(2);
        TestEqual(TEXT("Two empty slots of ten"),
            FCMLInventoryOperations::StorableQuantity(Inventory, Catalog, Iron, Capacity),
            static_cast<int64>(20));

        FCMLInventoryState Updated;
        ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
        TestFalse(TEXT("One more than fits is refused"),
            FCMLInventoryOperations::TryStoreEntire(
                Inventory, Catalog, Iron, 21, Capacity, Updated, Failure));
        TestEqual(TEXT("Failure is CapacityExceeded"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLInventoryFailure::CapacityExceeded));
        // All-or-nothing: nothing was stored on the way to discovering the refusal.
        TestFalse(TEXT("The refused store left slot 0 empty"), Updated.Slots[0].bHasStack);
        TestFalse(TEXT("The refused store left slot 1 empty"), Updated.Slots[1].bHasStack);
    }

    // The inventory's own capacity can bind before the slots do.
    {
        FCMLInventoryState Inventory = MakeInventory(4);
        TestEqual(TEXT("Capacity binds before slots"),
            FCMLInventoryOperations::StorableQuantity(Inventory, Catalog, Iron, 15),
            static_cast<int64>(15));
    }

    // Compatible stacks top up before an empty slot opens.
    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Iron, 7);
        FCMLInventoryState Updated;
        ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
        TestTrue(TEXT("Storing two succeeds"),
            FCMLInventoryOperations::TryStoreEntire(
                Inventory, Catalog, Iron, 2, Capacity, Updated, Failure));
        TestEqual(TEXT("The partial stack was topped up"),
            Updated.Slots[0].Stack.Quantity.Value, static_cast<int64>(9));
        TestFalse(TEXT("No new slot was opened"), Updated.Slots[1].bHasStack);
    }

    // Overflow past a full stack spills into the next empty slot.
    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Iron, 8);
        FCMLInventoryState Updated;
        ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
        TestTrue(TEXT("Storing five succeeds"),
            FCMLInventoryOperations::TryStoreEntire(
                Inventory, Catalog, Iron, 5, Capacity, Updated, Failure));
        TestEqual(TEXT("The first stack filled to max"),
            Updated.Slots[0].Stack.Quantity.Value, static_cast<int64>(10));
        TestEqual(TEXT("The remainder opened a slot"),
            Updated.Slots[1].Stack.Quantity.Value, static_cast<int64>(3));
    }

    // A different item never merges into an occupied slot.
    {
        FCMLInventoryState Inventory = MakeInventory(2);
        Fill(Inventory, 0, Iron, 5);
        TestEqual(TEXT("Only the empty slot accepts copper"),
            FCMLInventoryOperations::StorableQuantity(Inventory, Catalog, Copper, Capacity),
            static_cast<int64>(10));
    }

    // Taking is all-or-nothing too.
    {
        FCMLInventoryState Inventory = MakeInventory(3);
        Fill(Inventory, 0, Iron, 4);
        Fill(Inventory, 2, Iron, 3);
        TestEqual(TEXT("Count spans slots"),
            FCMLInventoryOperations::Count(Inventory, Iron), static_cast<int64>(7));

        FCMLInventoryState Updated;
        ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
        TestFalse(TEXT("Taking more than held is refused"),
            FCMLInventoryOperations::TryTakeEntire(Inventory, Iron, 8, Updated, Failure));
        TestEqual(TEXT("Failure is InsufficientQuantity"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::InsufficientQuantity));
        TestEqual(TEXT("The refused take changed nothing"),
            FCMLInventoryOperations::Count(Updated, Iron), static_cast<int64>(7));

        TestTrue(TEXT("Taking five succeeds"),
            FCMLInventoryOperations::TryTakeEntire(Inventory, Iron, 5, Updated, Failure));
        TestEqual(TEXT("Two are left"),
            FCMLInventoryOperations::Count(Updated, Iron), static_cast<int64>(2));
        // Drained from the back, so the earliest slot keeps its stack.
        TestTrue(TEXT("The first slot survives"), Updated.Slots[0].bHasStack);
        TestFalse(TEXT("The emptied slot is cleared"), Updated.Slots[2].bHasStack);
    }

    // A zero amount is a no-op that succeeds, not a refusal.
    {
        FCMLInventoryState Inventory = MakeInventory(1);
        FCMLInventoryState Updated;
        ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
        TestTrue(TEXT("Storing zero succeeds"),
            FCMLInventoryOperations::TryStoreEntire(
                Inventory, Catalog, Iron, 0, Capacity, Updated, Failure));
        TestTrue(TEXT("Taking zero succeeds"),
            FCMLInventoryOperations::TryTakeEntire(Inventory, Iron, 0, Updated, Failure));
        TestFalse(TEXT("Nothing was stored"), Updated.Slots[0].bHasStack);
    }
    return true;
}
#endif

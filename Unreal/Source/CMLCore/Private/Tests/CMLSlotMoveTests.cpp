#include "Simulation/CMLSlotMoveCommand.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Misc/AutomationTest.h"

namespace
{
    FCMLItemDefinition Item(const FCMLStableId& Id, const int64 MaxStack)
    {
        FCMLItemDefinition Definition;
        Definition.ItemId = Id;
        Definition.MaxStack = MaxStack;
        return Definition;
    }

    FCMLItemCatalog MakeCatalog()
    {
        FCMLItemCatalog Catalog;
        Catalog.Items.Add(Item(CMLContentIds::RawIron, 10));
        Catalog.Items.Add(Item(CMLContentIds::Stone, 10));
        return Catalog;
    }

    FCMLInventoryState MakeInventory()
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = CMLContentIds::PlayerInventory;
        Inventory.ContainerDefinitionId = CMLContentIds::PlayerInventory;
        Inventory.Slots.SetNum(4);
        return Inventory;
    }

    void Place(FCMLInventoryState& Inventory, const int32 Index, const FCMLStableId& Id, const uint64 Quantity)
    {
        Inventory.Slots[Index].bHasStack = true;
        Inventory.Slots[Index].Stack.ItemId = Id;
        Inventory.Slots[Index].Stack.Quantity.Value = Quantity;
    }

    int64 QuantityAt(const FCMLInventoryState& Inventory, const int32 Index)
    {
        return Inventory.Slots[Index].bHasStack
            ? static_cast<int64>(Inventory.Slots[Index].Stack.Quantity.Value) : 0;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLSlotMoveTest,
    "CML.Core.Inventory.SlotMove",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLSlotMoveTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLItemCatalog Catalog = MakeCatalog();
    FCMLInventoryState Updated;
    ECMLInventoryFailure Failure = ECMLInventoryFailure::None;

    // Onto an empty slot: a split, with the remainder left behind.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, RawIron, 7);
        TestTrue(TEXT("Splitting onto an empty slot works"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 2, 3, Updated, Failure));
        TestEqual(TEXT("Three moved"), QuantityAt(Updated, 2), static_cast<int64>(3));
        TestEqual(TEXT("Four stayed"), QuantityAt(Updated, 0), static_cast<int64>(4));

        // Zero means the whole stack, which is what a plain drag sends.
        TestTrue(TEXT("Zero moves everything"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 1, 0, Updated, Failure));
        TestEqual(TEXT("The source is emptied"), QuantityAt(Updated, 0), static_cast<int64>(0));
        TestFalse(TEXT("And holds nothing"), Updated.Slots[0].bHasStack);
        TestEqual(TEXT("The destination has it all"),
            QuantityAt(Updated, 1), static_cast<int64>(7));
    }

    // Onto the same item: a merge, capped by the stack limit, with whatever did
    // not fit staying where it was.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, RawIron, 6);
        Place(Inventory, 1, RawIron, 8);
        TestTrue(TEXT("Merging works"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 1, 0, Updated, Failure));
        TestEqual(TEXT("The destination fills to its limit"),
            QuantityAt(Updated, 1), static_cast<int64>(10));
        TestEqual(TEXT("The overflow stays behind"),
            QuantityAt(Updated, 0), static_cast<int64>(4));

        Place(Inventory, 1, RawIron, 10);
        TestFalse(TEXT("Merging onto a full stack is refused"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 1, 0, Updated, Failure));
        TestEqual(TEXT("The reason is capacity"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::CapacityExceeded));
    }

    // Onto a different item: a swap, and only a whole stack may swap.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, RawIron, 5);
        Place(Inventory, 3, Stone, 2);

        TestTrue(TEXT("A whole stack swaps"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 3, 0, Updated, Failure));
        TestTrue(TEXT("Iron went there"), Updated.Slots[3].Stack.ItemId == RawIron);
        TestEqual(TEXT("All of it"), QuantityAt(Updated, 3), static_cast<int64>(5));
        TestTrue(TEXT("Stone came back"), Updated.Slots[0].Stack.ItemId == Stone);
        TestEqual(TEXT("All of it"), QuantityAt(Updated, 0), static_cast<int64>(2));

        // The remainder would have nowhere to stay once its slot is taken.
        TestFalse(TEXT("A partial swap is refused"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 3, 2, Updated, Failure));
        TestEqual(TEXT("The reason is capacity"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::CapacityExceeded));
    }

    // Dropping a stack where it already is is an ordinary gesture, not an error.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 1, RawIron, 4);
        TestTrue(TEXT("A move onto itself succeeds"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 1, 1, 0, Updated, Failure));
        TestEqual(TEXT("And changes nothing"), QuantityAt(Updated, 1), static_cast<int64>(4));
    }

    // Refusals.
    {
        FCMLInventoryState Inventory = MakeInventory();
        Place(Inventory, 0, RawIron, 4);
        TestFalse(TEXT("An index off the end is refused"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 9, 0, Updated, Failure));
        TestEqual(TEXT("The reason is the definition"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::InvalidDefinition));

        TestFalse(TEXT("An empty source is refused"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 2, 3, 0, Updated, Failure));
        TestEqual(TEXT("The reason is quantity"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::InsufficientQuantity));

        TestFalse(TEXT("More than is held is refused"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Catalog, 0, 1, 9, Updated, Failure));
        TestEqual(TEXT("The reason is quantity"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::InsufficientQuantity));

        FCMLItemCatalog Empty;
        TestFalse(TEXT("An item the catalog does not define is refused"),
            FCMLInventoryOperations::TryMoveWithinInventory(
                Inventory, Empty, 0, 1, 0, Updated, Failure));
        TestEqual(TEXT("The reason is the item"), static_cast<int32>(Failure),
            static_cast<int32>(ECMLInventoryFailure::UnknownItem));
    }

    // The payload is four fixed big-endian bytes, so decoding depends on
    // neither a length nor the platform's byte order.
    {
        TArray<uint8> Payload;
        TestTrue(TEXT("Indices encode"),
            FCMLSlotMoveCommandPayload::TryEncode(0x0102, 0x0304, Payload));
        TestEqual(TEXT("Four bytes"), Payload.Num(), 4);
        TestEqual(TEXT("Most significant byte first"), static_cast<int32>(Payload[0]), 0x01);
        TestEqual(TEXT("Then the least"), static_cast<int32>(Payload[1]), 0x02);
        TestEqual(TEXT("And the same for the destination"),
            static_cast<int32>(Payload[2]), 0x03);
        TestEqual(TEXT("And the same for the destination"),
            static_cast<int32>(Payload[3]), 0x04);

        TestFalse(TEXT("An index past 16 bits is refused"),
            FCMLSlotMoveCommandPayload::TryEncode(0x10000, 0, Payload));
        TestFalse(TEXT("A negative index is refused"),
            FCMLSlotMoveCommandPayload::TryEncode(-1, 0, Payload));

        FCMLSimulationCommand Command;
        TestTrue(TEXT("A command is well-formed"),
            FCMLSimulationCommand::TryCreate(
                FCMLSimulationTick(5), 1,
                FCMLSlotMoveCommandPayload::CommandKind(), Command));
        Command.InitiatorId = PlayerInventory;
        Command.QuantizedValue = 3;
        TestTrue(TEXT("Its payload encodes"),
            FCMLSlotMoveCommandPayload::TryEncode(9, 11, Command.Payload));

        FCMLStableId InventoryId;
        int32 Source = 0;
        int32 Destination = 0;
        int64 MoveAmount = 0;
        TestTrue(TEXT("It decodes"),
            FCMLSlotMoveCommandPayload::TryDecode(
                Command, InventoryId, Source, Destination, MoveAmount));
        TestTrue(TEXT("The inventory is the initiator"), InventoryId == PlayerInventory);
        TestEqual(TEXT("The source survives the round trip"), Source, 9);
        TestEqual(TEXT("So does the destination"), Destination, 11);
        TestEqual(TEXT("And the amount"), MoveAmount, static_cast<int64>(3));

        Command.Payload.Add(0);
        TestFalse(TEXT("A payload of the wrong length is refused"),
            FCMLSlotMoveCommandPayload::TryDecode(
                Command, InventoryId, Source, Destination, MoveAmount));

        FCMLSimulationCommand Nameless;
        TestTrue(TEXT("A command with no initiator is still well-formed"),
            FCMLSimulationCommand::TryCreate(
                FCMLSimulationTick(5), 2,
                FCMLSlotMoveCommandPayload::CommandKind(), Nameless));
        TestTrue(TEXT("Its payload encodes"),
            FCMLSlotMoveCommandPayload::TryEncode(0, 1, Nameless.Payload));
        TestFalse(TEXT("But it names no inventory, so it is refused"),
            FCMLSlotMoveCommandPayload::TryDecode(
                Nameless, InventoryId, Source, Destination, MoveAmount));
    }
    return true;
}
#endif

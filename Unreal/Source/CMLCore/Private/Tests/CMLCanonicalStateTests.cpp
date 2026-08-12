#include "Simulation/CMLCanonicalStateSerializer.h"
#include "Simulation/CMLLogicalStateHasher.h"
#include "Simulation/CMLSimulationState.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalStateElementsTest,
    "CML.Core.Simulation.CanonicalStateElements",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalStateElementsTest::RunTest(const FString& Parameters)
{
    // A stable id is two tagged LEB128 fields behind a field count of two:
    // 02 01 <high> 02 <low>.
    {
        TArray<uint8> Bytes;
        FCMLCanonicalStateSerializer::SerializeStableId(FCMLStableId(0, 1), Bytes);
        TestEqual(TEXT("StableId(0,1) encoding"), FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("0201000201")));
    }

    // Quantities are count-prefixed, and each element is a length-prefixed blob
    // so its encoding never depends on its neighbours.
    {
        TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>> Entries;
        Entries.Emplace(FCMLStableId(0, 1), FCMLNonNegativeQuantity(5));
        TArray<uint8> Bytes;
        FCMLCanonicalStateSerializer::SerializeQuantities(Entries, Bytes);
        // 01 count, then a 10-byte element: 02 field count, 01 tag, 05 id-blob
        // length, the five id bytes, 02 tag, 05 value.
        TestEqual(TEXT("Single quantity encoding"), FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("010a02010502010002010205")));
    }

    {
        TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>> Empty;
        TArray<uint8> Bytes;
        FCMLCanonicalStateSerializer::SerializeQuantities(Empty, Bytes);
        TestEqual(TEXT("Empty map is a bare zero count"), Bytes.Num(), 1);
        TestEqual(TEXT("Empty map count byte"), static_cast<int32>(Bytes[0]), 0);
    }

    // Canonical order is part of the hash: Unity held these in a
    // SortedDictionary, so an unsorted Unreal container must be sorted first or
    // two identical worlds would hash differently.
    {
        TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>> Forward;
        Forward.Emplace(FCMLStableId(0, 1), FCMLNonNegativeQuantity(5));
        Forward.Emplace(FCMLStableId(0, 2), FCMLNonNegativeQuantity(7));
        TArray<TPair<FCMLStableId, FCMLNonNegativeQuantity>> Reversed;
        Reversed.Emplace(FCMLStableId(0, 2), FCMLNonNegativeQuantity(7));
        Reversed.Emplace(FCMLStableId(0, 1), FCMLNonNegativeQuantity(5));

        TArray<uint8> Unsorted;
        FCMLCanonicalStateSerializer::SerializeQuantities(Reversed, Unsorted);
        TArray<uint8> Sorted;
        FCMLCanonicalStateSerializer::SortQuantities(Reversed);
        FCMLCanonicalStateSerializer::SerializeQuantities(Reversed, Sorted);
        TArray<uint8> Expected;
        FCMLCanonicalStateSerializer::SerializeQuantities(Forward, Expected);

        TestNotEqual(TEXT("Insertion order would change the encoding"),
            FCMLLogicalStateHasher::ToHex(Unsorted), FCMLLogicalStateHasher::ToHex(Expected));
        TestEqual(TEXT("Sorting restores the canonical encoding"),
            FCMLLogicalStateHasher::ToHex(Sorted), FCMLLogicalStateHasher::ToHex(Expected));
    }

    // An accumulator element carries its key, denominator, remainder and rule
    // revision; the denominator and remainder are 128-bit.
    {
        FCMLAccumulatorKey Key;
        TestTrue(TEXT("Key is created"),
            FCMLAccumulatorKey::TryCreate(TEXT("M"), TEXT("I"), FCMLStableId(0, 1), 0, Key));
        FCMLRemainderAccumulator Accumulator;
        TestTrue(TEXT("Accumulator is created"),
            FCMLRemainderAccumulator::TryCreate(FCMLUnsigned128(7), FCMLUnsigned128(3), 12, Accumulator));

        TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>> Entries;
        Entries.Emplace(Key, Accumulator);
        TArray<uint8> Bytes;
        FCMLCanonicalStateSerializer::SerializeAccumulators(Entries, Bytes);

        TestEqual(TEXT("Count prefix"), static_cast<int32>(Bytes[0]), 1);
        // The hash must react to the rule revision, which is what refuses a
        // replay recorded under older rules.
        FCMLRemainderAccumulator OtherRevision;
        FCMLRemainderAccumulator::TryCreate(FCMLUnsigned128(7), FCMLUnsigned128(3), 11, OtherRevision);
        TArray<TPair<FCMLAccumulatorKey, FCMLRemainderAccumulator>> OtherEntries;
        OtherEntries.Emplace(Key, OtherRevision);
        TArray<uint8> OtherBytes;
        FCMLCanonicalStateSerializer::SerializeAccumulators(OtherEntries, OtherBytes);
        TestNotEqual(TEXT("Rule revision is part of the encoding"),
            FCMLLogicalStateHasher::ToHex(Bytes), FCMLLogicalStateHasher::ToHex(OtherBytes));
    }

    // The root schema is sixteen fields; the constant guards against the port
    // drifting from the Unity layout unnoticed.
    TestEqual(TEXT("Root field count"),
        static_cast<int32>(FCMLCanonicalStateSerializer::RootFieldCount), 16);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalRecordsTest,
    "CML.Core.Simulation.CanonicalRecords",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalRecordsTest::RunTest(const FString& Parameters)
{
    FCMLSimulationCommand Command;
    TestTrue(TEXT("Command is created"),
        FCMLSimulationCommand::TryCreate(FCMLSimulationTick(3), 1, TEXT("Transfer"), Command));
    FCMLSimulationCommand Blank;
    TestFalse(TEXT("A blank kind is refused"),
        FCMLSimulationCommand::TryCreate(FCMLSimulationTick(3), 1, TEXT("  "), Blank));

    // The quantized value is the schema's only signed field, so a negative
    // value must ZigZag rather than become a huge unsigned number.
    {
        FCMLSimulationCommand Negative = Command;
        Negative.QuantizedValue = -1;
        FCMLSimulationCommand Positive = Command;
        Positive.QuantizedValue = 1;
        TArray<uint8> NegativeBytes;
        TArray<uint8> PositiveBytes;
        FCMLCanonicalStateSerializer::SerializeCommand(Negative, NegativeBytes);
        FCMLCanonicalStateSerializer::SerializeCommand(Positive, PositiveBytes);
        TestEqual(TEXT("ZigZag keeps -1 and 1 the same length"),
            NegativeBytes.Num(), PositiveBytes.Num());
        TestNotEqual(TEXT("Sign is preserved"),
            FCMLLogicalStateHasher::ToHex(NegativeBytes),
            FCMLLogicalStateHasher::ToHex(PositiveBytes));
    }

    // The payload is length-prefixed, so an empty payload still occupies a byte
    // and cannot be confused with a missing field.
    {
        TArray<uint8> Bytes;
        FCMLCanonicalStateSerializer::SerializeCommand(Command, Bytes);
        TestEqual(TEXT("Empty payload terminates with a zero length"),
            static_cast<int32>(Bytes.Last()), 0);
    }

    {
        TArray<FCMLSimulationCommand> Commands;
        TArray<uint8> Bytes;
        FCMLCanonicalStateSerializer::SerializeCommands(Commands, Bytes);
        TestEqual(TEXT("No commands is a bare zero count"), Bytes.Num(), 1);
        Commands.Add(Command);
        FCMLCanonicalStateSerializer::SerializeCommands(Commands, Bytes);
        TestEqual(TEXT("One command is counted"), static_cast<int32>(Bytes[0]), 1);
    }

    // A creation key must react to its phase: the phase is what separates two
    // otherwise identical causes inside the same tick.
    {
        FCMLCreationKey KeyA;
        KeyA.Phase = ECMLSimulationPhase::ValidatedTransferCommit;
        KeyA.CauseCode = 7;
        FCMLCreationKey KeyB = KeyA;
        KeyB.Phase = ECMLSimulationPhase::CyclesNeedsAndTimers;
        TArray<uint8> BytesA;
        TArray<uint8> BytesB;
        FCMLCanonicalStateSerializer::SerializeCreationKey(KeyA, BytesA);
        FCMLCanonicalStateSerializer::SerializeCreationKey(KeyB, BytesB);
        TestNotEqual(TEXT("Phase is part of the creation key"),
            FCMLLogicalStateHasher::ToHex(BytesA), FCMLLogicalStateHasher::ToHex(BytesB));
        TestEqual(TEXT("ValidatedTransferCommit is phase nine"),
            static_cast<int32>(ECMLSimulationPhase::ValidatedTransferCommit), 9);
    }

    // A rejection carries the whole refused command plus its reason, so two
    // rejections differing only in reason must encode differently.
    {
        FCMLCommandRejection RejectionA;
        RejectionA.Tick = FCMLSimulationTick(3);
        RejectionA.Command = Command;
        RejectionA.Reason = ECMLCommandRejectionReason::InsufficientQuantity;
        FCMLCommandRejection RejectionB = RejectionA;
        RejectionB.Reason = ECMLCommandRejectionReason::TransferDestinationFull;

        TArray<uint8> BytesA;
        TArray<uint8> BytesB;
        FCMLCanonicalStateSerializer::SerializeCommandRejections({RejectionA}, BytesA);
        FCMLCanonicalStateSerializer::SerializeCommandRejections({RejectionB}, BytesB);
        TestNotEqual(TEXT("Rejection reason is part of the encoding"),
            FCMLLogicalStateHasher::ToHex(BytesA), FCMLLogicalStateHasher::ToHex(BytesB));
        TestEqual(TEXT("TransferDestinationFull is reason eight"),
            static_cast<int32>(ECMLCommandRejectionReason::TransferDestinationFull), 8);
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalInventoryTest,
    "CML.Core.Simulation.CanonicalInventory",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalInventoryTest::RunTest(const FString& Parameters)
{
    auto MakeInventory = [](const uint64 Id, const int32 SlotCount, const int32 FilledSlot)
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = FCMLStableId(0, Id);
        Inventory.ContainerDefinitionId = FCMLStableId(0, 100);
        for (int32 Index = 0; Index < SlotCount; ++Index)
        {
            FCMLInventorySlot Slot;
            if (Index == FilledSlot)
            {
                Slot.bHasStack = true;
                Slot.Stack.ItemId = FCMLStableId(0, 42);
                Slot.Stack.Quantity = FCMLNonNegativeQuantity(3);
            }
            Inventory.Slots.Add(Slot);
        }
        return Inventory;
    };

    // An empty subtree still carries its schema revision, so it cannot be
    // confused with an absent field.
    {
        FCMLInventorySimulationState Empty;
        TArray<uint8> Bytes;
        TestTrue(TEXT("Empty inventory state serialises"),
            FCMLCanonicalStateSerializer::TrySerializeInventories(Empty, Bytes));
        // 02 field count, 01 tag, 01 revision, 02 tag, 01 blob length, 00 count.
        TestEqual(TEXT("Empty INV subtree"), FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("020101020100")));
    }

    // Which slot holds the stack is part of the future, so moving an item
    // between slots must change the encoding even though the contents match.
    {
        FCMLInventorySimulationState First;
        First.Inventories.Add(MakeInventory(1, 4, 0));
        FCMLInventorySimulationState Second;
        Second.Inventories.Add(MakeInventory(1, 4, 2));

        TArray<uint8> FirstBytes;
        TArray<uint8> SecondBytes;
        TestTrue(TEXT("First serialises"),
            FCMLCanonicalStateSerializer::TrySerializeInventories(First, FirstBytes));
        TestTrue(TEXT("Second serialises"),
            FCMLCanonicalStateSerializer::TrySerializeInventories(Second, SecondBytes));
        TestEqual(TEXT("Slot position does not change the length"),
            FirstBytes.Num(), SecondBytes.Num());
        TestNotEqual(TEXT("Slot position changes the encoding"),
            FCMLLogicalStateHasher::ToHex(FirstBytes), FCMLLogicalStateHasher::ToHex(SecondBytes));
    }

    // Inventory order is canonical, not insertion order.
    {
        FCMLInventorySimulationState Forward;
        Forward.Inventories.Add(MakeInventory(1, 1, -1));
        Forward.Inventories.Add(MakeInventory(2, 1, -1));
        FCMLInventorySimulationState Reversed;
        Reversed.Inventories.Add(MakeInventory(2, 1, -1));
        Reversed.Inventories.Add(MakeInventory(1, 1, -1));

        TArray<uint8> ExpectedBytes;
        TArray<uint8> UnsortedBytes;
        FCMLCanonicalStateSerializer::TrySerializeInventories(Forward, ExpectedBytes);
        FCMLCanonicalStateSerializer::TrySerializeInventories(Reversed, UnsortedBytes);
        TestNotEqual(TEXT("Insertion order would change the encoding"),
            FCMLLogicalStateHasher::ToHex(ExpectedBytes),
            FCMLLogicalStateHasher::ToHex(UnsortedBytes));

        Reversed.Sort();
        TArray<uint8> SortedBytes;
        FCMLCanonicalStateSerializer::TrySerializeInventories(Reversed, SortedBytes);
        TestEqual(TEXT("Sorting restores the canonical encoding"),
            FCMLLogicalStateHasher::ToHex(SortedBytes),
            FCMLLogicalStateHasher::ToHex(ExpectedBytes));
    }

    // A duplicate id would make the subtree ambiguous, so it is refused rather
    // than hashed into something no replay could reproduce.
    {
        FCMLInventorySimulationState Duplicated;
        Duplicated.Inventories.Add(MakeInventory(1, 1, -1));
        Duplicated.Inventories.Add(MakeInventory(1, 1, 0));
        TArray<uint8> Bytes;
        TestFalse(TEXT("Duplicate inventory ids are refused"),
            FCMLCanonicalStateSerializer::TrySerializeInventories(Duplicated, Bytes));
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalAirshipTest,
    "CML.Core.Simulation.CanonicalAirship",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalAirshipTest::RunTest(const FString& Parameters)
{
    auto MakeAirship = [](const uint64 Id)
    {
        FCMLAirshipEntityState Airship;
        Airship.Id = FCMLStableId(0, Id);
        Airship.Pose.Position = {1000, 2000, 3000};
        Airship.Pose.YawTurn = 16384;
        Airship.Mode = ECMLAirshipFlightMode::Flying;
        Airship.RepairStatus = ECMLAirshipRepairStatus::Repaired;
        return Airship;
    };

    // An empty subtree still carries its schema revision and four empty
    // collections, so it cannot be confused with an absent field.
    {
        FCMLAirshipSimulationState Empty;
        TArray<uint8> Bytes;
        TestTrue(TEXT("Empty airship state serialises"),
            FCMLCanonicalStateSerializer::TrySerializeAirship(Empty, Bytes));
        // 05 field count, 01 tag / 05 revision, then four {tag, 01 length, 00}.
        TestEqual(TEXT("Empty AIR subtree"), FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("050105020100030100040100050100")));
    }

    // Repair status is in the hash on purpose: a grounded airship and a flyable
    // one are not the same world.
    {
        FCMLAirshipSimulationState Repaired;
        Repaired.Airships.Add(MakeAirship(1));
        FCMLAirshipSimulationState Damaged;
        FCMLAirshipEntityState DamagedShip = MakeAirship(1);
        DamagedShip.RepairStatus = ECMLAirshipRepairStatus::Damaged;
        Damaged.Airships.Add(DamagedShip);

        TArray<uint8> RepairedBytes;
        TArray<uint8> DamagedBytes;
        FCMLCanonicalStateSerializer::TrySerializeAirship(Repaired, RepairedBytes);
        FCMLCanonicalStateSerializer::TrySerializeAirship(Damaged, DamagedBytes);
        TestNotEqual(TEXT("Repair status changes the encoding"),
            FCMLLogicalStateHasher::ToHex(RepairedBytes),
            FCMLLogicalStateHasher::ToHex(DamagedBytes));
    }

    // The integration remainders are what make movement exact, so they must be
    // hashed rather than treated as scratch state.
    {
        FCMLAirshipSimulationState Base;
        Base.Airships.Add(MakeAirship(1));
        FCMLAirshipSimulationState Carried;
        FCMLAirshipEntityState WithRemainder = MakeAirship(1);
        WithRemainder.ForwardIntegrationRemainder = 1;
        Carried.Airships.Add(WithRemainder);

        TArray<uint8> BaseBytes;
        TArray<uint8> CarriedBytes;
        FCMLCanonicalStateSerializer::TrySerializeAirship(Base, BaseBytes);
        FCMLCanonicalStateSerializer::TrySerializeAirship(Carried, CarriedBytes);
        TestNotEqual(TEXT("Integration remainder is part of the state"),
            FCMLLogicalStateHasher::ToHex(BaseBytes),
            FCMLLogicalStateHasher::ToHex(CarriedBytes));
    }

    // Yaw is unsigned: a large turn value must not be read as a negative one.
    {
        FCMLAirshipSimulationState High;
        FCMLAirshipEntityState HighYaw = MakeAirship(1);
        HighYaw.Pose.YawTurn = 65535;
        High.Airships.Add(HighYaw);
        TArray<uint8> Bytes;
        TestTrue(TEXT("High yaw serialises"),
            FCMLCanonicalStateSerializer::TrySerializeAirship(High, Bytes));
    }

    {
        FCMLAirshipSimulationState Duplicated;
        Duplicated.Airships.Add(MakeAirship(1));
        Duplicated.Airships.Add(MakeAirship(1));
        TArray<uint8> Bytes;
        TestFalse(TEXT("Duplicate airship ids are refused"),
            FCMLCanonicalStateSerializer::TrySerializeAirship(Duplicated, Bytes));
    }

    // Canonical order again: insertion order must not reach the hash.
    {
        FCMLAirshipSimulationState Forward;
        Forward.Airships.Add(MakeAirship(1));
        Forward.Airships.Add(MakeAirship(2));
        FCMLAirshipSimulationState Reversed;
        Reversed.Airships.Add(MakeAirship(2));
        Reversed.Airships.Add(MakeAirship(1));

        TArray<uint8> ExpectedBytes;
        TArray<uint8> UnsortedBytes;
        FCMLCanonicalStateSerializer::TrySerializeAirship(Forward, ExpectedBytes);
        FCMLCanonicalStateSerializer::TrySerializeAirship(Reversed, UnsortedBytes);
        TestNotEqual(TEXT("Insertion order would change the encoding"),
            FCMLLogicalStateHasher::ToHex(ExpectedBytes),
            FCMLLogicalStateHasher::ToHex(UnsortedBytes));

        Reversed.Sort();
        TArray<uint8> SortedBytes;
        FCMLCanonicalStateSerializer::TrySerializeAirship(Reversed, SortedBytes);
        TestEqual(TEXT("Sorting restores the canonical encoding"),
            FCMLLogicalStateHasher::ToHex(SortedBytes),
            FCMLLogicalStateHasher::ToHex(ExpectedBytes));
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalMachineTest,
    "CML.Core.Simulation.CanonicalMachine",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalMachineTest::RunTest(const FString& Parameters)
{
    auto MakePort = [](const ECMLMachinePortKind Kind, const int32 SlotCount, const int32 FilledSlot)
    {
        FCMLMachinePort Port;
        Port.Kind = Kind;
        for (int32 Index = 0; Index < SlotCount; ++Index)
        {
            FCMLMachineSlot Slot;
            if (Index == FilledSlot)
            {
                Slot.ItemId = FCMLStableId(0, 42);
                Slot.Quantity = FCMLNonNegativeQuantity(2);
            }
            Port.Slots.Add(Slot);
        }
        return Port;
    };

    auto MakeBuffer = [&MakePort](const uint64 Id)
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, Id);
        Node.Kind = ECMLMachineNodeKind::Buffer;
        Node.Activity = ECMLMachineActivity::Idle;
        // A buffer's input and output are the same port.
        Node.Input = MakePort(ECMLMachinePortKind::Storage, 3, 0);
        Node.bInputOutputAliased = true;
        return Node;
    };

    {
        FCMLMachineSimulationState Empty;
        TArray<uint8> Bytes;
        TestTrue(TEXT("Empty machine state serialises"),
            FCMLCanonicalStateSerializer::TrySerializeMachines(Empty, Bytes));
        // 03 field count, 01 tag / 0a revision, then two {tag, 01 length, 00}.
        TestEqual(TEXT("Empty MCH subtree"), FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("03010a020100030100")));
    }

    // The aliasing rule: a buffer must encode its single port once. Encoding it
    // twice would put a crate's contents into the hash twice.
    {
        FCMLMachineSimulationState Aliased;
        Aliased.Nodes.Add(MakeBuffer(1));

        FCMLMachineSimulationState Separate;
        FCMLMachineNodeState SeparateNode = MakeBuffer(1);
        SeparateNode.bInputOutputAliased = false;
        SeparateNode.Output = SeparateNode.Input;
        Separate.Nodes.Add(SeparateNode);

        TArray<uint8> AliasedBytes;
        TArray<uint8> SeparateBytes;
        FCMLCanonicalStateSerializer::TrySerializeMachines(Aliased, AliasedBytes);
        FCMLCanonicalStateSerializer::TrySerializeMachines(Separate, SeparateBytes);
        TestTrue(TEXT("An aliased port encodes shorter than two identical ports"),
            AliasedBytes.Num() < SeparateBytes.Num());
    }

    // Slot position decides fill and drain order, so it is part of the future.
    {
        FCMLMachineSimulationState First;
        FCMLMachineNodeState FirstNode = MakeBuffer(1);
        First.Nodes.Add(FirstNode);

        FCMLMachineSimulationState Second;
        FCMLMachineNodeState SecondNode = MakeBuffer(1);
        SecondNode.Input = MakePort(ECMLMachinePortKind::Storage, 3, 2);
        Second.Nodes.Add(SecondNode);

        TArray<uint8> FirstBytes;
        TArray<uint8> SecondBytes;
        FCMLCanonicalStateSerializer::TrySerializeMachines(First, FirstBytes);
        FCMLCanonicalStateSerializer::TrySerializeMachines(Second, SecondBytes);
        TestEqual(TEXT("Slot position does not change the length"),
            FirstBytes.Num(), SecondBytes.Num());
        TestNotEqual(TEXT("Slot position changes the encoding"),
            FCMLLogicalStateHasher::ToHex(FirstBytes),
            FCMLLogicalStateHasher::ToHex(SecondBytes));
    }

    // Lane cargo positions are hashed: two lanes with the same items at
    // different positions are different futures, one delivers sooner.
    {
        auto MakeLane = [](const int64 Position)
        {
            FCMLBeltLaneState Lane;
            Lane.Id = FCMLStableId(0, 10);
            Lane.LengthMillimetres = 1000;
            FCMLBeltLaneItem Item;
            Item.ItemId = FCMLStableId(0, 42);
            Item.PositionMillimetres = Position;
            Lane.Items.Add(Item);
            return Lane;
        };

        FCMLMachineSimulationState Near;
        Near.Lanes.Add(MakeLane(100));
        FCMLMachineSimulationState Far;
        Far.Lanes.Add(MakeLane(900));

        TArray<uint8> NearBytes;
        TArray<uint8> FarBytes;
        FCMLCanonicalStateSerializer::TrySerializeMachines(Near, NearBytes);
        FCMLCanonicalStateSerializer::TrySerializeMachines(Far, FarBytes);
        TestNotEqual(TEXT("Cargo position is part of the encoding"),
            FCMLLogicalStateHasher::ToHex(NearBytes), FCMLLogicalStateHasher::ToHex(FarBytes));
    }

    {
        FCMLMachineSimulationState Duplicated;
        Duplicated.Nodes.Add(MakeBuffer(1));
        Duplicated.Nodes.Add(MakeBuffer(1));
        TArray<uint8> Bytes;
        TestFalse(TEXT("Duplicate node ids are refused"),
            FCMLCanonicalStateSerializer::TrySerializeMachines(Duplicated, Bytes));
    }

    {
        FCMLMachineSimulationState Forward;
        Forward.Nodes.Add(MakeBuffer(1));
        Forward.Nodes.Add(MakeBuffer(2));
        FCMLMachineSimulationState Reversed;
        Reversed.Nodes.Add(MakeBuffer(2));
        Reversed.Nodes.Add(MakeBuffer(1));

        TArray<uint8> ExpectedBytes;
        TArray<uint8> UnsortedBytes;
        FCMLCanonicalStateSerializer::TrySerializeMachines(Forward, ExpectedBytes);
        FCMLCanonicalStateSerializer::TrySerializeMachines(Reversed, UnsortedBytes);
        TestNotEqual(TEXT("Insertion order would change the encoding"),
            FCMLLogicalStateHasher::ToHex(ExpectedBytes),
            FCMLLogicalStateHasher::ToHex(UnsortedBytes));

        Reversed.Sort();
        TArray<uint8> SortedBytes;
        FCMLCanonicalStateSerializer::TrySerializeMachines(Reversed, SortedBytes);
        TestEqual(TEXT("Sorting restores the canonical encoding"),
            FCMLLogicalStateHasher::ToHex(SortedBytes),
            FCMLLogicalStateHasher::ToHex(ExpectedBytes));
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalRootTest,
    "CML.Core.Simulation.CanonicalRoot",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalRootTest::RunTest(const FString& Parameters)
{
    FCMLSimulationState State;
    State.ContentRevision = TEXT("rev-1");
    State.Tick = FCMLSimulationTick(7);

    TArray<uint8> Bytes;
    State.SortForCanonicalEncoding();
    TestTrue(TEXT("An empty world serialises"),
        FCMLCanonicalStateSerializer::TrySerializeRoot(State, Bytes));
    TestEqual(TEXT("Root opens with the sixteen-field count"),
        static_cast<int32>(Bytes[0]), 16);

    FString Hex;
    TestTrue(TEXT("An empty world hashes"),
        FCMLLogicalStateHasher::TryComputeHashHex(Bytes, Hex));
    TestEqual(TEXT("Hash is 64 hex characters"), Hex.Len(), 64);

    // Every revision number is in the hash so a replay recorded under older
    // rules is refused rather than silently reinterpreted.
    {
        FCMLSimulationState OlderRules = State;
        OlderRules.RulesRevision = FCMLSimulationState::CurrentRulesRevision - 1;
        TArray<uint8> OlderBytes;
        OlderRules.SortForCanonicalEncoding();
        FCMLCanonicalStateSerializer::TrySerializeRoot(OlderRules, OlderBytes);
        FString OlderHex;
        FCMLLogicalStateHasher::TryComputeHashHex(OlderBytes, OlderHex);
        TestNotEqual(TEXT("Rules revision changes the hash"), Hex, OlderHex);
    }

    // The tick is field 6: two otherwise identical worlds at different ticks
    // are different states.
    {
        FCMLSimulationState Later = State;
        Later.Tick = FCMLSimulationTick(8);
        TArray<uint8> LaterBytes;
        Later.SortForCanonicalEncoding();
        FCMLCanonicalStateSerializer::TrySerializeRoot(Later, LaterBytes);
        FString LaterHex;
        FCMLLogicalStateHasher::TryComputeHashHex(LaterBytes, LaterHex);
        TestNotEqual(TEXT("Tick changes the hash"), Hex, LaterHex);
    }

    // Content revision is ordinal: a different catalog is a different world.
    {
        FCMLSimulationState OtherContent = State;
        OtherContent.ContentRevision = TEXT("rev-2");
        TArray<uint8> OtherBytes;
        OtherContent.SortForCanonicalEncoding();
        FCMLCanonicalStateSerializer::TrySerializeRoot(OtherContent, OtherBytes);
        FString OtherHex;
        FCMLLogicalStateHasher::TryComputeHashHex(OtherBytes, OtherHex);
        TestNotEqual(TEXT("Content revision changes the hash"), Hex, OtherHex);
    }

    // A populated world must hash stably regardless of insertion order, which
    // is the property the whole canonical ordering exists to guarantee.
    {
        FCMLSimulationState Populated = State;
        Populated.Quantities.Emplace(FCMLStableId(0, 2), FCMLNonNegativeQuantity(9));
        Populated.Quantities.Emplace(FCMLStableId(0, 1), FCMLNonNegativeQuantity(5));

        FCMLInventoryState Inventory;
        Inventory.InventoryId = FCMLStableId(0, 3);
        Inventory.Slots.AddDefaulted(2);
        Populated.Inventories.Inventories.Add(Inventory);

        FCMLSimulationState Shuffled = Populated;
        Shuffled.Quantities.Swap(0, 1);

        Populated.SortForCanonicalEncoding();
        Shuffled.SortForCanonicalEncoding();

        TArray<uint8> PopulatedBytes;
        TArray<uint8> ShuffledBytes;
        TestTrue(TEXT("Populated world serialises"),
            FCMLCanonicalStateSerializer::TrySerializeRoot(Populated, PopulatedBytes));
        TestTrue(TEXT("Shuffled world serialises"),
            FCMLCanonicalStateSerializer::TrySerializeRoot(Shuffled, ShuffledBytes));

        FString PopulatedHex;
        FString ShuffledHex;
        FCMLLogicalStateHasher::TryComputeHashHex(PopulatedBytes, PopulatedHex);
        FCMLLogicalStateHasher::TryComputeHashHex(ShuffledBytes, ShuffledHex);
        TestEqual(TEXT("Insertion order does not reach the hash"), PopulatedHex, ShuffledHex);
        TestNotEqual(TEXT("A populated world differs from an empty one"), PopulatedHex, Hex);
    }

    // A malformed subtree must fail the whole root rather than hash into a
    // plausible-looking digest no replay could reproduce.
    {
        FCMLSimulationState Broken = State;
        FCMLInventoryState Duplicate;
        Duplicate.InventoryId = FCMLStableId(0, 4);
        Broken.Inventories.Inventories.Add(Duplicate);
        Broken.Inventories.Inventories.Add(Duplicate);
        TArray<uint8> BrokenBytes;
        Broken.SortForCanonicalEncoding();
        TestFalse(TEXT("A duplicate id fails the whole root"),
            FCMLCanonicalStateSerializer::TrySerializeRoot(Broken, BrokenBytes));
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLUnityGoldenBytesTest,
    "CML.Core.Simulation.UnityGoldenBytes",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

/**
 * Cross-engine byte equality against bytes the Unity build actually recorded.
 *
 * The Unity golden fixture in AirshipSimulationTests is stale: it records AIR
 * schema revision 4 with an eighteen-field airship element, while the current
 * C# serialiser writes revision 5 with twenty-two. Its airship section is
 * therefore unusable here. The player, obstacle and landing-surface sections
 * did not change between those revisions, so each is re-wrapped in a current
 * revision-5 root and compared byte for byte - which exercises the writer,
 * ZigZag, stable-id nesting and length prefixes against Unity's real output.
 */
bool FCMLUnityGoldenBytesTest::RunTest(const FString& Parameters)
{
    {
        FCMLAirshipSimulationState State;
        FCMLAirshipPlayerState Player;
        Player.Id = FCMLStableId(0, 3);
        Player.QuantizedPose.Position = FCMLAirshipVector{-400, 500, -600};
        Player.QuantizedPose.YawTurn = 123;
        State.Players.Add(Player);

        TArray<uint8> Bytes;
        TestTrue(TEXT("Player state serialises"),
            FCMLCanonicalStateSerializer::TrySerializeAirship(State, Bytes));
        TestEqual(TEXT("Player section matches Unity's recorded bytes"),
            FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("050105020100032601240501050201000203020003050201000200040f02010a")
                  + FString(TEXT("03019f0602e80703af09027b0500040100050100"))));
    }

    {
        FCMLAirshipSimulationState State;
        FCMLAirshipObstacle Obstacle;
        Obstacle.Id = FCMLStableId(0, 4);
        Obstacle.Minimum = FCMLAirshipVector{-10, -20, -30};
        Obstacle.Maximum = FCMLAirshipVector{10, 20, 30};
        State.Obstacles.Add(Obstacle);

        TArray<uint8> Bytes;
        TestTrue(TEXT("Obstacle state serialises"),
            FCMLCanonicalStateSerializer::TrySerializeAirship(State, Bytes));
        TestEqual(TEXT("Obstacle section matches Unity's recorded bytes"),
            FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("050105020100030100041c011a03010502010002040207030113022")
                  + FString(TEXT("7033b03070301140228033c050100"))));
    }

    {
        FCMLAirshipSimulationState State;
        FCMLAirshipLandingSurface Surface;
        Surface.Id = FCMLStableId(0, 5);
        Surface.Center = FCMLAirshipVector{700, 800, 900};
        Surface.YawTurn = 16384;
        Surface.HalfWidthMillimetres = 1000;
        Surface.HalfDepthMillimetres = 2000;
        State.LandingSurfaces.Add(Surface);

        TArray<uint8> Bytes;
        TestTrue(TEXT("Surface state serialises"),
            FCMLCanonicalStateSerializer::TrySerializeAirship(State, Bytes));
        TestEqual(TEXT("Surface section matches Unity's recorded bytes"),
            FCMLLogicalStateHasher::ToHex(Bytes),
            FString(TEXT("050105020100030100040100052701250601050201000205020a0301f80a02c0")
                  + FString(TEXT("0c03880e0380800104d00f05a01f06050201000200"))));
    }
    return true;
}
#endif

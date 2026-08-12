#include "Simulation/CMLSimulationCommandQueue.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    FCMLSimulationCommand MakeCommand(const uint64 Tick, const uint64 Sequence)
    {
        FCMLSimulationCommand Command;
        FCMLSimulationCommand::TryCreate(FCMLSimulationTick(Tick), Sequence, TEXT("NoOp"), Command);
        return Command;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLSimulationCommandQueueTest,
    "CML.Core.Simulation.CommandQueue",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLSimulationCommandQueueTest::RunTest(const FString& Parameters)
{
    FCMLSimulationCommandQueue Queue;
    TestEqual(TEXT("A new queue is empty"), Queue.Num(), 0);
    TestEqual(TEXT("An empty tick starts at sequence zero"),
        Queue.GetNextSequenceFor(FCMLSimulationTick(1)), static_cast<uint64>(0));

    TestTrue(TEXT("First command enqueues"), Queue.TryEnqueue(MakeCommand(1, 0)));
    TestTrue(TEXT("Second command enqueues"), Queue.TryEnqueue(MakeCommand(1, 1)));
    TestEqual(TEXT("Both are counted"), Queue.Num(), 2);
    TestEqual(TEXT("Next sequence follows the last"),
        Queue.GetNextSequenceFor(FCMLSimulationTick(1)), static_cast<uint64>(2));

    // The two rules that make the order reproducible rather than merely stable.
    TestFalse(TEXT("A sequence gap is refused"), Queue.TryEnqueueAt(MakeCommand(1, 5), 2));
    TestFalse(TEXT("A duplicate sequence is refused"), Queue.TryEnqueueAt(MakeCommand(1, 1), 1));
    TestEqual(TEXT("A refused command does not reach the queue"), Queue.Num(), 2);

    TestFalse(TEXT("A blank kind is refused"),
        Queue.TryEnqueueAt(FCMLSimulationCommand(), 2));

    // Ticks are kept in order regardless of the order they were filled in, so
    // the canonical list never depends on arrival order.
    TestTrue(TEXT("A later tick enqueues"), Queue.TryEnqueue(MakeCommand(9, 0)));
    TestTrue(TEXT("An earlier tick enqueues afterwards"), Queue.TryEnqueue(MakeCommand(4, 0)));

    TArray<FCMLSimulationCommand> Canonical;
    Queue.ToCanonicalList(Canonical);
    TestEqual(TEXT("Every command is listed"), Canonical.Num(), 4);
    TestEqual(TEXT("Tick 1 first"), Canonical[0].TargetTick.Value, static_cast<uint64>(1));
    TestEqual(TEXT("Sequence order inside a tick"), Canonical[1].Sequence, static_cast<uint64>(1));
    TestEqual(TEXT("Tick 4 before tick 9"), Canonical[2].TargetTick.Value, static_cast<uint64>(4));
    TestEqual(TEXT("Tick 9 last"), Canonical[3].TargetTick.Value, static_cast<uint64>(9));

    TArray<FCMLSimulationCommand> ForTick;
    Queue.GetCommandsFor(FCMLSimulationTick(1), ForTick);
    TestEqual(TEXT("Two commands for tick one"), ForTick.Num(), 2);
    Queue.GetCommandsFor(FCMLSimulationTick(2), ForTick);
    TestEqual(TEXT("No commands for an untouched tick"), ForTick.Num(), 0);

    Queue.RemoveCommandsFor(FCMLSimulationTick(1));
    TestEqual(TEXT("Removing a tick removes its commands"), Queue.Num(), 2);
    TestEqual(TEXT("The removed tick is empty"),
        Queue.GetCommandCountFor(FCMLSimulationTick(1)), 0);
    TestEqual(TEXT("A removed tick restarts at sequence zero"),
        Queue.GetNextSequenceFor(FCMLSimulationTick(1)), static_cast<uint64>(0));

    Queue.RemoveCommandsFor(FCMLSimulationTick(1));
    TestEqual(TEXT("Removing an absent tick is harmless"), Queue.Num(), 2);
    return true;
}
#endif

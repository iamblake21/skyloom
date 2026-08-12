#include "Simulation/CMLCanonicalStateSerializer.h"
#include "Simulation/CMLLogicalStateHasher.h"
#include "Simulation/CMLSimulationEngine.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    /** A system that records that it ran, and can be told to fail its phase. */
    class FRecordingSystem final : public ICMLSimulationPhaseSystem
    {
    public:
        FRecordingSystem(
            const ECMLSimulationPhase InPhase,
            const int32 InOrder,
            const uint64 InStableId,
            const FString& InName,
            TArray<FString>* InTrace,
            const bool bInShouldFail = false)
            : Phase(InPhase)
            , Order(InOrder)
            , StableId(0, InStableId)
            , Name(InName)
            , Trace(InTrace)
            , bShouldFail(bInShouldFail)
        {
        }

        virtual ECMLSimulationPhase GetPhase() const override { return Phase; }
        virtual int32 GetOrder() const override { return Order; }
        virtual FCMLStableId GetStableOrderId() const override { return StableId; }
        virtual FString GetTypeName() const override { return Name; }

        virtual bool Execute(FCMLSimulationPhaseContext& Context, FString& OutFailureCause) override
        {
            Trace->Add(Name);
            if (bShouldFail)
            {
                OutFailureCause = TEXT("deliberate failure");
                return false;
            }
            // Prove the working state is mutable and that a failure discards it.
            Context.WorkingState->Quantities.Emplace(
                FCMLStableId(0, static_cast<uint64>(Trace->Num())),
                FCMLNonNegativeQuantity(1));
            return true;
        }

    private:
        ECMLSimulationPhase Phase;
        int32 Order;
        FCMLStableId StableId;
        FString Name;
        TArray<FString>* Trace;
        bool bShouldFail;
    };
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLSimulationEngineTest,
    "CML.Core.Simulation.Engine",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLSimulationEngineTest::RunTest(const FString& Parameters)
{
    TestEqual(TEXT("A tick runs twelve phases"),
        FCMLSimulationEngine::GetCanonicalPhases().Num(), 12);

    // A clean tick commits and advances the published clock by exactly one.
    {
        FCMLSimulationEngine Engine;
        TArray<FString> Trace;
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CyclesNeedsAndTimers, 0, 1, TEXT("Cycles"), &Trace));

        const FCMLSimulationTickResult Result = Engine.AdvanceOneTick();
        TestTrue(TEXT("The tick commits"), Result.bCommitted);
        TestEqual(TEXT("The executing tick is one"), Result.ExecutingTick.Value, static_cast<uint64>(1));
        TestEqual(TEXT("The published clock advanced"), Engine.GetState().Tick.Value, static_cast<uint64>(1));
        TestEqual(TEXT("All twelve phases completed"), Engine.GetLastPhaseTrace().Num(), 12);
        TestEqual(TEXT("A tick costs exactly one working copy"),
            Engine.GetTickWorkingCloneCount(), static_cast<uint64>(1));
        TestEqual(TEXT("The system ran once"), Trace.Num(), 1);
    }

    // Registration order must never reach the simulation: systems sort by
    // phase, then order, then stable id, then type name.
    {
        FCMLSimulationEngine Engine;
        TArray<FString> Trace;
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::ValidatedTransferCommit, 0, 1, TEXT("Late"), &Trace));
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CommandsAndConfiguration, 5, 1, TEXT("EarlyPhaseHighOrder"), &Trace));
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CommandsAndConfiguration, 1, 1, TEXT("EarlyPhaseLowOrder"), &Trace));
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CommandsAndConfiguration, 1, 0, TEXT("SameOrderLowerId"), &Trace));

        Engine.AdvanceOneTick();
        TestEqual(TEXT("Four systems ran"), Trace.Num(), 4);
        TestEqual(TEXT("Lowest id wins a tie on order"), Trace[0], FString(TEXT("SameOrderLowerId")));
        TestEqual(TEXT("Then the same order, higher id"), Trace[1], FString(TEXT("EarlyPhaseLowOrder")));
        TestEqual(TEXT("Then the higher order in the same phase"), Trace[2], FString(TEXT("EarlyPhaseHighOrder")));
        TestEqual(TEXT("Then the later phase"), Trace[3], FString(TEXT("Late")));
    }

    // The transactional property: a phase failure leaves the published state
    // exactly as it was, tick included.
    {
        FCMLSimulationEngine Engine;
        TArray<FString> Trace;
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CommandsAndConfiguration, 0, 1, TEXT("Before"), &Trace));
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::PowerSupplyAndAllocation, 0, 2, TEXT("Failing"), &Trace, true));
        Engine.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::ObjectivesDiagnosticsAndNotifications, 0, 3, TEXT("After"), &Trace));

        FCMLSimulationState Before = Engine.GetState();
        Before.SortForCanonicalEncoding();
        TArray<uint8> BeforeBytes;
        FCMLCanonicalStateSerializer::TrySerializeRoot(Before, BeforeBytes);
        FString BeforeHash;
        FCMLLogicalStateHasher::TryComputeHashHex(BeforeBytes, BeforeHash);

        const FCMLSimulationTickResult Result = Engine.AdvanceOneTick();
        TestFalse(TEXT("The tick aborts"), Result.bCommitted);
        TestTrue(TEXT("The failing phase is reported"), Result.bHasFailedPhase);
        TestEqual(TEXT("The failing phase is identified"),
            static_cast<int32>(Result.FailedPhase),
            static_cast<int32>(ECMLSimulationPhase::PowerSupplyAndAllocation));
        TestTrue(TEXT("The cause names the system"), Result.FailureCause.Contains(TEXT("Failing")));

        TestEqual(TEXT("The published clock did not advance"),
            Engine.GetState().Tick.Value, static_cast<uint64>(0));
        TestEqual(TEXT("No quantity from the aborted tick survived"),
            Engine.GetState().Quantities.Num(), 0);
        TestEqual(TEXT("A system after the failure never ran"), Trace.Num(), 2);

        FCMLSimulationState After = Engine.GetState();
        After.SortForCanonicalEncoding();
        TArray<uint8> AfterBytes;
        FCMLCanonicalStateSerializer::TrySerializeRoot(After, AfterBytes);
        FString AfterHash;
        FCMLLogicalStateHasher::TryComputeHashHex(AfterBytes, AfterHash);
        TestEqual(TEXT("An aborted tick leaves the state hash untouched"), AfterHash, BeforeHash);
    }

    // Two engines fed the same commands must reach the same hash, which is the
    // property replay depends on.
    {
        auto RunFive = [](FCMLSimulationEngine& Engine)
        {
            for (int32 Index = 0; Index < 5; ++Index)
            {
                Engine.AdvanceOneTick();
            }
            FCMLSimulationState Final = Engine.GetState();
            Final.SortForCanonicalEncoding();
            TArray<uint8> Bytes;
            FCMLCanonicalStateSerializer::TrySerializeRoot(Final, Bytes);
            FString Hash;
            FCMLLogicalStateHasher::TryComputeHashHex(Bytes, Hash);
            return Hash;
        };

        FCMLSimulationEngine First;
        TArray<FString> FirstTrace;
        First.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CyclesNeedsAndTimers, 0, 1, TEXT("A"), &FirstTrace));
        First.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CyclesNeedsAndTimers, 1, 2, TEXT("B"), &FirstTrace));

        // The second engine registers the same systems in the opposite order.
        FCMLSimulationEngine Second;
        TArray<FString> SecondTrace;
        Second.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CyclesNeedsAndTimers, 1, 2, TEXT("B"), &SecondTrace));
        Second.RegisterSystem(MakeShared<FRecordingSystem>(
            ECMLSimulationPhase::CyclesNeedsAndTimers, 0, 1, TEXT("A"), &SecondTrace));

        TestEqual(TEXT("Registration order does not change the outcome"),
            RunFive(First), RunFive(Second));
        TestEqual(TEXT("Five ticks advanced the clock five times"),
            First.GetState().Tick.Value, static_cast<uint64>(5));
    }
    return true;
}
#endif

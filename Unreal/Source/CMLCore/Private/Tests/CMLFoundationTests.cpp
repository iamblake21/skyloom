#include "Foundation/CMLAccumulator.h"
#include "Foundation/CMLUnsigned128.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLUnsigned128ArithmeticTest,
    "CML.Core.Foundation.Unsigned128Arithmetic",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLUnsigned128ArithmeticTest::RunTest(const FString& Parameters)
{
    // The full 64x64 product is the operation the accumulator depends on most,
    // so it is checked at the boundary where a 64-bit result would wrap.
    const FCMLUnsigned128 Product = FCMLUnsigned128::Multiply(MAX_uint64, MAX_uint64);
    TestEqual(TEXT("MAX*MAX high half"), Product.High, MAX_uint64 - 1);
    TestEqual(TEXT("MAX*MAX low half"), Product.Low, static_cast<uint64>(1));

    FCMLUnsigned128 Sum;
    TestTrue(TEXT("Addition inside range succeeds"),
        FCMLUnsigned128::TryAdd(FCMLUnsigned128(0, MAX_uint64), FCMLUnsigned128(0, 1), Sum));
    TestEqual(TEXT("Carry crosses into the high half"), Sum.High, static_cast<uint64>(1));
    TestEqual(TEXT("Low half wrapped to zero"), Sum.Low, static_cast<uint64>(0));

    TestFalse(TEXT("Overflow past 128 bits is rejected"),
        FCMLUnsigned128::TryAdd(FCMLUnsigned128::MaxValue(), FCMLUnsigned128::One(), Sum));

    FCMLUnsigned128 Scaled;
    TestFalse(TEXT("A product past 128 bits is rejected"),
        FCMLUnsigned128::TryMultiply(FCMLUnsigned128::MaxValue(), 2, Scaled));

    // Division has to agree with multiplication exactly, including across the
    // 64-bit boundary where the bitwise path replaces the fast path.
    FCMLUnsigned128 Quotient;
    FCMLUnsigned128 Remainder;
    TestTrue(TEXT("DivMod succeeds"),
        FCMLUnsigned128::TryDivMod(Product, FCMLUnsigned128(MAX_uint64), Quotient, Remainder));
    TestEqual(TEXT("Quotient recovers the other factor"), Quotient.Low, MAX_uint64);
    TestEqual(TEXT("Quotient has no high half"), Quotient.High, static_cast<uint64>(0));
    TestTrue(TEXT("Division is exact"), Remainder.IsZero());

    TestFalse(TEXT("Division by zero is rejected"),
        FCMLUnsigned128::TryDivMod(Product, FCMLUnsigned128::Zero(), Quotient, Remainder));

    TestEqual(TEXT("Decimal rendering of a value above 64 bits"),
        FCMLUnsigned128(1, 0).ToString(), FString(TEXT("18446744073709551616")));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLStableIdAllocatorTest,
    "CML.Core.Foundation.StableIdAllocator",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLStableIdAllocatorTest::RunTest(const FString& Parameters)
{
    FCMLStableIdAllocator Allocator;
    FCMLStableId First;
    FCMLStableId Second;
    TestTrue(TEXT("First allocation succeeds"), Allocator.TryAllocate(First));
    TestTrue(TEXT("Second allocation succeeds"), Allocator.TryAllocate(Second));
    TestEqual(TEXT("Allocation starts at one"), First.Low, static_cast<uint64>(1));
    TestEqual(TEXT("Allocation is monotonic"), Second.Low, static_cast<uint64>(2));

    // The low half rolling over must carry into the high half rather than
    // reissuing an id that is already in use.
    FCMLStableIdAllocator RollOver;
    TestTrue(TEXT("Allocator accepts a rollover boundary"),
        FCMLStableIdAllocator::TryCreate(FCMLStableId(0, MAX_uint64), false, RollOver));
    FCMLStableId Boundary;
    TestTrue(TEXT("Boundary allocation succeeds"), RollOver.TryAllocate(Boundary));
    TestEqual(TEXT("Next id carried into the high half"), RollOver.NextId.High, static_cast<uint64>(1));
    TestEqual(TEXT("Next id low half reset"), RollOver.NextId.Low, static_cast<uint64>(0));

    // MaxValue is itself allocatable; only the call after it fails.
    FCMLStableIdAllocator Last;
    TestTrue(TEXT("Allocator accepts MaxValue as next"),
        FCMLStableIdAllocator::TryCreate(FCMLStableId::MaxValue(), false, Last));
    FCMLStableId Allocated;
    TestTrue(TEXT("MaxValue is allocated"), Last.TryAllocate(Allocated));
    TestTrue(TEXT("Allocator is now exhausted"), Last.bIsExhausted);
    TestFalse(TEXT("Exhausted allocator refuses"), Last.TryAllocate(Allocated));

    FCMLStableIdAllocator Invalid;
    TestFalse(TEXT("Zero next id is refused"),
        FCMLStableIdAllocator::TryCreate(FCMLStableId::None(), false, Invalid));
    TestFalse(TEXT("Exhausted allocator must retain MaxValue"),
        FCMLStableIdAllocator::TryCreate(FCMLStableId::First(), true, Invalid));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLRemainderAccumulatorTest,
    "CML.Core.Foundation.RemainderAccumulator",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLRemainderAccumulatorTest::RunTest(const FString& Parameters)
{
    FCMLRemainderAccumulator Accumulator;
    TestTrue(TEXT("Accumulator is created"),
        FCMLRemainderAccumulator::TryCreate(FCMLUnsigned128(7), FCMLUnsigned128(0), 1, Accumulator));

    FCMLRemainderAccumulator Rejected;
    TestFalse(TEXT("Zero denominator is refused"),
        FCMLRemainderAccumulator::TryCreate(FCMLUnsigned128(0), FCMLUnsigned128(0), 1, Rejected));
    TestFalse(TEXT("Non-Euclidean remainder is refused"),
        FCMLRemainderAccumulator::TryCreate(FCMLUnsigned128(7), FCMLUnsigned128(7), 1, Rejected));

    // The determinism property that matters: advancing by 3 with denominator 7
    // must yield exactly 3 units after 7 steps, with nothing lost to rounding.
    int64 Total = 0;
    FCMLRemainderAccumulator Current = Accumulator;
    for (int32 Step = 0; Step < 7; ++Step)
    {
        FCMLRemainderAdvance Advance;
        TestTrue(TEXT("Advance succeeds"), Current.TryAdvance(FCMLUnsigned128(3), Advance));
        Total += Advance.Produced.Value;
        TestTrue(TEXT("Remainder stays Euclidean"), Advance.NextRemainder < Current.OwnerDenominator);
        TestTrue(TEXT("Next accumulator is valid"),
            FCMLRemainderAccumulator::TryCreate(
                Current.OwnerDenominator, Advance.NextRemainder, Current.RuleRevision, Current));
    }
    TestEqual(TEXT("Seven advances of 3/7 produce exactly 3"), Total, static_cast<int64>(3));
    TestTrue(TEXT("The cycle closes on a zero remainder"), Current.Remainder.IsZero());

    // A scaled advance whose exact intermediate needs more than 128 bits must
    // fail the transaction rather than wrap.
    FCMLRemainderAdvance Overflowed;
    TestFalse(TEXT("A scaled intermediate past 128 bits is rejected"),
        Accumulator.TryAdvanceScaled(MAX_uint64, MAX_uint64, MAX_uint64, Overflowed));

    FCMLRemainderAdvance Scaled;
    TestTrue(TEXT("A representable scaled advance succeeds"),
        Accumulator.TryAdvanceScaled(1000, 20, 3, Scaled));
    TestEqual(TEXT("60000/7 produces 8571"), Scaled.Produced.Value, static_cast<int64>(8571));
    TestEqual(TEXT("60000 mod 7 is 3"), Scaled.NextRemainder.Low, static_cast<uint64>(3));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLAccumulatorKeyTest,
    "CML.Core.Foundation.AccumulatorKey",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLAccumulatorKeyTest::RunTest(const FString& Parameters)
{
    FCMLAccumulatorKey Key;
    TestTrue(TEXT("Key is created"),
        FCMLAccumulatorKey::TryCreate(TEXT("Machine"), TEXT("Iron"), FCMLStableId::First(), 0, Key));

    FCMLAccumulatorKey Invalid;
    TestFalse(TEXT("Blank system kind is refused"),
        FCMLAccumulatorKey::TryCreate(TEXT("   "), TEXT("Iron"), FCMLStableId::First(), 0, Invalid));
    TestFalse(TEXT("Blank resource kind is refused"),
        FCMLAccumulatorKey::TryCreate(TEXT("Machine"), TEXT(""), FCMLStableId::First(), 0, Invalid));
    TestFalse(TEXT("Zero entity id is refused"),
        FCMLAccumulatorKey::TryCreate(TEXT("Machine"), TEXT("Iron"), FCMLStableId::None(), 0, Invalid));

    // Ordering must be ordinal: a canonical hash cannot depend on case folding.
    FCMLAccumulatorKey Upper;
    FCMLAccumulatorKey Lower;
    FCMLAccumulatorKey::TryCreate(TEXT("Machine"), TEXT("Iron"), FCMLStableId::First(), 0, Upper);
    FCMLAccumulatorKey::TryCreate(TEXT("machine"), TEXT("Iron"), FCMLStableId::First(), 0, Lower);
    TestTrue(TEXT("Case is significant"), Upper != Lower);
    TestTrue(TEXT("Uppercase orders before lowercase"), Upper.Compare(Lower) < 0);

    FCMLAccumulatorKey OtherPort;
    FCMLAccumulatorKey::TryCreate(TEXT("Machine"), TEXT("Iron"), FCMLStableId::First(), 1, OtherPort);
    TestTrue(TEXT("Port index separates keys"), Key != OtherPort);
    TestTrue(TEXT("Port index orders keys"), Key.Compare(OtherPort) < 0);
    return true;
}
#endif

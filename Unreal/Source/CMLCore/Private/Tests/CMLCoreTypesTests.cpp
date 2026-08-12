#include "Foundation/CMLCoreTypes.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLStableIdRoundTripTest,
    "CML.Core.Foundation.StableIdRoundTrip",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLStableIdRoundTripTest::RunTest(const FString& Parameters)
{
    const FCMLStableId Source(0x1000000000000000ULL, 0x1AULL);
    FCMLStableId Parsed;
    TestTrue(TEXT("32-digit hexadecimal stable id parses"), FCMLStableId::TryParse(Source.ToString(), Parsed));
    TestEqual(TEXT("High half is preserved"), Parsed.High, Source.High);
    TestEqual(TEXT("Low half is preserved"), Parsed.Low, Source.Low);
    TestFalse(TEXT("Malformed id is rejected"), FCMLStableId::TryParse(TEXT("not-an-id"), Parsed));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLQuantityInvariantTest,
    "CML.Core.Foundation.NonNegativeQuantityInvariant",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLQuantityInvariantTest::RunTest(const FString& Parameters)
{
    FCMLNonNegativeQuantity Result;
    const FCMLNonNegativeQuantity Five(5);
    TestTrue(TEXT("Valid addition succeeds"), Five.TryAdd(FCMLNonNegativeQuantity(7), Result));
    TestEqual(TEXT("Addition result"), Result.Value, static_cast<int64>(12));
    TestFalse(TEXT("Underflow is rejected"), Five.TrySubtract(FCMLNonNegativeQuantity(6), Result));
    TestEqual(TEXT("Rejected subtraction preserves source"), Result.Value, static_cast<int64>(5));
    return true;
}
#endif

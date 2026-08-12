#include "Persistence/CMLSaveEnvelope.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLSaveEnvelopeTest,
    "CML.Core.Persistence.SaveEnvelope",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLSaveEnvelopeTest::RunTest(const FString& Parameters)
{
    FCMLSaveEnvelope Envelope;
    TestEqual(TEXT("A new envelope carries the current version"),
        Envelope.SchemaVersion, FCMLSaveEnvelope::CurrentVersion);
    TestEqual(TEXT("The ported version is one"), FCMLSaveEnvelope::CurrentVersion, 1);
    TestTrue(TEXT("A current envelope is readable"), Envelope.IsReadableByThisBuild());

    // An older save is readable; a newer one is not, because the fields it
    // carries are unknown and loading it would silently drop them.
    Envelope.SchemaVersion = FCMLSaveEnvelope::CurrentVersion - 1;
    TestTrue(TEXT("An older envelope is readable"), Envelope.IsReadableByThisBuild());
    Envelope.SchemaVersion = FCMLSaveEnvelope::CurrentVersion + 1;
    TestFalse(TEXT("A newer envelope is refused"), Envelope.IsReadableByThisBuild());
    return true;
}
#endif

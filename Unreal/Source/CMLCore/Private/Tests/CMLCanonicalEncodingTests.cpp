#include "Foundation/CMLSha256.h"
#include "Simulation/CMLCanonicalWriter.h"
#include "Simulation/CMLLogicalStateHasher.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    FString BytesToHex(const TArray<uint8>& Bytes)
    {
        return FCMLLogicalStateHasher::ToHex(Bytes);
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCanonicalWriterTest,
    "CML.Core.Simulation.CanonicalWriter",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCanonicalWriterTest::RunTest(const FString& Parameters)
{
    // Shortest-form LEB128, checked against the standard vectors. Any deviation
    // here changes every canonical hash.
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteUnsigned(static_cast<uint64>(0));
        Writer.WriteUnsigned(static_cast<uint64>(127));
        Writer.WriteUnsigned(static_cast<uint64>(128));
        Writer.WriteUnsigned(static_cast<uint64>(300));
        TestEqual(TEXT("LEB128 vectors"), BytesToHex(Writer.GetBytes()), FString(TEXT("007f8001ac02")));
    }

    // ZigZag: 0->0, -1->1, 1->2, -2->3.
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteSigned(0);
        Writer.WriteSigned(-1);
        Writer.WriteSigned(1);
        Writer.WriteSigned(-2);
        TestEqual(TEXT("ZigZag vectors"), BytesToHex(Writer.GetBytes()), FString(TEXT("00010203")));
    }

    // A 128-bit value must continue across the 64-bit boundary rather than
    // truncating to its low half.
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteUnsigned(FCMLUnsigned128(1, 0));
        const TArray<uint8>& Bytes = Writer.GetBytes();
        TestEqual(TEXT("2^64 needs ten LEB128 bytes"), Bytes.Num(), 10);
        TestEqual(TEXT("Final byte terminates"), static_cast<int32>(Bytes.Last() & 0x80), 0);
        TestEqual(TEXT("Final byte carries bit 63"), static_cast<int32>(Bytes.Last()), 2);
    }

    {
        FCMLCanonicalWriter Writer;
        TestFalse(TEXT("Tag zero is rejected"), Writer.TryWriteTag(0));
        TestTrue(TEXT("Tag one is accepted"), Writer.TryWriteTag(1));
        Writer.WriteBoolean(true);
        Writer.WriteBoolean(false);
        TestEqual(TEXT("Tag and booleans"), BytesToHex(Writer.GetBytes()), FString(TEXT("010100")));
    }

    // Strings are length-prefixed UTF-8; non-ASCII is counted because Unreal
    // cannot reproduce Unity's NFC normalisation.
    {
        FCMLCanonicalWriter Writer;
        Writer.WriteString(TEXT("Iron"));
        TestEqual(TEXT("Length-prefixed UTF-8"), BytesToHex(Writer.GetBytes()), FString(TEXT("0449726f6e")));
        TestEqual(TEXT("ASCII needs no normalisation review"), Writer.GetNumNonAsciiStrings(), 0);
        Writer.WriteString(TEXT("Fé"));
        TestEqual(TEXT("Non-ASCII is flagged"), Writer.GetNumNonAsciiStrings(), 1);
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLLogicalStateHasherTest,
    "CML.Core.Simulation.LogicalStateHasher",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLLogicalStateHasherTest::RunTest(const FString& Parameters)
{
    // The platform SHA-256 has to be the real thing before the framing on top
    // of it is worth testing: "abc" is the FIPS 180-4 sample vector, and the
    // hasher's own framing is what turns it into the fixture hash.
    {
        TArray<uint8> Empty;
        TArray<uint8> EmptyDigest;
        FCMLSha256::Hash(Empty, EmptyDigest);
        TestEqual(TEXT("SHA-256(\"\")"), FCMLLogicalStateHasher::ToHex(EmptyDigest),
            FString(TEXT("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")));

        const ANSICHAR* Sample = "abc";
        TArray<uint8> Input;
        Input.Append(reinterpret_cast<const uint8*>(Sample), 3);
        TArray<uint8> Hash;
        FCMLSha256::Hash(Input, Hash);
        TestEqual(TEXT("SHA-256(\"abc\")"), FCMLLogicalStateHasher::ToHex(Hash),
            FString(TEXT("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")));

        // A multi-block message exercises the padding path across a boundary.
        const ANSICHAR* Long = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
        TArray<uint8> LongInput;
        LongInput.Append(reinterpret_cast<const uint8*>(Long), 56);
        TArray<uint8> LongHash;
        FCMLSha256::Hash(LongInput, LongHash);
        TestEqual(TEXT("SHA-256 two-block vector"), FCMLLogicalStateHasher::ToHex(LongHash),
            FString(TEXT("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")));
    }

    // Empty canonical state still hashes prefix || 0x00, so the result must not
    // be the hash of nothing.
    {
        FString EmptyStateHex;
        TestTrue(TEXT("Empty state hashes"),
            FCMLLogicalStateHasher::TryComputeHashHex(TArray<uint8>(), EmptyStateHex));
        TestEqual(TEXT("Hash is 64 hex characters"), EmptyStateHex.Len(), 64);
        TestNotEqual(TEXT("Framing is applied, not bypassed"), EmptyStateHex,
            FString(TEXT("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")));
        TestTrue(TEXT("Hex is lowercase"), EmptyStateHex == EmptyStateHex.ToLower());
    }

    // Different canonical bytes must produce different hashes.
    {
        TArray<uint8> StateA = {1, 2, 3};
        TArray<uint8> StateB = {1, 2, 4};
        FString HexA;
        FString HexB;
        TestTrue(TEXT("State A hashes"), FCMLLogicalStateHasher::TryComputeHashHex(StateA, HexA));
        TestTrue(TEXT("State B hashes"), FCMLLogicalStateHasher::TryComputeHashHex(StateB, HexB));
        TestNotEqual(TEXT("A single differing byte changes the hash"), HexA, HexB);
    }
    return true;
}
#endif

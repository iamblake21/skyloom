#include "Simulation/CMLLogicalStateHasher.h"

#include "Foundation/CMLSha256.h"

namespace
{
    const ANSICHAR* const DomainPrefix = "LC-HLOGIC-v1";
    constexpr int32 DomainPrefixLength = 12;
}

const ANSICHAR* FCMLLogicalStateHasher::GetDomainPrefix()
{
    return DomainPrefix;
}

bool FCMLLogicalStateHasher::TryComputeHash(const TArray<uint8>& CanonicalState, TArray<uint8>& OutHash)
{
    TArray<uint8> Input;
    Input.Reserve(DomainPrefixLength + 1 + CanonicalState.Num());
    Input.Append(reinterpret_cast<const uint8*>(DomainPrefix), DomainPrefixLength);
    // The separator keeps the prefix from running into a state that happens to
    // begin with the same bytes.
    Input.Add(0);
    Input.Append(CanonicalState);

    FCMLSha256::Hash(Input, OutHash);
    return true;
}

bool FCMLLogicalStateHasher::TryComputeHashHex(const TArray<uint8>& CanonicalState, FString& OutHex)
{
    TArray<uint8> Hash;
    if (!TryComputeHash(CanonicalState, Hash))
    {
        return false;
    }
    OutHex = ToHex(Hash);
    return true;
}

FString FCMLLogicalStateHasher::ToHex(const TArray<uint8>& Hash)
{
    // Lowercase, matching the C# ComputeHashHex alphabet; FString::ToHex would
    // produce uppercase and silently break fixture comparison.
    static const ANSICHAR* const Alphabet = "0123456789abcdef";
    FString Result;
    Result.Reserve(Hash.Num() * 2);
    for (const uint8 Byte : Hash)
    {
        Result.AppendChar(static_cast<TCHAR>(Alphabet[Byte >> 4]));
        Result.AppendChar(static_cast<TCHAR>(Alphabet[Byte & 0x0F]));
    }
    return Result;
}

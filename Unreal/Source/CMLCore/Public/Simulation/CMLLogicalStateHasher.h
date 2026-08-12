#pragma once

#include "CoreMinimal.h"

/**
 * Domain-separated logical state hash, ported from
 * CML.Simulation.LogicalStateHasher.
 *
 * The framing is part of the contract: the ASCII domain prefix, a single zero
 * byte, then the canonical state bytes, hashed with SHA-256. Changing any part
 * of that framing changes every recorded fixture hash, so it is isolated here
 * from the state serialiser that produces the canonical bytes.
 */
class CMLCORE_API FCMLLogicalStateHasher
{
public:
    /** "LC-HLOGIC-v1" - the domain prefix the Unity fixtures were hashed with. */
    static const ANSICHAR* GetDomainPrefix();

    /** SHA-256 over prefix || 0x00 || CanonicalState. */
    static bool TryComputeHash(const TArray<uint8>& CanonicalState, TArray<uint8>& OutHash);

    /** Lowercase hexadecimal form, matching ComputeHashHex. */
    static bool TryComputeHashHex(const TArray<uint8>& CanonicalState, FString& OutHex);

    static FString ToHex(const TArray<uint8>& Hash);
};

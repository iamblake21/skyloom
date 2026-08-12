#pragma once

#include "CoreMinimal.h"

/**
 * SHA-256 (FIPS 180-4).
 *
 * Unreal's `FPlatformMisc::GetSHA256Signature` has no Windows implementation -
 * it asserts with "No SHA256 Platform implementation" - and the engine's other
 * hash helpers are SHA-1. The canonical state hash has to be byte-identical on
 * every platform and must match hashes already recorded by the Unity build, so
 * the algorithm is implemented here rather than delegated to something whose
 * availability varies by target.
 */
class CMLCORE_API FCMLSha256
{
public:
    static constexpr int32 DigestBytes = 32;

    FCMLSha256();

    void Update(const uint8* Data, int64 ByteCount);
    void Update(const TArray<uint8>& Data) { Update(Data.GetData(), Data.Num()); }

    /** Finalises into a 32-byte digest. The hasher must not be reused after this. */
    void Finalize(TArray<uint8>& OutDigest);

    static void Hash(const TArray<uint8>& Data, TArray<uint8>& OutDigest);

private:
    void ProcessBlock(const uint8* Block);

    uint32 State[8];
    uint8 Buffer[64];
    int32 BufferLength;
    uint64 TotalBits;
};

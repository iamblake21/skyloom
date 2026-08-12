#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLUnsigned128.h"

/**
 * Writer for the LC canonical schema, ported from
 * CML.Simulation.CanonicalEncoding.CanonicalWriter: shortest unsigned LEB128,
 * ZigZag signed values and length-prefixed normalized data.
 *
 * Every byte this produces feeds the logical state hash, so the encoding has to
 * match the Unity original exactly - a single differing byte changes the hash
 * and breaks replay compatibility with the recorded fixtures.
 */
class CMLCORE_API FCMLCanonicalWriter
{
public:
    int64 Num() const { return Buffer.Num(); }

    void WriteFieldCount(uint64 Count) { WriteUnsigned(Count); }

    /** Canonical field tags start at one; tag zero is rejected. */
    bool TryWriteTag(uint64 Tag);

    void WriteUnsigned(uint64 Value);
    void WriteUnsigned(const FCMLUnsigned128& Value);
    void WriteSigned(int64 Value);
    void WriteBoolean(bool bValue);
    void WriteBytes(const TArray<uint8>& Bytes);

    /**
     * UTF-8, length-prefixed. Unity normalised to NFC first; Unreal's Core has
     * no NFC normaliser, so a string outside ASCII cannot be guaranteed to
     * encode identically. Those are counted rather than silently accepted -
     * see NumNonAsciiStrings - because a divergence here is invisible until a
     * canonical hash fails to match.
     */
    void WriteString(const FString& Value);

    /** How many written strings contained a code point above U+007F. */
    int32 GetNumNonAsciiStrings() const { return NumNonAsciiStrings; }

    const TArray<uint8>& GetBytes() const { return Buffer; }

private:
    TArray<uint8> Buffer;
    int32 NumNonAsciiStrings = 0;
};

#include "Simulation/CMLCanonicalWriter.h"

bool FCMLCanonicalWriter::TryWriteTag(const uint64 Tag)
{
    if (Tag == 0)
    {
        return false;
    }
    WriteUnsigned(Tag);
    return true;
}

void FCMLCanonicalWriter::WriteUnsigned(uint64 Value)
{
    // Shortest-form LEB128: emit seven bits at a time, continuation bit set
    // while anything remains. Zero still costs one byte.
    do
    {
        uint8 Next = static_cast<uint8>(Value & 0x7F);
        Value >>= 7;
        if (Value != 0)
        {
            Next |= 0x80;
        }
        Buffer.Add(Next);
    }
    while (Value != 0);
}

void FCMLCanonicalWriter::WriteUnsigned(const FCMLUnsigned128& Value)
{
    uint64 CurrentHigh = Value.High;
    uint64 CurrentLow = Value.Low;
    do
    {
        uint8 Next = static_cast<uint8>(CurrentLow & 0x7F);
        // The low half has to take the seven bits leaving the high half, so the
        // pre-shift high is what feeds it.
        const uint64 PreviousHigh = CurrentHigh;
        CurrentHigh >>= 7;
        CurrentLow = (CurrentLow >> 7) | (PreviousHigh << 57);
        if (CurrentHigh != 0 || CurrentLow != 0)
        {
            Next |= 0x80;
        }
        Buffer.Add(Next);
    }
    while (CurrentHigh != 0 || CurrentLow != 0);
}

void FCMLCanonicalWriter::WriteSigned(const int64 Value)
{
    // ZigZag: small magnitudes stay short whichever sign they carry.
    const uint64 ZigZag = static_cast<uint64>((Value << 1) ^ (Value >> 63));
    WriteUnsigned(ZigZag);
}

void FCMLCanonicalWriter::WriteBoolean(const bool bValue)
{
    Buffer.Add(bValue ? 1 : 0);
}

void FCMLCanonicalWriter::WriteBytes(const TArray<uint8>& Bytes)
{
    WriteUnsigned(static_cast<uint64>(Bytes.Num()));
    Buffer.Append(Bytes);
}

void FCMLCanonicalWriter::WriteString(const FString& Value)
{
    for (const TCHAR Character : Value)
    {
        if (static_cast<uint32>(Character) > 0x7F)
        {
            ++NumNonAsciiStrings;
            break;
        }
    }

    FTCHARToUTF8 Converter(*Value);
    TArray<uint8> Bytes;
    Bytes.Append(reinterpret_cast<const uint8*>(Converter.Get()), Converter.Length());
    WriteBytes(Bytes);
}

#include "Foundation/CMLUnsigned128.h"

namespace
{
    /** Shift left by one, reporting the bit that leaves the top. */
    void ShiftLeftOne(FCMLUnsigned128& Value, uint64& OutCarryOut)
    {
        OutCarryOut = Value.High >> 63;
        Value.High = (Value.High << 1) | (Value.Low >> 63);
        Value.Low <<= 1;
    }

    void SetBit0(FCMLUnsigned128& Value)
    {
        Value.Low |= 1ULL;
    }

    bool AddNoCheck(const FCMLUnsigned128& A, const FCMLUnsigned128& B, FCMLUnsigned128& OutResult)
    {
        const uint64 Low = A.Low + B.Low;
        const uint64 Carry = Low < A.Low ? 1ULL : 0ULL;
        const uint64 High = A.High + B.High + Carry;
        // Overflow when the high sum wrapped: either term exceeded, or the carry did.
        const bool bOverflow = (High < A.High) || (High == A.High && (B.High != 0 || Carry != 0));
        OutResult = FCMLUnsigned128(High, Low);
        return !bOverflow;
    }
}

FString FCMLUnsigned128::ToString() const
{
    if (IsZero())
    {
        return TEXT("0");
    }

    // Repeated division by 10^19, the largest power of ten inside 64 bits, so
    // the decimal expansion needs at most three divisions.
    static constexpr uint64 Chunk = 10000000000000000000ULL;
    FCMLUnsigned128 Value = *this;
    TArray<uint64, TInlineAllocator<3>> Chunks;
    while (!Value.IsZero())
    {
        FCMLUnsigned128 Quotient;
        FCMLUnsigned128 Remainder;
        TryDivMod(Value, FCMLUnsigned128(Chunk), Quotient, Remainder);
        Chunks.Add(Remainder.Low);
        Value = Quotient;
    }

    FString Result = FString::Printf(TEXT("%llu"), Chunks.Last());
    for (int32 Index = Chunks.Num() - 2; Index >= 0; --Index)
    {
        Result += FString::Printf(TEXT("%019llu"), Chunks[Index]);
    }
    return Result;
}

bool FCMLUnsigned128::TryToUInt64(uint64& OutValue) const
{
    OutValue = Low;
    return High == 0;
}

int32 FCMLUnsigned128::Compare(const FCMLUnsigned128& Other) const
{
    if (High != Other.High)
    {
        return High < Other.High ? -1 : 1;
    }
    if (Low != Other.Low)
    {
        return Low < Other.Low ? -1 : 1;
    }
    return 0;
}

bool FCMLUnsigned128::TryAdd(const FCMLUnsigned128& A, const FCMLUnsigned128& B, FCMLUnsigned128& OutResult)
{
    return AddNoCheck(A, B, OutResult);
}

FCMLUnsigned128 FCMLUnsigned128::Multiply(const uint64 A, const uint64 B)
{
    // 32-bit limb decomposition: portable and exact, with no reliance on a
    // compiler-specific 128-bit integer type.
    const uint64 ALow = A & 0xFFFFFFFFULL;
    const uint64 AHigh = A >> 32;
    const uint64 BLow = B & 0xFFFFFFFFULL;
    const uint64 BHigh = B >> 32;

    const uint64 LowLow = ALow * BLow;
    const uint64 LowHigh = ALow * BHigh;
    const uint64 HighLow = AHigh * BLow;
    const uint64 HighHigh = AHigh * BHigh;

    const uint64 Middle = (LowLow >> 32) + (LowHigh & 0xFFFFFFFFULL) + (HighLow & 0xFFFFFFFFULL);
    const uint64 ResultLow = (LowLow & 0xFFFFFFFFULL) | (Middle << 32);
    const uint64 ResultHigh = HighHigh + (LowHigh >> 32) + (HighLow >> 32) + (Middle >> 32);
    return FCMLUnsigned128(ResultHigh, ResultLow);
}

bool FCMLUnsigned128::TryMultiply(const FCMLUnsigned128& A, const uint64 B, FCMLUnsigned128& OutResult)
{
    const FCMLUnsigned128 LowProduct = Multiply(A.Low, B);
    const FCMLUnsigned128 HighProduct = Multiply(A.High, B);
    // Anything landing above bit 127 from the high half cannot be represented.
    if (HighProduct.High != 0)
    {
        OutResult = Zero();
        return false;
    }

    FCMLUnsigned128 Shifted(HighProduct.Low, 0);
    return AddNoCheck(LowProduct, Shifted, OutResult);
}

bool FCMLUnsigned128::TryDivMod(
    const FCMLUnsigned128& Numerator,
    const FCMLUnsigned128& Denominator,
    FCMLUnsigned128& OutQuotient,
    FCMLUnsigned128& OutRemainder)
{
    if (Denominator.IsZero())
    {
        OutQuotient = Zero();
        OutRemainder = Zero();
        return false;
    }

    // Both halves fitting in 64 bits is by far the common case on the
    // simulation path, so it avoids the bitwise loop entirely.
    if (Numerator.High == 0 && Denominator.High == 0)
    {
        OutQuotient = FCMLUnsigned128(Numerator.Low / Denominator.Low);
        OutRemainder = FCMLUnsigned128(Numerator.Low % Denominator.Low);
        return true;
    }

    // Restoring binary long division: 128 fixed iterations, no data-dependent
    // branching on magnitude, so the cost is identical for every input.
    FCMLUnsigned128 Quotient = Zero();
    FCMLUnsigned128 Remainder = Zero();
    for (int32 Bit = 127; Bit >= 0; --Bit)
    {
        uint64 Discarded = 0;
        ShiftLeftOne(Remainder, Discarded);
        const uint64 NumeratorBit = Bit >= 64
            ? (Numerator.High >> (Bit - 64)) & 1ULL
            : (Numerator.Low >> Bit) & 1ULL;
        if (NumeratorBit != 0)
        {
            SetBit0(Remainder);
        }

        ShiftLeftOne(Quotient, Discarded);
        if (Remainder.Compare(Denominator) >= 0)
        {
            // Remainder -= Denominator, borrowing across the halves.
            const uint64 Low = Remainder.Low - Denominator.Low;
            const uint64 Borrow = Remainder.Low < Denominator.Low ? 1ULL : 0ULL;
            Remainder = FCMLUnsigned128(Remainder.High - Denominator.High - Borrow, Low);
            SetBit0(Quotient);
        }
    }

    OutQuotient = Quotient;
    OutRemainder = Remainder;
    return true;
}

#pragma once

#include "CoreMinimal.h"
#include "CMLUnsigned128.generated.h"

/**
 * Engine-independent unsigned 128-bit value, ported from CML.Foundation.Unsigned128.
 *
 * The Unity original leaned on System.Numerics.BigInteger for its exact
 * intermediates. Doing the same here would put heap allocation and arbitrary
 * precision on the 20 Hz simulation path, so every operation is implemented as
 * fixed-width 128-bit arithmetic with an explicit overflow result instead.
 * Nothing here is allowed to wrap silently: an operation that cannot be
 * represented reports failure and the caller fails the transaction, which is
 * the same contract the C# version expressed with OverflowException.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLUnsigned128
{
    GENERATED_BODY()

    UPROPERTY()
    uint64 High = 0;

    UPROPERTY()
    uint64 Low = 0;

    constexpr FCMLUnsigned128() = default;
    explicit constexpr FCMLUnsigned128(const uint64 InLow) : High(0), Low(InLow) {}
    constexpr FCMLUnsigned128(const uint64 InHigh, const uint64 InLow) : High(InHigh), Low(InLow) {}

    static constexpr FCMLUnsigned128 Zero() { return FCMLUnsigned128(0, 0); }
    static constexpr FCMLUnsigned128 One() { return FCMLUnsigned128(0, 1); }
    static constexpr FCMLUnsigned128 MaxValue() { return FCMLUnsigned128(MAX_uint64, MAX_uint64); }

    bool IsZero() const { return High == 0 && Low == 0; }

    /** Decimal representation, matching the C# ToString. */
    FString ToString() const;

    /** True when the value fits in 64 bits; OutValue is always the low half. */
    bool TryToUInt64(uint64& OutValue) const;

    int32 Compare(const FCMLUnsigned128& Other) const;

    /** Sum, rejected on 128-bit overflow. */
    static bool TryAdd(const FCMLUnsigned128& A, const FCMLUnsigned128& B, FCMLUnsigned128& OutResult);

    /** Exact 64x64 product; never overflows 128 bits. */
    static FCMLUnsigned128 Multiply(uint64 A, uint64 B);

    /** Product of a 128-bit and a 64-bit value, rejected on 128-bit overflow. */
    static bool TryMultiply(const FCMLUnsigned128& A, uint64 B, FCMLUnsigned128& OutResult);

    /**
     * Euclidean division. Returns false only for a zero divisor, which callers
     * must already have excluded through validated content data.
     */
    static bool TryDivMod(
        const FCMLUnsigned128& Numerator,
        const FCMLUnsigned128& Denominator,
        FCMLUnsigned128& OutQuotient,
        FCMLUnsigned128& OutRemainder);

    friend bool operator==(const FCMLUnsigned128& A, const FCMLUnsigned128& B)
    {
        return A.High == B.High && A.Low == B.Low;
    }
    friend bool operator!=(const FCMLUnsigned128& A, const FCMLUnsigned128& B) { return !(A == B); }
    friend bool operator<(const FCMLUnsigned128& A, const FCMLUnsigned128& B) { return A.Compare(B) < 0; }
    friend bool operator>(const FCMLUnsigned128& A, const FCMLUnsigned128& B) { return A.Compare(B) > 0; }
    friend bool operator<=(const FCMLUnsigned128& A, const FCMLUnsigned128& B) { return A.Compare(B) <= 0; }
    friend bool operator>=(const FCMLUnsigned128& A, const FCMLUnsigned128& B) { return A.Compare(B) >= 0; }
};

FORCEINLINE uint32 GetTypeHash(const FCMLUnsigned128& Value)
{
    return HashCombineFast(::GetTypeHash(Value.High), ::GetTypeHash(Value.Low));
}

#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "Foundation/CMLUnsigned128.h"
#include "CMLAccumulator.generated.h"

/**
 * Monotonic 128-bit ID allocator, ported from CML.Foundation.StableIdAllocator.
 * Zero is reserved and IDs are never reused. The exhausted flag is explicit so
 * MaxValue can itself be allocated without wrapping back to zero.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLStableIdAllocator
{
    GENERATED_BODY()

    UPROPERTY()
    FCMLStableId NextId = FCMLStableId::First();

    UPROPERTY()
    bool bIsExhausted = false;

    FCMLStableIdAllocator() = default;

    /** Rejects the states the C# constructor refused: zero next id, or an
     *  exhausted allocator whose next id is not MaxValue. */
    static bool TryCreate(const FCMLStableId& NextId, bool bIsExhausted, FCMLStableIdAllocator& OutAllocator);

    bool TryAllocate(FCMLStableId& OutAllocated);
};

/**
 * Canonical owner of a fractional remainder, ported from
 * CML.Foundation.AccumulatorKey. One entity may own many independent
 * accumulators across systems, resources and ports/cycles.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLAccumulatorKey
{
    GENERATED_BODY()

    UPROPERTY()
    FString SystemKind;

    UPROPERTY()
    FString ResourceKind;

    UPROPERTY()
    FCMLStableId EntityId;

    UPROPERTY()
    uint32 PortOrCycleIndex = 0;

    FCMLAccumulatorKey() = default;

    /** Rejects empty kinds and a zero entity id, exactly as the C# constructor did. */
    static bool TryCreate(
        const FString& SystemKind,
        const FString& ResourceKind,
        const FCMLStableId& EntityId,
        uint32 PortOrCycleIndex,
        FCMLAccumulatorKey& OutKey);

    /** Ordinal ordering, so a canonical hash never depends on culture. */
    int32 Compare(const FCMLAccumulatorKey& Other) const;

    friend bool operator==(const FCMLAccumulatorKey& A, const FCMLAccumulatorKey& B)
    {
        return A.Compare(B) == 0;
    }
    friend bool operator!=(const FCMLAccumulatorKey& A, const FCMLAccumulatorKey& B) { return !(A == B); }
};

CMLCORE_API uint32 GetTypeHash(const FCMLAccumulatorKey& Key);

/** Result of advancing an accumulator: the next state and what it produced. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLRemainderAdvance
{
    GENERATED_BODY()

    UPROPERTY()
    FCMLUnsigned128 NextRemainder;

    UPROPERTY(BlueprintReadOnly, Category="CML|Simulation")
    FCMLNonNegativeQuantity Produced;
};

/**
 * Exact fixed-denominator accumulator, ported from
 * CML.Foundation.RemainderAccumulator. The denominator is validated content
 * data and never changes at runtime; blocked work leaves the remainder intact.
 *
 * Every Advance overload reports failure rather than clamping or wrapping: an
 * intermediate that does not fit must fail the transaction, which is what the
 * C# original signalled with OverflowException.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLRemainderAccumulator
{
    GENERATED_BODY()

    UPROPERTY()
    FCMLUnsigned128 OwnerDenominator = FCMLUnsigned128::One();

    UPROPERTY()
    FCMLUnsigned128 Remainder;

    UPROPERTY()
    uint32 RuleRevision = 0;

    FCMLRemainderAccumulator() = default;

    /** Rejects a zero denominator and a remainder that is not Euclidean. */
    static bool TryCreate(
        const FCMLUnsigned128& OwnerDenominator,
        const FCMLUnsigned128& Remainder,
        uint32 RuleRevision,
        FCMLRemainderAccumulator& OutAccumulator);

    bool TryAdvance(const FCMLUnsigned128& Numerator, FCMLRemainderAdvance& OutAdvance) const;

    /**
     * Advances by the product of three validated integer factors, used when a
     * catalog rate also carries a fixed nominal-service scale. The exact
     * intermediate is accepted through 128 bits and rejected above it.
     */
    bool TryAdvanceScaled(uint64 Numerator, uint64 Service, uint64 Scale, FCMLRemainderAdvance& OutAdvance) const;

    friend bool operator==(const FCMLRemainderAccumulator& A, const FCMLRemainderAccumulator& B)
    {
        return A.OwnerDenominator == B.OwnerDenominator
            && A.Remainder == B.Remainder
            && A.RuleRevision == B.RuleRevision;
    }
    friend bool operator!=(const FCMLRemainderAccumulator& A, const FCMLRemainderAccumulator& B)
    {
        return !(A == B);
    }
};

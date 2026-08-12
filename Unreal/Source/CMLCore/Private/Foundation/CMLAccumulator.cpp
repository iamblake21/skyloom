#include "Foundation/CMLAccumulator.h"

bool FCMLStableIdAllocator::TryCreate(
    const FCMLStableId& NextId,
    const bool bIsExhausted,
    FCMLStableIdAllocator& OutAllocator)
{
    if (!bIsExhausted && NextId.IsNone())
    {
        return false;
    }
    if (bIsExhausted && NextId != FCMLStableId::MaxValue())
    {
        return false;
    }

    OutAllocator.NextId = NextId;
    OutAllocator.bIsExhausted = bIsExhausted;
    return true;
}

bool FCMLStableIdAllocator::TryAllocate(FCMLStableId& OutAllocated)
{
    if (bIsExhausted)
    {
        OutAllocated = FCMLStableId::None();
        return false;
    }

    OutAllocated = NextId;
    if (NextId == FCMLStableId::MaxValue())
    {
        // MaxValue is a legal allocation; the allocator retires instead of
        // wrapping, so the next call fails rather than reissuing an id.
        bIsExhausted = true;
        return true;
    }

    NextId = NextId.Low == MAX_uint64
        ? FCMLStableId(NextId.High + 1, 0)
        : FCMLStableId(NextId.High, NextId.Low + 1);
    return true;
}

bool FCMLAccumulatorKey::TryCreate(
    const FString& SystemKind,
    const FString& ResourceKind,
    const FCMLStableId& EntityId,
    const uint32 PortOrCycleIndex,
    FCMLAccumulatorKey& OutKey)
{
    if (SystemKind.TrimStartAndEnd().IsEmpty() || ResourceKind.TrimStartAndEnd().IsEmpty())
    {
        return false;
    }
    if (EntityId.IsNone())
    {
        return false;
    }

    OutKey.SystemKind = SystemKind;
    OutKey.ResourceKind = ResourceKind;
    OutKey.EntityId = EntityId;
    OutKey.PortOrCycleIndex = PortOrCycleIndex;
    return true;
}

int32 FCMLAccumulatorKey::Compare(const FCMLAccumulatorKey& Other) const
{
    // Case-sensitive ordinal comparison: the canonical hash must not depend on
    // culture or case folding.
    int32 Comparison = FCString::Strcmp(*SystemKind, *Other.SystemKind);
    if (Comparison != 0)
    {
        return Comparison;
    }
    Comparison = FCString::Strcmp(*ResourceKind, *Other.ResourceKind);
    if (Comparison != 0)
    {
        return Comparison;
    }
    if (EntityId != Other.EntityId)
    {
        return EntityId < Other.EntityId ? -1 : 1;
    }
    if (PortOrCycleIndex != Other.PortOrCycleIndex)
    {
        return PortOrCycleIndex < Other.PortOrCycleIndex ? -1 : 1;
    }
    return 0;
}

uint32 GetTypeHash(const FCMLAccumulatorKey& Key)
{
    uint32 Hash = GetTypeHash(Key.SystemKind);
    Hash = HashCombineFast(Hash, GetTypeHash(Key.ResourceKind));
    Hash = HashCombineFast(Hash, GetTypeHash(Key.EntityId));
    return HashCombineFast(Hash, ::GetTypeHash(Key.PortOrCycleIndex));
}

bool FCMLRemainderAccumulator::TryCreate(
    const FCMLUnsigned128& OwnerDenominator,
    const FCMLUnsigned128& Remainder,
    const uint32 RuleRevision,
    FCMLRemainderAccumulator& OutAccumulator)
{
    if (OwnerDenominator.IsZero())
    {
        return false;
    }
    if (Remainder >= OwnerDenominator)
    {
        return false;
    }

    OutAccumulator.OwnerDenominator = OwnerDenominator;
    OutAccumulator.Remainder = Remainder;
    OutAccumulator.RuleRevision = RuleRevision;
    return true;
}

bool FCMLRemainderAccumulator::TryAdvance(
    const FCMLUnsigned128& Numerator,
    FCMLRemainderAdvance& OutAdvance) const
{
    FCMLUnsigned128 StagedTotal;
    if (!FCMLUnsigned128::TryAdd(Numerator, Remainder, StagedTotal))
    {
        return false;
    }

    FCMLUnsigned128 Produced;
    FCMLUnsigned128 NextRemainder;
    if (!FCMLUnsigned128::TryDivMod(StagedTotal, OwnerDenominator, Produced, NextRemainder))
    {
        return false;
    }

    // The produced amount becomes a NonNegativeQuantity, so anything past
    // int64 is out of the supported quantity range.
    uint64 ProducedValue = 0;
    if (!Produced.TryToUInt64(ProducedValue) || ProducedValue > static_cast<uint64>(MAX_int64))
    {
        return false;
    }

    OutAdvance.NextRemainder = NextRemainder;
    OutAdvance.Produced = FCMLNonNegativeQuantity(static_cast<int64>(ProducedValue));
    return true;
}

bool FCMLRemainderAccumulator::TryAdvanceScaled(
    const uint64 Numerator,
    const uint64 Service,
    const uint64 Scale,
    FCMLRemainderAdvance& OutAdvance) const
{
    // numerator * service is exact in 128 bits; multiplying by the scale is
    // where a validated catalog rate can still exceed the representable range.
    const FCMLUnsigned128 Partial = FCMLUnsigned128::Multiply(Numerator, Service);
    FCMLUnsigned128 Scaled;
    if (!FCMLUnsigned128::TryMultiply(Partial, Scale, Scaled))
    {
        return false;
    }

    return TryAdvance(Scaled, OutAdvance);
}

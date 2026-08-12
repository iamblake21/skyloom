#pragma once

#include "CoreMinimal.h"
#include "CMLCoreTypes.generated.h"

/** Engine-independent 128-bit identifier. Zero is reserved for None. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLStableId
{
    GENERATED_BODY()

    UPROPERTY()
    uint64 High = 0;

    UPROPERTY()
    uint64 Low = 0;

    constexpr FCMLStableId() = default;
    constexpr FCMLStableId(const uint64 InHigh, const uint64 InLow)
        : High(InHigh), Low(InLow)
    {
    }

    static constexpr FCMLStableId None() { return FCMLStableId(0, 0); }
    static constexpr FCMLStableId First() { return FCMLStableId(0, 1); }
    static constexpr FCMLStableId MaxValue() { return FCMLStableId(MAX_uint64, MAX_uint64); }

    bool IsNone() const { return High == 0 && Low == 0; }
    FString ToString() const;
    static bool TryParse(const FString& Value, FCMLStableId& OutId);

    friend bool operator==(const FCMLStableId& A, const FCMLStableId& B)
    {
        return A.High == B.High && A.Low == B.Low;
    }

    friend bool operator!=(const FCMLStableId& A, const FCMLStableId& B) { return !(A == B); }
    friend bool operator<(const FCMLStableId& A, const FCMLStableId& B)
    {
        return A.High != B.High ? A.High < B.High : A.Low < B.Low;
    }
};

FORCEINLINE uint32 GetTypeHash(const FCMLStableId& Value)
{
    return HashCombineFast(::GetTypeHash(Value.High), ::GetTypeHash(Value.Low));
}

/**
 * The keys every catalog definition carries beside its id.
 *
 * Unity validates these identically for all six definition types and against
 * one shared key namespace, so they live in a single struct rather than being
 * repeated six times and drifting apart.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLDefinitionIdentity
{
    GENERATED_BODY()

    /** Content key: lowercase ASCII letters, digits, '.', '_' or '-'. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FString Key;

    /** Localisation key, held to the same canonical form. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FString NameKey;

    /** The rule both keys must satisfy, ported from CatalogValidator. */
    static bool IsCanonicalKey(const FString& Value);
};

/** Position on the authoritative 20 Hz simulation clock. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLSimulationTick
{
    GENERATED_BODY()

    static constexpr int32 TicksPerSecond = 20;
    static constexpr int32 MillisecondsPerTick = 50;

    UPROPERTY()
    uint64 Value = 0;

    constexpr FCMLSimulationTick() = default;
    explicit constexpr FCMLSimulationTick(const uint64 InValue) : Value(InValue) {}

    bool TryNext(FCMLSimulationTick& OutNext) const;
};

/** Checked quantity which can never represent a negative value. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLNonNegativeQuantity
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Simulation")
    int64 Value = 0;

    FCMLNonNegativeQuantity() = default;
    explicit FCMLNonNegativeQuantity(const int64 InValue) : Value(FMath::Max<int64>(0, InValue)) {}

    bool IsZero() const { return Value == 0; }
    bool TryAdd(const FCMLNonNegativeQuantity& Amount, FCMLNonNegativeQuantity& OutResult) const;
    bool TrySubtract(const FCMLNonNegativeQuantity& Amount, FCMLNonNegativeQuantity& OutResult) const;
};

/** Fixed-step accumulator shared by runtime presentation and deterministic tests. */
class CMLCORE_API FCMLFixedStepClock
{
public:
    static constexpr double StepSeconds = 1.0 / FCMLSimulationTick::TicksPerSecond;

    int32 Accumulate(double DeltaSeconds, int32 MaxSteps = 8);
    void Reset();

    double GetRemainderSeconds() const { return AccumulatorSeconds; }

private:
    double AccumulatorSeconds = 0.0;
};

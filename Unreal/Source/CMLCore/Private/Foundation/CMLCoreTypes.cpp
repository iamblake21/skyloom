#include "Foundation/CMLCoreTypes.h"

#include "Misc/Char.h"

FString FCMLStableId::ToString() const
{
    return FString::Printf(TEXT("%016llx%016llx"), High, Low);
}

bool FCMLDefinitionIdentity::IsCanonicalKey(const FString& Value)
{
    if (Value.IsEmpty())
    {
        return false;
    }
    for (const TCHAR Character : Value)
    {
        // Spelled out rather than using FChar::IsAlnum: the rule is ASCII and
        // lowercase only, and a locale-aware test would accept 'A' or 'à'.
        const bool bAllowed =
            (Character >= TEXT('a') && Character <= TEXT('z'))
            || (Character >= TEXT('0') && Character <= TEXT('9'))
            || Character == TEXT('.')
            || Character == TEXT('_')
            || Character == TEXT('-');
        if (!bAllowed)
        {
            return false;
        }
    }
    return true;
}

bool FCMLStableId::TryParse(const FString& Value, FCMLStableId& OutId)
{
    OutId = None();
    if (Value.Len() != 32)
    {
        return false;
    }

    for (const TCHAR Character : Value)
    {
        if (!FChar::IsHexDigit(Character))
        {
            return false;
        }
    }

    const FString HighText = Value.Left(16);
    const FString LowText = Value.Right(16);
    TCHAR* HighEnd = nullptr;
    TCHAR* LowEnd = nullptr;
    const uint64 ParsedHigh = FCString::Strtoui64(*HighText, &HighEnd, 16);
    const uint64 ParsedLow = FCString::Strtoui64(*LowText, &LowEnd, 16);
    if (HighEnd == *HighText || LowEnd == *LowText || *HighEnd != 0 || *LowEnd != 0)
    {
        return false;
    }

    OutId = FCMLStableId(ParsedHigh, ParsedLow);
    return true;
}

bool FCMLSimulationTick::TryNext(FCMLSimulationTick& OutNext) const
{
    if (Value == MAX_uint64)
    {
        OutNext = *this;
        return false;
    }

    OutNext = FCMLSimulationTick(Value + 1);
    return true;
}

bool FCMLNonNegativeQuantity::TryAdd(
    const FCMLNonNegativeQuantity& Amount,
    FCMLNonNegativeQuantity& OutResult) const
{
    if (Amount.Value > MAX_int64 - Value)
    {
        OutResult = *this;
        return false;
    }

    OutResult.Value = Value + Amount.Value;
    return true;
}

bool FCMLNonNegativeQuantity::TrySubtract(
    const FCMLNonNegativeQuantity& Amount,
    FCMLNonNegativeQuantity& OutResult) const
{
    if (Amount.Value > Value)
    {
        OutResult = *this;
        return false;
    }

    OutResult.Value = Value - Amount.Value;
    return true;
}

int32 FCMLFixedStepClock::Accumulate(const double DeltaSeconds, const int32 MaxSteps)
{
    if (!FMath::IsFinite(DeltaSeconds) || DeltaSeconds <= 0.0 || MaxSteps <= 0)
    {
        return 0;
    }

    AccumulatorSeconds += DeltaSeconds;
    const int32 AvailableSteps = FMath::FloorToInt(AccumulatorSeconds / StepSeconds);
    const int32 Steps = FMath::Min(AvailableSteps, MaxSteps);
    AccumulatorSeconds -= Steps * StepSeconds;

    // Never retain an unbounded backlog after a hitch.
    AccumulatorSeconds = FMath::Min(AccumulatorSeconds, StepSeconds * MaxSteps);
    return Steps;
}

void FCMLFixedStepClock::Reset()
{
    AccumulatorSeconds = 0.0;
}

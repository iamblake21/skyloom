#include "Environment/CMLDayNightSubsystem.h"

#include "EngineUtils.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StructOnScope.h"
#include "UObject/UnrealType.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLDayNight, Log, All);

namespace
{
    FString CanonicalizeMemberName(FString Name)
    {
        FString Canonical;
        Canonical.Reserve(Name.Len());
        for (const TCHAR Character : Name)
        {
            if (FChar::IsAlnum(Character))
            {
                Canonical.AppendChar(FChar::ToLower(Character));
            }
        }
        return Canonical;
    }

    bool SetClockParameter(FProperty& Property, void* ParameterMemory, const double Value)
    {
        void* ValueAddress = Property.ContainerPtrToValuePtr<void>(ParameterMemory);
        if (FNumericProperty* NumericProperty = CastField<FNumericProperty>(&Property))
        {
            if (NumericProperty->IsInteger())
            {
                NumericProperty->SetIntPropertyValue(ValueAddress, FMath::RoundToInt64(Value));
            }
            else
            {
                NumericProperty->SetFloatingPointPropertyValue(ValueAddress, Value);
            }
            return true;
        }

        const FString Formatted = FString::Printf(TEXT("%02d"), FMath::RoundToInt(Value));
        if (FStrProperty* StringProperty = CastField<FStrProperty>(&Property))
        {
            StringProperty->SetPropertyValue(ValueAddress, Formatted);
            return true;
        }
        if (FNameProperty* NameProperty = CastField<FNameProperty>(&Property))
        {
            NameProperty->SetPropertyValue(ValueAddress, FName(*Formatted));
            return true;
        }
        if (FTextProperty* TextProperty = CastField<FTextProperty>(&Property))
        {
            TextProperty->SetPropertyValue(ValueAddress, FText::FromString(Formatted));
            return true;
        }
        return false;
    }
}

bool UCMLDayNightSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
    if (!Super::ShouldCreateSubsystem(Outer))
    {
        return false;
    }

    // The intro owns a deliberately frozen, shot-specific Alien sky.  The
    // project clock belongs to gameplay and must never discover or configure
    // that transient actor after it spawns.
    const UWorld* World = Cast<UWorld>(Outer);
    return World == nullptr
        || !World->GetMapName().Contains(
            TEXT("A_01_IntroCinematic"), ESearchCase::IgnoreCase);
}

void UCMLDayNightSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
    Super::Initialize(Collection);
    TimeOfDayHours = 12.0f;
    SecondsPerFullDay = 1200.0f;
    bAdvanceClock = true;
    InitialSyncDelayRemaining = 0.25f;
    bInitialSyncPending = true;
    SoStylizedSky.Reset();
    bLoggedSuccessfulBinding = false;
    bLoggedMissingSky = false;
    bLoggedMissingClockFunction = false;
}

void UCMLDayNightSubsystem::Deinitialize()
{
    SoStylizedSky.Reset();
    Super::Deinitialize();
}

void UCMLDayNightSubsystem::Tick(const float DeltaTime)
{
    UWorld* World = GetWorld();
    if (World == nullptr || !World->IsGameWorld())
    {
        return;
    }

    if (bAdvanceClock)
    {
        const float SafeDayDuration = FMath::Max(10.0f, SecondsPerFullDay);
        TimeOfDayHours = WrapHour(TimeOfDayHours + DeltaTime * 24.0f / SafeDayDuration);
    }

    if (!bInitialSyncPending)
    {
        return;
    }

    InitialSyncDelayRemaining -= DeltaTime;
    if (InitialSyncDelayRemaining > 0.0f)
    {
        return;
    }

    ApplyCurrentTime();
    bInitialSyncPending = false;
}

TStatId UCMLDayNightSubsystem::GetStatId() const
{
    RETURN_QUICK_DECLARE_CYCLE_STAT(UCMLDayNightSubsystem, STATGROUP_Tickables);
}

void UCMLDayNightSubsystem::SetTimeOfDayHours(const float NewTimeOfDayHours)
{
    TimeOfDayHours = WrapHour(NewTimeOfDayHours);
    bInitialSyncPending = false;
    ApplyCurrentTime();
    OnTimeOfDayChanged.Broadcast(TimeOfDayHours);
}

void UCMLDayNightSubsystem::AddHours(const float Hours)
{
    SetTimeOfDayHours(TimeOfDayHours + Hours);
}

void UCMLDayNightSubsystem::SetClockRunning(const bool bRunning)
{
    bAdvanceClock = bRunning;
    if (AActor* SkyActor = ResolveSoStylizedSky())
    {
        SetMarketplaceCycleEnabled(*SkyActor, bRunning);
    }
}

float UCMLDayNightSubsystem::WrapHour(const float Hour)
{
    float Wrapped = FMath::Fmod(Hour, 24.0f);
    if (Wrapped < 0.0f)
    {
        Wrapped += 24.0f;
    }
    return Wrapped;
}

AActor* UCMLDayNightSubsystem::ResolveSoStylizedSky()
{
    if (SoStylizedSky.IsValid())
    {
        return SoStylizedSky.Get();
    }

    UWorld* World = GetWorld();
    if (World == nullptr)
    {
        return nullptr;
    }

    for (TActorIterator<AActor> Iterator(World); Iterator; ++Iterator)
    {
        AActor* Candidate = *Iterator;
        if (Candidate == nullptr || Candidate->GetClass() == nullptr)
        {
            continue;
        }

        const FString ClassName = Candidate->GetClass()->GetName();
        if (!ClassName.Contains(TEXT("BP_StylizedSky"), ESearchCase::IgnoreCase))
        {
            continue;
        }

        SoStylizedSky = Candidate;
        ConfigureMarketplaceCycle(*Candidate);
        bLoggedMissingSky = false;
        return Candidate;
    }

    if (!bLoggedMissingSky)
    {
        UE_LOG(
            LogCMLDayNight,
            Verbose,
            TEXT("No So Stylized sky is present in %s; the gameplay clock continues without a visual adapter."),
            *World->GetMapName());
        bLoggedMissingSky = true;
    }
    return nullptr;
}

void UCMLDayNightSubsystem::SetMarketplaceCycleEnabled(AActor& SkyActor, const bool bEnabled)
{
    for (TFieldIterator<FProperty> PropertyIterator(SkyActor.GetClass());
         PropertyIterator;
         ++PropertyIterator)
    {
        FBoolProperty* BoolProperty = CastField<FBoolProperty>(*PropertyIterator);
        if (BoolProperty == nullptr)
        {
            continue;
        }

        const FString InternalName = CanonicalizeMemberName(BoolProperty->GetName());
        if (InternalName != TEXT("daycycleenabled"))
        {
            continue;
        }

        BoolProperty->SetPropertyValue_InContainer(&SkyActor, bEnabled);
        UE_LOG(
            LogCMLDayNight,
            Display,
            TEXT("So Stylized day cycle %s."),
            bEnabled ? TEXT("enabled") : TEXT("paused"));
        return;
    }
}

void UCMLDayNightSubsystem::ConfigureMarketplaceCycle(AActor& SkyActor)
{
    SetMarketplaceCycleEnabled(SkyActor, bAdvanceClock);

    for (TFieldIterator<FProperty> PropertyIterator(SkyActor.GetClass());
         PropertyIterator;
         ++PropertyIterator)
    {
        FProperty* Property = *PropertyIterator;
        const FString InternalName = CanonicalizeMemberName(Property->GetName());
        const bool bIsDayLength = InternalName == TEXT("daylength");
        const bool bIsNightLength = InternalName == TEXT("nightlength");
        if (!bIsDayLength && !bIsNightLength)
        {
            continue;
        }

        if (FNumericProperty* NumericProperty = CastField<FNumericProperty>(Property))
        {
            void* ValueAddress = Property->ContainerPtrToValuePtr<void>(&SkyActor);
            if (NumericProperty->IsInteger())
            {
                NumericProperty->SetIntPropertyValue(ValueAddress, static_cast<int64>(600));
            }
            else
            {
                NumericProperty->SetFloatingPointPropertyValue(ValueAddress, 600.0);
            }
        }
    }

    // Freeze All Time overrides the individual day-cycle toggle in the pack.
    for (TFieldIterator<FProperty> PropertyIterator(SkyActor.GetClass());
         PropertyIterator;
         ++PropertyIterator)
    {
        FBoolProperty* BoolProperty = CastField<FBoolProperty>(*PropertyIterator);
        if (BoolProperty == nullptr)
        {
            continue;
        }

        const FString InternalName = CanonicalizeMemberName(BoolProperty->GetName());
        if (InternalName == TEXT("freezealltime"))
        {
            BoolProperty->SetPropertyValue_InContainer(&SkyActor, false);
            break;
        }
    }
}

bool UCMLDayNightSubsystem::ApplyClockToSoStylized(AActor& SkyActor)
{
    static const FName ClockFunctionName(TEXT("Set New Time ClockBased"));
    UFunction* ClockFunction = SkyActor.FindFunction(ClockFunctionName);
    if (ClockFunction == nullptr)
    {
        if (!bLoggedMissingClockFunction)
        {
            UE_LOG(
                LogCMLDayNight,
                Error,
                TEXT("%s does not expose So Stylized function '%s'."),
                *SkyActor.GetName(),
                *ClockFunctionName.ToString());
            bLoggedMissingClockFunction = true;
        }
        return false;
    }

    FStructOnScope Parameters(ClockFunction);
    void* ParameterMemory = Parameters.GetStructMemory();

    // The official ClockBased API accepts conventional civil time and performs
    // the pack's internal midnight-to-sunrise conversion itself. Passing an
    // additional 12-hour offset would turn CML noon into So Stylized midnight.
    const float SoStylizedTimeHours = TimeOfDayHours;
    const double TotalSeconds = static_cast<double>(SoStylizedTimeHours) * 3600.0;
    const int32 Hour = FMath::FloorToInt(TotalSeconds / 3600.0) % 24;
    const int32 Minute = FMath::FloorToInt(FMath::Fmod(TotalSeconds, 3600.0) / 60.0);
    const double Second = FMath::Fmod(TotalSeconds, 60.0);
    int32 AssignedInputs = 0;

    for (TFieldIterator<FProperty> PropertyIterator(ClockFunction);
         PropertyIterator;
         ++PropertyIterator)
    {
        FProperty* Property = *PropertyIterator;
        if (!Property->HasAnyPropertyFlags(CPF_Parm) ||
            Property->HasAnyPropertyFlags(CPF_ReturnParm | CPF_OutParm))
        {
            continue;
        }

        // Match the same reflected argument names exposed by Python's
        // call_method: new_hour, new_minutes, new_seconds. Blueprint field
        // iteration is not declaration ordered, so positional iteration can
        // encounter optional/internal clock parameters first.
        const FString InternalName = CanonicalizeMemberName(Property->GetName());
        double Value = 0.0;
        if (InternalName == TEXT("newhour"))
        {
            Value = Hour;
        }
        else if (InternalName == TEXT("newminutes"))
        {
            Value = Minute;
        }
        else if (InternalName == TEXT("newseconds"))
        {
            Value = Second;
        }
        else if (InternalName == TEXT("dailyhours"))
        {
            Value = 24.0;
        }
        else if (InternalName == TEXT("hourlyminutes"))
        {
            Value = 60.0;
        }
        else if (InternalName == TEXT("minutelyseconds"))
        {
            Value = 60.0;
        }
        else
        {
            continue;
        }

        if (SetClockParameter(*Property, ParameterMemory, Value))
        {
            ++AssignedInputs;
        }
        else
        {
            UE_LOG(
                LogCMLDayNight,
                Error,
                TEXT("Unsupported So Stylized clock input %s of type %s."),
                *Property->GetName(),
                *Property->GetClass()->GetName());
        }
    }

    if (AssignedInputs != 6)
    {
        UE_LOG(
            LogCMLDayNight,
            Error,
            TEXT("So Stylized clock expected 6 clock inputs but CML bound %d."),
            AssignedInputs);
        return false;
    }

    SkyActor.ProcessEvent(ClockFunction, ParameterMemory);
    bLoggedMissingClockFunction = false;
    if (!bLoggedSuccessfulBinding)
    {
        UE_LOG(
            LogCMLDayNight,
            Display,
            TEXT("CML day/night clock bound to %s at civil %.2f h (So Stylized %02d:%02d:%02d, 1,200 seconds per day)."),
            *SkyActor.GetName(),
            TimeOfDayHours,
            Hour,
            Minute,
            FMath::FloorToInt(Second));
        bLoggedSuccessfulBinding = true;
    }
    return true;
}

void UCMLDayNightSubsystem::ApplyCurrentTime()
{
    AActor* SkyActor = ResolveSoStylizedSky();
    if (SkyActor != nullptr)
    {
        ApplyClockToSoStylized(*SkyActor);
    }
}

#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"
#include "CMLDayNightSubsystem.generated.h"

class AActor;

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FCMLTimeOfDayChanged, float, TimeOfDayHours);

/**
 * Project-owned gameplay clock for the 24-hour environment cycle.
 *
 * Unity's MeasuredStylizedDaylight advanced a 24-hour clock from noon over a
 * 1,200 second real-time day.  Unreal keeps that contract here and treats the
 * Marketplace sky as a rendering adapter: gameplay owns the time, while
 * BP_StylizedSky receives hour/minute/second through its official clock API.
 */
UCLASS()
class CHANGINGMYLIFE_API UCMLDayNightSubsystem final : public UTickableWorldSubsystem
{
    GENERATED_BODY()

public:
    virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
    virtual void Initialize(FSubsystemCollectionBase& Collection) override;
    virtual void Deinitialize() override;
    virtual void Tick(float DeltaTime) override;
    virtual TStatId GetStatId() const override;
    virtual bool IsTickableInEditor() const override { return false; }

    /** Current canonical gameplay time in the range [0, 24). */
    UFUNCTION(BlueprintPure, Category="CML|Day Night")
    float GetTimeOfDayHours() const { return TimeOfDayHours; }

    /** One complete game day takes 1,200 real seconds, matching the Unity build. */
    UFUNCTION(BlueprintPure, Category="CML|Day Night")
    float GetSecondsPerFullDay() const { return SecondsPerFullDay; }

    UFUNCTION(BlueprintPure, Category="CML|Day Night")
    bool IsClockRunning() const { return bAdvanceClock; }

    UFUNCTION(BlueprintCallable, Category="CML|Day Night")
    void SetTimeOfDayHours(float NewTimeOfDayHours);

    UFUNCTION(BlueprintCallable, Category="CML|Day Night")
    void AddHours(float Hours);

    UFUNCTION(BlueprintCallable, Category="CML|Day Night")
    void SetClockRunning(bool bRunning);

    UPROPERTY(BlueprintAssignable, Category="CML|Day Night")
    FCMLTimeOfDayChanged OnTimeOfDayChanged;

private:
    static float WrapHour(float Hour);
    AActor* ResolveSoStylizedSky();
    void ConfigureMarketplaceCycle(AActor& SkyActor);
    void SetMarketplaceCycleEnabled(AActor& SkyActor, bool bEnabled);
    bool ApplyClockToSoStylized(AActor& SkyActor);
    void ApplyCurrentTime();

    UPROPERTY(Transient)
    TWeakObjectPtr<AActor> SoStylizedSky;

    UPROPERTY(Transient)
    float TimeOfDayHours = 12.0f;

    UPROPERTY(Transient)
    float SecondsPerFullDay = 1200.0f;

    UPROPERTY(Transient)
    bool bAdvanceClock = true;

    // The Marketplace Blueprint initializes its own timelines during
    // BeginPlay. Apply our noon start once immediately afterwards, then let
    // its official 600 s day + 600 s night timelines render the same clock.
    float InitialSyncDelayRemaining = 0.25f;
    bool bInitialSyncPending = true;
    bool bLoggedSuccessfulBinding = false;
    bool bLoggedMissingSky = false;
    bool bLoggedMissingClockFunction = false;
};

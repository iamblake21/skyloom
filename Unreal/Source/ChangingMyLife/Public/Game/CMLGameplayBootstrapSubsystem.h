#pragma once

#include "CoreMinimal.h"
#include "Subsystems/WorldSubsystem.h"

#include "CMLGameplayBootstrapSubsystem.generated.h"

/** Connects migrated visual actors to the native gameplay runtime at world start. */
UCLASS()
class CHANGINGMYLIFE_API UCMLGameplayBootstrapSubsystem final : public UWorldSubsystem
{
    GENERATED_BODY()

public:
    virtual bool ShouldCreateSubsystem(UObject* Outer) const override;
    virtual void OnWorldBeginPlay(UWorld& InWorld) override;
    virtual void Deinitialize() override;

private:
    void BootstrapWorld();
    void AttachKnownStationTargets();
    void ConnectAuthoredGatherables();
    void ConnectStarterMiningSources();
    void ConnectStarterTrees();
    void HandleRuntimeCommandResolved(
        const struct FCMLSimulationCommand& Command,
        bool bSucceeded,
        bool bWorldCommitted);
};

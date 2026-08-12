#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "Simulation/CMLMachineBuildRule.h"

class AActor;
class USceneComponent;

/** One binding between a deterministic build item and its migrated artwork. */
class CHANGINGMYLIFE_API FCMLBuildableVisuals
{
public:
    static bool IsBuildable(const FCMLStableId& ItemId);
    static const TCHAR* ClassPath(const FCMLStableId& ItemId);
    static UClass* LoadActorClass(const FCMLStableId& ItemId);
    static FString DisplayName(const FCMLStableId& ItemId);
    static ECMLMachineBuildKind BuildKind(const FCMLStableId& ItemId);
    static ECMLMachineNodeKind NodeKind(const FCMLStableId& ItemId);
    static FCMLStableId DefinitionId(const FCMLStableId& ItemId);

    static FVector WorldLocation(const FCMLMachineBuildPose& Pose);
    static FRotator WorldRotation(
        const FCMLStableId& ItemId, const FCMLMachineBuildPose& Pose);

    /** Reconstructs multi-part Unity stations from their complete runtime mesh. */
    static USceneComponent* RebuildMigratedVisual(
        AActor& Actor, const FCMLStableId& ItemId);

    /** Replaces every visible slot with Unity's official valid/invalid material. */
    static void ConfigureHologram(AActor& Actor, bool bValid);

    /** Restores collision on an accepted runtime construction. */
    static void ConfigureCommittedCollision(AActor& Actor);
};

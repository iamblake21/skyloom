#pragma once

#include "CoreMinimal.h"
#include "CMLSaveEnvelope.generated.h"

/**
 * Versioned persistence boundary, ported from CML.Persistence.SaveEnvelope.
 *
 * Worth knowing before extending this: the Unity persistence layer is a stub.
 * `CML.Persistence` contains exactly two files — this envelope and a version
 * constant — and no reader, writer or payload. Nothing in the Unity runtime
 * writes a save. The migration therefore reproduces the boundary faithfully
 * rather than inventing a save format that the Unity build never had; the
 * payload belongs to whichever engine implements saving first, and if that is
 * Unreal then this is where it starts, not a thing to be back-ported.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLSaveEnvelope
{
    GENERATED_BODY()

    /** Bumped whenever the envelope's own shape changes. */
    static constexpr int32 CurrentVersion = 1;

    UPROPERTY(BlueprintReadWrite, Category="CML|Persistence")
    int32 SchemaVersion = CurrentVersion;

    UPROPERTY(BlueprintReadWrite, Category="CML|Persistence")
    int64 SimulationTick = 0;

    UPROPERTY(BlueprintReadWrite, Category="CML|Persistence")
    FString ContentRevision;

    /**
     * A save from a newer schema cannot be read by an older build: the fields
     * it carries are unknown, so loading it would silently drop them.
     */
    bool IsReadableByThisBuild() const { return SchemaVersion <= CurrentVersion; }
};

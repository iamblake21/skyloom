#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"

#include "CMLLandscapeImportLibrary.generated.h"

class ALandscape;
class ULandscapeLayerInfoObject;
class UWorld;

/** One Unity terrain layer, as a landscape paint layer plus its weights. */
USTRUCT(BlueprintType)
struct FCMLLandscapeLayerImport
{
    GENERATED_BODY()

    /** Layer name; also names the ULandscapeLayerInfoObject asset. */
    UPROPERTY(BlueprintReadWrite, Category = "CML")
    FString Name;

    /** Path to the raw file of one uint8 weight per landscape vertex. */
    UPROPERTY(BlueprintReadWrite, Category = "CML")
    FString WeightFile;

    /** Uses Unreal's reserved Landscape Visibility layer instead of a paint layer. */
    UPROPERTY(BlueprintReadWrite, Category = "CML")
    bool IsVisibility = false;
};

/**
 * Builds an Unreal Landscape from the files `extract_unity_terrain.py` writes.
 *
 * `ALandscape::Import` is editor-only C++ and has no scripting exposure, so the
 * terrain step of the migration cannot be done from Python alone the way every
 * other step is. This library is the smallest bridge that closes that gap: it
 * takes paths and numbers, and leaves every decision about *what* to import to
 * the exporter and the Python driver.
 */
UCLASS()
class UCMLLandscapeImportLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    /** Blocks until editor asset/shader compilation is integrated by the render thread. */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static void WaitForEditorCompilation();

    /** Reports the actual component-local render material chain used by Landscape. */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static TArray<FString> DescribeLandscapeMaterialInstances(ALandscape* Landscape);

    /** Rebuild component-local Landscape material permutations after a master changes. */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static bool RefreshLandscapeMaterials(ALandscape* Landscape);

    /** Registers and fills one paint layer across an existing Landscape. */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static bool FillLandscapeLayer(
        ALandscape* Landscape,
        ULandscapeLayerInfoObject* LayerInfo,
        int32 Weight = 255);

    /**
     * Registers an official paint layer and restores its weights from one or
     * more raw R8 files. Multiple files are added and clamped to 255, which is
     * useful when several source terrain layers collapse into one target layer.
     */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static bool ImportLandscapeLayerFromRawFiles(
        ALandscape* Landscape,
        ULandscapeLayerInfoObject* LayerInfo,
        const TArray<FString>& WeightFiles);

    /** Builds grass maps and synchronously generates instances around validation cameras. */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static bool BuildLandscapeGrass(
        ALandscape* Landscape,
        const TArray<FVector>& CameraLocations);

    /**
     * Spawns a landscape and imports the heightmap and weightmaps into it.
     *
     * @param Resolution      Vertices per side; must be ComponentCount * SectionSizeQuads + 1.
     * @param HeightmapFile   Raw little-endian uint16 heights, Resolution^2 of them.
     * @return The landscape actor, or nullptr with a reason logged.
     */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static ALandscape* ImportLandscapeFromRawFiles(
        UWorld* World,
        const FString& ActorLabel,
        const FString& HeightmapFile,
        int32 Resolution,
        int32 SectionSizeQuads,
        int32 SubsectionsPerComponent,
        FVector Location,
        FVector DrawScale,
        const TArray<FCMLLandscapeLayerImport>& Layers,
        const FString& LayerInfoPackagePath,
        const FString& LandscapeMaterialPath);

    /**
     * Reads back one row of stored landscape heights.
     *
     * Import reporting success only says the call returned. This reads what the
     * landscape actually holds, so the migration can be checked against the
     * Unity heightmap sample by sample rather than taken on trust.
     */
    UFUNCTION(BlueprintCallable, Category = "CML|Migration")
    static TArray<int32> ReadLandscapeHeightRow(ALandscape* Landscape, int32 Row, int32 Width);
};

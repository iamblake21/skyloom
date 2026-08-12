#include "CMLLandscapeImportLibrary.h"

#include "AssetCompilingManager.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "Landscape.h"
#include "LandscapeEdit.h"
#include "LandscapeInfo.h"
#include "LandscapeComponent.h"
#include "LandscapeLayerInfoObject.h"
#include "LandscapeProxy.h"
#include "Materials/MaterialInstance.h"
#include "Materials/MaterialInstanceConstant.h"
#include "Materials/Material.h"
#include "Materials/MaterialInterface.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "RenderingThread.h"
#include "ShaderCompiler.h"
#include "UObject/Package.h"
#include "UObject/SavePackage.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLLandscapeImport, Log, All);

namespace
{
    /** Loads a raw file of fixed-width samples, refusing a wrong-sized one. */
    template <typename SampleType>
    bool LoadRawSamples(const FString& FilePath, int32 ExpectedCount, TArray<SampleType>& OutSamples)
    {
        TArray<uint8> Bytes;
        if (!FFileHelper::LoadFileToArray(Bytes, *FilePath))
        {
            UE_LOG(LogCMLLandscapeImport, Error, TEXT("Cannot read %s"), *FilePath);
            return false;
        }
        const int64 ExpectedBytes = static_cast<int64>(ExpectedCount) * sizeof(SampleType);
        if (Bytes.Num() != ExpectedBytes)
        {
            UE_LOG(LogCMLLandscapeImport, Error,
                TEXT("%s holds %d bytes, expected %lld"), *FilePath, Bytes.Num(), ExpectedBytes);
            return false;
        }
        OutSamples.SetNumUninitialized(ExpectedCount);
        FMemory::Memcpy(OutSamples.GetData(), Bytes.GetData(), ExpectedBytes);
        return true;
    }

    /** Finds or creates the layer info asset a paint layer needs to exist at all. */
    ULandscapeLayerInfoObject* AcquireLayerInfo(const FString& PackagePath, const FString& Name)
    {
        const FString PackageName = PackagePath / Name;
        UPackage* Package = FindPackage(nullptr, *PackageName);
        if (Package == nullptr)
        {
            Package = LoadPackage(nullptr, *PackageName, LOAD_NoWarn | LOAD_Quiet);
        }
        if (Package != nullptr)
        {
            if (ULandscapeLayerInfoObject* Existing =
                    FindObject<ULandscapeLayerInfoObject>(Package, *Name))
            {
                return Existing;
            }
        }
        else
        {
            Package = CreatePackage(*PackageName);
        }
        if (Package == nullptr)
        {
            return nullptr;
        }

        ULandscapeLayerInfoObject* LayerInfo = NewObject<ULandscapeLayerInfoObject>(
            Package, FName(*Name), RF_Public | RF_Standalone | RF_Transactional);
        LayerInfo->SetLayerName(FName(*Name), /*bInModify=*/false);
        FAssetRegistryModule::AssetCreated(LayerInfo);

        FSavePackageArgs SaveArguments;
        SaveArguments.TopLevelFlags = RF_Public | RF_Standalone;
        SaveArguments.SaveFlags = SAVE_NoError;
        const FString FileName = FPackageName::LongPackageNameToFilename(
            PackageName, FPackageName::GetAssetPackageExtension());
        if (!UPackage::SavePackage(Package, LayerInfo, *FileName, SaveArguments))
        {
            UE_LOG(LogCMLLandscapeImport, Warning, TEXT("Could not save %s"), *PackageName);
        }
        return LayerInfo;
    }
}

void UCMLLandscapeImportLibrary::WaitForEditorCompilation()
{
    FAssetCompilingManager::Get().FinishAllCompilation();
    if (GShaderCompilingManager != nullptr)
    {
        GShaderCompilingManager->FinishAllCompilation();
    }
    FlushRenderingCommands();
    UE_LOG(LogCMLLandscapeImport, Display,
        TEXT("Editor asset and shader compilation is ready for rendering"));
}

TArray<FString> UCMLLandscapeImportLibrary::DescribeLandscapeMaterialInstances(
    ALandscape* Landscape)
{
    TArray<FString> Results;
    if (Landscape == nullptr)
    {
        Results.Add(TEXT("<null landscape>"));
        return Results;
    }

    for (int32 Index = 0; Index < Landscape->LandscapeComponents.Num(); ++Index)
    {
        ULandscapeComponent* Component = Landscape->LandscapeComponents[Index];
        if (Component == nullptr)
        {
            Results.Add(FString::Printf(TEXT("%d:<null component>"), Index));
            continue;
        }

        UMaterialInterface* Requested = Component->GetLandscapeMaterial();
        UMaterialInterface* RenderMaterial = Component->GetMaterial(0);
        UMaterialInstance* Generated = Component->GetMaterialInstance(0, false);
        UMaterialInterface* Parent = Generated != nullptr ? Generated->Parent : nullptr;
        UMaterial* BaseMaterial = Generated != nullptr ? Generated->GetMaterial() : nullptr;
        const bool bChildOfBase = Generated != nullptr && BaseMaterial != nullptr
            ? Generated->IsChildOf(BaseMaterial)
            : false;
        const bool bStaticLightingUsage = Generated != nullptr
            ? Generated->CheckMaterialUsage_Concurrent(MATUSAGE_StaticLighting)
            : false;
        const bool bDefaultBase = BaseMaterial == UMaterial::GetDefaultMaterial(MD_Surface);
        const bool bPSOPrecaching = Component->GetPSOPrecacheComponentData().IsPSOPrecaching();
        Results.Add(FString::Printf(
            TEXT("%d component=%s requested=%s render=%s generated=%s parent=%s base=%s child=%d staticUsage=%d defaultBase=%d psoPrecaching=%d"),
            Index,
            *Component->GetPathName(),
            Requested != nullptr ? *Requested->GetPathName() : TEXT("<null>"),
            RenderMaterial != nullptr ? *RenderMaterial->GetPathName() : TEXT("<null>"),
            Generated != nullptr ? *Generated->GetPathName() : TEXT("<null>"),
            Parent != nullptr ? *Parent->GetPathName() : TEXT("<null>"),
            BaseMaterial != nullptr ? *BaseMaterial->GetPathName() : TEXT("<null>"),
            bChildOfBase ? 1 : 0,
            bStaticLightingUsage ? 1 : 0,
            bDefaultBase ? 1 : 0,
            bPSOPrecaching ? 1 : 0));
    }
    return Results;
}

bool UCMLLandscapeImportLibrary::RefreshLandscapeMaterials(ALandscape* Landscape)
{
    if (Landscape == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error, TEXT("Cannot refresh a null Landscape"));
        return false;
    }

    // Landscape components can carry static-lighting data even when their
    // current level is previewed with movable lights.  FLandscapeSceneProxy
    // rejects any material that lacks that permutation and silently replaces
    // it with the engine grid material.  Force the usage before rebuilding the
    // component-local MIC chain so both diagnostics and production render the
    // requested master rather than a plausible-looking fallback.
    TSet<UMaterialInterface*> LandscapeMaterials;
    Landscape->RetrieveAllLandscapeMaterials(LandscapeMaterials);
    for (UMaterialInterface* Material : LandscapeMaterials)
    {
        if (Material != nullptr && !Material->CheckMaterialUsage(MATUSAGE_StaticLighting))
        {
            UE_LOG(LogCMLLandscapeImport, Error,
                TEXT("Landscape material %s cannot compile the Static Lighting usage"),
                *Material->GetPathName());
            return false;
        }
    }

    Landscape->UpdateAllComponentMaterialInstances(/*bInInvalidateCombinationMaterials=*/true);
    Landscape->InvalidateGeneratedComponentData(/*bInvalidateLightingCache=*/false);
    WaitForEditorCompilation();
    Landscape->ReregisterAllComponents();
    FlushRenderingCommands();
    Landscape->MarkPackageDirty();
    UE_LOG(LogCMLLandscapeImport, Display,
        TEXT("Refreshed Landscape material permutations for %s"), *Landscape->GetActorLabel());
    return true;
}

bool UCMLLandscapeImportLibrary::FillLandscapeLayer(
    ALandscape* Landscape,
    ULandscapeLayerInfoObject* LayerInfo,
    const int32 Weight)
{
    if (Landscape == nullptr || LayerInfo == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Cannot fill a Landscape layer with a null Landscape or LayerInfo"));
        return false;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (LandscapeInfo == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Landscape %s has no LandscapeInfo"), *Landscape->GetActorLabel());
        return false;
    }

    const FName LayerName = LayerInfo->GetLayerName();
    Landscape->AddTargetLayer(LayerName, FLandscapeTargetLayerSettings(LayerInfo));
    LandscapeInfo->UpdateLayerInfoMap(Landscape);
    const int32 LayerIndex = LandscapeInfo->GetLayerInfoIndex(LayerName);
    if (LayerIndex != INDEX_NONE)
    {
        LandscapeInfo->Layers[LayerIndex].LayerInfoObj = LayerInfo;
    }

    int32 MinX = 0;
    int32 MinY = 0;
    int32 MaxX = 0;
    int32 MaxY = 0;
    if (!LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY))
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Could not determine extent for Landscape %s"), *Landscape->GetActorLabel());
        return false;
    }

    const int32 Width = MaxX - MinX + 1;
    const int32 Height = MaxY - MinY + 1;
    TArray<uint8> Weights;
    Weights.Init(static_cast<uint8>(FMath::Clamp(Weight, 0, 255)), Width * Height);
    FLandscapeEditDataInterface Edit(LandscapeInfo, /*bInUploadTextureChangesToGPU=*/true);
    Edit.SetAlphaData(
        LayerInfo,
        MinX,
        MinY,
        MaxX,
        MaxY,
        Weights.GetData(),
        Width,
        ELandscapeLayerPaintingRestriction::None);
    Edit.Flush();

    Landscape->UpdateAllComponentMaterialInstances(/*bInInvalidateCombinationMaterials=*/true);
    Landscape->InvalidateGeneratedComponentData(/*bInvalidateLightingCache=*/false);
    Landscape->MarkPackageDirty();
    UE_LOG(LogCMLLandscapeImport, Display,
        TEXT("Filled Landscape layer %s on %s with weight %d"),
        *LayerName.ToString(), *Landscape->GetActorLabel(), FMath::Clamp(Weight, 0, 255));
    return true;
}

bool UCMLLandscapeImportLibrary::ImportLandscapeLayerFromRawFiles(
    ALandscape* Landscape,
    ULandscapeLayerInfoObject* LayerInfo,
    const TArray<FString>& WeightFiles)
{
    if (Landscape == nullptr || LayerInfo == nullptr || WeightFiles.IsEmpty())
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Cannot import a Landscape layer without a Landscape, LayerInfo and source files"));
        return false;
    }

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (LandscapeInfo == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Landscape %s has no LandscapeInfo"), *Landscape->GetActorLabel());
        return false;
    }

    int32 MinX = 0;
    int32 MinY = 0;
    int32 MaxX = 0;
    int32 MaxY = 0;
    if (!LandscapeInfo->GetLandscapeExtent(MinX, MinY, MaxX, MaxY))
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Could not determine extent for Landscape %s"), *Landscape->GetActorLabel());
        return false;
    }

    const int32 Width = MaxX - MinX + 1;
    const int32 Height = MaxY - MinY + 1;
    const int32 SampleCount = Width * Height;
    TArray<uint8> CombinedWeights;
    CombinedWeights.Init(0, SampleCount);

    for (const FString& WeightFile : WeightFiles)
    {
        TArray<uint8> SourceWeights;
        if (!LoadRawSamples(WeightFile, SampleCount, SourceWeights))
        {
            return false;
        }
        for (int32 Index = 0; Index < SampleCount; ++Index)
        {
            CombinedWeights[Index] = static_cast<uint8>(FMath::Min(
                255,
                static_cast<int32>(CombinedWeights[Index]) + static_cast<int32>(SourceWeights[Index])));
        }
    }

    const FName LayerName = LayerInfo->GetLayerName();
    Landscape->AddTargetLayer(LayerName, FLandscapeTargetLayerSettings(LayerInfo));
    LandscapeInfo->UpdateLayerInfoMap(Landscape);
    const int32 LayerIndex = LandscapeInfo->GetLayerInfoIndex(LayerName);
    if (LayerIndex != INDEX_NONE)
    {
        LandscapeInfo->Layers[LayerIndex].LayerInfoObj = LayerInfo;
    }

    FLandscapeEditDataInterface Edit(LandscapeInfo, /*bInUploadTextureChangesToGPU=*/true);
    Edit.SetAlphaData(
        LayerInfo,
        MinX,
        MinY,
        MaxX,
        MaxY,
        CombinedWeights.GetData(),
        Width,
        ELandscapeLayerPaintingRestriction::None);
    Edit.Flush();

    // Read the stored values back before reporting success. This catches a
    // wrong resolution, missing component allocation or partial write here,
    // instead of leaving a visually plausible but incorrect paint layer.
    TArray<uint8> StoredWeights;
    StoredWeights.SetNumZeroed(SampleCount);
    FLandscapeEditDataInterface VerifyEdit(LandscapeInfo, /*bInUploadTextureChangesToGPU=*/false);
    VerifyEdit.GetWeightDataFast(
        LayerInfo,
        MinX,
        MinY,
        MaxX,
        MaxY,
        StoredWeights.GetData(),
        Width);

    int32 MismatchCount = 0;
    int32 MaxDifference = 0;
    for (int32 Index = 0; Index < SampleCount; ++Index)
    {
        const int32 Difference = FMath::Abs(
            static_cast<int32>(StoredWeights[Index]) - static_cast<int32>(CombinedWeights[Index]));
        if (Difference != 0)
        {
            ++MismatchCount;
            MaxDifference = FMath::Max(MaxDifference, Difference);
        }
    }
    if (MismatchCount != 0)
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Landscape layer %s verification failed: %d/%d samples differ (max difference %d)"),
            *LayerName.ToString(), MismatchCount, SampleCount, MaxDifference);
        return false;
    }

    Landscape->UpdateAllComponentMaterialInstances(/*bInInvalidateCombinationMaterials=*/true);
    Landscape->InvalidateGeneratedComponentData(/*bInvalidateLightingCache=*/false);
    Landscape->MarkPackageDirty();
    UE_LOG(LogCMLLandscapeImport, Display,
        TEXT("Imported and verified Landscape layer %s on %s from %d raw file(s), %dx%d samples"),
        *LayerName.ToString(), *Landscape->GetActorLabel(), WeightFiles.Num(), Width, Height);
    return true;
}

bool UCMLLandscapeImportLibrary::BuildLandscapeGrass(
    ALandscape* Landscape,
    const TArray<FVector>& CameraLocations)
{
    if (Landscape == nullptr || CameraLocations.IsEmpty())
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Cannot build Landscape grass without a Landscape and camera locations"));
        return false;
    }
    Landscape->BuildGrassMaps();
    WaitForEditorCompilation();
    Landscape->UpdateGrass(CameraLocations, /*bForceSync=*/true);
    FlushRenderingCommands();
    UE_LOG(LogCMLLandscapeImport, Display,
        TEXT("Built Landscape grass for %s around %d validation cameras"),
        *Landscape->GetActorLabel(), CameraLocations.Num());
    return true;
}

ALandscape* UCMLLandscapeImportLibrary::ImportLandscapeFromRawFiles(
    UWorld* World,
    const FString& ActorLabel,
    const FString& HeightmapFile,
    const int32 Resolution,
    const int32 SectionSizeQuads,
    const int32 SubsectionsPerComponent,
    const FVector Location,
    const FVector DrawScale,
    const TArray<FCMLLandscapeLayerImport>& Layers,
    const FString& LayerInfoPackagePath,
    const FString& LandscapeMaterialPath)
{
    if (World == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error, TEXT("No world to import into"));
        return nullptr;
    }

    const int32 QuadsPerComponent = SectionSizeQuads * SubsectionsPerComponent;
    if (QuadsPerComponent <= 0 || (Resolution - 1) % QuadsPerComponent != 0)
    {
        UE_LOG(LogCMLLandscapeImport, Error,
            TEXT("Resolution %d is not a whole number of %d-quad components"),
            Resolution, QuadsPerComponent);
        return nullptr;
    }

    const int32 SampleCount = Resolution * Resolution;
    TArray<uint16> Heights;
    if (!LoadRawSamples(HeightmapFile, SampleCount, Heights))
    {
        return nullptr;
    }

    TArray<FLandscapeImportLayerInfo> ImportLayers;
    ImportLayers.Reserve(Layers.Num());
    for (const FCMLLandscapeLayerImport& Layer : Layers)
    {
        TArray<uint8> Weights;
        if (!LoadRawSamples(Layer.WeightFile, SampleCount, Weights))
        {
            return nullptr;
        }
        ULandscapeLayerInfoObject* LayerInfo = Layer.IsVisibility
            ? ALandscapeProxy::VisibilityLayer
            : AcquireLayerInfo(LayerInfoPackagePath, Layer.Name);
        if (LayerInfo == nullptr)
        {
            UE_LOG(LogCMLLandscapeImport, Error,
                TEXT("Could not create layer info for %s"), *Layer.Name);
            return nullptr;
        }
        FLandscapeImportLayerInfo& Entry = ImportLayers.AddDefaulted_GetRef();
        Entry.LayerName = Layer.IsVisibility
            ? ALandscapeProxy::VisibilityLayer->GetLayerName()
            : FName(*Layer.Name);
        Entry.LayerInfo = LayerInfo;
        Entry.SourceFilePath = Layer.WeightFile;
        Entry.LayerData = MoveTemp(Weights);
    }

    // Re-running the migration must replace the ground rather than stack a
    // second landscape on top of the first.
    for (TActorIterator<ALandscape> It(World); It; ++It)
    {
        if (It->GetActorLabel() == ActorLabel)
        {
            World->DestroyActor(*It);
        }
    }

    ALandscape* Landscape = World->SpawnActor<ALandscape>(Location, FRotator::ZeroRotator);
    if (Landscape == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error, TEXT("Could not spawn a landscape actor"));
        return nullptr;
    }
    Landscape->SetActorRelativeScale3D(DrawScale);
    // Assigned before Import so the paint layers are compiled against the
    // material that will actually shade them, as the editor's own path does.
    if (!LandscapeMaterialPath.IsEmpty())
    {
        UMaterialInterface* Material =
            LoadObject<UMaterialInterface>(nullptr, *LandscapeMaterialPath);
        if (Material != nullptr)
        {
            Landscape->LandscapeMaterial = Material;
            // The same masked master contains LandscapeVisibilityMask.  Setting
            // it explicitly as the hole material prevents components painted
            // with visibility from briefly compiling an incompatible material
            // combination while the import is being finalised.
            Landscape->LandscapeHoleMaterial = Material;
        }
        else
        {
            UE_LOG(LogCMLLandscapeImport, Warning,
                TEXT("Landscape material %s could not be loaded"), *LandscapeMaterialPath);
        }
    }
    // Matches the engine's own new-landscape path: keep Lightmass within its
    // limits as the vertex count grows.
    Landscape->StaticLightingLOD =
        FMath::DivideAndRoundUp(FMath::CeilLogTwo((SampleCount) / (2048 * 2048) + 1), 2u);

    TMap<FGuid, TArray<uint16>> HeightsPerEditLayer;
    HeightsPerEditLayer.Add(FGuid(), MoveTemp(Heights));
    TMap<FGuid, TArray<FLandscapeImportLayerInfo>> LayersPerEditLayer;
    LayersPerEditLayer.Add(FGuid(), MoveTemp(ImportLayers));

    Landscape->Import(
        FGuid::NewGuid(),
        0, 0, Resolution - 1, Resolution - 1,
        SubsectionsPerComponent, SectionSizeQuads,
        HeightsPerEditLayer,
        *HeightmapFile,
        LayersPerEditLayer,
        // Unity normalises its splat weights so they total 1, which is exactly
        // what Unreal calls an additive alphamap.
        ELandscapeImportAlphamapType::Additive,
        TArrayView<const FLandscapeLayer>());

    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (LandscapeInfo == nullptr)
    {
        UE_LOG(LogCMLLandscapeImport, Error, TEXT("Import produced no landscape info"));
        return nullptr;
    }
    LandscapeInfo->UpdateLayerInfoMap(Landscape);

    for (const FLandscapeImportLayerInfo& Entry : LayersPerEditLayer[FGuid()])
    {
        Landscape->AddTargetLayer(
            Entry.LayerName,
            Entry.LayerInfo == ALandscapeProxy::VisibilityLayer
                ? FLandscapeTargetLayerSettings()
                : FLandscapeTargetLayerSettings(Entry.LayerInfo));
        const int32 Index = LandscapeInfo->GetLayerInfoIndex(Entry.LayerName);
        if (Index != INDEX_NONE)
        {
            LandscapeInfo->Layers[Index].LayerInfoObj = Entry.LayerInfo;
        }
    }

    // Import allocates weightmap channels before all target layers and the hole
    // material are registered.  Rebuild every component permutation now, while
    // the actor is in a consistent state, rather than relying on MapCheck to
    // repair stale LandscapeMaterialInstanceConstant arrays on the next load.
    Landscape->UpdateAllComponentMaterialInstances(/*bInInvalidateCombinationMaterials=*/true);
    Landscape->InvalidateGeneratedComponentData(/*bInvalidateLightingCache=*/false);

    Landscape->SetActorLabel(ActorLabel);
    UE_LOG(LogCMLLandscapeImport, Display,
        TEXT("Imported landscape '%s': %dx%d vertices, %d layers"),
        *ActorLabel, Resolution, Resolution, Layers.Num());
    return Landscape;
}

TArray<int32> UCMLLandscapeImportLibrary::ReadLandscapeHeightRow(
    ALandscape* Landscape, const int32 Row, const int32 Width)
{
    TArray<int32> Heights;
    if (Landscape == nullptr || Width <= 0)
    {
        return Heights;
    }
    ULandscapeInfo* LandscapeInfo = Landscape->GetLandscapeInfo();
    if (LandscapeInfo == nullptr)
    {
        return Heights;
    }

    // Two rows are read even though one is wanted. The engine maps a vertex
    // range to components with CalcComponentIndicesNoOverlap, which sends a
    // single row that sits on a component's far edge to the component *after*
    // the last one -- which does not exist, so nothing is written and the row
    // reads back as zeroes. A two-row band always lands inside a real
    // component.
    const int32 BandStart = (Row > 0) ? Row - 1 : Row;
    TArray<uint16> Raw;
    Raw.SetNumZeroed(Width * 2);
    FLandscapeEditDataInterface Interface(LandscapeInfo, /*bInUploadTextureChangesToGPU=*/false);
    Interface.GetHeightDataFast(0, BandStart, Width - 1, BandStart + 1, Raw.GetData(), 0);

    // Widened to int32 because Blueprint has no uint16.
    const int32 Offset = (Row - BandStart) * Width;
    Heights.Reserve(Width);
    for (int32 Index = 0; Index < Width; ++Index)
    {
        Heights.Add(static_cast<int32>(Raw[Offset + Index]));
    }
    return Heights;
}

#pragma once

#include "ProceduralMeshComponent.h"

#include "CMLTreeRuntimeMeshComponent.generated.h"

/**
 * Procedural trunk whose lighting/culling bounds remain those of the authored
 * static mesh. A millimetre-scale cut must not move the primitive's probe
 * sample and make the whole tree change illumination on the first hit.
 */
UCLASS()
class UCMLTreeRuntimeMeshComponent final : public UProceduralMeshComponent
{
    GENERATED_BODY()

public:
    void SetAuthoredLocalBounds(const FBoxSphereBounds& InBounds)
    {
        AuthoredLocalBounds = InBounds;
        bHasAuthoredLocalBounds = true;
        UpdateBounds();
        MarkRenderTransformDirty();
    }

    virtual FBoxSphereBounds CalcBounds(const FTransform& LocalToWorld) const override
    {
        return bHasAuthoredLocalBounds
            ? AuthoredLocalBounds.TransformBy(LocalToWorld)
            : Super::CalcBounds(LocalToWorld);
    }

private:
    FBoxSphereBounds AuthoredLocalBounds = FBoxSphereBounds(
        FVector::ZeroVector, FVector::ZeroVector, 0.0f);
    bool bHasAuthoredLocalBounds = false;
};

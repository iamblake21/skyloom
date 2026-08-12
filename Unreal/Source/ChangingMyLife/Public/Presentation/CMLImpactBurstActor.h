#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Presentation/CMLImpactBurstGeometry.h"
#include "CMLImpactBurstActor.generated.h"

/** Runtime renderer for Unity's mesh-based pickaxe smoke and fragments. */
UCLASS(NotBlueprintable, Transient)
class CHANGINGMYLIFE_API ACMLImpactBurstActor final : public AActor
{
    GENERATED_BODY()

public:
    ACMLImpactBurstActor();
    void Initialise(
        const FVector& Position,
        const FVector& SurfaceNormal,
        ECMLImpactSurface Surface);
    void InitialiseAirshipSmoke(
        const FVector& Position,
        const FVector& ExhaustDirection);
    virtual void Tick(float DeltaSeconds) override;

private:
    struct FParticle
    {
        TWeakObjectPtr<class UProceduralMeshComponent> Mesh;
        TWeakObjectPtr<class UMaterialInstanceDynamic> Material;
        FVector Velocity = FVector::ZeroVector;
        FRotator AngularVelocity = FRotator::ZeroRotator;
        float Age = 0.0f;
        float Lifetime = 1.0f;
        float StartSize = 1.0f;
        float GravityScale = 0.0f;
        float BaseAlpha = 1.0f;
        bool bSmoke = false;
    };

    void SpawnParticle(
        const FCMLBurstMesh& Geometry,
        const FLinearColor& Tint,
        const FVector& Direction,
        bool bSmoke,
        bool bStone,
        int32 Index);

    UPROPERTY(VisibleAnywhere)
    TObjectPtr<class USceneComponent> BurstRoot;

    TArray<FParticle> Particles;
    FRandomStream Random;
    FVector EmissionNormal = FVector::UpVector;
    FVector EmissionTangent = FVector::ForwardVector;
    FVector EmissionBitangent = FVector::RightVector;
    float Elapsed = 0.0f;
};

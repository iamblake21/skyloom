#include "Presentation/CMLImpactBurstActor.h"

#include "Components/SceneComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Materials/MaterialInterface.h"
#include "ProceduralMeshComponent.h"

namespace
{
    void BuildNormals(const FCMLBurstMesh& Geometry, TArray<FVector>& OutNormals)
    {
        OutNormals.Init(FVector::ZeroVector, Geometry.Vertices.Num());
        for (int32 Index = 0; Index + 2 < Geometry.Triangles.Num(); Index += 3)
        {
            const int32 A = Geometry.Triangles[Index];
            const int32 B = Geometry.Triangles[Index + 1];
            const int32 C = Geometry.Triangles[Index + 2];
            if (!OutNormals.IsValidIndex(A) || !OutNormals.IsValidIndex(B)
                || !OutNormals.IsValidIndex(C))
            {
                continue;
            }
            const FVector Face = FVector::CrossProduct(
                Geometry.Vertices[B] - Geometry.Vertices[A],
                Geometry.Vertices[C] - Geometry.Vertices[A]).GetSafeNormal();
            OutNormals[A] += Face;
            OutNormals[B] += Face;
            OutNormals[C] += Face;
        }
        for (FVector& Normal : OutNormals)
        {
            Normal = Normal.GetSafeNormal(UE_SMALL_NUMBER, FVector::UpVector);
        }
    }
}

ACMLImpactBurstActor::ACMLImpactBurstActor()
{
    PrimaryActorTick.bCanEverTick = true;
    BurstRoot = CreateDefaultSubobject<USceneComponent>(TEXT("BurstRoot"));
    SetRootComponent(BurstRoot);
    SetActorEnableCollision(false);
}

void ACMLImpactBurstActor::Initialise(
    const FVector& Position,
    const FVector& SurfaceNormal,
    const ECMLImpactSurface Surface)
{
    EmissionNormal = SurfaceNormal.GetSafeNormal(UE_SMALL_NUMBER, FVector::UpVector);
    EmissionTangent = FVector::CrossProduct(
        FMath::Abs(EmissionNormal.Z) < 0.92f ? FVector::UpVector : FVector::ForwardVector,
        EmissionNormal).GetSafeNormal();
    EmissionBitangent = FVector::CrossProduct(EmissionNormal, EmissionTangent).GetSafeNormal();
    SetActorLocation(Position + EmissionNormal * FCMLImpactBurstGeometry::SurfaceOffsetUnrealUnits);
    Random.Initialize(GetTypeHash(Position) ^ static_cast<uint32>(Surface));

    const bool bStone = Surface == ECMLImpactSurface::Stone;
    const auto ConeDirection = [this](const float AngleDegrees)
    {
        const float Radius = FMath::Tan(FMath::DegreesToRadians(AngleDegrees))
            * FMath::Sqrt(Random.FRand());
        const float Angle = Random.FRandRange(0.0f, UE_TWO_PI);
        return (EmissionNormal
            + EmissionTangent * FMath::Cos(Angle) * Radius
            + EmissionBitangent * FMath::Sin(Angle) * Radius).GetSafeNormal();
    };
    for (int32 Index = 0; Index < FCMLImpactBurstGeometry::SmokeCountFor(Surface); ++Index)
    {
        SpawnParticle(
            FCMLImpactBurstGeometry::SmokePuff(),
            FCMLImpactBurstGeometry::SmokeTintFor(Surface),
            ConeDirection(bStone ? 41.0f : 34.0f),
            true, bStone, Index);
    }
    for (int32 Index = 0; Index < FCMLImpactBurstGeometry::FragmentCountFor(Surface); ++Index)
    {
        SpawnParticle(
            FCMLImpactBurstGeometry::FragmentMeshFor(Surface),
            FCMLImpactBurstGeometry::FragmentTintFor(Surface),
            ConeDirection(bStone ? 46.0f : 37.0f),
            false, bStone, Index);
    }
}

void ACMLImpactBurstActor::InitialiseAirshipSmoke(
    const FVector& Position,
    const FVector& ExhaustDirection)
{
    EmissionNormal = ExhaustDirection.GetSafeNormal(
        UE_SMALL_NUMBER, -FVector::ForwardVector);
    EmissionTangent = FVector::CrossProduct(
        FMath::Abs(EmissionNormal.Z) < 0.92f ? FVector::UpVector : FVector::ForwardVector,
        EmissionNormal).GetSafeNormal();
    EmissionBitangent = FVector::CrossProduct(EmissionNormal, EmissionTangent).GetSafeNormal();
    SetActorLocation(Position);
    Random.Initialize(GetTypeHash(Position) ^ 0xB1AC5A0u);
    for (int32 Index = 0; Index < 3; ++Index)
    {
        const FVector Direction = (EmissionNormal
            + EmissionTangent * Random.FRandRange(-0.22f, 0.22f)
            + EmissionBitangent * Random.FRandRange(-0.22f, 0.22f)).GetSafeNormal();
        SpawnParticle(
            FCMLImpactBurstGeometry::SmokePuff(),
            FLinearColor(0.018f, 0.014f, 0.012f, 0.88f),
            Direction, true, true, Index);
    }
}

void ACMLImpactBurstActor::SpawnParticle(
    const FCMLBurstMesh& Geometry,
    const FLinearColor& Tint,
    const FVector& Direction,
    const bool bSmoke,
    const bool bStone,
    const int32 Index)
{
    UProceduralMeshComponent* Mesh = NewObject<UProceduralMeshComponent>(
        this, *FString::Printf(TEXT("FX_%s_%02d"), bSmoke ? TEXT("Smoke") : TEXT("Fragment"), Index));
    AddInstanceComponent(Mesh);
    Mesh->SetupAttachment(BurstRoot);
    Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Mesh->SetCastShadow(false);
    Mesh->bUseAsyncCooking = true;
    Mesh->RegisterComponent();

    TArray<FVector> Normals;
    BuildNormals(Geometry, Normals);
    TArray<FVector2D> UVs;
    UVs.Init(FVector2D::ZeroVector, Geometry.Vertices.Num());
    TArray<FLinearColor> Colours;
    Colours.Init(Tint, Geometry.Vertices.Num());
    TArray<FProcMeshTangent> Tangents;
    Mesh->CreateMeshSection_LinearColor(
        0, Geometry.Vertices, Geometry.Triangles, Normals, UVs, Colours, Tangents, false);
    const TCHAR* MaterialPath = bSmoke
        ? TEXT("/Engine/EngineDebugMaterials/M_SimpleUnlitTranslucent.M_SimpleUnlitTranslucent")
        : TEXT("/Engine/EngineDebugMaterials/VertexColorMaterial.VertexColorMaterial");
    UMaterialInstanceDynamic* ParticleMaterial = nullptr;
    if (UMaterialInterface* BaseMaterial = LoadObject<UMaterialInterface>(nullptr, MaterialPath))
    {
        ParticleMaterial = UMaterialInstanceDynamic::Create(BaseMaterial, this);
        ParticleMaterial->SetVectorParameterValue(TEXT("Color"), Tint);
        ParticleMaterial->SetVectorParameterValue(TEXT("BaseColor"), Tint);
        ParticleMaterial->SetScalarParameterValue(TEXT("Opacity"), Tint.A);
        Mesh->SetMaterial(0, ParticleMaterial);
    }

    FParticle Particle;
    Particle.Mesh = Mesh;
    Particle.Material = ParticleMaterial;
    Particle.bSmoke = bSmoke;
    Particle.BaseAlpha = Tint.A;
    Particle.Age = 0.0f;
    if (bSmoke)
    {
        Particle.Lifetime = bStone
            ? Random.FRandRange(0.48f, 0.82f) : Random.FRandRange(0.32f, 0.60f);
        Particle.StartSize = bStone
            ? Random.FRandRange(0.13f, 0.25f) : Random.FRandRange(0.08f, 0.16f);
        Particle.Velocity = Direction * (bStone
            ? Random.FRandRange(18.0f, 46.0f) : Random.FRandRange(24.0f, 58.0f));
        Particle.GravityScale = bStone ? -0.015f : 0.035f;
        Particle.AngularVelocity = FRotator(
            Random.FRandRange(-40.0f, 40.0f),
            Random.FRandRange(-52.0f, 52.0f),
            Random.FRandRange(-34.0f, 34.0f));
        Mesh->SetRelativeLocation(
            EmissionTangent * Random.FRandRange(-2.8f, 2.8f)
            + EmissionBitangent * Random.FRandRange(-2.8f, 2.8f));
        Mesh->SetRelativeScale3D(FVector(Particle.StartSize * 0.42f));
    }
    else
    {
        Particle.Lifetime = bStone
            ? Random.FRandRange(0.42f, 0.76f) : Random.FRandRange(0.48f, 0.92f);
        Particle.StartSize = bStone
            ? Random.FRandRange(0.055f, 0.11f) : Random.FRandRange(0.075f, 0.15f);
        Particle.Velocity = Direction * (bStone
            ? Random.FRandRange(52.0f, 115.0f) : Random.FRandRange(68.0f, 142.0f));
        Particle.GravityScale = bStone ? 1.10f : 0.82f;
        Particle.AngularVelocity = FRotator(
            Random.FRandRange(-630.0f, 630.0f),
            Random.FRandRange(-800.0f, 800.0f),
            Random.FRandRange(-515.0f, 515.0f));
        Mesh->SetRelativeLocation(
            EmissionTangent * Random.FRandRange(-2.2f, 2.2f)
            + EmissionBitangent * Random.FRandRange(-2.2f, 2.2f));
        Mesh->SetRelativeScale3D(FVector(Particle.StartSize * 0.82f));
    }
    Mesh->SetRelativeRotation(FRotator(
        Random.FRandRange(-180.0f, 180.0f),
        Random.FRandRange(-180.0f, 180.0f),
        Random.FRandRange(-180.0f, 180.0f)));
    Particles.Add(Particle);
}

void ACMLImpactBurstActor::Tick(const float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);
    const float Dt = FMath::Max(0.0f, DeltaSeconds);
    Elapsed += Dt;
    for (FParticle& Particle : Particles)
    {
        UProceduralMeshComponent* Mesh = Particle.Mesh.Get();
        if (Mesh == nullptr || Particle.Age >= Particle.Lifetime)
        {
            if (Mesh != nullptr)
            {
                Mesh->SetVisibility(false);
            }
            continue;
        }
        Particle.Age += Dt;
        const float Progress = FMath::Clamp(Particle.Age / Particle.Lifetime, 0.0f, 1.0f);
        Particle.Velocity.Z += GetWorld()->GetGravityZ() * Particle.GravityScale * Dt;
        if (Particle.bSmoke)
        {
            Particle.Velocity *= FMath::Pow(0.52f, Dt);
        }
        Mesh->AddWorldOffset(Particle.Velocity * Dt);
        Mesh->AddLocalRotation(Particle.AngularVelocity * Dt);
        float ScaleCurve;
        if (Particle.bSmoke)
        {
            ScaleCurve = Progress < 0.22f
                ? FMath::Lerp(0.42f, 0.96f, Progress / 0.22f)
                : FMath::Lerp(0.96f, 1.78f, (Progress - 0.22f) / 0.78f);
            if (UMaterialInstanceDynamic* Material = Particle.Material.Get())
            {
                const float Fade = Progress < 0.18f
                    ? FMath::SmoothStep(0.0f, 1.0f, Progress / 0.18f)
                    : FMath::Square(1.0f - (Progress - 0.18f) / 0.82f);
                Material->SetScalarParameterValue(
                    TEXT("Opacity"), Particle.BaseAlpha * Fade);
            }
        }
        else if (Progress < 0.12f)
        {
            ScaleCurve = FMath::Lerp(0.82f, 1.0f, Progress / 0.12f);
        }
        else if (Progress < 0.82f)
        {
            ScaleCurve = 1.0f;
        }
        else
        {
            ScaleCurve = FMath::Lerp(1.0f, 0.06f, (Progress - 0.82f) / 0.18f);
        }
        Mesh->SetWorldScale3D(FVector(Particle.StartSize * ScaleCurve));
    }
    if (Elapsed >= FCMLImpactBurstGeometry::EffectLifetimeSeconds)
    {
        Destroy();
    }
}

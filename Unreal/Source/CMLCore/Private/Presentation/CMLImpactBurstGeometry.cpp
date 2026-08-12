#include "Presentation/CMLImpactBurstGeometry.h"

namespace
{
    /** Unity metres to Unreal units. */
    constexpr double MetresToUnreal = 100.0;

    /**
     * One Unity vertex in Unreal space.
     *
     * Unreal.X = Unity.z, Unreal.Y = Unity.x, Unreal.Z = Unity.y. The
     * permutation is cyclic, so handedness — and therefore the winding of every
     * triangle built from these indices — is preserved.
     */
    FVector FromUnity(const double X, const double Y, const double Z)
    {
        return FVector(Z * MetresToUnreal, X * MetresToUnreal, Y * MetresToUnreal);
    }

    /**
     * Appends one triangle with the middle and last index swapped.
     *
     * The Unity original authored its index lists one way round and swapped
     * them when building the mesh. Reproducing the swap here keeps the faces
     * pointing the way the artist saw them; dropping it would turn every mesh
     * inside out, which reads as an invisible effect rather than an obvious bug.
     */
    void AddTriangle(TArray<int32>& Triangles, const int32 A, const int32 B, const int32 C)
    {
        Triangles.Add(A);
        Triangles.Add(C);
        Triangles.Add(B);
    }

    FCMLBurstMesh BuildSmokePuff()
    {
        using Geometry = FCMLImpactBurstGeometry;
        constexpr int32 Latitudes = Geometry::SmokeLatitudeSegments;
        constexpr int32 Longitudes = Geometry::SmokeLongitudeSegments;

        FCMLBurstMesh Mesh;
        Mesh.Vertices.Add(FromUnity(0.0, 0.50, 0.0));

        for (int32 Latitude = 1; Latitude < Latitudes; ++Latitude)
        {
            const double Phi = UE_DOUBLE_PI * Latitude / Latitudes;
            const double RingRadius = FMath::Sin(Phi);
            const double Y = FMath::Cos(Phi);
            for (int32 Longitude = 0; Longitude < Longitudes; ++Longitude)
            {
                const double Theta = UE_DOUBLE_PI * 2.0 * Longitude / Longitudes;
                // Two out-of-phase waves knock the sphere out of true. Without
                // them the puff is a ball, and a ball reads as a bubble rather
                // than as smoke.
                const double Irregularity = 1.0
                    + FMath::Sin(Theta * 3.0 + Phi * 2.0) * 0.11
                    + FMath::Cos(Theta * 5.0 - Phi * 3.0) * 0.065;
                const double Radius = 0.50 * Irregularity;
                Mesh.Vertices.Add(FromUnity(
                    FMath::Cos(Theta) * RingRadius * Radius,
                    Y * Radius,
                    FMath::Sin(Theta) * RingRadius * Radius));
            }
        }

        const int32 BottomIndex = Mesh.Vertices.Num();
        Mesh.Vertices.Add(FromUnity(0.0, -0.50, 0.0));

        for (int32 Longitude = 0; Longitude < Longitudes; ++Longitude)
        {
            const int32 Next = (Longitude + 1) % Longitudes;
            AddTriangle(Mesh.Triangles, 0, 1 + Longitude, 1 + Next);
        }

        for (int32 Latitude = 0; Latitude < Latitudes - 2; ++Latitude)
        {
            const int32 CurrentRing = 1 + Latitude * Longitudes;
            const int32 NextRing = CurrentRing + Longitudes;
            for (int32 Longitude = 0; Longitude < Longitudes; ++Longitude)
            {
                const int32 Next = (Longitude + 1) % Longitudes;
                AddTriangle(Mesh.Triangles,
                    CurrentRing + Longitude, NextRing + Longitude, NextRing + Next);
                AddTriangle(Mesh.Triangles,
                    CurrentRing + Longitude, NextRing + Next, CurrentRing + Next);
            }
        }

        const int32 FinalRing = BottomIndex - Longitudes;
        for (int32 Longitude = 0; Longitude < Longitudes; ++Longitude)
        {
            const int32 Next = (Longitude + 1) % Longitudes;
            AddTriangle(Mesh.Triangles, FinalRing + Next, FinalRing + Longitude, BottomIndex);
        }
        return Mesh;
    }

    FCMLBurstMesh BuildFromArrays(
        const TArray<FVector>& Vertices, std::initializer_list<int32> Indices)
    {
        FCMLBurstMesh Mesh;
        Mesh.Vertices = Vertices;
        const TArray<int32> Source(Indices);
        for (int32 Index = 0; Index + 2 < Source.Num(); Index += 3)
        {
            AddTriangle(Mesh.Triangles, Source[Index], Source[Index + 1], Source[Index + 2]);
        }
        return Mesh;
    }

    /** A chip, not a shard: four faces, none of them flat on to the camera. */
    FCMLBurstMesh BuildStoneFragment()
    {
        return BuildFromArrays(
            {
                FromUnity(-0.42, -0.30, -0.28),
                FromUnity(0.48, -0.22, -0.18),
                FromUnity(0.05, -0.12, 0.46),
                FromUnity(-0.06, 0.54, -0.04),
            },
            {0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3});
    }

    /** Long and thin, so it tumbles rather than spins on the spot. */
    FCMLBurstMesh BuildWoodSplinter()
    {
        return BuildFromArrays(
            {
                FromUnity(-0.075, -0.48, -0.025),
                FromUnity(0.075, -0.48, -0.025),
                FromUnity(0.060, -0.48, 0.025),
                FromUnity(-0.060, -0.48, 0.025),
                FromUnity(0.018, 0.52, 0.0),
            },
            {0, 2, 1, 0, 3, 2, 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4});
    }
}

const FLinearColor& FCMLImpactBurstGeometry::StoneSmokeTint()
{
    static const FLinearColor Tint(0.86f, 0.82f, 0.74f, 0.78f);
    return Tint;
}

const FLinearColor& FCMLImpactBurstGeometry::WoodDustTint()
{
    static const FLinearColor Tint(0.90f, 0.59f, 0.33f, 0.58f);
    return Tint;
}

const FLinearColor& FCMLImpactBurstGeometry::StoneFragmentTint()
{
    static const FLinearColor Tint(0.68f, 0.64f, 0.56f, 1.0f);
    return Tint;
}

const FLinearColor& FCMLImpactBurstGeometry::WoodSplinterTint()
{
    static const FLinearColor Tint(0.78f, 0.43f, 0.20f, 1.0f);
    return Tint;
}

const FCMLBurstMesh& FCMLImpactBurstGeometry::SmokePuff()
{
    static const FCMLBurstMesh Mesh = BuildSmokePuff();
    return Mesh;
}

const FCMLBurstMesh& FCMLImpactBurstGeometry::StoneFragment()
{
    static const FCMLBurstMesh Mesh = BuildStoneFragment();
    return Mesh;
}

const FCMLBurstMesh& FCMLImpactBurstGeometry::WoodSplinter()
{
    static const FCMLBurstMesh Mesh = BuildWoodSplinter();
    return Mesh;
}

int32 FCMLImpactBurstGeometry::SmokeCountFor(const ECMLImpactSurface Surface)
{
    return Surface == ECMLImpactSurface::Stone ? StoneSmokeCount : WoodSmokeCount;
}

int32 FCMLImpactBurstGeometry::FragmentCountFor(const ECMLImpactSurface Surface)
{
    return Surface == ECMLImpactSurface::Stone ? StoneFragmentCount : WoodSplinterCount;
}

const FCMLBurstMesh& FCMLImpactBurstGeometry::FragmentMeshFor(const ECMLImpactSurface Surface)
{
    return Surface == ECMLImpactSurface::Stone ? StoneFragment() : WoodSplinter();
}

const FLinearColor& FCMLImpactBurstGeometry::SmokeTintFor(const ECMLImpactSurface Surface)
{
    return Surface == ECMLImpactSurface::Stone ? StoneSmokeTint() : WoodDustTint();
}

const FLinearColor& FCMLImpactBurstGeometry::FragmentTintFor(const ECMLImpactSurface Surface)
{
    return Surface == ECMLImpactSurface::Stone ? StoneFragmentTint() : WoodSplinterTint();
}

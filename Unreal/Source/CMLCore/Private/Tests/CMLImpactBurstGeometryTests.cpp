#include "Presentation/CMLImpactBurstGeometry.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    /**
     * A closed surface has every edge shared by exactly two triangles, once in
     * each direction. That single check catches holes, duplicated faces and a
     * triangle wound the wrong way round all at once — and a hole in a smoke
     * puff is visible from inside as a missing face.
     */
    bool IsClosedAndConsistentlyWound(const FCMLBurstMesh& Mesh, FString& OutReason)
    {
        TMap<TPair<int32, int32>, int32> Edges;
        for (int32 Index = 0; Index + 2 < Mesh.Triangles.Num(); Index += 3)
        {
            const int32 Corner[3] = {
                Mesh.Triangles[Index], Mesh.Triangles[Index + 1], Mesh.Triangles[Index + 2]};
            for (int32 Side = 0; Side < 3; ++Side)
            {
                const int32 From = Corner[Side];
                const int32 To = Corner[(Side + 1) % 3];
                if (!Mesh.Vertices.IsValidIndex(From) || !Mesh.Vertices.IsValidIndex(To))
                {
                    OutReason = TEXT("a triangle names a vertex that does not exist");
                    return false;
                }
                Edges.FindOrAdd(TPair<int32, int32>(From, To)) += 1;
            }
        }
        for (const TPair<TPair<int32, int32>, int32>& Edge : Edges)
        {
            if (Edge.Value != 1)
            {
                OutReason = FString::Printf(
                    TEXT("edge %d->%d is used %d times, expected once"),
                    Edge.Key.Key, Edge.Key.Value, Edge.Value);
                return false;
            }
            if (Edges.FindRef(TPair<int32, int32>(Edge.Key.Value, Edge.Key.Key)) != 1)
            {
                OutReason = FString::Printf(
                    TEXT("edge %d->%d has no opposite, so the surface is open or misfacing"),
                    Edge.Key.Key, Edge.Key.Value);
                return false;
            }
        }
        return true;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLImpactBurstGeometryTest,
    "CML.Core.Presentation.ImpactBurstGeometry",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLImpactBurstGeometryTest::RunTest(const FString& Parameters)
{
    using Geometry = FCMLImpactBurstGeometry;
    constexpr int32 Longitudes = Geometry::SmokeLongitudeSegments;
    constexpr int32 Latitudes = Geometry::SmokeLatitudeSegments;

    // Every one of these is a solid. The whole point of the effect is that none
    // of them is a quad: four vertices and two triangles anywhere here would
    // mean a billboard had crept in.
    {
        const FCMLBurstMesh* Meshes[] = {
            &Geometry::SmokePuff(), &Geometry::StoneFragment(), &Geometry::WoodSplinter()};
        for (const FCMLBurstMesh* Mesh : Meshes)
        {
            FString Reason;
            TestTrue(*FString::Printf(TEXT("The mesh is a closed solid (%s)"), *Reason),
                IsClosedAndConsistentlyWound(*Mesh, Reason));

            // A billboard is flat. Depth on all three axes is what says this is
            // geometry and not a camera-facing card. (The stone chip is a
            // tetrahedron — only four vertices, but a solid one.)
            FBox Box(ForceInit);
            for (const FVector& Vertex : Mesh->Vertices)
            {
                Box += Vertex;
            }
            TestTrue(TEXT("It has depth on every axis"), Box.GetSize().GetMin() > 1.0);
            TestTrue(TEXT("It is more than a pair of triangles"), Mesh->TriangleCount() > 2);
        }
    }

    // The puff's resolution is the shape: one pole, one ring per latitude step,
    // one pole again.
    {
        const FCMLBurstMesh& Puff = Geometry::SmokePuff();
        TestEqual(TEXT("Two poles and six rings"),
            Puff.Vertices.Num(), 2 + (Latitudes - 1) * Longitudes);
        TestEqual(TEXT("Two fans and five bands of quads"),
            Puff.TriangleCount(), Longitudes * 2 + (Latitudes - 2) * Longitudes * 2);
    }

    // A ball reads as a bubble, not as smoke. The irregularity is what makes it
    // smoke, so its absence has to fail rather than pass quietly.
    {
        const FCMLBurstMesh& Puff = Geometry::SmokePuff();
        double Shortest = TNumericLimits<double>::Max();
        double Longest = 0.0;
        for (const FVector& Vertex : Puff.Vertices)
        {
            const double Radius = Vertex.Size();
            Shortest = FMath::Min(Shortest, Radius);
            Longest = FMath::Max(Longest, Radius);
        }
        TestTrue(TEXT("The puff is not a sphere"), Longest - Shortest > 1.0);
        // ...but it is still recognisably round rather than spiky.
        TestTrue(TEXT("The puff is still round"), Longest < Shortest * 1.4);
    }

    // Metres became Unreal units exactly once. A puff half a metre across has to
    // come out 50 units across, not 0.5 and not 5000.
    {
        const FCMLBurstMesh& Puff = Geometry::SmokePuff();
        double Longest = 0.0;
        for (const FVector& Vertex : Puff.Vertices)
        {
            Longest = FMath::Max(Longest, Vertex.Size());
        }
        TestTrue(TEXT("The puff is tens of units across, not fractions or thousands"),
            Longest > 40.0 && Longest < 70.0);

        // The poles sit on Unreal's Z, which is Unity's Y: an axis permutation
        // applied wrongly would put them on X or Y and lay the puff on its side.
        TestEqual(TEXT("The top pole is straight up"), Puff.Vertices[0].X, 0.0, 1e-6);
        TestEqual(TEXT("The top pole is straight up"), Puff.Vertices[0].Y, 0.0, 1e-6);
        TestEqual(TEXT("The top pole is 50 units up"), Puff.Vertices[0].Z, 50.0, 1e-6);
        TestEqual(TEXT("The bottom pole is 50 units down"),
            Puff.Vertices.Last().Z, -50.0, 1e-6);
    }

    // A splinter is long and thin; a chip is not. If the two ever came out the
    // same shape, wood and stone would throw the same debris.
    {
        auto Extent = [](const FCMLBurstMesh& Mesh)
        {
            FBox Box(ForceInit);
            for (const FVector& Vertex : Mesh.Vertices)
            {
                Box += Vertex;
            }
            return Box.GetSize();
        };
        const FVector Splinter = Extent(Geometry::WoodSplinter());
        const FVector Chip = Extent(Geometry::StoneFragment());
        TestTrue(TEXT("A splinter is much longer than it is wide"),
            Splinter.Z > FMath::Max(Splinter.X, Splinter.Y) * 4.0);
        TestTrue(TEXT("A chip is roughly as wide as it is tall"),
            Chip.Z < FMath::Max(Chip.X, Chip.Y) * 2.0);
    }

    // Stone throws more, and heavier, than wood.
    {
        TestTrue(TEXT("Stone throws more smoke than wood"),
            Geometry::SmokeCountFor(ECMLImpactSurface::Stone)
                > Geometry::SmokeCountFor(ECMLImpactSurface::Wood));
        TestTrue(TEXT("Stone smoke is more opaque than wood dust"),
            Geometry::SmokeTintFor(ECMLImpactSurface::Stone).A
                > Geometry::SmokeTintFor(ECMLImpactSurface::Wood).A);
        TestTrue(TEXT("Stone throws chips"),
            &Geometry::FragmentMeshFor(ECMLImpactSurface::Stone) == &Geometry::StoneFragment());
        TestTrue(TEXT("Wood throws splinters"),
            &Geometry::FragmentMeshFor(ECMLImpactSurface::Wood) == &Geometry::WoodSplinter());
    }
    return true;
}
#endif

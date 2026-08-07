using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds the single continuous mesh below the Starter Island Terrain.
    ///
    /// The caller owns the organic rim because it must use the same contour
    /// and height sampling as the Terrain. This builder owns the faceted rock
    /// apron and angular tapered continuation below that rim. The generated
    /// shell is open only at the Terrain-covered upper rim, sealed at its lower
    /// tip, contains no modular rock pieces and deliberately has no Collider.
    /// </summary>
    public static class StarterIslandUnderbodyBuilder
    {
        public const string DataRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Data";
        public const string MeshAssetPath =
            DataRoot + "/MESH_StarterIsland_Underbody.asset";
        public const string ObjectName = "TerrainUnderbody";
        public const float DesignWidth = 660f;
        public const float DesignLength = 500f;
        public const float DefaultBottomY = -115f;
        public const int RecommendedRimSampleCount = 192;

        private const float RimMergeEpsilon = 0.025f;
        private const float BoundsTolerance = 12f;
        private const float MinimumFootprintFraction = 0.65f;

        // The first ring is the exact caller-supplied rim. The second ring
        // extends only ~2 m beyond it and sits slightly lower: that narrow
        // overlap hides raster cracks between the Terrain and the vertical
        // shell without creating a visible top cap. The following rings form
        // a 20-28 m near-vertical rock apron. Below it the silhouette contracts
        // decisively toward the keel. Angular sectors retain only modest mass
        // as buttresses while narrow sectors become creases; none of the huge
        // soft lobes used by the old shape are retained.
        private static readonly RingProfile[] RingProfiles =
        {
            new RingProfile(1.000f, 0.000f, 0.000f),
            new RingProfile(1.006f, 0.004f, 0.000f),
            new RingProfile(1.003f, 0.080f, 0.004f),
            new RingProfile(0.988f, 0.160f, 0.010f),
            new RingProfile(0.925f, 0.285f, 0.040f),
            new RingProfile(0.810f, 0.415f, 0.070f),
            new RingProfile(0.675f, 0.545f, 0.095f),
            new RingProfile(0.525f, 0.665f, 0.110f),
            new RingProfile(0.385f, 0.765f, 0.105f),
            new RingProfile(0.270f, 0.845f, 0.090f),
            new RingProfile(0.175f, 0.905f, 0.068f),
            new RingProfile(0.105f, 0.950f, 0.045f),
            new RingProfile(0.055f, 0.978f, 0.024f),
            new RingProfile(0.025f, 0.992f, 0.010f)
        };

        /// <summary>
        /// Samples a caller-owned organic rim at evenly spaced polar angles.
        /// The final sample must not repeat the first point.
        /// </summary>
        public static IReadOnlyList<Vector3> SampleRim(
            int sampleCount,
            Func<float, Vector3> sampleAtAngleRadians)
        {
            if (sampleAtAngleRadians == null)
            {
                throw new ArgumentNullException(
                    nameof(sampleAtAngleRadians));
            }

            if (sampleCount < 24)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleCount),
                    sampleCount,
                    "The underbody rim needs at least 24 samples.");
            }

            var result = new Vector3[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                var angle =
                    index * Mathf.PI * 2f / sampleCount;
                result[index] = sampleAtAngleRadians(angle);
            }

            ValidateRim(result);
            return result;
        }

        /// <summary>
        /// Creates or updates the underbody child and its deterministic Mesh
        /// asset. The supplied Material is assigned as a shared material.
        /// </summary>
        public static GameObject BuildOrUpdate(
            Transform parent,
            IReadOnlyList<Vector3> organicRim,
            Material warmCliffMaterial,
            float bottomY = DefaultBottomY)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (warmCliffMaterial == null)
            {
                throw new ArgumentNullException(
                    nameof(warmCliffMaterial));
            }

            var mesh = BuildOrUpdateMesh(organicRim, bottomY);
            var underbody = FindOrCreateDirectChild(
                parent,
                ObjectName);
            AssertIdentity(underbody.transform);

            var filter = underbody.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = underbody.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = mesh;

            var renderer = underbody.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = underbody.AddComponent<MeshRenderer>();
            }

            renderer.sharedMaterial = warmCliffMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.BlendProbes;

            RemoveColliders(underbody);
            GameObjectUtility.SetStaticEditorFlags(
                underbody,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.ReflectionProbeStatic);

            var report = Validate(
                underbody,
                organicRim.Count,
                warmCliffMaterial,
                bottomY);
            Debug.Log(
                "STARTER_ISLAND_UNDERBODY_BUILD " +
                $"asset={MeshAssetPath} " +
                $"vertices={report.VertexCount} " +
                $"indices={report.IndexCount} " +
                $"triangles={report.TriangleCount} " +
                $"edges={report.EdgeCount} " +
                $"boundaryEdges={report.BoundaryEdgeCount} " +
                $"bounds={report.Bounds} " +
                "openRim=1 bottomClosed=1 continuous=1 " +
                "collider=0 status=PASS");
            return underbody;
        }

        /// <summary>
        /// Creates or updates only the deterministic Mesh asset.
        /// </summary>
        public static Mesh BuildOrUpdateMesh(
            IReadOnlyList<Vector3> organicRim,
            float bottomY = DefaultBottomY)
        {
            ValidateRim(organicRim);
            ValidateBottom(organicRim, bottomY);
            EnsureFolder(DataRoot);

            var geometry = BuildGeometry(organicRim, bottomY);
            var mesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (mesh == null)
            {
                var occupied =
                    AssetDatabase.LoadMainAssetAtPath(MeshAssetPath);
                if (occupied != null)
                {
                    throw new InvalidOperationException(
                        $"Asset path is occupied by {occupied.GetType().Name}: " +
                        MeshAssetPath);
                }

                mesh = new Mesh
                {
                    name = "MESH_StarterIsland_Underbody"
                };
                AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            }
            else
            {
                mesh.Clear(false);
                mesh.name = "MESH_StarterIsland_Underbody";
            }

            mesh.indexFormat =
                geometry.Vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
            mesh.SetVertices(geometry.Vertices);
            mesh.SetUVs(0, geometry.Uv);
            mesh.SetColors(geometry.Colors);
            mesh.SetTriangles(geometry.Indices, 0, true);
            mesh.RecalculateNormals();
            ApplyFacetedNormals(mesh, 40);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            ValidateMesh(
                mesh,
                organicRim.Count,
                bottomY);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// Validates generated topology, deterministic counts, bounds,
        /// material sharing and the explicit absence of Colliders.
        /// </summary>
        public static ValidationReport Validate(
            GameObject underbody,
            int rimSampleCount,
            Material expectedWarmCliffMaterial,
            float bottomY = DefaultBottomY)
        {
            if (underbody == null)
            {
                throw new ArgumentNullException(nameof(underbody));
            }

            if (expectedWarmCliffMaterial == null)
            {
                throw new ArgumentNullException(
                    nameof(expectedWarmCliffMaterial));
            }

            if (!string.Equals(
                    underbody.name,
                    ObjectName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Underbody object must be named '{ObjectName}'.");
            }

            AssertIdentity(underbody.transform);
            var filter = underbody.GetComponent<MeshFilter>();
            var renderer = underbody.GetComponent<MeshRenderer>();
            if (filter == null || filter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    "Underbody requires one MeshFilter with the generated Mesh.");
            }

            if (renderer == null ||
                renderer.sharedMaterial != expectedWarmCliffMaterial)
            {
                throw new InvalidOperationException(
                    "Underbody must share the supplied warm cliff Material.");
            }

            if (underbody.GetComponentsInChildren<Collider>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The visual underbody must not contain a Collider. " +
                    "TerrainCollider is the only landscape collision authority.");
            }

            return ValidateMesh(
                filter.sharedMesh,
                rimSampleCount,
                bottomY);
        }

        private static MeshGeometry BuildGeometry(
            IReadOnlyList<Vector3> rim,
            float bottomY)
        {
            var rimCount = rim.Count;
            var ringCount = RingProfiles.Length;
            var expectedVertexCount = rimCount * ringCount + 1;
            var expectedTriangleCount =
                rimCount * (ringCount * 2 - 1);
            var vertices =
                new List<Vector3>(expectedVertexCount);
            var uv = new List<Vector2>(expectedVertexCount);
            var colors = new List<Color32>(expectedVertexCount);
            var indices =
                new List<int>(expectedTriangleCount * 3);

            var signedArea = SignedPlanarArea(rim);
            var counterClockwise = signedArea > 0f;
            var center = PlanarCentroid(rim, signedArea);
            var rimBounds = CalculateBounds(rim);
            var driftTarget = new Vector2(
                rimBounds.size.x * 0.018f,
                -rimBounds.size.z * 0.014f);

            for (var ringIndex = 0;
                 ringIndex < ringCount;
                 ringIndex++)
            {
                var profile = RingProfiles[ringIndex];
                var driftProgress =
                    SmootherStep(profile.Depth);
                var drift = driftTarget * driftProgress;
                for (var rimIndex = 0;
                     rimIndex < rimCount;
                     rimIndex++)
                {
                    var source = rim[rimIndex];
                    var angle = Mathf.Atan2(
                        source.z - center.y,
                        source.x - center.x);
                    const int facetCount = 40;
                    var facetStep =
                        Mathf.PI * 2f / facetCount;
                    var normalizedAngle =
                        Mathf.Repeat(
                            angle + Mathf.PI,
                            Mathf.PI * 2f) /
                        (Mathf.PI * 2f);
                    var facetIndex =
                        Mathf.FloorToInt(normalizedAngle * facetCount);
                    var facetedAngle =
                        -Mathf.PI +
                        (facetIndex + 0.5f) * facetStep;
                    var facetedDirection =
                        new Vector2(
                            Mathf.Cos(facetedAngle),
                            Mathf.Sin(facetedAngle));
                    var sectorNoise =
                        Mathf.Sin(
                            (facetIndex + 1) * 2.173f + 0.35f) * 0.58f +
                        Mathf.Sin(
                            (facetIndex + 1) * 0.917f - 0.80f) * 0.27f +
                        Mathf.Sin(
                            (facetIndex + 1) * 4.117f + 1.20f) * 0.15f;
                    var scale =
                        profile.Scale *
                        (1f + sectorNoise * profile.RibStrength);
                    var buttressStrength =
                        EvaluateButtressStrength(facetedAngle);
                    var creaseStrength =
                        EvaluateCreaseStrength(facetedAngle);
                    var massRise =
                        SmootherStep(
                            Mathf.InverseLerp(
                                0.18f,
                                0.42f,
                                profile.Depth));
                    var massFade =
                        1f -
                        SmootherStep(
                            Mathf.InverseLerp(
                                0.82f,
                                0.985f,
                                profile.Depth));
                    var massEnvelope =
                        massRise * massFade;
                    scale *=
                        1f +
                        buttressStrength * massEnvelope * 0.115f -
                        creaseStrength * massEnvelope * 0.105f;
                    scale = Mathf.Clamp(scale, 0.018f, 1.008f);
                    var local = new Vector2(
                        source.x - center.x,
                        source.z - center.y);
                    var radialLength =
                        Mathf.Max(0.001f, local.magnitude);
                    var angularBlend =
                        SmootherStep(
                            Mathf.InverseLerp(
                                0.12f,
                                0.58f,
                                profile.Depth)) *
                        0.72f;
                    angularBlend *=
                        Mathf.Lerp(
                            1f,
                            0.55f,
                            SmootherStep(
                                Mathf.InverseLerp(
                                    0.84f,
                                    0.992f,
                                    profile.Depth)));
                    var angularLocal =
                        Vector2.Lerp(
                            local,
                            facetedDirection * radialLength,
                            angularBlend);
                    var xz =
                        new Vector2(center.x, center.y) +
                        angularLocal * scale +
                        drift;
                    var verticalStriation =
                        sectorNoise * 0.75f +
                        Mathf.Sin(
                            (ringIndex + 1) * 1.73f +
                            facetIndex * 0.61f) * 0.25f;
                    var striationEnvelope =
                        massEnvelope *
                        Mathf.Lerp(
                            1.5f,
                            5.2f,
                            SmootherStep(
                                Mathf.InverseLerp(
                                    0.24f,
                                    0.76f,
                                    profile.Depth)));
                    var buttressDrop =
                        buttressStrength *
                        massEnvelope *
                        Mathf.Lerp(
                            2.5f,
                            10f,
                            SmootherStep(
                                Mathf.InverseLerp(
                                    0.24f,
                                    0.76f,
                                    profile.Depth)));
                    var creaseLift =
                        creaseStrength *
                        massEnvelope *
                        Mathf.Lerp(
                            1f,
                            4f,
                            SmootherStep(
                                Mathf.InverseLerp(
                                    0.25f,
                                    0.74f,
                                    profile.Depth)));
                    var y =
                        Mathf.Lerp(
                            source.y,
                            bottomY,
                            profile.Depth) +
                        verticalStriation * striationEnvelope -
                        buttressDrop +
                        creaseLift;

                    vertices.Add(new Vector3(xz.x, y, xz.y));
                    uv.Add(
                        new Vector2(
                            source.x / DesignWidth + 0.5f,
                            source.z / DesignLength + 0.5f));
                    var depthByte =
                        (byte)Mathf.RoundToInt(
                            Mathf.Lerp(
                                255f,
                                112f,
                                profile.Depth));
                    var alphaByte =
                        (byte)Mathf.RoundToInt(
                            Mathf.Lerp(
                                8f,
                                96f,
                                SmootherStep(profile.Depth)));
                    colors.Add(
                        new Color32(
                            depthByte,
                            depthByte,
                            depthByte,
                            alphaByte));
                }
            }

            var bottomTipIndex = vertices.Count;
            vertices.Add(
                new Vector3(
                    center.x + driftTarget.x,
                    bottomY,
                    center.y + driftTarget.y));
            uv.Add(
                new Vector2(
                    (center.x + driftTarget.x) / DesignWidth + 0.5f,
                    (center.y + driftTarget.y) / DesignLength + 0.5f));
            colors.Add(new Color32(96, 96, 96, 112));

            for (var ringIndex = 0;
                 ringIndex < ringCount - 1;
                 ringIndex++)
            {
                var upperStart = ringIndex * rimCount;
                var lowerStart = (ringIndex + 1) * rimCount;
                for (var rimIndex = 0;
                     rimIndex < rimCount;
                     rimIndex++)
                {
                    var next = (rimIndex + 1) % rimCount;
                    var upper = upperStart + rimIndex;
                    var upperNext = upperStart + next;
                    var lower = lowerStart + rimIndex;
                    var lowerNext = lowerStart + next;
                    if (counterClockwise)
                    {
                        AddTriangle(
                            indices,
                            upper,
                            upperNext,
                            lower);
                        AddTriangle(
                            indices,
                            upperNext,
                            lowerNext,
                            lower);
                    }
                    else
                    {
                        AddTriangle(
                            indices,
                            upper,
                            lower,
                            upperNext);
                        AddTriangle(
                            indices,
                            upperNext,
                            lower,
                            lowerNext);
                    }
                }
            }

            var finalRingStart = (ringCount - 1) * rimCount;
            for (var rimIndex = 0;
                 rimIndex < rimCount;
                 rimIndex++)
            {
                var next = (rimIndex + 1) % rimCount;
                if (counterClockwise)
                {
                    AddTriangle(
                        indices,
                        finalRingStart + rimIndex,
                        finalRingStart + next,
                        bottomTipIndex);
                }
                else
                {
                    AddTriangle(
                        indices,
                        finalRingStart + rimIndex,
                        bottomTipIndex,
                        finalRingStart + next);
                }
            }

            if (vertices.Count != expectedVertexCount ||
                indices.Count != expectedTriangleCount * 3)
            {
                throw new InvalidOperationException(
                    "Deterministic underbody vertex/index count changed " +
                    "during geometry construction.");
            }

            return ExpandForFlatShading(
                vertices,
                uv,
                colors,
                indices);
        }

        private static MeshGeometry ExpandForFlatShading(
            IReadOnlyList<Vector3> logicalVertices,
            IReadOnlyList<Vector2> logicalUv,
            IReadOnlyList<Color32> logicalColors,
            IReadOnlyList<int> logicalIndices)
        {
            var vertices =
                new List<Vector3>(logicalIndices.Count);
            var uv =
                new List<Vector2>(logicalIndices.Count);
            var colors =
                new List<Color32>(logicalIndices.Count);
            var indices =
                new List<int>(logicalIndices.Count);

            for (var index = 0;
                 index < logicalIndices.Count;
                 index++)
            {
                var logicalIndex = logicalIndices[index];
                ValidateIndex(logicalIndex, logicalVertices.Count);
                vertices.Add(logicalVertices[logicalIndex]);
                uv.Add(logicalUv[logicalIndex]);
                colors.Add(logicalColors[logicalIndex]);
                indices.Add(index);
            }

            return new MeshGeometry(
                vertices,
                uv,
                colors,
                indices);
        }

        private static ValidationReport ValidateMesh(
            Mesh mesh,
            int rimSampleCount,
            float bottomY)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (rimSampleCount < 24)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rimSampleCount));
            }

            var expectedLogicalVertexCount =
                rimSampleCount * RingProfiles.Length + 1;
            var expectedTriangleCount =
                rimSampleCount * (RingProfiles.Length * 2 - 1);
            var expectedIndexCount = expectedTriangleCount * 3;
            var expectedVertexCount = expectedIndexCount;
            var expectedEdgeCount =
                rimSampleCount * (RingProfiles.Length * 3 - 1);
            var vertices = mesh.vertices;
            var indices = mesh.triangles;

            if (mesh.subMeshCount != 1 ||
                vertices.Length != expectedVertexCount ||
                indices.Length != expectedIndexCount)
            {
                throw new InvalidOperationException(
                    "Underbody deterministic counts are invalid. " +
                    $"Expected {expectedVertexCount} vertices, " +
                    $"{expectedIndexCount} indices and one submesh; got " +
                    $"{vertices.Length}, {indices.Length}, " +
                    $"{mesh.subMeshCount}.");
            }

            var weldMap =
                new Dictionary<PositionKey, int>(
                    expectedLogicalVertexCount);
            var weldedIds = new int[vertices.Length];
            var weldedPositions =
                new List<Vector3>(expectedLogicalVertexCount);
            for (var index = 0;
                 index < vertices.Length;
                 index++)
            {
                var key = new PositionKey(vertices[index]);
                if (!weldMap.TryGetValue(key, out var weldedId))
                {
                    weldedId = weldedPositions.Count;
                    weldMap.Add(key, weldedId);
                    weldedPositions.Add(vertices[index]);
                }

                weldedIds[index] = weldedId;
            }

            if (weldedPositions.Count != expectedLogicalVertexCount)
            {
                throw new InvalidOperationException(
                    "Underbody flat-shaded vertices do not weld back to " +
                    "the deterministic logical shell. Expected " +
                    $"{expectedLogicalVertexCount} positions; got " +
                    $"{weldedPositions.Count}.");
            }

            var connectivity =
                new DisjointSet(weldedPositions.Count);
            var edges =
                new Dictionary<ulong, EdgeUse>(
                    expectedIndexCount / 2);
            for (var index = 0;
                 index < indices.Length;
                 index += 3)
            {
                var a = indices[index];
                var b = indices[index + 1];
                var c = indices[index + 2];
                ValidateIndex(a, vertices.Length);
                ValidateIndex(b, vertices.Length);
                ValidateIndex(c, vertices.Length);
                if (a != index ||
                    b != index + 1 ||
                    c != index + 2)
                {
                    throw new InvalidOperationException(
                        "Every underbody triangle must own three vertices " +
                        "so its rock face remains genuinely flat shaded.");
                }

                var weldedA = weldedIds[a];
                var weldedB = weldedIds[b];
                var weldedC = weldedIds[c];
                if (weldedA == weldedB ||
                    weldedB == weldedC ||
                    weldedC == weldedA)
                {
                    throw new InvalidOperationException(
                        $"Underbody triangle {index / 3} repeats a vertex.");
                }

                var cross = Vector3.Cross(
                    vertices[b] - vertices[a],
                    vertices[c] - vertices[a]);
                if (cross.sqrMagnitude <= 0.00000001f)
                {
                    throw new InvalidOperationException(
                        $"Underbody triangle {index / 3} is degenerate.");
                }

                connectivity.Union(weldedA, weldedB);
                connectivity.Union(weldedB, weldedC);
                RegisterEdge(edges, weldedA, weldedB);
                RegisterEdge(edges, weldedB, weldedC);
                RegisterEdge(edges, weldedC, weldedA);
            }

            var root = connectivity.Find(0);
            for (var index = 0;
                 index < weldedPositions.Count;
                 index++)
            {
                if (connectivity.Find(index) != root)
                {
                    throw new InvalidOperationException(
                        "Underbody must be one continuous connected mesh.");
                }
            }

            var boundaryEdgeCount = 0;
            var boundaryAdjacency =
                new Dictionary<int, List<int>>();
            foreach (var pair in edges)
            {
                if (pair.Value.Count == 1)
                {
                    if (Mathf.Abs(pair.Value.DirectionBalance) != 1)
                    {
                        throw new InvalidOperationException(
                            "Underbody upper-rim boundary is invalid. " +
                            $"Edge 0x{pair.Key:X16} has " +
                            $"uses={pair.Value.Count}, " +
                            "directionBalance=" +
                            $"{pair.Value.DirectionBalance}.");
                    }

                    var from = (int)(pair.Key >> 32);
                    var to = (int)(pair.Key & uint.MaxValue);
                    AddBoundaryNeighbour(
                        boundaryAdjacency,
                        from,
                        to);
                    AddBoundaryNeighbour(
                        boundaryAdjacency,
                        to,
                        from);
                    boundaryEdgeCount++;
                    continue;
                }

                if (pair.Value.Count != 2 ||
                    pair.Value.DirectionBalance != 0)
                {
                    throw new InvalidOperationException(
                        "Underbody topology is not a consistently wound, " +
                        "two-manifold surface with one intentional open rim. " +
                        $"Edge 0x{pair.Key:X16} has " +
                        $"uses={pair.Value.Count}, " +
                        $"directionBalance={pair.Value.DirectionBalance}.");
                }
            }

            if (edges.Count != expectedEdgeCount ||
                boundaryEdgeCount != rimSampleCount)
            {
                throw new InvalidOperationException(
                    "Underbody open-rim topology has invalid deterministic " +
                    $"counts. Expected {expectedEdgeCount} unique edges and " +
                    $"{rimSampleCount} upper boundary edges; got " +
                    $"{edges.Count} and {boundaryEdgeCount}.");
            }

            ValidateSingleBoundaryLoop(
                boundaryAdjacency,
                rimSampleCount);

            var bounds = mesh.bounds;
            if (!IsFinite(bounds.min) || !IsFinite(bounds.max) ||
                bounds.size.x > DesignWidth + BoundsTolerance ||
                bounds.size.z > DesignLength + BoundsTolerance ||
                bounds.size.x < DesignWidth * MinimumFootprintFraction ||
                bounds.size.z < DesignLength * MinimumFootprintFraction)
            {
                throw new InvalidOperationException(
                    "Underbody bounds do not correspond to the large " +
                    "Starter Island footprint (nominally 660 x 500 m): " +
                    $"{bounds}.");
            }

            if (Mathf.Abs(bounds.min.y - bottomY) > 0.08f ||
                bounds.max.y <= 0f)
            {
                throw new InvalidOperationException(
                    "Underbody vertical bounds are invalid. Expected its " +
                    $"tip at y={bottomY:0.###}; got {bounds}.");
            }

            return new ValidationReport(
                vertices.Length,
                indices.Length,
                indices.Length / 3,
                edges.Count,
                boundaryEdgeCount,
                bounds);
        }

        private static void ValidateRim(
            IReadOnlyList<Vector3> rim)
        {
            if (rim == null)
            {
                throw new ArgumentNullException(nameof(rim));
            }

            if (rim.Count < 24)
            {
                throw new ArgumentException(
                    "The organic rim needs at least 24 points.",
                    nameof(rim));
            }

            var bounds = CalculateBounds(rim);
            for (var index = 0; index < rim.Count; index++)
            {
                var current = rim[index];
                var next = rim[(index + 1) % rim.Count];
                if (!IsFinite(current))
                {
                    throw new ArgumentException(
                        $"Rim point {index} is not finite.",
                        nameof(rim));
                }

                var planarDelta =
                    new Vector2(
                        next.x - current.x,
                        next.z - current.z);
                if (planarDelta.sqrMagnitude <
                    RimMergeEpsilon * RimMergeEpsilon)
                {
                    throw new ArgumentException(
                        $"Rim points {index} and " +
                        $"{(index + 1) % rim.Count} overlap.",
                        nameof(rim));
                }
            }

            if (bounds.size.x > DesignWidth + BoundsTolerance ||
                bounds.size.z > DesignLength + BoundsTolerance ||
                bounds.size.x < DesignWidth * MinimumFootprintFraction ||
                bounds.size.z < DesignLength * MinimumFootprintFraction)
            {
                throw new ArgumentException(
                    "The supplied rim does not match the large Starter " +
                    "Island design envelope (nominally 660 x 500 m): " +
                    $"{bounds}.",
                    nameof(rim));
            }

            var signedArea = SignedPlanarArea(rim);
            if (Mathf.Abs(signedArea) < 1f)
            {
                throw new ArgumentException(
                    "The supplied rim has no usable planar area.",
                    nameof(rim));
            }

            ValidateNoSelfIntersections(rim);
        }

        private static void ValidateBottom(
            IReadOnlyList<Vector3> rim,
            float bottomY)
        {
            if (!IsFinite(bottomY))
            {
                throw new ArgumentOutOfRangeException(nameof(bottomY));
            }

            var minimumRimY = float.PositiveInfinity;
            for (var index = 0; index < rim.Count; index++)
            {
                minimumRimY = Mathf.Min(minimumRimY, rim[index].y);
            }

            if (bottomY > -10f ||
                bottomY > minimumRimY - 12f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bottomY),
                    bottomY,
                    "Underbody tip must sit substantially below the rim.");
            }
        }

        private static void ValidateNoSelfIntersections(
            IReadOnlyList<Vector3> rim)
        {
            for (var first = 0; first < rim.Count; first++)
            {
                var firstNext = (first + 1) % rim.Count;
                var a = new Vector2(rim[first].x, rim[first].z);
                var b =
                    new Vector2(
                        rim[firstNext].x,
                        rim[firstNext].z);
                for (var second = first + 1;
                     second < rim.Count;
                     second++)
                {
                    var secondNext = (second + 1) % rim.Count;
                    if (first == second ||
                        first == secondNext ||
                        firstNext == second ||
                        firstNext == secondNext)
                    {
                        continue;
                    }

                    var c =
                        new Vector2(
                            rim[second].x,
                            rim[second].z);
                    var d =
                        new Vector2(
                            rim[secondNext].x,
                            rim[secondNext].z);
                    if (SegmentsIntersect(a, b, c, d))
                    {
                        throw new ArgumentException(
                            "The supplied organic rim self-intersects at " +
                            $"segments {first} and {second}.",
                            nameof(rim));
                    }
                }
            }
        }

        private static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            const float epsilon = 0.00001f;
            var abC = Cross(b - a, c - a);
            var abD = Cross(b - a, d - a);
            var cdA = Cross(d - c, a - c);
            var cdB = Cross(d - c, b - c);
            if (((abC > epsilon && abD < -epsilon) ||
                 (abC < -epsilon && abD > epsilon)) &&
                ((cdA > epsilon && cdB < -epsilon) ||
                 (cdA < -epsilon && cdB > epsilon)))
            {
                return true;
            }

            return Mathf.Abs(abC) <= epsilon && OnSegment(a, b, c) ||
                   Mathf.Abs(abD) <= epsilon && OnSegment(a, b, d) ||
                   Mathf.Abs(cdA) <= epsilon && OnSegment(c, d, a) ||
                   Mathf.Abs(cdB) <= epsilon && OnSegment(c, d, b);
        }

        private static bool OnSegment(
            Vector2 a,
            Vector2 b,
            Vector2 point)
        {
            const float epsilon = 0.00001f;
            return point.x >= Mathf.Min(a.x, b.x) - epsilon &&
                   point.x <= Mathf.Max(a.x, b.x) + epsilon &&
                   point.y >= Mathf.Min(a.y, b.y) - epsilon &&
                   point.y <= Mathf.Max(a.y, b.y) + epsilon;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float SignedPlanarArea(
            IReadOnlyList<Vector3> rim)
        {
            var twiceArea = 0f;
            for (var index = 0; index < rim.Count; index++)
            {
                var current = rim[index];
                var next = rim[(index + 1) % rim.Count];
                twiceArea +=
                    current.x * next.z -
                    next.x * current.z;
            }

            return twiceArea * 0.5f;
        }

        private static Vector2 PlanarCentroid(
            IReadOnlyList<Vector3> rim,
            float signedArea)
        {
            var x = 0f;
            var z = 0f;
            for (var index = 0; index < rim.Count; index++)
            {
                var current = rim[index];
                var next = rim[(index + 1) % rim.Count];
                var factor =
                    current.x * next.z -
                    next.x * current.z;
                x += (current.x + next.x) * factor;
                z += (current.z + next.z) * factor;
            }

            var divisor = 6f * signedArea;
            return new Vector2(x / divisor, z / divisor);
        }

        private static Bounds CalculateBounds(
            IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0)
            {
                throw new ArgumentException(
                    "Cannot calculate empty bounds.",
                    nameof(points));
            }

            var bounds = new Bounds(points[0], Vector3.zero);
            for (var index = 1; index < points.Count; index++)
            {
                bounds.Encapsulate(points[index]);
            }

            return bounds;
        }

        private static GameObject FindOrCreateDirectChild(
            Transform parent,
            string objectName)
        {
            Transform found = null;
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (!string.Equals(
                        child.name,
                        objectName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"Duplicate generated object '{objectName}'.");
                }

                found = child;
            }

            if (found != null)
            {
                return found.gameObject;
            }

            var created = new GameObject(objectName);
            created.transform.SetParent(parent, false);
            return created;
        }

        private static void RemoveColliders(GameObject root)
        {
            var colliders =
                root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index]);
            }
        }

        private static void AssertIdentity(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            if (transform.localPosition != Vector3.zero ||
                transform.localRotation != Quaternion.identity ||
                transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    "Underbody transform must remain local identity.");
            }
        }

        private static bool IsExpectedOpenRimEdge(
            ulong edgeKey,
            int rimSampleCount)
        {
            var minimum = (int)(edgeKey >> 32);
            var maximum = (int)(edgeKey & uint.MaxValue);
            if (minimum < 0 ||
                maximum < 0 ||
                minimum >= rimSampleCount ||
                maximum >= rimSampleCount)
            {
                return false;
            }

            return maximum == minimum + 1 ||
                   minimum == 0 &&
                   maximum == rimSampleCount - 1;
        }

        private static void RegisterEdge(
            IDictionary<ulong, EdgeUse> edges,
            int from,
            int to)
        {
            var minimum = Math.Min(from, to);
            var maximum = Math.Max(from, to);
            var key =
                ((ulong)(uint)minimum << 32) |
                (uint)maximum;
            var direction = from < to ? 1 : -1;
            edges.TryGetValue(key, out var use);
            edges[key] = new EdgeUse(
                use.Count + 1,
                use.DirectionBalance + direction);
        }

        private static void AddBoundaryNeighbour(
            IDictionary<int, List<int>> adjacency,
            int from,
            int to)
        {
            if (!adjacency.TryGetValue(from, out var neighbours))
            {
                neighbours = new List<int>(2);
                adjacency.Add(from, neighbours);
            }

            neighbours.Add(to);
        }

        private static void ValidateSingleBoundaryLoop(
            IReadOnlyDictionary<int, List<int>> adjacency,
            int expectedVertexCount)
        {
            if (adjacency.Count != expectedVertexCount)
            {
                throw new InvalidOperationException(
                    "Underbody open rim has an invalid number of unique " +
                    $"boundary vertices. Expected {expectedVertexCount}; " +
                    $"got {adjacency.Count}.");
            }

            foreach (var pair in adjacency)
            {
                if (pair.Value.Count != 2 ||
                    pair.Value[0] == pair.Value[1])
                {
                    throw new InvalidOperationException(
                        "Underbody open rim must be one non-branching loop. " +
                        $"Boundary vertex {pair.Key} has " +
                        $"{pair.Value.Count} neighbours.");
                }
            }

            using var enumerator = adjacency.Keys.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                throw new InvalidOperationException(
                    "Underbody open rim is empty.");
            }

            var visited = new HashSet<int>();
            var pending = new Queue<int>();
            pending.Enqueue(enumerator.Current);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                var neighbours = adjacency[current];
                for (var index = 0;
                     index < neighbours.Count;
                     index++)
                {
                    pending.Enqueue(neighbours[index]);
                }
            }

            if (visited.Count != expectedVertexCount)
            {
                throw new InvalidOperationException(
                    "Underbody has multiple disconnected open-rim loops. " +
                    $"Expected one loop with {expectedVertexCount} vertices; " +
                    $"reached {visited.Count}.");
            }
        }

        private static void AddTriangle(
            ICollection<int> indices,
            int a,
            int b,
            int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        private static void ApplyFacetedNormals(
            Mesh mesh,
            int facetCount)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (facetCount < 8)
            {
                throw new ArgumentOutOfRangeException(nameof(facetCount));
            }

            var normals = mesh.normals;
            var facetStep = Mathf.PI * 2f / facetCount;
            for (var index = 0; index < normals.Length; index++)
            {
                var normal = normals[index];
                var horizontal =
                    new Vector2(normal.x, normal.z).magnitude;
                if (horizontal <= 0.0001f)
                {
                    continue;
                }

                var angle = Mathf.Atan2(normal.z, normal.x);
                var facetedAngle =
                    Mathf.Round(angle / facetStep) * facetStep;
                var elevation =
                    Mathf.Atan2(normal.y, horizontal);
                var facetedElevation =
                    Mathf.Round(
                        elevation / (Mathf.PI / 18f)) *
                    (Mathf.PI / 18f);
                normals[index] =
                    new Vector3(
                        Mathf.Cos(facetedAngle) *
                        Mathf.Cos(facetedElevation),
                        Mathf.Sin(facetedElevation),
                        Mathf.Sin(facetedAngle) *
                        Mathf.Cos(facetedElevation)).normalized;
            }

            mesh.normals = normals;
        }

        private static float EvaluateButtressStrength(float angle)
        {
            var strength = 0f;
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -2.82f, 0.30f, 0.98f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -1.98f, 0.26f, 0.78f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -1.18f, 0.32f, 0.94f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -0.23f, 0.25f, 0.84f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 0.66f, 0.31f, 1.00f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 1.55f, 0.27f, 0.82f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 2.42f, 0.33f, 0.92f));
            return Mathf.Clamp01(strength);
        }

        private static float EvaluateCreaseStrength(float angle)
        {
            var strength = 0f;
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -2.39f, 0.115f, 0.90f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -1.58f, 0.135f, 0.80f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, -0.69f, 0.105f, 0.98f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 0.20f, 0.125f, 0.84f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 1.11f, 0.110f, 0.94f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 1.99f, 0.135f, 0.78f));
            strength = Mathf.Max(
                strength,
                AngularLobe(angle, 2.83f, 0.110f, 0.96f));
            return Mathf.Clamp01(strength);
        }

        private static float AngularLobe(
            float angle,
            float center,
            float width,
            float strength)
        {
            var delta = Mathf.Atan2(
                Mathf.Sin(angle - center),
                Mathf.Cos(angle - center));
            var normalized = delta / Mathf.Max(0.001f, width);
            return strength * Mathf.Exp(-normalized * normalized);
        }

        private static void ValidateIndex(
            int index,
            int vertexCount)
        {
            if (index < 0 || index >= vertexCount)
            {
                throw new InvalidOperationException(
                    $"Underbody index {index} is outside 0..{vertexCount - 1}.");
            }
        }

        private static float SmootherStep(float value)
        {
            var t = Mathf.Clamp01(value);
            return t * t * t *
                   (t * (t * 6f - 15f) + 10f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(
                        current,
                        segments[index]);
                }

                current = next;
            }
        }

        private readonly struct RingProfile
        {
            public RingProfile(
                float scale,
                float depth,
                float ribStrength)
            {
                Scale = scale;
                Depth = depth;
                RibStrength = ribStrength;
            }

            public float Scale { get; }

            public float Depth { get; }

            public float RibStrength { get; }
        }

        private readonly struct MeshGeometry
        {
            public MeshGeometry(
                List<Vector3> vertices,
                List<Vector2> uv,
                List<Color32> colors,
                List<int> indices)
            {
                Vertices = vertices;
                Uv = uv;
                Colors = colors;
                Indices = indices;
            }

            public List<Vector3> Vertices { get; }

            public List<Vector2> Uv { get; }

            public List<Color32> Colors { get; }

            public List<int> Indices { get; }
        }

        private readonly struct EdgeUse
        {
            public EdgeUse(
                int count,
                int directionBalance)
            {
                Count = count;
                DirectionBalance = directionBalance;
            }

            public int Count { get; }

            public int DirectionBalance { get; }
        }

        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private const float Precision = 10000f;

            public PositionKey(Vector3 position)
            {
                X = Mathf.RoundToInt(position.x * Precision);
                Y = Mathf.RoundToInt(position.y * Precision);
                Z = Mathf.RoundToInt(position.z * Precision);
            }

            private int X { get; }

            private int Y { get; }

            private int Z { get; }

            public bool Equals(PositionKey other)
            {
                return X == other.X &&
                       Y == other.Y &&
                       Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is PositionKey other &&
                       Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = X;
                    hashCode = (hashCode * 397) ^ Y;
                    hashCode = (hashCode * 397) ^ Z;
                    return hashCode;
                }
            }
        }

        private sealed class DisjointSet
        {
            private readonly int[] parent;
            private readonly byte[] rank;

            public DisjointSet(int count)
            {
                parent = new int[count];
                rank = new byte[count];
                for (var index = 0; index < count; index++)
                {
                    parent[index] = index;
                }
            }

            public int Find(int value)
            {
                var root = value;
                while (parent[root] != root)
                {
                    root = parent[root];
                }

                while (parent[value] != value)
                {
                    var next = parent[value];
                    parent[value] = root;
                    value = next;
                }

                return root;
            }

            public void Union(int first, int second)
            {
                var firstRoot = Find(first);
                var secondRoot = Find(second);
                if (firstRoot == secondRoot)
                {
                    return;
                }

                if (rank[firstRoot] < rank[secondRoot])
                {
                    parent[firstRoot] = secondRoot;
                    return;
                }

                if (rank[firstRoot] > rank[secondRoot])
                {
                    parent[secondRoot] = firstRoot;
                    return;
                }

                parent[secondRoot] = firstRoot;
                rank[firstRoot]++;
            }
        }

        public readonly struct ValidationReport
        {
            public ValidationReport(
                int vertexCount,
                int indexCount,
                int triangleCount,
                int edgeCount,
                int boundaryEdgeCount,
                Bounds bounds)
            {
                VertexCount = vertexCount;
                IndexCount = indexCount;
                TriangleCount = triangleCount;
                EdgeCount = edgeCount;
                BoundaryEdgeCount = boundaryEdgeCount;
                Bounds = bounds;
            }

            public int VertexCount { get; }

            public int IndexCount { get; }

            public int TriangleCount { get; }

            public int EdgeCount { get; }

            public int BoundaryEdgeCount { get; }

            public Bounds Bounds { get; }
        }
    }
}

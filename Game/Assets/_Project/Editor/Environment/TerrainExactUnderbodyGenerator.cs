using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds an underbody from the exact visible boundary of a Unity Terrain.
    /// Paint-Holes data is authoritative: transitions between visible and
    /// removed cells define the seam. No old mesh, FBX, voxel field or guessed
    /// island outline participates in generation.
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainExactUnderbodyGenerator
    {
        public const string MeshAssetPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Data/" +
            "MESH_StarterIsland_Underbody_Exact.asset";

        private const string MaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_UnderbodyCliff.mat";
        private const string ObjectName = "TerrainUnderbody";
        private const string OneShotMarker =
            "Temp/CML_GenerateExactTerrainUnderbody.once";

        private const int RingCount = 49;
        // Removes raster-step corrugation below the untouched Paint-Holes rim.
        private const int ContourSmoothingRadius = 12;
        private const float MinimumBelowTerrain = 0.08f;
        private const float FirstRelaxedRingDepth = 0.006f;
        private const float TipRise = 16f;
        private const float SeamTolerance = 0.00025f;

        // A deliberately terraced silhouette. Each short outward recovery
        // creates one integrated rock shelf before the next inward taper. The
        // changes begin below the fixed Terrain collar, so Paint-Holes seams
        // remain byte-for-byte unchanged.
        private static readonly ScaleKey[] ScaleProfile =
        {
            new ScaleKey(0.000f, 1.000f),
            new ScaleKey(0.110f, 1.000f),
            new ScaleKey(0.185f, 0.945f),
            new ScaleKey(0.225f, 0.958f),
            new ScaleKey(0.305f, 0.850f),
            new ScaleKey(0.350f, 0.870f),
            new ScaleKey(0.440f, 0.735f),
            new ScaleKey(0.485f, 0.758f),
            new ScaleKey(0.585f, 0.605f),
            new ScaleKey(0.630f, 0.632f),
            new ScaleKey(0.720f, 0.475f),
            new ScaleKey(0.765f, 0.500f),
            new ScaleKey(0.845f, 0.350f),
            new ScaleKey(0.885f, 0.370f),
            new ScaleKey(0.940f, 0.245f),
            new ScaleKey(0.965f, 0.258f),
            new ScaleKey(0.990f, 0.115f),
            new ScaleKey(1.000f, 0.055f)
        };

        static TerrainExactUnderbodyGenerator()
        {
            EditorApplication.delayCall += RunOneShotIfRequested;
        }

        [MenuItem(
            "CML/Environment/Terrain Underbody/" +
            "Generate Exact Hole-Aware Underbody")]
        public static void GenerateFromSelectedTerrain()
        {
            Terrain terrain = ResolveTerrain();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog(
                    "Exact Terrain Underbody",
                    "Select a Terrain, or open a scene containing TerrainTop.",
                    "OK");
                return;
            }

            GenerateValidateAndApply(terrain);
            Selection.activeGameObject = terrain.gameObject;
        }

        [MenuItem(
            "CML/Environment/Terrain Underbody/" +
            "Generate Exact Hole-Aware Underbody",
            true)]
        private static bool ValidateGenerateFromSelectedTerrain()
        {
            return ResolveTerrain() != null;
        }

        public static Mesh GenerateValidateAndApply(Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null)
                throw new ArgumentNullException(nameof(terrain));
            Transform root = terrain.transform.parent;
            if (root == null)
                throw new InvalidOperationException("TerrainTop has no island root.");

            TerrainData data = terrain.terrainData;
            EditorUtility.DisplayProgressBar(
                "Hole-aware Terrain Underbody",
                "Reading Paint Holes and tracing exact visible boundaries...",
                0.08f);
            try
            {
                BoundaryExtraction extraction = ExtractBoundaries(data);
                Geometry geometry = BuildGeometry(
                    terrain,
                    root,
                    extraction.Loops);
                Mesh mesh = SaveMesh(geometry);
                ValidationReport report = ValidateMesh(
                    mesh,
                    terrain,
                    root,
                    geometry.Seams);
                ApplyValidatedMesh(terrain, root, mesh);

                Debug.Log(
                    "EXACT_TERRAIN_UNDERBODY_BUILD " +
                    $"asset={MeshAssetPath} terrain={terrain.name} " +
                    $"heightmap={data.heightmapResolution} " +
                    $"holes={data.holesResolution} " +
                    $"solidCells={extraction.SolidCellCount} " +
                    $"components={extraction.ComponentCount} " +
                    $"loops={extraction.Loops.Count} " +
                    $"outerLoops={extraction.OuterLoopCount} " +
                    $"innerHoleLoops={extraction.InnerLoopCount} " +
                    $"rim={report.BoundaryEdges} rings={RingCount} " +
                    $"vertices={mesh.vertexCount} " +
                    $"triangles={mesh.triangles.Length / 3} " +
                    $"maxSeamError={report.MaximumSeamError:F7} " +
                    $"maxTerrainCrossing={report.MaximumTerrainCrossing:F7} " +
                    $"maxEdge={report.MaximumEdge:F3} " +
                    "source=TerrainData.GetHoles holeAware=1 exactRim=1 " +
                    "bottomClosed=1 manifold=1 terrainChanged=0 " +
                    "cameraChanged=0 collider=0 status=PASS");
                return mesh;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void RunOneShotIfRequested()
        {
            string markerPath = Path.GetFullPath(OneShotMarker);
            if (!File.Exists(markerPath))
                return;
            try
            {
                Terrain terrain = ResolveTerrain();
                if (terrain == null)
                {
                    Debug.LogError(
                        "[TerrainExactUnderbodyGenerator] TerrainTop was not " +
                        "found. The marker was kept for retry.");
                    return;
                }

                GenerateValidateAndApply(terrain);
                File.Delete(markerPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static BoundaryExtraction ExtractBoundaries(TerrainData data)
        {
            int width = data.holesResolution;
            int height = data.holesResolution;
            if (width < 1 || height < 1)
                throw new InvalidOperationException("Terrain holes mask is empty.");

            bool[,] visible = data.GetHoles(0, 0, width, height);
            int[,] labels = new int[height, width];
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
                labels[z, x] = -1;

            int[] queue = new int[width * height];
            var components = new List<ComponentInfo>();
            int solidCellCount = 0;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!visible[z, x] || labels[z, x] >= 0)
                        continue;

                    int componentId = components.Count;
                    int head = 0;
                    int tail = 0;
                    queue[tail++] = z * width + x;
                    labels[z, x] = componentId;
                    int count = 0;
                    double sumX = 0d;
                    double sumZ = 0d;
                    while (head < tail)
                    {
                        int packed = queue[head++];
                        int cellX = packed % width;
                        int cellZ = packed / width;
                        count++;
                        solidCellCount++;
                        sumX += cellX + 0.5d;
                        sumZ += cellZ + 0.5d;
                        EnqueueVisibleCell(
                            cellX - 1,
                            cellZ,
                            width,
                            height,
                            visible,
                            labels,
                            componentId,
                            queue,
                            ref tail);
                        EnqueueVisibleCell(
                            cellX + 1,
                            cellZ,
                            width,
                            height,
                            visible,
                            labels,
                            componentId,
                            queue,
                            ref tail);
                        EnqueueVisibleCell(
                            cellX,
                            cellZ - 1,
                            width,
                            height,
                            visible,
                            labels,
                            componentId,
                            queue,
                            ref tail);
                        EnqueueVisibleCell(
                            cellX,
                            cellZ + 1,
                            width,
                            height,
                            visible,
                            labels,
                            componentId,
                            queue,
                            ref tail);
                    }

                    components.Add(new ComponentInfo(
                        count,
                        new Vector2(
                            (float)(sumX / count),
                            (float)(sumZ / count))));
                }
            }

            if (solidCellCount == 0)
                throw new InvalidOperationException("Paint Holes removed the entire Terrain.");

            var edgesByComponent = new List<List<GridEdge>>(components.Count);
            for (int index = 0; index < components.Count; index++)
                edgesByComponent.Add(new List<GridEdge>());

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int componentId = labels[z, x];
                    if (componentId < 0)
                        continue;
                    List<GridEdge> edges = edgesByComponent[componentId];
                    if (z == 0 || !visible[z - 1, x])
                        edges.Add(new GridEdge(
                            new GridPoint(x, z),
                            new GridPoint(x + 1, z)));
                    if (x == width - 1 || !visible[z, x + 1])
                        edges.Add(new GridEdge(
                            new GridPoint(x + 1, z),
                            new GridPoint(x + 1, z + 1)));
                    if (z == height - 1 || !visible[z + 1, x])
                        edges.Add(new GridEdge(
                            new GridPoint(x + 1, z + 1),
                            new GridPoint(x, z + 1)));
                    if (x == 0 || !visible[z, x - 1])
                        edges.Add(new GridEdge(
                            new GridPoint(x, z + 1),
                            new GridPoint(x, z)));
                }
            }

            var loops = new List<BoundaryLoop>();
            int outerCount = 0;
            int innerCount = 0;
            for (int componentId = 0;
                 componentId < edgesByComponent.Count;
                 componentId++)
            {
                List<List<GridPoint>> gridLoops = TraceLoops(
                    edgesByComponent[componentId]);
                for (int loopIndex = 0;
                     loopIndex < gridLoops.Count;
                     loopIndex++)
                {
                    List<GridPoint> gridLoop = gridLoops[loopIndex];
                    float area = SignedArea(gridLoop);
                    if (Mathf.Abs(area) < 0.5f)
                        throw new InvalidOperationException("A Paint Holes boundary has zero area.");
                    bool outer = area > 0f;
                    if (outer)
                        outerCount++;
                    else
                        innerCount++;

                    List<Vector3> points = ConvertLoopToTerrainSpace(
                        data,
                        gridLoop,
                        width,
                        height);
                    Vector2 anchor = outer
                        ? FindInteriorAnchor(
                            gridLoop,
                            components[componentId].AverageCell,
                            data.size,
                            width,
                            height)
                        : PolygonCentroid(points, area);
                    loops.Add(new BoundaryLoop(
                        componentId,
                        outer,
                        points,
                        anchor));
                }
            }

            if (loops.Count == 0 || outerCount == 0)
                throw new InvalidOperationException("No visible Paint Holes boundary was found.");
            return new BoundaryExtraction(
                loops,
                components.Count,
                solidCellCount,
                outerCount,
                innerCount);
        }

        private static void EnqueueVisibleCell(
            int x,
            int z,
            int width,
            int height,
            bool[,] visible,
            int[,] labels,
            int componentId,
            int[] queue,
            ref int tail)
        {
            if (x < 0 || z < 0 || x >= width || z >= height ||
                !visible[z, x] || labels[z, x] >= 0)
            {
                return;
            }

            labels[z, x] = componentId;
            queue[tail++] = z * width + x;
        }

        private static List<List<GridPoint>> TraceLoops(List<GridEdge> edges)
        {
            var outgoing = new Dictionary<GridPoint, List<GridEdge>>();
            var unused = new HashSet<GridEdge>();
            for (int index = 0; index < edges.Count; index++)
            {
                GridEdge edge = edges[index];
                unused.Add(edge);
                if (!outgoing.TryGetValue(edge.Start, out List<GridEdge> list))
                {
                    list = new List<GridEdge>(2);
                    outgoing.Add(edge.Start, list);
                }
                list.Add(edge);
            }

            var loops = new List<List<GridPoint>>();
            while (unused.Count > 0)
            {
                GridEdge first = default;
                foreach (GridEdge candidate in unused)
                {
                    first = candidate;
                    break;
                }

                unused.Remove(first);
                var loop = new List<GridPoint> { first.Start };
                GridPoint current = first.End;
                GridPoint direction = first.End - first.Start;
                int safety = 0;
                while (!current.Equals(first.Start))
                {
                    loop.Add(current);
                    if (!outgoing.TryGetValue(current, out List<GridEdge> candidates))
                        throw new InvalidOperationException("Paint Holes boundary is open.");
                    GridEdge next = ChooseContinuation(
                        direction,
                        candidates,
                        unused);
                    if (!unused.Remove(next))
                        throw new InvalidOperationException("Paint Holes boundary repeats an edge.");
                    direction = next.End - next.Start;
                    current = next.End;
                    safety++;
                    if (safety > edges.Count + 1)
                        throw new InvalidOperationException("Paint Holes boundary never closes.");
                }

                if (loop.Count < 4)
                    throw new InvalidOperationException("Paint Holes boundary is too small.");
                loops.Add(loop);
            }

            return loops;
        }

        private static GridEdge ChooseContinuation(
            GridPoint incoming,
            IReadOnlyList<GridEdge> candidates,
            ISet<GridEdge> unused)
        {
            bool found = false;
            GridEdge best = default;
            int bestRank = int.MinValue;
            for (int index = 0; index < candidates.Count; index++)
            {
                GridEdge candidate = candidates[index];
                if (!unused.Contains(candidate))
                    continue;
                GridPoint outgoing = candidate.End - candidate.Start;
                int cross = incoming.X * outgoing.Z - incoming.Z * outgoing.X;
                int dot = incoming.X * outgoing.X + incoming.Z * outgoing.Z;
                int rank = cross > 0 ? 3 : dot > 0 ? 2 : cross < 0 ? 1 : 0;
                if (!found || rank > bestRank)
                {
                    found = true;
                    best = candidate;
                    bestRank = rank;
                }
            }

            if (!found)
                throw new InvalidOperationException("Paint Holes boundary has no continuation.");
            return best;
        }

        private static List<Vector3> ConvertLoopToTerrainSpace(
            TerrainData data,
            IReadOnlyList<GridPoint> loop,
            int gridWidth,
            int gridHeight)
        {
            var points = new List<Vector3>(loop.Count);
            for (int index = 0; index < loop.Count; index++)
            {
                float normalizedX = loop[index].X / (float)gridWidth;
                float normalizedZ = loop[index].Z / (float)gridHeight;
                points.Add(new Vector3(
                    normalizedX * data.size.x,
                    data.GetInterpolatedHeight(normalizedX, normalizedZ),
                    normalizedZ * data.size.z));
            }
            return points;
        }

        private static Vector2 FindInteriorAnchor(
            IReadOnlyList<GridPoint> loop,
            Vector2 averageCell,
            Vector3 terrainSize,
            int gridWidth,
            int gridHeight)
        {
            Vector2 gridPoint = averageCell;
            if (!PointInPolygon(gridPoint, loop))
            {
                float bestDistance = float.PositiveInfinity;
                GridPoint best = loop[0];
                int bestIndex = 0;
                for (int index = 0; index < loop.Count; index++)
                {
                    float distance = ((Vector2)loop[index] - averageCell).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = loop[index];
                        bestIndex = index;
                    }
                }
                GridPoint next = loop[(bestIndex + 1) % loop.Count];
                gridPoint = Vector2.Lerp((Vector2)best, (Vector2)next, 0.5f);
            }
            return new Vector2(
                gridPoint.x / gridWidth * terrainSize.x,
                gridPoint.y / gridHeight * terrainSize.z);
        }

        private static bool PointInPolygon(
            Vector2 point,
            IReadOnlyList<GridPoint> polygon)
        {
            bool inside = false;
            int previous = polygon.Count - 1;
            for (int current = 0; current < polygon.Count; current++)
            {
                Vector2 a = polygon[previous];
                Vector2 b = polygon[current];
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float crossingX = (b.x - a.x) * (point.y - a.y) /
                                      (b.y - a.y) + a.x;
                    if (point.x < crossingX)
                        inside = !inside;
                }
                previous = current;
            }
            return inside;
        }

        private static float SignedArea(IReadOnlyList<GridPoint> loop)
        {
            long twiceArea = 0L;
            for (int index = 0; index < loop.Count; index++)
            {
                GridPoint a = loop[index];
                GridPoint b = loop[(index + 1) % loop.Count];
                twiceArea += (long)a.X * b.Z - (long)b.X * a.Z;
            }
            return twiceArea * 0.5f;
        }

        private static Vector2 PolygonCentroid(
            IReadOnlyList<Vector3> points,
            float signedGridArea)
        {
            double twiceArea = 0d;
            double x = 0d;
            double z = 0d;
            for (int index = 0; index < points.Count; index++)
            {
                Vector3 a = points[index];
                Vector3 b = points[(index + 1) % points.Count];
                double cross = (double)a.x * b.z - (double)b.x * a.z;
                twiceArea += cross;
                x += (a.x + b.x) * cross;
                z += (a.z + b.z) * cross;
            }
            if (Math.Abs(twiceArea) < 0.000001d)
            {
                Vector3 first = points[0];
                return new Vector2(first.x, first.z);
            }
            double factor = 1d / (3d * twiceArea);
            return new Vector2((float)(x * factor), (float)(z * factor));
        }

        private static Geometry BuildGeometry(
            Terrain terrain,
            Transform root,
            IReadOnlyList<BoundaryLoop> loops)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var seams = new List<SeamRecord>(loops.Count);
            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;

            for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
            {
                BoundaryLoop loop = loops[loopIndex];
                int count = loop.Points.Count;
                int baseVertex = vertices.Count;
                float minimumRimY = float.PositiveInfinity;
                Bounds bounds = new Bounds(loop.Points[0], Vector3.zero);
                for (int index = 0; index < count; index++)
                {
                    minimumRimY = Mathf.Min(minimumRimY, loop.Points[index].y);
                    bounds.Encapsulate(loop.Points[index]);
                }
                float depth = Mathf.Max(
                    6f,
                    Mathf.Max(bounds.size.x, bounds.size.z) * 0.42f);
                float bottomY = minimumRimY - depth;
                Vector2[] relaxedContour = BuildRelaxedContour(
                    loop.Points,
                    ContourSmoothingRadius);

                for (int ring = 0; ring < RingCount; ring++)
                {
                    float t = ring / (RingCount - 1f);
                    float scale = EvaluateScale(t);
                    float verticalT = ring == 1
                        ? FirstRelaxedRingDepth
                        : t;
                    for (int pointIndex = 0; pointIndex < count; pointIndex++)
                    {
                        Vector3 source = loop.Points[pointIndex];
                        Vector2 contour = ring == 0
                            ? new Vector2(source.x, source.z)
                            : relaxedContour[pointIndex];
                        Vector2 radial = contour - loop.Anchor;
                        Vector2 xz = loop.Anchor + radial * scale;
                        float nominalY = Mathf.Lerp(
                            source.y,
                            bottomY + TipRise,
                            verticalT);
                        float y = ring == 0
                            ? source.y
                            : Mathf.Min(
                                nominalY,
                                SampleTerrainHeight(
                                    terrain.terrainData,
                                    xz.x,
                                    xz.y) - MinimumBelowTerrain);
                        Vector3 terrainLocal = new Vector3(xz.x, y, xz.y);
                        Vector3 rootLocal = root.InverseTransformPoint(
                            terrain.transform.TransformPoint(terrainLocal));
                        vertices.Add(rootLocal);
                        minimumY = Mathf.Min(minimumY, rootLocal.y);
                        maximumY = Mathf.Max(maximumY, rootLocal.y);
                    }
                }

                bool counterClockwise = loop.Outer;
                for (int ring = 0; ring < RingCount - 1; ring++)
                {
                    int upperStart = baseVertex + ring * count;
                    int lowerStart = upperStart + count;
                    for (int pointIndex = 0; pointIndex < count; pointIndex++)
                    {
                        int next = (pointIndex + 1) % count;
                        AddQuad(
                            indices,
                            upperStart + pointIndex,
                            upperStart + next,
                            lowerStart + pointIndex,
                            lowerStart + next,
                            counterClockwise);
                    }
                }

                int tip = vertices.Count;
                Vector3 tipTerrainLocal = new Vector3(
                    loop.Anchor.x,
                    bottomY,
                    loop.Anchor.y);
                Vector3 tipRootLocal = root.InverseTransformPoint(
                    terrain.transform.TransformPoint(tipTerrainLocal));
                vertices.Add(tipRootLocal);
                minimumY = Mathf.Min(minimumY, tipRootLocal.y);
                maximumY = Mathf.Max(maximumY, tipRootLocal.y);
                int finalStart = baseVertex + (RingCount - 1) * count;
                for (int pointIndex = 0; pointIndex < count; pointIndex++)
                {
                    int next = (pointIndex + 1) % count;
                    if (counterClockwise)
                    {
                        indices.Add(finalStart + pointIndex);
                        indices.Add(finalStart + next);
                        indices.Add(tip);
                    }
                    else
                    {
                        indices.Add(finalStart + pointIndex);
                        indices.Add(tip);
                        indices.Add(finalStart + next);
                    }
                }

                var rootRim = new List<Vector3>(count);
                for (int pointIndex = 0; pointIndex < count; pointIndex++)
                {
                    rootRim.Add(root.InverseTransformPoint(
                        terrain.transform.TransformPoint(loop.Points[pointIndex])));
                }
                seams.Add(new SeamRecord(baseVertex, rootRim));
            }

            return new Geometry(
                vertices,
                indices,
                seams,
                minimumY,
                maximumY);
        }

        private static void AddQuad(
            ICollection<int> indices,
            int upper,
            int upperNext,
            int lower,
            int lowerNext,
            bool counterClockwise)
        {
            if (counterClockwise)
            {
                indices.Add(upper);
                indices.Add(upperNext);
                indices.Add(lower);
                indices.Add(upperNext);
                indices.Add(lowerNext);
                indices.Add(lower);
            }
            else
            {
                indices.Add(upper);
                indices.Add(lower);
                indices.Add(upperNext);
                indices.Add(upperNext);
                indices.Add(lower);
                indices.Add(lowerNext);
            }
        }

        private static float EvaluateScale(float t)
        {
            t = Mathf.Clamp01(t);
            for (int index = 0; index < ScaleProfile.Length - 1; index++)
            {
                ScaleKey current = ScaleProfile[index];
                ScaleKey next = ScaleProfile[index + 1];
                if (t > next.Depth)
                    continue;
                float progress = Mathf.InverseLerp(
                    current.Depth,
                    next.Depth,
                    t);
                return Mathf.Lerp(
                    current.Scale,
                    next.Scale,
                    SmootherStep(progress));
            }
            return ScaleProfile[ScaleProfile.Length - 1].Scale;
        }

        private static Vector2[] BuildRelaxedContour(
            IReadOnlyList<Vector3> points,
            int radius)
        {
            int count = points.Count;
            var result = new Vector2[count];
            if (count == 0)
                return result;

            radius = Mathf.Clamp(radius, 1, Mathf.Max(1, (count - 1) / 2));
            for (int point = 0; point < count; point++)
            {
                Vector2 sum = Vector2.zero;
                float totalWeight = 0f;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int neighbour = (point + offset + count) % count;
                    float weight = radius + 1 - Mathf.Abs(offset);
                    Vector3 sample = points[neighbour];
                    sum += new Vector2(sample.x, sample.z) * weight;
                    totalWeight += weight;
                }

                result[point] = sum / Mathf.Max(totalWeight, 0.0001f);
            }

            return result;
        }

        private static float SampleTerrainHeight(
            TerrainData data,
            float terrainLocalX,
            float terrainLocalZ)
        {
            return data.GetInterpolatedHeight(
                Mathf.Clamp01(terrainLocalX / data.size.x),
                Mathf.Clamp01(terrainLocalZ / data.size.z));
        }

        private static Mesh SaveMesh(Geometry geometry)
        {
            EnsureFolder(Path.GetDirectoryName(MeshAssetPath)?.Replace('\\', '/'));
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (mesh == null)
            {
                UnityEngine.Object occupied =
                    AssetDatabase.LoadMainAssetAtPath(MeshAssetPath);
                if (occupied != null)
                    throw new InvalidOperationException("Exact mesh asset path is occupied.");
                mesh = new Mesh
                {
                    name = "MESH_StarterIsland_Underbody_Exact"
                };
                AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            }
            else
            {
                mesh.Clear(false);
                mesh.name = "MESH_StarterIsland_Underbody_Exact";
            }

            mesh.indexFormat = geometry.Vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(geometry.Vertices);
            mesh.SetTriangles(geometry.Indices, 0, true);
            var uv = new List<Vector2>(geometry.Vertices.Count);
            var colors = new List<Color32>(geometry.Vertices.Count);
            float heightRange = Mathf.Max(
                1f,
                geometry.MaximumY - geometry.MinimumY);
            for (int index = 0; index < geometry.Vertices.Count; index++)
            {
                Vector3 vertex = geometry.Vertices[index];
                uv.Add(new Vector2(vertex.x * 0.035f, vertex.z * 0.035f));
                float height01 = Mathf.Clamp01(
                    (vertex.y - geometry.MinimumY) / heightRange);
                byte blend = (byte)Mathf.RoundToInt(
                    Mathf.Lerp(104f, 218f, height01));
                byte wet = (byte)Mathf.RoundToInt(
                    Mathf.Lerp(92f, 22f, height01));
                colors.Add(new Color32(blend, blend, blend, wet));
            }
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            SmoothCircumferentialNormals(mesh, geometry, 24);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static void SmoothCircumferentialNormals(
            Mesh mesh,
            Geometry geometry,
            int radius)
        {
            Vector3[] source = mesh.normals;
            Vector3[] smoothed = (Vector3[])source.Clone();
            radius = Mathf.Max(1, radius);

            for (int seamIndex = 0; seamIndex < geometry.Seams.Count; seamIndex++)
            {
                SeamRecord seam = geometry.Seams[seamIndex];
                int count = seam.Points.Count;
                if (count < 3)
                    continue;

                int localRadius = Mathf.Min(radius, (count - 1) / 2);
                for (int ring = 0; ring < RingCount; ring++)
                {
                    int ringStart = seam.StartVertex + ring * count;
                    for (int point = 0; point < count; point++)
                    {
                        Vector3 sum = Vector3.zero;
                        float totalWeight = 0f;
                        for (int offset = -localRadius;
                             offset <= localRadius;
                             offset++)
                        {
                            int neighbour = (point + offset + count) % count;
                            float weight = localRadius + 1 - Mathf.Abs(offset);
                            sum += source[ringStart + neighbour] * weight;
                            totalWeight += weight;
                        }

                        Vector3 normal = totalWeight > 0f
                            ? sum / totalWeight
                            : source[ringStart + point];
                        if (normal.sqrMagnitude > 0.000001f)
                            normal.Normalize();
                        else
                            normal = source[ringStart + point];
                        smoothed[ringStart + point] = normal;
                    }
                }
            }

            mesh.normals = smoothed;
        }

        private static ValidationReport ValidateMesh(
            Mesh mesh,
            Terrain terrain,
            Transform root,
            IReadOnlyList<SeamRecord> seams)
        {
            if (mesh == null || mesh.vertexCount < 4)
                throw new InvalidOperationException("Hole-aware mesh is empty.");
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var topVertices = new HashSet<int>();
            int expectedBoundaryEdges = 0;
            float maximumSeamError = 0f;
            for (int seamIndex = 0; seamIndex < seams.Count; seamIndex++)
            {
                SeamRecord seam = seams[seamIndex];
                expectedBoundaryEdges += seam.Points.Count;
                for (int index = 0; index < seam.Points.Count; index++)
                {
                    int vertexIndex = seam.StartVertex + index;
                    topVertices.Add(vertexIndex);
                    float error = Vector3.Distance(
                        vertices[vertexIndex],
                        seam.Points[index]);
                    maximumSeamError = Mathf.Max(maximumSeamError, error);
                    if (error > SeamTolerance)
                        throw new InvalidOperationException(
                            $"Hole seam vertex error is {error:F7} m.");
                }
            }

            float maximumTerrainCrossing = 0f;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index];
                if (!IsFinite(vertex))
                    throw new InvalidOperationException($"Vertex {index} is not finite.");
                Vector3 terrainLocal = terrain.transform.InverseTransformPoint(
                    root.TransformPoint(vertex));
                float crossing = terrainLocal.y - SampleTerrainHeight(
                    terrain.terrainData,
                    terrainLocal.x,
                    terrainLocal.z);
                maximumTerrainCrossing = Mathf.Max(
                    maximumTerrainCrossing,
                    crossing);
                if (!topVertices.Contains(index) &&
                    crossing > -MinimumBelowTerrain + 0.002f)
                {
                    throw new InvalidOperationException(
                        $"Vertex {index} crosses or touches the Terrain.");
                }
            }

            var edgeUse = new Dictionary<Edge, int>(triangles.Length);
            float maximumEdge = 0f;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                Vector3 ab = vertices[b] - vertices[a];
                Vector3 ac = vertices[c] - vertices[a];
                if (Vector3.Cross(ab, ac).sqrMagnitude < 0.00000001f)
                    throw new InvalidOperationException(
                        $"Degenerate triangle {index / 3}.");
                maximumEdge = Mathf.Max(
                    maximumEdge,
                    (vertices[a] - vertices[b]).magnitude,
                    (vertices[b] - vertices[c]).magnitude,
                    (vertices[c] - vertices[a]).magnitude);
                CountEdge(edgeUse, a, b);
                CountEdge(edgeUse, b, c);
                CountEdge(edgeUse, c, a);
            }

            int boundaryEdges = 0;
            foreach (KeyValuePair<Edge, int> pair in edgeUse)
            {
                if (pair.Value == 1)
                {
                    boundaryEdges++;
                    if (!topVertices.Contains(pair.Key.A) ||
                        !topVertices.Contains(pair.Key.B))
                    {
                        throw new InvalidOperationException(
                            "A mesh opening exists away from a Paint Holes seam.");
                    }
                }
                else if (pair.Value != 2)
                {
                    throw new InvalidOperationException(
                        "Hole-aware underbody is non-manifold.");
                }
            }
            if (boundaryEdges != expectedBoundaryEdges)
                throw new InvalidOperationException(
                    $"Boundary edge count is {boundaryEdges}; " +
                    $"expected {expectedBoundaryEdges}.");

            float allowedEdge = Mathf.Max(
                terrain.terrainData.size.x,
                terrain.terrainData.size.z) * 0.09f;
            if (maximumEdge > allowedEdge)
                throw new InvalidOperationException(
                    $"Oversized edge {maximumEdge:F3} m exceeds {allowedEdge:F3} m.");
            return new ValidationReport(
                boundaryEdges,
                maximumSeamError,
                maximumTerrainCrossing,
                maximumEdge);
        }

        private static void ApplyValidatedMesh(
            Terrain terrain,
            Transform root,
            Mesh mesh)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
                throw new InvalidOperationException("Underbody material is missing.");
            Transform underbody = root.Find(ObjectName);
            if (underbody == null)
            {
                var created = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(created, "Create hole-aware underbody");
                underbody = created.transform;
                underbody.SetParent(root, false);
            }
            Undo.RecordObject(underbody, "Align hole-aware underbody");
            underbody.localPosition = Vector3.zero;
            underbody.localRotation = Quaternion.identity;
            underbody.localScale = Vector3.one;
            MeshFilter filter = underbody.GetComponent<MeshFilter>();
            MeshRenderer renderer = underbody.GetComponent<MeshRenderer>();
            if (filter == null)
                filter = Undo.AddComponent<MeshFilter>(underbody.gameObject);
            if (renderer == null)
                renderer = Undo.AddComponent<MeshRenderer>(underbody.gameObject);
            Undo.RecordObject(filter, "Apply hole-aware underbody mesh");
            Undo.RecordObject(renderer, "Apply hole-aware underbody material");
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            foreach (Collider collider in underbody.GetComponents<Collider>())
                UnityEngine.Object.DestroyImmediate(collider);
            GameObjectUtility.SetStaticEditorFlags(
                underbody.gameObject,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.ReflectionProbeStatic);
            EditorUtility.SetDirty(underbody);
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(underbody);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            Scene scene = terrain.gameObject.scene;
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Terrain ResolveTerrain()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                Terrain selectedTerrain = selected.GetComponent<Terrain>();
                if (selectedTerrain == null)
                    selectedTerrain = selected.GetComponentInParent<Terrain>();
                if (selectedTerrain != null)
                    return selectedTerrain;
            }
            foreach (Terrain terrain in Resources.FindObjectsOfTypeAll<Terrain>())
            {
                Scene scene = terrain.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded && terrain.name == "TerrainTop")
                    return terrain;
            }
            return null;
        }

        private static void CountEdge(
            IDictionary<Edge, int> edges,
            int a,
            int b)
        {
            var edge = new Edge(a, b);
            edges.TryGetValue(edge, out int count);
            edges[edge] = count + 1;
        }

        private static float SmootherStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value *
                   (value * (value * 6f - 15f) + 10f);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder))
                throw new ArgumentException("Asset folder is empty.");
            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private sealed class BoundaryExtraction
        {
            public readonly List<BoundaryLoop> Loops;
            public readonly int ComponentCount;
            public readonly int SolidCellCount;
            public readonly int OuterLoopCount;
            public readonly int InnerLoopCount;
            public BoundaryExtraction(
                List<BoundaryLoop> loops,
                int componentCount,
                int solidCellCount,
                int outerLoopCount,
                int innerLoopCount)
            {
                Loops = loops;
                ComponentCount = componentCount;
                SolidCellCount = solidCellCount;
                OuterLoopCount = outerLoopCount;
                InnerLoopCount = innerLoopCount;
            }
        }

        private sealed class BoundaryLoop
        {
            public readonly int ComponentId;
            public readonly bool Outer;
            public readonly List<Vector3> Points;
            public readonly Vector2 Anchor;
            public BoundaryLoop(
                int componentId,
                bool outer,
                List<Vector3> points,
                Vector2 anchor)
            {
                ComponentId = componentId;
                Outer = outer;
                Points = points;
                Anchor = anchor;
            }
        }

        private sealed class ComponentInfo
        {
            public readonly int CellCount;
            public readonly Vector2 AverageCell;
            public ComponentInfo(int cellCount, Vector2 averageCell)
            {
                CellCount = cellCount;
                AverageCell = averageCell;
            }
        }

        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices;
            public readonly List<int> Indices;
            public readonly List<SeamRecord> Seams;
            public readonly float MinimumY;
            public readonly float MaximumY;
            public Geometry(
                List<Vector3> vertices,
                List<int> indices,
                List<SeamRecord> seams,
                float minimumY,
                float maximumY)
            {
                Vertices = vertices;
                Indices = indices;
                Seams = seams;
                MinimumY = minimumY;
                MaximumY = maximumY;
            }
        }

        private sealed class SeamRecord
        {
            public readonly int StartVertex;
            public readonly List<Vector3> Points;
            public SeamRecord(int startVertex, List<Vector3> points)
            {
                StartVertex = startVertex;
                Points = points;
            }
        }

        private readonly struct ValidationReport
        {
            public readonly int BoundaryEdges;
            public readonly float MaximumSeamError;
            public readonly float MaximumTerrainCrossing;
            public readonly float MaximumEdge;
            public ValidationReport(
                int boundaryEdges,
                float maximumSeamError,
                float maximumTerrainCrossing,
                float maximumEdge)
            {
                BoundaryEdges = boundaryEdges;
                MaximumSeamError = maximumSeamError;
                MaximumTerrainCrossing = maximumTerrainCrossing;
                MaximumEdge = maximumEdge;
            }
        }

        private readonly struct ScaleKey
        {
            public readonly float Depth;
            public readonly float Scale;
            public ScaleKey(float depth, float scale)
            {
                Depth = depth;
                Scale = scale;
            }
        }

        private readonly struct GridPoint : IEquatable<GridPoint>
        {
            public readonly int X;
            public readonly int Z;
            public GridPoint(int x, int z)
            {
                X = x;
                Z = z;
            }
            public bool Equals(GridPoint other) => X == other.X && Z == other.Z;
            public override bool Equals(object obj) =>
                obj is GridPoint other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (X * 397) ^ Z; }
            }
            public static GridPoint operator -(GridPoint a, GridPoint b) =>
                new GridPoint(a.X - b.X, a.Z - b.Z);
            public static implicit operator Vector2(GridPoint point) =>
                new Vector2(point.X, point.Z);
        }

        private readonly struct GridEdge : IEquatable<GridEdge>
        {
            public readonly GridPoint Start;
            public readonly GridPoint End;
            public GridEdge(GridPoint start, GridPoint end)
            {
                Start = start;
                End = end;
            }
            public bool Equals(GridEdge other) =>
                Start.Equals(other.Start) && End.Equals(other.End);
            public override bool Equals(object obj) =>
                obj is GridEdge other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (Start.GetHashCode() * 397) ^ End.GetHashCode(); }
            }
        }

        private readonly struct Edge : IEquatable<Edge>
        {
            public readonly int A;
            public readonly int B;
            public Edge(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
            }
            public bool Equals(Edge other) => A == other.A && B == other.B;
            public override bool Equals(object obj) =>
                obj is Edge other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return (A * 397) ^ B; }
            }
        }
    }
}

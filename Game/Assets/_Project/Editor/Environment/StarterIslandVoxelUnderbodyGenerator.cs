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
    /// Rebuilds the existing Starter Island underbody as a procedural voxel
    /// surface directly in Unity. It deliberately avoids an FBX/Blender round
    /// trip, so the rim remains in the Terrain prefab's exact local space.
    ///
    /// The signed field combines a closed tapered shell, deterministic
    /// ellipsoidal masses, vertical side striation and low-frequency 3D rock
    /// noise. Marching tetrahedra performs the voxel remesh. The result remains
    /// visual-only; TerrainCollider stays the sole collision authority.
    /// </summary>
    [InitializeOnLoad]
    public static class StarterIslandVoxelUnderbodyGenerator
    {
        public const string GeneratedMeshPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Data/" +
            "MESH_StarterIsland_Underbody_Procedural.asset";

        private const string UnderbodyMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_UnderbodyCliff.mat";
        private const string OneShotMarker =
            "Temp/CML_GenerateMonolithicUnderbody.once";
        private const string DisableBrokenMarker =
            "Temp/CML_DisableBrokenUnderbody.once";

        // Roughly isotropic 6-7 m cells across the 660 x 500 m island and a
        // finer vertical step. The procedural field supplies the soft forms;
        // the stylized shader supplies the sub-meter surface detail.
        private const int GridX = 97;
        private const int GridZ = 73;
        private const int GridY = 57;
        private const int RandomBulgeCount = 28;
        private const int Seed = 7319;
        private const float TerrainOverlap = 0.65f;
        private const float RimOverlap = 1.8f;
        private const float RimThickness = 9f;
        private const float SideStableDepth = 7f;
        private const float BottomNoiseAmplitude = 5.5f;
        private const float SideStriationAmplitude = 3.2f;
        private const float MinimumGeneratedDepth = 250f;
        private const float PositionWeldScale = 1000f;
        private const int MonolithRingCount = 58;

        private static readonly MonolithLobe[] MonolithLobes =
        {
            new MonolithLobe(0.08f, 0.42f, 0.42f, 0.24f, 0.115f),
            new MonolithLobe(0.62f, 0.34f, 0.58f, 0.28f, -0.068f),
            new MonolithLobe(1.13f, 0.48f, 0.49f, 0.27f, 0.138f),
            new MonolithLobe(1.76f, 0.31f, 0.39f, 0.20f, -0.052f),
            new MonolithLobe(2.19f, 0.52f, 0.62f, 0.30f, 0.126f),
            new MonolithLobe(2.91f, 0.38f, 0.48f, 0.24f, -0.061f),
            new MonolithLobe(3.38f, 0.55f, 0.52f, 0.31f, 0.145f),
            new MonolithLobe(4.07f, 0.33f, 0.66f, 0.23f, -0.057f),
            new MonolithLobe(4.52f, 0.46f, 0.43f, 0.26f, 0.122f),
            new MonolithLobe(5.18f, 0.36f, 0.57f, 0.25f, -0.064f),
            new MonolithLobe(5.68f, 0.50f, 0.61f, 0.29f, 0.132f),
            new MonolithLobe(6.05f, 0.28f, 0.36f, 0.19f, -0.046f)
        };

        private static readonly int[,] Tetrahedra =
        {
            { 0, 5, 1, 6 },
            { 0, 1, 2, 6 },
            { 0, 2, 3, 6 },
            { 0, 3, 7, 6 },
            { 0, 7, 4, 6 },
            { 0, 4, 5, 6 }
        };

        static StarterIslandVoxelUnderbodyGenerator()
        {
            EditorApplication.delayCall += DisableBrokenUnderbodyIfRequested;
            EditorApplication.delayCall += RunOneShotIfRequested;
        }

        private static void DisableBrokenUnderbodyIfRequested()
        {
            string markerPath = Path.GetFullPath(DisableBrokenMarker);
            if (!File.Exists(markerPath))
                return;

            foreach (MeshRenderer candidate in
                     Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                Scene scene = candidate.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded ||
                    candidate.name != StarterIslandUnderbodyBuilder.ObjectName)
                {
                    continue;
                }

                Undo.RecordObject(candidate, "Disable rejected terrain underbody");
                candidate.enabled = false;
                EditorUtility.SetDirty(candidate);
                PrefabUtility.RecordPrefabInstancePropertyModifications(candidate);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            File.Delete(markerPath);
            Debug.Log(
                "STARTER_ISLAND_UNDERBODY_REJECTED disabled=1 " +
                "terrainChanged=0 status=PASS");
        }

        [MenuItem(
            "CML/Environment/Terrain Underbody/" +
            "Rebuild Selected Terrain Underbody (Monolithic Rock)")]
        public static void RebuildSelectedTerrainUnderbody()
        {
            var terrain = ResolveTargetTerrain();
            if (terrain == null)
            {
                EditorUtility.DisplayDialog(
                    "Voxel Terrain Underbody",
                    "Select TerrainTop, or open a scene containing it.",
                    "OK");
                return;
            }

            BuildAndApply(terrain);
            Selection.activeGameObject = terrain.gameObject;
        }

        [MenuItem(
            "CML/Environment/Terrain Underbody/" +
            "Rebuild Selected Terrain Underbody (Monolithic Rock)",
            true)]
        private static bool ValidateRebuildSelectedTerrainUnderbody()
        {
            return ResolveTargetTerrain() != null;
        }

        public static Mesh BuildAndApply(Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null)
                throw new ArgumentNullException(nameof(terrain));

            Transform root = terrain.transform.parent;
            if (root == null)
            {
                throw new InvalidOperationException(
                    "TerrainTop must be parented under the island root.");
            }

            Transform underbodyTransform = root.Find(
                StarterIslandUnderbodyBuilder.ObjectName);
            if (underbodyTransform == null)
            {
                var underbodyObject = new GameObject(
                    StarterIslandUnderbodyBuilder.ObjectName);
                Undo.RegisterCreatedObjectUndo(
                    underbodyObject,
                    "Create voxel terrain underbody");
                underbodyTransform = underbodyObject.transform;
                underbodyTransform.SetParent(root, false);
                underbodyTransform.localPosition = Vector3.zero;
                underbodyTransform.localRotation = Quaternion.identity;
                underbodyTransform.localScale = Vector3.one;
            }

            var filter = underbodyTransform.GetComponent<MeshFilter>();
            var renderer = underbodyTransform.GetComponent<MeshRenderer>();
            if (filter == null)
                filter = Undo.AddComponent<MeshFilter>(underbodyTransform.gameObject);
            if (renderer == null)
                renderer = Undo.AddComponent<MeshRenderer>(underbodyTransform.gameObject);

            Mesh rimSource = AssetDatabase.LoadAssetAtPath<Mesh>(
                StarterIslandUnderbodyBuilder.MeshAssetPath);
            if (rimSource == null)
                rimSource = filter.sharedMesh;
            if (rimSource == null)
                throw new InvalidOperationException("No underbody rim source exists.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                UnderbodyMaterialPath);
            if (material == null)
                material = renderer.sharedMaterial;
            if (material == null)
                throw new InvalidOperationException("Underbody material is missing.");

            var rim = ExtractOpenRim(rimSource);
            float averageRimY = AverageHeight(rim);
            float requestedBottomY = Mathf.Min(
                rimSource.bounds.min.y,
                averageRimY - MinimumGeneratedDepth);

            EditorUtility.DisplayProgressBar(
                "Voxel Terrain Underbody",
                "Preparing the signed voxel field...",
                0.03f);

            try
            {
                var context = CreateFieldContext(
                    terrain,
                    root,
                    rim,
                    requestedBottomY);
                var geometry = BuildMonolithicGeometry(context);
                var mesh = SaveGeneratedMesh(geometry, context);
                ValidateGeneratedMesh(mesh, context, rim.Count);

                Undo.RecordObject(filter, "Apply voxel terrain underbody");
                Undo.RecordObject(renderer, "Apply voxel terrain material");
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                RemoveColliders(underbodyTransform.gameObject);
                EditorUtility.SetDirty(filter);
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                if (terrain.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

                Debug.Log(
                    "STARTER_ISLAND_VOXEL_UNDERBODY_BUILD " +
                    $"asset={GeneratedMeshPath} " +
                    $"rim={rim.Count} rings={MonolithRingCount} " +
                    $"vertices={mesh.vertexCount} " +
                    $"triangles={mesh.triangles.Length / 3} " +
                    $"bounds={mesh.bounds} material={material.name} " +
                    "mode=continuous_tapered_monolith openTop=1 " +
                    "terrainChanged=0 collider=0 status=PASS");
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
                var terrain = ResolveTargetTerrain();
                if (terrain == null)
                {
                    Debug.LogError(
                        "[StarterIslandVoxelUnderbodyGenerator] TerrainTop " +
                        "was not found in a loaded scene.");
                    return;
                }

                BuildAndApply(terrain);
                StarterIslandTerrainSetup.RenderUnderbodyView();
                File.Delete(markerPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static Terrain ResolveTargetTerrain()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                var selectedTerrain = selected.GetComponent<Terrain>();
                if (selectedTerrain != null)
                    return selectedTerrain;

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

        private static FieldContext CreateFieldContext(
            Terrain terrain,
            Transform root,
            IReadOnlyList<Vector3> rim,
            float bottomY)
        {
            Bounds rimBounds = CalculateBounds(rim);
            float horizontalStep = Mathf.Max(
                rimBounds.size.x / (GridX - 3f),
                rimBounds.size.z / (GridZ - 3f));
            float padding = horizontalStep * 1.35f;
            float minX = rimBounds.min.x - padding;
            float maxX = rimBounds.max.x + padding;
            float minZ = rimBounds.min.z - padding;
            float maxZ = rimBounds.max.z + padding;

            var ceiling = new float[GridX * GridZ];
            var lateral = new float[GridX * GridZ];
            float maximumCeiling = float.NegativeInfinity;
            float maximumInset = 0f;

            for (int z = 0; z < GridZ; z++)
            {
                float pz = Mathf.Lerp(minZ, maxZ, z / (GridZ - 1f));
                for (int x = 0; x < GridX; x++)
                {
                    float px = Mathf.Lerp(minX, maxX, x / (GridX - 1f));
                    int planeIndex = x + GridX * z;
                    lateral[planeIndex] = SignedDistanceToPolygon(
                        new Vector2(px, pz),
                        rim);
                    maximumInset = Mathf.Max(maximumInset, lateral[planeIndex]);
                    ceiling[planeIndex] = SampleTerrainHeightInRootSpace(
                        terrain,
                        root,
                        px,
                        pz) - TerrainOverlap;
                    maximumCeiling = Mathf.Max(
                        maximumCeiling,
                        ceiling[planeIndex]);
                }
            }

            float minimumY = bottomY - horizontalStep * 1.5f;
            float maximumY = maximumCeiling + horizontalStep * 1.5f;
            var context = new FieldContext(
                terrain,
                root,
                rim,
                rimBounds,
                minX,
                maxX,
                minimumY,
                maximumY,
                minZ,
                maxZ,
                bottomY,
                Mathf.Max(1f, maximumInset),
                ceiling,
                lateral);

            return context;
        }

        private static VoxelGeometry BuildMonolithicGeometry(
            FieldContext context)
        {
            int rimCount = context.Rim.Count;
            int expectedVertices = rimCount * MonolithRingCount + 1;
            int expectedTriangles =
                rimCount * ((MonolithRingCount - 1) * 2 + 1);
            var vertices = new List<Vector3>(expectedVertices);
            var indices = new List<int>(expectedTriangles * 3);
            Vector2 center = CalculatePlanarCentroid(context.Rim);
            Vector2 driftTarget = new Vector2(
                context.RimBounds.size.x * -0.018f,
                context.RimBounds.size.z * 0.022f);
            bool counterClockwise = SignedAreaXZ(context.Rim) > 0f;

            for (int ring = 0; ring < MonolithRingCount; ring++)
            {
                float t = ring / (MonolithRingCount - 0.35f);
                t = Mathf.Clamp(t, 0f, 0.985f);
                float taper = EvaluateMonolithTaper(t);
                float seamFade = SmootherStep(
                    Mathf.InverseLerp(0.025f, 0.17f, t));
                float tipFade = 1f - SmootherStep(
                    Mathf.InverseLerp(0.84f, 0.985f, t));
                float detailEnvelope = seamFade * tipFade;
                Vector2 drift = driftTarget * SmootherStep(t);

                for (int rimIndex = 0; rimIndex < rimCount; rimIndex++)
                {
                    Vector3 source = context.Rim[rimIndex];
                    Vector2 local = new Vector2(
                        source.x - center.x,
                        source.z - center.y);
                    float angle = Mathf.Atan2(local.y, local.x);
                    float normalizedAngle = Mathf.Repeat(
                        angle,
                        Mathf.PI * 2f) /
                        (Mathf.PI * 2f);

                    float broadStriation =
                        Mathf.Sin(angle * 11f + 0.41f) * 0.46f +
                        Mathf.Sin(angle * 17f - 1.17f) * 0.29f +
                        Mathf.Sin(angle * 29f + 2.03f) * 0.16f +
                        Mathf.Sin(angle * 41f - 0.62f) * 0.09f;
                    float coherentRock = Fbm2D(
                        normalizedAngle * 3.2f + 7.4f,
                        t * 1.75f + 13.2f,
                        4);
                    float integratedLobes = EvaluateMonolithLobes(angle, t);
                    float radialVariation = detailEnvelope *
                        (broadStriation * 0.048f +
                         coherentRock * 0.024f +
                         integratedLobes);
                    float radialScale = Mathf.Max(
                        0.018f,
                        taper * (1f + radialVariation));

                    Vector2 xz = center + local * radialScale + drift;
                    float verticalRock = detailEnvelope *
                        (coherentRock * 1.25f +
                         broadStriation * 1.8f);
                    float y = Mathf.Lerp(
                        source.y - 0.08f,
                        context.BottomY,
                        t) + verticalRock;
                    vertices.Add(new Vector3(xz.x, y, xz.y));
                }
            }

            for (int ring = 0; ring < MonolithRingCount - 1; ring++)
            {
                int upperStart = ring * rimCount;
                int lowerStart = (ring + 1) * rimCount;
                for (int rimIndex = 0; rimIndex < rimCount; rimIndex++)
                {
                    int next = (rimIndex + 1) % rimCount;
                    int upper = upperStart + rimIndex;
                    int upperNext = upperStart + next;
                    int lower = lowerStart + rimIndex;
                    int lowerNext = lowerStart + next;
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
            }

            int tip = vertices.Count;
            vertices.Add(
                new Vector3(
                    center.x + driftTarget.x,
                    context.BottomY,
                    center.y + driftTarget.y));
            int finalRingStart = (MonolithRingCount - 1) * rimCount;
            for (int rimIndex = 0; rimIndex < rimCount; rimIndex++)
            {
                int next = (rimIndex + 1) % rimCount;
                if (counterClockwise)
                {
                    indices.Add(finalRingStart + rimIndex);
                    indices.Add(finalRingStart + next);
                    indices.Add(tip);
                }
                else
                {
                    indices.Add(finalRingStart + rimIndex);
                    indices.Add(tip);
                    indices.Add(finalRingStart + next);
                }
            }

            if (vertices.Count != expectedVertices ||
                indices.Count != expectedTriangles * 3)
            {
                throw new InvalidOperationException(
                    "Monolithic underbody produced non-deterministic topology.");
            }

            return new VoxelGeometry(vertices, indices);
        }

        private static float EvaluateMonolithTaper(float t)
        {
            if (t <= 0.08f)
                return Mathf.Lerp(1f, 1.006f, SmootherStep(t / 0.08f));
            if (t <= 0.18f)
            {
                return Mathf.Lerp(
                    1.006f,
                    0.975f,
                    SmootherStep(Mathf.InverseLerp(0.08f, 0.18f, t)));
            }
            if (t <= 0.34f)
            {
                return Mathf.Lerp(
                    0.975f,
                    0.835f,
                    SmootherStep(Mathf.InverseLerp(0.18f, 0.34f, t)));
            }
            if (t <= 0.52f)
            {
                return Mathf.Lerp(
                    0.835f,
                    0.585f,
                    SmootherStep(Mathf.InverseLerp(0.34f, 0.52f, t)));
            }
            if (t <= 0.68f)
            {
                return Mathf.Lerp(
                    0.585f,
                    0.365f,
                    SmootherStep(Mathf.InverseLerp(0.52f, 0.68f, t)));
            }
            if (t <= 0.81f)
            {
                return Mathf.Lerp(
                    0.365f,
                    0.205f,
                    SmootherStep(Mathf.InverseLerp(0.68f, 0.81f, t)));
            }
            if (t <= 0.91f)
            {
                return Mathf.Lerp(
                    0.205f,
                    0.098f,
                    SmootherStep(Mathf.InverseLerp(0.81f, 0.91f, t)));
            }

            return Mathf.Lerp(
                0.098f,
                0.018f,
                SmootherStep(Mathf.InverseLerp(0.91f, 0.985f, t)));
        }

        private static float EvaluateMonolithLobes(float angle, float t)
        {
            float result = 0f;
            for (int index = 0; index < MonolithLobes.Length; index++)
            {
                MonolithLobe lobe = MonolithLobes[index];
                float angleDelta = Mathf.DeltaAngle(
                    angle * Mathf.Rad2Deg,
                    lobe.Angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float angularWeight = Mathf.Exp(
                    -0.5f * angleDelta * angleDelta /
                    (lobe.AngleWidth * lobe.AngleWidth));
                float depthDelta = (t - lobe.DepthCenter) / lobe.DepthWidth;
                // Buttresses remain coherent from the apron toward the keel.
                // The Gaussian only varies their intensity; it never turns
                // them into isolated balls at a single depth.
                float depthWeight =
                    0.68f +
                    Mathf.Exp(-0.5f * depthDelta * depthDelta) * 0.32f;
                result += lobe.Amplitude * angularWeight * depthWeight;
            }

            return result;
        }

        private static Vector2 CalculatePlanarCentroid(
            IReadOnlyList<Vector3> points)
        {
            Vector2 sum = Vector2.zero;
            for (int index = 0; index < points.Count; index++)
                sum += new Vector2(points[index].x, points[index].z);
            return sum / points.Count;
        }

        private static float SignedAreaXZ(IReadOnlyList<Vector3> points)
        {
            float twiceArea = 0f;
            int previous = points.Count - 1;
            for (int current = 0; current < points.Count; current++)
            {
                twiceArea +=
                    points[previous].x * points[current].z -
                    points[current].x * points[previous].z;
                previous = current;
            }

            return twiceArea * 0.5f;
        }

        private static VoxelGeometry BuildVoxelGeometry(FieldContext context)
        {
            int sampleCount = GridX * GridY * GridZ;
            var positions = new Vector3[sampleCount];
            var field = new float[sampleCount];

            for (int z = 0; z < GridZ; z++)
            {
                EditorUtility.DisplayProgressBar(
                    "Voxel Terrain Underbody",
                    "Sampling procedural rock volume...",
                    0.05f + 0.35f * z / (GridZ - 1f));
                float pz = Mathf.Lerp(
                    context.MinZ,
                    context.MaxZ,
                    z / (GridZ - 1f));
                for (int y = 0; y < GridY; y++)
                {
                    float py = Mathf.Lerp(
                        context.MinY,
                        context.MaxY,
                        y / (GridY - 1f));
                    for (int x = 0; x < GridX; x++)
                    {
                        float px = Mathf.Lerp(
                            context.MinX,
                            context.MaxX,
                            x / (GridX - 1f));
                        int index = GridIndex(x, y, z);
                        var position = new Vector3(px, py, pz);
                        positions[index] = position;
                        field[index] = EvaluateField(context, position, x, z);
                    }
                }
            }

            var vertices = new List<Vector3>(120000);
            var indices = new List<int>(300000);
            var edgeVertices = new Dictionary<EdgeKey, int>(180000);
            var cube = new int[8];

            for (int z = 0; z < GridZ - 1; z++)
            {
                EditorUtility.DisplayProgressBar(
                    "Voxel Terrain Underbody",
                    "Remeshing voxels into the final shell...",
                    0.42f + 0.48f * z / (GridZ - 2f));
                for (int y = 0; y < GridY - 1; y++)
                {
                    for (int x = 0; x < GridX - 1; x++)
                    {
                        cube[0] = GridIndex(x, y, z);
                        cube[1] = GridIndex(x + 1, y, z);
                        cube[2] = GridIndex(x + 1, y + 1, z);
                        cube[3] = GridIndex(x, y + 1, z);
                        cube[4] = GridIndex(x, y, z + 1);
                        cube[5] = GridIndex(x + 1, y, z + 1);
                        cube[6] = GridIndex(x + 1, y + 1, z + 1);
                        cube[7] = GridIndex(x, y + 1, z + 1);

                        for (int tetrahedron = 0; tetrahedron < 6; tetrahedron++)
                        {
                            PolygoniseTetrahedron(
                                cube[Tetrahedra[tetrahedron, 0]],
                                cube[Tetrahedra[tetrahedron, 1]],
                                cube[Tetrahedra[tetrahedron, 2]],
                                cube[Tetrahedra[tetrahedron, 3]],
                                positions,
                                field,
                                vertices,
                                indices,
                                edgeVertices);
                        }
                    }
                }
            }

            if (vertices.Count < 1000 || indices.Count < 3000)
                throw new InvalidOperationException("Voxel remesh produced no useful shell.");
            if (vertices.Count > 240000)
            {
                throw new InvalidOperationException(
                    $"Voxel shell produced {vertices.Count} vertices, above " +
                    "the safe single-mesh budget of 240000.");
            }

            return new VoxelGeometry(vertices, indices);
        }

        private static float EvaluateField(
            FieldContext context,
            Vector3 position,
            int gridX,
            int gridZ)
        {
            int planeIndex = gridX + GridX * gridZ;
            float ceiling = context.Ceiling[planeIndex];
            float signedDistance = context.Lateral[planeIndex];
            float topField = ceiling - position.y;
            float belowTop = ceiling - position.y;
            float stableMask = SmootherStep(
                Mathf.InverseLerp(
                    SideStableDepth,
                    SideStableDepth + 18f,
                    belowTop));

            float sideNoise = Fbm2D(
                position.x * 0.038f + Seed * 0.013f,
                position.z * 0.041f - Seed * 0.017f,
                3);
            float lateralField =
                signedDistance + RimOverlap +
                sideNoise * SideStriationAmplitude * stableMask;

            float interior = SmootherStep(
                Mathf.Clamp01(
                    (signedDistance + RimOverlap) /
                    Mathf.Max(1f, context.MaximumInset * 0.72f)));
            float centerShape = Mathf.Pow(interior, 0.68f);
            float bottomNoise = Fbm2D(
                position.x * 0.0105f + 19.37f,
                position.z * 0.0118f - 8.11f,
                4);
            float centralBottom =
                context.BottomY + 30f + bottomNoise * 12f;
            float baseBottom = Mathf.Lerp(
                ceiling - RimThickness,
                centralBottom,
                centerShape);
            float bottomField = position.y - baseBottom;
            float baseVolume = Mathf.Min(
                topField,
                Mathf.Min(lateralField, bottomField));

            float bulgeVolume = float.NegativeInfinity;
            for (int index = 0; index < context.Bulges.Count; index++)
            {
                Bulge bulge = context.Bulges[index];
                float dx = (position.x - bulge.Center.x) / bulge.Radius.x;
                float dy = (position.y - bulge.Center.y) / bulge.Radius.y;
                float dz = (position.z - bulge.Center.z) / bulge.Radius.z;
                float radial = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                float bulgeField =
                    (1f - radial) *
                    Mathf.Min(
                        bulge.Radius.x,
                        Mathf.Min(bulge.Radius.y, bulge.Radius.z));
                bulgeVolume = Mathf.Max(bulgeVolume, bulgeField);
            }

            float volume = Mathf.Max(baseVolume, bulgeVolume);
            volume = Mathf.Min(volume, topField);
            volume = Mathf.Min(volume, lateralField + 6f * stableMask);

            float roughness = Fbm3D(
                position * 0.026f +
                new Vector3(13.1f, -4.7f, 9.3f),
                3);
            volume += roughness * BottomNoiseAmplitude * stableMask;
            return Mathf.Min(volume, topField);
        }

        private static void BuildBulges(FieldContext context)
        {
            var random = new System.Random(Seed);

            AddAuthoredBulge(context, 0.50f, 0.51f, 92f, 78f, 66f);
            AddAuthoredBulge(context, 0.31f, 0.48f, 67f, 58f, 48f);
            AddAuthoredBulge(context, 0.69f, 0.46f, 72f, 62f, 52f);
            AddAuthoredBulge(context, 0.47f, 0.68f, 58f, 52f, 45f);
            AddAuthoredBulge(context, 0.55f, 0.31f, 62f, 50f, 47f);

            int created = 0;
            int attempts = 0;
            while (created < RandomBulgeCount && attempts < 2000)
            {
                attempts++;
                float radiusX = Mathf.Lerp(22f, 58f, NextFloat(random));
                float radiusZ = Mathf.Lerp(20f, 52f, NextFloat(random));
                float radiusY = Mathf.Lerp(16f, 43f, NextFloat(random));
                float x = Mathf.Lerp(
                    context.RimBounds.min.x,
                    context.RimBounds.max.x,
                    NextFloat(random));
                float z = Mathf.Lerp(
                    context.RimBounds.min.z,
                    context.RimBounds.max.z,
                    NextFloat(random));
                float inside = SignedDistanceToPolygon(
                    new Vector2(x, z),
                    context.Rim);
                if (inside < Mathf.Min(radiusX, radiusZ) * 0.32f)
                    continue;

                AddBulgeAt(context, x, z, radiusX, radiusZ, radiusY);
                created++;
            }

            if (created != RandomBulgeCount)
            {
                throw new InvalidOperationException(
                    $"Could create only {created}/{RandomBulgeCount} " +
                    "procedural underbody bulges.");
            }
        }

        private static void AddAuthoredBulge(
            FieldContext context,
            float normalizedX,
            float normalizedZ,
            float radiusX,
            float radiusZ,
            float radiusY)
        {
            float x = Mathf.Lerp(
                context.RimBounds.min.x,
                context.RimBounds.max.x,
                normalizedX);
            float z = Mathf.Lerp(
                context.RimBounds.min.z,
                context.RimBounds.max.z,
                normalizedZ);
            if (SignedDistanceToPolygon(new Vector2(x, z), context.Rim) > 1f)
                AddBulgeAt(context, x, z, radiusX, radiusZ, radiusY);
        }

        private static void AddBulgeAt(
            FieldContext context,
            float x,
            float z,
            float radiusX,
            float radiusZ,
            float radiusY)
        {
            float ceiling = SampleTerrainHeightInRootSpace(
                context.Terrain,
                context.Root,
                x,
                z) - TerrainOverlap;
            float signedDistance = SignedDistanceToPolygon(
                new Vector2(x, z),
                context.Rim);
            float interior = SmootherStep(
                Mathf.Clamp01(
                    signedDistance /
                    Mathf.Max(1f, context.MaximumInset * 0.72f)));
            float baseBottom = Mathf.Lerp(
                ceiling - RimThickness,
                context.BottomY + 34f,
                Mathf.Pow(interior, 0.68f));
            float centerY = Mathf.Max(
                context.BottomY + radiusY,
                baseBottom - radiusY * 0.38f);
            context.Bulges.Add(
                new Bulge(
                    new Vector3(x, centerY, z),
                    new Vector3(radiusX, radiusY, radiusZ)));
        }

        private static void PolygoniseTetrahedron(
            int id0,
            int id1,
            int id2,
            int id3,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<float> field,
            List<Vector3> vertices,
            List<int> indices,
            Dictionary<EdgeKey, int> edgeVertices)
        {
            int inside0 = -1;
            int inside1 = -1;
            int inside2 = -1;
            int inside3 = -1;
            int outside0 = -1;
            int outside1 = -1;
            int outside2 = -1;
            int outside3 = -1;
            int insideCount = 0;
            int outsideCount = 0;

            Classify(
                id0,
                field[id0] >= 0f,
                ref insideCount,
                ref inside0,
                ref inside1,
                ref inside2,
                ref inside3,
                ref outsideCount,
                ref outside0,
                ref outside1,
                ref outside2,
                ref outside3);
            Classify(
                id1,
                field[id1] >= 0f,
                ref insideCount,
                ref inside0,
                ref inside1,
                ref inside2,
                ref inside3,
                ref outsideCount,
                ref outside0,
                ref outside1,
                ref outside2,
                ref outside3);
            Classify(
                id2,
                field[id2] >= 0f,
                ref insideCount,
                ref inside0,
                ref inside1,
                ref inside2,
                ref inside3,
                ref outsideCount,
                ref outside0,
                ref outside1,
                ref outside2,
                ref outside3);
            Classify(
                id3,
                field[id3] >= 0f,
                ref insideCount,
                ref inside0,
                ref inside1,
                ref inside2,
                ref inside3,
                ref outsideCount,
                ref outside0,
                ref outside1,
                ref outside2,
                ref outside3);

            if (insideCount == 0 || insideCount == 4)
                return;

            Vector3 insideReference = Vector3.zero;
            if (insideCount > 0) insideReference += positions[inside0];
            if (insideCount > 1) insideReference += positions[inside1];
            if (insideCount > 2) insideReference += positions[inside2];
            if (insideCount > 3) insideReference += positions[inside3];
            insideReference /= insideCount;

            if (insideCount == 1)
            {
                int a = GetEdgeVertex(
                    inside0, outside0, positions, field, vertices, edgeVertices);
                int b = GetEdgeVertex(
                    inside0, outside1, positions, field, vertices, edgeVertices);
                int c = GetEdgeVertex(
                    inside0, outside2, positions, field, vertices, edgeVertices);
                AddOrientedTriangle(
                    a, b, c, insideReference, vertices, indices);
                return;
            }

            if (insideCount == 3)
            {
                int a = GetEdgeVertex(
                    outside0, inside0, positions, field, vertices, edgeVertices);
                int b = GetEdgeVertex(
                    outside0, inside1, positions, field, vertices, edgeVertices);
                int c = GetEdgeVertex(
                    outside0, inside2, positions, field, vertices, edgeVertices);
                AddOrientedTriangle(
                    a, b, c, insideReference, vertices, indices);
                return;
            }

            int q0 = GetEdgeVertex(
                inside0, outside0, positions, field, vertices, edgeVertices);
            int q1 = GetEdgeVertex(
                inside0, outside1, positions, field, vertices, edgeVertices);
            int q2 = GetEdgeVertex(
                inside1, outside0, positions, field, vertices, edgeVertices);
            int q3 = GetEdgeVertex(
                inside1, outside1, positions, field, vertices, edgeVertices);
            AddOrientedTriangle(
                q0, q1, q2, insideReference, vertices, indices);
            AddOrientedTriangle(
                q1, q3, q2, insideReference, vertices, indices);
        }

        private static void Classify(
            int id,
            bool inside,
            ref int insideCount,
            ref int inside0,
            ref int inside1,
            ref int inside2,
            ref int inside3,
            ref int outsideCount,
            ref int outside0,
            ref int outside1,
            ref int outside2,
            ref int outside3)
        {
            if (inside)
            {
                AssignSlot(
                    insideCount++,
                    id,
                    ref inside0,
                    ref inside1,
                    ref inside2,
                    ref inside3);
            }
            else
            {
                AssignSlot(
                    outsideCount++,
                    id,
                    ref outside0,
                    ref outside1,
                    ref outside2,
                    ref outside3);
            }
        }

        private static void AssignSlot(
            int slot,
            int value,
            ref int value0,
            ref int value1,
            ref int value2,
            ref int value3)
        {
            switch (slot)
            {
                case 0: value0 = value; break;
                case 1: value1 = value; break;
                case 2: value2 = value; break;
                case 3: value3 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static int GetEdgeVertex(
            int idA,
            int idB,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<float> field,
            List<Vector3> vertices,
            Dictionary<EdgeKey, int> edgeVertices)
        {
            var key = new EdgeKey(idA, idB);
            if (edgeVertices.TryGetValue(key, out int existing))
                return existing;

            float valueA = field[idA];
            float valueB = field[idB];
            float denominator = valueA - valueB;
            float interpolation =
                Mathf.Abs(denominator) < 0.000001f
                    ? 0.5f
                    : Mathf.Clamp01(valueA / denominator);
            var position = Vector3.Lerp(
                positions[idA],
                positions[idB],
                interpolation);
            int index = vertices.Count;
            vertices.Add(position);
            edgeVertices.Add(key, index);
            return index;
        }

        private static void AddOrientedTriangle(
            int a,
            int b,
            int c,
            Vector3 insideReference,
            IReadOnlyList<Vector3> vertices,
            List<int> indices)
        {
            if (a == b || b == c || c == a)
                return;
            Vector3 va = vertices[a];
            Vector3 vb = vertices[b];
            Vector3 vc = vertices[c];
            Vector3 normal = Vector3.Cross(vb - va, vc - va);
            if (normal.sqrMagnitude < 0.0000001f)
                return;
            Vector3 centroid = (va + vb + vc) / 3f;
            if (Vector3.Dot(normal, insideReference - centroid) > 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        private static Mesh SaveGeneratedMesh(
            VoxelGeometry geometry,
            FieldContext context)
        {
            EnsureFolder(Path.GetDirectoryName(GeneratedMeshPath));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GeneratedMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "MESH_StarterIsland_Underbody_Voxel"
                };
                AssetDatabase.CreateAsset(mesh, GeneratedMeshPath);
            }
            else
            {
                mesh.Clear(false);
                mesh.name = "MESH_StarterIsland_Underbody_Voxel";
            }

            mesh.indexFormat =
                geometry.Vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
            mesh.SetVertices(geometry.Vertices);
            mesh.SetTriangles(geometry.Indices, 0, true);

            var uv = new List<Vector2>(geometry.Vertices.Count);
            var colors = new List<Color32>(geometry.Vertices.Count);
            float heightRange = Mathf.Max(1f, context.MaxY - context.BottomY);
            for (int index = 0; index < geometry.Vertices.Count; index++)
            {
                Vector3 vertex = geometry.Vertices[index];
                uv.Add(
                    new Vector2(
                        Mathf.InverseLerp(context.MinX, context.MaxX, vertex.x),
                        Mathf.InverseLerp(context.MinZ, context.MaxZ, vertex.z)));
                float height = Mathf.Clamp01(
                    (vertex.y - context.BottomY) / heightRange);
                byte blend = (byte)Mathf.RoundToInt(Mathf.Lerp(104f, 218f, height));
                byte wet = (byte)Mathf.RoundToInt(Mathf.Lerp(92f, 22f, height));
                colors.Add(new Color32(blend, blend, blend, wet));
            }

            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static void ValidateGeneratedMesh(
            Mesh mesh,
            FieldContext context,
            int rimCount)
        {
            if (mesh == null || mesh.vertexCount < 1000)
                throw new InvalidOperationException("Generated voxel mesh is empty.");
            if (mesh.triangles.Length < 3000 || mesh.triangles.Length % 3 != 0)
                throw new InvalidOperationException("Generated voxel indices are invalid.");
            if (mesh.bounds.min.y > context.BottomY + 18f)
            {
                throw new InvalidOperationException(
                    "Generated voxel shell does not reach its intended lower mass.");
            }
            if (mesh.bounds.size.x < context.RimBounds.size.x * 0.92f ||
                mesh.bounds.size.z < context.RimBounds.size.z * 0.92f)
            {
                throw new InvalidOperationException(
                    "Generated voxel shell no longer covers the Terrain rim.");
            }
            if (rimCount < 24)
                throw new InvalidOperationException("Source rim is too small.");

            Vector3[] vertices = mesh.vertices;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index];
                if (float.IsNaN(vertex.x) || float.IsInfinity(vertex.x) ||
                    float.IsNaN(vertex.y) || float.IsInfinity(vertex.y) ||
                    float.IsNaN(vertex.z) || float.IsInfinity(vertex.z))
                {
                    throw new InvalidOperationException(
                        $"Generated voxel vertex {index} is not finite.");
                }
            }
        }

        private static List<Vector3> ExtractOpenRim(Mesh source)
        {
            Vector3[] vertices = source.vertices;
            int[] triangles = source.triangles;
            var welded = new Dictionary<QuantizedPosition, int>();
            var weldedPositions = new List<Vector3>();
            var weldedIds = new int[vertices.Length];

            for (int index = 0; index < vertices.Length; index++)
            {
                var key = new QuantizedPosition(vertices[index]);
                if (!welded.TryGetValue(key, out int id))
                {
                    id = weldedPositions.Count;
                    welded.Add(key, id);
                    weldedPositions.Add(vertices[index]);
                }

                weldedIds[index] = id;
            }

            var edgeUse = new Dictionary<LogicalEdge, int>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                AddEdgeUse(edgeUse, weldedIds[triangles[index]], weldedIds[triangles[index + 1]]);
                AddEdgeUse(edgeUse, weldedIds[triangles[index + 1]], weldedIds[triangles[index + 2]]);
                AddEdgeUse(edgeUse, weldedIds[triangles[index + 2]], weldedIds[triangles[index]]);
            }

            var adjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<LogicalEdge, int> pair in edgeUse)
            {
                if (pair.Value != 1)
                    continue;
                AddNeighbour(adjacency, pair.Key.A, pair.Key.B);
                AddNeighbour(adjacency, pair.Key.B, pair.Key.A);
            }

            if (adjacency.Count < 24)
            {
                throw new InvalidOperationException(
                    "The source underbody has no usable open Terrain rim.");
            }

            foreach (KeyValuePair<int, List<int>> pair in adjacency)
            {
                if (pair.Value.Count != 2)
                {
                    throw new InvalidOperationException(
                        "The source rim is branching or non-manifold.");
                }
            }

            int start = -1;
            foreach (int vertex in adjacency.Keys)
            {
                if (start < 0 || weldedPositions[vertex].x < weldedPositions[start].x)
                    start = vertex;
            }

            var rim = new List<Vector3>(adjacency.Count);
            int previous = -1;
            int current = start;
            do
            {
                rim.Add(weldedPositions[current]);
                List<int> neighbours = adjacency[current];
                int next = neighbours[0] != previous ? neighbours[0] : neighbours[1];
                previous = current;
                current = next;
                if (rim.Count > adjacency.Count + 1)
                    throw new InvalidOperationException("The source rim loop never closed.");
            }
            while (current != start);

            if (rim.Count != adjacency.Count)
            {
                throw new InvalidOperationException(
                    "The source underbody contains multiple open boundary loops.");
            }

            return rim;
        }

        private static void AddEdgeUse(
            IDictionary<LogicalEdge, int> edges,
            int a,
            int b)
        {
            var edge = new LogicalEdge(a, b);
            edges.TryGetValue(edge, out int count);
            edges[edge] = count + 1;
        }

        private static void AddNeighbour(
            IDictionary<int, List<int>> adjacency,
            int from,
            int to)
        {
            if (!adjacency.TryGetValue(from, out List<int> neighbours))
            {
                neighbours = new List<int>(2);
                adjacency.Add(from, neighbours);
            }

            if (!neighbours.Contains(to))
                neighbours.Add(to);
        }

        private static float SampleTerrainHeightInRootSpace(
            Terrain terrain,
            Transform root,
            float rootX,
            float rootZ)
        {
            Vector3 worldProbe = root.TransformPoint(
                new Vector3(rootX, 0f, rootZ));
            Vector3 terrainLocal = terrain.transform.InverseTransformPoint(worldProbe);
            TerrainData data = terrain.terrainData;
            float normalizedX = Mathf.Clamp01(terrainLocal.x / data.size.x);
            float normalizedZ = Mathf.Clamp01(terrainLocal.z / data.size.z);
            float height = data.GetInterpolatedHeight(normalizedX, normalizedZ);
            Vector3 worldSurface = terrain.transform.TransformPoint(
                new Vector3(terrainLocal.x, height, terrainLocal.z));
            return root.InverseTransformPoint(worldSurface).y;
        }

        private static float SignedDistanceToPolygon(
            Vector2 point,
            IReadOnlyList<Vector3> polygon)
        {
            bool inside = false;
            float minimumSquaredDistance = float.PositiveInfinity;
            int previous = polygon.Count - 1;
            for (int current = 0; current < polygon.Count; current++)
            {
                var a = new Vector2(polygon[previous].x, polygon[previous].z);
                var b = new Vector2(polygon[current].x, polygon[current].z);
                Vector2 segment = b - a;
                float lengthSquared = Mathf.Max(0.000001f, segment.sqrMagnitude);
                float progress = Mathf.Clamp01(
                    Vector2.Dot(point - a, segment) / lengthSquared);
                minimumSquaredDistance = Mathf.Min(
                    minimumSquaredDistance,
                    (point - (a + segment * progress)).sqrMagnitude);

                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float crossingX =
                        (b.x - a.x) * (point.y - a.y) /
                        (b.y - a.y) + a.x;
                    if (point.x < crossingX)
                        inside = !inside;
                }

                previous = current;
            }

            float distance = Mathf.Sqrt(minimumSquaredDistance);
            return inside ? distance : -distance;
        }

        private static float Fbm2D(float x, float y, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.58f;
            float normalization = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                float noise = Mathf.PerlinNoise(x, y) * 2f - 1f;
                sum += noise * amplitude;
                normalization += amplitude;
                x = x * 2.031f + 11.17f;
                y = y * 2.017f - 7.43f;
                amplitude *= 0.5f;
            }

            return normalization > 0f ? sum / normalization : 0f;
        }

        private static float Fbm3D(Vector3 point, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.58f;
            float normalization = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                float xy = Mathf.PerlinNoise(point.x, point.y);
                float yz = Mathf.PerlinNoise(point.y + 31.7f, point.z - 19.3f);
                float xz = Mathf.PerlinNoise(point.x - 47.1f, point.z + 13.9f);
                float noise = ((xy + yz + xz) / 3f) * 2f - 1f;
                sum += noise * amplitude;
                normalization += amplitude;
                point = point * 2.021f +
                        new Vector3(7.13f, -11.71f, 5.37f);
                amplitude *= 0.5f;
            }

            return normalization > 0f ? sum / normalization : 0f;
        }

        private static float SmootherStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value *
                   (value * (value * 6f - 15f) + 10f);
        }

        private static float NextFloat(System.Random random)
        {
            return (float)random.NextDouble();
        }

        private static int GridIndex(int x, int y, int z)
        {
            return x + GridX * (y + GridY * z);
        }

        private static float AverageHeight(IReadOnlyList<Vector3> points)
        {
            float sum = 0f;
            for (int index = 0; index < points.Count; index++)
                sum += points[index].y;
            return sum / points.Count;
        }

        private static Bounds CalculateBounds(IReadOnlyList<Vector3> points)
        {
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int index = 1; index < points.Count; index++)
                bounds.Encapsulate(points[index]);
            return bounds;
        }

        private static void RemoveColliders(GameObject target)
        {
            foreach (Collider collider in target.GetComponents<Collider>())
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder))
                throw new ArgumentException("Asset folder is empty.");
            assetFolder = assetFolder.Replace('\\', '/');
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

        private sealed class FieldContext
        {
            public readonly Terrain Terrain;
            public readonly Transform Root;
            public readonly IReadOnlyList<Vector3> Rim;
            public readonly Bounds RimBounds;
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float MinY;
            public readonly float MaxY;
            public readonly float MinZ;
            public readonly float MaxZ;
            public readonly float BottomY;
            public readonly float MaximumInset;
            public readonly float[] Ceiling;
            public readonly float[] Lateral;
            public readonly List<Bulge> Bulges = new List<Bulge>();

            public FieldContext(
                Terrain terrain,
                Transform root,
                IReadOnlyList<Vector3> rim,
                Bounds rimBounds,
                float minX,
                float maxX,
                float minY,
                float maxY,
                float minZ,
                float maxZ,
                float bottomY,
                float maximumInset,
                float[] ceiling,
                float[] lateral)
            {
                Terrain = terrain;
                Root = root;
                Rim = rim;
                RimBounds = rimBounds;
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
                MinZ = minZ;
                MaxZ = maxZ;
                BottomY = bottomY;
                MaximumInset = maximumInset;
                Ceiling = ceiling;
                Lateral = lateral;
            }
        }

        private readonly struct Bulge
        {
            public readonly Vector3 Center;
            public readonly Vector3 Radius;

            public Bulge(Vector3 center, Vector3 radius)
            {
                Center = center;
                Radius = radius;
            }
        }

        private readonly struct MonolithLobe
        {
            public readonly float Angle;
            public readonly float AngleWidth;
            public readonly float DepthCenter;
            public readonly float DepthWidth;
            public readonly float Amplitude;

            public MonolithLobe(
                float angle,
                float angleWidth,
                float depthCenter,
                float depthWidth,
                float amplitude)
            {
                Angle = angle;
                AngleWidth = angleWidth;
                DepthCenter = depthCenter;
                DepthWidth = depthWidth;
                Amplitude = amplitude;
            }
        }

        private sealed class VoxelGeometry
        {
            public readonly List<Vector3> Vertices;
            public readonly List<int> Indices;

            public VoxelGeometry(List<Vector3> vertices, List<int> indices)
            {
                Vertices = vertices;
                Indices = indices;
            }
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
            }

            public bool Equals(EdgeKey other)
            {
                return A == other.A && B == other.B;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A * 397) ^ B;
                }
            }
        }

        private readonly struct LogicalEdge : IEquatable<LogicalEdge>
        {
            public readonly int A;
            public readonly int B;

            public LogicalEdge(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
            }

            public bool Equals(LogicalEdge other)
            {
                return A == other.A && B == other.B;
            }

            public override bool Equals(object obj)
            {
                return obj is LogicalEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A * 397) ^ B;
                }
            }
        }

        private readonly struct QuantizedPosition : IEquatable<QuantizedPosition>
        {
            private readonly int x;
            private readonly int y;
            private readonly int z;

            public QuantizedPosition(Vector3 position)
            {
                x = Mathf.RoundToInt(position.x * PositionWeldScale);
                y = Mathf.RoundToInt(position.y * PositionWeldScale);
                z = Mathf.RoundToInt(position.z * PositionWeldScale);
            }

            public bool Equals(QuantizedPosition other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is QuantizedPosition other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = hash * 397 ^ y;
                    hash = hash * 397 ^ z;
                    return hash;
                }
            }
        }
    }
}

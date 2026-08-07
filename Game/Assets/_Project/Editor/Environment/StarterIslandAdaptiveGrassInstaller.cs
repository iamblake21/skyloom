using System;
using System.Collections.Generic;
using System.IO;
using CML.Unity.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Installs the Starter Island grass as native Unity Terrain details.
    /// The operation edits TerrainData and supporting assets in place; it
    /// never rebuilds or saves a scene.
    /// </summary>
    public static class StarterIslandAdaptiveGrassInstaller
    {
        internal const int DetailResolution = 512;
        internal const int DetailResolutionPerPatch = 16;
        internal const string GrassShaderName =
            "CML/Environment/Starter Island Ground Detail";

        private const string TerrainDataPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Data/TD_StarterIsland.asset";
        private const string TerrainPrefabPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Prefabs/PF_StarterIsland_Terrain.prefab";
        private const string AdaptiveRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "AdaptiveGrass";
        private const string MeshRoot = AdaptiveRoot + "/Meshes";
        private const string PrefabRoot = AdaptiveRoot + "/Prefabs";
        private const string MaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_GroundDetail.mat";
        private const string GrassMeshAPath =
            MeshRoot + "/MD_TerrainGrass_Carpet_A.asset";
        private const string GrassMeshBPath =
            MeshRoot + "/MD_TerrainGrass_Carpet_B.asset";
        private const string GrassPrefabAPath =
            PrefabRoot + "/PF_TerrainDetail_AdaptiveGrass_A.prefab";
        private const string GrassPrefabBPath =
            PrefabRoot + "/PF_TerrainDetail_AdaptiveGrass_B.prefab";
        private const string BladeMaskPath =
            "Assets/Proxy Games/Stylized Nature Kit Lite/Textures/" +
            "Grass.png";
        private const string FoliageMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Foliage/" +
            "Materials/M_StarterIsland_FoliageAtlas.mat";
        private const string FlowerWhiteModelPath =
            "Assets/_Project/Art/Environment/StarterIsland/Foliage/" +
            "Models/ENV_FlowerPatch_White_A.fbx";
        private const string FlowerOrangeModelPath =
            "Assets/_Project/Art/Environment/StarterIsland/Foliage/" +
            "Models/ENV_FlowerPatch_Orange_B.fbx";
        private const string FlowerWhiteMeshPath =
            MeshRoot + "/MD_TerrainFlower_White_Upright.asset";
        private const string FlowerOrangeMeshPath =
            MeshRoot + "/MD_TerrainFlower_Orange_Upright.asset";
        private const string FlowerWhitePrefabPath =
            PrefabRoot + "/PF_TerrainDetail_UprightFlower_White.prefab";
        private const string FlowerOrangePrefabPath =
            PrefabRoot + "/PF_TerrainDetail_UprightFlower_Orange.prefab";
        private const string LegacyGroundCoverName = "GroundCover_Chunked";
        private const string LegacyGroundDetailName = "GroundDetailRoot";
        private const string OneShotMarker =
            "Temp/CML_InstallAdaptiveGrass.once";

        [InitializeOnLoadMethod]
        private static void QueueOneShotInstall()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            var markerPath = Path.Combine(projectRoot, OneShotMarker);
            if (!File.Exists(markerPath))
            {
                return;
            }

            File.Delete(markerPath);
            EditorApplication.delayCall += RunOneShotWhenReady;
        }

        private static void RunOneShotWhenReady()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += RunOneShotWhenReady;
                return;
            }

            try
            {
                Install(forceReseedGrass: false);
                StarterIslandAdaptiveGrassReviewCapture.Run();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("CML/Art/Install Paintable Adaptive Terrain Grass")]
        public static void InstallFromMenu()
        {
            Install(forceReseedGrass: false);
        }

        [MenuItem("CML/Art/Reseed Adaptive Grass From Terrain Surface")]
        public static void ReseedFromMenu()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reseed adaptive Terrain grass?",
                    "This replaces only the two adaptive grass detail maps. " +
                    "Terrain heights, painted surface layers, flowers, " +
                    "materials and the scene are preserved.",
                    "Reseed Grass",
                    "Cancel"))
            {
                return;
            }

            Install(forceReseedGrass: true);
        }

        /// <summary>
        /// Batch-mode entry point used by automated installation and QA.
        /// </summary>
        public static void RunBatch()
        {
            Install(forceReseedGrass: false);
        }

        internal static void ConfigureTerrainDataForBuild(TerrainData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var assets = BuildRequiredAssets();
            ConfigureTerrainData(
                data,
                assets,
                forceReseedGrass: true,
                registerUndo: false);
        }

        private static void Install(bool forceReseedGrass)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(
                TerrainDataPath);
            if (data == null)
            {
                throw new FileNotFoundException(
                    "Starter Island TerrainData is missing.",
                    TerrainDataPath);
            }

            var assets = BuildRequiredAssets();
            var reseedGrass = forceReseedGrass || assets.RequiresReseed;
            ConfigureTerrainData(
                data,
                assets,
                reseedGrass,
                registerUndo: !Application.isBatchMode);
            var removedLegacyRoots = PatchTerrainPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            TerrainSurfaceBlendGlobals.BindActiveTerrain();

            var grassA = data.GetDetailLayer(
                0,
                0,
                data.detailResolution,
                data.detailResolution,
                0);
            var grassB = data.GetDetailLayer(
                0,
                0,
                data.detailResolution,
                data.detailResolution,
                1);
            Debug.Log(
                "ADAPTIVE_TERRAIN_GRASS_INSTALL status=PASS " +
                $"resolution={data.detailResolution} " +
                $"prototypes={data.detailPrototypes.Length} " +
                $"grassInstances={CountMap(grassA) + CountMap(grassB)} " +
                $"legacyPrefabRootsRemoved={removedLegacyRoots} " +
                $"reseeded={reseedGrass} " +
                "sceneWrites=0 heightWrites=0 alphamapWrites=0 " +
                "materialShaderWrites=adaptive-only");
        }

        private static AdaptiveAssets BuildRequiredAssets()
        {
            EnsureFolder(AdaptiveRoot);
            EnsureFolder(MeshRoot);
            EnsureFolder(PrefabRoot);

            var material = BuildGrassMaterial();
            var meshA = BuildGrassMesh(
                GrassMeshAPath,
                variant: 0,
                cardCount: 5,
                out var rebuiltA);
            var meshB = BuildGrassMesh(
                GrassMeshBPath,
                variant: 1,
                cardCount: 4,
                out var rebuiltB);
            var grassA = BuildGrassPrefab(
                GrassPrefabAPath,
                meshA,
                material);
            var grassB = BuildGrassPrefab(
                GrassPrefabBPath,
                meshB,
                material);
            var foliageMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    FoliageMaterialPath);
            if (foliageMaterial == null)
            {
                throw new FileNotFoundException(
                    "Starter Island foliage material is missing.",
                    FoliageMaterialPath);
            }

            var flowerWhite = BuildUprightFlowerDetail(
                FlowerWhiteModelPath,
                FlowerWhiteMeshPath,
                FlowerWhitePrefabPath,
                foliageMaterial);
            var flowerOrange = BuildUprightFlowerDetail(
                FlowerOrangeModelPath,
                FlowerOrangeMeshPath,
                FlowerOrangePrefabPath,
                foliageMaterial);

            return new AdaptiveAssets(
                grassA,
                grassB,
                flowerWhite,
                flowerOrange,
                rebuiltA || rebuiltB);
        }

        private static Material BuildGrassMaterial()
        {
            var shader = Shader.Find(GrassShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Adaptive grass shader is unavailable: " +
                    $"{GrassShaderName}");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_GroundDetail"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.enableInstancing = true;
            var bladeMask = AssetDatabase.LoadAssetAtPath<Texture2D>(
                BladeMaskPath);
            if (bladeMask == null)
            {
                throw new FileNotFoundException(
                    "Adaptive grass silhouette mask is missing.",
                    BladeMaskPath);
            }

            if (material.HasProperty("_BladeMask"))
            {
                material.SetTexture("_BladeMask", bladeMask);
            }

            SetColor(
                material,
                "_FallbackColor",
                new Color(0.29f, 0.48f, 0.20f, 1f));
            SetFloat(material, "_Cutoff", 0.38f);
            SetFloat(material, "_RootBrightness", 0.96f);
            SetFloat(material, "_TipBrightness", 1.025f);
            SetFloat(material, "_WindStrength", 0.022f);
            SetFloat(material, "_WindSpeed", 1.38f);
            SetFloat(material, "_GustStrength", 0.28f);
            SetFloat(material, "_AmbientStrength", 0.66f);
            SetFloat(material, "_ShadowFloor", 0.16f);
            SetFloat(material, "_ShadowAttenuationFloor", 0.08f);
            SetFloat(material, "_TerrainMacroVariation", 0.13f);
            SetFloat(material, "_FarMatchStart", 24f);
            SetFloat(material, "_FarMatchEnd", 54f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh BuildGrassMesh(
            string assetPath,
            int variant,
            int cardCount,
            out bool requiresReseed)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            var expectedName = Path.GetFileNameWithoutExtension(assetPath);
            requiresReseed = mesh == null ||
                mesh.vertexCount != cardCount * 8 ||
                mesh.triangles.Length != cardCount * 18;
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = expectedName
                };
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            else
            {
                mesh.Clear();
                mesh.name = expectedName;
            }

            const int verticesPerCard = 8;
            const int indicesPerCard = 18;
            var vertices = new List<Vector3>(
                cardCount * verticesPerCard);
            var normals = new List<Vector3>(
                cardCount * verticesPerCard);
            var colors = new List<Color>(
                cardCount * verticesPerCard);
            var uv = new List<Vector2>(
                cardCount * verticesPerCard);
            var indices = new List<int>(
                cardCount * indicesPerCard);
            for (var card = 0; card < cardCount; card++)
            {
                AppendGrassCard(
                    variant,
                    card,
                    cardCount,
                    vertices,
                    normals,
                    colors,
                    uv,
                    indices);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0.12f, 0.04f, 0.12f));
            mesh.bounds = bounds;
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void AppendGrassCard(
            int variant,
            int card,
            int cardCount,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color> colors,
            ICollection<Vector2> uv,
            ICollection<int> indices)
        {
            var hash = Hash01(card, variant, 0x45D9);
            var secondHash = Hash01(card, variant, 0xA361);
            var angle = (
                card * (180f / cardCount) +
                variant * 19f +
                (hash - 0.5f) * 16f) * Mathf.Deg2Rad;
            var widthDirection = new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle));
            var centerAngle = (
                card * 137.50776f +
                variant * 47f) * Mathf.Deg2Rad;
            var centerRadius = Mathf.Lerp(0.008f, 0.045f, secondHash);
            var center = new Vector3(
                Mathf.Cos(centerAngle) * centerRadius,
                0f,
                Mathf.Sin(centerAngle) * centerRadius);
            var commonLeanAngle = (variant * 73f + 28f) * Mathf.Deg2Rad;
            var commonLean = new Vector3(
                Mathf.Cos(commonLeanAngle),
                0f,
                Mathf.Sin(commonLeanAngle));
            var cardLean = Vector3.Normalize(
                commonLean * 0.82f +
                new Vector3(
                    Mathf.Cos(centerAngle),
                    0f,
                    Mathf.Sin(centerAngle)) * 0.18f);
            var widthRange = variant == 0
                ? new Vector2(0.29f, 0.37f)
                : new Vector2(0.24f, 0.32f);
            var heightRange = variant == 0
                ? new Vector2(0.115f, 0.165f)
                : new Vector2(0.078f, 0.125f);
            var width = Mathf.Lerp(widthRange.x, widthRange.y, hash);
            var height = Mathf.Lerp(
                heightRange.x,
                heightRange.y,
                Hash01(card, variant, 0x7F4A));
            var lean = Mathf.Lerp(
                variant == 0 ? 0.026f : 0.018f,
                variant == 0 ? 0.048f : 0.038f,
                Hash01(card, variant, 0xC2B2));
            var faceNormal = Vector3.Normalize(new Vector3(
                -widthDirection.z,
                0.82f,
                widthDirection.x));
            var phase = Hash01(card, variant, 0x1656);
            var flexibility = Mathf.Lerp(
                0.38f,
                0.82f,
                Hash01(card, variant, 0xD3A2));
            var flipUv = Hash01(card, variant, 0xB529) > 0.5f;
            var baseIndex = vertices.Count;
            var levels = new[] { 0f, 0.38f, 0.72f, 1f };
            for (var levelIndex = 0;
                 levelIndex < levels.Length;
                 levelIndex++)
            {
                var level = levels[levelIndex];
                var curve = level * level * (3f - 2f * level);
                var halfWidth = width * 0.5f *
                    Mathf.Lerp(1f, 0.94f, level);
                var levelCenter = center +
                    cardLean * lean * curve +
                    Vector3.up * height * level;
                vertices.Add(
                    levelCenter - widthDirection * halfWidth);
                vertices.Add(
                    levelCenter + widthDirection * halfWidth);
                normals.Add(faceNormal);
                normals.Add(faceNormal);
                colors.Add(new Color(level, phase, flexibility, 1f));
                colors.Add(new Color(level, phase, flexibility, 1f));
                uv.Add(new Vector2(flipUv ? 1f : 0f, level));
                uv.Add(new Vector2(flipUv ? 0f : 1f, level));
            }

            for (var segment = 0; segment < 3; segment++)
            {
                var bottom = baseIndex + segment * 2;
                var top = bottom + 2;
                indices.Add(bottom);
                indices.Add(top);
                indices.Add(bottom + 1);
                indices.Add(bottom + 1);
                indices.Add(top);
                indices.Add(top + 1);
            }
        }

        private static GameObject BuildGrassPrefab(
            string assetPath,
            Mesh mesh,
            Material material)
        {
            var temporary = new GameObject(
                Path.GetFileNameWithoutExtension(assetPath));
            try
            {
                temporary.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = temporary.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    temporary,
                    assetPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not create adaptive grass prefab: " +
                        $"{assetPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        private static GameObject BuildUprightFlowerDetail(
            string sourceModelPath,
            string meshAssetPath,
            string prefabAssetPath,
            Material material)
        {
            var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                sourceModelPath);
            if (sourceRoot == null)
            {
                throw new FileNotFoundException(
                    "Authored flower model is missing.",
                    sourceModelPath);
            }

            var combined = new Mesh
            {
                name = Path.GetFileNameWithoutExtension(meshAssetPath),
                indexFormat = IndexFormat.UInt32
            };
            try
            {
                var combines = new List<CombineInstance>();
                foreach (var filter in
                         sourceRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    var sourceMesh = filter.sharedMesh;
                    var sourceRenderer = filter.GetComponent<MeshRenderer>();
                    if (sourceMesh == null ||
                        sourceRenderer == null ||
                        !sourceRenderer.enabled ||
                        string.Equals(
                            sourceMesh.name,
                            "Cube",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Keep the complete imported-model matrix. In these FBX
                    // files it contains the -90 degree X axis correction that
                    // the old identity wrapper accidentally discarded.
                    var transform = filter.transform.localToWorldMatrix;
                    for (var subMesh = 0;
                         subMesh < sourceMesh.subMeshCount;
                         subMesh++)
                    {
                        combines.Add(new CombineInstance
                        {
                            mesh = sourceMesh,
                            subMeshIndex = subMesh,
                            transform = transform
                        });
                    }
                }

                if (combines.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"No renderable flower mesh was found below " +
                        $"{sourceModelPath}.");
                }

                combined.CombineMeshes(
                    combines.ToArray(),
                    mergeSubMeshes: true,
                    useMatrices: true,
                    hasLightmapData: false);
                combined.RecalculateBounds();

                // The FBX axis correction lives on intermediate transforms.
                // Baking those matrices preserves the authored upright pose.
                // Moving min Y to zero gives Terrain an exact ground pivot
                // while preserving the authored horizontal placement pivot.
                var sourceBounds = combined.bounds;
                var offset = new Vector3(
                    0f,
                    -sourceBounds.min.y,
                    0f);
                var vertices = combined.vertices;
                for (var index = 0; index < vertices.Length; index++)
                {
                    vertices[index] += offset;
                }

                combined.vertices = vertices;
                combined.RecalculateBounds();
                var uprightBounds = combined.bounds;
                var horizontalSize = Mathf.Max(
                    uprightBounds.size.x,
                    uprightBounds.size.z);
                if (uprightBounds.size.y < 0.05f ||
                    uprightBounds.size.y < horizontalSize * 0.45f)
                {
                    throw new InvalidOperationException(
                        $"Baked flower is not upright: " +
                        $"source={sourceModelPath} " +
                        $"bounds={uprightBounds.size}.");
                }

                uprightBounds.Expand(new Vector3(0.04f, 0.08f, 0.04f));
                combined.bounds = uprightBounds;
                var flowerMesh = SaveOrReplaceMesh(
                    combined,
                    meshAssetPath);
                return BuildFlowerPrefab(
                    prefabAssetPath,
                    flowerMesh,
                    material);
            }
            finally
            {
                if (!AssetDatabase.Contains(combined))
                {
                    UnityEngine.Object.DestroyImmediate(combined);
                }

            }
        }

        private static Mesh SaveOrReplaceMesh(
            Mesh source,
            string assetPath)
        {
            var destination = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (destination == null)
            {
                AssetDatabase.CreateAsset(source, assetPath);
                EditorUtility.SetDirty(source);
                return source;
            }

            var destinationName = Path.GetFileNameWithoutExtension(assetPath);
            EditorUtility.CopySerialized(source, destination);
            destination.name = destinationName;
            EditorUtility.SetDirty(destination);
            return destination;
        }

        private static GameObject BuildFlowerPrefab(
            string assetPath,
            Mesh mesh,
            Material material)
        {
            var temporary = new GameObject(
                Path.GetFileNameWithoutExtension(assetPath));
            try
            {
                temporary.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = temporary.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    temporary,
                    assetPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not create upright flower detail prefab: " +
                        $"{assetPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        private static void ConfigureTerrainData(
            TerrainData data,
            AdaptiveAssets assets,
            bool forceReseedGrass,
            bool registerUndo)
        {
            if (registerUndo)
            {
                Undo.RegisterCompleteObjectUndo(
                    data,
                    "Install adaptive Terrain grass");
            }

            var captured = CaptureLayers(data);
            var grassAPrototype = BuildGrassPrototype(
                assets.GrassA,
                0.88f,
                1.18f,
                0.72f,
                1.28f,
                0x1357);
            var grassBPrototype = BuildGrassPrototype(
                assets.GrassB,
                0.84f,
                1.14f,
                0.78f,
                1.22f,
                0x2468);

            var prototypes = new List<DetailPrototype>
            {
                grassAPrototype,
                grassBPrototype
            };
            AddPreservedNonGrassPrototypes(prototypes, captured);
            EnsureFlowerPrototype(
                prototypes,
                assets.FlowerWhite,
                0x3579);
            EnsureFlowerPrototype(
                prototypes,
                assets.FlowerOrange,
                0x468A);
            ValidatePrototypes(prototypes);

            data.SetDetailScatterMode(
                DetailScatterMode.InstanceCountMode);
            if (data.detailResolution != DetailResolution ||
                data.detailResolutionPerPatch != DetailResolutionPerPatch)
            {
                data.SetDetailResolution(
                    DetailResolution,
                    DetailResolutionPerPatch);
            }

            data.detailPrototypes = prototypes.ToArray();
            data.RefreshPrototypes();

            var seededGrass = BuildSeededGrassMaps(data);
            var grassAPath = AssetDatabase.GetAssetPath(assets.GrassA);
            var grassBPath = AssetDatabase.GetAssetPath(assets.GrassB);
            var preservedGrassA = FindCapturedMap(
                captured,
                grassAPath,
                DetailResolution);
            var preservedGrassB = FindCapturedMap(
                captured,
                grassBPath,
                DetailResolution);
            var hasPaintedAdaptiveGrass =
                CountMap(preservedGrassA) + CountMap(preservedGrassB) > 0;
            data.SetDetailLayer(
                0,
                0,
                0,
                !forceReseedGrass && hasPaintedAdaptiveGrass
                    ? preservedGrassA
                    : seededGrass[0]);
            data.SetDetailLayer(
                0,
                0,
                1,
                !forceReseedGrass && hasPaintedAdaptiveGrass
                    ? preservedGrassB
                    : seededGrass[1]);

            for (var index = 2; index < prototypes.Count; index++)
            {
                var prototypePath = AssetDatabase.GetAssetPath(
                    prototypes[index].prototype);
                var preserved = FindCapturedMap(
                    captured,
                    prototypePath,
                    DetailResolution);
                if (CountMap(preserved) == 0 &&
                    IsFlower(prototypes[index]))
                {
                    var orange = IsOrangeFlower(prototypes[index]);
                    preserved = FindCapturedFlowerMap(
                        captured,
                        orange,
                        DetailResolution);
                    if (CountMap(preserved) == 0)
                    {
                        preserved = BuildFlowerMap(data, orange);
                    }
                }

                data.SetDetailLayer(0, 0, index, preserved);
            }

            EditorUtility.SetDirty(data);
        }

        private static DetailPrototype BuildGrassPrototype(
            GameObject prefab,
            float minimumWidth,
            float maximumWidth,
            float minimumHeight,
            float maximumHeight,
            int seed)
        {
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Adaptive grass prototype prefab is missing.");
            }

            return new DetailPrototype
            {
                prototype = prefab,
                renderMode = DetailRenderMode.VertexLit,
                usePrototypeMesh = true,
                useInstancing = true,
                useDensityScaling = true,
                positionJitter = 0.92f,
                alignToGround = 0.16f,
                minWidth = minimumWidth,
                maxWidth = maximumWidth,
                minHeight = minimumHeight,
                maxHeight = maximumHeight,
                noiseSeed = seed,
                noiseSpread = 0.22f,
                holeEdgePadding = 0.08f,
                density = 1f,
                healthyColor = Color.white,
                dryColor = Color.white
            };
        }

        private static DetailPrototype BuildFlowerPrototype(
            GameObject prefab,
            int seed)
        {
            return new DetailPrototype
            {
                prototype = prefab,
                renderMode = DetailRenderMode.VertexLit,
                usePrototypeMesh = true,
                useInstancing = false,
                useDensityScaling = true,
                positionJitter = 0.86f,
                alignToGround = 0.12f,
                minWidth = 0.36f,
                maxWidth = 0.56f,
                minHeight = 0.34f,
                maxHeight = 0.54f,
                noiseSeed = seed,
                noiseSpread = 0.28f,
                holeEdgePadding = 0.10f,
                density = 1f,
                healthyColor = Color.white,
                dryColor = Color.white
            };
        }

        private static void AddPreservedNonGrassPrototypes(
            ICollection<DetailPrototype> destination,
            IReadOnlyList<CapturedLayer> captured)
        {
            for (var index = 0; index < captured.Count; index++)
            {
                var prototype = captured[index].Prototype;
                if (prototype == null ||
                    prototype.prototype == null ||
                    IsGrass(prototype) ||
                    IsFlower(prototype) ||
                    ContainsPrototype(destination, prototype.prototype))
                {
                    continue;
                }

                destination.Add(prototype);
            }
        }

        private static void EnsureFlowerPrototype(
            ICollection<DetailPrototype> prototypes,
            GameObject prefab,
            int seed)
        {
            if (prefab == null || ContainsPrototype(prototypes, prefab))
            {
                return;
            }

            prototypes.Add(BuildFlowerPrototype(prefab, seed));
        }

        private static void ValidatePrototypes(
            IReadOnlyList<DetailPrototype> prototypes)
        {
            for (var index = 0; index < prototypes.Count; index++)
            {
                if (!prototypes[index].Validate(out var error))
                {
                    throw new InvalidOperationException(
                        $"Invalid adaptive Terrain detail prototype " +
                        $"{index}: {error}");
                }
            }
        }

        private static List<CapturedLayer> CaptureLayers(TerrainData data)
        {
            var result = new List<CapturedLayer>();
            var prototypes = data.detailPrototypes;
            var resolution = data.detailResolution;
            for (var index = 0; index < prototypes.Length; index++)
            {
                result.Add(new CapturedLayer(
                    prototypes[index],
                    AssetDatabase.GetAssetPath(prototypes[index].prototype),
                    data.GetDetailLayer(
                        0,
                        0,
                        resolution,
                        resolution,
                        index),
                    resolution));
            }

            return result;
        }

        private static int[,] FindCapturedMap(
            IReadOnlyList<CapturedLayer> captured,
            string prototypePath,
            int targetResolution)
        {
            for (var index = 0; index < captured.Count; index++)
            {
                if (!string.Equals(
                        captured[index].PrototypePath,
                        prototypePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return ResampleInstanceMap(
                    captured[index].Map,
                    captured[index].Resolution,
                    targetResolution,
                    index * 0x1F31 + 0x7193);
            }

            return new int[targetResolution, targetResolution];
        }

        private static int[,] FindCapturedFlowerMap(
            IReadOnlyList<CapturedLayer> captured,
            bool orange,
            int targetResolution)
        {
            for (var index = 0; index < captured.Count; index++)
            {
                var prototype = captured[index].Prototype;
                if (!IsFlower(prototype) ||
                    IsOrangeFlower(prototype) != orange)
                {
                    continue;
                }

                return ResampleInstanceMap(
                    captured[index].Map,
                    captured[index].Resolution,
                    targetResolution,
                    orange ? 0x468A : 0x3579);
            }

            return new int[targetResolution, targetResolution];
        }

        private static int[,] ResampleInstanceMap(
            int[,] source,
            int sourceResolution,
            int targetResolution,
            int seed)
        {
            var result = new int[targetResolution, targetResolution];
            if (source == null || sourceResolution <= 0)
            {
                return result;
            }

            for (var z = 0; z < sourceResolution; z++)
            {
                for (var x = 0; x < sourceResolution; x++)
                {
                    var count = source[z, x];
                    for (var instance = 0; instance < count; instance++)
                    {
                        var jitterX = Hash01(
                            x + instance * 17,
                            z,
                            seed);
                        var jitterZ = Hash01(
                            x,
                            z + instance * 29,
                            seed ^ 0x5A17);
                        var targetX = Mathf.Clamp(
                            Mathf.FloorToInt(
                                (x + jitterX) /
                                sourceResolution *
                                targetResolution),
                            0,
                            targetResolution - 1);
                        var targetZ = Mathf.Clamp(
                            Mathf.FloorToInt(
                                (z + jitterZ) /
                                sourceResolution *
                                targetResolution),
                            0,
                            targetResolution - 1);
                        result[targetZ, targetX]++;
                    }
                }
            }

            return result;
        }

        private static int[][,] BuildSeededGrassMaps(TerrainData data)
        {
            var resolution = data.detailResolution;
            var shortGrass = new int[resolution, resolution];
            var tallGrass = new int[resolution, resolution];
            var alphamaps = data.GetAlphamaps(
                0,
                0,
                data.alphamapWidth,
                data.alphamapHeight);
            var holes = data.GetHoles(
                0,
                0,
                data.holesResolution,
                data.holesResolution);
            ResolveTerrainLayerIndices(
                data,
                out var sunLayer,
                out var deepLayer,
                out var pathLayer,
                out var cliffLayer);
            var surfaceCells = 0;
            var grassCells = 0;
            var gentleCells = 0;
            var waterClearCells = 0;
            double expectedInstances = 0d;

            for (var z = 0; z < resolution; z++)
            {
                var normalizedZ = (z + 0.5f) / resolution;
                for (var x = 0; x < resolution; x++)
                {
                    var normalizedX = (x + 0.5f) / resolution;
                    if (!IsSurface(holes, normalizedX, normalizedZ))
                    {
                        continue;
                    }

                    surfaceCells++;

                    SampleAlphamap(
                        alphamaps,
                        normalizedX,
                        normalizedZ,
                        sunLayer,
                        deepLayer,
                        pathLayer,
                        cliffLayer,
                        out var sun,
                        out var deep,
                        out var path,
                        out var cliff);
                    var grass = Mathf.Clamp01(sun + deep);
                    if (grass > 0.5f)
                    {
                        grassCells++;
                    }

                    var slope = data.GetSteepness(
                        normalizedX,
                        normalizedZ);
                    var slopeFade = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(28f, 48f, slope));
                    var cliffFade = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(0.08f, 0.52f, cliff));
                    if (slopeFade > 0.5f)
                    {
                        gentleCells++;
                    }
                    var worldX = normalizedX * data.size.x -
                        data.size.x * 0.5f;
                    var worldZ = normalizedZ * data.size.z -
                        data.size.z * 0.5f;
                    if (!StarterIslandTerrainSetup
                            .IsGroundCoverClearOfWater(
                                new Vector2(worldX, worldZ)))
                    {
                        continue;
                    }

                    waterClearCells++;

                    var broadNoise = Mathf.PerlinNoise(
                        (worldX + 411f) * 0.018f,
                        (worldZ + 283f) * 0.018f);
                    var mediumNoise = Mathf.PerlinNoise(
                        (worldX + 83f) * 0.071f,
                        (worldZ + 129f) * 0.071f);
                    var band = Mathf.Sin(
                        worldX * 0.54f +
                        worldZ * 0.18f +
                        broadNoise * 2.1f) * 0.5f + 0.5f;
                    var densityVariation = Mathf.Lerp(
                        0.76f,
                        1.25f,
                        broadNoise * 0.62f +
                        mediumNoise * 0.23f +
                        band * 0.15f);
                    var commonMask = slopeFade * cliffFade;
                    var shortExpected = commonMask * densityVariation *
                        (grass * 4.20f + path * 0.17f);
                    var tallExpected = commonMask * densityVariation *
                        (grass * Mathf.Lerp(2.15f, 2.65f, deep) +
                         path * 0.030f);
                    expectedInstances += shortExpected + tallExpected;
                    shortGrass[z, x] = StochasticCount(
                        shortExpected,
                        x,
                        z,
                        0x1357);
                    tallGrass[z, x] = StochasticCount(
                        tallExpected,
                        x,
                        z,
                        0x2468);
                }
            }

            Debug.Log(
                "ADAPTIVE_TERRAIN_GRASS_SEED " +
                $"surfaceCells={surfaceCells} grassCells={grassCells} " +
                $"gentleCells={gentleCells} " +
                $"waterClearCells={waterClearCells} " +
                $"expected={expectedInstances:F1} " +
                $"actual={CountMap(shortGrass) + CountMap(tallGrass)}");

            return new[] { shortGrass, tallGrass };
        }

        private static int[,] BuildFlowerMap(
            TerrainData data,
            bool orange)
        {
            var resolution = data.detailResolution;
            var result = new int[resolution, resolution];
            var alphamaps = data.GetAlphamaps(
                0,
                0,
                data.alphamapWidth,
                data.alphamapHeight);
            var holes = data.GetHoles(
                0,
                0,
                data.holesResolution,
                data.holesResolution);
            ResolveTerrainLayerIndices(
                data,
                out var sunLayer,
                out var deepLayer,
                out _,
                out var cliffLayer);
            for (var z = 0; z < resolution; z++)
            {
                var normalizedZ = (z + 0.5f) / resolution;
                for (var x = 0; x < resolution; x++)
                {
                    var normalizedX = (x + 0.5f) / resolution;
                    if (!IsSurface(holes, normalizedX, normalizedZ))
                    {
                        continue;
                    }

                    var alphaX = Mathf.Clamp(
                        Mathf.FloorToInt(
                            normalizedX * alphamaps.GetLength(1)),
                        0,
                        alphamaps.GetLength(1) - 1);
                    var alphaZ = Mathf.Clamp(
                        Mathf.FloorToInt(
                            normalizedZ * alphamaps.GetLength(0)),
                        0,
                        alphamaps.GetLength(0) - 1);
                    var grass = Mathf.Clamp01(
                        alphamaps[alphaZ, alphaX, sunLayer] +
                        alphamaps[alphaZ, alphaX, deepLayer]);
                    var cliff = alphamaps[alphaZ, alphaX, cliffLayer];
                    if (grass < 0.72f || cliff > 0.10f ||
                        data.GetSteepness(normalizedX, normalizedZ) > 27f)
                    {
                        continue;
                    }

                    var worldX = normalizedX * data.size.x -
                        data.size.x * 0.5f;
                    var worldZ = normalizedZ * data.size.z -
                        data.size.z * 0.5f;
                    var patch = Mathf.PerlinNoise(
                        (worldX + (orange ? 217f : 91f)) * 0.032f,
                        (worldZ + (orange ? 43f : 177f)) * 0.032f);
                    var expected = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(0.56f, 0.82f, patch)) *
                        (orange ? 0.016f : 0.026f);
                    result[z, x] = StochasticCount(
                        expected,
                        x,
                        z,
                        orange ? 0x468A : 0x3579);
                }
            }

            return result;
        }

        private static void ResolveTerrainLayerIndices(
            TerrainData data,
            out int sun,
            out int deep,
            out int path,
            out int cliff)
        {
            sun = FindTerrainLayer(data, "GrassSun", 0);
            deep = FindTerrainLayer(data, "GrassDeep", 1);
            path = FindTerrainLayer(data, "DirtPath", 2);
            cliff = FindTerrainLayer(data, "CliffWarm", 3);
        }

        private static int FindTerrainLayer(
            TerrainData data,
            string nameFragment,
            int fallback)
        {
            var layers = data.terrainLayers;
            for (var index = 0; index < layers.Length; index++)
            {
                if (layers[index] != null &&
                    layers[index].name.IndexOf(
                        nameFragment,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return index;
                }
            }

            if (fallback >= 0 && fallback < data.alphamapLayers)
            {
                return fallback;
            }

            throw new InvalidOperationException(
                $"Terrain layer containing '{nameFragment}' is missing.");
        }

        private static void SampleAlphamap(
            float[,,] alphamaps,
            float normalizedX,
            float normalizedZ,
            int sunLayer,
            int deepLayer,
            int pathLayer,
            int cliffLayer,
            out float sun,
            out float deep,
            out float path,
            out float cliff)
        {
            var x = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedX * alphamaps.GetLength(1)),
                0,
                alphamaps.GetLength(1) - 1);
            var z = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedZ * alphamaps.GetLength(0)),
                0,
                alphamaps.GetLength(0) - 1);
            sun = alphamaps[z, x, sunLayer];
            deep = alphamaps[z, x, deepLayer];
            path = alphamaps[z, x, pathLayer];
            cliff = alphamaps[z, x, cliffLayer];
        }

        private static bool IsSurface(
            bool[,] holes,
            float normalizedX,
            float normalizedZ)
        {
            var x = Mathf.Clamp(
                Mathf.FloorToInt(normalizedX * holes.GetLength(1)),
                0,
                holes.GetLength(1) - 1);
            var z = Mathf.Clamp(
                Mathf.FloorToInt(normalizedZ * holes.GetLength(0)),
                0,
                holes.GetLength(0) - 1);
            return holes[z, x];
        }

        private static int StochasticCount(
            float expected,
            int x,
            int z,
            int seed)
        {
            expected = Mathf.Max(0f, expected);
            var whole = Mathf.FloorToInt(expected);
            var fraction = expected - whole;
            return whole + (Hash01(x, z, seed) < fraction ? 1 : 0);
        }

        private static float Hash01(int x, int z, int seed)
        {
            unchecked
            {
                var value = (uint)(x * 0x1F123BB5) ^
                    (uint)(z * 0x5F356495) ^
                    (uint)seed;
                value ^= value >> 16;
                value *= 0x7FEB352D;
                value ^= value >> 15;
                value *= 0x846CA68B;
                value ^= value >> 16;
                return (value & 0x00FFFFFF) / 16777216f;
            }
        }

        private static int PatchTerrainPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(TerrainPrefabPath);
            if (root == null)
            {
                throw new FileNotFoundException(
                    "Starter Island Terrain prefab is missing.",
                    TerrainPrefabPath);
            }

            var removed = 0;
            try
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var index = transforms.Length - 1; index >= 0; index--)
                {
                    if (transforms[index] == null ||
                        transforms[index] == root.transform ||
                        (!string.Equals(
                             transforms[index].name,
                             LegacyGroundCoverName,
                             StringComparison.Ordinal) &&
                         !string.Equals(
                             transforms[index].name,
                             LegacyGroundDetailName,
                             StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(
                        transforms[index].gameObject);
                    removed++;
                }

                var terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain == null)
                {
                    throw new InvalidOperationException(
                        "Starter Island prefab has no Terrain component.");
                }

                terrain.detailObjectDistance = 92f;
                terrain.detailObjectDensity = 1f;
                terrain.drawTreesAndFoliage = true;
                terrain.drawInstanced = true;
                EditorUtility.SetDirty(terrain);
                PrefabUtility.SaveAsPrefabAsset(root, TerrainPrefabPath);
                return removed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool IsGrass(DetailPrototype prototype)
        {
            return prototype != null &&
                prototype.prototype != null &&
                prototype.prototype.name.IndexOf(
                    "Grass",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFlower(DetailPrototype prototype)
        {
            return prototype != null &&
                prototype.prototype != null &&
                prototype.prototype.name.IndexOf(
                    "Flower",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsOrangeFlower(DetailPrototype prototype)
        {
            return IsFlower(prototype) &&
                prototype.prototype.name.IndexOf(
                    "Orange",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsPrototype(
            IEnumerable<DetailPrototype> prototypes,
            GameObject prefab)
        {
            foreach (var prototype in prototypes)
            {
                if (prototype != null && prototype.prototype == prefab)
                {
                    return true;
                }
            }

            return false;
        }

        private static long CountMap(int[,] map)
        {
            if (map == null)
            {
                return 0L;
            }

            long count = 0;
            foreach (var value in map)
            {
                count += value;
            }

            return count;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(
                    $"Invalid asset folder path: {folder}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void SetFloat(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetColor(
            Material material,
            string property,
            Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private readonly struct AdaptiveAssets
        {
            public AdaptiveAssets(
                GameObject grassA,
                GameObject grassB,
                GameObject flowerWhite,
                GameObject flowerOrange,
                bool requiresReseed)
            {
                GrassA = grassA;
                GrassB = grassB;
                FlowerWhite = flowerWhite;
                FlowerOrange = flowerOrange;
                RequiresReseed = requiresReseed;
            }

            public GameObject GrassA { get; }
            public GameObject GrassB { get; }
            public GameObject FlowerWhite { get; }
            public GameObject FlowerOrange { get; }
            public bool RequiresReseed { get; }
        }

        private sealed class CapturedLayer
        {
            public CapturedLayer(
                DetailPrototype prototype,
                string prototypePath,
                int[,] map,
                int resolution)
            {
                Prototype = prototype;
                PrototypePath = prototypePath;
                Map = map;
                Resolution = resolution;
            }

            public DetailPrototype Prototype { get; }
            public string PrototypePath { get; }
            public int[,] Map { get; }
            public int Resolution { get; }
        }
    }
}

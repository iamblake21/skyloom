using System;
using System.Collections.Generic;
using System.IO;
using CML.Unity.Bootstrap;
using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity boundary for the Starter Island.
    ///
    /// Blender owns authored geometry, material-slot names and gameplay markers.
    /// Unity owns material assets, the single native MeshCollider, the production
    /// prefab and the disposable review scene. No logical/proxy collision state
    /// is attached to the island.
    /// </summary>
    public static class StarterIslandAssetSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StarterIsland";
        public const string ModelPath =
            Root + "/Models/ENV_StarterIsland.fbx";
        public const string MaterialsRoot = Root + "/Materials";
        public const string PrefabsRoot = Root + "/Prefabs";
        public const string PrefabPath =
            PrefabsRoot + "/PF_StarterIsland.prefab";
        public const string ReviewScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Review.unity";
        private const string FoliageRoot = Root + "/Foliage";
        private const string FoliageModelsRoot = FoliageRoot + "/Models";
        private const string FoliageTexturesRoot = FoliageRoot + "/Textures";
        private const string FoliageMaterialsRoot =
            FoliageRoot + "/Materials";
        private const string FoliagePrefabsRoot =
            FoliageRoot + "/Prefabs";
        private const string FoliageBaseTexturePath =
            FoliageTexturesRoot +
            "/T_StarterIsland_Foliage_BaseColor.png";
        private const string FoliageMaskTexturePath =
            FoliageTexturesRoot +
            "/T_StarterIsland_Foliage_Mask.png";
        private const string FoliageMaterialPath =
            FoliageMaterialsRoot +
            "/M_StarterIsland_FoliageAtlas.mat";

        private const string SurfaceShaderName =
            "CML/Environment/Starter Island Stylized Surface";
        private const string WaterShaderName =
            "CML/Environment/Starter Island Stylized Water";
        private const string FoliageShaderName =
            "CML/Environment/Starter Island Foliage";
        private const string ReviewSceneId =
            "cml.environment.starter_island.review";
        private const int ReviewSceneRevision = 1;
        private const string IslandMeshName = "GEO_IslandMass";
        private const string ReviewRootName =
            "ENV_StarterIsland_Review";
        private const string PlayerName =
            "ENV_StarterIsland_ReviewPlayer";
        private const string FoliageSceneRootName =
            "ENV_StarterIsland_Foliage";

        private static readonly string[] RequiredMarkers =
        {
            "REF_PlayerSpawn",
            "REF_AirshipDock",
            "REF_TutorialCenter",
            "REF_FactoryCenter",
            "REF_FactoryCorner_SW",
            "REF_FactoryCorner_SE",
            "REF_FactoryCorner_NW",
            "REF_FactoryCorner_NE",
            "REF_AgricultureCenter",
            "REF_PortalAnchor",
            "REF_SpringSource",
            "REF_PondCenter",
            "REF_WaterfallLip",
            "REF_DepositAnchor_Stone",
            "REF_DepositAnchor_Iron",
            "REF_DepositAnchor_Copper",
            "REF_DepositAnchor_Clay"
        };

        private static readonly MaterialDefinition[] MaterialDefinitions =
        {
            Opaque(
                "M_StarterIsland_GrassSun",
                "#A6C94B",
                "#C6A46B",
                "#4D8060",
                1f),
            Opaque(
                "M_StarterIsland_GrassMid",
                "#78B94E",
                "#C6A46B",
                "#306F5B",
                1f),
            Opaque(
                "M_StarterIsland_GrassDeep",
                "#3F7A3F",
                "#A97E50",
                "#28594B",
                1f),
            Opaque(
                "M_StarterIsland_Dirt",
                "#D8B47A",
                "#B98558",
                "#8E765B",
                0f),
            Opaque(
                "M_StarterIsland_CliffWarm",
                "#D0A078",
                "#C18A62",
                "#91664E",
                0f),
            Opaque(
                "M_StarterIsland_CliffMid",
                "#B78160",
                "#A56F52",
                "#7D5948",
                0f),
            Opaque(
                "M_StarterIsland_CliffDeep",
                "#8B624F",
                "#765444",
                "#5B453C",
                0f),
            Opaque(
                "M_StarterIsland_DetailRock",
                "#A9AEA8",
                "#858C88",
                "#687472",
                0f),
            new MaterialDefinition(
                "M_StarterIsland_WaterGuide",
                WaterShaderName,
                "#58D9DB",
                "#197EAA",
                "#D4FFF2",
                0f,
                true)
        };

        [MenuItem("CML/Art/Rebuild Starter Island")]
        public static void Run()
        {
            var prefab = BuildPrefabOnly();
            var foliagePrefabs = BuildOptionalFoliagePrefabs();
            BuildReviewScene(prefab, foliagePrefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            var islandMesh = FindRecursive(
                prefab.transform,
                IslandMeshName);
            var mesh = islandMesh.GetComponent<MeshFilter>().sharedMesh;
            Debug.Log(
                $"STARTER_ISLAND_UNITY_VALIDATION prefab={PrefabPath} " +
                $"scene={ReviewScenePath} meshes=" +
                $"{prefab.GetComponentsInChildren<MeshFilter>(true).Length} " +
                $"triangles={CountTriangles(mesh)} markers={RequiredMarkers.Length} " +
                "colliders=1 colliderAuthority=Unity.MeshCollider " +
                "rigidbodies=0 customCollision=0 status=PASS");
        }

        [MenuItem("CML/Art/Rebuild Starter Island Prefab Only")]
        public static void RunPrefabOnly()
        {
            var prefab = BuildPrefabOnly();
            BuildOptionalFoliagePrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }
        }

        [MenuItem("CML/Art/Rebuild Starter Island Review Scene")]
        public static void RunReviewSceneOnly()
        {
            var prefab = RequireAsset<GameObject>(PrefabPath);
            ValidatePrefab(prefab, BuildMaterialMap());
            var foliagePrefabs = BuildOptionalFoliagePrefabs();
            BuildReviewScene(prefab, foliagePrefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("CML/Art/Rebuild Starter Island Foliage Only")]
        public static void RunFoliageOnly()
        {
            var foliagePrefabs = BuildOptionalFoliagePrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"STARTER_ISLAND_FOLIAGE_ONLY prefabs=" +
                $"{foliagePrefabs.Count} status=PASS");
        }

        private static GameObject BuildPrefabOnly()
        {
            RequireModelFile();
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);
            EnsureFolder("Assets/_Project/Scenes");

            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var materials = BuildMaterialMap();
            ConfigureModelMaterialRemaps(materials);

            var source = RequireAsset<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate Starter Island model {ModelPath}.");
            }

            try
            {
                instance.name = "PF_StarterIsland";
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                RemoveImportedPhysics(instance);
                AssignProductionMaterials(instance, materials);
                AddSingleUnityCollider(instance);
                ConfigureStaticFlags(instance);

                var prefab =
                    PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save Starter Island prefab {PrefabPath}.");
                }

                ValidatePrefab(prefab, materials);
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Dictionary<string, Material> BuildMaterialMap()
        {
            EnsureFolder(MaterialsRoot);

            var surfaceShader = Shader.Find(SurfaceShaderName);
            var waterShader = Shader.Find(WaterShaderName);
            if (surfaceShader == null)
            {
                throw new InvalidOperationException(
                    $"Required shader '{SurfaceShaderName}' is unavailable.");
            }

            if (waterShader == null)
            {
                throw new InvalidOperationException(
                    $"Required shader '{WaterShaderName}' is unavailable.");
            }

            var result = new Dictionary<string, Material>(
                StringComparer.Ordinal);
            foreach (var definition in MaterialDefinitions)
            {
                var shader = definition.Transparent
                    ? waterShader
                    : surfaceShader;
                var path =
                    MaterialsRoot + "/" + definition.Name + ".mat";
                var material = UpsertMaterial(path, shader, definition);
                result.Add(definition.Name, material);
            }

            var skyboxPath =
                MaterialsRoot + "/M_StarterIsland_Skybox.mat";
            var skyboxShader = Shader.Find("Skybox/Procedural");
            if (skyboxShader == null)
            {
                throw new InvalidOperationException(
                    "Unity procedural skybox shader is unavailable.");
            }

            var skybox = AssetDatabase.LoadAssetAtPath<Material>(skyboxPath);
            if (skybox == null)
            {
                skybox = new Material(skyboxShader)
                {
                    name = "M_StarterIsland_Skybox"
                };
                AssetDatabase.CreateAsset(skybox, skyboxPath);
            }
            else if (skybox.shader != skyboxShader)
            {
                skybox.shader = skyboxShader;
            }

            SetColor(skybox, "_SkyTint", Html("#7ED5E8"));
            SetColor(skybox, "_GroundColor", Html("#B8C99E"));
            SetFloat(skybox, "_AtmosphereThickness", 0.82f);
            SetFloat(skybox, "_SunSize", 0.035f);
            SetFloat(skybox, "_SunSizeConvergence", 5f);
            SetFloat(skybox, "_Exposure", 1.12f);
            EditorUtility.SetDirty(skybox);
            result.Add(skybox.name, skybox);

            return result;
        }

        private static Material UpsertMaterial(
            string path,
            Shader shader,
            MaterialDefinition definition)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = definition.Name
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (definition.Transparent)
            {
                var shallow = Html(definition.PrimaryColor);
                shallow.a = 0.64f;
                var deep = Html(definition.SecondaryColor);
                deep.a = 0.82f;
                SetColor(material, "_ShallowColor", shallow);
                SetColor(material, "_DeepColor", deep);
                SetColor(material, "_FoamColor", Html(definition.WetColor));
                SetFloat(material, "_DepthRange", 4f);
                SetFloat(material, "_FoamDistance", 0.65f);
                SetFloat(material, "_FoamFeather", 0.38f);
                SetFloat(material, "_WaveScaleA", 0.11f);
                SetFloat(material, "_WaveScaleB", 0.27f);
                SetFloat(material, "_WaveSpeedA", 0.58f);
                SetFloat(material, "_WaveSpeedB", 1.16f);
                SetFloat(material, "_WaveStrength", 0.14f);
                SetFloat(material, "_FresnelPower", 3.2f);
                SetFloat(material, "_GlintPower", 72f);
                SetFloat(material, "_GlintStrength", 0.48f);
                SetFloat(material, "_Opacity", 0.76f);
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                SetColor(
                    material,
                    "_BaseColor",
                    Html(definition.PrimaryColor));
                SetColor(
                    material,
                    "_SecondaryColor",
                    Html(definition.SecondaryColor));
                SetColor(
                    material,
                    "_WetColor",
                    Html(definition.WetColor));
                SetFloat(
                    material,
                    "_VertexBlend",
                    definition.VertexBlend);
                SetFloat(material, "_AmbientStrength", 0.72f);
                SetFloat(material, "_ShadowFloor", 0.38f);
                SetFloat(material, "_ColorVariation", 0.035f);
                if (definition.Name == "M_StarterIsland_DetailRock")
                {
                    SetColor(material, "_BaseColor", Html("#9FA39E"));
                    SetColor(material, "_SecondaryColor", Html("#B5B8B1"));
                    SetColor(material, "_WetColor", Html("#747D78"));
                    SetFloat(material, "_AmbientStrength", 0.92f);
                    SetFloat(material, "_ShadowFloor", 0.50f);
                    SetFloat(material, "_ColorVariation", 0.025f);
                    SetFloat(material, "_RockDetail", 1f);
                    SetColor(material, "_RockTopColor", Html("#D7CABB"));
                    SetColor(material, "_RockUnderColor", Html("#727B74"));
                    SetFloat(material, "_RockTopStrength", 0.68f);
                    SetFloat(material, "_RockUnderStrength", 0.34f);
                    SetFloat(material, "_RockMacroScale", 0.48f);
                    SetFloat(material, "_RockMacroStrength", 0.105f);
                    SetFloat(material, "_RockGrainScale", 4.6f);
                    SetFloat(material, "_RockGrainStrength", 0.045f);
                    SetFloat(material, "_RockContactBlend", 0.74f);
                    SetFloat(material, "_RockContactHeight", 0.24f);
                    SetFloat(material, "_RockContactFeather", 0.20f);
                    SetFloat(material, "_RockContactNoise", 0.14f);
                    SetColor(
                        material,
                        "_RockContactGrassColor",
                        Html("#496A35"));
                    SetColor(
                        material,
                        "_RockContactDeepGrassColor",
                        Html("#314C2B"));
                    SetColor(
                        material,
                        "_RockContactDirtColor",
                        Html("#B78F60"));
                    SetColor(
                        material,
                        "_RockContactCliffColor",
                        Html("#87503F"));
                }
                material.renderQueue = (int)RenderQueue.Geometry;
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static List<GameObject> BuildOptionalFoliagePrefabs()
        {
            if (!AssetDatabase.IsValidFolder(FoliageModelsRoot))
            {
                Debug.Log(
                    "STARTER_ISLAND_FOLIAGE status=SKIPPED reason=models_missing");
                return DiscoverFoliagePrefabs();
            }

            var modelPaths = FindAssetsWithExtension(
                FoliageModelsRoot,
                ".fbx");
            if (modelPaths.Count == 0)
            {
                Debug.Log(
                    "STARTER_ISLAND_FOLIAGE status=SKIPPED reason=models_missing");
                return DiscoverFoliagePrefabs();
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                    FoliageBaseTexturePath) == null ||
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    FoliageMaskTexturePath) == null)
            {
                Debug.LogWarning(
                    "Starter Island foliage models are present but their " +
                    "shared palette textures are not imported yet. Foliage " +
                    "setup was skipped and can be rerun safely.");
                return DiscoverFoliagePrefabs();
            }

            EnsureFolder(FoliageMaterialsRoot);
            EnsureFolder(FoliagePrefabsRoot);
            AssetDatabase.ImportAsset(
                FoliageBaseTexturePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                FoliageMaskTexturePath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var shader = Shader.Find(FoliageShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Required shader '{FoliageShaderName}' is unavailable.");
            }

            var foliageMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    FoliageMaterialPath);
            if (foliageMaterial == null)
            {
                foliageMaterial = new Material(shader)
                {
                    name = "M_StarterIsland_FoliageAtlas"
                };
                AssetDatabase.CreateAsset(
                    foliageMaterial,
                    FoliageMaterialPath);
            }
            else if (foliageMaterial.shader != shader)
            {
                foliageMaterial.shader = shader;
            }

            SetTexture(
                foliageMaterial,
                "_BaseMap",
                RequireAsset<Texture2D>(FoliageBaseTexturePath));
            SetTexture(
                foliageMaterial,
                "_MaskMap",
                RequireAsset<Texture2D>(FoliageMaskTexturePath));
            SetColor(foliageMaterial, "_BaseColor", Color.white);
            SetFloat(foliageMaterial, "_WindStrength", 0.12f);
            SetFloat(foliageMaterial, "_WindSpeed", 0.85f);
            SetFloat(foliageMaterial, "_AmbientStrength", 0.82f);
            SetFloat(foliageMaterial, "_ShadowFloor", 0.42f);
            foliageMaterial.enableInstancing = true;
            EditorUtility.SetDirty(foliageMaterial);

            var built = new List<GameObject>();
            foreach (var modelPath in modelPaths)
            {
                AssetDatabase.ImportAsset(
                    modelPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                ConfigureFoliageMaterialRemap(
                    modelPath,
                    foliageMaterial);

                var source = RequireAsset<GameObject>(modelPath);
                var instance =
                    PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate foliage model {modelPath}.");
                }

                try
                {
                    var prefabName = FoliagePrefabName(modelPath);
                    instance.name = prefabName;
                    instance.transform.SetPositionAndRotation(
                        Vector3.zero,
                        Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    RemoveImportedPhysics(instance);

                    foreach (var renderer in
                             instance.GetComponentsInChildren<Renderer>(true))
                    {
                        var assigned =
                            new Material[renderer.sharedMaterials.Length];
                        for (var index = 0;
                             index < assigned.Length;
                             index++)
                        {
                            assigned[index] = foliageMaterial;
                        }

                        renderer.sharedMaterials = assigned;
                        GameObjectUtility.SetStaticEditorFlags(
                            renderer.gameObject,
                            StaticEditorFlags.BatchingStatic |
                            StaticEditorFlags.OccludeeStatic |
                            StaticEditorFlags.ReflectionProbeStatic);
                    }

                    var prefabPath =
                        FoliagePrefabsRoot + "/" + prefabName + ".prefab";
                    var prefab = PrefabUtility.SaveAsPrefabAsset(
                        instance,
                        prefabPath);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not save foliage prefab {prefabPath}.");
                    }

                    ValidateFoliagePrefab(prefab, foliageMaterial);
                    built.Add(prefab);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            built.Sort(
                (left, right) => string.Compare(
                    left.name,
                    right.name,
                    StringComparison.Ordinal));
            Debug.Log(
                $"STARTER_ISLAND_FOLIAGE prefabs={built.Count} " +
                "colliders=0 material=shared status=PASS");
            return built;
        }

        private static void ConfigureFoliageMaterialRemap(
            string modelPath,
            Material foliageMaterial)
        {
            var importer =
                AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load foliage ModelImporter {modelPath}.");
            }

            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    "M_StarterIsland_FoliageAtlas"),
                foliageMaterial);
            importer.SaveAndReimport();
        }

        private static void ValidateFoliagePrefab(
            GameObject prefab,
            Material foliageMaterial)
        {
            AssertIdentity(prefab.transform, prefab.name);
            var renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            var filters =
                prefab.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != 1 ||
                filters.Length != 1 ||
                filters[0].sharedMesh == null ||
                !filters[0].sharedMesh.HasVertexAttribute(
                    VertexAttribute.Color))
            {
                throw new InvalidOperationException(
                    $"Foliage prefab {prefab.name} must contain one " +
                    "vertex-colored render mesh.");
            }

            if (renderers[0].sharedMaterials.Length != 1 ||
                renderers[0].sharedMaterial != foliageMaterial ||
                prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Joint>(true).Length != 0 ||
                prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Foliage prefab {prefab.name} differs from its " +
                    "non-colliding review contract.");
            }

            if (CountTransforms(prefab.transform, "REF_Placement") != 1)
            {
                throw new InvalidOperationException(
                    $"Foliage prefab {prefab.name} lacks REF_Placement.");
            }

            var isTree = prefab.name.StartsWith(
                "PF_Tree_",
                StringComparison.Ordinal);
            var chopCount =
                CountTransforms(prefab.transform, "REF_ChopPoint");
            var canopyCount =
                CountTransforms(prefab.transform, "REF_CanopyCenter");
            if ((isTree && (chopCount != 1 || canopyCount != 1)) ||
                (!isTree && (chopCount != 0 || canopyCount != 0)))
            {
                throw new InvalidOperationException(
                    $"Foliage marker contract changed for {prefab.name}.");
            }
        }

        private static List<GameObject> DiscoverFoliagePrefabs()
        {
            var result = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(FoliagePrefabsRoot))
            {
                return result;
            }

            foreach (var guid in AssetDatabase.FindAssets(
                         "t:Prefab",
                         new[] { FoliagePrefabsRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null &&
                    IsSupportedFoliagePrefab(prefab.name))
                {
                    result.Add(prefab);
                }
            }

            result.Sort(
                (left, right) => string.Compare(
                    left.name,
                    right.name,
                    StringComparison.Ordinal));
            return result;
        }

        private static List<string> FindAssetsWithExtension(
            string folder,
            string extension)
        {
            var result = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:GameObject",
                         new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(
                        extension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path);
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static string FoliagePrefabName(string modelPath)
        {
            var modelName =
                Path.GetFileNameWithoutExtension(modelPath);
            if (modelName.StartsWith(
                    "ENV_FlowerPatch_",
                    StringComparison.Ordinal))
            {
                return "PF_Flower_" +
                       modelName.Substring("ENV_FlowerPatch_".Length);
            }

            foreach (var prefix in new[]
                     {
                         "ENV_Tree_",
                         "ENV_Shrub_",
                         "ENV_Grass_"
                     })
            {
                if (modelName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return "PF_" +
                           modelName.Substring("ENV_".Length);
                }
            }

            throw new InvalidOperationException(
                $"Unsupported Starter Island foliage model '{modelName}'.");
        }

        private static bool IsSupportedFoliagePrefab(string name)
        {
            return name.StartsWith("PF_Tree_", StringComparison.Ordinal) ||
                   name.StartsWith("PF_Shrub_", StringComparison.Ordinal) ||
                   name.StartsWith("PF_Grass_", StringComparison.Ordinal) ||
                   name.StartsWith("PF_Flower_", StringComparison.Ordinal);
        }

        private static void ConfigureModelMaterialRemaps(
            IReadOnlyDictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {ModelPath}.");
            }

            foreach (var definition in MaterialDefinitions)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(
                        typeof(Material),
                        definition.Name),
                    materials[definition.Name]);
            }

            importer.SaveAndReimport();
        }

        private static void RemoveImportedPhysics(GameObject instance)
        {
            foreach (var joint in instance.GetComponentsInChildren<Joint>(true))
            {
                UnityEngine.Object.DestroyImmediate(joint);
            }

            foreach (var rigidbody in
                     instance.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(rigidbody);
            }

            foreach (var collider in
                     instance.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static void AssignProductionMaterials(
            GameObject instance,
            IReadOnlyDictionary<string, Material> materials)
        {
            var island = RequireTransform(instance.transform, IslandMeshName);
            var islandRenderer = RequireComponent<MeshRenderer>(island);
            islandRenderer.sharedMaterials = new[]
            {
                materials["M_StarterIsland_GrassSun"],
                materials["M_StarterIsland_GrassMid"],
                materials["M_StarterIsland_GrassDeep"],
                materials["M_StarterIsland_CliffWarm"],
                materials["M_StarterIsland_CliffMid"],
                materials["M_StarterIsland_CliffDeep"]
            };

            foreach (var renderer in
                     instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == islandRenderer)
                {
                    continue;
                }

                if (renderer.name.StartsWith(
                        "GEO_Path_",
                        StringComparison.Ordinal))
                {
                    renderer.sharedMaterials = new[]
                    {
                        materials["M_StarterIsland_Dirt"]
                    };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    continue;
                }

                if (renderer.name.StartsWith(
                        "GEO_Water_",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        renderer.name,
                        "GEO_Waterfall",
                        StringComparison.Ordinal))
                {
                    renderer.sharedMaterials = new[]
                    {
                        materials["M_StarterIsland_WaterGuide"]
                    };
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Unexpected Starter Island renderer '{renderer.name}'.");
            }
        }

        private static void AddSingleUnityCollider(GameObject instance)
        {
            var island = RequireTransform(instance.transform, IslandMeshName);
            var filter = RequireComponent<MeshFilter>(island);
            if (filter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    $"{IslandMeshName} has no imported mesh.");
            }

            var collider = island.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices |
                MeshColliderCookingOptions.UseFastMidphase;
        }

        private static void ConfigureStaticFlags(GameObject instance)
        {
            foreach (var renderer in
                     instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsWater(renderer.transform.name))
                {
                    GameObjectUtility.SetStaticEditorFlags(
                        renderer.gameObject,
                        (StaticEditorFlags)0);
                    continue;
                }

                var flags =
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic;
                if (string.Equals(
                        renderer.transform.name,
                        IslandMeshName,
                        StringComparison.Ordinal))
                {
                    flags |= StaticEditorFlags.OccluderStatic;
                }

                GameObjectUtility.SetStaticEditorFlags(
                    renderer.gameObject,
                    flags);
            }
        }

        private static void ValidatePrefab(
            GameObject prefab,
            IReadOnlyDictionary<string, Material> materials)
        {
            AssertIdentity(prefab.transform, "Starter Island prefab root");

            var island = RequireTransform(prefab.transform, IslandMeshName);
            AssertIdentityScaleChain(island, prefab.transform);
            var filter = RequireComponent<MeshFilter>(island);
            var renderer = RequireComponent<MeshRenderer>(island);
            var collider = RequireComponent<MeshCollider>(island);

            if (filter.sharedMesh == null ||
                filter.sharedMesh.subMeshCount != 6 ||
                !filter.sharedMesh.HasVertexAttribute(
                    VertexAttribute.Color))
            {
                throw new InvalidOperationException(
                    $"{IslandMeshName} must have one mesh, six active " +
                    "terrain/cliff submeshes and the authored vertex-color " +
                    "layer. Dirt remains on the separate path meshes.");
            }

            var expectedIslandMaterials = new[]
            {
                materials["M_StarterIsland_GrassSun"],
                materials["M_StarterIsland_GrassMid"],
                materials["M_StarterIsland_GrassDeep"],
                materials["M_StarterIsland_CliffWarm"],
                materials["M_StarterIsland_CliffMid"],
                materials["M_StarterIsland_CliffDeep"]
            };
            var assigned = renderer.sharedMaterials;
            if (assigned.Length != expectedIslandMaterials.Length)
            {
                throw new InvalidOperationException(
                    "Starter Island material-slot count changed.");
            }

            for (var index = 0;
                 index < expectedIslandMaterials.Length;
                 index++)
            {
                if (assigned[index] != expectedIslandMaterials[index])
                {
                    throw new InvalidOperationException(
                        $"Starter Island material slot {index} is invalid.");
                }
            }

            var colliders =
                prefab.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 1 ||
                colliders[0] != collider ||
                collider.sharedMesh != filter.sharedMesh ||
                collider.convex ||
                collider.isTrigger ||
                !collider.enabled)
            {
                throw new InvalidOperationException(
                    "PF_StarterIsland must contain exactly one enabled, " +
                    "non-trigger, non-convex Unity MeshCollider using the " +
                    "visible island mesh.");
            }

            if (prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Joint>(true).Length != 0 ||
                prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "PF_StarterIsland must not contain Rigidbody, Joint or " +
                    "custom MonoBehaviour components.");
            }

            var bounds = renderer.bounds;
            if (Mathf.Abs(bounds.size.x - 230f) > 0.12f ||
                Mathf.Abs(bounds.size.z - 175f) > 0.12f ||
                bounds.size.y < 110f ||
                bounds.size.y > 135f ||
                bounds.max.y < 20f ||
                bounds.max.y > 23f ||
                bounds.min.y > -90f ||
                bounds.min.y < -112f)
            {
                throw new InvalidOperationException(
                    $"Unexpected Starter Island Unity bounds: " +
                    $"center={bounds.center}, size={bounds.size}.");
            }

            var flags =
                GameObjectUtility.GetStaticEditorFlags(island.gameObject);
            var requiredFlags =
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic;
            if ((flags & requiredFlags) != requiredFlags)
            {
                throw new InvalidOperationException(
                    "Starter Island mass is missing required static flags.");
            }

            foreach (var markerName in RequiredMarkers)
            {
                if (CountTransforms(prefab.transform, markerName) != 1)
                {
                    throw new InvalidOperationException(
                        $"PF_StarterIsland requires exactly one " +
                        $"'{markerName}' marker.");
                }
            }

            var renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var childRenderer in renderers)
            {
                if (IsWater(childRenderer.name))
                {
                    if (childRenderer.sharedMaterials.Length != 1 ||
                        childRenderer.sharedMaterial !=
                        materials["M_StarterIsland_WaterGuide"] ||
                        childRenderer.GetComponent<Collider>() != null)
                    {
                        throw new InvalidOperationException(
                            $"Water renderer '{childRenderer.name}' differs " +
                            "from the non-colliding water contract.");
                    }
                }
                else if (childRenderer.name.StartsWith(
                             "GEO_Path_",
                             StringComparison.Ordinal) &&
                         (childRenderer.sharedMaterials.Length != 1 ||
                          childRenderer.sharedMaterial !=
                          materials["M_StarterIsland_Dirt"]))
                {
                    throw new InvalidOperationException(
                        $"Path renderer '{childRenderer.name}' has an " +
                        "unexpected material.");
                }
            }
        }

        private static void BuildReviewScene(
            GameObject prefab,
            IReadOnlyList<GameObject> foliagePrefabs)
        {
            if (TryValidateExistingReviewScene(foliagePrefabs.Count))
            {
                EnsureReviewSceneInBuildSettings();
                return;
            }

            var previousActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                var reviewRoot = new GameObject(ReviewRootName);
                reviewRoot.AddComponent<GeneratedSceneRevision>().Configure(
                    ReviewSceneId,
                    ReviewSceneRevision);

                var island =
                    PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (island == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate PF_StarterIsland in review scene.");
                }

                island.name = "PF_StarterIsland";
                island.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                island.transform.localScale = Vector3.one;

                CreateLighting(
                    RequireAsset<Material>(
                        MaterialsRoot + "/M_StarterIsland_Skybox.mat"));
                ScatterFoliage(island, foliagePrefabs);
                CreateReviewPlayer(island);

                if (!EditorSceneManager.SaveScene(
                        scene,
                        ReviewScenePath))
                {
                    throw new InvalidOperationException(
                        $"Could not save review scene {ReviewScenePath}.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
            }

            EnsureReviewSceneInBuildSettings();
            if (!TryValidateExistingReviewScene(foliagePrefabs.Count))
            {
                throw new InvalidOperationException(
                    "Generated Starter Island review scene failed validation.");
            }
        }

        private static void CreateLighting(Material skybox)
        {
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Html("#8FD8E4");
            RenderSettings.ambientEquatorColor = Html("#B9D9C7");
            RenderSettings.ambientGroundColor = Html("#6E806C");
            RenderSettings.ambientIntensity = 1.08f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Html("#A9D8E3");
            RenderSettings.fogDensity = 0.0019f;

            var lightObject = new GameObject("ENV_Sun");
            lightObject.transform.rotation =
                Quaternion.Euler(46f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Html("#FFD4A3");
            light.intensity = 1.42f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.35f;
            RenderSettings.sun = light;
        }

        private static void ScatterFoliage(
            GameObject island,
            IReadOnlyList<GameObject> foliagePrefabs)
        {
            var foliageRoot = new GameObject(FoliageSceneRootName);
            var signature = new GameObject(
                $"ENV_Foliage_SourceCount_{foliagePrefabs.Count:00}");
            signature.transform.SetParent(foliageRoot.transform, false);
            if (foliagePrefabs.Count == 0)
            {
                Debug.Log(
                    "STARTER_ISLAND_FOLIAGE_SCATTER status=SKIPPED " +
                    "reason=prefabs_missing");
                return;
            }

            var trees = FilterFoliage(
                foliagePrefabs,
                "PF_Tree_");
            var shrubs = FilterFoliage(
                foliagePrefabs,
                "PF_Shrub_");
            var grass = FilterFoliage(
                foliagePrefabs,
                "PF_Grass_");
            var flowers = FilterFoliage(
                foliagePrefabs,
                "PF_Flower_");
            if (trees.Count == 0 ||
                shrubs.Count == 0 ||
                grass.Count == 0 ||
                flowers.Count == 0)
            {
                Debug.LogWarning(
                    "Starter Island foliage prefabs are incomplete. " +
                    "The review scene keeps an empty deterministic foliage " +
                    "root and will rebuild when the full kit is available.");
                return;
            }

            var islandCollider =
                island.GetComponentInChildren<MeshCollider>(true);
            if (islandCollider == null)
            {
                throw new InvalidOperationException(
                    "Cannot scatter foliage without the native island collider.");
            }

            var context = new ScatterContext(
                island,
                islandCollider,
                foliageRoot.transform,
                BuildFactoryRect(island.transform),
                RequireTransform(
                    island.transform,
                    "REF_PlayerSpawn").position,
                RequireTransform(
                    island.transform,
                    "REF_AirshipDock").position,
                RequireTransform(
                    island.transform,
                    "REF_TutorialCenter").position,
                RequireTransform(
                    island.transform,
                    "REF_FactoryCenter").position,
                RequireTransform(
                    island.transform,
                    "REF_PortalAnchor").position,
                RequireTransform(
                    island.transform,
                    "REF_SpringSource").position,
                RequireTransform(
                    island.transform,
                    "REF_PondCenter").position,
                new System.Random(0x53A71A4D));

            var treePositions = new List<Vector3>();
            var shrubPositions = new List<Vector3>();
            var grassPositions = new List<Vector3>();
            var flowerPositions = new List<Vector3>();
            var instanceIndex = 0;

            ScatterBatch(
                context,
                trees,
                FoliageZone.SpringForest,
                88,
                5.2f,
                0.82f,
                0.88f,
                1.14f,
                treePositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                trees,
                FoliageZone.Margin,
                118,
                5.2f,
                0.80f,
                0.86f,
                1.12f,
                treePositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                trees,
                FoliageZone.Interior,
                24,
                7.5f,
                0.84f,
                0.86f,
                1.08f,
                treePositions,
                ref instanceIndex);

            ScatterBatch(
                context,
                shrubs,
                FoliageZone.SpringForest,
                74,
                2.4f,
                0.72f,
                0.86f,
                1.18f,
                shrubPositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                shrubs,
                FoliageZone.Margin,
                86,
                2.4f,
                0.70f,
                0.82f,
                1.16f,
                shrubPositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                shrubs,
                FoliageZone.Interior,
                38,
                3.0f,
                0.78f,
                0.82f,
                1.10f,
                shrubPositions,
                ref instanceIndex);

            ScatterBatch(
                context,
                grass,
                FoliageZone.SpringForest,
                42,
                1.1f,
                0.66f,
                0.80f,
                1.28f,
                grassPositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                grass,
                FoliageZone.Margin,
                62,
                1.1f,
                0.64f,
                0.78f,
                1.24f,
                grassPositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                grass,
                FoliageZone.Interior,
                118,
                1.25f,
                0.72f,
                0.78f,
                1.18f,
                grassPositions,
                ref instanceIndex);

            ScatterBatch(
                context,
                flowers,
                FoliageZone.SpringForest,
                30,
                1.35f,
                0.70f,
                0.88f,
                1.16f,
                flowerPositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                flowers,
                FoliageZone.Margin,
                34,
                1.35f,
                0.68f,
                0.86f,
                1.14f,
                flowerPositions,
                ref instanceIndex);
            ScatterBatch(
                context,
                flowers,
                FoliageZone.Interior,
                76,
                1.55f,
                0.74f,
                0.86f,
                1.12f,
                flowerPositions,
                ref instanceIndex);

            Debug.Log(
                $"STARTER_ISLAND_FOLIAGE_SCATTER trees={treePositions.Count} " +
                $"shrubs={shrubPositions.Count} grass={grassPositions.Count} " +
                $"flowers={flowerPositions.Count} instances={instanceIndex} " +
                "factoryClear=1 sightlinesClear=1 status=PASS");
        }

        private static void ScatterBatch(
            ScatterContext context,
            IReadOnlyList<GameObject> prefabs,
            FoliageZone zone,
            int attempts,
            float minimumSpacing,
            float minimumNormalY,
            float minimumScale,
            float maximumScale,
            List<Vector3> acceptedPositions,
            ref int instanceIndex)
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var candidate = NextFoliageCandidate(context, zone);
                if (!IsFoliageCandidateClear(context, candidate) ||
                    !HasMinimumSpacing(
                        acceptedPositions,
                        candidate,
                        minimumSpacing))
                {
                    continue;
                }

                var ray = new Ray(
                    new Vector3(
                        candidate.x,
                        context.IslandCollider.bounds.max.y + 35f,
                        candidate.y),
                    Vector3.down);
                if (!context.IslandCollider.Raycast(
                        ray,
                        out var hit,
                        context.IslandCollider.bounds.size.y + 90f) ||
                    hit.normal.y < minimumNormalY)
                {
                    continue;
                }

                var prefab =
                    prefabs[context.Random.Next(prefabs.Count)];
                var instance = PrefabUtility.InstantiatePrefab(
                    prefab,
                    context.Island.scene) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"Could not scatter foliage prefab {prefab.name}.");
                }

                instance.name =
                    $"DEC_{prefab.name.Substring(3)}_{instanceIndex:000}";
                instance.transform.SetParent(
                    context.FoliageRoot,
                    true);
                instance.transform.position = hit.point;
                instance.transform.rotation = Quaternion.Euler(
                    0f,
                    NextFloat(context.Random, 0f, 360f),
                    0f);
                var scale = NextFloat(
                    context.Random,
                    minimumScale,
                    maximumScale);
                instance.transform.localScale = Vector3.one * scale;

                acceptedPositions.Add(hit.point);
                instanceIndex++;
            }
        }

        private static Vector2 NextFoliageCandidate(
            ScatterContext context,
            FoliageZone zone)
        {
            var angle =
                NextFloat(context.Random, 0f, Mathf.PI * 2f);
            switch (zone)
            {
                case FoliageZone.SpringForest:
                {
                    var radius =
                        Mathf.Sqrt(NextFloat(context.Random, 0f, 1f));
                    return new Vector2(
                        context.Spring.x +
                        Mathf.Cos(angle) * Mathf.Lerp(10f, 47f, radius),
                        context.Spring.z +
                        Mathf.Sin(angle) * Mathf.Lerp(8f, 34f, radius));
                }
                case FoliageZone.Margin:
                {
                    var radial =
                        NextFloat(context.Random, 0.64f, 0.88f);
                    var bounds = context.IslandCollider.bounds;
                    return new Vector2(
                        bounds.center.x +
                        Mathf.Cos(angle) * bounds.extents.x * radial,
                        bounds.center.z +
                        Mathf.Sin(angle) * bounds.extents.z * radial);
                }
                default:
                {
                    var bounds = context.IslandCollider.bounds;
                    return new Vector2(
                        NextFloat(
                            context.Random,
                            bounds.center.x - bounds.extents.x * 0.70f,
                            bounds.center.x + bounds.extents.x * 0.70f),
                        NextFloat(
                            context.Random,
                            bounds.center.z - bounds.extents.z * 0.70f,
                            bounds.center.z + bounds.extents.z * 0.70f));
                }
            }
        }

        private static bool IsFoliageCandidateClear(
            ScatterContext context,
            Vector2 candidate)
        {
            if (context.FactoryRect.Contains(candidate) ||
                Vector2.Distance(
                    candidate,
                    Xz(context.Dock)) < 21f ||
                Vector2.Distance(
                    candidate,
                    Xz(context.Portal)) < 15f ||
                Vector2.Distance(
                    candidate,
                    Xz(context.Pond)) < 22f ||
                Vector2.Distance(
                    candidate,
                    Xz(context.Spring)) < 8f)
            {
                return false;
            }

            foreach (var corridor in context.SightlineCorridors)
            {
                if (DistanceToSegment(
                        candidate,
                        corridor.Start,
                        corridor.End) < corridor.HalfWidth)
                {
                    return false;
                }
            }

            return true;
        }

        private static Rect BuildFactoryRect(Transform island)
        {
            var names = new[]
            {
                "REF_FactoryCorner_SW",
                "REF_FactoryCorner_SE",
                "REF_FactoryCorner_NW",
                "REF_FactoryCorner_NE"
            };
            var minimum = new Vector2(
                float.PositiveInfinity,
                float.PositiveInfinity);
            var maximum = new Vector2(
                float.NegativeInfinity,
                float.NegativeInfinity);
            foreach (var name in names)
            {
                var point = Xz(
                    RequireTransform(island, name).position);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }

            const float clearance = 5f;
            return Rect.MinMaxRect(
                minimum.x - clearance,
                minimum.y - clearance,
                maximum.x + clearance,
                maximum.y + clearance);
        }

        private static List<GameObject> FilterFoliage(
            IReadOnlyList<GameObject> source,
            string prefix)
        {
            var result = new List<GameObject>();
            for (var index = 0; index < source.Count; index++)
            {
                if (source[index] != null &&
                    source[index].name.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                {
                    result.Add(source[index]);
                }
            }

            return result;
        }

        private static bool HasMinimumSpacing(
            IReadOnlyList<Vector3> accepted,
            Vector2 candidate,
            float minimumSpacing)
        {
            var squared = minimumSpacing * minimumSpacing;
            for (var index = 0; index < accepted.Count; index++)
            {
                var delta =
                    Xz(accepted[index]) - candidate;
                if (delta.sqrMagnitude < squared)
                {
                    return false;
                }
            }

            return true;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
            {
                return Vector2.Distance(point, start);
            }

            var t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) /
                lengthSquared);
            return Vector2.Distance(
                point,
                start + segment * t);
        }

        private static float NextFloat(
            System.Random random,
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        private static Vector2 Xz(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void CreateReviewPlayer(GameObject island)
        {
            var spawn =
                RequireTransform(island.transform, "REF_PlayerSpawn");
            var factory =
                RequireTransform(island.transform, "REF_FactoryCenter");

            var player = new GameObject(PlayerName);
            player.transform.SetPositionAndRotation(
                spawn.position + Vector3.up * 0.06f,
                Quaternion.identity);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.30f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.32f;
            controller.skinWidth = 0.08f;
            controller.minMoveDistance = 0.001f;

            var yaw = new GameObject("ENV_ViewYaw");
            yaw.transform.SetParent(player.transform, false);
            yaw.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            var pitch = new GameObject("ENV_ViewPitch");
            pitch.transform.SetParent(yaw.transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pitch.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 900f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.depthTextureMode |= DepthTextureMode.Depth;
            cameraObject.AddComponent<AudioListener>();

            var input = player.AddComponent<StarterIslandReviewPlayer>();
            input.Configure(controller, yaw.transform, pitch.transform);
            input.SetLookDirection(factory.position - player.transform.position);
        }

        private static bool TryValidateExistingReviewScene(
            int expectedFoliageSourceCount)
        {
            var absoluteScenePath = Path.Combine(
                Application.dataPath,
                ReviewScenePath.Substring("Assets/".Length));
            if (!File.Exists(absoluteScenePath))
            {
                return false;
            }

            var scene = SceneManager.GetSceneByPath(ReviewScenePath);
            var openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(
                        ReviewScenePath,
                        OpenSceneMode.Additive);
                    openedHere = true;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                return ValidateReviewScene(
                    scene,
                    expectedFoliageSourceCount);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static bool ValidateReviewScene(
            Scene scene,
            int expectedFoliageSourceCount)
        {
            var reviewRoot = FindInScene(scene, ReviewRootName);
            var island = FindInScene(scene, "PF_StarterIsland");
            var player = FindInScene(scene, PlayerName);
            var sun = FindInScene(scene, "ENV_Sun");
            var foliageRoot =
                FindInScene(scene, FoliageSceneRootName);
            if (reviewRoot == null ||
                island == null ||
                player == null ||
                sun == null ||
                foliageRoot == null ||
                CountTransforms(
                    foliageRoot.transform,
                    $"ENV_Foliage_SourceCount_" +
                    $"{expectedFoliageSourceCount:00}") != 1)
            {
                return false;
            }

            var revision =
                reviewRoot.GetComponent<GeneratedSceneRevision>();
            var controller = player.GetComponent<CharacterController>();
            var input = player.GetComponent<StarterIslandReviewPlayer>();
            var camera = player.GetComponentInChildren<Camera>(true);
            var collider =
                island.GetComponentInChildren<MeshCollider>(true);
            if (revision == null ||
                !revision.Matches(ReviewSceneId, ReviewSceneRevision) ||
                controller == null ||
                input == null ||
                input.CharacterController != controller ||
                camera == null ||
                camera.transform.parent != input.PitchPivot ||
                collider == null ||
                island.GetComponentsInChildren<Collider>(true).Length != 1 ||
                island.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                sun.GetComponent<Light>() == null)
            {
                return false;
            }

            if (expectedFoliageSourceCount > 0 &&
                foliageRoot.transform.childCount <= 1)
            {
                return false;
            }

            foreach (var behaviour in
                     island.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                {
                    return false;
                }
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in
                         root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                    {
                        return false;
                    }

                    var typeName = behaviour.GetType().Name;
                    if (string.Equals(
                            typeName,
                            "AirshipObstacleIdentity",
                            StringComparison.Ordinal) ||
                        string.Equals(
                            typeName,
                            "AirshipTechnicalScenario",
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void EnsureReviewSceneInBuildSettings()
        {
            var scenes =
                new List<EditorBuildSettingsScene>(
                    EditorBuildSettings.scenes);
            for (var index = 0; index < scenes.Count; index++)
            {
                if (!string.Equals(
                        scenes[index].path,
                        ReviewScenePath,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!scenes[index].enabled)
                {
                    scenes[index] =
                        new EditorBuildSettingsScene(
                            ReviewScenePath,
                            true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }

                return;
            }

            scenes.Add(
                new EditorBuildSettingsScene(ReviewScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void RequireModelFile()
        {
            var absolutePath = Path.Combine(
                Application.dataPath,
                ModelPath.Substring("Assets/".Length));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    "The authored Starter Island FBX is not present yet. " +
                    "Run the Blender Starter Island builder first, then call " +
                    "CML.Editor.Art.StarterIslandAssetSetup.Run from Unity.",
                    ModelPath);
            }
        }

        private static void AssertIdentity(
            Transform transform,
            string context)
        {
            if (transform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(
                    transform.localRotation,
                    Quaternion.identity) > 0.001f ||
                (transform.localScale - Vector3.one).sqrMagnitude >
                0.000001f)
            {
                throw new InvalidOperationException(
                    $"{context} must have an identity transform.");
            }
        }

        private static void AssertIdentityScaleChain(
            Transform leaf,
            Transform root)
        {
            for (var current = leaf;
                 current != null;
                 current = current.parent)
            {
                if ((current.localScale - Vector3.one).sqrMagnitude >
                    0.000001f)
                {
                    throw new InvalidOperationException(
                        $"Collider ancestor '{current.name}' has a non-unit " +
                        "scale. Island dimensions must be authored in metres.");
                }

                if (current == root)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                "Island collider is not parented under the prefab root.");
        }

        private static bool IsWater(string objectName)
        {
            return objectName.StartsWith(
                       "GEO_Water_",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       objectName,
                       "GEO_Waterfall",
                       StringComparison.Ordinal);
        }

        private static long CountTriangles(Mesh mesh)
        {
            long triangles = 0;
            for (var subMesh = 0;
                 subMesh < mesh.subMeshCount;
                 subMesh++)
            {
                triangles += mesh.GetIndexCount(subMesh) / 3L;
            }

            return triangles;
        }

        private static int CountTransforms(
            Transform root,
            string name)
        {
            var count = string.Equals(
                root.name,
                name,
                StringComparison.Ordinal)
                ? 1
                : 0;
            for (var index = 0; index < root.childCount; index++)
            {
                count += CountTransforms(root.GetChild(index), name);
            }

            return count;
        }

        private static Transform RequireTransform(
            Transform root,
            string name)
        {
            var result = FindRecursive(root, name);
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"Starter Island is missing required transform '{name}'.");
            }

            return result;
        }

        private static Transform FindRecursive(
            Transform root,
            string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindRecursive(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindInScene(
            Scene scene,
            string objectName)
        {
            GameObject result = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(
                            transform.name,
                            objectName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (result != null)
                    {
                        return null;
                    }

                    result = transform.gameObject;
                }
            }

            return result;
        }

        private static T RequireComponent<T>(Transform transform)
            where T : Component
        {
            var component = transform.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"Transform '{transform.name}' requires component " +
                    $"{typeof(T).Name}.");
            }

            return component;
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    $"Required Unity asset is missing: {path}");
            }

            return asset;
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
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
            {
                throw new InvalidOperationException(
                    $"Invalid HTML color '{value}'.");
            }

            return color;
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

        private static void SetTexture(
            Material material,
            string property,
            Texture value)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, value);
            }
        }

        private static MaterialDefinition Opaque(
            string name,
            string baseColor,
            string secondaryColor,
            string wetColor,
            float vertexBlend)
        {
            return new MaterialDefinition(
                name,
                SurfaceShaderName,
                baseColor,
                secondaryColor,
                wetColor,
                vertexBlend,
                false);
        }

        private enum FoliageZone
        {
            SpringForest,
            Margin,
            Interior
        }

        private readonly struct SightlineCorridor
        {
            public SightlineCorridor(
                Vector3 start,
                Vector3 end,
                float halfWidth)
            {
                Start = Xz(start);
                End = Xz(end);
                HalfWidth = halfWidth;
            }

            public Vector2 Start { get; }
            public Vector2 End { get; }
            public float HalfWidth { get; }
        }

        private sealed class ScatterContext
        {
            public ScatterContext(
                GameObject island,
                MeshCollider islandCollider,
                Transform foliageRoot,
                Rect factoryRect,
                Vector3 player,
                Vector3 dock,
                Vector3 tutorial,
                Vector3 factory,
                Vector3 portal,
                Vector3 spring,
                Vector3 pond,
                System.Random random)
            {
                Island = island;
                IslandCollider = islandCollider;
                FoliageRoot = foliageRoot;
                FactoryRect = factoryRect;
                Player = player;
                Dock = dock;
                Tutorial = tutorial;
                Factory = factory;
                Portal = portal;
                Spring = spring;
                Pond = pond;
                Random = random;
                SightlineCorridors = new[]
                {
                    new SightlineCorridor(player, tutorial, 7f),
                    new SightlineCorridor(dock, tutorial, 7f),
                    new SightlineCorridor(tutorial, factory, 8f),
                    new SightlineCorridor(factory, portal, 8f),
                    new SightlineCorridor(factory, spring, 8f)
                };
            }

            public GameObject Island { get; }
            public MeshCollider IslandCollider { get; }
            public Transform FoliageRoot { get; }
            public Rect FactoryRect { get; }
            public Vector3 Player { get; }
            public Vector3 Dock { get; }
            public Vector3 Tutorial { get; }
            public Vector3 Factory { get; }
            public Vector3 Portal { get; }
            public Vector3 Spring { get; }
            public Vector3 Pond { get; }
            public System.Random Random { get; }
            public IReadOnlyList<SightlineCorridor> SightlineCorridors
            {
                get;
            }
        }

        private readonly struct MaterialDefinition
        {
            public MaterialDefinition(
                string name,
                string shaderName,
                string primaryColor,
                string secondaryColor,
                string wetColor,
                float vertexBlend,
                bool transparent)
            {
                Name = name;
                ShaderName = shaderName;
                PrimaryColor = primaryColor;
                SecondaryColor = secondaryColor;
                WetColor = wetColor;
                VertexBlend = vertexBlend;
                Transparent = transparent;
            }

            public string Name { get; }
            public string ShaderName { get; }
            public string PrimaryColor { get; }
            public string SecondaryColor { get; }
            public string WetColor { get; }
            public float VertexBlend { get; }
            public bool Transparent { get; }
        }
    }

    internal sealed class StarterIslandAssetPostprocessor : AssetPostprocessor
    {
        private const string FoliageModelsRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Foliage/Models/";
        private const string FoliageTexturesRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Foliage/Textures/";

        private void OnPreprocessModel()
        {
            var isIsland = string.Equals(
                assetPath,
                StarterIslandAssetSetup.ModelPath,
                StringComparison.Ordinal);
            var isFoliage = assetPath.StartsWith(
                FoliageModelsRoot,
                StringComparison.Ordinal);
            if (!isIsland && !isFoliage)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importConstraints = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents =
                ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.meshCompression =
                ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation =
                ModelImporterMaterialLocation.InPrefab;
            importer.materialName =
                ModelImporterMaterialName.BasedOnMaterialName;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(
                    FoliageTexturesRoot,
                    StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            var isMask = assetPath.EndsWith(
                "_Mask.png",
                StringComparison.OrdinalIgnoreCase);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !isMask;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 512;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
        }
    }
}

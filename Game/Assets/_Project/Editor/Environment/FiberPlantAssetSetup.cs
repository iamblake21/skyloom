using System;
using System.Collections.Generic;
using CML.Unity.Gathering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// CML.Editor.Art, not CML.Editor.Environment: declaring the latter would make
// `Environment` resolve to this namespace instead of System.Environment in
// every file under CML.Editor, and half the editor tooling reads environment
// variables. CML.Editor.EnvironmentAssets exists for the same reason.
namespace CML.Editor.Art
{
    /// <summary>
    /// Imports the wild fibre plant kit and the Plant Fibre item.
    ///
    /// The meshes carry their painted colour in the first vertex colour layer
    /// (<c>Tint</c>) and their maps are luminance only, which is exactly the
    /// contract the CloudTall Tree shader already implements. Reusing that
    /// shader is what keeps the plant in the island's painted language instead
    /// of introducing a second, flatter one.
    /// </summary>
    public static class FiberPlantAssetSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/Clutter/FiberPlant";
        public const string PlantLargePrefabPath =
            Root + "/Prefabs/PF_ENV_FiberPlant_Wild_A.prefab";
        public const string PlantSmallPrefabPath =
            Root + "/Prefabs/PF_ENV_FiberPlant_Wild_B.prefab";
        public const string ItemPrefabPath =
            Root + "/Prefabs/PF_Item_PlantFiber.prefab";
        public const string CarriedItemPrefabPath =
            "Assets/_Project/Resources/Items/PF_PlantFiber.prefab";

        private const string TreeShaderName =
            "CML/Environment/Starter Island CloudTall Tree";
        private const string LeafAtlasPath =
            Root + "/Textures/T_ENV_FiberPlant_LeafAtlas.png";
        private const string BundleAtlasPath =
            Root + "/Textures/T_ENV_FiberBundle.png";
        private const string LeafMaterialPath =
            Root + "/Materials/M_ENV_FiberPlant_Leaves.mat";
        private const string BundleMaterialPath =
            Root + "/Materials/M_ENV_FiberBundle.mat";
        private const string PlantLargeModelPath =
            Root + "/Models/ENV_FiberPlant_Wild_A.fbx";
        private const string PlantSmallModelPath =
            Root + "/Models/ENV_FiberPlant_Wild_B.fbx";
        private const string ItemModelPath =
            Root + "/Models/ENV_Item_PlantFiber.fbx";

        // Blender writes these names into the FBX; the remap keys have to match
        // them exactly or Unity keeps its own imported stand-ins.
        private const string LeafMaterialName = "M_ENV_FiberPlant_Leaves";
        private const string BundleMaterialName = "M_ENV_FiberBundle";

        /// <summary>
        /// Headless entry point: assets, then the inventory icon, then the
        /// scene placement, in the only order that works. The icon renders from
        /// the item prefab and the placement instantiates the plant prefabs, so
        /// both depend on <see cref="Run"/> having already run.
        /// </summary>
        public static void RunFullPipelineBatch()
        {
            Run();
            StickAssetSetup.Run();
            UI.ItemIconRenderPipeline.RenderMissingIconsBatch();
            FiberPlantSceneSetup.Run();
        }

        [MenuItem("CML/Art/Rebuild Fiber Plant Kit")]
        public static void Run()
        {
            EnsureFolder("Assets/_Project/Art/Environment/Clutter");
            EnsureFolder(Root);
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder("Assets/_Project/Resources/Items");

            ConfigureTexture(LeafAtlasPath, alphaIsTransparency: true);
            ConfigureTexture(BundleAtlasPath, alphaIsTransparency: false);

            var shader = Shader.Find(TreeShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Shader '{TreeShaderName}' is unavailable.");
            }

            var leafMaterial = BuildLeafMaterial(shader);
            var bundleMaterial = BuildBundleMaterial(shader);

            foreach (var modelPath in new[]
                     {
                         PlantLargeModelPath,
                         PlantSmallModelPath,
                         ItemModelPath
                     })
            {
                ConfigureModelImporter(modelPath);
            }

            RemapMaterial(PlantLargeModelPath, LeafMaterialName, leafMaterial);
            RemapMaterial(PlantSmallModelPath, LeafMaterialName, leafMaterial);
            RemapMaterial(ItemModelPath, BundleMaterialName, bundleMaterial);

            // Knee-high plants must never block the player, so their collider
            // is a trigger. FactoryCentralInteractor queries with
            // QueryTriggerInteraction.Collide, so gathering still aims at them.
            BuildPlantPrefab(
                PlantLargeModelPath,
                PlantLargePrefabPath,
                leafMaterial,
                height: 0.88f,
                radius: 0.34f);
            BuildPlantPrefab(
                PlantSmallModelPath,
                PlantSmallPrefabPath,
                leafMaterial,
                height: 0.59f,
                radius: 0.26f);
            BuildItemPrefab(ItemModelPath, ItemPrefabPath, bundleMaterial);
            BuildItemPrefab(
                ItemModelPath, CarriedItemPrefabPath, bundleMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var report = Validate();
            Debug.Log(
                "FIBER_PLANT_KIT " + report + " status=PASS");
        }

        private static Material BuildLeafMaterial(Shader shader)
        {
            var atlas = RequireAsset<Texture2D>(LeafAtlasPath);
            var material = LoadOrCreateMaterial(LeafMaterialPath, shader);
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            // Two-sided: a leaf is one surface and has to be lit from beneath.
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 1f);
            // The tree wind is authored over eight metres above a two metre
            // trunk. A rosette is under a metre tall and starts at the ground,
            // so the same numbers would leave it rigid.
            material.SetVector("_WindDirection", new Vector4(0.82f, 0f, 0.57f, 0f));
            material.SetFloat("_WindStrength", 0.075f);
            material.SetFloat("_WindSpeed", 1.15f);
            material.SetFloat("_WindGustStrength", 0.42f);
            material.SetFloat("_WindFlutterStrength", 0.018f);
            material.SetFloat("_WindBaseHeight", 0.06f);
            material.SetFloat("_WindHeight", 0.85f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.enableInstancing = true;
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildBundleMaterial(Shader shader)
        {
            var atlas = RequireAsset<Texture2D>(BundleAtlasPath);
            var material = LoadOrCreateMaterial(BundleMaterialPath, shader);
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.06f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            // The bundle map is fully opaque; a near-zero cutoff keeps the
            // shared clip harmless while the queue stays in Geometry.
            material.SetFloat("_Cutoff", 0.02f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_ZWrite", 1f);
            // A cut bundle lying on the ground does not sway.
            material.SetVector("_WindDirection", new Vector4(0.82f, 0f, 0.57f, 0f));
            material.SetFloat("_WindStrength", 0f);
            material.SetFloat("_WindGustStrength", 0f);
            material.SetFloat("_WindFlutterStrength", 0f);
            material.SetFloat("_WindBaseHeight", 0f);
            material.SetFloat("_WindHeight", 1f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");
            material.enableInstancing = true;
            material.doubleSidedGI = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        private static void ConfigureTexture(
            string path,
            bool alphaIsTransparency)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load TextureImporter for {path}.");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.mipmapEnabled = true;
            // Clamp, not repeat: the atlas holds separate cells and a repeat
            // wrap bleeds one leaf variant into its neighbour at the border.
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }

        private static void ConfigureModelImporter(string path)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {path}.");
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importConstraints = false;
            // Import, never recalculate: the smooth normals authored in Blender
            // are the whole reason the surface reads as painted rather than
            // faceted, and the cut sections carry their own hard edges.
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = false;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation =
                ModelImporterMaterialLocation.External;
            importer.materialName =
                ModelImporterMaterialName.BasedOnMaterialName;
            importer.SaveAndReimport();
        }

        private static void RemapMaterial(
            string modelPath,
            string materialName,
            Material material)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {modelPath}.");
            }

            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    materialName),
                material);
            importer.SaveAndReimport();
        }

        private static void BuildPlantPrefab(
            string modelPath,
            string prefabPath,
            Material material,
            float height,
            float radius)
        {
            var instance = InstantiateModel(modelPath);
            try
            {
                instance.name =
                    System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                ApplyMaterial(instance, material);

                var collider = instance.AddComponent<CapsuleCollider>();
                collider.isTrigger = true;
                collider.direction = 1;
                collider.height = height;
                collider.radius = radius;
                collider.center = new Vector3(0f, height * 0.5f, 0f);

                // Identity first: the prompt component requires it. The scene
                // placement then gives each instance its stable id and yield.
                instance.AddComponent<HandGatherSourceIdentity>();
                instance.AddComponent<HandGatherInteractionTarget>();
                instance.AddComponent<HandGatherHighlight>();

                SavePrefab(instance, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void BuildItemPrefab(
            string modelPath,
            string prefabPath,
            Material material)
        {
            var instance = InstantiateModel(modelPath);
            try
            {
                instance.name =
                    System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                ApplyMaterial(instance, material);
                SavePrefab(instance, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static GameObject InstantiateModel(string modelPath)
        {
            var source = RequireAsset<GameObject>(modelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate {modelPath}.");
            }

            PrefabUtility.UnpackPrefabInstance(
                instance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            instance.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static void ApplyMaterial(GameObject instance, Material material)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{instance.name} has no renderer.");
            }

            foreach (var renderer in renderers)
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (var index = 0; index < materials.Length; index++)
                {
                    materials[index] = material;
                }

                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static void SavePrefab(GameObject instance, string prefabPath)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Could not save {prefabPath}.");
            }
        }

        private static string Validate()
        {
            var parts = new List<string>();
            // Authored height in Blender, which is +Z there and must land on
            // Unity's +Y. Measuring it is the only way to catch an axis that
            // arrived rotated: the prefab root reads identity either way,
            // because bakeAxisConversion writes the rotation into the vertices.
            foreach (var pair in new[]
                     {
                         (PlantLargePrefabPath, true, 0.883f),
                         (PlantSmallPrefabPath, true, 0.590f),
                         (ItemPrefabPath, false, 0.120f),
                         (CarriedItemPrefabPath, false, 0.120f)
                     })
            {
                var prefab = RequireAsset<GameObject>(pair.Item1);
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                var triangles = 0;
                var coloured = 0;
                var size = Vector3.zero;
                foreach (var filter in filters)
                {
                    var mesh = filter.sharedMesh;
                    if (mesh == null)
                    {
                        throw new InvalidOperationException(
                            $"{pair.Item1} has a MeshFilter without a mesh.");
                    }

                    triangles += mesh.triangles.Length / 3;
                    size = Vector3.Max(size, mesh.bounds.size);
                    if (mesh.colors32 != null && mesh.colors32.Length > 0)
                    {
                        coloured++;
                    }
                }

                if (Mathf.Abs(size.y - pair.Item3) > pair.Item3 * 0.12f)
                {
                    throw new InvalidOperationException(
                        $"{pair.Item1} stands {size.y:F3} m tall but was " +
                        $"authored at {pair.Item3:F3} m " +
                        $"(imported size {size.x:F3} x {size.y:F3} x " +
                        $"{size.z:F3}). The mesh arrived rotated.");
                }

                if (coloured != filters.Length)
                {
                    throw new InvalidOperationException(
                        $"{pair.Item1} lost its Tint vertex colours on import; " +
                        "the painted palette lives there and the plant would " +
                        "render white.");
                }

                foreach (var renderer in
                         prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.sharedMaterial == null)
                    {
                        throw new InvalidOperationException(
                            $"{pair.Item1} has a renderer without a material.");
                    }
                }

                if (pair.Item2
                    && prefab.GetComponent<CapsuleCollider>() == null)
                {
                    throw new InvalidOperationException(
                        $"{pair.Item1} is missing its gather collider.");
                }

                parts.Add(
                    $"{System.IO.Path.GetFileNameWithoutExtension(pair.Item1)}" +
                    $"={triangles}tris/{size.x:F2}x{size.y:F2}x{size.z:F2}");
            }

            return string.Join(" ", parts);
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Missing asset at {path}.");
            }

            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = System.IO.Path.GetDirectoryName(path)
                ?.Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                throw new InvalidOperationException(
                    $"Cannot create folder {path}.");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

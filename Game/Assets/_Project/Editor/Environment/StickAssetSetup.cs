using System;
using System.Collections.Generic;
using CML.Unity.Gathering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// CML.Editor.Art for the same reason as FiberPlantAssetSetup: declaring
// CML.Editor.Environment would shadow System.Environment across the whole
// editor assembly.
namespace CML.Editor.Art
{
    /// <summary>
    /// Imports the fallen sticks and the Bastone item.
    ///
    /// Opaque, unlike the fibre plant: a stick is a solid shaft, so the shared
    /// CloudTall Tree shader runs here with alpha clip off and the wind at
    /// zero. Wood on the ground does not sway.
    /// </summary>
    public static class StickAssetSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/Clutter/Sticks";
        public const string ScatterLargePrefabPath =
            Root + "/Prefabs/PF_ENV_FallenSticks_A.prefab";
        public const string ScatterSmallPrefabPath =
            Root + "/Prefabs/PF_ENV_FallenSticks_B.prefab";
        public const string ItemPrefabPath =
            Root + "/Prefabs/PF_Item_Stick.prefab";
        public const string CarriedItemPrefabPath =
            "Assets/_Project/Resources/Items/PF_Stick.prefab";

        private const string TreeShaderName =
            "CML/Environment/Starter Island CloudTall Tree";
        private const string BarkAtlasPath =
            Root + "/Textures/T_ENV_Stick_Bark.png";
        private const string BarkMaterialPath =
            Root + "/Materials/M_ENV_Stick_Bark.mat";
        private const string BarkMaterialName = "M_ENV_Stick_Bark";

        // The two scatter meshes are gone. The kit used to ship piles of five
        // and three sticks as the gatherable props; what stands in the world
        // now is the same single stick the player picks up, so there is one
        // mesh here and the scatter prefabs -- kept, because the review scene
        // has instances of them placed -- were repointed at it by hand.
        private static readonly (string Model, string Prefab, bool Gatherable)[]
            Assets =
            {
                (Root + "/Models/ENV_Item_Stick.fbx", ItemPrefabPath, false),
                (Root + "/Models/ENV_Item_Stick.fbx",
                    CarriedItemPrefabPath, false)
            };

        [MenuItem("CML/Art/Rebuild Stick Kit")]
        public static void Run()
        {
            EnsureFolder("Assets/_Project/Art/Environment/Clutter");
            EnsureFolder(Root);
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder("Assets/_Project/Resources/Items");

            ConfigureTexture(BarkAtlasPath);
            var shader = Shader.Find(TreeShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Shader '{TreeShaderName}' is unavailable.");
            }

            var material = BuildBarkMaterial(shader);
            var models = new HashSet<string>();
            foreach (var asset in Assets)
            {
                models.Add(asset.Model);
            }

            foreach (var model in models)
            {
                ConfigureModelImporter(model);
                RemapMaterial(model, BarkMaterialName, material);
            }

            foreach (var asset in Assets)
            {
                BuildPrefab(
                    asset.Model,
                    asset.Prefab,
                    material,
                    asset.Gatherable);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("STICK_KIT " + Validate() + " status=PASS");
        }

        private static Material BuildBarkMaterial(Shader shader)
        {
            var atlas = RequireAsset<Texture2D>(BarkAtlasPath);
            var material = LoadOrCreateMaterial(BarkMaterialPath, shader);
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.05f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cutoff", 0.02f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_ZWrite", 1f);
            material.SetVector(
                "_WindDirection", new Vector4(0.82f, 0f, 0.57f, 0f));
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

        private static void BuildPrefab(
            string modelPath,
            string prefabPath,
            Material material,
            bool gatherable)
        {
            var source = RequireAsset<GameObject>(modelPath);
            var instance =
                PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate {modelPath}.");
            }

            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.name =
                    System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                instance.transform.SetPositionAndRotation(
                    Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in
                         instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials =
                        new Material[renderer.sharedMaterials.Length];
                    for (var index = 0; index < materials.Length; index++)
                    {
                        materials[index] = material;
                    }

                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                if (gatherable)
                {
                    // Sticks lie flat, so the aiming volume is a shallow box
                    // over the scatter rather than a capsule. It is a trigger:
                    // the player has to be able to walk over them.
                    var bounds = RendererBounds(instance);
                    var collider = instance.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.center = new Vector3(
                        bounds.center.x, 0.11f, bounds.center.z);
                    collider.size = new Vector3(
                        Mathf.Max(bounds.size.x * 0.85f, 0.25f),
                        0.22f,
                        Mathf.Max(bounds.size.z * 0.85f, 0.25f));
                    instance.AddComponent<HandGatherSourceIdentity>();
                    instance.AddComponent<HandGatherInteractionTarget>();
                    instance.AddComponent<HandGatherHighlight>();
                }

                if (PrefabUtility.SaveAsPrefabAsset(instance, prefabPath)
                    == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save {prefabPath}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Bounds RendererBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{instance.name} has no renderer.");
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static string Validate()
        {
            var parts = new List<string>();
            foreach (var asset in Assets)
            {
                var prefab = RequireAsset<GameObject>(asset.Prefab);
                var triangles = 0;
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                foreach (var filter in filters)
                {
                    if (filter.sharedMesh == null)
                    {
                        throw new InvalidOperationException(
                            $"{asset.Prefab} has a MeshFilter without a mesh.");
                    }

                    if (filter.sharedMesh.colors32 == null
                        || filter.sharedMesh.colors32.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{asset.Prefab} lost its Tint vertex colours on " +
                            "import; the bark palette lives there.");
                    }

                    triangles += filter.sharedMesh.triangles.Length / 3;
                }

                if (asset.Gatherable
                    && prefab.GetComponent<HandGatherSourceIdentity>() == null)
                {
                    throw new InvalidOperationException(
                        $"{asset.Prefab} is missing its gather identity.");
                }

                parts.Add(
                    System.IO.Path.GetFileNameWithoutExtension(asset.Prefab)
                    + $"={triangles}tris");
            }

            return string.Join(" ", parts);
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

        private static void ConfigureTexture(string path)
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
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
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
                    typeof(Material), materialName),
                material);
            importer.SaveAndReimport();
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
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

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity-side setup for the definitive crude pickaxe.
    /// Blender owns geometry, hierarchy and UVs; Unity owns import settings,
    /// the shared Manual Era material and the reusable prefab.
    /// </summary>
    public static class CrudePickaxeAssetSetup
    {
        private const string ToolRoot = "Assets/_Project/Art/Tools/Pickaxe";
        private const string SharedRoot = "Assets/_Project/Art/Shared/ManualEra";
        private const string ModelPath = ToolRoot + "/Models/TOOL_PickaxeCrude.fbx";
        private const string PrefabsRoot = ToolRoot + "/Prefabs";
        private const string PrefabPath = PrefabsRoot + "/PF_PickaxeCrude.prefab";
        private const string MaterialsRoot = SharedRoot + "/Materials";
        private const string MaterialPath = MaterialsRoot + "/M_ManualEra_OpaqueAtlas.mat";
        private const string TextureRoot = SharedRoot + "/Textures";
        private const string BaseTexturePath = TextureRoot + "/T_ManualEra_BaseColor.png";
        private const string MaskTexturePath = TextureRoot + "/T_ManualEra_Mask.png";

        private static readonly string[] RequiredTransforms =
        {
            "GEO_Handle",
            "GEO_StoneHead_Active",
            "GEO_StoneHead_Back",
            "GEO_Binding_Head",
            "GEO_Binding_Grip",
            "REF_GripPrimary",
            "REF_GripSupport",
            "REF_ImpactTip",
            "REF_ImpactBack"
        };

        [MenuItem("CML/Art/Rebuild Crude Pickaxe Asset")]
        public static void Run()
        {
            EnsureFolder(PrefabsRoot);
            EnsureFolder(MaterialsRoot);

            AssetDatabase.ImportAsset(
                BaseTexturePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                MaskTexturePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The URP/Lit shader is unavailable. Verify the active render pipeline.");
            }

            var baseTexture = RequireAsset<Texture2D>(BaseTexturePath);
            var maskTexture = RequireAsset<Texture2D>(MaskTexturePath);
            var material = UpsertMaterial(shader, baseTexture, maskTexture);
            ConfigureModelMaterialRemap(material);
            var prefab = BuildPrefab(material);
            ValidatePrefab(prefab, material);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;

            var metrics = CollectMetrics(prefab);
            Debug.Log(
                $"PICKAXE_UNITY_VALIDATION prefab={PrefabPath} " +
                $"renderers={metrics.RendererCount} meshes={metrics.MeshCount} " +
                $"triangles={metrics.TriangleCount} " +
                $"bounds=({metrics.Bounds.size.x:F4},{metrics.Bounds.size.y:F4}," +
                $"{metrics.Bounds.size.z:F4}) markers=4 stones=2");
        }

        private static Material UpsertMaterial(
            Shader shader,
            Texture2D baseTexture,
            Texture2D maskTexture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(MaterialPath)
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            SetTexture(material, "_BaseMap", baseTexture);
            SetTexture(material, "_MainTex", baseTexture);
            SetColor(material, "_BaseColor", Color.white);
            SetColor(material, "_Color", Color.white);
            SetTexture(material, "_MetallicGlossMap", maskTexture);
            SetFloat(material, "_Metallic", 1f);
            SetFloat(material, "_Smoothness", 1f);
            SetFloat(material, "_SmoothnessTextureChannel", 0f);
            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", (float)CullMode.Back);
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_ReceiveShadows", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureModelMaterialRemap(Material material)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not load ModelImporter for: {ModelPath}");
            }

            importer.AddRemap(
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    "M_ManualEra_OpaqueAtlas"),
                material);
            importer.SaveAndReimport();
        }

        private static GameObject BuildPrefab(Material material)
        {
            var source = RequireAsset<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate model asset: {ModelPath}");
            }

            try
            {
                instance.name = "PF_PickaxeCrude";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var assigned = new Material[renderer.sharedMaterials.Length];
                    for (var index = 0; index < assigned.Length; index++)
                    {
                        assigned[index] = material;
                    }

                    renderer.sharedMaterials = assigned;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Could not save prefab: {PrefabPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ValidatePrefab(GameObject prefab, Material expectedMaterial)
        {
            foreach (var transformName in RequiredTransforms)
            {
                if (FindRecursive(prefab.transform, transformName) == null)
                {
                    throw new InvalidOperationException(
                        $"Crude pickaxe prefab is missing required transform '{transformName}'.");
                }
            }

            if (prefab.transform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity) > 0.001f ||
                (prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException("Crude pickaxe prefab root transform is not identity.");
            }

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != expectedMaterial)
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{HierarchyPath(renderer.transform)}' does not use " +
                            "the shared Manual Era material.");
                    }
                }
            }

            var metrics = CollectMetrics(prefab);
            if (metrics.RendererCount != 5 || metrics.MeshCount != 5)
            {
                throw new InvalidOperationException(
                    $"Crude pickaxe must contain five render meshes; found " +
                    $"{metrics.RendererCount} renderers and {metrics.MeshCount} meshes.");
            }

            if (metrics.TriangleCount <= 0 || metrics.TriangleCount > 3500)
            {
                throw new InvalidOperationException(
                    $"Crude pickaxe triangle count is outside budget: {metrics.TriangleCount}.");
            }

            var size = metrics.Bounds.size;
            if (!Approximately(size.x, 0.1765f, 0.003f) ||
                !Approximately(size.y, 0.9200f, 0.005f) ||
                !Approximately(size.z, 0.7650f, 0.005f))
            {
                throw new InvalidOperationException(
                    $"Unexpected Unity bounds ({size.x:F4}, {size.y:F4}, {size.z:F4}). " +
                    "Expected approximately (0.1765, 0.9200, 0.7650) metres.");
            }

            var grip = FindRecursive(prefab.transform, "REF_GripPrimary");
            var impact = FindRecursive(prefab.transform, "REF_ImpactTip");
            var gripPosition = prefab.transform.InverseTransformPoint(grip.position);
            var impactPosition = prefab.transform.InverseTransformPoint(impact.position);
            if (gripPosition.sqrMagnitude > 0.000025f)
            {
                throw new InvalidOperationException(
                    $"Primary grip is not at the authored origin: {gripPosition}.");
            }

            var expectedImpact = new Vector3(0f, 0.674f, 0.450f);
            if (Vector3.Distance(impactPosition, expectedImpact) > 0.005f)
            {
                throw new InvalidOperationException(
                    $"Impact marker {impactPosition} differs from expected {expectedImpact}.");
            }
        }

        private static PickaxeMetrics CollectMetrics(GameObject root)
        {
            var bounds = new Bounds();
            var hasBounds = false;
            var rendererCount = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                rendererCount++;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            var meshCount = 0;
            long triangleCount = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                meshCount++;
                foreach (var subMesh in filter.sharedMesh.subMeshCount == 0
                             ? Array.Empty<int>()
                             : BuildSubMeshIndices(filter.sharedMesh.subMeshCount))
                {
                    triangleCount += filter.sharedMesh.GetIndexCount(subMesh) / 3L;
                }
            }

            return new PickaxeMetrics(
                rendererCount,
                meshCount,
                triangleCount,
                hasBounds ? bounds : new Bounds());
        }

        private static int[] BuildSubMeshIndices(int count)
        {
            var indices = new int[count];
            for (var index = 0; index < count; index++)
            {
                indices[index] = index;
            }

            return indices;
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindRecursive(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static string HierarchyPath(Transform transform)
        {
            var path = transform.name;
            for (var current = transform.parent; current != null; current = current.parent)
            {
                path = current.name + "/" + path;
            }

            return path;
        }

        private static bool Approximately(float actual, float expected, float tolerance)
        {
            return Mathf.Abs(actual - expected) <= tolerance;
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    $"Required Unity asset is missing or failed to import: {path}");
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

        private static void SetTexture(Material material, string property, Texture value)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, value);
            }
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private readonly struct PickaxeMetrics
        {
            public PickaxeMetrics(
                int rendererCount,
                int meshCount,
                long triangleCount,
                Bounds bounds)
            {
                RendererCount = rendererCount;
                MeshCount = meshCount;
                TriangleCount = triangleCount;
                Bounds = bounds;
            }

            public int RendererCount { get; }
            public int MeshCount { get; }
            public long TriangleCount { get; }
            public Bounds Bounds { get; }
        }
    }

    internal sealed class CrudePickaxeAssetPostprocessor : AssetPostprocessor
    {
        private const string ModelPath =
            "Assets/_Project/Art/Tools/Pickaxe/Models/TOOL_PickaxeCrude.fbx";
        private const string TextureRoot =
            "Assets/_Project/Art/Shared/ManualEra/Textures/";

        private void OnPreprocessModel()
        {
            if (!string.Equals(assetPath, ModelPath, StringComparison.Ordinal))
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
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureRoot, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            var isMask = Path.GetFileNameWithoutExtension(assetPath)
                .EndsWith("_Mask", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !isMask;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
        }
    }
}

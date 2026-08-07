using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Unity-side production setup for the Mechanical Era press.
    ///
    /// Blender owns geometry, UVs, hierarchy and the animation/port markers.
    /// Unity owns deterministic import settings, URP materials, compound collision
    /// and the reusable prefab consumed by the M0.4B scene.
    /// </summary>
    public static class MechanicalPressAssetSetup
    {
        private const string Root = "Assets/_Project/Art/MechanicalEra";
        private const string ModelPath = Root + "/Models/MEC_MechanicalPress.fbx";
        private const string TextureRoot = Root + "/Textures";
        private const string MaterialRoot = Root + "/Materials";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string PrefabPath = PrefabRoot + "/PF_MechanicalPress.prefab";

        private const string WoodTexturePath =
            TextureRoot + "/T_MechanicalPress_Wood_BaseColor.png";
        private const string IronTexturePath =
            TextureRoot + "/T_MechanicalPress_Iron_BaseColor.png";
        private const string BronzeTexturePath =
            TextureRoot + "/T_MechanicalPress_Bronze_BaseColor.png";

        private const string WoodMaterialPath =
            MaterialRoot + "/M_MechanicalPress_Wood.mat";
        private const string IronMaterialPath =
            MaterialRoot + "/M_MechanicalPress_Iron.mat";
        private const string BronzeMaterialPath =
            MaterialRoot + "/M_MechanicalPress_Bronze.mat";
        private const float ItemTransferHeight = 0.60f;

        private static readonly string[] RequiredMarkers =
        {
            "ANM_PressRam",
            "PORT_ItemIn",
            "PORT_ItemOut",
            "REF_Interact",
            "REF_Workpiece"
        };

        private static readonly string[] RequiredStaticParts =
        {
            "GEO_VerticalDriveHousing",
            "GEO_VerticalDriveCylinder",
            "GEO_RamGuide_L",
            "GEO_RamGuide_R"
        };

        private static readonly string[] RequiredRamParts =
        {
            "GEO_PressRam_PistonRod",
            "GEO_PressRam_Crosshead",
            "GEO_PressRam_CrossheadFace",
            "GEO_PressRam_GuideShoe_L",
            "GEO_PressRam_GuideShoe_R",
            "GEO_PressRam_DieStem",
            "GEO_PressRam_Die",
            "GEO_PressRam_WorkingFace"
        };

        private static readonly string[] ForbiddenLegacyDriveFragments =
        {
            "DriveFlywheel",
            "DriveShaft",
            "PressLink",
            "RamClevis",
            "Eccentric"
        };

        private static readonly ColliderDefinition[] CompoundColliders =
        {
            // Four timber posts. Keeping these separate leaves the working throat
            // visually and physically open instead of sealing the machine in a box.
            new ColliderDefinition(
                "COL_Post_LF",
                new Vector3(-0.66f, 1.285f, -0.43f),
                new Vector3(0.24f, 2.18f, 0.24f)),
            new ColliderDefinition(
                "COL_Post_RF",
                new Vector3(0.66f, 1.285f, -0.43f),
                new Vector3(0.24f, 2.18f, 0.24f)),
            new ColliderDefinition(
                "COL_Post_LB",
                new Vector3(-0.66f, 1.285f, 0.43f),
                new Vector3(0.24f, 2.18f, 0.24f)),
            new ColliderDefinition(
                "COL_Post_RB",
                new Vector3(0.66f, 1.285f, 0.43f),
                new Vector3(0.24f, 2.18f, 0.24f)),
            new ColliderDefinition(
                "COL_Crown",
                new Vector3(0f, 2.345f, 0f),
                new Vector3(1.58f, 0.27f, 1.08f)),
            new ColliderDefinition(
                "COL_LowerFrame",
                new Vector3(0f, 0.24f, 0f),
                new Vector3(1.58f, 0.30f, 1.28f)),
            new ColliderDefinition(
                "COL_PressBed",
                new Vector3(0f, 0.565f, 0f),
                new Vector3(1.14f, 0.14f, 0.84f)),
            new ColliderDefinition(
                "COL_Ram",
                new Vector3(0f, 1.33f, 0f),
                new Vector3(1.05f, 0.92f, 0.62f))
        };

        [MenuItem("CML/Art/Rebuild Mechanical Press")]
        public static void Run()
        {
            RequireFile(ModelPath);
            RequireFile(WoodTexturePath);
            RequireFile(IronTexturePath);
            RequireFile(BronzeTexturePath);
            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);

            ConfigureTexture(WoodTexturePath);
            ConfigureTexture(IronTexturePath);
            ConfigureTexture(BronzeTexturePath);
            ConfigureModelImporter();

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is unavailable.");
            }

            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                ["M_MechanicalPress_Wood"] = UpsertMaterial(
                    WoodMaterialPath,
                    shader,
                    RequireAsset<Texture2D>(WoodTexturePath),
                    roughness: 0.72f,
                    metallic: 0f),
                ["M_MechanicalPress_Iron"] = UpsertMaterial(
                    IronMaterialPath,
                    shader,
                    RequireAsset<Texture2D>(IronTexturePath),
                    roughness: 0.46f,
                    metallic: 0.34f),
                ["M_MechanicalPress_Bronze"] = UpsertMaterial(
                    BronzeMaterialPath,
                    shader,
                    RequireAsset<Texture2D>(BronzeTexturePath),
                    roughness: 0.42f,
                    metallic: 0.52f)
            };

            ConfigureMaterialRemaps(materials);
            var prefab = BuildPrefab(materials);
            ValidatePrefab(prefab, materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            Debug.Log(
                "MECHANICAL_PRESS_UNITY_VALIDATION " +
                $"prefab={PrefabPath} markers={RequiredMarkers.Length} " +
                $"boxColliders={CompoundColliders.Length} status=PASS");
        }

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {ModelPath}.");
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
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.SaveAndReimport();
        }

        private static void ConfigureMaterialRemaps(
            IReadOnlyDictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {ModelPath}.");
            }

            foreach (var pair in materials)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(
                        typeof(Material),
                        pair.Key),
                    pair.Value);
            }

            importer.SaveAndReimport();
        }

        private static GameObject BuildPrefab(
            IReadOnlyDictionary<string, Material> materials)
        {
            var source = RequireAsset<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate {ModelPath}.");
            }

            try
            {
                instance.name = "PF_MechanicalPress";
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                SetMarkerWorldHeight(
                    instance.transform,
                    "PORT_ItemIn",
                    ItemTransferHeight);
                SetMarkerWorldHeight(
                    instance.transform,
                    "PORT_ItemOut",
                    ItemTransferHeight);

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var assigned = renderer.sharedMaterials;
                    for (var index = 0; index < assigned.Length; index++)
                    {
                        var sourceName = assigned[index] != null
                            ? assigned[index].name
                            : string.Empty;
                        if (!materials.TryGetValue(sourceName, out var material))
                        {
                            throw new InvalidDataException(
                                $"Renderer '{HierarchyPath(renderer.transform)}' uses " +
                                $"unsupported material '{sourceName}'.");
                        }

                        assigned[index] = material;
                    }

                    renderer.sharedMaterials = assigned;
                }

                foreach (var definition in CompoundColliders)
                {
                    var holder = new GameObject(definition.Name);
                    holder.transform.SetParent(instance.transform, false);
                    var collider = holder.AddComponent<BoxCollider>();
                    collider.center = definition.Center;
                    collider.size = definition.Size;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save {PrefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ValidatePrefab(
            GameObject prefab,
            IReadOnlyDictionary<string, Material> materials)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            foreach (var marker in RequiredMarkers)
            {
                var transform = FindChild(prefab.transform, marker);
                if (transform == null)
                {
                    throw new InvalidDataException(
                        $"Mechanical press is missing '{marker}'.");
                }
            }

            var ram = FindChild(prefab.transform, "ANM_PressRam");
            var ramRenderers = ram.GetComponentsInChildren<Renderer>(true);
            if (ramRenderers.Length != RequiredRamParts.Length)
            {
                throw new InvalidDataException(
                    $"ANM_PressRam owns {ramRenderers.Length} renderers; " +
                    $"expected exactly {RequiredRamParts.Length} vertical moving parts.");
            }

            foreach (var partName in RequiredRamParts)
            {
                var part = FindChild(prefab.transform, partName);
                if (part == null || !IsDescendantOf(part, ram))
                {
                    throw new InvalidDataException(
                        $"Movable part '{partName}' must be under ANM_PressRam.");
                }
            }

            foreach (var partName in RequiredStaticParts)
            {
                var part = FindChild(prefab.transform, partName);
                if (part == null)
                {
                    throw new InvalidDataException(
                        $"Mechanical press is missing static part '{partName}'.");
                }

                if (IsDescendantOf(part, ram))
                {
                    throw new InvalidDataException(
                        $"Static part '{partName}' cannot move with ANM_PressRam.");
                }
            }

            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                foreach (var fragment in ForbiddenLegacyDriveFragments)
                {
                    if (transform.name.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                    {
                        throw new InvalidDataException(
                            $"Obsolete horizontal-drive part remains: '{transform.name}'.");
                    }
                }
            }

            AssertNear(
                FindChild(prefab.transform, "PORT_ItemIn").position.y,
                ItemTransferHeight,
                0.001f,
                "input transfer height");
            AssertNear(
                FindChild(prefab.transform, "PORT_ItemOut").position.y,
                ItemTransferHeight,
                0.001f,
                "output transfer height");
            var itemIn = FindChild(prefab.transform, "PORT_ItemIn");
            var itemOut = FindChild(prefab.transform, "PORT_ItemOut");
            if (Vector3.Distance(itemIn.position, itemOut.position) < 1.50f)
            {
                throw new InvalidDataException(
                    "Mechanical press input and output ports overlap.");
            }

            var workpiece = FindChild(prefab.transform, "REF_Workpiece");
            var workpieceLocal = prefab.transform.InverseTransformPoint(workpiece.position);
            if (Mathf.Abs(workpieceLocal.x) > 0.002f
                || Mathf.Abs(workpieceLocal.z) > 0.002f)
            {
                throw new InvalidDataException(
                    "REF_Workpiece must be centred between the item ports.");
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length < 20)
            {
                throw new InvalidDataException(
                    $"Mechanical press has only {renderers.Length} renderers.");
            }

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !IsApprovedMaterial(materials, material))
                    {
                        throw new InvalidDataException(
                            $"Renderer '{HierarchyPath(renderer.transform)}' is not " +
                            "using an approved Mechanical Press material.");
                    }
                }
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            var size = bounds.size;
            AssertNear(size.x, 1.9000f, 0.035f, "width");
            AssertNear(size.y, 2.4800f, 0.035f, "height");
            AssertNear(size.z, 1.5030f, 0.035f, "depth");

            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != CompoundColliders.Length)
            {
                throw new InvalidDataException(
                    $"Expected {CompoundColliders.Length} compound colliders, " +
                    $"found {colliders.Length}.");
            }

            foreach (var collider in colliders)
            {
                if (!(collider is BoxCollider))
                {
                    throw new InvalidDataException(
                        "Mechanical press collision must use BoxCollider only.");
                }
            }
        }

        private static bool IsApprovedMaterial(
            IReadOnlyDictionary<string, Material> materials,
            Material candidate)
        {
            foreach (var pair in materials)
            {
                if (pair.Value == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetMarkerWorldHeight(
            Transform root,
            string markerName,
            float height)
        {
            var marker = FindChild(root, markerName);
            if (marker == null)
            {
                throw new InvalidDataException(
                    $"Mechanical press is missing '{markerName}'.");
            }

            var position = marker.position;
            position.y = root.position.y + height;
            marker.position = position;
        }

        private static Material UpsertMaterial(
            string path,
            Shader shader,
            Texture2D texture,
            float roughness,
            float metallic)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", (float)CullMode.Back);
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_ReceiveShadows", 1f);
            SetTexture(material, "_BaseMap", texture);
            SetColor(material, "_BaseColor", Color.white);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", 1f - roughness);
            material.SetOverrideTag("RenderType", "Opaque");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_EMISSION");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
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
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsDescendantOf(Transform candidate, Transform ancestor)
        {
            var current = candidate.parent;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static string HierarchyPath(Transform transform)
        {
            var path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    $"Required asset was not found: {path}",
                    path);
            }

            return asset;
        }

        private static void RequireFile(string assetPath)
        {
            var absolute = Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length));
            if (!File.Exists(absolute))
            {
                throw new FileNotFoundException(
                    $"Required authored file was not found: {assetPath}",
                    assetPath);
            }
        }

        private static void EnsureFolder(string folder)
        {
            var normalized = folder.Replace('\\', '/');
            var segments = normalized.Split('/');
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

        private static void AssertNear(
            float actual,
            float expected,
            float tolerance,
            string label)
        {
            if (Mathf.Abs(actual - expected) > tolerance)
            {
                throw new InvalidDataException(
                    $"Mechanical press {label} is {actual:F4} m, " +
                    $"expected {expected:F4} ± {tolerance:F4} m.");
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetTexture(
            Material material,
            string property,
            Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private readonly struct ColliderDefinition
        {
            public ColliderDefinition(string name, Vector3 center, Vector3 size)
            {
                Name = name;
                Center = center;
                Size = size;
            }

            public string Name { get; }

            public Vector3 Center { get; }

            public Vector3 Size { get; }
        }
    }
}

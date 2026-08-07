using System;
using System.Collections.Generic;
using System.IO;
using CML.Unity.Presentation.Machines;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity-side setup for the Manual Era production props.
    /// Blender owns geometry, hierarchy, UVs and gameplay markers; Unity owns
    /// import settings, shared-material remapping, colliders and prefabs.
    /// </summary>
    public static class ManualEraAssetSetup
    {
        private const string ArtRoot = "Assets/_Project/Art/ManualEra";
        private const string ModelPath = ArtRoot + "/Models/STR_Workbench.fbx";
        private const string PrefabsRoot = ArtRoot + "/Prefabs";
        private const string PrefabPath = PrefabsRoot + "/PF_Workbench.prefab";
        private const string SharedMaterialPath =
            "Assets/_Project/Art/Shared/ManualEra/Materials/M_ManualEra_OpaqueAtlas.mat";
        private const string FireMaterialPath =
            "Assets/_Project/Art/Shared/ManualEra/Materials/" +
            "M_ManualEra_FireEmissive.mat";
        private const string SharedBaseTexturePath =
            "Assets/_Project/Art/Shared/ManualEra/Textures/" +
            "T_ManualEra_BaseColor.png";
        private const string SharedMaskTexturePath =
            "Assets/_Project/Art/Shared/ManualEra/Textures/" +
            "T_ManualEra_Mask.png";
        private const string FurnaceModelPath =
            ArtRoot + "/Models/STR_CrudeFurnace.fbx";
        private const string FurnacePrefabPath =
            PrefabsRoot + "/PF_CrudeFurnace.prefab";

        private const int ExpectedMeshCount = 6;
        private const long ExpectedTriangleCount = 1502;
        private static readonly Vector3 ExpectedBounds =
            new Vector3(1.45f, 0.92f, 0.72f);

        private static readonly string[] RequiredMeshes =
        {
            "GEO_Worktop",
            "GEO_Legs",
            "GEO_StoneFeet",
            "GEO_Frame",
            "GEO_LowerShelf",
            "GEO_Joinery"
        };

        private static readonly MarkerDefinition[] RequiredMarkers =
        {
            new MarkerDefinition("REF_Placement", Vector3.zero),
            new MarkerDefinition("REF_Interact", new Vector3(0f, 0.760f, -0.480f)),
            new MarkerDefinition("REF_WorkSurface", new Vector3(0f, 0.925f, 0f)),
            new MarkerDefinition("REF_Output", new Vector3(0.395f, 0.940f, -0.020f)),
            new MarkerDefinition(
                "REF_NetworkConnector",
                new Vector3(0.420f, 0.485f, 0.390f))
        };

        private static readonly ColliderDefinition[] CompoundColliders =
        {
            new ColliderDefinition(
                "COL_Upper",
                new Vector3(0f, 0.800f, 0f),
                new Vector3(1.450f, 0.240f, 0.720f)),
            new ColliderDefinition(
                "COL_TrestleLeft",
                new Vector3(-0.550f, 0.400f, 0f),
                new Vector3(0.340f, 0.800f, 0.720f)),
            new ColliderDefinition(
                "COL_TrestleRight",
                new Vector3(0.550f, 0.400f, 0f),
                new Vector3(0.340f, 0.800f, 0.720f))
        };

        private const int ExpectedFurnaceMeshCount = 4;
        // Kept beside the authored bounds and marker contract so topology changes
        // cannot silently enter production. Updated only after the Blender audit.
        private const long ExpectedFurnaceTriangleCount = 2737;
        private static readonly Vector3 ExpectedFurnaceBounds =
            new Vector3(1.557375f, 1.300000f, 1.078125f);
        private static readonly Vector3 ExpectedFurnaceBoundsCenter =
            new Vector3(0.141313f, 0.650000f, -0.000937f);

        private static readonly string[] RequiredFurnaceMeshes =
        {
            "GEO_StoneBlocks",
            "GEO_CavityLining",
            "GEO_ConstructionLogs",
            "GEO_Fire",
        };

        private static readonly MarkerDefinition[] RequiredFurnaceMarkers =
        {
            new MarkerDefinition("REF_Placement", Vector3.zero),
            new MarkerDefinition(
                "REF_Interact",
                new Vector3(0f, 0.620f, -0.740f)),
            new MarkerDefinition(
                "PORT_MineralInput",
                new Vector3(0f, 0.940f, -0.555f)),
            new MarkerDefinition(
                "PORT_FuelInput",
                new Vector3(0f, 0.340f, -0.555f)),
            new MarkerDefinition(
                "PORT_ProductOutput",
                new Vector3(0.935f, 0.405f, -0.055f)),
            new MarkerDefinition(
                "REF_NetworkConnector",
                new Vector3(0f, 0.640f, 0.555f))
        };

        private static readonly PortDefinition[] RequiredFurnacePorts =
        {
            new PortDefinition(
                "PORT_MineralInput",
                Vector3.back),
            new PortDefinition(
                "PORT_FuelInput",
                Vector3.back),
            new PortDefinition(
                "PORT_ProductOutput",
                Vector3.right)
        };

        // Compound collision follows the masonry and tray without sealing the
        // three functional openings.
        private static readonly ColliderDefinition[] FurnaceCompoundColliders =
        {
            new ColliderDefinition(
                "COL_Core",
                new Vector3(0f, 0.650f, 0.130f),
                new Vector3(1.200f, 1.280f, 0.780f)),
            new ColliderDefinition(
                "COL_FrontLeft",
                new Vector3(-0.485f, 0.650f, -0.400f),
                new Vector3(0.290f, 1.260f, 0.280f)),
            new ColliderDefinition(
                "COL_FrontRight",
                new Vector3(0.485f, 0.650f, -0.400f),
                new Vector3(0.290f, 1.260f, 0.280f)),
            new ColliderDefinition(
                "COL_FrontBridge",
                new Vector3(0f, 0.7575f, -0.400f),
                new Vector3(0.680f, 0.095f, 0.280f)),
            new ColliderDefinition(
                "COL_FrontTop",
                new Vector3(0f, 1.185f, -0.400f),
                new Vector3(0.680f, 0.230f, 0.280f)),
            new ColliderDefinition(
                "COL_ProductTray",
                new Vector3(0.780f, 0.360f, -0.055f),
                new Vector3(0.280f, 0.090f, 0.380f))
        };

        private static readonly ClearanceDefinition[] FurnacePortClearances =
        {
            new ClearanceDefinition(
                "PORT_MineralInput",
                new Vector3(0f, 0.940f, -0.405f),
                new Vector3(0.320f, 0.230f, 0.270f)),
            new ClearanceDefinition(
                "PORT_FuelInput",
                new Vector3(0f, 0.340f, -0.405f),
                new Vector3(0.360f, 0.300f, 0.270f)),
            new ClearanceDefinition(
                "PORT_ProductOutput",
                new Vector3(0.800f, 0.5275f, -0.055f),
                new Vector3(0.250f, 0.225f, 0.300f))
        };

        private static readonly SimplePropDefinition[] RemainingKitProps =
        {
            new SimplePropDefinition(
                "Crate",
                ArtRoot + "/Models/STR_Crate.fbx",
                PrefabsRoot + "/PF_Crate.prefab",
                "PF_Crate",
                "STR_Crate",
                new[]
                {
                    "GEO_CrateBody",
                    "GEO_CrateLid"
                },
                new[]
                {
                    new MarkerDefinition("REF_Placement", Vector3.zero),
                    new MarkerDefinition(
                        "REF_Interact",
                        new Vector3(0f, 0.590f, -0.480f)),
                    new MarkerDefinition(
                        "PORT_ItemIO",
                        new Vector3(0f, 0.4375f, -0.375f)),
                    new MarkerDefinition(
                        "REF_NetworkConnector",
                        new Vector3(0f, 0.300f, 0.375f))
                },
                new[]
                {
                    new OrientedMarkerDefinition(
                        "REF_Interact",
                        Vector3.back),
                    new OrientedMarkerDefinition(
                        "PORT_ItemIO",
                        Vector3.back),
                    new OrientedMarkerDefinition(
                        "REF_NetworkConnector",
                        Vector3.forward)
                },
                new[]
                {
                    new ColliderDefinition(
                        "COL_Body",
                        new Vector3(0f, 0.290f, 0f),
                        new Vector3(1.000f, 0.580f, 0.720f))
                },
                2380,
                new Vector3(1.000f, 0.580f, 0.7274f),
                new Vector3(0f, 0.290f, 0.0037f),
                0.012f),
            new SimplePropDefinition(
                "Iron Ingot",
                ArtRoot + "/Models/ITM_IronIngot.fbx",
                PrefabsRoot + "/PF_IronIngot.prefab",
                "PF_IronIngot",
                "ITM_IronIngot",
                new[]
                {
                    "GEO_IronIngot"
                },
                new[]
                {
                    new MarkerDefinition("REF_Placement", Vector3.zero),
                    new MarkerDefinition(
                        "REF_Pickup",
                        new Vector3(0f, 0.065f, 0f))
                },
                Array.Empty<OrientedMarkerDefinition>(),
                new[]
                {
                    new ColliderDefinition(
                        "COL_Body",
                        new Vector3(0f, 0.050f, 0f),
                        new Vector3(0.320f, 0.100f, 0.160f))
                },
                60,
                new Vector3(0.320f, 0.100f, 0.160f),
                new Vector3(0f, 0.050f, 0f),
                0.003f),
            new SimplePropDefinition(
                "Iron Plate",
                ArtRoot + "/Models/ITM_IronPlate.fbx",
                PrefabsRoot + "/PF_IronPlate.prefab",
                "PF_IronPlate",
                "ITM_IronPlate",
                new[]
                {
                    "GEO_IronPlate"
                },
                new[]
                {
                    new MarkerDefinition("REF_Placement", Vector3.zero),
                    new MarkerDefinition(
                        "REF_Pickup",
                        new Vector3(0f, 0.050f, 0f))
                },
                Array.Empty<OrientedMarkerDefinition>(),
                new[]
                {
                    new ColliderDefinition(
                        "COL_Body",
                        new Vector3(0f, 0.020f, 0f),
                        new Vector3(0.340f, 0.040f, 0.240f))
                },
                150,
                new Vector3(0.340f, 0.040f, 0.240f),
                new Vector3(0f, 0.020f, 0f),
                0.003f)
        };

        [MenuItem("CML/Art/Rebuild Manual Era Workbench")]
        public static void Run()
        {
            EnsureFolder(PrefabsRoot);

            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            // This material is authored and maintained by the shared Manual Era
            // palette. The Workbench must consume it, never duplicate or mutate it.
            var sharedMaterial = RequireAsset<Material>(SharedMaterialPath);
            ValidateSharedOpaqueMaterial(sharedMaterial);
            ConfigureModelMaterialRemap(ModelPath, sharedMaterial);

            var prefab = BuildWorkbenchPrefab(sharedMaterial);
            ValidateWorkbenchPrefab(prefab, sharedMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            Debug.Log(
                $"MANUAL_ERA_WORKBENCH_UNITY_VALIDATION prefab={PrefabPath} " +
                $"meshes={ExpectedMeshCount} triangles={ExpectedTriangleCount} " +
                $"bounds=({ExpectedBounds.x:F2},{ExpectedBounds.y:F2}," +
                $"{ExpectedBounds.z:F2}) markers={RequiredMarkers.Length} " +
                $"boxColliders={CompoundColliders.Length} material=shared status=PASS");
        }

        [MenuItem("CML/Art/Rebuild Manual Era Crude Furnace")]
        public static void RunCrudeFurnace()
        {
            EnsureFolder(PrefabsRoot);

            var absoluteFurnacePath = Path.Combine(
                Application.dataPath,
                FurnaceModelPath.Substring("Assets/".Length));
            if (!File.Exists(absoluteFurnacePath))
            {
                throw new FileNotFoundException(
                    "The authored Crude Furnace FBX is not present yet. " +
                    "Finish and validate Blender geometry before running Unity setup.",
                    FurnaceModelPath);
            }

            AssetDatabase.ImportAsset(
                FurnaceModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var sharedMaterial = RequireAsset<Material>(SharedMaterialPath);
            ValidateSharedOpaqueMaterial(sharedMaterial);
            var fireMaterial = EnsureFireMaterial();
            ConfigureModelMaterialRemap(
                FurnaceModelPath,
                sharedMaterial,
                fireMaterial);

            var prefab = BuildCrudeFurnacePrefab(
                sharedMaterial,
                fireMaterial);
            ValidateCrudeFurnacePrefab(
                prefab,
                sharedMaterial,
                fireMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            Debug.Log(
                $"MANUAL_ERA_CRUDE_FURNACE_UNITY_VALIDATION " +
                $"prefab={FurnacePrefabPath} meshes={ExpectedFurnaceMeshCount} " +
                $"triangles={ExpectedFurnaceTriangleCount} " +
                $"bounds=({ExpectedFurnaceBounds.x:F2}," +
                $"{ExpectedFurnaceBounds.y:F2}," +
                $"{ExpectedFurnaceBounds.z:F2}) " +
                $"markers={RequiredFurnaceMarkers.Length} " +
                $"ports={RequiredFurnacePorts.Length} " +
                $"boxColliders={FurnaceCompoundColliders.Length} " +
                "material=shared status=PASS");
        }

        [MenuItem("CML/Art/Rebuild Manual Era Crate and Iron Items")]
        public static void RunRemainingManualEraKit()
        {
            EnsureFolder(PrefabsRoot);

            // Refuse the entire batch before mutating any Unity asset when an
            // authored FBX is still missing.
            foreach (var definition in RemainingKitProps)
            {
                RequireModelFile(definition);
            }

            foreach (var definition in RemainingKitProps)
            {
                AssetDatabase.ImportAsset(
                    definition.ModelPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }

            var sharedMaterial = RequireAsset<Material>(SharedMaterialPath);
            ValidateSharedOpaqueMaterial(sharedMaterial);
            GameObject selectedPrefab = null;
            foreach (var definition in RemainingKitProps)
            {
                ConfigureModelMaterialRemap(
                    definition.ModelPath,
                    sharedMaterial);
                var prefab = BuildSimplePropPrefab(
                    definition,
                    sharedMaterial);
                ValidateSimplePropPrefab(
                    prefab,
                    definition,
                    sharedMaterial);
                selectedPrefab ??= prefab;

                Debug.Log(
                    $"MANUAL_ERA_SIMPLE_PROP_UNITY_VALIDATION " +
                    $"asset={definition.Label} prefab={definition.PrefabPath} " +
                    $"meshes={definition.MeshNames.Length} " +
                    $"triangles={definition.ExpectedTriangleCount} " +
                    $"bounds=({definition.ExpectedBounds.x:F3}," +
                    $"{definition.ExpectedBounds.y:F3}," +
                    $"{definition.ExpectedBounds.z:F3}) " +
                    $"markers={definition.Markers.Length} " +
                    $"boxColliders={definition.Colliders.Length} " +
                    "material=shared status=PASS");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
            {
                Selection.activeObject = selectedPrefab;
            }
        }

        /// <summary>
        /// Reimports and rebuilds only the wooden Crate. This is the safe
        /// Unity companion to build_manual_era_kit.py --crate-only.
        /// </summary>
        [MenuItem("CML/Art/Rebuild Manual Era Crate Only")]
        public static void RunCrateOnly()
        {
            EnsureFolder(PrefabsRoot);
            var definition = RemainingKitProps[0];
            RequireModelFile(definition);
            AssetDatabase.ImportAsset(
                definition.ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var sharedMaterial = RequireAsset<Material>(SharedMaterialPath);
            ValidateSharedOpaqueMaterial(sharedMaterial);
            ConfigureModelMaterialRemap(
                definition.ModelPath,
                sharedMaterial);
            var prefab = BuildSimplePropPrefab(
                definition,
                sharedMaterial);
            ValidateSimplePropPrefab(
                prefab,
                definition,
                sharedMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            Debug.Log(
                $"MANUAL_ERA_CRATE_UNITY_VALIDATION " +
                $"prefab={definition.PrefabPath} " +
                $"triangles={definition.ExpectedTriangleCount} " +
                $"markers={definition.Markers.Length} " +
                "material=shared status=PASS");
        }

        private static void ConfigureModelMaterialRemap(
            string modelPath,
            Material sharedMaterial,
            Material fireMaterial = null)
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
                    "M_ManualEra_OpaqueAtlas"),
                sharedMaterial);
            if (fireMaterial != null)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(
                        typeof(Material),
                        "M_ManualEra_FireEmissive"),
                    fireMaterial);
            }

            importer.SaveAndReimport();
        }

        private static Material EnsureFireMaterial()
        {
            var materialsFolder = Path.GetDirectoryName(FireMaterialPath);
            if (string.IsNullOrEmpty(materialsFolder))
            {
                throw new InvalidOperationException(
                    $"Invalid fire material path: {FireMaterialPath}");
            }

            EnsureFolder(materialsFolder.Replace('\\', '/'));

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Required URP Lit shader is unavailable.");
            }
            var sharedBaseTexture =
                RequireAsset<Texture2D>(SharedBaseTexturePath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                FireMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_ManualEra_FireEmissive"
                };
                AssetDatabase.CreateAsset(material, FireMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Geometry;
            material.EnableKeyword("_EMISSION");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");

            SetFloatIfPresent(material, "_Surface", 0f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(material, "_AlphaClip", 0f);
            SetFloatIfPresent(material, "_SrcBlend", 1f);
            SetFloatIfPresent(material, "_DstBlend", 0f);
            SetFloatIfPresent(material, "_ZWrite", 1f);
            SetFloatIfPresent(material, "_Cull", 2f);
            SetFloatIfPresent(material, "_Metallic", 0f);
            SetFloatIfPresent(material, "_Smoothness", 0.12f);

            var baseColor = Color.white;
            var emissionColor = new Color(2.4f, 2.4f, 2.4f, 1f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", baseColor);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emissionColor);
            }

            foreach (var textureProperty in material.GetTexturePropertyNames())
            {
                var usesSharedColorAtlas =
                    string.Equals(
                        textureProperty,
                        "_BaseMap",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        textureProperty,
                        "_MainTex",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        textureProperty,
                        "_EmissionMap",
                        StringComparison.Ordinal);
                material.SetTexture(
                    textureProperty,
                    usesSharedColorAtlas ? sharedBaseTexture : null);
            }

            material.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidateSharedOpaqueMaterial(Material material)
        {
            var sharedBaseTexture =
                RequireAsset<Texture2D>(SharedBaseTexturePath);
            var sharedMaskTexture =
                RequireAsset<Texture2D>(SharedMaskTexturePath);
            if (material == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(material),
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                material.shader == null ||
                !string.Equals(
                    material.shader.name,
                    "Universal Render Pipeline/Lit",
                    StringComparison.Ordinal) ||
                material.renderQueue !=
                (int)UnityEngine.Rendering.RenderQueue.Geometry ||
                material.GetTag("RenderType", false) != "Opaque" ||
                !material.HasProperty("_BaseMap") ||
                material.GetTexture("_BaseMap") != sharedBaseTexture ||
                !material.HasProperty("_MetallicGlossMap") ||
                material.GetTexture("_MetallicGlossMap") !=
                sharedMaskTexture ||
                !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
            {
                throw new InvalidOperationException(
                    "Shared Manual Era opaque material does not use the " +
                    "expected URP base-color and metallic/smoothness atlases.");
            }

            if (material.HasProperty("_Surface") &&
                !Approximately(
                    material.GetFloat("_Surface"),
                    0f,
                    0.0001f) ||
                material.HasProperty("_Metallic") &&
                !Approximately(
                    material.GetFloat("_Metallic"),
                    1f,
                    0.0001f) ||
                material.HasProperty("_Smoothness") &&
                !Approximately(
                    material.GetFloat("_Smoothness"),
                    1f,
                    0.0001f))
            {
                throw new InvalidOperationException(
                    "Shared Manual Era material must let the mask atlas drive " +
                    "metallic and smoothness at full strength.");
            }

            var baseImporter = AssetImporter.GetAtPath(
                SharedBaseTexturePath) as TextureImporter;
            var maskImporter = AssetImporter.GetAtPath(
                SharedMaskTexturePath) as TextureImporter;
            if (baseImporter == null ||
                maskImporter == null ||
                !baseImporter.sRGBTexture ||
                maskImporter.sRGBTexture ||
                baseImporter.mipmapEnabled ||
                maskImporter.mipmapEnabled)
            {
                throw new InvalidOperationException(
                    "Shared Manual Era base atlas must import as sRGB and the " +
                    "mask as linear, both without mipmaps.");
            }
        }

        private static void SetFloatIfPresent(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static GameObject BuildWorkbenchPrefab(Material sharedMaterial)
        {
            var source = RequireAsset<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate model asset {ModelPath}.");
            }

            try
            {
                instance.name = "PF_Workbench";
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = new[] { sharedMaterial };
                }

                // Imported collision is intentionally discarded. The production
                // prop uses a stable three-box compound collision contract.
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                foreach (var definition in CompoundColliders)
                {
                    AddBoxCollider(instance.transform, definition);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save prefab {PrefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static GameObject BuildCrudeFurnacePrefab(
            Material sharedMaterial,
            Material fireMaterial)
        {
            var source = RequireAsset<GameObject>(FurnaceModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate model asset {FurnaceModelPath}.");
            }

            try
            {
                instance.name = "PF_CrudeFurnace";
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in
                         instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = new[]
                    {
                        string.Equals(
                            renderer.transform.name,
                            "GEO_Fire",
                            StringComparison.Ordinal)
                            ? fireMaterial
                            : sharedMaterial
                    };
                }

                var fire = FindRecursive(instance.transform, "GEO_Fire");
                if (fire == null)
                {
                    throw new InvalidOperationException(
                        "Crude Furnace model is missing GEO_Fire.");
                }

                // The concept render may show fire for legibility, but the
                // production prefab represents an unlit furnace by default.
                fire.gameObject.SetActive(false);

                // FBX axis conversion can preserve marker positions while
                // flipping the authored empty axes. Normalize the production
                // prefab ports to the gameplay-facing Unity contract.
                foreach (var port in RequiredFurnacePorts)
                {
                    var portTransform = FindRecursive(instance.transform, port.Name);
                    if (portTransform == null)
                    {
                        throw new InvalidOperationException(
                            $"Crude Furnace model is missing port '{port.Name}'.");
                    }

                    portTransform.rotation = Quaternion.LookRotation(
                        instance.transform.TransformDirection(port.Forward),
                        instance.transform.TransformDirection(Vector3.up));
                }

                foreach (var collider in
                         instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                foreach (var definition in FurnaceCompoundColliders)
                {
                    AddBoxCollider(instance.transform, definition);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    FurnacePrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save prefab {FurnacePrefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static GameObject BuildSimplePropPrefab(
            SimplePropDefinition definition,
            Material sharedMaterial)
        {
            var source = RequireAsset<GameObject>(definition.ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate model asset {definition.ModelPath}.");
            }

            try
            {
                instance.name = definition.PrefabName;
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in
                         instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = new[] { sharedMaterial };
                }

                // As with the Furnace, normalize gameplay-facing axes after
                // FBX conversion while retaining the authored positions.
                foreach (var marker in definition.OrientedMarkers)
                {
                    var markerTransform = FindRecursive(
                        instance.transform,
                        marker.Name);
                    if (markerTransform == null)
                    {
                        throw new InvalidOperationException(
                            $"{definition.Label} model is missing oriented " +
                            $"marker '{marker.Name}'.");
                    }

                    markerTransform.rotation = Quaternion.LookRotation(
                        instance.transform.TransformDirection(marker.Forward),
                        instance.transform.TransformDirection(Vector3.up));
                }

                foreach (var collider in
                         instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                foreach (var collider in definition.Colliders)
                {
                    AddBoxCollider(instance.transform, collider);
                }

                // Keep future deterministic crate rebuilds animation-ready.
                // Existing scene instances are also covered by EnsureFor at
                // interaction time, so this does not force a scene migration.
                if (string.Equals(
                        definition.Label,
                        "Crate",
                        StringComparison.Ordinal)
                    && instance.GetComponent<ChestLidAnimator>() == null)
                {
                    instance.AddComponent<ChestLidAnimator>();
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    definition.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save prefab {definition.PrefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AddBoxCollider(
            Transform root,
            ColliderDefinition definition)
        {
            var colliderObject = new GameObject(definition.Name);
            colliderObject.transform.SetParent(root, false);
            colliderObject.transform.localPosition = definition.Center;
            colliderObject.transform.localRotation = Quaternion.identity;
            colliderObject.transform.localScale = Vector3.one;

            var collider = colliderObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = definition.Size;
            collider.isTrigger = false;
        }

        private static void ValidateWorkbenchPrefab(
            GameObject prefab,
            Material expectedMaterial)
        {
            AssertIdentity(prefab.transform, "Workbench prefab root");

            foreach (var meshName in RequiredMeshes)
            {
                if (CountTransforms(prefab.transform, meshName) != 1)
                {
                    throw new InvalidOperationException(
                        $"Workbench must contain one exact '{meshName}' transform.");
                }

                var meshTransform = FindRecursive(prefab.transform, meshName);
                if (meshTransform.GetComponent<MeshFilter>() == null ||
                    meshTransform.GetComponent<Renderer>() == null)
                {
                    throw new InvalidOperationException(
                        $"Workbench transform '{meshName}' is not a render mesh.");
                }
            }

            foreach (var marker in RequiredMarkers)
            {
                if (CountTransforms(prefab.transform, marker.Name) != 1)
                {
                    throw new InvalidOperationException(
                        $"Workbench must contain one exact '{marker.Name}' marker.");
                }

                var markerTransform = FindRecursive(prefab.transform, marker.Name);
                var localPosition = prefab.transform.InverseTransformPoint(
                    markerTransform.position);
                if (Vector3.Distance(localPosition, marker.Position) > 0.006f)
                {
                    throw new InvalidOperationException(
                        $"Marker '{marker.Name}' is at {localPosition}; " +
                        $"expected {marker.Position}.");
                }
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != ExpectedMeshCount ||
                filters.Length != ExpectedMeshCount)
            {
                throw new InvalidOperationException(
                    $"Workbench must contain {ExpectedMeshCount} render meshes; " +
                    $"found {renderers.Length} renderers and {filters.Length} filters.");
            }

            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterials[0] != expectedMaterial)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{HierarchyPath(renderer.transform)}' does not use " +
                        "the single shared Manual Era material.");
                }
            }

            long triangleCount = 0;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"MeshFilter '{HierarchyPath(filter.transform)}' has no mesh.");
                }

                if (mesh.subMeshCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Mesh '{mesh.name}' must contain exactly one submesh.");
                }

                if (!mesh.HasVertexAttribute(
                        UnityEngine.Rendering.VertexAttribute.TexCoord0))
                {
                    throw new InvalidOperationException(
                        $"Mesh '{mesh.name}' has no UV0 coordinates.");
                }

                triangleCount += mesh.GetIndexCount(0) / 3L;
            }

            if (triangleCount != ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Workbench triangle count changed: {triangleCount}; " +
                    $"expected {ExpectedTriangleCount}.");
            }

            var bounds = CollectRendererBounds(renderers);
            if (!Approximately(bounds.size.x, ExpectedBounds.x, 0.015f) ||
                !Approximately(bounds.size.y, ExpectedBounds.y, 0.012f) ||
                !Approximately(bounds.size.z, ExpectedBounds.z, 0.010f) ||
                Mathf.Abs(bounds.min.y) > 0.006f)
            {
                throw new InvalidOperationException(
                    $"Unexpected Workbench bounds {bounds.size} with minimum Y " +
                    $"{bounds.min.y:F4}; expected {ExpectedBounds} grounded at Y=0.");
            }

            var colliders = prefab.GetComponentsInChildren<BoxCollider>(true);
            if (colliders.Length != CompoundColliders.Length ||
                prefab.GetComponentsInChildren<Collider>(true).Length !=
                CompoundColliders.Length)
            {
                throw new InvalidOperationException(
                    "Workbench must contain exactly three BoxColliders and no " +
                    "other collider type.");
            }

            foreach (var definition in CompoundColliders)
            {
                var colliderTransform = FindRecursive(prefab.transform, definition.Name);
                if (colliderTransform == null)
                {
                    throw new InvalidOperationException(
                        $"Missing compound collider '{definition.Name}'.");
                }

                AssertIdentityExceptPosition(
                    colliderTransform,
                    definition.Center,
                    definition.Name);
                var collider = colliderTransform.GetComponent<BoxCollider>();
                if (collider == null ||
                    Vector3.Distance(collider.center, Vector3.zero) > 0.0001f ||
                    Vector3.Distance(collider.size, definition.Size) > 0.0001f ||
                    collider.isTrigger)
                {
                    throw new InvalidOperationException(
                        $"Compound collider '{definition.Name}' differs from contract.");
                }
            }

            if (prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Workbench production prefab must not contain a Rigidbody.");
            }
        }

        private static void ValidateCrudeFurnacePrefab(
            GameObject prefab,
            Material expectedMaterial,
            Material expectedFireMaterial)
        {
            AssertIdentity(prefab.transform, "Crude Furnace prefab root");
            var authoredRoot = FindRecursive(
                prefab.transform,
                "GEO_StoneBlocks").parent;
            if (authoredRoot != prefab.transform)
            {
                if (authoredRoot.parent != prefab.transform ||
                    !string.Equals(
                        authoredRoot.name,
                        "STR_CrudeFurnace",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Crude Furnace authored hierarchy must be either flat " +
                        "or use one direct STR_CrudeFurnace import wrapper.");
                }
            }

            foreach (var meshName in RequiredFurnaceMeshes)
            {
                if (CountTransforms(prefab.transform, meshName) != 1)
                {
                    throw new InvalidOperationException(
                        $"Crude Furnace must contain one exact '{meshName}' " +
                        "transform.");
                }

                var meshTransform = FindRecursive(prefab.transform, meshName);
                if (meshTransform.GetComponent<MeshFilter>() == null ||
                    meshTransform.GetComponent<Renderer>() == null)
                {
                    throw new InvalidOperationException(
                        $"Crude Furnace transform '{meshName}' is not a render mesh.");
                }

                if (meshTransform.parent != authoredRoot)
                {
                    throw new InvalidOperationException(
                        $"Crude Furnace mesh '{meshName}' must be a direct " +
                        "child of the authored Furnace root.");
                }
            }

            foreach (var marker in RequiredFurnaceMarkers)
            {
                if (CountTransforms(prefab.transform, marker.Name) != 1)
                {
                    throw new InvalidOperationException(
                        $"Crude Furnace must contain one exact '{marker.Name}' " +
                        "marker.");
                }

                var markerTransform = FindRecursive(prefab.transform, marker.Name);
                var localPosition = prefab.transform.InverseTransformPoint(
                    markerTransform.position);
                if (Vector3.Distance(localPosition, marker.Position) > 0.006f)
                {
                    throw new InvalidOperationException(
                        $"Marker '{marker.Name}' is at {localPosition}; " +
                        $"expected {marker.Position}.");
                }

                if (markerTransform.parent != authoredRoot)
                {
                    throw new InvalidOperationException(
                        $"Crude Furnace marker '{marker.Name}' must be a direct " +
                        "child of the authored Furnace root.");
                }
            }

            var portOrientationErrors = new List<string>();
            foreach (var port in RequiredFurnacePorts)
            {
                var portTransform = FindRecursive(prefab.transform, port.Name);
                var localForward = prefab.transform.InverseTransformDirection(
                    portTransform.forward);
                if (Vector3.Angle(localForward, port.Forward) > 0.1f)
                {
                    portOrientationErrors.Add(
                        $"{port.Name} forward={localForward}, " +
                        $"expected={port.Forward}");
                }

                var localUp = prefab.transform.InverseTransformDirection(
                    portTransform.up);
                if (Vector3.Angle(localUp, Vector3.up) > 0.1f)
                {
                    portOrientationErrors.Add(
                        $"{port.Name} up={localUp}, expected={Vector3.up}");
                }
            }

            if (portOrientationErrors.Count != 0)
            {
                throw new InvalidOperationException(
                    "Crude Furnace port orientation contract failed: " +
                    string.Join("; ", portOrientationErrors));
            }

            var fireTransform = FindRecursive(prefab.transform, "GEO_Fire");
            if (fireTransform.gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "Crude Furnace prefab must start unlit: GEO_Fire must be " +
                    "inactive by default.");
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != ExpectedFurnaceMeshCount ||
                filters.Length != ExpectedFurnaceMeshCount)
            {
                throw new InvalidOperationException(
                    $"Crude Furnace must contain {ExpectedFurnaceMeshCount} " +
                    $"render meshes; found {renderers.Length} renderers and " +
                    $"{filters.Length} filters.");
            }

            foreach (var renderer in renderers)
            {
                var requiredMaterial = string.Equals(
                    renderer.transform.name,
                    "GEO_Fire",
                    StringComparison.Ordinal)
                    ? expectedFireMaterial
                    : expectedMaterial;
                if (renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterials[0] != requiredMaterial)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{HierarchyPath(renderer.transform)}' does " +
                        $"not use its required shared Manual Era material.");
                }
            }

            ValidateFireMaterial(expectedFireMaterial);

            long triangleCount = 0;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"MeshFilter '{HierarchyPath(filter.transform)}' has no mesh.");
                }

                if (mesh.subMeshCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Mesh '{mesh.name}' must contain exactly one submesh.");
                }

                if (!mesh.HasVertexAttribute(
                        UnityEngine.Rendering.VertexAttribute.TexCoord0))
                {
                    throw new InvalidOperationException(
                        $"Mesh '{mesh.name}' has no UV0 coordinates.");
                }

                triangleCount += mesh.GetIndexCount(0) / 3L;
            }

            if (triangleCount != ExpectedFurnaceTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Crude Furnace triangle count changed: {triangleCount}; " +
                    $"expected {ExpectedFurnaceTriangleCount}.");
            }

            var bounds = CollectRendererBounds(renderers);
            if (!Approximately(
                    bounds.size.x,
                    ExpectedFurnaceBounds.x,
                    0.015f) ||
                !Approximately(
                    bounds.size.y,
                    ExpectedFurnaceBounds.y,
                    0.015f) ||
                !Approximately(
                    bounds.size.z,
                    ExpectedFurnaceBounds.z,
                    0.015f) ||
                Vector3.Distance(
                    bounds.center,
                    ExpectedFurnaceBoundsCenter) > 0.012f ||
                Mathf.Abs(bounds.min.y) > 0.006f)
            {
                throw new InvalidOperationException(
                    $"Unexpected Crude Furnace bounds {bounds.size} with " +
                    $"center {bounds.center} and minimum Y {bounds.min.y:F4}; " +
                    $"expected size {ExpectedFurnaceBounds}, center " +
                    $"{ExpectedFurnaceBoundsCenter}, grounded at Y=0.");
            }

            var boxColliders = prefab.GetComponentsInChildren<BoxCollider>(true);
            if (boxColliders.Length != FurnaceCompoundColliders.Length ||
                prefab.GetComponentsInChildren<Collider>(true).Length !=
                FurnaceCompoundColliders.Length)
            {
                throw new InvalidOperationException(
                    $"Crude Furnace must contain exactly " +
                    $"{FurnaceCompoundColliders.Length} authored BoxColliders " +
                    "and no other collider type.");
            }

            foreach (var definition in FurnaceCompoundColliders)
            {
                if (CountTransforms(prefab.transform, definition.Name) != 1)
                {
                    throw new InvalidOperationException(
                        $"Missing or duplicate compound collider " +
                        $"'{definition.Name}'.");
                }

                var colliderTransform = FindRecursive(
                    prefab.transform,
                    definition.Name);
                AssertIdentityExceptPosition(
                    colliderTransform,
                    definition.Center,
                    definition.Name);
                var collider = colliderTransform.GetComponent<BoxCollider>();
                if (collider == null ||
                    Vector3.Distance(collider.center, Vector3.zero) > 0.0001f ||
                    Vector3.Distance(collider.size, definition.Size) > 0.0001f ||
                    collider.isTrigger)
                {
                    throw new InvalidOperationException(
                        $"Compound collider '{definition.Name}' differs from " +
                        "contract.");
                }
            }

            foreach (var clearance in FurnacePortClearances)
            {
                var clearanceBounds = new Bounds(
                    clearance.Center,
                    clearance.Size);
                foreach (var collider in FurnaceCompoundColliders)
                {
                    var colliderBounds = new Bounds(
                        collider.Center,
                        collider.Size);
                    if (BoundsInteriorsOverlap(
                            colliderBounds,
                            clearanceBounds))
                    {
                        throw new InvalidOperationException(
                            $"Collider '{collider.Name}' blocks the physical " +
                            $"clearance for port '{clearance.Name}'.");
                    }
                }

                var markerTransform = FindRecursive(
                    prefab.transform,
                    clearance.Name);
                var port = FindPortDefinition(clearance.Name);
                var markerLocalPosition =
                    prefab.transform.InverseTransformPoint(
                        markerTransform.position);
                var sightStart = markerLocalPosition +
                                 port.Forward * 0.10f;
                foreach (var collider in FurnaceCompoundColliders)
                {
                    var colliderBounds = new Bounds(
                        collider.Center,
                        collider.Size);
                    if (SegmentIntersectsBounds(
                            sightStart,
                            clearance.Center,
                            colliderBounds))
                    {
                        throw new InvalidOperationException(
                            $"Collider '{collider.Name}' blocks line of sight " +
                            $"from '{clearance.Name}' to its physical opening.");
                    }
                }
            }

            if (prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animation>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Crude Furnace production prefab must not contain a " +
                "Rigidbody, Camera, Light, Animation or Animator.");
            }
        }

        private static void ValidateSimplePropPrefab(
            GameObject prefab,
            SimplePropDefinition definition,
            Material expectedMaterial)
        {
            AssertIdentity(
                prefab.transform,
                $"{definition.Label} prefab root");

            if (definition.MeshNames.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} has no required render mesh contract.");
            }

            var firstMesh = FindRecursive(
                prefab.transform,
                definition.MeshNames[0]);
            if (firstMesh == null)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} is missing its first required mesh " +
                    $"'{definition.MeshNames[0]}'.");
            }

            var authoredRoot = firstMesh.parent;
            if (authoredRoot != prefab.transform)
            {
                if (authoredRoot.parent != prefab.transform ||
                    !string.Equals(
                        authoredRoot.name,
                        definition.AuthoredRootName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} hierarchy must be flat or use one " +
                        $"direct '{definition.AuthoredRootName}' import wrapper.");
                }
            }

            foreach (var meshName in definition.MeshNames)
            {
                if (CountTransforms(prefab.transform, meshName) != 1)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} must contain one exact " +
                        $"'{meshName}' transform.");
                }

                var meshTransform = FindRecursive(prefab.transform, meshName);
                if (meshTransform.parent != authoredRoot ||
                    meshTransform.GetComponent<MeshFilter>() == null ||
                    meshTransform.GetComponent<Renderer>() == null)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} mesh '{meshName}' must be a direct " +
                        "render-mesh child of its authored root.");
                }
            }

            foreach (var marker in definition.Markers)
            {
                if (CountTransforms(prefab.transform, marker.Name) != 1)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} must contain one exact " +
                        $"'{marker.Name}' marker.");
                }

                var markerTransform = FindRecursive(
                    prefab.transform,
                    marker.Name);
                var localPosition = prefab.transform.InverseTransformPoint(
                    markerTransform.position);
                if (Vector3.Distance(localPosition, marker.Position) > 0.006f)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} marker '{marker.Name}' is at " +
                        $"{localPosition}; expected {marker.Position}.");
                }

                if (markerTransform.parent != authoredRoot ||
                    markerTransform.GetComponent<Renderer>() != null ||
                    markerTransform.GetComponent<Collider>() != null)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} marker '{marker.Name}' must be an " +
                        "empty direct child of its authored root.");
                }
            }

            foreach (var marker in definition.OrientedMarkers)
            {
                var markerTransform = FindRecursive(
                    prefab.transform,
                    marker.Name);
                var localForward = prefab.transform.InverseTransformDirection(
                    markerTransform.forward);
                var localUp = prefab.transform.InverseTransformDirection(
                    markerTransform.up);
                if (Vector3.Angle(localForward, marker.Forward) > 0.1f ||
                    Vector3.Angle(localUp, Vector3.up) > 0.1f)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} marker '{marker.Name}' orientation " +
                        $"is forward={localForward}, up={localUp}; expected " +
                        $"forward={marker.Forward}, up={Vector3.up}.");
                }

                var localPosition = prefab.transform.InverseTransformPoint(
                    markerTransform.position);
                foreach (var colliderDefinition in definition.Colliders)
                {
                    var colliderBounds = new Bounds(
                        colliderDefinition.Center,
                        colliderDefinition.Size);
                    colliderBounds.Expand(-0.004f);
                    if (colliderBounds.Contains(localPosition))
                    {
                        throw new InvalidOperationException(
                            $"{definition.Label} collider " +
                            $"'{colliderDefinition.Name}' encloses accessible " +
                            $"marker '{marker.Name}'.");
                    }
                }
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != definition.MeshNames.Length ||
                filters.Length != definition.MeshNames.Length)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} must contain exactly " +
                    $"{definition.MeshNames.Length} render meshes; found " +
                    $"{renderers.Length} renderers and {filters.Length} filters.");
            }

            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != expectedMaterial)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} renderer " +
                        $"'{HierarchyPath(renderer.transform)}' must use exactly " +
                        "the shared Manual Era atlas material.");
                }
            }

            long triangleCount = 0;
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null ||
                    mesh.subMeshCount != 1 ||
                    !mesh.HasVertexAttribute(
                        UnityEngine.Rendering.VertexAttribute.TexCoord0))
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} mesh " +
                        $"'{HierarchyPath(filter.transform)}' must have one " +
                        "submesh and UV0.");
                }

                triangleCount += mesh.GetIndexCount(0) / 3L;
            }

            if (definition.ExpectedTriangleCount <= 0 ||
                triangleCount != definition.ExpectedTriangleCount)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} topology changed: expected " +
                    $"{definition.ExpectedTriangleCount} triangles, found " +
                    $"{triangleCount}.");
            }

            var bounds = CollectRendererBounds(renderers);
            if (Mathf.Abs(bounds.size.x - definition.ExpectedBounds.x) >
                    definition.BoundsTolerance ||
                Mathf.Abs(bounds.size.y - definition.ExpectedBounds.y) >
                    definition.BoundsTolerance ||
                Mathf.Abs(bounds.size.z - definition.ExpectedBounds.z) >
                    definition.BoundsTolerance ||
                Vector3.Distance(
                    bounds.center,
                    definition.ExpectedBoundsCenter) >
                    definition.BoundsTolerance)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} bounds changed: size={bounds.size}, " +
                    $"center={bounds.center}; expected size=" +
                    $"{definition.ExpectedBounds}, center=" +
                    $"{definition.ExpectedBoundsCenter}.");
            }

            if (Mathf.Abs(bounds.min.y) > 0.002f)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} is not grounded; minimum Y is " +
                    $"{bounds.min.y:F4}.");
            }

            var boxColliders = prefab.GetComponentsInChildren<BoxCollider>(true);
            if (boxColliders.Length != definition.Colliders.Length ||
                prefab.GetComponentsInChildren<Collider>(true).Length !=
                definition.Colliders.Length)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} must contain exactly " +
                    $"{definition.Colliders.Length} BoxColliders and no other " +
                    "collider type.");
            }

            foreach (var colliderDefinition in definition.Colliders)
            {
                if (CountTransforms(
                        prefab.transform,
                        colliderDefinition.Name) != 1)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} is missing exact collider " +
                        $"'{colliderDefinition.Name}'.");
                }

                var colliderTransform = FindRecursive(
                    prefab.transform,
                    colliderDefinition.Name);
                AssertIdentityExceptPosition(
                    colliderTransform,
                    colliderDefinition.Center,
                    $"{definition.Label}/{colliderDefinition.Name}");
                var collider = colliderTransform.GetComponent<BoxCollider>();
                if (collider == null ||
                    Vector3.Distance(collider.center, Vector3.zero) > 0.0001f ||
                    Vector3.Distance(
                        collider.size,
                        colliderDefinition.Size) > 0.0001f ||
                    collider.isTrigger)
                {
                    throw new InvalidOperationException(
                        $"{definition.Label} collider " +
                        $"'{colliderDefinition.Name}' differs from contract.");
                }
            }

            if (prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animation>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{definition.Label} production prefab must not contain a " +
                    "Rigidbody, Camera, Light, Animation or Animator.");
            }
        }

        private static void ValidateFireMaterial(Material material)
        {
            if (material == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(material),
                    FireMaterialPath,
                    StringComparison.Ordinal) ||
                material.shader == null ||
                !string.Equals(
                    material.shader.name,
                    "Universal Render Pipeline/Lit",
                    StringComparison.Ordinal) ||
                material.renderQueue !=
                (int)UnityEngine.Rendering.RenderQueue.Geometry ||
                material.GetTag("RenderType", false) != "Opaque" ||
                !material.IsKeywordEnabled("_EMISSION"))
            {
                throw new InvalidOperationException(
                    "Shared Manual Era fire material does not match the " +
                    "opaque URP emissive contract.");
            }

            if (material.HasProperty("_Surface") &&
                !Approximately(material.GetFloat("_Surface"), 0f, 0.0001f))
            {
                throw new InvalidOperationException(
                    "Shared fire material must remain opaque.");
            }

            if (material.HasProperty("_EmissionColor"))
            {
                var emission = material.GetColor("_EmissionColor");
                if (emission.maxColorComponent <= 1f)
                {
                    throw new InvalidOperationException(
                        "Shared fire material must retain HDR emission.");
                }
            }

            var sharedBaseTexture =
                RequireAsset<Texture2D>(SharedBaseTexturePath);
            foreach (var textureProperty in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(textureProperty);
                if (texture != null && texture != sharedBaseTexture)
                {
                    throw new InvalidOperationException(
                        $"Shared fire material references a private or unexpected " +
                        $"texture in '{textureProperty}'.");
                }
            }

            if (!material.HasProperty("_BaseMap") ||
                material.GetTexture("_BaseMap") != sharedBaseTexture ||
                !material.HasProperty("_EmissionMap") ||
                material.GetTexture("_EmissionMap") != sharedBaseTexture)
            {
                throw new InvalidOperationException(
                    "Shared fire material must reuse the Manual Era color atlas " +
                    "for both base color and emission.");
            }
        }

        private static Bounds CollectRendererBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                return new Bounds();
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static int CountTransforms(Transform root, string name)
        {
            var count = string.Equals(root.name, name, StringComparison.Ordinal)
                ? 1
                : 0;
            for (var index = 0; index < root.childCount; index++)
            {
                count += CountTransforms(root.GetChild(index), name);
            }

            return count;
        }

        private static Transform FindRecursive(Transform root, string name)
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

        private static void AssertIdentity(Transform transform, string context)
        {
            if (transform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(transform.localRotation, Quaternion.identity) > 0.001f ||
                (transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"{context} transform is not identity.");
            }
        }

        private static void AssertIdentityExceptPosition(
            Transform transform,
            Vector3 expectedPosition,
            string context)
        {
            if (Vector3.Distance(transform.localPosition, expectedPosition) > 0.0001f ||
                Quaternion.Angle(transform.localRotation, Quaternion.identity) > 0.001f ||
                (transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"{context} transform differs from the compound-collider contract.");
            }
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

        private static bool Approximately(
            float actual,
            float expected,
            float tolerance)
        {
            return Mathf.Abs(actual - expected) <= tolerance;
        }

        private static PortDefinition FindPortDefinition(string name)
        {
            foreach (var port in RequiredFurnacePorts)
            {
                if (string.Equals(
                        port.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return port;
                }
            }

            throw new InvalidOperationException(
                $"Missing port definition for '{name}'.");
        }

        private static bool SegmentIntersectsBounds(
            Vector3 start,
            Vector3 end,
            Bounds bounds)
        {
            // Ignore mere face contact; the contract concerns a collider
            // occupying the usable approach volume.
            bounds.Expand(-0.004f);
            var delta = end - start;
            var length = delta.magnitude;
            if (length <= 0.0001f)
            {
                return bounds.Contains(start);
            }

            return bounds.IntersectRay(
                       new Ray(start, delta / length),
                       out var distance) &&
                   distance < length - 0.001f;
        }

        private static bool BoundsInteriorsOverlap(
            Bounds first,
            Bounds second)
        {
            // Contact at a shared face is valid (for example the output
            // shelf touching the lower edge of its attachment rectangle).
            first.Expand(-0.004f);
            second.Expand(-0.004f);
            return first.Intersects(second);
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    $"Required Unity asset is missing or failed to import: {path}");
            }

            return asset;
        }

        private static void RequireModelFile(SimplePropDefinition definition)
        {
            var absolutePath = Path.Combine(
                Application.dataPath,
                definition.ModelPath.Substring("Assets/".Length));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"The authored {definition.Label} FBX is not present yet. " +
                    "Finish and validate Blender geometry before running the " +
                    "Unity setup batch.",
                    definition.ModelPath);
            }
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

        private readonly struct MarkerDefinition
        {
            public MarkerDefinition(string name, Vector3 position)
            {
                Name = name;
                Position = position;
            }

            public string Name { get; }
            public Vector3 Position { get; }
        }

        private readonly struct PortDefinition
        {
            public PortDefinition(
                string name,
                Vector3 forward)
            {
                Name = name;
                Forward = forward;
            }

            public string Name { get; }
            public Vector3 Forward { get; }
        }

        private readonly struct OrientedMarkerDefinition
        {
            public OrientedMarkerDefinition(
                string name,
                Vector3 forward)
            {
                Name = name;
                Forward = forward;
            }

            public string Name { get; }
            public Vector3 Forward { get; }
        }

        private readonly struct ColliderDefinition
        {
            public ColliderDefinition(
                string name,
                Vector3 center,
                Vector3 size)
            {
                Name = name;
                Center = center;
                Size = size;
            }

            public string Name { get; }
            public Vector3 Center { get; }
            public Vector3 Size { get; }
        }

        private readonly struct ClearanceDefinition
        {
            public ClearanceDefinition(
                string name,
                Vector3 center,
                Vector3 size)
            {
                Name = name;
                Center = center;
                Size = size;
            }

            public string Name { get; }
            public Vector3 Center { get; }
            public Vector3 Size { get; }
        }

        private readonly struct SimplePropDefinition
        {
            public SimplePropDefinition(
                string label,
                string modelPath,
                string prefabPath,
                string prefabName,
                string authoredRootName,
                string[] meshNames,
                MarkerDefinition[] markers,
                OrientedMarkerDefinition[] orientedMarkers,
                ColliderDefinition[] colliders,
                long expectedTriangleCount,
                Vector3 expectedBounds,
                Vector3 expectedBoundsCenter,
                float boundsTolerance)
            {
                Label = label;
                ModelPath = modelPath;
                PrefabPath = prefabPath;
                PrefabName = prefabName;
                AuthoredRootName = authoredRootName;
                MeshNames = meshNames;
                Markers = markers;
                OrientedMarkers = orientedMarkers;
                Colliders = colliders;
                ExpectedTriangleCount = expectedTriangleCount;
                ExpectedBounds = expectedBounds;
                ExpectedBoundsCenter = expectedBoundsCenter;
                BoundsTolerance = boundsTolerance;
            }

            public string Label { get; }
            public string ModelPath { get; }
            public string PrefabPath { get; }
            public string PrefabName { get; }
            public string AuthoredRootName { get; }
            public string[] MeshNames { get; }
            public MarkerDefinition[] Markers { get; }
            public OrientedMarkerDefinition[] OrientedMarkers { get; }
            public ColliderDefinition[] Colliders { get; }
            public long ExpectedTriangleCount { get; }
            public Vector3 ExpectedBounds { get; }
            public Vector3 ExpectedBoundsCenter { get; }
            public float BoundsTolerance { get; }
        }
    }

    internal sealed class ManualEraAssetPostprocessor : AssetPostprocessor
    {
        private const string WorkbenchModelPath =
            "Assets/_Project/Art/ManualEra/Models/STR_Workbench.fbx";
        private const string FurnaceModelPath =
            "Assets/_Project/Art/ManualEra/Models/STR_CrudeFurnace.fbx";
        private static readonly string[] RemainingKitModelPaths =
        {
            "Assets/_Project/Art/ManualEra/Models/STR_Crate.fbx",
            "Assets/_Project/Art/ManualEra/Models/ITM_IronIngot.fbx",
            "Assets/_Project/Art/ManualEra/Models/ITM_IronPlate.fbx"
        };

        private void OnPreprocessModel()
        {
            if (!string.Equals(
                    assetPath,
                    WorkbenchModelPath,
                    StringComparison.Ordinal) &&
                !string.Equals(
                    assetPath,
                    FurnaceModelPath,
                    StringComparison.Ordinal) &&
                Array.IndexOf(RemainingKitModelPaths, assetPath) < 0)
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
            importer.materialImportMode =
                ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName =
                ModelImporterMaterialName.BasedOnMaterialName;
        }
    }
}

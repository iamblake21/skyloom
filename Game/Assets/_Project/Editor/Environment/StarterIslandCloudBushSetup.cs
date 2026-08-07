using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity import boundary for the CloudBush shrubs. Blender owns
    /// the meshes, the UVs and the painted colour baked into the vertex colour;
    /// Unity owns texture import settings, URP materials and clean prefabs.
    ///
    /// The shrubs share the CloudTall tree shader: same contract of a luminance
    /// map multiplied by the vertex tint, so there is nothing shrub-specific to
    /// add on the rendering side.
    /// </summary>
    public static class StarterIslandCloudBushSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/Clutter/CloudBush";
        public const string ModelsRoot = Root + "/Models";
        public const string TexturesRoot = Root + "/Textures";
        public const string MaterialsRoot = Root + "/Materials";
        public const string PrefabsRoot = Root + "/Prefabs";

        private const string TreeShaderName =
            "CML/Environment/Starter Island CloudTall Tree";

        private const string MassGrainPath =
            TexturesRoot + "/T_ENV_Bush_CloudBush_MassGrain.png";
        private const string SprigAtlasPath =
            TexturesRoot + "/T_ENV_Bush_CloudBush_SprigAtlas.png";

        public static readonly string[] Sizes = { "Small", "Medium", "Wide" };
        public static readonly string[] SeasonSuffixes = { string.Empty, "_Autumn" };

        public static string AssetName(string size, string seasonSuffix)
        {
            return $"ENV_Bush_CloudBush_{size}{seasonSuffix}_LOD0";
        }

        public static string PrefabPath(string size, string seasonSuffix)
        {
            return $"{PrefabsRoot}/PF_{AssetName(size, seasonSuffix)}.prefab";
        }

        private static string ModelPath(string size, string seasonSuffix)
        {
            return $"{ModelsRoot}/{AssetName(size, seasonSuffix)}.fbx";
        }

        /// <summary>True when every CloudBush prefab already exists.</summary>
        public static bool PrefabsReady()
        {
            foreach (var seasonSuffix in SeasonSuffixes)
            {
                foreach (var size in Sizes)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(
                            PrefabPath(size, seasonSuffix)) == null)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        [MenuItem("CML/Art/CloudBush Shrubs/Rebuild Production Prefabs")]
        public static void Run()
        {
            RequireFile(MassGrainPath);
            RequireFile(SprigAtlasPath);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);

            ConfigureColorTexture(MassGrainPath, false);
            ConfigureColorTexture(SprigAtlasPath, true);

            var shader = Shader.Find(TreeShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Required shader '{TreeShaderName}' is unavailable.");
            }

            var massGrain = RequireAsset<Texture2D>(MassGrainPath);
            var sprigAtlas = RequireAsset<Texture2D>(SprigAtlasPath);

            var built = 0;
            foreach (var seasonSuffix in SeasonSuffixes)
            {
                var season = seasonSuffix == string.Empty ? "Summer" : "Autumn";
                var massMaterial = BuildMaterial(
                    $"{MaterialsRoot}/M_ENV_Bush_CloudBush_{season}_Mass.mat",
                    shader,
                    massGrain,
                    cutout: false,
                    windStrength: 0.08f);
                var sprigMaterial = BuildMaterial(
                    $"{MaterialsRoot}/M_ENV_Bush_CloudBush_{season}_Sprigs.mat",
                    shader,
                    sprigAtlas,
                    cutout: true,
                    windStrength: 0.14f);

                foreach (var size in Sizes)
                {
                    var modelPath = ModelPath(size, seasonSuffix);
                    RequireFile(modelPath);
                    ConfigureModelImporter(modelPath);
                    BuildPrefab(size, seasonSuffix, massMaterial, sprigMaterial);
                    built++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();

            Debug.Log(
                $"CLOUD_BUSH_SETUP prefabs={built} materialSets=2 " +
                "vertexColorAlbedo=true alphaClip=0.45 status=PASS");
        }

        [MenuItem("CML/Art/CloudBush Shrubs/Validate Production Prefabs")]
        public static void Validate()
        {
            var checkedPrefabs = 0;
            foreach (var seasonSuffix in SeasonSuffixes)
            {
                foreach (var size in Sizes)
                {
                    var asset = AssetName(size, seasonSuffix);
                    var prefab = RequireAsset<GameObject>(PrefabPath(size, seasonSuffix));
                    var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 2)
                    {
                        throw new InvalidOperationException(
                            $"{asset} must expose exactly its mass and sprig " +
                            $"meshes, found {renderers.Length}.");
                    }

                    if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                        prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                        prefab.GetComponentsInChildren<Camera>(true).Length != 0)
                    {
                        throw new InvalidOperationException(
                            $"{asset} still contains an imported helper.");
                    }

                    foreach (var suffix in new[] { "Mass", "Sprigs" })
                    {
                        var renderer = RequireNamedRenderer(prefab, $"GEO_{asset}_{suffix}");
                        if (renderer.sharedMaterial == null ||
                            renderer.sharedMaterial.shader == null ||
                            renderer.sharedMaterial.shader.name != TreeShaderName)
                        {
                            throw new InvalidOperationException(
                                $"{asset} {suffix} has no CloudBush material.");
                        }

                        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null)
                        {
                            throw new InvalidOperationException(
                                $"{asset} {suffix} lacks a production mesh.");
                        }

                        // The painted albedo lives in the vertex colour, so its
                        // loss would render the whole shrub flat white.
                        if (mesh.colors32 == null ||
                            mesh.colors32.Length != mesh.vertexCount)
                        {
                            throw new InvalidOperationException(
                                $"{asset} {suffix} lost its vertex colours on import.");
                        }
                    }

                    checkedPrefabs++;
                }
            }

            Debug.Log(
                $"CLOUD_BUSH_VALIDATION prefabs={checkedPrefabs} renderMeshes=2 " +
                "vertexColors=present colliders=0 status=PASS");
        }

        private static void ConfigureColorTexture(string path, bool atlas)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load TextureImporter for {path}.");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = atlas;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = atlas ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = atlas ? 2 : 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureModelImporter(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {path}.");
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importConstraints = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.generateSecondaryUV = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();
        }

        private static Material BuildMaterial(
            string path,
            Shader shader,
            Texture2D baseMap,
            bool cutout,
            float windStrength)
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
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.SetVector("_WindDirection", new Vector4(0.82f, 0f, 0.57f, 0f));
            material.SetFloat("_WindStrength", windStrength);
            material.SetFloat("_WindSpeed", 0.9f);
            material.SetFloat("_WindGustStrength", 0.34f);
            material.SetFloat("_WindFlutterStrength", 0.03f);
            // A shrub grows from the ground, so the sway ramps from just above it.
            material.SetFloat("_WindBaseHeight", 0.12f);
            material.SetFloat("_WindHeight", 1.1f);

            if (cutout)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.45f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.doubleSidedGI = true;
            }
            else
            {
                material.SetFloat("_AlphaClip", 0f);
                // The mass map is fully opaque; a near-zero cutoff keeps the
                // shader's shared clip harmless while the queue stays opaque.
                material.SetFloat("_Cutoff", 0.02f);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.Geometry;
                material.SetOverrideTag("RenderType", "Opaque");
                material.doubleSidedGI = false;
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildPrefab(
            string size,
            string seasonSuffix,
            Material massMaterial,
            Material sprigMaterial)
        {
            var asset = AssetName(size, seasonSuffix);
            var source = RequireAsset<GameObject>(ModelPath(size, seasonSuffix));
            if (PrefabUtility.InstantiatePrefab(source) is not GameObject instance)
            {
                throw new InvalidOperationException($"Could not instantiate {asset}.");
            }

            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.name = $"PF_{asset}";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                var mass = RequireNamedRenderer(instance, $"GEO_{asset}_Mass");
                var sprigs = RequireNamedRenderer(instance, $"GEO_{asset}_Sprigs");

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer != mass && renderer != sprigs)
                    {
                        UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                    }
                }

                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                mass.sharedMaterials = new[] { massMaterial };
                sprigs.sharedMaterials = new[] { sprigMaterial };
                foreach (var renderer in new[] { mass, sprigs })
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                }

                if (PrefabUtility.SaveAsPrefabAsset(
                        instance,
                        PrefabPath(size, seasonSuffix)) == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save the prefab for {asset}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static MeshRenderer RequireNamedRenderer(GameObject root, string name)
        {
            MeshRenderer match = null;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!string.Equals(renderer.name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Multiple renderers named {name} exist in {root.name}.");
                }

                match = renderer;
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Required renderer {name} is missing from {root.name}.");
            }

            return match;
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Required asset is missing or failed to import: {path}");
            }

            return asset;
        }

        private static void RequireFile(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException(
                    "Could not resolve the Unity project root.");
            }

            var absolutePath = Path.GetFullPath(
                Path.Combine(projectRoot.FullName, assetPath));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"Required CloudBush source is missing: {assetPath}",
                    absolutePath);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(
                    $"Invalid Unity folder path: {assetPath}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}

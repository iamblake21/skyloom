using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity import boundary for the CloudTall reference-match
    /// trees. Blender owns the meshes, UVs and the baked painted colour in the
    /// vertex colour channel; Unity owns texture import settings, URP materials
    /// and clean production prefabs.
    /// </summary>
    public static class StarterIslandCloudTallTreeSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Trees";
        public const string ModelsRoot = Root + "/Models";
        public const string TexturesRoot = Root + "/Textures";
        public const string MaterialsRoot = Root + "/Materials";
        public const string PrefabsRoot = Root + "/Prefabs";
        public const string ShadersRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Shaders";

        private const string TreeShaderName =
            "CML/Environment/Starter Island CloudTall Tree";
        private const string TreeShaderPath =
            ShadersRoot + "/StarterIslandCloudTallTree.shader";

        private const string CanopyGrainPath =
            TexturesRoot + "/T_ENV_Tree_CloudTall_CanopyGrain.png";
        private const string TuftAtlasPath =
            TexturesRoot + "/T_ENV_Tree_CloudTall_TuftAtlas.png";
        private const string BarkMapPath =
            TexturesRoot + "/T_ENV_Tree_CloudTall_Bark.png";
        private const string FallingColliderRootName =
            "WOOD_FallingColliders_V4";
        private const string FallingColliderRootPrefix =
            "WOOD_FallingColliders";
        private const int FallingCapsuleCount = 3;
        private const float FallingCapsuleSeamOverlap = 0.035f;
        private const float GeometryPositionTolerance = 0.0005f;
        private const float GeometryRotationTolerance = 0.05f;
        private const float GeometryScalarTolerance = 0.0005f;
        private const int MinimumBandVertexCount = 3;
        private static readonly float[] FallingPathFractions =
        {
            0.045f,
            0.35f,
            0.65f,
            0.90f
        };
        private static readonly float[] FallingRadiusFactors =
        {
            1.00f,
            0.86f,
            0.70f
        };

        private readonly struct FallingCapsuleSpec
        {
            public FallingCapsuleSpec(
                string name,
                Vector3 localPosition,
                Quaternion localRotation,
                float radius,
                float height)
            {
                Name = name;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                Radius = radius;
                Height = height;
            }

            public string Name { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public float Radius { get; }
            public float Height { get; }
        }

        [InitializeOnLoadMethod]
        private static void ScheduleAuthoredColliderUpgrade()
        {
            EditorApplication.delayCall +=
                EnsureAuthoredTrunkCollidersAfterReload;
        }

        /// <summary>Shape variants, in the order the scene swap cycles them.</summary>
        public static readonly string[] Variants = { "A", "B", "C" };

        /// <summary>Season suffixes: summer carries none, autumn carries "_Autumn".</summary>
        public static readonly string[] SeasonSuffixes = { string.Empty, "_Autumn" };

        public static string AssetName(string variant, string seasonSuffix)
        {
            return $"ENV_Tree_CloudTall_{variant}{seasonSuffix}_LOD0";
        }

        public static string PrefabPath(string variant, string seasonSuffix)
        {
            return $"{PrefabsRoot}/PF_{AssetName(variant, seasonSuffix)}.prefab";
        }

        private static string ModelPath(string variant, string seasonSuffix)
        {
            return $"{ModelsRoot}/{AssetName(variant, seasonSuffix)}.fbx";
        }

        /// <summary>
        /// Rebuilds the texture import settings and the URP materials without
        /// touching the prefabs.
        ///
        /// Use this once the trees are placed in a scene: saving a prefab asset
        /// over an existing one can silently drop the instance overrides that
        /// hold every placement transform, so a material or map change must not
        /// drag prefab regeneration along with it.
        /// </summary>
        [MenuItem("CML/Art/CloudTall Trees/Refresh Materials Only")]
        public static void RefreshMaterials()
        {
            BuildMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "CLOUD_TALL_TREES_MATERIALS materialSets=2 prefabsTouched=0 " +
                "status=PASS");
        }

        private static (Material canopy, Material fringe, Material bark)[] BuildMaterials()
        {
            RequireFile(TreeShaderPath);
            RequireFile(CanopyGrainPath);
            RequireFile(TuftAtlasPath);
            RequireFile(BarkMapPath);
            EnsureFolder(MaterialsRoot);

            ConfigureColorTexture(CanopyGrainPath, false);
            ConfigureColorTexture(TuftAtlasPath, true);
            ConfigureColorTexture(BarkMapPath, false);

            AssetDatabase.ImportAsset(
                TreeShaderPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var shader = RequireShader(TreeShaderName);

            var canopyGrain = RequireAsset<Texture2D>(CanopyGrainPath);
            var tuftAtlas = RequireAsset<Texture2D>(TuftAtlasPath);
            var barkMap = RequireAsset<Texture2D>(BarkMapPath);

            // One material set per season: geometry and maps are shared, only
            // the baked vertex colour differs, so the materials differ only by
            // name and exist to keep the two seasons separable in the scene.
            var sets = new (Material, Material, Material)[SeasonSuffixes.Length];
            for (var index = 0; index < SeasonSuffixes.Length; index++)
            {
                var season = SeasonSuffixes[index] == string.Empty ? "Summer" : "Autumn";
                sets[index] = (
                    BuildOpaqueMaterial(
                        $"{MaterialsRoot}/M_ENV_Tree_CloudTall_{season}_Canopy.mat",
                        shader,
                        canopyGrain,
                        0.24f),
                    BuildCutoutMaterial(
                        $"{MaterialsRoot}/M_ENV_Tree_CloudTall_{season}_Fringe.mat",
                        shader,
                        tuftAtlas),
                    BuildOpaqueMaterial(
                        $"{MaterialsRoot}/M_ENV_Tree_CloudTall_{season}_Bark.mat",
                        shader,
                        barkMap,
                        0.03f));
            }

            return sets;
        }

        [MenuItem("CML/Art/CloudTall Trees/Rebuild Production Prefabs")]
        public static void Run()
        {
            EnsureFolder(PrefabsRoot);
            var sets = BuildMaterials();

            var built = 0;
            for (var index = 0; index < SeasonSuffixes.Length; index++)
            {
                var seasonSuffix = SeasonSuffixes[index];
                var (canopyMaterial, fringeMaterial, barkMaterial) = sets[index];

                foreach (var variant in Variants)
                {
                    var modelPath = ModelPath(variant, seasonSuffix);
                    RequireFile(modelPath);
                    ConfigureModelImporter(modelPath);
                    BuildPrefab(
                        variant,
                        seasonSuffix,
                        canopyMaterial,
                        fringeMaterial,
                        barkMaterial);
                    built++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();

            Debug.Log(
                $"CLOUD_TALL_TREES_SETUP prefabs={built} materialSets=2 " +
                "vertexColorAlbedo=true alphaClip=0.45 " +
                "trunkColliders=exactStanding+authoredCompound status=PASS");
        }

        [MenuItem(
            "CML/Art/CloudTall Trees/Ensure Trunk Collider Pairs")]
        public static void EnsureAuthoredTrunkColliders()
        {
            var changed = UpgradeAuthoredTrunkColliders();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();
            Debug.Log(
                $"CLOUD_TALL_TREE_COLLIDERS changedPrefabs={changed} " +
                "authority=exactStanding+authoredTrunkCompound status=PASS");
        }

        [MenuItem("CML/Art/CloudTall Trees/Validate Production Prefabs")]
        public static void Validate()
        {
            var checkedPrefabs = 0;
            foreach (var seasonSuffix in SeasonSuffixes)
            {
                foreach (var variant in Variants)
                {
                    var asset = AssetName(variant, seasonSuffix);
                    var prefab = RequireAsset<GameObject>(
                        PrefabPath(variant, seasonSuffix));

                    var renderers =
                        prefab.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 3)
                    {
                        throw new InvalidOperationException(
                            $"{asset} must expose exactly its crown, fringe " +
                            $"and trunk meshes, found {renderers.Length}.");
                    }

                    var trunk = RequireNamedRenderer(
                        prefab,
                        $"GEO_{asset}_Trunk");
                    var colliders =
                        prefab.GetComponentsInChildren<Collider>(true);
                    var standingCollider =
                        ResolveStandingTrunkCollider(trunk);
                    var trunkMesh =
                        trunk.GetComponent<MeshFilter>()?.sharedMesh;
                    var hasValidFallingCompound =
                        trunkMesh != null
                        && IsAuthoredFallingCompoundValid(
                            prefab,
                            trunk,
                            trunkMesh);
                    if (prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                        prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                        colliders.Length != 1 + FallingCapsuleCount ||
                        prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                        prefab.GetComponentsInChildren<Animator>(true).Length != 0)
                    {
                        throw new InvalidOperationException(
                            $"{asset} still contains an imported helper.");
                    }

                    if (standingCollider == null
                        || !standingCollider.enabled
                        || standingCollider.isTrigger
                        || standingCollider.convex
                        || trunkMesh == null
                        || standingCollider.sharedMesh != trunkMesh
                        || !hasValidFallingCompound
                        || trunk.GetComponents<MeshCollider>().Length != 1)
                    {
                        throw new InvalidOperationException(
                            $"{asset} requires an enabled exact non-convex " +
                            "standing collider and a disabled authored " +
                            "compound collider following only its trunk.");
                    }

                    foreach (var suffix in new[] { "Canopy", "Fringe", "Trunk" })
                    {
                        var renderer = suffix == "Trunk"
                            ? trunk
                            : RequireNamedRenderer(
                                prefab,
                                $"GEO_{asset}_{suffix}");
                        if (renderer.sharedMaterials.Length != 1 ||
                            renderer.sharedMaterial == null ||
                            renderer.sharedMaterial.shader == null ||
                            renderer.sharedMaterial.shader.name != TreeShaderName)
                        {
                            throw new InvalidOperationException(
                                $"{asset} {suffix} has no CloudTall material.");
                        }

                        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null)
                        {
                            throw new InvalidOperationException(
                                $"{asset} {suffix} lacks a production mesh.");
                        }

                        // Without the baked vertex colour the whole crown would
                        // render as flat white, so this is the load-bearing check.
                        if (mesh.colors32 == null || mesh.colors32.Length != mesh.vertexCount)
                        {
                            throw new InvalidOperationException(
                                $"{asset} {suffix} lost its vertex colours on " +
                                "import; the painted albedo lives there.");
                        }
                    }

                    var bounds = CalculateBounds(renderers);
                    if (bounds.size.y < 8f || bounds.size.y > 12f)
                    {
                        throw new InvalidOperationException(
                            $"{asset} has an implausible height of " +
                            $"{bounds.size.y:F2} metres.");
                    }

                    checkedPrefabs++;
                }
            }

            Debug.Log(
                $"CLOUD_TALL_TREES_VALIDATION prefabs={checkedPrefabs} " +
                "renderMeshes=3 vertexColors=present " +
                "trunkColliderCompounds=valid status=PASS");
        }

        private static void ConfigureColorTexture(string path, bool atlas)
        {
            RequireFile(path);
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport);
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
            importer.wrapMode = atlas
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = atlas ? 2 : 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureModelImporter(string path)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport);
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
            // The shader samples no normal map, so tangents are dead weight.
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            // WOOD-003 reads the real base vertices to place its physical
            // release pivot. Runtime access is intentional for these six
            // compact production meshes.
            importer.isReadable = true;
            importer.weldVertices = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.generateSecondaryUV = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();
        }

        private static Material BuildOpaqueMaterial(
            string path,
            Shader shader,
            Texture2D baseMap,
            float windStrength)
        {
            var material = LoadOrCreateMaterial(path, shader);
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            // The opaque maps are fully opaque; a near-zero cutoff keeps the
            // shared clip harmless while the queue stays in Geometry.
            material.SetFloat("_Cutoff", 0.02f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_ZWrite", 1f);
            ApplyWind(material, windStrength);
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");
            material.enableInstancing = true;
            material.doubleSidedGI = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildCutoutMaterial(
            string path,
            Shader shader,
            Texture2D baseMap)
        {
            var material = LoadOrCreateMaterial(path, shader);
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 1f);
            ApplyWind(material, 0.24f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.enableInstancing = true;
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyWind(Material material, float strength)
        {
            material.SetVector("_WindDirection", new Vector4(0.82f, 0f, 0.57f, 0f));
            material.SetFloat("_WindStrength", strength);
            material.SetFloat("_WindSpeed", 0.82f);
            material.SetFloat("_WindGustStrength", 0.38f);
            material.SetFloat("_WindFlutterStrength", 0.045f);
            // The crown starts around two metres up on a ten metre tree.
            material.SetFloat("_WindBaseHeight", 2f);
            material.SetFloat("_WindHeight", 8f);
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
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

            return material;
        }

        private static void BuildPrefab(
            string variant,
            string seasonSuffix,
            Material canopyMaterial,
            Material fringeMaterial,
            Material barkMaterial)
        {
            var asset = AssetName(variant, seasonSuffix);
            var source = RequireAsset<GameObject>(ModelPath(variant, seasonSuffix));
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
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
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                var canopy = RequireNamedRenderer(instance, $"GEO_{asset}_Canopy");
                var fringe = RequireNamedRenderer(instance, $"GEO_{asset}_Fringe");
                var trunk = RequireNamedRenderer(instance, $"GEO_{asset}_Trunk");

                RemoveImportedHelpers(instance, canopy, fringe, trunk);
                canopy.sharedMaterials = new[] { canopyMaterial };
                fringe.sharedMaterials = new[] { fringeMaterial };
                trunk.sharedMaterials = new[] { barkMaterial };
                foreach (var renderer in new[] { canopy, fringe, trunk })
                {
                    ConfigureRenderer(renderer);
                }

                ConfigureTrunkColliders(instance, trunk);
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    PrefabPath(variant, seasonSuffix));
                if (prefab == null)
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

        private static void RemoveImportedHelpers(
            GameObject root,
            params MeshRenderer[] keep)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (Array.IndexOf(keep, renderer) >= 0)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            }

            foreach (var component in root.GetComponentsInChildren<Camera>(true))
            {
                UnityEngine.Object.DestroyImmediate(component.gameObject);
            }

            foreach (var component in root.GetComponentsInChildren<Light>(true))
            {
                UnityEngine.Object.DestroyImmediate(component.gameObject);
            }

            foreach (var component in root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }

            foreach (var component in root.GetComponentsInChildren<Animator>(true))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        private static void ConfigureTrunkColliders(
            GameObject prefabRoot,
            MeshRenderer trunkRenderer)
        {
            var filter = trunkRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    $"{trunkRenderer.name} cannot author collision without " +
                    "its production mesh.");
            }

            foreach (var existing in
                     trunkRenderer.GetComponents<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }

            RemoveLegacyFallingColliderRoots(prefabRoot.transform);

            var standingCollider =
                trunkRenderer.gameObject.AddComponent<MeshCollider>();
            var cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices |
                MeshColliderCookingOptions.UseFastMidphase;
            standingCollider.sharedMesh = filter.sharedMesh;
            standingCollider.enabled = true;
            standingCollider.isTrigger = false;
            standingCollider.convex = false;
            standingCollider.cookingOptions = cookingOptions;

            AuthorFallingTrunkCompound(
                prefabRoot.transform,
                trunkRenderer,
                filter.sharedMesh);
        }

        private static void RemoveLegacyFallingColliderRoots(
            Transform prefabRoot)
        {
            var descendants =
                prefabRoot.GetComponentsInChildren<Transform>(true);
            // GetComponentsInChildren returns parents before children. Walking
            // backwards makes this safe even if an obsolete root was nested
            // inside another obsolete root.
            for (var index = descendants.Length - 1;
                 index >= 0;
                 index--)
            {
                var candidate = descendants[index];
                if (candidate == null
                    || candidate == prefabRoot
                    || !candidate.name.StartsWith(
                        FallingColliderRootPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(
                    candidate.gameObject);
            }
        }

        private static MeshCollider ResolveStandingTrunkCollider(
            MeshRenderer trunkRenderer)
        {
            var colliders =
                trunkRenderer.GetComponents<MeshCollider>();
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (!collider.convex)
                {
                    return collider;
                }
            }

            return null;
        }

        private static bool IsAuthoredFallingCompoundValid(
            GameObject prefabRoot,
            MeshRenderer trunkRenderer,
            Mesh trunkMesh)
        {
            var expected = BuildFallingCapsuleSpecs(
                prefabRoot.transform,
                trunkRenderer,
                trunkMesh);
            var colliderRoot = prefabRoot.transform.Find(
                FallingColliderRootName);
            if (colliderRoot == null
                || colliderRoot.parent != prefabRoot.transform
                || !colliderRoot.gameObject.activeSelf
                || colliderRoot.gameObject.layer
                != prefabRoot.layer
                || !Approximately(
                    colliderRoot.localPosition,
                    Vector3.zero)
                || Quaternion.Angle(
                    colliderRoot.localRotation,
                    Quaternion.identity)
                > GeometryRotationTolerance
                || !Approximately(
                    colliderRoot.localScale,
                    Vector3.one)
                || colliderRoot.GetComponents<Component>().Length != 1
                || colliderRoot.childCount != expected.Length)
            {
                return false;
            }

            var namedRoots = 0;
            var transforms = prefabRoot.GetComponentsInChildren<Transform>(
                includeInactive: true);
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate != prefabRoot.transform
                    && candidate.name.StartsWith(
                        FallingColliderRootPrefix,
                        StringComparison.Ordinal))
                {
                    namedRoots++;
                    if (candidate != colliderRoot)
                    {
                        return false;
                    }
                }
            }

            if (namedRoots != 1)
            {
                return false;
            }

            for (var index = 0; index < expected.Length; index++)
            {
                var child = colliderRoot.GetChild(index);
                var specification = expected[index];
                var collider = child.GetComponent<CapsuleCollider>();
                if (!string.Equals(
                        child.name,
                        specification.Name,
                        StringComparison.Ordinal)
                    || child.parent != colliderRoot
                    || !child.gameObject.activeSelf
                    || child.gameObject.layer != prefabRoot.layer
                    || child.GetComponents<Component>().Length != 2
                    || collider == null
                    || collider.enabled
                    || collider.isTrigger
                    || collider.direction != 1
                    || !Approximately(collider.center, Vector3.zero)
                    || !Approximately(
                        child.localPosition,
                        specification.LocalPosition)
                    || Quaternion.Angle(
                        child.localRotation,
                        specification.LocalRotation)
                    > GeometryRotationTolerance
                    || !Approximately(child.localScale, Vector3.one)
                    || Mathf.Abs(
                        collider.radius - specification.Radius)
                    > GeometryScalarTolerance
                    || Mathf.Abs(
                        collider.height - specification.Height)
                    > GeometryScalarTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude
                   <= GeometryPositionTolerance
                   * GeometryPositionTolerance;
        }

        private static void AuthorFallingTrunkCompound(
            Transform prefabRoot,
            MeshRenderer trunkRenderer,
            Mesh trunkMesh)
        {
            var specifications = BuildFallingCapsuleSpecs(
                prefabRoot,
                trunkRenderer,
                trunkMesh);
            var compoundRoot = new GameObject(
                FallingColliderRootName);
            compoundRoot.layer = prefabRoot.gameObject.layer;
            compoundRoot.transform.SetParent(prefabRoot, false);
            compoundRoot.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            compoundRoot.transform.localScale = Vector3.one;

            for (var index = 0;
                 index < specifications.Length;
                 index++)
            {
                var specification = specifications[index];
                var segmentObject = new GameObject(
                    specification.Name);
                segmentObject.layer = prefabRoot.gameObject.layer;
                segmentObject.transform.SetParent(
                    compoundRoot.transform,
                    false);
                segmentObject.transform.SetLocalPositionAndRotation(
                    specification.LocalPosition,
                    specification.LocalRotation);
                segmentObject.transform.localScale = Vector3.one;

                var collider =
                    segmentObject.AddComponent<CapsuleCollider>();
                collider.direction = 1;
                collider.center = Vector3.zero;
                collider.radius = specification.Radius;
                collider.height = specification.Height;
                collider.isTrigger = false;
                collider.enabled = false;
            }
        }

        private static FallingCapsuleSpec[] BuildFallingCapsuleSpecs(
            Transform prefabRoot,
            MeshRenderer trunkRenderer,
            Mesh trunkMesh)
        {
            if (FallingPathFractions.Length
                    != FallingCapsuleCount + 1
                || FallingRadiusFactors.Length
                    != FallingCapsuleCount)
            {
                throw new InvalidOperationException(
                    "CloudTall falling-collider constants are inconsistent.");
            }

            var localVertices = new Vector3[trunkMesh.vertexCount];
            var sourceVertices = trunkMesh.vertices;
            var minimumY = float.PositiveInfinity;
            var maximumY = float.NegativeInfinity;
            for (var index = 0; index < sourceVertices.Length; index++)
            {
                var local = prefabRoot.InverseTransformPoint(
                    trunkRenderer.transform.TransformPoint(
                        sourceVertices[index]));
                localVertices[index] = local;
                minimumY = Mathf.Min(minimumY, local.y);
                maximumY = Mathf.Max(maximumY, local.y);
            }

            var trunkHeight = maximumY - minimumY;
            if (!float.IsFinite(trunkHeight) || trunkHeight < 1f)
            {
                throw new InvalidOperationException(
                    $"{trunkRenderer.name} has invalid trunk bounds.");
            }

            var path = new Vector3[FallingPathFractions.Length];
            path[0] = ResolveBandCentre(
                localVertices,
                minimumY,
                trunkHeight,
                FallingPathFractions[0],
                0.045f,
                Vector2.zero,
                float.PositiveInfinity,
                trunkRenderer.name);
            var baseRadius = ResolveBandRadius(
                localVertices,
                minimumY,
                trunkHeight,
                FallingPathFractions[0],
                0.055f,
                new Vector2(path[0].x, path[0].z),
                trunkRenderer.name);
            baseRadius = Mathf.Clamp(
                baseRadius * 0.92f,
                Mathf.Max(0.10f, trunkHeight * 0.011f),
                Mathf.Min(0.55f, trunkHeight * 0.050f));

            var searchRadius = Mathf.Clamp(
                baseRadius * 2.5f,
                0.35f,
                1.25f);
            for (var index = 1; index < path.Length; index++)
            {
                var previous = new Vector2(
                    path[index - 1].x,
                    path[index - 1].z);
                path[index] = ResolveBandCentre(
                    localVertices,
                    minimumY,
                    trunkHeight,
                    FallingPathFractions[index],
                    0.055f,
                    previous,
                    searchRadius,
                    trunkRenderer.name);
            }

            var specifications =
                new FallingCapsuleSpec[FallingCapsuleCount];
            for (var index = 0;
                 index < FallingCapsuleCount;
                 index++)
            {
                var start = path[index];
                var end = path[index + 1];
                var segment = end - start;
                var length = segment.magnitude;
                if (length <= 0.05f)
                {
                    throw new InvalidOperationException(
                        $"{trunkRenderer.name} produced an invalid trunk " +
                        $"collider segment {index + 1}.");
                }

                var radius = Mathf.Max(
                    0.08f,
                    baseRadius * FallingRadiusFactors[index]);
                var direction = segment / length;
                if (direction.y <= 0.25f)
                {
                    throw new InvalidOperationException(
                        $"{trunkRenderer.name} produced a non-rising trunk " +
                        $"collider segment {index + 1}.");
                }

                // The compound owns the physical release from the authored
                // base. Its first surface point must coincide with minimumY:
                // even a centimetre of air here produces a visible drop when
                // the standing collider is swapped for the dynamic compound.
                // Only internal seams overlap, by a deliberately tiny amount.
                var startExtension = index == 0
                    ? (start.y - minimumY) / direction.y
                    : FallingCapsuleSeamOverlap * 0.5f;
                var endExtension = index
                                   == FallingCapsuleCount - 1
                    ? (maximumY - end.y) / direction.y
                    : FallingCapsuleSeamOverlap * 0.5f;
                var physicalStart = start - direction * startExtension;
                var physicalEnd = end + direction * endExtension;
                if (index == 0)
                {
                    physicalStart.y = minimumY;
                }

                if (index == FallingCapsuleCount - 1)
                {
                    physicalEnd.y = maximumY;
                }

                var physicalLength = Vector3.Distance(
                    physicalStart,
                    physicalEnd);
                if (physicalLength
                    <= radius * 2f + GeometryScalarTolerance)
                {
                    throw new InvalidOperationException(
                        $"{trunkRenderer.name} produced a capsule too short " +
                        $"for segment {index + 1}.");
                }

                specifications[index] = new FallingCapsuleSpec(
                    $"WOOD_FallingTrunk_{index + 1:00}",
                    (physicalStart + physicalEnd) * 0.5f,
                    Quaternion.FromToRotation(Vector3.up, direction),
                    radius,
                    physicalLength);
            }

            return specifications;
        }

        private static Vector3 ResolveBandCentre(
            IReadOnlyList<Vector3> vertices,
            float minimumY,
            float trunkHeight,
            float fraction,
            float halfBandFraction,
            Vector2 expectedCentre,
            float maximumDistance,
            string assetName)
        {
            var targetY = minimumY + trunkHeight * fraction;
            var halfBand = trunkHeight * halfBandFraction;
            var xValues = new List<float>();
            var zValues = new List<float>();
            var maximumDistanceSquared = maximumDistance * maximumDistance;
            for (var index = 0; index < vertices.Count; index++)
            {
                var vertex = vertices[index];
                if (Mathf.Abs(vertex.y - targetY) > halfBand)
                {
                    continue;
                }

                var offset = new Vector2(
                    vertex.x - expectedCentre.x,
                    vertex.z - expectedCentre.y);
                if (offset.sqrMagnitude > maximumDistanceSquared)
                {
                    continue;
                }

                xValues.Add(vertex.x);
                zValues.Add(vertex.z);
            }

            if (xValues.Count < MinimumBandVertexCount)
            {
                throw new InvalidOperationException(
                    $"{assetName} cannot resolve the trunk centre at " +
                    $"{fraction:P0}: only {xValues.Count} vertices remain " +
                    "inside the tracked stem band.");
            }

            return new Vector3(
                ResolvePercentile(xValues, 0.5f),
                targetY,
                ResolvePercentile(zValues, 0.5f));
        }

        private static float ResolveBandRadius(
            IReadOnlyList<Vector3> vertices,
            float minimumY,
            float trunkHeight,
            float fraction,
            float halfBandFraction,
            Vector2 centre,
            string assetName)
        {
            var targetY = minimumY + trunkHeight * fraction;
            var halfBand = trunkHeight * halfBandFraction;
            var radii = new List<float>();
            for (var index = 0; index < vertices.Count; index++)
            {
                var vertex = vertices[index];
                if (Mathf.Abs(vertex.y - targetY) > halfBand)
                {
                    continue;
                }

                radii.Add(Vector2.Distance(
                    new Vector2(vertex.x, vertex.z),
                    centre));
            }

            if (radii.Count < MinimumBandVertexCount)
            {
                throw new InvalidOperationException(
                    $"{assetName} cannot resolve the trunk radius at " +
                    $"{fraction:P0}: only {radii.Count} vertices exist in " +
                    "the base band.");
            }

            return ResolvePercentile(radii, 0.68f);
        }

        private static float ResolvePercentile(
            List<float> values,
            float percentile)
        {
            values.Sort();
            var index = Mathf.Clamp(
                Mathf.RoundToInt(
                    (values.Count - 1) * Mathf.Clamp01(percentile)),
                0,
                values.Count - 1);
            return values[index];
        }

        private static void EnsureAuthoredTrunkCollidersAfterReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall +=
                    EnsureAuthoredTrunkCollidersAfterReload;
                return;
            }

            try
            {
                var changed = UpgradeAuthoredTrunkColliders();
                if (changed <= 0)
                {
                    return;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    $"CLOUD_TALL_TREE_COLLIDERS changedPrefabs={changed} " +
                    "authority=exactStanding+authoredTrunkCompound " +
                    "status=PASS");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static int UpgradeAuthoredTrunkColliders()
        {
            var changed = 0;
            foreach (var seasonSuffix in SeasonSuffixes)
            {
                foreach (var variant in Variants)
                {
                    var asset = AssetName(variant, seasonSuffix);
                    var path = PrefabPath(variant, seasonSuffix);
                    RequireFile(path);
                    var contents =
                        PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var trunk = RequireNamedRenderer(
                            contents,
                            $"GEO_{asset}_Trunk");
                        var filter = trunk.GetComponent<MeshFilter>();
                        var standingCollider =
                            ResolveStandingTrunkCollider(trunk);
                        var colliderCount =
                            contents.GetComponentsInChildren<Collider>(
                                includeInactive: true).Length;
                        var hasValidFallingCompound = filter != null
                            && filter.sharedMesh != null
                            && IsAuthoredFallingCompoundValid(
                                contents,
                                trunk,
                                filter.sharedMesh);
                        var requiresSave = standingCollider == null
                            || filter == null
                            || filter.sharedMesh == null
                            || standingCollider.sharedMesh
                            != filter.sharedMesh
                            || !standingCollider.enabled
                            || standingCollider.isTrigger
                            || standingCollider.convex
                            || trunk.GetComponents<MeshCollider>().Length != 1
                            || !hasValidFallingCompound
                            || colliderCount != 1 + FallingCapsuleCount;
                        if (!requiresSave)
                        {
                            continue;
                        }

                        ConfigureTrunkColliders(contents, trunk);
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        changed++;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }
            }

            return changed;
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

        private static Bounds CalculateBounds(IReadOnlyList<MeshRenderer> renderers)
        {
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Count; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Required shader '{name}' is unavailable.");
            }

            return shader;
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
                    $"Required CloudTall source is missing: {assetPath}",
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

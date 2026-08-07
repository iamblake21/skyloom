using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity import boundary for the two approved Starter Island
    /// V4 LOD0 trees. Blender owns the meshes and UVs; Unity owns texture import
    /// settings, URP materials and clean production prefabs.
    /// </summary>
    public static class StarterIslandV4TreeSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Trees";
        public const string ModelsRoot = Root + "/Models";
        public const string TexturesRoot = Root + "/Textures";
        public const string MaterialsRoot = Root + "/Materials";
        public const string PrefabsRoot = Root + "/Prefabs";
        public const string ShadersRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Shaders";

        public const string CommonModelPath =
            ModelsRoot + "/ENV_Tree_CommonTall_A_LOD0.fbx";
        public const string AutumnModelPath =
            ModelsRoot + "/ENV_Tree_Autumn_A_LOD0.fbx";
        public const string CommonPrefabPath =
            PrefabsRoot + "/PF_ENV_Tree_CommonTall_A_LOD0.prefab";
        public const string AutumnPrefabPath =
            PrefabsRoot + "/PF_ENV_Tree_Autumn_A_LOD0.prefab";

        private const string LeafShaderName =
            "CML/Environment/Starter Island V4 Tree Leaves";
        private const string LeafShaderPath =
            ShadersRoot + "/StarterIslandV4TreeLeavesWind.shader";
        private const string LitShaderName =
            "Universal Render Pipeline/Lit";

        private const string BarkBasePath =
            TexturesRoot +
            "/T_ENV_Tree_CommonTall_A_Bark_BaseColor.png";
        private const string BarkNormalPath =
            TexturesRoot +
            "/T_ENV_Tree_CommonTall_A_Bark_Normal.png";
        private const string CommonLeafBasePath =
            TexturesRoot +
            "/T_ENV_Tree_CommonTall_A_LeafAtlas_BaseColor.png";
        private const string CommonLeafNormalPath =
            TexturesRoot +
            "/T_ENV_Tree_CommonTall_A_LeafAtlas_Normal.png";
        private const string AutumnAmberBasePath =
            TexturesRoot +
            "/T_ENV_Tree_Autumn_A_LeafAtlas_Amber_BaseColor.png";
        private const string AutumnOrangeBasePath =
            TexturesRoot +
            "/T_ENV_Tree_Autumn_A_LeafAtlas_Orange_BaseColor.png";
        private const string AutumnRedBasePath =
            TexturesRoot +
            "/T_ENV_Tree_Autumn_A_LeafAtlas_Red_BaseColor.png";

        private const string CommonBarkMaterialPath =
            MaterialsRoot + "/M_ENV_Tree_CommonTall_A_Bark.mat";
        private const string CommonLeafMaterialPath =
            MaterialsRoot + "/M_ENV_Tree_CommonTall_A_Leaves.mat";
        private const string AutumnBarkMaterialPath =
            MaterialsRoot + "/M_ENV_Tree_Autumn_A_Bark.mat";
        private const string AutumnAmberMaterialPath =
            MaterialsRoot + "/M_ENV_Tree_Autumn_A_Leaves_Amber.mat";
        private const string AutumnOrangeMaterialPath =
            MaterialsRoot + "/M_ENV_Tree_Autumn_A_Leaves_Orange.mat";
        private const string AutumnRedMaterialPath =
            MaterialsRoot + "/M_ENV_Tree_Autumn_A_Leaves_Red.mat";

        private static readonly TreeDefinition CommonTree =
            new TreeDefinition(
                CommonModelPath,
                CommonPrefabPath,
                "PF_ENV_Tree_CommonTall_A_LOD0",
                "ENV_Tree_CommonTall_A_LOD0_Branches",
                "ENV_Tree_CommonTall_A_LOD0_Leaves",
                1);

        private static readonly TreeDefinition AutumnTree =
            new TreeDefinition(
                AutumnModelPath,
                AutumnPrefabPath,
                "PF_ENV_Tree_Autumn_A_LOD0",
                "ENV_Tree_Autumn_A_LOD0_Branches",
                "ENV_Tree_Autumn_A_LOD0_Leaves",
                3);

        [MenuItem("CML/Art/V4 Trees/Rebuild Production Prefabs")]
        public static void Run()
        {
            RequireFile(CommonModelPath);
            RequireFile(AutumnModelPath);
            RequireFile(LeafShaderPath);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);

            ConfigureTextureImports();
            ConfigureModelImporter(CommonModelPath);
            ConfigureModelImporter(AutumnModelPath);

            AssetDatabase.ImportAsset(
                LeafShaderPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var litShader = RequireShader(LitShaderName);
            var leafShader = RequireShader(LeafShaderName);
            var commonBark = BuildBarkMaterial(
                CommonBarkMaterialPath,
                litShader);
            var autumnBark = BuildBarkMaterial(
                AutumnBarkMaterialPath,
                litShader);
            var commonLeaves = BuildLeafMaterial(
                CommonLeafMaterialPath,
                leafShader,
                CommonLeafBasePath);
            var autumnAmber = BuildLeafMaterial(
                AutumnAmberMaterialPath,
                leafShader,
                AutumnAmberBasePath);
            var autumnOrange = BuildLeafMaterial(
                AutumnOrangeMaterialPath,
                leafShader,
                AutumnOrangeBasePath);
            var autumnRed = BuildLeafMaterial(
                AutumnRedMaterialPath,
                leafShader,
                AutumnRedBasePath);

            BuildPrefab(
                CommonTree,
                commonBark,
                new[] { commonLeaves });
            BuildPrefab(
                AutumnTree,
                autumnBark,
                new[] { autumnAmber, autumnOrange, autumnRed });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();

            if (!Application.isBatchMode)
            {
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        CommonPrefabPath);
            }

            Debug.Log(
                "STARTER_ISLAND_V4_TREES_SETUP prefabs=2 barkMaterials=2 " +
                "leafMaterials=4 alphaClip=0.45 cull=Off " +
                "windSway=0.24 windSpeed=0.82 " +
                "shadowDepth=deformed status=PASS");
        }

        [MenuItem("CML/Art/V4 Trees/Validate Production Prefabs")]
        public static void Validate()
        {
            var commonBark =
                RequireAsset<Material>(CommonBarkMaterialPath);
            var autumnBark =
                RequireAsset<Material>(AutumnBarkMaterialPath);
            var commonLeaves =
                RequireAsset<Material>(CommonLeafMaterialPath);
            var autumnAmber =
                RequireAsset<Material>(AutumnAmberMaterialPath);
            var autumnOrange =
                RequireAsset<Material>(AutumnOrangeMaterialPath);
            var autumnRed =
                RequireAsset<Material>(AutumnRedMaterialPath);

            ValidateBarkMaterial(commonBark);
            ValidateBarkMaterial(autumnBark);
            ValidateLeafMaterial(commonLeaves, CommonLeafBasePath);
            ValidateLeafMaterial(autumnAmber, AutumnAmberBasePath);
            ValidateLeafMaterial(autumnOrange, AutumnOrangeBasePath);
            ValidateLeafMaterial(autumnRed, AutumnRedBasePath);

            ValidatePrefab(
                CommonTree,
                RequireAsset<GameObject>(CommonPrefabPath),
                commonBark,
                new[] { commonLeaves });
            ValidatePrefab(
                AutumnTree,
                RequireAsset<GameObject>(AutumnPrefabPath),
                autumnBark,
                new[] { autumnAmber, autumnOrange, autumnRed });

            Debug.Log(
                "STARTER_ISLAND_V4_TREES_VALIDATION prefabs=2 " +
                "renderMeshes=4 importedHelpers=0 colliders=0 status=PASS");
        }

        private static void ConfigureTextureImports()
        {
            ConfigureColorTexture(BarkBasePath, false);
            ConfigureNormalTexture(BarkNormalPath, TextureWrapMode.Repeat);
            ConfigureColorTexture(CommonLeafBasePath, true);
            ConfigureNormalTexture(
                CommonLeafNormalPath,
                TextureWrapMode.Clamp);
            ConfigureColorTexture(AutumnAmberBasePath, true);
            ConfigureColorTexture(AutumnOrangeBasePath, true);
            ConfigureColorTexture(AutumnRedBasePath, true);
        }

        private static void ConfigureColorTexture(
            string path,
            bool leafAtlas)
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
            importer.alphaIsTransparency = leafAtlas;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = leafAtlas
                ? TextureWrapMode.Clamp
                : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = leafAtlas ? 2 : 4;
            importer.textureCompression =
                TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureNormalTexture(
            string path,
            TextureWrapMode wrapMode)
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

            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = wrapMode;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression =
                TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureModelImporter(string path)
        {
            RequireFile(path);
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
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents =
                ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.generateSecondaryUV = false;
            importer.preserveHierarchy = true;
            importer.SaveAndReimport();
        }

        private static Material BuildBarkMaterial(
            string path,
            Shader shader)
        {
            var material = LoadOrCreateMaterial(path, shader);
            material.SetTexture(
                "_BaseMap",
                RequireAsset<Texture2D>(BarkBasePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture(
                "_BumpMap",
                RequireAsset<Texture2D>(BarkNormalPath));
            material.SetFloat("_BumpScale", 0.42f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.16f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.EnableKeyword("_NORMALMAP");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");
            material.enableInstancing = true;
            material.doubleSidedGI = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildLeafMaterial(
            string path,
            Shader shader,
            string baseTexturePath)
        {
            var material = LoadOrCreateMaterial(path, shader);
            material.SetTexture(
                "_BaseMap",
                RequireAsset<Texture2D>(baseTexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture(
                "_BumpMap",
                RequireAsset<Texture2D>(CommonLeafNormalPath));
            material.SetFloat("_BumpScale", 0.65f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetVector(
                "_WindDirection",
                new Vector4(0.82f, 0f, 0.57f, 0f));
            material.SetFloat("_WindStrength", 0.24f);
            material.SetFloat("_WindSpeed", 0.82f);
            material.SetFloat("_WindGustStrength", 0.38f);
            material.SetFloat("_WindFlutterStrength", 0.045f);
            material.SetFloat("_WindBaseHeight", 0.75f);
            material.SetFloat("_WindHeight", 9.5f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetOverrideTag(
                "RenderType",
                "TransparentCutout");
            material.enableInstancing = true;
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(
            string path,
            Shader shader)
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
            TreeDefinition definition,
            Material barkMaterial,
            IReadOnlyList<Material> leafMaterials)
        {
            var source = RequireAsset<GameObject>(definition.ModelPath);
            var instance =
                PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate {definition.ModelPath}.");
            }

            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.name = definition.PrefabName;
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                var branches = RequireNamedRenderer(
                    instance,
                    definition.BranchesName);
                var leaves = RequireNamedRenderer(
                    instance,
                    definition.LeavesName);

                RemoveImportedHelpers(instance, branches, leaves);
                AssignSingleMaterial(branches, barkMaterial);
                AssignMaterials(leaves, leafMaterials);
                ConfigureRenderer(branches);
                ConfigureRenderer(leaves);

                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    definition.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save {definition.PrefabPath}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RemoveImportedHelpers(
            GameObject root,
            MeshRenderer branches,
            MeshRenderer leaves)
        {
            var renderers =
                root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == branches || renderer == leaves)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(
                    renderer.gameObject);
            }

            RemoveComponentGameObjects<Camera>(root);
            RemoveComponentGameObjects<Light>(root);
            RemoveComponents<Collider>(root);
            RemoveComponents<Rigidbody>(root);
            RemoveComponents<Joint>(root);
            RemoveComponents<Animator>(root);
            RemoveComponents<Animation>(root);
        }

        private static void RemoveComponentGameObjects<T>(
            GameObject root)
            where T : Component
        {
            foreach (var component in
                     root.GetComponentsInChildren<T>(true))
            {
                if (component.gameObject == root)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(
                        component.gameObject);
                }
            }
        }

        private static void RemoveComponents<T>(GameObject root)
            where T : Component
        {
            foreach (var component in
                     root.GetComponentsInChildren<T>(true))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static void AssignSingleMaterial(
            MeshRenderer renderer,
            Material material)
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null ||
                meshFilter.sharedMesh == null ||
                meshFilter.sharedMesh.subMeshCount != 1)
            {
                throw new InvalidOperationException(
                    $"{renderer.name} must contain exactly one submesh.");
            }

            renderer.sharedMaterials = new[] { material };
        }

        private static void AssignMaterials(
            MeshRenderer renderer,
            IReadOnlyList<Material> materials)
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    $"{renderer.name} lacks a production mesh.");
            }

            if (meshFilter.sharedMesh.subMeshCount != materials.Count)
            {
                throw new InvalidOperationException(
                    $"{renderer.name} has " +
                    $"{meshFilter.sharedMesh.subMeshCount} submeshes, " +
                    $"but {materials.Count} leaf materials were supplied.");
            }

            var assigned = new Material[materials.Count];
            for (var index = 0; index < materials.Count; index++)
            {
                assigned[index] = materials[index];
            }

            renderer.sharedMaterials = assigned;
        }

        private static void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.Camera;
        }

        private static void ValidatePrefab(
            TreeDefinition definition,
            GameObject prefab,
            Material barkMaterial,
            IReadOnlyList<Material> leafMaterials)
        {
            AssertIdentity(prefab.transform, definition.PrefabName);
            var renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            var filters =
                prefab.GetComponentsInChildren<MeshFilter>(true);
            if (renderers.Length != 2 || filters.Length != 2)
            {
                throw new InvalidOperationException(
                    $"{definition.PrefabName} must contain only its " +
                    "branch and leaf render meshes.");
            }

            if (prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Joint>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animation>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{definition.PrefabName} still contains an imported " +
                    "helper, collider or animation component.");
            }

            var branches = RequireNamedRenderer(
                prefab,
                definition.BranchesName);
            var leaves = RequireNamedRenderer(
                prefab,
                definition.LeavesName);
            if (branches.sharedMaterials.Length != 1 ||
                branches.sharedMaterial != barkMaterial)
            {
                throw new InvalidOperationException(
                    $"{definition.PrefabName} has the wrong bark material.");
            }

            if (leaves.sharedMaterials.Length !=
                definition.LeafMaterialCount)
            {
                throw new InvalidOperationException(
                    $"{definition.PrefabName} has the wrong number of " +
                    "leaf material slots.");
            }

            for (var index = 0;
                 index < leafMaterials.Count;
                 index++)
            {
                if (leaves.sharedMaterials[index] != leafMaterials[index])
                {
                    throw new InvalidOperationException(
                        $"{definition.PrefabName} leaf slot {index} " +
                        "has the wrong production material.");
                }
            }

            foreach (var renderer in renderers)
            {
                if (renderer.shadowCastingMode != ShadowCastingMode.On ||
                    !renderer.receiveShadows)
                {
                    throw new InvalidOperationException(
                        $"{definition.PrefabName} has invalid shadow " +
                        $"settings on {renderer.name}.");
                }
            }

            var bounds = CalculateBounds(renderers);
            if (bounds.size.y < 3f || bounds.size.y > 40f)
            {
                throw new InvalidOperationException(
                    $"{definition.PrefabName} has an implausible " +
                    $"height of {bounds.size.y:F2} metres.");
            }
        }

        private static void ValidateBarkMaterial(Material material)
        {
            if (material.shader == null ||
                material.shader.name != LitShaderName ||
                material.GetTexture("_BaseMap") !=
                RequireAsset<Texture2D>(BarkBasePath) ||
                material.GetTexture("_BumpMap") !=
                RequireAsset<Texture2D>(BarkNormalPath) ||
                !material.IsKeywordEnabled("_NORMALMAP") ||
                material.renderQueue != (int)RenderQueue.Geometry)
            {
                throw new InvalidOperationException(
                    $"Bark material {material.name} is not a valid " +
                    "opaque URP material.");
            }
        }

        private static void ValidateLeafMaterial(
            Material material,
            string baseTexturePath)
        {
            if (material.shader == null ||
                material.shader.name != LeafShaderName ||
                material.GetTexture("_BaseMap") !=
                RequireAsset<Texture2D>(baseTexturePath) ||
                material.GetTexture("_BumpMap") !=
                RequireAsset<Texture2D>(CommonLeafNormalPath) ||
                !material.IsKeywordEnabled("_ALPHATEST_ON") ||
                !material.IsKeywordEnabled("_NORMALMAP") ||
                !Mathf.Approximately(
                    material.GetFloat("_Cutoff"),
                    0.45f) ||
                !Mathf.Approximately(
                    material.GetFloat("_Cull"),
                    (float)CullMode.Off) ||
                !material.HasProperty("_WindStrength") ||
                !material.HasProperty("_WindSpeed") ||
                !material.HasProperty("_WindGustStrength") ||
                !material.HasProperty("_WindFlutterStrength") ||
                !Mathf.Approximately(
                    material.GetFloat("_WindStrength"),
                    0.24f) ||
                !Mathf.Approximately(
                    material.GetFloat("_WindSpeed"),
                    0.82f) ||
                !Mathf.Approximately(
                    material.GetFloat("_WindGustStrength"),
                    0.38f) ||
                !Mathf.Approximately(
                    material.GetFloat("_WindFlutterStrength"),
                    0.045f) ||
                material.renderQueue != (int)RenderQueue.AlphaTest ||
                !material.doubleSidedGI)
            {
                throw new InvalidOperationException(
                    $"Leaf material {material.name} is not a valid " +
                    "double-sided alpha-clipped URP material.");
            }
        }

        private static MeshRenderer RequireNamedRenderer(
            GameObject root,
            string name)
        {
            MeshRenderer match = null;
            foreach (var renderer in
                     root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!string.Equals(
                        renderer.name,
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Multiple renderers named {name} exist in " +
                        $"{root.name}.");
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

        private static Bounds CalculateBounds(
            IReadOnlyList<MeshRenderer> renderers)
        {
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Count; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void AssertIdentity(
            Transform transform,
            string assetName)
        {
            if (transform.localPosition.sqrMagnitude > 0.000001f ||
                Quaternion.Angle(
                    transform.localRotation,
                    Quaternion.identity) > 0.001f ||
                (transform.localScale - Vector3.one).sqrMagnitude >
                0.000001f)
            {
                throw new InvalidOperationException(
                    $"{assetName} root transform is not identity.");
            }
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

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
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
                    $"Required tree source is missing: {assetPath}",
                    absolutePath);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            var name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) ||
                string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(
                    $"Invalid Unity folder path: {assetPath}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private sealed class TreeDefinition
        {
            public TreeDefinition(
                string modelPath,
                string prefabPath,
                string prefabName,
                string branchesName,
                string leavesName,
                int leafMaterialCount)
            {
                ModelPath = modelPath;
                PrefabPath = prefabPath;
                PrefabName = prefabName;
                BranchesName = branchesName;
                LeavesName = leavesName;
                LeafMaterialCount = leafMaterialCount;
            }

            public string ModelPath { get; }
            public string PrefabPath { get; }
            public string PrefabName { get; }
            public string BranchesName { get; }
            public string LeavesName { get; }
            public int LeafMaterialCount { get; }
        }
    }
}

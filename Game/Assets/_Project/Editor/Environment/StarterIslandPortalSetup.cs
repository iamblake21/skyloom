using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Deterministic Unity-side import and prefab setup for the authored
    /// Meshy ancient-stone portal. The FBX owns geometry and UVs; Unity owns
    /// physical scale, material response and the clean scene-ready prefab.
    /// </summary>
    public static class StarterIslandPortalSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/Portal";
        public const string ModelsRoot = Root + "/Models";
        public const string TexturesRoot = Root + "/Textures";
        public const string MaterialsRoot = Root + "/Materials";
        public const string PrefabsRoot = Root + "/Prefabs";

        public const string ModelPath =
            ModelsRoot + "/ENV_AncientStonePortal.fbx";
        public const string BaseColorPath =
            TexturesRoot + "/T_ENV_AncientStonePortal_BaseColor.png";
        public const string NormalPath =
            TexturesRoot + "/T_ENV_AncientStonePortal_Normal.png";
        public const string MaterialPath =
            MaterialsRoot + "/M_ENV_AncientStonePortal_Stone.mat";
        public const string PrefabPath =
            PrefabsRoot + "/PF_AncientStonePortal.prefab";

        private const string LitShaderName =
            "Universal Render Pipeline/Lit";
        private const float TargetHeightMetres = 19f;
        private const float HeightToleranceMetres = 0.06f;
        private const long TriangleBudget = 85000L;

        [MenuItem("CML/Art/Rebuild Starter Island Ancient Portal")]
        public static void Run()
        {
            RequireFile(ModelPath);
            RequireFile(BaseColorPath);
            RequireFile(NormalPath);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);

            ConfigureBaseColorTexture();
            ConfigureNormalTexture();
            ConfigureModelImporter();

            var shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP/Lit is unavailable. Verify the active render pipeline.");
            }

            var material = BuildMaterial(shader);
            BuildPrefab(material);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate();

            if (!Application.isBatchMode)
            {
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            Debug.Log(
                "STARTER_ISLAND_ANCIENT_PORTAL_SETUP height=19.00m " +
                "material=URP_Lit metallic=0 smoothness=0.18 " +
                "colliders=0 triangleBudget=85000 status=PASS");
        }

        [MenuItem("CML/Art/Validate Starter Island Ancient Portal")]
        public static void Validate()
        {
            var importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null ||
                !importer.bakeAxisConversion ||
                !importer.preserveHierarchy ||
                importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.addCollider)
            {
                throw new InvalidOperationException(
                    "Ancient portal FBX importer settings are invalid.");
            }

            var baseImporter =
                AssetImporter.GetAtPath(BaseColorPath) as TextureImporter;
            var normalImporter =
                AssetImporter.GetAtPath(NormalPath) as TextureImporter;
            if (baseImporter == null ||
                baseImporter.textureType != TextureImporterType.Default ||
                !baseImporter.sRGBTexture ||
                normalImporter == null ||
                normalImporter.textureType !=
                TextureImporterType.NormalMap ||
                normalImporter.sRGBTexture)
            {
                throw new InvalidOperationException(
                    "Ancient portal texture import settings are invalid.");
            }

            var material = RequireAsset<Material>(MaterialPath);
            if (material.shader == null ||
                !string.Equals(
                    material.shader.name,
                    LitShaderName,
                    StringComparison.Ordinal) ||
                material.GetTexture("_BaseMap") !=
                RequireAsset<Texture2D>(BaseColorPath) ||
                material.GetTexture("_BumpMap") !=
                RequireAsset<Texture2D>(NormalPath) ||
                material.GetFloat("_Metallic") > 0.001f ||
                material.GetFloat("_Smoothness") > 0.25f ||
                !material.IsKeywordEnabled("_NORMALMAP"))
            {
                throw new InvalidOperationException(
                    "Ancient portal material is not configured as matte stone.");
            }

            var prefab = RequireAsset<GameObject>(PrefabPath);
            ValidateIdentity(prefab.transform, "Portal prefab root");
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Ancient portal prefab must not contain colliders.");
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Ancient portal prefab has no renderers.");
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer.shadowCastingMode != ShadowCastingMode.On ||
                    !renderer.receiveShadows)
                {
                    throw new InvalidOperationException(
                        $"Portal renderer '{renderer.name}' has invalid " +
                        "shadow settings.");
                }

                var assigned = renderer.sharedMaterials;
                if (assigned.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Portal renderer '{renderer.name}' has no material.");
                }

                for (var slot = 0; slot < assigned.Length; slot++)
                {
                    if (assigned[slot] != material)
                    {
                        throw new InvalidOperationException(
                            $"Portal renderer '{renderer.name}' slot {slot} " +
                            "does not use the approved stone material.");
                    }
                }
            }

            var bounds = CalculateRendererBounds(prefab);
            if (Mathf.Abs(bounds.size.y - TargetHeightMetres) >
                HeightToleranceMetres)
            {
                throw new InvalidOperationException(
                    $"Ancient portal height is {bounds.size.y:F3}m; expected " +
                    $"{TargetHeightMetres:F2}m.");
            }

            if (Mathf.Abs(bounds.min.y) > HeightToleranceMetres)
            {
                throw new InvalidOperationException(
                    $"Ancient portal base is at Y={bounds.min.y:F3}; expected " +
                    "it to rest at Y=0.");
            }

            var triangleCount = CountRenderedTriangles(prefab);
            if (triangleCount <= 0 || triangleCount > TriangleBudget)
            {
                throw new InvalidOperationException(
                    $"Ancient portal triangle count {triangleCount} is outside " +
                    $"the 1-{TriangleBudget} production budget.");
            }

            Debug.Log(
                $"STARTER_ISLAND_ANCIENT_PORTAL_VALIDATION " +
                $"height={bounds.size.y:F2}m triangles={triangleCount} " +
                $"renderers={renderers.Length} colliders=0 status=PASS");
        }

        private static void ConfigureBaseColorTexture()
        {
            AssetDatabase.ImportAsset(
                BaseColorPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer =
                AssetImporter.GetAtPath(BaseColorPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load TextureImporter for {BaseColorPath}.");
            }

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression =
                TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureNormalTexture()
        {
            AssetDatabase.ImportAsset(
                NormalPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer =
                AssetImporter.GetAtPath(NormalPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load TextureImporter for {NormalPath}.");
            }

            importer.textureType = TextureImporterType.NormalMap;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression =
                TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(
                ModelPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not load ModelImporter for {ModelPath}.");
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
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.weldVertices = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.generateSecondaryUV = false;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
            importer.SaveAndReimport();
        }

        private static Material BuildMaterial(Shader shader)
        {
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_ENV_AncientStonePortal_Stone"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            SetTexture(
                material,
                "_BaseMap",
                RequireAsset<Texture2D>(BaseColorPath));
            SetTexture(
                material,
                "_MainTex",
                RequireAsset<Texture2D>(BaseColorPath));
            SetColor(material, "_BaseColor", Color.white);
            SetColor(material, "_Color", Color.white);
            SetTexture(
                material,
                "_BumpMap",
                RequireAsset<Texture2D>(NormalPath));
            SetFloat(material, "_BumpScale", 0.82f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.18f);
            SetFloat(material, "_Surface", 0f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", (float)CullMode.Back);
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_ReceiveShadows", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildPrefab(Material material)
        {
            var source = RequireAsset<GameObject>(ModelPath);
            var root = new GameObject("PF_AncientStonePortal");
            root.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            root.transform.localScale = Vector3.one;

            try
            {
                var visual =
                    PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (visual == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate ancient portal model: " +
                        $"{ModelPath}");
                }

                visual.name = "VIS_AncientStonePortal";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                RemoveCollidersAndAnimation(visual);
                ConfigureRenderers(visual, material);

                var sourceBounds = CalculateRendererBounds(root);
                if (sourceBounds.size.y <= 0.001f)
                {
                    throw new InvalidOperationException(
                        "Ancient portal FBX has zero renderable height.");
                }

                var uniformScale =
                    TargetHeightMetres / sourceBounds.size.y;
                visual.transform.localScale =
                    Vector3.one * uniformScale;

                var scaledBounds = CalculateRendererBounds(root);
                visual.transform.localPosition += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);

                SetStaticRecursively(root);
                var prefab =
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save ancient portal prefab: {PrefabPath}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void RemoveCollidersAndAnimation(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index]);
            }

            var animators = root.GetComponentsInChildren<Animator>(true);
            for (var index = 0; index < animators.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(animators[index]);
            }

            var animations = root.GetComponentsInChildren<Animation>(true);
            for (var index = 0; index < animations.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(animations[index]);
            }
        }

        private static void ConfigureRenderers(
            GameObject root,
            Material material)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Ancient portal FBX has no renderers.");
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                var sourceSlots = renderer.sharedMaterials.Length;
                var assigned = new Material[Mathf.Max(1, sourceSlots)];
                for (var slot = 0; slot < assigned.Length; slot++)
                {
                    assigned[slot] = material;
                }

                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage =
                    ReflectionProbeUsage.BlendProbes;
                renderer.allowOcclusionWhenDynamic = true;
            }
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{root.name} has no renderers.");
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static long CountRenderedTriangles(GameObject root)
        {
            long triangles = 0;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < filters.Length; index++)
            {
                triangles += CountTriangles(filters[index].sharedMesh);
            }

            var skinned =
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var index = 0; index < skinned.Length; index++)
            {
                triangles += CountTriangles(skinned[index].sharedMesh);
            }

            return triangles;
        }

        private static long CountTriangles(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            long triangles = 0;
            for (var subMesh = 0;
                 subMesh < mesh.subMeshCount;
                 subMesh++)
            {
                triangles += mesh.GetIndexCount(subMesh) / 3L;
            }

            return triangles;
        }

        private static void ValidateIdentity(
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
                    $"{context} must use an identity transform.");
            }
        }

        private static void SetStaticRecursively(GameObject root)
        {
            var flags =
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic;
            var transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    transforms[index].gameObject,
                    flags);
            }
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

        private static void RequireFile(string path)
        {
            var absolutePath = Path.Combine(
                Application.dataPath,
                path.Substring("Assets/".Length));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"Required ancient portal source file is missing: {path}",
                    path);
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
                    AssetDatabase.CreateFolder(
                        current,
                        segments[index]);
                }

                current = next;
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
    }
}

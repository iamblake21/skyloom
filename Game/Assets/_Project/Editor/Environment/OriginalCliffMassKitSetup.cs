using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Imports and validates the original cliff-mass kit. The FBXs are generated
    /// from original procedural source; locally extracted study references never
    /// enter the Unity project.
    /// </summary>
    public static class OriginalCliffMassKitSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/OriginalCliffMassKit";
        public const string CatalogScenePath =
            Root + "/Preview/SCN_OriginalCliffMassKit_Catalog.unity";
        public const string AssemblyScenePath =
            Root + "/Preview/SCN_OriginalCliffMassKit_Assembly.unity";

        private const string Models = Root + "/Models";
        private const string Materials = Root + "/Materials";
        private const string Prefabs = Root + "/Prefabs";
        private const string MaterialPath =
            Materials + "/M_OriginalCliffMass.mat";
        private const string ShaderName =
            "CML/Environment/Original Cliff Mass";
        private const string CliffAlbedoPath =
            Root + "/Textures/T_OriginalCliff_Albedo_Procedural_v2.png";
        private const string SessionKey =
            "CML.OriginalCliffMassKitSetup.v1";

        private static readonly string[] AssetNames =
        {
            "ENV_CliffMass_A",
            "ENV_CliffMass_B",
            "ENV_CliffMass_C",
            "ENV_CliffMass_D",
            "ENV_CliffShelf_A",
            "ENV_CliffShelf_B",
            "ENV_CliffShelf_C",
            "ENV_CliffShelf_D"
        };

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticBuild()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                var firstModel = Models + "/" + AssetNames[0] + ".fbx";
                if (File.Exists(Path.GetFullPath(firstModel)))
                {
                    Run();
                }
            };
        }

        [MenuItem("CML/Art/Rebuild Original Cliff Mass Kit")]
        public static void Run()
        {
            EnsureFolder(Root);
            EnsureFolder(Materials);
            EnsureFolder(Prefabs);
            EnsureFolder(Root + "/Preview");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var material = BuildMaterial();
            var prefabs = new List<GameObject>(AssetNames.Length);
            foreach (var assetName in AssetNames)
            {
                var modelPath = Models + "/" + assetName + ".fbx";
                ConfigureModel(modelPath);
                prefabs.Add(BuildPrefab(assetName, modelPath, material));
            }

            BuildCatalogScene(prefabs);
            BuildAssemblyScene(prefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("ORIGINAL_CLIFF_MASS_KIT " + Validate(prefabs) +
                      " status=PASS");
        }

        [MenuItem("CML/Art/Render Original Cliff Mass Kit Validation")]
        public static void RenderValidation()
        {
            var outputDirectory = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../Artifacts/OriginalCliffMassKit"));
            Directory.CreateDirectory(outputDirectory);
            RenderSceneToPng(
                CatalogScenePath,
                Path.Combine(outputDirectory, "Unity_OriginalCliffMassKit_Catalog.png"));
            RenderSceneToPng(
                AssemblyScenePath,
                Path.Combine(outputDirectory, "Unity_OriginalCliffMassKit_Assembly.png"));
            Debug.Log("ORIGINAL_CLIFF_MASS_KIT_RENDER status=PASS output=" +
                      outputDirectory);
        }

        private static Material BuildMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Shader '{ShaderName}' is unavailable or failed to compile.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            var cliffAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                CliffAlbedoPath);
            if (cliffAlbedo == null)
            {
                throw new InvalidOperationException(
                    $"Generated cliff albedo is unavailable: {CliffAlbedoPath}");
            }

            material.name = "M_OriginalCliffMass";
            material.SetTexture("_CliffAlbedo", cliffAlbedo);
            material.SetTexture("_CliffNormal", null);
            material.SetColor("_CliffTint", new Color(0.86f, 1.0f, 1.08f));
            material.SetFloat("_CliffTileScale", 0.022f);
            material.SetFloat("_ProjectionSharpness", 4.0f);
            material.SetFloat("_UseMeshUV", 1.0f);
            material.SetFloat("_CliffNormalStrength", 0.0f);
            material.SetFloat("_MacroVariation", 0.045f);
            material.SetFloat("_NormalRelief", 0.16f);
            material.SetColor("_GrassDarkColor", new Color(0.208f, 0.271f, 0.165f));
            material.SetColor("_GrassColor", new Color(0.443f, 0.518f, 0.231f));
            material.SetColor("_GrassHighlightColor", new Color(0.620f, 0.671f, 0.329f));
            material.SetFloat("_TopSlopeStart", 0.48f);
            material.SetFloat("_TopSlopeEnd", 0.84f);
            material.SetFloat("_TopBreakup", 0.15f);
            material.SetFloat("_GrassNoiseScale", 0.17f);
            material.SetFloat("_AmbientStrength", 0.82f);
            material.SetFloat("_DirectStrength", 0.86f);
            material.SetFloat("_ShadowFloor", 0.28f);
            material.enableInstancing = true;
            material.doubleSidedGI = false;
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureModel(string path)
        {
            if (!File.Exists(Path.GetFullPath(path)))
            {
                throw new FileNotFoundException("Missing original cliff FBX", path);
            }

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
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.normalCalculationMode =
                ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            importer.generateSecondaryUV = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.meshCompression = ModelImporterMeshCompression.Low;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static GameObject BuildPrefab(
            string assetName,
            string modelPath,
            Material material)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    $"Imported model is unavailable: {modelPath}");
            }

            var sourceFilters = model.GetComponentsInChildren<MeshFilter>(true);
            var meshes = new Mesh[3];
            var sourceTransforms = new Transform[3];
            for (var lod = 0; lod < meshes.Length; lod++)
            {
                var suffix = "_LOD" + lod;
                var filter = sourceFilters.FirstOrDefault(
                    candidate => candidate.name.EndsWith(
                        suffix, StringComparison.Ordinal));
                if (filter == null || filter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        $"{assetName} does not contain {suffix}.");
                }

                meshes[lod] = filter.sharedMesh;
                sourceTransforms[lod] = filter.transform;
            }

            var root = new GameObject("PF_" + assetName);
            try
            {
                GameObjectUtility.SetStaticEditorFlags(
                    root,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic);

                var renderers = new Renderer[3];
                for (var lod = 0; lod < meshes.Length; lod++)
                {
                    var child = new GameObject("LOD" + lod);
                    child.transform.SetParent(root.transform, false);
                    CopyImportedTransform(
                        model.transform,
                        sourceTransforms[lod],
                        child.transform);
                    var filter = child.AddComponent<MeshFilter>();
                    filter.sharedMesh = meshes[lod];
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderers[lod] = renderer;
                }

                var lodGroup = root.AddComponent<LODGroup>();
                lodGroup.fadeMode = LODFadeMode.CrossFade;
                lodGroup.animateCrossFading = false;
                lodGroup.SetLODs(new[]
                {
                    new LOD(0.46f, new[] { renderers[0] })
                    {
                        fadeTransitionWidth = 0.08f
                    },
                    new LOD(0.18f, new[] { renderers[1] })
                    {
                        fadeTransitionWidth = 0.08f
                    },
                    new LOD(0.012f, new[] { renderers[2] })
                    {
                        fadeTransitionWidth = 0.05f
                    }
                });
                lodGroup.RecalculateBounds();

                var collisionObject = new GameObject("Collision");
                collisionObject.transform.SetParent(root.transform, false);
                CopyImportedTransform(
                    model.transform,
                    sourceTransforms[2],
                    collisionObject.transform);
                var collider = collisionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshes[2];
                collider.convex = false;

                var bounds = renderers[0].bounds;
                CreateAnchor(root.transform, "Snap_Base", new Vector3(
                    bounds.center.x, bounds.min.y, bounds.center.z));
                CreateAnchor(root.transform, "Snap_Top", new Vector3(
                    bounds.center.x, bounds.max.y, bounds.center.z));
                CreateAnchor(root.transform, "Snap_West", new Vector3(
                    bounds.min.x, bounds.center.y, bounds.center.z));
                CreateAnchor(root.transform, "Snap_East", new Vector3(
                    bounds.max.x, bounds.center.y, bounds.center.z));
                CreateAnchor(root.transform, "Snap_South", new Vector3(
                    bounds.center.x, bounds.center.y, bounds.min.z));
                CreateAnchor(root.transform, "Snap_North", new Vector3(
                    bounds.center.x, bounds.center.y, bounds.max.z));

                return PrefabUtility.SaveAsPrefabAsset(
                    root, Prefabs + "/PF_" + assetName + ".prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateAnchor(
            Transform parent,
            string name,
            Vector3 localPosition)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPosition;
        }

        private static void CopyImportedTransform(
            Transform modelRoot,
            Transform source,
            Transform destination)
        {
            destination.localPosition = modelRoot.InverseTransformPoint(source.position);
            destination.localRotation =
                Quaternion.Inverse(modelRoot.rotation) * source.rotation;
            var rootScale = modelRoot.lossyScale;
            var sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                sourceScale.x / Mathf.Max(Mathf.Abs(rootScale.x), 0.00001f),
                sourceScale.y / Mathf.Max(Mathf.Abs(rootScale.y), 0.00001f),
                sourceScale.z / Mathf.Max(Mathf.Abs(rootScale.z), 0.00001f));
        }

        private static Scene BeginPreviewScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Preview Ground";
            ground.transform.localScale = new Vector3(35f, 1f, 25f);
            ground.GetComponent<Renderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            var lightObject = new GameObject("Sun");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.4f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(43f, -32f, 0f);

            var cameraObject = new GameObject("Preview Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.57f, 0.78f);
            camera.fieldOfView = 48f;
            return scene;
        }

        private static void BuildCatalogScene(IReadOnlyList<GameObject> prefabs)
        {
            var scene = BeginPreviewScene();
            var positions = new[]
            {
                new Vector3(-82f, 0f, 30f),
                new Vector3(-27f, 0f, 30f),
                new Vector3(31f, 0f, 30f),
                new Vector3(88f, 0f, 30f),
                new Vector3(-55f, 0f, -32f),
                new Vector3(-17f, 0f, -32f),
                new Vector3(21f, 0f, -32f),
                new Vector3(58f, 0f, -32f)
            };

            for (var index = 0; index < prefabs.Count; index++)
            {
                var instance = PrefabUtility.InstantiatePrefab(
                    prefabs[index], scene) as GameObject;
                if (instance != null)
                {
                    instance.transform.position = positions[index];
                }
            }

            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            camera.transform.position = new Vector3(145f, 112f, -245f);
            camera.transform.LookAt(new Vector3(0f, 19f, 8f));
            EditorSceneManager.SaveScene(scene, CatalogScenePath);
        }

        private static void BuildAssemblyScene(IReadOnlyList<GameObject> prefabs)
        {
            var scene = BeginPreviewScene();
            var placements = new[]
            {
                new Placement(2, new Vector3(-48f, -14f, 24f), new Vector3(1.18f, 0.82f, 1.02f), -8f),
                new Placement(0, new Vector3(-6f, -6f, 20f), new Vector3(1.10f, 0.90f, 1.00f), 7f),
                new Placement(1, new Vector3(36f, -7f, 23f), new Vector3(1.06f, 0.84f, 1.00f), -11f),
                new Placement(3, new Vector3(70f, -18f, 30f), new Vector3(0.96f, 0.82f, 0.96f), 12f),
                new Placement(1, new Vector3(-34f, 29f, 28f), new Vector3(0.86f, 0.74f, 0.82f), 11f),
                new Placement(2, new Vector3(3f, 24f, 29f), new Vector3(0.82f, 0.72f, 0.80f), -6f),
                new Placement(0, new Vector3(37f, 26f, 31f), new Vector3(0.78f, 0.68f, 0.76f), 9f),
                new Placement(5, new Vector3(-67f, -3f, -6f), new Vector3(1.45f, 0.90f, 1.10f), -8f),
                new Placement(6, new Vector3(-26f, -2f, -3f), new Vector3(1.30f, 0.92f, 1.06f), 7f),
                new Placement(7, new Vector3(21f, -2f, -1f), new Vector3(1.38f, 0.94f, 1.05f), -4f),
                new Placement(4, new Vector3(62f, -3f, 3f), new Vector3(1.10f, 0.88f, 1.00f), 8f)
            };

            foreach (var placement in placements)
            {
                var instance = PrefabUtility.InstantiatePrefab(
                    prefabs[placement.PrefabIndex], scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.transform.position = placement.Position;
                instance.transform.localScale = placement.Scale;
                instance.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);
            }

            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            camera.transform.position = new Vector3(145f, 102f, -238f);
            camera.transform.LookAt(new Vector3(2f, 29f, 15f));
            EditorSceneManager.SaveScene(scene, AssemblyScenePath);
        }

        private static void RenderSceneToPng(string scenePath, string outputPath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                throw new InvalidOperationException(
                    $"Validation scene has no camera: {scenePath}");
            }

            var renderTexture = new RenderTexture(
                1800, 1000, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var texture = new Texture2D(
                1800, 1000, TextureFormat.RGB24, false, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0f, 0f, 1800f, 1000f), 0, 0);
                texture.Apply(false, false);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static string Validate(IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs.Count != AssetNames.Length || prefabs.Any(item => item == null))
            {
                throw new InvalidOperationException(
                    $"Expected {AssetNames.Length} valid prefabs, found {prefabs.Count}.");
            }

            long lod0Triangles = 0;
            foreach (var prefab in prefabs)
            {
                var group = prefab.GetComponent<LODGroup>();
                var collider = prefab.GetComponentInChildren<MeshCollider>(true);
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true)
                    .OrderBy(filter => filter.name)
                    .ToArray();
                if (group == null || group.GetLODs().Length != 3 ||
                    collider == null || collider.sharedMesh == null ||
                    filters.Length != 3 ||
                    prefab.transform.Find("Snap_Base") == null ||
                    prefab.transform.Find("Snap_Top") == null ||
                    prefab.transform.Find("Snap_West") == null ||
                    prefab.transform.Find("Snap_East") == null ||
                    prefab.transform.Find("Snap_South") == null ||
                    prefab.transform.Find("Snap_North") == null)
                {
                    throw new InvalidOperationException(
                        $"Prefab contract failed for {prefab.name}.");
                }

                var triangleCounts = filters
                    .Select(filter => filter.sharedMesh.triangles.Length / 3)
                    .ToArray();
                if (!(triangleCounts[0] > triangleCounts[1] &&
                      triangleCounts[1] > triangleCounts[2]))
                {
                    throw new InvalidOperationException(
                        $"LOD triangle order failed for {prefab.name}: " +
                        string.Join(",", triangleCounts));
                }

                var bounds = prefab.GetComponentsInChildren<Renderer>(true)
                    .First(renderer => renderer.name == "LOD0").bounds;
                if (Mathf.Abs(bounds.min.y) > 0.05f ||
                    bounds.size.x < 10f || bounds.size.y < 8f || bounds.size.z < 10f)
                {
                    throw new InvalidOperationException(
                        $"Scale or bottom pivot failed for {prefab.name}: {bounds}");
                }

                lod0Triangles += triangleCounts[0];
            }

            return $"prefabs={prefabs.Count} lods=3 colliders={prefabs.Count} " +
                   $"anchors={prefabs.Count * 6} lod0_triangles={lod0Triangles}";
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

        private readonly struct Placement
        {
            public Placement(
                int prefabIndex,
                Vector3 position,
                Vector3 scale,
                float yaw)
            {
                PrefabIndex = prefabIndex;
                Position = position;
                Scale = scale;
                Yaw = yaw;
            }

            public int PrefabIndex { get; }
            public Vector3 Position { get; }
            public Vector3 Scale { get; }
            public float Yaw { get; }
        }
    }
}

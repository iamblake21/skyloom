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
    /// Imports the reference-matched faceted rock kit and builds runtime-ready
    /// prefabs. The source FBXs contain three named meshes; this setup owns the
    /// LODGroup, static collider, material, import contract, and QA scene.
    /// </summary>
    public static class ReferenceRockKitSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StylizedRockWorldKit";
        public const string PreviewScenePath =
            Root + "/Preview/SCN_ReferenceRockKit_Catalog.unity";

        private const string Models = Root + "/Models";
        private const string Materials = Root + "/Materials";
        private const string Prefabs = Root + "/Prefabs";
        private const string MaterialPath =
            Materials + "/M_ReferenceFacetedRock.mat";
        private const string ShaderName =
            "CML/Environment/Starter Island Stylized Surface";
        private const string SessionKey =
            "CML.ReferenceRockKitSetup.v4";

        private static readonly string[] AssetNames =
        {
            "ENV_ReferenceArch_A",
            "ENV_ReferenceLedge_A",
            "ENV_ReferenceLedge_B",
            "ENV_ReferencePillar_Narrow_A",
            "ENV_ReferencePillar_Narrow_B",
            "ENV_ReferenceWall_A",
            "ENV_ReferenceWall_B",
            "ENV_ReferenceSpire_A",
            "ENV_ReferenceBoulder_Undercut_A",
            "ENV_ReferenceBoulder_TopHeavy_A",
            "ENV_ReferenceBoulder_Block_A",
            "ENV_ReferenceBoulder_Block_B",
            "ENV_ReferenceRock_S_A",
            "ENV_ReferenceRock_S_B",
            "ENV_ReferenceRock_S_C",
            "ENV_ReferenceRock_S_D",
            "ENV_ReferenceCluster_S_A",
            "ENV_ReferenceCluster_S_B",
            "ENV_ReferenceCluster_S_C",
            "ENV_ReferenceCluster_S_D",
            "ENV_ReferenceFlat_A",
            "ENV_ReferenceFlat_B",
            "ENV_ReferenceFlat_C",
            "ENV_ReferencePebble_A",
            "ENV_ReferencePebble_B",
            "ENV_ReferenceRock_Front_A",
            "ENV_ReferenceRock_Front_B",
            "ENV_ReferenceRock_Front_C"
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

        [MenuItem("CML/Art/Rebuild Reference Rock Kit")]
        public static void Run()
        {
            EnsureFolder(Root);
            EnsureFolder(Materials);
            EnsureFolder(Prefabs);
            EnsureFolder(Root + "/Preview");

            var material = BuildMaterial();
            var prefabs = new List<GameObject>(AssetNames.Length);
            foreach (var assetName in AssetNames)
            {
                var modelPath = Models + "/" + assetName + ".fbx";
                ConfigureModel(modelPath);
                prefabs.Add(BuildPrefab(assetName, modelPath, material));
            }

            BuildPreviewScene(prefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var report = Validate(prefabs);
            Debug.Log("REFERENCE_ROCK_KIT " + report + " status=PASS");
        }

        private static Material BuildMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Shader '{ShaderName}' is unavailable.");
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

            material.name = "M_ReferenceFacetedRock";
            material.SetColor("_BaseColor", new Color(0.58f, 0.66f, 0.70f));
            material.SetColor("_SecondaryColor", new Color(0.36f, 0.46f, 0.53f));
            material.SetColor("_WetColor", new Color(0.29f, 0.38f, 0.45f));
            material.SetFloat("_VertexBlend", 1f);
            material.SetFloat("_AmbientStrength", 0.86f);
            material.SetFloat("_ShadowFloor", 0.46f);
            material.SetFloat("_ColorVariation", 0.012f);
            material.SetFloat("_RockDetail", 1f);
            material.SetColor("_RockTopColor", new Color(0.66f, 0.71f, 0.72f));
            material.SetColor("_RockUnderColor", new Color(0.30f, 0.38f, 0.45f));
            material.SetFloat("_RockTopStrength", 0.34f);
            material.SetFloat("_RockUnderStrength", 0.25f);
            material.SetFloat("_RockMacroScale", 0.31f);
            material.SetFloat("_RockMacroStrength", 0.035f);
            material.SetFloat("_RockGrainScale", 3.2f);
            material.SetFloat("_RockGrainStrength", 0.012f);
            material.SetFloat("_RockContactBlend", 0f);
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
                throw new FileNotFoundException("Missing reference rock FBX", path);
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
            importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            importer.generateSecondaryUV = true;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
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
            for (var index = 0; index < meshes.Length; index++)
            {
                var suffix = "_LOD" + index;
                var filter = sourceFilters.FirstOrDefault(
                    candidate => candidate.name.EndsWith(
                        suffix,
                        StringComparison.Ordinal));
                if (filter == null || filter.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        $"{assetName} does not contain {suffix}.");
                }

                meshes[index] = filter.sharedMesh;
            }

            var root = new GameObject("PF_" + assetName);
            var renderers = new Renderer[3];
            try
            {
                GameObjectUtility.SetStaticEditorFlags(
                    root,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic);

                for (var index = 0; index < meshes.Length; index++)
                {
                    var child = new GameObject("LOD" + index);
                    child.transform.SetParent(root.transform, false);
                    var filter = child.AddComponent<MeshFilter>();
                    filter.sharedMesh = meshes[index];
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderers[index] = renderer;
                }

                var lodGroup = root.AddComponent<LODGroup>();
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.animateCrossFading = false;
                lodGroup.SetLODs(new[]
                {
                    new LOD(0.55f, new[] { renderers[0] }),
                    new LOD(0.22f, new[] { renderers[1] }),
                    new LOD(0.01f, new[] { renderers[2] })
                });
                lodGroup.RecalculateBounds();

                var collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = meshes[2];
                collider.convex = false;

                var bounds = meshes[0].bounds;
                CreateAnchor(root.transform, "Snap_Left", new Vector3(
                    bounds.min.x, bounds.min.y, bounds.center.z));
                CreateAnchor(root.transform, "Snap_Right", new Vector3(
                    bounds.max.x, bounds.min.y, bounds.center.z));
                CreateAnchor(root.transform, "Snap_Top", new Vector3(
                    bounds.center.x, bounds.max.y, bounds.center.z));

                var prefabPath = Prefabs + "/PF_" + assetName + ".prefab";
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
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

        private static void BuildPreviewScene(IReadOnlyList<GameObject> prefabs)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                Application.isBatchMode
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Reference Ground";
                ground.transform.localScale = new Vector3(8f, 1f, 5f);
                var groundMaterial = new Material(
                    Shader.Find("Universal Render Pipeline/Lit"));
                groundMaterial.color = new Color(0.78f, 0.84f, 0.89f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

                var lightObject = new GameObject("Key Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2.6f;
                light.shadows = LightShadows.Soft;
                lightObject.transform.rotation = Quaternion.Euler(42f, -34f, 0f);

                for (var index = 0; index < prefabs.Count; index++)
                {
                    var instance = PrefabUtility.InstantiatePrefab(
                        prefabs[index], scene) as GameObject;
                    if (instance == null)
                    {
                        continue;
                    }

                    var column = index % 8;
                    var row = index / 8;
                    instance.transform.position = new Vector3(
                        (column - 3.5f) * 8.5f,
                        0f,
                        (row - 1.5f) * 10f);
                    instance.transform.rotation = Quaternion.Euler(
                        0f,
                        ((index * 17) % 13) - 6f,
                        0f);
                }

                var cameraObject = new GameObject("Catalog Camera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.31f, 0.70f, 0.92f);
                cameraObject.transform.position = new Vector3(35f, 36f, -48f);
                cameraObject.transform.LookAt(new Vector3(0f, 2.5f, 0f));

                EditorSceneManager.SaveScene(scene, PreviewScenePath);
            }
            finally
            {
                if (!Application.isBatchMode && previous.IsValid())
                {
                    SceneManager.SetActiveScene(previous);
                }

                if (!Application.isBatchMode)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static string Validate(IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs.Count != AssetNames.Length)
            {
                throw new InvalidOperationException(
                    $"Expected {AssetNames.Length} prefabs, found {prefabs.Count}.");
            }

            var totalTriangles = 0L;
            foreach (var prefab in prefabs)
            {
                if (prefab == null)
                {
                    throw new InvalidOperationException("A prefab failed to save.");
                }

                var group = prefab.GetComponent<LODGroup>();
                var collider = prefab.GetComponent<MeshCollider>();
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                if (group == null || group.GetLODs().Length != 3 ||
                    collider == null || collider.sharedMesh == null ||
                    filters.Length != 3 ||
                    prefab.transform.Find("Snap_Left") == null ||
                    prefab.transform.Find("Snap_Right") == null ||
                    prefab.transform.Find("Snap_Top") == null)
                {
                    throw new InvalidOperationException(
                        $"Prefab contract failed for {prefab.name}.");
                }

                var triangleCounts = filters
                    .OrderBy(filter => filter.name)
                    .Select(filter => filter.sharedMesh.triangles.Length / 3)
                    .ToArray();
                if (!(triangleCounts[0] > triangleCounts[1] &&
                      triangleCounts[1] >= triangleCounts[2]))
                {
                    throw new InvalidOperationException(
                        $"LOD triangle order failed for {prefab.name}: " +
                        string.Join(",", triangleCounts));
                }

                totalTriangles += triangleCounts[0];
            }

            return $"prefabs={prefabs.Count} lods=3 colliders={prefabs.Count} " +
                   $"lod0_triangles={totalTriangles}";
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
    }
}

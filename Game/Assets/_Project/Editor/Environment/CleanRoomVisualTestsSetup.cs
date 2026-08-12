using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Imports clean-room visual tests without loading or copying study assets.
    /// The current working scene is never saved or replaced: the test scene is
    /// created additively, saved, then closed.
    /// </summary>
    public static class CleanRoomVisualTestsSetup
    {
        private const string Root =
            "Assets/_Project/Art/Environment/CleanRoomVisualTests";
        private const string RockRoot = Root + "/Rocks";
        private const string GrassRoot = Root + "/Grass";
        private const string CloudRoot = Root + "/Clouds";
        private const string Materials = Root + "/Materials";
        private const string Prefabs = Root + "/Prefabs";
        private const string Preview = Root + "/Preview";
        private const string ScenePath = Preview + "/SCN_CleanRoomVisualTests.unity";
        private const string SessionKey = "CML.CleanRoomVisualTestsSetup.v28";
        private const int GroundLayer = 27;
        private const int CloudLayer = 28;
        private const int GrassLayer = 29;
        private const int RockLayer = 30;

        private static readonly string[] RockNames =
        {
            "CR_CliffMass_A", "CR_CliffMass_B", "CR_CliffMass_C", "CR_CliffMass_D",
            "CR_CliffShelf_A", "CR_CliffShelf_B", "CR_CliffShelf_C", "CR_CliffShelf_D"
        };

        private static readonly int[] RockVertices =
        {
            274, 333, 235, 302, 240, 273, 189, 200
        };

        private static readonly int[] RockTriangles =
        {
            428, 486, 360, 464, 348, 386, 310, 282
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
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    File.Exists(Path.GetFullPath(RockModelPath(RockNames[0]))) &&
                    File.Exists(Path.GetFullPath(GrassModelPath)) &&
                    File.Exists(Path.GetFullPath(CloudModelPath)))
                {
                    Run();
                }
            };
        }

        [MenuItem("CML/Art/Clean Room Visual Tests/Rebuild")]
        public static void Run()
        {
            EnsureFolder(Materials);
            EnsureFolder(Prefabs);
            EnsureFolder(Preview);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var rockMaterial = BuildRockMaterial();
            var grassMaterial = BuildGrassMaterial();
            var cloudMaterial = BuildCloudMaterial();
            var groundMaterial = BuildGroundMaterial();

            var rockPrefabs = new List<GameObject>();
            for (var index = 0; index < RockNames.Length; index++)
            {
                var path = RockModelPath(RockNames[index]);
                ConfigureModel(path, false);
                rockPrefabs.Add(BuildLodPrefab(
                    RockNames[index], path, rockMaterial, 3, true));
            }

            ConfigureModel(GrassModelPath, true);
            var grassPrefab = BuildLodPrefab(
                "CR_GrassClump_A", GrassModelPath, grassMaterial, 2, false);
            ConfigureModel(CloudModelPath, false);
            var cloudPrefab = BuildCloudPrefab(cloudMaterial);

            Validate(rockPrefabs, grassPrefab, cloudPrefab);
            BuildIsolatedScene(
                rockPrefabs, grassPrefab, cloudPrefab, groundMaterial);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RenderValidation();
            Debug.Log(
                "CLEAN_ROOM_VISUAL_TESTS status=PASS rocks=8 " +
                "grass=102v/68t/17ribbons cloud=4064v/4168t/288fragments");
        }

        [MenuItem("CML/Art/Clean Room Visual Tests/Render Validation")]
        public static void RenderValidation()
        {
            var previousActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var cameras = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                    .OrderBy(item => item.name)
                    .ToArray();
                if (cameras.Length == 0)
                {
                    throw new InvalidOperationException("Test scene cameras are missing.");
                }
                foreach (var camera in cameras)
                {
                    AuditCameraContents(camera);
                    var output = Path.GetFullPath(Path.Combine(
                        Application.dataPath,
                        "../../Artifacts/CleanRoomVisualTests/Unity_" + camera.name + ".png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(output) ?? string.Empty);
                    RenderCamera(camera, output);
                    Debug.Log("CLEAN_ROOM_VISUAL_TESTS_RENDER status=PASS output=" + output);
                    if (camera.name == "Validation_Grass")
                    {
                        RenderGrassWindPhases(camera, output);
                    }
                }
            }
            finally
            {
                if (previousActive.IsValid())
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string GrassModelPath =>
            GrassRoot + "/Models/CR_GrassClump_A.fbx";

        private static string CloudModelPath =>
            CloudRoot + "/Models/CR_CloudComplex_A.fbx";

        private static string RockModelPath(string name) =>
            RockRoot + "/Models/" + name + ".fbx";

        private static Material BuildRockMaterial()
        {
            var material = GetOrCreateMaterial(
                Materials + "/M_CR_Cliff.mat", "CML/Clean Room/Measured Cliff");
            material.SetColor("_RockDark", new Color(0.50f, 0.22f, 0.09f));
            material.SetColor("_RockBase", new Color(0.72f, 0.34f, 0.14f));
            material.SetColor("_RockLight", new Color(0.88f, 0.54f, 0.30f));
            material.SetFloat("_MacroScale", 0.026f);
            material.SetFloat("_StrataScale", 0.082f);
            material.SetFloat("_StrataStrength", 0.14f);
            material.SetColor("_GrassDark", new Color(0.13f, 0.24f, 0.045f));
            material.SetColor("_GrassBase", new Color(0.30f, 0.46f, 0.075f));
            material.SetColor("_GrassLight", new Color(0.50f, 0.65f, 0.12f));
            material.SetFloat("_GrassSlopeStart", 0.52f);
            material.SetFloat("_GrassSlopeEnd", 0.84f);
            material.SetFloat("_GrassBreakup", 0.12f);
            material.SetFloat("_AmbientStrength", 0.92f);
            material.SetFloat("_DirectStrength", 0.95f);
            material.SetFloat("_ShadowFloor", 0.44f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildGrassMaterial()
        {
            var material = GetOrCreateMaterial(
                Materials + "/M_CR_GrassWind.mat",
                "CML/Clean Room/Measured Grass Wind");
            material.SetColor("_BottomColor", new Color(0.12f, 0.25f, 0.035f));
            material.SetColor("_TopColor", new Color(0.48f, 0.68f, 0.10f));
            material.SetColor("_DryColor", new Color(0.62f, 0.58f, 0.12f));
            material.SetVector("_WindDirection", new Vector4(0f, 0f, -1f, 0f));
            material.SetFloat("_WindIntensity", 5f);
            material.SetFloat("_WindWeight", 0.25f);
            material.SetFloat("_WindSpeed", 1f);
            material.SetFloat("_UsePreviewTime", 0f);
            material.SetFloat("_PreviewTime", 0f);
            material.SetFloat("_AlphaCutoff", 0.32f);
            material.enableInstancing = true;
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildCloudMaterial()
        {
            var material = GetOrCreateMaterial(
                Materials + "/M_CR_Cloud.mat",
                "CML/Clean Room/Measured Geometric Cloud");
            material.SetColor("_BottomColor", new Color(0.52f, 0.68f, 0.74f));
            material.SetColor("_LayerColor", new Color(0.76f, 0.86f, 0.87f));
            material.SetColor("_TopColor", new Color(1.0f, 0.97f, 0.88f));
            material.SetFloat("_EdgeNoiseScale", 0.0025f);
            material.SetFloat("_EdgeNoise", 0.18f);
            material.SetFloat("_Cutoff", 0.035f);
            material.SetFloat("_LightResponse", 0.34f);
            material.renderQueue = -1;
            material.enableInstancing = true;
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildGroundMaterial()
        {
            var material = GetOrCreateMaterial(
                Materials + "/M_CR_Ground.mat", "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", new Color(0.20f, 0.35f, 0.055f));
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Shader '{shaderName}' is unavailable or failed to compile.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            return material;
        }

        private static void ConfigureModel(string path, bool preserveVertexColors)
        {
            if (!File.Exists(Path.GetFullPath(path)))
            {
                throw new FileNotFoundException("Missing clean-room FBX", path);
            }

            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                throw new InvalidOperationException("ModelImporter unavailable: " + path);
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
            importer.importTangents = ModelImporterTangents.None;
            importer.generateSecondaryUV = false;
            importer.optimizeMeshPolygons = false;
            importer.optimizeMeshVertices = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static GameObject BuildLodPrefab(
            string name,
            string modelPath,
            Material material,
            int lodCount,
            bool collider)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new InvalidOperationException("Imported model unavailable: " + modelPath);
            }

            var sourceFilters = model.GetComponentsInChildren<MeshFilter>(true);
            var root = new GameObject("PF_" + name);
            try
            {
                var renderers = new Renderer[lodCount];
                var meshes = new Mesh[lodCount];
                var transforms = new Transform[lodCount];
                for (var lod = 0; lod < lodCount; lod++)
                {
                    var suffix = "_LOD" + lod;
                    var source = sourceFilters.FirstOrDefault(
                        filter => filter.name.EndsWith(suffix, StringComparison.Ordinal));
                    if (source == null || source.sharedMesh == null)
                    {
                        throw new InvalidOperationException(name + " missing " + suffix);
                    }

                    meshes[lod] = source.sharedMesh;
                    transforms[lod] = source.transform;
                    var child = new GameObject("LOD" + lod);
                    child.transform.SetParent(root.transform, false);
                    CopyImportedTransform(model.transform, source.transform, child.transform);
                    child.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderers[lod] = renderer;
                }

                var group = root.AddComponent<LODGroup>();
                var lods = new LOD[lodCount];
                for (var lod = 0; lod < lodCount; lod++)
                {
                    var threshold = lodCount == 3
                        ? new[] { 0.46f, 0.18f, 0.012f }[lod]
                        : new[] { 0.34f, 0.012f }[lod];
                    lods[lod] = new LOD(threshold, new[] { renderers[lod] })
                    {
                        fadeTransitionWidth = 0.06f
                    };
                }

                group.fadeMode = LODFadeMode.CrossFade;
                group.SetLODs(lods);
                group.RecalculateBounds();

                if (collider)
                {
                    var collision = new GameObject("Collision");
                    collision.transform.SetParent(root.transform, false);
                    CopyImportedTransform(
                        model.transform, transforms[lodCount - 1], collision.transform);
                    collision.AddComponent<MeshCollider>().sharedMesh = meshes[lodCount - 1];
                    CreateAnchors(root.transform, renderers[0].bounds);
                }

                return PrefabUtility.SaveAsPrefabAsset(
                    root, Prefabs + "/PF_" + name + ".prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildCloudPrefab(Material material)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CloudModelPath);
            var source = model?.GetComponentInChildren<MeshFilter>(true);
            if (source == null || source.sharedMesh == null)
            {
                throw new InvalidOperationException("Cloud LOD0 mesh is unavailable.");
            }

            var root = new GameObject("PF_CR_CloudComplex_A");
            try
            {
                var child = new GameObject("CloudGeometry");
                child.transform.SetParent(root.transform, false);
                CopyImportedTransform(model.transform, source.transform, child.transform);
                // The aggregate study data was measured after Blender's glTF
                // Y-up to Z-up conversion.  The generated FBX arrives in Unity
                // with that study Z axis still stored as mesh depth, so align
                // it back to Unity Y before evaluating the source front view.
                child.transform.localRotation =
                    Quaternion.Euler(90f, 0f, 0f) * child.transform.localRotation;
                child.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                root.AddComponent<CleanRoomCloudMotion>();
                return PrefabUtility.SaveAsPrefabAsset(
                    root, Prefabs + "/PF_CR_CloudComplex_A.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildIsolatedScene(
            IReadOnlyList<GameObject> rocks,
            GameObject grass,
            GameObject cloud,
            Material groundMaterial)
        {
            var previousActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Test Ground";
                SetLayerRecursively(ground, GroundLayer);
                ground.transform.localScale = new Vector3(42f, 1f, 32f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

                var positions = new[]
                {
                    new Vector3(-86, 0, 32), new Vector3(-29, 0, 32),
                    new Vector3(31, 0, 32), new Vector3(88, 0, 32),
                    new Vector3(-54, 0, -30), new Vector3(-18, 0, -30),
                    new Vector3(18, 0, -30), new Vector3(54, 0, -30)
                };
                for (var index = 0; index < rocks.Count; index++)
                {
                    var instance = PrefabUtility.InstantiatePrefab(rocks[index], scene) as GameObject;
                    if (instance != null)
                    {
                        ForceLod0(instance);
                        SetLayerRecursively(instance, RockLayer);
                        instance.transform.position = positions[index];
                    }
                }

                var rng = new System.Random(7142);
                for (var index = 0; index < 90; index++)
                {
                    var instance = PrefabUtility.InstantiatePrefab(grass, scene) as GameObject;
                    if (instance == null)
                    {
                        continue;
                    }

                    ForceLod0(instance);
                    SetLayerRecursively(instance, GrassLayer);
                    instance.transform.position = new Vector3(
                        Mathf.Lerp(-18f, 18f, (float)rng.NextDouble()),
                        0.02f,
                        Mathf.Lerp(-10f, 14f, (float)rng.NextDouble()));
                    instance.transform.rotation = Quaternion.Euler(
                        0f, (float)rng.NextDouble() * 360f, 0f);
                    var scale = Mathf.Lerp(0.85f, 1.55f, (float)rng.NextDouble());
                    instance.transform.localScale = Vector3.one * scale;
                }

                var cloudInstance = PrefabUtility.InstantiatePrefab(cloud, scene) as GameObject;
                if (cloudInstance != null)
                {
                    SetLayerRecursively(cloudInstance, CloudLayer);
                    cloudInstance.transform.position = new Vector3(0f, 190f, 250f);
                    cloudInstance.transform.localScale = Vector3.one * 0.10f;
                    cloudInstance.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                }

                var sunObject = new GameObject("Measured Sun");
                var sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.55f;
                sun.color = new Color(1f, 0.89f, 0.75f);
                sun.cullingMask =
                    (1 << GroundLayer) | (1 << CloudLayer) |
                    (1 << GrassLayer) | (1 << RockLayer);
                sun.shadows = LightShadows.Soft;
                sunObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.43f, 0.68f, 0.78f);
                RenderSettings.ambientEquatorColor = new Color(0.31f, 0.43f, 0.42f);
                RenderSettings.ambientGroundColor = new Color(0.16f, 0.18f, 0.11f);
                RenderSettings.ambientIntensity = 1f;

                CreateValidationCamera(
                    "Validation_Rocks",
                    (1 << GroundLayer) | (1 << RockLayer),
                    new Vector3(132f, 80f, -218f),
                    new Vector3(0f, 24f, 14f),
                    46f);
                CreateValidationCamera(
                    "Validation_Grass",
                    (1 << GroundLayer) | (1 << GrassLayer),
                    new Vector3(22f, 7.5f, -26f),
                    new Vector3(0f, 0.6f, 1f),
                    42f);
                CreateValidationCamera(
                    "Validation_Clouds",
                    1 << CloudLayer,
                    new Vector3(0f, 190f, -80f),
                    new Vector3(0f, 190f, 250f),
                    34f);
                CreateValidationCamera(
                    "Validation_Clouds_Side",
                    1 << CloudLayer,
                    new Vector3(330f, 190f, 250f),
                    new Vector3(0f, 190f, 250f),
                    34f);
                CreateValidationCamera(
                    "Validation_Clouds_Top",
                    1 << CloudLayer,
                    new Vector3(0f, 520f, 250f),
                    new Vector3(0f, 190f, 250f),
                    34f);

                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previousActive.IsValid())
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Validate(
            IReadOnlyList<GameObject> rocks,
            GameObject grass,
            GameObject cloud)
        {
            for (var index = 0; index < rocks.Count; index++)
            {
                var mesh = rocks[index].transform.Find("LOD0")
                    ?.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount != RockVertices[index] ||
                    mesh.triangles.Length / 3 != RockTriangles[index])
                {
                    throw new InvalidOperationException(
                        $"Rock contract failed {RockNames[index]}: " +
                        $"{mesh?.vertexCount}v/{mesh?.triangles.Length / 3}t");
                }
            }

            var grassMesh = grass.transform.Find("LOD0")
                ?.GetComponent<MeshFilter>()?.sharedMesh;
            var cloudMesh = cloud.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
            if (grassMesh == null || grassMesh.vertexCount != 102 ||
                grassMesh.triangles.Length / 3 != 68 ||
                !grassMesh.HasVertexAttribute(VertexAttribute.Color))
            {
                throw new InvalidOperationException(
                    "Grass measured contract failed: " +
                    $"{grassMesh?.vertexCount}v/" +
                    $"{grassMesh?.triangles.Length / 3}t/" +
                    $"color={grassMesh?.HasVertexAttribute(VertexAttribute.Color)}");
            }

            if (cloudMesh == null || cloudMesh.vertexCount != 4064 ||
                cloudMesh.triangles.Length / 3 != 4168)
            {
                throw new InvalidOperationException(
                    "Cloud measured contract failed: " +
                    $"{cloudMesh?.vertexCount}v/" +
                    $"{cloudMesh?.triangles.Length / 3}t");
            }
        }

        private static void CopyImportedTransform(
            Transform modelRoot, Transform source, Transform destination)
        {
            destination.localPosition = modelRoot.InverseTransformPoint(source.position);
            destination.localRotation = Quaternion.Inverse(modelRoot.rotation) * source.rotation;
            var rootScale = modelRoot.lossyScale;
            var sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                sourceScale.x / Mathf.Max(Mathf.Abs(rootScale.x), 0.00001f),
                sourceScale.y / Mathf.Max(Mathf.Abs(rootScale.y), 0.00001f),
                sourceScale.z / Mathf.Max(Mathf.Abs(rootScale.z), 0.00001f));
        }

        private static void CreateAnchors(Transform parent, Bounds bounds)
        {
            CreateAnchor(parent, "Snap_Base", new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            CreateAnchor(parent, "Snap_Top", new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            CreateAnchor(parent, "Snap_West", new Vector3(bounds.min.x, bounds.center.y, bounds.center.z));
            CreateAnchor(parent, "Snap_East", new Vector3(bounds.max.x, bounds.center.y, bounds.center.z));
            CreateAnchor(parent, "Snap_South", new Vector3(bounds.center.x, bounds.center.y, bounds.min.z));
            CreateAnchor(parent, "Snap_North", new Vector3(bounds.center.x, bounds.center.y, bounds.max.z));
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void ForceLod0(GameObject root)
        {
            var group = root.GetComponent<LODGroup>();
            if (group != null)
            {
                group.enabled = false;
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.enabled = renderer.gameObject.name == "LOD0";
            }
        }

        private static Camera CreateValidationCamera(
            string name,
            int cullingMask,
            Vector3 position,
            Vector3 target,
            float fieldOfView)
        {
            var cameraObject = new GameObject(name);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.63f, 0.79f);
            camera.cullingMask = cullingMask;
            camera.fieldOfView = fieldOfView;
            camera.transform.position = position;
            camera.transform.LookAt(target);
            return camera;
        }

        private static void CreateAnchor(Transform parent, string name, Vector3 position)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = position;
        }

        private static void RenderCamera(Camera camera, string outputPath)
        {
            var target = new RenderTexture(1800, 1000, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var texture = new Texture2D(1800, 1000, TextureFormat.RGB24, false, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1800, 1000), 0, 0);
                texture.Apply(false, false);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void AuditCameraContents(Camera camera)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var renderers = camera.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer =>
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                .OrderBy(renderer => renderer.name)
                .ToArray();

            foreach (var renderer in renderers)
            {
                var material = renderer.sharedMaterial;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                var bounds = renderer.bounds;
                Debug.Log(
                    "CLEAN_ROOM_CAMERA_AUDIT " +
                    $"camera={camera.name} renderer={renderer.name} " +
                    $"active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} " +
                    $"layer={renderer.gameObject.layer} inFrustum={GeometryUtility.TestPlanesAABB(planes, bounds)} " +
                    $"boundsCenter={bounds.center:F3} boundsSize={bounds.size:F3} " +
                    $"lossyScale={renderer.transform.lossyScale:F4} " +
                    $"mesh={(mesh == null ? "none" : mesh.name)} " +
                    $"vertices={(mesh == null ? 0 : mesh.vertexCount)} " +
                    $"triangles={(mesh == null ? 0 : mesh.triangles.Length / 3)} " +
                    $"shader={(material == null || material.shader == null ? "none" : material.shader.name)} " +
                    $"passes={(material == null ? 0 : material.passCount)} " +
                    $"queue={(material == null ? 0 : material.renderQueue)}");
            }
        }

        private static void RenderGrassWindPhases(Camera camera, string baseOutputPath)
        {
            var materials = camera.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Select(renderer => renderer.sharedMaterial)
                .Where(material => material != null && material.shader != null &&
                    material.shader.name == "CML/Clean Room/Measured Grass Wind")
                .Distinct()
                .ToArray();
            try
            {
                foreach (var material in materials)
                {
                    material.SetFloat("_UsePreviewTime", 1f);
                    material.SetFloat("_PreviewTime", 0.25f);
                }
                var first = Path.Combine(
                    Path.GetDirectoryName(baseOutputPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(baseOutputPath) + "_WindA.png");
                RenderCamera(camera, first);

                foreach (var material in materials)
                {
                    material.SetFloat("_PreviewTime", 2.15f);
                }
                var second = Path.Combine(
                    Path.GetDirectoryName(baseOutputPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(baseOutputPath) + "_WindB.png");
                RenderCamera(camera, second);
                Debug.Log(
                    "CLEAN_ROOM_GRASS_WIND_VALIDATION status=PASS " +
                    $"phaseA={first} phaseB={second}");
            }
            finally
            {
                foreach (var material in materials)
                {
                    material.SetFloat("_UsePreviewTime", 0f);
                    material.SetFloat("_PreviewTime", 0f);
                }
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
    }
}

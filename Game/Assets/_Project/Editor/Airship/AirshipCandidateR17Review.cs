using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Isolated import and visual-review pipeline for the R17 airship.
    /// It never writes to the production Airship folder or any gameplay scene.
    /// </summary>
    public static class AirshipCandidateR17Review
    {
        private const string Root = "Assets/_Project/Art/Vehicles/AirshipCandidate/R17";
        private const string ModelPath = Root + "/Models/AIR_Airship_R17.fbx";
        private const string TextureRoot = Root + "/Textures";
        private const string MaterialRoot = Root + "/Materials";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string PrefabPath = PrefabRoot + "/PF_Airship_R17_Review.prefab";

        private const string OpaquePath = MaterialRoot + "/M_Airship_R17_Opaque.mat";
        private const string GlassPath = MaterialRoot + "/M_Airship_R17_Glass.mat";
        private const string WoodPath = MaterialRoot + "/M_Airship_R17_Wood.mat";
        private const string EmissionPath = MaterialRoot + "/M_Airship_R17_Emission.mat";

        private const int Width = 1600;
        private const int Height = 1200;

        private static readonly string[] RequiredTransforms =
        {
            "GEO_Static",
            "ANM_Moving",
            "ANM_BoardingRamp",
            "ANM_AccessDoor",
            "ANM_PropellerRotor",
            "ANM_NacelleRotor_Port",
            "ANM_NacelleRotor_Starboard",
            "REF_PilotCamera",
            "REF_PilotControls",
            "REF_PilotExit",
            "REF_RampTip"
        };

        [MenuItem("CML/Art/Review Airship Candidate R17")]
        public static void Run()
        {
            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);
            ConfigureModelImporter();
            ImportTextures();

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP/Lit shader unavailable.");
            }

            var materials = BuildMaterials(shader);
            var prefab = BuildPrefab(materials);
            ValidatePrefab(prefab, materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RenderReviewSet(prefab);
            Debug.Log($"AIRSHIP_R17_REVIEW status=PASS prefab={PrefabPath}");
        }

        private static void ConfigureModelImporter()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Missing model importer: {ModelPath}");
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.preserveHierarchy = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }

        private static void ImportTextures()
        {
            ConfigureTexture(TextureRoot + "/T_Airship_BaseColor.png", true);
            ConfigureTexture(TextureRoot + "/T_Airship_Mask.png", false);
            ConfigureTexture(TextureRoot + "/T_Airship_Wood_BaseColor.png", true);
            ConfigureTexture(TextureRoot + "/T_Airship_Wood_Mask.png", false);
            ConfigureTexture(TextureRoot + "/T_Airship_Emission.png", true);
            ConfigureTexture(TextureRoot + "/T_Airship_Detail.png", true);
        }

        private static void ConfigureTexture(string path, bool sRgb)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Missing texture importer: {path}");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRgb;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, Material> BuildMaterials(Shader shader)
        {
            var opaque = Upsert(OpaquePath, shader);
            ConfigureOpaque(
                opaque,
                Require<Texture2D>(TextureRoot + "/T_Airship_BaseColor.png"),
                Require<Texture2D>(TextureRoot + "/T_Airship_Mask.png"),
                Require<Texture2D>(TextureRoot + "/T_Airship_Detail.png"),
                0.34f);

            var wood = Upsert(WoodPath, shader);
            ConfigureOpaque(
                wood,
                Require<Texture2D>(TextureRoot + "/T_Airship_Wood_BaseColor.png"),
                Require<Texture2D>(TextureRoot + "/T_Airship_Wood_Mask.png"),
                Require<Texture2D>(TextureRoot + "/T_Airship_Detail.png"),
                0.42f);

            var glass = Upsert(GlassPath, shader);
            ConfigureGlass(glass);

            var emission = Upsert(EmissionPath, shader);
            ConfigureOpaque(
                emission,
                Require<Texture2D>(TextureRoot + "/T_Airship_Emission.png"),
                null,
                null,
                0.24f);
            emission.EnableKeyword("_EMISSION");
            emission.SetColor("_EmissionColor", new Color(1.4f, 1.15f, 0.48f, 1f));
            emission.SetTexture(
                "_EmissionMap",
                Require<Texture2D>(TextureRoot + "/T_Airship_Emission.png"));
            emission.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            return new Dictionary<string, Material>
            {
                ["opaque"] = opaque,
                ["wood"] = wood,
                ["glass"] = glass,
                ["emission"] = emission
            };
        }

        private static void ConfigureOpaque(
            Material material,
            Texture2D baseMap,
            Texture2D maskMap,
            Texture2D detailMap,
            float smoothness)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", baseMap);
            material.SetTexture("_MetallicGlossMap", maskMap);
            material.SetTexture("_DetailAlbedoMap", detailMap);
            material.SetFloat("_DetailAlbedoMapScale", detailMap == null ? 0f : 1f);
            material.SetFloat("_UVSec", 1f);
            if (maskMap == null)
            {
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            if (detailMap == null)
            {
                material.DisableKeyword("_DETAIL_MULX2");
            }
            else
            {
                material.EnableKeyword("_DETAIL_MULX2");
            }
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Geometry;
        }

        private static void ConfigureGlass(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.72f);
            material.SetColor("_BaseColor", new Color(0.035f, 0.24f, 0.52f, 0.54f));
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static GameObject BuildPrefab(IReadOnlyDictionary<string, Material> materials)
        {
            var source = Require<GameObject>(ModelPath);
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Could not instantiate the R17 model.");
            }

            try
            {
                instance.name = "PF_Airship_R17_Review";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.StartsWith(
                            "COLMESH_PLAYER_",
                            StringComparison.Ordinal))
                    {
                        UnityEngine.Object.DestroyImmediate(renderer);
                        continue;
                    }

                    var sourceMaterials = renderer.sharedMaterials;
                    var assigned = new Material[Math.Max(1, sourceMaterials.Length)];
                    for (var index = 0; index < assigned.Length; index++)
                    {
                        var sourceMaterial =
                            sourceMaterials.Length > index ? sourceMaterials[index] : null;
                        assigned[index] = ResolveMaterial(renderer.name, sourceMaterial, materials);
                    }

                    renderer.sharedMaterials = assigned;
                    var glass = Array.IndexOf(assigned, materials["glass"]) >= 0;
                    renderer.shadowCastingMode =
                        glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = !glass;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException("Could not save R17 review prefab.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Material ResolveMaterial(
            string rendererName,
            Material source,
            IReadOnlyDictionary<string, Material> materials)
        {
            var combined = (rendererName + " " + (source == null ? string.Empty : source.name))
                .ToLowerInvariant();
            if (combined.Contains("panoramiccanopy") || combined.Contains("glass"))
            {
                return materials["glass"];
            }

            if (combined.Contains("emission") || combined.Contains("noselight"))
            {
                return materials["emission"];
            }

            if (combined.Contains("wood"))
            {
                return materials["wood"];
            }

            return materials["opaque"];
        }

        private static void ValidatePrefab(
            GameObject prefab,
            IReadOnlyDictionary<string, Material> materials)
        {
            foreach (var required in RequiredTransforms)
            {
                if (FindDeep(prefab.transform, required) == null)
                {
                    throw new InvalidOperationException($"R17 missing transform: {required}");
                }
            }

            if (prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException("R17 root scale must be one.");
            }

            var glassCount = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == materials["glass"])
                    {
                        glassCount++;
                        if (renderer.name.IndexOf(
                                "PanoramicCanopy",
                                StringComparison.Ordinal) < 0)
                        {
                            throw new InvalidOperationException(
                                $"Glass assigned outside canopy: {renderer.name}");
                        }
                    }
                }

                if (renderer.transform.lossyScale.x < 0f
                    || renderer.transform.lossyScale.y < 0f
                    || renderer.transform.lossyScale.z < 0f)
                {
                    throw new InvalidOperationException(
                        $"Negative scale is forbidden: {renderer.name}");
                }
            }

            if (glassCount < 24)
            {
                throw new InvalidOperationException(
                    $"Expected the canopy to use individually sortable panes, found {glassCount}.");
            }

        }

        private static void RenderReviewSet(GameObject prefab)
        {
            var outputRoot = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    "Artifacts",
                    "Renders",
                    "AirshipCandidate",
                    "R17"));
            Directory.CreateDirectory(outputRoot);

            var previewScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            SceneManager.SetActiveScene(previewScene);
            GameObject instance = null;
            GameObject cameraObject = null;
            Material groundMaterial = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException("Could not instantiate R17 review prefab.");
                }

                SceneManager.MoveGameObjectToScene(instance, previewScene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "R17_ReviewGround";
                ground.transform.position = new Vector3(0f, -0.08f, 0f);
                ground.transform.localScale = new Vector3(2.2f, 1f, 2.2f);
                SceneManager.MoveGameObjectToScene(ground, previewScene);
                groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                groundMaterial.SetColor("_BaseColor", new Color(0.36f, 0.43f, 0.43f, 1f));
                groundMaterial.SetFloat("_Smoothness", 0.16f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

                CreateLight(
                    previewScene,
                    "R17_Key",
                    new Vector3(6f, 9f, 8f),
                    new Vector3(28f, -145f, 0f),
                    1.65f,
                    new Color(1f, 0.88f, 0.72f));
                CreateLight(
                    previewScene,
                    "R17_Fill",
                    new Vector3(-7f, 5f, 5f),
                    new Vector3(38f, 35f, 0f),
                    0.65f,
                    new Color(0.58f, 0.76f, 1f));

                cameraObject = new GameObject("R17_ReviewCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.50f, 0.68f, 0.76f, 1f);
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 200f;
                camera.allowHDR = true;

                var door = FindDeep(instance.transform, "ANM_AccessDoor");
                var closedDoorRotation = door.localRotation;
                // The FBX null keeps Blender's source-axis basis.  Rotate about
                // the ship's actual vertical expressed in that local basis,
                // otherwise a nominal local-Y rotation tips the door into a
                // horizontal shelf instead of swinging it out of the jamb.
                var localDoorHingeAxis = door.InverseTransformDirection(
                    instance.transform.up).normalized;

                Capture(camera, outputRoot, "01_exterior_three_quarter_starboard.png",
                    new Vector3(-7.8f, 4.5f, 7.8f), new Vector3(0f, 1.5f, -0.25f), 38f);
                Capture(camera, outputRoot, "02_exterior_three_quarter_port.png",
                    new Vector3(7.8f, 4.5f, 7.8f), new Vector3(0f, 1.5f, -0.25f), 38f);
                Capture(camera, outputRoot, "03_starboard_door_closed.png",
                    new Vector3(-8.7f, 2.0f, -0.1f), new Vector3(0f, 1.45f, -0.1f), 32f);

                door.localRotation = closedDoorRotation
                    * Quaternion.AngleAxis(100f, localDoorHingeAxis);
                Capture(camera, outputRoot, "04_starboard_door_open.png",
                    new Vector3(-8.7f, 2.0f, -0.1f), new Vector3(0f, 1.45f, -0.1f), 32f);
                Capture(camera, outputRoot, "05_interior_from_door.png",
                    new Vector3(-0.55f, 1.75f, 0.55f), new Vector3(0f, 1.50f, 3.50f), 65f);

                door.localRotation = closedDoorRotation;
                Capture(camera, outputRoot, "06_interior_toward_door_bulkhead.png",
                    new Vector3(0.76f, 1.76f, 3.05f), new Vector3(-0.65f, 1.50f, -0.40f), 66f);

                var pilot = FindDeep(instance.transform, "REF_PilotCamera");
                camera.transform.position = pilot.position;
                camera.transform.rotation = instance.transform.rotation;
                camera.fieldOfView = 68f;
                CaptureCurrent(camera, Path.Combine(outputRoot, "07_pilot_view.png"));

                Capture(camera, outputRoot, "08_front.png",
                    new Vector3(0f, 2.0f, 10.3f), new Vector3(0f, 1.45f, 0.5f), 32f);
                Capture(camera, outputRoot, "09_rear.png",
                    new Vector3(0f, 2.0f, -10.3f), new Vector3(0f, 1.50f, -0.6f), 32f);
                Capture(camera, outputRoot, "10_top.png",
                    new Vector3(0f, 11.3f, 0f), new Vector3(0f, 1.35f, -0.3f), 35f);
                Capture(camera, outputRoot, "11_fan_detail.png",
                    new Vector3(4.7f, 3.1f, 2.6f), new Vector3(1.72f, 1.48f, -2.70f), 31f);
                Capture(camera, outputRoot, "12_dorsal_fins_detail.png",
                    new Vector3(7.4f, 5.4f, -7.4f), new Vector3(0f, 2.75f, -2.75f), 35f);
            }
            finally
            {
                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }

                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }

                if (groundMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(groundMaterial);
                }

                // The review scene is intentionally unsaved and exists only
                // for this batch invocation. Unity closes it on process exit.
            }
        }

        private static void Capture(
            Camera camera,
            string outputRoot,
            string filename,
            Vector3 position,
            Vector3 target,
            float fieldOfView)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target, Vector3.up);
            camera.fieldOfView = fieldOfView;
            CaptureCurrent(camera, Path.Combine(outputRoot, filename));
        }

        private static void CaptureCurrent(Camera camera, string path)
        {
            var target = new RenderTexture(
                Width,
                Height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var capture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                capture.Apply(false, false);
                File.WriteAllBytes(path, capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(capture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void CreateLight(
            Scene scene,
            string name,
            Vector3 position,
            Vector3 rotation,
            float intensity,
            Color color)
        {
            var lightObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.position = position;
            lightObject.transform.eulerAngles = rotation;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.Soft;
        }

        private static Material Upsert(string path, Shader shader)
        {
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

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException($"Missing asset: {path}");
            }

            return asset;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDeep(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static void EnsureFolder(string folder)
        {
            var segments = folder.Split('/');
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

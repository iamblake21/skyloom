using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Editor.Art
{
    /// <summary>
    /// Renders the CloudTall prefabs offscreen so the URP result can be
    /// compared against the Blender reference-match renders. The vertex colour
    /// carries the whole albedo, so a colour space mismatch on import would be
    /// invisible to an asset check but obvious here.
    /// </summary>
    public static class StarterIslandCloudTallTreeCapture
    {
        private const int Width = 720;
        private const int Height = 900;

        [MenuItem("CML/Art/CloudTall Trees/Capture Prefab Sheet")]
        public static void Run()
        {
            var outputDirectory = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../Artifacts/Renders/Environment/StarterIsland/V4/Trees/CloudTall"));
            Directory.CreateDirectory(outputDirectory);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var lightObject = new GameObject("Key");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var cameraObject = new GameObject("Capture");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.549f, 0.753f, 0.886f, 1f);
            camera.fieldOfView = 16f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 200f;

            // A freshly created scene has an empty ambient probe, and gradient
            // ambient only reaches it once the environment is rebuilt. Without
            // this the fringe and the trunk, whose normals barely face the key,
            // render black and look like an asset fault.
            RenderSettings.ambientMode =
                UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.26f);
            RenderSettings.ambientIntensity = 1f;
            DynamicGI.UpdateEnvironment();

            var captured = 0;
            foreach (var seasonSuffix in StarterIslandCloudTallTreeSetup.SeasonSuffixes)
            {
                foreach (var variant in StarterIslandCloudTallTreeSetup.Variants)
                {
                    var path = StarterIslandCloudTallTreeSetup.PrefabPath(
                        variant,
                        seasonSuffix);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        Debug.LogError($"CLOUD_TALL_CAPTURE missing prefab {path}");
                        continue;
                    }

                    var instance =
                        PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    if (instance == null)
                    {
                        continue;
                    }

                    instance.transform.position = Vector3.zero;
                    cameraObject.transform.position = new Vector3(0f, 6.2f, -42f);
                    cameraObject.transform.LookAt(new Vector3(0f, 5.2f, 0f));

                    var name =
                        StarterIslandCloudTallTreeSetup.AssetName(variant, seasonSuffix);
                    Render(camera, Path.Combine(outputDirectory, $"{name}_unity.png"));
                    captured++;
                    Object.DestroyImmediate(instance);
                }
            }

            Debug.Log(
                $"CLOUD_TALL_CAPTURE captured={captured} directory={outputDirectory}");
            EditorSceneManager.CloseScene(scene, true);
        }

        private static void Render(Camera camera, string path)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8
            };
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}

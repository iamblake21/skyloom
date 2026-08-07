using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Cattura la scena vetrina in PNG, così l'aggancio si verifica senza dover
    /// aprire l'Editor a mano.
    /// </summary>
    public static class BeltTopologyShowcaseCapture
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/93_BeltTopology_Showcase.unity";

        public static void Capture()
        {
            var outputDirectory = Path.Combine(
                Directory.GetParent(Application.dataPath)?.Parent?.FullName
                ?? Application.dataPath,
                "Artifacts",
                "Renders",
                "BeltTopology");
            Directory.CreateDirectory(outputDirectory);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var shots = new[]
            {
                ("panoramica", new Vector3(4f, 11f, -10f), new Vector3(42f, 0f, 0f)),
                ("A_curva_destra", new Vector3(1.5f, 4.2f, -3.4f), new Vector3(40f, 12f, 0f)),
                ("B_curva_sinistra", new Vector3(7.2f, 4.2f, -3.4f), new Vector3(40f, 8f, 0f)),
                ("C_pressa_curva", new Vector3(-7.4f, 4.6f, -4.0f), new Vector3(38f, 8f, 0f)),
                ("D_salita", new Vector3(16.5f, 4.4f, -3.6f), new Vector3(34f, 6f, 0f))
            };

            var cameraObject = new GameObject("CaptureCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.60f, 0.72f, 0.82f);
            camera.fieldOfView = 55f;

            foreach (var (name, position, euler) in shots)
            {
                cameraObject.transform.SetPositionAndRotation(
                    position,
                    Quaternion.Euler(euler));

                var target = new RenderTexture(1280, 800, 24);
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
                image.Apply();
                RenderTexture.active = null;
                camera.targetTexture = null;

                var path = Path.Combine(outputDirectory, $"showcase_{name}.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
                Debug.Log($"SHOWCASE_SHOT {path}");
            }

            Object.DestroyImmediate(cameraObject);
        }
    }
}

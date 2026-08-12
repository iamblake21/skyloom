using System.IO;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.Art
{
    [InitializeOnLoad]
    internal static class TemporaryLandmassShaderAudit
    {
        static TemporaryLandmassShaderAudit()
        {
            EditorApplication.delayCall += Audit;
        }

        private static void Audit()
        {
            AuditShader("CML/Environment/Starter Island Terrain Splat");
            AuditShader("CML/Environment/Original Cliff Mass");
            AuditEnvironmentLighting();
            CaptureDeterministicOverview(
                new Vector3(-390.0f, 250.0f, -440.0f),
                new Vector3(-4.0f, -24.0f, 0.0f),
                43.0f,
                "DeterministicOverview_AfterNormalAndSkylightFix.png");
            CaptureDeterministicOverview(
                new Vector3(-82.0f, 68.0f, -17.0f),
                new Vector3(-142.0f, 45.0f, 43.0f),
                36.0f,
                "PrefabCloseup_AfterNormalAndSkylightFix.png");
        }

        private static void AuditEnvironmentLighting()
        {
            var directions = new[]
            {
                Vector3.up,
                Vector3.down,
                Vector3.forward,
                Vector3.back,
                Vector3.right,
                Vector3.left
            };
            var values = new Color[directions.Length];
            RenderSettings.ambientProbe.Evaluate(directions, values);
            var sun = RenderSettings.sun;
            Debug.Log(
                "CML_LANDMASS_LIGHTING " +
                $"ambientMode={RenderSettings.ambientMode} " +
                $"ambientIntensity={RenderSettings.ambientIntensity:F4} " +
                $"reflectionIntensity={RenderSettings.reflectionIntensity:F4} " +
                $"sun={(sun != null ? sun.name : "none")} " +
                $"sunColor={(sun != null ? sun.color.ToString("F4") : "none")} " +
                $"sunIntensity={(sun != null ? sun.intensity.ToString("F4") : "none")} " +
                $"sunRay={(sun != null ? sun.transform.forward.ToString("F4") : "none")} " +
                $"SH_up={values[0].ToString("F4")} " +
                $"SH_down={values[1].ToString("F4")} " +
                $"SH_forward={values[2].ToString("F4")} " +
                $"SH_back={values[3].ToString("F4")} " +
                $"SH_right={values[4].ToString("F4")} " +
                $"SH_left={values[5].ToString("F4")}");
        }

        private static void CaptureDeterministicOverview(
            Vector3 position,
            Vector3 targetPoint,
            float fieldOfView,
            string fileName)
        {
            var sceneView = SceneView.lastActiveSceneView;
            var sourceCamera = sceneView != null ? sceneView.camera : null;
            if (sourceCamera == null)
            {
                Debug.LogError("CML_LANDMASS_CAPTURE status=NO_SCENEVIEW");
                return;
            }

            const int width = 1600;
            const int height = 900;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "CML Landmass Temporary Capture",
                antiAliasing = 1
            };
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var cameraObject = new GameObject(
                "CML Temporary Landmass QA Camera",
                typeof(Camera))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var camera = cameraObject.GetComponent<Camera>();
            camera.CopyFrom(sourceCamera);
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(targetPoint - position, Vector3.up));
            camera.fieldOfView = fieldOfView;
            camera.aspect = (float)width / height;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                var path = Path.GetFullPath(
                    Path.Combine(
                        Application.dataPath,
                        "..",
                        "Artifacts",
                        "Reviews",
                        "Landmass",
                        fileName));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log($"CML_LANDMASS_CAPTURE status=CAPTURED path='{path}'");
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = null;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void AuditShader(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"CML_LANDMASS_SHADER_AUDIT name='{shaderName}' found=0");
                return;
            }

            var messages = ShaderUtil.GetShaderMessages(shader);
            foreach (var message in messages)
            {
                Debug.LogError(
                    $"CML_LANDMASS_SHADER_MESSAGE name='{shaderName}' " +
                    $"severity={message.severity} platform={message.platform} " +
                    $"line={message.line} message={message.message}");
            }

            Debug.Log(
                $"CML_LANDMASS_SHADER_AUDIT name='{shaderName}' " +
                $"supported={shader.isSupported} messages={messages.Length}");
        }
    }
}

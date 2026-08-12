using System;
using CML.Unity.World;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.Art
{
    /// <summary>
    /// Refreshes the open-scene daylight preview only after the authored sky
    /// shader or material has completed importing. No scene asset is dirtied
    /// or saved; the controller replaces only its hidden material clone.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class MeasuredStylizedDaylightSkyboxPostprocessor :
        AssetPostprocessor
    {
        private const string SkyShaderPath =
            "Assets/_Project/Art/Environment/StarterIsland/Shaders/" +
            "StarterIslandAtmosphericSky.shader";
        private const string SkyMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_Skybox.mat";
        private const string RefreshPendingSessionKey =
            "CML.MeasuredStylizedDaylight.SkyRefreshPending";
        private const double PollIntervalSeconds = 0.25d;
        private const double IdleReadinessTimeoutSeconds = 30d;

        private static bool refreshQueued;
        private static bool observedIdleEditor;
        private static double nextPollTime;
        private static double readinessDeadline;

        static MeasuredStylizedDaylightSkyboxPostprocessor()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // A domain reload invalidates all non-serialized clone provenance.
            // Revalidate loaded controllers once even when the reload was not
            // initiated by a watched asset; this also repairs previews left by
            // an interrupted import or editor crash.
            QueueRefresh(resetReadinessWindow: true);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!ContainsWatchedPath(importedAssets) &&
                !ContainsWatchedPath(deletedAssets) &&
                !ContainsWatchedPath(movedAssets) &&
                !ContainsWatchedPath(movedFromAssetPaths))
            {
                return;
            }

            QueueRefresh(resetReadinessWindow: true);
        }

        private static bool ContainsWatchedPath(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            foreach (var path in paths)
            {
                if (string.Equals(
                        path,
                        SkyShaderPath,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        path,
                        SkyMaterialPath,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void QueueRefresh(bool resetReadinessWindow)
        {
            SessionState.SetBool(RefreshPendingSessionKey, true);
            if (resetReadinessWindow)
            {
                observedIdleEditor = false;
                readinessDeadline = 0d;
            }

            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            nextPollTime = 0d;
            EditorApplication.update += RefreshWhenReady;
        }

        private static void RefreshWhenReady()
        {
            if (!refreshQueued)
            {
                UnsubscribeRefresh();
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < nextPollTime)
            {
                return;
            }

            nextPollTime = now + PollIntervalSeconds;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Keep the request in SessionState and resume it after Unity
                // returns to edit mode. Never replace preview resources while
                // a play-mode transition is in progress.
                UnsubscribeRefresh();
                return;
            }

            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            if (!observedIdleEditor)
            {
                observedIdleEditor = true;
                readinessDeadline = now + IdleReadinessTimeoutSeconds;
            }

            if (!TryGetReadySkyAssets(out var shader, out var material))
            {
                if (now >= readinessDeadline)
                {
                    CompleteRefresh();
                    Debug.LogWarning(
                        "Measured daylight sky preview refresh stopped: " +
                        "the authored shader/material remained invalid for " +
                        $"{IdleReadinessTimeoutSeconds:0} idle seconds. A " +
                        "later sky asset import will retry automatically.");
                }

                return;
            }

            var rebuiltCount = 0;
            var controllers =
                UnityEngine.Object.FindObjectsByType<MeasuredStylizedDaylight>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller == null ||
                    !controller.isActiveAndEnabled ||
                    !controller.gameObject.scene.IsValid() ||
                    !controller.gameObject.scene.isLoaded)
                {
                    continue;
                }

                if (controller.RebuildSkyboxPreview())
                {
                    rebuiltCount++;
                }
            }

            CompleteRefresh();
            if (rebuiltCount > 0)
            {
                SceneView.RepaintAll();
            }

            var shaderHash =
                AssetDatabase.GetAssetDependencyHash(SkyShaderPath);
            var materialHash =
                AssetDatabase.GetAssetDependencyHash(SkyMaterialPath);
            Debug.Log(
                "Measured daylight sky refresh complete: " +
                $"shader='{shader.name}', shaderHash={shaderHash}, " +
                $"material='{material.name}', materialHash={materialHash}, " +
                $"rebuiltControllers={rebuiltCount}, cloudContract=valid.");
        }

        private static bool TryGetReadySkyAssets(
            out Shader shader,
            out Material material)
        {
            shader = AssetDatabase.LoadAssetAtPath<Shader>(SkyShaderPath);
            material =
                AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            return shader != null &&
                shader.isSupported &&
                !ShaderUtil.ShaderHasError(shader) &&
                material != null &&
                material.shader == shader &&
                MeasuredStylizedDaylight.HasSkyMaterialContract(material);
        }

        private static void BeforeAssemblyReload()
        {
            UnsubscribeRefresh();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode &&
                SessionState.GetBool(RefreshPendingSessionKey, false))
            {
                QueueRefresh(resetReadinessWindow: true);
            }
        }

        private static void CompleteRefresh()
        {
            SessionState.EraseBool(RefreshPendingSessionKey);
            observedIdleEditor = false;
            readinessDeadline = 0d;
            UnsubscribeRefresh();
        }

        private static void UnsubscribeRefresh()
        {
            if (!refreshQueued)
            {
                return;
            }

            EditorApplication.update -= RefreshWhenReady;
            refreshQueued = false;
        }
    }
}

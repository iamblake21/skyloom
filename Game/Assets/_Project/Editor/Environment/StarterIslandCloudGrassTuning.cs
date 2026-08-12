using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Tunes the approved procedural clouds and the existing native Terrain
    /// grass in place. This never opens, rebuilds or saves a scene.
    /// </summary>
    public static class StarterIslandCloudGrassTuning
    {
        private const string SkyMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_Skybox.mat";
        private const string GrassMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_GroundDetail.mat";
        private const string TerrainDataPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Data/TD_StarterIsland.asset";
        private const string OneShotMarker =
            "Temp/CML_ApplyCloudGrassTuning.once";
        private const string GentleGlobalBalanceMarker =
            "Temp/CML_ApplyGentleGlobalBalance.once";
        private const string GentleLightingBalanceMarker =
            "Temp/CML_ApplyGentleLightingBalance.once";
        private const string RestoreLightingBaselineMarker =
            "Temp/CML_RestoreLightingBaseline.once";
        private const string ColorProfilePath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/VP_StarterIsland_ColorGrading.asset";

        private const float CloudSpeed = 0.024f;
        private const float WindStrength = 0.040f;
        private const float WindSpeed = 1.62f;
        private const float GustStrength = 0.46f;
        private const float PathEdgeStart = 0.065f;
        private const float PathInteriorStart = 0.34f;

        [InitializeOnLoadMethod]
        private static void QueueOneShot()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            var marker = Path.Combine(projectRoot, OneShotMarker);
            if (!File.Exists(marker))
            {
                return;
            }

            File.Delete(marker);
            EditorApplication.delayCall += ApplyWhenReady;
        }

        [InitializeOnLoadMethod]
        private static void QueueGentleGlobalBalance()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            var marker = Path.Combine(
                projectRoot,
                GentleGlobalBalanceMarker);
            if (!File.Exists(marker))
            {
                return;
            }

            File.Delete(marker);
            EditorApplication.delayCall += ApplyGentleBalanceWhenReady;
        }

        [InitializeOnLoadMethod]
        private static void QueueGentleLightingBalance()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            var marker = Path.Combine(
                projectRoot,
                GentleLightingBalanceMarker);
            if (!File.Exists(marker))
            {
                return;
            }

            File.Delete(marker);
            EditorApplication.delayCall += ApplyGentleLightingWhenReady;
        }

        [InitializeOnLoadMethod]
        private static void QueueRestoreLightingBaseline()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            var marker = Path.Combine(
                projectRoot,
                RestoreLightingBaselineMarker);
            if (!File.Exists(marker))
            {
                return;
            }

            File.Delete(marker);
            EditorApplication.delayCall += RestoreLightingBaselineWhenReady;
        }

        private static void RestoreLightingBaselineWhenReady()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += RestoreLightingBaselineWhenReady;
                return;
            }

            try
            {
                RestoreGlobalLightingBaseline();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void ApplyGentleLightingWhenReady()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += ApplyGentleLightingWhenReady;
                return;
            }

            try
            {
                ApplyGentleLightingBalance();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void ApplyGentleBalanceWhenReady()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += ApplyGentleBalanceWhenReady;
                return;
            }

            try
            {
                ApplyGentleGlobalBalance();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void ApplyWhenReady()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += ApplyWhenReady;
                return;
            }

            try
            {
                Apply();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("CML/Art/Apply Cloud And Terrain Grass Tuning")]
        public static void Apply()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(
                SkyMaterialPath);
            var grassMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                GrassMaterialPath);
            var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(
                TerrainDataPath);
            if (sky == null || grassMaterial == null || terrainData == null)
            {
                throw new InvalidOperationException(
                    "Starter Island sky, grass material or TerrainData is missing.");
            }

            Undo.RegisterCompleteObjectUndo(
                terrainData,
                "Clear Terrain grass from paths");
            Undo.RecordObject(sky, "Animate procedural clouds");
            Undo.RecordObject(grassMaterial, "Increase Terrain grass wind");

            SetFloat(sky, "_CloudSpeed", CloudSpeed);
            SetFloat(grassMaterial, "_WindStrength", WindStrength);
            SetFloat(grassMaterial, "_WindSpeed", WindSpeed);
            SetFloat(grassMaterial, "_GustStrength", GustStrength);

            var pathLayer = FindPathLayer(terrainData);
            var alphamaps = terrainData.GetAlphamaps(
                0,
                0,
                terrainData.alphamapWidth,
                terrainData.alphamapHeight);
            var resolution = terrainData.detailResolution;
            long instancesBefore = 0;
            long instancesAfter = 0;
            long removedFromPaths = 0;
            long retainedEdgeTufts = 0;
            var grassLayerCount = 0;
            var prototypes = terrainData.detailPrototypes;

            for (var layer = 0; layer < prototypes.Length; layer++)
            {
                var prototype = prototypes[layer];
                if (prototype == null || prototype.prototype == null ||
                    prototype.prototype.name.IndexOf(
                        "Grass",
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                grassLayerCount++;
                var density = terrainData.GetDetailLayer(
                    0,
                    0,
                    resolution,
                    resolution,
                    layer);
                for (var z = 0; z < resolution; z++)
                {
                    var normalizedZ = (z + 0.5f) / resolution;
                    for (var x = 0; x < resolution; x++)
                    {
                        var original = density[z, x];
                        instancesBefore += original;
                        if (original <= 0)
                        {
                            continue;
                        }

                        var normalizedX = (x + 0.5f) / resolution;
                        var path = SampleLayerBilinear(
                            alphamaps,
                            normalizedX,
                            normalizedZ,
                            pathLayer);
                        if (path >= PathInteriorStart)
                        {
                            removedFromPaths += original;
                            density[z, x] = 0;
                            continue;
                        }

                        if (path > PathEdgeStart)
                        {
                            var edgeDepth = Mathf.InverseLerp(
                                PathEdgeStart,
                                PathInteriorStart,
                                path);
                            var keepChance = Mathf.Lerp(
                                0.20f,
                                0.012f,
                                edgeDepth);
                            if (Deterministic01(x, z, layer) > keepChance)
                            {
                                removedFromPaths += original;
                                density[z, x] = 0;
                            }
                            else
                            {
                                density[z, x] = Mathf.Min(original, 1);
                                retainedEdgeTufts += density[z, x];
                            }
                        }
                    }
                }

                instancesAfter += CountMap(density);
                terrainData.SetDetailLayer(0, 0, layer, density);
            }

            EditorUtility.SetDirty(sky);
            EditorUtility.SetDirty(grassMaterial);
            EditorUtility.SetDirty(terrainData);
            AssetDatabase.SaveAssets();

            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain != null && terrain.terrainData == terrainData)
                {
                    terrain.Flush();
                }
            }

            Debug.Log(
                "CML_CLOUD_GRASS_TUNING status=PASS " +
                $"cloudSpeed={CloudSpeed:F3} " +
                $"windStrength={WindStrength:F3} " +
                $"windSpeed={WindSpeed:F2} gust={GustStrength:F2} " +
                $"grassLayers={grassLayerCount} " +
                $"instancesBefore={instancesBefore} " +
                $"instancesAfter={instancesAfter} " +
                $"removedFromPaths={removedFromPaths} " +
                $"retainedEdgeTufts={retainedEdgeTufts} " +
                "sceneRebuilds=0 sceneWrites=0 heightWrites=0 " +
                "alphamapWrites=0");
        }

        [MenuItem("CML/Art/Apply Gentle Global 80-20 Balance")]
        public static void ApplyGentleGlobalBalance()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                ColorProfilePath);
            if (profile == null ||
                !profile.TryGet<ColorAdjustments>(out var grading))
            {
                throw new InvalidOperationException(
                    "Starter Island ColorAdjustments profile is missing.");
            }

            Undo.RecordObject(grading, "Apply gentle global 80-20 balance");
            grading.active = true;
            grading.postExposure.overrideState = true;
            grading.postExposure.value = 0.0f;
            grading.contrast.overrideState = true;
            grading.contrast.value = 8.0f;
            grading.saturation.overrideState = true;
            grading.saturation.value = 3.0f;
            EditorUtility.SetDirty(grading);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "CML_GENTLE_GLOBAL_BALANCE status=PASS " +
                "exposure=0 contrast=8 saturation=3 " +
                "materialWrites=0 shaderWrites=0 ssaoWrites=0 " +
                "sunWrites=0 sceneRebuilds=0 sceneWrites=0");
        }

        [MenuItem("CML/Art/Restore Second Screenshot Global Baseline")]
        public static void RestoreSecondScreenshotBaseline()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                ColorProfilePath);
            if (profile == null ||
                !profile.TryGet<ColorAdjustments>(out var grading))
            {
                throw new InvalidOperationException(
                    "Starter Island ColorAdjustments profile is missing.");
            }

            Undo.RecordObject(grading, "Restore second screenshot baseline");
            grading.postExposure.value = -0.05f;
            grading.contrast.value = 12.0f;
            grading.saturation.value = 4.0f;
            EditorUtility.SetDirty(grading);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "CML_SECOND_SCREENSHOT_BASELINE status=RESTORED " +
                "exposure=-0.05 contrast=12 saturation=4 " +
                "sceneRebuilds=0 sceneWrites=0");
        }

        [MenuItem("CML/Art/Apply Gentle Global Lighting Balance")]
        public static void ApplyGentleLightingBalance()
        {
            ApplyLightingValues(
                ambientIntensity: 0.54f,
                shadowStrength: 0.82f,
                operationName: "Apply gentle global lighting balance");
            Debug.Log(
                "CML_GENTLE_LIGHTING_BALANCE status=PASS " +
                "ambientIntensity=0.54 shadowStrength=0.82 " +
                "sunIntensityWrites=0 sunColorWrites=0 " +
                "materialWrites=0 gradingWrites=0 sceneRebuilds=0 " +
                "sceneSaved=0");
        }

        [MenuItem("CML/Art/Restore Global Lighting Baseline")]
        public static void RestoreGlobalLightingBaseline()
        {
            ApplyLightingValues(
                ambientIntensity: 0.48f,
                shadowStrength: 0.88f,
                operationName: "Restore global lighting baseline");
            Debug.Log(
                "CML_GLOBAL_LIGHTING_BASELINE status=RESTORED " +
                "ambientIntensity=0.48 shadowStrength=0.88 " +
                "sceneRebuilds=0 sceneSaved=0");
        }

        private static void ApplyLightingValues(
            float ambientIntensity,
            float shadowStrength,
            string operationName)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(
                    scene.name,
                    "91_StarterIsland_Terrain_Review",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Open 91_StarterIsland_Terrain_Review before applying " +
                    "the Starter Island lighting balance.");
            }

            var sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var candidate in
                             root.GetComponentsInChildren<Light>(true))
                    {
                        if (candidate.type == LightType.Directional &&
                            string.Equals(
                                candidate.gameObject.name,
                                "ENV_Sun",
                                StringComparison.Ordinal))
                        {
                            sun = candidate;
                            break;
                        }
                    }

                    if (sun != null)
                    {
                        break;
                    }
                }
            }

            if (sun == null)
            {
                throw new InvalidOperationException(
                    "Starter Island directional sun is missing.");
            }

            Undo.RecordObject(sun, operationName);
            RenderSettings.ambientIntensity = ambientIntensity;
            sun.shadowStrength = shadowStrength;
            EditorUtility.SetDirty(sun);
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
        }

        private static int FindPathLayer(TerrainData data)
        {
            var layers = data.terrainLayers;
            for (var index = 0; index < layers.Length; index++)
            {
                if (layers[index] != null &&
                    layers[index].name.IndexOf(
                        "DirtPath",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return index;
                }
            }

            if (data.alphamapLayers > 2)
            {
                return 2;
            }

            throw new InvalidOperationException(
                "Starter Island DirtPath Terrain layer is missing.");
        }

        private static float SampleLayerBilinear(
            float[,,] alphamaps,
            float normalizedX,
            float normalizedZ,
            int layer)
        {
            var width = alphamaps.GetLength(1);
            var height = alphamaps.GetLength(0);
            var sampleX = Mathf.Clamp01(normalizedX) * (width - 1);
            var sampleZ = Mathf.Clamp01(normalizedZ) * (height - 1);
            var x0 = Mathf.FloorToInt(sampleX);
            var z0 = Mathf.FloorToInt(sampleZ);
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var z1 = Mathf.Min(z0 + 1, height - 1);
            var tx = sampleX - x0;
            var tz = sampleZ - z0;
            var bottom = Mathf.Lerp(
                alphamaps[z0, x0, layer],
                alphamaps[z0, x1, layer],
                tx);
            var top = Mathf.Lerp(
                alphamaps[z1, x0, layer],
                alphamaps[z1, x1, layer],
                tx);
            return Mathf.Lerp(bottom, top, tz);
        }

        private static float Deterministic01(int x, int z, int layer)
        {
            unchecked
            {
                uint hash = (uint)(x * 73856093) ^
                    (uint)(z * 19349663) ^
                    (uint)(layer * 83492791) ^ 0x9E3779B9u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static long CountMap(int[,] map)
        {
            long count = 0;
            foreach (var value in map)
            {
                count += value;
            }

            return count;
        }

        private static void SetFloat(
            Material material,
            string property,
            float value)
        {
            if (!material.HasProperty(property))
            {
                throw new InvalidOperationException(
                    $"Material {material.name} has no {property} property.");
            }

            material.SetFloat(property, value);
        }
    }
}

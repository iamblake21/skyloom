using System;
using System.IO;
using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Renders the production Terrain grass in an isolated additive scene.
    /// Starter Island's scene, TerrainData and prefab are never saved here.
    /// </summary>
    public static class StarterIslandAdaptiveGrassReviewCapture
    {
        private const string TerrainPrefabPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Prefabs/PF_StarterIsland_Terrain.prefab";
        private const string OutputRoot =
            "Artifacts/Reviews/TerrainGrass";
        private const string OneShotMarker =
            "Temp/CML_RenderAdaptiveGrass.once";
        private const int PanelWidth = 640;
        private const int PanelHeight = 400;
        private const int ReviewLayer = 30;

        private static readonly int WindOverrideEnabledId =
            Shader.PropertyToID("_CMLGrassWindTimeOverrideEnabled");
        private static readonly int WindOverrideTimeId =
            Shader.PropertyToID("_CMLGrassWindTimeOverride");

        [InitializeOnLoadMethod]
        private static void QueueOneShotCapture()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            var markerPath = Path.Combine(projectRoot, OneShotMarker);
            if (!File.Exists(markerPath))
            {
                return;
            }

            File.Delete(markerPath);
            EditorApplication.delayCall += Run;
        }

        [MenuItem("CML/Art/Render Adaptive Terrain Grass Review")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                TerrainPrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    "Starter Island Terrain prefab is missing.",
                    TerrainPrefabPath);
            }

            var previousActiveScene = SceneManager.GetActiveScene();
            var replacesEmptyUntitled =
                string.IsNullOrEmpty(previousActiveScene.path) &&
                previousActiveScene.rootCount == 0 &&
                !previousActiveScene.isDirty;
            if (string.IsNullOrEmpty(previousActiveScene.path) &&
                !replacesEmptyUntitled)
            {
                throw new InvalidOperationException(
                    "Save the current untitled scene before rendering the " +
                    "adaptive-grass review.");
            }

            var previousShadowDistance = QualitySettings.shadowDistance;
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbientLight = RenderSettings.ambientLight;
            var previousAmbientIntensity = RenderSettings.ambientIntensity;
            var previousFog = RenderSettings.fog;
            var previousSun = RenderSettings.sun;
            var previewScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replacesEmptyUntitled
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            Texture2D[] panels = null;
            try
            {
                if (SceneManager.GetActiveScene() != previewScene &&
                    !SceneManager.SetActiveScene(previewScene))
                {
                    throw new InvalidOperationException(
                        "Could not activate isolated grass review scene.");
                }

                var island = PrefabUtility.InstantiatePrefab(
                    prefab,
                    previewScene) as GameObject;
                if (island == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the production Terrain.");
                }

                island.name = "REVIEW_ProductionTerrain_AdaptiveGrass";
                SetLayerRecursively(island, ReviewLayer);
                var terrain = island.GetComponentInChildren<Terrain>(true);
                if (terrain == null || terrain.terrainData == null)
                {
                    throw new InvalidOperationException(
                        "Production Terrain prefab has no TerrainData.");
                }

                terrain.detailObjectDistance = 92f;
                terrain.detailObjectDensity = 1f;
                terrain.drawTreesAndFoliage = true;
                terrain.drawInstanced = true;
                terrain.Flush();

                ConfigureLighting(previewScene);
                var camera = CreateCamera(previewScene);
                TerrainSurfaceBlendGlobals.BindTerrain(terrain);
                Debug.Log(
                    "ADAPTIVE_TERRAIN_GRASS_BLEND_BIND " +
                    $"enabled=" +
                    $"{Shader.GetGlobalFloat("_CMLTerrainBlendEnabled"):F1} " +
                    $"control=" +
                    $"{Shader.GetGlobalTexture("_CMLTerrainBlendControl")?.name}");
                QualitySettings.shadowDistance = 110f;
                Shader.SetGlobalFloat(WindOverrideEnabledId, 1f);

                panels = new[]
                {
                    RenderView(
                        terrain,
                        camera,
                        new Vector2(85f, 55f),
                        new Vector2(102f, 55f),
                        50f,
                        0.15f),
                    RenderView(
                        terrain,
                        camera,
                        new Vector2(-218f, 10f),
                        new Vector2(-201f, 10f),
                        50f,
                        0.15f),
                    RenderView(
                        terrain,
                        camera,
                        new Vector2(-81.18f, 23.50f),
                        new Vector2(-70.08f, 40.14f),
                        52f,
                        0.15f),
                    RenderView(
                        terrain,
                        camera,
                        new Vector2(91f, 55f),
                        new Vector2(99f, 55f),
                        46f,
                        0.15f),
                    RenderView(
                        terrain,
                        camera,
                        new Vector2(-218f, 10f),
                        new Vector2(-201f, 10f),
                        50f,
                        0.15f),
                    RenderView(
                        terrain,
                        camera,
                        new Vector2(-218f, 10f),
                        new Vector2(-201f, 10f),
                        50f,
                        2.55f)
                };

                var names = new[]
                {
                    "GrassSun",
                    "GrassDeep",
                    "GrassPathBlend",
                    "GrassBladeClose",
                    "GrassWindT0",
                    "GrassWindT1"
                };
                var absoluteRoot = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    OutputRoot));
                Directory.CreateDirectory(absoluteRoot);
                for (var index = 0; index < panels.Length; index++)
                {
                    File.WriteAllBytes(
                        Path.Combine(absoluteRoot, names[index] + ".png"),
                        panels[index].EncodeToPNG());
                }

                var sheet = ComposeContactSheet(panels);
                try
                {
                    var sheetPath = Path.Combine(
                        absoluteRoot,
                        "AdaptiveTerrainGrassReview.png");
                    File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
                    Debug.Log(
                        "ADAPTIVE_TERRAIN_GRASS_REVIEW status=CAPTURED " +
                        $"output={sheetPath} panels=6 " +
                        "views=sun,deep,path,close,wind0,wind1 " +
                        $"detailPrototypes=" +
                        $"{terrain.terrainData.detailPrototypes.Length} " +
                        "sceneWrites=0 prefabWrites=0 terrainDataWrites=0");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(sheet);
                }
            }
            finally
            {
                Shader.SetGlobalFloat(WindOverrideEnabledId, 0f);
                Shader.SetGlobalFloat(WindOverrideTimeId, 0f);
                QualitySettings.shadowDistance = previousShadowDistance;
                if (panels != null)
                {
                    for (var index = 0; index < panels.Length; index++)
                    {
                        if (panels[index] != null)
                        {
                            UnityEngine.Object.DestroyImmediate(panels[index]);
                        }
                    }
                }

                if (replacesEmptyUntitled)
                {
                    EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Single);
                    RenderSettings.ambientMode = previousAmbientMode;
                    RenderSettings.ambientLight = previousAmbientLight;
                    RenderSettings.ambientIntensity = previousAmbientIntensity;
                    RenderSettings.fog = previousFog;
                    RenderSettings.sun = previousSun;
                }
                else
                {
                    if (previousActiveScene.IsValid() &&
                        previousActiveScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(previousActiveScene);
                        RenderSettings.ambientMode = previousAmbientMode;
                        RenderSettings.ambientLight = previousAmbientLight;
                        RenderSettings.ambientIntensity =
                            previousAmbientIntensity;
                        RenderSettings.fog = previousFog;
                        RenderSettings.sun = previousSun;
                    }

                    EditorSceneManager.CloseScene(
                        previewScene,
                        removeScene: true);
                }

                TerrainSurfaceBlendGlobals.BindActiveTerrain();
            }
        }

        private static void ConfigureLighting(Scene scene)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(
                0.39f,
                0.43f,
                0.33f,
                1f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;

            var lightObject = new GameObject("REVIEW_Sun")
            {
                layer = ReviewLayer
            };
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation = Quaternion.Euler(
                47f,
                -32f,
                0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.91f, 0.78f, 1f);
            light.intensity = 1.18f;
            light.shadows = LightShadows.Soft;
            light.cullingMask = 1 << ReviewLayer;
            RenderSettings.sun = light;
        }

        private static Camera CreateCamera(Scene scene)
        {
            var cameraObject = new GameObject("REVIEW_GrassCamera")
            {
                layer = ReviewLayer
            };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(
                0.54f,
                0.69f,
                0.72f,
                1f);
            camera.cullingMask = 1 << ReviewLayer;
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 220f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.depthTextureMode = DepthTextureMode.DepthNormals;
            return camera;
        }

        private static Texture2D RenderView(
            Terrain terrain,
            Camera camera,
            Vector2 cameraXZ,
            Vector2 targetXZ,
            float fieldOfView,
            float windTime)
        {
            var cameraPosition = new Vector3(
                cameraXZ.x,
                SampleWorldHeight(terrain, cameraXZ) + 1.45f,
                cameraXZ.y);
            var targetPosition = new Vector3(
                targetXZ.x,
                SampleWorldHeight(terrain, targetXZ) + 0.25f,
                targetXZ.y);
            camera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(
                    targetPosition - cameraPosition,
                    Vector3.up));
            camera.fieldOfView = fieldOfView;
            Shader.SetGlobalFloat(WindOverrideTimeId, windTime);
            terrain.Flush();

            var renderTexture = RenderTexture.GetTemporary(
                PanelWidth,
                PanelHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default,
                4);
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                camera.Render();
                camera.Render();
                RenderTexture.active = renderTexture;
                var result = new Texture2D(
                    PanelWidth,
                    PanelHeight,
                    TextureFormat.RGB24,
                    false,
                    false);
                result.ReadPixels(
                    new Rect(0, 0, PanelWidth, PanelHeight),
                    0,
                    0,
                    false);
                result.Apply(false, false);
                return result;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static float SampleWorldHeight(
            Terrain terrain,
            Vector2 point)
        {
            return terrain.SampleHeight(new Vector3(
                       point.x,
                       0f,
                       point.y)) +
                   terrain.transform.position.y;
        }

        private static Texture2D ComposeContactSheet(Texture2D[] panels)
        {
            var sheet = new Texture2D(
                PanelWidth * 3,
                PanelHeight * 2,
                TextureFormat.RGB24,
                false,
                false);
            var background = new Color32(13, 17, 14, 255);
            var pixels = new Color32[sheet.width * sheet.height];
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = background;
            }

            sheet.SetPixels32(pixels);
            for (var index = 0; index < panels.Length; index++)
            {
                var column = index % 3;
                var row = 1 - index / 3;
                sheet.SetPixels32(
                    column * PanelWidth,
                    row * PanelHeight,
                    PanelWidth,
                    PanelHeight,
                    panels[index].GetPixels32());
            }

            sheet.Apply(false, false);
            return sheet;
        }

        private static void SetLayerRecursively(
            GameObject root,
            int layer)
        {
            foreach (var transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
        }
    }
}

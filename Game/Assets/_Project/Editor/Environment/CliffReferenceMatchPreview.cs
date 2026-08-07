using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.CliffReferenceMatch
{
    internal static class CliffReferenceMatchPreview
    {
        private const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/ReferenceMatch";
        private const string TexturePath =
            Root + "/T_StarterIsland_CliffPeach_ReferenceMatch_v1.png";
        private const string ShaderName =
            "CML/Environment/Starter Island Terrain Reference Match";
        private const string MaterialPath =
            Root + "/M_StarterIsland_Terrain_ReferenceMatch_v1.mat";
        private const string SourceIslandMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Materials/" +
            "M_StarterIsland_Terrain.mat";
        private const string OriginalShaderCloneMaterialPath =
            Root + "/M_StarterIsland_Terrain_ReferenceMatch_v2_OriginalShader.mat";
        private const string MainIslandMaterialPath =
            Root + "/M_StarterIsland_Terrain_ReferenceMatch_v3_MainIsland.mat";
        private const string LayerPath =
            Root + "/TL_StarterIsland_CliffPeach_ReferenceMatch_v1.terrainlayer";
        private const string PreviewPath =
            Root + "/Preview_StarterIsland_CliffPeach_ReferenceMatch_v1.png";
        private const string TargetScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";
        private const string AutoApplyMarkerPath =
            Root + "/APPLY_CLIFF_REFERENCE_MATCH.pending";
        private const string OriginalShaderApplyMarkerPath =
            Root + "/APPLY_ORIGINAL_ISLAND_SHADER.pending";
        private const string MainIslandApplyMarkerPath =
            Root + "/APPLY_REFERENCE_MATCH_TO_MAIN_ISLAND.pending";

        [MenuItem("CML/Environment/Generate Cliff Reference Match Preview")]
        public static void Generate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImporter();

            Texture2D cliffTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (cliffTexture == null)
            {
                throw new InvalidOperationException(
                    "Reference-match cliff texture was not imported: " + TexturePath);
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "Reference-match terrain shader is unavailable or unsupported: " +
                    ShaderName);
            }

            Material material = CreateOrUpdateMaterial(shader);
            TerrainLayer cliffLayer = CreateOrUpdateCliffLayer(cliffTexture);
            AssetDatabase.SaveAssets();

            RenderPreview(material, cliffLayer);
            AssetDatabase.ImportAsset(
                PreviewPath,
                ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Generated isolated cliff reference-match preview at " + PreviewPath);
        }

        public static void GenerateFromCommandLine()
        {
            try
            {
                Generate();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        [InitializeOnLoadMethod]
        private static void ApplyPendingRequestAfterReload()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string markerAbsolutePath = Path.Combine(
                projectRoot,
                AutoApplyMarkerPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(markerAbsolutePath))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    ApplyToStandaloneTerrainFromCommandLine();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    AssetDatabase.DeleteAsset(AutoApplyMarkerPath);
                }
            };
        }

        [InitializeOnLoadMethod]
        private static void ApplyOriginalIslandShaderRequestAfterReload()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string markerAbsolutePath = Path.Combine(
                projectRoot,
                OriginalShaderApplyMarkerPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (!File.Exists(markerAbsolutePath))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    ApplyOriginalIslandShaderToStandaloneTerrain();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    AssetDatabase.DeleteAsset(OriginalShaderApplyMarkerPath);
                }
            };
        }

        [MenuItem("CML/Environment/Apply Original Island Shader to Root Terrain")]
        public static void ApplyOriginalIslandShaderToStandaloneTerrain()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Material sourceMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SourceIslandMaterialPath);
            TerrainLayer cliffLayer =
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (sourceMaterial == null || cliffLayer == null)
            {
                throw new InvalidOperationException(
                    "The source island material or reference cliff layer is missing.");
            }

            Material clone = AssetDatabase.LoadAssetAtPath<Material>(
                OriginalShaderCloneMaterialPath);
            if (clone == null)
            {
                clone = new Material(sourceMaterial);
                AssetDatabase.CreateAsset(clone, OriginalShaderCloneMaterialPath);
            }
            else
            {
                clone.shader = sourceMaterial.shader;
                clone.CopyPropertiesFromMaterial(sourceMaterial);
            }
            clone.name = "M_StarterIsland_Terrain_ReferenceMatch_v2_OriginalShader";

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != TargetScenePath)
            {
                throw new InvalidOperationException(
                    "Safety check failed: the active scene is not " + TargetScenePath);
            }

            GameObject targetObject = null;
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == "Terrain")
                {
                    targetObject = rootObject;
                    break;
                }
            }

            Terrain targetTerrain =
                targetObject != null ? targetObject.GetComponent<Terrain>() : null;
            if (targetTerrain == null || targetTerrain.terrainData == null)
            {
                throw new InvalidOperationException(
                    "The standalone root Terrain was not found.");
            }

            TerrainData data = targetTerrain.terrainData;
            string terrainDataPath = AssetDatabase.GetAssetPath(data);
            if (terrainDataPath != "Assets/New Terrain 3.asset")
            {
                throw new InvalidOperationException(
                    "Safety check failed: unexpected TerrainData " + terrainDataPath);
            }

            TerrainLayer[] layers = data.terrainLayers;
            if (layers == null || layers.Length < 4)
            {
                throw new InvalidOperationException(
                    "The target Terrain does not have the expected four-layer stack.");
            }
            layers = (TerrainLayer[])layers.Clone();
            layers[3] = cliffLayer;
            data.terrainLayers = layers;

            clone.SetVector(
                "_TerrainSizeXZ",
                new Vector4(data.size.x, data.size.z, 0f, 0f));
            targetTerrain.materialTemplate = clone;

            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(clone);
            EditorUtility.SetDirty(targetTerrain);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException("Unity could not save the target scene.");
            }

            Debug.Log(
                "Applied original island shader clone to root Terrain. " +
                "Material=" + OriginalShaderCloneMaterialPath +
                "; TerrainData=" + terrainDataPath);
        }

        [InitializeOnLoadMethod]
        private static void ApplyMainIslandRequestAfterReload()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string markerAbsolutePath = Path.Combine(
                projectRoot,
                MainIslandApplyMarkerPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(markerAbsolutePath))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    ApplyReferenceMatchToMainIsland();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    AssetDatabase.DeleteAsset(MainIslandApplyMarkerPath);
                }
            };
        }

        [MenuItem("CML/Environment/Apply Reference Match to Main Island")]
        public static void ApplyReferenceMatchToMainIsland()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Material sourceMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SourceIslandMaterialPath);
            TerrainLayer cliffLayer =
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (sourceMaterial == null || cliffLayer == null)
            {
                throw new InvalidOperationException(
                    "The source island material or reference cliff layer is missing.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != TargetScenePath)
            {
                throw new InvalidOperationException(
                    "Safety check failed: the active scene is not " + TargetScenePath);
            }

            Terrain targetTerrain = null;
            Terrain[] terrains = UnityEngine.Object.FindObjectsByType<Terrain>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (Terrain terrain in terrains)
            {
                if (terrain.name == "TerrainTop" && terrain.terrainData != null)
                {
                    string candidatePath = AssetDatabase.GetAssetPath(terrain.terrainData);
                    if (candidatePath.EndsWith("/TD_StarterIsland.asset", StringComparison.Ordinal))
                    {
                        targetTerrain = terrain;
                        break;
                    }
                }
            }
            if (targetTerrain == null)
            {
                throw new InvalidOperationException(
                    "Main Island TerrainTop using TD_StarterIsland was not found.");
            }

            TerrainData data = targetTerrain.terrainData;
            TerrainLayer[] layers = data.terrainLayers;
            if (layers == null || layers.Length < 4)
            {
                throw new InvalidOperationException(
                    "Main Island does not have the expected four-layer stack.");
            }
            layers = (TerrainLayer[])layers.Clone();
            layers[3] = cliffLayer;
            data.terrainLayers = layers;

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MainIslandMaterialPath);
            if (material == null)
            {
                material = new Material(sourceMaterial);
                AssetDatabase.CreateAsset(material, MainIslandMaterialPath);
            }
            else
            {
                material.shader = sourceMaterial.shader;
                material.CopyPropertiesFromMaterial(sourceMaterial);
            }
            material.name = "M_StarterIsland_Terrain_ReferenceMatch_v3_MainIsland";
            material.SetVector(
                "_TerrainSizeXZ",
                new Vector4(data.size.x, data.size.z, 0f, 0f));
            targetTerrain.materialTemplate = material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(targetTerrain);

            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(targetTerrain);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException("Unity could not save the target scene.");
            }

            Debug.Log(
                "Applied reference-match shader and cliff layer to Main Island. " +
                "Material=" + MainIslandMaterialPath +
                "; TerrainData=" + AssetDatabase.GetAssetPath(data) +
                "; Size=" + data.size);
        }

        [MenuItem("CML/Environment/Apply Cliff Reference Match to Root Terrain")]
        public static void ApplyToStandaloneTerrainFromCommandLine()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            TerrainLayer cliffLayer =
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (material == null || cliffLayer == null)
            {
                throw new InvalidOperationException(
                    "Reference-match material or cliff layer is missing.");
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sceneAbsolutePath = Path.Combine(
                projectRoot,
                TargetScenePath.Replace('/', Path.DirectorySeparatorChar));
            string backupPath = Path.Combine(
                Path.GetTempPath(),
                "CML_91_before_cliff_reference_match_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");
            File.Copy(sceneAbsolutePath, backupPath, true);

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != TargetScenePath)
            {
                throw new InvalidOperationException(
                    "Safety check failed: the active scene is not " + TargetScenePath);
            }
            GameObject targetObject = null;
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == "Terrain")
                {
                    targetObject = rootObject;
                    break;
                }
            }

            Terrain targetTerrain =
                targetObject != null ? targetObject.GetComponent<Terrain>() : null;
            if (targetTerrain == null || targetTerrain.terrainData == null)
            {
                throw new InvalidOperationException(
                    "The root Terrain in the Starter Island review scene was not found.");
            }

            TerrainData data = targetTerrain.terrainData;
            string terrainDataPath = AssetDatabase.GetAssetPath(data);
            if (terrainDataPath != "Assets/New Terrain 3.asset")
            {
                throw new InvalidOperationException(
                    "Safety check failed: the target Terrain uses unexpected data: " +
                    terrainDataPath);
            }

            TerrainLayer[] layers = data.terrainLayers;
            if (layers == null || layers.Length < 4)
            {
                TerrainLayer grassSun = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    "Assets/_Project/Art/Environment/StarterIsland/Terrain/Layers/" +
                    "TL_StarterIsland_GrassSun.terrainlayer");
                TerrainLayer grassDeep = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    "Assets/_Project/Art/Environment/StarterIsland/Terrain/Layers/" +
                    "TL_StarterIsland_GrassDeep.terrainlayer");
                TerrainLayer dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    "Assets/_Project/Art/Environment/StarterIsland/Terrain/Layers/" +
                    "TL_StarterIsland_DirtPath.terrainlayer");
                if (grassSun == null || grassDeep == null || dirt == null)
                {
                    throw new InvalidOperationException(
                        "The Starter Island base terrain layers are missing.");
                }

                layers = new[] { grassSun, grassDeep, dirt, cliffLayer };
            }
            else
            {
                layers = (TerrainLayer[])layers.Clone();
                layers[3] = cliffLayer;
            }

            data.terrainLayers = layers;
            material.SetVector(
                "_TerrainSizeXZ",
                new Vector4(data.size.x, data.size.z, 0f, 0f));
            targetTerrain.materialTemplate = material;

            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(targetTerrain);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException("Unity could not save the target scene.");
            }

            Debug.Log(
                "Applied cliff reference-match material only to root Terrain. " +
                "TerrainData=" + terrainDataPath + "; backup=" + backupPath);
        }

        private static void ConfigureTextureImporter()
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(
                    TexturePath,
                    ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            }

            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Unable to configure texture importer: " + TexturePath);
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static Material CreateOrUpdateMaterial(Shader shader)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_Terrain_ReferenceMatch_v1"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetVector("_TerrainSizeXZ", new Vector4(120f, 80f, 0f, 0f));
            material.SetFloat("_AmbientStrength", 0.76f);
            material.SetFloat("_DirectStrength", 0.70f);
            material.SetFloat("_ShadowFloor", 0.50f);
            material.SetFloat("_CliffSlopeStart", 0.22f);
            material.SetFloat("_CliffSlopeEnd", 0.43f);
            material.SetFloat("_CliffProjectionSharpness", 3.5f);
            material.SetFloat("_CliffBrightness", 0.97f);
            material.SetColor("_CliffTint", Color.white);
            material.SetColor(
                "_LipColor",
                new Color(0.32f, 0.35f, 0.075f, 1f));
            material.SetFloat("_LipStrength", 0.13f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static TerrainLayer CreateOrUpdateCliffLayer(Texture2D cliffTexture)
        {
            TerrainLayer layer =
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                layer.name = "TL_StarterIsland_CliffPeach_ReferenceMatch_v1";
                AssetDatabase.CreateAsset(layer, LayerPath);
            }

            layer.diffuseTexture = cliffTexture;
            layer.normalMapTexture = null;
            layer.maskMapTexture = null;
            layer.tileSize = new Vector2(22f, 22f);
            layer.tileOffset = Vector2.zero;
            layer.metallic = 0f;
            layer.smoothness = 0.01f;
            layer.specular = Color.black;
            layer.normalScale = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static void RenderPreview(Material material, TerrainLayer cliffLayer)
        {
            TerrainData terrainData = null;
            GameObject terrainObject = null;
            GameObject lightObject = null;
            GameObject cameraObject = null;
            RenderTexture renderTexture = null;
            Texture2D readback = null;

            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientLight = RenderSettings.ambientLight;
            bool previousFog = RenderSettings.fog;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                terrainData = CreatePreviewTerrainData(cliffLayer);
                terrainObject = Terrain.CreateTerrainGameObject(terrainData);
                terrainObject.name = "TEMP_CliffReferenceMatchTerrain";
                Terrain terrain = terrainObject.GetComponent<Terrain>();
                terrain.materialTemplate = material;
                terrain.drawInstanced = true;
                terrain.basemapDistance = 1000f;
                terrain.heightmapPixelError = 1f;
                terrain.shadowCastingMode = ShadowCastingMode.On;

                lightObject = new GameObject("TEMP_CliffReferenceMatchLight");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.92f, 0.82f, 1f);
                light.intensity = 1.05f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.35f;
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.46f, 0.55f, 0.56f, 1f);
                RenderSettings.fog = false;

                cameraObject = new GameObject("TEMP_CliffReferenceMatchCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.56f, 0.78f, 0.82f, 1f);
                camera.fieldOfView = 35f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 300f;
                camera.allowHDR = false;
                camera.allowMSAA = true;
                camera.transform.position = new Vector3(60f, 25f, -32f);
                camera.transform.LookAt(new Vector3(60f, 15f, 43f));

                renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 4,
                    name = "TEMP_CliffReferenceMatchRT"
                };
                renderTexture.Create();
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                readback = new Texture2D(1280, 720, TextureFormat.RGB24, false, false);
                readback.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                readback.Apply(false, false);

                string absolutePath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", PreviewPath));
                File.WriteAllBytes(absolutePath, readback.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.fog = previousFog;

                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
                if (lightObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(lightObject);
                }
                if (terrainObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(terrainObject);
                }
                if (terrainData != null)
                {
                    UnityEngine.Object.DestroyImmediate(terrainData);
                }
                if (readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
            }
        }

        private static TerrainData CreatePreviewTerrainData(TerrainLayer cliffLayer)
        {
            const int heightResolution = 257;
            const int alphaResolution = 128;
            const float terrainWidth = 120f;
            const float terrainDepth = 80f;
            const float terrainHeight = 35f;

            TerrainLayer grassSun = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/_Project/Art/Environment/StarterIsland/Terrain/Layers/" +
                "TL_StarterIsland_GrassSun.terrainlayer");
            TerrainLayer grassDeep = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/_Project/Art/Environment/StarterIsland/Terrain/Layers/" +
                "TL_StarterIsland_GrassDeep.terrainlayer");
            TerrainLayer dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/_Project/Art/Environment/StarterIsland/Terrain/Layers/" +
                "TL_StarterIsland_DirtPath.terrainlayer");
            if (grassSun == null || grassDeep == null || dirt == null)
            {
                throw new InvalidOperationException(
                    "Starter Island terrain layers required by the isolated preview are missing.");
            }

            TerrainData data = new TerrainData
            {
                heightmapResolution = heightResolution,
                alphamapResolution = alphaResolution,
                baseMapResolution = 256,
                size = new Vector3(terrainWidth, terrainHeight, terrainDepth),
                terrainLayers = new[] { grassSun, grassDeep, dirt, cliffLayer }
            };

            float[,] heights = new float[heightResolution, heightResolution];
            for (int z = 0; z < heightResolution; z++)
            {
                float nz = z / (float)(heightResolution - 1);
                for (int x = 0; x < heightResolution; x++)
                {
                    float nx = x / (float)(heightResolution - 1);
                    float boundary =
                        0.52f +
                        Mathf.Sin(nx * Mathf.PI * 2.0f) * 0.018f +
                        Mathf.Sin(nx * Mathf.PI * 5.0f + 0.7f) * 0.007f;
                    float cliff = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(boundary - 0.017f, boundary + 0.017f, nz));
                    float plateauRoll =
                        Mathf.Sin(nx * Mathf.PI * 2.2f + 0.5f) *
                        Mathf.Sin(nz * Mathf.PI * 1.7f) * 0.65f;
                    float foregroundRoll =
                        Mathf.Sin(nx * Mathf.PI * 1.5f) *
                        Mathf.Sin(nz * Mathf.PI * 2.0f + 0.3f) * 0.22f;
                    float worldHeight =
                        3.0f +
                        cliff * (23.0f + plateauRoll) +
                        foregroundRoll * (1.0f - cliff);
                    heights[z, x] = Mathf.Clamp01(worldHeight / terrainHeight);
                }
            }
            data.SetHeights(0, 0, heights);

            float[,,] splats = new float[alphaResolution, alphaResolution, 4];
            for (int z = 0; z < alphaResolution; z++)
            {
                for (int x = 0; x < alphaResolution; x++)
                {
                    splats[z, x, 0] = 1f;
                }
            }
            data.SetAlphamaps(0, 0, splats);
            return data;
        }
    }
}

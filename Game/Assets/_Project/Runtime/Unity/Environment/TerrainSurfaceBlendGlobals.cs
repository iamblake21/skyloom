using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Unity.World
{
    /// <summary>
    /// Publishes the active Unity Terrain alphamap to environment materials.
    /// Decorative meshes can therefore inherit the real grass, dirt or cliff
    /// substrate at their contact edge without a second terrain simulation.
    /// </summary>
    public static class TerrainSurfaceBlendGlobals
    {
        private const int SupportedLayerCount = 4;

        private static readonly int ControlId =
            Shader.PropertyToID("_CMLTerrainBlendControl");
        private static readonly int OriginInvSizeId =
            Shader.PropertyToID("_CMLTerrainBlendOriginInvSize");
        private static readonly int EnabledId =
            Shader.PropertyToID("_CMLTerrainBlendEnabled");
        private static readonly int[] LayerTextureIds =
        {
            Shader.PropertyToID("_CMLTerrainBlendLayer0"),
            Shader.PropertyToID("_CMLTerrainBlendLayer1"),
            Shader.PropertyToID("_CMLTerrainBlendLayer2"),
            Shader.PropertyToID("_CMLTerrainBlendLayer3")
        };
        private static readonly int[] LayerStIds =
        {
            Shader.PropertyToID("_CMLTerrainBlendLayer0_ST"),
            Shader.PropertyToID("_CMLTerrainBlendLayer1_ST"),
            Shader.PropertyToID("_CMLTerrainBlendLayer2_ST"),
            Shader.PropertyToID("_CMLTerrainBlendLayer3_ST")
        };
        private static readonly int[] LayerRemapMinIds =
        {
            Shader.PropertyToID("_CMLTerrainBlendLayer0_RemapMin"),
            Shader.PropertyToID("_CMLTerrainBlendLayer1_RemapMin"),
            Shader.PropertyToID("_CMLTerrainBlendLayer2_RemapMin"),
            Shader.PropertyToID("_CMLTerrainBlendLayer3_RemapMin")
        };
        private static readonly int[] LayerRemapScaleIds =
        {
            Shader.PropertyToID("_CMLTerrainBlendLayer0_RemapScale"),
            Shader.PropertyToID("_CMLTerrainBlendLayer1_RemapScale"),
            Shader.PropertyToID("_CMLTerrainBlendLayer2_RemapScale"),
            Shader.PropertyToID("_CMLTerrainBlendLayer3_RemapScale")
        };

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterSceneBinding()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BindAfterInitialSceneLoad()
        {
            BindActiveTerrain();
        }

        public static void BindActiveTerrain()
        {
            var terrains = Terrain.activeTerrains;
            Terrain terrain = null;
            for (var index = 0; index < terrains.Length; index++)
            {
                var candidate = terrains[index];
                if (candidate == null ||
                    !candidate.isActiveAndEnabled ||
                    candidate.terrainData == null)
                {
                    continue;
                }

                terrain = candidate;
                if (candidate.name == "TerrainTop")
                {
                    break;
                }
            }

            if (terrain == null)
            {
                Shader.SetGlobalFloat(EnabledId, 0f);
                return;
            }

            BindTerrain(terrain);
        }

        public static void BindTerrain(Terrain terrain)
        {
            if (terrain == null ||
                !terrain.isActiveAndEnabled ||
                terrain.terrainData == null)
            {
                Shader.SetGlobalFloat(EnabledId, 0f);
                return;
            }

            var data = terrain.terrainData;
            if (data.alphamapTextureCount <= 0)
            {
                Shader.SetGlobalFloat(EnabledId, 0f);
                return;
            }

            var control = data.GetAlphamapTexture(0);
            var origin = terrain.transform.position;
            var size = data.size;
            Shader.SetGlobalTexture(ControlId, control);
            Shader.SetGlobalVector(
                OriginInvSizeId,
                new Vector4(
                    origin.x,
                    origin.z,
                    1f / Mathf.Max(size.x, 0.0001f),
                    1f / Mathf.Max(size.z, 0.0001f)));
            BindTerrainLayers(data.terrainLayers);
            Shader.SetGlobalFloat(EnabledId, 1f);
        }

        private static void BindTerrainLayers(TerrainLayer[] layers)
        {
            for (var index = 0; index < SupportedLayerCount; index++)
            {
                var layer = layers != null && index < layers.Length
                    ? layers[index]
                    : null;
                var texture = layer != null
                    ? layer.diffuseTexture
                    : null;
                Shader.SetGlobalTexture(
                    LayerTextureIds[index],
                    texture != null ? texture : Texture2D.grayTexture);

                var tileSize = layer != null
                    ? layer.tileSize
                    : Vector2.one;
                var tileOffset = layer != null
                    ? layer.tileOffset
                    : Vector2.zero;
                var inverseTileX = 1f / Mathf.Max(
                    Mathf.Abs(tileSize.x),
                    0.0001f);
                var inverseTileY = 1f / Mathf.Max(
                    Mathf.Abs(tileSize.y),
                    0.0001f);
                Shader.SetGlobalVector(
                    LayerStIds[index],
                    new Vector4(
                        inverseTileX,
                        inverseTileY,
                        tileOffset.x * inverseTileX,
                        tileOffset.y * inverseTileY));

                var remapMinimum = layer != null
                    ? layer.diffuseRemapMin
                    : Vector4.zero;
                var remapMaximum = layer != null
                    ? layer.diffuseRemapMax
                    : Vector4.one;
                Shader.SetGlobalVector(
                    LayerRemapMinIds[index],
                    remapMinimum);
                Shader.SetGlobalVector(
                    LayerRemapScaleIds[index],
                    remapMaximum - remapMinimum);
            }
        }

        private static void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            BindActiveTerrain();
        }
    }
}

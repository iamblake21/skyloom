using System;
using System.IO;
using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Editor.Art
{
    /// <summary>
    /// Photographs the Iron Deposit where it actually lives, in the Starter
    /// Island scene, with the production materials and lighting.
    ///
    /// Blender previews approximate the runtime shader; only this capture shows
    /// the deposit against the island's own rocks and grass, which is the only
    /// comparison that decides whether the kit is coherent. The scene is opened
    /// read-only and never saved.
    /// </summary>
    public static class OreDepositSceneCapture
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";
        private const string OutputRoot = "Artifacts/Reviews/OreDeposit";
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("CML/Art/Capture Ore Deposit In Scene")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException(
                    $"Could not open {ScenePath}.");
            }

            var deposit = GameObject.Find("MINE_IronDeposit");
            if (deposit == null)
            {
                throw new InvalidOperationException(
                    "The scene has no MINE_IronDeposit. Rebuild the mining " +
                    "sources first.");
            }

            var terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            if (terrain == null)
            {
                throw new InvalidOperationException(
                    "The scene has no Terrain.");
            }

            // Without this the rocks cannot read the terrain alphamap and the
            // contact blend at their base stays off, which is exactly the
            // detail under review.
            TerrainSurfaceBlendGlobals.BindTerrain(terrain);

            var bounds = RendererBounds(deposit);
            var directory = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                OutputRoot));
            Directory.CreateDirectory(directory);

            var reference = SpawnIslandRockBeside(deposit, terrain, bounds);
            var cameraObject = new GameObject("CAPTURE_OreDeposit");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.fieldOfView = 62f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 900f;

                Capture(
                    camera,
                    bounds.center + new Vector3(0f, 1.75f, -1f) * 1f
                        - new Vector3(0f, 0f, bounds.extents.z + 7.5f),
                    bounds.center + Vector3.up * 0.4f,
                    Path.Combine(directory, "ore_deposit_in_scene_wide.png"));

                Capture(
                    camera,
                    bounds.center
                        + new Vector3(1.6f, 1.55f, -(bounds.extents.z + 2.6f)),
                    bounds.center + new Vector3(0.6f, 0.55f, 0f),
                    Path.Combine(directory, "ore_deposit_in_scene_close.png"));

                Capture(
                    camera,
                    bounds.center + new Vector3(-0.4f, 1.62f, -2.4f),
                    bounds.center + new Vector3(0.1f, -0.55f, 0.6f),
                    Path.Combine(directory, "ore_deposit_in_scene_floor.png"));

                // Side by side with an untouched island boulder: whatever the
                // lighting of the day does, both stones must do the same.
                var middle = (reference.transform.position + bounds.center) * 0.5f;
                Capture(
                    camera,
                    middle + new Vector3(0.4f, 1.70f, -6.4f),
                    middle + new Vector3(0f, 0.20f, 0f),
                    Path.Combine(
                        directory,
                        "ore_deposit_beside_island_rock.png"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                if (reference != null)
                {
                    UnityEngine.Object.DestroyImmediate(reference);
                }
            }

            Debug.Log(
                $"ORE_DEPOSIT_SCENE_CAPTURE directory={directory} shots=3 " +
                "status=PASS");
        }

        private static void Capture(
            Camera camera,
            Vector3 position,
            Vector3 target,
            string path)
        {
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(target - position, Vector3.up));

            var renderTexture = RenderTexture.GetTemporary(
                Width,
                Height,
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
                RenderTexture.active = renderTexture;
                var image = new Texture2D(
                    Width,
                    Height,
                    TextureFormat.RGB24,
                    false,
                    false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static GameObject SpawnIslandRockBeside(
            GameObject deposit,
            Terrain terrain,
            Bounds bounds)
        {
            const string modelPath =
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderMedium_A.fbx";
            const string materialPath =
                "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
                "Materials/M_StarterIsland_DetailRock.mat";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (model == null || material == null)
            {
                throw new InvalidOperationException(
                    "The Starter Island rock kit is missing; the comparison " +
                    "shot cannot be produced.");
            }

            var instance =
                UnityEngine.Object.Instantiate(model, deposit.transform.parent);
            instance.name = "CAPTURE_IslandRockReference";
            // Standing inside the deposit, not beside it: same sun, same
            // shadow, same probe. Any remaining difference is the material.
            var position = new Vector3(
                bounds.center.x - 0.9f,
                0f,
                bounds.center.z - bounds.extents.z - 0.5f);
            position.y =
                terrain.SampleHeight(position) + terrain.transform.position.y;
            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, 34f, 0f));
            instance.transform.localScale = Vector3.one * 1.35f;
            foreach (var renderer in
                     instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    materials[slot] = material;
                }

                renderer.sharedMaterials = materials;
            }

            return instance;
        }

        private static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{root.name} has no renderers to frame.");
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }
    }
}

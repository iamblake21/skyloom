using System;
using System.Collections.Generic;
using System.IO;
using CML.Editor.Art;
using CML.Editor.Wood;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.UI
{
    /// <summary>
    /// Renders inventory icons from the same prefabs used by the game.
    /// Existing authored icons are never touched by RenderMissingIcons.
    /// </summary>
    public static class ItemIconRenderPipeline
    {
        private const int Resolution = 512;
        internal const string IconRoot =
            "Assets/_Project/Art/UI/Icons";
        private const string InventoryStyleSheet =
            "Assets/_Project/Art/UI/Inventory/InventoryHUD.uss";
        private const string StoneIconFileName = "ICON_Stone.png";
        private const string WoodLogIconFileName = "ICON_WoodLog.png";
        private const string PlantFiberIconFileName = "ICON_PlantFiber.png";
        private const string StickIconFileName = "ICON_Stick.png";
        private const string StoneMaterial =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_DetailRock.mat";
        private const string BeltKit =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/";

        private static readonly IconSource[] Sources =
        {
            new IconSource(
                StoneIconFileName,
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderSmall_A.fbx",
                StoneMaterial,
                validateVisiblePixels: true),
            new IconSource(
                WoodLogIconFileName,
                WoodHarvestAssetSetup.LogPrefabPath,
                validateVisiblePixels: true),
            new IconSource(
                "ICON_BeltStraight.png",
                BeltKit + "PF_Belt_Straight.prefab",
                validateVisiblePixels: true),
            // FactoryBuildController intentionally binds the exported curve
            // meshes in reverse: these paths mirror the actual runtime item.
            new IconSource(
                "ICON_BeltCurve.png",
                BeltKit + "PF_Belt_CurveLeft.prefab",
                validateVisiblePixels: true),
            new IconSource(
                "ICON_BeltCurveLeft.png",
                BeltKit + "PF_Belt_Curve.prefab",
                validateVisiblePixels: true),
            new IconSource(
                "ICON_BeltIncline.png",
                BeltKit + "PF_Belt_Incline.prefab",
                validateVisiblePixels: true),
            new IconSource(
                "ICON_BeltSupport.png",
                BeltKit + "PF_Belt_Support.prefab",
                validateVisiblePixels: true),
            new IconSource(
                "ICON_BeltFunnel.png",
                BeltKit + "PF_Belt_Funnel.prefab",
                validateVisiblePixels: true),
            new IconSource(
                "ICON_BeltDriveUnit.png",
                BeltKit + "PF_Belt_DriveUnit.prefab",
                validateVisiblePixels: true),
            new IconSource(
                "ICON_MechanicalPress.png",
                "Assets/_Project/Art/MechanicalEra/Prefabs/" +
                "PF_MechanicalPress.prefab",
                validateVisiblePixels: true),
            // La Trivella è l'unico prefab piazzabile che vive sotto Resources
            // invece che accanto agli asset d'arte: si costruisce dal nulla su
            // un giacimento e il controller la carica a runtime.
            new IconSource(
                "ICON_MechanicalDrill.png",
                "Assets/_Project/Resources/Machinery/PF_MechanicalDrill.prefab",
                validateVisiblePixels: true),
            // Prodotto dalla raccolta a mani nude. Il prefab dell'item vive
            // accanto agli asset d'arte del kit, come Nastri e Pressa.
            new IconSource(
                PlantFiberIconFileName,
                FiberPlantAssetSetup.ItemPrefabPath,
                validateVisiblePixels: true),
            new IconSource(
                StickIconFileName,
                StickAssetSetup.ItemPrefabPath,
                validateVisiblePixels: true)
        };

        [MenuItem("CML/UI/Render Missing Item Icons")]
        public static void RenderMissingIcons()
        {
            RenderIcons(overwrite: false);
        }

        [MenuItem("CML/UI/Re-render All Generated Item Icons")]
        public static void RenderAllGeneratedIcons()
        {
            RenderIcons(overwrite: true);
        }

        [MenuItem("CML/UI/Re-render Wood Log Icon")]
        public static void RenderWoodLogIcon()
        {
            WoodHarvestAssetSetup.EnsureLogItemAssets();
            Directory.CreateDirectory(IconRoot);
            var source = Sources[1];
            var outputPath = IconRoot + "/" + WoodLogIconFileName;
            RenderOne(source, outputPath);
            if (!IsUsableGeneratedIcon(outputPath))
            {
                throw new InvalidOperationException(
                    "The rendered wood-log icon contains no visible model.");
            }

            ConfigureImporter(outputPath);
            ReimportInventoryStyleSheet();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Wood-log model and inventory icon rebuilt.");
        }

        // Entry point for a headless editor invocation.
        public static void RenderMissingIconsBatch()
        {
            RenderMissingIcons();
        }

        private static void RenderIcons(bool overwrite)
        {
            WoodHarvestAssetSetup.EnsureLogItemAssets();
            Directory.CreateDirectory(IconRoot);
            var rendered = 0;
            var skipped = 0;

            try
            {
                for (var index = 0; index < Sources.Length; index++)
                {
                    var source = Sources[index];
                    var outputPath = IconRoot + "/" + source.FileName;
                    var needsRender =
                        overwrite ||
                        !File.Exists(outputPath) ||
                        (source.ValidateVisiblePixels &&
                         !IsUsableGeneratedIcon(outputPath));
                    if (!needsRender)
                    {
                        // An icon can already exist on disk while still being
                        // imported as a generic Texture2D. In that state USS
                        // cannot reliably resolve it as an inventory image and
                        // UI Toolkit displays its yellow missing-asset marker.
                        // Always enforce the importer contract, even when the
                        // expensive render itself is skipped.
                        ConfigureImporter(outputPath);
                        skipped++;
                        continue;
                    }

                    RenderOne(source, outputPath);
                    ConfigureImporter(outputPath);
                    rendered++;
                }

                ReimportInventoryStyleSheet();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    $"Item icons ready: {rendered} rendered, " +
                    $"{skipped} existing icons preserved.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static void RenderOne(
            IconSource source,
            string outputPath)
        {
            var preview = new PreviewRenderUtility();
            GameObject subject = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    source.PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Missing icon source prefab: {source.PrefabPath}");
                }

                subject = UnityEngine.Object.Instantiate(prefab);
                if (subject == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate icon source: " +
                        source.PrefabPath);
                }

                subject.name = "IconSubject";
                subject.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                SetLayerRecursively(subject, 0);
                ApplyMaterialOverride(subject, source.MaterialOverridePath);
                preview.AddSingleGO(subject);
                var bounds = CalculateBounds(subject);
                ConfigurePreview(preview, bounds);

                Texture2D previewTexture = null;
                Texture2D texture = null;
                try
                {
                    preview.BeginStaticPreview(
                        new Rect(0f, 0f, Resolution, Resolution));
                    preview.camera.Render();
                    previewTexture = preview.EndStaticPreview();
                    if (previewTexture == null)
                    {
                        throw new InvalidOperationException(
                            $"Unity returned no preview for {source.FileName}.");
                    }

                    // PreviewRenderUtility can return RGB24. Alpha edits on
                    // that texture are silently discarded, leaving a black
                    // square in UI Toolkit. Copy into a known RGBA texture
                    // before removing the border-connected background.
                    texture = new Texture2D(
                        previewTexture.width,
                        previewTexture.height,
                        TextureFormat.RGBA32,
                        mipChain: false,
                        linear: false);
                    texture.SetPixels32(previewTexture.GetPixels32());
                    texture.Apply(
                        updateMipmaps: false,
                        makeNoLongerReadable: false);
                    MakeBorderBackgroundTransparent(texture);
                    File.WriteAllBytes(outputPath, texture.EncodeToPNG());
                }
                finally
                {
                    if (texture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }

                    if (previewTexture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(previewTexture);
                    }
                }
            }
            finally
            {
                preview.Cleanup();
            }
        }

        /// <summary>
        /// PreviewRenderUtility can return an opaque black clear colour under
        /// URP even though its camera was cleared with alpha zero. Remove only
        /// the dark region connected to the image border; dark details enclosed
        /// by the model remain intact.
        /// </summary>
        private static void MakeBorderBackgroundTransparent(
            Texture2D texture)
        {
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels32();
            var background = new bool[pixels.Length];
            var pending = new Queue<int>();

            void EnqueueIfBackground(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    return;
                }

                var index = y * width + x;
                if (background[index])
                {
                    return;
                }

                var pixel = pixels[index];
                if (Mathf.Max(pixel.r, pixel.g, pixel.b) > 20)
                {
                    return;
                }

                background[index] = true;
                pending.Enqueue(index);
            }

            for (var x = 0; x < width; x++)
            {
                EnqueueIfBackground(x, 0);
                EnqueueIfBackground(x, height - 1);
            }

            for (var y = 1; y < height - 1; y++)
            {
                EnqueueIfBackground(0, y);
                EnqueueIfBackground(width - 1, y);
            }

            while (pending.Count > 0)
            {
                var index = pending.Dequeue();
                var x = index % width;
                var y = index / width;
                EnqueueIfBackground(x - 1, y);
                EnqueueIfBackground(x + 1, y);
                EnqueueIfBackground(x, y - 1);
                EnqueueIfBackground(x, y + 1);
            }

            for (var index = 0; index < pixels.Length; index++)
            {
                if (!background[index])
                {
                    continue;
                }

                var pixel = pixels[index];
                pixel.a = 0;
                pixels[index] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private static void ConfigurePreview(
            PreviewRenderUtility preview,
            Bounds bounds)
        {
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;

            var rotation = Quaternion.Euler(24f, -38f, 0f);
            camera.transform.rotation = rotation;
            var largestExtent = Mathf.Max(
                bounds.extents.x,
                bounds.extents.y,
                bounds.extents.z);
            camera.transform.position =
                bounds.center - camera.transform.forward *
                (largestExtent * 4f + 2f);

            var corners = BoundsCorners(bounds);
            var halfWidth = 0f;
            var halfHeight = 0f;
            for (var index = 0; index < corners.Count; index++)
            {
                var local = camera.transform.InverseTransformPoint(
                    corners[index]);
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
            }

            camera.orthographicSize =
                Mathf.Max(halfHeight, halfWidth) * 1.18f;
            preview.ambientColor = new Color(0.34f, 0.38f, 0.42f, 1f);
            preview.lights[0].transform.rotation =
                Quaternion.Euler(42f, -32f, 0f);
            preview.lights[0].color =
                new Color(1f, 0.91f, 0.76f);
            preview.lights[0].intensity = 1.35f;
            preview.lights[0].shadows = LightShadows.Soft;
            preview.lights[1].transform.rotation =
                Quaternion.Euler(325f, 138f, 0f);
            preview.lights[1].color =
                new Color(0.56f, 0.72f, 1f);
            preview.lights[1].intensity = 0.65f;
            preview.lights[1].shadows = LightShadows.None;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(
                includeInactive: true);
            var found = false;
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            for (var index = 0; index < renderers.Length; index++)
            {
                if (!renderers[index].enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderers[index].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
            }

            if (!found || bounds.size.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException(
                    $"{root.name} has no renderable geometry.");
            }

            return bounds;
        }

        private static IReadOnlyList<Vector3> BoundsCorners(Bounds bounds)
        {
            var minimum = bounds.min;
            var maximum = bounds.max;
            return new[]
            {
                new Vector3(minimum.x, minimum.y, minimum.z),
                new Vector3(maximum.x, minimum.y, minimum.z),
                new Vector3(minimum.x, maximum.y, minimum.z),
                new Vector3(maximum.x, maximum.y, minimum.z),
                new Vector3(minimum.x, minimum.y, maximum.z),
                new Vector3(maximum.x, minimum.y, maximum.z),
                new Vector3(minimum.x, maximum.y, maximum.z),
                new Vector3(maximum.x, maximum.y, maximum.z)
            };
        }

        private static void ConfigureImporter(string assetPath)
        {
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(assetPath)
                as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Could not configure generated icon: {assetPath}");
            }

            ApplyImporterContract(importer);
            importer.SaveAndReimport();
        }

        internal static void ApplyImporterContract(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = Resolution;
        }

        private static void ApplyMaterialOverride(
            GameObject root,
            string materialPath)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
            {
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                materialPath);
            if (material == null)
            {
                throw new InvalidOperationException(
                    $"Missing icon material override: {materialPath}");
            }

            var renderers =
                root.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var materialCount =
                    Mathf.Max(1, renderer.sharedMaterials.Length);
                var materials = new Material[materialCount];
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    materials[materialIndex] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static bool IsUsableGeneratedIcon(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return false;
            }

            Texture2D texture = null;
            try
            {
                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    mipChain: false);
                if (!texture.LoadImage(
                        File.ReadAllBytes(assetPath),
                        markNonReadable: false))
                {
                    return false;
                }

                var pixels = texture.GetPixels32();
                var stride = Mathf.Max(1, pixels.Length / 4096);
                var hasVisibleColor = false;
                var hasTransparentBackground = false;
                for (var index = 0; index < pixels.Length; index += stride)
                {
                    var pixel = pixels[index];
                    if (pixel.a < 240)
                    {
                        hasTransparentBackground = true;
                    }

                    if (pixel.a > 16 &&
                        (pixel.r > 12 || pixel.g > 12 || pixel.b > 12))
                    {
                        hasVisibleColor = true;
                    }

                    if (hasVisibleColor && hasTransparentBackground)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static void ReimportInventoryStyleSheet()
        {
            AssetDatabase.ImportAsset(
                InventoryStyleSheet,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static void SetLayerRecursively(
            GameObject gameObject,
            int layer)
        {
            gameObject.layer = layer;
            for (var index = 0;
                 index < gameObject.transform.childCount;
                 index++)
            {
                SetLayerRecursively(
                    gameObject.transform.GetChild(index).gameObject,
                    layer);
            }
        }

        private readonly struct IconSource
        {
            public IconSource(
                string fileName,
                string prefabPath,
                string materialOverridePath = null,
                bool validateVisiblePixels = false)
            {
                FileName = fileName;
                PrefabPath = prefabPath;
                MaterialOverridePath = materialOverridePath;
                ValidateVisiblePixels = validateVisiblePixels;
            }

            public string FileName { get; }

            public string PrefabPath { get; }

            public string MaterialOverridePath { get; }

            public bool ValidateVisiblePixels { get; }
        }
    }

    /// <summary>
    /// Keeps every inventory render importable by UI Toolkit, including icons
    /// authored outside Unity. This prevents a newly copied PNG from silently
    /// becoming the yellow missing-image marker merely because the render menu
    /// was not run afterwards.
    /// </summary>
    internal sealed class ItemIconImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(
                    ItemIconRenderPipeline.IconRoot + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (assetImporter is TextureImporter importer)
            {
                ItemIconRenderPipeline.ApplyImporterContract(importer);
            }
        }
    }
}

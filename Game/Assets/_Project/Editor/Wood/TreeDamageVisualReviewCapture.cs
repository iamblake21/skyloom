using System;
using System.IO;
using System.Linq;
using CML.Unity.Wood;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Renders the production birch-like tree with one real runtime wound from
    /// three useful review angles. The additive scene is temporary and never
    /// writes to Starter Island or to the production prefab.
    /// </summary>
    public static class TreeDamageVisualReviewCapture
    {
        private const string TreePrefabPath =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Trees/" +
            "Prefabs/PF_ENV_Tree_CloudTall_B_LOD0.prefab";
        private const string OutputRelativePath =
            "Artifacts/Reviews/Wood/TreeDamageReview.png";
        private const int PanelWidth = 640;
        private const int PanelHeight = 900;
        private const int Gutter = 12;
        private const int ReviewLayer = 31;

        [MenuItem("CML/Wood/Render Tree Damage Review")]
        public static void Run()
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(TreePrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing production tree prefab: {TreePrefabPath}");
            }

            var previousActiveScene = SceneManager.GetActiveScene();
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbientLight = RenderSettings.ambientLight;
            var previousFog = RenderSettings.fog;
            var previousSun = RenderSettings.sun;
            var replacesEmptyUntitled =
                string.IsNullOrEmpty(previousActiveScene.path) &&
                previousActiveScene.rootCount == 0 &&
                !previousActiveScene.isDirty;
            if (string.IsNullOrEmpty(previousActiveScene.path) &&
                !replacesEmptyUntitled)
            {
                throw new InvalidOperationException(
                    "Save the current untitled scene before rendering the " +
                    "tree-damage review; its unsaved contents will not be " +
                    "replaced.");
            }

            // RenderSettings are active-scene scoped. In normal editor use we
            // add a clean unsaved scene. Batch mode starts from an empty,
            // untitled scene that Unity refuses to keep beside another
            // untitled scene, so only that known-empty scene is replaced.
            var previewScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replacesEmptyUntitled
                    ? NewSceneMode.Single
                    : NewSceneMode.Additive);
            Material groundMaterial = null;
            try
            {
                if (SceneManager.GetActiveScene() != previewScene &&
                    !SceneManager.SetActiveScene(previewScene))
                {
                    throw new InvalidOperationException(
                        "Could not activate the isolated review scene.");
                }

                var tree = PrefabUtility.InstantiatePrefab(
                    prefab,
                    previewScene) as GameObject;
                if (tree == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the production review tree.");
                }

                tree.name = "REVIEW_Tree_WithRuntimeHit";
                tree.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                tree.transform.localScale = Vector3.one * 1.6f;
                var trunkRenderer = tree
                    .GetComponentsInChildren<MeshRenderer>(true)
                    .FirstOrDefault(renderer =>
                        renderer.name.IndexOf(
                            "Trunk",
                            StringComparison.OrdinalIgnoreCase) >= 0);
                if (trunkRenderer == null)
                {
                    throw new InvalidOperationException(
                        "Production tree has no trunk renderer.");
                }

                var trunkCollider =
                    trunkRenderer.GetComponent<MeshCollider>();
                if (trunkCollider == null ||
                    trunkCollider.sharedMesh == null)
                {
                    throw new InvalidOperationException(
                        "Production trunk has no exact MeshCollider.");
                }

                Debug.Log(
                    "TREE_MESH_METRICS " +
                    $"vertices={trunkCollider.sharedMesh.vertexCount} " +
                    $"triangles={trunkCollider.sharedMesh.triangles.Length / 3} " +
                    $"subMeshes={trunkCollider.sharedMesh.subMeshCount} " +
                    $"readable={trunkCollider.sharedMesh.isReadable}");

                var identity =
                    tree.AddComponent<FellableTreeIdentity>();
                identity.Configure("review.tree.runtime-wound");
                Physics.SyncTransforms();
                if (!TryHitLowerTrunk(
                        trunkCollider,
                        trunkRenderer.bounds,
                        out var hit,
                        out var rayOrigin))
                {
                    throw new InvalidOperationException(
                        "Could not acquire a review hit on the lower trunk.");
                }

                SetLayerRecursively(tree, ReviewLayer);

                var treeBounds = EncapsulateRenderers(tree);
                groundMaterial = CreateGround(
                    previewScene,
                    treeBounds);
                CreateLighting(previewScene, hit.normal);
                var camera = CreateCamera(previewScene);
                var beforeClose = RenderCloseup(
                    camera,
                    hit.point,
                    hit.normal,
                    0f);

                if (!identity.RegisterImpact(
                        hit,
                        hit.point - rayOrigin))
                {
                    throw new InvalidOperationException(
                        "The runtime tree rejected its review impact.");
                }

                var deformedMesh = trunkRenderer
                    .GetComponent<MeshFilter>()
                    .sharedMesh;
                if (deformedMesh == null ||
                    !deformedMesh.name.StartsWith(
                        "MESH_WOOD_VoxelCarvedTrunk_",
                        StringComparison.Ordinal) ||
                    trunkCollider.sharedMesh != deformedMesh)
                {
                    throw new InvalidOperationException(
                        "Runtime tree damage did not deform the visible " +
                        "trunk and its collider together.");
                }

                var runtimeTriangleCount =
                    deformedMesh.triangles.Length / 3;
                if (runtimeTriangleCount > 24000)
                {
                    throw new InvalidOperationException(
                        "Runtime tree damage exceeded its 24k triangle " +
                        $"budget: {runtimeTriangleCount}.");
                }

                var fullTree = RenderFullTree(
                    camera,
                    treeBounds,
                    hit.normal);
                var afterClose = RenderCloseup(
                    camera,
                    hit.point,
                    hit.normal,
                    0f);
                var grazingClose = RenderCloseup(
                    camera,
                    hit.point,
                    hit.normal,
                    58f);
                var outerLightingDelta = MeasureOuterLightingDelta(
                    beforeClose,
                    afterClose);
                if (outerLightingDelta > 0.02f)
                {
                    throw new InvalidOperationException(
                        "Tree damage changed intact-trunk lighting: " +
                        $"outerDelta={outerLightingDelta:F4}.");
                }

                var contactSheet = ComposeContactSheet(
                    fullTree,
                    beforeClose,
                    afterClose,
                    grazingClose);
                try
                {
                    var absoluteOutput = Path.GetFullPath(
                        Path.Combine(
                            Application.dataPath,
                            "..",
                            OutputRelativePath));
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(absoluteOutput) ??
                        throw new InvalidOperationException(
                            "Review output has no directory."));
                    File.WriteAllBytes(
                        absoluteOutput,
                        contactSheet.EncodeToPNG());
                    Debug.Log(
                        "TREE_DAMAGE_VISUAL_REVIEW status=CAPTURED " +
                        $"output={absoluteOutput} " +
                        $"triangles={runtimeTriangleCount} " +
                        $"outerLightingDelta={outerLightingDelta:F4} " +
                        "views=full,before-close,after-close,grazing-close " +
                        "sceneWrites=0 prefabWrites=0");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(contactSheet);
                    UnityEngine.Object.DestroyImmediate(fullTree);
                    UnityEngine.Object.DestroyImmediate(beforeClose);
                    UnityEngine.Object.DestroyImmediate(afterClose);
                    UnityEngine.Object.DestroyImmediate(grazingClose);
                }
            }
            finally
            {
                if (groundMaterial != null)
                {
                    UnityEngine.Object.DestroyImmediate(groundMaterial);
                }

                if (replacesEmptyUntitled)
                {
                    EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Single);
                    RenderSettings.ambientMode = previousAmbientMode;
                    RenderSettings.ambientLight = previousAmbientLight;
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
                        RenderSettings.fog = previousFog;
                        RenderSettings.sun = previousSun;
                    }

                    EditorSceneManager.CloseScene(
                        previewScene,
                        removeScene: true);
                }
            }
        }

        private static bool TryHitLowerTrunk(
            MeshCollider trunkCollider,
            Bounds bounds,
            out RaycastHit hit,
            out Vector3 rayOrigin)
        {
            var hitHeight = bounds.min.y +
                Mathf.Clamp(bounds.size.y * 0.16f, 1.0f, 1.45f);
            var target = new Vector3(
                bounds.center.x,
                hitHeight,
                bounds.center.z);
            var directions = new[]
            {
                Vector3.forward,
                Vector3.back,
                Vector3.right,
                Vector3.left
            };
            var distance = Mathf.Max(
                bounds.size.x,
                bounds.size.z) + 2f;
            for (var index = 0; index < directions.Length; index++)
            {
                var rayDirection = directions[index];
                rayOrigin = target - rayDirection * distance;
                if (trunkCollider.Raycast(
                        new Ray(rayOrigin, rayDirection),
                        out hit,
                        distance * 2f))
                {
                    return true;
                }
            }

            rayOrigin = Vector3.zero;
            hit = default;
            return false;
        }

        private static Bounds EncapsulateRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled)
                .ToArray();
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Review tree has no enabled renderers.");
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static Material CreateGround(
            Scene scene,
            Bounds treeBounds)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "REVIEW_Ground";
            ground.layer = ReviewLayer;
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = new Vector3(
                treeBounds.center.x,
                treeBounds.min.y - 0.018f,
                treeBounds.center.z);
            ground.transform.localScale = Vector3.one *
                Mathf.Max(2f, treeBounds.size.y * 0.22f);
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = "M_REVIEW_Grass"
            };
            SetMaterialColor(
                material,
                new Color(0.39f, 0.53f, 0.20f, 1f));
            material.SetFloat(
                material.HasProperty("_Smoothness")
                    ? "_Smoothness"
                    : "_Glossiness",
                0.02f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }

        private static void CreateLighting(
            Scene scene,
            Vector3 frontNormal)
        {
            var lightObject = new GameObject("REVIEW_KeyLight");
            lightObject.layer = ReviewLayer;
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var up = Vector3.ProjectOnPlane(
                Vector3.up,
                frontNormal).normalized;
            if (up.sqrMagnitude < 0.01f)
            {
                up = Vector3.up;
            }

            var right = Vector3.Cross(up, frontNormal).normalized;
            var rayDirection = (
                -frontNormal - up * 0.48f + right * 0.30f).normalized;
            lightObject.transform.rotation =
                Quaternion.LookRotation(rayDirection, up);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.82f);
            light.intensity = 1.45f;
            light.shadows = LightShadows.Soft;
            light.cullingMask = 1 << ReviewLayer;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight =
                new Color(0.44f, 0.44f, 0.44f);
            RenderSettings.fog = false;
            RenderSettings.sun = light;
        }

        private static Camera CreateCamera(Scene scene)
        {
            var cameraObject = new GameObject("REVIEW_Camera");
            cameraObject.layer = ReviewLayer;
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.cullingMask = 1 << ReviewLayer;
            camera.useOcclusionCulling = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color(0.43f, 0.75f, 0.91f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 200f;
            return camera;
        }

        private static void SetLayerRecursively(
            GameObject root,
            int layer)
        {
            root.layer = layer;
            for (var index = 0;
                 index < root.transform.childCount;
                 index++)
            {
                SetLayerRecursively(
                    root.transform.GetChild(index).gameObject,
                    layer);
            }
        }

        private static Texture2D RenderFullTree(
            Camera camera,
            Bounds bounds,
            Vector3 frontNormal)
        {
            camera.fieldOfView = 31f;
            var direction = Vector3.ProjectOnPlane(
                frontNormal,
                Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector3.back;
            }

            var distance = bounds.extents.y /
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) *
                1.12f;
            camera.transform.position =
                bounds.center + direction * distance;
            camera.transform.rotation = Quaternion.LookRotation(
                bounds.center - camera.transform.position,
                Vector3.up);
            return RenderPanel(camera);
        }

        private static Texture2D RenderCloseup(
            Camera camera,
            Vector3 point,
            Vector3 normal,
            float yaw)
        {
            camera.fieldOfView = yaw == 0f ? 30f : 32f;
            var up = Vector3.ProjectOnPlane(
                Vector3.up,
                normal).normalized;
            if (up.sqrMagnitude < 0.01f)
            {
                up = Vector3.up;
            }

            var viewDirection = Quaternion.AngleAxis(yaw, up) * normal;
            camera.transform.position =
                point + viewDirection * 0.72f + up * 0.010f;
            camera.transform.rotation = Quaternion.LookRotation(
                point - camera.transform.position,
                up);
            return RenderPanel(camera);
        }

        private static Texture2D RenderPanel(Camera camera)
        {
            var renderTexture = RenderTexture.GetTemporary(
                PanelWidth,
                PanelHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var previousActive = RenderTexture.active;
            var texture = new Texture2D(
                PanelWidth,
                PanelHeight,
                TextureFormat.RGBA32,
                false,
                false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(
                    new Rect(0f, 0f, PanelWidth, PanelHeight),
                    0,
                    0,
                    false);
                texture.Apply(false, false);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            return texture;
        }

        private static Texture2D ComposeContactSheet(
            Texture2D fullTree,
            Texture2D beforeClose,
            Texture2D afterClose,
            Texture2D grazingClose)
        {
            var width = PanelWidth * 4 + Gutter * 5;
            var height = PanelHeight + Gutter * 2;
            var sheet = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false);
            var background = Enumerable.Repeat(
                new Color32(24, 30, 34, 255),
                width * height).ToArray();
            sheet.SetPixels32(background);
            sheet.SetPixels32(
                Gutter,
                Gutter,
                PanelWidth,
                PanelHeight,
                fullTree.GetPixels32());
            sheet.SetPixels32(
                Gutter * 2 + PanelWidth,
                Gutter,
                PanelWidth,
                PanelHeight,
                beforeClose.GetPixels32());
            sheet.SetPixels32(
                Gutter * 3 + PanelWidth * 2,
                Gutter,
                PanelWidth,
                PanelHeight,
                afterClose.GetPixels32());
            sheet.SetPixels32(
                Gutter * 4 + PanelWidth * 3,
                Gutter,
                PanelWidth,
                PanelHeight,
                grazingClose.GetPixels32());
            sheet.Apply(false, false);
            return sheet;
        }

        private static float MeasureOuterLightingDelta(
            Texture2D before,
            Texture2D after)
        {
            var beforePixels = before.GetPixels32();
            var afterPixels = after.GetPixels32();
            long channelDelta = 0;
            var pixelCount = 0;
            for (var y = 0; y < PanelHeight; y++)
            {
                var normalizedY =
                    Mathf.Abs((y + 0.5f) / PanelHeight - 0.5f);
                for (var x = 0; x < PanelWidth; x++)
                {
                    var normalizedX =
                        Mathf.Abs((x + 0.5f) / PanelWidth - 0.5f);
                    // The physical wound is centred by RenderCloseup. Ignore
                    // only that compact region, then compare the rest of the
                    // same trunk render pixel-for-pixel.
                    if (normalizedX < 0.20f && normalizedY < 0.20f)
                    {
                        continue;
                    }

                    var index = y * PanelWidth + x;
                    var a = beforePixels[index];
                    var b = afterPixels[index];
                    channelDelta += Mathf.Abs(a.r - b.r);
                    channelDelta += Mathf.Abs(a.g - b.g);
                    channelDelta += Mathf.Abs(a.b - b.b);
                    pixelCount++;
                }
            }

            return pixelCount > 0
                ? channelDelta / (pixelCount * 3f * 255f)
                : 0f;
        }

        private static void SetMaterialColor(
            Material material,
            Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }
    }
}

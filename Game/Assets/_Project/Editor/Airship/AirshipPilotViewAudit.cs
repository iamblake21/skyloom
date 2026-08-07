using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.Airship
{
    /// <summary>
    /// Measures how much of the world a pilot can actually see, from the eye position the
    /// model authors: <c>REF_PilotCamera</c>.
    ///
    /// It does not raycast. Colliders are not what occludes a view, renderers are, and a
    /// raycast cannot tell a solid panel from a pane of glass — which is precisely the
    /// distinction that decides whether a cockpit is usable. Instead the same view is
    /// rendered twice against two different background colours: a pixel that **changes**
    /// between the two renders is showing the outside through something, transparent or
    /// open, and a pixel that stays identical is opaque airship. That classifier is exact,
    /// needs no material introspection, and counts tinted glass as visibility, which is
    /// what a player experiences.
    ///
    /// The result is split into vertical bands, because the complaint being measured is
    /// not "can I see the sky" but "can I tell whether I am about to hit something", and
    /// that lives in the level and downward bands.
    /// </summary>
    public static class AirshipPilotViewAudit
    {
        private const string PrefabPath =
            "Assets/_Project/Art/Vehicles/Airship/Prefabs/PF_Airship.prefab";

        private const string EyeAnchorName = "REF_PilotCamera";

        private const int Width = 960;
        private const int Height = 720;

        /// <summary>
        /// Horizontal field of view. Wide enough to include the frames a pilot turns their
        /// head into, narrow enough that the numbers still describe looking forward.
        /// </summary>
        private const float FieldOfView = 68f;

        [MenuItem("CML/Art/Audit Airship Pilot View")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"PILOT_VIEW_AUDIT status=FAIL reason=missing_prefab path={PrefabPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("PilotViewAuditCamera");
            var outputRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "Artifacts", "Renders", "Airship"));
            Directory.CreateDirectory(outputRoot);

            try
            {
                var eye = FindDeep(instance.transform, EyeAnchorName);
                if (eye == null)
                {
                    Debug.LogError(
                        $"PILOT_VIEW_AUDIT status=FAIL reason=missing_anchor name={EyeAnchorName}");
                    return;
                }

                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = eye.position;
                // Forward is +Z in the authored model, and the pilot faces the nose.
                camera.transform.rotation = instance.transform.rotation;
                camera.fieldOfView = FieldOfView;
                camera.nearClipPlane = 0.02f;
                camera.farClipPlane = 500f;
                camera.clearFlags = CameraClearFlags.SolidColor;

                var first = Capture(camera, new Color(1f, 0f, 0f, 1f));
                var second = Capture(camera, new Color(0f, 1f, 0f, 1f));

                var report = Compare(first, second);
                WritePng(second, Path.Combine(outputRoot, "pilot_view_baseline.png"));

                Debug.Log(report);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(instance);
            }
        }

        private static Texture2D Capture(Camera camera, Color background)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.backgroundColor = background;
            camera.targetTexture = target;

            var capture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                capture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }

            return capture;
        }

        /// <summary>
        /// A pixel that differs between the two backgrounds is showing the outside; one
        /// that is identical is opaque. Bands are measured on the vertical axis of the
        /// image, which maps to the pitch of the look direction.
        /// </summary>
        private static string Compare(Texture2D first, Texture2D second)
        {
            var a = first.GetPixels32();
            var b = second.GetPixels32();

            var bandNames = new[] { "up", "level", "down" };
            var bandOpen = new int[3];
            var bandTotal = new int[3];
            var totalOpen = 0;

            for (var y = 0; y < Height; y++)
            {
                // Row 0 is the bottom of the image, so the lowest third is the down band.
                var band = y < Height / 3 ? 2 : y < (Height * 2) / 3 ? 1 : 0;
                for (var x = 0; x < Width; x++)
                {
                    var index = (y * Width) + x;
                    var isOpen =
                        a[index].r != b[index].r
                        || a[index].g != b[index].g
                        || a[index].b != b[index].b;

                    bandTotal[band]++;
                    if (isOpen)
                    {
                        bandOpen[band]++;
                        totalOpen++;
                    }
                }
            }

            var text = new StringBuilder();
            text.Append("PILOT_VIEW_AUDIT status=PASS");
            text.Append(
                " fov=" + FieldOfView.ToString("0", CultureInfo.InvariantCulture));
            text.Append(" open_total=" + Percent(totalOpen, Width * Height));
            for (var band = 0; band < 3; band++)
            {
                text.Append(
                    " open_" + bandNames[band] + "=" + Percent(bandOpen[band], bandTotal[band]));
            }

            return text.ToString();
        }

        private static string Percent(int part, int whole)
        {
            if (whole == 0)
            {
                return "0.0%";
            }

            return (part * 100f / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static void WritePng(Texture2D capture, string path)
        {
            File.WriteAllBytes(path, capture.EncodeToPNG());
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDeep(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

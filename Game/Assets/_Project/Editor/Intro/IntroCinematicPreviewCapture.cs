using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Editor.Intro
{
    /// <summary>
    /// Diagnostic only. Renders three still frames of the opening scene so the
    /// hand written cinematic shaders can be checked without stepping through
    /// Play Mode. It is not an acceptance criterion and writes nothing into
    /// Assets.
    /// </summary>
    public static class IntroCinematicPreviewCapture
    {
        private const int Width = 1920;
        private const int Height = 1080;

        /// <summary>
        /// Diagnostic renders belong to the repository's outputs folder, next
        /// to the other QA captures, never inside the Unity project.
        /// </summary>
        private static string OutputFolder => Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../outputs/IntroCinematic"));

        [MenuItem("CML/Cinematics/Render Intro Preview Frames")]
        public static void Run()
        {
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.OpenScene(
                IntroCinematicSceneBuilder.ScenePath,
                OpenSceneMode.Single);

            try
            {
                var chase = GameObject.Find("CIN_ChaseCamera");
                var tunnel = GameObject.Find("CIN_WarpTunnel");
                var rift = GameObject.Find("CIN_Rift");
                if (chase == null || tunnel == null || rift == null)
                {
                    throw new FileNotFoundException(
                        "The intro scene has no chase camera, tunnel or rift. "
                        + "Run CML/Cinematics/Rebuild Intro Sequence first.");
                }

                var camera = chase.GetComponent<Camera>();
                var tunnelMaterial = tunnel.GetComponent<Renderer>().sharedMaterial;
                var riftMaterial = rift.GetComponent<Renderer>().sharedMaterial;
                var airship = GameObject.Find("CIN_AirshipActor");
                var pivot = airship != null
                    ? airship.transform.position
                    : Vector3.zero;

                Directory.CreateDirectory(OutputFolder);

                PlaceCamera(camera, pivot, -46f, 31f, 8.6f);
                tunnelMaterial.SetFloat("_Intensity", 0f);
                riftMaterial.SetFloat("_Openness", 0f);
                Capture(camera, "01_deep_space_cruise");

                PlaceCamera(camera, pivot, -3f, 33f, 3.1f);
                tunnelMaterial.SetFloat("_Intensity", 3.4f);
                Capture(camera, "02_hyperspace_jump");

                CaptureCockpitAsteroid(pivot);

                var cockpit = GameObject.Find("CIN_CockpitCamera");
                if (cockpit != null)
                {
                    // Same placement rule the director uses at runtime: the
                    // tear sits on the cockpit's optical axis, not on a fixed
                    // offset in the scene.
                    var eye = cockpit.transform;
                    rift.transform.SetPositionAndRotation(
                        eye.position + eye.forward * 98f,
                        Quaternion.LookRotation(eye.forward, Vector3.up));
                    riftMaterial.SetFloat("_Openness", 1f);
                    tunnelMaterial.SetFloat("_Intensity", 0.9f);
                    var cockpitCamera = cockpit.GetComponent<Camera>();
                    Capture(cockpitCamera, "04_cockpit_rift");
                }

                // The scene is a generated artifact; never persist the poses
                // this preview forced onto it.
                tunnelMaterial.SetFloat("_Intensity", 0f);
                riftMaterial.SetFloat("_Openness", 0f);
                Debug.Log(
                    "CML_INTRO_PREVIEW_READY folder=" + OutputFolder);
            }
            finally
            {
                if (previousSetup != null && previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        /// <summary>
        /// The framing the teaching card freezes on: the seat the player knows,
        /// with a rock filling the windscreen.
        /// </summary>
        private static void CaptureCockpitAsteroid(Vector3 pivot)
        {
            var cockpit = GameObject.Find("CIN_CockpitCamera");

            // The teaching rock ships disabled, so GameObject.Find cannot see
            // it: walk the loaded scene instead.
            var threat = FindInactive("CIN_Asteroid_Threat");
            if (cockpit == null || threat == null)
            {
                return;
            }

            // Particles do not advance outside Play Mode, so the field has to be
            // simulated explicitly. Without this the preview cannot show where
            // the streaks actually end up relative to the hull.
            SimulateStarField();

            var eye = cockpit.transform;
            threat.SetActive(true);
            threat.transform.position = eye.position
                + eye.forward * 130f
                + eye.right * 10f
                + eye.up * 3f;

            Capture(cockpit.GetComponent<Camera>(), "03_cockpit_asteroid");
            threat.SetActive(false);
        }

        /// <summary>
        /// Advances the streak field to the state the director drives it to at
        /// cruise, so a still frame shows the real density and the real
        /// clearance around the hull.
        /// </summary>
        private static void SimulateStarField()
        {
            var host = FindInactive("CIN_StarStreaks");
            var system = host != null
                ? host.GetComponent<ParticleSystem>()
                : null;
            if (system == null)
            {
                return;
            }

            var main = system.main;
            main.startSpeed = 260f;
            main.startLifetime = 0.55f;

            var emission = system.emission;
            emission.rateOverTime = 900f;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.lengthScale = 34f;
            }

            system.Clear(true);
            system.Simulate(2.5f, true, true, false);
        }

        private static GameObject FindInactive(string exactName)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                var descendants =
                    roots[index].GetComponentsInChildren<Transform>(true);
                for (var child = 0; child < descendants.Length; child++)
                {
                    if (descendants[child].name == exactName)
                    {
                        return descendants[child].gameObject;
                    }
                }
            }

            return null;
        }

        private static void PlaceCamera(
            Camera camera,
            Vector3 pivot,
            float orbitDegrees,
            float distance,
            float height)
        {
            var offset = Quaternion.Euler(0f, orbitDegrees, 0f)
                * new Vector3(0f, height, -distance);
            var position = pivot + offset;
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(
                    (pivot + Vector3.up * 1.9f) - position,
                    Vector3.up));
        }

        private static void Capture(Camera camera, string name)
        {
            var wasEnabled = camera.enabled;
            camera.enabled = true;

            var target = new RenderTexture(Width, Height, 24)
            {
                antiAliasing = 2
            };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var capture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                capture.Apply();

                var path = Path.Combine(OutputFolder, name + ".png");
                File.WriteAllBytes(path, capture.EncodeToPNG());
                Debug.Log("CML_INTRO_PREVIEW_FRAME " + path);
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.enabled = wasEnabled;
                Object.DestroyImmediate(capture);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}

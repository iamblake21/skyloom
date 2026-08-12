using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Adds the measured daylight owner to the open Starter Island review
    /// scene.  The scene is marked dirty but deliberately never saved here.
    /// </summary>
    public static class MeasuredStylizedDaylightSetup
    {
        private const string SkyboxPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Materials/" +
            "M_StarterIsland_Skybox.mat";
        [MenuItem("CML/Art/Apply Measured Stylized Daylight to Open Scene")]
        public static void ApplyToOpenScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded)
            {
                Debug.LogWarning("No loaded scene is available for daylight setup.");
                return;
            }

            var sun = FindSun();
            if (sun == null)
            {
                Debug.LogError(
                    "Measured daylight setup could not find a directional light.");
                return;
            }

            var owner = FindDaylightAuthority();
            if (owner == null)
            {
                var gameObject = new GameObject("ENV_MeasuredStylizedDaylight");
                Undo.RegisterCreatedObjectUndo(
                    gameObject,
                    "Create measured stylized daylight");
                owner = Undo.AddComponent<MeasuredStylizedDaylight>(gameObject);
            }

            var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            var volume = FindGlobalVolume();
            Undo.RecordObject(owner, "Configure measured stylized daylight");
            Undo.RecordObject(sun, "Configure measured stylized sun");
            owner.Configure(sun, skybox, volume);
            EditorUtility.SetDirty(owner);
            EditorUtility.SetDirty(sun);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = owner.gameObject;

            Debug.Log(
                "Applied measured stylized daylight to the open scene. " +
                "The scene was marked dirty but was not saved.");
        }

        [MenuItem("CML/Art/Day Night/Preview Dawn (06:00)")]
        private static void PreviewDawn()
        {
            PreviewTime(6f);
        }

        [MenuItem("CML/Art/Day Night/Preview Noon (12:00)")]
        private static void PreviewNoon()
        {
            PreviewTime(12f);
        }

        [MenuItem("CML/Art/Day Night/Preview Sunset (21:30)")]
        private static void PreviewSunset()
        {
            PreviewTime(21.5f);
        }

        [MenuItem("CML/Art/Day Night/Preview Midnight (00:00)")]
        private static void PreviewMidnight()
        {
            PreviewTime(0f);
        }

        private static void PreviewTime(float hour)
        {
            var owner = FindDaylightAuthority();
            if (owner == null)
            {
                ApplyToOpenScene();
                owner = FindDaylightAuthority();
            }

            if (owner == null)
            {
                return;
            }

            Undo.RecordObject(owner, "Preview measured day night time");
            owner.SetTimeOfDay(hour);
            EditorUtility.SetDirty(owner);
            EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
            Selection.activeGameObject = owner.gameObject;
        }

        private static MeasuredStylizedDaylight FindDaylightAuthority()
        {
            var authority = MeasuredStylizedDaylight.ActiveAuthority;
            if (authority != null)
            {
                return authority;
            }

            var controllers = Object.FindObjectsByType<MeasuredStylizedDaylight>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < controllers.Length; index++)
            {
                var candidate = controllers[index];
                if (candidate != null &&
                    candidate.gameObject.name == "ENV_MeasuredStylizedDaylight")
                {
                    return candidate;
                }
            }

            return controllers.Length > 0 ? controllers[0] : null;
        }

        private static Light FindSun()
        {
            if (RenderSettings.sun != null &&
                RenderSettings.sun.type == LightType.Directional)
            {
                return RenderSettings.sun;
            }

            var lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < lights.Length; index++)
            {
                var candidate = lights[index];
                if (candidate != null &&
                    candidate.type == LightType.Directional &&
                    candidate.name == "ENV_Sun")
                {
                    return candidate;
                }
            }

            for (var index = 0; index < lights.Length; index++)
            {
                var candidate = lights[index];
                if (candidate != null && candidate.type == LightType.Directional)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Volume FindGlobalVolume()
        {
            var volumes = Object.FindObjectsByType<Volume>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Volume best = null;
            for (var index = 0; index < volumes.Length; index++)
            {
                var candidate = volumes[index];
                if (candidate == null || !candidate.isGlobal)
                {
                    continue;
                }

                if (best == null || candidate.priority > best.priority)
                {
                    best = candidate;
                }
            }

            return best;
        }
    }
}

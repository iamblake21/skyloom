using System;
using CML.Unity.Presentation.Equipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Editor.Equipment
{
    public static class PickaxeHandsSetupSceneBuilder
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/94_PickaxeHands_Setup.unity";
        private const string PoseAssetPath =
            "Assets/_Project/Resources/Equipment/" +
            "FirstPersonEquipmentPose.asset";
        private const string PickaxePath =
            "Assets/_Project/Resources/Equipment/" +
            "PF_PickaxeCrudeView.prefab";
        [MenuItem("CML/Art/Open Pickaxe Hands Setup Scene")]
        public static void OpenOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existing != null)
            {
                var scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);
                UpgradeExistingScene(scene);
                return;
            }

            BuildScene();
        }

        private static void BuildScene()
        {
            var pose = LoadOrCreatePose();
            var pickaxePrefab = RequireAsset<GameObject>(PickaxePath);

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var cameraObject = new GameObject("SETUP_CAMERA");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.12f, 0.15f, 1f);
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;

            var keyLightObject = new GameObject("SETUP_KEY_LIGHT");
            SceneManager.MoveGameObjectToScene(keyLightObject, scene);
            keyLightObject.transform.rotation =
                Quaternion.Euler(36f, -42f, 0f);
            var keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.93f, 0.82f);
            keyLight.intensity = 1.4f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.39f, 0.46f);

            var root = new GameObject("EDIT_POSE_HERE");
            root.transform.SetParent(cameraObject.transform, false);

            var pickaxe = InstantiateAsset(
                pickaxePrefab,
                root.transform,
                "PICKAXE");
            pose.Pickaxe.ApplyTo(pickaxe.transform);
            ConfigureRenderers(pickaxe);
            RemovePhysics(pickaxe);

            var authoring = root.AddComponent<PickaxeHandsSetupAuthoring>();
            authoring.Configure(pose, pickaxe.transform);

            var instructions = new GameObject(
                "SPOSTA PICKAXE, POI SELEZIONA EDIT_POSE_HERE PER SALVARE");
            instructions.transform.SetParent(root.transform, false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static void UpgradeExistingScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            Transform setupRoot = null;
            for (var index = 0; index < roots.Length; index++)
            {
                setupRoot = FindRecursive(
                    roots[index].transform,
                    "EDIT_POSE_HERE");
                if (setupRoot != null)
                {
                    break;
                }
            }

            if (setupRoot == null)
            {
                throw new InvalidOperationException(
                    "The pickaxe setup scene has no EDIT_POSE_HERE root.");
            }

            DestroyNamedChild(setupRoot, "RIGHT_HAND");
            DestroyNamedChild(setupRoot, "LEFT_HAND");

            var pickaxe = FindRecursive(setupRoot, "PICKAXE");
            if (pickaxe == null)
            {
                throw new InvalidOperationException(
                    "The pickaxe setup scene has no PICKAXE transform.");
            }

            var authoring =
                setupRoot.GetComponent<PickaxeHandsSetupAuthoring>();
            if (authoring == null)
            {
                authoring =
                    setupRoot.gameObject
                        .AddComponent<PickaxeHandsSetupAuthoring>();
            }

            authoring.Configure(LoadOrCreatePose(), pickaxe);

            var legacyInstructions = FindRecursive(
                setupRoot,
                "SELEZIONA EDIT_POSE_HERE PER SALVARE");
            if (legacyInstructions != null)
            {
                legacyInstructions.name =
                    "SPOSTA PICKAXE, POI SELEZIONA " +
                    "EDIT_POSE_HERE PER SALVARE";
            }

            EditorUtility.SetDirty(authoring);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = setupRoot.gameObject;
        }

        private static void DestroyNamedChild(Transform root, string name)
        {
            var target = FindRecursive(root, name);
            if (target != null && target != root)
            {
                UnityEngine.Object.DestroyImmediate(target.gameObject);
            }
        }

        private static FirstPersonEquipmentPose LoadOrCreatePose()
        {
            var pose =
                AssetDatabase.LoadAssetAtPath<FirstPersonEquipmentPose>(
                    PoseAssetPath);
            if (pose != null)
            {
                return pose;
            }

            pose = ScriptableObject.CreateInstance<FirstPersonEquipmentPose>();
            AssetDatabase.CreateAsset(pose, PoseAssetPath);
            return pose;
        }

        private static GameObject InstantiateAsset(
            GameObject source,
            Transform parent,
            string objectName)
        {
            var instance =
                PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(source, parent);
            }

            instance.name = objectName;
            return instance;
        }

        private static void ConfigureRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                renderers[index].shadowCastingMode = ShadowCastingMode.Off;
                renderers[index].receiveShadows = false;
            }
        }

        private static void RemovePhysics(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index]);
            }

            var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (var index = 0; index < rigidbodies.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(rigidbodies[index]);
            }
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindRecursive(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Required setup asset not found at {path}.");
            }

            return asset;
        }

    }
}

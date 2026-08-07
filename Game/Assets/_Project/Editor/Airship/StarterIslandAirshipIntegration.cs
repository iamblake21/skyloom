using System;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using CML.Unity.Presentation.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Replaces only the playable airship instance in the authored Starter
    /// Island scene and reconnects the existing player, camera, HUD and AIR
    /// composition root. It deliberately does not regenerate the island.
    /// </summary>
    public static class StarterIslandAirshipIntegration
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";
        private const string PrefabPath =
            "Assets/_Project/Art/Vehicles/Airship/Prefabs/PF_Airship.prefab";
        private const string AirshipName = "PF_Airship";
        private const string PlayerName = "AIR_FirstPersonPlayer";

        [MenuItem("CML/Art/Integrate Approved Airship In Starter Island")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Approved airship prefab is missing: {PrefabPath}");
            }

            var previous = FindGameObject(scene, AirshipName);
            if (previous == null)
            {
                throw new InvalidOperationException(
                    $"Starter Island has no '{AirshipName}' instance to replace.");
            }

            var parent = previous.transform.parent;
            var position = previous.transform.position;
            var rotation = previous.transform.rotation;
            var scale = previous.transform.localScale;
            UnityEngine.Object.DestroyImmediate(previous);

            var airship = PrefabUtility.InstantiatePrefab(prefab, scene)
                as GameObject;
            if (airship == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the approved airship prefab.");
            }

            airship.name = AirshipName;
            airship.transform.SetParent(parent, true);
            airship.transform.SetPositionAndRotation(position, rotation);
            airship.transform.localScale = scale;
            AlignRampToTerrain(airship, scene);

            var player = RequireGameObject(scene, PlayerName);
            var controller = RequireComponent<CharacterController>(player);
            var passenger = RequireComponent<AirshipRelativePassenger>(player);
            var input = RequireComponent<AirshipInputAdapter>(player);
            var characterMotor =
                RequireComponent<FirstPersonCharacterMotor>(player);
            var mouseLook = RequireComponent<FirstPersonMouseLook>(player);

            var bridge = RequireComponent<AirshipSimulationBridge>(airship);
            var frame = RequireComponent<AirshipFrame>(airship);
            var station =
                RequireComponentInChildren<AirshipPilotStation>(airship);
            var controls = RequireTransform(
                airship.transform,
                "REF_PilotControls");

            passenger.Configure(player.transform, controller, bridge);
            characterMotor.Configure(
                controller,
                mouseLook.YawPivot,
                passenger);
            bridge.Configure(
                bridge.Motor,
                frame,
                passenger,
                bridge.LandingProbe,
                automaticAdvance: true);
            station.Configure(
                frame,
                bridge,
                passenger,
                controls,
                interactionDistance: 1.50f);
            input.Configure(bridge, station);

            var inventoryHud =
                FindComponentInScene<InventoryHudController>(scene);
            if (inventoryHud != null)
            {
                inventoryHud.ConfigureGameplayInput(
                    input,
                    mouseLook,
                    useReviewContents: true);
                EditorUtility.SetDirty(inventoryHud);
            }

            var scenario =
                FindComponentInScene<AirshipTechnicalScenario>(scene);
            if (scenario == null)
            {
                throw new InvalidOperationException(
                    "Starter Island AIR scenario is missing.");
            }

            scenario.Configure(
                bridge,
                passenger,
                station,
                input,
                Array.Empty<AirshipLandingSurfaceIdentity>(),
                Array.Empty<AirshipObstacleIdentity>(),
                automaticInitialization: true);

            player.transform.SetPositionAndRotation(
                airship.transform.TransformPoint(
                    ToUnityMetres(
                        AirshipSimulationConstants
                            .PilotExitBodyRootPosition)),
                airship.transform.rotation);

            var camera = player.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                throw new InvalidOperationException(
                    "Starter Island player camera is missing.");
            }

            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1800f;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            ValidatePilotViewGeometry(
                airship.transform,
                player.transform,
                mouseLook,
                camera.transform,
                station);

            EditorUtility.SetDirty(airship);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(scenario);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "STARTER_ISLAND_AIRSHIP_INTEGRATION status=PASS "
                + $"scene={ScenePath} interaction=E anchor={controls.name} "
                + $"distance=1.50m cameraFov={camera.fieldOfView:F0} "
                + $"cameraNear={camera.nearClipPlane:F2}");
        }

        private static void AlignRampToTerrain(GameObject airship, Scene scene)
        {
            var terrain = FindComponentInScene<Terrain>(scene);
            if (terrain == null)
            {
                return;
            }

            var rampTip = new Vector3(
                AirshipSimulationConstants.RampTipLocalXMillimetres / 1000f,
                AirshipSimulationConstants.RampTipLocalYMillimetres / 1000f,
                AirshipSimulationConstants.RampTipLocalZMillimetres / 1000f);
            var worldTip = airship.transform.TransformPoint(rampTip);
            var surfaceY =
                terrain.SampleHeight(
                    new Vector3(worldTip.x, 0f, worldTip.z))
                + terrain.transform.position.y;
            airship.transform.position +=
                Vector3.up * ((surfaceY + 0.05f) - worldTip.y);
        }

        private static void ValidatePilotViewGeometry(
            Transform airship,
            Transform player,
            FirstPersonMouseLook mouseLook,
            Transform camera,
            AirshipPilotStation station)
        {
            var authoredEye = RequireTransform(
                airship,
                "REF_PilotCamera");
            var expectedBody = ToUnityMetres(
                AirshipSimulationConstants.PilotViewBodyRootPosition);
            var eyeFromGameplay =
                expectedBody + player.InverseTransformPoint(camera.position);
            var authoredEyeLocal =
                airship.InverseTransformPoint(authoredEye.position);
            if (Vector3.Distance(eyeFromGameplay, authoredEyeLocal) > 0.06f)
            {
                throw new InvalidOperationException(
                    "Pilot camera does not coincide with REF_PilotCamera: "
                    + $"gameplay={eyeFromGameplay}, authored={authoredEyeLocal}.");
            }

            if (station.InteractionPoint !=
                RequireTransform(airship, "REF_PilotControls"))
            {
                throw new InvalidOperationException(
                    "Pilot interaction is not anchored to the cockpit controls.");
            }

            if (mouseLook.YawPivot == null
                || mouseLook.PitchPivot == null
                || camera.parent != mouseLook.PitchPivot)
            {
                throw new InvalidOperationException(
                    "Pilot camera yaw/pitch hierarchy is incomplete.");
            }
        }

        private static GameObject RequireGameObject(
            Scene scene,
            string name)
        {
            return FindGameObject(scene, name)
                ?? throw new InvalidOperationException(
                    $"Scene object '{name}' is missing.");
        }

        private static GameObject FindGameObject(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindTransform(root.transform, name);
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static T FindComponentInScene<T>(Scene scene)
            where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static T RequireComponent<T>(GameObject owner)
            where T : Component
        {
            return owner.GetComponent<T>()
                ?? throw new InvalidOperationException(
                    $"'{owner.name}' is missing {typeof(T).Name}.");
        }

        private static T RequireComponentInChildren<T>(GameObject owner)
            where T : Component
        {
            return owner.GetComponentInChildren<T>(true)
                ?? throw new InvalidOperationException(
                    $"'{owner.name}' is missing child {typeof(T).Name}.");
        }

        private static Transform RequireTransform(
            Transform root,
            string name)
        {
            return FindTransform(root, name)
                ?? throw new InvalidOperationException(
                    $"'{root.name}' is missing transform '{name}'.");
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindTransform(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Vector3 ToUnityMetres(
            AirshipVector3Millimetres value)
        {
            return new Vector3(
                value.X / 1000f,
                value.Y / 1000f,
                value.Z / 1000f);
        }
    }
}

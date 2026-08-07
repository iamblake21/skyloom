using System;
using System.IO;
using CML.Editor.Art;
using CML.Editor.Intro;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using CML.Unity.Bootstrap;
using CML.Unity.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Build
{
    public static class ProjectSetup
    {
        private const string ScenesFolder = "Assets/_Project/Scenes";
        private const string BootstrapScenePath = ScenesFolder + "/00_Bootstrap.unity";
        private const string TechnicalScenePath = ScenesFolder + "/90_Technical.unity";
        private const string IntroScenePath =
            "Assets/_Project/Scenes/01_IntroCinematic.unity";
        private const string AirshipPrefabPath =
            "Assets/_Project/Art/Vehicles/Airship/Prefabs/PF_Airship.prefab";

        public static void Run()
        {
            RemoveTemplateArtifacts();
            Directory.CreateDirectory(ScenesFolder);
            AirshipAssetSetup.Run();

            CreateBootstrapScene();
            CreateTechnicalScene();
            IntroCinematicSceneBuilder.BuildScene();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(IntroScenePath, true),
                new EditorBuildSettingsScene(TechnicalScenePath, true)
            };

            PlayerSettings.companyName = "Slicc";
            PlayerSettings.productName = "Changing My Life";
            PlayerSettings.bundleVersion = "0.0.1-dev";
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Standalone,
                "com.slicc.changingmylife");

            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("CML project setup completed.");
        }

        private static void RemoveTemplateArtifacts()
        {
            AssetDatabase.DeleteAsset("Assets/Readme.asset");
            AssetDatabase.DeleteAsset("Assets/TutorialInfo");
            AssetDatabase.DeleteAsset("Assets/Scenes");
        }

        private static void CreateBootstrapScene()
        {
            if (TryOpenCurrentGeneratedScene(
                BootstrapScenePath,
                GeneratedSceneRevision.BootstrapSceneId,
                GeneratedSceneRevision.CurrentBootstrapRevision,
                ValidateBootstrapScene))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(new Vector3(0f, 2.5f, -7f), new Vector3(12f, 0f, 0f));
            CreateLight();

            var bootstrap = new GameObject("GameBootstrap");
            bootstrap.AddComponent<GameBootstrap>();
            bootstrap.AddComponent<BuildInfoOverlay>();
            bootstrap.AddComponent<GeneratedSceneRevision>().Configure(
                GeneratedSceneRevision.BootstrapSceneId,
                GeneratedSceneRevision.CurrentBootstrapRevision);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "FoundationReadyMarker";
            marker.transform.position = new Vector3(0f, 1f, 0f);
            marker.transform.localScale = new Vector3(3f, 2f, 1f);

            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
        }

        private static void CreateTechnicalScene()
        {
            if (TryOpenCurrentGeneratedScene(
                TechnicalScenePath,
                GeneratedSceneRevision.TechnicalSceneId,
                GeneratedSceneRevision.CurrentTechnicalRevision,
                ValidateTechnicalScene))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLight();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AirshipPrefabPath);
            if (prefab == null)
            {
                throw new InvalidDataException($"Missing AIR prefab: {AirshipPrefabPath}");
            }

            var airship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            airship.name = "PF_Airship";
            airship.transform.SetPositionAndRotation(
                new Vector3(0f, 1f, 0f),
                Quaternion.identity);
            var bridge = airship.GetComponent<AirshipSimulationBridge>();
            var frame = airship.GetComponent<AirshipFrame>();
            var station = airship.GetComponentInChildren<AirshipPilotStation>(true);
            if (bridge == null || frame == null || station == null)
            {
                throw new InvalidDataException("AIR prefab gameplay rig is incomplete.");
            }

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "AIR_LandingPlatform";
            platform.transform.SetPositionAndRotation(
                new Vector3(
                    (AirshipSimulationConstants.RampTipLocalXMillimetres + 850) / 1000f,
                    0.75f,
                    AirshipSimulationConstants.RampTipLocalZMillimetres / 1000f),
                Quaternion.identity);
            platform.transform.localScale = new Vector3(1f, 0.5f, 1f);
            var obstacleIdentity = platform.AddComponent<AirshipObstacleIdentity>();
            obstacleIdentity.Configure(AirshipTechnicalIds.PlatformObstacle);
            var landingIdentity = platform.AddComponent<AirshipLandingSurfaceIdentity>();
            landingIdentity.Configure(
                AirshipTechnicalIds.LandingSurface,
                AirshipTechnicalIds.PlatformObstacle);

            var island = GameObject.CreatePrimitive(PrimitiveType.Cube);
            island.name = "AIR_TestIsland";
            island.transform.SetPositionAndRotation(
                new Vector3(0f, -1.5f, 0f),
                Quaternion.identity);
            island.transform.localScale = new Vector3(20f, 3f, 20f);
            var islandObstacle = island.AddComponent<AirshipObstacleIdentity>();
            islandObstacle.Configure(AirshipTechnicalIds.IslandObstacle);

            var flightObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flightObstacle.name = "AIR_FlightTestObstacle";
            flightObstacle.transform.SetPositionAndRotation(
                new Vector3(0f, 3f, 10f),
                Quaternion.identity);
            flightObstacle.transform.localScale = new Vector3(3f, 6f, 2f);
            var flightObstacleIdentity =
                flightObstacle.AddComponent<AirshipObstacleIdentity>();
            flightObstacleIdentity.Configure(
                AirshipTechnicalIds.FlightTestObstacle);

            var player = new GameObject("AIR_FirstPersonPlayer");
            player.transform.position = airship.transform.TransformPoint(
                new Vector3(
                    AirshipSimulationConstants.PilotExitBodyRootPosition.X / 1000f,
                    AirshipSimulationConstants.PilotExitBodyRootPosition.Y / 1000f,
                    AirshipSimulationConstants.PilotExitBodyRootPosition.Z / 1000f));
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            var passenger = player.AddComponent<AirshipRelativePassenger>();
            var input = player.AddComponent<AirshipInputAdapter>();
            player.AddComponent<FirstPersonCharacterMotor>();

            var viewYaw = new GameObject("AIR_ViewYaw");
            viewYaw.transform.SetParent(player.transform, false);
            viewYaw.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var viewPitch = new GameObject("AIR_ViewPitch");
            viewPitch.transform.SetParent(viewYaw.transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(viewPitch.transform, false);
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            var mouseLook = player.AddComponent<FirstPersonMouseLook>();
            mouseLook.Configure(viewYaw.transform, viewPitch.transform);

            passenger.Configure(player.transform, controller, bridge);
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
                station.InteractionPoint,
                1.50f);
            input.Configure(bridge, station);

            var ready = new GameObject("AIR_TechnicalReady");
            var scenario = ready.AddComponent<AirshipTechnicalScenario>();
            ready.AddComponent<GeneratedSceneRevision>().Configure(
                GeneratedSceneRevision.TechnicalSceneId,
                GeneratedSceneRevision.CurrentTechnicalRevision);
            scenario.Configure(
                bridge,
                passenger,
                station,
                input,
                new[] { landingIdentity },
                new[]
                {
                    obstacleIdentity,
                    islandObstacle,
                    flightObstacleIdentity,
                },
                automaticInitialization: true);

            EditorSceneManager.SaveScene(scene, TechnicalScenePath);
        }

        private static bool TryOpenCurrentGeneratedScene(
            string scenePath,
            string sceneId,
            int revision,
            Func<Scene, bool> validateStructure)
        {
            if (!File.Exists(scenePath))
            {
                return false;
            }

            Scene scene;
            try
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Single);
            }
            catch
            {
                return false;
            }

            try
            {
                var marker =
                    FindSingleComponentInScene<GeneratedSceneRevision>(scene);
                return marker != null
                    && marker.Matches(sceneId, revision)
                    && validateStructure(scene);
            }
            catch
            {
                return false;
            }
        }

        private static bool ValidateBootstrapScene(Scene scene)
        {
            var bootstrap = FindGameObject(scene, "GameBootstrap");
            var camera = FindGameObject(scene, "Main Camera");
            var light = FindGameObject(scene, "Directional Light");
            var readyMarker = FindGameObject(scene, "FoundationReadyMarker");
            return bootstrap != null
                && bootstrap.GetComponent<GameBootstrap>() != null
                && bootstrap.GetComponent<BuildInfoOverlay>() != null
                && camera != null
                && camera.GetComponent<Camera>() != null
                && camera.GetComponent<AudioListener>() != null
                && light != null
                && light.GetComponent<Light>() != null
                && readyMarker != null
                && readyMarker.GetComponent<BoxCollider>() != null
                && readyMarker.GetComponent<Renderer>() != null
                && HasNoMissingScripts(scene);
        }

        private static bool ValidateTechnicalScene(Scene scene)
        {
            var airship = FindGameObject(scene, "PF_Airship");
            var player = FindGameObject(scene, "AIR_FirstPersonPlayer");
            var platform = FindGameObject(scene, "AIR_LandingPlatform");
            var island = FindGameObject(scene, "AIR_TestIsland");
            var flightObstacle =
                FindGameObject(scene, "AIR_FlightTestObstacle");
            var ready = FindGameObject(scene, "AIR_TechnicalReady");
            if (airship == null
                || player == null
                || platform == null
                || island == null
                || flightObstacle == null
                || ready == null)
            {
                return false;
            }

            var bridge = airship.GetComponent<AirshipSimulationBridge>();
            var frame = airship.GetComponent<AirshipFrame>();
            var station =
                airship.GetComponentInChildren<AirshipPilotStation>(true);
            var passenger = player.GetComponent<AirshipRelativePassenger>();
            var input = player.GetComponent<AirshipInputAdapter>();
            var mouseLook = player.GetComponent<FirstPersonMouseLook>();
            var scenario = ready.GetComponent<AirshipTechnicalScenario>();
            var landing =
                platform.GetComponent<AirshipLandingSurfaceIdentity>();
            var platformObstacle =
                platform.GetComponent<AirshipObstacleIdentity>();
            if (bridge == null
                || bridge.Motor == null
                || bridge.LandingProbe == null
                || frame == null
                || station == null
                || passenger == null
                || input == null
                || player.GetComponent<FirstPersonCharacterMotor>() == null
                || mouseLook == null
                || mouseLook.YawPivot == null
                || mouseLook.PitchPivot == null
                || mouseLook.PitchPivot.parent != mouseLook.YawPivot
                || player.GetComponent<CharacterController>() == null
                || player.GetComponentInChildren<Camera>(true) == null
                || player.GetComponentInChildren<Camera>(true).transform.parent
                    != mouseLook.PitchPivot
                || scenario == null
                || scenario.Bridge != bridge
                || scenario.Passenger != passenger
                || landing == null
                || platformObstacle == null
                || !landing.TryBuildLogicalState(out _)
                || !landing.OwnsCollider(platform.GetComponent<BoxCollider>())
                || island.GetComponent<AirshipObstacleIdentity>() == null
                || flightObstacle.GetComponent<AirshipObstacleIdentity>() == null
                || !HasNoMissingScripts(scene))
            {
                return false;
            }

            try
            {
                return landing.StableId == AirshipTechnicalIds.LandingSurface
                    && landing.SupportingObstacleId
                        == AirshipTechnicalIds.PlatformObstacle
                    && platformObstacle.StableId
                        == AirshipTechnicalIds.PlatformObstacle
                    && island.GetComponent<AirshipObstacleIdentity>().StableId
                        == AirshipTechnicalIds.IslandObstacle
                    && flightObstacle.GetComponent<AirshipObstacleIdentity>().StableId
                        == AirshipTechnicalIds.FlightTestObstacle;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static T FindSingleComponentInScene<T>(Scene scene)
            where T : Component
        {
            T result = null;
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var matches = roots[rootIndex].GetComponentsInChildren<T>(true);
                for (var matchIndex = 0; matchIndex < matches.Length; matchIndex++)
                {
                    if (result != null)
                    {
                        return null;
                    }

                    result = matches[matchIndex];
                }
            }

            return result;
        }

        private static GameObject FindGameObject(Scene scene, string objectName)
        {
            GameObject result = null;
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var transforms =
                    roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (var transformIndex = 0;
                     transformIndex < transforms.Length;
                     transformIndex++)
                {
                    if (!string.Equals(
                        transforms[transformIndex].name,
                        objectName,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (result != null)
                    {
                        return null;
                    }

                    result = transforms[transformIndex].gameObject;
                }
            }

            return result;
        }

        private static bool HasNoMissingScripts(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var transforms =
                    roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (var transformIndex = 0;
                     transformIndex < transforms.Length;
                     transformIndex++)
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transforms[transformIndex].gameObject) != 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void CreateCamera(Vector3 position, Vector3 rotation)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = position;
            cameraObject.transform.eulerAngles = rotation;

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.06f, 0.09f, 1f);
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.eulerAngles = new Vector3(50f, -30f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }
    }
}

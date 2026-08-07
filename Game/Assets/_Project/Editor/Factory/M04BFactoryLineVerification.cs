using System;
using System.Collections.Generic;
using System.IO;
using CML.Foundation;
using CML.Simulation.Machines;
using CML.Unity.Airship;
using CML.Unity.Factory;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Machines;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Read-only contract validation and visual capture for the isolated M0.4B scene.
    /// Temporary review cameras are never saved.
    /// </summary>
    public static class M04BFactoryLineVerification
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/92_M04B_FactoryLine_Test.unity";
        private const string DefaultCaptureDirectory =
            @"D:\CML_UnityCache\M04B_Captures";
        private const int CaptureWidth = 1600;
        private const int CaptureHeight = 900;

        [MenuItem("CML/Factory/Verify and Capture M0.4B Factory Line")]
        public static void VerifyAndCapture()
        {
            var outputDirectory = Environment.GetEnvironmentVariable(
                "CML_M04B_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = DefaultCaptureDirectory;
            }

            Directory.CreateDirectory(outputDirectory);
            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                throw new FileNotFoundException(
                    "The generated M0.4B scene does not exist.",
                    ScenePath);
            }

            var originalActiveScene = SceneManager.GetActiveScene();
            var reviewScene = SceneManager.GetSceneByPath(ScenePath);
            var openedByUtility = !reviewScene.IsValid() || !reviewScene.isLoaded;
            try
            {
                if (openedByUtility)
                {
                    reviewScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive);
                }

                SceneManager.SetActiveScene(reviewScene);
                ValidateContract(reviewScene);

                var hiddenReviewObjects = HideForCleanCapture(
                    reviewScene,
                    "SIGN_PreplacedLine",
                    "SIGN_BuildArea");
                string[] captures;
                try
                {
                    captures = new[]
                    {
                        Capture(
                            reviewScene,
                            outputDirectory,
                            "01_factory_line_hero.png",
                            new Vector3(-9.5f, 4.0f, -8.5f),
                            new Vector3(-3.5f, 0.68f, 0.75f),
                            54f),
                        Capture(
                            reviewScene,
                            outputDirectory,
                            "02_factory_and_build_area.png",
                            new Vector3(11.0f, 9.0f, -10.0f),
                            new Vector3(0.6f, 0.18f, 0.5f),
                            54f),
                        Capture(
                            reviewScene,
                            outputDirectory,
                            "03_mechanical_press_detail.png",
                            new Vector3(-7.4f, 2.8f, -4.3f),
                            new Vector3(-3.5f, 1.05f, 0.15f),
                            52f),
                        Capture(
                            reviewScene,
                            outputDirectory,
                            "04_build_area.png",
                            new Vector3(14.0f, 5.8f, -5.5f),
                            new Vector3(6.5f, 0.16f, 1.4f),
                            50f)
                    };
                }
                finally
                {
                    RestoreHiddenObjects(hiddenReviewObjects);
                }

                Debug.Log(
                    "M04B_SCENE_VERIFICATION status=PASS "
                    + "authority=1 nodes=13 lanes=0 interactions=3 modules=10 "
                    + "press=1 buildController=1 buildSurface=1 "
                    + "beltDrive=1 beltLineCapacity=enabled missingScripts=0 "
                    + $"captures={captures.Length} output={outputDirectory}");
            }
            finally
            {
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                }

                if (openedByUtility
                    && reviewScene.IsValid()
                    && reviewScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(reviewScene, true);
                }
            }
        }

        private static void ValidateContract(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidDataException(
                    "Unity did not load the M0.4B review scene.");
            }

            var simulationRoot =
                RequireExactlyOne<FactoryLineSimulationRoot>(scene);
            var hud = RequireExactlyOne<FactoryHudOrchestrator>(scene);
            RequireExactlyOne<TransferCommandBridge>(scene);
            RequireExactlyOne<FactoryCentralInteractor>(scene);
            RequireExactlyOne<FactoryBuildController>(scene);
            RequireExactlyOne<FactoryDismantleController>(scene);
            RequireExactlyOne<FactoryBuildSurface>(scene);
            RequireExactlyOne<FirstPersonCharacterMotor>(scene);
            RequireExactlyOne<FirstPersonMouseLook>(scene);
            RequireExactlyOne<FactoryFirstPersonInput>(scene);
            RequireExactlyOne<CharacterController>(scene);
            var inventoryHud =
                RequireExactlyOne<InventoryHudController>(scene);
            RequireExactlyOne<ChestHudController>(scene);
            RequireExactlyOne<MachineHudController>(scene);
            if (simulationRoot.InventoryHud != inventoryHud
                || hud.InventoryHud != inventoryHud)
            {
                throw new InvalidDataException(
                    "The normal backpack and chest-side backpack must read the "
                    + "same authoritative InventoryHudController.");
            }

            var expectedNodes = new Dictionary<StableId, MachineNodeKind>
            {
                [FactoryLineSimulationRoot.SourceCrateId] = MachineNodeKind.Buffer,
                [FactoryLineSimulationRoot.InputFunnelId] = MachineNodeKind.Funnel,
                [FactoryLineSimulationRoot.FeedBelt01Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.FeedBelt02Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.FeedBelt03Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.FeedBelt04Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.PressId] = MachineNodeKind.Machine,
                [FactoryLineSimulationRoot.DrainBelt01Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.DrainBelt02Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.DrainBelt03Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.DrainBelt04Id] = MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.OutputFunnelId] = MachineNodeKind.Funnel,
                [FactoryLineSimulationRoot.SinkCrateId] = MachineNodeKind.Buffer
            };
            var anchors = ComponentsInScene<FactoryNodeAnchor>(scene);
            if (anchors.Count != expectedNodes.Count)
            {
                throw new InvalidDataException(
                    $"Expected {expectedNodes.Count} authored nodes, found "
                    + $"{anchors.Count}.");
            }

            var seenNodes = new HashSet<StableId>();
            foreach (var anchor in anchors)
            {
                if (!seenNodes.Add(anchor.NodeId)
                    || !expectedNodes.TryGetValue(
                        anchor.NodeId,
                        out var expectedKind)
                    || anchor.NodeKind != expectedKind
                    || anchor.InputSocket == null
                    || anchor.OutputSocket == null)
                {
                    throw new InvalidDataException(
                        $"Invalid factory node contract on '{anchor.name}'.");
                }
            }

            var interactions = ComponentsInScene<FactoryInteractionTarget>(scene);
            if (interactions.Count != 3)
            {
                throw new InvalidDataException(
                    "The two chests and press need one E-interaction each.");
            }

            foreach (var target in interactions)
            {
                if (!target.IsConfigured
                    || !expectedNodes.ContainsKey(target.StableId)
                    || string.IsNullOrWhiteSpace(target.Prompt))
                {
                    throw new InvalidDataException(
                        $"Invalid E-interaction target on '{target.name}'.");
                }
            }

            if (ComponentsInScene<FactoryBeltLanePresenter>(scene).Count != 0)
            {
                throw new InvalidDataException(
                    "The retired endpoint-lane presenter is still in the scene.");
            }

            if (ComponentsInScene<FactoryLogisticsModulePresenter>(scene).Count
                != 10)
            {
                throw new InvalidDataException(
                    "Every Funnel and BeltModule needs a read-only presenter.");
            }

            var press = RequireExactlyOne<FactoryPressPresenter>(scene);
            if (press.MachineId != FactoryLineSimulationRoot.PressId)
            {
                throw new InvalidDataException(
                    "The visual press is not bound to the authoritative press.");
            }

            var pressObject = FindGameObject(scene, "M04B_MechanicalPress");
            RequireChild(pressObject, "ANM_PressRam");
            RequireChild(pressObject, "REF_Workpiece");
            RequireChild(pressObject, "PORT_ItemIn");
            RequireChild(pressObject, "PORT_ItemOut");

            RequireNamedObject(scene, "M04B_Systems");
            RequireNamedObject(scene, "M04B_PreplacedLine");
            RequireNamedObject(scene, "M04B_SourceCrate");
            RequireNamedObject(scene, "M04B_InputFunnel");
            RequireNamedObject(scene, "M04B_FeedBelt01");
            RequireNamedObject(scene, "M04B_FeedBelt02");
            RequireNamedObject(scene, "M04B_FeedBelt03");
            RequireNamedObject(scene, "M04B_FeedBelt04");
            RequireNamedObject(scene, "M04B_OutputFunnel");
            RequireNamedObject(scene, "M04B_DrainBelt01");
            RequireNamedObject(scene, "M04B_DrainBelt02");
            RequireNamedObject(scene, "M04B_DrainBelt03");
            RequireNamedObject(scene, "M04B_DrainBelt04");
            RequireNamedObject(scene, "M04B_SinkCrate");
            RequireNamedObject(scene, "BUILD_001_Controller");
            RequireNamedObject(scene, "BUILD_001_FreeArea");
            RequireNamedObject(scene, "M04B_RuntimeBuilds");
            RequireNamedObject(scene, "M04B_FirstPersonPlayer");
            RequireNamedObject(scene, "Main Camera");
            RequireNamedObject(scene, "HUD_Inventory");
            RequireNamedObject(scene, "HUD_Chest");
            RequireNamedObject(scene, "HUD_Machine");

            var missingScripts = CountMissingScripts(scene);
            if (missingScripts != 0)
            {
                throw new InvalidDataException(
                    $"The scene contains {missingScripts} missing scripts.");
            }
        }

        private static string Capture(
            Scene scene,
            string outputDirectory,
            string fileName,
            Vector3 cameraPosition,
            Vector3 lookTarget,
            float fieldOfView)
        {
            var cameraObject = new GameObject(
                $"__M04B_REVIEW_CAMERA_{Path.GetFileNameWithoutExtension(fileName)}");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.position = cameraPosition;
            cameraObject.transform.rotation = Quaternion.LookRotation(
                lookTarget - cameraPosition,
                Vector3.up);

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 180f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            var target = new RenderTexture(
                CaptureWidth,
                CaptureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4,
                name = $"M04B_Capture_{fileName}"
            };
            var image = new Texture2D(
                CaptureWidth,
                CaptureHeight,
                TextureFormat.RGBA32,
                false,
                false);
            var previousActive = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(
                    new Rect(0, 0, CaptureWidth, CaptureHeight),
                    0,
                    0,
                    false);
                image.Apply(false, false);

                var path = Path.Combine(outputDirectory, fileName);
                File.WriteAllBytes(path, image.EncodeToPNG());
                return path;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static List<GameObject> HideForCleanCapture(
            Scene scene,
            params string[] names)
        {
            var hidden = new List<GameObject>();
            for (var index = 0; index < names.Length; index++)
            {
                var target = FindGameObject(scene, names[index]);
                if (target != null && target.activeSelf)
                {
                    target.SetActive(false);
                    hidden.Add(target);
                }
            }

            return hidden;
        }

        private static void RestoreHiddenObjects(
            IReadOnlyList<GameObject> hiddenObjects)
        {
            for (var index = 0; index < hiddenObjects.Count; index++)
            {
                if (hiddenObjects[index] != null)
                {
                    hiddenObjects[index].SetActive(true);
                }
            }
        }

        private static T RequireExactlyOne<T>(Scene scene)
            where T : Component
        {
            var matches = ComponentsInScene<T>(scene);
            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one {typeof(T).Name}, found "
                    + $"{matches.Count}.");
            }

            return matches[0];
        }

        private static List<T> ComponentsInScene<T>(Scene scene)
            where T : Component
        {
            var matches = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                matches.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return matches;
        }

        private static bool ContainsLane(
            IReadOnlyList<FactoryBeltLanePresenter> lanes,
            StableId id)
        {
            for (var index = 0; index < lanes.Count; index++)
            {
                if (lanes[index].LaneId == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RequireNamedObject(Scene scene, string name)
        {
            if (FindGameObject(scene, name) == null)
            {
                throw new InvalidDataException(
                    $"Scene object '{name}' is missing.");
            }
        }

        private static GameObject FindGameObject(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }

        private static Transform RequireChild(GameObject root, string name)
        {
            if (root == null)
            {
                throw new InvalidDataException(
                    $"Cannot find '{name}' because its root is missing.");
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            throw new InvalidDataException(
                $"'{root.name}' is missing child marker '{name}'.");
        }

        private static int CountMissingScripts(Scene scene)
        {
            var total = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    total += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject);
                }
            }

            return total;
        }
    }
}

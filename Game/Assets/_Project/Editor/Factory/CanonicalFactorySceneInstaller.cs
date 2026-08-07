using System;
using System.IO;
using CML.Foundation;
using CML.Editor.UI;
using CML.Unity.Airship;
using CML.Unity.Mining;
using CML.Unity.Presentation.Equipment;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Crafting;
using CML.Unity.Presentation.Machines;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CML.Unity.Factory.Editor
{
    /// <summary>
    /// Installs the factory feature into a gameplay scene without copying the
    /// technical fixture. Rebuilding the island and patching the saved scene use
    /// this same path, so their compositions cannot drift apart again.
    /// </summary>
    public static class CanonicalFactorySceneInstaller
    {
        public const string RootName = "FACTORY_CanonicalGameplay";

        private static bool repairScheduled;

        private const string StarterIslandScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";
        private const string CratePrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_Crate.prefab";
        private const string FunnelPrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_Funnel.prefab";
        private const string BeltStraightPrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_Straight.prefab";
        private const string BeltDrivePrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_DriveUnit.prefab";
        private const string BeltCurvePrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_Curve.prefab";
        private const string BeltInclinePrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_Incline.prefab";
        private const string BeltCurveLeftPrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_CurveLeft.prefab";
        private const string PressPrefabPath =
            "Assets/_Project/Art/MechanicalEra/Prefabs/PF_MechanicalPress.prefab";
        private const string IronIngotPrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_IronIngot.prefab";
        private const string IronPlatePrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_IronPlate.prefab";
        private const string ChestHudPrefabPath =
            "Assets/_Project/Art/UI/Chest/PF_ChestHUD.prefab";
        private const string InventoryHudPrefabPath =
            "Assets/_Project/Art/UI/Inventory/PF_InventoryHUD.prefab";
        private const string MachineHudPrefabPath =
            "Assets/_Project/Art/UI/Machine/PF_MachineHUD.prefab";
        private const string WorkbenchHudPrefabPath =
            "Assets/_Project/Art/UI/Crafting/PF_WorkbenchHUD.prefab";
        private static readonly StableId StarterWorkbenchId =
            new StableId(0x574F524B42454E43UL, 0x485F535441525445UL);
        private const string ValidHologramMaterialPath =
            "Assets/_Project/Art/FactoryTest/Materials/M_M04B_HologramValid.mat";
        private const string InvalidHologramMaterialPath =
            "Assets/_Project/Art/FactoryTest/Materials/M_M04B_HologramInvalid.mat";

        [InitializeOnLoadMethod]
        private static void ScheduleOpenSceneRepair()
        {
            if (repairScheduled)
            {
                return;
            }

            repairScheduled = true;
            EditorApplication.delayCall += RepairOpenStarterIslandIfNeeded;
        }

        private static void RepairOpenStarterIslandIfNeeded()
        {
            repairScheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()
                || !scene.isLoaded
                || !string.Equals(
                    scene.path,
                    StarterIslandScenePath,
                    StringComparison.Ordinal))
            {
                return;
            }

            var scenario = FindOptional<AirshipTechnicalScenario>(scene);
            var factoryRoot = FindOptional<FactoryLineSimulationRoot>(scene);
            var mouseLook = FindOptional<FirstPersonMouseLook>(scene);
            var characterMotor = FindOptional<FirstPersonCharacterMotor>(scene);
            var inventoryHud = FindOptional<InventoryHudController>(scene);
            var equipmentView = FindOptional<FirstPersonEquipmentView>(scene);
            var miningController = FindOptional<ManualMiningController>(scene);
            var workbenchHud = FindOptional<WorkbenchHudController>(scene);
            if (scenario != null
                && scenario.Bridge != null
                && factoryRoot != null
                && factoryRoot.InitializationProfile ==
                    FactorySimulationProfile.CanonicalGameplay
                && mouseLook != null
                && mouseLook.YawPivot != null
                && mouseLook.PitchPivot != null
                && characterMotor != null
                && characterMotor.ViewYawPivot != null
                && inventoryHud != null
                && workbenchHud != null
                && equipmentView != null
                && miningController != null)
            {
                return;
            }

            RepairAirshipComposition(scene);
            InstallByDiscovery(scene);
            if (!EditorSceneManager.SaveScene(scene, StarterIslandScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save the repaired Starter Island composition.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "CML_CANONICAL_GAMEPLAY_WIRING_REPAIRED "
                + $"scene={StarterIslandScenePath}");
        }

        [MenuItem("CML/Factory/Install Canonical Factory In Starter Island")]
        public static void InstallIntoSavedStarterIsland()
        {
            if (!File.Exists(Path.GetFullPath(StarterIslandScenePath)))
            {
                throw new FileNotFoundException(
                    "The starter-island scene does not exist.",
                    StarterIslandScenePath);
            }

            var scene = EditorSceneManager.OpenScene(
                StarterIslandScenePath,
                OpenSceneMode.Single);
            InstallByDiscovery(scene);
            if (!EditorSceneManager.SaveScene(scene, StarterIslandScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save the canonically integrated starter island.");
            }

            Debug.Log(
                $"CML_CANONICAL_FACTORY_INSTALLED scene={StarterIslandScenePath}");
        }

        public static void InstallByDiscovery(Scene scene)
        {
            WorkbenchHudAssetSetup.EnsureAssets();

            // The island player is authored directly in the scene. Discovering it by
            // its canonical object name is intentionally more stable than asking the
            // editor's global component index while a large scene is still integrating.
            var player = FindNamedGameObject(scene, "AIR_FirstPersonPlayer");
            var mouseLook = player.GetComponent<FirstPersonMouseLook>();
            var input = player.GetComponent<AirshipInputAdapter>();
            var camera = player.GetComponentInChildren<Camera>(true);
            var inventoryHud = FindOptional<InventoryHudController>(scene);
            if (inventoryHud == null)
            {
                RemoveNamedObject(scene, "PF_InventoryHUD");
                var inventoryPrefab = RequirePrefab(InventoryHudPrefabPath);
                inventoryHud = InstantiateComponent<InventoryHudController>(
                    inventoryPrefab,
                    scene,
                    player.transform.parent,
                    "PF_InventoryHUD");
            }
            var terrain = FindExactlyOne<Terrain>(scene);

            Install(
                scene,
                player,
                camera,
                mouseLook,
                input,
                inventoryHud,
                terrain,
                player.transform.parent);
        }

        private static void RepairAirshipComposition(Scene scene)
        {
            var player = FindNamedGameObject(scene, "AIR_FirstPersonPlayer");
            var controller = player.GetComponent<CharacterController>();
            var passenger = player.GetComponent<AirshipRelativePassenger>();
            var input = player.GetComponent<AirshipInputAdapter>();
            var motor = player.GetComponent<FirstPersonCharacterMotor>();
            var mouseLook = player.GetComponent<FirstPersonMouseLook>();
            var yawPivot = FindNamedGameObject(scene, "AIR_ViewYaw").transform;
            var pitchPivot = FindNamedGameObject(scene, "AIR_ViewPitch").transform;
            var bridge = FindExactlyOne<AirshipSimulationBridge>(scene);
            var station = bridge.GetComponentInChildren<AirshipPilotStation>(true);
            var scenario = FindExactlyOne<AirshipTechnicalScenario>(scene);
            if (controller == null || passenger == null || input == null
                || motor == null || mouseLook == null || station == null)
            {
                throw new InvalidOperationException(
                    "Starter Island AIR composition is incomplete and cannot be repaired.");
            }

            passenger.Configure(player.transform, controller, bridge);
            mouseLook.Configure(yawPivot, pitchPivot);
            motor.Configure(controller, yawPivot, passenger);
            bridge.Configure(
                bridge.Motor,
                bridge.GetComponent<AirshipFrame>(),
                passenger,
                bridge.LandingProbe,
                automaticAdvance: true);
            station.Configure(
                bridge.GetComponent<AirshipFrame>(),
                bridge,
                passenger,
                station.InteractionPoint,
                1.5f);
            input.Configure(bridge, station);
            scenario.Configure(
                bridge,
                passenger,
                station,
                input,
                FindAllInScene<AirshipLandingSurfaceIdentity>(scene),
                FindAllInScene<AirshipObstacleIdentity>(scene),
                automaticInitialization: true);

            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(passenger);
            EditorUtility.SetDirty(input);
            EditorUtility.SetDirty(motor);
            EditorUtility.SetDirty(mouseLook);
            EditorUtility.SetDirty(bridge);
            EditorUtility.SetDirty(station);
            EditorUtility.SetDirty(scenario);
        }

        private static GameObject FindNamedGameObject(Scene scene, string exactName)
        {
            GameObject found = null;
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (var index = 0; index < transforms.Length; index++)
                {
                    if (!string.Equals(
                            transforms[index].name,
                            exactName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        throw new InvalidOperationException(
                            $"Scene '{scene.name}' contains more than one object "
                            + $"named '{exactName}'.");
                    }

                    found = transforms[index].gameObject;
                }
            }

            return found != null
                ? found
                : throw new InvalidOperationException(
                    $"Scene '{scene.name}' contains no object named '{exactName}'.");
        }

        public static void Install(
            Scene scene,
            GameObject player,
            Camera camera,
            FirstPersonMouseLook mouseLook,
            AirshipInputAdapter input,
            InventoryHudController inventoryHud,
            Terrain terrain,
            Transform parent = null)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("A loaded scene is required.", nameof(scene));
            }

            if (player == null || camera == null || mouseLook == null
                || input == null || inventoryHud == null || terrain == null)
            {
                throw new InvalidOperationException(
                    "Canonical factory installation requires the real player, camera, "
                    + "mouse look, gameplay input, inventory HUD and terrain.");
            }

            RemovePreviousInstallation(scene, player);

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            var transferBridge = root.AddComponent<TransferCommandBridge>();
            var craftingBridge = root.AddComponent<CraftingCommandBridge>();
            var hud = root.AddComponent<FactoryHudOrchestrator>();
            var simulationRoot = root.AddComponent<FactoryLineSimulationRoot>();

            var chestHud = InstantiateComponent<ChestHudController>(
                RequirePrefab(ChestHudPrefabPath),
                scene,
                root.transform,
                "HUD_Chest");
            var localBridge = chestHud.GetComponent<TransferCommandBridge>();
            if (localBridge != null && localBridge != transferBridge)
            {
                UnityEngine.Object.DestroyImmediate(localBridge);
            }

            var machineHud = InstantiateComponent<MachineHudController>(
                RequirePrefab(MachineHudPrefabPath),
                scene,
                root.transform,
                "HUD_Machine");
            var workbenchHud = InstantiateComponent<WorkbenchHudController>(
                RequirePrefab(WorkbenchHudPrefabPath),
                scene,
                root.transform,
                "HUD_Workbench");

            hud.ConfigureUi(
                inventoryHud,
                chestHud,
                machineHud,
                transferBridge,
                mouseLook,
                input,
                workbenchHud,
                craftingBridge);
            simulationRoot.Configure(
                transferBridge,
                inventoryHud,
                hud,
                RequirePrefab(IronIngotPrefabPath),
                RequirePrefab(IronPlatePrefabPath),
                FactorySimulationProfile.CanonicalGameplay);

            var equipmentView =
                camera.GetComponent<FirstPersonEquipmentView>();
            if (equipmentView == null)
            {
                equipmentView =
                    camera.gameObject.AddComponent<FirstPersonEquipmentView>();
            }

            equipmentView.Configure(inventoryHud);
            var miningController =
                camera.GetComponent<ManualMiningController>();
            if (miningController == null)
            {
                miningController =
                    camera.gameObject.AddComponent<ManualMiningController>();
            }

            miningController.Configure(equipmentView, inventoryHud);
            CollectionFeedHudController.EnsureFor(inventoryHud);
            EditorUtility.SetDirty(equipmentView);
            EditorUtility.SetDirty(miningController);
            EditorUtility.SetDirty(inventoryHud);

            var promptDocument = inventoryHud.GetComponentInParent<UIDocument>();
            var interactor = player.AddComponent<FactoryCentralInteractor>();
            interactor.Configure(camera, hud, promptDocument, 3.25f);

            var authoredWorkbench = FindNamedGameObject(
                scene,
                "ENV_StarterProp_00_PF_Workbench");
            var workbenchTarget =
                authoredWorkbench.GetComponent<FactoryInteractionTarget>()
                ?? authoredWorkbench.AddComponent<FactoryInteractionTarget>();
            workbenchTarget.Configure(
                StarterWorkbenchId,
                FactoryInteractionKind.Workbench,
                "Usa Banco da lavoro");
            EditorUtility.SetDirty(workbenchTarget);

            var runtimeBuilds = new GameObject("FACTORY_RuntimeBuilds").transform;
            runtimeBuilds.SetParent(root.transform, false);

            var buildObject = new GameObject("FACTORY_BuildController");
            buildObject.transform.SetParent(root.transform, false);
            buildObject.AddComponent<FactoryBuildController>().Configure(
                simulationRoot,
                hud,
                camera,
                runtimeBuilds,
                RequirePrefab(CratePrefabPath),
                RequirePrefab(FunnelPrefabPath),
                RequirePrefab(BeltStraightPrefabPath),
                RequirePrefab(PressPrefabPath),
                RequirePrefab(IronIngotPrefabPath),
                RequirePrefab(IronPlatePrefabPath),
                RequireMaterial(ValidHologramMaterialPath),
                RequireMaterial(InvalidHologramMaterialPath),
                RequirePrefab(BeltDrivePrefabPath),
                RequirePrefab(BeltCurvePrefabPath),
                RequirePrefab(BeltInclinePrefabPath),
                RequirePrefab(BeltCurveLeftPrefabPath));

            var dismantleObject = new GameObject("FACTORY_DismantleController");
            dismantleObject.transform.SetParent(root.transform, false);
            dismantleObject.AddComponent<FactoryDismantleController>().Configure(
                simulationRoot,
                hud,
                camera);

            if (terrain.GetComponent<FactoryBuildSurface>() == null)
            {
                terrain.gameObject.AddComponent<FactoryBuildSurface>();
            }

            inventoryHud.ConfigureGameplayInput(input, mouseLook, false);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void RemovePreviousInstallation(Scene scene, GameObject player)
        {
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                var transforms = roots[index].GetComponentsInChildren<Transform>(true);
                for (var childIndex = transforms.Length - 1; childIndex >= 0; childIndex--)
                {
                    if (transforms[childIndex].name == RootName)
                    {
                        UnityEngine.Object.DestroyImmediate(
                            transforms[childIndex].gameObject);
                    }
                }
            }

            var existingInteractor = player.GetComponent<FactoryCentralInteractor>();
            if (existingInteractor != null)
            {
                UnityEngine.Object.DestroyImmediate(existingInteractor);
            }
        }

        private static void RemoveNamedObject(Scene scene, string exactName)
        {
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (var index = transforms.Length - 1; index >= 0; index--)
                {
                    if (transforms[index].name == exactName)
                    {
                        UnityEngine.Object.DestroyImmediate(transforms[index].gameObject);
                    }
                }
            }
        }

        private static T InstantiateComponent<T>(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name)
            where T : Component
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate prefab '{prefab.name}'.");
            }

            instance.name = name;
            instance.transform.SetParent(parent, false);
            var component = instance.GetComponentInChildren<T>(true);
            if (component == null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                throw new InvalidOperationException(
                    $"Prefab '{prefab.name}' has no {typeof(T).Name}.");
            }

            return component;
        }

        private static GameObject RequirePrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null
                ? prefab
                : throw new FileNotFoundException(
                    $"Required canonical factory prefab is missing: {path}",
                    path);
        }

        private static Material RequireMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            return material != null
                ? material
                : throw new FileNotFoundException(
                    $"Required canonical factory material is missing: {path}",
                    path);
        }

        private static T FindExactlyOne<T>(
            Scene scene,
            Func<T, bool> predicate = null)
            where T : Component
        {
            T found = null;
            var components = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < components.Length; index++)
            {
                if (components[index].gameObject.scene != scene
                    || (predicate != null && !predicate(components[index])))
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.name}' contains more than one {typeof(T).Name}.");
                }

                found = components[index];
            }

            return found != null
                ? found
                : throw new InvalidOperationException(
                    $"Scene '{scene.name}' contains no {typeof(T).Name}.");
        }


        private static T FindOptional<T>(Scene scene)
            where T : Component
        {
            T found = null;
            var components = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < components.Length; index++)
            {
                if (components[index].gameObject.scene != scene)
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.name}' contains more than one {typeof(T).Name}.");
                }

                found = components[index];
            }

            return found;
        }

        private static T[] FindAllInScene<T>(Scene scene)
            where T : Component
        {
            var all = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var count = 0;
            for (var index = 0; index < all.Length; index++)
            {
                if (all[index].gameObject.scene == scene)
                {
                    count++;
                }
            }

            var result = new T[count];
            var destination = 0;
            for (var index = 0; index < all.Length; index++)
            {
                if (all[index].gameObject.scene == scene)
                {
                    result[destination++] = all[index];
                }
            }

            return result;
        }
    }
}

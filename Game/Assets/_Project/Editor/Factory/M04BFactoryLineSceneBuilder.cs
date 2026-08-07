using System;
using System.Collections.Generic;
using System.IO;
using CML.Content;
using CML.Foundation;
using CML.Simulation.Machines;
using CML.Unity.Airship;
using CML.Unity.Factory;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Crafting;
using CML.Unity.Presentation.Machines;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Builds the isolated M0.4B playable factory slice.
    ///
    /// The scene is intentionally not added to Build Settings and never opens or
    /// edits a production scene. It contains one authoritative simulation root, a
    /// prebuilt line that works immediately, and a separate BUILD-001 test pad.
    /// </summary>
    public static class M04BFactoryLineSceneBuilder
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/92_M04B_FactoryLine_Test.unity";

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
        private const string InventoryHudPrefabPath =
            "Assets/_Project/Art/UI/Inventory/PF_InventoryHUD.prefab";
        private const string ChestHudPrefabPath =
            "Assets/_Project/Art/UI/Chest/PF_ChestHUD.prefab";
        private const string MachineHudPrefabPath =
            "Assets/_Project/Art/UI/Machine/PF_MachineHUD.prefab";
        private const string GeneratedArtRoot =
            "Assets/_Project/Art/FactoryTest";
        private const string GeneratedMaterialsRoot =
            GeneratedArtRoot + "/Materials";
        private const string YardMaterialPath =
            GeneratedMaterialsRoot + "/M_M04B_Yard.mat";
        private const string BuildPadMaterialPath =
            GeneratedMaterialsRoot + "/M_M04B_BuildPad.mat";
        private const string StationMaterialPath =
            GeneratedMaterialsRoot + "/M_M04B_Station.mat";
        private const string ValidHologramMaterialPath =
            GeneratedMaterialsRoot + "/M_M04B_HologramValid.mat";
        private const string InvalidHologramMaterialPath =
            GeneratedMaterialsRoot + "/M_M04B_HologramInvalid.mat";

        private const float BeltSurfaceHeight = 0.60f;
        private const float LaneLength = 4.0f;
        [MenuItem("CML/Factory/Build M0.4B Factory Line Test Scene")]
        public static void BuildScene()
        {
            // Resolve and validate every dependency before touching the editor scene
            // setup. In particular, absence of the approved press is a hard stop:
            // this builder never substitutes production art with a cube.
            var assets = LoadAssets();
            ValidatePressPrefabContract(assets.Press);

            EnsureFolder("Assets/_Project/Scenes");
            EnsureFolder(GeneratedArtRoot);
            EnsureFolder(GeneratedMaterialsRoot);
            var materials = CreateMaterials();

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            var originalActiveScene = SceneManager.GetActiveScene();
            var replaceBatchBootstrapScene =
                Application.isBatchMode && HasCleanUntitledBootstrapScene();
            Scene generatedScene = default;

            try
            {
                CloseExistingGeneratedSceneIfOpen();
                generatedScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    replaceBatchBootstrapScene
                        ? NewSceneMode.Single
                        : NewSceneMode.Additive);
                generatedScene.name = "92_M04B_FactoryLine_Test";
                SceneManager.SetActiveScene(generatedScene);

                BuildContents(generatedScene, assets, materials);
                ValidateScene(generatedScene);

                EditorSceneManager.MarkSceneDirty(generatedScene);
                if (!EditorSceneManager.SaveScene(generatedScene, ScenePath))
                {
                    throw new IOException(
                        $"Unity could not save the M0.4B scene at '{ScenePath}'.");
                }

                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"M04B_FACTORY_SCENE_BUILT path={ScenePath} "
                    + "line=Crate>Funnel>4xBelt>Press>4xBelt>Funnel>Crate "
                    + "building=enabled beltLineCapacity=enabled");
            }
            finally
            {
                if (generatedScene.IsValid()
                    && generatedScene.isLoaded
                    && !replaceBatchBootstrapScene)
                {
                    EditorSceneManager.CloseScene(generatedScene, true);
                }

                if (!replaceBatchBootstrapScene)
                {
                    RestoreOpenSceneSetup(originalSetup, originalActiveScene);
                }
            }
        }

        /// <summary>
        /// Replaces only the obsolete preplaced logistics line in the already playable
        /// M0.4B scene. Keeping the existing player and HUD is intentional: those are
        /// designer-edited assets and are unrelated to the lane-to-module migration.
        /// </summary>
        [MenuItem("CML/Factory/Migrate M0.4B Line To Modules")]
        public static void MigrateExistingSceneToModules()
        {
            var assets = LoadAssets();
            ValidatePressPrefabContract(assets.Press);

            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            var oldLine = FindGameObject(scene, "M04B_PreplacedLine");
            if (oldLine == null)
            {
                throw new InvalidDataException(
                    "The existing M0.4B scene has no M04B_PreplacedLine root.");
            }

            UnityEngine.Object.DestroyImmediate(oldLine);
            var lineRoot = new GameObject("M04B_PreplacedLine").transform;
            CreatePreplacedModuleLine(
                scene,
                lineRoot,
                assets,
                CreateMaterials());

            RepairInventoryHudBindings(scene);
            ValidateScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException(
                    $"Unity could not save the migrated M0.4B scene at '{ScenePath}'.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"M04B_FACTORY_LINE_MIGRATED path={ScenePath} "
                + "nodes=13 lanes=0 modules=10");
        }

        private static void RepairInventoryHudBindings(Scene scene)
        {
            var inventoryHud =
                RequireExactlyOne<InventoryHudController>(scene);
            var simulationRoot =
                RequireExactlyOne<FactoryLineSimulationRoot>(scene);
            var hud = RequireExactlyOne<FactoryHudOrchestrator>(scene);

            SetSerializedReference(
                simulationRoot,
                "inventoryHud",
                inventoryHud);
            SetSerializedReference(
                hud,
                "inventoryHud",
                inventoryHud);
        }

        private static void SetSerializedReference(
            UnityEngine.Object owner,
            string propertyName,
            UnityEngine.Object value)
        {
            var serialized = new SerializedObject(owner);
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidDataException(
                    $"'{owner.GetType().Name}' has no serialized "
                    + $"'{propertyName}' reference.");
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
        }

        [MenuItem("CML/Factory/Diagnose M0.4B Script Binding")]
        public static void DiagnoseScriptBinding()
        {
            const string scriptPath =
                "Assets/_Project/Runtime/Unity/Factory/FactoryLineSimulationRoot.cs";
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            var scriptClass = script != null ? script.GetClass() : null;
            var scene = SceneManager.GetActiveScene();
            var systems = FindGameObject(scene, "M04B_Systems");
            var behaviours = systems != null
                ? systems.GetComponents<MonoBehaviour>()
                : Array.Empty<MonoBehaviour>();
            var componentNames = new List<string>();
            for (var index = 0; index < behaviours.Length; index++)
            {
                componentNames.Add(
                    behaviours[index] == null
                        ? "<missing>"
                        : behaviours[index].GetType().AssemblyQualifiedName);
            }

            Debug.Log(
                $"M04B_SCRIPT_BINDING script={(script != null)} "
                + $"class={scriptClass?.AssemblyQualifiedName ?? "<null>"} "
                + $"components=[{string.Join(" | ", componentNames)}]");
        }

        [MenuItem("CML/Factory/Repair CML.Unity Script Bindings")]
        public static void RepairUnityScriptBindings()
        {
            const string runtimeRoot = "Assets/_Project/Runtime/Unity";
            const string assemblyDefinition =
                runtimeRoot + "/CML.Unity.asmdef";

            AssetDatabase.ImportAsset(
                assemblyDefinition,
                ImportAssetOptions.ForceUpdate
                | ImportAssetOptions.ForceSynchronousImport);

            var scriptGuids = AssetDatabase.FindAssets(
                "t:MonoScript",
                new[] { runtimeRoot });
            for (var index = 0; index < scriptGuids.Length; index++)
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(
                    scriptGuids[index]);
                AssetDatabase.ImportAsset(
                    scriptPath,
                    ImportAssetOptions.ForceUpdate
                    | ImportAssetOptions.ForceSynchronousImport);
            }

            AssetDatabase.Refresh(
                ImportAssetOptions.ForceUpdate
                | ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"CML_UNITY_SCRIPT_BINDINGS_REIMPORTED "
                + $"scripts={scriptGuids.Length}");
        }

        private static void BuildContents(
            Scene scene,
            AssetSet assets,
            MaterialSet materials)
        {
            CreateEnvironment(materials);

            var systems = new GameObject("M04B_Systems");
            var transferBridge = systems.AddComponent<TransferCommandBridge>();
            var craftingBridge = systems.AddComponent<CraftingCommandBridge>();
            var hud = systems.AddComponent<FactoryHudOrchestrator>();
            var simulationRoot = systems.AddComponent<FactoryLineSimulationRoot>();

            var player = CreatePlayer(out var camera, out var mouseLook);
            var inventoryHud = InstantiatePrefabInScene<InventoryHudController>(
                assets.InventoryHud,
                scene,
                "HUD_Inventory");
            var chestHud = InstantiatePrefabInScene<ChestHudController>(
                assets.ChestHud,
                scene,
                "HUD_Chest");
            var prefabLocalBridge = chestHud.GetComponent<TransferCommandBridge>();
            if (prefabLocalBridge != null && prefabLocalBridge != transferBridge)
            {
                // PF_ChestHUD is independently reviewable and therefore carries a
                // local bridge. This integrated scene routes it through the sole
                // composition bridge instead of leaving a second unused command path.
                UnityEngine.Object.DestroyImmediate(prefabLocalBridge);
            }

            var machineHud = InstantiatePrefabInScene<MachineHudController>(
                assets.MachineHud,
                scene,
                "HUD_Machine");

            hud.ConfigureUi(
                inventoryHud,
                chestHud,
                machineHud,
                transferBridge,
                mouseLook,
                gameplayInput: null,
                workbenchController: null,
                craftingCommandBridge: craftingBridge);
            simulationRoot.Configure(
                transferBridge,
                inventoryHud,
                hud,
                assets.IronIngot,
                assets.IronPlate);

            var interactor = player.AddComponent<FactoryCentralInteractor>();
            interactor.Configure(
                camera,
                hud,
                inventoryHud.GetComponentInParent<
                    UnityEngine.UIElements.UIDocument>(),
                3.25f);

            var authoredRoot = new GameObject("M04B_PreplacedLine").transform;
            var runtimeBuildRoot = new GameObject("M04B_RuntimeBuilds").transform;
            CreatePreplacedModuleLine(
                scene,
                authoredRoot,
                assets,
                materials);

            var buildControllerObject = new GameObject("BUILD_001_Controller");
            var buildController =
                buildControllerObject.AddComponent<FactoryBuildController>();
            buildController.Configure(
                simulationRoot,
                hud,
                camera,
                runtimeBuildRoot,
                assets.Crate,
                assets.Funnel,
                assets.BeltStraight,
                assets.Press,
                assets.IronIngot,
                assets.IronPlate,
                materials.ValidHologram,
                materials.InvalidHologram,
                assets.BeltDrive,
                assets.BeltCurve,
                assets.BeltIncline,
                assets.BeltCurveLeft);

            // Salvaging lives on its own object because it must work with empty hands,
            // which is exactly when the build controller stands down.
            var dismantleControllerObject =
                new GameObject("SALVAGE_001_Controller");
            dismantleControllerObject
                .AddComponent<FactoryDismantleController>()
                .Configure(simulationRoot, hud, camera);

            CreateInstructionSigns(materials.Station);
        }

        private static void CreatePreplacedModuleLine(
            Scene scene,
            Transform lineRoot,
            AssetSet assets,
            MaterialSet materials)
        {
            const float lineX = -4f;

            var sourceCrate = InstantiatePrefab(
                assets.Crate,
                scene,
                lineRoot,
                "M04B_SourceCrate",
                new Vector3(lineX, 0f, -7f),
                Quaternion.Euler(0f, 180f, 0f));
            var sourceCrateSocket =
                RequireChild(sourceCrate.transform, "PORT_ItemIO");
            ConfigureNode(
                sourceCrate,
                FactoryLineSimulationRoot.SourceCrateId,
                MachineNodeKind.Buffer,
                sourceCrateSocket,
                sourceCrateSocket,
                FactoryInteractionKind.Chest,
                "Apri Cassa di legno");

            var sourceFunnel = InstantiatePrefab(
                assets.Funnel,
                scene,
                lineRoot,
                "M04B_InputFunnel",
                new Vector3(lineX, 0f, -6f),
                Quaternion.Euler(0f, 180f, 0f));
            var sourceFunnelInventory =
                RequireChild(sourceFunnel.transform, "PORT_Inventory");
            var sourceFunnelBelt =
                RequireChild(sourceFunnel.transform, "PORT_Belt");
            ConfigureModuleNode(
                sourceFunnel,
                FactoryLineSimulationRoot.InputFunnelId,
                MachineNodeKind.Funnel,
                sourceFunnelInventory,
                sourceFunnelBelt,
                assets);

            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_FeedBelt01",
                FactoryLineSimulationRoot.FeedBelt01Id,
                new Vector3(lineX, 0f, -5f),
                assets.BeltDrive,
                assets,
                out var feedBelt01);
            AlignMarker(
                sourceFunnel.transform,
                sourceFunnelBelt,
                RequireChild(feedBelt01.transform, "PORT_ModuleInput"));
            AlignMarker(
                sourceCrate.transform,
                sourceCrateSocket,
                sourceFunnelInventory);
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_FeedBelt02",
                FactoryLineSimulationRoot.FeedBelt02Id,
                new Vector3(lineX, 0f, -4f),
                assets.BeltStraight,
                assets,
                out _);
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_FeedBelt03",
                FactoryLineSimulationRoot.FeedBelt03Id,
                new Vector3(lineX, 0f, -3f),
                assets.BeltStraight,
                assets,
                out _);
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_FeedBelt04",
                FactoryLineSimulationRoot.FeedBelt04Id,
                new Vector3(lineX, 0f, -2f),
                assets.BeltStraight,
                assets,
                out _);

            var press = InstantiatePrefab(
                assets.Press,
                scene,
                lineRoot,
                "M04B_MechanicalPress",
                new Vector3(lineX, 0f, -1f),
                Quaternion.identity);
            var pressInput = RequireChild(press.transform, "PORT_ItemIn");
            var pressOutput = RequireChild(press.transform, "PORT_ItemOut");
            var pressRam = RequireChild(press.transform, "ANM_PressRam");
            var workpiece = RequireChild(press.transform, "REF_Workpiece");
            EnsureCollider(press, new Vector3(1.8f, 2.5f, 1.4f));
            ConfigureNode(
                press,
                FactoryLineSimulationRoot.PressId,
                MachineNodeKind.Machine,
                pressInput,
                pressOutput,
                FactoryInteractionKind.Machine,
                "Ispeziona Pressa");
            var pressPresenter = press.GetComponent<FactoryPressPresenter>()
                ?? press.AddComponent<FactoryPressPresenter>();
            pressPresenter.ConfigureAuthoring(
                FactoryLineSimulationRoot.PressId,
                pressRam,
                workpiece,
                assets.IronIngot,
                assets.IronPlate);
            pressPresenter.ConfigureMotion(
                Vector3.down,
                FactoryPressPresenter.ProductionRamTravelMetres,
                new Vector3(1.4f, 0.2f, 1.3f));
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_DrainBelt01",
                FactoryLineSimulationRoot.DrainBelt01Id,
                new Vector3(lineX, 0f, 0f),
                assets.BeltStraight,
                assets,
                out _);
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_DrainBelt02",
                FactoryLineSimulationRoot.DrainBelt02Id,
                new Vector3(lineX, 0f, 1f),
                assets.BeltStraight,
                assets,
                out _);
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_DrainBelt03",
                FactoryLineSimulationRoot.DrainBelt03Id,
                new Vector3(lineX, 0f, 2f),
                assets.BeltStraight,
                assets,
                out _);
            CreateBeltModule(
                scene,
                lineRoot,
                "M04B_DrainBelt04",
                FactoryLineSimulationRoot.DrainBelt04Id,
                new Vector3(lineX, 0f, 3f),
                assets.BeltStraight,
                assets,
                out var drainBelt04);

            var outputFunnel = InstantiatePrefab(
                assets.Funnel,
                scene,
                lineRoot,
                "M04B_OutputFunnel",
                new Vector3(lineX, 0f, 4f),
                Quaternion.identity);
            var outputFunnelInventory =
                RequireChild(outputFunnel.transform, "PORT_Inventory");
            var outputFunnelBelt =
                RequireChild(outputFunnel.transform, "PORT_Belt");
            ConfigureModuleNode(
                outputFunnel,
                FactoryLineSimulationRoot.OutputFunnelId,
                MachineNodeKind.Funnel,
                outputFunnelInventory,
                outputFunnelBelt,
                assets);
            AlignMarker(
                outputFunnel.transform,
                outputFunnelBelt,
                RequireChild(drainBelt04.transform, "PORT_ModuleOutput"));

            var sinkCrate = InstantiatePrefab(
                assets.Crate,
                scene,
                lineRoot,
                "M04B_SinkCrate",
                new Vector3(lineX, 0f, 5f),
                Quaternion.identity);
            var sinkCrateSocket =
                RequireChild(sinkCrate.transform, "PORT_ItemIO");
            AlignMarker(
                sinkCrate.transform,
                sinkCrateSocket,
                outputFunnelInventory);
            ConfigureNode(
                sinkCrate,
                FactoryLineSimulationRoot.SinkCrateId,
                MachineNodeKind.Buffer,
                sinkCrateSocket,
                sinkCrateSocket,
                FactoryInteractionKind.Chest,
                "Apri Cassa di legno");

        }

        private static void CreateBeltModule(
            Scene scene,
            Transform parent,
            string name,
            StableId nodeId,
            Vector3 position,
            GameObject prefab,
            AssetSet assets,
            out GameObject module)
        {
            module = InstantiatePrefab(
                prefab,
                scene,
                parent,
                name,
                position,
                Quaternion.identity);
            var input = CreateSocket(
                module.transform,
                "PORT_ModuleInput",
                module.transform.TransformPoint(
                    new Vector3(0f, BeltSurfaceHeight, -0.5f)));
            var output = CreateSocket(
                module.transform,
                "PORT_ModuleOutput",
                module.transform.TransformPoint(
                    new Vector3(0f, BeltSurfaceHeight, 0.5f)));
            ConfigureModuleNode(
                module,
                nodeId,
                MachineNodeKind.BeltModule,
                input,
                output,
                assets);
        }

        private static void ConfigureModuleNode(
            GameObject instance,
            StableId id,
            MachineNodeKind nodeKind,
            Transform inputSocket,
            Transform outputSocket,
            AssetSet assets)
        {
            var anchor = instance.GetComponent<FactoryNodeAnchor>()
                ?? instance.AddComponent<FactoryNodeAnchor>();
            anchor.Configure(id, nodeKind, inputSocket, outputSocket);

            var presenter =
                instance.GetComponent<FactoryLogisticsModulePresenter>()
                ?? instance.AddComponent<FactoryLogisticsModulePresenter>();
            presenter.ConfigureAuthoring(
                id,
                nodeKind,
                assets.IronIngot,
                assets.IronPlate);
        }

        private static void CreatePreplacedLine(
            Scene scene,
            Transform lineRoot,
            AssetSet assets,
            MaterialSet materials)
        {
            var forward = Vector3.forward;
            var lineX = -3.5f;

            var press = InstantiatePrefab(
                assets.Press,
                scene,
                lineRoot,
                "M04B_MechanicalPress",
                new Vector3(lineX, 0f, 0f),
                Quaternion.identity);
            var pressInput = RequireChild(press.transform, "PORT_ItemIn");
            var pressOutput = RequireChild(press.transform, "PORT_ItemOut");
            var pressRam = RequireChild(press.transform, "ANM_PressRam");
            var workpiece = RequireChild(press.transform, "REF_Workpiece");
            EnsureCollider(press, new Vector3(1.8f, 2.5f, 1.4f));
            ConfigureNode(
                press,
                FactoryLineSimulationRoot.PressId,
                MachineNodeKind.Machine,
                pressInput,
                pressOutput,
                FactoryInteractionKind.Machine,
                "ispeziona Pressa");
            var pressPresenter = press.GetComponent<FactoryPressPresenter>()
                ?? press.AddComponent<FactoryPressPresenter>();
            pressPresenter.ConfigureAuthoring(
                FactoryLineSimulationRoot.PressId,
                pressRam,
                workpiece,
                assets.IronIngot,
                assets.IronPlate);
            pressPresenter.ConfigureMotion(
                Vector3.down,
                FactoryPressPresenter.ProductionRamTravelMetres,
                new Vector3(1.4f, 0.2f, 1.3f));

            var feedEnd = HorizontalAtBeltHeight(pressInput.position);
            var feedStart = feedEnd - forward * LaneLength;
            var sourceFunnelPosition = feedStart;
            sourceFunnelPosition.y = 0f;
            var sourceFunnel = InstantiatePrefab(
                assets.Funnel,
                scene,
                lineRoot,
                "M04B_InputFunnel",
                sourceFunnelPosition,
                Quaternion.Euler(0f, 180f, 0f));
            var sourceFunnelInventory =
                RequireChild(sourceFunnel.transform, "PORT_Inventory");
            var sourceFunnelBelt =
                RequireChild(sourceFunnel.transform, "PORT_Belt");
            ConfigureNode(
                sourceFunnel,
                FactoryLineSimulationRoot.InputFunnelId,
                MachineNodeKind.Funnel,
                sourceFunnelBelt,
                sourceFunnelBelt,
                FactoryInteractionKind.Machine,
                "ispeziona Imbuto d’ingresso");

            var sourceCratePosition =
                sourceFunnelInventory.position - forward * 0.72f;
            sourceCratePosition.y = 0f;
            var sourceCrate = InstantiatePrefab(
                assets.Crate,
                scene,
                lineRoot,
                "M04B_SourceCrate",
                sourceCratePosition,
                Quaternion.Euler(0f, 180f, 0f));
            var sourceCrateSocket =
                RequireChild(sourceCrate.transform, "PORT_ItemIO");
            AlignMarker(
                sourceCrate.transform,
                sourceCrateSocket,
                sourceFunnelInventory);
            ConfigureNode(
                sourceCrate,
                FactoryLineSimulationRoot.SourceCrateId,
                MachineNodeKind.Buffer,
                sourceCrateSocket,
                sourceCrateSocket,
                FactoryInteractionKind.Chest,
                "Apri Cassa di legno");

            var feedLane = CreateLane(
                scene,
                lineRoot,
                "M04B_FeedLane",
                FactoryLineSimulationRoot.FeedLaneId,
                sourceFunnelBelt.position,
                feedEnd,
                assets,
                driveSegmentIndex: 3,
                out var driveUnit);

            var drainStart = HorizontalAtBeltHeight(pressOutput.position);
            var drainEnd = drainStart + forward * LaneLength;
            var outputFunnelPosition = drainEnd;
            outputFunnelPosition.y = 0f;
            var outputFunnel = InstantiatePrefab(
                assets.Funnel,
                scene,
                lineRoot,
                "M04B_OutputFunnel",
                outputFunnelPosition,
                Quaternion.identity);
            var outputFunnelInventory =
                RequireChild(outputFunnel.transform, "PORT_Inventory");
            var outputFunnelBelt =
                RequireChild(outputFunnel.transform, "PORT_Belt");
            ConfigureNode(
                outputFunnel,
                FactoryLineSimulationRoot.OutputFunnelId,
                MachineNodeKind.Funnel,
                outputFunnelBelt,
                outputFunnelBelt,
                FactoryInteractionKind.Machine,
                "ispeziona Imbuto d’uscita");

            var sinkCratePosition =
                outputFunnelInventory.position + forward * 0.72f;
            sinkCratePosition.y = 0f;
            var sinkCrate = InstantiatePrefab(
                assets.Crate,
                scene,
                lineRoot,
                "M04B_SinkCrate",
                sinkCratePosition,
                Quaternion.identity);
            var sinkCrateSocket =
                RequireChild(sinkCrate.transform, "PORT_ItemIO");
            AlignMarker(
                sinkCrate.transform,
                sinkCrateSocket,
                outputFunnelInventory);
            ConfigureNode(
                sinkCrate,
                FactoryLineSimulationRoot.SinkCrateId,
                MachineNodeKind.Buffer,
                sinkCrateSocket,
                sinkCrateSocket,
                FactoryInteractionKind.Chest,
                "Apri Cassa di legno");

            CreateLane(
                scene,
                lineRoot,
                "M04B_DrainLane",
                FactoryLineSimulationRoot.DrainLaneId,
                drainStart,
                outputFunnelBelt.position,
                assets,
                driveSegmentIndex: -1,
                out _);

        }

        private static FactoryBeltLanePresenter CreateLane(
            Scene scene,
            Transform parent,
            string name,
            StableId laneId,
            Vector3 start,
            Vector3 end,
            AssetSet assets,
            int driveSegmentIndex,
            out GameObject driveUnit)
        {
            var laneRoot = new GameObject(name).transform;
            laneRoot.SetParent(parent, false);

            var pathStart = CreateSocket(laneRoot, "PATH_Start", start);
            var pathEnd = CreateSocket(laneRoot, "PATH_End", end);
            var itemVisualRoot = new GameObject("ItemVisuals").transform;
            itemVisualRoot.SetParent(laneRoot, false);

            var direction = Vector3.ProjectOnPlane(end - start, Vector3.up);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Lane '{name}' does not have a horizontal direction.");
            }

            direction.Normalize();
            var rotation = Quaternion.LookRotation(direction, Vector3.up);
            var distance = Vector3.Distance(start, end);
            var segmentCount = Mathf.RoundToInt(distance);
            if (segmentCount != 4 || Mathf.Abs(distance - LaneLength) > 0.02f)
            {
                throw new InvalidOperationException(
                    $"Lane '{name}' must be exactly four one-metre modules.");
            }

            driveUnit = null;
            for (var index = 0; index < segmentCount; index++)
            {
                var prefab = index == driveSegmentIndex
                    ? assets.BeltDrive
                    : assets.BeltStraight;
                var centre = Vector3.Lerp(
                    start,
                    end,
                    (index + 0.5f) / segmentCount);
                centre.y -= BeltSurfaceHeight;
                var segment = InstantiatePrefab(
                    prefab,
                    scene,
                    laneRoot,
                    index == driveSegmentIndex
                        ? $"DriveUnit_{index + 1:00}"
                        : $"Belt_{index + 1:00}",
                    centre,
                    rotation);
                if (index == driveSegmentIndex)
                {
                    driveUnit = segment;
                }
            }

            var presenter =
                laneRoot.gameObject.AddComponent<FactoryBeltLanePresenter>();
            presenter.ConfigureAuthoring(
                laneId,
                new[] { pathStart, pathEnd },
                itemVisualRoot,
                new Vector3(0f, 0.12f, 0f));
            return presenter;
        }

        private static GameObject CreatePlayer(
            out Camera camera,
            out FirstPersonMouseLook mouseLook)
        {
            var player = new GameObject("M04B_FirstPersonPlayer");
            player.transform.SetPositionAndRotation(
                new Vector3(-3.5f, 0.08f, -10.5f),
                Quaternion.identity);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.30f;
            controller.center = new Vector3(0f, 0.90f, 0f);
            controller.stepOffset = 0.32f;
            controller.slopeLimit = 48f;

            var yaw = new GameObject("ViewYaw").transform;
            yaw.SetParent(player.transform, false);
            yaw.localPosition = new Vector3(0f, 1.62f, 0f);
            var pitch = new GameObject("ViewPitch").transform;
            pitch.SetParent(yaw, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pitch, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 180f;
            camera.fieldOfView = 72f;
            cameraObject.AddComponent<AudioListener>();

            var motor = player.AddComponent<FirstPersonCharacterMotor>();
            motor.Configure(controller, yaw, null);
            mouseLook = player.AddComponent<FirstPersonMouseLook>();
            mouseLook.Configure(yaw, pitch);
            var input = player.AddComponent<FactoryFirstPersonInput>();
            input.Configure(motor, mouseLook);
            return player;
        }

        private static void CreateEnvironment(MaterialSet materials)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor =
                new Color(0.55f, 0.68f, 0.78f, 1f);
            RenderSettings.ambientEquatorColor =
                new Color(0.34f, 0.40f, 0.38f, 1f);
            RenderSettings.ambientGroundColor =
                new Color(0.18f, 0.20f, 0.18f, 1f);
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.76f, 0.80f, 1f);
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 145f;

            var keyObject = new GameObject("Key Light");
            keyObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.90f, 0.76f, 1f);
            key.intensity = 1.35f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.78f;

            var fillObject = new GameObject("Fill Light");
            fillObject.transform.rotation = Quaternion.Euler(58f, 142f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.66f, 0.80f, 1f, 1f);
            fill.intensity = 0.32f;
            fill.shadows = LightShadows.None;

            CreateCube(
                "M04B_TestYard",
                new Vector3(0f, -0.25f, 0f),
                new Vector3(28f, 0.5f, 24f),
                materials.Yard,
                addCollider: true);

            var buildPad = CreateCube(
                "BUILD_001_FreeArea",
                new Vector3(7.2f, 0.025f, 0f),
                new Vector3(10f, 0.05f, 12f),
                materials.BuildPad,
                addCollider: true);
            buildPad.AddComponent<FactoryBuildSurface>();

            // Low visual divider: it makes the two purposes of the test yard
            // immediately legible without blocking movement or build raycasts.
            CreateCube(
                "YardDivider",
                new Vector3(1.7f, 0.10f, 0f),
                new Vector3(0.12f, 0.20f, 18f),
                materials.Station,
                addCollider: false);
        }

        private static void CreateInstructionSigns(Material material)
        {
            CreateSign(
                "SIGN_PreplacedLine",
                new Vector3(-3.5f, 1.25f, -8.7f),
                "LINEA ATTIVA\nE: apri / ispeziona   TAB: inventario",
                material);
            CreateSign(
                "SIGN_BuildArea",
                new Vector3(7.2f, 1.25f, -6.2f),
                "COSTRUZIONE\nSeleziona dalla hotbar   Click: piazza   R: ruota",
                material);
        }

        private static void CreateSign(
            string name,
            Vector3 position,
            string text,
            Material material)
        {
            var board = CreateCube(
                name,
                position,
                new Vector3(5.8f, 1.35f, 0.08f),
                material,
                addCollider: false);
            var textObject = new GameObject("Label");
            textObject.transform.SetParent(board.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.055f);
            textObject.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);
            var mesh = textObject.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 52;
            mesh.characterSize = 0.055f;
            mesh.color = new Color(0.94f, 0.97f, 0.90f, 1f);
        }

        private static void ConfigureNode(
            GameObject instance,
            StableId id,
            MachineNodeKind nodeKind,
            Transform inputSocket,
            Transform outputSocket,
            FactoryInteractionKind interactionKind,
            string prompt)
        {
            var anchor = instance.GetComponent<FactoryNodeAnchor>()
                ?? instance.AddComponent<FactoryNodeAnchor>();
            anchor.Configure(id, nodeKind, inputSocket, outputSocket);

            var target = instance.GetComponent<FactoryInteractionTarget>()
                ?? instance.AddComponent<FactoryInteractionTarget>();
            target.Configure(id, interactionKind, prompt);
        }

        private static GameObject InstantiatePrefab(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation)
        {
            var instance =
                PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate prefab '{prefab.name}'.");
            }

            instance.name = name;
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        private static T InstantiatePrefabInScene<T>(
            GameObject prefab,
            Scene scene,
            string name)
            where T : Component
        {
            var instance =
                PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate UI prefab '{prefab.name}'.");
            }

            instance.name = name;
            var component = instance.GetComponentInChildren<T>(true);
            if (component == null)
            {
                throw new InvalidDataException(
                    $"Prefab '{prefab.name}' does not contain {typeof(T).Name}.");
            }

            return component;
        }

        private static Transform CreateSocket(
            Transform parent,
            string name,
            Vector3 worldPosition)
        {
            var socket = new GameObject(name).transform;
            socket.SetParent(parent, true);
            socket.position = worldPosition;
            socket.rotation = parent.rotation;
            return socket;
        }

        private static void AlignMarker(
            Transform movableRoot,
            Transform movableMarker,
            Transform targetMarker)
        {
            movableRoot.position += targetMarker.position - movableMarker.position;
            if (Vector3.Distance(movableMarker.position, targetMarker.position) > 0.001f)
            {
                throw new InvalidDataException(
                    $"Could not align '{movableMarker.name}' with "
                    + $"'{targetMarker.name}'.");
            }
        }

        private static GameObject CreateCube(
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool addCollider)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!addCollider)
            {
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            }

            return cube;
        }

        private static void EnsureCollider(GameObject root, Vector3 fallbackSize)
        {
            if (root.GetComponentInChildren<Collider>(true) != null)
            {
                return;
            }

            var collider = root.AddComponent<BoxCollider>();
            collider.size = fallbackSize;
            collider.center = new Vector3(0f, fallbackSize.y * 0.5f, 0f);
        }

        private static Vector3 HorizontalAtBeltHeight(Vector3 source) =>
            new Vector3(source.x, BeltSurfaceHeight, source.z);

        private static Transform RequireChild(Transform root, string name)
        {
            var result = FindChild(root, name);
            if (result == null)
            {
                throw new InvalidDataException(
                    $"'{root.name}' is missing required marker '{name}'.");
            }

            return result;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static AssetSet LoadAssets()
        {
            return new AssetSet(
                RequirePrefab(CratePrefabPath),
                RequirePrefab(FunnelPrefabPath),
                RequirePrefab(BeltStraightPrefabPath),
                RequirePrefab(BeltDrivePrefabPath),
                RequirePrefab(BeltCurvePrefabPath),
                RequirePrefab(BeltInclinePrefabPath),
                RequirePrefab(BeltCurveLeftPrefabPath),
                RequirePrefab(PressPrefabPath),
                RequirePrefab(IronIngotPrefabPath),
                RequirePrefab(IronPlatePrefabPath),
                RequirePrefab(InventoryHudPrefabPath),
                RequirePrefab(ChestHudPrefabPath),
                RequirePrefab(MachineHudPrefabPath));
        }

        private static GameObject RequirePrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var pressSpecific = string.Equals(
                    path,
                    PressPrefabPath,
                    StringComparison.Ordinal);
                throw new FileNotFoundException(
                    pressSpecific
                        ? "M0.4B scene not built: the approved mechanical press "
                          + $"prefab is required at '{PressPrefabPath}'. "
                          + "No placeholder will be generated."
                        : $"Required prefab is missing at '{path}'.",
                    path);
            }

            return prefab;
        }

        private static void ValidatePressPrefabContract(GameObject press)
        {
            var requiredMarkers = new[]
            {
                "PORT_ItemIn",
                "PORT_ItemOut",
                "REF_Interact",
                "REF_Workpiece",
                "ANM_PressRam"
            };
            var missing = new List<string>();
            for (var index = 0; index < requiredMarkers.Length; index++)
            {
                if (FindChild(press.transform, requiredMarkers[index]) == null)
                {
                    missing.Add(requiredMarkers[index]);
                }
            }

            if (missing.Count > 0)
            {
                throw new InvalidDataException(
                    $"Mechanical press prefab '{PressPrefabPath}' is incomplete. "
                    + $"Missing: {string.Join(", ", missing)}.");
            }

            if (press.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidDataException(
                    $"Mechanical press prefab '{PressPrefabPath}' has no renderers.");
            }
        }

        private static MaterialSet CreateMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Universal Render Pipeline/Lit is required for the M0.4B scene.");
            }

            var yard = UpsertMaterial(
                YardMaterialPath,
                shader,
                new Color(0.38f, 0.44f, 0.36f, 1f),
                metallic: 0f,
                smoothness: 0.16f);
            var buildPad = UpsertMaterial(
                BuildPadMaterialPath,
                shader,
                new Color(0.20f, 0.38f, 0.40f, 1f),
                metallic: 0.08f,
                smoothness: 0.24f);
            var station = UpsertMaterial(
                StationMaterialPath,
                shader,
                new Color(0.16f, 0.20f, 0.19f, 1f),
                metallic: 0.18f,
                smoothness: 0.30f);
            var valid = UpsertHologram(
                ValidHologramMaterialPath,
                shader,
                new Color(0.14f, 0.95f, 0.49f, 0.42f));
            var invalid = UpsertHologram(
                InvalidHologramMaterialPath,
                shader,
                new Color(1.0f, 0.16f, 0.10f, 0.46f));
            return new MaterialSet(
                yard,
                buildPad,
                station,
                valid,
                invalid);
        }

        private static Material UpsertMaterial(
            string path,
            Shader shader,
            Color color,
            float metallic,
            float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            SetColor(material, "_BaseColor", color);
            SetColor(material, "_Color", color);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", smoothness);
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material UpsertHologram(
            string path,
            Shader shader,
            Color color)
        {
            var material = UpsertMaterial(
                path,
                shader,
                color,
                metallic: 0f,
                smoothness: 0.18f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
            SetFloat(material, "_Surface", 1f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_SrcBlend", 5f);
            SetFloat(material, "_DstBlend", 10f);
            SetFloat(material, "_ZWrite", 0f);
            SetColor(material, "_EmissionColor", color * 0.45f);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidateScene(Scene scene)
        {
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
                    "The normal backpack and chest-side backpack must use the "
                    + "same authoritative InventoryHudController.");
            }

            var anchors = ComponentsInScene<FactoryNodeAnchor>(scene);
            var expectedNodes = new Dictionary<StableId, MachineNodeKind>
            {
                [FactoryLineSimulationRoot.SourceCrateId] = MachineNodeKind.Buffer,
                [FactoryLineSimulationRoot.InputFunnelId] = MachineNodeKind.Funnel,
                [FactoryLineSimulationRoot.FeedBelt01Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.FeedBelt02Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.FeedBelt03Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.FeedBelt04Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.PressId] = MachineNodeKind.Machine,
                [FactoryLineSimulationRoot.DrainBelt01Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.DrainBelt02Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.DrainBelt03Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.DrainBelt04Id] =
                    MachineNodeKind.BeltModule,
                [FactoryLineSimulationRoot.OutputFunnelId] = MachineNodeKind.Funnel,
                [FactoryLineSimulationRoot.SinkCrateId] = MachineNodeKind.Buffer
            };
            if (anchors.Count != expectedNodes.Count)
            {
                throw new InvalidDataException(
                    $"M0.4B needs {expectedNodes.Count} preplaced node anchors, "
                    + $"found {anchors.Count}.");
            }

            foreach (var anchor in anchors)
            {
                if (!expectedNodes.TryGetValue(anchor.NodeId, out var expectedKind)
                    || anchor.NodeKind != expectedKind
                    || anchor.InputSocket == null
                    || anchor.OutputSocket == null)
                {
                    throw new InvalidDataException(
                        $"Invalid factory node anchor on '{anchor.name}'.");
                }
            }

            if (ComponentsInScene<FactoryBeltLanePresenter>(scene).Count != 0)
            {
                throw new InvalidDataException(
                    "M0.4B must not contain the retired endpoint-lane presenter.");
            }

            var modulePresenters =
                ComponentsInScene<FactoryLogisticsModulePresenter>(scene);
            if (modulePresenters.Count != 10)
            {
                throw new InvalidDataException(
                    "The two Funnels and eight BeltModules need read-only presenters.");
            }

            var press = RequireExactlyOne<FactoryPressPresenter>(scene);
            if (press.MachineId != FactoryLineSimulationRoot.PressId)
            {
                throw new InvalidDataException(
                    "The press presenter is not bound to the authoritative press id.");
            }

            var interactions = ComponentsInScene<FactoryInteractionTarget>(scene);
            if (interactions.Count != 3)
            {
                throw new InvalidDataException(
                    "The two chests and press expose an E interaction.");
            }

            if (FindGameObject(scene, "M04B_SourceCrate") == null
                || FindGameObject(scene, "M04B_SinkCrate") == null
                || FindGameObject(scene, "M04B_MechanicalPress") == null
                || FindGameObject(scene, "BUILD_001_FreeArea") == null
                || FindGameObject(scene, "Main Camera") == null)
            {
                throw new InvalidDataException(
                    "M0.4B scene is missing a required named object.");
            }

            ValidatePreplacedLogisticsContacts(scene);

            if (HasMissingScripts(scene))
            {
                throw new InvalidDataException(
                    "M0.4B scene contains one or more missing MonoBehaviour scripts.");
            }
        }

        private static void ValidatePreplacedLogisticsContacts(Scene scene)
        {
            var sourceCrate = FindGameObject(scene, "M04B_SourceCrate");
            var inputFunnel = FindGameObject(scene, "M04B_InputFunnel");
            var feedBelt = FindGameObject(scene, "M04B_FeedBelt01");
            var drainBelt = FindGameObject(scene, "M04B_DrainBelt04");
            var outputFunnel = FindGameObject(scene, "M04B_OutputFunnel");
            var sinkCrate = FindGameObject(scene, "M04B_SinkCrate");
            if (sourceCrate == null
                || inputFunnel == null
                || feedBelt == null
                || drainBelt == null
                || outputFunnel == null
                || sinkCrate == null)
            {
                throw new InvalidDataException(
                    "M0.4B is missing a preplaced logistics contact object.");
            }

            RequireMarkersTouch(
                sourceCrate.transform,
                "PORT_ItemIO",
                inputFunnel.transform,
                "PORT_Inventory");
            RequireMarkersTouch(
                inputFunnel.transform,
                "PORT_Belt",
                feedBelt.transform,
                "PORT_ModuleInput");
            RequireMarkersTouch(
                drainBelt.transform,
                "PORT_ModuleOutput",
                outputFunnel.transform,
                "PORT_Belt");
            RequireMarkersTouch(
                outputFunnel.transform,
                "PORT_Inventory",
                sinkCrate.transform,
                "PORT_ItemIO");
        }

        private static void RequireMarkersTouch(
            Transform firstRoot,
            string firstMarkerName,
            Transform secondRoot,
            string secondMarkerName)
        {
            var firstMarker = RequireChild(firstRoot, firstMarkerName);
            var secondMarker = RequireChild(secondRoot, secondMarkerName);
            var separation =
                Vector3.Distance(firstMarker.position, secondMarker.position);
            if (separation > 0.001f)
            {
                throw new InvalidDataException(
                    $"Disconnected preplaced logistics ports: "
                    + $"'{firstRoot.name}/{firstMarkerName}' and "
                    + $"'{secondRoot.name}/{secondMarkerName}' are "
                    + $"{separation:0.###} m apart.");
            }
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

        private static T RequireExactlyOne<T>(Scene scene)
            where T : Component
        {
            var matches = ComponentsInScene<T>(scene);
            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"Scene requires exactly one {typeof(T).Name}; "
                    + $"found {matches.Count}.");
            }

            return matches[0];
        }

        private static List<T> ComponentsInScene<T>(Scene scene)
            where T : Component
        {
            var result = new List<T>();
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                result.AddRange(
                    roots[rootIndex].GetComponentsInChildren<T>(true));
            }

            return result;
        }

        private static GameObject FindGameObject(Scene scene, string name)
        {
            GameObject result = null;
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                foreach (var child in roots[rootIndex]
                    .GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(child.name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (result != null)
                    {
                        return null;
                    }

                    result = child.gameObject;
                }
            }

            return result;
        }

        private static bool HasMissingScripts(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                foreach (var child in roots[rootIndex]
                    .GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            child.gameObject)
                        != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RestoreOpenSceneSetup(
            SceneSetup[] originalSetup,
            Scene originalActiveScene)
        {
            // The generated scene was additive, so in the normal path all original
            // scenes and their unsaved in-memory edits are still present. Avoid
            // reloading them. RestoreSceneManagerSetup is reserved for an exceptional
            // path where Unity did change the open set.
            if (MatchesOpenSceneSetup(originalSetup))
            {
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                }

                return;
            }

            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }

        private static void CloseExistingGeneratedSceneIfOpen()
        {
            for (var index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!string.Equals(
                        scene.path,
                        ScenePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        $"Cannot rebuild '{ScenePath}' while that scene has "
                        + "unsaved changes. Save or discard those changes first.");
                }

                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool HasCleanUntitledBootstrapScene()
        {
            if (SceneManager.sceneCount != 1)
            {
                return false;
            }

            var scene = SceneManager.GetSceneAt(0);
            return scene.IsValid()
                && scene.isLoaded
                && !scene.isDirty
                && string.IsNullOrEmpty(scene.path);
        }

        private static bool MatchesOpenSceneSetup(SceneSetup[] setup)
        {
            var current = EditorSceneManager.GetSceneManagerSetup();
            if (current.Length != setup.Length)
            {
                return false;
            }

            for (var index = 0; index < setup.Length; index++)
            {
                if (!string.Equals(
                        current[index].path,
                        setup[index].path,
                        StringComparison.Ordinal)
                    || current[index].isLoaded != setup[index].isLoaded)
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private static void SetColor(
            Material material,
            string property,
            Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetFloat(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private readonly struct AssetSet
        {
            public AssetSet(
                GameObject crate,
                GameObject funnel,
                GameObject beltStraight,
                GameObject beltDrive,
                GameObject beltCurve,
                GameObject beltIncline,
                GameObject beltCurveLeft,
                GameObject press,
                GameObject ironIngot,
                GameObject ironPlate,
                GameObject inventoryHud,
                GameObject chestHud,
                GameObject machineHud)
            {
                Crate = crate;
                Funnel = funnel;
                BeltStraight = beltStraight;
                BeltDrive = beltDrive;
                BeltCurve = beltCurve;
                BeltIncline = beltIncline;
                BeltCurveLeft = beltCurveLeft;
                Press = press;
                IronIngot = ironIngot;
                IronPlate = ironPlate;
                InventoryHud = inventoryHud;
                ChestHud = chestHud;
                MachineHud = machineHud;
            }

            public GameObject Crate { get; }

            public GameObject Funnel { get; }

            public GameObject BeltStraight { get; }

            public GameObject BeltDrive { get; }

            public GameObject BeltCurve { get; }

            public GameObject BeltIncline { get; }

            public GameObject BeltCurveLeft { get; }

            public GameObject Press { get; }

            public GameObject IronIngot { get; }

            public GameObject IronPlate { get; }

            public GameObject InventoryHud { get; }

            public GameObject ChestHud { get; }

            public GameObject MachineHud { get; }
        }

        private readonly struct MaterialSet
        {
            public MaterialSet(
                Material yard,
                Material buildPad,
                Material station,
                Material validHologram,
                Material invalidHologram)
            {
                Yard = yard;
                BuildPad = buildPad;
                Station = station;
                ValidHologram = validHologram;
                InvalidHologram = invalidHologram;
            }

            public Material Yard { get; }

            public Material BuildPad { get; }

            public Material Station { get; }

            public Material ValidHologram { get; }

            public Material InvalidHologram { get; }
        }
    }
}

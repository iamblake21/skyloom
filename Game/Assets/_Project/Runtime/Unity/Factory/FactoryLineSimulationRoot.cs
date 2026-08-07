using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Crafting;
using CML.Unity.Presentation.Machines;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Selects only the initial contents of the authoritative gameplay state.
    /// The engine, catalog, clock and player inventory are identical in every
    /// profile; the technical fixture is the only profile allowed to seed a line.
    /// </summary>
    public enum FactorySimulationProfile
    {
        TechnicalFixture = 0,
        CanonicalGameplay = 1
    }

    /// <summary>
    /// Authoritative composition root shared by the technical factory and gameplay.
    ///
    /// This is the sole owner of the authoritative engine. Every HUD, presenter and
    /// build command is attached to this instance; no view is allowed to create a
    /// second simulation.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class FactoryLineSimulationRoot : MonoBehaviour
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        public static readonly StableId PlayerInventoryId = Id(1UL);

        /// <summary>
        /// The airship's cargo hold. One more instance in the inventory
        /// collection, which is already serialized and hashed as a whole, so it
        /// needs no rule and no schema of its own.
        /// </summary>
        public static readonly StableId AirshipHoldId = Id(2UL);
        public static readonly StableId SourceCrateId = Id(10UL);
        public static readonly StableId InputFunnelId = Id(11UL);
        public static readonly StableId PressId = Id(12UL);
        public static readonly StableId OutputFunnelId = Id(13UL);
        public static readonly StableId SinkCrateId = Id(14UL);
        public static readonly StableId StarterFurnaceId = Id(15UL);
        public static readonly StableId FeedBelt01Id = Id(20UL);
        public static readonly StableId FeedBelt02Id = Id(21UL);
        public static readonly StableId FeedBelt03Id = Id(22UL);
        public static readonly StableId FeedBelt04Id = Id(23UL);
        public static readonly StableId DrainBelt01Id = Id(24UL);
        public static readonly StableId DrainBelt02Id = Id(25UL);
        public static readonly StableId DrainBelt03Id = Id(26UL);
        public static readonly StableId DrainBelt04Id = Id(27UL);
        // Retained only so older serialized/editor utilities can deserialize without
        // inventing a live lane. InitializeNow never creates either id.
        public static readonly StableId FeedLaneId = Id(40UL);
        public static readonly StableId DrainLaneId = Id(41UL);

        [SerializeField] private TransferCommandBridge transferBridge;
        [SerializeField] private InventoryHudController inventoryHud;
        [SerializeField] private FactoryHudOrchestrator hudOrchestrator;
        [SerializeField] private GameObject ironIngotVisualPrefab;
        [SerializeField] private GameObject ironPlateVisualPrefab;
        [SerializeField, Min(1)] private int startingIronIngots = 12;
        [SerializeField] private FactorySimulationProfile initializationProfile =
            FactorySimulationProfile.TechnicalFixture;
        [SerializeField] private bool runSimulation = true;

        [NonSerialized] private FixedStepSimulationClock _clock;
        [NonSerialized] private CML.Unity.Airship.AirshipTechnicalScenario _airshipScenario;
        [NonSerialized] private Action<CML.Unity.Airship.AirshipPilotStation>
            _airshipInspectionHandler;
        [NonSerialized] private SimulationEngine _engine;
        [NonSerialized] private GameCatalog _catalog;
        [NonSerialized] private bool _initialized;
        [NonSerialized] private bool _halted;
        [NonSerialized] private InventoryState _lastPresentedAuthoritativeInventory;

        [field: NonSerialized]
        public event Action<SimulationEngine> StateCommitted;

        public SimulationEngine Engine =>
            _engine ?? throw new InvalidOperationException(
                "The M0.4B simulation root has not initialized.");

        public GameCatalog Catalog =>
            _catalog ?? throw new InvalidOperationException(
                "The M0.4B simulation root has not initialized.");

        public bool IsInitialized => _initialized;

        public bool IsHalted => _halted;

        public InventoryHudController InventoryHud => inventoryHud;

        public FactorySimulationProfile InitializationProfile =>
            initializationProfile;

        /// <summary>
        /// Stores a world object back into the authoritative player inventory.
        /// The caller may remove the world object only after this succeeds.
        /// </summary>
        public bool TryStorePlayerItem(
            StableId itemId,
            long quantity,
            out InventoryFailure failure)
        {
            failure = InventoryFailure.InvalidDefinition;
            if (!_initialized
                || _engine == null
                || itemId.IsNone
                || quantity <= 0L
                || !_engine.State.GetInventorySnapshot().TryGet(
                    PlayerInventoryId,
                    out var inventory))
            {
                return false;
            }

            if (!inventory.TryStoreEntire(
                    itemId,
                    new NonNegativeQuantity(quantity),
                    out var successor,
                    out failure))
            {
                return false;
            }

            if (!_engine.TryPublishInventorySuccessor(successor))
            {
                failure = InventoryFailure.InvalidDefinition;
                return false;
            }

            _lastPresentedAuthoritativeInventory = successor;
            RefreshBoundViews();
            StateCommitted?.Invoke(_engine);
            failure = InventoryFailure.None;
            return true;
        }

        public void Configure(
            TransferCommandBridge bridge,
            InventoryHudController playerInventoryHud,
            FactoryHudOrchestrator orchestrator = null,
            GameObject ingotVisual = null,
            GameObject plateVisual = null,
            FactorySimulationProfile profile =
                FactorySimulationProfile.TechnicalFixture)
        {
            var profileChanged = initializationProfile != profile;
            transferBridge = bridge;
            inventoryHud = playerInventoryHud;
            hudOrchestrator = orchestrator;
            ironIngotVisualPrefab = ingotVisual;
            ironPlateVisualPrefab = plateVisual;
            initializationProfile = profile;

            if (profileChanged && _initialized)
            {
                ResetAuthority();
                InitializeNow();
                return;
            }

            // Configure can legitimately be called immediately after AddComponent,
            // after Unity has already invoked Awake. Rebind the already-created
            // authority instead of leaving the newly supplied UI on a detached bridge.
            if (_initialized)
            {
                transferBridge?.Attach(_engine, _catalog);
                hudOrchestrator?.AttachSimulation(
                    _engine,
                    _catalog,
                    PlayerInventoryId);
                AttachScenePresenters();
                RefreshBoundViews();
            }
        }

        public void InitializeNow()
        {
            if (_initialized
                && _clock != null
                && _engine != null
                && _catalog != null)
            {
                return;
            }

            _initialized = false;
            _halted = false;
            _catalog = BootstrapCatalog.Load();
            var machines = initializationProfile ==
                           FactorySimulationProfile.TechnicalFixture
                ? CreateTechnicalFixtureMachines()
                : CreateCanonicalGameplayMachines();

            var inventory = initializationProfile ==
                            FactorySimulationProfile.TechnicalFixture
                ? CreateTechnicalFixtureInventory()
                : CreateCanonicalStarterInventory();
            var hold = InventoryState.Restore(
                AirshipHoldId,
                _catalog,
                ContentIds.AirshipHold,
                Array.Empty<InventoryStackRecord>());
            var inventories =
                InventorySimulationState.Create(_catalog, inventory, hold);

            // The hull belongs in this same state, not in a second engine of its
            // own. Installing a repair component moves matter out of the
            // inventory and into the airship in one commit, which is only
            // expressible if both live under one authority.
            _airshipScenario = ClaimAirshipScenario();
            var startsDamaged = initializationProfile
                != FactorySimulationProfile.TechnicalFixture;
            var airships = _airshipScenario != null
                ? _airshipScenario.BuildInitialAirshipState(startsDamaged)
                : new AirshipSimulationState();
            Debug.Log(
                $"CML_AIR_CLAIM scenario={(_airshipScenario != null ? "found" : "MISSING")} "
                + $"profile={initializationProfile} damaged={startsDamaged} "
                + $"airships={airships.AirshipCount} scene={gameObject.scene.name}",
                this);

            var state = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                airships,
                machines,
                inventories);

            _engine = new SimulationEngine(state, null, _catalog);
            _clock = new FixedStepSimulationClock();
            transferBridge?.Attach(_engine, _catalog);
            hudOrchestrator?.AttachSimulation(
                _engine,
                _catalog,
                PlayerInventoryId);
            _airshipScenario?.AdoptExternalEngine(_engine);
            WireAirshipInspection();
            _initialized = true;
            AttachScenePresenters();
            RefreshBoundViews();
            StateCommitted?.Invoke(_engine);
        }

        private MachineSimulationState CreateTechnicalFixtureMachines()
        {
            return new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(
                    SourceCrateId,
                    ContentIds.WoodenCrate,
                    Pose(-4, -7, 0))
                .AddFunnel(
                    InputFunnelId,
                    ContentIds.BeltFunnel,
                    Pose(-4, -6, 0))
                .AddBeltModule(
                    FeedBelt01Id,
                    ContentIds.BeltDriveUnit,
                    Pose(-4, -5, 0))
                .AddBeltModule(
                    FeedBelt02Id,
                    ContentIds.BeltStraight,
                    Pose(-4, -4, 0))
                .AddBeltModule(
                    FeedBelt03Id,
                    ContentIds.BeltStraight,
                    Pose(-4, -3, 0))
                .AddBeltModule(
                    FeedBelt04Id,
                    ContentIds.BeltStraight,
                    Pose(-4, -2, 0))
                .AddMachine(
                    PressId,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    Pose(-4, -1, 0))
                .AddBeltModule(
                    DrainBelt01Id,
                    ContentIds.BeltStraight,
                    Pose(-4, 0, 0))
                .AddBeltModule(
                    DrainBelt02Id,
                    ContentIds.BeltStraight,
                    Pose(-4, 1, 0))
                .AddBeltModule(
                    DrainBelt03Id,
                    ContentIds.BeltStraight,
                    Pose(-4, 2, 0))
                .AddBeltModule(
                    DrainBelt04Id,
                    ContentIds.BeltStraight,
                    Pose(-4, 3, 0))
                .AddFunnel(
                    OutputFunnelId,
                    ContentIds.BeltFunnel,
                    Pose(-4, 4, 2))
                .AddBuffer(
                    SinkCrateId,
                    ContentIds.WoodenCrate,
                    Pose(-4, 5, 0))
                .Build();
        }

        private MachineSimulationState CreateCanonicalGameplayMachines()
        {
            // The island already contains the authored furnace visual. Its logical
            // counterpart is seeded here and paired with that visual at runtime, so
            // no scene rewrite is required and the object is authoritative from the
            // first playable frame.
            return new MachineSimulationStateBuilder(_catalog)
                .AddMachine(
                    StarterFurnaceId,
                    ContentIds.CrudeFurnace,
                    ContentIds.SmeltIronIngot,
                    new MachineBuildPose(
                        -225_000,
                        26_192,
                        -172_000,
                        0))
                .Build();
        }

        private InventoryState CreateTechnicalFixtureInventory()
        {
            return InventoryState.Restore(
                PlayerInventoryId,
                _catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.IronIngot,
                        new NonNegativeQuantity(startingIronIngots)),
                    new InventoryStackRecord(
                        1,
                        ContentIds.WoodenCrateItem,
                        new NonNegativeQuantity(2L)),
                    new InventoryStackRecord(
                        2,
                        ContentIds.BeltFunnel,
                        new NonNegativeQuantity(4L)),
                    new InventoryStackRecord(
                        3,
                        ContentIds.BeltStraight,
                        new NonNegativeQuantity(24L)),
                    new InventoryStackRecord(
                        4,
                        ContentIds.MechanicalPressItem,
                        new NonNegativeQuantity(1L)),
                    new InventoryStackRecord(
                        5,
                        ContentIds.BeltDriveUnit,
                        new NonNegativeQuantity(2L)),
                    // Senza la Curva in inventario il pezzo resta impiazzabile:
                    // la selezione di costruzione si risolve dall'oggetto in
                    // mano, quindi il modulo non comparirebbe mai fra le scelte.
                    new InventoryStackRecord(
                        6,
                        ContentIds.BeltCurve,
                        new NonNegativeQuantity(12L)),
                    new InventoryStackRecord(
                        7,
                        ContentIds.BeltIncline,
                        new NonNegativeQuantity(12L)),
                    new InventoryStackRecord(
                        8,
                        ContentIds.BeltCurveLeft,
                        new NonNegativeQuantity(12L))
                });
        }

        private InventoryState CreateCanonicalStarterInventory()
        {
            // Empty, as DVS-001 requires: the player wakes beside the wreck
            // owning nothing. The free stock that used to live here existed to
            // validate BUILD-001 on the real island, and it hid the fact that
            // the opening could not actually be started -- every first resource
            // needed a Piccone, and the Piccone needed a Tronco off a tree that
            // needed the Piccone. Hand gathering broke that circle, so the
            // scaffolding comes out.
            //
            // Consequence to keep in mind: Nastri, Presse, Casse and Imbuti are
            // no longer in anyone's hands at spawn, so the factory can only be
            // exercised by hand in 92_M04B_FactoryLine_Test until crafting
            // produces those modules.
            return InventoryState.Restore(
                PlayerInventoryId,
                _catalog,
                ContentIds.PlayerInventory,
                Array.Empty<InventoryStackRecord>());
        }

        /// <summary>
        /// Queues one authoritative BUILD-001 command for the next tick. Physical
        /// collision and hologram checks happen before this call; topology is checked
        /// again by the simulation against the state that actually reaches the tick.
        /// </summary>
        public SimulationCommandAcceptance SubmitBuild(
            MachineBuildSpecification specification)
        {
            InitializeNow();
            var targetTick = _engine.State.Tick.Next();
            var sequence = NextSequence(targetTick);
            return _engine.EnqueueCommand(
                new SimulationCommand(
                    targetTick,
                    sequence,
                    SimulationCommandKinds.BuildMachineGraphElement,
                    PlayerInventoryId,
                    StableId.None,
                    0L,
                    MachineBuildCommandPayload.Encode(specification)));
        }

        public bool TryPreflightBuild(
            MachineBuildSpecification specification,
            out CommandRejectionReason rejection)
        {
            InitializeNow();
            return MachineBuildRule.TryPreflight(
                _engine.State.GetMachineSnapshot(),
                _engine.State.GetInventorySnapshot(),
                _catalog,
                PlayerInventoryId,
                specification,
                out rejection);
        }

        /// <summary>
        /// Asks for one placed element to be removed and refunded. The node travels in
        /// DestinationId; the inventory that receives the refund is the initiator.
        /// </summary>
        public SimulationCommandAcceptance SubmitSalvage(StableId nodeId)
        {
            InitializeNow();
            var targetTick = _engine.State.Tick.Next();
            var sequence = NextSequence(targetTick);
            return _engine.EnqueueCommand(
                new SimulationCommand(
                    targetTick,
                    sequence,
                    SimulationCommandKinds.SalvageMachineGraphElement,
                    PlayerInventoryId,
                    nodeId,
                    0L,
                    Array.Empty<byte>()));
        }

        public bool TryPreflightSalvage(
            StableId nodeId,
            out CommandRejectionReason rejection)
        {
            InitializeNow();
            return MachineSalvageRule.TryPreflight(
                _engine.State.GetMachineSnapshot(),
                _engine.State.GetInventorySnapshot(),
                PlayerInventoryId,
                nodeId,
                out rejection);
        }

        public bool TryResolveCreatedEntity(
            SimulationCommand acceptedCommand,
            out StableId entityId)
        {
            InitializeNow();
            return _engine.State.TryGetCreatedEntityId(
                acceptedCommand.TargetTick,
                MachineBuildCommandPayload.CreationKey(acceptedCommand),
                out entityId);
        }

        public bool TryGetPlayerInventory(out InventoryState inventory)
        {
            InitializeNow();
            return _engine.State.GetInventorySnapshot().TryGet(
                PlayerInventoryId,
                out inventory);
        }

        private void Awake()
        {
            ResolveCanonicalGameplayComposition();
            InitializeNow();
        }

        /// <summary>
        /// Takes the scene's airship scenario under this root's authority, so it
        /// stops creating an engine of its own. Called from Awake at execution
        /// order -500, comfortably before the scenario's Start.
        ///
        /// Safe to call again: this root rebuilds its engine on every
        /// re-initialization, and the scenario has to be pointed at the new one.
        /// </summary>
        private CML.Unity.Airship.AirshipTechnicalScenario ClaimAirshipScenario()
        {
            var ownerScene = gameObject.scene;
            if (!ownerScene.IsValid())
            {
                return null;
            }

            // Deliberately not gated on ownerScene.isLoaded. Awake runs while
            // the scene is still loading, so that flag is false exactly when
            // this has to work; the objects themselves already exist and
            // FindObjectsByType sees them.
            var scenario =
                FindSceneComponent<CML.Unity.Airship.AirshipTechnicalScenario>(
                    ownerScene);
            scenario?.SuppressAutomaticInitialization();
            return scenario;
        }

        /// <summary>
        /// Routes the pilot station's inspection to the repair panel, creating
        /// the panel on first use. It is loaded from Resources and instantiated
        /// rather than authored into the scene, because the Starter Island
        /// predates it and must not be regenerated to add a HUD.
        /// </summary>
        private void WireAirshipInspection()
        {
            var station = _airshipScenario != null
                ? _airshipScenario.PilotStation
                : null;
            if (station == null || hudOrchestrator == null)
            {
                return;
            }

            EnsureAirshipRepairHud();

            // Re-initialization rebuilds the engine, so the handler is replaced
            // rather than stacked.
            if (_airshipInspectionHandler != null)
            {
                station.InspectionRequested -= _airshipInspectionHandler;
            }

            _airshipInspectionHandler = inspected =>
            {
                var bridge = inspected != null ? inspected.SimulationBridge : null;
                if (bridge != null)
                {
                    hudOrchestrator.OpenAirshipRepair(bridge);
                }
            };
            station.InspectionRequested += _airshipInspectionHandler;
        }

        private void EnsureAirshipRepairHud()
        {
            if (hudOrchestrator.AirshipRepairHud != null)
            {
                return;
            }

            var ownerScene = gameObject.scene;
            var existing = ownerScene.IsValid()
                ? FindSceneComponent<
                    CML.Unity.Presentation.Airship.AirshipRepairHudController>(
                    ownerScene)
                : null;
            if (existing == null)
            {
                var prefab = Resources.Load<GameObject>("PF_AirshipRepairHUD");
                if (prefab == null)
                {
                    Debug.LogWarning(
                        "PF_AirshipRepairHUD is missing from Resources; run "
                        + "CML/UI/Rebuild Airship Repair HUD.",
                        this);
                    return;
                }

                var instance = Instantiate(prefab);
                instance.name = "PF_AirshipRepairHUD";
                existing = instance
                    .GetComponent<
                        CML.Unity.Presentation.Airship.AirshipRepairHudController>();
            }

            hudOrchestrator.ConfigureAirshipRepairHud(existing);
        }

        private void ResolveCanonicalGameplayComposition()
        {
            if (!string.Equals(
                    gameObject.name,
                    "FACTORY_CanonicalGameplay",
                    StringComparison.Ordinal))
            {
                return;
            }

            initializationProfile = FactorySimulationProfile.CanonicalGameplay;
            transferBridge ??= GetComponent<TransferCommandBridge>();
            hudOrchestrator ??= GetComponent<FactoryHudOrchestrator>();

            var ownerScene = gameObject.scene;
            inventoryHud ??= FindSceneComponent<InventoryHudController>(ownerScene);
            if (hudOrchestrator == null || inventoryHud == null)
            {
                return;
            }

            var chestHud = FindSceneComponent<ChestHudController>(ownerScene);
            var machineHud = FindSceneComponent<MachineHudController>(ownerScene);
            var workbenchHud = FindSceneComponent<WorkbenchHudController>(ownerScene);
            var craftingBridge = GetComponent<CraftingCommandBridge>();
            var mouseLook = FindSceneComponent<CML.Unity.Airship.FirstPersonMouseLook>(
                ownerScene);
            var gameplayInput = FindSceneComponent<CML.Unity.Airship.AirshipInputAdapter>(
                ownerScene);
            hudOrchestrator.ConfigureUi(
                inventoryHud,
                chestHud,
                machineHud,
                transferBridge,
                mouseLook,
                gameplayInput,
                workbenchHud,
                craftingBridge);
        }

        private static T FindSceneComponent<T>(
            UnityEngine.SceneManagement.Scene scene)
            where T : Component
        {
            var components = FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < components.Length; index++)
            {
                if (components[index].gameObject.scene == scene)
                {
                    return components[index];
                }
            }

            return null;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // Unity hot reload can preserve a private "_initialized = true" while
            // discarding the non-serializable engine and clock. Treat the runtime
            // graph as atomic: if any part is absent, rebuild all of it before the
            // next Update instead of leaving the scene frozen by a null reference.
            if (_initialized
                && _clock != null
                && _engine != null
                && _catalog != null)
            {
                return;
            }

            _clock = null;
            _engine = null;
            _catalog = null;
            _initialized = false;
            _halted = false;
            InitializeNow();
        }

        private void Update()
        {
            if (!_initialized || _halted || !runSimulation)
            {
                return;
            }

            // Mining and woodcutting predate the simulation-owned inventory and
            // publish immutable successor snapshots through the HUD. Adopt those
            // successors at this authority boundary before any factory command is
            // evaluated, so gameplay never owns a second inventory.
            if (SynchronizeLegacyGameplayInventory())
            {
                StateCommitted?.Invoke(_engine);
            }

            var elapsed = TimeSpan.FromSeconds(Math.Max(0d, Time.unscaledDeltaTime));
            var result = _clock.Advance(elapsed, _engine);
            if (!result.Succeeded)
            {
                _halted = true;
                var failure = result.Failure.Value;
                Debug.LogError(
                    $"M0.4B simulation aborted at tick {failure.ExecutingTick}, "
                    + $"phase {failure.FailedPhase}: {failure.FailureCause}",
                    this);
                return;
            }

            // Every frame, not only on a committed tick: the hull interpolates
            // between ticks and would stutter if it were projected at 20 Hz.
            ProjectAdoptedAirship();

            if (result.CommittedTicks == 0UL)
            {
                return;
            }

            RefreshBoundViews();
            StateCommitted?.Invoke(_engine);
        }

        private void ProjectAdoptedAirship()
        {
            var bridge = _airshipScenario != null ? _airshipScenario.Bridge : null;
            if (bridge == null || !bridge.IsAdopted)
            {
                return;
            }

            bridge.ProjectAdopted(
                Mathf.Clamp01(
                    _clock.PendingTimeSpanTicks
                    / (float)FixedStepSimulationClock.TimeSpanTicksPerSimulationTick));
        }

        private bool SynchronizeLegacyGameplayInventory()
        {
            var presented = inventoryHud != null
                ? inventoryHud.BoundState
                : null;
            if (presented == null
                || presented.InventoryId != PlayerInventoryId)
            {
                return false;
            }

            if (!_engine.State.GetInventorySnapshot().TryGet(
                    PlayerInventoryId,
                    out var authoritative))
            {
                return false;
            }

            if (ReferenceEquals(presented, authoritative))
            {
                _lastPresentedAuthoritativeInventory = authoritative;
                return false;
            }

            // A crafting or command boundary may have advanced the authoritative
            // inventory while the legacy HUD still holds the previous snapshot.
            // Never publish that stale view back over the newer state.
            if (!ReferenceEquals(
                    authoritative,
                    _lastPresentedAuthoritativeInventory))
            {
                RefreshBoundViews();
                return false;
            }

            if (!_engine.TryPublishInventorySuccessor(presented))
            {
                Debug.LogWarning(
                    "The gameplay inventory successor could not be published "
                    + "because the simulation was advancing.",
                    this);
                return false;
            }

            _lastPresentedAuthoritativeInventory = presented;
            return true;
        }

        private void RefreshBoundViews()
        {
            if (inventoryHud == null
                || !_engine.State.GetInventorySnapshot().TryGet(
                    PlayerInventoryId,
                    out var inventory))
            {
                return;
            }

            inventoryHud.BindInventory(inventory, _catalog);
            _lastPresentedAuthoritativeInventory = inventory;
        }

        private void AttachScenePresenters()
        {
            var ownerScene = gameObject.scene;
            AttachAuthoredStarterFurnace(ownerScene);
            foreach (var presenter in FindObjectsByType<FactoryBeltLanePresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (presenter.gameObject.scene != ownerScene)
                {
                    continue;
                }

                presenter.AttachSimulation(_engine);
                if (ironIngotVisualPrefab != null)
                {
                    presenter.SetItemPrefab(
                        ContentIds.IronIngot,
                        ironIngotVisualPrefab);
                }

                if (ironPlateVisualPrefab != null)
                {
                    presenter.SetItemPrefab(
                        ContentIds.IronPlate,
                        ironPlateVisualPrefab);
                }
            }

            foreach (var presenter in FindObjectsByType<FactoryPressPresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (presenter.gameObject.scene != ownerScene)
                {
                    continue;
                }

                presenter.AttachSimulation(_engine, _catalog);
            }

            foreach (var presenter in FindObjectsByType<
                FactoryLogisticsModulePresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (presenter.gameObject.scene != ownerScene)
                {
                    continue;
                }

                presenter.AttachSimulation(_engine);
            }

        }

        private void AttachAuthoredStarterFurnace(
            UnityEngine.SceneManagement.Scene ownerScene)
        {
            if (initializationProfile != FactorySimulationProfile.CanonicalGameplay
                || !_engine.State.GetMachineSnapshot().TryGetNode(
                    StarterFurnaceId,
                    out _))
            {
                return;
            }

            Transform furnace = null;
            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].gameObject.scene == ownerScene
                    && string.Equals(
                        transforms[index].name,
                        "ENV_StarterProp_01_PF_CrudeFurnace",
                        StringComparison.Ordinal))
                {
                    furnace = transforms[index];
                    break;
                }
            }

            if (furnace == null)
            {
                Debug.LogWarning(
                    "The canonical furnace exists in simulation but its authored "
                    + "Starter Island visual was not found.",
                    this);
                return;
            }

            var input = EnsureRuntimeSocket(
                furnace,
                "PORT_ItemIn",
                new Vector3(0f, 0.55f, -0.65f));
            var output = EnsureRuntimeSocket(
                furnace,
                "PORT_ItemOut",
                new Vector3(0f, 0.55f, 0.65f));
            var anchor = furnace.GetComponent<FactoryNodeAnchor>()
                ?? furnace.gameObject.AddComponent<FactoryNodeAnchor>();
            anchor.Configure(
                StarterFurnaceId,
                MachineNodeKind.Machine,
                input,
                output);

            var interaction = furnace.GetComponent<FactoryInteractionTarget>()
                ?? furnace.gameObject.AddComponent<FactoryInteractionTarget>();
            interaction.Configure(
                StarterFurnaceId,
                FactoryInteractionKind.Machine,
                "Usa Fornace");
        }

        private static Transform EnsureRuntimeSocket(
            Transform parent,
            string socketName,
            Vector3 localPosition)
        {
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (string.Equals(child.name, socketName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            return socket;
        }

        private ulong NextSequence(SimulationTick targetTick)
        {
            var sequence = 0UL;
            var pending = _engine.State.GetAcceptedCommandsCanonical();
            for (var index = 0; index < pending.Count; index++)
            {
                if (pending[index].TargetTick == targetTick)
                {
                    sequence = checked(sequence + 1UL);
                }
            }

            return sequence;
        }

        private static StableId Id(ulong low) =>
            new StableId(0x4D3034425F4C494EUL, low);

        private static MachineBuildPose Pose(int xMetres, int zMetres, byte yaw) =>
            new MachineBuildPose(
                checked(xMetres * 1_000),
                0,
                checked(zMetres * 1_000),
                yaw);

        private void ResetAuthority()
        {
            _clock = null;
            _engine = null;
            _catalog = null;
            _initialized = false;
            _halted = false;
            _lastPresentedAuthoritativeInventory = null;
        }

        private void OnValidate()
        {
            startingIronIngots = Mathf.Max(1, startingIronIngots);
        }
    }
}

using System;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    public static class AirshipTechnicalIds
    {
        public static readonly StableId Airship =
            new StableId(0x414952534849505FUL, 1UL);
        public static readonly StableId Player =
            new StableId(0x414952504C415945UL, 1UL);
        public static readonly StableId LandingSurface =
            new StableId(0x4149524C414E445FUL, 1UL);
        public static readonly StableId PlatformObstacle =
            new StableId(0x414952504C415446UL, 1UL);
        public static readonly StableId IslandObstacle =
            new StableId(0x41495249534C414EUL, 1UL);
        public static readonly StableId FlightTestObstacle =
            new StableId(0x414952464C595445UL, 1UL);
    }

    /// <summary>
    /// Regenerable technical-scene composition root. It builds canonical geometry
    /// from authored identities, creates the global state and initializes AIR.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipTechnicalScenario : MonoBehaviour
    {
        [SerializeField] private AirshipSimulationBridge bridge;
        [SerializeField] private AirshipRelativePassenger passenger;
        [SerializeField] private AirshipPilotStation pilotStation;
        [SerializeField] private AirshipInputAdapter inputAdapter;
        [SerializeField] private AirshipLandingSurfaceIdentity[] landingSurfaces =
            Array.Empty<AirshipLandingSurfaceIdentity>();
        [SerializeField] private AirshipObstacleIdentity[] obstacles =
            Array.Empty<AirshipObstacleIdentity>();
        [SerializeField] private bool initializeOnStart = true;

        [NonSerialized] private bool _claimedByExternalRoot;

        public bool IsReady { get; private set; }

        /// <summary>True once a composition root has taken over the engine.</summary>
        public bool ClaimedByExternalRoot => _claimedByExternalRoot;

        public AirshipSimulationBridge Bridge => bridge;

        public AirshipRelativePassenger Passenger => passenger;

        public AirshipPilotStation PilotStation => pilotStation;

        public void Configure(
            AirshipSimulationBridge simulationBridge,
            AirshipRelativePassenger relativePassenger,
            AirshipPilotStation station,
            AirshipInputAdapter adapter,
            AirshipLandingSurfaceIdentity[] surfaces,
            AirshipObstacleIdentity[] obstacleIdentities,
            bool automaticInitialization)
        {
            bridge = simulationBridge;
            passenger = relativePassenger;
            pilotStation = station;
            inputAdapter = adapter;
            landingSurfaces = surfaces ?? Array.Empty<AirshipLandingSurfaceIdentity>();
            obstacles = obstacleIdentities ?? Array.Empty<AirshipObstacleIdentity>();
            initializeOnStart = automaticInitialization;
        }

        /// <summary>
        /// Claimed by a composition root that owns the one authoritative engine
        /// of its scene. The scenario then stops creating an engine of its own
        /// and waits for <see cref="AdoptExternalEngine"/>.
        /// </summary>
        public void SuppressAutomaticInitialization()
        {
            initializeOnStart = false;
            _claimedByExternalRoot = true;
        }

        /// <summary>
        /// The airship half of the initial state, with nothing else in it. A
        /// root that owns inventories and machines folds this into the single
        /// state it builds, so installing a component can be atomic across the
        /// inventory and the hull.
        /// </summary>
        public AirshipSimulationState BuildInitialAirshipState(bool startsDamaged)
        {
            ResolveMissingSceneReferences();
            RequireSceneReferences();
            return BuildAirshipStateBuilder(startsDamaged).Build();
        }

        /// <summary>
        /// Wires the presentation components to an engine built elsewhere. The
        /// scenario never advances it: the owner does, and calls
        /// <see cref="AirshipSimulationBridge.ProjectAdopted"/> each frame.
        /// </summary>
        public void AdoptExternalEngine(SimulationEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            // Deliberately not guarded by IsReady: the owner rebuilds its engine
            // on re-initialization, and the bridge must follow the live one
            // instead of projecting a state nobody advances any more.
            ResolveMissingSceneReferences();
            RequireSceneReferences();
            ConfigurePresentationComponents();
            bridge.Adopt(
                engine,
                AirshipTechnicalIds.Airship,
                AirshipTechnicalIds.Player);
            IsReady = true;
            engine.State.GetAirshipSnapshot().TryGetAirship(
                AirshipTechnicalIds.Airship,
                out var adopted);
            Debug.Log(
                "CML_AIR_SCENARIO_ADOPTED status="
                + (adopted != null ? adopted.RepairStatus.ToString() : "MISSING"),
                this);
        }

        public void InitializeNow()
        {
            if (IsReady)
            {
                return;
            }

            if (_claimedByExternalRoot)
            {
                throw new InvalidOperationException(
                    "This AIR scenario was claimed by a composition root; it "
                    + "must be initialized through AdoptExternalEngine.");
            }

            ResolveMissingSceneReferences();
            RequireSceneReferences();

            var initialState = new SimulationState(
                new SimulationTick(0),
                new CatalogRevision(CatalogSchema.BootstrapContentRevision),
                BuildAirshipStateBuilder(false).Build());
            ConfigurePresentationComponents();
            bridge.Initialize(
                initialState,
                AirshipTechnicalIds.Airship,
                AirshipTechnicalIds.Player);
            IsReady = true;
            Debug.Log("CML_AIR_SCENARIO_INITIALIZED", this);
        }

        private void RequireSceneReferences()
        {
            if (bridge == null || passenger == null)
            {
                throw new InvalidOperationException(
                    "AIR technical scenario requires a bridge and player.");
            }
        }

        private void ConfigurePresentationComponents()
        {
            passenger.Configure(
                passenger.BodyRoot,
                passenger.CharacterController,
                bridge);
            bridge.Configure(
                bridge.Motor,
                bridge.GetComponent<AirshipFrame>(),
                passenger,
                bridge.LandingProbe,
                automaticAdvance: !_claimedByExternalRoot);
            pilotStation?.Configure(
                bridge.GetComponent<AirshipFrame>(),
                bridge,
                passenger,
                pilotStation.InteractionPoint,
                2f);
            inputAdapter?.Configure(bridge, pilotStation);
        }

        private AirshipSimulationStateBuilder BuildAirshipStateBuilder(
            bool startsDamaged)
        {
            var builder = new AirshipSimulationStateBuilder();
            if (startsDamaged)
            {
                builder.AddDamagedAirship(
                    AirshipTechnicalIds.Airship,
                    QuantizeWorldPose(bridge.Motor.VehicleRoot));
            }
            else
            {
                builder.AddAirship(
                    AirshipTechnicalIds.Airship,
                    QuantizeWorldPose(bridge.Motor.VehicleRoot));
            }

            builder
                .AddAboardPlayer(
                    AirshipTechnicalIds.Player,
                    AirshipTechnicalIds.Airship,
                    new AirshipPoseState(
                        AirshipSimulationConstants.PilotExitBodyRootPosition,
                        0),
                    isPiloting: false);
            for (var index = 0; index < obstacles.Length; index++)
            {
                if (obstacles[index] != null)
                {
                    var obstacle = obstacles[index].BuildLogicalState();
                    builder.AddObstacle(
                        obstacle.Id,
                        obstacle.Minimum,
                        obstacle.Maximum);
                }
            }

            for (var index = 0; index < landingSurfaces.Length; index++)
            {
                if (landingSurfaces[index] != null)
                {
                    var surface = landingSurfaces[index].BuildLogicalState();
                    builder.AddLandingSurface(
                        surface.Id,
                        surface.Center,
                        surface.YawTurn,
                        surface.HalfWidthMillimetres,
                        surface.HalfDepthMillimetres,
                        surface.SupportingObstacleId);
                }
            }

            if (landingSurfaces.Length > 0 && landingSurfaces[0] != null)
            {
                builder.DockAirship(
                    AirshipTechnicalIds.Airship,
                    landingSurfaces[0].StableId);
            }

            return builder;
        }

        private void ResolveMissingSceneReferences()
        {
            var ownerScene = gameObject.scene;
            if (!ownerScene.IsValid())
            {
                return;
            }

            // Not gated on isLoaded: a composition root resolves this from its
            // own Awake, while the scene is still loading and that flag is
            // false. Skipping there would leave the airship with no landing
            // surfaces and no obstacles.
            bridge ??= FindSceneComponent<AirshipSimulationBridge>(ownerScene);
            passenger ??= FindSceneComponent<AirshipRelativePassenger>(ownerScene);
            inputAdapter ??= FindSceneComponent<AirshipInputAdapter>(ownerScene);
            pilotStation ??= bridge != null
                ? bridge.GetComponentInChildren<AirshipPilotStation>(true)
                : FindSceneComponent<AirshipPilotStation>(ownerScene);

            if (landingSurfaces == null || landingSurfaces.Length == 0)
            {
                landingSurfaces = FindSceneComponents<AirshipLandingSurfaceIdentity>(
                    ownerScene);
            }

            if (obstacles == null || obstacles.Length == 0)
            {
                obstacles = FindSceneComponents<AirshipObstacleIdentity>(ownerScene);
            }
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

        private static T[] FindSceneComponents<T>(
            UnityEngine.SceneManagement.Scene scene)
            where T : Component
        {
            var all = FindObjectsByType<T>(
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

        private void Start()
        {
            if (initializeOnStart)
            {
                InitializeNow();
            }
        }

        private static AirshipPoseState QuantizeWorldPose(Transform value)
        {
            return new AirshipPoseState(
                AirshipObstacleIdentity.Quantize(value.position),
                AirshipObstacleIdentity.QuantizeYaw(value.eulerAngles.y));
        }
    }
}

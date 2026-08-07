using System;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// The sole Unity boundary for AIR. It queues quantized commands into the
    /// global engine, advances the one global clock and projects committed state.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class AirshipSimulationBridge : MonoBehaviour
    {
        [SerializeField] private AirshipMotor motor;
        [SerializeField] private AirshipFrame airshipFrame;
        [SerializeField] private AirshipRelativePassenger passenger;
        [SerializeField] private AirshipLandingSurfaceProbe landingProbe;
        [SerializeField] private AirshipAccessDoor accessDoor;
        [SerializeField] private bool advanceAutomatically = true;

        private SimulationEngine _engine;
        private FixedStepSimulationClock _clock;
        private bool _isAdopted;
        private StableId _airshipId;
        private StableId _playerId;
        private double _elapsedSecondsTotal;
        private long _convertedElapsedTimeSpanTicks;
        private bool _playerAlive;
        private bool _playerDestructionQueued;
        private AirshipInputAdapter _inputAdapter;
        private AirshipPoseState _poseBeforeTick;
        private int _pitchBeforeTick;
        private bool _hasPoseBeforeTick;

        public event Action<SimulationTick> StateProjected;

        public bool IsInitialized => _engine != null;

        public SimulationEngine Engine =>
            _engine ?? throw new InvalidOperationException("AIR bridge is not initialized.");

        public StableId AirshipId => _airshipId;

        public StableId PlayerId => _playerId;

        public AirshipMotor Motor => motor;

        public AirshipRelativePassenger Passenger => passenger;

        public AirshipLandingSurfaceProbe LandingProbe => landingProbe;

        public AirshipAccessDoor AccessDoor => accessDoor;

        public void Configure(
            AirshipMotor airshipMotor,
            AirshipFrame frame,
            AirshipRelativePassenger relativePassenger,
            AirshipLandingSurfaceProbe surfaceProbe,
            bool automaticAdvance)
        {
            motor = airshipMotor;
            airshipFrame = frame;
            passenger = relativePassenger;
            landingProbe = surfaceProbe;
            advanceAutomatically = automaticAdvance;
            EnsureAccessDoorConfigured();
        }

        public void Initialize(
            SimulationState initialState,
            StableId airshipId,
            StableId playerId)
        {
            if (initialState == null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }

            if (airshipId.IsNone || playerId.IsNone)
            {
                throw new ArgumentException("AIR scene ids cannot be StableId.None.");
            }

            var snapshot = initialState.GetAirshipSnapshot();
            if (!snapshot.TryGetAirship(airshipId, out _)
                || !snapshot.TryGetPlayer(playerId, out _))
            {
                throw new ArgumentException(
                    "Initial global state does not contain the configured AIR entities.",
                    nameof(initialState));
            }

            _airshipId = airshipId;
            _playerId = playerId;
            _playerAlive = true;
            _playerDestructionQueued = false;
            _engine = new SimulationEngine(initialState);
            _clock = new FixedStepSimulationClock();
            _isAdopted = false;
            _elapsedSecondsTotal = 0d;
            _convertedElapsedTimeSpanTicks = 0L;
            EnsureAccessDoorConfigured();
            EnsureDamageSmokeConfigured();
            ProjectCommittedState(true);
        }

        /// <summary>
        /// Runs on an engine somebody else owns and advances. Used by the
        /// canonical scene, where inventories, machines and the hull have to
        /// live in one authoritative state so that installing a repair
        /// component can be atomic across the inventory and the airship.
        ///
        /// The bridge keeps no clock here: it would be a second source of time
        /// against the same state.
        /// </summary>
        public void Adopt(
            SimulationEngine engine,
            StableId airshipId,
            StableId playerId)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            if (airshipId.IsNone || playerId.IsNone)
            {
                throw new ArgumentException("AIR scene ids cannot be StableId.None.");
            }

            var snapshot = engine.State.GetAirshipSnapshot();
            if (!snapshot.TryGetAirship(airshipId, out _)
                || !snapshot.TryGetPlayer(playerId, out _))
            {
                throw new ArgumentException(
                    "The adopted engine does not contain the configured AIR entities.",
                    nameof(engine));
            }

            _airshipId = airshipId;
            _playerId = playerId;
            _playerAlive = true;
            _playerDestructionQueued = false;
            _engine = engine;
            _clock = null;
            _isAdopted = true;
            advanceAutomatically = false;
            _elapsedSecondsTotal = 0d;
            _convertedElapsedTimeSpanTicks = 0L;
            EnsureAccessDoorConfigured();
            EnsureDamageSmokeConfigured();
            ProjectCommittedState(true);
        }

        /// <summary>True when the engine belongs to another composition root.</summary>
        public bool IsAdopted => _isAdopted;

        /// <summary>
        /// Projects the committed state of an adopted engine. The owner calls
        /// this once per frame after advancing, passing its own interpolation
        /// alpha so both halves of the scene read the same clock.
        /// </summary>
        public void ProjectAdopted(float interpolation)
        {
            if (!_isAdopted || !IsInitialized)
            {
                return;
            }

            ProjectCommittedState(false);
            RenderPresentation(interpolation);
        }

        public SimulationTickResult AdvanceOneTick()
        {
            PrepareNextTick();
            var result = Engine.AdvanceOneTick();
            if (result.Committed)
            {
                ResolveCommittedWorldCollision();
                ProjectCommittedState(false);
            }

            return result;
        }

        /// <summary>
        /// Converts presentation seconds from one absolute accumulated timeline.
        /// Rounding happens on that total, so sub-TimeSpan-tick fractions survive
        /// frame boundaries instead of creating a per-frame bias.
        /// </summary>
        public FixedStepAdvanceResult AdvanceFrameSeconds(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds)
                || elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            }

            _elapsedSecondsTotal += elapsedSeconds;
            var targetTimeSpanTicks = checked((long)Math.Round(
                _elapsedSecondsTotal * TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero));
            var elapsedTimeSpanTicks = checked(
                targetTimeSpanTicks - _convertedElapsedTimeSpanTicks);
            _convertedElapsedTimeSpanTicks = targetTimeSpanTicks;
            return AdvanceFrame(TimeSpan.FromTicks(elapsedTimeSpanTicks));
        }

        public FixedStepAdvanceResult AdvanceFrame(TimeSpan elapsed)
        {
            var result = _clock.Advance(
                elapsed,
                Engine,
                PrepareNextTick,
                ResolveCommittedWorldCollision);
            if (result.CommittedTicks > 0UL)
            {
                ProjectCommittedState(false);
            }

            RenderPresentation(InterpolationAlpha);
            return result;
        }

        public void RenderPresentation(float interpolation)
        {
            if (motor != null)
            {
                motor.Render(interpolation);
            }

            if (passenger != null)
            {
                passenger.Render(interpolation);
            }
        }

        public SimulationCommandAcceptance QueueBoard()
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.Board(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId));
        }

        public SimulationCommandAcceptance QueueDisembark()
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.Disembark(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId));
        }

        public SimulationCommandAcceptance QueuePilotBegin()
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.PilotBegin(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId));
        }

        public SimulationCommandAcceptance QueuePilotEnd()
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.PilotEnd(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId));
        }

        public SimulationCommandAcceptance QueueTakeoff()
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.Takeoff(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId));
        }

        public bool QueueLandingFromProbe()
        {
            if (landingProbe == null)
            {
                return false;
            }

            var snapshot = GetAirshipSnapshot();
            if (!snapshot.TryGetAirship(_airshipId, out var airship)
                || !landingProbe.TryFindLandingSurface(
                    airship.Pose,
                    out var surface)
                || !snapshot.TryGetLandingSurface(
                    surface.SurfaceId,
                    out var logicalSurface)
                || surface.Identity == null
                || !surface.Identity.MatchesLogicalState(logicalSurface))
            {
                return false;
            }

            QueueLanding(surface.SurfaceId);
            return true;
        }

        public SimulationCommandAcceptance QueueLanding(StableId surfaceId)
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.LandingRequest(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId,
                surfaceId));
        }

        public SimulationCommandAcceptance QueuePilotInput(
            AirshipPilotInputState input)
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                schedule.Tick,
                schedule.Sequence,
                _playerId,
                _airshipId,
                input));
        }

        public SimulationCommandAcceptance QueuePlayerDestroyed()
        {
            if (_playerDestructionQueued)
            {
                throw new InvalidOperationException(
                    "AIR player destruction is already queued.");
            }

            var schedule = NextSchedule();
            var acceptance = Engine.EnqueueCommand(AirshipCommandCodec.PlayerDestroyed(
                schedule.Tick,
                schedule.Sequence,
                _playerId));
            _playerDestructionQueued = true;
            return acceptance;
        }

        public AirshipSimulationState GetAirshipSnapshot()
        {
            return Engine.State.GetAirshipSnapshot();
        }

        /// <summary>
        /// The repair state of this bridge's airship, read from the committed
        /// snapshot. Presentation asks for it to choose a word and to show a
        /// counter; it never owns it.
        /// </summary>
        public bool TryGetRepairState(out AirshipEntityState airship)
        {
            airship = null;
            return IsInitialized
                && !_airshipId.IsNone
                && GetAirshipSnapshot().TryGetAirship(_airshipId, out airship);
        }

        /// <summary>True while the hull is not yet airworthy.</summary>
        public bool IsAwaitingRepair =>
            TryGetRepairState(out var airship)
            && airship.RepairStatus != AirshipRepairStatus.Repaired;

        /// <summary>
        /// Queues one installation of <paramref name="amount"/> units of
        /// <paramref name="itemId"/>, paid from <paramref name="sourceInventoryId"/>.
        /// Like every other queue method here it reports what the boundary
        /// accepted, not whether the install will succeed: phase 9 decides.
        /// </summary>
        public SimulationCommandAcceptance QueueRepairInstall(
            StableId sourceInventoryId,
            StableId itemId,
            long amount)
        {
            var schedule = NextSchedule();
            return Engine.EnqueueCommand(
                AirshipCommandCodec.RepairInstall(
                    schedule.Tick,
                    schedule.Sequence,
                    _playerId,
                    _airshipId,
                    sourceInventoryId,
                    itemId,
                    amount));
        }

        internal void AttachInputAdapter(AirshipInputAdapter adapter)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            if (_inputAdapter != null && _inputAdapter != adapter)
            {
                throw new InvalidOperationException(
                    "One AIR bridge can own only one input adapter.");
            }

            _inputAdapter = adapter;
        }

        internal void DetachInputAdapter(AirshipInputAdapter adapter)
        {
            if (_inputAdapter == adapter)
            {
                _inputAdapter = null;
            }
        }

        private float InterpolationAlpha
        {
            get
            {
                if (_clock == null)
                {
                    return 1f;
                }

                return Mathf.Clamp01(
                    _clock.PendingTimeSpanTicks
                    / (float)FixedStepSimulationClock.TimeSpanTicksPerSimulationTick);
            }
        }

        private void Update()
        {
            // An adopted engine is advanced and projected by its owner, in its
            // execution order. Doing either here would double the tick rate
            // against a state this component does not own.
            if (_isAdopted || !advanceAutomatically || !IsInitialized)
            {
                return;
            }

            var result = AdvanceFrameSeconds(Time.unscaledDeltaTime);
            if (!result.Succeeded)
            {
                Debug.LogError(
                    $"AIR simulation aborted: {result.Failure.Value.FailureCause}",
                    this);
                enabled = false;
            }
        }

        private void ProjectCommittedState(bool snap)
        {
            var state = Engine.State.GetAirshipSnapshot();
            if (!state.TryGetAirship(_airshipId, out var airship))
            {
                throw new SimulationInvariantException(
                    "The committed AIR airship is missing from global state.");
            }

            if (motor != null)
            {
                motor.CommitPose(
                    Engine.State.Tick,
                    airship.Pose,
                    airship.PitchTurnUnits);
            }

            var hasPlayer = state.TryGetPlayer(_playerId, out var player);
            if (hasPlayer)
            {
                _playerAlive = true;
                if (passenger != null)
                {
                    passenger.CommitState(
                        Engine.State.Tick,
                        player,
                        airshipFrame);
                }
            }
            else
            {
                _playerAlive = false;
                _playerDestructionQueued = false;
            }
            if (snap)
            {
                if (motor != null)
                {
                    motor.SnapToCurrent();
                }

                if (hasPlayer && passenger != null)
                {
                    passenger.Render(1f);
                }
            }

            NotifyStateProjected(Engine.State.Tick);
        }

        private void NotifyStateProjected(SimulationTick tick)
        {
            var subscribers = StateProjected;
            if (subscribers == null)
            {
                return;
            }

            foreach (Action<SimulationTick> subscriber
                in subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(tick);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void PrepareNextTick()
        {
            var snapshot = Engine.State.GetAirshipSnapshot();
            _hasPoseBeforeTick = snapshot.TryGetAirship(
                _airshipId,
                out var airshipBeforeTick);
            if (_hasPoseBeforeTick)
            {
                _poseBeforeTick = airshipBeforeTick.Pose;
                _pitchBeforeTick = airshipBeforeTick.PitchTurnUnits;
            }

            _inputAdapter?.FlushLatchedSample(Engine.State.Tick.Next());
        }

        private void ResolveCommittedWorldCollision()
        {
            if (!_hasPoseBeforeTick || motor == null)
            {
                return;
            }

            var snapshot = Engine.State.GetAirshipSnapshot();
            if (!snapshot.TryGetAirship(_airshipId, out var candidate)
                || candidate.Mode != AirshipFlightMode.Flying
                || motor.IsWorldMotionClear(
                    _poseBeforeTick,
                    _pitchBeforeTick,
                    candidate.Pose,
                    candidate.PitchTurnUnits))
            {
                return;
            }

            if (!Engine.TryResolveAirshipWorldCollision(
                    _airshipId,
                    _poseBeforeTick,
                    _pitchBeforeTick))
            {
                throw new InvalidOperationException(
                    "AIR could not publish a committed Unity world-collision resolution.");
            }
        }

        private CommandSchedule NextSchedule()
        {
            if (!_playerAlive)
            {
                throw new InvalidOperationException(
                    "AIR commands cannot be queued after the player was destroyed.");
            }

            var tick = Engine.State.Tick.Next();
            ulong sequence = 0UL;
            var commands = Engine.State.GetAcceptedCommandsCanonical();
            for (var index = 0; index < commands.Count; index++)
            {
                if (commands[index].TargetTick == tick)
                {
                    sequence = checked(sequence + 1UL);
                }
            }

            return new CommandSchedule(tick, sequence);
        }

        private void EnsureAccessDoorConfigured()
        {
            var root = motor != null ? motor.VehicleRoot : transform;
            var doorRoot = FindDescendant(root, "ANM_AccessDoor");
            if (doorRoot == null)
            {
                return;
            }

            if (accessDoor == null)
            {
                accessDoor = GetComponent<AirshipAccessDoor>();
            }

            if (accessDoor == null)
            {
                accessDoor = gameObject.AddComponent<AirshipAccessDoor>();
            }

            accessDoor.Configure(root, doorRoot, passenger, initiallyOpen: true);
        }

        private void EnsureDamageSmokeConfigured()
        {
            var root = motor != null ? motor.VehicleRoot : transform;
            if (root == null)
            {
                return;
            }

            // Unity-aware null check rather than `??`, which would accept a
            // destroyed component.
            var smoke = GetComponent<AirshipDamageSmoke>();
            if (smoke == null)
            {
                smoke = gameObject.AddComponent<AirshipDamageSmoke>();
            }

            smoke.Configure(this, root);
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }

            var descendants = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < descendants.Length; index++)
            {
                if (string.Equals(
                        descendants[index].name,
                        objectName,
                        StringComparison.Ordinal))
                {
                    return descendants[index];
                }
            }

            return null;
        }

        private readonly struct CommandSchedule
        {
            public CommandSchedule(SimulationTick tick, ulong sequence)
            {
                Tick = tick;
                Sequence = sequence;
            }

            public SimulationTick Tick { get; }

            public ulong Sequence { get; }
        }
    }
}

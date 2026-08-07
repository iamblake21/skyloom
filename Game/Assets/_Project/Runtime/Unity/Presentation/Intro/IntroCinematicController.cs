using System;
using System.Collections.Generic;
using CML.Simulation.Airship;
using CML.Unity.Factory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UIDocument = UnityEngine.UIElements.UIDocument;

namespace CML.Unity.Presentation.Intro
{
    /// <summary>
    /// Directs the opening sequence as an explicit list of shots: a third
    /// person cruise through deep space, the jump to superluminal speed, a hard
    /// cut into the cockpit, the failure that tears a rift open ahead of the
    /// ship, and the first person arrival that flies through the island's own
    /// ancient portal before the pilot wakes up aboard.
    ///
    /// The ship used in space is a visual duplicate. Nothing in the gameplay
    /// scene is moved: the arrival owns a camera of its own and the real player
    /// is only repositioned once, behind a black screen, at the very end.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class IntroCinematicController : MonoBehaviour
    {
        private const string DefaultGameplaySceneName =
            "91_StarterIsland_Terrain_Review";
        private const string AncientPortalObjectName = "ENV_AncientStonePortal";
        private const string AirshipObjectName = "PF_Airship";
        private const float MinimumSkipDelaySeconds = 0.5f;
        private const float MaximumFrameDeltaSeconds = 0.05f;

        /// <summary>Fraction of the dive at which the arch goes past.</summary>
        private const float PortalGateAt = 0.36f;

        [SerializeField] private IntroCinematicBindings bindings =
            new IntroCinematicBindings();
        [SerializeField] private string gameplaySceneName =
            DefaultGameplaySceneName;
        [SerializeField] private bool allowSkip = true;

        [Header("Shot lengths (seconds)")]
        [SerializeField, Min(0.5f)] private float hyperspaceSeconds = 4.5f;
        [SerializeField, Min(0.5f)] private float cockpitSeconds = 2.6f;
        [SerializeField, Min(0.5f)] private float alarmSeconds = 4.5f;
        [SerializeField, Min(0.5f)] private float riftOpenSeconds = 5.5f;
        [SerializeField, Min(0.5f)] private float riftEntrySeconds = 2f;
        [SerializeField, Min(1f)] private float fallSeconds = 7f;
        [Tooltip("Ceiling for the crash. It normally ends when the wreck has "
            + "actually stopped, not when this runs out.")]
        [SerializeField, Min(0.4f)] private float crashSeconds = 5f;
        [SerializeField, Min(5f)] private float crashTouchdownSpeed = 88f;
        [Tooltip("Ground drag, in metres per second squared. Together with the "
            + "touchdown speed this decides how long the wreck ploughs.")]
        [SerializeField, Min(1f)] private float crashFriction = 34f;
        [SerializeField, Min(0f)] private float crashHullClearance = 2.2f;
        [Tooltip("Pure black held while the cinematic is dismantled behind it.")]
        [SerializeField, Min(0.3f)] private float blackoutSeconds = 2.8f;
        [SerializeField, Min(0.5f)] private float wakeSeconds = 4.2f;

        [Header("Flight leg")]
        [SerializeField, Min(0.2f)] private float flightSettleSeconds = 3.4f;
        [Tooltip("Must outlast the pass itself: the rock has to be gone before "
            + "the next one is launched, since there is only one of them.")]
        [SerializeField, Min(0.2f)] private float flightRecoverSeconds = 4.4f;
        [SerializeField, Min(0.02f)] private float lookSensitivity = 0.11f;
        [Tooltip("Degrees of yaw the player must produce to clear a lesson.")]
        [SerializeField, Min(3f)] private float tutorialTurnDegrees = 26f;
        [Tooltip("Seconds the completed turn must be held before the sequence "
            + "resumes, so releasing reads as a consequence of the gesture.")]
        [SerializeField, Min(0f)] private float tutorialHoldSeconds = 0.32f;
        [SerializeField, Min(5f)] private float asteroidWarningDistance = 300f;
        [SerializeField, Min(50f)] private float asteroidLaunchDistance = 900f;
        [SerializeField, Min(10f)] private float asteroidApproachSpeed = 190f;
        [Tooltip("Closing speed once the lesson is passed. Slower than the "
            + "approach so the rock is visibly seen going by.")]
        [SerializeField, Min(10f)] private float asteroidPassSpeed = 112f;
        [Tooltip("Extra metres between the hull and the rock on a clean pass.")]
        [SerializeField, Min(0f)] private float asteroidMissMargin = 16f;

        [Header("Island portal transit")]
        [Tooltip("Height of the arch aperture as a fraction of the portal's " +
            "total height. Measured from the mesh when the scene is generated.")]
        [SerializeField, Range(0.05f, 0.95f)] private float portalApertureHeight = 0.42f;
        [Tooltip("Radius of the arch aperture as a fraction of the portal's " +
            "total height. Measured from the mesh when the scene is generated.")]
        [SerializeField, Range(0.02f, 0.6f)] private float portalApertureRadius = 0.24f;

        private readonly List<Behaviour> _suspendedPlayerBehaviours =
            new List<Behaviour>();
        private readonly List<Behaviour> _hiddenOverlayBehaviours =
            new List<Behaviour>();
        private readonly List<UIDocument> _hiddenUiDocuments =
            new List<UIDocument>();

        private Shot _shot;
        private float _shotElapsed;
        private float _fadeAlpha;
        private float _flashAlpha;
        private float _eyelid;
        private float _shakeAmount;
        private float _alarmPulse;
        private float _warpBlend;

        private FlightStep _flightStep;
        private float _flightTimer;
        private float _pilotYaw;
        private float _pilotPitch;
        private float _lessonYaw;
        private float _promptAlpha;
        private float _greyAlpha;
        private bool _frozen;
        private IntroTutorialPrompt _prompt;
        private Vector3[] _asteroidDrift = Array.Empty<Vector3>();
        private float _threatDistance;
        private float _threatLateral;
        private float _threatDirection = 1f;
        private float _threatClearance;
        private float _threatCleared;
        private float _lessonHold;
        private float _promptDirection = 1f;
        private int _asteroidCycle;

        // Travel frame of the flight leg, captured before the player steers so
        // the rocks keep a straight line no matter how the hull turns.
        private Vector3 _flightAxisForward = Vector3.forward;
        private Vector3 _flightAxisRight = Vector3.right;
        private Vector3 _flightAxisUp = Vector3.up;

        private Material _skyboxInstance;
        private Material _skyboxOriginal;
        private Material _warpMaterial;
        private Material _riftMaterial;
        private Material _veilMaterial;
        private IntroCinematicGrade _grade;
        private ParticleSystemRenderer _starStreakRenderer;
        private float _delta;

        private float _chaseBaseFov = 61f;
        private float _cockpitBaseFov = 66f;
        private Vector3 _riftOrigin;
        private Vector3 _riftForward;
        private float _riftStartDistance;
        private AsyncOperation _islandLoad;
        private bool _islandActivationRequested;

        private Camera _arrivalCamera;
        private Transform _arrivalShake;
        private Transform _portalVeil;
        private Transform _fallShip;
        private Vector3 _fallVelocity;
        private Vector3 _crashVelocity;
        private float _crashVerticalVelocity;
        private float _crashJolt;
        private bool _crashStarted;
        private bool _handedOver;
        private Transform _wakeCamera;
        private Vector3 _wakeCameraRestPosition;
        private Quaternion _wakeCameraRestRotation = Quaternion.identity;
        private Vector3 _fallStart;
        private Vector3 _fallGate;
        private Vector3 _fallEnd;
        private Quaternion _diveRotation;
        private readonly List<Renderer> _hiddenAirshipRenderers =
            new List<Renderer>();

        private Camera _playerCamera;
        private bool _playerCameraWasEnabled;
        private string _playerCameraTag;
        private AudioListener _playerAudioListener;
        private bool _playerAudioListenerWasEnabled;
        private CharacterController _playerController;
        private bool _playerControllerWasEnabled;
        private FactoryHudOrchestrator _hudOrchestrator;
        private Transform _gameplayAirship;
        private bool _gameplayRestored;

        public string GameplaySceneName => gameplaySceneName;

        public bool HasCinematicBindings =>
            bindings != null
            && bindings.SpaceRoot != null
            && bindings.Airship != null
            && bindings.AirshipAttitude != null
            && bindings.ChaseRig != null
            && bindings.ChaseCamera != null
            && bindings.CockpitShake != null
            && bindings.CockpitCamera != null
            && bindings.WarpTunnel != null
            && bindings.WarpTunnelRenderer != null
            && bindings.Rift != null
            && bindings.RiftRenderer != null
            && bindings.RiftLight != null
            && bindings.CockpitFillLight != null
            && bindings.StarStreaks != null
            && bindings.CockpitSparks != null
            && bindings.RiftDebris != null
            && bindings.PostProcessVolume != null
            && bindings.SkyboxMaterial != null
            && bindings.PortalVeilMaterial != null
            && bindings.AirshipHeading != null
            && AlertLightCount > 0
            && AsteroidCount > 1;

        public int AsteroidCount =>
            bindings != null && bindings.Asteroids != null
                ? bindings.Asteroids.Length
                : 0;

        public int AlertLightCount =>
            bindings != null && bindings.AlertLights != null
                ? bindings.AlertLights.Length
                : 0;

        public float TotalDurationSeconds =>
            hyperspaceSeconds + cockpitSeconds + alarmSeconds
            + riftOpenSeconds + riftEntrySeconds + fallSeconds
            + crashSeconds + blackoutSeconds + wakeSeconds
            + flightSettleSeconds + flightRecoverSeconds * 2f;

        public void Configure(
            IntroCinematicBindings cinematicBindings,
            string destinationScene)
        {
            bindings = cinematicBindings
                ?? throw new ArgumentNullException(nameof(cinematicBindings));
            gameplaySceneName = string.IsNullOrWhiteSpace(destinationScene)
                ? DefaultGameplaySceneName
                : destinationScene;
        }

        /// <summary>
        /// Stores the arch aperture the builder measured on the portal mesh, so
        /// the arrival flies through the opening instead of through the stone.
        /// </summary>
        public void ConfigurePortalAperture(
            float heightFraction,
            float radiusFraction)
        {
            portalApertureHeight = Mathf.Clamp(heightFraction, 0.05f, 0.95f);
            portalApertureRadius = Mathf.Clamp(radiusFraction, 0.02f, 0.6f);
        }

        private void Awake()
        {
            if (!HasCinematicBindings)
            {
                Debug.LogError(
                    "CML intro cinematic is missing one or more scene bindings. "
                    + "Rigenerare con CML/Cinematics/Rebuild Intro Sequence.");
                enabled = false;
                return;
            }

            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;

            _skyboxOriginal = RenderSettings.skybox;
            _skyboxInstance = new Material(bindings.SkyboxMaterial);
            RenderSettings.skybox = _skyboxInstance;

            _warpMaterial = bindings.WarpTunnelRenderer.material;
            _riftMaterial = bindings.RiftRenderer.material;
            _veilMaterial = new Material(bindings.PortalVeilMaterial);

            _grade = IntroCinematicGrade.TryCreate(bindings.PostProcessVolume);
            _grade?.SetActive(true);
            _grade?.Apply(IntroGradeState.Cruise());
            _starStreakRenderer = bindings.StarStreaks
                .GetComponent<ParticleSystemRenderer>();

            _chaseBaseFov = bindings.ChaseCamera.fieldOfView;
            _cockpitBaseFov = bindings.CockpitCamera.fieldOfView;

            _riftStartDistance = Vector3.Distance(
                bindings.Rift.position,
                bindings.CockpitCamera.transform.position);
            AnchorRiftAxis();

            _flightAxisForward = bindings.SpaceRoot.forward;
            _flightAxisRight = bindings.SpaceRoot.right;
            _flightAxisUp = bindings.SpaceRoot.up;
            InitialiseAsteroids();

            SetRiftOpenness(0f);
            SetWarpIntensity(0f);
            SetWarpBlend(0f);
            bindings.RiftLight.intensity = 0f;
            SetAlertIntensity(0f);
            bindings.RiftDebris.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            ActivateCamera(bindings.ChaseCamera, bindings.CockpitCamera);
            _shot = Shot.Hyperspace;
            LockCursor();
        }

        private void Update()
        {
            if (!enabled)
            {
                return;
            }

            LockCursor();
            _delta = Mathf.Min(
                Time.unscaledDeltaTime,
                MaximumFrameDeltaSeconds);

            // A frozen lesson stops the sequence, not the input: the player has
            // to be able to steer while the card is up.
            if (!_frozen)
            {
                _shotElapsed += _delta;
            }

            if (allowSkip
                && _shot != Shot.Complete
                && _shotElapsed >= MinimumSkipDelaySeconds
                && IsSkipRequested())
            {
                Skip();
                return;
            }

            switch (_shot)
            {
                case Shot.Hyperspace:
                    TickHyperspace();
                    break;
                case Shot.Cockpit:
                    TickCockpit();
                    break;
                case Shot.Flight:
                    TickFlight();
                    break;
                case Shot.Alarm:
                    TickAlarm();
                    break;
                case Shot.RiftOpen:
                    TickRiftOpen();
                    break;
                case Shot.RiftEntry:
                    TickRiftEntry();
                    break;
                case Shot.Fall:
                    TickFall();
                    break;
                case Shot.Crash:
                    TickCrash();
                    break;
                case Shot.Blackout:
                    TickBlackout();
                    break;
                case Shot.Wake:
                    TickWake();
                    break;
            }

            if (_shot != Shot.Flight)
            {
                _greyAlpha = Mathf.MoveTowards(_greyAlpha, 0f, _delta * 4f);
                _promptAlpha = Mathf.MoveTowards(_promptAlpha, 0f, _delta * 4f);
            }

            ApplyCameraShake();
        }

        /// <summary>
        /// Shake overwrites the target's whole local pose, so it may only ever
        /// be applied to a camera that sits at local identity. Applying it to a
        /// rig that carries the authored eye offset would erase that offset
        /// every frame and drag the view back to the hull's origin — and it
        /// would never show up in an editor render, because nothing shakes
        /// outside Play Mode.
        /// </summary>
        private void ApplyCameraShake()
        {
            if (_shot >= Shot.Fall)
            {
                ApplyShake(_arrivalCamera != null
                    ? _arrivalCamera.transform
                    : null);
                return;
            }

            ApplyShake(_shot >= Shot.Cockpit
                ? bindings.CockpitCamera != null
                    ? bindings.CockpitCamera.transform
                    : null
                : bindings.ChaseCamera != null
                    ? bindings.ChaseCamera.transform
                    : null);
        }

        private void OnGUI()
        {
            if (_shot == Shot.Complete)
            {
                return;
            }

            var previousColor = GUI.color;
            var full = new Rect(0f, 0f, Screen.width, Screen.height);

            // The teaching card dims the frozen frame instead of hiding it, so
            // the player still sees the rock they are being asked to avoid.
            if (_greyAlpha > 0.001f)
            {
                GUI.color = new Color(0.14f, 0.15f, 0.17f, Mathf.Clamp01(_greyAlpha));
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }

            if (_promptAlpha > 0.001f)
            {
                _prompt ??= new IntroTutorialPrompt();
                _prompt.Draw(
                    "Muovi",
                    _promptDirection >= 0f
                        ? "per girare a destra"
                        : "per girare a sinistra",
                    _promptDirection,
                    _promptAlpha);
            }

            if (_flashAlpha > 0.001f)
            {
                GUI.color = new Color(0.88f, 0.95f, 1f, Mathf.Clamp01(_flashAlpha));
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }

            if (_fadeAlpha > 0.001f)
            {
                GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_fadeAlpha));
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }

            if (_eyelid > 0.001f)
            {
                var lid = Screen.height * 0.5f * Mathf.Clamp01(_eyelid);
                GUI.color = Color.black;
                GUI.DrawTexture(
                    new Rect(0f, 0f, Screen.width, lid),
                    Texture2D.whiteTexture);
                GUI.DrawTexture(
                    new Rect(0f, Screen.height - lid, Screen.width, lid),
                    Texture2D.whiteTexture);
            }

            if (allowSkip
                && _shot < Shot.Crash
                && _fadeAlpha < 0.2f
                && _flashAlpha < 0.2f)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.LowerRight,
                    fontSize = 13
                };
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.Label(
                    new Rect(0f, Screen.height - 46f, Screen.width - 34f, 24f),
                    "INVIO PER SALTARE",
                    style);
            }

            GUI.color = previousColor;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            RestoreGameplay();
            _grade?.SetActive(false);
            ReleaseSpaceSky();

            if (_veilMaterial != null)
            {
                Destroy(_veilMaterial);
                _veilMaterial = null;
            }

            _prompt?.Dispose();
            _prompt = null;
        }

        /// <summary>
        /// RenderSettings belong to the loaded scene. Once the island owns the
        /// sky, putting the opening scene's skybox back would overwrite it, so
        /// the swap is only undone while our own material is still the one in
        /// use.
        /// </summary>
        private void ReleaseSpaceSky()
        {
            if (_skyboxInstance == null)
            {
                return;
            }

            if (ReferenceEquals(RenderSettings.skybox, _skyboxInstance))
            {
                RenderSettings.skybox = _skyboxOriginal;
            }

            Destroy(_skyboxInstance);
            _skyboxInstance = null;
        }

        // ----------------------------------------------------------------- //
        // Shot 1: already superluminal. The opening has no slow approach; the
        // first frame the player sees is the ship at full speed.
        // ----------------------------------------------------------------- //
        private void TickHyperspace()
        {
            var progress = Mathf.Clamp01(_shotElapsed / hyperspaceSeconds);
            var eased = SmoothStep(progress);

            var orbit = Mathf.Lerp(-19f, -3.5f, eased);
            var distance = Mathf.Lerp(36f, 24f, eased);
            var height = Mathf.Lerp(6.2f, 3.4f, EaseInOut(progress));
            PlaceChase(orbit, distance, height, 0.42f);

            bindings.ChaseCamera.fieldOfView =
                _chaseBaseFov + 16f - eased * 12f;

            SetWarpBlend(1f);
            SetWarpIntensity(3.4f);
            SetWarpSpeed(7.8f);
            DriveStarStreaks(
                speed: 260f,
                rate: 900f,
                stretch: 34f,
                lifetime: 0.55f);

            if (bindings.KeyLight != null)
            {
                bindings.KeyLight.intensity = 2.55f;
            }

            _shakeAmount = 0.14f;

            var grade = IntroGradeState.Cruise();
            grade.BloomIntensity = 3.1f;
            grade.ChromaticAberration = 0.38f;
            grade.LensDistortion = 0.32f;
            grade.MotionBlur = 0.66f;
            grade.Panini = 0.52f;
            grade.PostExposure = 0.42f;
            grade.Contrast = 22f;
            grade.VignetteIntensity = 0.42f;
            ApplyGrade(grade);

            // Open on black, but on a ship that is already moving.
            _fadeAlpha = 1f - SmoothStep(Mathf.Clamp01(_shotElapsed / 0.95f));

            if (_shotElapsed >= hyperspaceSeconds)
            {
                CutToCockpit();
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 2: hard cut to the seat. This is the framing the player gets
        // whenever they pilot the airship in the game, not a special one.
        // ----------------------------------------------------------------- //
        private void CutToCockpit()
        {
            ActivateCamera(bindings.CockpitCamera, bindings.ChaseCamera);
            bindings.CockpitCamera.fieldOfView = _cockpitBaseFov;
            Advance(Shot.Cockpit);
        }

        private void TickCockpit()
        {
            var progress = Mathf.Clamp01(_shotElapsed / cockpitSeconds);
            DriveCruiseSpeed();

            // The hull breathes at speed. A slow roll under the seat keeps the
            // shot alive without turning into a handheld camera.
            var roll = Mathf.Sin(Time.unscaledTime * 0.75f) * 0.55f;
            var pitch = Mathf.Sin(Time.unscaledTime * 0.51f + 1.7f) * 0.35f;
            bindings.AirshipAttitude.localRotation =
                Quaternion.Euler(pitch, 0f, roll);

            bindings.CockpitCamera.fieldOfView =
                _cockpitBaseFov + Mathf.Sin(progress * Mathf.PI) * 1.6f;
            bindings.CockpitFillLight.intensity = 1.35f;
            _shakeAmount = 0.05f;
            ApplyGrade(CockpitGrade());

            _flashAlpha = Mathf.Max(0f, 1f - _shotElapsed * 6.5f) * 0.5f;

            if (_shotElapsed >= cockpitSeconds)
            {
                _flightStep = FlightStep.Settle;
                _flightTimer = 0f;
                Advance(Shot.Flight);
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 3: the player flies. Two rocks on a collision course teach the
        // only control the opening needs, and the sequence does not continue
        // until the player has actually used it.
        // ----------------------------------------------------------------- //
        private void TickFlight()
        {
            ReadPilotInput();
            ApplyPilotHeading();
            DriveCruiseSpeed();
            bindings.CockpitFillLight.intensity = 1.35f;
            bindings.CockpitCamera.fieldOfView = _cockpitBaseFov;
            _shakeAmount = 0.05f;
            ApplyGrade(CockpitGrade());

            if (!_frozen)
            {
                _flightTimer += _delta;

                // The card dissolves rather than blinking out, so passing the
                // lesson feels like the world resuming.
                _greyAlpha = Mathf.MoveTowards(_greyAlpha, 0f, _delta * 3.2f);
                _promptAlpha = Mathf.MoveTowards(_promptAlpha, 0f, _delta * 4.5f);
            }

            TickAmbientAsteroids();

            switch (_flightStep)
            {
                case FlightStep.Settle:
                    if (_flightTimer >= flightSettleSeconds)
                    {
                        LaunchThreat(1f);
                        _flightStep = FlightStep.ApproachRight;
                    }

                    break;

                case FlightStep.ApproachRight:
                    TickThreat();
                    if (_threatDistance <= asteroidWarningDistance)
                    {
                        BeginLesson();
                        _flightStep = FlightStep.TeachRight;
                    }

                    break;

                case FlightStep.TeachRight:
                    TickThreat();
                    if (TickLesson(1f))
                    {
                        _flightStep = FlightStep.RecoverRight;
                        _flightTimer = 0f;
                    }

                    break;

                case FlightStep.RecoverRight:
                    TickThreat();
                    if (_flightTimer >= flightRecoverSeconds)
                    {
                        LaunchThreat(-1f);
                        _flightStep = FlightStep.ApproachLeft;
                    }

                    break;

                case FlightStep.ApproachLeft:
                    TickThreat();
                    if (_threatDistance <= asteroidWarningDistance)
                    {
                        BeginLesson();
                        _flightStep = FlightStep.TeachLeft;
                    }

                    break;

                case FlightStep.TeachLeft:
                    TickThreat();
                    if (TickLesson(-1f))
                    {
                        _flightStep = FlightStep.RecoverLeft;
                        _flightTimer = 0f;
                    }

                    break;

                case FlightStep.RecoverLeft:
                    TickThreat();
                    if (_flightTimer >= flightRecoverSeconds)
                    {
                        _flightStep = FlightStep.Handover;
                    }

                    break;

                case FlightStep.Handover:
                    Advance(Shot.Alarm);
                    break;
            }
        }

        private static IntroGradeState CockpitGrade()
        {
            var grade = IntroGradeState.Cruise();
            grade.BloomIntensity = 2.4f;
            // Behind glass the split lands on hundreds of tiny streak heads and
            // reads as coloured noise, so the cockpit gets far less of it than
            // the exterior shots.
            grade.ChromaticAberration = 0.16f;
            grade.LensDistortion = 0.14f;
            grade.MotionBlur = 0.5f;
            grade.Panini = 0.3f;
            grade.PostExposure = 0.28f;
            grade.VignetteIntensity = 0.44f;
            return grade;
        }

        private void DriveCruiseSpeed()
        {
            SetWarpBlend(1f);
            SetWarpIntensity(3.4f);
            SetWarpSpeed(7.8f);
            DriveStarStreaks(
                speed: 260f,
                rate: 900f,
                stretch: 34f,
                lifetime: 0.55f);
        }

        /// <summary>
        /// Mouse look on the hull itself. The lesson is about the airship, so
        /// the input turns the ship and the seat comes with it.
        /// </summary>
        private void ReadPilotInput()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var mouseDelta = mouse.delta.ReadValue();
            if (Mathf.Abs(mouseDelta.x) > 250f || Mathf.Abs(mouseDelta.y) > 250f)
            {
                return;
            }

            var yawDelta = mouseDelta.x * lookSensitivity;
            _pilotYaw += yawDelta;
            _lessonYaw += yawDelta;
            _pilotPitch = Mathf.Clamp(
                _pilotPitch - mouseDelta.y * lookSensitivity * 0.55f,
                -22f,
                22f);
        }

        private void ApplyPilotHeading()
        {
            if (bindings.AirshipHeading == null)
            {
                return;
            }

            // Banking into the turn is what makes a hull feel flown rather than
            // dragged around a pivot.
            var bank = Mathf.Clamp(-_pilotYaw * 0.35f, -28f, 28f);
            bindings.AirshipHeading.localRotation = Quaternion.Euler(
                _pilotPitch,
                _pilotYaw,
                bank);
        }

        private void LaunchThreat(float direction)
        {
            _threatDirection = direction;
            _promptDirection = direction;
            _threatDistance = asteroidLaunchDistance;
            _threatLateral = 0f;
            _threatCleared = 0f;
            _lessonYaw = 0f;
            _lessonHold = 0f;

            var threat = ThreatAsteroid();
            if (threat != null)
            {
                threat.gameObject.SetActive(true);
                _threatClearance = MeasureThreatClearance(threat);
            }
        }

        /// <summary>
        /// How far to the side the rock has to end up for a turn to actually
        /// clear it. Derived from the real sizes rather than a tuned constant:
        /// a rock wider than its own miss distance would pass straight through
        /// the hull no matter how well the lesson was performed.
        /// </summary>
        private float MeasureThreatClearance(Transform threat)
        {
            var rock = MeasureRendererBounds(threat);
            var hull = MeasureRendererBounds(bindings.Airship);
            return Mathf.Max(rock.extents.x, rock.extents.z)
                + Mathf.Max(hull.extents.x, hull.extents.z)
                + asteroidMissMargin;
        }

        private void TickThreat()
        {
            var threat = ThreatAsteroid();
            if (threat == null)
            {
                return;
            }

            if (!_frozen)
            {
                // Once the lesson is passed the rock slows down, so the pass is
                // something the player watches instead of something that is
                // over before they can register it.
                _threatDistance -= _delta * Mathf.Lerp(
                    asteroidApproachSpeed,
                    asteroidPassSpeed,
                    _threatCleared);
            }

            // The rock keeps its own straight line, so turning the ship already
            // slides it out of the windscreen. What the turn earns on top of
            // that is the lateral clearance that makes the pass a real miss.
            var earned = Mathf.Clamp01(
                _lessonYaw * _threatDirection / Mathf.Max(tutorialTurnDegrees, 1f));
            var offset = _threatClearance * Mathf.Max(earned, _threatCleared);
            _threatLateral = -_threatDirection * offset;

            var origin = bindings.Airship.position;
            threat.position = origin
                + _flightAxisForward * _threatDistance
                + _flightAxisRight * _threatLateral
                + _flightAxisUp * 4f;
            threat.Rotate(11f * _delta, 17f * _delta, 6f * _delta, Space.Self);

            if (_threatDistance < -140f)
            {
                threat.gameObject.SetActive(false);
            }
        }

        private void TickAmbientAsteroids()
        {
            var asteroids = bindings.Asteroids;
            if (asteroids == null || asteroids.Length <= 1)
            {
                return;
            }

            var origin = bindings.Airship.position;
            for (var index = 1; index < asteroids.Length; index++)
            {
                var asteroid = asteroids[index];
                if (asteroid == null)
                {
                    continue;
                }

                var drift = _asteroidDrift[index];
                if (!_frozen)
                {
                    drift.z -= _delta * 190f;
                    if (drift.z < -220f)
                    {
                        drift = NextAsteroidDrift(index);
                    }

                    _asteroidDrift[index] = drift;
                }

                asteroid.position = origin
                    + _flightAxisForward * drift.z
                    + _flightAxisRight * drift.x
                    + _flightAxisUp * drift.y;
                asteroid.Rotate(
                    9f * _delta,
                    13f * _delta,
                    5f * _delta,
                    Space.Self);
            }
        }

        private Vector3 NextAsteroidDrift(int index)
        {
            var random = new System.Random(unchecked(index * 7919 + _asteroidCycle++));
            return new Vector3(
                Mathf.Lerp(-260f, 260f, (float)random.NextDouble()),
                Mathf.Lerp(-140f, 160f, (float)random.NextDouble()),
                Mathf.Lerp(620f, 1250f, (float)random.NextDouble()));
        }

        private Transform ThreatAsteroid()
        {
            return bindings.Asteroids != null && bindings.Asteroids.Length > 0
                ? bindings.Asteroids[0]
                : null;
        }

        private void BeginLesson()
        {
            _frozen = true;
            _lessonYaw = 0f;
            _lessonHold = 0f;
            PauseParticles(true);
        }

        /// <summary>
        /// Holds the sequence until the player really turns the requested way.
        /// Input keeps steering while frozen, so the lesson is learnt by doing
        /// it rather than by reading it, and the turn has to be held for a beat
        /// before the world resumes: tripping a threshold on the way past does
        /// not teach anything.
        /// </summary>
        private bool TickLesson(float direction)
        {
            _greyAlpha = Mathf.MoveTowards(_greyAlpha, 0.52f, _delta * 3.4f);
            _promptAlpha = Mathf.MoveTowards(_promptAlpha, 1f, _delta * 3.4f);
            _promptDirection = direction;

            var progress = Mathf.Clamp01(
                _lessonYaw * direction / tutorialTurnDegrees);
            if (progress < 1f)
            {
                _lessonHold = 0f;
                return false;
            }

            _lessonHold += _delta;
            if (_lessonHold < tutorialHoldSeconds)
            {
                return false;
            }

            // The clearance is locked in here, so turning back afterwards can
            // no longer drag the rock into the hull.
            _threatCleared = 1f;
            _frozen = false;
            PauseParticles(false);
            return true;
        }

        private void PauseParticles(bool paused)
        {
            if (bindings.StarStreaks == null)
            {
                return;
            }

            if (paused)
            {
                bindings.StarStreaks.Pause(true);
            }
            else
            {
                bindings.StarStreaks.Play(true);
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 4: something breaks. Red light, shudder, sparks.
        // ----------------------------------------------------------------- //
        private void TickAlarm()
        {
            var progress = Mathf.Clamp01(_shotElapsed / alarmSeconds);
            var onset = SmoothStep(Mathf.Clamp01(_shotElapsed / 0.45f));

            // A klaxon lamp is a sharp attack and a long decay, never a sine.
            var beat = Mathf.Repeat(_shotElapsed, 0.82f) / 0.82f;
            _alarmPulse = Mathf.Pow(1f - beat, 2.6f) * onset;
            SetAlertIntensity(_alarmPulse * 9.5f);
            bindings.CockpitFillLight.intensity =
                Mathf.Lerp(1.35f, 0.18f, onset);

            var severity = onset * Mathf.Lerp(0.6f, 1f, progress);
            _shakeAmount = 0.05f + severity * 0.42f;

            var shudder = Mathf.Sin(Time.unscaledTime * 26f) * severity;
            var yaw = Mathf.Sin(Time.unscaledTime * 3.1f) * severity * 7.5f;
            bindings.AirshipAttitude.localRotation = Quaternion.Euler(
                shudder * 2.4f,
                yaw,
                -yaw * 1.35f + shudder * 3.2f);

            // The drive is losing containment: the tunnel stutters instead of
            // simply dimming.
            var stutter = 1f - severity * 0.55f * Mathf.PerlinNoise(
                Time.unscaledTime * 9f,
                0.37f);
            SetWarpIntensity(3.4f * stutter);
            SetWarpSpeed(7.8f * Mathf.Lerp(1f, 1.55f, severity) * stutter);
            SetWarpBlend(Mathf.Lerp(1f, 0.55f, progress));
            DriveStarStreaks(
                speed: Mathf.Lerp(260f, 150f, progress),
                rate: Mathf.Lerp(900f, 520f, progress),
                stretch: Mathf.Lerp(34f, 18f, progress),
                lifetime: 0.55f);

            EmitSparksOn(progress, 0.18f, 24);
            EmitSparksOn(progress, 0.52f, 34);
            EmitSparksOn(progress, 0.81f, 46);

            var grade = IntroGradeState.Cruise();
            grade.BloomIntensity = 2.4f + _alarmPulse * 1.6f;
            grade.ChromaticAberration = 0.44f + severity * 0.3f;
            grade.LensDistortion = 0.2f + severity * 0.12f;
            grade.MotionBlur = 0.5f;
            grade.Panini = 0.35f;
            grade.Saturation = Mathf.Lerp(6f, -22f, severity);
            grade.Contrast = Mathf.Lerp(12f, 26f, severity);
            grade.VignetteIntensity = 0.44f + _alarmPulse * 0.22f;
            grade.VignetteColor = Color.Lerp(
                Color.black,
                new Color(0.42f, 0.02f, 0.02f, 1f),
                onset);
            grade.ColorFilter = Color.Lerp(
                Color.white,
                new Color(1f, 0.82f, 0.78f, 1f),
                _alarmPulse);
            ApplyGrade(grade);

            // The drive fights the hull back to level while the alarm runs, so
            // the tear opens on a horizon the player can read.
            var level = 1f - Mathf.Exp(-3.2f * _delta);
            _pilotYaw = Mathf.Lerp(_pilotYaw, 0f, level);
            _pilotPitch = Mathf.Lerp(_pilotPitch, 0f, level);
            ApplyPilotHeading();
            HideAsteroids();

            if (_shotElapsed >= alarmSeconds)
            {
                BeginIslandLoad();
                AnchorRiftAxis();
                Advance(Shot.RiftOpen);
            }
        }

        private void HideAsteroids()
        {
            var asteroids = bindings.Asteroids;
            if (asteroids == null)
            {
                return;
            }

            for (var index = 0; index < asteroids.Length; index++)
            {
                if (asteroids[index] != null
                    && asteroids[index].gameObject.activeSelf)
                {
                    asteroids[index].gameObject.SetActive(false);
                }
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 5: the rift tears open ahead of the ship.
        // ----------------------------------------------------------------- //
        private void TickRiftOpen()
        {
            var progress = Mathf.Clamp01(_shotElapsed / riftOpenSeconds);

            // Slow, then all at once: the tear resists before it gives.
            var tear = Mathf.Pow(SmoothStep(progress), 1.9f);
            SetRiftOpenness(tear);

            // Holding the tear further out keeps black space around it, so it
            // still reads as an opening in something rather than as a wall.
            var approach = SmoothStep(Mathf.InverseLerp(0.18f, 1f, progress));
            SetRiftDistance(Mathf.Lerp(_riftStartDistance, 98f, approach));

            bindings.RiftLight.intensity = tear * 46f;
            bindings.RiftLight.color = Color.Lerp(
                new Color(0.32f, 0.82f, 1f, 1f),
                new Color(0.72f, 0.36f, 1f, 1f),
                tear);

            var alarm = Mathf.Pow(
                1f - Mathf.Repeat(_shotElapsed, 0.68f) / 0.68f,
                2.6f);
            SetAlertIntensity(alarm * 8f);
            _alarmPulse = alarm;

            _shakeAmount = 0.34f + tear * 0.55f;
            var shudder = Mathf.Sin(Time.unscaledTime * 24f) * tear;
            bindings.AirshipAttitude.localRotation = Quaternion.Euler(
                shudder * 2.8f,
                Mathf.Sin(Time.unscaledTime * 2.4f) * 5.5f * tear,
                shudder * 4.4f);

            SetWarpBlend(Mathf.Lerp(0.55f, 0.1f, progress));
            SetWarpIntensity(Mathf.Lerp(2.4f, 0.5f, progress));
            DriveStarStreaks(
                speed: Mathf.Lerp(150f, 60f, progress),
                rate: Mathf.Lerp(520f, 180f, progress),
                stretch: Mathf.Lerp(18f, 7f, progress),
                lifetime: 0.9f);

            DriveDebris(tear);
            bindings.CockpitCamera.fieldOfView =
                _cockpitBaseFov + tear * 9f;

            var grade = IntroGradeState.Cruise();
            grade.BloomIntensity = 2.4f + tear * 3.4f;
            grade.ChromaticAberration = 0.42f + tear * 0.4f;
            grade.LensDistortion = 0.24f + tear * 0.38f;
            grade.MotionBlur = 0.55f;
            grade.Panini = 0.42f;
            grade.PostExposure = tear * 0.75f;
            grade.Contrast = 24f;
            grade.Saturation = Mathf.Lerp(-22f, 12f, tear);
            grade.VignetteIntensity = 0.46f + alarm * 0.16f;
            grade.VignetteColor = Color.Lerp(
                new Color(0.42f, 0.02f, 0.02f, 1f),
                new Color(0.16f, 0.06f, 0.34f, 1f),
                tear);
            ApplyGrade(grade);

            // Two discharges as the tear widens.
            _flashAlpha =
                DischargeFlash(progress, 0.46f, 0.42f)
                + DischargeFlash(progress, 0.79f, 0.58f);

            if (_shotElapsed >= riftOpenSeconds)
            {
                Advance(Shot.RiftEntry);
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 6: pulled through. Everything blows out to white.
        // ----------------------------------------------------------------- //
        private void TickRiftEntry()
        {
            // Once activation is requested the opening scene is torn down under
            // our feet. Nothing bound to it may be touched again; the frame is
            // already white, so holding it is exactly the right picture.
            if (_islandActivationRequested)
            {
                _flashAlpha = 1f;
                _shakeAmount = 0f;
                return;
            }

            var progress = Mathf.Clamp01(_shotElapsed / riftEntrySeconds);
            var rush = Mathf.Pow(progress, 2.4f);

            SetRiftOpenness(1f);
            SetRiftDistance(Mathf.Lerp(98f, 5f, rush));
            bindings.RiftLight.intensity = 46f + rush * 90f;

            _shakeAmount = 0.9f * (1f - rush * 0.4f);
            bindings.CockpitCamera.fieldOfView =
                _cockpitBaseFov + 9f + rush * 26f;

            DriveDebris(1f);
            SetAlertIntensity(6f * (1f - rush));

            var grade = IntroGradeState.Cruise();
            grade.BloomIntensity = 5.8f + rush * 4f;
            grade.ChromaticAberration = Mathf.Lerp(0.82f, 1f, rush);
            grade.LensDistortion = Mathf.Lerp(0.62f, 0.9f, rush);
            grade.MotionBlur = 0.8f;
            grade.Panini = 0.5f;
            grade.PostExposure = 0.75f + rush * 1.9f;
            grade.VignetteIntensity = 0.5f;
            grade.VignetteColor = new Color(0.16f, 0.06f, 0.34f, 1f);
            ApplyGrade(grade);

            _flashAlpha = SmoothStep(Mathf.InverseLerp(0.55f, 1f, progress));

            if (_shotElapsed >= riftEntrySeconds)
            {
                ActivateIslandScene();
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 7: out the far side of the rift the airship is already falling.
        // It dives past the island's ancient portal on the way down.
        // ----------------------------------------------------------------- //
        private void TickFall()
        {
            if (_arrivalCamera == null)
            {
                Advance(Shot.Crash);
                return;
            }

            var progress = Mathf.Clamp01(_shotElapsed / fallSeconds);

            // Gravity, not a dolly: the descent accelerates all the way in.
            var travel = Mathf.Pow(progress, 1.55f);
            var position = FallPoint(travel);
            var ahead = FallPoint(Mathf.Min(travel + 0.035f, 1f));
            var heading = ahead - position;
            var flight = heading.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(heading.normalized, Vector3.up)
                : _diveRotation;

            // A wounded hull does not fly straight. The tumble grows as the
            // ground gets closer.
            var tumble = Mathf.Lerp(6f, 34f, progress);
            var wobble = Quaternion.Euler(
                Mathf.Sin(Time.unscaledTime * 2.3f) * tumble * 0.35f,
                Mathf.Sin(Time.unscaledTime * 1.7f) * tumble * 0.22f,
                Mathf.Sin(Time.unscaledTime * 2.9f) * tumble);

            // The real velocity of the last frame is what the skid inherits, so
            // touchdown continues the dive instead of restarting from nothing.
            if (_delta > 0.0001f)
            {
                _fallVelocity = (position - _fallShip.position) / _delta;
            }

            _fallShip.SetPositionAndRotation(position, flight * wobble);
            _arrivalCamera.fieldOfView = Mathf.Lerp(68f, 88f, SmoothStep(progress));
            _shakeAmount = Mathf.Lerp(0.35f, 0.95f, Mathf.Pow(progress, 1.6f));

            // The stone portal lights up as the wreck tears past it.
            var gate = 1f - Mathf.Abs(travel - PortalGateAt) / 0.22f;
            SetVeilCharge(Mathf.Clamp01(gate));

            var grade = IntroGradeState.Cruise();
            var settle = SmoothStep(Mathf.InverseLerp(0.02f, 0.42f, progress));
            grade.BloomIntensity = Mathf.Lerp(6.5f, 1.9f, settle);
            grade.ChromaticAberration = Mathf.Lerp(0.95f, 0.22f, settle)
                + progress * 0.34f;
            grade.LensDistortion = Mathf.Lerp(0.85f, 0.12f, settle)
                + progress * 0.24f;
            grade.MotionBlur = Mathf.Lerp(0.8f, 0.42f, settle) + progress * 0.3f;
            grade.Panini = Mathf.Lerp(0.5f, 0.18f, settle);
            grade.PostExposure = Mathf.Lerp(2.4f, 0f, settle);
            grade.Saturation = Mathf.Lerp(-30f, 4f, settle);
            grade.VignetteIntensity = Mathf.Lerp(0.62f, 0.34f, settle)
                + progress * 0.2f;
            ApplyGrade(grade);

            _flashAlpha = 1f - SmoothStep(Mathf.Clamp01(_shotElapsed / 0.9f));

            if (_shotElapsed >= fallSeconds)
            {
                Advance(Shot.Crash);
            }
        }

        // ----------------------------------------------------------------- //
        // Shot 8: impact. The eyes slam shut and stay shut.
        // ----------------------------------------------------------------- //
        private void TickCrash()
        {
            SetVeilCharge(0f);
            if (_fallShip == null)
            {
                _fadeAlpha = 1f;
                _eyelid = 1f;
                Advance(Shot.Blackout);
                return;
            }

            if (!_crashStarted)
            {
                BeginCrash();
            }

            var speed = TickSkid();
            var carry = Mathf.Clamp01(speed / Mathf.Max(crashTouchdownSpeed, 1f));

            // Everything is driven by how much speed is left, so the shot ends
            // because the wreck has come to rest and not because a timer ran
            // out underneath it.
            var stopped = 1f - carry;
            var impact = Mathf.Exp(-_shotElapsed * 4.5f);
            _shakeAmount = 0.25f + carry * 1.15f + impact * 0.5f + _crashJolt;
            _crashJolt = Mathf.MoveTowards(_crashJolt, 0f, _delta * 3.2f);
            _flashAlpha = Mathf.Max(0f, 0.7f - _shotElapsed * 4.5f);

            var grade = IntroGradeState.Cruise();
            grade.BloomIntensity = 1.9f;
            grade.ChromaticAberration = 0.42f * carry + 0.06f;
            grade.LensDistortion = 0.3f * carry;
            grade.MotionBlur = 0.4f + carry * 0.35f;
            grade.Saturation = Mathf.Lerp(4f, -50f, stopped);
            grade.VignetteIntensity = Mathf.Lerp(0.54f, 0.9f, stopped);
            ApplyGrade(grade);

            // Eyelids, not a dip to black: they close from top and bottom while
            // the hull is still sliding, and consciousness goes before the
            // picture does.
            var fading = Mathf.Max(stopped, _shotElapsed / crashSeconds);
            _eyelid = SmoothStep(Mathf.InverseLerp(0.18f, 0.88f, fading));
            _fadeAlpha = SmoothStep(Mathf.InverseLerp(0.72f, 1f, fading));

            if (fading >= 1f || _shotElapsed >= crashSeconds)
            {
                _fadeAlpha = 1f;
                _eyelid = 1f;
                Advance(Shot.Blackout);
            }
        }

        private void BeginCrash()
        {
            _crashStarted = true;

            // Keep the direction the dive earned, take the magnitude from the
            // tuning: a predictable touchdown speed is what makes the plough
            // end where the wreck is supposed to come to rest.
            var heading = _fallVelocity;
            heading.y = 0f;
            heading = heading.sqrMagnitude > 0.0001f
                ? heading.normalized
                : _fallShip.forward;

            _crashVelocity = heading * crashTouchdownSpeed;
            _crashVerticalVelocity = -34f;
            _crashJolt = 1.4f;
        }

        /// <summary>
        /// Ground contact and plough. The hull keeps its own momentum, sheds it
        /// against the terrain, bounces once or twice and follows the slope
        /// until it stops.
        /// </summary>
        private float TickSkid()
        {
            var speed = _crashVelocity.magnitude;
            var slowed = Mathf.MoveTowards(speed, 0f, crashFriction * _delta);
            _crashVelocity = speed > 0.0001f
                ? _crashVelocity * (slowed / speed)
                : Vector3.zero;

            var position = _fallShip.position + _crashVelocity * _delta;

            _crashVerticalVelocity -= 30f * _delta;
            position.y += _crashVerticalVelocity * _delta;

            var floor = GroundHeight(position) + crashHullClearance;
            if (position.y <= floor)
            {
                position.y = floor;
                if (_crashVerticalVelocity < -7f)
                {
                    // Every touch of the ground throws the nose back up a
                    // little less than the one before.
                    _crashVerticalVelocity *= -0.34f;
                    _crashJolt = Mathf.Max(_crashJolt, slowed * 0.012f + 0.35f);
                }
                else
                {
                    _crashVerticalVelocity = 0f;
                }
            }

            var carry = Mathf.Clamp01(slowed / Mathf.Max(crashTouchdownSpeed, 1f));
            var travelling = _crashVelocity.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(_crashVelocity.normalized, Vector3.up)
                : _fallShip.rotation;

            // Nose down into the dirt, slewing sideways, both easing off as the
            // hull loses way.
            var slew = Mathf.Sin(Time.unscaledTime * 1.9f) * 16f * carry;
            var dig = Mathf.Lerp(2f, 21f, carry);
            var roll = Mathf.Sin(Time.unscaledTime * 3.1f) * 19f * carry;
            _fallShip.SetPositionAndRotation(
                position,
                travelling * Quaternion.Euler(dig, slew * 0.35f, roll));

            return slowed;
        }

        // ----------------------------------------------------------------- //
        // Shot 9: pure black. The whole cinematic is taken apart behind it.
        // ----------------------------------------------------------------- //
        private void TickBlackout()
        {
            _fadeAlpha = 1f;
            _eyelid = 1f;
            _flashAlpha = 0f;
            _shakeAmount = 0f;

            // Done on the first frame of full black, never during the fade:
            // the wreck disappearing and the parked hull coming back must not
            // be visible for a single frame.
            HandOverToTheIsland();

            if (_shotElapsed >= blackoutSeconds)
            {
                Advance(Shot.Wake);
            }
        }

        /// <summary>
        /// Everything that belongs to the cinematic is dismantled here, behind
        /// a black screen, so the waking shot is already the island rendering
        /// itself: its lighting, its volume, its skybox, its player camera.
        /// Gameplay itself stays suspended until the eyes are open.
        /// </summary>
        private void HandOverToTheIsland()
        {
            if (_handedOver)
            {
                return;
            }

            _handedOver = true;
            MoveThePlayerAboard();
            TeardownArrivalRig();
            ReleaseSpaceSky();
            _grade?.SetActive(false);
            RevealPlayerCamera();
        }

        /// <summary>
        /// Brings the island's own camera back without waking the player up:
        /// the image is already the main scene, but nothing accepts input yet.
        /// </summary>
        /// <summary>
        /// Reads as a body coming back up: the view starts low and rolled onto
        /// its side on the deck, then rights itself and rises to the standing
        /// pose. Only the camera's local transform moves, so the authoritative
        /// player position is never touched.
        /// </summary>
        private void AnimateGettingUp(float progress)
        {
            if (_wakeCamera == null)
            {
                return;
            }

            var rise = SmoothStep(Mathf.InverseLerp(0.18f, 0.92f, progress));
            var settle = SmoothStep(Mathf.InverseLerp(0.55f, 1f, progress));

            // A slow sway on the way up, fading out as balance returns.
            var sway = Mathf.Sin(Time.unscaledTime * 1.35f)
                * 2.4f
                * (1f - settle);

            _wakeCamera.localPosition = _wakeCameraRestPosition
                + Vector3.up * Mathf.Lerp(-0.82f, 0f, rise)
                + Vector3.forward * Mathf.Lerp(-0.16f, 0f, rise);
            _wakeCamera.localRotation = _wakeCameraRestRotation
                * Quaternion.Euler(
                    Mathf.Lerp(26f, 0f, rise),
                    sway * 0.6f,
                    Mathf.Lerp(-34f, 0f, rise) + sway);

            if (progress >= 1f)
            {
                _wakeCamera.localPosition = _wakeCameraRestPosition;
                _wakeCamera.localRotation = _wakeCameraRestRotation;
            }
        }

        private void RevealPlayerCamera()
        {
            if (_playerCamera == null)
            {
                return;
            }

            _playerCamera.tag = string.IsNullOrEmpty(_playerCameraTag)
                ? "MainCamera"
                : _playerCameraTag;
            _playerCamera.enabled = _playerCameraWasEnabled;
            if (_playerAudioListener != null)
            {
                _playerAudioListener.enabled = _playerAudioListenerWasEnabled;
            }

            _wakeCamera = _playerCamera.transform;
            _wakeCameraRestPosition = _wakeCamera.localPosition;
            _wakeCameraRestRotation = _wakeCamera.localRotation;
        }

        // ----------------------------------------------------------------- //
        // Shot 9: waking up aboard the airship, inside the island scene and
        // with nothing of the cinematic left on the image.
        // ----------------------------------------------------------------- //
        private void TickWake()
        {
            var progress = Mathf.Clamp01(_shotElapsed / wakeSeconds);

            // Two half-blinks before the eyes stay open.
            var blink =
                Mathf.Clamp01(1f - progress * 2.2f)
                + Mathf.Max(0f, 0.62f - Mathf.Abs(progress - 0.38f) * 5f);
            _fadeAlpha = Mathf.Clamp01(blink) * 0.94f;
            _eyelid = Mathf.Clamp01(
                Mathf.Max(blink, 1f - SmoothStep(progress * 1.15f)));

            AnimateGettingUp(progress);

            if (progress >= 1f)
            {
                Finish();
            }
        }

        // ----------------------------------------------------------------- //
        // Scene handover.
        // ----------------------------------------------------------------- //
        private void BeginIslandLoad()
        {
            if (_islandLoad != null)
            {
                return;
            }

            try
            {
                _islandLoad = SceneManager.LoadSceneAsync(
                    gameplaySceneName,
                    LoadSceneMode.Single);
                if (_islandLoad != null)
                {
                    // The island only appears once the frame is already white,
                    // so the load never shows as a stall mid-shot.
                    _islandLoad.allowSceneActivation = false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "CML intro cinematic could not load gameplay scene '"
                    + gameplaySceneName + "': " + exception.Message);
                Finish();
            }
        }

        private void ActivateIslandScene()
        {
            if (_islandActivationRequested)
            {
                return;
            }

            BeginIslandLoad();
            if (_islandLoad == null)
            {
                Finish();
                return;
            }

            _islandActivationRequested = true;
            _flashAlpha = 1f;

            // The whole hyperspace set hangs off this director, and this
            // director survives the scene change. Without switching it off it
            // keeps drawing tunnel, streaks, hull and rift on top of the
            // island, straight through the crash and the wake.
            if (bindings.SpaceRoot != null)
            {
                bindings.SpaceRoot.gameObject.SetActive(false);
            }

            _islandLoad.allowSceneActivation = true;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_islandActivationRequested
                || _shot >= Shot.Fall
                || !string.Equals(
                    scene.name,
                    gameplaySceneName,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (!BeginFall())
            {
                RestoreGameplay();
                Finish();
                return;
            }

            Advance(Shot.Fall);
        }

        private bool BeginFall()
        {
            var portal = FindSceneTransform(AncientPortalObjectName);
            _playerController = FindFirstObjectByType<CharacterController>();
            _gameplayAirship = FindSceneTransform(AirshipObjectName);
            if (portal == null || _playerController == null
                || _gameplayAirship == null)
            {
                Debug.LogError(
                    "CML intro arrival needs " + AncientPortalObjectName
                    + ", " + AirshipObjectName
                    + " and the player CharacterController in '"
                    + gameplaySceneName + "'.");
                return false;
            }

            SuspendGameplay();

            var bounds = MeasureRendererBounds(portal);
            var apertureCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + bounds.size.y * portalApertureHeight,
                bounds.center.z);
            var apertureRadius = bounds.size.y * portalApertureRadius;

            // The portal was authored looking at the island centre, so flying
            // along +forward is the direction that carries the wreck inland.
            var facing = portal.forward;
            facing.y = 0f;
            facing = facing.sqrMagnitude > 0.0001f
                ? facing.normalized
                : Vector3.forward;

            // The dive no longer ends at the crash site: it ends where the
            // wreck first touches down, far enough short that the plough is
            // what carries it the rest of the way. Distance comes from the
            // tuning, so speed, drag and resting place stay consistent.
            var crashSite = _gameplayAirship.position;
            var skidDistance = crashTouchdownSpeed * crashTouchdownSpeed
                / (2f * Mathf.Max(crashFriction, 1f));

            // The hull is far wider than the arch, so it screams past the
            // pillars rather than through them.
            var sideways = Vector3.Cross(Vector3.up, facing).normalized;
            _fallGate = apertureCenter
                + sideways * (apertureRadius + 15f)
                + Vector3.up * (apertureRadius * 0.35f);

            // Spat out of the rift high above and behind the portal.
            var toCrash = (crashSite - apertureCenter);
            toCrash.y = 0f;
            var inbound = toCrash.sqrMagnitude > 0.0001f
                ? toCrash.normalized
                : facing;

            var touchdown = crashSite - inbound * skidDistance;
            touchdown.y = GroundHeight(touchdown) + crashHullClearance;
            _fallEnd = touchdown;

            _fallStart = apertureCenter - inbound * 240f;
            _fallStart.y = Mathf.Max(
                apertureCenter.y + 210f,
                GroundHeight(_fallStart) + 190f);
            _diveRotation = Quaternion.LookRotation(inbound, Vector3.up);

            if (!CreateFallingAirship())
            {
                return false;
            }

            CreatePortalVeil(apertureCenter, -facing, apertureRadius);
            return true;
        }

        /// <summary>
        /// Descent path. Routing it through an explicit gate point beside the
        /// arch is what guarantees the portal is actually in shot on the way
        /// down instead of somewhere off screen.
        /// </summary>
        private Vector3 FallPoint(float travel)
        {
            return travel <= PortalGateAt
                ? Vector3.Lerp(_fallStart, _fallGate, travel / PortalGateAt)
                : Vector3.Lerp(
                    _fallGate,
                    _fallEnd,
                    (travel - PortalGateAt) / (1f - PortalGateAt));
        }

        /// <summary>
        /// A render-only twin of the parked airship carries the crash. None of
        /// its scripts survive the copy, and the real hull is hidden until the
        /// wreck is gone, so only one airship is ever on screen.
        /// </summary>
        private bool CreateFallingAirship()
        {
            var copy = Instantiate(
                _gameplayAirship.gameObject,
                _fallStart,
                _diveRotation);
            copy.name = "CIN_FallingAirship";
            _fallShip = copy.transform;

            var behaviours = copy.GetComponentsInChildren<Behaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] != null)
                {
                    behaviours[index].enabled = false;
                }
            }

            var colliders = copy.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != null)
                {
                    colliders[index].enabled = false;
                }
            }

            HideParkedAirship(true);

            var anchor = FindDescendant(_fallShip, "REF_PilotCamera");
            if (anchor == null)
            {
                Debug.LogError(
                    "CML intro crash needs REF_PilotCamera on the airship.");
                return false;
            }

            CreateArrivalRig(_fallShip, anchor);
            return true;
        }

        private void HideParkedAirship(bool hidden)
        {
            if (_gameplayAirship == null)
            {
                return;
            }

            var renderers = _gameplayAirship.GetComponentsInChildren<Renderer>(true);
            if (hidden)
            {
                _hiddenAirshipRenderers.Clear();
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (hidden)
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        _hiddenAirshipRenderers.Add(renderer);
                    }
                }
            }

            if (hidden)
            {
                return;
            }

            for (var index = 0; index < _hiddenAirshipRenderers.Count; index++)
            {
                if (_hiddenAirshipRenderers[index] != null)
                {
                    _hiddenAirshipRenderers[index].enabled = true;
                }
            }

            _hiddenAirshipRenderers.Clear();
        }

        private static Transform FindDescendant(Transform root, string exactName)
        {
            var descendants = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < descendants.Length; index++)
            {
                if (string.Equals(
                        descendants[index].name,
                        exactName,
                        StringComparison.Ordinal))
                {
                    return descendants[index];
                }
            }

            return null;
        }

        private static float GroundHeight(Vector3 position)
        {
            var origin = new Vector3(position.x, position.y + 600f, position.z);
            return Physics.Raycast(
                origin,
                Vector3.down,
                out var hit,
                1600f,
                ~0,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : position.y;
        }

        private static Vector3 LiftAboveGround(Vector3 position, float clearance)
        {
            position.y = Mathf.Max(
                position.y,
                GroundHeight(position) + clearance);
            return position;
        }

        /// <summary>
        /// Same rule as the cockpit shot: the eye goes on the authored anchor,
        /// the orientation comes from the hull. Hanging the camera off the
        /// anchor itself would inherit a rotation the player never flies with.
        /// </summary>
        private void CreateArrivalRig(Transform shipRoot, Transform anchor)
        {
            var pivot = new GameObject("CIN_ArrivalRig");
            pivot.transform.SetParent(shipRoot, false);
            pivot.transform.localPosition =
                shipRoot.InverseTransformPoint(anchor.position);
            pivot.transform.localRotation = Quaternion.identity;
            _arrivalShake = pivot.transform;

            var cameraObject = new GameObject("CIN_ArrivalCamera");
            cameraObject.transform.SetParent(pivot.transform, false);
            cameraObject.tag = "MainCamera";
            _arrivalCamera = cameraObject.AddComponent<Camera>();
            _arrivalCamera.fieldOfView = 68f;
            _arrivalCamera.nearClipPlane = 0.02f;
            _arrivalCamera.farClipPlane = 2400f;
            _arrivalCamera.allowHDR = true;
            _arrivalCamera.allowMSAA = true;
            _arrivalCamera.clearFlags = CameraClearFlags.Skybox;
            _arrivalCamera.depthTextureMode |= DepthTextureMode.Depth;

            // The arrival is graded by the same volume as the space shots, so
            // it needs post-processing enabled just like the cockpit camera.
            var urp = cameraObject
                .AddComponent<UnityEngine.Rendering.Universal
                    .UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = UnityEngine.Rendering.Universal
                .AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            urp.requiresColorOption =
                UnityEngine.Rendering.Universal.CameraOverrideOption.On;
            urp.requiresDepthOption =
                UnityEngine.Rendering.Universal.CameraOverrideOption.On;

            cameraObject.AddComponent<AudioListener>();
        }

        private void CreatePortalVeil(
            Vector3 center,
            Vector3 normal,
            float radius)
        {
            var veil = GameObject.CreatePrimitive(PrimitiveType.Quad);
            veil.name = "CIN_PortalVeil";
            var collider = veil.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            veil.transform.SetPositionAndRotation(
                center,
                Quaternion.LookRotation(normal, Vector3.up));
            veil.transform.localScale = Vector3.one * (radius * 2f);

            var renderer = veil.GetComponent<Renderer>();
            renderer.sharedMaterial = _veilMaterial;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _portalVeil = veil.transform;
            SetVeilCharge(0f);
        }

        private void TeardownArrivalRig()
        {
            if (_portalVeil != null)
            {
                Destroy(_portalVeil.gameObject);
                _portalVeil = null;
            }

            if (_arrivalCamera != null)
            {
                _arrivalCamera.enabled = false;
                _arrivalCamera.tag = "Untagged";
                var listener = _arrivalCamera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }

            if (_fallShip != null)
            {
                Destroy(_fallShip.gameObject);
                _fallShip = null;
                _arrivalShake = null;
                _arrivalCamera = null;
            }

            HideParkedAirship(false);

            if (_arrivalShake != null)
            {
                Destroy(_arrivalShake.gameObject);
                _arrivalShake = null;
                _arrivalCamera = null;
            }
        }

        /// <summary>
        /// Puts the player exactly where the project already considers a body
        /// to be standing inside the hull, so waking up aboard is the same pose
        /// the airship rig produces when a pilot leaves the seat.
        /// </summary>
        private void MoveThePlayerAboard()
        {
            if (_playerController == null || _gameplayAirship == null)
            {
                return;
            }

            var localStand = new Vector3(
                AirshipSimulationConstants.PilotExitBodyRootPosition.X / 1000f,
                AirshipSimulationConstants.PilotExitBodyRootPosition.Y / 1000f,
                AirshipSimulationConstants.PilotExitBodyRootPosition.Z / 1000f);

            var stand = _gameplayAirship.TransformPoint(localStand);

            // Settle the feet onto the deck. The authored pose is a body root,
            // not a contact point, and the hull is scaled: re-enabling the
            // capsule a few centimetres above the floor resolves as a drop on
            // the exact frame control comes back, which reads as a jolt right
            // after the eyes open.
            // The probe starts just above the authored pose, never higher: from
            // far enough up it would clear the cabin roof and put the pilot on
            // top of the hull instead of inside it.
            var probe = stand + Vector3.up * 0.5f;
            if (Physics.Raycast(
                    probe,
                    Vector3.down,
                    out var hit,
                    1.5f,
                    ~0,
                    QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(_gameplayAirship))
            {
                stand.y = hit.point.y + _playerController.skinWidth;
            }

            var wasEnabled = _playerController.enabled;
            _playerController.enabled = false;
            _playerController.transform.SetPositionAndRotation(
                stand,
                _gameplayAirship.rotation);
            _playerController.enabled = wasEnabled;
        }

        // ----------------------------------------------------------------- //
        // Gameplay suspension. No UIDocument is ever disabled: UI Toolkit
        // rebuilds its whole visual tree on the next enable and every panel
        // would come back bound to elements nobody renders.
        // ----------------------------------------------------------------- //
        private void SuspendGameplay()
        {
            _playerCamera = Camera.main;
            if (_playerCamera == null)
            {
                _playerCamera = FindFirstObjectByType<Camera>();
            }

            if (_playerCamera != null)
            {
                _playerCameraWasEnabled = _playerCamera.enabled;
                _playerCameraTag = _playerCamera.tag;
                _playerCamera.enabled = false;
                _playerCamera.tag = "Untagged";

                _playerAudioListener = _playerCamera.GetComponent<AudioListener>();
                if (_playerAudioListener != null)
                {
                    _playerAudioListenerWasEnabled = _playerAudioListener.enabled;
                    _playerAudioListener.enabled = false;
                }
            }

            _playerControllerWasEnabled = _playerController.enabled;
            _playerController.enabled = false;
            SuspendEnabledBehaviours(
                _playerController.gameObject,
                _suspendedPlayerBehaviours);

            _hudOrchestrator = FindFirstObjectByType<FactoryHudOrchestrator>();
            if (_hudOrchestrator != null)
            {
                _hudOrchestrator.SetCinematicSuppressed(true);
            }

            var documents = FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < documents.Length; index++)
            {
                var document = documents[index];
                var root = document != null ? document.rootVisualElement : null;
                if (root == null)
                {
                    continue;
                }

                root.style.display = UnityEngine.UIElements.DisplayStyle.None;
                _hiddenUiDocuments.Add(document);
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour != null
                    && behaviour.enabled
                    && behaviour.GetType().Name == "BuildInfoOverlay")
                {
                    behaviour.enabled = false;
                    _hiddenOverlayBehaviours.Add(behaviour);
                }
            }
        }

        private void RestoreGameplay()
        {
            if (_gameplayRestored)
            {
                return;
            }

            _gameplayRestored = true;

            RestoreBehaviours(_suspendedPlayerBehaviours);
            if (_playerController != null)
            {
                _playerController.enabled = _playerControllerWasEnabled;
            }

            for (var index = 0; index < _hiddenUiDocuments.Count; index++)
            {
                var document = _hiddenUiDocuments[index];
                var root = document != null ? document.rootVisualElement : null;
                if (root != null)
                {
                    root.style.display =
                        UnityEngine.UIElements.StyleKeyword.Null;
                }
            }

            // Not "?.": that is a plain C# null check and it walks straight
            // past Unity's destroyed-object sentinel. Tearing the intro down
            // after the island scene has already gone would then throw.
            if (_hudOrchestrator != null)
            {
                _hudOrchestrator.SetCinematicSuppressed(false);
            }
            RestoreBehaviours(_hiddenOverlayBehaviours);

            // The camera is handed back earlier, at the top of the blackout, so
            // the waking shot is already rendered by the island itself.
            RevealPlayerCamera();
            if (_wakeCamera != null)
            {
                _wakeCamera.localPosition = _wakeCameraRestPosition;
                _wakeCamera.localRotation = _wakeCameraRestRotation;
            }

            LockCursor();
        }

        // ----------------------------------------------------------------- //
        // Actor drivers.
        // ----------------------------------------------------------------- //
        private void PlaceChase(
            float orbitDegrees,
            float distance,
            float height,
            float driftAmount)
        {
            var pivot = bindings.Airship.position;
            var rotation = Quaternion.Euler(0f, orbitDegrees, 0f);
            var offset = rotation * new Vector3(0f, height, -distance);

            // A little low-frequency drift keeps the rig from feeling welded to
            // the hull without reading as camera shake.
            var drift = new Vector3(
                Mathf.PerlinNoise(Time.unscaledTime * 0.23f, 4.1f) - 0.5f,
                Mathf.PerlinNoise(7.3f, Time.unscaledTime * 0.19f) - 0.5f,
                Mathf.PerlinNoise(Time.unscaledTime * 0.17f, 2.7f) - 0.5f)
                * driftAmount * 2f;

            bindings.ChaseRig.position = pivot + offset + drift;
            bindings.ChaseRig.rotation = Quaternion.LookRotation(
                (pivot + Vector3.up * 1.9f) - bindings.ChaseRig.position,
                Vector3.up);
        }

        private void ApplyShake(Transform target)
        {
            if (target == null)
            {
                return;
            }

            if (_shakeAmount <= 0.0001f)
            {
                target.localPosition = Vector3.zero;
                target.localRotation = Quaternion.identity;
                return;
            }

            var t = Time.unscaledTime * 21f;
            var offset = new Vector3(
                Mathf.PerlinNoise(t, 0.13f) - 0.5f,
                Mathf.PerlinNoise(0.71f, t) - 0.5f,
                Mathf.PerlinNoise(t * 0.63f, t * 0.41f) - 0.5f);
            target.localPosition = offset * (_shakeAmount * 0.14f);
            target.localRotation = Quaternion.Euler(
                offset * (_shakeAmount * 2.6f));
        }

        private void DriveStarStreaks(
            float speed,
            float rate,
            float stretch,
            float lifetime)
        {
            var main = bindings.StarStreaks.main;
            main.startSpeed = speed;
            main.startLifetime = lifetime;

            var emission = bindings.StarStreaks.emission;
            emission.rateOverTime = rate;

            if (_starStreakRenderer != null)
            {
                _starStreakRenderer.lengthScale = stretch;
            }
        }

        private void DriveDebris(float pull)
        {
            var emission = bindings.RiftDebris.emission;
            emission.rateOverTime = pull * 160f;

            var main = bindings.RiftDebris.main;
            main.startSpeed = 12f + pull * 48f;

            if (pull > 0.02f && !bindings.RiftDebris.isPlaying)
            {
                bindings.RiftDebris.Play();
            }
        }

        private void EmitSparksOn(float progress, float trigger, int count)
        {
            var previous = (_shotElapsed - _delta) / alarmSeconds;
            if (previous < trigger && progress >= trigger)
            {
                bindings.CockpitSparks.Emit(count);
            }
        }

        private void SetAlertIntensity(float intensity)
        {
            var lights = bindings.AlertLights;
            for (var index = 0; index < lights.Length; index++)
            {
                var light = lights[index];
                if (light == null)
                {
                    continue;
                }

                light.enabled = intensity > 0.02f;
                light.intensity = intensity;
            }
        }

        private void SetWarpIntensity(float intensity)
        {
            if (_warpMaterial != null)
            {
                _warpMaterial.SetFloat("_Intensity", Mathf.Max(0f, intensity));
            }
        }

        private void SetWarpSpeed(float speed)
        {
            if (_warpMaterial != null)
            {
                _warpMaterial.SetFloat("_Speed", Mathf.Max(0f, speed));
            }
        }

        private void SetWarpBlend(float blend)
        {
            _warpBlend = Mathf.Clamp01(blend);
            if (_skyboxInstance != null)
            {
                _skyboxInstance.SetFloat("_WarpBlend", _warpBlend);
            }
        }

        /// <summary>
        /// The tear has to sit dead centre in the cockpit view, and the
        /// authored eye anchor is not axis aligned. Reading the optical axis at
        /// the moment the shot starts keeps the framing right even after the
        /// player has spent the flight leg turning the ship.
        /// </summary>
        private void AnchorRiftAxis()
        {
            if (bindings.CockpitCamera == null)
            {
                return;
            }

            var eye = bindings.CockpitCamera.transform;
            _riftOrigin = eye.position;
            _riftForward = eye.forward;
            SetRiftDistance(_riftStartDistance);
        }

        private void InitialiseAsteroids()
        {
            var asteroids = bindings.Asteroids;
            if (asteroids == null || asteroids.Length == 0)
            {
                _asteroidDrift = Array.Empty<Vector3>();
                return;
            }

            _asteroidDrift = new Vector3[asteroids.Length];
            for (var index = 0; index < asteroids.Length; index++)
            {
                if (asteroids[index] == null)
                {
                    continue;
                }

                _asteroidDrift[index] = NextAsteroidDrift(index);

                // The teaching rock only exists when a lesson needs it.
                asteroids[index].gameObject.SetActive(index != 0);
            }
        }

        private void SetRiftDistance(float distance)
        {
            if (bindings.Rift == null)
            {
                return;
            }

            var position = _riftOrigin + _riftForward * Mathf.Max(distance, 0.5f);
            bindings.Rift.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(_riftForward, Vector3.up));
        }

        private void SetRiftOpenness(float openness)
        {
            if (_riftMaterial != null)
            {
                _riftMaterial.SetFloat("_Openness", Mathf.Clamp01(openness));
            }
        }

        private void SetVeilCharge(float charge)
        {
            if (_veilMaterial != null)
            {
                _veilMaterial.SetFloat("_Charge", Mathf.Clamp01(charge));
            }
        }

        private void ApplyGrade(in IntroGradeState state)
        {
            _grade?.Apply(state);
        }

        // ----------------------------------------------------------------- //
        // Flow control.
        // ----------------------------------------------------------------- //
        private void Advance(Shot next)
        {
            _shot = next;
            _shotElapsed = 0f;
        }

        private void Skip()
        {
            // Skipping also releases a frozen lesson, otherwise the sequence
            // would sit waiting for input nobody is going to give.
            if (_frozen)
            {
                _frozen = false;
                PauseParticles(false);
            }

            _greyAlpha = 0f;
            _promptAlpha = 0f;

            if (_shot < Shot.Fall)
            {
                _flashAlpha = 1f;
                _fadeAlpha = 0f;
                _shakeAmount = 0f;
                HideAsteroids();
                Advance(Shot.RiftEntry);
                _shotElapsed = riftEntrySeconds;
                ActivateIslandScene();
                return;
            }

            if (_shot == Shot.Fall)
            {
                Advance(Shot.Crash);
                return;
            }

            if (_shot == Shot.Crash)
            {
                _shotElapsed = crashSeconds;
                return;
            }

            if (_shot == Shot.Blackout)
            {
                _shotElapsed = blackoutSeconds;
            }
        }

        private void Finish()
        {
            RestoreGameplay();
            TeardownArrivalRig();
            _shot = Shot.Complete;
            _fadeAlpha = 0f;
            _flashAlpha = 0f;
            _eyelid = 0f;
            Destroy(gameObject);
        }

        private void ActivateCamera(Camera active, Camera inactive)
        {
            if (inactive != null)
            {
                inactive.tag = "Untagged";
                inactive.enabled = false;
                var listener = inactive.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }

            if (active != null)
            {
                active.tag = "MainCamera";
                active.enabled = true;
                var listener = active.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = true;
                }
            }
        }

        private static void SuspendEnabledBehaviours(
            GameObject root,
            ICollection<Behaviour> destination)
        {
            if (root == null)
            {
                return;
            }

            var behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour != null && behaviour.enabled)
                {
                    behaviour.enabled = false;
                    destination.Add(behaviour);
                }
            }
        }

        private static void RestoreBehaviours(IReadOnlyList<Behaviour> behaviours)
        {
            for (var index = 0; index < behaviours.Count; index++)
            {
                if (behaviours[index] != null)
                {
                    behaviours[index].enabled = true;
                }
            }
        }

        private static Bounds MeasureRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.position, Vector3.one * 20f);
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static Transform FindSceneTransform(string exactName)
        {
            var candidate = GameObject.Find(exactName);
            return candidate != null ? candidate.transform : null;
        }

        private static float DischargeFlash(
            float progress,
            float at,
            float strength)
        {
            return Mathf.Max(0f, 1f - Mathf.Abs(progress - at) * 22f) * strength;
        }

        private static float SmoothStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float EaseInOut(float value)
        {
            value = Mathf.Clamp01(value);
            return value < 0.5f
                ? 2f * value * value
                : 1f - Mathf.Pow(-2f * value + 2f, 2f) * 0.5f;
        }

        private static void LockCursor()
        {
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
        }

        private static bool IsSkipRequested()
        {
            var keyboard = Keyboard.current;
            return keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame
                    || keyboard.escapeKey.wasPressedThisFrame);
        }

        private enum Shot
        {
            Hyperspace,
            Cockpit,
            Flight,
            Alarm,
            RiftOpen,
            RiftEntry,
            Fall,
            Crash,
            Blackout,
            Wake,
            Complete
        }

        private enum FlightStep
        {
            Settle,
            ApproachRight,
            TeachRight,
            RecoverRight,
            ApproachLeft,
            TeachLeft,
            RecoverLeft,
            Handover
        }
    }
}

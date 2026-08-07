using System;
using CML.Foundation;
using CML.Simulation.Airship;
using CML.Unity.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Reads the normal Unity Input System. Walking is applied immediately by
    /// FirstPersonCharacterMotor; only airship controls and interaction edges
    /// are queued into the deterministic flight state.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class AirshipInputAdapter : MonoBehaviour
    {
        public const string ForwardAction = "Forward";
        public const string StrafeAction = "Strafe";
        public const string SprintAction = "Sprint";
        public const string JumpAction = "Jump";
        public const string VerticalAction = "Vertical";
        public const string LookAction = "Look";
        public const string LandingAction = "Landing";
        public const string TogglePilotAction = "TogglePilot";
        public const string DisembarkAction = "Disembark";

        [SerializeField] private AirshipSimulationBridge simulationBridge;
        [SerializeField] private AirshipPilotStation pilotStation;
        [SerializeField] private AirshipBoardingVolume boardingVolume;
        [SerializeField] private FirstPersonMouseLook mouseLook;
        [SerializeField] private FirstPersonCharacterMotor characterMotor;

        private InputActionMap _controls;
        private AirshipControlSample _latchedSample;
        private bool _hasLatchedSample;
        private bool _uiInputSuppressed;
        private FirstPersonJumpPresentation _jumpPresentation;

        public AirshipSimulationBridge SimulationBridge => simulationBridge;

        public InputActionMap Controls
        {
            get
            {
                EnsureControls();
                return _controls;
            }
        }

        public bool UiInputSuppressed => _uiInputSuppressed;

        /// <summary>
        /// Modal UI suppression uses the same Unity Input System action map,
        /// but projects a neutral sample so flight cannot retain stale thrust.
        /// </summary>
        public void SetUiInputSuppressed(bool suppressed)
        {
            if (_uiInputSuppressed == suppressed)
            {
                return;
            }

            _uiInputSuppressed = suppressed;
            if (_uiInputSuppressed)
            {
                _latchedSample = default;
                _hasLatchedSample = true;
                characterMotor?.ResetVerticalVelocity();
            }

            mouseLook?.SetUiInputSuppressed(suppressed);
        }

        public void Configure(
            AirshipSimulationBridge bridge,
            AirshipPilotStation station)
        {
            if (simulationBridge != null)
            {
                simulationBridge.StateProjected -= HandleStateProjected;
                if (simulationBridge != bridge)
                {
                    simulationBridge.DetachInputAdapter(this);
                }
            }

            simulationBridge = bridge;
            pilotStation = station;
            boardingVolume = simulationBridge != null
                ? simulationBridge.GetComponentInChildren<AirshipBoardingVolume>(
                    true)
                : null;
            if (mouseLook == null)
            {
                mouseLook = GetComponent<FirstPersonMouseLook>();
            }

            if (characterMotor == null)
            {
                characterMotor = GetComponent<FirstPersonCharacterMotor>();
            }

            characterMotor?.Configure(
                GetComponent<CharacterController>(),
                mouseLook != null ? mouseLook.YawPivot : null,
                simulationBridge != null ? simulationBridge.Passenger : null);
            EnsureJumpPresentation();
            EnsureControls();
            if (simulationBridge != null)
            {
                simulationBridge.AttachInputAdapter(this);
                simulationBridge.StateProjected += HandleStateProjected;
                SynchronizeMouseLookWithCommittedState();
            }
        }

        /// <summary>
        /// Applies an Input System binding override without changing AIR commands
        /// or simulation. Composite parts use names such as Positive/Negative;
        /// button bindings use Primary.
        /// </summary>
        public bool ApplyBindingOverride(
            string actionName,
            string bindingName,
            string overridePath)
        {
            if (string.IsNullOrWhiteSpace(actionName)
                || string.IsNullOrWhiteSpace(bindingName)
                || string.IsNullOrWhiteSpace(overridePath))
            {
                return false;
            }

            EnsureControls();
            var action = _controls.FindAction(actionName, false);
            if (action == null)
            {
                return false;
            }

            for (var index = 0; index < action.bindings.Count; index++)
            {
                if (string.Equals(
                    action.bindings[index].name,
                    bindingName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    action.ApplyBindingOverride(index, overridePath);
                    return true;
                }
            }

            return false;
        }

        public AirshipControlSample ReadCurrentControlSample()
        {
            EnsureControls();
            var isPiloting = TryGetCurrentPlayer(out var player)
                && player.IsPiloting;
            mouseLook?.SetPiloting(isPiloting);

            if (_uiInputSuppressed)
            {
                return default;
            }

            var forward = QuantizeAxis(
                _controls.FindAction(ForwardAction, true).ReadValue<float>());
            var strafe = QuantizeAxis(
                _controls.FindAction(StrafeAction, true).ReadValue<float>());
            if (!isPiloting)
            {
                NormalizeWalkingAxes(ref forward, ref strafe);
            }

            var lookDelta = Cursor.lockState == CursorLockMode.Locked
                ? _controls.FindAction(LookAction, true).ReadValue<Vector2>()
                : Vector2.zero;
            if (mouseLook != null)
            {
                lookDelta = mouseLook.FilterMouseDelta(lookDelta);
            }

            if (!isPiloting)
            {
                mouseLook?.ApplyLookDelta(lookDelta);
            }

            var togglePilot = _controls.FindAction(
                TogglePilotAction,
                true).WasPressedThisFrame()
                && !WorldInteractionInput.WasConsumedThisFrame;

            return new AirshipControlSample(
                forward,
                strafe,
                isPiloting
                    ? QuantizeAxis(
                        _controls.FindAction(VerticalAction, true).ReadValue<float>())
                    : 0,
                isPiloting ? QuantizeMouseDelta(lookDelta.x) : 0,
                _controls.FindAction(LandingAction, true).WasPressedThisFrame(),
                togglePilot,
                _controls.FindAction(DisembarkAction, true).WasPressedThisFrame(),
                isPiloting ? QuantizeMouseDelta(lookDelta.y) : 0);
        }

        public bool QueueSampleForNextTick(AirshipControlSample sample)
        {
            if (simulationBridge == null || !simulationBridge.IsInitialized)
            {
                return false;
            }

            _latchedSample = _hasLatchedSample
                ? AirshipControlSample.MergeLatest(_latchedSample, sample)
                : sample;
            _hasLatchedSample = true;
            return true;
        }

        internal void FlushLatchedSample(SimulationTick targetTick)
        {
            if (!_hasLatchedSample
                || simulationBridge == null
                || simulationBridge.Engine.State.Tick.Next() != targetTick)
            {
                return;
            }

            var sample = _latchedSample;
            _latchedSample = sample.WithoutEdges();
            var snapshot = simulationBridge.GetAirshipSnapshot();
            if (!snapshot.TryGetPlayer(
                simulationBridge.PlayerId,
                out var player))
            {
                return;
            }

            if (sample.TogglePilot)
            {
                if (player.IsPiloting)
                {
                    if (pilotStation != null)
                    {
                        pilotStation.StopPiloting();
                    }
                    else
                    {
                        simulationBridge.QueuePilotEnd();
                    }
                }
                else
                {
                    if (pilotStation != null)
                    {
                        pilotStation.TryBeginPiloting(
                            simulationBridge.Passenger);
                    }
                    else
                    {
                        simulationBridge.QueuePilotBegin();
                    }
                }
            }

            if (sample.ToggleBoarding)
            {
                if (player.FrameKind == AirshipPlayerFrameKind.Airship
                    && !player.IsPiloting
                    && boardingVolume != null
                    && boardingVolume.HasDetectedPassenger)
                {
                    simulationBridge.QueueDisembark();
                }
                else if (player.FrameKind == AirshipPlayerFrameKind.World
                    && boardingVolume != null
                    && boardingVolume.HasDetectedPassenger)
                {
                    simulationBridge.QueueBoard();
                }
            }

            if (player.IsPiloting)
            {
                simulationBridge.QueuePilotInput(new AirshipPilotInputState(
                    sample.ForwardPermille,
                    sample.VerticalPermille,
                    sample.YawDeltaPermille,
                    sample.PitchDeltaPermille));
                if (sample.RequestLanding)
                {
                    simulationBridge.QueueLandingFromProbe();
                }
            }
        }

        private void Update()
        {
            var sample = ReadCurrentControlSample();
            if (TryGetCurrentPlayer(out var player) && !player.IsPiloting)
            {
                var sprint = !_uiInputSuppressed
                    && _controls.FindAction(
                        SprintAction,
                        true).IsPressed();
                var jump = !_uiInputSuppressed
                    && _controls.FindAction(
                        JumpAction,
                        true).WasPressedThisFrame();
                characterMotor?.Move(
                    sample.ForwardPermille,
                    sample.StrafePermille,
                    sprint,
                    jump,
                    Time.deltaTime);
            }
            else
            {
                characterMotor?.ResetVerticalVelocity();
            }

            QueueSampleForNextTick(sample);
        }

        private void OnEnable()
        {
            EnsureControls();
            _controls.Enable();
            EnsureJumpPresentation();
        }

        private void OnDisable()
        {
            _controls?.Disable();
        }

        private void OnDestroy()
        {
            if (simulationBridge != null)
            {
                simulationBridge.StateProjected -= HandleStateProjected;
                simulationBridge.DetachInputAdapter(this);
            }

            _controls?.Dispose();
            _controls = null;
        }

        private void EnsureJumpPresentation()
        {
            if (!Application.isPlaying || characterMotor == null)
            {
                return;
            }

            if (_jumpPresentation == null)
            {
                _jumpPresentation = GetComponent<FirstPersonJumpPresentation>();
            }

            if (_jumpPresentation == null)
            {
                _jumpPresentation = gameObject.AddComponent<
                    FirstPersonJumpPresentation>();
            }

            _jumpPresentation.Configure(
                characterMotor,
                mouseLook != null ? mouseLook.YawPivot : null);
        }

        private void HandleStateProjected(SimulationTick _)
        {
            SynchronizeMouseLookWithCommittedState();
        }

        private void SynchronizeMouseLookWithCommittedState()
        {
            var isPiloting = TryGetCurrentPlayer(out var player)
                && player.IsPiloting;
            mouseLook?.SetPiloting(isPiloting);
        }

        private void EnsureControls()
        {
            if (_controls != null)
            {
                return;
            }

            _controls = new InputActionMap("Airship");
            AddAxis(ForwardAction, "<Keyboard>/s", "<Keyboard>/w");
            AddAxis(StrafeAction, "<Keyboard>/a", "<Keyboard>/d");
            AddButton(SprintAction, "<Keyboard>/leftShift");
            AddButton(JumpAction, "<Keyboard>/space");
            AddAxis(VerticalAction, "<Keyboard>/leftShift", "<Keyboard>/space");
            _controls.AddAction(LookAction, InputActionType.PassThrough)
                .AddBinding("<Mouse>/delta")
                .WithName("Primary");
            AddButton(LandingAction, "<Keyboard>/l");
            AddButton(TogglePilotAction, "<Keyboard>/e");
            AddButton(DisembarkAction, "<Keyboard>/x");
            if (isActiveAndEnabled)
            {
                _controls.Enable();
            }
        }

        private void AddAxis(string name, string negativePath, string positivePath)
        {
            _controls.AddAction(name, InputActionType.Value)
                .AddCompositeBinding("1DAxis")
                .With("Negative", negativePath)
                .With("Positive", positivePath);
        }

        private void AddButton(string name, string path)
        {
            _controls.AddAction(name, InputActionType.Button)
                .AddBinding(path)
                .WithName("Primary");
        }

        private static int QuantizeAxis(float value)
        {
            if (value > 0.5f)
            {
                return 1_000;
            }

            return value < -0.5f ? -1_000 : 0;
        }

        private bool TryGetCurrentPlayer(out AirshipPlayerState player)
        {
            player = null;
            return simulationBridge != null
                && simulationBridge.IsInitialized
                && simulationBridge.GetAirshipSnapshot().TryGetPlayer(
                    simulationBridge.PlayerId,
                    out player);
        }

        private static int QuantizeMouseDelta(float value)
        {
            return Mathf.Clamp(Mathf.RoundToInt(value * 80f), -1_000, 1_000);
        }

        private static void NormalizeWalkingAxes(ref int forward, ref int strafe)
        {
            if (forward != 0 && strafe != 0)
            {
                forward = Math.Sign(forward) * 707;
                strafe = Math.Sign(strafe) * 707;
            }
        }
    }

    public readonly struct AirshipControlSample
    {
        public AirshipControlSample(
            int forwardPermille,
            int strafePermille,
            int verticalPermille,
            int yawPermille,
            bool requestLanding,
            bool togglePilot,
            bool toggleBoarding = false,
            int pitchDeltaPermille = 0)
        {
            ForwardPermille = forwardPermille;
            StrafePermille = strafePermille;
            VerticalPermille = verticalPermille;
            YawDeltaPermille = RequireImpulse(yawPermille);
            PitchDeltaPermille = RequireImpulse(pitchDeltaPermille);
            RequestLanding = requestLanding;
            TogglePilot = togglePilot;
            ToggleBoarding = toggleBoarding;
        }

        public int ForwardPermille { get; }

        public int StrafePermille { get; }

        public int VerticalPermille { get; }

        public int YawDeltaPermille { get; }

        public int YawPermille => YawDeltaPermille;

        public int PitchDeltaPermille { get; }

        public bool RequestLanding { get; }

        public bool TogglePilot { get; }

        public bool ToggleBoarding { get; }

        internal AirshipControlSample WithoutEdges()
        {
            return new AirshipControlSample(
                ForwardPermille,
                StrafePermille,
                VerticalPermille,
                0,
                false,
                false,
                false,
                0);
        }

        internal static AirshipControlSample MergeLatest(
            AirshipControlSample previous,
            AirshipControlSample latest)
        {
            return new AirshipControlSample(
                latest.ForwardPermille,
                latest.StrafePermille,
                latest.VerticalPermille,
                MergeImpulse(
                    previous.YawDeltaPermille,
                    latest.YawDeltaPermille),
                previous.RequestLanding || latest.RequestLanding,
                previous.TogglePilot || latest.TogglePilot,
                previous.ToggleBoarding || latest.ToggleBoarding,
                MergeImpulse(
                    previous.PitchDeltaPermille,
                    latest.PitchDeltaPermille));
        }

        private static int MergeImpulse(int previous, int latest)
        {
            return Math.Max(-1_000, Math.Min(1_000, checked(previous + latest)));
        }

        private static int RequireImpulse(int value)
        {
            if (value < -1_000 || value > 1_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A control impulse must be between -1000 and 1000.");
            }

            return value;
        }
    }
}

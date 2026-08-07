using System;
using CML.Foundation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Presentation of the canonical player frame. Boarding, walking and piloting
    /// are reducer outcomes; this component only reparents and interpolates them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipRelativePassenger : MonoBehaviour
    {
        [SerializeField] private Transform bodyRoot;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private AirshipSimulationBridge simulationBridge;

        private AirshipFrame _currentFrame;
        private Transform _worldParent;
        private AirshipPoseState _previousPose;
        private AirshipPoseState _currentPose;
        private AirshipPlayerFrameKind _frameKind;
        private SimulationTick _currentTick;
        private bool _hasState;
        private bool _isPiloting;
        private Transform _viewTransform;

        public event Action<AirshipFrame> Boarded;

        public event Action<AirshipFrame> Disembarked;

        public Transform BodyRoot => bodyRoot != null ? bodyRoot : transform;

        public CharacterController CharacterController => characterController;

        public AirshipFrame CurrentFrame => _currentFrame;

        public bool IsAboard => _frameKind == AirshipPlayerFrameKind.Airship;

        public bool IsPiloting => _isPiloting;

        public AirshipPoseState CurrentCommittedPose => _currentPose;

        public void Configure(
            Transform firstPersonBodyRoot,
            CharacterController controller,
            AirshipSimulationBridge bridge)
        {
            bodyRoot = firstPersonBodyRoot != null ? firstPersonBodyRoot : transform;
            characterController = controller;
            simulationBridge = bridge;
            _worldParent = BodyRoot.parent;
            _viewTransform = null;
        }

        public void CommitState(
            SimulationTick tick,
            AirshipPlayerState state,
            AirshipFrame airshipFrame)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (_hasState && tick < _currentTick)
            {
                throw new InvalidOperationException(
                    "Passenger presentation cannot consume an older commit.");
            }

            var changedFrame = !_hasState || state.FrameKind != _frameKind;
            var changedPiloting = _hasState
                && state.IsPiloting != _isPiloting;
            if ((changedFrame || changedPiloting)
                && state.IsPiloting
                && characterController != null)
            {
                characterController.enabled = false;
            }

            if (changedFrame)
            {
                ApplyFrame(
                    state.FrameKind,
                    airshipFrame,
                    state.IsPiloting);
                _previousPose = state.QuantizedPose;
                _currentPose = state.QuantizedPose;
            }
            else if (changedPiloting)
            {
                ApplyPilotAttachment(state.IsPiloting, airshipFrame);
                // Entering or leaving the controls is a discrete interaction,
                // not locomotion. Project its authoritative pose immediately so
                // the camera cannot glide in from the point where E was pressed.
                _previousPose = state.QuantizedPose;
                _currentPose = state.QuantizedPose;
            }
            else if (tick > _currentTick)
            {
                _previousPose = _currentPose;
                _currentPose = state.QuantizedPose;
            }
            else if (_hasState && state.QuantizedPose != _currentPose)
            {
                throw new InvalidOperationException(
                    "One committed tick cannot project two passenger poses.");
            }

            _frameKind = state.FrameKind;
            _isPiloting = state.IsPiloting;
            _currentTick = tick;
            _hasState = true;
            if (changedFrame || changedPiloting)
            {
                RenderCommittedPose(1f);
                if (!state.IsPiloting && characterController != null)
                {
                    characterController.enabled = true;
                }
            }
        }

        public void Render(float interpolation)
        {
            if (!_hasState || !_isPiloting)
            {
                return;
            }

            RenderCommittedPose(interpolation);
        }

        private void RenderCommittedPose(float interpolation)
        {
            var alpha = Mathf.Clamp01(interpolation);
            var position = Vector3.LerpUnclamped(
                AirshipMotor.ToUnityPosition(_previousPose.Position),
                AirshipMotor.ToUnityPosition(_currentPose.Position),
                alpha);
            var yawDelta = unchecked((short)(
                _currentPose.YawTurn - _previousPose.YawTurn));
            var yaw = (_previousPose.YawTurn + (yawDelta * alpha))
                * (360f / 65_536f);
            SetBodyPose(position, Quaternion.Euler(0f, yaw, 0f));
        }

        private void ApplyFrame(
            AirshipPlayerFrameKind frameKind,
            AirshipFrame airshipFrame,
            bool isPiloting)
        {
            var root = BodyRoot;
            var controllerWasEnabled = characterController != null
                && characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            try
            {
                var previousFrame = _currentFrame;
                if (frameKind == AirshipPlayerFrameKind.Airship)
                {
                    if (airshipFrame == null)
                    {
                        throw new ArgumentNullException(
                            nameof(airshipFrame),
                            "An aboard projection requires its airship frame.");
                    }

                    if (_worldParent == null)
                    {
                        _worldParent = root.parent;
                    }

                    // Never parent the player below the airship. A uniformly
                    // scaled vehicle must scale authored positions and
                    // colliders, but never the CharacterController itself.
                    root.SetParent(_worldParent, worldPositionStays: true);
                    _currentFrame = airshipFrame;
                    airshipFrame.Register(this);
                    if (previousFrame == null)
                    {
                        NotifyObservers(Boarded, airshipFrame);
                    }
                }
                else
                {
                    if (previousFrame != null)
                    {
                        previousFrame.Unregister(this);
                    }

                    root.SetParent(_worldParent, false);
                    _currentFrame = null;
                    if (previousFrame != null)
                    {
                        NotifyObservers(Disembarked, previousFrame);
                    }
                }
            }
            finally
            {
                if (controllerWasEnabled && characterController != null)
                {
                    characterController.enabled = true;
                }
            }
        }

        private void ApplyPilotAttachment(
            bool isPiloting,
            AirshipFrame airshipFrame)
        {
            var root = BodyRoot;
            if (isPiloting)
            {
                if (airshipFrame == null)
                {
                    throw new ArgumentNullException(nameof(airshipFrame));
                }

                if (_worldParent == null)
                {
                    _worldParent = root.parent;
                }

                root.SetParent(_worldParent, worldPositionStays: true);
                _currentFrame = airshipFrame;
                return;
            }

            root.SetParent(_worldParent, worldPositionStays: true);
        }

        private void NotifyObservers(
            Action<AirshipFrame> observers,
            AirshipFrame frame)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<AirshipFrame> observer
                in observers.GetInvocationList())
            {
                try
                {
                    observer(frame);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void SetBodyPose(Vector3 position, Quaternion rotation)
        {
            var root = BodyRoot;
            if (_frameKind == AirshipPlayerFrameKind.Airship
                && _currentFrame != null)
            {
                root.SetPositionAndRotation(
                    _currentFrame.ToWorldPoint(position),
                    _currentFrame.ToWorldRotation(rotation));
                AlignPilotViewToAuthoredAnchor(root);
            }
            else
            {
                root.SetPositionAndRotation(position, rotation);
            }
        }

        private void AlignPilotViewToAuthoredAnchor(Transform root)
        {
            if (!_isPiloting)
            {
                return;
            }

            if (_viewTransform == null)
            {
                var firstPersonCamera =
                    root.GetComponentInChildren<Camera>(true);
                _viewTransform = firstPersonCamera != null
                    ? firstPersonCamera.transform
                    : null;
            }

            var anchor = _currentFrame.PilotCameraAnchor;
            if (_viewTransform == null || anchor == null)
            {
                return;
            }

            // The player hierarchy deliberately stays at world scale 1 while
            // the airship may be uniformly enlarged.  Correct the body root
            // in world space so the actual camera coincides with the authored
            // eye anchor and the cockpit framing remains scale-independent.
            root.position += anchor.position - _viewTransform.position;
        }

        private void OnDestroy()
        {
            if (_currentFrame != null)
            {
                _currentFrame.Unregister(this);
            }
        }
    }
}

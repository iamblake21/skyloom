using System;
using CML.Unity.Airship;
using CML.Unity.Mining;
using CML.Unity.Wood;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.Presentation.Equipment
{
    /// <summary>
    /// Presentation-only motion for the currently equipped first-person item.
    /// The calibrated item pose remains untouched: bob, look inertia and the
    /// swing are composed on a dedicated parent pivot.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonEquipmentMotion : MonoBehaviour
    {
        private const float StrikeVisibilityEpsilon = 0.001f;

        [Header("Locomotion")]
        [SerializeField, Min(0.1f)] private float walkCyclesPerSecond = 1.75f;
        [SerializeField, Min(0.1f)] private float sprintCyclesPerSecond = 2.35f;
        [SerializeField] private Vector3 walkPositionAmplitude =
            new Vector3(0.012f, 0.017f, 0.006f);
        [SerializeField] private Vector3 sprintPositionAmplitude =
            new Vector3(0.014f, 0.020f, 0.007f);
        [SerializeField] private Vector3 walkRotationAmplitude =
            new Vector3(0.7f, 0.9f, 1.2f);
        [SerializeField] private Vector3 sprintRotationAmplitude =
            new Vector3(0.8f, 1.05f, 1.35f);
        [SerializeField, Min(0.1f)] private float locomotionSharpness = 10f;
        [SerializeField, Min(0.1f)] private float sprintBlendSharpness = 7.5f;

        [Header("Jump")]
        [SerializeField] private Vector3 jumpAscentPosition =
            new Vector3(0f, -0.042f, -0.026f);
        [SerializeField] private Vector3 jumpDescentPosition =
            new Vector3(0f, 0.018f, 0.012f);
        [SerializeField] private Vector3 jumpAscentRotation =
            new Vector3(-3.5f, 0f, -0.8f);
        [SerializeField] private Vector3 jumpDescentRotation =
            new Vector3(2.2f, 0f, 0.6f);
        [SerializeField, Min(0.1f)] private float jumpPoseSharpness = 9f;
        [SerializeField, Min(0.01f)] private float jumpLandingDuration = 0.3f;
        [SerializeField] private Vector3 jumpLandingPosition =
            new Vector3(0f, -0.052f, 0.024f);
        [SerializeField] private Vector3 jumpLandingRotation =
            new Vector3(4.5f, 0f, 1.1f);

        [Header("Look inertia")]
        [SerializeField, Min(0f)] private float lookPositionInfluence = 0.0012f;
        [SerializeField, Min(0f)] private float lookRotationInfluence = 0.55f;
        [SerializeField, Min(0.1f)] private float lookSharpness = 16f;
        [SerializeField, Min(0f)] private float maximumLookDelta = 8f;

        [Header("Pickaxe swing")]
        [SerializeField, Min(0.1f)] private float swingDuration = 0.62f;
        [SerializeField, Min(0.1f)] private float missSwingDuration = 0.95f;
        [SerializeField, Min(0.1f)] private float maximumStrikeDistance = 4.5f;
        [SerializeField, Min(0f)] private float minimumContactDistance = 0.55f;
        [SerializeField, Range(0f, 0.15f)]
        private float embeddedSourceTolerance = 0.06f;
        [SerializeField, Range(0f, 0.12f)]
        private float strikeAssistRadius = 0.055f;
        [SerializeField] private LayerMask strikeLayers =
            Physics.DefaultRaycastLayers;
        [SerializeField] private Vector3 windupPosition =
            new Vector3(-0.008f, 0.115f, -0.055f);
        [SerializeField] private Vector3 windupRotation =
            new Vector3(-13f, 0f, 3f);
        [SerializeField] private Vector3 nearContactPosition =
            new Vector3(-0.022f, -0.052f, 0.030f);
        [SerializeField] private Vector3 farContactPosition =
            new Vector3(-0.030f, -0.205f, 0.125f);
        [SerializeField] private Vector3 nearContactRotation =
            new Vector3(16f, 0f, 2f);
        [SerializeField] private Vector3 farContactRotation =
            new Vector3(34f, 0f, 3f);
        [SerializeField] private Vector3 missPosition =
            new Vector3(-0.105f, -0.720f, 0.230f);
        [SerializeField] private Vector3 missRotation =
            new Vector3(48f, -3f, 22f);
        [SerializeField] private Vector3 reboundOffset =
            new Vector3(0.004f, 0.062f, -0.035f);
        [SerializeField] private Vector3 reboundRotationOffset =
            new Vector3(-9f, 0f, -1f);

        [SerializeField] private Transform motionRoot;
        [SerializeField] private Transform swingRoot;
        [SerializeField] private FirstPersonCharacterMotor characterMotor;
        [SerializeField] private FirstPersonEquipmentCollision
            equipmentCollision;
        [SerializeField, Min(0.1f)]
        private float collisionReleaseSharpness = 14f;

        private float _bobPhase;
        private float _locomotionWeight;
        private float _sprintWeight;
        private float _swingElapsed;
        private bool _swinging;
        private bool _impactEmitted;
        private bool _swingHasTarget;
        private RaycastHit _swingTargetHit;
        private readonly RaycastHit[] _strikeHits =
            new RaycastHit[32];
        private readonly RaycastHit[] _strikeAssistHits =
            new RaycastHit[32];
        private Camera _targetingCamera;
        private ManualMiningSourceIdentity _swingMiningSource;
        private FellableTreeIdentity _swingTree;
        private float _swingTargetDistance;
        private bool _hasPreviousCameraRotation;
        private Quaternion _previousCameraRotation;
        private Vector3 _lookPosition;
        private Vector3 _lookRotation;
        private Vector3 _swingRootRestPosition;
        private Quaternion _swingRootRestRotation = Quaternion.identity;
        private float _collisionRetraction;
        private Vector3 _jumpPosition;
        private Vector3 _jumpRotation;
        private float _jumpLandingElapsed = float.PositiveInfinity;
        private float _jumpLandingStrength = 1f;

        public event Action PickaxeImpact;

        /// <summary>
        /// Same approved impact frame, with the immutable target captured when
        /// the swing began. Gameplay must use this payload rather than repeat
        /// a raycast after the viewmodel has already moved.
        /// </summary>
        public event Action<RaycastHit> PickaxeImpactHit;

        /// <summary>
        /// The authored mining source selected under the reticle. This is
        /// separate from the physical RaycastHit because low rocks can be
        /// partly embedded in the Terrain while remaining visibly targetable.
        /// </summary>
        public event Action<ManualMiningSourceIdentity>
            PickaxeMiningSourceHit;

        /// <summary>
        /// The exact tree query surface and immutable hit captured at the start
        /// of the approved single-click swing.
        /// </summary>
        public event Action<FellableTreeIdentity, RaycastHit>
            PickaxeTreeHit;

        public bool IsSwinging => _swinging;

        public float SwingProgress =>
            _swinging
                ? Mathf.Clamp01(_swingElapsed / ActiveSwingDuration)
                : 0f;

        public Transform MotionRoot => motionRoot;

        public void Configure(
            Transform animatedRoot,
            Transform animatedSwingRoot,
            FirstPersonCharacterMotor motor,
            FirstPersonEquipmentCollision collision = null)
        {
            if (motionRoot != animatedRoot
                || swingRoot != animatedSwingRoot)
            {
                ResetMotionImmediate();
                motionRoot = animatedRoot;
                swingRoot = animatedSwingRoot;
                _swingRootRestPosition = swingRoot != null
                    ? swingRoot.localPosition
                    : Vector3.zero;
                _swingRootRestRotation = swingRoot != null
                    ? swingRoot.localRotation
                    : Quaternion.identity;
            }

            characterMotor = motor;
            equipmentCollision = collision;
            _previousCameraRotation = transform.rotation;
            _hasPreviousCameraRotation = true;
        }

        /// <summary>
        /// Starts one complete swing. Further requests are ignored until its
        /// recovery has finished, so holding the mouse button cannot repeat it.
        /// </summary>
        public bool RequestSwing()
        {
            if (_swinging
                || motionRoot == null
                || swingRoot == null
                || !motionRoot.gameObject.activeInHierarchy)
            {
                return false;
            }

            _swinging = true;
            _impactEmitted = false;
            _swingElapsed = 0f;
            ResolveStrikeTarget();
            return true;
        }

        private void LateUpdate()
        {
            ResolveCharacterMotor();
            if (motionRoot == null
                || swingRoot == null
                || !motionRoot.gameObject.activeInHierarchy)
            {
                ResetMotionImmediate();
                return;
            }

            if (Cursor.lockState == CursorLockMode.Locked
                && Mouse.current?.leftButton.wasPressedThisFrame == true)
            {
                RequestSwing();
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            UpdateLookInertia(deltaTime);
            EvaluateLocomotion(
                deltaTime,
                out var locomotionPosition,
                out var locomotionRotation);
            EvaluateJump(
                deltaTime,
                out var jumpPosition,
                out var jumpRotation);
            EvaluateSwing(
                deltaTime,
                out var swingPosition,
                out var swingRotation);

            motionRoot.localPosition =
                locomotionPosition + _lookPosition + jumpPosition;
            motionRoot.localRotation = Quaternion.Euler(
                locomotionRotation + _lookRotation + jumpRotation);
            var desiredSwingPosition =
                _swingRootRestPosition + swingPosition;
            var desiredSwingRotation =
                swingRotation * _swingRootRestRotation;
            ApplyPhysicalClearance(
                desiredSwingPosition,
                desiredSwingRotation,
                deltaTime);
        }

        private void ResolveCharacterMotor()
        {
            if (characterMotor == null)
            {
                characterMotor =
                    GetComponentInParent<FirstPersonCharacterMotor>();
            }

            if (equipmentCollision == null)
            {
                equipmentCollision =
                    GetComponentInChildren<
                        FirstPersonEquipmentCollision>(true);
            }
        }

        private void ApplyPhysicalClearance(
            Vector3 desiredLocalPosition,
            Quaternion desiredLocalRotation,
            float deltaTime)
        {
            var requiredRetraction = equipmentCollision != null
                ? equipmentCollision.FindRequiredRetraction(
                    desiredLocalPosition,
                    desiredLocalRotation,
                    _swinging && _swingHasTarget
                        ? _swingTargetHit.collider
                        : null)
                : 0f;

            // Collision wins immediately, so not even one rendered frame can
            // place the mesh through a wall. Returning to the authored pose is
            // smoothed to avoid a visible snap once the obstacle is gone.
            _collisionRetraction =
                requiredRetraction > _collisionRetraction
                    ? requiredRetraction
                    : Damp(
                        _collisionRetraction,
                        requiredRetraction,
                        collisionReleaseSharpness,
                        deltaTime);

            swingRoot.localPosition =
                desiredLocalPosition
                + Vector3.back * _collisionRetraction;
            swingRoot.localRotation = desiredLocalRotation;
        }

        private void EvaluateLocomotion(
            float deltaTime,
            out Vector3 position,
            out Vector3 rotation)
        {
            var controller = characterMotor != null
                ? characterMotor.CharacterController
                : null;
            var moving = characterMotor != null
                && characterMotor.PlanarInputMagnitude > 0.01f
                && controller != null
                && controller.enabled
                && controller.isGrounded;
            var targetWeight = moving
                ? Mathf.Clamp01(characterMotor.PlanarInputMagnitude)
                : 0f;
            _locomotionWeight = Damp(
                _locomotionWeight,
                targetWeight,
                locomotionSharpness,
                deltaTime);

            var sprintTarget = characterMotor != null
                && characterMotor.IsSprinting
                ? 1f
                : 0f;
            _sprintWeight = Damp(
                _sprintWeight,
                sprintTarget,
                sprintBlendSharpness,
                deltaTime);
            var sprintBlend = _sprintWeight;
            if (moving)
            {
                var frequency = Mathf.Lerp(
                    walkCyclesPerSecond,
                    sprintCyclesPerSecond,
                    sprintBlend);
                _bobPhase = Mathf.Repeat(
                    _bobPhase
                    + deltaTime * frequency * Mathf.PI * 2f,
                    Mathf.PI * 2f);
            }

            var positionAmplitude = Vector3.Lerp(
                walkPositionAmplitude,
                sprintPositionAmplitude,
                sprintBlend);
            var rotationAmplitude = Vector3.Lerp(
                walkRotationAmplitude,
                sprintRotationAmplitude,
                sprintBlend);
            var lateral = Mathf.Sin(_bobPhase);
            var step = Mathf.Abs(Mathf.Sin(_bobPhase));
            var depth = Mathf.Cos(_bobPhase * 2f);

            position = new Vector3(
                lateral * positionAmplitude.x,
                -step * positionAmplitude.y,
                depth * positionAmplitude.z)
                * _locomotionWeight;
            rotation = new Vector3(
                step * rotationAmplitude.x,
                lateral * rotationAmplitude.y,
                -lateral * rotationAmplitude.z)
                * _locomotionWeight;
        }

        private void EvaluateJump(
            float deltaTime,
            out Vector3 position,
            out Vector3 rotation)
        {
            var controller = characterMotor != null
                ? characterMotor.CharacterController
                : null;
            var airborne = characterMotor != null
                && controller != null
                && controller.enabled
                && !characterMotor.IsGrounded;
            var targetPosition = Vector3.zero;
            var targetRotation = Vector3.zero;
            if (airborne)
            {
                var launchSpeed = Mathf.Max(
                    0.1f,
                    characterMotor.JumpLaunchSpeed);
                var descentBlend = Mathf.InverseLerp(
                    launchSpeed,
                    -launchSpeed,
                    characterMotor.VerticalVelocity);
                targetPosition = Vector3.LerpUnclamped(
                    jumpAscentPosition,
                    jumpDescentPosition,
                    descentBlend);
                targetRotation = Vector3.LerpUnclamped(
                    jumpAscentRotation,
                    jumpDescentRotation,
                    descentBlend);
            }

            var blend = ExponentialBlend(jumpPoseSharpness, deltaTime);
            _jumpPosition = Vector3.Lerp(
                _jumpPosition,
                targetPosition,
                blend);
            _jumpRotation = Vector3.Lerp(
                _jumpRotation,
                targetRotation,
                blend);

            if (characterMotor != null && characterMotor.LandedThisFrame)
            {
                _jumpLandingElapsed = 0f;
                _jumpLandingStrength = Mathf.Lerp(
                    0.72f,
                    1.2f,
                    Mathf.InverseLerp(
                        3f,
                        11f,
                        characterMotor.LastLandingSpeed));
            }

            var landingWeight = 0f;
            if (_jumpLandingElapsed < jumpLandingDuration)
            {
                _jumpLandingElapsed += deltaTime;
                var progress = Mathf.Clamp01(
                    _jumpLandingElapsed / jumpLandingDuration);
                landingWeight = progress < 0.3f
                    ? Smooth01(progress / 0.3f)
                    : 1f - Smoother01((progress - 0.3f) / 0.7f);
            }

            position = _jumpPosition
                + jumpLandingPosition * landingWeight * _jumpLandingStrength;
            rotation = _jumpRotation
                + jumpLandingRotation * landingWeight * _jumpLandingStrength;
        }

        private void UpdateLookInertia(float deltaTime)
        {
            var currentRotation = transform.rotation;
            if (!_hasPreviousCameraRotation)
            {
                _previousCameraRotation = currentRotation;
                _hasPreviousCameraRotation = true;
                return;
            }

            var delta = Quaternion.Inverse(
                _previousCameraRotation) * currentRotation;
            _previousCameraRotation = currentRotation;
            var signedEuler = ToSignedEuler(delta.eulerAngles);
            var pitchDelta = Mathf.Clamp(
                signedEuler.x,
                -maximumLookDelta,
                maximumLookDelta);
            var yawDelta = Mathf.Clamp(
                signedEuler.y,
                -maximumLookDelta,
                maximumLookDelta);
            var targetPosition = new Vector3(
                -yawDelta * lookPositionInfluence,
                pitchDelta * lookPositionInfluence * 0.65f,
                0f);
            var targetRotation = new Vector3(
                -pitchDelta * lookRotationInfluence,
                yawDelta * lookRotationInfluence * 0.65f,
                yawDelta * lookRotationInfluence);
            var blend = ExponentialBlend(lookSharpness, deltaTime);
            _lookPosition = Vector3.Lerp(
                _lookPosition,
                targetPosition,
                blend);
            _lookRotation = Vector3.Lerp(
                _lookRotation,
                targetRotation,
                blend);
        }

        private void EvaluateSwing(
            float deltaTime,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!_swinging)
            {
                return;
            }

            var previousProgress = Mathf.Clamp01(
                _swingElapsed / ActiveSwingDuration);
            _swingElapsed += deltaTime;
            var progress = Mathf.Clamp01(
                _swingElapsed / ActiveSwingDuration);
            var impactProgress = _swingHasTarget
                ? 0.48f
                : 1f;
            if (!_impactEmitted
                && _swingHasTarget
                && previousProgress < impactProgress
                && progress >= impactProgress)
            {
                _impactEmitted = true;
                if (_swingMiningSource != null)
                {
                    PickaxeImpactBurst.Play(
                        _swingTargetHit,
                        PickaxeImpactSurface.Stone);
                }
                else if (_swingTree != null)
                {
                    PickaxeImpactBurst.Play(
                        _swingTargetHit,
                        PickaxeImpactSurface.Wood);
                }

                PickaxeImpact?.Invoke();
                PickaxeImpactHit?.Invoke(_swingTargetHit);
                if (_swingMiningSource != null)
                {
                    PickaxeMiningSourceHit?.Invoke(_swingMiningSource);
                }

                if (_swingTree != null)
                {
                    PickaxeTreeHit?.Invoke(
                        _swingTree,
                        _swingTargetHit);
                }
            }

            EvaluateSwingPose(progress, out position, out rotation);
            if (progress >= 1f)
            {
                _swinging = false;
                _swingElapsed = 0f;
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }
        }

        private void EvaluateSwingPose(
            float progress,
            out Vector3 position,
            out Quaternion rotation)
        {
            var windupEnd = _swingHasTarget ? 0.28f : 0.19f;
            if (progress <= windupEnd)
            {
                var phase = Smooth01(progress / windupEnd);
                position = Vector3.LerpUnclamped(
                    Vector3.zero,
                    windupPosition,
                    phase);
                rotation = SlerpEuler(
                    Vector3.zero,
                    windupRotation,
                    phase);
                return;
            }

            ResolveTerminalPose(
                out var terminalPosition,
                out var terminalRotation);
            var descentEnd = _swingHasTarget ? 0.48f : 0.40f;
            if (progress <= descentEnd)
            {
                var phase = CubicIn(
                    (progress - windupEnd)
                    / (descentEnd - windupEnd));
                position = Vector3.LerpUnclamped(
                    windupPosition,
                    terminalPosition,
                    phase);
                rotation = SlerpEuler(
                    windupRotation,
                    terminalRotation,
                    phase);
                return;
            }

            var holdEnd = _swingHasTarget ? 0.53f : 0.426f;
            if (progress <= holdEnd)
            {
                position = terminalPosition;
                rotation = Quaternion.Euler(terminalRotation);
                return;
            }

            var reboundEnd = _swingHasTarget ? 0.68f : 0.485f;
            var reboundPosition = terminalPosition + reboundOffset;
            var reboundRotation =
                terminalRotation + reboundRotationOffset;
            if (progress <= reboundEnd)
            {
                var phase = CubicOut(
                    (progress - holdEnd)
                    / (reboundEnd - holdEnd));
                position = Vector3.LerpUnclamped(
                    terminalPosition,
                    reboundPosition,
                    phase);
                rotation = SlerpEuler(
                    terminalRotation,
                    reboundRotation,
                    phase);
                return;
            }

            var settle = Smoother01(
                (progress - reboundEnd) / (1f - reboundEnd));
            position = Vector3.LerpUnclamped(
                reboundPosition,
                Vector3.zero,
                settle);
            rotation = SlerpEuler(
                reboundRotation,
                Vector3.zero,
                settle);
        }

        private float ActiveSwingDuration =>
            _swingHasTarget ? swingDuration : missSwingDuration;

        private void ResolveStrikeTarget()
        {
            if (_targetingCamera == null)
            {
                _targetingCamera = GetComponent<Camera>();
            }

            var ray = _targetingCamera != null
                ? _targetingCamera.ViewportPointToRay(
                    new Vector3(0.5f, 0.5f, 0f))
                : new Ray(transform.position, transform.forward);
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                _strikeHits,
                maximumStrikeDistance,
                strikeLayers,
                QueryTriggerInteraction.Collide);
            var closestSolidDistance = float.PositiveInfinity;
            var closestAssistSolidDistance = float.PositiveInfinity;
            var closestSourceDistance = float.PositiveInfinity;
            var closestTreeDistance = float.PositiveInfinity;
            RaycastHit closestSolidHit = default;
            RaycastHit closestAssistSolidHit = default;
            RaycastHit closestSourceHit = default;
            RaycastHit closestTreeHit = default;
            ManualMiningSourceIdentity closestSource = null;
            FellableTreeIdentity closestTree = null;

            for (var index = 0; index < hitCount; index++)
            {
                var candidate = _strikeHits[index];
                if (candidate.collider == null)
                {
                    continue;
                }

                if (!candidate.collider.isTrigger &&
                    candidate.distance < closestSolidDistance)
                {
                    closestSolidDistance = candidate.distance;
                    closestSolidHit = candidate;
                }

                var candidateSource =
                    ResolveMiningSource(candidate.collider);
                if (candidateSource != null &&
                    candidate.distance < closestSourceDistance)
                {
                    closestSourceDistance = candidate.distance;
                    closestSourceHit = candidate;
                    closestSource = candidateSource;
                }

                var candidateTree = ResolveTree(candidate.collider);
                if (candidateTree != null
                    && !candidateTree.IsReadyForFelling
                    && candidate.distance < closestTreeDistance)
                {
                    closestTreeDistance = candidate.distance;
                    closestTreeHit = candidate;
                    closestTree = candidateTree;
                }
            }

            // Keep the reticle ray authoritative whenever it reaches a valid
            // resource. If it slips just outside an irregular mesh silhouette,
            // a very small sphere sweep supplies first-person aim tolerance
            // while still querying the real MeshCollider (never a box proxy).
            if (strikeAssistRadius > 0f &&
                closestSource == null &&
                closestTree == null)
            {
                var assistedHitCount = Physics.SphereCastNonAlloc(
                    ray,
                    strikeAssistRadius,
                    _strikeAssistHits,
                    maximumStrikeDistance,
                    strikeLayers,
                    QueryTriggerInteraction.Collide);
                for (var index = 0;
                     index < assistedHitCount;
                     index++)
                {
                    var candidate = _strikeAssistHits[index];
                    if (candidate.collider == null)
                    {
                        continue;
                    }

                    if (!candidate.collider.isTrigger &&
                        candidate.distance < closestAssistSolidDistance)
                    {
                        closestAssistSolidDistance = candidate.distance;
                        closestAssistSolidHit = candidate;
                    }

                    var candidateSource =
                        ResolveMiningSource(candidate.collider);
                    if (candidateSource != null &&
                        candidate.distance < closestSourceDistance)
                    {
                        closestSourceDistance = candidate.distance;
                        closestSourceHit = candidate;
                        closestSource = candidateSource;
                    }

                    var candidateTree = ResolveTree(candidate.collider);
                    if (candidateTree != null &&
                        !candidateTree.IsReadyForFelling &&
                        candidate.distance < closestTreeDistance)
                    {
                        closestTreeDistance = candidate.distance;
                        closestTreeHit = candidate;
                        closestTree = candidateTree;
                    }
                }
            }

            // An assisted resource is valid only if it is also the nearest
            // solid surface reached by the sweep. Otherwise a wall, another
            // rock or any other solid geometry in front of it must occlude it.
            // The direct reticle ray remains an additional conservative
            // blocker in case it hits a surface the offset sweep did not.
            var closestOccludingHit = SelectCloserSolidHit(
                closestSolidHit,
                closestAssistSolidHit);
            var sourceIsVisible =
                IsResourceVisible(
                    closestSource,
                    closestSourceDistance,
                    closestOccludingHit,
                    embeddedSourceTolerance);
            var treeIsVisible =
                IsResourceVisible(
                    closestTree,
                    closestTreeDistance,
                    closestOccludingHit,
                    0f);
            if (sourceIsVisible
                && (!treeIsVisible
                    || closestSourceDistance <= closestTreeDistance))
            {
                _swingHasTarget = true;
                _swingTargetHit = closestSourceHit;
                _swingTargetDistance = closestSourceDistance;
                _swingMiningSource = closestSource;
                _swingTree = null;
                return;
            }

            if (treeIsVisible)
            {
                _swingHasTarget = true;
                _swingTargetHit = closestTreeHit;
                _swingTargetDistance = closestTreeDistance;
                _swingMiningSource = null;
                _swingTree = closestTree;
                return;
            }

            _swingHasTarget =
                !float.IsPositiveInfinity(closestSolidDistance);
            _swingTargetHit = _swingHasTarget
                ? closestSolidHit
                : default;
            _swingTargetDistance = _swingHasTarget
                ? closestSolidDistance
                : maximumStrikeDistance;
            _swingMiningSource = null;
            _swingTree = null;
        }

        private static RaycastHit SelectCloserSolidHit(
            RaycastHit directHit,
            RaycastHit assistedHit)
        {
            if (directHit.collider == null)
            {
                return assistedHit;
            }

            if (assistedHit.collider == null)
            {
                return directHit;
            }

            return directHit.distance <= assistedHit.distance
                ? directHit
                : assistedHit;
        }

        private static bool IsResourceVisible(
            Component resource,
            float resourceDistance,
            RaycastHit closestSolidHit,
            float terrainTolerance)
        {
            if (resource == null)
            {
                return false;
            }

            var solidCollider = closestSolidHit.collider;
            if (solidCollider == null)
            {
                return true;
            }

            var solidTransform = solidCollider.transform;
            if (solidTransform == resource.transform ||
                solidTransform.IsChildOf(resource.transform))
            {
                return true;
            }

            if (resourceDistance <=
                closestSolidHit.distance + StrikeVisibilityEpsilon)
            {
                return true;
            }

            // Decorative rocks can be authored a few centimetres into the
            // Terrain. That is the only occluder for which a tolerance is
            // justified; ordinary solid geometry always blocks the assist.
            return terrainTolerance > 0f &&
                   solidCollider is TerrainCollider &&
                   resourceDistance <=
                   closestSolidHit.distance + terrainTolerance;
        }

        private static ManualMiningSourceIdentity ResolveMiningSource(
            Collider collider) =>
            collider != null
                ? collider.GetComponentInParent<
                    ManualMiningSourceIdentity>()
                : null;

        private static FellableTreeIdentity ResolveTree(
            Collider collider) =>
            collider != null
                ? collider.GetComponentInParent<
                    FellableTreeIdentity>()
                : null;

        private void ResolveTerminalPose(
            out Vector3 position,
            out Vector3 rotation)
        {
            if (!_swingHasTarget)
            {
                position = missPosition;
                rotation = missRotation;
                return;
            }

            var distanceWeight = Mathf.InverseLerp(
                minimumContactDistance,
                maximumStrikeDistance,
                _swingTargetDistance);
            position = Vector3.LerpUnclamped(
                nearContactPosition,
                farContactPosition,
                distanceWeight);
            rotation = Vector3.LerpUnclamped(
                nearContactRotation,
                farContactRotation,
                distanceWeight);
        }

        private void OnDisable()
        {
            ResetMotionImmediate();
        }

        private void OnValidate()
        {
            walkCyclesPerSecond = Mathf.Max(0.1f, walkCyclesPerSecond);
            sprintCyclesPerSecond = Mathf.Max(0.1f, sprintCyclesPerSecond);
            locomotionSharpness = Mathf.Max(0.1f, locomotionSharpness);
            sprintBlendSharpness = Mathf.Max(0.1f, sprintBlendSharpness);
            lookPositionInfluence = Mathf.Max(0f, lookPositionInfluence);
            lookRotationInfluence = Mathf.Max(0f, lookRotationInfluence);
            lookSharpness = Mathf.Max(0.1f, lookSharpness);
            maximumLookDelta = Mathf.Max(0f, maximumLookDelta);
            swingDuration = Mathf.Max(0.1f, swingDuration);
            missSwingDuration = Mathf.Max(0.1f, missSwingDuration);
            maximumStrikeDistance = Mathf.Max(
                0.1f,
                maximumStrikeDistance);
            minimumContactDistance = Mathf.Clamp(
                minimumContactDistance,
                0f,
                maximumStrikeDistance);
            embeddedSourceTolerance = Mathf.Clamp(
                embeddedSourceTolerance,
                0f,
                0.15f);
            strikeAssistRadius = Mathf.Clamp(
                strikeAssistRadius,
                0f,
                0.12f);
            collisionReleaseSharpness =
                Mathf.Max(0.1f, collisionReleaseSharpness);
            jumpPoseSharpness = Mathf.Max(0.1f, jumpPoseSharpness);
            jumpLandingDuration = Mathf.Max(0.01f, jumpLandingDuration);
        }

        private void ResetMotionImmediate()
        {
            _locomotionWeight = 0f;
            _sprintWeight = 0f;
            _swingElapsed = 0f;
            _swinging = false;
            _impactEmitted = false;
            _swingHasTarget = false;
            _swingTargetHit = default;
            _swingMiningSource = null;
            _swingTree = null;
            _swingTargetDistance = 0f;
            _collisionRetraction = 0f;
            _lookPosition = Vector3.zero;
            _lookRotation = Vector3.zero;
            _jumpPosition = Vector3.zero;
            _jumpRotation = Vector3.zero;
            _jumpLandingElapsed = float.PositiveInfinity;
            _jumpLandingStrength = 1f;
            _hasPreviousCameraRotation = false;
            if (motionRoot != null)
            {
                motionRoot.localPosition = Vector3.zero;
                motionRoot.localRotation = Quaternion.identity;
            }

            if (swingRoot != null)
            {
                swingRoot.localPosition = _swingRootRestPosition;
                swingRoot.localRotation = _swingRootRestRotation;
            }
        }

        private static float Damp(
            float current,
            float target,
            float sharpness,
            float deltaTime)
        {
            return Mathf.Lerp(
                current,
                target,
                ExponentialBlend(sharpness, deltaTime));
        }

        private static float ExponentialBlend(
            float sharpness,
            float deltaTime)
        {
            return 1f - Mathf.Exp(-sharpness * deltaTime);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float Smoother01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value
                * (value * (value * 6f - 15f) + 10f);
        }

        private static float CubicOut(float value)
        {
            value = Mathf.Clamp01(value);
            var inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private static float CubicIn(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value;
        }

        private static Quaternion SlerpEuler(
            Vector3 from,
            Vector3 to,
            float value)
        {
            return Quaternion.SlerpUnclamped(
                Quaternion.Euler(from),
                Quaternion.Euler(to),
                Mathf.Clamp01(value));
        }

        private static Vector3 ToSignedEuler(Vector3 euler)
        {
            return new Vector3(
                Mathf.DeltaAngle(0f, euler.x),
                Mathf.DeltaAngle(0f, euler.y),
                Mathf.DeltaAngle(0f, euler.z));
        }
    }
}

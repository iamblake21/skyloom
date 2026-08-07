using System;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Conventional Unity first-person movement. CharacterController.Move and
    /// the scene colliders are the only authorities for player collision.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonCharacterMotor : MonoBehaviour
    {
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform viewYawPivot;
        [SerializeField] private AirshipRelativePassenger passenger;
        [SerializeField, Min(0.1f)] private float movementSpeed = 4f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 1.6f;
        [SerializeField] private float gravity = -25f;
        [SerializeField, Min(0f)] private float groundedPull = 2f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.05f;

        private float _verticalVelocity;
        private bool _hasMovementSample;

        public CharacterController CharacterController =>
            characterController != null
                ? characterController
                : GetComponent<CharacterController>();

        public Transform ViewYawPivot => viewYawPivot;

        public float VerticalVelocity => _verticalVelocity;

        public float JumpLaunchSpeed => Mathf.Sqrt(
            jumpHeight * -2f * Mathf.Min(-0.01f, gravity));

        public bool IsGrounded { get; private set; }

        public bool JumpedThisFrame { get; private set; }

        public bool LandedThisFrame { get; private set; }

        public float LastLandingSpeed { get; private set; }

        public float PlanarInputMagnitude { get; private set; }

        public float PlanarSpeedMetersPerSecond { get; private set; }

        public bool IsSprinting { get; private set; }

        public Collider LastCollision { get; private set; }

        public void Configure(
            CharacterController controller,
            Transform yawPivot,
            AirshipRelativePassenger relativePassenger)
        {
            characterController =
                controller != null ? controller : GetComponent<CharacterController>();
            viewYawPivot = yawPivot;
            passenger = relativePassenger;
            EnsurePhysicalVolume();
        }

        private void Awake()
        {
            EnsurePhysicalVolume();
            var controller = CharacterController;
            IsGrounded = controller != null && controller.isGrounded;
        }

        public CollisionFlags Move(
            int forwardPermille,
            int strafePermille,
            float deltaTime)
        {
            return Move(
                forwardPermille,
                strafePermille,
                sprint: false,
                jumpRequested: false,
                deltaTime);
        }

        public CollisionFlags Move(
            int forwardPermille,
            int strafePermille,
            bool sprint,
            float deltaTime)
        {
            return Move(
                forwardPermille,
                strafePermille,
                sprint,
                jumpRequested: false,
                deltaTime);
        }

        public CollisionFlags Move(
            int forwardPermille,
            int strafePermille,
            bool sprint,
            bool jumpRequested,
            float deltaTime)
        {
            if (deltaTime < 0f
                || float.IsNaN(deltaTime)
                || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            JumpedThisFrame = false;
            LandedThisFrame = false;
            var controller = CharacterController;
            if (controller == null
                || !controller.enabled
                || (passenger != null && passenger.IsPiloting)
                || deltaTime <= 0f)
            {
                ResetPlanarMotion();
                _verticalVelocity = 0f;
                IsGrounded = false;
                return CollisionFlags.None;
            }

            var input = Vector2.ClampMagnitude(
                new Vector2(strafePermille, forwardPermille) / 1000f,
                1f);
            PlanarInputMagnitude = input.magnitude;
            IsSprinting = sprint && PlanarInputMagnitude > 0.001f;
            PlanarSpeedMetersPerSecond =
                PlanarInputMagnitude
                * movementSpeed
                * (IsSprinting ? sprintMultiplier : 1f);
            var yawReference = viewYawPivot != null
                ? viewYawPivot
                : transform;
            var forward = Vector3.ProjectOnPlane(
                yawReference.forward,
                Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(
                yawReference.right,
                Vector3.up).normalized;
            var planarVelocity =
                (right * input.x + forward * input.y)
                * movementSpeed
                * (IsSprinting ? sprintMultiplier : 1f);

            var groundedBeforeMove = controller.isGrounded;
            if (groundedBeforeMove && _verticalVelocity < 0f)
            {
                _verticalVelocity = -groundedPull;
            }

            if (jumpRequested && groundedBeforeMove)
            {
                _verticalVelocity = JumpLaunchSpeed;
                JumpedThisFrame = true;
            }

            _verticalVelocity += gravity * deltaTime;
            var impactVelocity = _verticalVelocity;
            LastCollision = null;
            var flags = controller.Move(
                (planarVelocity + Vector3.up * _verticalVelocity)
                * deltaTime);
            var groundedAfterMove = !JumpedThisFrame
                && (((flags & CollisionFlags.Below) != 0)
                    || controller.isGrounded);
            if (groundedAfterMove && impactVelocity < 0f)
            {
                if (_hasMovementSample && !IsGrounded)
                {
                    LandedThisFrame = true;
                    LastLandingSpeed = -impactVelocity;
                }

                _verticalVelocity = -groundedPull;
            }
            else if ((flags & CollisionFlags.Above) != 0
                     && _verticalVelocity > 0f)
            {
                _verticalVelocity = 0f;
            }

            IsGrounded = groundedAfterMove;
            _hasMovementSample = true;

            return flags;
        }

        public void ResetVerticalVelocity()
        {
            _verticalVelocity = 0f;
            var controller = CharacterController;
            IsGrounded = controller != null
                && controller.enabled
                && controller.isGrounded;
            JumpedThisFrame = false;
            LandedThisFrame = false;
            ResetPlanarMotion();
        }

        private void ResetPlanarMotion()
        {
            PlanarInputMagnitude = 0f;
            PlanarSpeedMetersPerSecond = 0f;
            IsSprinting = false;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            LastCollision = hit != null ? hit.collider : null;
        }

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(0.1f, movementSpeed);
            sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            groundedPull = Mathf.Max(0f, groundedPull);
            jumpHeight = Mathf.Max(0.1f, jumpHeight);
        }

        private void EnsurePhysicalVolume()
        {
            var controller = CharacterController;
            if (controller == null)
            {
                return;
            }

            // CharacterController is the player's real physical capsule.  A
            // scene or prefab override must never turn it into a trigger-like
            // transform that can be projected through the airship hull.
            controller.detectCollisions = true;
            controller.enableOverlapRecovery = true;
        }
    }
}

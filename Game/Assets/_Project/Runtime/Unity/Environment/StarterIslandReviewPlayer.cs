using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.World
{
    /// <summary>
    /// Minimal review-scene input driver. Unity's CharacterController and the
    /// scene Collider components are the only movement/collision authorities.
    /// This class does not maintain proxy geometry, obstacle state or a second
    /// collision representation.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class StarterIslandReviewPlayer : MonoBehaviour
    {
        private const float MaximumTrustedMouseDelta = 250f;

        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform yawPivot;
        [SerializeField] private Transform pitchPivot;
        [SerializeField, Min(0.1f)] private float movementSpeed = 6f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 1.65f;
        [SerializeField] private float gravity = -25f;
        [SerializeField, Min(0f)] private float groundedPull = 2f;
        [SerializeField, Min(0.01f)] private float lookSensitivity = 0.12f;
        [SerializeField, Range(-89f, 0f)] private float minimumPitch = -85f;
        [SerializeField, Range(0f, 89f)] private float maximumPitch = 85f;

        private float _yaw;
        private float _pitch;
        private float _verticalVelocity;
        private bool _ignoreNextMouseDelta;

        public CharacterController CharacterController =>
            characterController != null
                ? characterController
                : GetComponent<CharacterController>();

        public Transform YawPivot => yawPivot;

        public Transform PitchPivot => pitchPivot;

        public Collider LastUnityCollider { get; private set; }

        public CollisionFlags LastCollisionFlags { get; private set; }

        public void Configure(
            CharacterController controller,
            Transform viewYawPivot,
            Transform viewPitchPivot)
        {
            characterController =
                controller != null ? controller : GetComponent<CharacterController>();
            yawPivot = viewYawPivot;
            pitchPivot = viewPitchPivot;
            ReadLookAngles();
            ApplyLook();
        }

        public void SetLookDirection(Vector3 worldDirection)
        {
            var planar = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            if (planar.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            _yaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
            _pitch = 0f;
            ApplyLook();
        }

        public void ApplyLookDelta(Vector2 mouseDelta)
        {
            if (yawPivot == null || pitchPivot == null)
            {
                return;
            }

            if (_ignoreNextMouseDelta)
            {
                _ignoreNextMouseDelta = false;
                return;
            }

            if (Mathf.Abs(mouseDelta.x) > MaximumTrustedMouseDelta ||
                Mathf.Abs(mouseDelta.y) > MaximumTrustedMouseDelta)
            {
                return;
            }

            _yaw = Mathf.Repeat(
                _yaw + mouseDelta.x * lookSensitivity,
                360f);
            _pitch = Mathf.Clamp(
                _pitch - mouseDelta.y * lookSensitivity,
                minimumPitch,
                maximumPitch);
            ApplyLook();
        }

        public CollisionFlags StepMovement(
            Vector2 planarInput,
            bool sprint,
            float deltaTime)
        {
            if (deltaTime < 0f ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            var controller = CharacterController;
            if (controller == null || !controller.enabled || deltaTime <= 0f)
            {
                LastCollisionFlags = CollisionFlags.None;
                _verticalVelocity = 0f;
                return LastCollisionFlags;
            }

            var input = Vector2.ClampMagnitude(planarInput, 1f);
            var reference = yawPivot != null ? yawPivot : transform;
            var forward = Vector3.ProjectOnPlane(
                reference.forward,
                Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(
                reference.right,
                Vector3.up).normalized;
            var speed = movementSpeed * (sprint ? sprintMultiplier : 1f);
            var planarVelocity =
                (right * input.x + forward * input.y) * speed;

            Physics.SyncTransforms();
            if (controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -groundedPull;
            }

            _verticalVelocity += gravity * deltaTime;
            LastUnityCollider = null;
            LastCollisionFlags = controller.Move(
                (planarVelocity + Vector3.up * _verticalVelocity)
                * deltaTime);
            if ((LastCollisionFlags & CollisionFlags.Below) != 0 &&
                _verticalVelocity < 0f)
            {
                _verticalVelocity = -groundedPull;
            }

            return LastCollisionFlags;
        }

        private void Awake()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            ReadLookAngles();
        }

        private void OnEnable()
        {
            LockCursor();
        }

        private void Update()
        {
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (Mouse.current?.leftButton.wasPressedThisFrame == true)
                {
                    LockCursor();
                }
            }
            else
            {
                ApplyLookDelta(Mouse.current?.delta.ReadValue() ?? Vector2.zero);
            }

            var input = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                input.x =
                    (keyboard.dKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed ? 1f : 0f);
                input.y =
                    (keyboard.wKey.isPressed ? 1f : 0f)
                    - (keyboard.sKey.isPressed ? 1f : 0f);
            }

            StepMovement(
                input,
                keyboard?.leftShiftKey.isPressed == true,
                Time.deltaTime);
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                _ignoreNextMouseDelta = true;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            LastUnityCollider = hit != null ? hit.collider : null;
        }

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(0.1f, movementSpeed);
            sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            groundedPull = Mathf.Max(0f, groundedPull);
            lookSensitivity = Mathf.Max(0.01f, lookSensitivity);
            minimumPitch = Mathf.Clamp(minimumPitch, -89f, 0f);
            maximumPitch = Mathf.Clamp(maximumPitch, 0f, 89f);
        }

        private void ReadLookAngles()
        {
            if (yawPivot != null)
            {
                _yaw = NormalizeSignedAngle(yawPivot.localEulerAngles.y);
            }

            if (pitchPivot != null)
            {
                _pitch = Mathf.Clamp(
                    NormalizeSignedAngle(pitchPivot.localEulerAngles.x),
                    minimumPitch,
                    maximumPitch);
            }
        }

        private void ApplyLook()
        {
            if (yawPivot != null)
            {
                yawPivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            if (pitchPivot != null)
            {
                pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _ignoreNextMouseDelta = true;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}

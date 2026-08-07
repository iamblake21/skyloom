using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Presentation-only first-person mouse look. It rotates dedicated view
    /// pivots and never mutates the authoritative player or airship transform.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonMouseLook : MonoBehaviour
    {
        private const float MaximumTrustedMouseDelta = 250f;

        [SerializeField] private Transform yawPivot;
        [SerializeField] private Transform pitchPivot;
        [SerializeField, Min(0.01f)] private float sensitivity = 0.12f;
        [SerializeField, Range(-89f, 0f)] private float minimumPitch = -85f;
        [SerializeField, Range(0f, 89f)] private float maximumPitch = 85f;

        private float _yaw;
        private float _pitch;
        private bool _isPiloting;
        private bool _uiInputSuppressed;
        private bool _ignoreNextMouseDelta;

        public Transform YawPivot => yawPivot;

        public Transform PitchPivot => pitchPivot;

        public ushort RelativeYawTurn => unchecked((ushort)Mathf.RoundToInt(
            Mathf.Repeat(_yaw, 360f) * (65_536f / 360f)));

        public void Configure(Transform viewYawPivot, Transform viewPitchPivot)
        {
            yawPivot = viewYawPivot;
            pitchPivot = viewPitchPivot;
            ReadPivotAngles();
            ApplyPivotRotations();
        }

        public void ApplyLookDelta(Vector2 mouseDelta)
        {
            if (_uiInputSuppressed ||
                _isPiloting ||
                yawPivot == null ||
                pitchPivot == null)
            {
                return;
            }

            _yaw = Mathf.Repeat(_yaw + mouseDelta.x * sensitivity, 360f);
            _pitch = Mathf.Clamp(
                _pitch - mouseDelta.y * sensitivity,
                minimumPitch,
                maximumPitch);
            ApplyPivotRotations();
        }

        public Vector2 FilterMouseDelta(Vector2 mouseDelta)
        {
            if (_uiInputSuppressed)
            {
                return Vector2.zero;
            }

            if (!_ignoreNextMouseDelta)
            {
                return Mathf.Abs(mouseDelta.x) > MaximumTrustedMouseDelta
                        || Mathf.Abs(mouseDelta.y) > MaximumTrustedMouseDelta
                    ? Vector2.zero
                    : mouseDelta;
            }

            _ignoreNextMouseDelta = false;
            return Vector2.zero;
        }

        internal void SuppressNextMouseDelta()
        {
            _ignoreNextMouseDelta = true;
        }

        public void SetPiloting(bool isPiloting)
        {
            if (_isPiloting == isPiloting)
            {
                return;
            }

            _isPiloting = isPiloting;
            if (_isPiloting)
            {
                _yaw = 0f;
                _pitch = 0f;
                if (yawPivot != null && pitchPivot != null)
                {
                    ApplyPivotRotations();
                }
            }
        }

        /// <summary>
        /// Gives a modal UI exclusive ownership of the pointer without
        /// disabling this component or creating a second input system.
        /// </summary>
        public void SetUiInputSuppressed(bool suppressed)
        {
            if (_uiInputSuppressed == suppressed)
            {
                if (suppressed)
                {
                    UnlockCursor();
                }

                return;
            }

            _uiInputSuppressed = suppressed;
            if (_uiInputSuppressed)
            {
                UnlockCursor();
            }
            else if (isActiveAndEnabled)
            {
                LockCursor();
            }
        }

        private void Awake()
        {
            ResolveMissingPivots();
            ReadPivotAngles();
        }

        private void OnEnable()
        {
            if (_uiInputSuppressed)
            {
                UnlockCursor();
            }
            else
            {
                LockCursor();
            }
        }

        private void Update()
        {
            if (_uiInputSuppressed)
            {
                UnlockCursor();
                return;
            }

            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (Mouse.current?.leftButton.wasPressedThisFrame == true)
                {
                    LockCursor();
                }

                return;
            }

            // Mouse delta is consumed exactly once by AirshipInputAdapter. That
            // adapter routes it either to these view pivots or to authoritative
            // pilot intent, preventing doubled camera/vehicle rotation.
        }

        private void OnDisable()
        {
            UnlockCursor();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                SuppressNextMouseDelta();
            }
        }

        private void OnValidate()
        {
            sensitivity = Mathf.Max(0.01f, sensitivity);
            minimumPitch = Mathf.Clamp(minimumPitch, -89f, 0f);
            maximumPitch = Mathf.Clamp(maximumPitch, 0f, 89f);
        }

        private void ResolveMissingPivots()
        {
            if (yawPivot != null && pitchPivot != null)
            {
                return;
            }

            var descendants = GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < descendants.Length; index++)
            {
                var candidate = descendants[index];
                if (yawPivot == null && candidate.name == "AIR_ViewYaw")
                {
                    yawPivot = candidate;
                }
                else if (pitchPivot == null && candidate.name == "AIR_ViewPitch")
                {
                    pitchPivot = candidate;
                }
            }
        }

        private void ReadPivotAngles()
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

        private void ApplyPivotRotations()
        {
            yawPivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SuppressNextMouseDelta();
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

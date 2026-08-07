using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Additive first-person jump pose. The CharacterController owns the real arc;
    /// this component only supplies a short take-off compression and landing settle
    /// without changing collision, mouse pitch or the player's world position.
    /// </summary>
    [DefaultExecutionOrder(275)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonJumpPresentation : MonoBehaviour
    {
        [SerializeField] private FirstPersonCharacterMotor characterMotor;
        [SerializeField] private Transform viewRoot;
        [SerializeField, Min(0.01f)] private float takeoffDuration = 0.14f;
        [SerializeField, Min(0f)] private float takeoffDip = 0.026f;
        [SerializeField, Min(0.01f)] private float landingDuration = 0.32f;
        [SerializeField, Min(0f)] private float landingDip = 0.065f;
        [SerializeField, Min(0f)] private float landingForward = 0.016f;

        private Vector3 _restLocalPosition;
        private bool _hasRestPose;
        private float _takeoffElapsed = float.PositiveInfinity;
        private float _landingElapsed = float.PositiveInfinity;
        private float _landingStrength = 1f;

        public void Configure(
            FirstPersonCharacterMotor motor,
            Transform animatedViewRoot)
        {
            if (_hasRestPose && viewRoot != null && viewRoot != animatedViewRoot)
            {
                viewRoot.localPosition = _restLocalPosition;
            }

            characterMotor = motor;
            viewRoot = animatedViewRoot;
            CaptureRestPose();
        }

        private void Awake()
        {
            ResolveDependencies();
            CaptureRestPose();
        }

        private void LateUpdate()
        {
            ResolveDependencies();
            CaptureRestPose();
            if (characterMotor == null || viewRoot == null || !_hasRestPose)
            {
                return;
            }

            var controller = characterMotor.CharacterController;
            if (controller == null || !controller.enabled)
            {
                ResetPose();
                return;
            }

            if (characterMotor.JumpedThisFrame)
            {
                _takeoffElapsed = 0f;
            }

            if (characterMotor.LandedThisFrame)
            {
                _landingElapsed = 0f;
                _landingStrength = Mathf.Lerp(
                    0.72f,
                    1.2f,
                    Mathf.InverseLerp(
                        3f,
                        11f,
                        characterMotor.LastLandingSpeed));
            }

            var deltaTime = Time.deltaTime;
            var takeoffWeight = 0f;
            if (_takeoffElapsed < takeoffDuration)
            {
                _takeoffElapsed += deltaTime;
                var progress = Mathf.Clamp01(_takeoffElapsed / takeoffDuration);
                takeoffWeight = Mathf.Sin(progress * Mathf.PI);
            }

            var landingWeight = 0f;
            if (_landingElapsed < landingDuration)
            {
                _landingElapsed += deltaTime;
                var progress = Mathf.Clamp01(_landingElapsed / landingDuration);
                landingWeight = progress < 0.28f
                    ? Smooth01(progress / 0.28f)
                    : 1f - Smoother01((progress - 0.28f) / 0.72f);
            }

            var landing = landingWeight * _landingStrength;
            viewRoot.localPosition = _restLocalPosition + new Vector3(
                0f,
                -takeoffDip * takeoffWeight - landingDip * landing,
                landingForward * landing);
        }

        private void ResolveDependencies()
        {
            if (characterMotor == null)
            {
                characterMotor = GetComponent<FirstPersonCharacterMotor>();
            }

            if (viewRoot == null)
            {
                var mouseLook = GetComponent<FirstPersonMouseLook>();
                viewRoot = mouseLook != null ? mouseLook.YawPivot : null;
            }
        }

        private void CaptureRestPose()
        {
            if (_hasRestPose || viewRoot == null)
            {
                return;
            }

            _restLocalPosition = viewRoot.localPosition;
            _hasRestPose = true;
        }

        private void OnDisable()
        {
            ResetPose();
        }

        private void ResetPose()
        {
            _takeoffElapsed = float.PositiveInfinity;
            _landingElapsed = float.PositiveInfinity;
            if (_hasRestPose && viewRoot != null)
            {
                viewRoot.localPosition = _restLocalPosition;
            }
        }

        private void OnValidate()
        {
            takeoffDuration = Mathf.Max(0.01f, takeoffDuration);
            takeoffDip = Mathf.Max(0f, takeoffDip);
            landingDuration = Mathf.Max(0.01f, landingDuration);
            landingDip = Mathf.Max(0f, landingDip);
            landingForward = Mathf.Max(0f, landingForward);
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
    }
}

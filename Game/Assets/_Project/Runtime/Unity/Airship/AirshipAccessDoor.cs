using System;
using CML.Unity.Presentation;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Runtime interaction and presentation for the authored starboard access
    /// door. The exact MeshCollider remains below the animated hinge, so visual
    /// pose and physical passage always share the same transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipAccessDoor : MonoBehaviour,
        IWorldInteractionTarget
    {
        private const float OpenAngleDegrees = 100f;
        private const int ObstructionCapacity = 32;

        [SerializeField] private Transform vehicleRoot;
        [SerializeField] private Transform doorRoot;
        [SerializeField] private AirshipRelativePassenger passenger;
        [SerializeField, Min(0.05f)] private float animationDuration = 0.45f;
        [SerializeField] private bool startsOpen = true;

        private readonly Collider[] _obstructionHits =
            new Collider[ObstructionCapacity];
        private Renderer[] _doorRenderers = Array.Empty<Renderer>();
        private Collider[] _doorColliders = Array.Empty<Collider>();
        private Quaternion _closedRotation;
        private Quaternion _openRotation;
        private float _openProgress;
        private bool _targetOpen;
        private bool _initialized;
        private float _blockedMessageUntil;

        public Transform VehicleRoot => vehicleRoot;

        public Transform DoorRoot => doorRoot;

        public bool IsOpen => _targetOpen;

        public bool IsMoving => _initialized
            && !Mathf.Approximately(_openProgress, _targetOpen ? 1f : 0f);

        public bool IsInteractionAvailable
        {
            get
            {
                EnsureInitialized();
                return enabled
                    && gameObject.activeInHierarchy
                    && (passenger == null || !passenger.IsPiloting);
            }
        }

        public string InteractionPrompt =>
            Time.unscaledTime < _blockedMessageUntil
                ? "Passaggio occupato"
                : _targetOpen
                    ? "Chiudi porta"
                    : "Apri porta";

        public void Configure(
            Transform airshipRoot,
            Transform authoredDoorRoot,
            AirshipRelativePassenger relativePassenger,
            bool initiallyOpen = true)
        {
            if (airshipRoot == null)
            {
                throw new ArgumentNullException(nameof(airshipRoot));
            }

            if (authoredDoorRoot == null)
            {
                throw new ArgumentNullException(nameof(authoredDoorRoot));
            }

            var geometryChanged = vehicleRoot != airshipRoot
                || doorRoot != authoredDoorRoot;
            vehicleRoot = airshipRoot;
            doorRoot = authoredDoorRoot;
            passenger = relativePassenger;
            startsOpen = initiallyOpen;
            if (!_initialized || geometryChanged)
            {
                InitializePose();
            }
        }

        public bool TryInteract()
        {
            EnsureInitialized();
            if (passenger != null && passenger.IsPiloting)
            {
                return false;
            }

            if (_targetOpen)
            {
                if (IsDoorwayObstructed())
                {
                    _blockedMessageUntil = Time.unscaledTime + 1.2f;
                    return true;
                }

                _targetOpen = false;
            }
            else
            {
                _targetOpen = true;
            }

            return true;
        }

        private void Update()
        {
            EnsureInitialized();
            if (!_targetOpen && IsDoorwayObstructed())
            {
                _targetOpen = true;
                _blockedMessageUntil = Time.unscaledTime + 1.2f;
            }

            var target = _targetOpen ? 1f : 0f;
            _openProgress = Mathf.MoveTowards(
                _openProgress,
                target,
                Time.deltaTime / Mathf.Max(0.05f, animationDuration));
            var eased = _openProgress * _openProgress
                * (3f - (2f * _openProgress));
            doorRoot.localRotation = Quaternion.SlerpUnclamped(
                _closedRotation,
                _openRotation,
                eased);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (vehicleRoot == null || doorRoot == null)
            {
                enabled = false;
                return;
            }

            InitializePose();
        }

        private void InitializePose()
        {
            var hingeAxis = doorRoot.InverseTransformDirection(
                vehicleRoot.up).normalized;
            if (startsOpen)
            {
                _openRotation = doorRoot.localRotation;
                _closedRotation = _openRotation
                    * Quaternion.AngleAxis(-OpenAngleDegrees, hingeAxis);
                _openProgress = 1f;
                _targetOpen = true;
            }
            else
            {
                _closedRotation = doorRoot.localRotation;
                _openRotation = _closedRotation
                    * Quaternion.AngleAxis(OpenAngleDegrees, hingeAxis);
                _openProgress = 0f;
                _targetOpen = false;
            }

            _initialized = true;
            _doorRenderers = doorRoot.GetComponentsInChildren<Renderer>(true);
            _doorColliders = doorRoot.GetComponentsInChildren<Collider>(true);
        }

        public bool OwnsInteractionCollider(Collider collider)
        {
            return collider != null
                && doorRoot != null
                && (collider.transform == doorRoot
                    || collider.transform.IsChildOf(doorRoot));
        }

        public bool TryGetInteractionBounds(out Bounds bounds)
        {
            EnsureInitialized();
            return TryGetDoorBounds(out bounds);
        }

        public bool TryGetDoorBounds(out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            for (var index = 0; index < _doorRenderers.Length; index++)
            {
                var renderer = _doorRenderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            for (var index = 0; index < _doorColliders.Length; index++)
            {
                var collider = _doorColliders[index];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private bool IsDoorwayObstructed()
        {
            if (vehicleRoot == null)
            {
                return false;
            }

            // Closed authored doorway: X is shell thickness, Y/Z are the octagonal
            // opening envelope. The root scale is applied because the passenger
            // deliberately remains world-scale 1 while the airship may be enlarged.
            var scale = vehicleRoot.lossyScale;
            var halfExtents = new Vector3(
                0.22f * Mathf.Abs(scale.x),
                1.03f * Mathf.Abs(scale.y),
                0.66f * Mathf.Abs(scale.z));
            var center = vehicleRoot.TransformPoint(
                new Vector3(1.43f, 1.33f, -0.16f));
            var count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _obstructionHits,
                vehicleRoot.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var collider = _obstructionHits[index];
                if (collider == null
                    || !collider.enabled
                    || collider.isTrigger
                    || collider.transform == vehicleRoot
                    || collider.transform.IsChildOf(vehicleRoot))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void OnValidate()
        {
            animationDuration = Mathf.Max(0.05f, animationDuration);
        }
    }
}

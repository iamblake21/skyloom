using CML.Unity.Airship;
using UnityEngine;

namespace CML.Unity.Presentation.Equipment
{
    /// <summary>
    /// Physical shape and collision preflight for the equipped pickaxe.
    ///
    /// The collider is a real, non-trigger Unity collider fitted to the
    /// rendered mesh. The overlap query uses that same fitted volume to find
    /// the minimum camera-relative retraction required before the viewmodel is
    /// drawn, so the mesh cannot pass through world geometry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonEquipmentCollision : MonoBehaviour
    {
        [SerializeField] private LayerMask collisionLayers =
            Physics.DefaultRaycastLayers;
        [SerializeField, Min(0.05f)] private float maximumRetraction = 0.62f;
        [SerializeField, Range(0.001f, 0.03f)]
        private float contactSkin = 0.008f;

        private readonly Collider[] _overlaps = new Collider[24];
        private Transform _swingRoot;
        private Transform _playerRoot;
        private BoxCollider _physicalCollider;
        private Bounds _localBounds;

        public Collider PhysicalCollider => _physicalCollider;

        public void Configure(
            Transform animatedSwingRoot,
            FirstPersonCharacterMotor characterMotor)
        {
            _swingRoot = animatedSwingRoot;
            _playerRoot = characterMotor != null
                ? characterMotor.transform
                : transform.root;
            BuildPhysicalCollider(characterMotor);
        }

        public float FindRequiredRetraction(
            Vector3 desiredSwingLocalPosition,
            Quaternion desiredSwingLocalRotation,
            Collider ignoredStrikeTarget = null)
        {
            if (_swingRoot == null
                || _swingRoot.parent == null
                || _physicalCollider == null
                || !_physicalCollider.enabled
                || IsClear(
                    desiredSwingLocalPosition,
                    desiredSwingLocalRotation,
                    0f,
                    ignoredStrikeTarget))
            {
                return 0f;
            }

            if (!IsClear(
                    desiredSwingLocalPosition,
                    desiredSwingLocalRotation,
                    maximumRetraction,
                    ignoredStrikeTarget))
            {
                return maximumRetraction;
            }

            var blocked = 0f;
            var clear = maximumRetraction;
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var candidate = (blocked + clear) * 0.5f;
                if (IsClear(
                        desiredSwingLocalPosition,
                        desiredSwingLocalRotation,
                        candidate,
                        ignoredStrikeTarget))
                {
                    clear = candidate;
                }
                else
                {
                    blocked = candidate;
                }
            }

            return Mathf.Min(
                maximumRetraction,
                clear + contactSkin);
        }

        private void BuildPhysicalCollider(
            FirstPersonCharacterMotor characterMotor)
        {
            if (!TryCalculateLocalRendererBounds(out _localBounds))
            {
                _localBounds = new Bounds(
                    new Vector3(0f, 0.24f, 0f),
                    new Vector3(0.20f, 1.0f, 0.20f));
            }

            _physicalCollider = GetComponent<BoxCollider>();
            if (_physicalCollider == null)
            {
                _physicalCollider = gameObject.AddComponent<BoxCollider>();
            }

            _physicalCollider.center = _localBounds.center;
            _physicalCollider.size = _localBounds.size;
            _physicalCollider.isTrigger = false;

            var body = GetComponent<Rigidbody>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.None;

            var ignoreRaycastLayer =
                LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0)
            {
                gameObject.layer = ignoreRaycastLayer;
            }

            if (characterMotor == null)
            {
                return;
            }

            var playerColliders =
                characterMotor.GetComponentsInChildren<Collider>(true);
            for (var index = 0;
                 index < playerColliders.Length;
                 index++)
            {
                var playerCollider = playerColliders[index];
                if (playerCollider != null
                    && playerCollider != _physicalCollider)
                {
                    Physics.IgnoreCollision(
                        _physicalCollider,
                        playerCollider,
                        true);
                }
            }
        }

        private bool IsClear(
            Vector3 desiredSwingLocalPosition,
            Quaternion desiredSwingLocalRotation,
            float retraction,
            Collider ignoredStrikeTarget)
        {
            var candidatePosition =
                desiredSwingLocalPosition
                + Vector3.back * retraction;
            var swingMatrix =
                _swingRoot.parent.localToWorldMatrix
                * Matrix4x4.TRS(
                    candidatePosition,
                    desiredSwingLocalRotation,
                    _swingRoot.localScale);
            var equipmentMatrix =
                swingMatrix
                * Matrix4x4.TRS(
                    transform.localPosition,
                    transform.localRotation,
                    transform.localScale);

            var worldCenter =
                equipmentMatrix.MultiplyPoint3x4(_localBounds.center);
            var axisX =
                equipmentMatrix.MultiplyVector(Vector3.right);
            var axisY =
                equipmentMatrix.MultiplyVector(Vector3.up);
            var axisZ =
                equipmentMatrix.MultiplyVector(Vector3.forward);
            var halfExtents = new Vector3(
                _localBounds.extents.x * axisX.magnitude,
                _localBounds.extents.y * axisY.magnitude,
                _localBounds.extents.z * axisZ.magnitude);
            halfExtents += Vector3.one * contactSkin;

            var worldRotation = Quaternion.LookRotation(
                axisZ.normalized,
                axisY.normalized);
            var count = Physics.OverlapBoxNonAlloc(
                worldCenter,
                halfExtents,
                _overlaps,
                worldRotation,
                collisionLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var candidate = _overlaps[index];
                if (candidate == null
                    || candidate == _physicalCollider
                    // A frozen swing target is intentional contact, not an
                    // obstacle that should snap the tool backwards. Only that
                    // exact collider is ignored; walls and every other solid
                    // still participate in clearance.
                    || candidate == ignoredStrikeTarget
                    || candidate.transform.IsChildOf(transform)
                    || (_playerRoot != null
                        && candidate.transform.IsChildOf(_playerRoot)))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private bool TryCalculateLocalRendererBounds(
            out Bounds bounds)
        {
            bounds = default;
            var renderers = GetComponentsInChildren<Renderer>(true);
            var worldToEquipment = transform.worldToLocalMatrix;
            var found = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                var localBounds = renderer.localBounds;
                var toEquipment =
                    worldToEquipment * renderer.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var localCorner = localBounds.center + new Vector3(
                        (corner & 1) == 0
                            ? -localBounds.extents.x
                            : localBounds.extents.x,
                        (corner & 2) == 0
                            ? -localBounds.extents.y
                            : localBounds.extents.y,
                        (corner & 4) == 0
                            ? -localBounds.extents.z
                            : localBounds.extents.z);
                    var point =
                        toEquipment.MultiplyPoint3x4(localCorner);
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return found;
        }

        private void OnValidate()
        {
            maximumRetraction =
                Mathf.Max(0.05f, maximumRetraction);
            contactSkin = Mathf.Clamp(
                contactSkin,
                0.001f,
                0.03f);
        }
    }
}

using UnityEngine;

namespace CML.Unity.Presentation.Machines
{
    /// <summary>
    /// Animates the authored wooden-crate lid without requiring an Animator
    /// Controller on every placed factory object. The source asset deliberately
    /// keeps <c>GEO_CrateLid</c> as a separate mesh, so the runtime only has to
    /// insert one hinge at the rear edge and rotate it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChestLidAnimator : MonoBehaviour
    {
        private const string LidName = "GEO_CrateLid";
        private const string HingeName = "ANM_CrateLidHinge";

        [SerializeField] private Transform lid;
        [SerializeField] private Vector3 hingeLocalPosition =
            new Vector3(0f, 0.54f, 0.35f);
        [SerializeField, Range(1f, 140f)] private float openAngle = 105f;
        [SerializeField, Min(0.01f)] private float transitionSeconds = 0.32f;
        [SerializeField, HideInInspector] private Transform hinge;

        private float _openAmount;
        private float _targetOpenAmount;
        private bool _rigReady;

        public float OpenAmount => _openAmount;

        public bool TargetOpen => _targetOpenAmount > 0.5f;

        public Transform Lid => lid;

        public Transform Hinge => hinge;

        /// <summary>
        /// Placed crates acquire the lightweight presenter only when first used.
        /// This also covers already-authored scenes and old prefab instances.
        /// </summary>
        public static ChestLidAnimator EnsureFor(GameObject chestRoot)
        {
            if (chestRoot == null)
            {
                return null;
            }

            var presenter = chestRoot.GetComponent<ChestLidAnimator>()
                ?? chestRoot.AddComponent<ChestLidAnimator>();
            presenter.EnsureRig();
            return presenter;
        }

        /// <summary>
        /// Changes direction from the current pose, so closing midway through an
        /// opening never snaps. Returns false only when the expected lid mesh is
        /// absent from the object.
        /// </summary>
        public bool SetOpen(bool open, bool immediate = false)
        {
            if (!EnsureRig())
            {
                return false;
            }

            _targetOpenAmount = open ? 1f : 0f;
            if (immediate)
            {
                _openAmount = _targetOpenAmount;
                ApplyPose();
            }

            return true;
        }

        private void Awake()
        {
            EnsureRig();
        }

        private void Update()
        {
            if (!_rigReady
                || Mathf.Approximately(_openAmount, _targetOpenAmount))
            {
                return;
            }

            _openAmount = Mathf.MoveTowards(
                _openAmount,
                _targetOpenAmount,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, transitionSeconds));
            ApplyPose();
        }

        private void OnDisable()
        {
            _targetOpenAmount = 0f;
            _openAmount = 0f;
            ApplyPose();
        }

        private bool EnsureRig()
        {
            if (_rigReady && lid != null && hinge != null)
            {
                return true;
            }

            if (lid == null)
            {
                lid = FindRecursive(transform, LidName);
            }

            if (lid == null)
            {
                return false;
            }

            if (hinge == null)
            {
                hinge = FindRecursive(transform, HingeName);
            }

            if (hinge == null)
            {
                var hingeObject = new GameObject(HingeName);
                hinge = hingeObject.transform;
                hinge.SetParent(transform, false);
            }

            hinge.localPosition = hingeLocalPosition;
            hinge.localScale = Vector3.one;
            if (lid.parent != hinge)
            {
                lid.SetParent(hinge, worldPositionStays: true);
            }

            _rigReady = true;
            ApplyPose();
            return true;
        }

        private void ApplyPose()
        {
            if (!_rigReady || hinge == null)
            {
                return;
            }

            // Smoothstep gives the lid weight at both ends while the normalized
            // amount still reverses continuously if the panel closes midway.
            var eased = _openAmount * _openAmount * (3f - 2f * _openAmount);
            hinge.localRotation = Quaternion.AngleAxis(
                openAngle * eased,
                Vector3.right);
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindRecursive(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnValidate()
        {
            openAngle = Mathf.Clamp(openAngle, 1f, 140f);
            transitionSeconds = Mathf.Max(0.01f, transitionSeconds);
        }
    }
}

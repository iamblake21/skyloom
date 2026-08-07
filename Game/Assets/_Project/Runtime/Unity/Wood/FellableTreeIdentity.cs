using System;
using UnityEngine;

namespace CML.Unity.Wood
{
    /// <summary>
    /// Runtime identity and damage state for one authored tree. The visible
    /// trunk and its exact MeshCollider are deformed together after each
    /// approved impact; no proxy collider or damage overlay is created.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FellableTreeIdentity : MonoBehaviour
    {
        public const string LegacyHitProxyName = "WOOD_TrunkHitProxy";
        public const int HitsRequired = 5;

        [SerializeField] private string stableTreeId = string.Empty;
        [SerializeField, Range(0, HitsRequired)] private int hitCount;
        [SerializeField] private Vector3 finalHitPoint;
        [SerializeField] private Vector3 finalHitNormal = Vector3.forward;
        [SerializeField] private Vector3 finalStrikeDirection =
            Vector3.forward;
        [NonSerialized] private IntactTreeFallAnimator _fallingTree;
        [NonSerialized] private Vector3 _previousHitPoint;
        [NonSerialized] private Vector3 _previousHitNormal = Vector3.forward;
        [NonSerialized] private Vector3 _previousStrikeDirection =
            Vector3.forward;
        [NonSerialized] private MeshCollider _runtimeTrunkCollider;

        public string StableTreeId => stableTreeId;
        public int HitCount => hitCount;
        public bool IsReadyForFelling => hitCount >= HitsRequired;
        public Vector3 FinalHitPoint => finalHitPoint;
        public Vector3 FinalHitNormal => finalHitNormal;
        public Vector3 FinalStrikeDirection => finalStrikeDirection;
        public bool IsFalling => _fallingTree != null;
        internal IntactTreeFallAnimator FallingTree => _fallingTree;

        public void Configure(string treeId)
        {
            if (string.IsNullOrWhiteSpace(treeId))
            {
                throw new ArgumentException(
                    "A fellable tree requires a stable identifier.",
                    nameof(treeId));
            }

            stableTreeId = treeId.Trim();
            hitCount = Mathf.Clamp(hitCount, 0, HitsRequired);
        }

        /// <summary>
        /// Resolves the exact mesh used by both the visible trunk and physics.
        /// A runtime-deformed mesh remains valid because both references are
        /// replaced atomically by TreeChopVoxelCarver.
        /// </summary>
        public MeshCollider ResolveAuthoredTrunkCollider()
        {
            if (!TryResolveTrunkRenderer(out var trunkRenderer))
            {
                return null;
            }

            var legacyProxy = transform.Find(LegacyHitProxyName);
            if (legacyProxy != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(legacyProxy.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(
                        legacyProxy.gameObject);
                }
            }

            MeshCollider collider = null;
            var meshColliders =
                trunkRenderer.GetComponents<MeshCollider>();
            for (var index = 0;
                 index < meshColliders.Length;
                 index++)
            {
                if (!meshColliders[index].convex)
                {
                    collider = meshColliders[index];
                    break;
                }
            }

            var mesh = trunkRenderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (collider == null ||
                mesh == null ||
                collider.sharedMesh != mesh)
            {
                return null;
            }

            collider.isTrigger = false;
            collider.enabled = true;
            return collider;
        }

        /// <summary>
        /// Commits one approved immutable hit and carves the real trunk at
        /// that location. A failed visual/physics update rolls state back.
        /// </summary>
        public bool RegisterImpact(
            RaycastHit hit,
            Vector3 strikeDirection)
        {
            if (IsReadyForFelling ||
                IsFalling ||
                hit.collider == null ||
                hit.collider.GetComponentInParent<
                    FellableTreeIdentity>() != this)
            {
                return false;
            }

            var previousHitPoint = finalHitPoint;
            var previousHitNormal = finalHitNormal;
            var previousStrikeDirection = finalStrikeDirection;
            hitCount++;
            finalHitPoint = hit.point;
            finalHitNormal = hit.normal.sqrMagnitude > 0.0001f
                ? hit.normal.normalized
                : -strikeDirection.normalized;
            finalStrikeDirection =
                strikeDirection.sqrMagnitude > 0.0001f
                    ? strikeDirection.normalized
                    : -finalHitNormal;

            if (!(hit.collider is MeshCollider trunkCollider) ||
                 !TreeChopVoxelCarver.Apply(
                     this,
                     trunkCollider,
                    hit.point,
                    finalHitNormal,
                    hitCount))
            {
                hitCount--;
                finalHitPoint = previousHitPoint;
                finalHitNormal = previousHitNormal;
                finalStrikeDirection = previousStrikeDirection;
                return false;
            }

            _previousHitPoint = previousHitPoint;
            _previousHitNormal = previousHitNormal;
            _previousStrikeDirection = previousStrikeDirection;
            _runtimeTrunkCollider = trunkCollider;

            return true;
        }

        public bool BeginPhysicalFall()
        {
            if (!IsReadyForFelling || IsFalling)
            {
                return false;
            }

            if (!TreeFellingFactory.TryCreatePhysicalFall(
                    this,
                    out var fallingTree))
            {
                return false;
            }

            _fallingTree = fallingTree;
            return true;
        }

        public int DetermineDeterministicYield()
        {
            unchecked
            {
                uint hash = 2166136261;
                var value = stableTreeId ?? string.Empty;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619;
                }

                return 3 + (int)(hash % 3u);
            }
        }

        public void RollBackFinalImpact()
        {
            if (hitCount != HitsRequired || IsFalling)
            {
                return;
            }

            var trunkCollider = _runtimeTrunkCollider != null
                ? _runtimeTrunkCollider
                : ResolveAuthoredTrunkCollider();
            if (trunkCollider == null ||
                !TreeChopVoxelCarver.Undo(
                    this,
                    trunkCollider,
                    HitsRequired - 1))
            {
                return;
            }

            hitCount = HitsRequired - 1;
            finalHitPoint = _previousHitPoint;
            finalHitNormal = _previousHitNormal;
            finalStrikeDirection = _previousStrikeDirection;
        }

        internal bool TryResolveTrunkRenderer(
            out MeshRenderer trunkRenderer)
        {
            var renderers =
                GetComponentsInChildren<MeshRenderer>(true);
            MeshRenderer legacyBranchesRenderer = null;
            for (var index = 0; index < renderers.Length; index++)
            {
                var candidate = renderers[index];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.name.IndexOf(
                        "Trunk",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    trunkRenderer = candidate;
                    return true;
                }

                if (legacyBranchesRenderer == null &&
                    candidate.name.IndexOf(
                        "Branches",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    legacyBranchesRenderer = candidate;
                }
            }

            if (legacyBranchesRenderer != null)
            {
                trunkRenderer = legacyBranchesRenderer;
                return true;
            }

            trunkRenderer = null;
            return false;
        }
    }
}

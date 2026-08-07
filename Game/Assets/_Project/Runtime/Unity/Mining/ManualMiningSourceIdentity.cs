using System;
using System.Collections.Generic;
using UnityEngine;

namespace CML.Unity.Mining
{
    /// <summary>
    /// Authoring identity for a source that can later be handled by the
    /// authoritative manual-mining command. This component deliberately owns
    /// no reward, durability or destruction logic: MINE-003 only establishes
    /// the readable world targets, while MINE-004 will consume this identity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManualMiningSourceIdentity : MonoBehaviour
    {
        public const string InfiniteDepositSurfaceName =
            "MINE_InfiniteExtractionSurface";
        public const string MiningHitProxyName =
            "MINE_HitProxy";

        private const string AuthoredRockPrefix = "DEC_Rock_";
        private const string AuthoredPathPebblePrefix =
            "DEC_PathPebble_";

        /// <summary>
        /// Catalog key of the raw item this deposit yields, for example
        /// <c>item.raw_iron</c>. Only meaningful on a deposit surface.
        ///
        /// It exists so the mechanical drill can decide what to extract from
        /// where it stands instead of the answer being hardcoded. Deliberately a
        /// catalog key and not an enum: adding an ore then means adding a
        /// catalog entry and an extraction recipe, with no code to change.
        /// </summary>
        public const string DefaultDepositOreItemKey = "item.raw_iron";

        [SerializeField] private ManualMiningSourceKind sourceKind;
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField] private string depositOreItemKey = string.Empty;

        public ManualMiningSourceKind SourceKind => sourceKind;

        public string SourceId => sourceId;

        /// <summary>
        /// Falls back to iron when blank so the deposits already painted into
        /// Starter Island keep working: they were authored before any ore other
        /// than iron existed, and rewriting the scene to add a field it already
        /// implies would be a change with no content behind it.
        /// </summary>
        public string DepositOreItemKey =>
            string.IsNullOrWhiteSpace(depositOreItemKey)
                ? DefaultDepositOreItemKey
                : depositOreItemKey.Trim();

        public bool IsMachineExtractable =>
            sourceKind == ManualMiningSourceKind.IronDepositSurface;

        public void ConfigureDepositOre(string itemKey)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
            {
                throw new ArgumentException(
                    "A deposit requires the catalog key of the item it yields.",
                    nameof(itemKey));
            }

            depositOreItemKey = itemKey.Trim();
        }

        public bool IsFinite =>
            sourceKind != ManualMiningSourceKind.IronDepositSurface;

        public void Configure(
            ManualMiningSourceKind kind,
            string stableSourceId)
        {
            if (string.IsNullOrWhiteSpace(stableSourceId))
            {
                throw new ArgumentException(
                    "A manual-mining source requires a stable identifier.",
                    nameof(stableSourceId));
            }

            sourceKind = kind;
            sourceId = stableSourceId.Trim();
        }

        /// <summary>
        /// Prevents another physics impact in the frame between the canonical
        /// commit and Unity destroying the finite source object.
        /// </summary>
        public void DisableForCommittedExtraction()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        /// <summary>
        /// Gives every finite source collision that follows its rendered mesh
        /// exactly. Older scenes used a padded BoxCollider child as an aiming
        /// proxy; it could both report empty space and hide valid edge hits.
        /// The legacy proxy is removed here so runtime bootstrap cannot bring
        /// that approximation back after an editor-side repair.
        /// </summary>
        public MeshCollider EnsureMiningMeshColliders()
        {
            return EnsureMiningMeshColliders(
                AddRuntimeMeshCollider,
                DestroyRuntimeObject,
                _ => { });
        }

        /// <summary>
        /// Uses the same reconciliation policy as the runtime entry point,
        /// while allowing editor tools to supply Undo-aware mutations without
        /// taking a dependency on UnityEditor from the runtime assembly.
        /// </summary>
        public MeshCollider EnsureMiningMeshColliders(
            Func<GameObject, MeshCollider> addMeshCollider,
            Action<UnityEngine.Object> destroyObject,
            Action<UnityEngine.Object> recordObject)
        {
            if (!IsFinite)
            {
                return null;
            }

            if (addMeshCollider == null)
            {
                throw new ArgumentNullException(nameof(addMeshCollider));
            }

            if (destroyObject == null)
            {
                throw new ArgumentNullException(nameof(destroyObject));
            }

            if (recordObject == null)
            {
                throw new ArgumentNullException(nameof(recordObject));
            }

            RemoveLegacyMiningHitProxy(destroyObject, recordObject);

            MeshCollider firstCollider = null;
            var ownedColliderOwners = new HashSet<GameObject>
            {
                gameObject
            };
            var canonicalColliders =
                new Dictionary<GameObject, MeshCollider>();
            var filters = GetComponentsInChildren<MeshFilter>(true);
            for (var filterIndex = 0;
                 filterIndex < filters.Length;
                 filterIndex++)
            {
                var filter = filters[filterIndex];
                if (!IsOwnedMiningMeshFilter(filter))
                {
                    continue;
                }

                AddOwnedColliderAncestors(
                    filter.transform,
                    ownedColliderOwners);
                MeshCollider collider = null;
                var candidates =
                    filter.GetComponents<MeshCollider>();
                for (var colliderIndex = 0;
                     colliderIndex < candidates.Length;
                     colliderIndex++)
                {
                    if (candidates[colliderIndex].sharedMesh ==
                        filter.sharedMesh)
                    {
                        collider = candidates[colliderIndex];
                        break;
                    }
                }

                // Reuse one existing collider when possible. This keeps the
                // operation idempotent even if an older repair left a collider
                // with the wrong mesh assigned.
                if (collider == null && candidates.Length > 0)
                {
                    collider = candidates[0];
                }

                if (collider == null)
                {
                    collider = addMeshCollider(filter.gameObject);
                    if (collider == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not add a MeshCollider to " +
                            $"'{filter.gameObject.name}'.");
                    }
                }

                if (collider.sharedMesh != filter.sharedMesh ||
                    collider.convex ||
                    collider.isTrigger ||
                    !collider.enabled)
                {
                    recordObject(collider);
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                    collider.isTrigger = false;
                    collider.enabled = true;
                }

                canonicalColliders[filter.gameObject] = collider;
                if (firstCollider == null)
                {
                    firstCollider = collider;
                }
            }

            // Only the identity root, the visible mesh objects and the
            // structural ancestors connecting them belong to the rock's
            // collision representation. Collider-only siblings remain intact
            // because they may be interaction volumes or semantic children.
            foreach (var owner in ownedColliderOwners)
            {
                canonicalColliders.TryGetValue(
                    owner,
                    out var colliderToKeep);
                RemoveOwnedColliderComponents(
                    owner,
                    colliderToKeep,
                    destroyObject,
                    recordObject);
            }

            return firstCollider;
        }

        /// <summary>
        /// Restores the canonical stable identifier for the environmental
        /// rocks that were painted into Starter Island before mining authoring
        /// was introduced. A blank id makes an otherwise valid physics hit get
        /// rejected by the gameplay controller.
        /// </summary>
        public bool TryConfigureFromAuthoredRockName()
        {
            if (!TryGetAuthoredEnvironmentalStoneSourceId(
                    name,
                    out var stableSourceId))
            {
                return false;
            }

            Configure(
                ManualMiningSourceKind.EnvironmentalStone,
                stableSourceId);
            return true;
        }

        public static bool TryGetAuthoredEnvironmentalStoneSourceId(
            string objectName,
            out string stableSourceId)
        {
            if (TryBuildSourceId(
                    objectName,
                    AuthoredRockPrefix,
                    "starter-island.environment-rock.",
                    out stableSourceId))
            {
                return true;
            }

            return TryBuildSourceId(
                objectName,
                AuthoredPathPebblePrefix,
                "starter-island.path-pebble.",
                out stableSourceId);
        }

        /// <summary>
        /// Returns the visible world-space footprint used by the mining aim
        /// resolver. Unlike a physics query, this remains reliable when a
        /// deliberately embedded decorative rock loses the collision race
        /// against the Terrain underneath it.
        /// </summary>
        public bool TryGetVisibleWorldBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            var found = false;
            bounds = default;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        /// <summary>
        /// Creates the bounded Unity trigger representing the mineable ground
        /// of one deposit. It is safe both during scene authoring and at runtime:
        /// the second call reuses the same collider instead of creating another
        /// collision system.
        /// </summary>
        public static ManualMiningSourceIdentity
            EnsureInfiniteDepositSurface(
                GameObject deposit,
                string stableSourceId)
        {
            if (deposit == null)
            {
                throw new ArgumentNullException(nameof(deposit));
            }

            var existing = deposit.transform.Find(
                InfiniteDepositSurfaceName);
            GameObject surface;
            if (existing != null)
            {
                surface = existing.gameObject;
            }
            else
            {
                if (!TryGetLocalRendererBounds(
                        deposit.transform,
                        out var localBounds))
                {
                    throw new InvalidOperationException(
                        "The mineral deposit has no renderable footprint.");
                }

                surface = new GameObject(InfiniteDepositSurfaceName);
                surface.layer = deposit.layer;
                surface.transform.SetParent(deposit.transform, false);
                surface.transform.localPosition = new Vector3(
                    localBounds.center.x,
                    localBounds.min.y + 0.04f,
                    localBounds.center.z);

                var collider = surface.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = new Vector3(
                    Mathf.Max(0.6f, localBounds.size.x * 0.82f),
                    0.08f,
                    Mathf.Max(0.6f, localBounds.size.z * 0.82f));
                collider.isTrigger = true;
            }

            // Prefab assets and older scene instances can both carry a
            // surface created by an earlier authoring pass. Reconcile its
            // footprint every time instead of trusting a stale or disabled
            // collider; this keeps drill placement aligned with the visible
            // deposit without requiring a scene regeneration.
            if (TryGetLocalRendererBounds(
                    deposit.transform,
                    out var refreshedBounds))
            {
                surface.layer = deposit.layer;
                surface.SetActive(true);
                surface.transform.localPosition = new Vector3(
                    refreshedBounds.center.x,
                    refreshedBounds.min.y + 0.04f,
                    refreshedBounds.center.z);

                var refreshedCollider =
                    surface.GetComponent<BoxCollider>();
                if (refreshedCollider == null)
                {
                    refreshedCollider = surface.AddComponent<BoxCollider>();
                }

                refreshedCollider.center = Vector3.zero;
                refreshedCollider.size = new Vector3(
                    Mathf.Max(0.6f, refreshedBounds.size.x * 0.82f),
                    0.08f,
                    Mathf.Max(0.6f, refreshedBounds.size.z * 0.82f));
                refreshedCollider.isTrigger = true;
                refreshedCollider.enabled = true;
            }

            var identity =
                surface.GetComponent<ManualMiningSourceIdentity>();
            if (identity == null)
            {
                identity =
                    surface.AddComponent<ManualMiningSourceIdentity>();
            }

            identity.Configure(
                ManualMiningSourceKind.IronDepositSurface,
                stableSourceId);
            return identity;
        }

        private static bool TryGetLocalRendererBounds(
            Transform root,
            out Bounds bounds)
        {
            var renderers =
                root.GetComponentsInChildren<Renderer>(true);
            var found = false;
            bounds = default;

            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var local = renderer.localBounds;
                var minimum = local.min;
                var maximum = local.max;
                for (var cornerIndex = 0;
                     cornerIndex < 8;
                     cornerIndex++)
                {
                    var rendererLocalPoint = new Vector3(
                        (cornerIndex & 1) == 0 ? minimum.x : maximum.x,
                        (cornerIndex & 2) == 0 ? minimum.y : maximum.y,
                        (cornerIndex & 4) == 0 ? minimum.z : maximum.z);
                    var rootLocalPoint = root.InverseTransformPoint(
                        renderer.transform.TransformPoint(
                            rendererLocalPoint));
                    if (!found)
                    {
                        bounds = new Bounds(rootLocalPoint, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rootLocalPoint);
                    }
                }
            }

            return found;
        }

        private bool IsOwnedMiningMeshFilter(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            // A nested mining source owns its own geometry. Likewise, a child
            // rigidbody is a separate physics entity; preserving its colliders
            // is safer than silently folding it into this static rock.
            if (filter.GetComponentInParent<
                    ManualMiningSourceIdentity>() != this)
            {
                return false;
            }

            var body = filter.GetComponentInParent<Rigidbody>();
            return body == null || body.transform == transform;
        }

        private void AddOwnedColliderAncestors(
            Transform meshTransform,
            ISet<GameObject> owners)
        {
            for (var current = meshTransform;
                 current != null;
                 current = current.parent)
            {
                owners.Add(current.gameObject);
                if (current == transform)
                {
                    return;
                }
            }
        }

        private static void RemoveOwnedColliderComponents(
            GameObject owner,
            MeshCollider colliderToKeep,
            Action<UnityEngine.Object> destroyObject,
            Action<UnityEngine.Object> recordObject)
        {
            var meshColliders = owner.GetComponents<MeshCollider>();
            for (var index = 0; index < meshColliders.Length; index++)
            {
                var collider = meshColliders[index];
                if (collider != null && collider != colliderToKeep)
                {
                    RemoveCollider(
                        collider,
                        destroyObject,
                        recordObject);
                }
            }

            var boxColliders = owner.GetComponents<BoxCollider>();
            for (var index = 0; index < boxColliders.Length; index++)
            {
                RemoveCollider(
                    boxColliders[index],
                    destroyObject,
                    recordObject);
            }
        }

        private static void RemoveCollider(
            Collider collider,
            Action<UnityEngine.Object> destroyObject,
            Action<UnityEngine.Object> recordObject)
        {
            if (collider == null)
            {
                return;
            }

            recordObject(collider);
            collider.enabled = false;
            destroyObject(collider);
        }

        private void RemoveLegacyMiningHitProxy(
            Action<UnityEngine.Object> destroyObject,
            Action<UnityEngine.Object> recordObject)
        {
            for (var childIndex = transform.childCount - 1;
                 childIndex >= 0;
                 childIndex--)
            {
                var proxy = transform.GetChild(childIndex);
                if (!string.Equals(
                        proxy.name,
                        MiningHitProxyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var colliders = proxy.GetComponents<Collider>();
                for (var index = 0; index < colliders.Length; index++)
                {
                    if (colliders[index] == null)
                    {
                        continue;
                    }

                    recordObject(colliders[index]);
                    colliders[index].enabled = false;
                }

                recordObject(proxy.gameObject);
                destroyObject(proxy.gameObject);
            }
        }

        private static MeshCollider AddRuntimeMeshCollider(
            GameObject owner) => owner.AddComponent<MeshCollider>();

        private static void DestroyRuntimeObject(
            UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static bool TryBuildSourceId(
            string objectName,
            string objectPrefix,
            string sourcePrefix,
            out string stableSourceId)
        {
            if (!string.IsNullOrEmpty(objectName) &&
                objectName.StartsWith(
                    objectPrefix,
                    StringComparison.Ordinal) &&
                objectName.Length > objectPrefix.Length)
            {
                stableSourceId = sourcePrefix +
                    objectName.Substring(objectPrefix.Length);
                return true;
            }

            stableSourceId = string.Empty;
            return false;
        }
    }

    public enum ManualMiningSourceKind
    {
        EnvironmentalStone = 0,
        IronOreRock = 1,
        IronDepositSurface = 2
    }
}

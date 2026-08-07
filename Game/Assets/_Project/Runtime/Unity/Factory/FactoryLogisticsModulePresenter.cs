using System;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Machines;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Read-only view of the single authoritative slot carried by a Funnel or one
    /// one-metre BeltModule. The presenter never advances cargo locally.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class FactoryLogisticsModulePresenter : MonoBehaviour
    {
        [SerializeField] private FactoryStableIdField nodeId =
            new FactoryStableIdField();
        [SerializeField] private MachineNodeKind nodeKind;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform beltPathStart;
        [SerializeField] private Transform beltPathEnd;
        [SerializeField] private Transform funnelCargoMarker;
        [SerializeField] private GameObject ironIngotPrefab;
        [SerializeField] private GameObject ironPlatePrefab;

        private readonly System.Collections.Generic.Dictionary<StableId, GameObject>
            _carriedPrefabs =
                new System.Collections.Generic.Dictionary<StableId, GameObject>();

        private SimulationEngine _engine;
        private StableId _presentedItemId;
        private GameObject _visual;
        [NonSerialized] private bool _hasBeltSupportHeight;
        [NonSerialized] private float _beltSupportLocalHeight;
        [NonSerialized] private bool _usesExplicitFunnelRelease;
        [NonSerialized] private FactoryLogisticsModulePresenter
            _connectedBeltPresenter;
        [NonSerialized] private CML.Unity.Presentation.Logistics.BeltVisuals
            _beltVisuals;

        public StableId NodeId => nodeId.GetValueOrNone();

        public MachineNodeKind NodeKind => nodeKind;

        public bool IsAttached => _engine != null && !NodeId.IsNone;

        public bool HasCargoVisual => _visual != null && _visual.activeSelf;

        public void Configure(
            SimulationEngine engine,
            StableId authoritativeNodeId,
            MachineNodeKind authoritativeKind,
            GameObject ingotPrefab,
            GameObject platePrefab)
        {
            ConfigureAuthoring(
                authoritativeNodeId,
                authoritativeKind,
                ingotPrefab,
                platePrefab);
            AttachSimulation(engine);
        }

        public void ConfigureAuthoring(
            StableId authoritativeNodeId,
            MachineNodeKind authoritativeKind,
            GameObject ingotPrefab,
            GameObject platePrefab)
        {
            if (authoritativeNodeId.IsNone)
            {
                throw new ArgumentException(
                    "A logistics presenter needs an authoritative node id.",
                    nameof(authoritativeNodeId));
            }

            if (authoritativeKind != MachineNodeKind.Funnel
                && authoritativeKind != MachineNodeKind.BeltModule)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authoritativeKind),
                    "Only Funnel and BeltModule have this presenter.");
            }

            nodeId.Set(authoritativeNodeId);
            nodeKind = authoritativeKind;
            ironIngotPrefab = ingotPrefab;
            ironPlatePrefab = platePrefab;
            visualRoot = visualRoot != null ? visualRoot : transform;
            ResolvePathMarkers();
        }

        public void AttachSimulation(SimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            RefreshImmediate();
        }

        public void RefreshImmediate()
        {
            if (!IsAttached
                || !_engine.State.GetMachineSnapshot().TryGetNode(
                    NodeId,
                    out var node)
                || node.Kind != nodeKind
                || node.Input.SlotCount == 0)
            {
                HideVisual();
                return;
            }

            if (nodeKind == MachineNodeKind.BeltModule)
            {
                _beltVisuals = _beltVisuals != null
                    ? _beltVisuals
                    : GetComponent<
                        CML.Unity.Presentation.Logistics.BeltVisuals>();
                if (_beltVisuals != null)
                {
                    _beltVisuals.SetTravelDirection(
                        node.BeltTravelDirection);
                    _beltVisuals.SetPowerRatio(
                        node.BeltLineStatus == BeltLineStatus.Operational
                            ? 1f
                            : 0f);
                }
            }

            var slot = node.Input.GetSlot(0);
            if (slot.IsEmpty)
            {
                HideVisual();
                return;
            }

            EnsureVisual(slot.ItemId);
            if (_visual == null)
            {
                return;
            }

            if (nodeKind == MachineNodeKind.BeltModule)
            {
                var normalized = Mathf.Clamp01(
                    node.TransportProgressMillimetres / 1_000f);
                var start = beltPathStart != null
                    ? beltPathStart.position
                    : transform.TransformPoint(new Vector3(0f, 0f, -0.5f));
                var end = beltPathEnd != null
                    ? beltPathEnd.position
                    : transform.TransformPoint(new Vector3(0f, 0f, 0.5f));
                if (node.BeltTravelDirection == BeltTravelDirection.Reverse)
                {
                    var originalStart = start;
                    start = end;
                    end = originalStart;
                }

                var supportHeight = ResolveBeltSupportWorldHeight();
                start.y = supportHeight;
                end.y = supportHeight;
                PlaceOnBeltSurface(start, end, normalized);
            }
            else
            {
                var marker = funnelCargoMarker != null
                    ? funnelCargoMarker
                    : transform;
                PlaceAtFunnelRelease(marker);
            }

            _visual.SetActive(true);
        }

        private void Awake()
        {
            visualRoot = visualRoot != null ? visualRoot : transform;
            ResolvePathMarkers();
        }

        private void LateUpdate()
        {
            RefreshImmediate();
        }

        private void OnDisable()
        {
            if (_visual != null)
            {
                _visual.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_visual != null)
            {
                Destroy(_visual);
            }
        }

        private void EnsureVisual(StableId itemId)
        {
            var prefab = ResolvePrefab(itemId);
            if (_visual != null
                && _presentedItemId == itemId
                && prefab != null)
            {
                return;
            }

            if (_visual != null)
            {
                Destroy(_visual);
                _visual = null;
            }

            _presentedItemId = itemId;
            if (prefab == null)
            {
                return;
            }

            _visual = Instantiate(
                prefab,
                visualRoot != null ? visualRoot : transform);
            _visual.name = $"{prefab.name}_ModuleVisual";
            MakePresentationOnly(_visual);
        }

        private void ResolvePathMarkers()
        {
            var anchor = GetComponent<FactoryNodeAnchor>();
            if (nodeKind == MachineNodeKind.BeltModule)
            {
                beltPathStart = beltPathStart != null
                    ? beltPathStart
                    : anchor != null
                        ? anchor.InputSocket
                        : FindNamed(transform, "PORT_ModuleInput");
                beltPathEnd = beltPathEnd != null
                    ? beltPathEnd
                    : anchor != null
                        ? anchor.OutputSocket
                        : FindNamed(transform, "PORT_ModuleOutput");
                CaptureBeltSupportHeight();
                return;
            }

            var explicitRelease = FindNamed(transform, "REF_CargoRelease");
            _usesExplicitFunnelRelease = explicitRelease != null;
            funnelCargoMarker = funnelCargoMarker != null
                ? funnelCargoMarker
                : explicitRelease
                  ?? (anchor != null ? anchor.OutputSocket : null)
                  ?? FindNamed(transform, "PORT_Belt");
        }

        private void PlaceOnBeltSurface(
            Vector3 surfaceStart,
            Vector3 surfaceEnd,
            float normalized)
        {
            var direction = surfaceEnd - surfaceStart;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                _visual.transform.SetPositionAndRotation(
                    surfaceStart,
                    transform.rotation);
            }
            else
            {
                direction.Normalize();
                _visual.transform.SetPositionAndRotation(
                    Vector3.zero,
                    transform.rotation);
                if (FactoryCargoVisualGeometry.TryGetWorldProjection(
                    _visual.transform,
                    direction,
                    out var back,
                    out var front))
                {
                    surfaceStart -= direction * back;
                    surfaceEnd -= direction * front;
                }

                _visual.transform.SetPositionAndRotation(
                    Vector3.Lerp(
                        surfaceStart,
                        surfaceEnd,
                        Mathf.Clamp01(normalized)),
                    transform.rotation);
            }

            FactoryCargoVisualGeometry.AlignMinimumToPlane(
                _visual.transform,
                surfaceStart,
                Vector3.up,
                out _);
        }

        private void PlaceAtFunnelRelease(Transform marker)
        {
            var releaseSurface = marker.position;
            releaseSurface.y = ResolveFunnelSupportWorldHeight(marker);
            var outward = _usesExplicitFunnelRelease
                ? marker.forward
                : -marker.forward;
            outward.y = 0f;
            if (outward.sqrMagnitude <= 0.000001f)
            {
                outward = transform.forward;
                outward.y = 0f;
            }

            outward.Normalize();
            _visual.transform.SetPositionAndRotation(
                releaseSurface,
                transform.rotation);
            FactoryCargoVisualGeometry.AlignMinimumToPlane(
                _visual.transform,
                releaseSurface,
                outward,
                out _);
            FactoryCargoVisualGeometry.AlignMinimumToPlane(
                _visual.transform,
                releaseSurface,
                Vector3.up,
                out _);
        }

        private float ResolveFunnelSupportWorldHeight(Transform releaseMarker)
        {
            if (_connectedBeltPresenter != null
                && _connectedBeltPresenter.gameObject.scene == gameObject.scene)
            {
                return _connectedBeltPresenter.ResolveBeltSupportWorldHeight();
            }

            foreach (var presenter in FindObjectsByType<
                FactoryLogisticsModulePresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (presenter == this
                    || presenter.nodeKind != MachineNodeKind.BeltModule
                    || presenter.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                presenter.ResolvePathMarkers();
                if (!Touches(
                        releaseMarker.position,
                        presenter.beltPathStart)
                    && !Touches(
                        releaseMarker.position,
                        presenter.beltPathEnd))
                {
                    continue;
                }

                _connectedBeltPresenter = presenter;
                return presenter.ResolveBeltSupportWorldHeight();
            }

            return releaseMarker.position.y;
        }

        private static bool Touches(Vector3 point, Transform marker) =>
            marker != null
            && Vector3.Distance(point, marker.position) <= 0.002f;

        private float ResolveBeltSupportWorldHeight()
        {
            if (!_hasBeltSupportHeight)
            {
                CaptureBeltSupportHeight();
            }

            return transform.TransformPoint(
                new Vector3(0f, _beltSupportLocalHeight, 0f)).y;
        }

        private void CaptureBeltSupportHeight()
        {
            if (nodeKind != MachineNodeKind.BeltModule)
            {
                return;
            }

            var explicitSurface = FindNamed(transform, "REF_CargoSurface");
            if (explicitSurface != null)
            {
                _beltSupportLocalHeight =
                    transform.InverseTransformPoint(explicitSurface.position).y;
                _hasBeltSupportHeight = true;
                return;
            }

            if (TryGetSupportRendererHeight("Batten", out var supportHeight)
                || TryGetSupportRendererHeight("Band", out supportHeight))
            {
                _beltSupportLocalHeight =
                    transform.InverseTransformPoint(
                        new Vector3(
                            transform.position.x,
                            supportHeight,
                            transform.position.z)).y;
                _hasBeltSupportHeight = true;
                return;
            }

            var colliders = GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                supportHeight = float.NegativeInfinity;
                for (var index = 0; index < colliders.Length; index++)
                {
                    supportHeight = Mathf.Max(
                        supportHeight,
                        colliders[index].bounds.max.y);
                }

                _beltSupportLocalHeight =
                    transform.InverseTransformPoint(
                        new Vector3(
                            transform.position.x,
                            supportHeight,
                            transform.position.z)).y;
                _hasBeltSupportHeight = true;
                return;
            }

            var fallback = beltPathStart != null
                ? beltPathStart.position
                : transform.position;
            _beltSupportLocalHeight =
                transform.InverseTransformPoint(fallback).y;
            _hasBeltSupportHeight = true;
        }

        private bool TryGetSupportRendererHeight(
            string nameFragment,
            out float worldHeight)
        {
            worldHeight = float.NegativeInfinity;
            var found = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.IndexOf(
                    nameFragment,
                    StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                worldHeight = Mathf.Max(worldHeight, renderer.bounds.max.y);
                found = true;
            }

            return found;
        }

        private GameObject ResolvePrefab(StableId itemId)
        {
            if (itemId == ContentIds.IronIngot)
            {
                return ironIngotPrefab;
            }

            if (itemId == ContentIds.IronPlate)
            {
                return ironPlatePrefab;
            }

            // Grezzi, Pietra e Tronco non hanno un campo serializzato come
            // Lingotto e Piastra: non erano mai arrivati su un nastro, e
            // aggiungere cinque riferimenti da collegare a mano nella scena
            // significherebbe cinque modi di dimenticarsene. Vengono quindi
            // risolti da Resources come il prefab della Trivella.
            if (itemId == ContentIds.RawIron)
            {
                return ResolveCarriedPrefab(itemId, "RawIron");
            }

            if (itemId == ContentIds.RawCopper)
            {
                return ResolveCarriedPrefab(itemId, "RawCopper");
            }

            if (itemId == ContentIds.RawTin)
            {
                return ResolveCarriedPrefab(itemId, "RawTin");
            }

            if (itemId == ContentIds.Stone)
            {
                return ResolveCarriedPrefab(itemId, "Stone");
            }

            if (itemId == ContentIds.WoodLog)
            {
                return ResolveCarriedPrefab(itemId, "WoodLog");
            }

            return null;
        }

        private GameObject ResolveCarriedPrefab(StableId itemId, string itemName)
        {
            if (_carriedPrefabs.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            var prefab = Resources.Load<GameObject>("Items/PF_" + itemName);
            if (prefab == null)
            {
                Debug.LogError(
                    $"Manca Resources/Items/PF_{itemName}. "
                    + "Eseguire CML/Art/Rebuild Carried Item Assets.",
                    this);
            }

            // Anche il fallimento va in cache: senza, un asset mancante
            // ripeterebbe la Resources.Load e il log a ogni oggetto in transito.
            _carriedPrefabs[itemId] = prefab;
            return prefab;
        }

        private void HideVisual()
        {
            if (_visual != null)
            {
                _visual.SetActive(false);
            }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static void MakePresentationOnly(GameObject instance)
        {
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            foreach (var rigidbody in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }
        }
    }

}

using System;
using System.Collections.Generic;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Machines;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Read-only Unity representation of the authoritative cargo riding one belt lane.
    /// Position is never integrated locally: every visual is placed from
    /// BeltItemState.PositionMillimetres / BeltLaneState.LengthMillimetres.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class FactoryBeltLanePresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class SerializedItemVisual
        {
            [SerializeField] private FactoryStableIdField itemId =
                new FactoryStableIdField();
            [SerializeField] private GameObject prefab;

            public StableId ItemId => itemId.GetValueOrNone();

            public GameObject Prefab => prefab;
        }

        private sealed class ActiveVisual
        {
            public StableId ItemId;
            public GameObject Prefab;
            public GameObject Instance;
        }

        [SerializeField] private FactoryStableIdField laneId =
            new FactoryStableIdField();
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform[] pathPoints = Array.Empty<Transform>();
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.12f, 0f);
        [SerializeField] private bool orientAlongPath = true;
        [SerializeField] private GameObject fallbackPrefab;
        [SerializeField] private SerializedItemVisual[] serializedVisuals =
            Array.Empty<SerializedItemVisual>();

        private readonly Dictionary<StableId, GameObject> _prefabs =
            new Dictionary<StableId, GameObject>();
        private readonly Dictionary<StableId, Stack<GameObject>> _pool =
            new Dictionary<StableId, Stack<GameObject>>();
        private readonly List<ActiveVisual> _active = new List<ActiveVisual>();
        private readonly List<GameObject> _created = new List<GameObject>();

        private SimulationEngine _engine;
        private Vector3[] _worldPath = Array.Empty<Vector3>();
        private float[] _cumulativeLength = Array.Empty<float>();
        private float _pathLength;

        public StableId LaneId => laneId.GetValueOrNone();

        public int ActiveVisualCount => _active.Count;

        public bool IsAttached => _engine != null && !LaneId.IsNone;

        public void Configure(
            SimulationEngine engine,
            StableId authoritativeLaneId,
            Transform[] route,
            Transform itemVisualRoot = null,
            Vector3? itemWorldOffset = null)
        {
            ConfigureAuthoring(
                authoritativeLaneId,
                route,
                itemVisualRoot,
                itemWorldOffset);
            AttachSimulation(engine);
        }

        /// <summary>
        /// Scene-builder boundary: persists lane identity and route without inventing an
        /// engine in edit mode. The sole scene composition root attaches the engine when
        /// play begins.
        /// </summary>
        public void ConfigureAuthoring(
            StableId authoritativeLaneId,
            Transform[] route,
            Transform itemVisualRoot = null,
            Vector3? itemWorldOffset = null)
        {
            if (authoritativeLaneId.IsNone)
            {
                throw new ArgumentException(
                    "A belt presenter needs an authoritative lane id.",
                    nameof(authoritativeLaneId));
            }

            laneId.Set(authoritativeLaneId);
            pathPoints = route ?? Array.Empty<Transform>();
            visualRoot = itemVisualRoot != null ? itemVisualRoot : transform;
            if (itemWorldOffset.HasValue)
            {
                worldOffset = itemWorldOffset.Value;
            }

            RebuildPath();
        }

        public void AttachSimulation(SimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            LoadSerializedMappings();
            RebuildPath();
            RefreshImmediate();
        }

        /// <summary>
        /// Registers the visual for one content item. Calling it again replaces the
        /// mapping; active instances of that item are swapped on the next refresh.
        /// </summary>
        public void SetItemPrefab(StableId itemId, GameObject prefab)
        {
            if (itemId.IsNone)
            {
                throw new ArgumentException(
                    "An item visual needs a content id.",
                    nameof(itemId));
            }

            if (prefab == null)
            {
                _prefabs.Remove(itemId);
            }
            else
            {
                _prefabs[itemId] = prefab;
            }

            DestroyPooledVisuals(itemId);
            RefreshImmediate();
        }

        public void SetFallbackPrefab(GameObject prefab)
        {
            fallbackPrefab = prefab;
            RefreshImmediate();
        }

        public void RebuildPath()
        {
            if (pathPoints == null || pathPoints.Length == 0)
            {
                _worldPath = new[] { transform.position, transform.position + transform.forward };
            }
            else if (pathPoints.Length == 1)
            {
                _worldPath = new[]
                {
                    pathPoints[0] != null ? pathPoints[0].position : transform.position,
                    (pathPoints[0] != null ? pathPoints[0].position : transform.position)
                    + transform.forward
                };
            }
            else
            {
                _worldPath = new Vector3[pathPoints.Length];
                for (var index = 0; index < pathPoints.Length; index++)
                {
                    _worldPath[index] = pathPoints[index] != null
                        ? pathPoints[index].position
                        : (index == 0 ? transform.position : _worldPath[index - 1]);
                }
            }

            _cumulativeLength = new float[_worldPath.Length];
            _pathLength = 0f;
            for (var index = 1; index < _worldPath.Length; index++)
            {
                _pathLength += Vector3.Distance(
                    _worldPath[index - 1],
                    _worldPath[index]);
                _cumulativeLength[index] = _pathLength;
            }

            if (_pathLength <= 0.0001f)
            {
                _worldPath[1] = _worldPath[0] + transform.forward;
                _pathLength = 1f;
                _cumulativeLength[1] = 1f;
            }
        }

        public void RefreshImmediate()
        {
            if (!IsAttached
                || !_engine.State.GetMachineSnapshot().TryGetLane(
                    LaneId,
                    out var lane))
            {
                ReleaseAll();
                return;
            }

            if (_worldPath.Length < 2)
            {
                RebuildPath();
            }

            ReconcileVisualCount(lane);
            for (var index = 0; index < lane.Items.Count; index++)
            {
                var item = lane.Items[index];
                var visual = _active[index];
                EnsureVisualMatches(visual, item.ItemId);
                if (visual.Instance == null)
                {
                    continue;
                }

                var normalized = lane.LengthMillimetres <= 0
                    ? 0f
                    : Mathf.Clamp01(
                        item.PositionMillimetres
                        / (float)lane.LengthMillimetres);
                SamplePath(normalized, out var position, out var tangent);
                visual.Instance.transform.position = position + worldOffset;
                if (orientAlongPath && tangent.sqrMagnitude > 0.0001f)
                {
                    visual.Instance.transform.rotation =
                        Quaternion.LookRotation(tangent, Vector3.up);
                }

                visual.Instance.SetActive(true);
            }
        }

        private void Awake()
        {
            visualRoot = visualRoot != null ? visualRoot : transform;
            LoadSerializedMappings();
            RebuildPath();
        }

        private void OnEnable()
        {
            LoadSerializedMappings();
            RebuildPath();
        }

        private void LateUpdate()
        {
            RefreshImmediate();
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

        private void OnDestroy()
        {
            for (var index = 0; index < _created.Count; index++)
            {
                if (_created[index] != null)
                {
                    Destroy(_created[index]);
                }
            }

            _created.Clear();
            _active.Clear();
            _pool.Clear();
        }

        private void LoadSerializedMappings()
        {
            if (serializedVisuals == null)
            {
                return;
            }

            for (var index = 0; index < serializedVisuals.Length; index++)
            {
                var binding = serializedVisuals[index];
                if (binding != null
                    && !binding.ItemId.IsNone
                    && binding.Prefab != null)
                {
                    _prefabs[binding.ItemId] = binding.Prefab;
                }
            }
        }

        private void ReconcileVisualCount(BeltLaneState lane)
        {
            while (_active.Count > lane.Items.Count)
            {
                var last = _active.Count - 1;
                Release(_active[last]);
                _active.RemoveAt(last);
            }

            while (_active.Count < lane.Items.Count)
            {
                _active.Add(
                    new ActiveVisual
                    {
                        ItemId = StableId.None,
                        Prefab = null,
                        Instance = null
                    });
            }
        }

        private void EnsureVisualMatches(ActiveVisual visual, StableId itemId)
        {
            var prefab = ResolvePrefab(itemId);
            if (visual.ItemId == itemId
                && visual.Prefab == prefab
                && visual.Instance != null
                && visual.Instance.activeSelf)
            {
                return;
            }

            if (visual.Instance != null && visual.Prefab != prefab)
            {
                _created.Remove(visual.Instance);
                Destroy(visual.Instance);
                visual.ItemId = StableId.None;
                visual.Prefab = null;
                visual.Instance = null;
            }
            else
            {
                Release(visual);
            }

            visual.ItemId = itemId;
            visual.Prefab = prefab;
            visual.Instance = prefab == null ? null : Acquire(itemId, prefab);
        }

        private GameObject ResolvePrefab(StableId itemId)
        {
            return _prefabs.TryGetValue(itemId, out var prefab)
                ? prefab
                : fallbackPrefab;
        }

        private GameObject Acquire(StableId itemId, GameObject prefab)
        {
            if (_pool.TryGetValue(itemId, out var available))
            {
                while (available.Count > 0)
                {
                    var pooled = available.Pop();
                    if (pooled != null)
                    {
                        pooled.SetActive(true);
                        return pooled;
                    }
                }
            }

            var instance = Instantiate(
                prefab,
                visualRoot != null ? visualRoot : transform);
            instance.name = $"{prefab.name}_LaneVisual";
            MakePresentationOnly(instance);
            _created.Add(instance);
            return instance;
        }

        private void Release(ActiveVisual visual)
        {
            if (visual == null || visual.Instance == null)
            {
                if (visual != null)
                {
                    visual.ItemId = StableId.None;
                    visual.Prefab = null;
                }

                return;
            }

            visual.Instance.SetActive(false);
            if (!_pool.TryGetValue(visual.ItemId, out var available))
            {
                available = new Stack<GameObject>();
                _pool.Add(visual.ItemId, available);
            }

            available.Push(visual.Instance);
            visual.ItemId = StableId.None;
            visual.Prefab = null;
            visual.Instance = null;
        }

        private void DestroyPooledVisuals(StableId itemId)
        {
            if (!_pool.TryGetValue(itemId, out var available))
            {
                return;
            }

            while (available.Count > 0)
            {
                var pooled = available.Pop();
                if (pooled == null)
                {
                    continue;
                }

                _created.Remove(pooled);
                Destroy(pooled);
            }

            _pool.Remove(itemId);
        }

        private void ReleaseAll()
        {
            for (var index = _active.Count - 1; index >= 0; index--)
            {
                Release(_active[index]);
            }

            _active.Clear();
        }

        private void SamplePath(
            float normalized,
            out Vector3 position,
            out Vector3 tangent)
        {
            var distance = Mathf.Clamp01(normalized) * _pathLength;
            var segment = 1;
            while (segment < _cumulativeLength.Length - 1
                   && distance > _cumulativeLength[segment])
            {
                segment++;
            }

            var previousDistance = _cumulativeLength[segment - 1];
            var segmentLength =
                Mathf.Max(0.0001f, _cumulativeLength[segment] - previousDistance);
            var segmentT = Mathf.Clamp01((distance - previousDistance) / segmentLength);
            var start = _worldPath[segment - 1];
            var end = _worldPath[segment];
            position = Vector3.LerpUnclamped(start, end, segmentT);
            tangent = (end - start).normalized;
        }

        private static void MakePresentationOnly(GameObject instance)
        {
            var colliders = instance.GetComponentsInChildren<Collider>(includeInactive: true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            var rigidbodies = instance.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (var index = 0; index < rigidbodies.Length; index++)
            {
                rigidbodies[index].isKinematic = true;
                rigidbodies[index].detectCollisions = false;
            }
        }
    }
}

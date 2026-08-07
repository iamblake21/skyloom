using System;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Simulation;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Presentation-only press cycle. The ram and the workpiece are sampled from the
    /// authoritative machine progress; animation cannot start a recipe, complete one,
    /// or insert an output.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class FactoryPressPresenter : MonoBehaviour
    {
        // Authored against MEC_MechanicalPress: at exactly 0.196 m the die
        // reaches the bed while the piston remains captured by its cylinder.
        public const float ProductionRamTravelMetres = 0.196f;

        [SerializeField] private FactoryStableIdField machineId =
            new FactoryStableIdField();
        [SerializeField] private Transform ram;
        [SerializeField] private Transform workpieceAnchor;
        [Tooltip("Travel axis expressed in the press root's local space, not in the imported FBX marker's local axes.")]
        [SerializeField] private Vector3 ramLocalAxis = Vector3.down;
        [SerializeField, Min(0f)] private float ramTravel =
            ProductionRamTravelMetres;
        [SerializeField] private GameObject ingotPrefab;
        [SerializeField] private GameObject platePrefab;
        [SerializeField] private Vector3 compressedIngotScale =
            new Vector3(1.4f, 0.2f, 1.3f);
        [SerializeField, Range(0.01f, 0.99f)] private float ramDownEnd = 0.18f;
        [SerializeField, Range(0.01f, 0.99f)] private float conversionPoint = 0.60f;
        [SerializeField, Range(0.01f, 0.99f)] private float ramRiseStart = 0.68f;
        [SerializeField, Range(0.01f, 0.99f)] private float ramRiseEnd = 0.93f;

        private SimulationEngine _engine;
        private GameCatalog _catalog;
        private GameObject _ingotVisual;
        private GameObject _plateVisual;
        private Vector3 _ramRestLocalPosition;
        private Vector3 _ingotBaseScale = Vector3.one;
        private Vector3 _plateBaseScale = Vector3.one;
        private bool _hasRamRestPose;

        public StableId MachineId => machineId.GetValueOrNone();

        public bool IsAttached =>
            _engine != null && _catalog != null && !MachineId.IsNone;

        public bool IsPresentingCycle { get; private set; }

        public void Configure(
            SimulationEngine engine,
            GameCatalog catalog,
            StableId authoritativeMachineId,
            Transform animatedRam,
            Transform bedAnchor,
            GameObject ingotVisualPrefab,
            GameObject plateVisualPrefab)
        {
            ConfigureAuthoring(
                authoritativeMachineId,
                animatedRam,
                bedAnchor,
                ingotVisualPrefab,
                plateVisualPrefab);
            AttachSimulation(engine, catalog);
        }

        /// <summary>
        /// Persists the authored press binding without creating gameplay state in the
        /// editor. FactoryLineSimulationRoot supplies the one authoritative engine.
        /// </summary>
        public void ConfigureAuthoring(
            StableId authoritativeMachineId,
            Transform animatedRam,
            Transform bedAnchor,
            GameObject ingotVisualPrefab,
            GameObject plateVisualPrefab)
        {
            if (authoritativeMachineId.IsNone)
            {
                throw new ArgumentException(
                    "A press presenter needs an authoritative machine id.",
                    nameof(authoritativeMachineId));
            }

            machineId.Set(authoritativeMachineId);
            ram = animatedRam;
            workpieceAnchor = bedAnchor;
            ingotPrefab = ingotVisualPrefab;
            platePrefab = plateVisualPrefab;
            CaptureRamRestPose();
            // Authoring is also called by the editor scene builder. Runtime-only
            // workpiece instances must not be serialized into the scene: their
            // backing fields are intentionally transient and Start would otherwise
            // create a second pair on entering Play Mode.
            if (Application.isPlaying)
            {
                RecreateWorkpieceVisuals();
            }
        }

        public void AttachSimulation(
            SimulationEngine engine,
            GameCatalog catalog)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            RefreshImmediate();
        }

        public void ConfigureMotion(
            Vector3 localAxis,
            float travel,
            Vector3 finalCompressedScale)
        {
            ramLocalAxis = localAxis.sqrMagnitude > 0.0001f
                ? localAxis.normalized
                : Vector3.down;
            ramTravel = Mathf.Max(0f, travel);
            compressedIngotScale = finalCompressedScale;
            RefreshImmediate();
        }

        public void SetVisualPrefabs(
            GameObject ingotVisualPrefab,
            GameObject plateVisualPrefab)
        {
            ingotPrefab = ingotVisualPrefab;
            platePrefab = plateVisualPrefab;
            RecreateWorkpieceVisuals();
            RefreshImmediate();
        }

        public void RefreshImmediate()
        {
            if (!IsAttached
                || !MachineDiagnostics.TryDescribe(
                    _engine.State,
                    _catalog,
                    MachineId,
                    out var report)
                || !report.IsCycleActive)
            {
                PresentRestState();
                return;
            }

            EnsureWorkpieceVisuals();
            IsPresentingCycle = true;
            var progress = Mathf.Clamp01(report.ProgressPermille / 1000f);
            PresentRam(progress);
            PresentWorkpiece(progress);
        }

        private void Awake()
        {
            CaptureRamRestPose();
        }

        private void Start()
        {
            EnsureWorkpieceVisuals();
            PresentRestState();
        }

        private void LateUpdate()
        {
            RefreshImmediate();
        }

        private void OnDisable()
        {
            PresentRestState();
        }

        private void OnDestroy()
        {
            DestroyVisual(ref _ingotVisual);
            DestroyVisual(ref _plateVisual);
        }

        private void CaptureRamRestPose()
        {
            if (ram == null)
            {
                _hasRamRestPose = false;
                return;
            }

            _ramRestLocalPosition = ram.localPosition;
            _hasRamRestPose = true;
        }

        private void PresentRam(float progress)
        {
            if (ram == null)
            {
                return;
            }

            if (!_hasRamRestPose)
            {
                CaptureRamRestPose();
            }

            float compression;
            if (progress < ramDownEnd)
            {
                compression = Smooth01(progress / Mathf.Max(0.001f, ramDownEnd));
            }
            else if (progress < ramRiseStart)
            {
                compression = 1f;
            }
            else
            {
                compression = 1f - Smooth01(
                    (progress - ramRiseStart)
                    / Mathf.Max(0.001f, ramRiseEnd - ramRiseStart));
            }

            var pressLocalAxis = ramLocalAxis.sqrMagnitude > 0.0001f
                ? ramLocalAxis.normalized
                : Vector3.down;
            var worldDelta =
                transform.TransformDirection(pressLocalAxis)
                * (ramTravel * compression);
            var ramParent = ram.parent;
            var parentLocalDelta = ramParent != null
                ? ramParent.InverseTransformVector(worldDelta)
                : worldDelta;
            ram.localPosition =
                _ramRestLocalPosition + parentLocalDelta;
        }

        private void PresentWorkpiece(float progress)
        {
            if (_ingotVisual == null && _plateVisual == null)
            {
                return;
            }

            AlignWorkpieceWithPress(_ingotVisual);
            AlignWorkpieceWithPress(_plateVisual);

            if (progress < conversionPoint)
            {
                SetActive(_ingotVisual, true);
                SetActive(_plateVisual, false);
                if (_ingotVisual != null)
                {
                    var squashStart = Mathf.Min(ramDownEnd, conversionPoint);
                    var squash = Smooth01(
                        (progress - squashStart)
                        / Mathf.Max(0.001f, conversionPoint - squashStart));
                    _ingotVisual.transform.localScale = Vector3.Scale(
                        _ingotBaseScale,
                        Vector3.LerpUnclamped(
                            Vector3.one,
                            compressedIngotScale,
                            Mathf.Clamp01(squash)));
                }
            }
            else
            {
                SetActive(_ingotVisual, false);
                SetActive(_plateVisual, true);
                if (_plateVisual != null)
                {
                    _plateVisual.transform.localScale = _plateBaseScale;
                }
            }
        }

        private void PresentRestState()
        {
            IsPresentingCycle = false;
            if (ram != null && _hasRamRestPose)
            {
                ram.localPosition = _ramRestLocalPosition;
            }

            SetActive(_ingotVisual, false);
            SetActive(_plateVisual, false);
        }

        private void RecreateWorkpieceVisuals()
        {
            DestroyVisual(ref _ingotVisual);
            DestroyVisual(ref _plateVisual);
            EnsureWorkpieceVisuals();
        }

        private void EnsureWorkpieceVisuals()
        {
            if (workpieceAnchor == null)
            {
                return;
            }

            if (_ingotVisual == null && ingotPrefab != null)
            {
                _ingotVisual = Instantiate(ingotPrefab, workpieceAnchor);
                _ingotVisual.name = $"{ingotPrefab.name}_PressWorkpiece";
                ResetLocalTransform(_ingotVisual.transform);
                AlignWorkpieceWithPress(_ingotVisual);
                _ingotBaseScale = _ingotVisual.transform.localScale;
                MakePresentationOnly(_ingotVisual);
                _ingotVisual.SetActive(false);
            }

            if (_plateVisual == null && platePrefab != null)
            {
                _plateVisual = Instantiate(platePrefab, workpieceAnchor);
                _plateVisual.name = $"{platePrefab.name}_PressWorkpiece";
                ResetLocalTransform(_plateVisual.transform);
                AlignWorkpieceWithPress(_plateVisual);
                _plateBaseScale = _plateVisual.transform.localScale;
                MakePresentationOnly(_plateVisual);
                _plateVisual.SetActive(false);
            }
        }

        private void AlignWorkpieceWithPress(GameObject visual)
        {
            if (visual == null || workpieceAnchor == null)
            {
                return;
            }

            // REF_Workpiece supplies only the bed position. Imported marker axes
            // are not a valid cargo orientation: inheriting them can stand the
            // plate upright during the press cycle even though the same prefab is
            // flat on the adjacent belt. Cargo under the ram follows the press
            // root orientation, exactly like belt cargo follows its module root.
            visual.transform.SetPositionAndRotation(
                workpieceAnchor.position,
                transform.rotation);
        }

        private static void ResetLocalTransform(Transform target)
        {
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
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

        private static void DestroyVisual(ref GameObject visual)
        {
            if (visual != null)
            {
                Destroy(visual);
                visual = null;
            }
        }

        private void OnValidate()
        {
            ramTravel = Mathf.Max(0f, ramTravel);
            if (ramLocalAxis.sqrMagnitude <= 0.0001f)
            {
                ramLocalAxis = Vector3.down;
            }

            ramDownEnd = Mathf.Clamp(ramDownEnd, 0.01f, 0.99f);
            conversionPoint = Mathf.Clamp(
                conversionPoint,
                ramDownEnd + 0.01f,
                0.99f);
            ramRiseStart = Mathf.Clamp(
                ramRiseStart,
                conversionPoint,
                0.99f);
            ramRiseEnd = Mathf.Clamp(
                ramRiseEnd,
                ramRiseStart + 0.01f,
                0.99f);
        }
    }
}

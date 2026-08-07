using CML.Unity.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CML.Unity.Factory
{
    /// <summary>
    /// The single source for object interaction: one centre ray, one E edge and one
    /// world-space prompt. Chests, workbenches, machines and airship parts all expose
    /// the same target contract; this component contains no per-object input path.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class FactoryCentralInteractor : MonoBehaviour
    {
        private const int RaycastCapacity = 64;

        [SerializeField] private Camera viewCamera;
        [SerializeField] private FactoryHudOrchestrator hud;
        [SerializeField, Min(0.1f)] private float interactionDistance = 3.25f;
        [SerializeField] private LayerMask interactionLayers = ~0;

        private readonly RaycastHit[] _raycastHits = new RaycastHit[RaycastCapacity];
        private readonly Collider[] _nearbyColliders = new Collider[RaycastCapacity];
        private IWorldInteractionTarget _currentInteraction;
        private WorldInteractionPrompt _worldPrompt;
        private string _currentPrompt = string.Empty;

        // Kept for existing scene verification and diagnostics. New code should use
        // CurrentInteraction because the central source is no longer factory-only.
        public FactoryInteractionTarget CurrentTarget =>
            _currentInteraction as FactoryInteractionTarget;

        public IWorldInteractionTarget CurrentInteraction => _currentInteraction;

        public string CurrentPrompt => _currentPrompt;

        public void Configure(
            Camera camera,
            FactoryHudOrchestrator hudOrchestrator,
            UIDocument legacyPromptDocument = null,
            float distance = 3.25f,
            LayerMask? layers = null)
        {
            viewCamera = camera;
            hud = hudOrchestrator;
            interactionDistance = Mathf.Max(0.1f, distance);
            if (layers.HasValue)
            {
                interactionLayers = layers.Value;
            }
        }

        private void Awake()
        {
            ResolveDependencies();
            ClearTarget();
        }

        private void Update()
        {
            ResolveDependencies();
            var keyboard = Keyboard.current;
            if (hud != null && hud.AnyModalOpen)
            {
                ClearTarget();
                if (hud.InteractionPanelOpen
                    && keyboard != null
                    && keyboard.eKey.wasPressedThisFrame)
                {
                    WorldInteractionInput.ConsumeCurrentFrame();
                    hud.CloseInteractionPanel();
                }

                return;
            }

            FindTarget();
            if (_currentInteraction != null
                && keyboard != null
                && keyboard.eKey.wasPressedThisFrame)
            {
                WorldInteractionInput.ConsumeCurrentFrame();
                _currentInteraction.TryInteract();
            }
        }

        private void OnDisable()
        {
            ClearTarget();
        }

        private void OnDestroy()
        {
            _worldPrompt?.Dispose();
            _worldPrompt = null;
        }

        private void FindTarget()
        {
            if (viewCamera == null)
            {
                ClearTarget();
                return;
            }

            var ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                _raycastHits,
                interactionDistance,
                interactionLayers,
                QueryTriggerInteraction.Collide);
            IWorldInteractionTarget bestTarget = null;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < hitCount; index++)
            {
                var hit = _raycastHits[index];
                if (hit.collider == null || hit.distance >= bestDistance)
                {
                    continue;
                }

                var target = ResolveTarget(hit.collider);
                if (target == null || !target.IsInteractionAvailable)
                {
                    continue;
                }

                bestTarget = target;
                bestDistance = hit.distance;
            }

            Bounds bounds;
            if (bestTarget != null)
            {
                if (!bestTarget.TryGetInteractionBounds(out bounds))
                {
                    ClearTarget();
                    return;
                }
            }
            else if (!TryFindProximityTarget(out bestTarget, out bounds))
            {
                ClearTarget();
                return;
            }

            _currentInteraction = bestTarget;
            _currentPrompt = $"E   {bestTarget.InteractionPrompt}";
            EnsureWorldPrompt();
            _worldPrompt.Show(_currentPrompt, bounds, viewCamera);
        }

        private bool TryFindProximityTarget(
            out IWorldInteractionTarget bestTarget,
            out Bounds bestBounds)
        {
            bestTarget = null;
            bestBounds = default;
            var cameraPosition = viewCamera.transform.position;
            var count = Physics.OverlapSphereNonAlloc(
                cameraPosition,
                interactionDistance,
                _nearbyColliders,
                interactionLayers,
                QueryTriggerInteraction.Collide);
            var bestScore = float.PositiveInfinity;
            for (var index = 0; index < count; index++)
            {
                var collider = _nearbyColliders[index];
                var target = ResolveTarget(collider);
                if (target == null
                    || !target.IsInteractionAvailable
                    || !target.TryGetInteractionBounds(out var bounds)
                    || bounds.SqrDistance(cameraPosition)
                        > interactionDistance * interactionDistance)
                {
                    continue;
                }

                var viewport = viewCamera.WorldToViewportPoint(bounds.center);
                if (viewport.z <= 0f)
                {
                    continue;
                }

                var horizontal = Mathf.Abs(viewport.x - 0.5f) / 0.24f;
                var vertical = Mathf.Abs(viewport.y - 0.5f) / 0.3f;
                var score = horizontal * horizontal + vertical * vertical;
                if (score > 1f || score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestTarget = target;
                bestBounds = bounds;
            }

            return bestTarget != null;
        }

        private static IWorldInteractionTarget ResolveTarget(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            var behaviours = collider.GetComponentsInParent<MonoBehaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IWorldInteractionTarget candidate
                    && candidate.OwnsInteractionCollider(collider))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ResolveDependencies()
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            {
                viewCamera = Camera.main;
            }

            if (hud == null)
            {
                hud = FindFirstObjectByType<FactoryHudOrchestrator>();
            }

            EnsureWorldPrompt();
        }

        private void EnsureWorldPrompt()
        {
            if (_worldPrompt == null && Application.isPlaying)
            {
                _worldPrompt = new WorldInteractionPrompt();
            }
        }

        private void ClearTarget()
        {
            _currentInteraction = null;
            _currentPrompt = string.Empty;
            _worldPrompt?.Hide();
        }

        private void OnValidate()
        {
            interactionDistance = Mathf.Max(0.1f, interactionDistance);
        }
    }
}

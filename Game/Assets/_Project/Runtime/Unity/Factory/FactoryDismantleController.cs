using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Machines;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Holds the right mouse button to take a placed component back.
    ///
    /// This is deliberately a separate component from FactoryBuildController: that one
    /// only runs while a placeable item is held, and salvaging has to work with empty
    /// hands. The two never contend for input, since building is left button and
    /// salvaging is right.
    ///
    /// The controller never destroys anything itself on the strength of its own timer.
    /// It submits a salvage command and waits for the authoritative graph to actually
    /// lose the node before removing its GameObject, so a refused salvage - a full
    /// inventory, say - leaves the world exactly as it was.
    /// </summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class FactoryDismantleController : MonoBehaviour
    {
        private const float MaximumRayDistance = 18f;
        private const int RadialTickCount = 48;

        [SerializeField] private FactoryLineSimulationRoot simulationRoot;
        [SerializeField] private FactoryHudOrchestrator hud;
        [SerializeField] private Camera playerCamera;

        [Tooltip("Seconds of holding right mouse needed to break one component.")]
        [SerializeField] private float breakSeconds = 0.65f;

        private readonly List<PendingSalvage> _pending = new List<PendingSalvage>();
        private FactoryLineSimulationRoot _subscribedRoot;
        private FactoryNodeAnchor _heldTarget;
        private FactoryInteractionTarget _heldWorkbench;
        private float _heldSeconds;
        private string _status = string.Empty;
        private bool _statusIsRefusal;

        /// <summary>Zero when nothing is being broken, else how far along it is.</summary>
        public float BreakProgress =>
            (_heldTarget == null && _heldWorkbench == null)
                || breakSeconds <= 0f
                ? 0f
                : Mathf.Clamp01(_heldSeconds / breakSeconds);

        public void Configure(
            FactoryLineSimulationRoot root,
            FactoryHudOrchestrator hudOrchestrator,
            Camera viewCamera)
        {
            simulationRoot = root;
            hud = hudOrchestrator;
            playerCamera = viewCamera;
            RefreshSimulationSubscription();
        }

        private void OnEnable()
        {
            RefreshSimulationSubscription();
        }

        private void OnDisable()
        {
            if (_subscribedRoot != null)
            {
                _subscribedRoot.StateCommitted -= HandleStateCommitted;
                _subscribedRoot = null;
            }

            CancelHold();
        }

        private void RefreshSimulationSubscription()
        {
            if (_subscribedRoot == simulationRoot)
            {
                return;
            }

            if (_subscribedRoot != null)
            {
                _subscribedRoot.StateCommitted -= HandleStateCommitted;
            }

            _subscribedRoot = simulationRoot;
            if (_subscribedRoot != null)
            {
                _subscribedRoot.StateCommitted += HandleStateCommitted;
            }
        }

        private void Update()
        {
            if (simulationRoot == null || playerCamera == null)
            {
                return;
            }

            if (hud != null && hud.AnyModalOpen)
            {
                CancelHold();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                CancelHold();
                return;
            }

            if (!TryRaycastTarget(
                    out var anchor,
                    out var workbench,
                    out var hitPoint))
            {
                CancelHold();
                return;
            }

            if (workbench != null)
            {
                HoldAndBreakWorkbench(workbench, hitPoint);
                return;
            }

            // Sliding the crosshair onto a different component restarts the break.
            // Progress belongs to one object, never to the button being down.
            if (_heldTarget != anchor)
            {
                _heldTarget = anchor;
                _heldWorkbench = null;
                _heldSeconds = 0f;
            }

            var state = simulationRoot.Engine.State.GetMachineSnapshot();
            if (!state.TryGetNode(anchor.NodeId, out var node))
            {
                CancelHold();
                return;
            }

            if (!simulationRoot.TryPreflightSalvage(
                    anchor.NodeId,
                    out var rejection))
            {
                // Refused before any time is invested, so the player is told why
                // instead of watching a ring fill up and then achieve nothing.
                _heldSeconds = 0f;
                _status = DescribeRefusal(node.Kind, rejection);
                _statusIsRefusal = true;
                return;
            }

            _statusIsRefusal = false;
            _status = $"Sto smontando {DescribeKind(node.Kind)}";
            _heldSeconds += Time.deltaTime;
            if (_heldSeconds < breakSeconds)
            {
                return;
            }

            Break(anchor, node.Kind, hitPoint);
        }

        private void HoldAndBreakWorkbench(
            FactoryInteractionTarget workbench,
            Vector3 hitPoint)
        {
            if (_heldWorkbench != workbench)
            {
                _heldWorkbench = workbench;
                _heldTarget = null;
                _heldSeconds = 0f;
            }

            _statusIsRefusal = false;
            _status = "Sto smontando il banco da lavoro";
            _heldSeconds += Time.deltaTime;
            if (_heldSeconds < breakSeconds)
            {
                return;
            }

            if (!simulationRoot.TryStorePlayerItem(
                    ContentIds.WorkbenchItem,
                    1L,
                    out var storeFailure))
            {
                _heldSeconds = breakSeconds;
                _statusIsRefusal = true;
                _status = storeFailure == InventoryFailure.CapacityExceeded
                    ? "Zaino pieno: libera uno slot per smontare il banco"
                    : "Impossibile recuperare il banco da lavoro";
                return;
            }

            FactoryDustBurst.Play(
                hitPoint,
                ResolveDustTint(workbench.gameObject));
            _status = "Banco da lavoro aggiunto allo zaino";
            var removed = workbench.gameObject;
            _heldWorkbench = null;
            _heldSeconds = 0f;
            Destroy(removed);
        }

        private void Break(
            FactoryNodeAnchor anchor,
            MachineNodeKind kind,
            Vector3 hitPoint)
        {
            var acceptance = simulationRoot.SubmitSalvage(anchor.NodeId);
            _pending.Add(new PendingSalvage(acceptance.Command, anchor, hitPoint));
            _status = $"Smontato {DescribeKind(kind)}";
            _statusIsRefusal = false;
            _heldTarget = null;
            _heldSeconds = 0f;
        }

        /// <summary>
        /// Retires the visual only once the graph agrees the node has gone. Waiting for
        /// the commit is what keeps a refused salvage from leaving a hole in the world.
        /// </summary>
        private void HandleStateCommitted(SimulationEngine engine)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var state = engine.State.GetMachineSnapshot();
            for (var index = _pending.Count - 1; index >= 0; index--)
            {
                var pending = _pending[index];
                if (engine.State.Tick.Value < pending.Command.TargetTick.Value)
                {
                    continue;
                }

                if (pending.Anchor != null
                    && !state.TryGetNode(pending.Anchor.NodeId, out _))
                {
                    FactoryDustBurst.Play(
                        pending.HitPoint,
                        ResolveDustTint(pending.Anchor.gameObject));
                    Destroy(pending.Anchor.gameObject);
                }

                _pending.RemoveAt(index);
            }
        }

        private bool TryRaycastTarget(
            out FactoryNodeAnchor anchor,
            out FactoryInteractionTarget workbench,
            out Vector3 hitPoint)
        {
            anchor = null;
            workbench = null;
            hitPoint = default;
            var ray = playerCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            var hits = Physics.RaycastAll(
                ray,
                MaximumRayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < hits.Length; index++)
            {
                var candidateAnchor =
                    hits[index].collider.GetComponentInParent<FactoryNodeAnchor>();
                var candidateWorkbench =
                    hits[index].collider.GetComponentInParent<
                        FactoryInteractionTarget>();
                if (candidateWorkbench != null
                    && candidateWorkbench.InteractionKind
                        != FactoryInteractionKind.Workbench)
                {
                    candidateWorkbench = null;
                }

                if ((candidateAnchor == null && candidateWorkbench == null)
                    || hits[index].distance >= bestDistance)
                {
                    continue;
                }

                anchor = candidateAnchor;
                workbench = candidateAnchor == null
                    ? candidateWorkbench
                    : null;
                hitPoint = hits[index].point;
                bestDistance = hits[index].distance;
            }

            return anchor != null || workbench != null;
        }

        private void CancelHold()
        {
            _heldTarget = null;
            _heldWorkbench = null;
            _heldSeconds = 0f;
            _status = string.Empty;
            _statusIsRefusal = false;
        }

        /// <summary>
        /// Draws the fill ring at the crosshair. It is deliberately drawn from ticks
        /// rather than a texture so the component carries no art dependency and needs
        /// no scene wiring to work.
        /// </summary>
        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_status))
            {
                return;
            }

            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var progress = BreakProgress;
            if (progress > 0f)
            {
                DrawRadialFill(centre, progress);
            }

            var label = new Rect(centre.x - 200f, centre.y + 54f, 400f, 24f);
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
            };
            style.normal.textColor = _statusIsRefusal
                ? new Color(1f, 0.45f, 0.4f)
                : Color.white;
            GUI.Label(label, _status, style);
        }

        private static void DrawRadialFill(Vector2 centre, float progress)
        {
            const float innerRadius = 26f;
            const float outerRadius = 34f;
            var filled = Mathf.RoundToInt(progress * RadialTickCount);
            for (var index = 0; index < RadialTickCount; index++)
            {
                // Start at twelve o'clock and run clockwise, the direction a filling
                // gauge is read.
                var angle = (index / (float)RadialTickCount) * Mathf.PI * 2f
                    - Mathf.PI * 0.5f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var from = centre + direction * innerRadius;
                var to = centre + direction * outerRadius;
                var isFilled = index < filled;
                DrawTick(
                    from,
                    to,
                    isFilled
                        ? new Color(1f, 0.86f, 0.45f, 0.95f)
                        : new Color(0f, 0f, 0f, 0.35f));
            }
        }

        private static void DrawTick(Vector2 from, Vector2 to, Color colour)
        {
            var previous = GUI.color;
            GUI.color = colour;
            var mid = (from + to) * 0.5f;
            var length = (to - from).magnitude;
            var angleDegrees =
                Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            var rect = new Rect(mid.x - length * 0.5f, mid.y - 1.5f, length, 3f);
            var pivot = new Vector2(mid.x, mid.y);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDegrees, pivot);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private static Color ResolveDustTint(GameObject broken)
        {
            var renderer = broken.GetComponentInChildren<Renderer>();
            if (renderer == null
                || renderer.sharedMaterial == null
                || !renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                return new Color(0.72f, 0.55f, 0.36f);
            }

            return renderer.sharedMaterial.GetColor("_BaseColor");
        }

        private static string DescribeRefusal(
            MachineNodeKind kind,
            CommandRejectionReason rejection)
        {
            switch (rejection)
            {
                case CommandRejectionReason.SalvageDestinationFull:
                    return "Inventario pieno: "
                        + $"non c'è posto per {DescribeKind(kind)} e il suo contenuto";
                case CommandRejectionReason.BuildDefinitionMissing:
                    return $"{DescribeKind(kind)} non è recuperabile";
                default:
                    return $"Non posso smontare questo ({rejection})";
            }
        }

        private static string DescribeKind(MachineNodeKind kind)
        {
            switch (kind)
            {
                case MachineNodeKind.Buffer:
                    return "la cassa";
                case MachineNodeKind.Machine:
                    return "la macchina";
                case MachineNodeKind.Funnel:
                    return "l'imbuto";
                case MachineNodeKind.BeltModule:
                    return "il nastro";
                default:
                    return "questo";
            }
        }

        private readonly struct PendingSalvage
        {
            public PendingSalvage(
                SimulationCommand command,
                FactoryNodeAnchor anchor,
                Vector3 hitPoint)
            {
                Command = command;
                Anchor = anchor;
                HitPoint = hitPoint;
            }

            public SimulationCommand Command { get; }

            public FactoryNodeAnchor Anchor { get; }

            public Vector3 HitPoint { get; }
        }
    }
}

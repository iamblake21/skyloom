using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Machines;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Places the item currently held in the hotbar.
    ///
    /// This controller has no build mode and never asks the player to select a
    /// source or destination. Placement and transport are intentionally separate:
    ///
    /// - the pose says where a module physically exists;
    /// - adjacency says which physical ports touch;
    /// - the Belt Drive decides direction and capacity for its connected line;
    /// - a crate is directionless and can be served from all four sides.
    ///
    /// Preview and committed objects use the same resolved transform. There is no
    /// second set of hand-authored offsets for runtime-built modules.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class FactoryBuildController : MonoBehaviour
    {
        private const float MaximumRayDistance = 18f;
        private const float GridSizeMetres = 1f;
        private const float IntersectionToleranceMetres = 0.01f;
        private const float FunnelVisualYawOffsetDegrees = 180f;
        private const float InclineVisualYawOffsetDegrees = 180f;

        [SerializeField] private FactoryLineSimulationRoot simulationRoot;
        [SerializeField] private FactoryHudOrchestrator hud;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform builtObjectsRoot;
        [SerializeField] private GameObject cratePrefab;
        [SerializeField] private GameObject funnelPrefab;
        [SerializeField] private GameObject beltStraightPrefab;
        [SerializeField] private GameObject beltDrivePrefab;
        [SerializeField] private GameObject beltCurvePrefab;
        [SerializeField] private GameObject beltInclinePrefab;
        [SerializeField] private GameObject beltCurveLeftPrefab;
        [SerializeField] private GameObject pressPrefab;
        [SerializeField] private GameObject furnacePrefab;
        [SerializeField] private GameObject drillPrefab;

        /// <summary>
        /// Tenuto allineato a <c>MechanicalDrillAssetSetup.PrefabResourcePath</c>,
        /// che non è raggiungibile da qui perché vive nell'assembly Editor.
        /// </summary>
        private const string DrillPrefabResourcePath = "Machinery/PF_MechanicalDrill";
        [SerializeField] private GameObject ironIngotPrefab;
        [SerializeField] private GameObject ironPlatePrefab;
        [SerializeField] private Material validHologramMaterial;
        [SerializeField] private Material invalidHologramMaterial;

        private readonly List<PendingBuild> _pending = new List<PendingBuild>();
        private GameObject _nodePreview;
        private BuildSelection _selection;
        private MachineBuildSpecification _previewSpecification;
        private Vector3 _previewPosition;
        private Vector3 _previewVisualPosition;
        private Quaternion _previewRotation = Quaternion.identity;
        private StableId _heldItemId;
        private long _heldQuantity;
        private bool _hasPlaceableHeld;
        private bool _previewValid;
        private bool _yawExplicitlyRotated;
        private string _status = string.Empty;

        /// <summary>
        /// Perché l'ultimo tentativo di piazzare la Trivella non ha trovato un
        /// giacimento utilizzabile. Vive qui perché la specifica di build può
        /// dire soltanto "senza ricetta", non il motivo.
        /// </summary>
        private string _drillPlacementNote = string.Empty;
        private byte _yawQuarterTurns;
        private CursorLockMode _previousCursorLockState;
        private FactoryLineSimulationRoot _subscribedRoot;

        public bool HasPlaceableHeld => _hasPlaceableHeld;

        public bool PreviewValid => _previewValid;

        public void Configure(
            FactoryLineSimulationRoot root,
            FactoryHudOrchestrator hudOrchestrator,
            Camera viewCamera,
            Transform runtimeObjectsRoot,
            GameObject woodenCrate,
            GameObject funnel,
            GameObject straightBelt,
            GameObject mechanicalPress,
            GameObject ironIngot,
            GameObject ironPlate,
            Material validMaterial,
            Material invalidMaterial,
            GameObject driveBelt = null,
            GameObject curveBelt = null,
            GameObject inclineBelt = null,
            GameObject curveLeftBelt = null,
            GameObject crudeFurnace = null)
        {
            simulationRoot = root;
            hud = hudOrchestrator;
            playerCamera = viewCamera;
            builtObjectsRoot = runtimeObjectsRoot;
            cratePrefab = woodenCrate;
            funnelPrefab = funnel;
            beltStraightPrefab = straightBelt;
            beltDrivePrefab = driveBelt;
            beltCurvePrefab = curveBelt;
            beltInclinePrefab = inclineBelt;
            beltCurveLeftPrefab = curveLeftBelt;
            pressPrefab = mechanicalPress;
            if (crudeFurnace != null)
            {
                furnacePrefab = crudeFurnace;
            }
            ironIngotPrefab = ironIngot;
            ironPlatePrefab = ironPlate;
            validHologramMaterial = validMaterial;
            invalidHologramMaterial = invalidMaterial;
            RefreshSimulationSubscription();
        }

        private void Awake()
        {
            ResolveFurnaceTemplate();
        }

        private void OnEnable()
        {
            _previousCursorLockState = Cursor.lockState;
            RefreshSimulationSubscription();
        }

        private void OnDisable()
        {
            if (_subscribedRoot != null)
            {
                _subscribedRoot.StateCommitted -= HandleStateCommitted;
                _subscribedRoot = null;
            }

            ClearPreview();
        }

        private void Update()
        {
            if (simulationRoot == null || playerCamera == null)
            {
                return;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var cursorJustRelocked =
                _previousCursorLockState != CursorLockMode.Locked
                && Cursor.lockState == CursorLockMode.Locked;
            _previousCursorLockState = Cursor.lockState;

            if (hud != null && hud.AnyModalOpen)
            {
                SetNodePreviewVisible(false);
                return;
            }

            if (!RefreshHeldPlacement())
            {
                return;
            }

            if (_pending.Count > 0)
            {
                SetNodePreviewVisible(false);
                return;
            }

            if (keyboard?.rKey.wasPressedThisFrame == true)
            {
                _yawQuarterTurns = (byte)((_yawQuarterTurns + 1) & 3);
                _yawExplicitlyRotated = true;
            }

            UpdatePreview();
            if (!cursorJustRelocked
                && mouse?.leftButton.wasPressedThisFrame == true
                && _previewValid)
            {
                CommitPlacement();
            }
        }

        private bool RefreshHeldPlacement()
        {
            if (hud == null
                || !hud.TryGetSelectedHotbarItem(out var itemId, out var quantity)
                || quantity <= 0L
                || !TryResolveSelection(itemId, out var selection))
            {
                if (_hasPlaceableHeld)
                {
                    _hasPlaceableHeld = false;
                    _heldItemId = StableId.None;
                    _heldQuantity = 0L;
                    _status = string.Empty;
                    ClearPreview();
                }

                return false;
            }

            if (!_hasPlaceableHeld || _heldItemId != itemId)
            {
                _selection = selection;
                _heldItemId = itemId;
                _yawQuarterTurns = 0;
                _yawExplicitlyRotated = false;
                ClearPreview();
                _status = SelectionInstruction(selection);
            }

            _hasPlaceableHeld = true;
            _heldQuantity = quantity;
            return true;
        }

        private void UpdatePreview()
        {
            _previewValid = false;
            var prefab = PrefabFor(_selection);
            RaycastHit surfaceHit = default;
            FactoryNodeAnchor pointedNode = null;
            RaycastHit pointedHit = default;
            var aimed = prefab != null
                && TryRaycastPlacement(
                    out surfaceHit,
                    out pointedNode,
                    out pointedHit);
            if (!aimed)
            {
                SetNodePreviewVisible(false);
                _status = "Mira una superficie su cui costruire";
                return;
            }

            var state = simulationRoot.Engine.State.GetMachineSnapshot();
            var heldKind = BuildKindFor(_selection);
            var desired = Pose(Snap(surfaceHit.point), _yawQuarterTurns);
            var resolved = desired;
            var snappedTargetId = StableId.None;

            if (pointedNode != null
                && state.TryGetNode(pointedNode.NodeId, out var target)
                && target.HasPlacementPose)
            {
                var aimedSide = ResolveAimedSide(
                    pointedNode.transform,
                    pointedHit.point);
                resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                    state,
                    target.Id,
                    desired,
                    heldKind,
                    aimedSide,
                    _yawQuarterTurns,
                    _yawExplicitlyRotated,
                    DefinitionIdFor(_selection));
                snappedTargetId = target.Id;
            }
            else
            {
                resolved =
                    FactoryBuildPlacementResolver.ResolveOccupiedConnectorCell(
                        state,
                        desired,
                        heldKind,
                        inheritConnectorYaw: !_yawExplicitlyRotated,
                        heldDefinitionId: DefinitionIdFor(_selection));
            }

            _yawQuarterTurns = resolved.YawQuarterTurns;
            _previewPosition = Position(resolved);
            _previewRotation = VisualRotation(_selection, _yawQuarterTurns);

            EnsureNodePreview(prefab);
            EnsureStandardSockets(
                _nodePreview.transform,
                NodeKindFor(heldKind),
                DefinitionIdFor(_selection));
            _nodePreview.transform.SetPositionAndRotation(
                _previewPosition,
                _previewRotation);

            _previewVisualPosition = AlignToOneTarget(
                state,
                resolved,
                NodeKindFor(heldKind),
                snappedTargetId,
                out var attachedToTarget);
            _nodePreview.transform.position = _previewVisualPosition;
            SetNodePreviewVisible(true);

            _previewSpecification = SpecificationFor(_selection, resolved);
            var hasPhysicalOverlap = HasPhysicalOverlap(
                attachedToTarget ? snappedTargetId : StableId.None,
                out var blockerName);
            var rejection = default(CommandRejectionReason);
            var passesPreflight =
                !hasPhysicalOverlap
                && simulationRoot.TryPreflightBuild(
                    _previewSpecification,
                    out rejection);
            _previewValid = !hasPhysicalOverlap && passesPreflight;
            ApplyHologramMaterial(_nodePreview, _previewValid);

            if (_previewValid)
            {
                _status = "Click sinistro - piazza · R - ruota";
            }
            else if (hasPhysicalOverlap)
            {
                _status = $"Spazio occupato da {blockerName}";
            }
            else if (_selection == BuildSelection.Drill
                && _previewSpecification.SecondaryId.IsNone)
            {
                // Il rifiuto generico direbbe soltanto "topologia non valida",
                // che qui non aiuta: la causa vera è il terreno sotto la
                // macchina, e il giocatore deve sapere che cosa cercare.
                _status = _drillPlacementNote;
            }
            else
            {
                _status = DescribeRejection(state, resolved, rejection);
            }
        }

        /// <summary>
        /// Applies at most one physical attachment. The object explicitly under the
        /// crosshair is the only possible target: no global nearest-neighbour scan
        /// can pull the preview toward an unrelated crate, funnel or belt.
        /// </summary>
        private Vector3 AlignToOneTarget(
            MachineSimulationState state,
            MachineBuildPose previewPose,
            MachineNodeKind previewKind,
            StableId targetId,
            out bool attached)
        {
            attached = false;
            if (targetId.IsNone
                || !state.TryGetNode(targetId, out var target)
                || !target.HasPlacementPose
                || !FactoryBuildPlacementResolver.ArePortsAdjacent(
                    previewKind,
                    previewPose,
                    DefinitionIdFor(_selection),
                    target.Kind,
                    target.PlacementPose,
                    target.DefinitionId)
                || !TryFindAnchor(targetId, out var targetAnchor))
            {
                return _nodePreview.transform.position;
            }

            Vector3 previewContact;
            Vector3 targetContact;
            if (previewKind == MachineNodeKind.BeltModule
                && target.Kind == MachineNodeKind.BeltModule)
            {
                // Belt poses already name two adjacent one-metre cells. Their
                // contact is therefore the shared cell edge, not whichever
                // authored/runtime socket happens to be closest.
                //
                // Falling back to the root when a Curve socket was absent put
                // that contact at the centre of the Curve. Aligning the next
                // straight to it then buried exactly half of the straight inside
                // the curved module, which is the overlap visible in game.
                var alignedPosition =
                    FactoryBuildPlacementResolver.ResolveBeltVisualPosition(
                        _nodePreview.transform.position,
                        previewPose,
                        targetAnchor.transform.position,
                        target.PlacementPose);
                attached = true;
                return alignedPosition;
            }
            else if (!TryGetContactPoint(
                         _nodePreview.transform,
                         previewKind,
                         previewPose,
                         target.Kind,
                         target.PlacementPose,
                         out previewContact)
                     || !TryGetContactPoint(
                         targetAnchor.transform,
                         target.Kind,
                         target.PlacementPose,
                         previewKind,
                         previewPose,
                         out targetContact))
            {
                return _nodePreview.transform.position;
            }

            // Build surfaces define the vertical placement. Port snapping only
            // changes X/Z, preventing an authored marker from lifting a module.
            var delta = targetContact - previewContact;
            delta.y = 0f;
            _nodePreview.transform.position += delta;
            attached = true;
            return _nodePreview.transform.position;
        }

        private static bool TryGetContactPoint(
            Transform root,
            MachineNodeKind kind,
            MachineBuildPose ownPose,
            MachineNodeKind otherKind,
            MachineBuildPose otherPose,
            out Vector3 point)
        {
            if (kind == MachineNodeKind.Buffer)
            {
                if (!TryCalculatePhysicalBounds(root, out var bounds))
                {
                    point = root.position;
                    return false;
                }

                point = BoundsFaceToward(bounds, ownPose, otherPose);
                return true;
            }

            if (kind == MachineNodeKind.Funnel)
            {
                if (otherKind == MachineNodeKind.Buffer)
                {
                    // A crate has four equivalent faces. Using the authored
                    // one-sided crate marker here would recreate the old bug:
                    // one side looks right while the other three overlap or gap.
                    // The physical exterior of both assets is the contract.
                    if (!TryCalculatePhysicalBounds(root, out var bounds))
                    {
                        point = root.position;
                        return false;
                    }

                    point = BoundsFaceToward(bounds, ownPose, otherPose);
                    return true;
                }

                if (otherKind == MachineNodeKind.Machine)
                {
                    // Contro una macchina il contatto è il lato inventario, che
                    // è la morsa destinata a entrare nell'incastro — non il lato
                    // nastro. Allineando per PORT_Belt si prende l'estremità
                    // opposta dell'Imbuto e lo si spinge dentro la macchina per
                    // tutta la sua lunghezza.
                    var inventory = FindNamed(root, "PORT_Inventory");
                    point = inventory != null ? inventory.position : root.position;
                    return inventory != null;
                }

                var marker = FindNamed(root, "PORT_Belt");
                point = marker != null ? marker.position : root.position;
                return marker != null;
            }

            ResolveNodeSockets(root, kind, out var input, out var output);
            if (input == null || output == null)
            {
                point = root.position;
                return false;
            }

            var inputDistance = HorizontalSqrDistance(
                input.position,
                Position(otherPose));
            var outputDistance = HorizontalSqrDistance(
                output.position,
                Position(otherPose));
            point = inputDistance <= outputDistance
                ? input.position
                : output.position;
            return true;
        }

        private bool HasPhysicalOverlap(
            StableId attachmentTargetId,
            out string blockerName)
        {
            blockerName = string.Empty;
            if (!TryCalculatePhysicalBounds(
                    _nodePreview.transform,
                    out var previewBounds))
            {
                return false;
            }

            var anchors = FindObjectsByType<FactoryNodeAnchor>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var index = 0; index < anchors.Length; index++)
            {
                var anchor = anchors[index];
                if (anchor == null
                    || anchor.transform.IsChildOf(_nodePreview.transform)
                    || anchor.NodeId == attachmentTargetId
                    || !TryCalculatePhysicalBounds(
                        anchor.transform,
                        out var existingBounds))
                {
                    continue;
                }

                if (IntersectsWithVolume(previewBounds, existingBounds))
                {
                    blockerName = anchor.gameObject.name;
                    return true;
                }
            }

            var colliders = Physics.OverlapBox(
                previewBounds.center,
                previewBounds.extents,
                Quaternion.identity,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null
                    || collider.GetComponentInParent<FactoryBuildSurface>() != null
                    || collider.GetComponentInParent<FactoryNodeAnchor>() != null
                    || collider.transform.IsChildOf(_nodePreview.transform)
                    || (playerCamera != null
                        && collider.transform.IsChildOf(
                            playerCamera.transform.root)))
                {
                    continue;
                }

                if (IntersectsWithVolume(previewBounds, collider.bounds))
                {
                    blockerName = collider.gameObject.name;
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsWithVolume(Bounds left, Bounds right)
        {
            var overlapX = Mathf.Min(left.max.x, right.max.x)
                - Mathf.Max(left.min.x, right.min.x);
            var overlapY = Mathf.Min(left.max.y, right.max.y)
                - Mathf.Max(left.min.y, right.min.y);
            var overlapZ = Mathf.Min(left.max.z, right.max.z)
                - Mathf.Max(left.min.z, right.min.z);
            return overlapX > IntersectionToleranceMetres
                && overlapY > IntersectionToleranceMetres
                && overlapZ > IntersectionToleranceMetres;
        }

        private static bool TryCalculatePhysicalBounds(
            Transform root,
            out Bounds bounds)
        {
            var found = false;
            bounds = default;
            var boxColliders = root.GetComponentsInChildren<BoxCollider>(true);
            for (var colliderIndex = 0;
                 colliderIndex < boxColliders.Length;
                 colliderIndex++)
            {
                var collider = boxColliders[colliderIndex];
                var half = collider.size * 0.5f;
                for (var x = -1; x <= 1; x += 2)
                {
                    for (var y = -1; y <= 1; y += 2)
                    {
                        for (var z = -1; z <= 1; z += 2)
                        {
                            var localCorner = collider.center
                                + Vector3.Scale(
                                    half,
                                    new Vector3(x, y, z));
                            var worldCorner =
                                collider.transform.TransformPoint(localCorner);
                            if (!found)
                            {
                                bounds = new Bounds(
                                    worldCorner,
                                    Vector3.zero);
                                found = true;
                            }
                            else
                            {
                                bounds.Encapsulate(worldCorner);
                            }
                        }
                    }
                }
            }

            if (found)
            {
                return true;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                if (!renderers[index].enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderers[index].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
            }

            return found;
        }

        private static Vector3 BoundsFaceToward(
            Bounds bounds,
            MachineBuildPose source,
            MachineBuildPose target)
        {
            var point = bounds.center;
            var deltaX = target.XMillimetres - source.XMillimetres;
            var deltaZ = target.ZMillimetres - source.ZMillimetres;
            if (Math.Abs(deltaX) >= Math.Abs(deltaZ))
            {
                point.x = deltaX >= 0 ? bounds.max.x : bounds.min.x;
            }
            else
            {
                point.z = deltaZ >= 0 ? bounds.max.z : bounds.min.z;
            }

            return point;
        }

        private bool TryFindAnchor(
            StableId id,
            out FactoryNodeAnchor found)
        {
            var anchors = FindObjectsByType<FactoryNodeAnchor>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var index = 0; index < anchors.Length; index++)
            {
                if (anchors[index].NodeId == id)
                {
                    found = anchors[index];
                    return true;
                }
            }

            found = null;
            return false;
        }

        private bool TryRaycastPlacement(
            out RaycastHit surfaceHit,
            out FactoryNodeAnchor pointedNode,
            out RaycastHit pointedHit)
        {
            var ray = playerCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f));
            var hits = Physics.RaycastAll(
                ray,
                MaximumRayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(
                hits,
                (left, right) => left.distance.CompareTo(right.distance));

            pointedNode = null;
            pointedHit = default;
            for (var index = 0; index < hits.Length; index++)
            {
                var hit = hits[index];
                var node =
                    hit.collider.GetComponentInParent<FactoryNodeAnchor>();
                if (pointedNode == null && node != null)
                {
                    pointedNode = node;
                    pointedHit = hit;
                    continue;
                }

                if (hit.collider.GetComponentInParent<FactoryBuildSurface>()
                    != null)
                {
                    surfaceHit = hit;
                    return true;
                }
            }

            if (pointedNode != null)
            {
                // Looking straight at the side of a machine need not also hit the
                // floor behind it. The target already supplies the authoritative
                // build height; logical target snapping below will choose the cell.
                surfaceHit = pointedHit;
                if (simulationRoot.Engine.State.GetMachineSnapshot().TryGetNode(
                    pointedNode.NodeId,
                    out var target)
                    && target.HasPlacementPose)
                {
                    var point = surfaceHit.point;
                    point.y = target.PlacementPose.YMillimetres / 1_000f;
                    surfaceHit.point = point;
                }

                return true;
            }

            surfaceHit = default;
            pointedNode = null;
            pointedHit = default;
            return false;
        }

        private static byte ResolveAimedSide(
            Transform target,
            Vector3 worldHitPoint)
        {
            var delta = worldHitPoint - target.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0001f)
            {
                delta = -target.forward;
            }

            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.z))
            {
                return delta.x >= 0f ? (byte)1 : (byte)3;
            }

            return delta.z >= 0f ? (byte)0 : (byte)2;
        }

        private void CommitPlacement()
        {
            var acceptance = simulationRoot.SubmitBuild(_previewSpecification);
            _pending.Add(
                new PendingBuild(
                    acceptance.Command,
                    _previewSpecification,
                    _previewVisualPosition,
                    _previewRotation));
            ClearPreview();
            _status = "Piazzamento inviato";
        }

        private void HandleStateCommitted(SimulationEngine engine)
        {
            for (var index = _pending.Count - 1; index >= 0; index--)
            {
                var pending = _pending[index];
                if (simulationRoot.TryResolveCreatedEntity(
                    pending.Command,
                    out var createdId))
                {
                    InstantiateCommitted(pending, createdId);
                    _pending.RemoveAt(index);
                    _status = "Piazzato";
                    continue;
                }

                if (engine.State.Tick >= pending.Command.TargetTick)
                {
                    _pending.RemoveAt(index);
                    _status = "Piazzamento rifiutato";
                }
            }
        }

        private void InstantiateCommitted(PendingBuild pending, StableId createdId)
        {
            var prefab = PrefabFor(pending.Specification);
            if (prefab == null)
            {
                Debug.LogError(
                    $"No prefab configured for {pending.Specification.Kind}.",
                    this);
                return;
            }

            var parent = builtObjectsRoot != null
                ? builtObjectsRoot
                : transform;
            var instance = Instantiate(
                prefab,
                pending.Position,
                pending.VisualRotation,
                parent);
            instance.hideFlags = HideFlags.None;
            instance.SetActive(true);
            instance.name = $"{prefab.name}_{createdId.Low:x}";
            var kind = NodeKindFor(pending.Specification.Kind);
            // The committed object must expose the exact same physical ports as
            // its hologram. Passing no definition here silently turned every
            // built Curve back into a straight belt: the preview used the side
            // exit, while the real object exposed an output in front.
            EnsureStandardSockets(
                instance.transform,
                kind,
                pending.Specification.PrimaryId);
            ResolveNodeSockets(
                instance.transform,
                kind,
                out var inputSocket,
                out var outputSocket);

            var anchor = instance.GetComponent<FactoryNodeAnchor>()
                ?? instance.AddComponent<FactoryNodeAnchor>();
            anchor.Configure(createdId, kind, inputSocket, outputSocket);

            if (kind == MachineNodeKind.Buffer
                || kind == MachineNodeKind.Machine)
            {
                var interaction =
                    instance.GetComponent<FactoryInteractionTarget>()
                    ?? instance.AddComponent<FactoryInteractionTarget>();
                interaction.Configure(
                    createdId,
                    kind == MachineNodeKind.Buffer
                        ? FactoryInteractionKind.Chest
                        : FactoryInteractionKind.Machine,
                    kind == MachineNodeKind.Buffer
                        ? "Apri Cassa di legno"
                        : pending.Specification.PrimaryId == ContentIds.CrudeFurnace
                            ? "Usa Fornace"
                            : pending.Specification.PrimaryId == ContentIds.MechanicalDrill
                                ? "Usa Estrattore"
                                : "Ispeziona Pressa");
            }

            if (kind == MachineNodeKind.Machine
                && pending.Specification.PrimaryId == ContentIds.MechanicalPress)
            {
                var presenter = instance.GetComponent<FactoryPressPresenter>()
                    ?? instance.AddComponent<FactoryPressPresenter>();
                presenter.Configure(
                    simulationRoot.Engine,
                    simulationRoot.Catalog,
                    createdId,
                    FindByPrefix(instance.transform, "ANM_PressRam"),
                    FindByPrefix(instance.transform, "REF_Workpiece"),
                    ironIngotPrefab,
                    ironPlatePrefab);
            }
            else if (kind == MachineNodeKind.BeltModule
                     || kind == MachineNodeKind.Funnel)
            {
                ConfigureLogisticsPresenter(instance, createdId, kind);
            }

        }

        private void ConfigureLogisticsPresenter(
            GameObject instance,
            StableId nodeId,
            MachineNodeKind kind)
        {
            var presenter =
                instance.GetComponent<FactoryLogisticsModulePresenter>()
                ?? instance.AddComponent<FactoryLogisticsModulePresenter>();
            presenter.Configure(
                simulationRoot.Engine,
                nodeId,
                kind,
                ironIngotPrefab,
                ironPlatePrefab);
        }

        private void EnsureNodePreview(GameObject prefab)
        {
            if (_nodePreview != null
                && _nodePreview.name.StartsWith(
                    $"Hologram_{prefab.name}",
                    StringComparison.Ordinal))
            {
                return;
            }

            if (_nodePreview != null)
            {
                Destroy(_nodePreview);
            }

            _nodePreview = Instantiate(prefab, transform);
            _nodePreview.hideFlags = HideFlags.None;
            _nodePreview.SetActive(true);
            _nodePreview.name = $"Hologram_{prefab.name}";
            DisableGameplayComponents(_nodePreview);
        }

        private void DisableGameplayComponents(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            foreach (var collider in
                     instance.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }

            foreach (var behaviour in
                     instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Unity includes null entries here when a prefab contains a missing
                // script. A preview must ignore that incomplete component instead of
                // aborting the whole placement path.
                if (behaviour != null && behaviour != this)
                {
                    behaviour.enabled = false;
                }
            }
        }

        private void ApplyHologramMaterial(GameObject target, bool valid)
        {
            var material = valid
                ? validHologramMaterial
                : invalidHologramMaterial;
            if (target == null || material == null)
            {
                return;
            }

            foreach (var renderer in
                     target.GetComponentsInChildren<Renderer>(true))
            {
                var materials =
                    new Material[renderer.sharedMaterials.Length];
                for (var index = 0; index < materials.Length; index++)
                {
                    materials[index] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private GameObject PrefabFor(BuildSelection selection)
        {
            switch (selection)
            {
                case BuildSelection.Crate:
                    return cratePrefab;
                case BuildSelection.Funnel:
                    return funnelPrefab;
                case BuildSelection.Belt:
                    return beltStraightPrefab;
                case BuildSelection.Press:
                    return pressPrefab;
                case BuildSelection.Furnace:
                    ResolveFurnaceTemplate();
                    return furnacePrefab;
                case BuildSelection.Drill:
                    ResolveDrillTemplate();
                    return drillPrefab;
                case BuildSelection.DriveBelt:
                    return beltDrivePrefab;
                case BuildSelection.Curve:
                    return ResolveCurveVisualPrefab(
                        ContentIds.BeltCurve,
                        beltCurvePrefab,
                        beltCurveLeftPrefab);
                case BuildSelection.Incline:
                    return beltInclinePrefab;
                case BuildSelection.CurveLeft:
                    return ResolveCurveVisualPrefab(
                        ContentIds.BeltCurveLeft,
                        beltCurvePrefab,
                        beltCurveLeftPrefab);
                default:
                    return null;
            }
        }

        private GameObject PrefabFor(MachineBuildKind kind)
        {
            switch (kind)
            {
                case MachineBuildKind.Buffer:
                    return cratePrefab;
                case MachineBuildKind.Machine:
                    return pressPrefab;
                case MachineBuildKind.Funnel:
                    return funnelPrefab;
                case MachineBuildKind.BeltModule:
                    return beltStraightPrefab;
                default:
                    return null;
            }
        }

        private GameObject PrefabFor(MachineBuildSpecification specification)
        {
            if (specification.Kind == MachineBuildKind.BeltModule
                && specification.PrimaryId == ContentIds.BeltDriveUnit)
            {
                return beltDrivePrefab;
            }

            if (specification.Kind == MachineBuildKind.BeltModule
                && specification.PrimaryId == ContentIds.BeltCurve)
            {
                return ResolveCurveVisualPrefab(
                    specification.PrimaryId,
                    beltCurvePrefab,
                    beltCurveLeftPrefab);
            }

            if (specification.Kind == MachineBuildKind.BeltModule
                && specification.PrimaryId == ContentIds.BeltIncline)
            {
                return beltInclinePrefab;
            }

            if (specification.Kind == MachineBuildKind.BeltModule
                && specification.PrimaryId == ContentIds.BeltCurveLeft)
            {
                return ResolveCurveVisualPrefab(
                    specification.PrimaryId,
                    beltCurvePrefab,
                    beltCurveLeftPrefab);
            }

            if (specification.Kind == MachineBuildKind.Machine
                && specification.PrimaryId == ContentIds.CrudeFurnace)
            {
                ResolveFurnaceTemplate();
                return furnacePrefab;
            }

            if (specification.Kind == MachineBuildKind.Machine
                && specification.PrimaryId == ContentIds.MechanicalDrill)
            {
                ResolveDrillTemplate();
                return drillPrefab;
            }

            return PrefabFor(specification.Kind);
        }

        /// <summary>
        /// Restituisce la geometria che possiede davvero la manualità dichiarata
        /// dalla definizione.
        ///
        /// Il generatore artistico attuale ha esportato i due nomi al contrario:
        /// MEC_Belt_Curve/PF_Belt_Curve entra da -Z ed esce da -X, mentre la
        /// variante chiamata CurveLeft esce da +X. La simulazione, correttamente,
        /// definisce BeltCurve come svolta a destra (+X) e BeltCurveLeft come
        /// svolta a sinistra (-X). Se si lega il prefab in base al nome del file,
        /// la mesh mostra un'uscita e la topologia/potenza ne cercano un'altra.
        ///
        /// Il binding resta in un unico punto verificabile, anziché compensare
        /// l'errore con offset diversi nel preview, nell'oggetto costruito e nella
        /// propagazione della potenza.
        /// </summary>
        public static GameObject ResolveCurveVisualPrefab(
            StableId definitionId,
            GameObject exportedCurvePrefab,
            GameObject exportedCurveLeftPrefab)
        {
            if (definitionId == ContentIds.BeltCurve)
            {
                return exportedCurveLeftPrefab;
            }

            if (definitionId == ContentIds.BeltCurveLeft)
            {
                return exportedCurvePrefab;
            }

            return null;
        }

        private MachineBuildSpecification SpecificationFor(
            BuildSelection selection,
            MachineBuildPose pose)
        {
            switch (selection)
            {
                case BuildSelection.Crate:
                    return MachineBuildSpecification.Buffer(
                        ContentIds.WoodenCrate,
                        ContentIds.WoodenCrateItem,
                        1L,
                        pose);
                case BuildSelection.Funnel:
                    return MachineBuildSpecification.Funnel(
                        ContentIds.BeltFunnel,
                        pose);
                case BuildSelection.Belt:
                    return MachineBuildSpecification.BeltModule(
                        ContentIds.BeltStraight,
                        pose,
                        BeltTravelDirection.Stopped);
                case BuildSelection.Press:
                    return MachineBuildSpecification.Machine(
                        ContentIds.MechanicalPress,
                        ContentIds.PressIronPlate,
                        ContentIds.MechanicalPressItem,
                        1L,
                        pose);
                case BuildSelection.Furnace:
                    return MachineBuildSpecification.Machine(
                        ContentIds.CrudeFurnace,
                        ContentIds.SmeltIronIngot,
                        ContentIds.CrudeFurnaceItem,
                        1L,
                        pose);
                case BuildSelection.Drill:
                    // È l'unico piazzabile la cui ricetta non è fissa: la sceglie
                    // il giacimento sotto la macchina. Quando non ce n'è uno la
                    // specifica resta senza ricetta e il preflight autorevole la
                    // rifiuta, quindi l'ologramma diventa rosso da solo.
                    MechanicalDrillPlacement.TryResolveExtraction(
                        simulationRoot.Catalog,
                        Position(pose),
                        out var extractionRecipeId,
                        out _drillPlacementNote);
                    return MachineBuildSpecification.Machine(
                        ContentIds.MechanicalDrill,
                        extractionRecipeId,
                        ContentIds.MechanicalDrillItem,
                        1L,
                        pose);
                case BuildSelection.DriveBelt:
                    return MachineBuildSpecification.BeltModule(
                        ContentIds.BeltDriveUnit,
                        pose,
                        BeltTravelDirection.Stopped);
                case BuildSelection.Curve:
                    // La curva è un modulo nastro come gli altri: l'adiacenza si
                    // deriva dalla posa quantizzata, quindi la svolta di 90° è
                    // geometria e non cambia la topologia vista dalla macchina.
                    return MachineBuildSpecification.BeltModule(
                        ContentIds.BeltCurve,
                        pose,
                        BeltTravelDirection.Stopped);
                case BuildSelection.Incline:
                    // Idem per la salita: il dislivello vive nella posa, che
                    // porta già la quota in millimetri.
                    return MachineBuildSpecification.BeltModule(
                        ContentIds.BeltIncline,
                        pose,
                        BeltTravelDirection.Stopped);
                case BuildSelection.CurveLeft:
                    return MachineBuildSpecification.BeltModule(
                        ContentIds.BeltCurveLeft,
                        pose,
                        BeltTravelDirection.Stopped);
                default:
                    throw new ArgumentOutOfRangeException(nameof(selection));
            }
        }

        /// <summary>
        /// Definizione del modulo che si sta tenendo in mano. Serve a sapere se
        /// svolta o se sale, cose che la sola posa non dice.
        /// </summary>
        private static StableId DefinitionIdFor(BuildSelection selection)
        {
            switch (selection)
            {
                case BuildSelection.Crate:
                    return ContentIds.WoodenCrate;
                case BuildSelection.Funnel:
                    return ContentIds.BeltFunnel;
                case BuildSelection.Belt:
                    return ContentIds.BeltStraight;
                case BuildSelection.Press:
                    return ContentIds.MechanicalPress;
                case BuildSelection.Furnace:
                    return ContentIds.CrudeFurnace;
                case BuildSelection.Drill:
                    return ContentIds.MechanicalDrill;
                case BuildSelection.DriveBelt:
                    return ContentIds.BeltDriveUnit;
                case BuildSelection.Curve:
                    return ContentIds.BeltCurve;
                case BuildSelection.Incline:
                    return ContentIds.BeltIncline;
                case BuildSelection.CurveLeft:
                    return ContentIds.BeltCurveLeft;
                default:
                    return default;
            }
        }

        private static MachineNodeKind NodeKindFor(MachineBuildKind kind)
        {
            switch (kind)
            {
                case MachineBuildKind.Buffer:
                    return MachineNodeKind.Buffer;
                case MachineBuildKind.Machine:
                    return MachineNodeKind.Machine;
                case MachineBuildKind.Funnel:
                    return MachineNodeKind.Funnel;
                case MachineBuildKind.BeltModule:
                    return MachineNodeKind.BeltModule;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static MachineBuildKind BuildKindFor(
            BuildSelection selection)
        {
            switch (selection)
            {
                case BuildSelection.Crate:
                    return MachineBuildKind.Buffer;
                case BuildSelection.Funnel:
                    return MachineBuildKind.Funnel;
                case BuildSelection.Belt:
                    return MachineBuildKind.BeltModule;
                case BuildSelection.Press:
                case BuildSelection.Furnace:
                case BuildSelection.Drill:
                    return MachineBuildKind.Machine;
                case BuildSelection.DriveBelt:
                    return MachineBuildKind.BeltModule;
                case BuildSelection.Curve:
                    return MachineBuildKind.BeltModule;
                case BuildSelection.Incline:
                    return MachineBuildKind.BeltModule;
                case BuildSelection.CurveLeft:
                    return MachineBuildKind.BeltModule;
                default:
                    throw new ArgumentOutOfRangeException(nameof(selection));
            }
        }

        private static void ResolveNodeSockets(
            Transform root,
            MachineNodeKind kind,
            out Transform input,
            out Transform output)
        {
            switch (kind)
            {
                case MachineNodeKind.Funnel:
                    input = FindNamed(root, "PORT_Inventory") ?? root;
                    output = FindNamed(root, "PORT_Belt") ?? root;
                    return;
                case MachineNodeKind.Buffer:
                    input = FindNamed(root, "PORT_ItemIO") ?? root;
                    output = input;
                    return;
                case MachineNodeKind.BeltModule:
                    input = FindNamed(root, "PORT_ModuleInput") ?? root;
                    output = FindNamed(root, "PORT_ModuleOutput") ?? root;
                    return;
                default:
                    input = FindNamed(root, "PORT_ItemIn")
                        ?? FindNamed(root, "PORT_ItemIO");
                    output = FindNamed(root, "PORT_ItemOut")
                        ?? FindNamed(root, "PORT_ItemIO");
                    if (input != null && output != null)
                    {
                        return;
                    }

                    // La Pressa non porta marcatori di porta, e il ripiego era
                    // la radice: allineare un nastro al *centro* di una macchina
                    // larga quasi due metri dà un aggancio senza senso, che è il
                    // motivo per cui un modulo dopo la Pressa finiva sempre fuori
                    // asse. In mancanza di marcatori autorevoli si usano i bordi
                    // della cella, la stessa convenzione dei moduli nastro.
                    input = CreateSocketIfMissing(
                        root,
                        "PORT_ItemIn",
                        new Vector3(0f, 0.60f, -0.50f));
                    output = CreateSocketIfMissing(
                        root,
                        "PORT_ItemOut",
                        new Vector3(0f, 0.60f, 0.50f));
                    return;
            }
        }

        private static Transform FindSocket(
            Transform root,
            string preferred,
            string fallback) =>
            FindNamed(root, preferred)
            ?? FindNamed(root, fallback)
            ?? root;

        private static Transform FindByPrefix(Transform root, string prefix)
        {
            foreach (var child in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return root;
        }

        private static void EnsureStandardSockets(
            Transform root,
            MachineNodeKind kind)
        {
            EnsureStandardSockets(root, kind, default);
        }

        /// <summary>
        /// Socket del preview. L'uscita sta sul bordo da cui il modulo scarica
        /// davvero: in fondo per un rettilineo, di fianco per una Curva.
        ///
        /// Erano fissi a (0, ±0.5), cioè le due testate di un modulo dritto.
        /// Con una Curva l'allineamento usava quindi un punto di contatto che
        /// sul modello non esiste e la spingeva dentro il vicino — il preview
        /// diventava rosso per compenetrazione, non perché non fosse adiacente.
        /// </summary>
        private static void EnsureStandardSockets(
            Transform root,
            MachineNodeKind kind,
            StableId definitionId)
        {
            if (kind != MachineNodeKind.BeltModule)
            {
                return;
            }

            CreateSocketIfMissing(
                root,
                "PORT_ModuleInput",
                new Vector3(0f, 0.60f, -0.50f));

            var exit = new Vector3(0f, 0.60f, 0.50f);
            switch (BeltModuleShape.TurnQuarterTurns(definitionId))
            {
                case 1:
                    exit = new Vector3(0.50f, 0.60f, 0f);
                    break;
                case 3:
                    exit = new Vector3(-0.50f, 0.60f, 0f);
                    break;
            }

            exit.y += BeltModuleShape.RiseMillimetres(definitionId) / 1000f;
            CreateSocketIfMissing(root, "PORT_ModuleOutput", exit);
        }

        private static Transform CreateSocketIfMissing(
            Transform root,
            string name,
            Vector3 localPosition)
        {
            var existing = FindNamed(root, name);
            if (existing != null)
            {
                return existing;
            }

            var socket = new GameObject(name).transform;
            socket.SetParent(root, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            return socket;
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
            if (_subscribedRoot != null && isActiveAndEnabled)
            {
                _subscribedRoot.StateCommitted += HandleStateCommitted;
            }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (var child in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(
                    child.name,
                    name,
                    StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private void ClearPreview()
        {
            _previewValid = false;
            if (_nodePreview != null)
            {
                Destroy(_nodePreview);
                _nodePreview = null;
            }
        }

        private void SetNodePreviewVisible(bool visible)
        {
            if (_nodePreview != null)
            {
                _nodePreview.SetActive(visible);
            }
        }

        private static Vector3 Snap(Vector3 point) =>
            new Vector3(
                Mathf.Round(point.x / GridSizeMetres) * GridSizeMetres,
                point.y,
                Mathf.Round(point.z / GridSizeMetres) * GridSizeMetres);

        private static MachineBuildPose Pose(Vector3 position, byte yaw) =>
            new MachineBuildPose(
                Mathf.RoundToInt(position.x * 1_000f),
                Mathf.RoundToInt(position.y * 1_000f),
                Mathf.RoundToInt(position.z * 1_000f),
                yaw);

        private static Vector3 Position(MachineBuildPose pose) =>
            new Vector3(
                pose.XMillimetres / 1_000f,
                pose.YMillimetres / 1_000f,
                pose.ZMillimetres / 1_000f);

        private static Quaternion VisualRotation(
            BuildSelection selection,
            byte yaw)
        {
            var rotation = Quaternion.Euler(0f, yaw * 90f, 0f);
            switch (selection)
            {
                case BuildSelection.Funnel:
                    return rotation
                        * Quaternion.Euler(
                            0f,
                            FunnelVisualYawOffsetDegrees,
                            0f);

                case BuildSelection.Incline:
                    // The authored mesh names +Z as "In" (the low end) and
                    // -Z as "Out" (the high end). A build pose, instead, names
                    // +Z as the forward/output direction at yaw zero, exactly
                    // like Straight and the logical belt graph. Without this
                    // single visual binding correction, aiming at the visible
                    // high end selected the graph's low end: the following
                    // incline stayed on the neighbouring ground cell instead
                    // of gaining InclineRiseMillimetres.
                    return rotation
                        * Quaternion.Euler(
                            0f,
                            InclineVisualYawOffsetDegrees,
                            0f);

                default:
                    return rotation;
            }
        }

        private static float HorizontalSqrDistance(
            Vector3 left,
            Vector3 right)
        {
            var x = left.x - right.x;
            var z = left.z - right.z;
            return x * x + z * z;
        }

        private static bool TryResolveSelection(
            StableId itemId,
            out BuildSelection selection)
        {
            if (itemId == ContentIds.WoodenCrateItem)
            {
                selection = BuildSelection.Crate;
                return true;
            }

            if (itemId == ContentIds.BeltFunnel)
            {
                selection = BuildSelection.Funnel;
                return true;
            }

            if (itemId == ContentIds.BeltStraight)
            {
                selection = BuildSelection.Belt;
                return true;
            }

            if (itemId == ContentIds.MechanicalPressItem)
            {
                selection = BuildSelection.Press;
                return true;
            }

            if (itemId == ContentIds.CrudeFurnaceItem)
            {
                selection = BuildSelection.Furnace;
                return true;
            }

            if (itemId == ContentIds.MechanicalDrillItem)
            {
                selection = BuildSelection.Drill;
                return true;
            }

            if (itemId == ContentIds.BeltDriveUnit)
            {
                selection = BuildSelection.DriveBelt;
                return true;
            }

            if (itemId == ContentIds.BeltCurve)
            {
                selection = BuildSelection.Curve;
                return true;
            }

            if (itemId == ContentIds.BeltIncline)
            {
                selection = BuildSelection.Incline;
                return true;
            }

            if (itemId == ContentIds.BeltCurveLeft)
            {
                selection = BuildSelection.CurveLeft;
                return true;
            }

            selection = default;
            return false;
        }

        private static string DescribeRejection(
            MachineSimulationState state,
            MachineBuildPose pose,
            CommandRejectionReason rejection)
        {
            switch (rejection)
            {
                case CommandRejectionReason.BuildTopologyInvalid:
                    return FactoryBuildPlacementResolver.TryGetOccupant(
                        state,
                        pose,
                        out var occupant)
                        ? $"Cella occupata da {DescribeKind(occupant.Kind)}"
                        : "Questa macchina non accetta la ricetta";
                case CommandRejectionReason.InsufficientQuantity:
                case CommandRejectionReason.BuildSourceMissing:
                    return "Non hai più questo oggetto nell'inventario";
                case CommandRejectionReason.BuildMalformed:
                case CommandRejectionReason.BuildDefinitionMissing:
                    return "Oggetto non costruibile: definizione mancante";
                default:
                    return $"Piazzamento rifiutato ({rejection})";
            }
        }

        private static string DescribeKind(MachineNodeKind kind)
        {
            switch (kind)
            {
                case MachineNodeKind.Buffer:
                    return "una cassa";
                case MachineNodeKind.Machine:
                    return "una macchina";
                case MachineNodeKind.Funnel:
                    return "un imbuto";
                case MachineNodeKind.BeltModule:
                    return "un nastro";
                default:
                    return "qualcosa";
            }
        }

        private static string SelectionInstruction(BuildSelection selection) =>
            $"{SelectionName(selection)} selezionato · click per piazzare · R per ruotare";

        private void OnGUI()
        {
            if (!_hasPlaceableHeld)
            {
                return;
            }

            var centreX = Screen.width * 0.5f;
            var centreY = Screen.height * 0.5f;
            var headline = _previewValid
                ? $"{SelectionName(_selection)} x{_heldQuantity}"
                : _status;
            DrawCenteredBuildText(
                new Rect(centreX - 330f, centreY - 82f, 660f, 30f),
                headline.ToUpperInvariant(),
                17,
                _previewValid
                    ? new Color(0.97f, 0.98f, 0.94f, 0.96f)
                    : new Color(1f, 0.72f, 0.48f, 0.98f),
                FontStyle.Bold);

            var controls = _previewValid
                ? "CLICK SINISTRO  PIAZZA   ·   R  RUOTA"
                : "R  RUOTA";
            DrawCenteredBuildText(
                new Rect(centreX - 330f, centreY + 68f, 660f, 28f),
                controls,
                15,
                new Color(0.97f, 0.98f, 0.94f, 0.92f),
                FontStyle.Bold);
        }

        private static void DrawCenteredBuildText(
            Rect rect,
            string text,
            int fontSize,
            Color colour,
            FontStyle fontStyle)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = fontStyle
            };
            var shadowRect = new Rect(
                rect.x + 1.5f,
                rect.y + 1.5f,
                rect.width,
                rect.height);
            style.normal.textColor = new Color(0.02f, 0.025f, 0.02f, 0.68f);
            GUI.Label(shadowRect, text, style);
            style.normal.textColor = colour;
            GUI.Label(rect, text, style);
        }

        private static string SelectionName(BuildSelection selection)
        {
            switch (selection)
            {
                case BuildSelection.Crate:
                    return "Cassa di legno";
                case BuildSelection.Funnel:
                    return "Imbuto";
                case BuildSelection.Belt:
                    return "Nastro trasportatore";
                case BuildSelection.Press:
                    return "Pressa meccanica";
                case BuildSelection.Furnace:
                    return "Fornace rudimentale";
                case BuildSelection.DriveBelt:
                    return "Nastro motore";
                default:
                    return string.Empty;
            }
        }

        private enum BuildSelection
        {
            Crate,
            Funnel,
            Belt,
            Press,
            Furnace,
            DriveBelt,
            Curve,
            Incline,
            CurveLeft,
            Drill
        }

        /// <summary>
        /// La Trivella non ha un esemplare in scena da cui ricavare il template,
        /// come fa invece la Fornace: si costruisce dal nulla su un giacimento.
        /// Il prefab viene quindi caricato da Resources la prima volta che
        /// serve, così non esiste un riferimento da assegnare a mano che possa
        /// restare vuoto.
        /// </summary>
        private void ResolveDrillTemplate()
        {
            if (drillPrefab != null)
            {
                return;
            }

            drillPrefab = Resources.Load<GameObject>(DrillPrefabResourcePath);
            if (drillPrefab == null)
            {
                Debug.LogError(
                    $"Il prefab della Trivella non è stato trovato in Resources/{DrillPrefabResourcePath}. " +
                    "Eseguire CML/Art/Rebuild Mechanical Drill.",
                    this);
            }
        }

        private void ResolveFurnaceTemplate()
        {
            if (furnacePrefab != null)
            {
                return;
            }

            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate.gameObject.scene != gameObject.scene
                    || !string.Equals(
                        candidate.name,
                        "ENV_StarterProp_01_PF_CrudeFurnace",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                furnacePrefab = Instantiate(candidate.gameObject, transform);
                furnacePrefab.name = "RuntimeTemplate_PF_CrudeFurnace";
                furnacePrefab.hideFlags = HideFlags.HideAndDontSave;
                furnacePrefab.SetActive(false);
                return;
            }
        }

        private readonly struct PendingBuild
        {
            public PendingBuild(
                SimulationCommand command,
                MachineBuildSpecification specification,
                Vector3 position,
                Quaternion visualRotation)
            {
                Command = command;
                Specification = specification;
                Position = position;
                VisualRotation = visualRotation;
            }

            public SimulationCommand Command { get; }

            public MachineBuildSpecification Specification { get; }

            public Vector3 Position { get; }

            public Quaternion VisualRotation { get; }
        }
    }

    /// <summary>
    /// Pure logical placement contract. It never checks whether two modules are
    /// currently useful and never assigns source/destination roles. It only chooses
    /// an adjacent cell and a physical axis.
    /// </summary>
    public static class FactoryBuildPlacementResolver
    {
        /// <summary>
        /// Allinea visivamente due moduli nastro usando il bordo comune delle
        /// rispettive celle. I socket restano utili per animazioni e carico, ma
        /// non possono trascinare mezzo modulo dentro una Curva se sono assenti
        /// o provengono da una vecchia istanza.
        /// </summary>
        public static Vector3 ResolveBeltVisualPosition(
            Vector3 previewRootPosition,
            MachineBuildPose previewPose,
            Vector3 targetRootPosition,
            MachineBuildPose targetPose)
        {
            var previewContact = BeltCellEdgeToward(
                previewRootPosition,
                previewPose,
                targetPose);
            var targetContact = BeltCellEdgeToward(
                targetRootPosition,
                targetPose,
                previewPose);
            var delta = targetContact - previewContact;
            delta.y = 0f;
            return previewRootPosition + delta;
        }

        public static MachineBuildPose ResolveFromTarget(
            MachineSimulationState state,
            StableId targetId,
            MachineBuildPose desired,
            MachineBuildKind heldKind,
            byte aimedSideYaw,
            byte heldYaw,
            bool yawExplicitlyRotated,
            StableId heldDefinitionId = default)
        {
            if (state == null
                || targetId.IsNone
                || !state.TryGetNode(targetId, out var target)
                || !target.HasPlacementPose)
            {
                return desired;
            }

            var side = (byte)(aimedSideYaw & 3);
            var yaw = (byte)(heldYaw & 3);
            var targetBeltEntryYaw = target.PlacementPose.YawQuarterTurns;
            var targetBeltExitYaw = BeltModuleShape.ForwardExitYaw(
                target.DefinitionId,
                target.PlacementPose.YawQuarterTurns);
            var targetBeltEntrySide = Opposite(targetBeltEntryYaw);
            if (target.Kind == MachineNodeKind.BeltModule
                && BeltModuleShape.TryResolveTravelYaws(
                    target.DefinitionId,
                    target.PlacementPose.YawQuarterTurns,
                    target.BeltTravelDirection,
                    out targetBeltEntryYaw,
                    out targetBeltExitYaw))
            {
                // entryYaw is the direction of travel *into* the module; the
                // physical side on which its predecessor sits is the opposite.
                targetBeltEntrySide = Opposite(targetBeltEntryYaw);
            }

            if (target.Kind == MachineNodeKind.Buffer)
            {
                // A crate is directionless: its four faces are equivalent and none
                // of them is an input or an output. R is therefore the only way the
                // player can name the face they want, so it steers which side the
                // piece lands on and not merely the piece's own spin.
                //
                // Only a Funnel used to get this treatment. That is why a belt
                // could not be made to leave the side of the crate the player was
                // looking at: R turned the belt on the spot while the face stayed
                // wherever the crosshair had happened to land.
                side = yawExplicitlyRotated ? yaw : side;

                // The selected face is honoured even when it is taken. Hunting for a
                // free side instead would be the building system overriding a choice
                // the player made, so an occupied face turns the preview red and
                // OccupiedCrateFaceIsRejectedInsteadOfMovingTheFunnelElsewhere holds
                // that line.
                return Offset(target.PlacementPose, side, side);
            }

            if (heldKind == MachineBuildKind.Buffer
                && target.Kind == MachineNodeKind.Funnel)
            {
                side = Opposite(target.PlacementPose.YawQuarterTurns);
            }
            else if (heldKind == MachineBuildKind.BeltModule
                     && target.Kind == MachineNodeKind.Funnel)
            {
                side = target.PlacementPose.YawQuarterTurns;
                if (!yawExplicitlyRotated)
                {
                    yaw = target.PlacementPose.YawQuarterTurns;
                }
            }
            else if (heldKind == MachineBuildKind.Funnel
                     && target.Kind == MachineNodeKind.BeltModule)
            {
                if (!yawExplicitlyRotated)
                {
                    yaw = Opposite(side);
                }
            }
            else if (heldKind == MachineBuildKind.BeltModule
                     && target.Kind == MachineNodeKind.BeltModule)
            {
                // Uscendo da una Curva la marcia prosegue sull'asse d'uscita,
                // non su quello del bersaglio: ereditarne la posa farebbe
                // proseguire dritto un percorso che ha appena svoltato.
                // Un nastro si collega solo dalle sue due testate. Mirandone il
                // fianco il modulo veniva posato lì lo stesso, dove l'adiacenza
                // poi lo rifiutava: il pezzo appariva accanto al nastro senza
                // agganciarsi, ed era il difetto che si vedeva in gioco. Il lato
                // mirato viene quindi riportato sull'uscita reale. Per una Curva
                // già alimentata questa uscita dipende anche dal verso di moto:
                // non è necessariamente l'uscita con cui il modello è stato
                // costruito.
                if (side != targetBeltExitYaw && side != targetBeltEntrySide)
                {
                    side = targetBeltExitYaw;
                }

                if (!yawExplicitlyRotated)
                {
                    if (side == targetBeltExitYaw)
                    {
                        yaw = targetBeltExitYaw;
                    }
                    else
                    {
                        // The new module is being added upstream. Its forward
                        // output must enter the target along the target's actual
                        // entry yaw. On a Curve the authored output is not the
                        // placement yaw, so copying that yaw attached the wrong
                        // half of the mesh.
                        yaw = BeltModuleShape.PlacementYawForForwardExit(
                            heldDefinitionId,
                            targetBeltEntryYaw);
                    }
                }
            }
            else if (heldKind == MachineBuildKind.Machine
                     && target.Kind == MachineNodeKind.BeltModule)
            {
                side = target.PlacementPose.YawQuarterTurns;
                if (!yawExplicitlyRotated)
                {
                    yaw = target.PlacementPose.YawQuarterTurns;
                }
            }
            else if (heldKind == MachineBuildKind.BeltModule
                     && target.Kind == MachineNodeKind.Machine)
            {
                var machineExit = target.PlacementPose.YawQuarterTurns;
                var machineEntry = Opposite(machineExit);
                // A press has two useful ends, never lateral belt ports. Aiming
                // its broad side used to leave the Curve beside the frame while
                // orienting it as though it were in front.
                if (side != machineExit && side != machineEntry)
                {
                    side = machineExit;
                }

                if (!yawExplicitlyRotated)
                {
                    yaw = target.PlacementPose.YawQuarterTurns;
                }
            }

            // La quota nasce dalle due estremità fisiche che si toccano, non
            // dal verso di moto corrente. È la differenza essenziale per una
            // Salita ruotata e usata in discesa: il suo lato alto deve poggiare
            // sul modulo superiore, quindi la radice va 30 cm più in basso.
            var targetEndpointHeight = 0;
            if (target.Kind == MachineNodeKind.BeltModule)
            {
                BeltModuleShape.TryGetEndpointHeightMillimetres(
                    target.DefinitionId,
                    target.PlacementPose.YawQuarterTurns,
                    side,
                    out targetEndpointHeight);
            }

            var heldEndpointHeight = 0;
            if (heldKind == MachineBuildKind.BeltModule)
            {
                BeltModuleShape.TryGetEndpointHeightMillimetres(
                    heldDefinitionId,
                    yaw,
                    Opposite(side),
                    out heldEndpointHeight);
            }

            return Offset(
                target.PlacementPose,
                side,
                yaw,
                targetEndpointHeight - heldEndpointHeight);
        }

        private static Vector3 BeltCellEdgeToward(
            Vector3 rootPosition,
            MachineBuildPose ownPose,
            MachineBuildPose otherPose)
        {
            var deltaX = otherPose.XMillimetres - ownPose.XMillimetres;
            var deltaZ = otherPose.ZMillimetres - ownPose.ZMillimetres;
            var direction = Vector3.zero;
            if (Math.Abs(deltaX) > Math.Abs(deltaZ))
            {
                direction.x = deltaX >= 0 ? 1f : -1f;
            }
            else
            {
                direction.z = deltaZ >= 0 ? 1f : -1f;
            }

            var halfCellMetres =
                MachineSpatialTopology.GridCellSizeMillimetres / 2_000f;
            return rootPosition + direction * halfCellMetres;
        }

        public static MachineBuildPose ResolveConnectorCell(
            MachineSimulationState state,
            StableId occupiedNodeId,
            MachineBuildPose snappedPose,
            MachineBuildKind heldKind,
            bool inheritConnectorYaw = true,
            StableId heldDefinitionId = default)
        {
            if (state == null
                || occupiedNodeId.IsNone
                || !state.TryGetNode(occupiedNodeId, out var occupied)
                || !occupied.HasPlacementPose)
            {
                return snappedPose;
            }

            var aimedSide = DirectionFromTo(
                occupied.PlacementPose,
                snappedPose,
                snappedPose.YawQuarterTurns);
            return ResolveFromTarget(
                state,
                occupiedNodeId,
                snappedPose,
                heldKind,
                aimedSide,
                snappedPose.YawQuarterTurns,
                !inheritConnectorYaw,
                heldDefinitionId);
        }

        public static MachineBuildPose ResolveOccupiedConnectorCell(
            MachineSimulationState state,
            MachineBuildPose snappedPose,
            MachineBuildKind heldKind,
            bool inheritConnectorYaw = true,
            StableId heldDefinitionId = default)
        {
            if (!TryGetOccupant(state, snappedPose, out var occupied))
            {
                return snappedPose;
            }

            return ResolveFromTarget(
                state,
                occupied.Id,
                snappedPose,
                heldKind,
                snappedPose.YawQuarterTurns,
                snappedPose.YawQuarterTurns,
                !inheritConnectorYaw,
                heldDefinitionId);
        }

        public static bool ArePortsAdjacent(
            MachineNodeKind leftKind,
            MachineBuildPose left,
            MachineNodeKind rightKind,
            MachineBuildPose right)
        {
            return ArePortsAdjacent(
                leftKind,
                left,
                default,
                rightKind,
                right,
                default);
        }

        /// <summary>
        /// Come sopra, ma sapendo *quali* moduli sono.
        ///
        /// Serve perché Curva e Salita non si comportano come un rettilineo: la
        /// prima cambia asse fra ingresso e uscita, la seconda cambia quota.
        /// Senza le definizioni si può solo assumere che ogni nastro sia dritto
        /// e in piano, che è esattamente l'assunzione che le escludeva.
        /// </summary>
        public static bool ArePortsAdjacent(
            MachineNodeKind leftKind,
            MachineBuildPose left,
            StableId leftDefinitionId,
            MachineNodeKind rightKind,
            MachineBuildPose right,
            StableId rightDefinitionId)
        {
            var allowedRise = Math.Max(
                Math.Abs(BeltModuleShape.RiseMillimetres(leftDefinitionId)),
                Math.Abs(BeltModuleShape.RiseMillimetres(rightDefinitionId)));
            if (!TryDirectionBetween(left, right, allowedRise, out var leftToRight))
            {
                return false;
            }

            if ((leftKind == MachineNodeKind.Buffer
                 && rightKind == MachineNodeKind.Funnel)
                || (leftKind == MachineNodeKind.Funnel
                    && rightKind == MachineNodeKind.Buffer))
            {
                var funnel = leftKind == MachineNodeKind.Funnel
                    ? left
                    : right;
                var buffer = leftKind == MachineNodeKind.Buffer
                    ? left
                    : right;
                return IsBehind(funnel, buffer);
            }

            // Imbuto contro macchina, la coppia che mancava.
            //
            // Senza questo caso l'anteprima non risulta mai agganciata alla
            // macchina, e siccome il test d'ingombro esclude soltanto il nodo a
            // cui si è agganciati, l'Imbuto veniva rifiutato per
            // "spazio occupato" proprio nell'unica posizione corretta: quella
            // in cui la sua morsa entra nell'incastro, che è una compenetrazione
            // voluta.
            //
            // Vale per la sola Trivella, come la regola autorevole in
            // MachineSpatialTopology: la Fornace non ha ancora una sede
            // modellata e agganciarcisi darebbe un Imbuto inerte.
            if ((leftKind == MachineNodeKind.Machine
                 && rightKind == MachineNodeKind.Funnel
                 && leftDefinitionId == ContentIds.MechanicalDrill)
                || (leftKind == MachineNodeKind.Funnel
                    && rightKind == MachineNodeKind.Machine
                    && rightDefinitionId == ContentIds.MechanicalDrill))
            {
                var funnel = leftKind == MachineNodeKind.Funnel
                    ? left
                    : right;
                var machine = leftKind == MachineNodeKind.Machine
                    ? left
                    : right;
                return IsBehind(funnel, machine);
            }

            if ((leftKind == MachineNodeKind.Funnel
                 && rightKind == MachineNodeKind.BeltModule)
                || (leftKind == MachineNodeKind.BeltModule
                    && rightKind == MachineNodeKind.Funnel))
            {
                var funnel = leftKind == MachineNodeKind.Funnel
                    ? left
                    : right;
                var belt = leftKind == MachineNodeKind.BeltModule
                    ? left
                    : right;
                return IsInFront(funnel, belt)
                    && IsOnAxis(
                        DirectionFromTo(
                            funnel,
                            belt,
                            funnel.YawQuarterTurns),
                        belt.YawQuarterTurns);
            }

            if ((leftKind == MachineNodeKind.BeltModule
                 && rightKind == MachineNodeKind.BeltModule)
                || (leftKind == MachineNodeKind.BeltModule
                    && rightKind == MachineNodeKind.Machine)
                || (leftKind == MachineNodeKind.Machine
                    && rightKind == MachineNodeKind.BeltModule))
            {
                // Si confrontano le due estremità reali che si guardano. Una
                // Curva non occupa l'intero asse dei suoi due bracci: accettare
                // anche la semiretta opposta agganciava il rettilineo alla
                // seconda metà della sua cella. Le quote chiudono lo stesso
                // contratto per Salita e discesa.
                return TryGetConnectionHeight(
                           leftKind,
                           left,
                           leftDefinitionId,
                           leftToRight,
                           out var leftHeight)
                    && TryGetConnectionHeight(
                        rightKind,
                        right,
                        rightDefinitionId,
                        Opposite(leftToRight),
                        out var rightHeight)
                    && leftHeight == rightHeight;
            }

            return false;
        }

        private static bool TryGetConnectionHeight(
            MachineNodeKind kind,
            MachineBuildPose pose,
            StableId definitionId,
            byte sideYaw,
            out int worldHeightMillimetres)
        {
            worldHeightMillimetres = pose.YMillimetres;
            if (kind != MachineNodeKind.BeltModule
                && kind != MachineNodeKind.Machine)
            {
                return false;
            }

            if (!BeltModuleShape.TryGetEndpointHeightMillimetres(
                    definitionId,
                    pose.YawQuarterTurns,
                    sideYaw,
                    out var endpointHeight))
            {
                return false;
            }

            worldHeightMillimetres = checked(
                pose.YMillimetres + endpointHeight);
            return true;
        }

        public static bool TryGetOccupant(
            MachineSimulationState state,
            MachineBuildPose pose,
            out MachineNodeState occupant)
        {
            occupant = null;
            if (state == null)
            {
                return false;
            }

            var ids = state.GetNodeIdsCanonical();
            for (var index = 0; index < ids.Count; index++)
            {
                if (!state.TryGetNode(ids[index], out var node)
                    || !node.HasPlacementPose)
                {
                    continue;
                }

                var nodePose = node.PlacementPose;
                if (nodePose.XMillimetres == pose.XMillimetres
                    && nodePose.YMillimetres == pose.YMillimetres
                    && nodePose.ZMillimetres == pose.ZMillimetres)
                {
                    occupant = node;
                    return true;
                }
            }

            return false;
        }

        private static MachineBuildPose Offset(
            MachineBuildPose source,
            byte directionYaw,
            byte heldYaw)
        {
            return Offset(source, directionYaw, heldYaw, 0);
        }

        private static MachineBuildPose Offset(
            MachineBuildPose source,
            byte directionYaw,
            byte heldYaw,
            int riseMillimetres)
        {
            var x = source.XMillimetres;
            var z = source.ZMillimetres;
            switch (directionYaw & 3)
            {
                case 0:
                    z = checked(
                        z + MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
                case 1:
                    x = checked(
                        x + MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
                case 2:
                    z = checked(
                        z - MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
                case 3:
                    x = checked(
                        x - MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
            }

            return new MachineBuildPose(
                x,
                checked(source.YMillimetres + riseMillimetres),
                z,
                (byte)(heldYaw & 3));
        }

        private static bool TryDirectionBetween(
            MachineBuildPose source,
            MachineBuildPose target,
            out byte direction)
        {
            return TryDirectionBetween(source, target, 0, out direction);
        }

        /// <summary>
        /// Direzione fra due celle vicine.
        ///
        /// `allowedRiseMillimetres` è il dislivello che uno dei due moduli
        /// dichiara di superare. Senza, qualunque differenza di quota rendeva i
        /// due non adiacenti — ed era la ragione per cui dopo una Salita non si
        /// poteva più proseguire: il modulo successivo, che sta appunto più in
        /// alto, non veniva mai considerato un vicino.
        /// </summary>
        private static bool TryDirectionBetween(
            MachineBuildPose source,
            MachineBuildPose target,
            int allowedRiseMillimetres,
            out byte direction)
        {
            var deltaX =
                target.XMillimetres - source.XMillimetres;
            var deltaZ =
                target.ZMillimetres - source.ZMillimetres;
            var cell = MachineSpatialTopology.GridCellSizeMillimetres;
            var deltaY = target.YMillimetres - source.YMillimetres;
            if (deltaY != 0
                && (allowedRiseMillimetres == 0
                    || Math.Abs(deltaY) != Math.Abs(allowedRiseMillimetres)))
            {
                direction = 0;
                return false;
            }

            if (deltaX == 0 && deltaZ == cell)
            {
                direction = 0;
                return true;
            }

            if (deltaX == cell && deltaZ == 0)
            {
                direction = 1;
                return true;
            }

            if (deltaX == 0 && deltaZ == -cell)
            {
                direction = 2;
                return true;
            }

            if (deltaX == -cell && deltaZ == 0)
            {
                direction = 3;
                return true;
            }

            direction = 0;
            return false;
        }

        private static byte DirectionFromTo(
            MachineBuildPose source,
            MachineBuildPose target,
            byte fallback)
        {
            if (TryDirectionBetween(source, target, out var direction))
            {
                return direction;
            }

            var deltaX =
                target.XMillimetres - source.XMillimetres;
            var deltaZ =
                target.ZMillimetres - source.ZMillimetres;
            if (deltaX == 0 && deltaZ == 0)
            {
                return (byte)(fallback & 3);
            }

            if (Math.Abs(deltaX) > Math.Abs(deltaZ))
            {
                return deltaX >= 0 ? (byte)1 : (byte)3;
            }

            return deltaZ >= 0 ? (byte)0 : (byte)2;
        }

        private static bool IsBehind(
            MachineBuildPose funnel,
            MachineBuildPose buffer) =>
            TryDirectionBetween(funnel, buffer, out var direction)
            && direction == Opposite(funnel.YawQuarterTurns);

        private static bool IsInFront(
            MachineBuildPose funnel,
            MachineBuildPose belt) =>
            TryDirectionBetween(funnel, belt, out var direction)
            && direction == funnel.YawQuarterTurns;

        private static bool IsOnAxis(byte direction, byte yaw) =>
            direction == (yaw & 3)
            || direction == Opposite(yaw);

        private static byte Opposite(byte yaw) =>
            (byte)(((yaw & 3) + 2) & 3);
    }
}

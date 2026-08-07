using System;
using System.Collections.Generic;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Airship;
using CML.Unity.Presentation.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Machines
{
    /// <summary>
    /// UI-CONT-001. The crate on one side and the backpack on the other use the
    /// familiar two-click inventory gesture: the first click picks up a whole stack,
    /// and the second click on the other holder requests its transfer.
    ///
    /// The click does not move anything itself: it asks
    /// <see cref="TransferCommandBridge"/> for a move, and the authoritative rule from
    /// MACH-002 decides in phase 9. So the panel cannot lose or duplicate an item even if
    /// it is wrong about what it is showing, and a refusal comes back as a named cause
    /// rather than as a slot that quietly did not change.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ChestHudController : MonoBehaviour,
        IInventorySlotDragHost
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet chestStyleSheet;
        [SerializeField] private StyleSheet inventoryStyleSheet;
        [SerializeField] private TransferCommandBridge bridge;
        [SerializeField] private AirshipInputAdapter airshipInput;
        [SerializeField] private FirstPersonMouseLook mouseLook;

        private readonly List<InventorySlotView> _crateViews = new List<InventorySlotView>();
        private readonly List<InventorySlotView> _playerViews = new List<InventorySlotView>();

        private VisualElement _screen;
        private VisualElement _backdrop;
        private VisualElement _panel;
        private VisualElement _crateGrid;
        private VisualElement _playerGrid;
        private Label _titleLabel;
        private Label _crateTitle;
        private Label _crateNote;
        private Label _playerNote;
        private Label _statusLabel;

        private StableId _crateNodeId;
        private StableId _playerInventoryId;
        private MachinePortPresentation _crateSide;
        private InventoryUiSnapshot _playerSide;
        private bool _isBuilt;
        private bool _isOpen;

        // Shared with the machine and airship panels: how an inventory grid
        // behaves is not this panel's business. What stays here is only what is
        // specific to a crate — its two endpoints and its refusal messages.
        private InventorySlotDragController _drag;

        public bool PanelOpen => _isOpen;

        public string StatusText => _statusLabel == null ? string.Empty : _statusLabel.text;

        public void ConfigureUiAsset(
            UIDocument uiDocument,
            StyleSheet chestSheet,
            StyleSheet inventorySheet)
        {
            document = uiDocument;
            chestStyleSheet = chestSheet;
            inventoryStyleSheet = inventorySheet;
        }

        public void ConfigureGameplayInput(
            TransferCommandBridge transferBridge,
            AirshipInputAdapter inputAdapter,
            FirstPersonMouseLook firstPersonMouseLook)
        {
            bridge = transferBridge;
            airshipInput = inputAdapter;
            mouseLook = firstPersonMouseLook;
        }

        /// <summary>Names the two holders this panel joins. Both must already exist.</summary>
        public void Bind(StableId crateNodeId, StableId playerInventoryId)
        {
            if (crateNodeId.IsNone || playerInventoryId.IsNone)
            {
                throw new ArgumentException("A crate panel needs both holders.");
            }

            _crateNodeId = crateNodeId;
            _playerInventoryId = playerInventoryId;
            EnsureUi();
            Refresh();
        }

        public void SetPanelOpen(bool open)
        {
            EnsureUi();
            if (!open)
            {
                _drag?.Cancel();
            }

            _isOpen = open;
            ApplyModalState();
            if (open)
            {
                Refresh();
            }
        }

        public void TogglePanel()
        {
            SetPanelOpen(!_isOpen);
        }

        /// <summary>
        /// Reads the authoritative state again. Called on open and after every tick the
        /// scene advances, because a transfer lands a tick after the click.
        /// </summary>
        public void Refresh()
        {
            if (!_isBuilt || bridge == null || !bridge.IsAttached || _crateNodeId.IsNone)
            {
                return;
            }

            var catalog = bridge.Catalog;
            if (!MachineDiagnostics.TryDescribe(
                    bridge.Engine.State,
                    catalog,
                    _crateNodeId,
                    out var crateReport))
            {
                return;
            }

            var crateSnapshot = MachineHudPresenter.Project(crateReport, catalog);
            _crateSide = crateSnapshot.Ports[0];
            _titleLabel.text = crateSnapshot.Title.ToUpperInvariant();
            _crateTitle.text = _crateSide.Title;
            _crateNote.text = $"{_crateSide.TotalQuantity} oggetti · {_crateSide.Slots.Count} slot";

            if (!bridge.Inventories.TryGet(_playerInventoryId, out var inventory))
            {
                return;
            }

            _playerSide = InventoryHudPresenter.Project(inventory, catalog);
            _playerNote.text =
                $"{inventory.TotalQuantity} oggetti · {inventory.SlotCount} slot";

            BindGrid(
                _crateGrid,
                _crateViews,
                _crateSide.Slots,
                "crate-slot",
                ChestSlotSide.Crate);
            BindGrid(
                _playerGrid,
                _playerViews,
                _playerSide.Slots,
                "player-slot",
                ChestSlotSide.Player);
            ReportLastRejection(FindMyLatestRejection());
        }

        /// <summary>
        /// The most recent refusal that concerns these two holders, if any.
        ///
        /// The panel reads this itself rather than waiting to be told: a refusal that only
        /// appears when some other object remembers to report it is a refusal the player
        /// never sees, and a slot that silently did not change is the worst way to say no.
        /// Filtering by holder keeps one panel from showing another panel's refusal.
        /// </summary>
        private CommandRejectionReason? FindMyLatestRejection()
        {
            var rejections = bridge.Engine.State.GetCommandRejectionsCanonical();
            for (var index = rejections.Count - 1; index >= 0; index--)
            {
                var rejection = rejections[index];
                if (!string.Equals(
                        rejection.Command.Kind,
                        SimulationCommandKinds.Transfer,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (Concerns(rejection.Command.InitiatorId)
                    && Concerns(rejection.Command.DestinationId))
                {
                    return rejection.Reason;
                }
            }

            return null;
        }

        private bool Concerns(StableId ownerId)
        {
            return ownerId == _crateNodeId || ownerId == _playerInventoryId;
        }

        private void Awake()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }
        }

        private void Start()
        {
            EnsureUi();
            ApplyModalState();
        }

        private void Update()
        {
            if (!_isOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_drag != null && _drag.HasCursorStack)
                {
                    _drag.Cancel();
                }
                else
                {
                    SetPanelOpen(false);
                }
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            _isOpen = false;
            _drag?.Cancel();
            airshipInput?.SetUiInputSuppressed(false);
            mouseLook?.SetUiInputSuppressed(false);
        }

        private void EnsureUi()
        {
            if (document == null)
            {
                return;
            }

            var root = document.rootVisualElement;
            if (root == null)
            {
                return;
            }

            if (_isBuilt
                && _backdrop != null
                && _panel != null
                && ReferenceEquals(
                    _backdrop,
                    root.Q<VisualElement>("chest-backdrop"))
                && ReferenceEquals(
                    _panel,
                    root.Q<VisualElement>("chest-panel")))
            {
                return;
            }

            // UIDocument can rebuild its visual tree during a domain reload or when
            // Play Mode recompiles scripts. In that case _isBuilt may survive while
            // the cached VisualElements do not. Rebind every named element to the
            // current tree instead of dereferencing stale references.
            _isBuilt = false;
            _drag?.Cancel();
            _drag = new InventorySlotDragController(this);
            _crateViews.Clear();
            _playerViews.Clear();
            AddStyleSheet(root, inventoryStyleSheet);
            AddStyleSheet(root, chestStyleSheet);

            root.pickingMode = PickingMode.Ignore;
            _screen = root.Q<VisualElement>("chest-screen");
            _backdrop = root.Q<VisualElement>("chest-backdrop");
            _panel = root.Q<VisualElement>("chest-panel");
            _crateGrid = root.Q<VisualElement>("crate-grid");
            _playerGrid = root.Q<VisualElement>("player-grid");
            _titleLabel = root.Q<Label>("chest-title");
            _crateTitle = root.Q<Label>("crate-title");
            _crateNote = root.Q<Label>("crate-note");
            _playerNote = root.Q<Label>("player-note");
            _statusLabel = root.Q<Label>("chest-status");

            if (_screen == null
                || _backdrop == null
                || _panel == null
                || _crateGrid == null
                || _playerGrid == null
                || _titleLabel == null
                || _statusLabel == null)
            {
                throw new InvalidOperationException(
                    "ChestHUD.uxml is missing a required named element.");
            }

            // The closed chest document sorts above the Tab inventory. Its
            // full-screen structural elements must not become pointer targets;
            // the visible backdrop, panel and slots remain pickable children.
            _screen.pickingMode = PickingMode.Ignore;

            // The cursor stack keeps following pointer movement after the first click
            // has completed and the mouse button has been released.
            _drag.AttachRoot(root);
            _isBuilt = true;
        }

        private static void AddStyleSheet(VisualElement root, StyleSheet sheet)
        {
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }
        }

        private void BindGrid(
            VisualElement grid,
            List<InventorySlotView> views,
            IReadOnlyList<InventorySlotPresentation> slots,
            string namePrefix,
            ChestSlotSide side)
        {
            if (views.Count != slots.Count)
            {
                _drag.Cancel();
                _drag.ClearRegisteredSlots((int)side);
                grid.Clear();
                views.Clear();
                for (var index = 0; index < slots.Count; index++)
                {
                    var view = new InventorySlotView(
                        index,
                        namePrefix,
                        "chest-slot",
                        slots.Count);
                    _drag.RegisterSlot(view, (int)side, index);
                    views.Add(view);
                    grid.Add(view.Root);
                }
            }

            for (var index = 0; index < views.Count; index++)
            {
                views[index].Bind(slots[index], isSelected: false);
                _drag.RebindSlot(views[index], (int)side, index);
            }
        }

        /// <summary>
        /// Manda l'intera pila di uno slot all'altro contenitore, senza passare
        /// dal cursore. È lo spostamento rapido di Shift+sinistro.
        /// </summary>
        // ------------------------------------------------ IInventorySlotDragHost
        //
        // The pane key is ChestSlotSide cast to int; the shared controller treats
        // it as opaque, so no second mapping is needed.

        public bool IsDragEnabled => _isOpen;

        public VisualElement DragRoot =>
            document != null ? document.rootVisualElement : null;

        public string CursorNamePrefix => "chest-cursor-stack";

        public string CursorVariantClass => "chest-slot";

        public bool TryGetSlot(
            int pane,
            int slotIndex,
            out InventorySlotPresentation slot)
        {
            return TryGetSlot((ChestSlotSide)pane, slotIndex, out slot);
        }

        public int SlotCount(int pane)
        {
            return Slots((ChestSlotSide)pane)?.Count ?? 0;
        }

        /// <summary>
        /// Only the player's own inventory reorders. Crate slots have no
        /// meaningful order to rearrange.
        /// </summary>
        public bool SupportsReorder(int pane)
        {
            return (ChestSlotSide)pane == ChestSlotSide.Player;
        }

        public void QuickMove(
            int pane,
            int slotIndex,
            InventorySlotPresentation slot)
        {
            SubmitQuickMove((ChestSlotSide)pane, slot);
        }

        public void Reorder(
            int pane,
            int fromSlotIndex,
            int toSlotIndex,
            long quantity)
        {
            SubmitSlotMove(fromSlotIndex, toSlotIndex, quantity);
        }

        /// <summary>
        /// Capped by what the destination can still take, so a refused remainder
        /// stays on the cursor. The crate previously assumed the whole amount
        /// always landed; sharing the controller brings it in line with the
        /// machine panel, which always checked.
        /// </summary>
        public long MoveCursorStack(
            int fromPane,
            int toPane,
            StableId itemId,
            long requested)
        {
            if (bridge == null || !bridge.IsAttached || requested <= 0L)
            {
                return 0L;
            }

            var allowed = ResolveAllowedMoveQuantity(
                (ChestSlotSide)toPane,
                itemId,
                requested);
            if (allowed <= 0L)
            {
                _statusLabel.text = "La destinazione è piena";
                return 0L;
            }

            SubmitMove(
                Endpoint((ChestSlotSide)fromPane),
                Endpoint((ChestSlotSide)toPane),
                itemId,
                allowed);
            return allowed;
        }

        private TransferEndpoint Endpoint(ChestSlotSide side)
        {
            return side == ChestSlotSide.Crate
                ? TransferEndpoint.Port(_crateNodeId, MachinePortKind.Storage)
                : TransferEndpoint.Inventory(_playerInventoryId);
        }

        private long ResolveAllowedMoveQuantity(
            ChestSlotSide destinationSide,
            StableId itemId,
            long requested)
        {
            if (bridge == null
                || !bridge.IsAttached
                || requested <= 0L)
            {
                return 0L;
            }

            if (destinationSide == ChestSlotSide.Player)
            {
                return bridge.Inventories.TryGet(
                        _playerInventoryId,
                        out var inventory)
                    ? Math.Min(
                        requested,
                        inventory.StorableQuantity(itemId).Value)
                    : 0L;
            }

            if (!bridge.Machines.TryGetNode(_crateNodeId, out var crate)
                || crate.Kind != MachineNodeKind.Buffer)
            {
                return 0L;
            }

            return Math.Min(
                requested,
                crate.Input.StorableQuantity(itemId, bridge.Catalog).Value);
        }

        private void SubmitQuickMove(ChestSlotSide side, InventorySlotPresentation slot)
        {
            var destinationSide = side == ChestSlotSide.Crate
                ? ChestSlotSide.Player
                : ChestSlotSide.Crate;
            var quantity = ResolveAllowedMoveQuantity(
                destinationSide,
                slot.ItemId,
                slot.Quantity);
            if (quantity <= 0L)
            {
                _statusLabel.text = "La destinazione è piena";
                return;
            }

            SubmitMove(
                Endpoint(side),
                Endpoint(destinationSide),
                slot.ItemId,
                quantity);
        }

        /// <summary>
        /// Riordino fra due slot dell'inventario del giocatore.
        /// </summary>
        private void SubmitSlotMove(int sourceSlotIndex, int destinationSlotIndex, long quantity)
        {
            if (bridge == null
                || !bridge.IsAttached
                || quantity <= 0L
                || _playerInventoryId.IsNone)
            {
                return;
            }

            _statusLabel.text = string.Empty;
            bridge.SubmitSlotMove(
                _playerInventoryId,
                sourceSlotIndex,
                destinationSlotIndex,
                new NonNegativeQuantity(quantity));
        }

        private bool TryGetSlot(
            ChestSlotSide side,
            int slotIndex,
            out InventorySlotPresentation presentation)
        {
            presentation = default;
            var slots = Slots(side);
            if (slots == null
                || slotIndex < 0
                || slotIndex >= slots.Count)
            {
                return false;
            }

            presentation = slots[slotIndex];
            return true;
        }

        private IReadOnlyList<InventorySlotPresentation> Slots(
            ChestSlotSide side)
        {
            return side == ChestSlotSide.Crate
                ? _crateSide?.Slots
                : _playerSide?.Slots;
        }

        private void SubmitMove(
            TransferEndpoint source,
            TransferEndpoint destination,
            StableId itemId,
            long quantity)
        {
            if (bridge == null
                || !bridge.IsAttached
                || itemId.IsNone
                || quantity <= 0L)
            {
                return;
            }

            _statusLabel.text = string.Empty;
            bridge.SubmitTransfer(
                source,
                destination,
                itemId,
                new NonNegativeQuantity(quantity));
        }

        /// <summary>
        /// Shows why the last move was refused. The panel does not decide this: it reads
        /// the cause the authoritative rule recorded.
        /// </summary>
        public void ReportLastRejection(CommandRejectionReason? reason)
        {
            if (!_isBuilt)
            {
                return;
            }

            _statusLabel.text = reason.HasValue ? RejectionText(reason.Value) : string.Empty;
        }

        private static string RejectionText(CommandRejectionReason reason)
        {
            switch (reason)
            {
                case CommandRejectionReason.InsufficientQuantity:
                    return "Non c'è abbastanza materiale";
                case CommandRejectionReason.TransferDestinationFull:
                    return "La destinazione è piena";
                case CommandRejectionReason.TransferNotAdmitted:
                    return "La destinazione non accetta questo oggetto";
                case CommandRejectionReason.TransferSourceMissing:
                    return "L'origine non esiste più";
                case CommandRejectionReason.TransferDestinationMissing:
                    return "La destinazione non esiste più";
                case CommandRejectionReason.TransferSameEndpoint:
                    return "Origine e destinazione coincidono";
                case CommandRejectionReason.TransferUnknownItem:
                    return "Oggetto sconosciuto";
                case CommandRejectionReason.TransferZeroAmount:
                    return "Quantità nulla";
                default:
                    return "Spostamento rifiutato";
            }
        }

        private void ApplyModalState()
        {
            EnsureUi();
            if (!_isBuilt || _backdrop == null || _panel == null)
            {
                return;
            }

            _backdrop.style.display = _isOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _panel.EnableInClassList("chest-panel--open", _isOpen);
            airshipInput?.SetUiInputSuppressed(_isOpen);
            mouseLook?.SetUiInputSuppressed(_isOpen);
        }

        private enum ChestSlotSide : byte
        {
            Crate = 1,
            Player = 2
        }
    }
}

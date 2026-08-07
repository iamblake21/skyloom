using System;
using System.Collections.Generic;
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
    /// Interactive machine panel. Its inventory gestures intentionally match the
    /// Chest HUD: left click picks a stack, right click picks half/deposits one,
    /// Shift+left moves quickly, and player slots can be reordered. Every mutation is
    /// still a command handled by the authoritative transfer rules.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MachineHudController : MonoBehaviour,
        IInventorySlotDragHost
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet machineStyleSheet;
        [SerializeField] private StyleSheet inventoryStyleSheet;
        [SerializeField] private TransferCommandBridge bridge;
        [SerializeField] private AirshipInputAdapter airshipInput;
        [SerializeField] private FirstPersonMouseLook mouseLook;

        private readonly List<InventorySlotView> _inputViews = new List<InventorySlotView>();
        private readonly List<InventorySlotView> _fuelViews = new List<InventorySlotView>();
        private readonly List<InventorySlotView> _outputViews = new List<InventorySlotView>();
        private readonly List<InventorySlotView> _playerViews = new List<InventorySlotView>();

        private VisualElement _screen;
        private VisualElement _backdrop;
        private VisualElement _panel;
        private VisualElement _inputSection;
        private VisualElement _fuelSection;
        private VisualElement _outputSection;
        private VisualElement _progressRow;
        private VisualElement _inputGrid;
        private VisualElement _fuelGrid;
        private VisualElement _outputGrid;
        private VisualElement _playerGrid;
        private VisualElement _progressFill;
        private Label _titleLabel;
        private Label _recipeLabel;
        private Label _inputTitle;
        private Label _inputNote;
        private Label _fuelTitle;
        private Label _fuelNote;
        private Label _outputTitle;
        private Label _outputNote;
        private Label _playerNote;
        private Label _progressLabel;
        private Label _causeLabel;
        private Label _shortfallLabel;
        private Label _statusLabel;

        private MachineUiSnapshot _bound;
        private InventoryUiSnapshot _playerInventory;
        private StableId _playerInventoryId;
        private VisualElement _builtRoot;
        private bool _isBuilt;
        private bool _isOpen;

        // Picking a stack up, splitting it, carrying it and dropping it are not
        // this panel's business: they are how an inventory grid behaves, and the
        // same rules serve the crate and the airship. Only the machine-specific
        // answers stay here — which endpoint a side is, what a port will admit,
        // where a quick move should go.
        private InventorySlotDragController _drag;

        public bool PanelOpen => _isOpen;

        public MachineUiSnapshot BoundSnapshot => _bound;

        public void ConfigureUiAsset(
            UIDocument uiDocument,
            StyleSheet machineSheet,
            StyleSheet inventorySheet)
        {
            document = uiDocument;
            machineStyleSheet = machineSheet;
            inventoryStyleSheet = inventorySheet;
        }

        public void ConfigureGameplayInput(
            AirshipInputAdapter inputAdapter,
            FirstPersonMouseLook firstPersonMouseLook)
        {
            airshipInput = inputAdapter;
            mouseLook = firstPersonMouseLook;
        }

        public void ConfigureTransfers(
            TransferCommandBridge transferBridge,
            StableId playerInventoryId)
        {
            bridge = transferBridge;
            _playerInventoryId = playerInventoryId;
        }

        public void Bind(MachineUiSnapshot snapshot)
        {
            Bind(snapshot, _playerInventory);
        }

        public void Bind(
            MachineUiSnapshot snapshot,
            InventoryUiSnapshot playerInventory)
        {
            _bound = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _playerInventory = playerInventory;
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
        }

        public void TogglePanel()
        {
            SetPanelOpen(!_isOpen);
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
            if (!_isOpen
                || Keyboard.current?.escapeKey.wasPressedThisFrame != true)
            {
                return;
            }

            if (_drag != null && _drag.HasCursorStack)
            {
                _drag.Cancel();
            }
            else
            {
                SetPanelOpen(false);
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
            if (document == null || document.rootVisualElement == null)
            {
                return;
            }

            var root = document.rootVisualElement;
            if (_isBuilt
                && ReferenceEquals(_builtRoot, root)
                && _panel != null)
            {
                return;
            }

            _drag?.Cancel();
            _drag = new InventorySlotDragController(this);
            _isBuilt = false;
            _builtRoot = null;
            _inputViews.Clear();
            _fuelViews.Clear();
            _outputViews.Clear();
            _playerViews.Clear();

            AddStyleSheet(root, inventoryStyleSheet);
            AddStyleSheet(root, machineStyleSheet);
            root.pickingMode = PickingMode.Ignore;

            _screen = root.Q<VisualElement>("machine-screen");
            _backdrop = root.Q<VisualElement>("machine-backdrop");
            _panel = root.Q<VisualElement>("machine-panel");
            _inputSection = root.Q<VisualElement>("input-section");
            _fuelSection = root.Q<VisualElement>("fuel-section");
            _outputSection = root.Q<VisualElement>("output-section");
            _progressRow = root.Q<VisualElement>("progress-row");
            _inputGrid = root.Q<VisualElement>("input-grid");
            _fuelGrid = root.Q<VisualElement>("fuel-grid");
            _outputGrid = root.Q<VisualElement>("output-grid");
            _playerGrid = root.Q<VisualElement>("player-grid");
            _progressFill = root.Q<VisualElement>("progress-fill");
            _titleLabel = root.Q<Label>("machine-title");
            _recipeLabel = root.Q<Label>("machine-recipe");
            _inputTitle = root.Q<Label>("input-title");
            _inputNote = root.Q<Label>("input-note");
            _fuelTitle = root.Q<Label>("fuel-title");
            _fuelNote = root.Q<Label>("fuel-note");
            _outputTitle = root.Q<Label>("output-title");
            _outputNote = root.Q<Label>("output-note");
            _playerNote = root.Q<Label>("player-note");
            _progressLabel = root.Q<Label>("progress-label");
            _causeLabel = root.Q<Label>("cause-label");
            _shortfallLabel = root.Q<Label>("shortfall-label");
            _statusLabel = root.Q<Label>("machine-status");

            if (_screen == null
                || _backdrop == null
                || _panel == null
                || _inputSection == null
                || _fuelSection == null
                || _outputSection == null
                || _progressRow == null
                || _inputGrid == null
                || _fuelGrid == null
                || _outputGrid == null
                || _playerGrid == null
                || _progressFill == null
                || _titleLabel == null
                || _causeLabel == null
                || _statusLabel == null)
            {
                throw new InvalidOperationException(
                    "MachineHUD.uxml is missing a required named element.");
            }

            _screen.pickingMode = PickingMode.Ignore;
            _drag.AttachRoot(root);
            _builtRoot = root;
            _isBuilt = true;
            Refresh();
        }

        private static void AddStyleSheet(VisualElement root, StyleSheet sheet)
        {
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }
        }

        private void Refresh()
        {
            if (!_isBuilt || _bound == null)
            {
                return;
            }

            _titleLabel.text = _bound.Title.ToUpperInvariant();
            _recipeLabel.text = _bound.RecipeName;

            var input = FindPort(MachinePortKind.Input);
            var fuel = FindPort(MachinePortKind.Fuel);
            var output = FindPort(MachinePortKind.Output);
            BindPort(
                input,
                _inputViews,
                _inputGrid,
                _inputTitle,
                _inputNote,
                MachineSlotSide.Input,
                "machine-input-slot");
            BindPort(
                fuel,
                _fuelViews,
                _fuelGrid,
                _fuelTitle,
                _fuelNote,
                MachineSlotSide.Fuel,
                "machine-fuel-slot");
            BindPort(
                output,
                _outputViews,
                _outputGrid,
                _outputTitle,
                _outputNote,
                MachineSlotSide.Output,
                "machine-output-slot");
            _inputSection.style.display = input != null ? DisplayStyle.Flex : DisplayStyle.None;
            _fuelSection.style.display = fuel != null ? DisplayStyle.Flex : DisplayStyle.None;
            _outputSection.style.display = output != null ? DisplayStyle.Flex : DisplayStyle.None;

            _progressRow.style.display = _bound.Kind == MachineNodeKind.Machine
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _progressFill.style.width = Length.Percent(_bound.ProgressPermille / 10f);
            _progressLabel.text = _bound.ProgressText;
            _causeLabel.text = _bound.CauseText;
            _shortfallLabel.text = _bound.ShortfallText;
            _shortfallLabel.style.display = string.IsNullOrEmpty(_bound.ShortfallText)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            _panel.EnableInClassList("machine-panel--blocked", _bound.IsBlocked);
            _panel.EnableInClassList(
                "machine-panel--resting",
                _bound.Activity == MachineActivity.Idle);

            BindPlayerGrid();

            var rejection = FindLatestTransferRejection();
            _statusLabel.text = rejection.HasValue
                ? RejectionText(rejection.Value)
                : string.Empty;
        }

        private MachinePortPresentation FindPort(MachinePortKind kind)
        {
            for (var index = 0; index < _bound.Ports.Count; index++)
            {
                if (_bound.Ports[index].Kind == kind)
                {
                    return _bound.Ports[index];
                }
            }

            return null;
        }

        private void BindPort(
            MachinePortPresentation port,
            List<InventorySlotView> views,
            VisualElement grid,
            Label title,
            Label note,
            MachineSlotSide side,
            string namePrefix)
        {
            if (port == null)
            {
                grid.Clear();
                views.Clear();
                return;
            }

            if (views.Count != port.Slots.Count)
            {
                _drag.Cancel();
                _drag.ClearRegisteredSlots((int)side);
                grid.Clear();
                views.Clear();
                for (var index = 0; index < port.Slots.Count; index++)
                {
                    var view = new InventorySlotView(
                        index,
                        namePrefix,
                        "machine-slot",
                        port.Slots.Count);
                    _drag.RegisterSlot(view, (int)side, index);
                    views.Add(view);
                    grid.Add(view.Root);
                }
            }

            title.text = port.Title;
            note.text = PortNote(port);
            for (var index = 0; index < views.Count; index++)
            {
                views[index].Bind(port.Slots[index], isSelected: false);

                // Rebinding replaces the contents of the tile, which would show
                // a stack the player is currently carrying.
                _drag.RebindSlot(views[index], (int)side, index);
            }
        }

        private string PortNote(MachinePortPresentation port)
        {
            var capacity = ResolvePortCapacity(port.Kind);
            if (capacity <= 0L)
            {
                return port.TotalQuantity == 0L
                    ? "Vuoto"
                    : $"{port.TotalQuantity} oggetti";
            }

            return $"{port.TotalQuantity}/{capacity}";
        }

        private long ResolvePortCapacity(MachinePortKind kind)
        {
            if (bridge == null
                || !bridge.IsAttached
                || _bound == null
                || !bridge.Machines.TryGetNode(_bound.NodeId, out var node)
                || !bridge.Catalog.TryGetMachine(node.DefinitionId, out var definition))
            {
                return 0L;
            }

            switch (kind)
            {
                case MachinePortKind.Input:
                    return definition.InputBufferCapacityPerItem;
                case MachinePortKind.Fuel:
                    return definition.FuelBufferCapacityPerItem;
                default:
                    return 0L;
            }
        }

        private void BindPlayerGrid()
        {
            if (_playerInventory == null)
            {
                _playerGrid.Clear();
                _playerViews.Clear();
                return;
            }

            var slots = _playerInventory.Slots;
            if (_playerViews.Count != slots.Count)
            {
                _drag.Cancel();
                _drag.ClearRegisteredSlots((int)MachineSlotSide.Player);
                _playerGrid.Clear();
                _playerViews.Clear();
                for (var index = 0; index < slots.Count; index++)
                {
                    var view = new InventorySlotView(
                        index,
                        "machine-player-slot",
                        "machine-player-slot",
                        slots.Count);
                    _drag.RegisterSlot(
                        view,
                        (int)MachineSlotSide.Player,
                        index);
                    _playerViews.Add(view);
                    _playerGrid.Add(view.Root);
                }
            }

            for (var index = 0; index < _playerViews.Count; index++)
            {
                _playerViews[index].Bind(slots[index], isSelected: false);
                _drag.RebindSlot(
                    _playerViews[index],
                    (int)MachineSlotSide.Player,
                    index);
            }

            _playerNote.text = "Shift: rapido · destro: dividi/deposita uno";
        }

        // ------------------------------------------------ IInventorySlotDragHost
        //
        // Thin adapters over the helpers this panel already had. The pane key is
        // simply the MachineSlotSide cast to int: the shared controller treats it
        // as opaque, so no second mapping is needed.

        public bool IsDragEnabled => _isOpen;

        public VisualElement DragRoot =>
            document != null ? document.rootVisualElement : null;

        public string CursorNamePrefix => "machine-cursor-stack";

        public string CursorVariantClass => "machine-slot";

        public bool TryGetSlot(
            int pane,
            int slotIndex,
            out InventorySlotPresentation slot)
        {
            return TryGetSlot((MachineSlotSide)pane, slotIndex, out slot);
        }

        public int SlotCount(int pane)
        {
            return Slots((MachineSlotSide)pane)?.Count ?? 0;
        }

        /// <summary>
        /// Only the player's own inventory reorders. A machine port has no
        /// meaningful slot order, so a drop onto the pane it came from does
        /// nothing rather than pretending to move something.
        /// </summary>
        public bool SupportsReorder(int pane)
        {
            return (MachineSlotSide)pane == MachineSlotSide.Player;
        }

        public void QuickMove(
            int pane,
            int slotIndex,
            InventorySlotPresentation slot)
        {
            SubmitQuickMove((MachineSlotSide)pane, slot);
        }

        public void Reorder(
            int pane,
            int fromSlotIndex,
            int toSlotIndex,
            long quantity)
        {
            SubmitSlotMove(fromSlotIndex, toSlotIndex, quantity);
        }

        public long MoveCursorStack(
            int fromPane,
            int toPane,
            StableId itemId,
            long requested)
        {
            var destinationSide = (MachineSlotSide)toPane;
            var quantity = ResolveAllowedMoveQuantity(
                destinationSide,
                itemId,
                requested);
            if (quantity <= 0L)
            {
                _statusLabel.text = "La destinazione è piena";
                return 0L;
            }

            SubmitTransfer(
                Endpoint((MachineSlotSide)fromPane),
                Endpoint(destinationSide),
                itemId,
                quantity);
            return quantity;
        }

        private void SubmitQuickMove(
            MachineSlotSide sourceSide,
            InventorySlotPresentation slot)
        {
            var destinationSide = sourceSide == MachineSlotSide.Player
                ? SideForPort(ResolveDestinationPort(slot.ItemId))
                : MachineSlotSide.Player;
            var quantity = ResolveAllowedMoveQuantity(
                destinationSide,
                slot.ItemId,
                slot.Quantity);
            if (quantity <= 0L)
            {
                _statusLabel.text = "La destinazione è piena";
                return;
            }

            SubmitTransfer(
                Endpoint(sourceSide),
                Endpoint(destinationSide),
                slot.ItemId,
                quantity);
        }

        private void SubmitSlotMove(
            int sourceSlotIndex,
            int destinationSlotIndex,
            long quantity)
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

        private long ResolveAllowedMoveQuantity(
            MachineSlotSide destinationSide,
            StableId itemId,
            long requested)
        {
            if (requested <= 0L || bridge == null || !bridge.IsAttached)
            {
                return 0L;
            }

            if (destinationSide == MachineSlotSide.Player)
            {
                return bridge.Inventories.TryGet(
                        _playerInventoryId,
                        out var inventory)
                    ? Math.Min(
                        requested,
                        inventory.StorableQuantity(itemId).Value)
                    : 0L;
            }

            if (!bridge.Machines.TryGetNode(_bound.NodeId, out var node))
            {
                return 0L;
            }

            var kind = PortKind(destinationSide);
            var port = Port(node, kind);
            if (port == null)
            {
                return requested;
            }

            var physical = port.StorableQuantity(itemId, bridge.Catalog).Value;
            if (!IsAdmitted(node, kind, itemId))
            {
                // Let the authoritative rule report "not admitted" rather than
                // disguising a wrong item as a full slot.
                return requested;
            }

            var capacity = physical;
            if (bridge.Catalog.TryGetMachine(node.DefinitionId, out var definition))
            {
                if (kind == MachinePortKind.Input)
                {
                    capacity = Math.Min(
                        capacity,
                        Math.Max(
                            0L,
                            definition.InputBufferCapacityPerItem
                            - node.Input.Count(itemId).Value));
                }
                else if (kind == MachinePortKind.Fuel && node.Fuel != null)
                {
                    capacity = Math.Min(
                        capacity,
                        Math.Max(
                            0L,
                            definition.FuelBufferCapacityPerItem
                            - node.Fuel.Count(itemId).Value));
                }
            }

            return Math.Min(requested, capacity);
        }

        private bool IsAdmitted(
            MachineNodeState node,
            MachinePortKind kind,
            StableId itemId)
        {
            if (kind == MachinePortKind.Fuel)
            {
                return bridge.Catalog.TryGetMachine(
                        node.DefinitionId,
                        out var definition)
                    && definition.RequiresFuel
                    && definition.FuelItemId == itemId;
            }

            if (kind != MachinePortKind.Input
                || node.ActiveRecipeId.IsNone
                || !bridge.Catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe))
            {
                return false;
            }

            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                if (recipe.Inputs[index].ItemId == itemId)
                {
                    return true;
                }
            }

            return false;
        }

        private MachinePortKind ResolveDestinationPort(StableId itemId)
        {
            if (bridge != null
                && bridge.IsAttached
                && bridge.Machines.TryGetNode(_bound.NodeId, out var node)
                && bridge.Catalog.TryGetMachine(node.DefinitionId, out var definition)
                && definition.RequiresFuel
                && definition.FuelItemId == itemId)
            {
                return MachinePortKind.Fuel;
            }

            return MachinePortKind.Input;
        }

        private TransferEndpoint Endpoint(MachineSlotSide side)
        {
            return side == MachineSlotSide.Player
                ? TransferEndpoint.Inventory(_playerInventoryId)
                : TransferEndpoint.Port(_bound.NodeId, PortKind(side));
        }

        private static MachineSlotSide SideForPort(MachinePortKind kind)
        {
            switch (kind)
            {
                case MachinePortKind.Input:
                    return MachineSlotSide.Input;
                case MachinePortKind.Fuel:
                    return MachineSlotSide.Fuel;
                case MachinePortKind.Output:
                    return MachineSlotSide.Output;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static MachinePortKind PortKind(MachineSlotSide side)
        {
            switch (side)
            {
                case MachineSlotSide.Input:
                    return MachinePortKind.Input;
                case MachineSlotSide.Fuel:
                    return MachinePortKind.Fuel;
                case MachineSlotSide.Output:
                    return MachinePortKind.Output;
                default:
                    throw new ArgumentOutOfRangeException(nameof(side), side, null);
            }
        }

        private static MachinePort Port(
            MachineNodeState node,
            MachinePortKind kind)
        {
            switch (kind)
            {
                case MachinePortKind.Input:
                    return node.Input;
                case MachinePortKind.Fuel:
                    return node.Fuel;
                case MachinePortKind.Output:
                    return node.Output;
                default:
                    return null;
            }
        }

        private bool TryGetSlot(
            MachineSlotSide side,
            int slotIndex,
            out InventorySlotPresentation presentation)
        {
            presentation = default;
            var slots = Slots(side);
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Count)
            {
                return false;
            }

            presentation = slots[slotIndex];
            return true;
        }

        private IReadOnlyList<InventorySlotPresentation> Slots(MachineSlotSide side)
        {
            switch (side)
            {
                case MachineSlotSide.Input:
                    return FindPort(MachinePortKind.Input)?.Slots;
                case MachineSlotSide.Fuel:
                    return FindPort(MachinePortKind.Fuel)?.Slots;
                case MachineSlotSide.Output:
                    return FindPort(MachinePortKind.Output)?.Slots;
                case MachineSlotSide.Player:
                    return _playerInventory?.Slots;
                default:
                    return null;
            }
        }


        private void SubmitTransfer(
            TransferEndpoint source,
            TransferEndpoint destination,
            StableId itemId,
            long quantity)
        {
            if (bridge == null
                || !bridge.IsAttached
                || _playerInventoryId.IsNone
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

        private CommandRejectionReason? FindLatestTransferRejection()
        {
            if (bridge == null || !bridge.IsAttached || _bound == null)
            {
                return null;
            }

            var rejections = bridge.Engine.State.GetCommandRejectionsCanonical();
            for (var index = rejections.Count - 1; index >= 0; index--)
            {
                var rejection = rejections[index];
                if (string.Equals(
                        rejection.Command.Kind,
                        SimulationCommandKinds.Transfer,
                        StringComparison.Ordinal)
                    && (rejection.Command.InitiatorId == _bound.NodeId
                        || rejection.Command.InitiatorId == _playerInventoryId)
                    && (rejection.Command.DestinationId == _bound.NodeId
                        || rejection.Command.DestinationId == _playerInventoryId))
                {
                    return rejection.Reason;
                }
            }

            return null;
        }

        private static string RejectionText(CommandRejectionReason reason)
        {
            switch (reason)
            {
                case CommandRejectionReason.TransferDestinationFull:
                    return "La destinazione è piena";
                case CommandRejectionReason.TransferNotAdmitted:
                    return "Questa porta non accetta l'oggetto";
                case CommandRejectionReason.InsufficientQuantity:
                    return "Materiale insufficiente";
                case CommandRejectionReason.TransferMalformed:
                    return "Trasferimento non valido";
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
            _panel.EnableInClassList("machine-panel--open", _isOpen);
            airshipInput?.SetUiInputSuppressed(_isOpen);
            mouseLook?.SetUiInputSuppressed(_isOpen);
        }

        private enum MachineSlotSide : byte
        {
            Input = 1,
            Fuel = 2,
            Output = 3,
            Player = 4
        }
    }
}

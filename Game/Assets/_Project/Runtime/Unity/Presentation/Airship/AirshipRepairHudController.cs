using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Unity.Airship;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Machines;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Airship
{
    /// <summary>
    /// The damaged panel of DVS-001: what the hull still needs, what the player
    /// actually carries, and the eight seconds of work once the bill is paid.
    ///
    /// Strictly read-only on the authoritative state. Pressing Installa queues a
    /// command and nothing else; the counter moves when the rule in phase 9 says
    /// it moved, never because this panel decided so.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AirshipRepairHudController : MonoBehaviour,
        IInventorySlotDragHost
    {
        /// <summary>Opaque pane keys handed to the shared drag controller.</summary>
        private const int HoldPane = 0;
        private const int BackpackPane = 1;

        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private TransferCommandBridge transferBridge;

        private readonly List<RequirementRow> _rows = new List<RequirementRow>();

        private AirshipSimulationBridge _bridge;
        private GameCatalog _catalog;
        private InventoryState _inventory;
        private StableId _inventoryId;
        private VisualElement _screen;
        private VisualElement _backdrop;
        private VisualElement _panel;
        private VisualElement _requirements;
        private VisualElement _preview;
        private VisualElement _backpack;
        private Label _backpackNote;
        private VisualElement _hold;
        private Label _holdNote;
        private InventoryState _holdInventory;
        private readonly List<InventorySlotView> _holdViews =
            new List<InventorySlotView>();
        private readonly List<InventorySlotView> _backpackViews =
            new List<InventorySlotView>();
        private InventorySlotDragController _drag;
        private AirshipPreviewRenderer _previewRenderer;
        private Vector2 _dragOrigin;
        private bool _orbiting;
        private bool _panning;
        private VisualElement _progressFill;
        private VisualElement _causeDot;
        private Label _status;
        private Label _requirementsNote;
        private Label _progressLabel;
        private Label _causeLabel;
        private bool _panelOpen;
        private bool _isBuilt;

        public bool PanelOpen => _panelOpen;

        public void ConfigureUiAsset(
            UIDocument uiDocument,
            StyleSheet repairStyleSheet)
        {
            document = uiDocument;
            styleSheet = repairStyleSheet;
        }

        public void ConfigureTransfers(TransferCommandBridge commandBridge)
        {
            transferBridge = commandBridge;
        }

        public void Bind(
            AirshipSimulationBridge bridge,
            InventoryState inventory,
            InventoryState hold,
            GameCatalog catalog,
            StableId inventoryId)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _inventory = inventory
                ?? throw new ArgumentNullException(nameof(inventory));
            _holdInventory = hold;
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _inventoryId = inventoryId;
            EnsureUi();
            EnsurePreview();
            Refresh();
        }

        private void EnsurePreview()
        {
            if (_previewRenderer == null)
            {
                _previewRenderer = gameObject.AddComponent<AirshipPreviewRenderer>();
            }

            var hull = _bridge != null && _bridge.Motor != null
                ? _bridge.Motor.VehicleRoot
                : null;
            if (hull == null || _previewRenderer.IsReady)
            {
                return;
            }

            _previewRenderer.Build(hull);
            if (_previewRenderer.Texture != null)
            {
                _preview.style.backgroundImage =
                    Background.FromRenderTexture(_previewRenderer.Texture);
            }
        }

        public void SetPanelOpen(bool open)
        {
            EnsureUi();
            _panelOpen = open;
            ApplyPanelState();
            if (open)
            {
                Refresh();
            }
        }

        /// <summary>
        /// Re-reads the committed state. The owner calls this on every commit so
        /// the counters follow the simulation instead of the button press.
        /// </summary>
        public void Refresh(
            InventoryState inventory = null,
            InventoryState hold = null)
        {
            if (inventory != null)
            {
                _inventory = inventory;
            }

            if (hold != null)
            {
                _holdInventory = hold;
            }

            if (!_isBuilt || _bridge == null || _catalog == null)
            {
                return;
            }

            if (!_bridge.TryGetRepairState(out var airship))
            {
                return;
            }

            for (var index = 0; index < _rows.Count; index++)
            {
                RefreshRow(_rows[index], airship);
            }

            RefreshStatus(airship);
            RefreshGrid(
                _hold,
                _holdViews,
                _holdNote,
                _holdInventory,
                HoldPane);
            RefreshGrid(
                _backpack,
                _backpackViews,
                _backpackNote,
                _inventory,
                BackpackPane);
        }

        /// <summary>
        /// The preview renders only while the panel is open, and only here: an
        /// always-on camera would cost a draw of the hull every frame of the
        /// game for a picture nobody is looking at.
        /// </summary>
        private void LateUpdate()
        {
            if (_panelOpen && _previewRenderer != null && _previewRenderer.IsReady)
            {
                _previewRenderer.RenderNow();
            }
        }

        private void RefreshRow(RequirementRow row, AirshipEntityState airship)
        {
            var installed = row.ItemId == ContentIds.IronPlate
                ? airship.InstalledIronPlates
                : airship.InstalledInsulatedCables;
            var owned = _inventory != null
                ? _inventory.Count(row.ItemId).Value
                : 0L;
            var complete = installed >= row.Required;

            row.Count.text = installed.ToString() + "/" + row.Required.ToString();
            row.Count.EnableInClassList("is-complete", complete);
            row.Owned.text = complete
                ? "installato"
                : "ne possiedi " + owned.ToString();
            row.Owned.EnableInClassList("is-short", !complete && owned <= 0L);

            var canInstall = !complete
                && owned > 0L
                && airship.RepairStatus == AirshipRepairStatus.Damaged;
            row.Install.SetEnabled(canInstall);
            row.Install.text = complete ? "COMPLETO" : "INSTALLA";
        }

        private void RefreshStatus(AirshipEntityState airship)
        {
            switch (airship.RepairStatus)
            {
                case AirshipRepairStatus.Repairing:
                {
                    var total = (float)AirshipRepairBill.RepairDurationTicks;
                    var done = total <= 0f
                        ? 1f
                        : 1f - (airship.RepairTicksRemaining / total);
                    _status.text = "RIPARAZIONE IN CORSO";
                    SetProgress(Mathf.Clamp01(done));
                    SetCause(string.Empty);
                    _requirementsNote.text = "componenti installati";
                    break;
                }

                case AirshipRepairStatus.Repaired:
                    _status.text = "OPERATIVA";
                    SetProgress(1f);
                    SetCause("Pronta al volo");
                    _requirementsNote.text = string.Empty;
                    break;

                default:
                    _status.text = "NON OPERATIVA";
                    SetProgress(0f);
                    SetCause(MissingCause(airship));
                    _requirementsNote.text = "servono per tornare a volare";
                    break;
            }

            // Same convention as machine-panel--blocked: a bar and a dot left in
            // gold would read as "working" on a hull that cannot fly.
            _panel.EnableInClassList(
                "repair-panel--blocked",
                airship.RepairStatus == AirshipRepairStatus.Damaged);
        }

        /// <summary>
        /// Always names a cause. "Non riparabile" with no reason would be the
        /// same dead end the plan forbids for machines.
        /// </summary>
        private string MissingCause(AirshipEntityState airship)
        {
            var missingPlates = Mathf.Max(
                0,
                AirshipRepairBill.RequiredIronPlates - airship.InstalledIronPlates);
            var missingCables = Mathf.Max(
                0,
                AirshipRepairBill.RequiredInsulatedCables
                    - airship.InstalledInsulatedCables);
            if (missingPlates == 0 && missingCables == 0)
            {
                return "Pronta per la riparazione";
            }

            var parts = new List<string>(2);
            if (missingPlates > 0)
            {
                parts.Add(missingPlates.ToString() + " Piastre di ferro");
            }

            if (missingCables > 0)
            {
                parts.Add(missingCables.ToString() + " Cavi isolati");
            }

            return "Mancano " + string.Join(" e ", parts);
        }

        private void SetProgress(float normalized)
        {
            _progressFill.style.width =
                new StyleLength(new Length(normalized * 100f, LengthUnit.Percent));
            _progressLabel.text =
                Mathf.RoundToInt(normalized * 100f).ToString() + "%";
        }

        private void SetCause(string message)
        {
            _causeLabel.text = message;
            _causeDot.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void Awake()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }

            if (document != null && !document.enabled)
            {
                document.enabled = true;
            }
        }

        private void OnEnable()
        {
            _isBuilt = false;
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
                && _screen != null
                && _panel != null
                && ReferenceEquals(_screen, root.Q<VisualElement>("repair-screen"))
                && ReferenceEquals(_panel, root.Q<VisualElement>("repair-panel")))
            {
                return;
            }

            // UIDocument clones a brand new visual tree after a domain reload, a
            // Play Mode recompile or a disable/enable cycle. The cached elements
            // then belong to a tree nobody renders any more.
            _isBuilt = false;
            _rows.Clear();

            // The tree is about to be replaced, so the cached views belong to a
            // hierarchy nobody renders any more.
            _holdViews.Clear();
            _backpackViews.Clear();

            root.pickingMode = PickingMode.Ignore;
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            _screen = root.Q<VisualElement>("repair-screen");
            _backdrop = root.Q<VisualElement>("repair-backdrop");
            _panel = root.Q<VisualElement>("repair-panel");
            _requirements = root.Q<VisualElement>("repair-requirements");
            _preview = root.Q<VisualElement>("repair-preview");
            _backpack = root.Q<VisualElement>("repair-backpack");
            _backpackNote = root.Q<Label>("backpack-note");
            _hold = root.Q<VisualElement>("repair-hold");
            _holdNote = root.Q<Label>("hold-note");
            _progressFill = root.Q<VisualElement>("progress-fill");
            _causeDot = root.Q<VisualElement>("cause-dot");
            _status = root.Q<Label>("repair-status");
            _requirementsNote = root.Q<Label>("requirements-note");
            _progressLabel = root.Q<Label>("progress-label");
            _causeLabel = root.Q<Label>("cause-label");
            if (_screen == null || _backdrop == null || _panel == null
                || _requirements == null || _progressFill == null
                || _causeDot == null || _status == null
                || _requirementsNote == null || _progressLabel == null
                || _causeLabel == null || _preview == null
                || _backpack == null || _backpackNote == null
                || _hold == null || _holdNote == null)
            {
                throw new InvalidOperationException(
                    "AirshipRepairHUD.uxml is missing a required named element.");
            }

            _screen.pickingMode = PickingMode.Ignore;
            _drag = new InventorySlotDragController(this);
            _drag.AttachRoot(root);
            RegisterPreviewInput();
            BuildRows();
            _isBuilt = true;
            ApplyPanelState();
        }

        /// <summary>
        /// Left button orbits, right button pans, wheel zooms. The element
        /// captures the pointer so a drag that leaves the stage keeps working.
        /// </summary>
        private void RegisterPreviewInput()
        {
            _preview.pickingMode = PickingMode.Position;
            _preview.RegisterCallback<PointerDownEvent>(evt =>
            {
                _orbiting = evt.button == 0;
                _panning = evt.button == 1;
                _dragOrigin = evt.position;
                _preview.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            _preview.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_orbiting && !_panning)
                {
                    return;
                }

                var current = (Vector2)evt.position;
                var delta = current - _dragOrigin;
                _dragOrigin = current;
                if (_orbiting)
                {
                    _previewRenderer?.Orbit(delta);
                }
                else
                {
                    _previewRenderer?.Pan(delta);
                }

                evt.StopPropagation();
            });
            _preview.RegisterCallback<PointerUpEvent>(evt =>
            {
                _orbiting = false;
                _panning = false;
                _preview.ReleasePointer(evt.pointerId);
            });
            _preview.RegisterCallback<WheelEvent>(evt =>
            {
                _previewRenderer?.Zoom(evt.delta.y);
                evt.StopPropagation();
            });
        }

        /// <summary>
        /// Fills a grid with real <see cref="InventorySlotView"/> tiles and
        /// hands each one to the shared drag controller. Nothing about picking
        /// up, splitting or dropping is written here: that behaviour belongs to
        /// the inventory grid itself, not to whichever panel is showing it.
        /// </summary>
        private void RefreshGrid(
            VisualElement grid,
            List<InventorySlotView> views,
            Label note,
            InventoryState inventory,
            int pane)
        {
            if (inventory == null || _catalog == null)
            {
                grid.Clear();
                views.Clear();
                note.text = string.Empty;
                return;
            }

            // Views are created once and rebound afterwards, the same way the
            // crate and machine panels do it. Rebuilding them on every committed
            // tick destroys the element under the pointer twenty times a second,
            // which flickers :hover and would tear the tiles out from under a
            // drag in progress.
            if (views.Count != inventory.SlotCount)
            {
                _drag.Cancel();
                _drag.ClearRegisteredSlots(pane);
                grid.Clear();
                views.Clear();
                for (var index = 0; index < inventory.SlotCount; index++)
                {
                    var view = new InventorySlotView(
                        index,
                        "repair-store-slot",
                        "repair-backpack-slot",
                        inventory.SlotCount);
                    views.Add(view);
                    grid.Add(view.Root);
                    _drag.RegisterSlot(view, pane, index);
                }
            }

            var used = 0;
            for (var index = 0; index < views.Count; index++)
            {
                var slot = inventory.GetSlot(index);
                if (slot.Stack.HasValue
                    && _catalog.TryGetItem(
                        slot.Stack.Value.ItemId,
                        out var definition))
                {
                    used++;
                    var stack = slot.Stack.Value;
                    views[index].Bind(
                        InventoryHudPresenter.ProjectSlot(
                            index,
                            stack.ItemId,
                            stack.Quantity.Value,
                            definition),
                        isSelected: false);
                }
                else
                {
                    views[index].Bind(
                        InventorySlotPresentation.Empty(index),
                        isSelected: false);
                }

                _drag.RebindSlot(views[index], pane, index);
            }

            note.text = used.ToString() + "/" + inventory.SlotCount.ToString();
        }

        // ------------------------------------------------ IInventorySlotDragHost

        public bool IsDragEnabled => _panelOpen;

        public VisualElement DragRoot =>
            document != null ? document.rootVisualElement : null;

        public string CursorNamePrefix => "repair-cursor-stack";

        public string CursorVariantClass => "repair-backpack-slot";

        public bool TryGetSlot(
            int pane,
            int slotIndex,
            out InventorySlotPresentation slot)
        {
            slot = default;
            var inventory = InventoryFor(pane);
            if (inventory == null
                || _catalog == null
                || slotIndex < 0
                || slotIndex >= inventory.SlotCount)
            {
                return false;
            }

            var stack = inventory.GetSlot(slotIndex).Stack;
            if (!stack.HasValue
                || !_catalog.TryGetItem(stack.Value.ItemId, out var definition))
            {
                slot = InventorySlotPresentation.Empty(slotIndex);
                return true;
            }

            slot = InventoryHudPresenter.ProjectSlot(
                slotIndex,
                stack.Value.ItemId,
                stack.Value.Quantity.Value,
                definition);
            return true;
        }

        public int SlotCount(int pane)
        {
            var inventory = InventoryFor(pane);
            return inventory != null ? inventory.SlotCount : 0;
        }

        /// <summary>
        /// Both panes are plain inventories, so both accept reordering. The hold
        /// is the player's own storage too, not a machine port with admission
        /// rules.
        /// </summary>
        public bool SupportsReorder(int pane) => true;

        public void QuickMove(
            int pane,
            int slotIndex,
            InventorySlotPresentation slot)
        {
            var destination = pane == BackpackPane ? HoldPane : BackpackPane;
            MoveCursorStack(pane, destination, slot.ItemId, slot.Quantity);
        }

        public void Reorder(
            int pane,
            int fromSlotIndex,
            int toSlotIndex,
            long quantity)
        {
            if (transferBridge == null
                || !transferBridge.IsAttached
                || quantity <= 0L)
            {
                return;
            }

            transferBridge.SubmitSlotMove(
                InventoryIdFor(pane),
                fromSlotIndex,
                toSlotIndex,
                new NonNegativeQuantity(quantity));
        }

        public long MoveCursorStack(
            int fromPane,
            int toPane,
            StableId itemId,
            long requested)
        {
            var destination = InventoryFor(toPane);
            if (transferBridge == null
                || !transferBridge.IsAttached
                || destination == null
                || requested <= 0L)
            {
                return 0L;
            }

            // Capped by what the destination can actually hold, so a refused
            // remainder stays on the cursor instead of vanishing.
            var allowed = Math.Min(
                requested,
                destination.StorableQuantity(itemId).Value);
            if (allowed <= 0L)
            {
                return 0L;
            }

            transferBridge.SubmitTransfer(
                TransferEndpoint.Inventory(InventoryIdFor(fromPane)),
                TransferEndpoint.Inventory(InventoryIdFor(toPane)),
                itemId,
                new NonNegativeQuantity(allowed));
            return allowed;
        }

        private InventoryState InventoryFor(int pane)
        {
            return pane == HoldPane ? _holdInventory : _inventory;
        }

        private StableId InventoryIdFor(int pane)
        {
            return pane == HoldPane
                ? CML.Unity.Factory.FactoryLineSimulationRoot.AirshipHoldId
                : _inventoryId;
        }

        private void BuildRows()
        {
            _requirements.Clear();
            AddRow(
                AirshipRepairBill.IronPlateItemId,
                AirshipRepairBill.RequiredIronPlates,
                "PIASTRA DI FERRO");
            AddRow(
                AirshipRepairBill.InsulatedCableItemId,
                AirshipRepairBill.RequiredInsulatedCables,
                "CAVO ISOLATO");
        }

        private void AddRow(StableId itemId, int required, string fallbackName)
        {
            var container = new VisualElement();
            container.AddToClassList("repair-row");

            // inventory-slot gives the tile its frame, fill and hairlines, the
            // same ones every slot in the game already has. Without it the icon
            // floats with nothing around it.
            var iconHost = new VisualElement();
            iconHost.AddToClassList("inventory-slot");
            iconHost.AddToClassList("repair-row-slot");
            iconHost.Add(BuildIcon(itemId));
            container.Add(iconHost);

            var text = new VisualElement();
            text.AddToClassList("repair-row-text");
            var name = new Label(fallbackName);
            name.AddToClassList("repair-row-name");
            var owned = new Label(string.Empty);
            owned.AddToClassList("repair-row-owned");
            text.Add(name);
            text.Add(owned);
            container.Add(text);

            var count = new Label("0/" + required.ToString());
            count.AddToClassList("repair-row-count");
            container.Add(count);

            var install = new Button { text = "INSTALLA" };
            install.AddToClassList("repair-install");
            var installedItem = itemId;
            install.clicked += () => QueueInstall(installedItem);
            container.Add(install);

            _requirements.Add(container);
            _rows.Add(new RequirementRow(itemId, required, count, owned, install));
        }

        /// <summary>
        /// Uses the same icons as every other panel. An item with no dedicated
        /// icon kind yet falls back to the generic one rather than inventing a
        /// second visual language here.
        /// </summary>
        private VisualElement BuildIcon(StableId itemId)
        {
            if (_catalog != null && _catalog.TryGetItem(itemId, out var definition))
            {
                return InventorySlotView.CreateIcon(
                    InventoryHudPresenter.ProjectSlot(0, itemId, 1L, definition));
            }

            var placeholder = new VisualElement();
            placeholder.AddToClassList("item-icon");
            return placeholder;
        }

        private void QueueInstall(StableId itemId)
        {
            if (_bridge == null || !_bridge.IsInitialized || _inventoryId.IsNone)
            {
                return;
            }

            // One unit per press, and the boundary only accepts it: whether the
            // component is really installed is decided by the rule.
            _bridge.QueueRepairInstall(_inventoryId, itemId, 1L);
        }

        private void ApplyPanelState()
        {
            if (!_isBuilt)
            {
                return;
            }

            var display = _panelOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _panel.style.display = display;
            _backdrop.style.display = display;
            _screen.pickingMode = _panelOpen
                ? PickingMode.Position
                : PickingMode.Ignore;
        }

        private sealed class RequirementRow
        {
            public RequirementRow(
                StableId itemId,
                int required,
                Label count,
                Label owned,
                Button install)
            {
                ItemId = itemId;
                Required = required;
                Count = count;
                Owned = owned;
                Install = install;
            }

            public StableId ItemId { get; }

            public int Required { get; }

            public Label Count { get; }

            public Label Owned { get; }

            public Button Install { get; }
        }
    }
}

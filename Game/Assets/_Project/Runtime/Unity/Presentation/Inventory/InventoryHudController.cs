using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Unity.Airship;
using CML.Unity.Presentation.Crafting;
using CML.Unity.Presentation.Machines;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Inventory
{
    /// <summary>
    /// Owns only inventory presentation and modal UI input. The authoritative
    /// immutable InventoryState remains outside the visual tree.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryHudController : MonoBehaviour
    {
        private static readonly Key[] HotbarKeys =
        {
            Key.Digit1,
            Key.Digit2,
            Key.Digit3,
            Key.Digit4,
            Key.Digit5,
            Key.Digit6,
            Key.Digit7,
            Key.Digit8
        };

        private static readonly StableId PlayerInventoryInstanceId =
            new StableId(0x504C415945525F49UL, 0x4E56454E544F5259UL);

        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private AirshipInputAdapter airshipInput;
        [SerializeField] private FirstPersonMouseLook mouseLook;
        [SerializeField] private bool seedReviewContents;

        [NonSerialized] private InventorySlotView[] _hotbarViews =
            new InventorySlotView[InventoryHudPresenter.HotbarSlotCount];
        [NonSerialized] private InventorySlotView[] _inventoryViews =
            new InventorySlotView[InventoryHudPresenter.PlayerSlotCount];

        [NonSerialized] private GameCatalog _catalog;
        [NonSerialized] private InventoryState _boundState;
        [NonSerialized] private VisualElement _screen;
        [NonSerialized] private VisualElement _backdrop;
        [NonSerialized] private VisualElement _inventoryPanel;
        [NonSerialized] private VisualElement _hotbar;
        [NonSerialized] private Label _capacityLabel;
        [NonSerialized] private Label _selectedItemLabel;
        [NonSerialized] private VisualElement _quickCraftList;
        [NonSerialized] private Label _quickCraftFeedback;
        [NonSerialized] private readonly List<QuickCraftCardView>
            _quickCraftViews = new List<QuickCraftCardView>();
        [NonSerialized] private bool _isBuilt;
        [NonSerialized] private bool _inventoryOpen;
        [NonSerialized] private bool _gameplayPresentationVisible = true;
        [NonSerialized] private int _selectedHotbarIndex;

        [SerializeField] private TransferCommandBridge bridge;
        [SerializeField] private CraftingCommandBridge craftingBridge;

        // Pila tenuta sul cursore. Stesso modello del pannello delle casse, così
        // il gesto è lo stesso ovunque si guardi l'inventario.
        [NonSerialized] private bool _hasCursorStack;
        [NonSerialized] private int _cursorSourceIndex = -1;
        [NonSerialized] private StableId _cursorItemId;
        [NonSerialized] private long _cursorQuantity;
        [NonSerialized] private long _cursorSourceShownQuantity;
        [NonSerialized] private InventorySlotPresentation _cursorPresentation;
        [NonSerialized] private VisualElement _cursorSourceRoot;
        [NonSerialized] private InventorySlotView _cursorPreviewView;
        [NonSerialized] private VisualElement _cursorPreview;
        [NonSerialized] private VisualElement _hoveredDestinationRoot;

        public bool InventoryOpen => _inventoryOpen;

        public bool GameplayPresentationVisible =>
            _gameplayPresentationVisible;

        public int SelectedHotbarIndex => _selectedHotbarIndex;

        public InventoryState BoundState => _boundState;

        public GameCatalog BoundCatalog => _catalog;

        public bool TryGetSelectedHotbarItem(
            out StableId itemId,
            out long quantity)
        {
            itemId = StableId.None;
            quantity = 0L;
            if (_boundState == null
                || _selectedHotbarIndex < 0
                || _selectedHotbarIndex >= InventoryHudPresenter.HotbarSlotCount
                || _selectedHotbarIndex >= _boundState.SlotCount)
            {
                return false;
            }

            var slot = _boundState.GetSlot(_selectedHotbarIndex);
            if (!slot.Stack.HasValue)
            {
                return false;
            }

            itemId = slot.Stack.Value.ItemId;
            quantity = slot.Stack.Value.Quantity.Value;
            return quantity > 0L;
        }

        public void ConfigureUiAsset(
            UIDocument uiDocument,
            StyleSheet inventoryStyleSheet)
        {
            document = uiDocument;
            styleSheet = inventoryStyleSheet;
        }

        public void ConfigureGameplayInput(
            AirshipInputAdapter inputAdapter,
            FirstPersonMouseLook firstPersonMouseLook,
            bool useReviewContents)
        {
            airshipInput = inputAdapter;
            mouseLook = firstPersonMouseLook;
            seedReviewContents = useReviewContents;
        }

        /// <summary>
        /// Collega il canale dei comandi. Senza, il pannello resta di sola
        /// lettura: riordinare uno slot è una mutazione dello stato canonico e
        /// non può essere deciso qui.
        /// </summary>
        public void ConfigureCommandBridge(TransferCommandBridge commandBridge)
        {
            bridge = commandBridge;
        }

        public void ConfigureCrafting(CraftingCommandBridge commandBridge)
        {
            craftingBridge = commandBridge;
            Refresh();
        }

        public void BindInventory(
            InventoryState inventory,
            GameCatalog catalog)
        {
            _boundState =
                inventory ?? throw new ArgumentNullException(nameof(inventory));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Refresh();
        }

        public void SetInventoryOpen(bool open)
        {
            EnsureUi();
            if (!open && _hasCursorStack)
            {
                ClearCursorStack();
            }

            if (_inventoryOpen == open)
            {
                ApplyModalState();
                return;
            }

            _inventoryOpen = open;
            ApplyModalState();
        }

        public void ToggleInventory()
        {
            SetInventoryOpen(!_inventoryOpen);
        }

        public void SetGameplayPresentationVisible(bool visible)
        {
            EnsureUi();
            if (_gameplayPresentationVisible == visible)
            {
                return;
            }

            _gameplayPresentationVisible = visible;
            if (!visible)
            {
                if (_hasCursorStack)
                {
                    ClearCursorStack();
                }

                _inventoryOpen = false;
            }

            ApplyModalState();
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

            EnsureRuntimeCollections();
            CreateReviewInventoryIfRequired();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }

            if (document != null && !document.enabled)
            {
                document.enabled = true;
            }

            // Script recompilation while playing reconstructs managed assemblies.
            // Recreate the transient view/state graph before any build controller
            // asks which hotbar item is selected.
            EnsureRuntimeCollections();
            CreateReviewInventoryIfRequired();
            EnsureUi();
            ApplyModalState();
        }

        private void Start()
        {
            EnsureUi();
            ApplyModalState();
        }

        private void Update()
        {
            EnsureUi();
            if (!_gameplayPresentationVisible)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                ToggleInventory();
                return;
            }

            if (_inventoryOpen &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                SetInventoryOpen(false);
                return;
            }

            for (var index = 0; index < HotbarKeys.Length; index++)
            {
                if (keyboard[HotbarKeys[index]].wasPressedThisFrame)
                {
                    SelectHotbarSlot(index);
                    break;
                }
            }
        }

        private void OnDisable()
        {
            ClearCursorStack();
            _isBuilt = false;
            _screen = null;
            _backdrop = null;
            _inventoryPanel = null;
            _hotbar = null;
            _capacityLabel = null;
            _selectedItemLabel = null;
            _quickCraftList = null;
            _quickCraftFeedback = null;
            _quickCraftViews.Clear();

            if (!Application.isPlaying)
            {
                return;
            }

            _inventoryOpen = false;
            airshipInput?.SetUiInputSuppressed(false);
            mouseLook?.SetUiInputSuppressed(false);
        }

        private void CreateReviewInventoryIfRequired()
        {
            if (_boundState != null)
            {
                return;
            }

            _catalog = BootstrapCatalog.Load();
            _boundState = seedReviewContents
                ? InventoryState.Restore(
                    PlayerInventoryInstanceId,
                    _catalog,
                    ContentIds.PlayerInventory,
                    new[]
                    {
                        new InventoryStackRecord(
                            0,
                            ContentIds.CrudePickaxe,
                            new NonNegativeQuantity(1)),
                        new InventoryStackRecord(
                            1,
                            ContentIds.RawIron,
                            new NonNegativeQuantity(23)),
                        new InventoryStackRecord(
                            2,
                            ContentIds.IronIngot,
                            new NonNegativeQuantity(8)),
                        new InventoryStackRecord(
                            3,
                            ContentIds.IronPlate,
                            new NonNegativeQuantity(3))
                    })
                : InventoryState.CreateEmpty(
                    PlayerInventoryInstanceId,
                    _catalog,
                    ContentIds.PlayerInventory);
        }

        private void EnsureUi()
        {
            EnsureRuntimeCollections();
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
                && _hotbar != null
                && ReferenceEquals(
                    _screen,
                    root.Q<VisualElement>("inventory-screen"))
                && ReferenceEquals(
                    _hotbar,
                    root.Q<VisualElement>("hotbar")))
            {
                return;
            }

            // UIDocument throws its visual tree away and clones a brand new one
            // on a domain reload, on a Play Mode recompile and every time the
            // component is disabled and re-enabled. _isBuilt survives those
            // rebuilds while the cached VisualElements do not, so trusting the
            // flag alone leaves an empty Hotbar and a Tab panel that never
            // opens again. Rebind against the live tree, exactly like the chest
            // and machine panels already do.
            _isBuilt = false;
            ClearCursorStack();
            _quickCraftViews.Clear();

            // This UIDocument shares its panel with the machine and chest HUDs.
            // Their document roots overlap the whole screen, so a pickable root
            // would consume pointer input before it can reach a lower sorting
            // order document. The actual interactive children (panel, backdrop
            // and slots) remain pickable and still receive/bubble events.
            root.pickingMode = PickingMode.Ignore;

            if (styleSheet != null &&
                !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            _screen = root.Q<VisualElement>("inventory-screen");
            _backdrop = root.Q<VisualElement>("inventory-backdrop");
            _inventoryPanel = root.Q<VisualElement>("inventory-panel");
            _hotbar = root.Q<VisualElement>("hotbar");
            _capacityLabel = root.Q<Label>("capacity-label");
            _selectedItemLabel = root.Q<Label>("selected-item-label");
            _quickCraftList = root.Q<VisualElement>("quick-craft-list");
            _quickCraftFeedback = root.Q<Label>("quick-craft-feedback");
            var quickbarGrid =
                root.Q<VisualElement>("quickbar-grid");
            var backpackGrid =
                root.Q<VisualElement>("backpack-grid");

            if (_screen == null ||
                _backdrop == null ||
                _inventoryPanel == null ||
                _hotbar == null ||
                quickbarGrid == null ||
                backpackGrid == null ||
                _quickCraftList == null ||
                _quickCraftFeedback == null)
            {
                throw new InvalidOperationException(
                    "InventoryHUD.uxml is missing a required named element.");
            }

            _screen.pickingMode = PickingMode.Ignore;
            _hotbar.Clear();
            quickbarGrid.Clear();
            backpackGrid.Clear();
            root.UnregisterCallback<PointerMoveEvent>(HandlePointerMove);
            root.RegisterCallback<PointerMoveEvent>(HandlePointerMove);

            for (var index = 0;
                 index < InventoryHudPresenter.HotbarSlotCount;
                 index++)
            {
                _hotbarViews[index] =
                    new InventorySlotView(index, hotbar: true);
                _hotbar.Add(_hotbarViews[index].Root);
            }

            for (var index = 0;
                 index < InventoryHudPresenter.PlayerSlotCount;
                 index++)
            {
                _inventoryViews[index] =
                    new InventorySlotView(index, hotbar: false);
                var captured = index;
                var capturedRoot = _inventoryViews[index].Root;
                capturedRoot.RegisterCallback<PointerDownEvent>(
                    evt => HandleSlotPointerDown(evt, captured, capturedRoot),
                    TrickleDown.TrickleDown);
                if (index < InventoryHudPresenter.HotbarSlotCount)
                {
                    quickbarGrid.Add(_inventoryViews[index].Root);
                }
                else
                {
                    backpackGrid.Add(_inventoryViews[index].Root);
                }
            }

            _isBuilt = true;
            Refresh();
        }

        private void EnsureRuntimeCollections()
        {
            if (_hotbarViews == null
                || _hotbarViews.Length
                    != InventoryHudPresenter.HotbarSlotCount)
            {
                _hotbarViews =
                    new InventorySlotView[
                        InventoryHudPresenter.HotbarSlotCount];
            }

            if (_inventoryViews == null
                || _inventoryViews.Length
                    != InventoryHudPresenter.PlayerSlotCount)
            {
                _inventoryViews =
                    new InventorySlotView[
                        InventoryHudPresenter.PlayerSlotCount];
            }
        }

        private void SelectHotbarSlot(int index)
        {
            if (index < 0 ||
                index >= InventoryHudPresenter.HotbarSlotCount ||
                _selectedHotbarIndex == index)
            {
                return;
            }

            _selectedHotbarIndex = index;
            Refresh();
        }

        /// <summary>
        /// Prendi e posa dentro l'inventario del giocatore.
        ///
        /// Sinistro: prende tutta la pila, o la posa — scambiando se lo slot di
        /// arrivo tiene altro. Destro: prende metà, oppure deposita una unità
        /// sola. Shift+sinistro: manda la pila nell'altra fascia, cioè dalla
        /// cintura allo zaino e viceversa, che qui è l'unico "altro contenitore"
        /// che esista.
        ///
        /// Nessuna di queste mosse tocca lo stato: viene accodato un comando e
        /// la regola decide. Il pannello si limita a leggere il risultato al
        /// prossimo <see cref="Refresh"/>.
        /// </summary>
        private void HandleSlotPointerDown(
            PointerDownEvent evt,
            int slotIndex,
            VisualElement slotRoot)
        {
            var isLeft = evt.button == 0;
            var isRight = evt.button == 1;
            if (!_inventoryOpen
                || _boundState == null
                || (!isLeft && !isRight))
            {
                return;
            }

            var slot = _boundState.GetSlot(slotIndex);

            if (evt.shiftKey && isLeft && !_hasCursorStack)
            {
                if (slot.Stack.HasValue)
                {
                    SubmitQuickMove(
                        slotIndex,
                        slot.Stack.Value.ItemId,
                        slot.Stack.Value.Quantity.Value);
                }

                evt.StopPropagation();
                return;
            }

            if (!_hasCursorStack)
            {
                if (!slot.Stack.HasValue)
                {
                    return;
                }

                var presentation =
                    InventoryHudPresenter.Project(_boundState, _catalog)
                        .Slots[slotIndex];
                var held = presentation.Quantity;
                BeginCursorStack(
                    slotIndex,
                    slotRoot,
                    presentation,
                    evt.position,
                    isRight ? (held + 1L) / 2L : held);
                evt.StopPropagation();
                return;
            }

            if (slotIndex == _cursorSourceIndex)
            {
                ClearCursorStack();
                evt.StopPropagation();
                return;
            }

            if (!TryPlanCursorMove(
                    slotIndex,
                    isRight,
                    out var moved))
            {
                UpdateCursorPresentation(evt.position);
                evt.StopPropagation();
                return;
            }

            if (!SubmitSlotMove(
                    _cursorSourceIndex,
                    slotIndex,
                    moved))
            {
                UpdateCursorPresentation(evt.position);
                evt.StopPropagation();
                return;
            }

            _cursorQuantity -= moved;
            if (_cursorQuantity <= 0L)
            {
                ClearCursorStack();
            }
            else
            {
                UpdateCursorPreviewQuantity();
                UpdateCursorPresentation(evt.position);
            }

            evt.StopPropagation();
        }

        /// <summary>
        /// Primo slot utile nell'altra fascia: prima uno che contenga già lo
        /// stesso oggetto — così le pile si uniscono invece di frammentarsi — e
        /// solo dopo uno vuoto.
        /// </summary>
        private void SubmitQuickMove(
            int sourceSlotIndex,
            StableId itemId,
            long quantity)
        {
            if (_boundState == null
                || _catalog == null
                || !_catalog.TryGetItem(itemId, out var item)
                || quantity <= 0L)
            {
                return;
            }

            var hotbarCount = InventoryHudPresenter.HotbarSlotCount;
            var sourceIsHotbar = sourceSlotIndex < hotbarCount;
            var start = sourceIsHotbar ? hotbarCount : 0;
            var end = sourceIsHotbar ? _boundState.SlotCount : hotbarCount;

            var remaining = quantity;
            for (var index = start;
                 index < end && remaining > 0L;
                 index++)
            {
                var candidate = _boundState.GetSlot(index);
                if (!candidate.Stack.HasValue
                    || candidate.Stack.Value.ItemId != itemId)
                {
                    continue;
                }

                var room =
                    item.MaxStack - candidate.Stack.Value.Quantity.Value;
                var moved = Math.Min(remaining, Math.Max(0L, room));
                if (moved <= 0L)
                {
                    continue;
                }

                if (SubmitSlotMove(
                        sourceSlotIndex,
                        index,
                        moved))
                {
                    remaining -= moved;
                }
            }

            for (var index = start;
                 index < end && remaining > 0L;
                 index++)
            {
                if (_boundState.GetSlot(index).Stack.HasValue)
                {
                    continue;
                }

                var moved = Math.Min(remaining, item.MaxStack);
                if (SubmitSlotMove(
                        sourceSlotIndex,
                        index,
                        moved))
                {
                    remaining -= moved;
                }
            }
        }

        private void HandlePointerMove(PointerMoveEvent evt)
        {
            if (_hasCursorStack)
            {
                UpdateCursorPresentation(evt.position);
            }
        }

        private void BeginCursorStack(
            int sourceSlotIndex,
            VisualElement sourceRoot,
            InventorySlotPresentation presentation,
            Vector2 panelPosition,
            long quantity)
        {
            var picked =
                Math.Max(1L, Math.Min(quantity, presentation.Quantity));
            _hasCursorStack = true;
            _cursorSourceIndex = sourceSlotIndex;
            _cursorItemId = presentation.ItemId;
            _cursorQuantity = picked;
            _cursorSourceShownQuantity =
                presentation.Quantity - picked;
            _cursorPresentation = presentation;
            _cursorSourceRoot = sourceRoot;
            ApplyHeldSourcePresentation();

            _cursorPreviewView = new InventorySlotView(
                sourceSlotIndex,
                "inventory-cursor-stack",
                "backpack-slot",
                InventoryHudPresenter.PlayerSlotCount);
            _cursorPreviewView.Bind(
                presentation.WithQuantity(picked),
                isSelected: true);
            _cursorPreview = _cursorPreviewView.Root;
            _cursorPreview.name = "inventory-cursor-stack";
            SetPickingIgnoredRecursively(_cursorPreview);
            _cursorPreview.style.position = Position.Absolute;
            _cursorPreview.style.width =
                Mathf.Max(
                    1f,
                    sourceRoot?.resolvedStyle.width ?? 58f);
            _cursorPreview.style.height =
                Mathf.Max(
                    1f,
                    sourceRoot?.resolvedStyle.height ?? 58f);
            _cursorPreview.style.opacity = 0.90f;
            document.rootVisualElement.Add(_cursorPreview);
            _cursorPreview.BringToFront();
            UpdateCursorPresentation(panelPosition);
        }

        private bool TryPlanCursorMove(
            int destinationSlotIndex,
            bool isRightClick,
            out long moved)
        {
            moved = 0L;
            if (!_hasCursorStack
                || _boundState == null
                || _catalog == null
                || destinationSlotIndex < 0
                || destinationSlotIndex >= _boundState.SlotCount
                || destinationSlotIndex == _cursorSourceIndex)
            {
                return false;
            }

            var requested =
                isRightClick ? 1L : _cursorQuantity;
            var destination =
                _boundState.GetSlot(destinationSlotIndex);
            if (!destination.Stack.HasValue)
            {
                moved = requested;
                return true;
            }

            var destinationStack = destination.Stack.Value;
            if (destinationStack.ItemId == _cursorItemId)
            {
                if (!_catalog.TryGetItem(
                        _cursorItemId,
                        out var item))
                {
                    return false;
                }

                var room =
                    item.MaxStack - destinationStack.Quantity.Value;
                moved = Math.Min(requested, Math.Max(0L, room));
                return moved > 0L;
            }

            // A left click swaps only a complete source stack. A right click
            // over a different item deliberately does nothing.
            if (!isRightClick
                && _cursorSourceShownQuantity == 0L)
            {
                moved = _cursorQuantity;
                return true;
            }

            return false;
        }

        private void UpdateCursorPreviewQuantity()
        {
            if (_cursorPreviewView == null
                || _cursorQuantity <= 0L)
            {
                return;
            }

            _cursorPreviewView.Bind(
                _cursorPresentation.WithQuantity(_cursorQuantity),
                isSelected: true);
        }

        private void UpdateCursorPresentation(
            Vector2 panelPosition)
        {
            if (_cursorPreview != null)
            {
                var rootBounds =
                    document.rootVisualElement.worldBound;
                var previewWidth =
                    _cursorPreview.resolvedStyle.width;
                var previewHeight =
                    _cursorPreview.resolvedStyle.height;
                if (!float.IsFinite(previewWidth)
                    || previewWidth <= 0f)
                {
                    previewWidth = 58f;
                }

                if (!float.IsFinite(previewHeight)
                    || previewHeight <= 0f)
                {
                    previewHeight = 58f;
                }

                _cursorPreview.style.left =
                    panelPosition.x
                    - rootBounds.x
                    - previewWidth * 0.5f;
                _cursorPreview.style.top =
                    panelPosition.y
                    - rootBounds.y
                    - previewHeight * 0.5f;
            }

            VisualElement nextDestination = null;
            if (TryResolveSlotAt(
                    panelPosition,
                    out var destinationSlotIndex,
                    out var destinationRoot)
                && destinationSlotIndex != _cursorSourceIndex)
            {
                nextDestination = destinationRoot;
            }

            if (_hoveredDestinationRoot == nextDestination)
            {
                return;
            }

            _hoveredDestinationRoot?.EnableInClassList(
                "inventory-slot--selected",
                false);
            _hoveredDestinationRoot = nextDestination;
            _hoveredDestinationRoot?.EnableInClassList(
                "inventory-slot--selected",
                true);
        }

        private bool TryResolveSlotAt(
            Vector2 panelPosition,
            out int slotIndex,
            out VisualElement slotRoot)
        {
            slotIndex = -1;
            slotRoot = null;
            var picked =
                document?.rootVisualElement?.panel?.Pick(
                    panelPosition);
            while (picked != null)
            {
                const string Prefix = "inventory-slot-";
                if (!string.IsNullOrEmpty(picked.name)
                    && picked.name.StartsWith(
                        Prefix,
                        StringComparison.Ordinal)
                    && int.TryParse(
                        picked.name.Substring(Prefix.Length),
                        out slotIndex))
                {
                    slotRoot = picked;
                    return true;
                }

                picked = picked.parent;
            }

            return false;
        }

        private static void SetPickingIgnoredRecursively(
            VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            for (var index = 0;
                 index < element.hierarchy.childCount;
                 index++)
            {
                SetPickingIgnoredRecursively(
                    element.hierarchy[index]);
            }
        }

        private void ApplyHeldSourcePresentation()
        {
            if (!_hasCursorStack
                || _cursorSourceRoot == null
                || _cursorSourceIndex < 0
                || _cursorSourceIndex >= _inventoryViews.Length)
            {
                return;
            }

            _cursorSourceRoot.AddToClassList(
                "inventory-slot--held");
            if (_cursorSourceShownQuantity > 0L)
            {
                _inventoryViews[_cursorSourceIndex].Bind(
                    _cursorPresentation.WithQuantity(
                        _cursorSourceShownQuantity),
                    _cursorSourceIndex == _selectedHotbarIndex);
                SetSlotStackVisible(
                    _cursorSourceRoot,
                    visible: true);
            }
            else
            {
                SetSlotStackVisible(
                    _cursorSourceRoot,
                    visible: false);
            }
        }

        private static void SetSlotStackVisible(
            VisualElement slotRoot,
            bool visible)
        {
            if (slotRoot == null)
            {
                return;
            }

            var visibility =
                visible ? Visibility.Visible : Visibility.Hidden;
            var icon = slotRoot.Q<VisualElement>(
                className: "slot-icon-host");
            var quantity = slotRoot.Q<Label>(
                className: "slot-quantity");
            var durability = slotRoot.Q<VisualElement>(
                className: "slot-durability-track");
            if (icon != null)
            {
                icon.style.visibility = visibility;
            }

            if (quantity != null)
            {
                quantity.style.visibility = visibility;
            }

            if (durability != null)
            {
                durability.style.visibility = visibility;
            }
        }

        private bool SubmitSlotMove(
            int sourceSlotIndex,
            int destinationSlotIndex,
            long quantity)
        {
            if (bridge == null
                || !bridge.IsAttached
                || _boundState == null
                || quantity <= 0L)
            {
                return false;
            }

            bridge.SubmitSlotMove(
                _boundState.InventoryId,
                sourceSlotIndex,
                destinationSlotIndex,
                new NonNegativeQuantity(quantity));
            return true;
        }

        private void ClearCursorStack()
        {
            var sourceRoot = _cursorSourceRoot;

            _hasCursorStack = false;
            _cursorSourceIndex = -1;
            _cursorItemId = StableId.None;
            _cursorQuantity = 0L;
            _cursorSourceShownQuantity = 0L;
            _cursorPresentation = default;
            _cursorSourceRoot = null;
            _cursorPreviewView = null;

            if (sourceRoot != null)
            {
                sourceRoot.RemoveFromClassList(
                    "inventory-slot--held");
                SetSlotStackVisible(sourceRoot, visible: true);
            }

            _hoveredDestinationRoot?.EnableInClassList(
                "inventory-slot--selected",
                false);
            _hoveredDestinationRoot = null;

            if (_cursorPreview != null)
            {
                _cursorPreview.RemoveFromHierarchy();
                _cursorPreview = null;
            }
        }

        private void Refresh()
        {
            if (!_isBuilt || _boundState == null || _catalog == null)
            {
                return;
            }

            var snapshot =
                InventoryHudPresenter.Project(_boundState, _catalog);
            for (var index = 0; index < snapshot.Slots.Count; index++)
            {
                var isSelected = index == _selectedHotbarIndex;
                _inventoryViews[index].Bind(
                    snapshot.Slots[index],
                    isSelected);
                if (index < _hotbarViews.Length)
                {
                    _hotbarViews[index].Bind(
                        snapshot.Slots[index],
                        isSelected);
                }
            }

            ApplyHeldSourcePresentation();

            if (_capacityLabel != null)
            {
                _capacityLabel.text =
                    $"{_boundState.TotalQuantity.Value} oggetti · " +
                    $"{_boundState.SlotCount} slot";
            }

            if (_selectedItemLabel != null)
            {
                var selected = snapshot.Slots[_selectedHotbarIndex];
                _selectedItemLabel.text = selected.IsOccupied
                    ? selected.DisplayName
                    : "Mani libere";
            }

            RefreshQuickCrafting();
        }

        private void RefreshQuickCrafting()
        {
            if (_quickCraftList == null || _boundState == null || _catalog == null)
            {
                return;
            }

            var recipes = new List<RecipeDefinition>();
            for (var index = 0; index < _catalog.Recipes.Count; index++)
            {
                var recipe = _catalog.Recipes[index];
                if (recipe.Station == CraftingStationKind.Personal)
                {
                    recipes.Add(recipe);
                }
            }

            if (!QuickCraftViewsMatch(recipes))
            {
                RebuildQuickCraftViews(recipes);
            }

            for (var index = 0; index < recipes.Count; index++)
            {
                var recipe = recipes[index];
                var presentation = CraftingHudPresenter.Project(
                    _boundState,
                    _catalog,
                    recipe);
                var view = _quickCraftViews[index];
                view.Card.EnableInClassList(
                    "quick-craft-card--blocked",
                    !presentation.CanCraft);
                for (var ingredientIndex = 0;
                     ingredientIndex < presentation.Ingredients.Count;
                     ingredientIndex++)
                {
                    var ingredient = presentation.Ingredients[ingredientIndex];
                    var label = view.IngredientLabels[ingredientIndex];
                    label.text =
                        $"{ingredient.Item.DisplayName} " +
                        $"{ingredient.Owned}/{ingredient.Required}";
                    label.EnableInClassList(
                        "quick-craft-ingredient--missing",
                        !ingredient.IsAvailable);
                }

                view.CraftButton.EnableInClassList(
                    "quick-craft-button--blocked",
                    !presentation.CanCraft);
                // Keep the button clickable for unavailable recipes so the
                // player receives a useful reason instead of a silent control.
                view.CraftButton.SetEnabled(
                    craftingBridge != null && craftingBridge.IsAttached);
            }

            if (recipes.Count == 0)
            {
                _quickCraftFeedback.text = "Nessuna ricetta personale disponibile.";
            }
        }

        private bool QuickCraftViewsMatch(
            IReadOnlyList<RecipeDefinition> recipes)
        {
            if (_quickCraftViews.Count != recipes.Count)
            {
                return false;
            }

            for (var index = 0; index < recipes.Count; index++)
            {
                if (_quickCraftViews[index].RecipeId != recipes[index].Id)
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildQuickCraftViews(
            IReadOnlyList<RecipeDefinition> recipes)
        {
            _quickCraftList.Clear();
            _quickCraftViews.Clear();
            for (var index = 0; index < recipes.Count; index++)
            {
                var recipe = recipes[index];
                var presentation = CraftingHudPresenter.Project(
                    _boundState,
                    _catalog,
                    recipe);
                var card = new VisualElement();
                card.AddToClassList("quick-craft-card");

                var icon = new VisualElement();
                icon.AddToClassList("quick-craft-icon");
                icon.Add(InventorySlotView.CreateIcon(presentation.Output));
                card.Add(icon);

                var copy = new VisualElement();
                copy.AddToClassList("quick-craft-copy");
                var name = new Label(presentation.DisplayName);
                name.AddToClassList("quick-craft-name");
                copy.Add(name);

                var ingredients = new VisualElement();
                ingredients.AddToClassList("quick-craft-ingredients");
                var ingredientLabels = new List<Label>();
                for (var ingredientIndex = 0;
                     ingredientIndex < presentation.Ingredients.Count;
                     ingredientIndex++)
                {
                    var label = new Label();
                    label.AddToClassList("quick-craft-ingredient");
                    ingredientLabels.Add(label);
                    ingredients.Add(label);
                }

                copy.Add(ingredients);
                card.Add(copy);

                var craftButton = new Button { text = "CREA" };
                craftButton.AddToClassList("quick-craft-button");
                var capturedRecipeId = recipe.Id;
                craftButton.clicked += () => CraftPersonal(capturedRecipeId);
                card.Add(craftButton);

                _quickCraftViews.Add(
                    new QuickCraftCardView(
                        recipe.Id,
                        card,
                        craftButton,
                        ingredientLabels));
                _quickCraftList.Add(card);
            }
        }

        private void CraftPersonal(StableId recipeId)
        {
            if (craftingBridge == null || !craftingBridge.IsAttached)
            {
                _quickCraftFeedback.text = "Crafting non collegato.";
                return;
            }

            if (!craftingBridge.TryCraft(
                    _boundState.InventoryId,
                    recipeId,
                    CraftingStationKind.Personal,
                    1,
                    out var failure))
            {
                _quickCraftFeedback.text = FailureText(failure);
                RefreshQuickCrafting();
                return;
            }

            if (craftingBridge.TryGetInventory(
                    _boundState.InventoryId,
                    out var inventory))
            {
                _boundState = inventory;
            }

            _quickCraftFeedback.text = "Oggetto creato.";
            Refresh();
        }

        private static string FailureText(CraftingFailure failure)
        {
            switch (failure)
            {
                case CraftingFailure.InsufficientIngredients:
                    return "Materiali insufficienti.";
                case CraftingFailure.InventoryFull:
                    return "Libera uno slot nell'inventario.";
                case CraftingFailure.WrongStation:
                    return "Serve un'altra postazione.";
                case CraftingFailure.AuthorityBusy:
                    return "Riprova tra un istante.";
                default:
                    return "Impossibile creare l'oggetto.";
            }
        }

        private sealed class QuickCraftCardView
        {
            public QuickCraftCardView(
                StableId recipeId,
                VisualElement card,
                Button craftButton,
                IReadOnlyList<Label> ingredientLabels)
            {
                RecipeId = recipeId;
                Card = card ?? throw new ArgumentNullException(nameof(card));
                CraftButton = craftButton
                    ?? throw new ArgumentNullException(nameof(craftButton));
                IngredientLabels = ingredientLabels
                    ?? throw new ArgumentNullException(nameof(ingredientLabels));
            }

            public StableId RecipeId { get; }

            public VisualElement Card { get; }

            public Button CraftButton { get; }

            public IReadOnlyList<Label> IngredientLabels { get; }
        }

        private void ApplyModalState()
        {
            if (_isBuilt)
            {
                _screen.style.display = _gameplayPresentationVisible
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                _backdrop.style.display = _gameplayPresentationVisible
                    && _inventoryOpen
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                _inventoryPanel.style.display = _gameplayPresentationVisible
                    && _inventoryOpen
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                _screen.EnableInClassList(
                    "inventory-screen--open",
                    _inventoryOpen);
            }

            airshipInput?.SetUiInputSuppressed(_inventoryOpen);
            mouseLook?.SetUiInputSuppressed(_inventoryOpen);

            if (_inventoryOpen)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using CML.Foundation;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Inventory
{
    /// <summary>
    /// What a panel must answer for its slots to be draggable. Everything here
    /// is panel-specific: which endpoint a pane is, whether a destination can
    /// admit an item, where a quick move should go. The interaction itself is
    /// not — that lives in <see cref="InventorySlotDragController"/>.
    ///
    /// A "pane" is any region of slots the panel draws: a machine port, a crate,
    /// the player's backpack, an airship hold. The controller treats the key as
    /// opaque, which is why it does not need a per-panel enum.
    /// </summary>
    public interface IInventorySlotDragHost
    {
        /// <summary>False while the panel is closed; no click is interpreted.</summary>
        bool IsDragEnabled { get; }

        /// <summary>Element the cursor stack is parented to and positioned within.</summary>
        VisualElement DragRoot { get; }

        /// <summary>USS name prefix for the floating cursor stack.</summary>
        string CursorNamePrefix { get; }

        /// <summary>USS variant class giving the cursor stack its size.</summary>
        string CursorVariantClass { get; }

        bool TryGetSlot(int pane, int slotIndex, out InventorySlotPresentation slot);

        int SlotCount(int pane);

        /// <summary>
        /// True only where reordering inside one container is meaningful — the
        /// player's own inventory. Elsewhere a drop onto the same pane is a
        /// no-op rather than a move.
        /// </summary>
        bool SupportsReorder(int pane);

        void QuickMove(int pane, int slotIndex, InventorySlotPresentation slot);

        void Reorder(int pane, int fromSlotIndex, int toSlotIndex, long quantity);

        /// <summary>
        /// Moves up to <paramref name="requested"/> units between two panes and
        /// returns how many were actually committed. Returning less than asked
        /// keeps the remainder on the cursor instead of destroying it.
        /// </summary>
        long MoveCursorStack(
            int fromPane,
            int toPane,
            StableId itemId,
            long requested);
    }

    /// <summary>
    /// The one implementation of picking a stack up, splitting it, carrying it
    /// and dropping it. It was written twice — once in the crate panel, once in
    /// the machine panel — and a third copy in the airship panel is what
    /// prompted the extraction: identical rules diverging quietly is exactly
    /// what the project avoids everywhere else.
    ///
    /// Semantics preserved from the machine panel, which was the more evolved of
    /// the two: left picks the whole stack, right picks half rounded up,
    /// Shift+left quick-moves, clicking the source slot again cancels, dropping
    /// with the right button places a single unit.
    /// </summary>
    public sealed class InventorySlotDragController
    {
        private readonly IInventorySlotDragHost _host;

        /// <summary>
        /// Every registered tile, so the controller can hit-test the pointer
        /// itself and highlight the slot a drop would land in. Doing it here
        /// rather than asking the host keeps the drop feedback identical in
        /// every panel instead of present in one and missing in the next.
        /// </summary>
        private readonly List<RegisteredSlot> _slots = new List<RegisteredSlot>();

        private VisualElement _hoveredDestinationRoot;
        private bool _hasCursorStack;
        private int _cursorPane;
        private int _cursorSlotIndex = -1;
        private StableId _cursorItemId;
        private long _cursorQuantity;
        private long _cursorSourceShownQuantity;
        private VisualElement _cursorSourceRoot;
        private InventorySlotView _cursorSourceView;
        private InventorySlotPresentation _cursorPresentation;
        private InventorySlotView _cursorPreviewView;
        private VisualElement _cursorPreview;

        public InventorySlotDragController(IInventorySlotDragHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public bool HasCursorStack => _hasCursorStack;

        public StableId CursorItemId => _cursorItemId;

        public long CursorQuantity => _cursorQuantity;

        /// <summary>
        /// Registers the move handler on the panel root. Safe to call again: the
        /// callback is removed first, because UI Toolkit clones a fresh tree
        /// after a domain reload and the old one would leak a subscription.
        /// </summary>
        public void AttachRoot(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            root.UnregisterCallback<PointerMoveEvent>(HandlePointerMove);
            root.RegisterCallback<PointerMoveEvent>(HandlePointerMove);
        }

        /// <summary>
        /// Forgets every registered tile. Call before rebuilding a grid, so the
        /// hit test does not keep pointing at elements nobody renders.
        /// </summary>
        public void ClearRegisteredSlots(int pane)
        {
            for (var index = _slots.Count - 1; index >= 0; index--)
            {
                if (_slots[index].Pane == pane)
                {
                    _slots.RemoveAt(index);
                }
            }
        }

        public void RegisterSlot(
            InventorySlotView slotView,
            int pane,
            int slotIndex)
        {
            var slotRoot = slotView?.Root;
            if (slotRoot == null)
            {
                return;
            }

            _slots.Add(new RegisteredSlot(
                slotView,
                pane,
                slotIndex));

            // TrickleDown, matching the crate and machine panels: the carried
            // stack is drawn above the grid, and the slot must get the press
            // during the capture phase rather than after something on top of it.
            slotRoot.RegisterCallback<PointerDownEvent>(
                evt => HandleSlotPointerDown(
                    evt,
                    pane,
                    slotIndex,
                    slotView),
                TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Recomputes the carried-stack visuals of one slot. Call it for every
        /// slot after rebinding it.
        ///
        /// Visibility is derived here rather than switched off once and switched
        /// back on later. Hiding a slot and trusting a later refresh to restore
        /// it leaves the flag stuck: Bind replaces a tile's contents but never
        /// touches style.visibility, so every slot ever dragged from stayed
        /// blank until the panel was rebuilt — the items were still there, just
        /// invisible.
        /// </summary>
        public void RebindSlot(
            InventorySlotView slotView,
            int pane,
            int slotIndex)
        {
            var slotRoot = slotView?.Root;
            if (slotRoot == null)
            {
                return;
            }

            var isCursorSource = _hasCursorStack
                && pane == _cursorPane
                && slotIndex == _cursorSlotIndex;
            if (isCursorSource)
            {
                _cursorSourceRoot = slotRoot;
                _cursorSourceView = slotView;
                if (!_host.TryGetSlot(pane, slotIndex, out var current)
                    || !current.IsOccupied
                    || current.ItemId != _cursorItemId
                    || current.Quantity < _cursorQuantity)
                {
                    // Something other than this gesture changed the source.
                    // The authoritative view wins, and a stale virtual stack
                    // must not remain available for another command.
                    _cursorPresentation = current;
                    ClearCursor(restoreSource: true);
                    return;
                }

                _cursorPresentation = current;
                _cursorSourceShownQuantity =
                    current.Quantity - _cursorQuantity;
            }

            if (isCursorSource)
            {
                ApplyHeldSourcePresentation();
            }
            else
            {
                slotRoot.RemoveFromClassList("inventory-slot--held");
                SetSlotStackVisible(slotRoot, visible: true);
            }
        }

        /// <summary>Puts the carried stack back and removes the preview.</summary>
        public void Cancel()
        {
            ClearCursor(restoreSource: true);
        }

        /// <summary>
        /// Drops the cursor after the stack has actually gone somewhere.
        ///
        /// The source is deliberately left empty: a transfer is a command that
        /// commits on the next tick, so restoring it here made the stack blink
        /// back into its old slot for a frame or two before the refresh removed
        /// it again.
        /// </summary>
        private void ClearCursorAfterCommit()
        {
            ClearCursor(restoreSource: false);
        }

        private void ClearCursor(bool restoreSource)
        {
            if (_cursorSourceRoot != null)
            {
                _cursorSourceRoot.RemoveFromClassList(
                    "inventory-slot--held");
            }

            if (restoreSource && _cursorSourceView != null)
            {
                _cursorSourceView.Bind(
                    _cursorPresentation,
                    isSelected: false);
                SetSlotStackVisible(_cursorSourceRoot, true);
            }

            _hoveredDestinationRoot?.EnableInClassList(
                "inventory-slot--selected",
                false);
            _hoveredDestinationRoot = null;
            _cursorPreview?.RemoveFromHierarchy();
            _cursorPreview = null;
            _cursorPreviewView = null;
            _cursorSourceRoot = null;
            _cursorSourceView = null;
            _hasCursorStack = false;
            _cursorPane = 0;
            _cursorSlotIndex = -1;
            _cursorItemId = StableId.None;
            _cursorQuantity = 0L;
            _cursorSourceShownQuantity = 0L;
            _cursorPresentation = default;
        }

        private void HandleSlotPointerDown(
            PointerDownEvent evt,
            int pane,
            int slotIndex,
            InventorySlotView slotView)
        {
            var slotRoot = slotView.Root;
            var isLeft = evt.button == 0;
            var isRight = evt.button == 1;
            if (!_host.IsDragEnabled || (!isLeft && !isRight))
            {
                return;
            }

            if (evt.shiftKey && isLeft && !_hasCursorStack)
            {
                if (_host.TryGetSlot(pane, slotIndex, out var quickSlot)
                    && quickSlot.IsOccupied)
                {
                    _host.QuickMove(pane, slotIndex, quickSlot);
                    evt.StopPropagation();
                }

                return;
            }

            if (!_hasCursorStack)
            {
                if (!_host.TryGetSlot(pane, slotIndex, out var slot)
                    || !slot.IsOccupied)
                {
                    return;
                }

                // Right splits: half, rounded up, so a single unit still yields
                // one rather than nothing.
                var picked = isRight
                    ? (slot.Quantity + 1L) / 2L
                    : slot.Quantity;
                BeginCursorStack(
                    pane,
                    slotIndex,
                    slotView,
                    slot,
                    evt.position,
                    picked);
                evt.StopPropagation();
                return;
            }

            if (pane == _cursorPane && slotIndex == _cursorSlotIndex)
            {
                Cancel();
                evt.StopPropagation();
                return;
            }

            var requested = isRight ? 1L : _cursorQuantity;
            if (pane == _cursorPane)
            {
                if (_host.SupportsReorder(pane))
                {
                    _host.Reorder(pane, _cursorSlotIndex, slotIndex, requested);
                    ClearCursorAfterCommit();
                }
                else
                {
                    UpdateCursorPresentation(evt.position);
                }

                evt.StopPropagation();
                return;
            }

            var moved = _host.MoveCursorStack(
                _cursorPane,
                pane,
                _cursorItemId,
                requested);
            if (moved <= 0L)
            {
                UpdateCursorPresentation(evt.position);
                evt.StopPropagation();
                return;
            }

            if (moved < _cursorQuantity)
            {
                // A partial move keeps the remainder on the cursor. Cancelling
                // here would silently return it to a slot the player did not
                // choose.
                _cursorQuantity -= moved;
                _cursorPreviewView?.Bind(
                    _cursorPresentation.WithQuantity(_cursorQuantity),
                    isSelected: true);

                // Bind rebuilds the icon inside the preview, and those fresh
                // children default to PickingMode.Position. The preview sits
                // under the pointer, so without this it swallows the next click
                // and the stack can no longer be put down anywhere.
                SetPickingIgnoredRecursively(_cursorPreview);
                UpdateCursorPresentation(evt.position);
            }
            else
            {
                ClearCursorAfterCommit();
            }

            evt.StopPropagation();
        }

        private void HandlePointerMove(PointerMoveEvent evt)
        {
            if (_hasCursorStack)
            {
                UpdateCursorPresentation(evt.position);
            }
        }

        private void BeginCursorStack(
            int pane,
            int slotIndex,
            InventorySlotView slotView,
            InventorySlotPresentation presentation,
            Vector2 panelPosition,
            long quantity)
        {
            var picked = Math.Max(1L, Math.Min(quantity, presentation.Quantity));
            _hasCursorStack = true;
            _cursorPane = pane;
            _cursorSlotIndex = slotIndex;
            _cursorItemId = presentation.ItemId;
            _cursorQuantity = picked;
            _cursorSourceShownQuantity =
                presentation.Quantity - picked;
            _cursorSourceView = slotView;
            var slotRoot = slotView.Root;
            _cursorSourceRoot = slotRoot;
            _cursorPresentation = presentation;
            ApplyHeldSourcePresentation();

            var slotCount = Math.Max(1, _host.SlotCount(pane));
            _cursorPreviewView = new InventorySlotView(
                Math.Max(0, Math.Min(slotIndex, slotCount - 1)),
                _host.CursorNamePrefix,
                _host.CursorVariantClass,
                slotCount);
            _cursorPreviewView.Bind(
                picked >= presentation.Quantity
                    ? presentation
                    : presentation.WithQuantity(picked),
                isSelected: true);
            _cursorPreview = _cursorPreviewView.Root;
            _cursorPreview.name = _host.CursorNamePrefix;

            // The carried stack must never eat the click meant for the slot
            // underneath it.
            SetPickingIgnoredRecursively(_cursorPreview);
            _cursorPreview.style.position = Position.Absolute;
            _cursorPreview.style.width =
                Mathf.Max(1f, _cursorSourceRoot?.resolvedStyle.width ?? 58f);
            _cursorPreview.style.height =
                Mathf.Max(1f, _cursorSourceRoot?.resolvedStyle.height ?? 58f);
            _cursorPreview.style.opacity = 0.90f;
            _host.DragRoot?.Add(_cursorPreview);
            _cursorPreview.BringToFront();
            UpdateCursorPresentation(panelPosition);
        }

        private void UpdateCursorPresentation(Vector2 panelPosition)
        {
            if (_cursorPreview == null || _host.DragRoot == null)
            {
                return;
            }

            var rootBounds = _host.DragRoot.worldBound;
            var width = _cursorPreview.resolvedStyle.width;
            var height = _cursorPreview.resolvedStyle.height;
            if (!float.IsFinite(width) || width <= 0f)
            {
                width = 58f;
            }

            if (!float.IsFinite(height) || height <= 0f)
            {
                height = 58f;
            }

            _cursorPreview.style.left =
                panelPosition.x - rootBounds.x - (width * 0.5f);
            _cursorPreview.style.top =
                panelPosition.y - rootBounds.y - (height * 0.5f);
            HighlightDropTarget(panelPosition);
        }

        /// <summary>
        /// Marks the slot a drop would land in, and only that one. The source
        /// slot is excluded: releasing there cancels, so promising a drop would
        /// be a lie.
        /// </summary>
        private void HighlightDropTarget(Vector2 panelPosition)
        {
            VisualElement next = null;
            for (var index = 0; index < _slots.Count; index++)
            {
                var candidate = _slots[index];
                if (candidate.Root == null
                    || !candidate.Root.worldBound.Contains(panelPosition))
                {
                    continue;
                }

                if (candidate.Pane != _cursorPane
                    || candidate.SlotIndex != _cursorSlotIndex)
                {
                    next = candidate.Root;
                }

                break;
            }

            if (_hoveredDestinationRoot == next)
            {
                return;
            }

            _hoveredDestinationRoot?.EnableInClassList(
                "inventory-slot--selected",
                false);
            _hoveredDestinationRoot = next;
            _hoveredDestinationRoot?.EnableInClassList(
                "inventory-slot--selected",
                true);
        }

        private readonly struct RegisteredSlot
        {
            public RegisteredSlot(
                InventorySlotView view,
                int pane,
                int slotIndex)
            {
                Root = view.Root;
                Pane = pane;
                SlotIndex = slotIndex;
            }

            public VisualElement Root { get; }

            public int Pane { get; }

            public int SlotIndex { get; }
        }

        private void ApplyHeldSourcePresentation()
        {
            if (!_hasCursorStack
                || _cursorSourceView == null
                || _cursorSourceRoot == null)
            {
                return;
            }

            _cursorSourceRoot.AddToClassList("inventory-slot--held");
            if (_cursorSourceShownQuantity > 0L)
            {
                _cursorSourceView.Bind(
                    _cursorPresentation.WithQuantity(
                        _cursorSourceShownQuantity),
                    isSelected: false);
                SetSlotStackVisible(_cursorSourceRoot, visible: true);
            }
            else
            {
                SetSlotStackVisible(_cursorSourceRoot, visible: false);
            }
        }

        /// <summary>
        /// Hides the contents of a slot without removing the tile. The class
        /// names come from <see cref="InventorySlotView"/>, so this works for
        /// every panel that draws its slots with it.
        /// </summary>
        private static void SetSlotStackVisible(
            VisualElement slotRoot,
            bool visible)
        {
            if (slotRoot == null)
            {
                return;
            }

            var visibility = visible ? Visibility.Visible : Visibility.Hidden;
            var icon = slotRoot.Q<VisualElement>(className: "slot-icon-host");
            var quantity = slotRoot.Q<Label>(className: "slot-quantity");
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

        private static void SetPickingIgnoredRecursively(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            element.pickingMode = PickingMode.Ignore;
            for (var index = 0; index < element.hierarchy.childCount; index++)
            {
                SetPickingIgnoredRecursively(element.hierarchy[index]);
            }
        }
    }
}

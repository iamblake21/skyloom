using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Inventory
{
    /// <summary>
    /// Small retained-mode view for one inventory address.
    /// </summary>
    public sealed class InventorySlotView
    {
        private readonly Label _indexLabel;
        private readonly Label _quantityLabel;
        private readonly VisualElement _iconHost;
        private readonly VisualElement _durabilityTrack;
        private readonly VisualElement _durabilityFill;

        public InventorySlotView(int slotIndex, bool hotbar)
            : this(
                slotIndex,
                hotbar ? "hotbar-slot" : "inventory-slot",
                hotbar ? "hotbar-slot" : "backpack-slot",
                InventoryHudPresenter.PlayerSlotCount)
        {
        }

        /// <summary>
        /// A slot of the same kind outside the player inventory — a machine port, a
        /// crate. It shares this view, and therefore the same USS rules, rather than
        /// being drawn by a near-copy that would slowly stop matching.
        /// </summary>
        public InventorySlotView(
            int slotIndex,
            string namePrefix,
            string variantClass,
            int slotCount)
        {
            if (slotCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            if (slotIndex < 0 || slotIndex >= slotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            if (string.IsNullOrWhiteSpace(namePrefix)
                || string.IsNullOrWhiteSpace(variantClass))
            {
                throw new ArgumentException(
                    "A slot view requires a name prefix and a variant class.");
            }

            SlotIndex = slotIndex;
            Root = new VisualElement
            {
                name = $"{namePrefix}-{slotIndex}"
            };
            Root.AddToClassList("inventory-slot");
            Root.AddToClassList(variantClass);

            var selectionFrame = new VisualElement();
            selectionFrame.AddToClassList("slot-selection-frame");
            Root.Add(selectionFrame);

            _indexLabel = new Label((slotIndex + 1).ToString());
            _indexLabel.AddToClassList("slot-index");
            Root.Add(_indexLabel);

            _iconHost = new VisualElement();
            _iconHost.AddToClassList("slot-icon-host");
            Root.Add(_iconHost);

            _quantityLabel = new Label();
            _quantityLabel.AddToClassList("slot-quantity");
            Root.Add(_quantityLabel);

            _durabilityTrack = new VisualElement();
            _durabilityTrack.AddToClassList("slot-durability-track");
            _durabilityFill = new VisualElement();
            _durabilityFill.AddToClassList("slot-durability-fill");
            _durabilityTrack.Add(_durabilityFill);
            Root.Add(_durabilityTrack);
        }

        public int SlotIndex { get; }

        public VisualElement Root { get; }

        public void Bind(
            InventorySlotPresentation presentation,
            bool isSelected,
            bool isInvalid = false)
        {
            if (presentation.SlotIndex != SlotIndex)
            {
                throw new ArgumentException(
                    "A slot view can only bind its own inventory address.",
                    nameof(presentation));
            }

            Root.EnableInClassList(
                "inventory-slot--selected",
                isSelected);
            Root.EnableInClassList(
                "inventory-slot--occupied",
                presentation.IsOccupied);
            Root.EnableInClassList(
                "inventory-slot--invalid",
                isInvalid);

            _iconHost.Clear();
            if (!presentation.IsOccupied)
            {
                Root.tooltip = $"Slot {SlotIndex + 1} · Vuoto";
                _quantityLabel.text = string.Empty;
                _quantityLabel.style.display = DisplayStyle.None;
                _durabilityTrack.style.display = DisplayStyle.None;
                return;
            }

            Root.tooltip = presentation.CurrentDurability.HasValue
                ? $"{presentation.DisplayName} · " +
                  $"{presentation.CurrentDurability}/" +
                  $"{presentation.MaximumDurability}"
                : $"{presentation.DisplayName} · {presentation.Quantity}";
            _iconHost.Add(CreateIcon(presentation));

            _quantityLabel.text = presentation.Quantity.ToString();
            _quantityLabel.style.display = presentation.Quantity > 1L
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (presentation.Durability01.HasValue)
            {
                _durabilityTrack.style.display = DisplayStyle.Flex;
                _durabilityFill.style.width =
                    Length.Percent(presentation.Durability01.Value * 100f);
                _durabilityFill.style.backgroundColor =
                    DurabilityColor(presentation.Durability01.Value);
            }
            else
            {
                _durabilityTrack.style.display = DisplayStyle.None;
            }
        }

        private static Color DurabilityColor(float durability01)
        {
            var green = new Color(0.37f, 0.75f, 0.44f, 1f);
            var yellow = new Color(0.93f, 0.72f, 0.18f, 1f);
            var red = new Color(0.84f, 0.25f, 0.18f, 1f);

            return durability01 >= 0.5f
                ? Color.Lerp(
                    yellow,
                    green,
                    (durability01 - 0.5f) * 2f)
                : Color.Lerp(
                    red,
                    yellow,
                    durability01 * 2f);
        }

        internal static VisualElement CreateIcon(
            InventorySlotPresentation presentation)
        {
            var root = new VisualElement();
            root.AddToClassList("item-icon");
            // Icons are rendered from the real 3D models (Art/UI/Icons, built by
            // Tools/Art/build_item_icons.py) and are wired up in USS.  Tinting
            // them by an accent colour would multiply over those renders and
            // wash the palette out, so the accent stays presentation-only data.

            switch (presentation.IconKind)
            {
                case InventoryIconKind.Ore:
                    root.AddToClassList("item-icon--ore");
                    AddPart(root, "ore-chip ore-chip--one");
                    AddPart(root, "ore-chip ore-chip--two");
                    AddPart(root, "ore-chip ore-chip--three");
                    break;

                case InventoryIconKind.Ingot:
                    root.AddToClassList("item-icon--ingot");
                    AddPart(root, "ingot-body");
                    AddPart(root, "ingot-shine");
                    break;

                case InventoryIconKind.Plate:
                    root.AddToClassList("item-icon--plate");
                    AddPart(root, "plate-body");
                    AddPart(root, "plate-rivet plate-rivet--tl");
                    AddPart(root, "plate-rivet plate-rivet--tr");
                    AddPart(root, "plate-rivet plate-rivet--bl");
                    AddPart(root, "plate-rivet plate-rivet--br");
                    break;

                case InventoryIconKind.WoodenCrate:
                    root.AddToClassList("item-icon--wooden-crate");
                    break;

                case InventoryIconKind.BeltStraight:
                    root.AddToClassList("item-icon--belt-straight");
                    break;

                case InventoryIconKind.BeltCurve:
                    root.AddToClassList("item-icon--belt-curve");
                    break;

                case InventoryIconKind.BeltCurveLeft:
                    root.AddToClassList("item-icon--belt-curve-left");
                    break;

                case InventoryIconKind.BeltIncline:
                    root.AddToClassList("item-icon--belt-incline");
                    break;

                case InventoryIconKind.BeltSupport:
                    root.AddToClassList("item-icon--belt-support");
                    break;

                case InventoryIconKind.BeltFunnel:
                    root.AddToClassList("item-icon--belt-funnel");
                    break;

                case InventoryIconKind.BeltDriveUnit:
                    root.AddToClassList("item-icon--belt-drive-unit");
                    break;

                case InventoryIconKind.MechanicalPress:
                    root.AddToClassList("item-icon--mechanical-press");
                    break;

                case InventoryIconKind.MechanicalDrill:
                    root.AddToClassList("item-icon--mechanical-drill");
                    break;

                case InventoryIconKind.CrudePickaxe:
                    root.AddToClassList("item-icon--crude-pickaxe");
                    break;

                case InventoryIconKind.Stone:
                    root.AddToClassList("item-icon--stone");
                    break;

                case InventoryIconKind.IronPickaxe:
                    root.AddToClassList("item-icon--iron-pickaxe");
                    break;

                case InventoryIconKind.WoodLog:
                    root.AddToClassList("item-icon--wood-log");
                    break;

                case InventoryIconKind.PlantFiber:
                    root.AddToClassList("item-icon--plant-fiber");
                    break;

                case InventoryIconKind.Stick:
                    root.AddToClassList("item-icon--stick");
                    break;

                default:
                    root.AddToClassList("item-icon--generic");
                    var text = string.IsNullOrWhiteSpace(
                        presentation.DisplayName)
                        ? "?"
                        : presentation.DisplayName.Substring(
                            0,
                            Math.Min(2, presentation.DisplayName.Length))
                            .ToUpperInvariant();
                    var label = new Label(text);
                    label.AddToClassList("generic-icon-label");
                    root.Add(label);
                    break;
            }

            return root;
        }

        private static void AddPart(
            VisualElement parent,
            string classNames)
        {
            var part = new VisualElement();
            var names = classNames.Split(' ');
            for (var index = 0; index < names.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(names[index]))
                {
                    part.AddToClassList(names[index]);
                }
            }

            parent.Add(part);
        }
    }
}

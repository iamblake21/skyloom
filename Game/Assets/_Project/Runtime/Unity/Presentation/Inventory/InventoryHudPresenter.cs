using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using UnityEngine;

namespace CML.Unity.Presentation.Inventory
{
    /// <summary>
    /// Read-only projection from the authoritative immutable inventory state
    /// to presentation data. This class never stores, removes or moves items.
    /// </summary>
    public static class InventoryHudPresenter
    {
        public const int PlayerSlotCount = 16;
        public const int HotbarSlotCount = 8;

        public static InventoryUiSnapshot Project(
            InventoryState inventory,
            GameCatalog catalog)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (inventory.SlotCount != PlayerSlotCount)
            {
                throw new ArgumentException(
                    $"The player inventory must contain exactly " +
                    $"{PlayerSlotCount} slots, but contains " +
                    $"{inventory.SlotCount}.",
                    nameof(inventory));
            }

            var projected = new InventorySlotPresentation[PlayerSlotCount];
            for (var index = 0; index < projected.Length; index++)
            {
                var slot = inventory.GetSlot(index);
                if (slot.IsEmpty)
                {
                    projected[index] =
                        InventorySlotPresentation.Empty(index);
                    continue;
                }

                var stack = slot.Stack.Value;
                if (!catalog.TryGetItem(stack.ItemId, out var definition))
                {
                    throw new InvalidOperationException(
                        $"Inventory slot {index} references unknown item " +
                        $"'{stack.ItemId}'.");
                }

                projected[index] = ProjectSlot(
                    index,
                    stack.ItemId,
                    stack.Quantity.Value,
                    definition,
                    stack.Durability?.Normalized,
                    stack.Durability?.Current,
                    stack.Durability?.Maximum);
            }

            return new InventoryUiSnapshot(
                inventory,
                new ReadOnlyCollection<InventorySlotPresentation>(projected));
        }

        /// <summary>
        /// How one item looks in one slot. Public because the machine panel shows the
        /// same items in slots of the same kind: a second copy of this mapping would
        /// drift, and the two panels would end up disagreeing about what a plate is.
        /// </summary>
        public static InventorySlotPresentation ProjectSlot(
            int slotIndex,
            StableId itemId,
            long quantity,
            ItemDefinition definition,
            float? durability01 = null,
            int? currentDurability = null,
            int? maximumDurability = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (itemId == ContentIds.RawIron)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Ferro grezzo",
                    InventoryIconKind.Ore,
                    new Color(0.38f, 0.49f, 0.50f, 1f));
            }

            if (itemId == ContentIds.IronIngot)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Lingotto di ferro",
                    InventoryIconKind.Ingot,
                    new Color(0.52f, 0.65f, 0.65f, 1f));
            }

            if (itemId == ContentIds.IronPlate)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Piastra di ferro",
                    InventoryIconKind.Plate,
                    new Color(0.46f, 0.59f, 0.60f, 1f));
            }

            if (itemId == ContentIds.Stone)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Pietra",
                    InventoryIconKind.Stone,
                    new Color(0.46f, 0.49f, 0.48f, 1f));
            }

            if (itemId == ContentIds.WoodLog)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Tronco",
                    InventoryIconKind.WoodLog,
                    new Color(0.52f, 0.34f, 0.18f, 1f));
            }

            if (itemId == ContentIds.PlantFiber)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Fibra vegetale",
                    InventoryIconKind.PlantFiber,
                    new Color(0.42f, 0.60f, 0.24f, 1f));
            }

            if (itemId == ContentIds.Stick)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Bastone",
                    InventoryIconKind.Stick,
                    new Color(0.60f, 0.47f, 0.32f, 1f));
            }

            if (itemId == ContentIds.WorkbenchItem)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Banco da lavoro",
                    InventoryIconKind.Generic,
                    new Color(0.58f, 0.42f, 0.25f, 1f));
            }

            if (itemId == ContentIds.WoodenCrateItem)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Cassa di legno",
                    InventoryIconKind.WoodenCrate,
                    new Color(0.58f, 0.42f, 0.25f, 1f));
            }

            if (itemId == ContentIds.MechanicalPressItem)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Pressa meccanica",
                    InventoryIconKind.MechanicalPress,
                    new Color(0.46f, 0.53f, 0.50f, 1f));
            }

            if (itemId == ContentIds.CrudeFurnaceItem)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Fornace rudimentale",
                    InventoryIconKind.Generic,
                    new Color(0.48f, 0.34f, 0.24f, 1f));
            }

            if (itemId == ContentIds.MechanicalDrillItem)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Estrattore meccanico",
                    InventoryIconKind.MechanicalDrill,
                    new Color(0.44f, 0.46f, 0.47f, 1f));
            }

            if (itemId == ContentIds.BeltFunnel)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Imbuto",
                    InventoryIconKind.BeltFunnel,
                    new Color(0.55f, 0.45f, 0.30f, 1f));
            }

            if (itemId == ContentIds.BeltStraight)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Nastro trasportatore",
                    InventoryIconKind.BeltStraight,
                    new Color(0.52f, 0.40f, 0.28f, 1f));
            }

            if (itemId == ContentIds.BeltCurve)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Nastro curvo destro",
                    InventoryIconKind.BeltCurve,
                    new Color(0.52f, 0.40f, 0.28f, 1f));
            }

            if (itemId == ContentIds.BeltCurveLeft)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Nastro curvo sinistro",
                    InventoryIconKind.BeltCurveLeft,
                    new Color(0.52f, 0.40f, 0.28f, 1f));
            }

            if (itemId == ContentIds.BeltIncline)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Nastro inclinato",
                    InventoryIconKind.BeltIncline,
                    new Color(0.52f, 0.40f, 0.28f, 1f));
            }

            if (itemId == ContentIds.BeltSupport)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Supporto per nastro",
                    InventoryIconKind.BeltSupport,
                    new Color(0.48f, 0.43f, 0.36f, 1f));
            }

            if (itemId == ContentIds.BeltDriveUnit)
            {
                return Placeable(
                    slotIndex,
                    itemId,
                    quantity,
                    "Nastro motrice",
                    InventoryIconKind.BeltDriveUnit,
                    new Color(0.61f, 0.43f, 0.25f, 1f));
            }

            if (itemId == ContentIds.CrudePickaxe)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Piccone rudimentale",
                    InventoryIconKind.CrudePickaxe,
                    new Color(0.50f, 0.37f, 0.24f, 1f),
                    durability01,
                    currentDurability,
                    maximumDurability);
            }

            if (itemId == ContentIds.IronPickaxe)
            {
                return new InventorySlotPresentation(
                    slotIndex,
                    itemId,
                    quantity,
                    "Piccone di ferro",
                    InventoryIconKind.IronPickaxe,
                    new Color(0.48f, 0.56f, 0.57f, 1f),
                    durability01,
                    currentDurability,
                    maximumDurability);
            }

            return new InventorySlotPresentation(
                slotIndex,
                itemId,
                quantity,
                HumanizeKey(definition.Key),
                InventoryIconKind.Generic,
                new Color(0.55f, 0.67f, 0.55f, 1f));
        }

        private static InventorySlotPresentation Placeable(
            int slotIndex,
            StableId itemId,
            long quantity,
            string displayName,
            InventoryIconKind iconKind,
            Color accentColor) =>
            new InventorySlotPresentation(
                slotIndex,
                itemId,
                quantity,
                displayName,
                iconKind,
                accentColor);

        private static string HumanizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "Oggetto";
            }

            var separator = key.LastIndexOf('.');
            var value = separator >= 0 ? key.Substring(separator + 1) : key;
            value = value.Replace('_', ' ');
            if (value.Length == 0)
            {
                return "Oggetto";
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }

    public sealed class InventoryUiSnapshot
    {
        internal InventoryUiSnapshot(
            InventoryState source,
            IReadOnlyList<InventorySlotPresentation> slots)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
        }

        public InventoryState Source { get; }

        public IReadOnlyList<InventorySlotPresentation> Slots { get; }
    }

    public readonly struct InventorySlotPresentation
    {
        public InventorySlotPresentation(
            int slotIndex,
            StableId itemId,
            long quantity,
            string displayName,
            InventoryIconKind iconKind,
            Color accentColor,
            float? durability01 = null,
            int? currentDurability = null,
            int? maximumDurability = null)
        {
            if (slotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            if (itemId.IsNone)
            {
                throw new ArgumentException(
                    "An occupied slot requires an item id.",
                    nameof(itemId));
            }

            if (quantity <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            if (durability01.HasValue &&
                (durability01.Value < 0f || durability01.Value > 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(durability01));
            }

            if (currentDurability.HasValue != maximumDurability.HasValue
                || currentDurability.HasValue
                && (maximumDurability.Value <= 0
                    || currentDurability.Value < 0
                    || currentDurability.Value > maximumDurability.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentDurability));
            }

            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = quantity;
            DisplayName = displayName ?? string.Empty;
            IconKind = iconKind;
            AccentColor = accentColor;
            Durability01 = durability01;
            CurrentDurability = currentDurability;
            MaximumDurability = maximumDurability;
        }

        private InventorySlotPresentation(int slotIndex)
        {
            SlotIndex = slotIndex;
            ItemId = StableId.None;
            Quantity = 0L;
            DisplayName = string.Empty;
            IconKind = InventoryIconKind.Generic;
            AccentColor = Color.clear;
            Durability01 = null;
            CurrentDurability = null;
            MaximumDurability = null;
        }

        public int SlotIndex { get; }

        public StableId ItemId { get; }

        public long Quantity { get; }

        public string DisplayName { get; }

        public InventoryIconKind IconKind { get; }

        public Color AccentColor { get; }

        public float? Durability01 { get; }

        public int? CurrentDurability { get; }

        public int? MaximumDurability { get; }

        public bool IsOccupied => !ItemId.IsNone;

        public static InventorySlotPresentation Empty(int slotIndex)
        {
            if (slotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            return new InventorySlotPresentation(slotIndex);
        }

        /// <summary>
        /// Stessa pila con una quantità diversa. Serve all'anteprima sul cursore
        /// quando se ne preleva solo una parte: nome, icona e colore restano
        /// quelli dell'oggetto, cambia solo quanto se ne tiene in mano.
        /// </summary>
        public InventorySlotPresentation WithQuantity(long quantity)
        {
            if (!IsOccupied)
            {
                throw new InvalidOperationException(
                    "Uno slot vuoto non ha una quantità da riscrivere.");
            }

            return new InventorySlotPresentation(
                SlotIndex,
                ItemId,
                quantity,
                DisplayName,
                IconKind,
                AccentColor,
                Durability01,
                CurrentDurability,
                MaximumDurability);
        }
    }

    public enum InventoryIconKind
    {
        Generic = 0,
        Ore = 1,
        Ingot = 2,
        Plate = 3,
        WoodenCrate = 4,
        BeltStraight = 5,
        BeltCurve = 6,
        BeltCurveLeft = 7,
        BeltIncline = 8,
        BeltSupport = 9,
        BeltFunnel = 10,
        BeltDriveUnit = 11,
        MechanicalPress = 12,
        CrudePickaxe = 13,
        Stone = 14,
        IronPickaxe = 15,
        WoodLog = 16,
        MechanicalDrill = 17,
        PlantFiber = 18,
        Stick = 19
    }
}

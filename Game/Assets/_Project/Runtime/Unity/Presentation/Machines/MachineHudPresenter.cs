using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CML.Content;
using CML.Diagnostics;
using CML.Simulation.Machines;
using CML.Unity.Presentation.Inventory;

namespace CML.Unity.Presentation.Machines
{
    /// <summary>
    /// UI-MACH-001. Turns the authoritative diagnostic report of one node into
    /// presentation data. It never stores, moves or starts anything: like the inventory
    /// presenter, it is a pure projection, and it is the layer where the Italian text
    /// lives — the simulation and its diagnostics stay language-neutral.
    ///
    /// Item appearance comes from <see cref="InventoryHudPresenter.ProjectSlot"/>, so a
    /// plate in a press looks exactly like a plate in the backpack.
    /// </summary>
    public static class MachineHudPresenter
    {
        public static MachineUiSnapshot Project(
            MachineNodeReport report,
            GameCatalog catalog)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var ports = new MachinePortPresentation[report.Ports.Count];
            for (var index = 0; index < ports.Length; index++)
            {
                ports[index] = ProjectPort(report.Ports[index], catalog);
            }

            return new MachineUiSnapshot(
                report.NodeId,
                report.Kind,
                report.DefinitionKey,
                DisplayNameForDefinition(report.DefinitionKey),
                DisplayNameForRecipe(report.RecipeKey),
                CauseText(report.Activity),
                JoinStatus(
                    ShortfallText(report, catalog),
                    BeltLineText(report)),
                report.Activity,
                report.IsBlocked,
                report.ProgressPermille,
                report.CompletedCycles,
                new ReadOnlyCollection<MachinePortPresentation>(ports));
        }

        /// <summary>
        /// Italian for a cause key. Every activity has an entry: a machine that showed
        /// an empty string here would be reported as merely stopped, which is the one
        /// thing MACH-003 exists to prevent.
        /// </summary>
        public static string CauseText(MachineActivity activity)
        {
            switch (activity)
            {
                case MachineActivity.Running:
                    return "In lavorazione";
                case MachineActivity.Idle:
                    return "Deposito";
                case MachineActivity.NoRecipe:
                    return "Nessuna ricetta impostata";
                case MachineActivity.MissingInput:
                    return "Manca materiale in ingresso";
                case MachineActivity.MissingFuel:
                    return "Manca combustibile";
                case MachineActivity.OutputFull:
                    return "Uscita piena";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(activity),
                        activity,
                        "Every activity needs Italian text; this one has none.");
            }
        }

        public static string PortTitle(MachinePortKind kind)
        {
            switch (kind)
            {
                case MachinePortKind.Input:
                    return "INGRESSO";
                case MachinePortKind.Output:
                    return "USCITA";
                case MachinePortKind.Storage:
                    return "CONTENUTO";
                case MachinePortKind.Fuel:
                    return "COMBUSTIBILE";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static string BeltLineText(MachineNodeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            switch (report.BeltLineStatus)
            {
                case BeltLineStatus.NotApplicable:
                    return string.Empty;
                case BeltLineStatus.MissingDrive:
                    return "Nastro motore mancante";
                case BeltLineStatus.Operational:
                    return $"{report.BeltLineUsedCapacity}/"
                        + $"{report.BeltLineAvailableCapacity} elementi";
                case BeltLineStatus.Overloaded:
                    return "Sovraccarico linea - "
                        + $"{report.BeltLineUsedCapacity}/"
                        + $"{report.BeltLineAvailableCapacity} elementi";
                case BeltLineStatus.DirectionConflict:
                    return "Conflitto di direzione";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(report),
                        report.BeltLineStatus,
                        "Every belt-line status needs presentation text.");
            }
        }

        private static string JoinStatus(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second ?? string.Empty;
            }

            return string.IsNullOrEmpty(second)
                ? first
                : first + " · " + second;
        }

        private static MachinePortPresentation ProjectPort(
            MachinePortReport port,
            GameCatalog catalog)
        {
            var slots = new InventorySlotPresentation[port.Slots.Count];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = port.Slots[index];
                if (slot.IsEmpty || !catalog.TryGetItem(slot.ItemId, out var definition))
                {
                    slots[index] = InventorySlotPresentation.Empty(index);
                    continue;
                }

                slots[index] = InventoryHudPresenter.ProjectSlot(
                    index,
                    slot.ItemId,
                    slot.Quantity,
                    definition);
            }

            return new MachinePortPresentation(
                port.Kind,
                PortTitle(port.Kind),
                port.TotalQuantity,
                new ReadOnlyCollection<InventorySlotPresentation>(slots));
        }

        /// <summary>
        /// What is missing and how much of it. "Manca materiale" alone tells the player
        /// to go looking; naming the item and the amount tells them what to fetch.
        /// </summary>
        private static string ShortfallText(MachineNodeReport report, GameCatalog catalog)
        {
            if (report.Shortfalls.Count == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            for (var index = 0; index < report.Shortfalls.Count; index++)
            {
                var shortfall = report.Shortfalls[index];
                if (text.Length > 0)
                {
                    text.Append(" · ");
                }

                text.Append(shortfall.Missing);
                text.Append(" × ");
                text.Append(DisplayNameForItem(shortfall, catalog));
            }

            return text.ToString();
        }

        private static string DisplayNameForItem(
            MachineShortfallReport shortfall,
            GameCatalog catalog)
        {
            if (!catalog.TryGetItem(shortfall.ItemId, out var definition))
            {
                return Humanize(shortfall.ItemKey);
            }

            return InventoryHudPresenter
                .ProjectSlot(0, shortfall.ItemId, 1L, definition)
                .DisplayName;
        }

        private static string DisplayNameForDefinition(string definitionKey)
        {
            switch (definitionKey)
            {
                case "machine.mechanical_press":
                    return "Pressa meccanica";
                case "machine.crude_furnace":
                    return "Fornace rudimentale";
                case "machine.mechanical_drill":
                    return "Estrattore meccanico";
                case "item.belt_funnel":
                    return "Imbuto";
                case "container.wooden_crate":
                    return "Cassa di legno";
                case "container.player_inventory":
                    return "Inventario";
                case "item.belt_drive_unit":
                    return "Nastro motore";
                case "item.belt_straight":
                    return "Nastro trasportatore";
                default:
                    return Humanize(definitionKey);
            }
        }

        private static string DisplayNameForRecipe(string recipeKey)
        {
            switch (recipeKey)
            {
                case "":
                    return string.Empty;
                case "recipe.press_iron_plate":
                    return "Piastra di ferro";
                case "recipe.smelt_iron_ingot":
                    return "Lingotto di ferro";

                // La ricetta attiva di un estrattore non è una lavorazione ma il
                // minerale del giacimento su cui sta: nell'HUD deve leggersi
                // come "sta tirando fuori questo".
                case "recipe.drill_raw_iron":
                    return "Ferro grezzo";
                case "recipe.drill_raw_copper":
                    return "Rame grezzo";
                case "recipe.drill_raw_tin":
                    return "Stagno grezzo";
                default:
                    return Humanize(recipeKey);
            }
        }

        private static string Humanize(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "Sconosciuto";
            }

            var separator = key.LastIndexOf('.');
            var value = separator >= 0 ? key.Substring(separator + 1) : key;
            value = value.Replace('_', ' ');
            if (value.Length == 0)
            {
                return "Sconosciuto";
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }

    public sealed class MachinePortPresentation
    {
        public MachinePortPresentation(
            MachinePortKind kind,
            string title,
            long totalQuantity,
            IReadOnlyList<InventorySlotPresentation> slots)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            TotalQuantity = totalQuantity;
            Slots = slots ?? Array.Empty<InventorySlotPresentation>();
        }

        public MachinePortKind Kind { get; }

        public string Title { get; }

        public long TotalQuantity { get; }

        public IReadOnlyList<InventorySlotPresentation> Slots { get; }
    }

    public sealed class MachineUiSnapshot
    {
        public MachineUiSnapshot(
            CML.Foundation.StableId nodeId,
            MachineNodeKind kind,
            string definitionKey,
            string title,
            string recipeName,
            string causeText,
            string shortfallText,
            MachineActivity activity,
            bool isBlocked,
            int progressPermille,
            ulong completedCycles,
            IReadOnlyList<MachinePortPresentation> ports)
        {
            NodeId = nodeId;
            Kind = kind;
            DefinitionKey = definitionKey ?? string.Empty;
            Title = title ?? string.Empty;
            RecipeName = recipeName ?? string.Empty;
            CauseText = causeText ?? string.Empty;
            ShortfallText = shortfallText ?? string.Empty;
            Activity = activity;
            IsBlocked = isBlocked;
            ProgressPermille = progressPermille;
            CompletedCycles = completedCycles;
            Ports = ports ?? Array.Empty<MachinePortPresentation>();
        }

        public CML.Foundation.StableId NodeId { get; }

        public MachineNodeKind Kind { get; }

        public string DefinitionKey { get; }

        public string Title { get; }

        public string RecipeName { get; }

        public string CauseText { get; }

        /// <summary>Empty unless the machine is short of an input.</summary>
        public string ShortfallText { get; }

        public MachineActivity Activity { get; }

        public bool IsBlocked { get; }

        public int ProgressPermille { get; }

        public ulong CompletedCycles { get; }

        public IReadOnlyList<MachinePortPresentation> Ports { get; }

        /// <summary>Progress as a percentage string for the bar's label.</summary>
        public string ProgressText => (ProgressPermille / 10).ToString() + "%";
    }
}

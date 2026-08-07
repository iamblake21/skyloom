using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Machines;

namespace CML.Diagnostics
{
    /// <summary>
    /// Stable keys for the reason a node is not producing. They are keys and not prose:
    /// the simulation and its diagnostics stay language-neutral, and the Italian text
    /// lives in the presenter, as it already does for item names.
    /// </summary>
    public static class MachineCauseKeys
    {
        public const string Running = "machine.cause.running";
        public const string NoWork = "machine.cause.no_work";
        public const string NoRecipe = "machine.cause.no_recipe";
        public const string MissingInput = "machine.cause.missing_input";
        public const string MissingFuel = "machine.cause.missing_fuel";
        public const string OutputFull = "machine.cause.output_full";

        public static string For(MachineActivity activity)
        {
            switch (activity)
            {
                case MachineActivity.Running:
                    return Running;
                case MachineActivity.Idle:
                    return NoWork;
                case MachineActivity.NoRecipe:
                    return NoRecipe;
                case MachineActivity.MissingInput:
                    return MissingInput;
                case MachineActivity.MissingFuel:
                    return MissingFuel;
                case MachineActivity.OutputFull:
                    return OutputFull;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(activity),
                        activity,
                        "Every activity has to name a cause; this one names none.");
            }
        }
    }

    public readonly struct MachineSlotReport
    {
        public MachineSlotReport(
            int slotIndex,
            StableId itemId,
            string itemKey,
            long quantity,
            long maxStack)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            ItemKey = itemKey ?? string.Empty;
            Quantity = quantity;
            MaxStack = maxStack;
        }

        public int SlotIndex { get; }

        public StableId ItemId { get; }

        public string ItemKey { get; }

        public long Quantity { get; }

        public long MaxStack { get; }

        public bool IsEmpty => ItemId.IsNone;
    }

    public readonly struct MachinePortReport
    {
        public MachinePortReport(
            MachinePortKind kind,
            long totalQuantity,
            IReadOnlyList<MachineSlotReport> slots)
        {
            Kind = kind;
            TotalQuantity = totalQuantity;
            Slots = slots ?? Array.Empty<MachineSlotReport>();
        }

        public MachinePortKind Kind { get; }

        public long TotalQuantity { get; }

        public IReadOnlyList<MachineSlotReport> Slots { get; }

        public int SlotCount => Slots.Count;
    }

    /// <summary>What a recipe still needs, and how much of it the input port holds.</summary>
    public readonly struct MachineShortfallReport
    {
        public MachineShortfallReport(
            StableId itemId,
            string itemKey,
            long required,
            long present)
        {
            ItemId = itemId;
            ItemKey = itemKey ?? string.Empty;
            Required = required;
            Present = present;
        }

        public StableId ItemId { get; }

        public string ItemKey { get; }

        public long Required { get; }

        public long Present { get; }

        public long Missing => Required > Present ? Required - Present : 0L;
    }

    public sealed class MachineNodeReport
    {
        public MachineNodeReport(
            StableId nodeId,
            MachineNodeKind kind,
            string definitionKey,
            string recipeKey,
            MachineActivity activity,
            string causeKey,
            long progressMilliseconds,
            long durationMilliseconds,
            int progressPermille,
            bool isCycleActive,
            ulong completedCycles,
            BeltLineStatus beltLineStatus,
            int beltLineUsedCapacity,
            int beltLineAvailableCapacity,
            IReadOnlyList<MachinePortReport> ports,
            IReadOnlyList<MachineShortfallReport> shortfalls)
        {
            NodeId = nodeId;
            Kind = kind;
            DefinitionKey = definitionKey ?? string.Empty;
            RecipeKey = recipeKey ?? string.Empty;
            Activity = activity;
            CauseKey = causeKey ?? string.Empty;
            ProgressMilliseconds = progressMilliseconds;
            DurationMilliseconds = durationMilliseconds;
            ProgressPermille = progressPermille;
            IsCycleActive = isCycleActive;
            CompletedCycles = completedCycles;
            BeltLineStatus = beltLineStatus;
            BeltLineUsedCapacity = beltLineUsedCapacity;
            BeltLineAvailableCapacity = beltLineAvailableCapacity;
            Ports = ports ?? Array.Empty<MachinePortReport>();
            Shortfalls = shortfalls ?? Array.Empty<MachineShortfallReport>();
        }

        public StableId NodeId { get; }

        public MachineNodeKind Kind { get; }

        public string DefinitionKey { get; }

        public string RecipeKey { get; }

        public MachineActivity Activity { get; }

        public string CauseKey { get; }

        public long ProgressMilliseconds { get; }

        public long DurationMilliseconds { get; }

        /// <summary>
        /// Progress in thousandths, computed with integer arithmetic. A progress bar that
        /// divided the authoritative milliseconds itself would be the one place where a
        /// float touched a simulated value.
        /// </summary>
        public int ProgressPermille { get; }

        public bool IsCycleActive { get; }

        public ulong CompletedCycles { get; }

        public BeltLineStatus BeltLineStatus { get; }

        public int BeltLineUsedCapacity { get; }

        public int BeltLineAvailableCapacity { get; }

        public IReadOnlyList<MachinePortReport> Ports { get; }

        /// <summary>
        /// Empty unless <see cref="Activity"/> is
        /// <see cref="MachineActivity.MissingInput"/>. "Missing input" alone does not say
        /// which input, and the difference is what the player has to act on.
        /// </summary>
        public IReadOnlyList<MachineShortfallReport> Shortfalls { get; }

        public bool IsBlocked =>
            Activity != MachineActivity.Running && Activity != MachineActivity.Idle;
    }

    /// <summary>
    /// MACH-003. Read-only projection of one node of the machine graph, for an inspector
    /// or an HUD. It never mutates and never infers: every field either comes from the
    /// authoritative state or is derived from it and from the validated catalog.
    /// </summary>
    public static class MachineDiagnostics
    {
        public static bool TryDescribe(
            SimulationState state,
            GameCatalog catalog,
            StableId nodeId,
            out MachineNodeReport report)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!state.GetMachineSnapshot().TryGetNode(nodeId, out var node))
            {
                report = null;
                return false;
            }

            report = Describe(node, catalog);
            return true;
        }

        /// <summary>Every node, in the graph's own id order.</summary>
        public static IReadOnlyList<MachineNodeReport> DescribeAll(
            SimulationState state,
            GameCatalog catalog)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var snapshot = state.GetMachineSnapshot();
            var reports = new List<MachineNodeReport>(snapshot.NodeCount);
            foreach (var nodeId in snapshot.GetNodeIdsCanonical())
            {
                if (snapshot.TryGetNode(nodeId, out var node))
                {
                    reports.Add(Describe(node, catalog));
                }
            }

            return new ReadOnlyCollection<MachineNodeReport>(reports);
        }

        private static MachineNodeReport Describe(MachineNodeState node, GameCatalog catalog)
        {
            var definitionKey = DescribeDefinition(node, catalog);
            var recipeKey = string.Empty;
            var duration = 0L;
            RecipeDefinition recipe = null;
            if (!node.ActiveRecipeId.IsNone
                && catalog.TryGetRecipe(node.ActiveRecipeId, out recipe))
            {
                recipeKey = recipe.Key;
                duration = recipe.DurationMilliseconds;
            }

            var ports = new List<MachinePortReport>();
            ports.Add(DescribePort(node.Input, catalog));
            if (node.Fuel != null)
            {
                ports.Add(DescribePort(node.Fuel, catalog));
            }

            if (!ReferenceEquals(node.Input, node.Output))
            {
                ports.Add(DescribePort(node.Output, catalog));
            }

            return new MachineNodeReport(
                node.Id,
                node.Kind,
                definitionKey,
                recipeKey,
                node.Activity,
                MachineCauseKeys.For(node.Activity),
                node.ProgressMilliseconds,
                duration,
                Permille(node.ProgressMilliseconds, duration),
                node.IsCycleActive,
                node.CompletedCycles,
                node.BeltLineStatus,
                node.BeltLineUsedCapacity,
                node.BeltLineAvailableCapacity,
                new ReadOnlyCollection<MachinePortReport>(ports),
                DescribeShortfalls(node, recipe, catalog));
        }

        private static string DescribeDefinition(MachineNodeState node, GameCatalog catalog)
        {
            if (node.Kind == MachineNodeKind.Buffer)
            {
                return catalog.TryGetContainer(node.DefinitionId, out var container)
                    ? container.Key
                    : string.Empty;
            }

            // A funnel is a placeable item, not a container and not a machine.
            if (node.Kind == MachineNodeKind.Funnel)
            {
                return catalog.TryGetItem(node.DefinitionId, out var item)
                    ? item.Key
                    : string.Empty;
            }

            if (node.Kind == MachineNodeKind.BeltModule)
            {
                return catalog.TryGetItem(node.DefinitionId, out var item)
                    ? item.Key
                    : string.Empty;
            }

            return catalog.TryGetMachine(node.DefinitionId, out var machine)
                ? machine.Key
                : string.Empty;
        }

        private static MachinePortReport DescribePort(MachinePort port, GameCatalog catalog)
        {
            var slots = new MachineSlotReport[port.SlotCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = port.GetSlot(index);
                var itemKey = string.Empty;
                var maxStack = 0L;
                if (!slot.ItemId.IsNone && catalog.TryGetItem(slot.ItemId, out var item))
                {
                    itemKey = item.Key;
                    maxStack = item.MaxStack;
                }

                slots[index] = new MachineSlotReport(
                    index,
                    slot.ItemId,
                    itemKey,
                    slot.Quantity.Value,
                    maxStack);
            }

            return new MachinePortReport(
                port.Kind,
                port.TotalQuantity.Value,
                new ReadOnlyCollection<MachineSlotReport>(slots));
        }

        private static IReadOnlyList<MachineShortfallReport> DescribeShortfalls(
            MachineNodeState node,
            RecipeDefinition recipe,
            GameCatalog catalog)
        {
            if (node.Activity == MachineActivity.MissingFuel)
            {
                if (!catalog.TryGetMachine(node.DefinitionId, out var machine)
                    || !machine.RequiresFuel)
                {
                    return Array.Empty<MachineShortfallReport>();
                }

                var present = node.Fuel?.Count(machine.FuelItemId).Value ?? 0L;
                var itemKey = catalog.TryGetItem(machine.FuelItemId, out var fuelItem)
                    ? fuelItem.Key
                    : string.Empty;
                return new ReadOnlyCollection<MachineShortfallReport>(
                    new[]
                    {
                        new MachineShortfallReport(
                            machine.FuelItemId,
                            itemKey,
                            machine.FuelQuantityPerCycle,
                            present)
                    });
            }

            if (node.Activity != MachineActivity.MissingInput
                || recipe == null
                || recipe.Inputs == null)
            {
                return Array.Empty<MachineShortfallReport>();
            }

            var shortfalls = new List<MachineShortfallReport>(recipe.Inputs.Count);
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var input = recipe.Inputs[index];
                var present = node.Input.Count(input.ItemId).Value;
                if (present >= input.Quantity)
                {
                    continue;
                }

                var itemKey = catalog.TryGetItem(input.ItemId, out var item)
                    ? item.Key
                    : string.Empty;
                shortfalls.Add(
                    new MachineShortfallReport(
                        input.ItemId,
                        itemKey,
                        input.Quantity,
                        present));
            }

            return new ReadOnlyCollection<MachineShortfallReport>(shortfalls);
        }

        private static int Permille(long progress, long duration)
        {
            if (duration <= 0L || progress <= 0L)
            {
                return 0;
            }

            if (progress >= duration)
            {
                return 1000;
            }

            return (int)(progress * 1000L / duration);
        }
    }
}

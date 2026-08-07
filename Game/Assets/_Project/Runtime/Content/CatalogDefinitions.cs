using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CML.Foundation;

namespace CML.Content
{
    public enum EnergyKind
    {
        None = 0,
        Electrical = 2,
        Thermal = 3
    }

    public enum CraftingStationKind : byte
    {
        Personal = 1,
        Workbench = 2,
        Machine = 3
    }

    public enum RecipeCategory : byte
    {
        Tools = 1,
        Materials = 2,
        Structures = 3,
        Logistics = 4,
        Machinery = 5,

        /// <summary>
        /// A recipe that produces from a geological source instead of from
        /// ingredients, and therefore is the only kind allowed to declare no
        /// inputs. Declaring it explicitly is what keeps "no ingredients" from
        /// silently becoming a way to author a broken recipe: the validator
        /// requires extraction recipes to have no inputs and every other
        /// category to have at least one.
        /// </summary>
        Extraction = 6
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public ItemDefinition(
            StableId id,
            string key,
            string nameKey,
            long maxStack,
            int maximumDurability = 0)
        {
            Id = id;
            Key = key;
            NameKey = nameKey;
            MaxStack = maxStack;
            MaximumDurability = maximumDurability;
        }

        public StableId Id { get; }

        public string Key { get; }

        public string NameKey { get; }

        public long MaxStack { get; }

        /// <summary>
        /// Zero identifies an ordinary stackable item. A positive value makes
        /// every stored unit a durable, non-stackable tool whose current value
        /// belongs to the authoritative inventory stack.
        /// </summary>
        public int MaximumDurability { get; }

        public bool HasDurability => MaximumDurability > 0;
    }

    [Serializable]
    public sealed class RecipeAmountDefinition
    {
        public RecipeAmountDefinition(StableId itemId, long quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public StableId ItemId { get; }

        public long Quantity { get; }
    }

    [Serializable]
    public sealed class RecipeDefinition
    {
        public RecipeDefinition(
            StableId id,
            string key,
            string nameKey,
            IEnumerable<RecipeAmountDefinition> inputs,
            IEnumerable<RecipeAmountDefinition> outputs,
            long durationMilliseconds,
            CraftingStationKind station = CraftingStationKind.Machine,
            RecipeCategory category = RecipeCategory.Materials)
        {
            Id = id;
            Key = key;
            NameKey = nameKey;
            Inputs = CatalogCollection.Freeze(inputs);
            Outputs = CatalogCollection.Freeze(outputs);
            DurationMilliseconds = durationMilliseconds;
            Station = station;
            Category = category;
        }

        public StableId Id { get; }

        public string Key { get; }

        public string NameKey { get; }

        public IReadOnlyList<RecipeAmountDefinition> Inputs { get; }

        public IReadOnlyList<RecipeAmountDefinition> Outputs { get; }

        public long DurationMilliseconds { get; }

        public CraftingStationKind Station { get; }

        public RecipeCategory Category { get; }
    }

    [Serializable]
    public sealed class MachineDefinition
    {
        public MachineDefinition(
            StableId id,
            string key,
            string nameKey,
            int inputSlots,
            int outputSlots,
            EnergyKind requiredEnergyKind,
            long requiredPower,
            IEnumerable<StableId> supportedRecipeIds,
            int fuelSlots = 0,
            StableId fuelItemId = default,
            long fuelQuantityPerCycle = 0L,
            long inputBufferCapacityPerItem = 20L,
            long fuelBufferCapacityPerItem = 20L)
        {
            Id = id;
            Key = key;
            NameKey = nameKey;
            InputSlots = inputSlots;
            OutputSlots = outputSlots;
            RequiredEnergyKind = requiredEnergyKind;
            RequiredPower = requiredPower;
            SupportedRecipeIds = CatalogCollection.Freeze(supportedRecipeIds);
            FuelSlots = fuelSlots;
            FuelItemId = fuelItemId;
            FuelQuantityPerCycle = fuelQuantityPerCycle;
            InputBufferCapacityPerItem = inputBufferCapacityPerItem;
            FuelBufferCapacityPerItem = fuelBufferCapacityPerItem;
        }

        public StableId Id { get; }

        public string Key { get; }

        public string NameKey { get; }

        public int InputSlots { get; }

        public int OutputSlots { get; }

        public EnergyKind RequiredEnergyKind { get; }

        public long RequiredPower { get; }

        public IReadOnlyList<StableId> SupportedRecipeIds { get; }

        public int FuelSlots { get; }

        public StableId FuelItemId { get; }

        public long FuelQuantityPerCycle { get; }

        public bool RequiresFuel => FuelSlots > 0;

        public long InputBufferCapacityPerItem { get; }

        public long FuelBufferCapacityPerItem { get; }
    }

    [Serializable]
    public sealed class ContainerDefinition
    {
        public ContainerDefinition(
            StableId id,
            string key,
            string nameKey,
            int slotCount,
            long capacity)
        {
            Id = id;
            Key = key;
            NameKey = nameKey;
            SlotCount = slotCount;
            Capacity = capacity;
        }

        public StableId Id { get; }

        public string Key { get; }

        public string NameKey { get; }

        public int SlotCount { get; }

        public long Capacity { get; }
    }

    [Serializable]
    public sealed class EnergySourceDefinition
    {
        public EnergySourceDefinition(
            StableId id,
            string key,
            string nameKey,
            EnergyKind energyKind,
            long outputPower)
        {
            Id = id;
            Key = key;
            NameKey = nameKey;
            EnergyKind = energyKind;
            OutputPower = outputPower;
        }

        public StableId Id { get; }

        public string Key { get; }

        public string NameKey { get; }

        public EnergyKind EnergyKind { get; }

        public long OutputPower { get; }
    }

    [Serializable]
    public sealed class IslandResourceDefinition
    {
        public IslandResourceDefinition(StableId itemId, int minimumDeposits, int maximumDeposits)
        {
            ItemId = itemId;
            MinimumDeposits = minimumDeposits;
            MaximumDeposits = maximumDeposits;
        }

        public StableId ItemId { get; }

        public int MinimumDeposits { get; }

        public int MaximumDeposits { get; }
    }

    [Serializable]
    public sealed class IslandTemplateDefinition
    {
        public IslandTemplateDefinition(
            StableId id,
            string key,
            string nameKey,
            string biomeKey,
            IEnumerable<IslandResourceDefinition> resources)
        {
            Id = id;
            Key = key;
            NameKey = nameKey;
            BiomeKey = biomeKey;
            Resources = CatalogCollection.Freeze(resources);
        }

        public StableId Id { get; }

        public string Key { get; }

        public string NameKey { get; }

        public string BiomeKey { get; }

        public IReadOnlyList<IslandResourceDefinition> Resources { get; }
    }

    internal static class CatalogCollection
    {
        public static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
        {
            if (values == null)
            {
                return null;
            }

            return new ReadOnlyCollection<T>(values.ToArray());
        }
    }
}

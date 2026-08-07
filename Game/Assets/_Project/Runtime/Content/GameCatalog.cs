using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CML.Foundation;

namespace CML.Content
{
    /// <summary>
    /// Validated, immutable and indexed catalog used by the simulation.
    /// </summary>
    public sealed class GameCatalog
    {
        private readonly IReadOnlyDictionary<StableId, ItemDefinition> _itemsById;
        private readonly IReadOnlyDictionary<StableId, RecipeDefinition> _recipesById;
        private readonly IReadOnlyDictionary<StableId, MachineDefinition> _machinesById;
        private readonly IReadOnlyDictionary<StableId, ContainerDefinition> _containersById;
        private readonly IReadOnlyDictionary<StableId, EnergySourceDefinition> _energySourcesById;
        private readonly IReadOnlyDictionary<StableId, IslandTemplateDefinition> _islandTemplatesById;

        internal GameCatalog(CatalogDocument source)
        {
            SchemaVersion = source.SchemaVersion;
            Revision = new CatalogRevision(source.Revision);
            Items = CatalogCollection.Freeze(source.Items);
            Recipes = CatalogCollection.Freeze(source.Recipes);
            Machines = CatalogCollection.Freeze(source.Machines);
            Containers = CatalogCollection.Freeze(source.Containers);
            EnergySources = CatalogCollection.Freeze(source.EnergySources);
            IslandTemplates = CatalogCollection.Freeze(source.IslandTemplates);

            _itemsById = Index(Items, item => item.Id);
            _recipesById = Index(Recipes, recipe => recipe.Id);
            _machinesById = Index(Machines, machine => machine.Id);
            _containersById = Index(Containers, container => container.Id);
            _energySourcesById = Index(EnergySources, sourceDefinition => sourceDefinition.Id);
            _islandTemplatesById = Index(IslandTemplates, template => template.Id);
        }

        public int SchemaVersion { get; }

        public CatalogRevision Revision { get; }

        public IReadOnlyList<ItemDefinition> Items { get; }

        public IReadOnlyList<RecipeDefinition> Recipes { get; }

        public IReadOnlyList<MachineDefinition> Machines { get; }

        public IReadOnlyList<ContainerDefinition> Containers { get; }

        public IReadOnlyList<EnergySourceDefinition> EnergySources { get; }

        public IReadOnlyList<IslandTemplateDefinition> IslandTemplates { get; }

        public bool TryGetItem(StableId id, out ItemDefinition definition)
        {
            return _itemsById.TryGetValue(id, out definition);
        }

        public bool TryGetRecipe(StableId id, out RecipeDefinition definition)
        {
            return _recipesById.TryGetValue(id, out definition);
        }

        public bool TryGetMachine(StableId id, out MachineDefinition definition)
        {
            return _machinesById.TryGetValue(id, out definition);
        }

        public bool TryGetContainer(StableId id, out ContainerDefinition definition)
        {
            return _containersById.TryGetValue(id, out definition);
        }

        public bool TryGetEnergySource(StableId id, out EnergySourceDefinition definition)
        {
            return _energySourcesById.TryGetValue(id, out definition);
        }

        public bool TryGetIslandTemplate(StableId id, out IslandTemplateDefinition definition)
        {
            return _islandTemplatesById.TryGetValue(id, out definition);
        }

        private static IReadOnlyDictionary<StableId, T> Index<T>(
            IEnumerable<T> definitions,
            Func<T, StableId> idSelector)
        {
            var index = definitions.ToDictionary(idSelector);
            return new ReadOnlyDictionary<StableId, T>(index);
        }
    }
}

using System;
using System.Collections.Generic;

namespace CML.Content
{
    /// <summary>
    /// Serializable-shape source data. It is deliberately separate from the indexed
    /// runtime catalog so every source must pass validation before gameplay can use it.
    /// </summary>
    [Serializable]
    public sealed class CatalogDocument
    {
        public CatalogDocument(
            int schemaVersion,
            string revision,
            IEnumerable<ItemDefinition> items,
            IEnumerable<RecipeDefinition> recipes,
            IEnumerable<MachineDefinition> machines,
            IEnumerable<ContainerDefinition> containers,
            IEnumerable<EnergySourceDefinition> energySources,
            IEnumerable<IslandTemplateDefinition> islandTemplates)
        {
            SchemaVersion = schemaVersion;
            Revision = revision;
            Items = CatalogCollection.Freeze(items);
            Recipes = CatalogCollection.Freeze(recipes);
            Machines = CatalogCollection.Freeze(machines);
            Containers = CatalogCollection.Freeze(containers);
            EnergySources = CatalogCollection.Freeze(energySources);
            IslandTemplates = CatalogCollection.Freeze(islandTemplates);
        }

        public int SchemaVersion { get; }

        public string Revision { get; }

        public IReadOnlyList<ItemDefinition> Items { get; }

        public IReadOnlyList<RecipeDefinition> Recipes { get; }

        public IReadOnlyList<MachineDefinition> Machines { get; }

        public IReadOnlyList<ContainerDefinition> Containers { get; }

        public IReadOnlyList<EnergySourceDefinition> EnergySources { get; }

        public IReadOnlyList<IslandTemplateDefinition> IslandTemplates { get; }
    }
}

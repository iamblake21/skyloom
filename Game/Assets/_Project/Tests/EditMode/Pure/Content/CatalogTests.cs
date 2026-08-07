using System.Collections.Generic;
using System.Linq;
using CML.Content;
using CML.Foundation;
using NUnit.Framework;

namespace CML.Tests.Pure.Content
{
    public sealed class CatalogTests
    {
        [Test]
        public void BootstrapCatalogLoadsTheCompleteM0Slice()
        {
            var catalog = BootstrapCatalog.Load();

            Assert.That(catalog.SchemaVersion, Is.EqualTo(CatalogSchema.CurrentVersion));
            Assert.That(catalog.Revision.Value, Is.EqualTo(CatalogSchema.BootstrapContentRevision));
            // Materiali, kit logistico, strutture piazzabili, utensili e
            // macchine. Il conteggio va tenuto allineato a mano: è la rete che
            // si accorge se una voce sparisce dal catalogo.
            //
            // Questi numeri erano rimasti a 15/9/1 mentre il catalogo era già
            // arrivato a 18/11/2, quindi la rete era rotta e non segnalava più
            // niente da diverse aggiunte. È risuccesso: il Cavo isolato aveva
            // portato gli oggetti a 22 senza aggiornare il 21, e la Fibra
            // vegetale li porta a 23.
            Assert.That(catalog.Items.Count, Is.EqualTo(24));
            Assert.That(catalog.Recipes.Count, Is.EqualTo(15));
            Assert.That(catalog.Machines.Count, Is.EqualTo(3));
            // Terzo contenitore: la stiva dell'aeronave di HOLD-001, che aveva
            // lasciato indietro anche questo conteggio.
            Assert.That(catalog.Containers.Count, Is.EqualTo(3));
            Assert.That(catalog.EnergySources.Count, Is.EqualTo(0));
            Assert.That(catalog.IslandTemplates.Count, Is.EqualTo(2));

            Assert.That(catalog.TryGetItem(ContentIds.IronPlate, out var plate), Is.True);
            Assert.That(plate.MaxStack, Is.EqualTo(100));
            Assert.That(catalog.TryGetRecipe(ContentIds.PressIronPlate, out var recipe), Is.True);
            Assert.That(recipe.Inputs.Single().Quantity, Is.EqualTo(1));
            Assert.That(recipe.Outputs.Single().Quantity, Is.EqualTo(1));
            Assert.That(recipe.DurationMilliseconds, Is.EqualTo(5000));
            Assert.That(recipe.Station, Is.EqualTo(CraftingStationKind.Machine));
            Assert.That(
                catalog.Recipes.Count(value =>
                    value.Station == CraftingStationKind.Personal),
                Is.EqualTo(2));
            Assert.That(
                catalog.Recipes.Count(value =>
                    value.Station == CraftingStationKind.Workbench),
                Is.EqualTo(8));
            Assert.That(catalog.TryGetMachine(ContentIds.MechanicalPress, out _), Is.True);

            // L'Estrattore meccanico è l'unica macchina senza ingresso oggetti:
            // pesca dal giacimento su cui è piazzato. Le tre ricette sono la
            // mappatura giacimento -> minerale, e il piazzamento ne attiva una.
            Assert.That(catalog.TryGetMachine(ContentIds.MechanicalDrill, out var drill), Is.True);
            Assert.That(drill.InputSlots, Is.EqualTo(0));
            Assert.That(drill.OutputSlots, Is.EqualTo(1));
            Assert.That(drill.RequiresFuel, Is.True);
            Assert.That(drill.FuelItemId, Is.EqualTo(ContentIds.WoodLog));
            Assert.That(drill.SupportedRecipeIds.Count, Is.EqualTo(3));
            foreach (var recipeId in drill.SupportedRecipeIds)
            {
                Assert.That(catalog.TryGetRecipe(recipeId, out var extraction), Is.True);
                Assert.That(extraction.Category, Is.EqualTo(RecipeCategory.Extraction));
                Assert.That(extraction.Inputs.Count, Is.EqualTo(0));
                Assert.That(extraction.Outputs.Count, Is.EqualTo(1));
                Assert.That(extraction.DurationMilliseconds, Is.EqualTo(8000));
            }
            Assert.That(catalog.TryGetContainer(ContentIds.WoodenCrate, out _), Is.True);
            Assert.That(
                catalog.TryGetItem(ContentIds.WoodenCrateItem, out var crateItem),
                Is.True);
            Assert.That(crateItem.MaxStack, Is.EqualTo(20));
            Assert.That(
                catalog.TryGetItem(ContentIds.MechanicalPressItem, out var pressItem),
                Is.True);
            Assert.That(pressItem.MaxStack, Is.EqualTo(10));
            Assert.That(
                catalog.TryGetItem(ContentIds.CrudePickaxe, out var crudePickaxe),
                Is.True);
            Assert.That(crudePickaxe.MaxStack, Is.EqualTo(1));
            Assert.That(
                ContentIds.WoodenCrateItem,
                Is.Not.EqualTo(ContentIds.WoodenCrate));
            Assert.That(
                ContentIds.MechanicalPressItem,
                Is.Not.EqualTo(ContentIds.MechanicalPress));
            Assert.That(catalog.TryGetContainer(ContentIds.PlayerInventory, out var playerInventory), Is.True);
            Assert.That(playerInventory.SlotCount, Is.EqualTo(16));
            Assert.That(playerInventory.Capacity, Is.EqualTo(1600));
            Assert.That(catalog.TryGetIslandTemplate(ContentIds.MeadowIsland, out _), Is.True);
            Assert.That(catalog.TryGetIslandTemplate(ContentIds.HighlandIsland, out _), Is.True);
        }

        [Test]
        public void DuplicateStableIdFailsAtTheSecondDefinition()
        {
            var source = BootstrapCatalog.CreateDocument();
            var items = source.Items.Concat(
                new[]
                {
                    new ItemDefinition(
                        ContentIds.RawIron,
                        "item.duplicate_probe",
                        "item.duplicate_probe.name",
                        1)
                });
            var invalid = Copy(source, items: items);

            // L'indice atteso e quello della voce appena accodata, quindi si
            // deriva dal conteggio: scritto a mano si rompeva a ogni oggetto
            // nuovo aggiunto al catalogo, segnalando un guasto che non c'era.
            AssertRejected(
                invalid,
                CatalogValidationCodes.IdDuplicate,
                $"$.items[{source.Items.Count}].id",
                "duplicates $.items[0].id");
        }

        [Test]
        public void MissingRecipeIngredientFailsWithReferencePathAndCause()
        {
            var source = BootstrapCatalog.CreateDocument();
            var recipe = source.Recipes[0];
            var missingId = new StableId(0x1000000000000000UL, 0xffffffffffffffffUL);
            var invalidRecipe = new RecipeDefinition(
                recipe.Id,
                recipe.Key,
                recipe.NameKey,
                new[] { new RecipeAmountDefinition(missingId, 2) },
                recipe.Outputs,
                recipe.DurationMilliseconds);
            var invalid = Copy(source, recipes: new[] { invalidRecipe });

            AssertRejected(
                invalid,
                CatalogValidationCodes.ReferenceMissing,
                "$.recipes[0].inputs[0].itemId",
                "ingredient item " + missingId + " does not exist in $.items");
        }

        [Test]
        public void NegativeContainerCapacityFailsAtCapacity()
        {
            var source = BootstrapCatalog.CreateDocument();
            var container = source.Containers[0];
            var invalidContainer = new ContainerDefinition(
                container.Id,
                container.Key,
                container.NameKey,
                container.SlotCount,
                -1);
            var invalid = Copy(source, containers: new[] { invalidContainer });

            AssertRejected(
                invalid,
                CatalogValidationCodes.ValueOutOfRange,
                "$.containers[0].capacity",
                "capacity must be greater than zero");
        }

        [Test]
        public void RecipeDurationOutsideTheTickGridIsNotRepresentable()
        {
            var source = BootstrapCatalog.CreateDocument();
            var recipe = source.Recipes[0];
            var invalidRecipe = new RecipeDefinition(
                recipe.Id,
                recipe.Key,
                recipe.NameKey,
                recipe.Inputs,
                recipe.Outputs,
                75);
            var invalid = Copy(source, recipes: new[] { invalidRecipe });

            AssertRejected(
                invalid,
                CatalogValidationCodes.RecipeDurationUnrepresentable,
                "$.recipes[0].durationMilliseconds",
                "duration 75 ms is not an exact multiple of the 50 ms simulation tick");
        }

        [Test]
        public void ValidationErrorOrderAndTextAreDeterministic()
        {
            var source = BootstrapCatalog.CreateDocument();
            var invalid = Copy(
                source,
                items: source.Items.Concat(
                    new[]
                    {
                        new ItemDefinition(
                            ContentIds.RawIron,
                            "item.duplicate_probe",
                            "item.duplicate_probe.name",
                            -5)
                    }));

            var first = CatalogValidator.Validate(invalid);
            var second = CatalogValidator.Validate(invalid);

            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreEqual(
                first.Select(error => error.ToString()),
                second.Select(error => error.ToString()));
        }

        private static void AssertRejected(
            CatalogDocument document,
            string expectedCode,
            string expectedPath,
            string expectedCause)
        {
            var errors = CatalogValidator.Validate(document);
            var matching = errors.SingleOrDefault(error =>
                error.Code == expectedCode
                && error.Path == expectedPath
                && error.Cause == expectedCause);

            Assert.That(
                matching,
                Is.Not.Null,
                "Validation errors:" + System.Environment.NewLine + string.Join(System.Environment.NewLine, errors));

            var exception = Assert.Throws<CatalogValidationException>(() => CatalogLoader.Load(document));
            Assert.That(exception.Message, Does.Contain(expectedPath));
            Assert.That(exception.Message, Does.Contain(expectedCause));

            var loaded = CatalogLoader.TryLoad(document, out var catalog, out var tryLoadErrors);
            Assert.That(loaded, Is.False);
            Assert.That(catalog, Is.Null);
            CollectionAssert.AreEqual(errors, tryLoadErrors);
        }

        private static CatalogDocument Copy(
            CatalogDocument source,
            IEnumerable<ItemDefinition> items = null,
            IEnumerable<RecipeDefinition> recipes = null,
            IEnumerable<MachineDefinition> machines = null,
            IEnumerable<ContainerDefinition> containers = null,
            IEnumerable<EnergySourceDefinition> energySources = null,
            IEnumerable<IslandTemplateDefinition> islandTemplates = null)
        {
            return new CatalogDocument(
                source.SchemaVersion,
                source.Revision,
                items ?? source.Items,
                recipes ?? source.Recipes,
                machines ?? source.Machines,
                containers ?? source.Containers,
                energySources ?? source.EnergySources,
                islandTemplates ?? source.IslandTemplates);
        }
    }
}

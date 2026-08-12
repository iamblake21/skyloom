#include "Content/CMLGameCatalog.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId Ore(0, 1);
    const FCMLStableId Ingot(0, 2);
    const FCMLStableId Missing(0, 77);

    FCMLDefinitionIdentity Named(const TCHAR* Key)
    {
        FCMLDefinitionIdentity Identity;
        Identity.Key = Key;
        Identity.NameKey = FString(TEXT("name.")) + Key;
        return Identity;
    }

    FCMLGameCatalog MakeCatalog()
    {
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({Ore, 10, 0, Named(TEXT("item.ore"))});
        Catalog.Items.Add({Ingot, 10, 0, Named(TEXT("item.ingot"))});

        FCMLRecipeDefinition Recipe;
        Recipe.RecipeId = FCMLStableId(0, 10);
        Recipe.Identity = Named(TEXT("recipe.smelt"));
        Recipe.Station = ECMLCraftingStationKind::Workbench;
        Recipe.Inputs.Add({Ore, 2});
        Recipe.Outputs.Add({Ingot, 1});
        Catalog.Recipes.Add(Recipe);

        Catalog.Containers.Add({FCMLStableId(0, 20), 8, 100, Named(TEXT("container.crate"))});
        return Catalog;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLGameCatalogTest,
    "CML.Core.Content.GameCatalog",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLGameCatalogTest::RunTest(const FString& Parameters)
{
    ECMLCatalogFailure Failure = ECMLCatalogFailure::None;
    FCMLStableId FailingId;

    {
        const FCMLGameCatalog Catalog = MakeCatalog();
        TestTrue(TEXT("A well-formed catalog validates"), Catalog.Validate(Failure, FailingId));

        FCMLItemDefinition Item;
        TestTrue(TEXT("Items are indexed"), Catalog.TryGetItem(Ore, Item));
        TestEqual(TEXT("The right item is returned"), Item.MaxStack, static_cast<int64>(10));
        TestFalse(TEXT("An absent item is not found"), Catalog.TryGetItem(Missing, Item));

        FCMLContainerDefinition Container;
        TestTrue(TEXT("Containers are indexed"), Catalog.TryGetContainer(FCMLStableId(0, 20), Container));
        TestEqual(TEXT("Container slot count"), Container.SlotCount, 8);

        TestEqual(TEXT("The item catalog carries every item"),
            Catalog.ToItemCatalog().Items.Num(), 2);
        TestEqual(TEXT("The recipe catalog carries every recipe"),
            Catalog.ToRecipeCatalog().Recipes.Num(), 1);
    }

    // The revision is hashed into the canonical state, so a blank one would
    // make two different catalogs indistinguishable.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Revision.Value = TEXT("   ");
        TestFalse(TEXT("A blank revision is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueRequired"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ValueRequired));
    }

    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.SchemaVersion = FCMLGameCatalog::CurrentSchemaVersion + 1;
        TestFalse(TEXT("Another schema is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is SchemaUnsupported"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::SchemaUnsupported));
    }

    // A duplicate id makes lookup order decide which definition wins.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Items.Add({Ore, 5, 0, Named(TEXT("item.ore.again"))});
        TestFalse(TEXT("A duplicate item id is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is IdDuplicate"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::IdDuplicate));
        TestTrue(TEXT("The offending id is reported"), FailingId == Ore);
    }

    // A recipe naming an unknown item would let two builds disagree about the
    // same craft.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Recipes[0].Inputs.Add({Missing, 1});
        TestFalse(TEXT("A dangling recipe reference is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ReferenceMissing"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ReferenceMissing));
        TestTrue(TEXT("The missing item is reported"), FailingId == Missing);
    }

    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Items[0].MaxStack = 0;
        TestFalse(TEXT("A non-positive stack is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));
    }

    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Recipes[0].Outputs.Reset();
        TestFalse(TEXT("A recipe with no output is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));
    }

    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Items.Add({FCMLStableId::None(), 4, 0, Named(TEXT("item.nameless"))});
        TestFalse(TEXT("A zero id is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is IdMissing"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::IdMissing));
    }

    // Keys are the other half of a definition's identity. Content refers to
    // definitions by key, so a key that is not canonical, or that two
    // definitions share, is as ambiguous as a duplicate id.
    {
        for (const TCHAR* Rejected : {TEXT(""), TEXT("Item.Ore"), TEXT("item ore"), TEXT("item/ore")})
        {
            FCMLGameCatalog Catalog = MakeCatalog();
            Catalog.Items[0].Identity.Key = Rejected;
            TestFalse(
                *FString::Printf(TEXT("Key '%s' is refused"), Rejected),
                Catalog.Validate(Failure, FailingId));
            TestEqual(TEXT("Failure is KeyInvalid"),
                static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::KeyInvalid));
            TestTrue(TEXT("The offending item is reported"), FailingId == Ore);
        }

        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Items[0].Identity.NameKey = TEXT("Name.Ore");
        TestFalse(TEXT("A non-canonical localisation key is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is KeyInvalid"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::KeyInvalid));
    }

    // One key namespace covers every definition type, so an item and a
    // container may not both answer to the same key.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Containers[0].Identity.Key = TEXT("item.ore");
        TestFalse(TEXT("A key shared across kinds is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is KeyDuplicate"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::KeyDuplicate));
        TestTrue(TEXT("The offending container is reported"),
            FailingId == FCMLStableId(0, 20));
    }

    // Wear belongs to one unit: a durable item that stacked would share one
    // durability value between several tools.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        Catalog.Items[0].MaximumDurability = 50;
        TestFalse(TEXT("A durable item that stacks is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));

        Catalog.Items[0].MaxStack = 1;
        TestTrue(TEXT("A durable item with a stack of one validates"),
            Catalog.Validate(Failure, FailingId));

        Catalog.Items[0].MaximumDurability = -1;
        TestFalse(TEXT("Negative durability is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLCatalogMachinesTest,
    "CML.Core.Content.CatalogMachines",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLCatalogMachinesTest::RunTest(const FString& Parameters)
{
    ECMLCatalogFailure Failure = ECMLCatalogFailure::None;
    FCMLStableId FailingId;
    const FCMLStableId MachineId(0, 30);
    const FCMLStableId SourceId(0, 40);
    const FCMLStableId TemplateId(0, 50);

    auto WithMachine = [&](const int32 InputSlots, const int32 OutputSlots)
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        FCMLMachineDefinition Machine;
        Machine.Id = MachineId;
        Machine.Identity = Named(TEXT("machine.press"));
        Machine.InputSlots = InputSlots;
        Machine.OutputSlots = OutputSlots;
        Machine.RequiredEnergyKind = ECMLEnergyKind::Electrical;
        Machine.RequiredPower = 100;
        Machine.SupportedRecipeIds.Add(FCMLStableId(0, 10));
        Catalog.Machines.Add(Machine);
        return Catalog;
    };

    {
        const FCMLGameCatalog Catalog = WithMachine(1, 1);
        TestTrue(TEXT("A machine that fits its recipe validates"), Catalog.Validate(Failure, FailingId));
        FCMLMachineDefinition Machine;
        TestTrue(TEXT("Machines are indexed"), Catalog.TryGetMachine(MachineId, Machine));
        TestEqual(TEXT("Electrical is kind two"),
            static_cast<int32>(ECMLEnergyKind::Electrical), 2);
    }

    // A machine that cannot hold its recipe's inputs would accept a job it can
    // never run. One slot short is the case to test: zero slots is refused
    // earlier, by the extraction rule below, for a more specific reason.
    {
        FCMLGameCatalog Catalog = WithMachine(1, 1);
        Catalog.Recipes[0].Inputs.Add({Ingot, 1});
        TestFalse(TEXT("Too few input slots is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is MachineCapacityInsufficient"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCatalogFailure::MachineCapacityInsufficient));
        TestTrue(TEXT("The machine is reported"), FailingId == MachineId);
    }

    // An extractor draws from the deposit it stands on, so zero input slots is
    // legal — but only when every recipe it supports is extraction. The rule
    // runs both ways: otherwise a machine that genuinely transforms items would
    // have nowhere to receive them and would sit on MissingInput forever.
    {
        FCMLGameCatalog Catalog = WithMachine(0, 1);
        TestFalse(TEXT("An ordinary machine with no input slot is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));
        TestTrue(TEXT("The machine is reported"), FailingId == MachineId);

        // Turn the supported recipe into a genuine extraction and it validates.
        Catalog.Recipes[0].Category = ECMLRecipeCategory::Extraction;
        Catalog.Recipes[0].Inputs.Reset();
        TestTrue(TEXT("An extractor with no input slot validates"),
            Catalog.Validate(Failure, FailingId));

        // And the same extractor may not keep an input slot it cannot use.
        Catalog.Machines[0].InputSlots = 1;
        TestFalse(TEXT("An extractor with an input slot is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));
    }

    // "Extraction" must not become a label anyone can paste onto an ordinary
    // recipe to excuse it from declaring ingredients.
    {
        FCMLGameCatalog Catalog = WithMachine(1, 1);
        Catalog.Recipes[0].Category = ECMLRecipeCategory::Extraction;
        TestFalse(TEXT("An extraction recipe with ingredients is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));

        Catalog.Recipes[0].Category = ECMLRecipeCategory::Materials;
        Catalog.Recipes[0].Inputs.Reset();
        TestFalse(TEXT("An ordinary recipe with no ingredients is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));
    }

    // The energy pair has to agree or the power phase cannot decide whether the
    // machine may run.
    {
        FCMLGameCatalog Catalog = WithMachine(1, 1);
        Catalog.Machines[0].RequiredEnergyKind = ECMLEnergyKind::None;
        TestFalse(TEXT("A self-actuated machine demanding power is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is EnergyConfigurationInvalid"),
            static_cast<int32>(Failure),
            static_cast<int32>(ECMLCatalogFailure::EnergyConfigurationInvalid));

        Catalog.Machines[0].RequiredEnergyKind = ECMLEnergyKind::Electrical;
        Catalog.Machines[0].RequiredPower = 0;
        TestFalse(TEXT("A powered machine demanding nothing is refused"),
            Catalog.Validate(Failure, FailingId));
    }

    {
        FCMLGameCatalog Catalog = WithMachine(1, 1);
        Catalog.Machines[0].SupportedRecipeIds.Add(FCMLStableId(0, 999));
        TestFalse(TEXT("An unknown supported recipe is refused"),
            Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ReferenceMissing"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ReferenceMissing));
    }

    // A fuel slot with no fuel named is a machine that can never run.
    {
        FCMLGameCatalog Catalog = WithMachine(1, 1);
        Catalog.Machines[0].FuelSlots = 1;
        TestFalse(TEXT("A fuel slot without fuel is refused"), Catalog.Validate(Failure, FailingId));
        Catalog.Machines[0].FuelItemId = Ore;
        Catalog.Machines[0].FuelQuantityPerCycle = 1;
        TestTrue(TEXT("A complete fuel configuration validates"),
            Catalog.Validate(Failure, FailingId));
    }

    // A source producing nothing, or an unnamed kind, matches no machine.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        FCMLEnergySourceDefinition Source;
        Source.Id = SourceId;
        Source.Identity = Named(TEXT("energy.dynamo"));
        Source.EnergyKind = ECMLEnergyKind::Thermal;
        Source.OutputPower = 50;
        Catalog.EnergySources.Add(Source);
        TestTrue(TEXT("A valid source validates"), Catalog.Validate(Failure, FailingId));

        Catalog.EnergySources[0].OutputPower = 0;
        TestFalse(TEXT("A source producing nothing is refused"), Catalog.Validate(Failure, FailingId));
        Catalog.EnergySources[0].OutputPower = 50;
        Catalog.EnergySources[0].EnergyKind = ECMLEnergyKind::None;
        TestFalse(TEXT("A source of no kind is refused"), Catalog.Validate(Failure, FailingId));
    }

    // An inverted deposit range would ask generation for an impossible count.
    {
        FCMLGameCatalog Catalog = MakeCatalog();
        FCMLIslandTemplateDefinition Template;
        Template.Id = TemplateId;
        Template.Identity = Named(TEXT("island.meadow"));
        Template.BiomeKey = TEXT("starter");
        Template.Resources.Add({Ore, 1, 3});
        Catalog.IslandTemplates.Add(Template);
        TestTrue(TEXT("A valid template validates"), Catalog.Validate(Failure, FailingId));

        Catalog.IslandTemplates[0].Resources[0].MaximumDeposits = 0;
        TestFalse(TEXT("An inverted deposit range is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ValueOutOfRange"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ValueOutOfRange));

        Catalog.IslandTemplates[0].Resources[0].MaximumDeposits = 3;
        Catalog.IslandTemplates[0].BiomeKey = TEXT("  ");
        TestFalse(TEXT("A blank biome key is refused"), Catalog.Validate(Failure, FailingId));

        Catalog.IslandTemplates[0].BiomeKey = TEXT("starter");
        Catalog.IslandTemplates[0].Resources[0].ItemId = Missing;
        TestFalse(TEXT("An unknown resource item is refused"), Catalog.Validate(Failure, FailingId));
        TestEqual(TEXT("Failure is ReferenceMissing"),
            static_cast<int32>(Failure), static_cast<int32>(ECMLCatalogFailure::ReferenceMissing));
    }
    return true;
}
#endif

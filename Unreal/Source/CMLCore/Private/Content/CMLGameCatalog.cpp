#include "Content/CMLGameCatalog.h"

bool FCMLGameCatalog::TryGetItem(const FCMLStableId& Id, FCMLItemDefinition& OutDefinition) const
{
    for (const FCMLItemDefinition& Item : Items)
    {
        if (Item.ItemId == Id)
        {
            OutDefinition = Item;
            return true;
        }
    }
    return false;
}

bool FCMLGameCatalog::TryGetRecipe(const FCMLStableId& Id, FCMLRecipeDefinition& OutDefinition) const
{
    for (const FCMLRecipeDefinition& Recipe : Recipes)
    {
        if (Recipe.RecipeId == Id)
        {
            OutDefinition = Recipe;
            return true;
        }
    }
    return false;
}

bool FCMLGameCatalog::TryGetContainer(const FCMLStableId& Id, FCMLContainerDefinition& OutDefinition) const
{
    for (const FCMLContainerDefinition& Container : Containers)
    {
        if (Container.Id == Id)
        {
            OutDefinition = Container;
            return true;
        }
    }
    return false;
}

bool FCMLGameCatalog::TryGetMachine(const FCMLStableId& Id, FCMLMachineDefinition& OutDefinition) const
{
    for (const FCMLMachineDefinition& Machine : Machines)
    {
        if (Machine.Id == Id)
        {
            OutDefinition = Machine;
            return true;
        }
    }
    return false;
}

bool FCMLGameCatalog::TryGetEnergySource(
    const FCMLStableId& Id,
    FCMLEnergySourceDefinition& OutDefinition) const
{
    for (const FCMLEnergySourceDefinition& Source : EnergySources)
    {
        if (Source.Id == Id)
        {
            OutDefinition = Source;
            return true;
        }
    }
    return false;
}

bool FCMLGameCatalog::TryGetIslandTemplate(
    const FCMLStableId& Id,
    FCMLIslandTemplateDefinition& OutDefinition) const
{
    for (const FCMLIslandTemplateDefinition& Template : IslandTemplates)
    {
        if (Template.Id == Id)
        {
            OutDefinition = Template;
            return true;
        }
    }
    return false;
}

FCMLItemCatalog FCMLGameCatalog::ToItemCatalog() const
{
    FCMLItemCatalog Catalog;
    Catalog.Items = Items;
    return Catalog;
}

FCMLRecipeCatalog FCMLGameCatalog::ToRecipeCatalog() const
{
    FCMLRecipeCatalog Catalog;
    Catalog.Recipes = Recipes;
    return Catalog;
}

bool FCMLGameCatalog::Validate(ECMLCatalogFailure& OutFailure, FCMLStableId& OutFailingId) const
{
    OutFailure = ECMLCatalogFailure::None;
    OutFailingId = FCMLStableId::None();

    if (SchemaVersion != CurrentSchemaVersion)
    {
        // A catalog from another schema may mean something different by the
        // same fields; reading it would be guessing.
        OutFailure = ECMLCatalogFailure::SchemaUnsupported;
        return false;
    }
    if (!Revision.IsValid())
    {
        // The revision is hashed into the canonical state, so a blank one would
        // make two different catalogs indistinguishable.
        OutFailure = ECMLCatalogFailure::ValueRequired;
        return false;
    }

    // Keys live in one namespace across every definition type, exactly as in
    // the Unity validator: an item and a machine may not share a key, because
    // content refers to them by key alone.
    TSet<FString> Keys;
    const auto CheckIdentity =
        [&Keys, &OutFailure, &OutFailingId](
            const FCMLDefinitionIdentity& Identity, const FCMLStableId& Id) -> bool
    {
        if (!FCMLDefinitionIdentity::IsCanonicalKey(Identity.Key)
            || !FCMLDefinitionIdentity::IsCanonicalKey(Identity.NameKey))
        {
            OutFailure = ECMLCatalogFailure::KeyInvalid;
            OutFailingId = Id;
            return false;
        }
        bool bAlreadyPresent = false;
        Keys.Add(Identity.Key, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::KeyDuplicate;
            OutFailingId = Id;
            return false;
        }
        return true;
    };

    TSet<FCMLStableId> ItemIds;
    for (const FCMLItemDefinition& Item : Items)
    {
        if (Item.ItemId.IsNone())
        {
            OutFailure = ECMLCatalogFailure::IdMissing;
            return false;
        }
        if (!CheckIdentity(Item.Identity, Item.ItemId))
        {
            return false;
        }
        if (Item.MaxStack <= 0 || Item.MaximumDurability < 0)
        {
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Item.ItemId;
            return false;
        }
        if (Item.HasDurability() && Item.MaxStack != 1)
        {
            // Wear belongs to one unit. A durable item that stacked would have
            // to share one durability value between several tools.
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Item.ItemId;
            return false;
        }
        bool bAlreadyPresent = false;
        ItemIds.Add(Item.ItemId, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::IdDuplicate;
            OutFailingId = Item.ItemId;
            return false;
        }
    }

    TSet<FCMLStableId> ContainerIds;
    for (const FCMLContainerDefinition& Container : Containers)
    {
        if (Container.Id.IsNone())
        {
            OutFailure = ECMLCatalogFailure::IdMissing;
            return false;
        }
        if (!CheckIdentity(Container.Identity, Container.Id))
        {
            return false;
        }
        if (Container.SlotCount <= 0 || Container.Capacity < 0)
        {
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Container.Id;
            return false;
        }
        bool bAlreadyPresent = false;
        ContainerIds.Add(Container.Id, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::IdDuplicate;
            OutFailingId = Container.Id;
            return false;
        }
    }

    TSet<FCMLStableId> RecipeIds;
    for (const FCMLRecipeDefinition& Recipe : Recipes)
    {
        if (Recipe.RecipeId.IsNone())
        {
            OutFailure = ECMLCatalogFailure::IdMissing;
            return false;
        }
        if (!CheckIdentity(Recipe.Identity, Recipe.RecipeId))
        {
            return false;
        }
        bool bAlreadyPresent = false;
        RecipeIds.Add(Recipe.RecipeId, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::IdDuplicate;
            OutFailingId = Recipe.RecipeId;
            return false;
        }
        if (Recipe.Category == ECMLRecipeCategory::None || Recipe.Outputs.Num() == 0)
        {
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Recipe.RecipeId;
            return false;
        }
        // An extraction recipe produces from a deposit, so it is the one kind
        // allowed to declare no ingredients. The rule runs both ways on purpose:
        // without the second half, "extraction" would be a label anyone could
        // paste onto an ordinary recipe to silence the first.
        if (Recipe.Inputs.IsEmpty() != Recipe.IsExtraction())
        {
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Recipe.RecipeId;
            return false;
        }

        // A recipe that names an item the catalog does not define would let two
        // builds disagree about the same craft.
        for (const TArray<FCMLRecipeAmount>* Side : {&Recipe.Inputs, &Recipe.Outputs})
        {
            for (const FCMLRecipeAmount& Amount : *Side)
            {
                if (Amount.Quantity <= 0)
                {
                    OutFailure = ECMLCatalogFailure::ValueOutOfRange;
                    OutFailingId = Recipe.RecipeId;
                    return false;
                }
                if (!ItemIds.Contains(Amount.ItemId))
                {
                    OutFailure = ECMLCatalogFailure::ReferenceMissing;
                    OutFailingId = Amount.ItemId;
                    return false;
                }
            }
        }
    }

    TSet<FCMLStableId> MachineIds;
    for (const FCMLMachineDefinition& Machine : Machines)
    {
        if (Machine.Id.IsNone())
        {
            OutFailure = ECMLCatalogFailure::IdMissing;
            return false;
        }
        if (!CheckIdentity(Machine.Identity, Machine.Id))
        {
            return false;
        }
        bool bAlreadyPresent = false;
        MachineIds.Add(Machine.Id, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::IdDuplicate;
            OutFailingId = Machine.Id;
            return false;
        }
        if (Machine.InputSlots < 0 || Machine.OutputSlots < 0 || Machine.FuelSlots < 0)
        {
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Machine.Id;
            return false;
        }

        // An extractor has no input slots: it draws from the deposit it stands
        // on. Zero is therefore legal, but only for a machine whose whole recipe
        // list is extraction — otherwise a machine that genuinely transforms
        // items would have nowhere to receive them and would sit on
        // MissingInput forever. A machine with no recipes at all is not an
        // extractor, it is unconfigured, and keeps its input slot.
        bool bExtractsOnly = !Machine.SupportedRecipeIds.IsEmpty();
        for (const FCMLStableId& SupportedRecipeId : Machine.SupportedRecipeIds)
        {
            FCMLRecipeDefinition Supported;
            if (!TryGetRecipe(SupportedRecipeId, Supported) || !Supported.IsExtraction())
            {
                bExtractsOnly = false;
                break;
            }
        }
        if ((Machine.InputSlots == 0) != bExtractsOnly)
        {
            OutFailure = ECMLCatalogFailure::ValueOutOfRange;
            OutFailingId = Machine.Id;
            return false;
        }

        // A self-actuated machine must require no external power, and a powered
        // one must require some: either way round, the pair has to agree or the
        // power phase cannot decide whether the machine may run.
        if (Machine.RequiredEnergyKind == ECMLEnergyKind::None)
        {
            if (Machine.RequiredPower != 0)
            {
                OutFailure = ECMLCatalogFailure::EnergyConfigurationInvalid;
                OutFailingId = Machine.Id;
                return false;
            }
        }
        else if (Machine.RequiredPower <= 0)
        {
            OutFailure = ECMLCatalogFailure::EnergyConfigurationInvalid;
            OutFailingId = Machine.Id;
            return false;
        }

        for (const FCMLStableId& SupportedRecipeId : Machine.SupportedRecipeIds)
        {
            FCMLRecipeDefinition Recipe;
            if (!TryGetRecipe(SupportedRecipeId, Recipe))
            {
                OutFailure = ECMLCatalogFailure::ReferenceMissing;
                OutFailingId = SupportedRecipeId;
                return false;
            }
            // A machine that cannot hold a recipe's inputs or outputs would
            // accept a job it can never run.
            if (Recipe.Inputs.Num() > Machine.InputSlots
                || Recipe.Outputs.Num() > Machine.OutputSlots)
            {
                OutFailure = ECMLCatalogFailure::MachineCapacityInsufficient;
                OutFailingId = Machine.Id;
                return false;
            }
        }

        if (Machine.FuelSlots > 0)
        {
            if (Machine.FuelItemId.IsNone() || Machine.FuelQuantityPerCycle <= 0)
            {
                OutFailure = ECMLCatalogFailure::EnergyConfigurationInvalid;
                OutFailingId = Machine.Id;
                return false;
            }
            if (!ItemIds.Contains(Machine.FuelItemId))
            {
                OutFailure = ECMLCatalogFailure::ReferenceMissing;
                OutFailingId = Machine.FuelItemId;
                return false;
            }
        }
    }

    TSet<FCMLStableId> EnergyIds;
    for (const FCMLEnergySourceDefinition& Source : EnergySources)
    {
        if (Source.Id.IsNone())
        {
            OutFailure = ECMLCatalogFailure::IdMissing;
            return false;
        }
        if (!CheckIdentity(Source.Identity, Source.Id))
        {
            return false;
        }
        bool bAlreadyPresent = false;
        EnergyIds.Add(Source.Id, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::IdDuplicate;
            OutFailingId = Source.Id;
            return false;
        }
        // A source that produces nothing, or produces an unnamed kind of
        // energy, cannot be matched to any machine.
        if (Source.EnergyKind == ECMLEnergyKind::None || Source.OutputPower <= 0)
        {
            OutFailure = ECMLCatalogFailure::EnergyConfigurationInvalid;
            OutFailingId = Source.Id;
            return false;
        }
    }

    TSet<FCMLStableId> TemplateIds;
    for (const FCMLIslandTemplateDefinition& Template : IslandTemplates)
    {
        if (Template.Id.IsNone())
        {
            OutFailure = ECMLCatalogFailure::IdMissing;
            return false;
        }
        if (!CheckIdentity(Template.Identity, Template.Id))
        {
            return false;
        }
        bool bAlreadyPresent = false;
        TemplateIds.Add(Template.Id, &bAlreadyPresent);
        if (bAlreadyPresent)
        {
            OutFailure = ECMLCatalogFailure::IdDuplicate;
            OutFailingId = Template.Id;
            return false;
        }
        if (Template.BiomeKey.TrimStartAndEnd().IsEmpty())
        {
            OutFailure = ECMLCatalogFailure::ValueRequired;
            OutFailingId = Template.Id;
            return false;
        }
        for (const FCMLIslandResourceDefinition& Resource : Template.Resources)
        {
            if (!ItemIds.Contains(Resource.ItemId))
            {
                OutFailure = ECMLCatalogFailure::ReferenceMissing;
                OutFailingId = Resource.ItemId;
                return false;
            }
            // An inverted or negative range would make generation ask for an
            // impossible number of deposits.
            if (Resource.MinimumDeposits < 0 || Resource.MaximumDeposits < Resource.MinimumDeposits)
            {
                OutFailure = ECMLCatalogFailure::ValueOutOfRange;
                OutFailingId = Template.Id;
                return false;
            }
        }
    }
    return true;
}

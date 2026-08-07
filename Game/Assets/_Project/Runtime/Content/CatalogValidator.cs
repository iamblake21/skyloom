using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CML.Foundation;

namespace CML.Content
{
    public static class CatalogValidationCodes
    {
        public const string DocumentMissing = "CATALOG_DOCUMENT_MISSING";
        public const string SchemaUnsupported = "CATALOG_SCHEMA_UNSUPPORTED";
        public const string ValueRequired = "CATALOG_VALUE_REQUIRED";
        public const string CollectionMissing = "CATALOG_COLLECTION_MISSING";
        public const string EntryMissing = "CATALOG_ENTRY_MISSING";
        public const string IdMissing = "CATALOG_ID_MISSING";
        public const string IdDuplicate = "CATALOG_ID_DUPLICATE";
        public const string KeyInvalid = "CATALOG_KEY_INVALID";
        public const string KeyDuplicate = "CATALOG_KEY_DUPLICATE";
        public const string ValueOutOfRange = "CATALOG_VALUE_OUT_OF_RANGE";
        public const string ReferenceMissing = "CATALOG_REFERENCE_MISSING";
        public const string ReferenceDuplicate = "CATALOG_REFERENCE_DUPLICATE";
        public const string RecipeDurationUnrepresentable = "CATALOG_RECIPE_DURATION_UNREPRESENTABLE";
        public const string RecipeQuantityUnrepresentable = "CATALOG_RECIPE_QUANTITY_UNREPRESENTABLE";
        public const string EnergyConfigurationInvalid = "CATALOG_ENERGY_CONFIGURATION_INVALID";
        public const string MachineCapacityInsufficient = "CATALOG_MACHINE_CAPACITY_INSUFFICIENT";
    }

    [Serializable]
    public sealed class CatalogValidationError : IEquatable<CatalogValidationError>
    {
        public CatalogValidationError(string code, string path, string cause)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Cause = cause ?? throw new ArgumentNullException(nameof(cause));
        }

        public string Code { get; }

        public string Path { get; }

        public string Cause { get; }

        public bool Equals(CatalogValidationError other)
        {
            return other != null
                && string.Equals(Code, other.Code, StringComparison.Ordinal)
                && string.Equals(Path, other.Path, StringComparison.Ordinal)
                && string.Equals(Cause, other.Cause, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CatalogValidationError);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Code);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Path);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Cause);
                return hash;
            }
        }

        public override string ToString()
        {
            return Code + " at " + Path + ": " + Cause;
        }
    }

    public static class CatalogValidator
    {
        public static IReadOnlyList<CatalogValidationError> Validate(CatalogDocument document)
        {
            var errors = new List<CatalogValidationError>();
            if (document == null)
            {
                Add(errors, CatalogValidationCodes.DocumentMissing, "$", "catalog document is null");
                return new ReadOnlyCollection<CatalogValidationError>(errors);
            }

            if (document.SchemaVersion != CatalogSchema.CurrentVersion)
            {
                Add(
                    errors,
                    CatalogValidationCodes.SchemaUnsupported,
                    "$.schemaVersion",
                    "expected " + CatalogSchema.CurrentVersion + " but found " + document.SchemaVersion);
            }

            if (string.IsNullOrWhiteSpace(document.Revision))
            {
                Add(errors, CatalogValidationCodes.ValueRequired, "$.revision", "revision is empty");
            }

            ValidateCollectionPresence(document.Items, "$.items", errors);
            ValidateCollectionPresence(document.Recipes, "$.recipes", errors);
            ValidateCollectionPresence(document.Machines, "$.machines", errors);
            ValidateCollectionPresence(document.Containers, "$.containers", errors);
            ValidateCollectionPresence(document.EnergySources, "$.energySources", errors);
            ValidateCollectionPresence(document.IslandTemplates, "$.islandTemplates", errors);

            var ids = new Dictionary<StableId, string>();
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);

            ValidateItemIdentities(document.Items, ids, keys, errors);
            ValidateRecipeIdentities(document.Recipes, ids, keys, errors);
            ValidateMachineIdentities(document.Machines, ids, keys, errors);
            ValidateContainerIdentities(document.Containers, ids, keys, errors);
            ValidateEnergySourceIdentities(document.EnergySources, ids, keys, errors);
            ValidateIslandIdentities(document.IslandTemplates, ids, keys, errors);

            var itemIds = CollectIds(document.Items, definition => definition.Id);
            var recipeIds = CollectIds(document.Recipes, definition => definition.Id);

            ValidateItems(document.Items, errors);
            ValidateRecipes(document.Recipes, itemIds, errors);
            ValidateMachines(
                document.Machines,
                itemIds,
                recipeIds,
                document.Recipes,
                errors);
            ValidateContainers(document.Containers, errors);
            ValidateEnergySources(document.EnergySources, errors);
            ValidateIslands(document.IslandTemplates, itemIds, errors);

            return new ReadOnlyCollection<CatalogValidationError>(errors);
        }

        private static void ValidateItemIdentities(
            IReadOnlyList<ItemDefinition> definitions,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
        {
            ValidateIdentities(
                definitions,
                "$.items",
                definition => definition.Id,
                definition => definition.Key,
                definition => definition.NameKey,
                ids,
                keys,
                errors);
        }

        private static void ValidateRecipeIdentities(
            IReadOnlyList<RecipeDefinition> definitions,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
        {
            ValidateIdentities(
                definitions,
                "$.recipes",
                definition => definition.Id,
                definition => definition.Key,
                definition => definition.NameKey,
                ids,
                keys,
                errors);
        }

        private static void ValidateMachineIdentities(
            IReadOnlyList<MachineDefinition> definitions,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
        {
            ValidateIdentities(
                definitions,
                "$.machines",
                definition => definition.Id,
                definition => definition.Key,
                definition => definition.NameKey,
                ids,
                keys,
                errors);
        }

        private static void ValidateContainerIdentities(
            IReadOnlyList<ContainerDefinition> definitions,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
        {
            ValidateIdentities(
                definitions,
                "$.containers",
                definition => definition.Id,
                definition => definition.Key,
                definition => definition.NameKey,
                ids,
                keys,
                errors);
        }

        private static void ValidateEnergySourceIdentities(
            IReadOnlyList<EnergySourceDefinition> definitions,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
        {
            ValidateIdentities(
                definitions,
                "$.energySources",
                definition => definition.Id,
                definition => definition.Key,
                definition => definition.NameKey,
                ids,
                keys,
                errors);
        }

        private static void ValidateIslandIdentities(
            IReadOnlyList<IslandTemplateDefinition> definitions,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
        {
            ValidateIdentities(
                definitions,
                "$.islandTemplates",
                definition => definition.Id,
                definition => definition.Key,
                definition => definition.NameKey,
                ids,
                keys,
                errors);
        }

        private static void ValidateIdentities<T>(
            IReadOnlyList<T> definitions,
            string collectionPath,
            Func<T, StableId> idSelector,
            Func<T, string> keySelector,
            Func<T, string> nameKeySelector,
            IDictionary<StableId, string> ids,
            IDictionary<string, string> keys,
            ICollection<CatalogValidationError> errors)
            where T : class
        {
            if (definitions == null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var path = collectionPath + "[" + index + "]";
                if (definition == null)
                {
                    Add(errors, CatalogValidationCodes.EntryMissing, path, "entry is null");
                    continue;
                }

                var id = idSelector(definition);
                if (id.IsNone)
                {
                    Add(errors, CatalogValidationCodes.IdMissing, path + ".id", "the all-zero ID is reserved");
                }
                else if (ids.TryGetValue(id, out var firstIdPath))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.IdDuplicate,
                        path + ".id",
                        "duplicates " + firstIdPath);
                }
                else
                {
                    ids.Add(id, path + ".id");
                }

                var key = keySelector(definition);
                if (!IsCanonicalKey(key))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.KeyInvalid,
                        path + ".key",
                        "key must use lowercase ASCII letters, digits, '.', '_' or '-'");
                }
                else if (keys.TryGetValue(key, out var firstKeyPath))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.KeyDuplicate,
                        path + ".key",
                        "duplicates " + firstKeyPath);
                }
                else
                {
                    keys.Add(key, path + ".key");
                }

                if (!IsCanonicalKey(nameKeySelector(definition)))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.KeyInvalid,
                        path + ".nameKey",
                        "localization key must use lowercase ASCII letters, digits, '.', '_' or '-'");
                }
            }
        }

        private static void ValidateItems(
            IReadOnlyList<ItemDefinition> definitions,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                if (definition.MaxStack <= 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        "$.items[" + index + "].maxStack",
                        "maxStack must be greater than zero");
                }

                if (definition.MaximumDurability < 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        "$.items[" + index + "].maximumDurability",
                        "maximumDurability cannot be negative");
                }

                if (definition.MaximumDurability > 0
                    && definition.MaxStack != 1)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        "$.items[" + index + "].maxStack",
                        "a durable item must have maxStack equal to one");
                }
            }
        }

        private static void ValidateRecipes(
            IReadOnlyList<RecipeDefinition> definitions,
            ISet<StableId> itemIds,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                var path = "$.recipes[" + index + "]";
                ValidateRecipeAmounts(definition.Inputs, path + ".inputs", "ingredient", itemIds, errors);
                ValidateRecipeAmounts(definition.Outputs, path + ".outputs", "product", itemIds, errors);

                var isExtraction = definition.Category == RecipeCategory.Extraction;
                if (definition.Inputs != null && definition.Inputs.Count == 0 && !isExtraction)
                {
                    Add(errors, CatalogValidationCodes.ValueRequired, path + ".inputs", "recipe has no ingredients");
                }

                // The rule runs both ways on purpose. Without this half,
                // "extraction" would be a label anyone could paste onto an
                // ordinary recipe to silence the check above.
                if (definition.Inputs != null && definition.Inputs.Count > 0 && isExtraction)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".inputs",
                        "an extraction recipe produces from a deposit and cannot declare ingredients");
                }

                if (definition.Outputs != null && definition.Outputs.Count == 0)
                {
                    Add(errors, CatalogValidationCodes.ValueRequired, path + ".outputs", "recipe has no products");
                }

                if (definition.DurationMilliseconds <= 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".durationMilliseconds",
                        "duration must be greater than zero");
                }
                else if (definition.DurationMilliseconds % SimulationTick.MillisecondsPerTick != 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.RecipeDurationUnrepresentable,
                        path + ".durationMilliseconds",
                        "duration " + definition.DurationMilliseconds
                        + " ms is not an exact multiple of the "
                        + SimulationTick.MillisecondsPerTick
                        + " ms simulation tick");
                }

                if (!Enum.IsDefined(
                        typeof(CraftingStationKind),
                        definition.Station))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".station",
                        "crafting station is not supported");
                }

                if (!Enum.IsDefined(
                        typeof(RecipeCategory),
                        definition.Category))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".category",
                        "recipe category is not supported");
                }
            }
        }

        private static void ValidateRecipeAmounts(
            IReadOnlyList<RecipeAmountDefinition> amounts,
            string path,
            string role,
            ISet<StableId> itemIds,
            ICollection<CatalogValidationError> errors)
        {
            if (amounts == null)
            {
                Add(errors, CatalogValidationCodes.CollectionMissing, path, role + " collection is null");
                return;
            }

            var referencedItems = new Dictionary<StableId, string>();
            long total = 0;
            var totalRepresentable = true;

            for (var index = 0; index < amounts.Count; index++)
            {
                var amount = amounts[index];
                var amountPath = path + "[" + index + "]";
                if (amount == null)
                {
                    Add(errors, CatalogValidationCodes.EntryMissing, amountPath, role + " is null");
                    continue;
                }

                if (!itemIds.Contains(amount.ItemId))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ReferenceMissing,
                        amountPath + ".itemId",
                        role + " item " + amount.ItemId + " does not exist in $.items");
                }

                if (referencedItems.TryGetValue(amount.ItemId, out var firstPath))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ReferenceDuplicate,
                        amountPath + ".itemId",
                        "duplicates " + firstPath + "; combine quantities into one entry");
                }
                else
                {
                    referencedItems.Add(amount.ItemId, amountPath + ".itemId");
                }

                if (amount.Quantity <= 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        amountPath + ".quantity",
                        "quantity must be greater than zero");
                    continue;
                }

                if (totalRepresentable)
                {
                    try
                    {
                        total = checked(total + amount.Quantity);
                    }
                    catch (OverflowException)
                    {
                        totalRepresentable = false;
                        Add(
                            errors,
                            CatalogValidationCodes.RecipeQuantityUnrepresentable,
                            path,
                            "sum of " + role + " quantities exceeds Int64");
                    }
                }
            }
        }

        /// <summary>
        /// True when the machine declares at least one recipe and every one of
        /// them is extraction. A machine with no recipes at all is not an
        /// extractor — it is simply unconfigured — and must keep its input slot.
        /// </summary>
        private static bool ExtractsOnly(
            MachineDefinition definition,
            IReadOnlyDictionary<StableId, RecipeDefinition> recipesById)
        {
            if (definition.SupportedRecipeIds == null
                || definition.SupportedRecipeIds.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < definition.SupportedRecipeIds.Count; index++)
            {
                if (!recipesById.TryGetValue(definition.SupportedRecipeIds[index], out var recipe)
                    || recipe.Category != RecipeCategory.Extraction)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateMachines(
            IReadOnlyList<MachineDefinition> definitions,
            ISet<StableId> itemIds,
            ISet<StableId> recipeIds,
            IReadOnlyList<RecipeDefinition> recipes,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                return;
            }

            var recipesById = new Dictionary<StableId, RecipeDefinition>();
            if (recipes != null)
            {
                for (var index = 0; index < recipes.Count; index++)
                {
                    var recipe = recipes[index];
                    if (recipe != null && !recipe.Id.IsNone && !recipesById.ContainsKey(recipe.Id))
                    {
                        recipesById.Add(recipe.Id, recipe);
                    }
                }
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                var path = "$.machines[" + index + "]";

                // An extractor has no input slots: it draws from the deposit it
                // stands on. Zero is therefore legal, but only for a machine
                // whose whole recipe list is extraction — otherwise a machine
                // that genuinely transforms items would have nowhere to receive
                // them and would sit on MissingInput forever.
                var extractsOnly = ExtractsOnly(definition, recipesById);
                if (definition.InputSlots < 0
                    || (definition.InputSlots == 0 && !extractsOnly))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".inputSlots",
                        "inputSlots must be greater than zero unless every supported recipe is extraction");
                }

                if (definition.InputSlots > 0 && extractsOnly)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".inputSlots",
                        "an extraction-only machine must declare zero input slots");
                }

                if (definition.OutputSlots <= 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".outputSlots",
                        "outputSlots must be greater than zero");
                }

                if (definition.InputBufferCapacityPerItem <= 0L)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".inputBufferCapacityPerItem",
                        "input buffer capacity must be greater than zero");
                }

                if (definition.FuelSlots < 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".fuelSlots",
                        "fuelSlots cannot be negative");
                }

                if (definition.FuelSlots == 0)
                {
                    if (!definition.FuelItemId.IsNone
                        || definition.FuelQuantityPerCycle != 0L)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ValueOutOfRange,
                            path + ".fuelSlots",
                            "a machine without fuel slots cannot consume fuel");
                    }
                }
                else
                {
                    if (definition.FuelItemId.IsNone
                        || !itemIds.Contains(definition.FuelItemId))
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ReferenceMissing,
                            path + ".fuelItemId",
                            "fuel item does not exist in $.items");
                    }

                    if (definition.FuelQuantityPerCycle <= 0L)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ValueOutOfRange,
                            path + ".fuelQuantityPerCycle",
                            "fuel quantity per cycle must be greater than zero");
                    }


                    if (definition.FuelBufferCapacityPerItem <= 0L)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ValueOutOfRange,
                            path + ".fuelBufferCapacityPerItem",
                            "fuel buffer capacity must be greater than zero");
                    }
                }

                if (definition.RequiredEnergyKind == EnergyKind.None)
                {
                    if (definition.RequiredPower != 0)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.EnergyConfigurationInvalid,
                            path + ".requiredPower",
                            "a self-actuated machine must require zero external power");
                    }
                }
                else
                {
                    ValidatePowerConfiguration(
                        definition.RequiredEnergyKind,
                        definition.RequiredPower,
                        path + ".requiredEnergyKind",
                        path + ".requiredPower",
                        errors);
                }

                if (definition.SupportedRecipeIds == null)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.CollectionMissing,
                        path + ".supportedRecipeIds",
                        "supported recipe collection is null");
                    continue;
                }

                if (definition.SupportedRecipeIds.Count == 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueRequired,
                        path + ".supportedRecipeIds",
                        "machine supports no recipes");
                }

                var seen = new Dictionary<StableId, string>();
                for (var recipeIndex = 0; recipeIndex < definition.SupportedRecipeIds.Count; recipeIndex++)
                {
                    var recipeId = definition.SupportedRecipeIds[recipeIndex];
                    var recipePath = path + ".supportedRecipeIds[" + recipeIndex + "]";
                    if (!recipeIds.Contains(recipeId))
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ReferenceMissing,
                            recipePath,
                            "recipe " + recipeId + " does not exist in $.recipes");
                    }

                    if (seen.TryGetValue(recipeId, out var firstPath))
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ReferenceDuplicate,
                            recipePath,
                            "duplicates " + firstPath);
                    }
                    else
                    {
                        seen.Add(recipeId, recipePath);
                    }

                    if (!recipesById.TryGetValue(recipeId, out var recipe))
                    {
                        continue;
                    }

                    if (recipe.Inputs != null && recipe.Inputs.Count > definition.InputSlots)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.MachineCapacityInsufficient,
                            path + ".inputSlots",
                            "recipe " + recipe.Key + " requires " + recipe.Inputs.Count + " input slots");
                    }

                    if (recipe.Outputs != null && recipe.Outputs.Count > definition.OutputSlots)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.MachineCapacityInsufficient,
                            path + ".outputSlots",
                            "recipe " + recipe.Key + " requires " + recipe.Outputs.Count + " output slots");
                    }
                }
            }
        }

        private static void ValidateContainers(
            IReadOnlyList<ContainerDefinition> definitions,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                var path = "$.containers[" + index + "]";
                if (definition.SlotCount <= 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".slotCount",
                        "slotCount must be greater than zero");
                }

                if (definition.Capacity <= 0)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.ValueOutOfRange,
                        path + ".capacity",
                        "capacity must be greater than zero");
                }
            }
        }

        private static void ValidateEnergySources(
            IReadOnlyList<EnergySourceDefinition> definitions,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                ValidatePowerConfiguration(
                    definition.EnergyKind,
                    definition.OutputPower,
                    "$.energySources[" + index + "].energyKind",
                    "$.energySources[" + index + "].outputPower",
                    errors);
            }
        }

        private static void ValidateIslands(
            IReadOnlyList<IslandTemplateDefinition> definitions,
            ISet<StableId> itemIds,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                return;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                var path = "$.islandTemplates[" + index + "]";
                if (!IsCanonicalKey(definition.BiomeKey))
                {
                    Add(
                        errors,
                        CatalogValidationCodes.KeyInvalid,
                        path + ".biomeKey",
                        "biome key must use lowercase ASCII letters, digits, '.', '_' or '-'");
                }

                if (definition.Resources == null)
                {
                    Add(
                        errors,
                        CatalogValidationCodes.CollectionMissing,
                        path + ".resources",
                        "resource collection is null");
                    continue;
                }

                var seen = new Dictionary<StableId, string>();
                for (var resourceIndex = 0; resourceIndex < definition.Resources.Count; resourceIndex++)
                {
                    var resource = definition.Resources[resourceIndex];
                    var resourcePath = path + ".resources[" + resourceIndex + "]";
                    if (resource == null)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.EntryMissing,
                            resourcePath,
                            "resource is null");
                        continue;
                    }

                    if (!itemIds.Contains(resource.ItemId))
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ReferenceMissing,
                            resourcePath + ".itemId",
                            "resource item " + resource.ItemId + " does not exist in $.items");
                    }

                    if (seen.TryGetValue(resource.ItemId, out var firstPath))
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ReferenceDuplicate,
                            resourcePath + ".itemId",
                            "duplicates " + firstPath);
                    }
                    else
                    {
                        seen.Add(resource.ItemId, resourcePath + ".itemId");
                    }

                    if (resource.MinimumDeposits <= 0)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ValueOutOfRange,
                            resourcePath + ".minimumDeposits",
                            "minimumDeposits must be greater than zero");
                    }

                    if (resource.MaximumDeposits < resource.MinimumDeposits)
                    {
                        Add(
                            errors,
                            CatalogValidationCodes.ValueOutOfRange,
                            resourcePath + ".maximumDeposits",
                            "maximumDeposits must be greater than or equal to minimumDeposits");
                    }
                }
            }
        }

        private static void ValidatePowerConfiguration(
            EnergyKind kind,
            long power,
            string kindPath,
            string powerPath,
            ICollection<CatalogValidationError> errors)
        {
            if (!Enum.IsDefined(typeof(EnergyKind), kind) || kind == EnergyKind.None)
            {
                Add(
                    errors,
                    CatalogValidationCodes.EnergyConfigurationInvalid,
                    kindPath,
                    "energy kind must be Electrical or Thermal");
            }

            if (power <= 0)
            {
                Add(
                    errors,
                    CatalogValidationCodes.ValueOutOfRange,
                    powerPath,
                    "power must be greater than zero");
            }
        }

        private static void ValidateCollectionPresence<T>(
            IReadOnlyList<T> definitions,
            string path,
            ICollection<CatalogValidationError> errors)
        {
            if (definitions == null)
            {
                Add(errors, CatalogValidationCodes.CollectionMissing, path, "collection is null");
            }
        }

        private static HashSet<StableId> CollectIds<T>(
            IReadOnlyList<T> definitions,
            Func<T, StableId> idSelector)
            where T : class
        {
            var result = new HashSet<StableId>();
            if (definitions == null)
            {
                return result;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition != null)
                {
                    var id = idSelector(definition);
                    if (!id.IsNone)
                    {
                        result.Add(id);
                    }
                }
            }

            return result;
        }

        private static bool IsCanonicalKey(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var allowed = character >= 'a' && character <= 'z'
                    || character >= '0' && character <= '9'
                    || character == '.'
                    || character == '_'
                    || character == '-';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Add(
            ICollection<CatalogValidationError> errors,
            string code,
            string path,
            string cause)
        {
            errors.Add(new CatalogValidationError(code, path, cause));
        }
    }
}

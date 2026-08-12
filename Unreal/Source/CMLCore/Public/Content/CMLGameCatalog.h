#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "Inventory/CMLCraftingRule.h"
#include "Inventory/CMLInventoryOperations.h"
#include "CMLGameCatalog.generated.h"

/**
 * Catalog identity, ported from CML.Content.CatalogRevision. Compared
 * ordinally, because it is hashed into the canonical state as field 4.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLCatalogRevision
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FString Value;

    bool IsValid() const { return !Value.TrimStartAndEnd().IsEmpty(); }

    friend bool operator==(const FCMLCatalogRevision& A, const FCMLCatalogRevision& B)
    {
        return A.Value.Equals(B.Value, ESearchCase::CaseSensitive);
    }
    friend bool operator!=(const FCMLCatalogRevision& A, const FCMLCatalogRevision& B) { return !(A == B); }
};

/**
 * How a machine is powered, ported from CML.Content.EnergyKind.
 *
 * The value 1 is deliberately absent: the Unity enum skips it, and renumbering
 * to close the gap would change content that already refers to these values.
 */
UENUM(BlueprintType)
enum class ECMLEnergyKind : uint8
{
    None = 0,
    Electrical = 2,
    Thermal = 3
};

/** Why a catalog was refused, using the Unity validator's own codes. */
UENUM(BlueprintType)
enum class ECMLCatalogFailure : uint8
{
    None = 0,
    SchemaUnsupported = 1,
    ValueRequired = 2,
    IdMissing = 3,
    IdDuplicate = 4,
    ReferenceMissing = 5,
    ValueOutOfRange = 6,
    EnergyConfigurationInvalid = 7,
    MachineCapacityInsufficient = 8,
    // Appended rather than slotted in alphabetically: the values above are
    // already asserted by tests, and renumbering would quietly change what a
    // failing catalog reports.
    KeyInvalid = 9,
    KeyDuplicate = 10
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLContainerDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLStableId Id;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 SlotCount = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int64 Capacity = 0;

    // Last so the fields fixtures set positionally stay leading.
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLDefinitionIdentity Identity;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLStableId Id;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 InputSlots = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 OutputSlots = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    ECMLEnergyKind RequiredEnergyKind = ECMLEnergyKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int64 RequiredPower = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLStableId> SupportedRecipeIds;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 FuelSlots = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLStableId FuelItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int64 FuelQuantityPerCycle = 0;

    /**
     * How much of one item a machine will let its input buffer hold.
     *
     * A cap separate from the port's physical room: without it a belt would
     * keep pushing until every input slot was full of one ingredient, and a
     * two-ingredient recipe would deadlock with no space for the second.
     */
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int64 InputBufferCapacityPerItem = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int64 FuelBufferCapacityPerItem = 0;

    // Last so the fields fixtures set positionally stay leading.
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLDefinitionIdentity Identity;

    bool RequiresFuel() const { return FuelSlots > 0; }
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLEnergySourceDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLStableId Id;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    ECMLEnergyKind EnergyKind = ECMLEnergyKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int64 OutputPower = 0;

    // Last so the fields fixtures set positionally stay leading.
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLDefinitionIdentity Identity;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIslandResourceDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLStableId ItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 MinimumDeposits = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 MaximumDeposits = 0;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLIslandTemplateDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLStableId Id;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FString BiomeKey;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLIslandResourceDefinition> Resources;

    // Last so the fields fixtures set positionally stay leading.
    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLDefinitionIdentity Identity;
};

/**
 * Validated, immutable, indexed catalog, ported from CML.Content.GameCatalog.
 *
 * The simulation reads content through this and nothing else, so a catalog that
 * fails validation must never reach it: a dangling recipe reference or a
 * duplicate id would make two builds disagree about the same world.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLGameCatalog
{
    GENERATED_BODY()

    static constexpr int32 CurrentSchemaVersion = 1;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    int32 SchemaVersion = CurrentSchemaVersion;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    FCMLCatalogRevision Revision;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLItemDefinition> Items;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLRecipeDefinition> Recipes;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLContainerDefinition> Containers;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLMachineDefinition> Machines;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLEnergySourceDefinition> EnergySources;

    UPROPERTY(BlueprintReadOnly, Category="CML|Content")
    TArray<FCMLIslandTemplateDefinition> IslandTemplates;

    bool TryGetItem(const FCMLStableId& Id, FCMLItemDefinition& OutDefinition) const;
    bool TryGetRecipe(const FCMLStableId& Id, FCMLRecipeDefinition& OutDefinition) const;
    bool TryGetContainer(const FCMLStableId& Id, FCMLContainerDefinition& OutDefinition) const;
    bool TryGetMachine(const FCMLStableId& Id, FCMLMachineDefinition& OutDefinition) const;
    bool TryGetEnergySource(const FCMLStableId& Id, FCMLEnergySourceDefinition& OutDefinition) const;
    bool TryGetIslandTemplate(const FCMLStableId& Id, FCMLIslandTemplateDefinition& OutDefinition) const;

    /** The item catalog the inventory and crafting rules consume. */
    FCMLItemCatalog ToItemCatalog() const;
    FCMLRecipeCatalog ToRecipeCatalog() const;

    /**
     * Full validation. `OutFailingId` names the offending entry where one
     * exists, so a refusal points at the content rather than just saying no.
     */
    bool Validate(ECMLCatalogFailure& OutFailure, FCMLStableId& OutFailingId) const;
};

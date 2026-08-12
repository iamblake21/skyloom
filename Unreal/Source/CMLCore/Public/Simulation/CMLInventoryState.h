#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "CMLInventoryState.generated.h"

/** What a slot holds. Ported from the canonical projection of ItemStack. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLItemStack
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLStableId ItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLNonNegativeQuantity Quantity;
};

/**
 * One slot. An empty slot is still a slot: which position holds what decides
 * how the next insertion lands, so emptiness is part of the future and is
 * encoded rather than skipped.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLInventorySlot
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    bool bHasStack = false;

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLItemStack Stack;
};

/**
 * One inventory. Only the fields the canonical projection reads are carried
 * here; capacity, catalog revision and durability are runtime state that the
 * Unity encoder deliberately left out of the hash.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLInventoryState
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLStableId InventoryId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLStableId ContainerDefinitionId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    TArray<FCMLInventorySlot> Slots;
};

/**
 * The INV subtree, ported from CML.Simulation.Inventories.InventorySimulationState.
 *
 * Unity held the inventories in a SortedDictionary keyed by id, so the encoded
 * order is part of the hash. `Sort` must run before serialising.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLInventorySimulationState
{
    GENERATED_BODY()

    UPROPERTY()
    TArray<FCMLInventoryState> Inventories;

    void Sort();

    /** Duplicate inventory ids would make the encoding ambiguous. */
    bool HasUniqueIds() const;
};

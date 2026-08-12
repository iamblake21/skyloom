#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLInventoryState.h"
#include "CMLInventoryOperations.generated.h"

/** Why an inventory operation was refused, ported from CML.Inventory.InventoryFailure. */
UENUM(BlueprintType)
enum class ECMLInventoryFailure : uint8
{
    None = 0,
    UnknownItem = 1,
    CapacityExceeded = 2,
    InsufficientQuantity = 3,
    InvalidDefinition = 4,
    ArithmeticOverflow = 5
};

/**
 * An item definition, ported from CML.Content.ItemDefinition.
 *
 * Inventory logic reads only `ItemId` and `MaxStack`; the rest is here because
 * the catalog validator and the HUD need it, and a second item type would drift
 * away from this one.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLItemDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLStableId ItemId;

    /** Must be positive; a non-positive stack makes the item unstorable. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    int64 MaxStack = 0;

    /**
     * Zero is an ordinary stackable item. A positive value makes every stored
     * unit a durable, non-stackable tool, which is why the validator also
     * requires such an item's stack size to be exactly one.
     */
    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    int32 MaximumDurability = 0;

    // Last so that the id and stack size, which inventory logic needs and which
    // most fixtures set, stay the leading two fields.
    UPROPERTY(BlueprintReadOnly, Category="CML|Inventory")
    FCMLDefinitionIdentity Identity;

    bool HasDurability() const { return MaximumDurability > 0; }
};

/** Minimal item catalog: the lookup inventory logic performs. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLItemCatalog
{
    GENERATED_BODY()

    UPROPERTY()
    TArray<FCMLItemDefinition> Items;

    bool TryGetItem(const FCMLStableId& ItemId, FCMLItemDefinition& OutItem) const;
};

/**
 * Inventory transactions, ported from CML.Inventory.InventoryState.
 *
 * Every operation is all-or-nothing. A partial fit is refused outright and the
 * inventory is left untouched: gameplay must fail the transaction rather than
 * store some of an amount, which is the same contract the C# original enforced
 * by returning false before mutating anything.
 */
class CMLCORE_API FCMLInventoryOperations
{
public:
    /** How much of one item this inventory could still accept. */
    static int64 StorableQuantity(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        const FCMLStableId& ItemId,
        int64 Capacity);

    /** How much of one item this inventory currently holds. */
    static int64 Count(const FCMLInventoryState& Inventory, const FCMLStableId& ItemId);

    /**
     * Stores the whole amount or nothing. Compatible partial stacks are topped
     * up before an empty slot is opened, so an inventory does not fragment
     * while a stack still has room.
     */
    static bool TryStoreEntire(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        const FCMLStableId& ItemId,
        int64 Amount,
        int64 Capacity,
        FCMLInventoryState& OutUpdated,
        ECMLInventoryFailure& OutFailure);

    /**
     * Moves a stack, or part of one, between two slots of the same inventory.
     *
     * This is the drag in the panel, and it is a rearrangement rather than a
     * transaction: nothing enters or leaves the inventory, so no capacity is
     * consulted. `Amount` of zero means the whole source stack.
     *
     * Three cases, and the third is the one worth stating: onto an empty slot
     * it splits, onto the same item it merges up to the stack limit, and onto a
     * *different* item it swaps — but only a whole stack may swap. A partial
     * swap is refused because the remainder of the source would have nowhere to
     * stay once its slot is taken by the other item, and inventing a
     * destination for it is worse than refusing.
     */
    static bool TryMoveWithinInventory(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        int32 SourceSlotIndex,
        int32 DestinationSlotIndex,
        int64 Amount,
        FCMLInventoryState& OutUpdated,
        ECMLInventoryFailure& OutFailure);

    /**
     * Removes the whole amount or nothing. Slots are drained from the last
     * towards the first, so the earliest slots keep their stacks and slot
     * positions stay as stable as the operation allows.
     */
    static bool TryTakeEntire(
        const FCMLInventoryState& Inventory,
        const FCMLStableId& ItemId,
        int64 Amount,
        FCMLInventoryState& OutUpdated,
        ECMLInventoryFailure& OutFailure);
};

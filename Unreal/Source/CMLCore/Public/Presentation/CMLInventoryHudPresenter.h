#pragma once

#include "CoreMinimal.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Simulation/CMLInventoryState.h"

#include "CMLInventoryHudPresenter.generated.h"

/**
 * Which icon a slot draws, ported from
 * CML.Unity.Presentation.Inventory.InventoryIconKind.
 *
 * The numbers are the Unity ones. They are saved in no file, but the HUD and
 * the icon set both key off them, so renumbering would silently swap pictures.
 */
UENUM(BlueprintType)
enum class ECMLInventoryIconKind : uint8
{
    Generic = 0,
    Ore = 1,
    Ingot = 2,
    Plate = 3,
    WoodenCrate = 4,
    BeltStraight = 5,
    BeltCurve = 6,
    BeltCurveLeft = 7,
    BeltIncline = 8,
    BeltSupport = 9,
    BeltFunnel = 10,
    BeltDriveUnit = 11,
    MechanicalPress = 12,
    CrudePickaxe = 13,
    Stone = 14,
    IronPickaxe = 15,
    WoodLog = 16,
    MechanicalDrill = 17,
    PlantFiber = 18,
    Stick = 19
};

/** How one item looks in one slot. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLInventorySlotPresentation
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    int32 SlotIndex = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    FCMLStableId ItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    int64 Quantity = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    FString DisplayName;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    ECMLInventoryIconKind IconKind = ECMLInventoryIconKind::Generic;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    FLinearColor AccentColor = FLinearColor::Transparent;

    /**
     * Wear, present only for tools. C# used nullable fields; the flag keeps
     * that distinction, because a durability of zero means a broken tool and
     * must not read the same as "this item has no durability".
     */
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    bool bHasDurability = false;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    float Durability01 = 0.0f;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    int32 CurrentDurability = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    int32 MaximumDurability = 0;

    bool IsOccupied() const { return !ItemId.IsNone(); }

    /**
     * The same stack shown with a different quantity. The cursor preview needs
     * it when only part of a stack is picked up: the name, icon and colour stay
     * those of the item, only the amount held changes.
     */
    FCMLInventorySlotPresentation WithQuantity(int64 NewQuantity) const;
};

/** The whole player inventory as the HUD sees it. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLInventoryUiSnapshot
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    FCMLInventoryState Source;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    TArray<FCMLInventorySlotPresentation> Slots;
};

/**
 * Read-only projection from the authoritative inventory to presentation data,
 * ported from CML.Unity.Presentation.Inventory.InventoryHudPresenter.
 *
 * It never stores, removes or moves anything: the HUD is a view of the
 * simulation, and a presenter that could write would be a second, unsynchronised
 * source of truth for what the player is carrying.
 */
class CMLCORE_API FCMLInventoryHudPresenter
{
public:
    static constexpr int32 PlayerSlotCount = 16;
    static constexpr int32 HotbarSlotCount = 8;

    /**
     * Projects the whole player inventory.
     *
     * Fails rather than improvising when the inventory is the wrong size or
     * holds an item the catalog does not define: either means the HUD and the
     * simulation disagree about the world, which is worse shown than caught.
     */
    static bool TryProject(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        FCMLInventoryUiSnapshot& OutSnapshot);

    /**
     * How one item looks in one slot.
     *
     * Public because the machine panel shows the same items in slots of the
     * same kind: a second copy of this mapping would drift, and the two panels
     * would end up disagreeing about what a plate is.
     */
    static FCMLInventorySlotPresentation ProjectSlot(
        int32 SlotIndex,
        const FCMLStableId& ItemId,
        int64 Quantity,
        const FCMLItemDefinition& Definition);

    /** As above, plus the wear to show on a tool. */
    static FCMLInventorySlotPresentation ProjectToolSlot(
        int32 SlotIndex,
        const FCMLStableId& ItemId,
        int64 Quantity,
        const FCMLItemDefinition& Definition,
        int32 CurrentDurability,
        int32 MaximumDurability);

    /** An empty slot: an index and nothing else. */
    static FCMLInventorySlotPresentation EmptySlot(int32 SlotIndex);
};

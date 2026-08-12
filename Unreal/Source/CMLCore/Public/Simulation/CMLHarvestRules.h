#pragma once

#include "CoreMinimal.h"
#include "Inventory/CMLInventoryOperations.h"
#include "CMLHarvestRules.generated.h"

UENUM(BlueprintType)
enum class ECMLHandGatherTarget : uint8
{
    None = 0,
    WildFiberTuft = 1,
    FallenSticks = 2,
    LoosePebble = 3
};

UENUM(BlueprintType)
enum class ECMLHandGatherStatus : uint8
{
    None = 0,
    Gathered = 1,
    InventoryFull = 2,
    InvalidTarget = 3,
    InvalidYield = 4
};

UENUM(BlueprintType)
enum class ECMLMiningTarget : uint8
{
    None = 0,
    EnvironmentalStone = 1,
    IronOreRock = 2,
    IronDepositSurface = 3,
    CopperOreRock = 4,
    CopperDepositSurface = 5,
    TinOreRock = 6,
    TinDepositSurface = 7
};

UENUM(BlueprintType)
enum class ECMLMiningImpactStatus : uint8
{
    None = 0,
    Progressed = 1,
    Produced = 2,
    InventoryFull = 3,
    WrongTool = 4,
    BrokenTool = 5,
    InvalidTarget = 6
};

/**
 * A tool's wear, kept beside the inventory rather than inside it.
 *
 * Durability is deliberately absent from the canonical inventory projection, so
 * it travels separately instead of being bolted onto a slot that has to stay
 * byte-exact with Unity's encoding.
 */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLToolState
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    FCMLStableId ItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    int32 Current = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    int32 Maximum = 0;

    bool IsBroken() const { return Current <= 0; }
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLHandGatherResult
{
    GENERATED_BODY()

    UPROPERTY() ECMLHandGatherStatus Status = ECMLHandGatherStatus::None;
    UPROPERTY() FCMLInventoryState UpdatedInventory;
    UPROPERTY() FCMLStableId ProducedItemId;
    UPROPERTY() int64 ProducedQuantity = 0;

    bool Gathered() const { return Status == ECMLHandGatherStatus::Gathered; }
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMiningImpactResult
{
    GENERATED_BODY()

    UPROPERTY() ECMLMiningImpactStatus Status = ECMLMiningImpactStatus::None;
    UPROPERTY() FCMLInventoryState UpdatedInventory;
    UPROPERTY() FCMLToolState UpdatedTool;
    UPROPERTY() FCMLStableId ProducedItemId;
    UPROPERTY() int32 NextHitProgress = 0;
    UPROPERTY() bool bSourceExhausted = false;
};

/**
 * Harvesting by hand and with a tool, ported from
 * CML.Simulation.Gathering.HandGatherRule and
 * CML.Simulation.Mining.ManualMiningRule.
 *
 * The two rules stay separate on purpose. Mining is written around a tool: it
 * reads the equipped slot, refuses when the hand is empty, counts hits against
 * a tool-specific requirement and spends a durability point on success.
 * Gathering has none of those, and teaching the mining rule to accept an empty
 * hand would delete the very check that stops a player mining stone with their
 * fists.
 *
 * Both commit their whole yield at once. A partial store would let a nearly
 * full inventory swallow one fibre of two and still consume the tuft, which is
 * the classic way matter goes missing.
 */
class CMLCORE_API FCMLHarvestRules
{
public:
    /**
     * Content ids the rules award, transcribed from CML.Content.ContentIds.
     *
     * These values are not arbitrary: they are hashed into the canonical state,
     * so inventing convenient numbers here would silently produce a different
     * world from the same actions.
     */
    static const FCMLStableId& PlantFiber();
    static const FCMLStableId& Stick();
    static const FCMLStableId& Stone();
    static const FCMLStableId& RawIron();
    static const FCMLStableId& RawCopper();
    static const FCMLStableId& RawTin();
    static const FCMLStableId& CrudePickaxe();
    static const FCMLStableId& IronPickaxe();

    /** How many impacts a tool needs; zero means it cannot mine at all. */
    static int32 RequiredHits(const FCMLStableId& ToolId);

    static FCMLHandGatherResult Gather(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        ECMLHandGatherTarget Target,
        int32 Units,
        int64 Capacity);

    static FCMLMiningImpactResult Impact(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        const FCMLToolState& Tool,
        ECMLMiningTarget Target,
        int32 CompletedHits,
        int64 Capacity);
};

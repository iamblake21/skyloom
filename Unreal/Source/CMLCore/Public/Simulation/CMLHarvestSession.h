#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLHarvestRules.h"

#include "CMLHarvestSession.generated.h"

/** What one strike or one grab did, for the world and the feedback layer. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLHarvestOutcome
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    ECMLMiningImpactStatus MiningStatus = ECMLMiningImpactStatus::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    ECMLHandGatherStatus GatherStatus = ECMLHandGatherStatus::None;

    /** True only when the inventory actually changed. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    bool bProduced = false;

    /** True when the world may now remove the source, and not before. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    bool bSourceExhausted = false;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    FCMLStableId ProducedItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    int64 ProducedQuantity = 0;

    /** How many hits this source has taken, for a progress indicator. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    int32 CompletedHits = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    FCMLInventoryState UpdatedInventory;

    UPROPERTY(BlueprintReadOnly, Category="CML|Harvest")
    FCMLToolState UpdatedTool;
};

/**
 * Harvesting across many blows, ported from the Unity mining and gathering
 * controllers.
 *
 * `FCMLHarvestRules` decides what one blow is worth; this owns the one piece of
 * state that has to survive between blows — how far each source has been worked
 * — and the ordering rules around it.
 *
 * Three of those rules matter and are easy to get wrong:
 *
 *  - A blow that cannot be stored **still counts**. The swing happened and the
 *    rock is that much more broken; throwing the progress away because the
 *    backpack was full would let a player mine forever without ever finishing.
 *  - Producing **clears** the source's progress, so a fresh source of the same
 *    kind starts from zero rather than inheriting the last one's tally.
 *  - The source may only be removed once the rule has published a new
 *    inventory. Removing it first is how matter goes missing.
 */
class CMLCORE_API FCMLHarvestSession
{
public:
    /** One pickaxe blow against a source. */
    FCMLHarvestOutcome Strike(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        const FCMLToolState& Tool,
        const FCMLStableId& SourceId,
        ECMLMiningTarget Target,
        int64 Capacity);

    /**
     * One bare-handed grab. All or nothing: a full backpack leaves the source
     * standing, so no matter is produced and none is lost.
     */
    FCMLHarvestOutcome Gather(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Catalog,
        const FCMLStableId& SourceId,
        ECMLHandGatherTarget Target,
        int32 Units,
        int64 Capacity);

    /** How far a source has been worked, for a progress indicator. */
    int32 GetCompletedHits(const FCMLStableId& SourceId) const;

    /** Forgets a source, for when the world removes it for any other reason. */
    void Forget(const FCMLStableId& SourceId);

    void Reset() { HitProgress.Reset(); }

private:
    TMap<FCMLStableId, int32> HitProgress;
};

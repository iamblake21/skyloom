#include "Simulation/CMLHarvestSession.h"

int32 FCMLHarvestSession::GetCompletedHits(const FCMLStableId& SourceId) const
{
    const int32* Found = HitProgress.Find(SourceId);
    return Found != nullptr ? *Found : 0;
}

void FCMLHarvestSession::Forget(const FCMLStableId& SourceId)
{
    HitProgress.Remove(SourceId);
}

FCMLHarvestOutcome FCMLHarvestSession::Strike(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const FCMLToolState& Tool,
    const FCMLStableId& SourceId,
    const ECMLMiningTarget Target,
    const int64 Capacity)
{
    FCMLHarvestOutcome Outcome;
    Outcome.UpdatedInventory = Inventory;
    Outcome.UpdatedTool = Tool;
    Outcome.CompletedHits = GetCompletedHits(SourceId);

    const FCMLMiningImpactResult Result = FCMLHarvestRules::Impact(
        Inventory, Catalog, Tool, Target, Outcome.CompletedHits, Capacity);
    Outcome.MiningStatus = Result.Status;

    switch (Result.Status)
    {
        case ECMLMiningImpactStatus::Progressed:
        case ECMLMiningImpactStatus::InventoryFull:
            // A blow that cannot be stored still counts: the swing happened and
            // the rock is that much more broken. Discarding the progress would
            // let a player mine forever without ever finishing.
            HitProgress.Add(SourceId, Result.NextHitProgress);
            Outcome.CompletedHits = Result.NextHitProgress;
            Outcome.UpdatedTool = Result.UpdatedTool;
            return Outcome;

        case ECMLMiningImpactStatus::Produced:
            // Cleared, so a fresh source of the same kind starts from zero
            // rather than inheriting this one's tally.
            HitProgress.Remove(SourceId);
            Outcome.CompletedHits = 0;
            Outcome.bProduced = true;
            Outcome.ProducedItemId = Result.ProducedItemId;
            Outcome.ProducedQuantity = 1;
            Outcome.UpdatedInventory = Result.UpdatedInventory;
            Outcome.UpdatedTool = Result.UpdatedTool;
            // Only now may the world remove the source: the new inventory has
            // been published, so nothing can go missing between the two.
            Outcome.bSourceExhausted = Result.bSourceExhausted;
            return Outcome;

        default:
            // Wrong tool, broken tool or an invalid target: nothing happened at
            // all, and the progress is left exactly as it was.
            return Outcome;
    }
}

FCMLHarvestOutcome FCMLHarvestSession::Gather(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const FCMLStableId& SourceId,
    const ECMLHandGatherTarget Target,
    const int32 Units,
    const int64 Capacity)
{
    FCMLHarvestOutcome Outcome;
    Outcome.UpdatedInventory = Inventory;

    const FCMLHandGatherResult Result =
        FCMLHarvestRules::Gather(Inventory, Catalog, Target, Units, Capacity);
    Outcome.GatherStatus = Result.Status;
    if (!Result.Gathered())
    {
        // A full backpack leaves the source standing, so no matter is produced
        // and none is lost.
        return Outcome;
    }

    Outcome.bProduced = true;
    Outcome.ProducedItemId = Result.ProducedItemId;
    Outcome.ProducedQuantity = Result.ProducedQuantity;
    Outcome.UpdatedInventory = Result.UpdatedInventory;
    // Gathering takes the whole source in one go; there is no partial tuft.
    Outcome.bSourceExhausted = true;
    HitProgress.Remove(SourceId);
    return Outcome;
}

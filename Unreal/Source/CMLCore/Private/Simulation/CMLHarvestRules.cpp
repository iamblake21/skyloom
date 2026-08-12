#include "Simulation/CMLHarvestRules.h"

#include "Content/CMLContentIds.h"

namespace
{
    FCMLStableId RewardForGather(const ECMLHandGatherTarget Target)
    {
        switch (Target)
        {
        case ECMLHandGatherTarget::WildFiberTuft:
            return FCMLHarvestRules::PlantFiber();
        // A stick, not a log: felling yields the log and needs a tool.
        case ECMLHandGatherTarget::FallenSticks:
            return FCMLHarvestRules::Stick();
        // The same Stone a pickaxe frees from a boulder: a picked-up pebble is
        // the same matter, not a separate currency.
        case ECMLHandGatherTarget::LoosePebble:
            return FCMLHarvestRules::Stone();
        default:
            return FCMLStableId::None();
        }
    }

    FCMLStableId RewardForMining(const ECMLMiningTarget Target)
    {
        switch (Target)
        {
        case ECMLMiningTarget::EnvironmentalStone:
            return FCMLHarvestRules::Stone();
        case ECMLMiningTarget::IronOreRock:
        case ECMLMiningTarget::IronDepositSurface:
            return FCMLHarvestRules::RawIron();
        case ECMLMiningTarget::CopperOreRock:
        case ECMLMiningTarget::CopperDepositSurface:
            return FCMLHarvestRules::RawCopper();
        case ECMLMiningTarget::TinOreRock:
        case ECMLMiningTarget::TinDepositSurface:
            return FCMLHarvestRules::RawTin();
        default:
            return FCMLStableId::None();
        }
    }

    bool IsInfiniteDepositSurface(const ECMLMiningTarget Target)
    {
        return Target == ECMLMiningTarget::IronDepositSurface
            || Target == ECMLMiningTarget::CopperDepositSurface
            || Target == ECMLMiningTarget::TinDepositSurface;
    }

    FCMLMiningImpactResult RefuseImpact(
        const ECMLMiningImpactStatus Status,
        const FCMLInventoryState& Inventory,
        const FCMLToolState& Tool,
        const int32 Progress)
    {
        FCMLMiningImpactResult Result;
        Result.Status = Status;
        Result.UpdatedInventory = Inventory;
        Result.UpdatedTool = Tool;
        Result.NextHitProgress = FMath::Max(0, Progress);
        return Result;
    }
}

const FCMLStableId& FCMLHarvestRules::PlantFiber()
{
    return CMLContentIds::PlantFiber;
}

const FCMLStableId& FCMLHarvestRules::Stick()
{
    return CMLContentIds::Stick;
}

const FCMLStableId& FCMLHarvestRules::Stone()
{
    return CMLContentIds::Stone;
}

const FCMLStableId& FCMLHarvestRules::RawIron()
{
    return CMLContentIds::RawIron;
}

const FCMLStableId& FCMLHarvestRules::RawCopper()
{
    return CMLContentIds::RawCopper;
}

const FCMLStableId& FCMLHarvestRules::RawTin()
{
    return CMLContentIds::RawTin;
}

const FCMLStableId& FCMLHarvestRules::CrudePickaxe()
{
    return CMLContentIds::CrudePickaxe;
}

const FCMLStableId& FCMLHarvestRules::IronPickaxe()
{
    return CMLContentIds::IronPickaxe;
}

int32 FCMLHarvestRules::RequiredHits(const FCMLStableId& ToolId)
{
    if (ToolId == CrudePickaxe())
    {
        return 4;
    }
    if (ToolId == IronPickaxe())
    {
        return 2;
    }
    return 0;
}

FCMLHandGatherResult FCMLHarvestRules::Gather(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const ECMLHandGatherTarget Target,
    const int32 Units,
    const int64 Capacity)
{
    FCMLHandGatherResult Result;
    Result.UpdatedInventory = Inventory;

    const FCMLStableId Reward = RewardForGather(Target);
    if (Reward.IsNone())
    {
        Result.Status = ECMLHandGatherStatus::InvalidTarget;
        return Result;
    }
    if (Units < 1)
    {
        Result.Status = ECMLHandGatherStatus::InvalidYield;
        return Result;
    }

    FCMLInventoryState Stored;
    ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
    if (!FCMLInventoryOperations::TryStoreEntire(
            Inventory, Catalog, Reward, Units, Capacity, Stored, Failure))
    {
        // The source is untouched: the player frees space and the next gather
        // starts from scratch rather than from a half-paid tuft.
        Result.Status = ECMLHandGatherStatus::InventoryFull;
        return Result;
    }

    Result.Status = ECMLHandGatherStatus::Gathered;
    Result.UpdatedInventory = MoveTemp(Stored);
    Result.ProducedItemId = Reward;
    Result.ProducedQuantity = Units;
    return Result;
}

FCMLMiningImpactResult FCMLHarvestRules::Impact(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    const FCMLToolState& Tool,
    const ECMLMiningTarget Target,
    const int32 CompletedHits,
    const int64 Capacity)
{
    if (CompletedHits < 0 || Tool.ItemId.IsNone())
    {
        return RefuseImpact(ECMLMiningImpactStatus::WrongTool, Inventory, Tool, CompletedHits);
    }

    const int32 Required = RequiredHits(Tool.ItemId);
    if (Required == 0 || Tool.Maximum <= 0)
    {
        // Not a mining tool at all. This is the check that stops a player
        // mining stone with their fists, so it must not be relaxed.
        return RefuseImpact(ECMLMiningImpactStatus::WrongTool, Inventory, Tool, CompletedHits);
    }
    if (Tool.IsBroken())
    {
        return RefuseImpact(ECMLMiningImpactStatus::BrokenTool, Inventory, Tool, CompletedHits);
    }

    const FCMLStableId Reward = RewardForMining(Target);
    if (Reward.IsNone())
    {
        return RefuseImpact(ECMLMiningImpactStatus::InvalidTarget, Inventory, Tool, CompletedHits);
    }

    const int32 NextProgress = FMath::Min(Required, CompletedHits + 1);
    if (NextProgress < Required)
    {
        FCMLMiningImpactResult Result = RefuseImpact(
            ECMLMiningImpactStatus::Progressed, Inventory, Tool, NextProgress);
        // A hit that only progresses costs no durability: the tool is spent on
        // what it produces, not on how long it took.
        return Result;
    }

    FCMLInventoryState Rewarded;
    ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
    if (!FCMLInventoryOperations::TryStoreEntire(
            Inventory, Catalog, Reward, 1, Capacity, Rewarded, Failure))
    {
        // Keep the source one impact from completion. Once the player frees
        // capacity, the next real impact retries the whole transaction.
        return RefuseImpact(
            ECMLMiningImpactStatus::InventoryFull, Inventory, Tool, Required - 1);
    }

    FCMLMiningImpactResult Result;
    Result.Status = ECMLMiningImpactStatus::Produced;
    Result.UpdatedInventory = MoveTemp(Rewarded);
    Result.UpdatedTool = Tool;
    Result.UpdatedTool.Current = FMath::Max(0, Tool.Current - 1);
    Result.ProducedItemId = Reward;
    Result.NextHitProgress = 0;
    // A deposit surface survives being mined; a loose rock does not.
    Result.bSourceExhausted = !IsInfiniteDepositSurface(Target);
    return Result;
}

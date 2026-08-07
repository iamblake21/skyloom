using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;

namespace CML.Simulation.Mining
{
    public enum ManualMiningTargetKind : byte
    {
        EnvironmentalStone = 1,
        IronOreRock = 2,
        IronDepositSurface = 3
    }

    public enum ManualMiningImpactStatus : byte
    {
        Progressed = 1,
        Produced = 2,
        InventoryFull = 3,
        WrongTool = 4,
        BrokenTool = 5,
        InvalidTarget = 6
    }

    /// <summary>
    /// Complete outcome of one physical pickaxe impact. World presentation
    /// consumes SourceExhausted only after UpdatedInventory has been published.
    /// </summary>
    public readonly struct ManualMiningImpactResult
    {
        internal ManualMiningImpactResult(
            ManualMiningImpactStatus status,
            InventoryState updatedInventory,
            StableId producedItemId,
            int nextHitProgress,
            bool sourceExhausted)
        {
            Status = status;
            UpdatedInventory = updatedInventory;
            ProducedItemId = producedItemId;
            NextHitProgress = nextHitProgress;
            SourceExhausted = sourceExhausted;
        }

        public ManualMiningImpactStatus Status { get; }

        public InventoryState UpdatedInventory { get; }

        public StableId ProducedItemId { get; }

        public int NextHitProgress { get; }

        public bool SourceExhausted { get; }

        public bool Produced =>
            Status == ManualMiningImpactStatus.Produced;
    }

    /// <summary>
    /// Pure authoritative rule for MINE-004. A completed extraction publishes
    /// one immutable inventory successor containing both the reward and the
    /// consumed durability point. Failure publishes neither half.
    /// </summary>
    public static class ManualMiningRule
    {
        private static readonly NonNegativeQuantity One =
            new NonNegativeQuantity(1);

        public static ManualMiningImpactResult ApplyImpact(
            InventoryState inventory,
            int equippedSlotIndex,
            ManualMiningTargetKind target,
            int completedHits)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (equippedSlotIndex < 0
                || equippedSlotIndex >= inventory.SlotCount
                || completedHits < 0)
            {
                return Refuse(
                    ManualMiningImpactStatus.WrongTool,
                    inventory,
                    completedHits);
            }

            var slot = inventory.GetSlot(equippedSlotIndex);
            if (!slot.Stack.HasValue)
            {
                return Refuse(
                    ManualMiningImpactStatus.WrongTool,
                    inventory,
                    completedHits);
            }

            var tool = slot.Stack.Value;
            var requiredHits = RequiredHits(tool.ItemId);
            if (requiredHits == 0 || !tool.Durability.HasValue)
            {
                return Refuse(
                    ManualMiningImpactStatus.WrongTool,
                    inventory,
                    completedHits);
            }

            if (tool.Durability.Value.IsBroken)
            {
                return Refuse(
                    ManualMiningImpactStatus.BrokenTool,
                    inventory,
                    completedHits);
            }

            var reward = RewardFor(target);
            if (reward.IsNone)
            {
                return Refuse(
                    ManualMiningImpactStatus.InvalidTarget,
                    inventory,
                    completedHits);
            }

            var nextProgress = Math.Min(
                requiredHits,
                completedHits + 1);
            if (nextProgress < requiredHits)
            {
                return new ManualMiningImpactResult(
                    ManualMiningImpactStatus.Progressed,
                    inventory,
                    StableId.None,
                    nextProgress,
                    sourceExhausted: false);
            }

            if (!inventory.TryStoreEntire(
                    reward,
                    One,
                    out var rewarded,
                    out _))
            {
                // Keep the source one impact from completion. Once the player
                // frees capacity, the next real impact retries the transaction.
                return new ManualMiningImpactResult(
                    ManualMiningImpactStatus.InventoryFull,
                    inventory,
                    StableId.None,
                    requiredHits - 1,
                    sourceExhausted: false);
            }

            if (!rewarded.TryConsumeDurabilityAtSlot(
                    equippedSlotIndex,
                    out var committed,
                    out var durabilityFailure))
            {
                throw new InventoryInvariantException(
                    "Mining preflight accepted a usable equipped tool but the " +
                    $"atomic successor could not consume durability: " +
                    durabilityFailure);
            }

            return new ManualMiningImpactResult(
                ManualMiningImpactStatus.Produced,
                committed,
                reward,
                nextHitProgress: 0,
                sourceExhausted:
                    target != ManualMiningTargetKind.IronDepositSurface);
        }

        private static ManualMiningImpactResult Refuse(
            ManualMiningImpactStatus status,
            InventoryState inventory,
            int progress) =>
            new ManualMiningImpactResult(
                status,
                inventory,
                StableId.None,
                Math.Max(0, progress),
                sourceExhausted: false);

        private static int RequiredHits(StableId toolId)
        {
            if (toolId == ContentIds.CrudePickaxe)
            {
                return 4;
            }

            if (toolId == ContentIds.IronPickaxe)
            {
                return 2;
            }

            return 0;
        }

        private static StableId RewardFor(ManualMiningTargetKind target)
        {
            switch (target)
            {
                case ManualMiningTargetKind.EnvironmentalStone:
                    return ContentIds.Stone;

                case ManualMiningTargetKind.IronOreRock:
                case ManualMiningTargetKind.IronDepositSurface:
                    return ContentIds.RawIron;

                default:
                    return StableId.None;
            }
        }
    }
}

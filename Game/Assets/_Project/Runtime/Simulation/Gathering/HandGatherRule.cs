using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;

namespace CML.Simulation.Gathering
{
    public enum HandGatherTargetKind : byte
    {
        WildFiberTuft = 1,
        FallenSticks = 2,
        LoosePebble = 3
    }

    public enum HandGatherStatus : byte
    {
        Gathered = 1,
        InventoryFull = 2,
        InvalidTarget = 3,
        InvalidYield = 4
    }

    /// <summary>
    /// Outcome of one completed hand gather. The world removes the source only
    /// after <see cref="UpdatedInventory"/> has been published.
    /// </summary>
    public readonly struct HandGatherResult
    {
        internal HandGatherResult(
            HandGatherStatus status,
            InventoryState updatedInventory,
            StableId producedItemId,
            long producedQuantity)
        {
            Status = status;
            UpdatedInventory = updatedInventory;
            ProducedItemId = producedItemId;
            ProducedQuantity = producedQuantity;
        }

        public HandGatherStatus Status { get; }

        public InventoryState UpdatedInventory { get; }

        public StableId ProducedItemId { get; }

        public long ProducedQuantity { get; }

        public bool Gathered => Status == HandGatherStatus.Gathered;
    }

    /// <summary>
    /// Authoritative rule for picking a resource with bare hands.
    ///
    /// Deliberately not part of <c>ManualMiningRule</c>. That rule is written
    /// around a tool: it reads the equipped slot, refuses with
    /// <c>WrongTool</c> when the hand is empty, counts hits against a
    /// tool-specific requirement and spends a durability point on success.
    /// Gathering has none of those, and teaching the mining rule to accept an
    /// empty hand would delete the very check that stops the player mining
    /// stone with their fists.
    ///
    /// The whole yield commits at once. A partial store would let a nearly
    /// full inventory swallow one fibre of two and still consume the tuft,
    /// which is the classic way matter goes missing.
    /// </summary>
    public static class HandGatherRule
    {
        public static HandGatherResult Gather(
            InventoryState inventory,
            HandGatherTargetKind target,
            int units)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            var reward = RewardFor(target);
            if (reward.IsNone)
            {
                return Refuse(HandGatherStatus.InvalidTarget, inventory);
            }

            if (units < 1)
            {
                return Refuse(HandGatherStatus.InvalidYield, inventory);
            }

            if (!inventory.TryStoreEntire(
                    reward,
                    new NonNegativeQuantity(units),
                    out var stored,
                    out _))
            {
                // The source is untouched: the player frees space and the next
                // gather starts from scratch rather than from a half-paid tuft.
                return Refuse(HandGatherStatus.InventoryFull, inventory);
            }

            return new HandGatherResult(
                HandGatherStatus.Gathered,
                stored,
                reward,
                units);
        }

        private static HandGatherResult Refuse(
            HandGatherStatus status,
            InventoryState inventory) =>
            new HandGatherResult(status, inventory, StableId.None, 0L);

        private static StableId RewardFor(HandGatherTargetKind target)
        {
            switch (target)
            {
                case HandGatherTargetKind.WildFiberTuft:
                    return ContentIds.PlantFiber;

                // Bastone, non Tronco: quello resta la resa dell'abbattimento
                // e richiede un utensile.
                case HandGatherTargetKind.FallenSticks:
                    return ContentIds.Stick;

                // Stessa Pietra che il Piccone estrae dai massi: un sassolino
                // raccolto è la stessa materia, non una valuta separata.
                case HandGatherTargetKind.LoosePebble:
                    return ContentIds.Stone;

                default:
                    return StableId.None;
            }
        }
    }
}

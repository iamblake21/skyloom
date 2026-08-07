using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation.Inventories;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// The authoritative rule that turns an owned component into an installed
    /// one. It lives next to the transfer rule, in phase 9, because installing
    /// moves matter out of an inventory and must be as atomic as any other
    /// movement: either the item leaves the container and the counter rises, or
    /// neither happens.
    /// </summary>
    public static class AirshipRepairRule
    {
        public static AirshipRepairInstallResult TryInstall(
            AirshipSimulationState airships,
            InventorySimulationState inventories,
            StableId airshipId,
            StableId sourceInventoryId,
            StableId itemId,
            NonNegativeQuantity amount)
        {
            if (airships == null)
            {
                throw new ArgumentNullException(nameof(airships));
            }

            if (inventories == null)
            {
                throw new ArgumentNullException(nameof(inventories));
            }

            if (amount.Value <= 0L)
            {
                return AirshipRepairInstallResult.InvalidAmount;
            }

            if (!airships.TryGetAirshipMutable(airshipId, out var airship))
            {
                return AirshipRepairInstallResult.UnknownAirship;
            }

            if (airship.RepairStatus != AirshipRepairStatus.Damaged)
            {
                return AirshipRepairInstallResult.AlreadyRepaired;
            }

            var required = AirshipRepairBill.RequiredCountFor(itemId);
            if (required <= 0)
            {
                return AirshipRepairInstallResult.NotPartOfBill;
            }

            var installed = InstalledCountFor(airship, itemId);
            var missing = required - installed;
            if (missing <= 0)
            {
                return AirshipRepairInstallResult.AlreadySatisfied;
            }

            // Installing never accepts more than the hull still needs: a panel
            // that swallowed the surplus would quietly destroy the player's
            // materials.
            if (amount.Value > missing)
            {
                return AirshipRepairInstallResult.AlreadySatisfied;
            }

            if (!inventories.TryGet(sourceInventoryId, out var inventory))
            {
                return AirshipRepairInstallResult.MissingFromInventory;
            }

            if (!inventory.TryTakeEntire(itemId, amount, out var updated, out _))
            {
                return AirshipRepairInstallResult.MissingFromInventory;
            }

            // Past this point nothing can refuse: the successor inventory is
            // published and the counter rises in the same commit.
            inventories.Replace(updated);
            SetInstalledCount(airship, itemId, installed + (int)amount.Value);

            if (airship.IsBillSatisfied)
            {
                airship.RepairStatus = AirshipRepairStatus.Repairing;
                airship.RepairTicksRemaining = AirshipRepairBill.RepairDurationTicks;
            }

            return AirshipRepairInstallResult.Installed;
        }

        private static int InstalledCountFor(
            AirshipEntityState airship,
            StableId itemId)
        {
            return itemId == ContentIds.IronPlate
                ? airship.InstalledIronPlates
                : airship.InstalledInsulatedCables;
        }

        private static void SetInstalledCount(
            AirshipEntityState airship,
            StableId itemId,
            int value)
        {
            if (itemId == ContentIds.IronPlate)
            {
                airship.InstalledIronPlates = value;
                return;
            }

            airship.InstalledInsulatedCables = value;
        }
    }

    /// <summary>
    /// Reads the installation commands due this tick and commits them in phase
    /// 9, after transfers (100) and slot moves (200), so an item that arrived in
    /// the inventory this same tick can already pay for a component.
    /// </summary>
    internal sealed class AirshipRepairCommitPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.ValidatedTransferCommit;

        public int Order => 300;

        public StableId StableOrderId =>
            new StableId(0x4149525F52505253UL, 0x0000000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            var commands = context.GetCommandsForExecutingTick();
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (!string.Equals(
                        command.Kind,
                        AirshipCommandKinds.RepairInstall,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!AirshipCommandCodec.TryDecodeRepairInstall(
                        command,
                        out var sourceInventoryId,
                        out var itemId))
                {
                    context.RecordCommandRejection(
                        command,
                        CommandRejectionReason.RepairInstallMalformed);
                    continue;
                }

                var result = AirshipRepairRule.TryInstall(
                    context.GetAirshipMutable(),
                    context.GetInventoriesMutable(),
                    command.DestinationId,
                    sourceInventoryId,
                    itemId,
                    new NonNegativeQuantity(command.QuantizedValue));

                if (result == AirshipRepairInstallResult.Installed)
                {
                    continue;
                }

                context.RecordCommandRejection(command, ToRejectionReason(result));
            }
        }

        private static CommandRejectionReason ToRejectionReason(
            AirshipRepairInstallResult result)
        {
            switch (result)
            {
                case AirshipRepairInstallResult.UnknownAirship:
                    return CommandRejectionReason.RepairAirshipMissing;
                case AirshipRepairInstallResult.AlreadyRepaired:
                    return CommandRejectionReason.RepairNotDamaged;
                case AirshipRepairInstallResult.NotPartOfBill:
                    return CommandRejectionReason.RepairComponentNotInBill;
                case AirshipRepairInstallResult.AlreadySatisfied:
                    return CommandRejectionReason.RepairComponentAlreadySatisfied;
                case AirshipRepairInstallResult.MissingFromInventory:
                    return CommandRejectionReason.RepairComponentMissing;
                case AirshipRepairInstallResult.InvalidAmount:
                    return CommandRejectionReason.RepairInstallMalformed;
                default:
                    return CommandRejectionReason.RepairInstallMalformed;
            }
        }
    }
}

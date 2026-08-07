using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation.Inventories;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Persistent spatial elements BUILD-001 can create. Funnel and BeltModule
    /// carry no authored endpoint: adjacency is derived from their quantized pose.
    /// </summary>
    public enum MachineBuildKind : byte
    {
        Buffer = 1,
        Machine = 2,
        Funnel = 3,
        BeltModule = 4,
    }

    /// <summary>
    /// Quantized pose submitted by the hologram. The machine graph currently needs
    /// topology rather than collision geometry, but the pose travels with the command
    /// so replay/presentation can reconstruct exactly what the preview showed.
    /// </summary>
    public readonly struct MachineBuildPose : IEquatable<MachineBuildPose>
    {
        public MachineBuildPose(
            int xMillimetres,
            int yMillimetres,
            int zMillimetres,
            byte yawQuarterTurns)
        {
            if (yawQuarterTurns > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(yawQuarterTurns));
            }

            XMillimetres = xMillimetres;
            YMillimetres = yMillimetres;
            ZMillimetres = zMillimetres;
            YawQuarterTurns = yawQuarterTurns;
        }

        public int XMillimetres { get; }

        public int YMillimetres { get; }

        public int ZMillimetres { get; }

        public byte YawQuarterTurns { get; }

        public bool Equals(MachineBuildPose other)
        {
            return XMillimetres == other.XMillimetres
                && YMillimetres == other.YMillimetres
                && ZMillimetres == other.ZMillimetres
                && YawQuarterTurns == other.YawQuarterTurns;
        }

        public override bool Equals(object obj) =>
            obj is MachineBuildPose other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = XMillimetres;
                hash = (hash * 397) ^ YMillimetres;
                hash = (hash * 397) ^ ZMillimetres;
                return (hash * 397) ^ YawQuarterTurns;
            }
        }
    }

    /// <summary>
    /// Decoded BUILD-001 request.
    ///
    /// The ids deliberately change meaning by kind:
    /// Buffer: definition.
    /// Machine: definition, recipe.
    /// Funnel/BeltModule: the placeable item definition only.
    /// </summary>
    public readonly struct MachineBuildSpecification
    {
        private MachineBuildSpecification(
            MachineBuildKind kind,
            StableId primaryId,
            StableId secondaryId,
            StableId tertiaryId,
            StableId costItemId,
            long costQuantity,
            int lengthMillimetres,
            int speedMillimetresPerTick,
            int spacingMillimetres,
            MachineBuildPose pose,
            BeltTravelDirection beltTravelDirection)
        {
            Kind = kind;
            PrimaryId = primaryId;
            SecondaryId = secondaryId;
            TertiaryId = tertiaryId;
            CostItemId = costItemId;
            CostQuantity = costQuantity;
            LengthMillimetres = lengthMillimetres;
            SpeedMillimetresPerTick = speedMillimetresPerTick;
            SpacingMillimetres = spacingMillimetres;
            Pose = pose;
            BeltTravelDirection = beltTravelDirection;
        }

        public MachineBuildKind Kind { get; }

        public StableId PrimaryId { get; }

        public StableId SecondaryId { get; }

        public StableId TertiaryId { get; }

        public StableId CostItemId { get; }

        public long CostQuantity { get; }

        public int LengthMillimetres { get; }

        public int SpeedMillimetresPerTick { get; }

        public int SpacingMillimetres { get; }

        public MachineBuildPose Pose { get; }

        public BeltTravelDirection BeltTravelDirection { get; }

        public static MachineBuildSpecification Buffer(
            StableId containerDefinitionId,
            StableId placeableItemId,
            long costQuantity,
            MachineBuildPose pose) =>
            new MachineBuildSpecification(
                MachineBuildKind.Buffer,
                containerDefinitionId,
                StableId.None,
                StableId.None,
                placeableItemId,
                costQuantity,
                0,
                0,
                0,
                pose,
                BeltTravelDirection.Stopped);

        public static MachineBuildSpecification Machine(
            StableId machineDefinitionId,
            StableId recipeId,
            StableId placeableItemId,
            long costQuantity,
            MachineBuildPose pose) =>
            new MachineBuildSpecification(
                MachineBuildKind.Machine,
                machineDefinitionId,
                recipeId,
                StableId.None,
                placeableItemId,
                costQuantity,
                0,
                0,
                0,
                pose,
                BeltTravelDirection.Stopped);

        public static MachineBuildSpecification Funnel(
            StableId placeableItemId,
            MachineBuildPose pose) =>
            new MachineBuildSpecification(
                MachineBuildKind.Funnel,
                placeableItemId,
                StableId.None,
                StableId.None,
                placeableItemId,
                1L,
                0,
                0,
                0,
                pose,
                BeltTravelDirection.Stopped);

        public static MachineBuildSpecification BeltModule(
            StableId placeableItemId,
            MachineBuildPose pose,
            BeltTravelDirection beltTravelDirection = BeltTravelDirection.Stopped) =>
            new MachineBuildSpecification(
                MachineBuildKind.BeltModule,
                placeableItemId,
                StableId.None,
                StableId.None,
                placeableItemId,
                1L,
                0,
                0,
                0,
                pose,
                beltTravelDirection);

    }

    /// <summary>
    /// Fixed-width, big-endian payload for BUILD-001. A fixed layout keeps ingress
    /// validation independent from platform endianness and from Unity serialization.
    /// </summary>
    public static class MachineBuildCommandPayload
    {
        public const int Length = 100;
        public const byte Version = 5;
        public const uint CreationCauseCode = 0x4D434842U; // "MCHB"

        public static byte[] Encode(MachineBuildSpecification specification)
        {
            var payload = new byte[Length];
            payload[0] = Version;
            payload[1] = (byte)specification.Kind;
            payload[2] = specification.Pose.YawQuarterTurns;
            payload[3] = (byte)specification.BeltTravelDirection;
            WriteStableId(payload, 4, specification.PrimaryId);
            WriteStableId(payload, 20, specification.SecondaryId);
            WriteStableId(payload, 36, specification.TertiaryId);
            WriteInt32(payload, 52, specification.LengthMillimetres);
            WriteInt32(payload, 56, specification.SpeedMillimetresPerTick);
            WriteInt32(payload, 60, specification.SpacingMillimetres);
            WriteInt32(payload, 64, specification.Pose.XMillimetres);
            WriteInt32(payload, 68, specification.Pose.YMillimetres);
            WriteInt32(payload, 72, specification.Pose.ZMillimetres);
            WriteStableId(payload, 76, specification.CostItemId);
            WriteInt64(payload, 92, specification.CostQuantity);
            return payload;
        }

        public static bool TryDecode(
            SimulationCommand command,
            out MachineBuildSpecification specification)
        {
            specification = default;
            var payload = command.Payload;
            if (payload == null
                || payload.Count != Length
                || payload[0] != Version
                || payload[2] > 3
                || payload[3] > (byte)BeltTravelDirection.Reverse)
            {
                return false;
            }

            var kind = (MachineBuildKind)payload[1];
            if (kind < MachineBuildKind.Buffer || kind > MachineBuildKind.BeltModule)
            {
                return false;
            }

            var primary = ReadStableId(payload, 4);
            var secondary = ReadStableId(payload, 20);
            var tertiary = ReadStableId(payload, 36);
            var length = ReadInt32(payload, 52);
            var speed = ReadInt32(payload, 56);
            var spacing = ReadInt32(payload, 60);
            var pose = new MachineBuildPose(
                ReadInt32(payload, 64),
                ReadInt32(payload, 68),
                ReadInt32(payload, 72),
                payload[2]);
            var costItem = ReadStableId(payload, 76);
            var costQuantity = ReadInt64(payload, 92);
            var beltTravelDirection = (BeltTravelDirection)payload[3];

            if (costItem.IsNone || costQuantity <= 0L)
            {
                return false;
            }

            switch (kind)
            {
                case MachineBuildKind.Buffer:
                    specification = MachineBuildSpecification.Buffer(
                        primary,
                        costItem,
                        costQuantity,
                        pose);
                    return secondary.IsNone
                        && tertiary.IsNone
                        && beltTravelDirection == BeltTravelDirection.Stopped
                        && length == 0
                        && speed == 0
                        && spacing == 0;

                case MachineBuildKind.Machine:
                    specification = MachineBuildSpecification.Machine(
                        primary,
                        secondary,
                        costItem,
                        costQuantity,
                        pose);
                    return tertiary.IsNone
                        && beltTravelDirection == BeltTravelDirection.Stopped
                        && length == 0
                        && speed == 0
                        && spacing == 0;

                case MachineBuildKind.Funnel:
                    specification = MachineBuildSpecification.Funnel(
                        primary,
                        pose);
                    return secondary.IsNone
                        && tertiary.IsNone
                        && beltTravelDirection == BeltTravelDirection.Stopped
                        && costItem == primary
                        && costQuantity == 1L
                        && length == 0
                        && speed == 0
                        && spacing == 0;

                case MachineBuildKind.BeltModule:
                    specification = MachineBuildSpecification.BeltModule(
                        primary,
                        pose,
                        beltTravelDirection);
                    return secondary.IsNone
                        && tertiary.IsNone
                        && beltTravelDirection == BeltTravelDirection.Stopped
                        && costItem == primary
                        && costQuantity == 1L
                        && length == 0
                        && speed == 0
                        && spacing == 0;

                default:
                    return false;
            }
        }

        public static CreationKey CreationKey(SimulationCommand command)
        {
            return new CreationKey(
                SimulationPhase.CommandsAndConfiguration,
                CreationCauseCode,
                command.InitiatorId,
                command.Sequence,
                0U,
                0U);
        }

        private static void WriteStableId(byte[] buffer, int offset, StableId id)
        {
            WriteUInt64(buffer, offset, id.High);
            WriteUInt64(buffer, offset + 8, id.Low);
        }

        private static StableId ReadStableId(IReadOnlyList<byte> buffer, int offset) =>
            new StableId(ReadUInt64(buffer, offset), ReadUInt64(buffer, offset + 8));

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            for (var index = 0; index < 8; index++)
            {
                buffer[offset + index] = (byte)(value >> ((7 - index) * 8));
            }
        }

        private static ulong ReadUInt64(IReadOnlyList<byte> buffer, int offset)
        {
            var value = 0UL;
            for (var index = 0; index < 8; index++)
            {
                value = (value << 8) | buffer[offset + index];
            }

            return value;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            var unsigned = unchecked((uint)value);
            for (var index = 0; index < 4; index++)
            {
                buffer[offset + index] = (byte)(unsigned >> ((3 - index) * 8));
            }
        }

        private static int ReadInt32(IReadOnlyList<byte> buffer, int offset)
        {
            var value = 0U;
            for (var index = 0; index < 4; index++)
            {
                value = (value << 8) | buffer[offset + index];
            }

            return unchecked((int)value);
        }

        private static void WriteInt64(byte[] buffer, int offset, long value)
        {
            var unsigned = unchecked((ulong)value);
            for (var index = 0; index < 8; index++)
            {
                buffer[offset + index] =
                    (byte)(unsigned >> ((7 - index) * 8));
            }
        }

        private static long ReadInt64(IReadOnlyList<byte> buffer, int offset)
        {
            var value = 0UL;
            for (var index = 0; index < 8; index++)
            {
                value = (value << 8) | buffer[offset + index];
            }

            return unchecked((long)value);
        }
    }

    /// <summary>
    /// Shared preflight and application rule for the hologram and phase 3. Invalid
    /// preview placements never enter the command queue; live/replayed malformed
    /// commands are still rejected authoritatively.
    /// </summary>
    public static class MachineBuildRule
    {
        public const int BeltSegmentLengthMillimetres = 1_000;

        public static bool TryPreflightTopology(
            MachineSimulationState state,
            GameCatalog catalog,
            MachineBuildSpecification specification,
            out CommandRejectionReason rejection)
        {
            rejection = default;
            if (state == null || catalog == null)
            {
                rejection = CommandRejectionReason.BuildDefinitionMissing;
                return false;
            }

            if (!IsCorrectBuildCost(specification)
                || specification.CostItemId.IsNone
                || specification.CostQuantity <= 0L
                || !catalog.TryGetItem(specification.CostItemId, out _))
            {
                rejection = CommandRejectionReason.BuildDefinitionMissing;
                return false;
            }

            if (specification.Kind != MachineBuildKind.BeltModule
                && specification.BeltTravelDirection != BeltTravelDirection.Stopped)
            {
                rejection = CommandRejectionReason.BuildMalformed;
                return false;
            }

            if (!IsPlacementCellFree(state, specification.Pose))
            {
                rejection = CommandRejectionReason.BuildTopologyInvalid;
                return false;
            }

            switch (specification.Kind)
            {
                case MachineBuildKind.Buffer:
                    if (specification.PrimaryId.IsNone
                        || !catalog.TryGetContainer(specification.PrimaryId, out _))
                    {
                        rejection = CommandRejectionReason.BuildDefinitionMissing;
                        return false;
                    }

                    return true;

                case MachineBuildKind.Machine:
                    if (specification.PrimaryId.IsNone
                        || !catalog.TryGetMachine(specification.PrimaryId, out var machine))
                    {
                        rejection = CommandRejectionReason.BuildDefinitionMissing;
                        return false;
                    }

                    if (!specification.SecondaryId.IsNone
                        && !Supports(machine, specification.SecondaryId))
                    {
                        rejection = CommandRejectionReason.BuildTopologyInvalid;
                        return false;
                    }

                    // An extractor draws from the deposit it stands on, so the
                    // deposit is what chooses its recipe. Without one there is
                    // nothing under the machine and the build must be refused —
                    // not accepted and left sitting on NoRecipe forever, which
                    // is what would happen if this rule lived only in the UI.
                    if (specification.SecondaryId.IsNone
                        && ExtractsOnly(machine, catalog))
                    {
                        rejection = CommandRejectionReason.BuildTopologyInvalid;
                        return false;
                    }

                    return true;

                case MachineBuildKind.Funnel:
                    if (specification.PrimaryId.IsNone
                        || !catalog.TryGetItem(specification.PrimaryId, out _))
                    {
                        rejection = CommandRejectionReason.BuildDefinitionMissing;
                        return false;
                    }

                    return true;

                case MachineBuildKind.BeltModule:
                    if (specification.PrimaryId.IsNone
                        || specification.BeltTravelDirection
                            != BeltTravelDirection.Stopped
                        || !catalog.TryGetItem(specification.PrimaryId, out _))
                    {
                        rejection = CommandRejectionReason.BuildDefinitionMissing;
                        return false;
                    }

                    return true;

                default:
                    rejection = CommandRejectionReason.BuildMalformed;
                    return false;
            }
        }

        public static bool TryPreflight(
            MachineSimulationState state,
            InventorySimulationState inventories,
            GameCatalog catalog,
            StableId sourceInventoryId,
            MachineBuildSpecification specification,
            out CommandRejectionReason rejection)
        {
            if (!TryPreflightTopology(
                    state,
                    catalog,
                    specification,
                    out rejection))
            {
                return false;
            }

            if (inventories == null
                || sourceInventoryId.IsNone
                || !inventories.TryGet(sourceInventoryId, out var inventory))
            {
                rejection = CommandRejectionReason.BuildSourceMissing;
                return false;
            }

            if (inventory.Count(specification.CostItemId).Value
                < specification.CostQuantity)
            {
                rejection = CommandRejectionReason.InsufficientQuantity;
                return false;
            }

            return true;
        }

        internal static void ConsumeCost(
            InventorySimulationState inventories,
            StableId sourceInventoryId,
            MachineBuildSpecification specification)
        {
            if (!inventories.TryGet(sourceInventoryId, out var inventory)
                || !inventory.TryTakeEntire(
                    specification.CostItemId,
                    new NonNegativeQuantity(specification.CostQuantity),
                    out var updated,
                    out _))
            {
                throw new SimulationInvariantException(
                    "A build cost passed preflight but could not be consumed.");
            }

            inventories.Replace(updated);
        }

        internal static void Apply(
            MachineSimulationState state,
            GameCatalog catalog,
            StableId createdId,
            MachineBuildSpecification specification)
        {
            if (!TryPreflightTopology(
                    state,
                    catalog,
                    specification,
                    out var rejection))
            {
                throw new SimulationInvariantException(
                    $"A build passed phase-1 preflight and failed in phase 3: {rejection}.");
            }

            switch (specification.Kind)
            {
                case MachineBuildKind.Buffer:
                    catalog.TryGetContainer(specification.PrimaryId, out var container);
                    state.AddNode(MachineNodeState.CreateBuffer(
                        createdId,
                        specification.PrimaryId,
                        container.SlotCount,
                        specification.Pose));
                    break;

                case MachineBuildKind.Machine:
                    catalog.TryGetMachine(specification.PrimaryId, out var machine);
                    var node = MachineNodeState.CreateMachine(
                        createdId,
                        specification.PrimaryId,
                        machine.InputSlots,
                        machine.OutputSlots,
                        specification.Pose,
                        machine.FuelSlots);
                    if (!specification.SecondaryId.IsNone)
                    {
                        node.ActiveRecipeId = specification.SecondaryId;
                        node.Activity = MachineActivity.MissingInput;
                    }

                    state.AddNode(node);
                    break;

                case MachineBuildKind.Funnel:
                    state.AddNode(MachineNodeState.CreateFunnel(
                        createdId,
                        specification.PrimaryId,
                        specification.Pose));
                    break;

                case MachineBuildKind.BeltModule:
                    state.AddNode(MachineNodeState.CreateBeltModule(
                        createdId,
                        specification.PrimaryId,
                        specification.Pose));
                    break;

                default:
                    throw new SimulationInvariantException(
                        $"Unsupported machine build kind {specification.Kind}.");
            }

            state.ValidateInvariants(catalog);
        }

        private static bool Supports(MachineDefinition machine, StableId recipeId)
        {
            for (var index = 0; index < machine.SupportedRecipeIds.Count; index++)
            {
                if (machine.SupportedRecipeIds[index] == recipeId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when every recipe the machine supports is extraction, which is
        /// what makes it an extractor rather than a transformer. A machine with
        /// no recipes at all is simply unconfigured and does not qualify.
        /// </summary>
        private static bool ExtractsOnly(MachineDefinition machine, GameCatalog catalog)
        {
            if (machine.SupportedRecipeIds == null
                || machine.SupportedRecipeIds.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < machine.SupportedRecipeIds.Count; index++)
            {
                if (!catalog.TryGetRecipe(machine.SupportedRecipeIds[index], out var recipe)
                    || recipe.Category != RecipeCategory.Extraction)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPlacementCellFree(
            MachineSimulationState state,
            MachineBuildPose pose)
        {
            foreach (var pair in state.Nodes)
            {
                var node = pair.Value;
                if (node.HasPlacementPose
                    && node.PlacementPose.XMillimetres == pose.XMillimetres
                    && node.PlacementPose.YMillimetres == pose.YMillimetres
                    && node.PlacementPose.ZMillimetres == pose.ZMillimetres)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCorrectBuildCost(
            MachineBuildSpecification specification)
        {
            switch (specification.Kind)
            {
                case MachineBuildKind.Buffer:
                    return specification.PrimaryId == ContentIds.WoodenCrate
                        && specification.CostItemId == ContentIds.WoodenCrateItem
                        && specification.CostQuantity == 1L;
                case MachineBuildKind.Machine:
                    return specification.CostQuantity == 1L
                        && ((specification.PrimaryId == ContentIds.MechanicalPress
                                && specification.CostItemId == ContentIds.MechanicalPressItem)
                            || (specification.PrimaryId == ContentIds.CrudeFurnace
                                && specification.CostItemId == ContentIds.CrudeFurnaceItem)
                            || (specification.PrimaryId == ContentIds.MechanicalDrill
                                && specification.CostItemId == ContentIds.MechanicalDrillItem));
                case MachineBuildKind.Funnel:
                    return specification.PrimaryId == ContentIds.BeltFunnel
                        && specification.CostItemId == ContentIds.BeltFunnel
                        && specification.CostQuantity == 1L;
                case MachineBuildKind.BeltModule:
                    // Ogni modulo nastro costa un esemplare di se stesso. La
                    // Curva è ammessa come le altre: l'adiacenza si deriva dalla
                    // posa, quindi la svolta non cambia nulla per la topologia.
                    return specification.CostQuantity == 1L
                        && ((specification.PrimaryId == ContentIds.BeltStraight
                                && specification.CostItemId == ContentIds.BeltStraight)
                            || (specification.PrimaryId == ContentIds.BeltDriveUnit
                                && specification.CostItemId == ContentIds.BeltDriveUnit)
                            || (specification.PrimaryId == ContentIds.BeltCurve
                                && specification.CostItemId == ContentIds.BeltCurve)
                            || (specification.PrimaryId == ContentIds.BeltIncline
                                && specification.CostItemId == ContentIds.BeltIncline)
                            || (specification.PrimaryId == ContentIds.BeltCurveLeft
                                && specification.CostItemId == ContentIds.BeltCurveLeft));
                default:
                    return false;
            }
        }
    }

    /// <summary>Applies valid BUILD-001 commands after IDs are allocated in phase 1.</summary>
    internal sealed class MachineBuildTopologyPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.LocalTopologyChanges;

        public int Order => 100;

        public StableId StableOrderId =>
            new StableId(0x4D43485F4255494CUL, 0x4400000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            var commands = context.GetCommandsForExecutingTick();
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (!string.Equals(
                        command.Kind,
                        SimulationCommandKinds.BuildMachineGraphElement,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var creationKey = MachineBuildCommandPayload.CreationKey(command);
                if (!context.TryGetCreatedEntityId(creationKey, out var createdId))
                {
                    // Phase 1 rejected this command and deliberately staged no ID.
                    continue;
                }

                if (!MachineBuildCommandPayload.TryDecode(command, out var specification))
                {
                    throw new SimulationInvariantException(
                        "A machine build command passed ingress but became malformed.");
                }

                MachineBuildRule.Apply(
                    context.GetMachineMutable(),
                    context.Catalog,
                    createdId,
                    specification);
            }
        }
    }
}

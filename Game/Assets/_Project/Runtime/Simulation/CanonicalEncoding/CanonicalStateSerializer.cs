using System;
using System.Collections.Generic;
using CML.Foundation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;

namespace CML.Simulation.CanonicalEncoding
{
    /// <summary>
    /// Canonical projection for the authoritative state currently implemented in M0.
    /// New future-affecting fields require a logical schema revision and a new tag.
    /// </summary>
    public static class CanonicalStateSerializer
    {
        public static byte[] Serialize(SimulationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(16UL);

                writer.WriteTag(1UL);
                writer.WriteUnsigned(state.LogicalSchemaRevision);

                writer.WriteTag(2UL);
                writer.WriteUnsigned(state.RulesRevision);

                writer.WriteTag(3UL);
                writer.WriteUnsigned(CML.Content.CatalogSchema.CurrentVersion);

                writer.WriteTag(4UL);
                writer.WriteString(state.ContentRevision.Value);

                writer.WriteTag(5UL);
                writer.WriteUnsigned(state.GeneratorRevision);

                writer.WriteTag(6UL);
                writer.WriteUnsigned(state.Tick.Value);

                writer.WriteTag(7UL);
                writer.WriteBytes(SerializeStableId(state.NextEntityId));

                writer.WriteTag(8UL);
                writer.WriteBoolean(state.IsEntityIdSpaceExhausted);

                writer.WriteTag(9UL);
                writer.WriteBytes(SerializeQuantities(state.GetQuantitiesCanonical()));

                writer.WriteTag(10UL);
                writer.WriteBytes(SerializeAccumulators(state.GetAccumulatorsCanonical()));

                writer.WriteTag(11UL);
                writer.WriteBytes(SerializeCommands(state.GetAcceptedCommandsCanonical()));

                writer.WriteTag(12UL);
                writer.WriteBytes(SerializeCreations(state.GetCreationRecordsCanonical()));

                writer.WriteTag(13UL);
                writer.WriteBytes(SerializeCommandRejections(state.GetCommandRejectionsCanonical()));

                writer.WriteTag(14UL);
                writer.WriteBytes(AirshipCanonicalSerializer.Serialize(state.GetAirshipSnapshot()));

                writer.WriteTag(15UL);
                writer.WriteBytes(MachineCanonicalSerializer.Serialize(state.GetMachineSnapshot()));

                writer.WriteTag(16UL);
                writer.WriteBytes(
                    InventoryCanonicalSerializer.Serialize(state.GetInventorySnapshot()));

                return writer.ToArray();
            }
        }

        private static byte[] SerializeStableId(StableId id)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(2UL);
                writer.WriteTag(1UL);
                writer.WriteUnsigned(id.High);
                writer.WriteTag(2UL);
                writer.WriteUnsigned(id.Low);
                return writer.ToArray();
            }
        }

        private static byte[] SerializeQuantities(
            IReadOnlyList<KeyValuePair<StableId, NonNegativeQuantity>> entries)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteUnsigned((ulong)entries.Count);
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    using (var element = new CanonicalWriter())
                    {
                        element.WriteFieldCount(2UL);
                        element.WriteTag(1UL);
                        element.WriteBytes(SerializeStableId(entry.Key));
                        element.WriteTag(2UL);
                        element.WriteUnsigned((ulong)entry.Value.Value);
                        writer.WriteBytes(element.ToArray());
                    }
                }

                return writer.ToArray();
            }
        }

        private static byte[] SerializeAccumulators(
            IReadOnlyList<KeyValuePair<AccumulatorKey, RemainderAccumulator>> entries)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteUnsigned((ulong)entries.Count);
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    using (var element = new CanonicalWriter())
                    {
                        element.WriteFieldCount(4UL);
                        element.WriteTag(1UL);
                        element.WriteBytes(SerializeAccumulatorKey(entry.Key));
                        element.WriteTag(2UL);
                        element.WriteUnsigned(entry.Value.OwnerDenominator);
                        element.WriteTag(3UL);
                        element.WriteUnsigned(entry.Value.Remainder);
                        element.WriteTag(4UL);
                        element.WriteUnsigned(entry.Value.RuleRevision);
                        writer.WriteBytes(element.ToArray());
                    }
                }

                return writer.ToArray();
            }
        }

        private static byte[] SerializeAccumulatorKey(AccumulatorKey key)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(4UL);
                writer.WriteTag(1UL);
                writer.WriteString(key.SystemKind);
                writer.WriteTag(2UL);
                writer.WriteString(key.ResourceKind);
                writer.WriteTag(3UL);
                writer.WriteBytes(SerializeStableId(key.EntityId));
                writer.WriteTag(4UL);
                writer.WriteUnsigned(key.PortOrCycleIndex);
                return writer.ToArray();
            }
        }

        private static byte[] SerializeCommands(IReadOnlyList<SimulationCommand> commands)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteUnsigned((ulong)commands.Count);
                for (var index = 0; index < commands.Count; index++)
                {
                    writer.WriteBytes(SerializeCommand(commands[index]));
                }

                return writer.ToArray();
            }
        }

        private static byte[] SerializeCommand(SimulationCommand command)
        {
            using (var element = new CanonicalWriter())
            {
                element.WriteFieldCount(7UL);
                element.WriteTag(1UL);
                element.WriteUnsigned(command.TargetTick.Value);
                element.WriteTag(2UL);
                element.WriteUnsigned(command.Sequence);
                element.WriteTag(3UL);
                element.WriteString(command.Kind);
                element.WriteTag(4UL);
                element.WriteBytes(SerializeStableId(command.InitiatorId));
                element.WriteTag(5UL);
                element.WriteBytes(SerializeStableId(command.DestinationId));
                element.WriteTag(6UL);
                element.WriteSigned(command.QuantizedValue);
                element.WriteTag(7UL);
                element.WriteBytes(command.Payload);
                return element.ToArray();
            }
        }

        private static byte[] SerializeCreations(IReadOnlyList<CreationRecord> records)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteUnsigned((ulong)records.Count);
                for (var index = 0; index < records.Count; index++)
                {
                    var record = records[index];
                    using (var element = new CanonicalWriter())
                    {
                        element.WriteFieldCount(3UL);
                        element.WriteTag(1UL);
                        element.WriteUnsigned(record.Tick.Value);
                        element.WriteTag(2UL);
                        element.WriteBytes(SerializeCreationKey(record.Key));
                        element.WriteTag(3UL);
                        element.WriteBytes(SerializeStableId(record.EntityId));
                        writer.WriteBytes(element.ToArray());
                    }
                }

                return writer.ToArray();
            }
        }

        private static byte[] SerializeCreationKey(CreationKey key)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(6UL);
                writer.WriteTag(1UL);
                writer.WriteUnsigned((byte)key.Phase);
                writer.WriteTag(2UL);
                writer.WriteUnsigned(key.CauseCode);
                writer.WriteTag(3UL);
                writer.WriteBytes(SerializeStableId(key.InitiatorId));
                writer.WriteTag(4UL);
                writer.WriteUnsigned(key.CommandSequence);
                writer.WriteTag(5UL);
                writer.WriteUnsigned(key.OutputIndex);
                writer.WriteTag(6UL);
                writer.WriteUnsigned(key.LocalOrdinal);
                return writer.ToArray();
            }
        }

        private static byte[] SerializeCommandRejections(
            IReadOnlyList<CommandRejection> rejections)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteUnsigned((ulong)rejections.Count);
                for (var index = 0; index < rejections.Count; index++)
                {
                    var rejection = rejections[index];
                    using (var element = new CanonicalWriter())
                    {
                        element.WriteFieldCount(3UL);
                        element.WriteTag(1UL);
                        element.WriteUnsigned(rejection.Tick.Value);
                        element.WriteTag(2UL);
                        element.WriteBytes(SerializeCommand(rejection.Command));
                        element.WriteTag(3UL);
                        element.WriteUnsigned((byte)rejection.Reason);
                        writer.WriteBytes(element.ToArray());
                    }
                }

                return writer.ToArray();
            }
        }
    }
}

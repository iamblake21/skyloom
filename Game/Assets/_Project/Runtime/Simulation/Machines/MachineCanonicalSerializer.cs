using System;
using CML.Foundation;
using CML.Simulation.CanonicalEncoding;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Canonical MCH subtree. Every collection and composite element is
    /// length-prefixed, matching the root logical encoding contract.
    ///
    /// A node encodes its ports as a collection rather than as two fixed fields,
    /// because a buffer's input and output are one port: encoding it twice would put
    /// a crate's contents into the hash twice and make the encoding disagree with the
    /// state it describes.
    ///
    /// Empty slots are encoded, not skipped. Slot positions decide the order in which
    /// a port fills and drains, so two ports holding the same items in different slots
    /// are not the same future and must not share a hash.
    /// </summary>
    public static class MachineCanonicalSerializer
    {
        /// <summary>
        /// Revision 10 adds the optional fuel port used by combustion machines.
        /// Revision 9 persists each spatial node's quantized pose, a belt module's
        /// workpiece progress, and the status, direction and capacity derived from its
        /// Belt Drive line. Adjacency is deliberately absent: it is derived from those poses
        /// every tick, so moving one physical module cannot leave a stale logical
        /// connection in the save or hash. Tag 3 retains only transitional unposed
        /// bootstrap lanes; new gameplay creates spatial BeltModule nodes.
        /// </summary>
        public const uint SchemaRevision = 10U;

        public static byte[] Serialize(MachineSimulationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state.ValidateInvariants(null);
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(3UL);
                writer.WriteTag(1UL);
                writer.WriteUnsigned(SchemaRevision);
                writer.WriteTag(2UL);
                writer.WriteBytes(SerializeNodes(state));
                writer.WriteTag(3UL);
                writer.WriteBytes(SerializeLanes(state));
                return writer.ToArray();
            }
        }

        private static byte[] SerializeNodes(MachineSimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.NodeCount);
                foreach (var pair in state.Nodes)
                {
                    collection.WriteBytes(SerializeNode(pair.Value));
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializeNode(MachineNodeState node)
        {
            using (var element = new CanonicalWriter())
            {
                element.WriteFieldCount(17UL);
                WriteBytesField(element, 1UL, SerializeStableId(node.Id));
                WriteUnsignedField(element, 2UL, (byte)node.Kind);
                WriteBytesField(element, 3UL, SerializeStableId(node.DefinitionId));
                WriteBytesField(element, 4UL, SerializeStableId(node.ActiveRecipeId));
                WriteSignedField(element, 5UL, node.ProgressMilliseconds);
                element.WriteTag(6UL);
                element.WriteBoolean(node.IsCycleActive);
                WriteUnsignedField(element, 7UL, (byte)node.Activity);
                WriteUnsignedField(element, 8UL, node.CompletedCycles);
                WriteBytesField(element, 9UL, SerializePorts(node));
                WriteBytesField(element, 10UL, SerializeStableId(node.AttachedNodeId));
                element.WriteTag(11UL);
                element.WriteBoolean(node.HasPlacementPose);
                WriteBytesField(element, 12UL, SerializePose(node.PlacementPose));
                WriteSignedField(
                    element,
                    13UL,
                    node.TransportProgressMillimetres);
                WriteUnsignedField(
                    element,
                    14UL,
                    (byte)node.BeltTravelDirection);
                WriteUnsignedField(
                    element,
                    15UL,
                    (byte)node.BeltLineStatus);
                WriteSignedField(element, 16UL, node.BeltLineUsedCapacity);
                WriteSignedField(element, 17UL, node.BeltLineAvailableCapacity);
                return element.ToArray();
            }
        }

        private static byte[] SerializePose(MachineBuildPose pose)
        {
            using (var element = new CanonicalWriter())
            {
                element.WriteFieldCount(4UL);
                WriteSignedField(element, 1UL, pose.XMillimetres);
                WriteSignedField(element, 2UL, pose.YMillimetres);
                WriteSignedField(element, 3UL, pose.ZMillimetres);
                WriteUnsignedField(element, 4UL, pose.YawQuarterTurns);
                return element.ToArray();
            }
        }

        private static byte[] SerializePorts(MachineNodeState node)
        {
            var isAliased = ReferenceEquals(node.Input, node.Output);
            var portCount = isAliased ? 1UL : 2UL;
            if (node.Fuel != null)
            {
                portCount++;
            }

            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned(portCount);
                collection.WriteBytes(SerializePort(node.Input));
                if (node.Fuel != null)
                {
                    collection.WriteBytes(SerializePort(node.Fuel));
                }

                if (!isAliased)
                {
                    collection.WriteBytes(SerializePort(node.Output));
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializePort(MachinePort port)
        {
            using (var element = new CanonicalWriter())
            {
                element.WriteFieldCount(2UL);
                WriteUnsignedField(element, 1UL, (byte)port.Kind);
                element.WriteTag(2UL);
                using (var slots = new CanonicalWriter())
                {
                    slots.WriteUnsigned((ulong)port.SlotCount);
                    for (var index = 0; index < port.SlotCount; index++)
                    {
                        var slot = port.GetSlot(index);
                        using (var slotElement = new CanonicalWriter())
                        {
                            slotElement.WriteFieldCount(2UL);
                            WriteBytesField(slotElement, 1UL, SerializeStableId(slot.ItemId));
                            WriteUnsignedField(slotElement, 2UL, (ulong)slot.Quantity.Value);
                            slots.WriteBytes(slotElement.ToArray());
                        }
                    }

                    element.WriteBytes(slots.ToArray());
                }

                return element.ToArray();
            }
        }

        /// <summary>
        /// Lanes with their cargo. The items are encoded in the lane's own order, front
        /// first, and their positions are in the hash: two lanes holding the same items at
        /// different positions are different futures, because one of them delivers sooner.
        /// </summary>
        private static byte[] SerializeLanes(MachineSimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.LaneCount);
                foreach (var pair in state.Lanes)
                {
                    var lane = pair.Value;
                    using (var element = new CanonicalWriter())
                    {
                        element.WriteFieldCount(9UL);
                        WriteBytesField(element, 1UL, SerializeStableId(lane.Id));
                        WriteBytesField(element, 2UL, SerializeStableId(lane.SourceNodeId));
                        WriteBytesField(
                            element,
                            3UL,
                            SerializeStableId(lane.DestinationNodeId));
                        WriteBytesField(element, 4UL, SerializeStableId(lane.ItemFilter));
                        WriteSignedField(element, 5UL, lane.LengthMillimetres);
                        WriteSignedField(element, 6UL, lane.SpeedMillimetresPerTick);
                        WriteSignedField(element, 7UL, lane.SpacingMillimetres);
                        WriteUnsignedField(element, 8UL, lane.DeliveredUnits);
                        element.WriteTag(9UL);
                        using (var cargo = new CanonicalWriter())
                        {
                            cargo.WriteUnsigned((ulong)lane.ItemCount);
                            for (var index = 0; index < lane.ItemCount; index++)
                            {
                                var item = lane.Items[index];
                                using (var itemElement = new CanonicalWriter())
                                {
                                    itemElement.WriteFieldCount(2UL);
                                    WriteBytesField(
                                        itemElement,
                                        1UL,
                                        SerializeStableId(item.ItemId));
                                    WriteSignedField(
                                        itemElement,
                                        2UL,
                                        item.PositionMillimetres);
                                    cargo.WriteBytes(itemElement.ToArray());
                                }
                            }

                            element.WriteBytes(cargo.ToArray());
                        }

                        collection.WriteBytes(element.ToArray());
                    }
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializeStableId(StableId value)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(2UL);
                WriteUnsignedField(writer, 1UL, value.High);
                WriteUnsignedField(writer, 2UL, value.Low);
                return writer.ToArray();
            }
        }

        private static void WriteBytesField(CanonicalWriter writer, ulong tag, byte[] bytes)
        {
            writer.WriteTag(tag);
            writer.WriteBytes(bytes);
        }

        private static void WriteUnsignedField(CanonicalWriter writer, ulong tag, ulong value)
        {
            writer.WriteTag(tag);
            writer.WriteUnsigned(value);
        }

        private static void WriteSignedField(CanonicalWriter writer, ulong tag, long value)
        {
            writer.WriteTag(tag);
            writer.WriteSigned(value);
        }
    }
}

using System;
using CML.Foundation;
using CML.Simulation.CanonicalEncoding;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// Canonical AIR subtree. Every collection and every composite element is
    /// length-prefixed, matching the root logical encoding contract.
    /// </summary>
    public static class AirshipCanonicalSerializer
    {
        /// <summary>
        /// 5 adds the repair state of the hull: status, the two installed
        /// counters and the countdown. It is part of the logical hash because a
        /// grounded airship and a flyable one are not the same world.
        /// </summary>
        public const uint SchemaRevision = 5U;

        public static byte[] Serialize(AirshipSimulationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state.ValidateInvariants();
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(5);
                writer.WriteTag(1);
                writer.WriteUnsigned(SchemaRevision);
                writer.WriteTag(2);
                writer.WriteBytes(SerializeAirships(state));
                writer.WriteTag(3);
                writer.WriteBytes(SerializePlayers(state));
                writer.WriteTag(4);
                writer.WriteBytes(SerializeObstacles(state));
                writer.WriteTag(5);
                writer.WriteBytes(SerializeSurfaces(state));
                return writer.ToArray();
            }
        }

        private static byte[] SerializeAirships(AirshipSimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.AirshipCount);
                foreach (var pair in state.Airships)
                {
                    collection.WriteBytes(SerializeAirship(pair.Value));
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializeAirship(AirshipEntityState value)
        {
            using (var element = new CanonicalWriter())
            {
                element.WriteFieldCount(22);
                WriteBytesField(element, 1, SerializeStableId(value.Id));
                WriteBytesField(element, 2, SerializePose(value.Pose));
                WriteUnsignedField(element, 3, (byte)value.Mode);
                WriteSignedField(element, 4, value.LandingTicksRemaining);
                WriteSignedField(element, 5, value.ForwardSpeedMillimetresPerSecond);
                WriteSignedField(element, 6, value.StrafeSpeedMillimetresPerSecond);
                WriteSignedField(element, 7, value.VerticalSpeedMillimetresPerSecond);
                WriteSignedField(element, 8, value.YawRateTurnUnitsPerSecond);
                WriteSignedField(element, 9, value.ForwardIntegrationRemainder);
                WriteSignedField(element, 10, value.StrafeIntegrationRemainder);
                WriteSignedField(element, 11, value.VerticalIntegrationRemainder);
                WriteSignedField(element, 12, value.YawIntegrationRemainder);
                WriteBytesField(element, 13, SerializePilotInput(value.HeldInput));
                WriteBytesField(element, 14, SerializeStableId(value.PilotId));
                WriteBytesField(
                    element,
                    15,
                    SerializeStableId(value.AcceptedLandingSurfaceId));
                WriteBytesField(
                    element,
                    16,
                    SerializeStableId(value.DockedLandingSurfaceId));
                WriteUnsignedField(element, 17, (byte)value.LastLandingRequestResult);
                WriteSignedField(element, 18, value.PitchTurnUnits);
                WriteUnsignedField(element, 19, (byte)value.RepairStatus);
                WriteSignedField(element, 20, value.InstalledIronPlates);
                WriteSignedField(element, 21, value.InstalledInsulatedCables);
                WriteSignedField(element, 22, value.RepairTicksRemaining);
                return element.ToArray();
            }
        }

        private static byte[] SerializePlayers(AirshipSimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.PlayerCount);
                foreach (var pair in state.Players)
                {
                    using (var element = new CanonicalWriter())
                    {
                        var value = pair.Value;
                        element.WriteFieldCount(5);
                        WriteBytesField(element, 1, SerializeStableId(value.Id));
                        WriteUnsignedField(element, 2, (byte)value.FrameKind);
                        WriteBytesField(element, 3, SerializeStableId(value.FrameAirshipId));
                        WriteBytesField(element, 4, SerializePose(value.QuantizedPose));
                        element.WriteTag(5);
                        element.WriteBoolean(value.IsPiloting);
                        collection.WriteBytes(element.ToArray());
                    }
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializeObstacles(AirshipSimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.ObstacleCount);
                foreach (var pair in state.Obstacles)
                {
                    using (var element = new CanonicalWriter())
                    {
                        var value = pair.Value;
                        element.WriteFieldCount(3);
                        WriteBytesField(element, 1, SerializeStableId(value.Id));
                        WriteBytesField(element, 2, SerializeVector(value.Minimum));
                        WriteBytesField(element, 3, SerializeVector(value.Maximum));
                        collection.WriteBytes(element.ToArray());
                    }
                }

                return collection.ToArray();
            }
        }

        private static byte[] SerializeSurfaces(AirshipSimulationState state)
        {
            using (var collection = new CanonicalWriter())
            {
                collection.WriteUnsigned((ulong)state.LandingSurfaceCount);
                foreach (var pair in state.LandingSurfaces)
                {
                    using (var element = new CanonicalWriter())
                    {
                        var value = pair.Value;
                        element.WriteFieldCount(6);
                        WriteBytesField(element, 1, SerializeStableId(value.Id));
                        WriteBytesField(element, 2, SerializeVector(value.Center));
                        WriteUnsignedField(element, 3, value.YawTurn);
                        WriteSignedField(element, 4, value.HalfWidthMillimetres);
                        WriteSignedField(element, 5, value.HalfDepthMillimetres);
                        WriteBytesField(
                            element,
                            6,
                            SerializeStableId(value.SupportingObstacleId));
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
                writer.WriteFieldCount(2);
                WriteUnsignedField(writer, 1, value.High);
                WriteUnsignedField(writer, 2, value.Low);
                return writer.ToArray();
            }
        }

        private static byte[] SerializeVector(AirshipVector3Millimetres value)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(3);
                WriteSignedField(writer, 1, value.X);
                WriteSignedField(writer, 2, value.Y);
                WriteSignedField(writer, 3, value.Z);
                return writer.ToArray();
            }
        }

        private static byte[] SerializePose(AirshipPoseState value)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(2);
                WriteBytesField(writer, 1, SerializeVector(value.Position));
                WriteUnsignedField(writer, 2, value.YawTurn);
                return writer.ToArray();
            }
        }

        private static byte[] SerializePilotInput(AirshipPilotInputState value)
        {
            using (var writer = new CanonicalWriter())
            {
                writer.WriteFieldCount(4);
                WriteSignedField(writer, 1, value.ThrottleChangePermille);
                WriteSignedField(writer, 2, value.LiftPermille);
                WriteSignedField(writer, 3, value.YawDeltaPermille);
                WriteSignedField(writer, 4, value.PitchDeltaPermille);
                return writer.ToArray();
            }
        }

        private static void WriteBytesField(
            CanonicalWriter writer,
            ulong tag,
            byte[] bytes)
        {
            writer.WriteTag(tag);
            writer.WriteBytes(bytes);
        }

        private static void WriteUnsignedField(
            CanonicalWriter writer,
            ulong tag,
            ulong value)
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

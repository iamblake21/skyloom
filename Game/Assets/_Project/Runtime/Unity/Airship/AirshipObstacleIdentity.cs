using System;
using CML.Foundation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class AirshipObstacleIdentity : MonoBehaviour
    {
        [SerializeField] private string stableId = "00000000000000000000000000000000";

        public StableId StableId => ParseRequired(stableId, "obstacle");

        public void Configure(StableId id)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("An obstacle id cannot be none.", nameof(id));
            }

            stableId = id.ToString();
        }

        public AirshipObstacleState BuildLogicalState()
        {
            var collider = GetComponent<Collider>();
            var bounds = collider.bounds;
            return new AirshipObstacleState(
                StableId,
                Quantize(bounds.min),
                Quantize(bounds.max));
        }

        internal static StableId ParseRequired(string value, string kind)
        {
            if (!CML.Foundation.StableId.TryParse(value, out var id) || id.IsNone)
            {
                throw new InvalidOperationException(
                    $"AIR {kind} requires a non-zero 32-character stable id.");
            }

            return id;
        }

        internal static AirshipVector3Millimetres Quantize(Vector3 value)
        {
            return new AirshipVector3Millimetres(
                QuantizeMetres(value.x),
                QuantizeMetres(value.y),
                QuantizeMetres(value.z));
        }

        internal static ushort QuantizeYaw(float yawDegrees)
        {
            var turns = (long)Math.Round(
                yawDegrees * (65_536d / 360d),
                MidpointRounding.AwayFromZero);
            return unchecked((ushort)turns);
        }

        private static long QuantizeMetres(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return checked((long)Math.Round(
                value * 1000d,
                MidpointRounding.AwayFromZero));
        }
    }

}

using System;
using CML.Foundation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class AirshipLandingSurfaceIdentity : MonoBehaviour
    {
        [SerializeField] private string stableId =
            "00000000000000000000000000000000";
        [SerializeField] private string supportingObstacleId =
            "00000000000000000000000000000000";
        [SerializeField] private BoxCollider surfaceCollider;

        public StableId StableId =>
            AirshipObstacleIdentity.ParseRequired(stableId, "landing surface");

        public StableId SupportingObstacleId
        {
            get
            {
                if (string.IsNullOrEmpty(supportingObstacleId)
                    || supportingObstacleId
                        == "00000000000000000000000000000000")
                {
                    return StableId.None;
                }

                return AirshipObstacleIdentity.ParseRequired(
                    supportingObstacleId,
                    "supporting obstacle");
            }
        }

        public BoxCollider SurfaceCollider =>
            surfaceCollider != null
                ? surfaceCollider
                : GetComponent<BoxCollider>();

        public void Configure(
            StableId id,
            StableId supportId)
        {
            if (id.IsNone)
            {
                throw new ArgumentException(
                    "A landing surface id cannot be none.",
                    nameof(id));
            }

            stableId = id.ToString();
            supportingObstacleId = supportId.ToString();
            surfaceCollider = GetComponent<BoxCollider>();
        }

        public AirshipLandingSurfaceState BuildLogicalState()
        {
            if (!TryBuildLogicalState(out var state))
            {
                throw new InvalidOperationException(
                    "AIR landing surface requires one enabled, non-trigger BoxCollider "
                    + "whose world top is horizontal and whose X/Z footprint is at "
                    + "least 900 mm deep by 800 mm wide.");
            }

            return state;
        }

        public bool TryBuildLogicalState(out AirshipLandingSurfaceState state)
        {
            state = null;
            if (!CML.Foundation.StableId.TryParse(stableId, out var id)
                || id.IsNone
                || !TryGetSupportingObstacleId(out var supportId))
            {
                return false;
            }

            var collider = SurfaceCollider;
            if (collider == null
                || collider.gameObject != gameObject
                || !collider.enabled
                || collider.isTrigger)
            {
                return false;
            }

            var right = transform.TransformVector(
                Vector3.right * collider.size.x);
            var up = transform.TransformVector(
                Vector3.up * collider.size.y);
            var forward = transform.TransformVector(
                Vector3.forward * collider.size.z);
            if (!IsFinite(right)
                || !IsFinite(up)
                || !IsFinite(forward)
                || right.magnitude < 0.9f
                || forward.magnitude < 0.8f
                || up.magnitude <= 0f
                || Vector3.Dot(up.normalized, Vector3.up) < 0.999999f
                || Mathf.Abs(Vector3.Dot(right.normalized, forward.normalized))
                    > 0.000001f)
            {
                return false;
            }

            var topCenter = transform.TransformPoint(
                collider.center + (Vector3.up * (collider.size.y * 0.5f)));
            var halfDepth = QuantizePositiveMillimetres(right.magnitude * 0.5f);
            var halfWidth = QuantizePositiveMillimetres(forward.magnitude * 0.5f);
            if (halfDepth < 450 || halfWidth < 400)
            {
                return false;
            }

            state = new AirshipLandingSurfaceState(
                id,
                AirshipObstacleIdentity.Quantize(topCenter),
                AirshipObstacleIdentity.QuantizeYaw(transform.eulerAngles.y),
                halfWidth,
                halfDepth,
                supportId);
            return true;
        }

        public bool MatchesLogicalState(AirshipLandingSurfaceState expected)
        {
            if (expected == null || !TryBuildLogicalState(out var actual))
            {
                return false;
            }

            return actual.Id == expected.Id
                && actual.Center == expected.Center
                && actual.YawTurn == expected.YawTurn
                && actual.HalfWidthMillimetres
                    == expected.HalfWidthMillimetres
                && actual.HalfDepthMillimetres
                    == expected.HalfDepthMillimetres
                && actual.SupportingObstacleId
                    == expected.SupportingObstacleId;
        }

        public bool OwnsCollider(Collider candidate)
        {
            return candidate != null && candidate == SurfaceCollider;
        }

        private void OnValidate()
        {
            surfaceCollider = GetComponent<BoxCollider>();
        }

        private bool TryGetSupportingObstacleId(out StableId supportId)
        {
            if (string.IsNullOrEmpty(supportingObstacleId)
                || supportingObstacleId == "00000000000000000000000000000000")
            {
                supportId = StableId.None;
                return true;
            }

            return CML.Foundation.StableId.TryParse(
                    supportingObstacleId,
                    out supportId)
                && !supportId.IsNone;
        }

        private static int QuantizePositiveMillimetres(float metres)
        {
            if (float.IsNaN(metres)
                || float.IsInfinity(metres)
                || metres <= 0f)
            {
                return 0;
            }

            return checked((int)Math.Round(
                metres * 1000d,
                MidpointRounding.AwayFromZero));
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsNaN(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x)
                && !float.IsInfinity(value.y)
                && !float.IsInfinity(value.z);
        }
    }
}

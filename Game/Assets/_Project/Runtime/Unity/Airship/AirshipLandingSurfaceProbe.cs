using CML.Foundation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    public readonly struct AirshipLandingSurface
    {
        public AirshipLandingSurface(
            StableId surfaceId,
            Vector3 point,
            Vector3 normal,
            AirshipLandingSurfaceIdentity identity)
        {
            SurfaceId = surfaceId;
            Point = point;
            Normal = normal;
            Identity = identity;
        }

        public StableId SurfaceId { get; }

        public Vector3 Point { get; }

        public Vector3 Normal { get; }

        public AirshipLandingSurfaceIdentity Identity { get; }
    }

    /// <summary>
    /// Physical discovery only. It accepts one stable-id surface only when the
    /// complete required width/depth grid and passenger corridor are continuous.
    /// The pure core independently revalidates that id.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipLandingSurfaceProbe : MonoBehaviour
    {
        private const int MaximumHits = 64;

        [SerializeField] private Transform vehicleRoot;
        [SerializeField] private Transform gangwayOrigin;
        [SerializeField] private LayerMask disembarkationLayers = ~0;
        [SerializeField, Min(0.05f)] private float minimumHorizontalReach = 0.4f;
        [SerializeField, Min(0.05f)] private float maximumHorizontalReach = 2.5f;
        [SerializeField, Min(0.01f)] private float reachStep = 0.05f;
        [SerializeField, Min(0.1f)] private float requiredWidth = 0.8f;
        [SerializeField, Min(0.1f)] private float requiredDepth = 0.9f;
        [SerializeField, Range(3, 17)] private int lateralSamples = 5;
        [SerializeField, Range(3, 21)] private int depthSamples = 7;
        [SerializeField, Min(0.1f)] private float passengerHeight = 1.8f;
        [SerializeField, Min(0.05f)] private float passengerRadius = 0.3f;
        [SerializeField, Range(0f, 60f)] private float maximumSlopeDegrees = 35f;
        [SerializeField, Min(0.01f)] private float rayAllowance = 0.5f;
        [SerializeField, Min(0.01f)] private float maximumHeightDelta = 0.35f;

        private readonly Collider[] _overlapHits = new Collider[MaximumHits];
        private readonly RaycastHit[] _raycastHits = new RaycastHit[MaximumHits];

        public Transform GangwayOrigin => gangwayOrigin != null
            ? gangwayOrigin
            : transform;

        public void Configure(
            Transform root,
            Transform physicalGangwayOrigin,
            LayerMask surfaceLayers,
            float minimumReach,
            float maximumReach)
        {
            vehicleRoot = root != null ? root : transform;
            gangwayOrigin = physicalGangwayOrigin;
            disembarkationLayers = surfaceLayers;
            minimumHorizontalReach = minimumReach;
            maximumHorizontalReach = maximumReach;
        }

        public bool TryFindLandingSurface(
            AirshipPoseState committedPose,
            out AirshipLandingSurface surface)
        {
            surface = default;
            var rampLocal = new AirshipVector3Millimetres(
                AirshipSimulationConstants.RampTipLocalXMillimetres,
                AirshipSimulationConstants.RampTipLocalYMillimetres,
                AirshipSimulationConstants.RampTipLocalZMillimetres);
            var rampWorld = committedPose.Position
                + FixedTurnTrig.RotateLocalToWorld(
                    rampLocal,
                    committedPose.YawTurn);
            var outwardMillimetres = FixedTurnTrig.RotateLocalToWorld(
                new AirshipVector3Millimetres(1_000, 0, 0),
                committedPose.YawTurn);
            var origin = AirshipMotor.ToUnityPosition(rampWorld);
            var outward = new Vector3(
                outwardMillimetres.X,
                0f,
                outwardMillimetres.Z).normalized;
            if (outward.sqrMagnitude < 0.99f)
            {
                return false;
            }

            var lateral = Vector3.Cross(Vector3.up, outward).normalized;
            Physics.SyncTransforms();
            for (var reach = minimumHorizontalReach;
                 reach <= maximumHorizontalReach + 0.0001f;
                 reach += Mathf.Max(0.01f, reachStep))
            {
                if (TryValidateFootprint(
                    origin,
                    outward,
                    lateral,
                    reach,
                    out surface))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryValidateFootprint(
            Vector3 origin,
            Vector3 outward,
            Vector3 lateral,
            float reach,
            out AirshipLandingSurface surface)
        {
            surface = default;
            AirshipLandingSurfaceIdentity commonIdentity = null;
            Vector3 firstPoint = default;
            Vector3 firstNormal = default;
            var slopeCosine = Mathf.Cos(maximumSlopeDegrees * Mathf.Deg2Rad);

            for (var depthIndex = 0; depthIndex < Mathf.Max(3, depthSamples); depthIndex++)
            {
                var depthT = depthIndex / (float)(Mathf.Max(3, depthSamples) - 1);
                var forward = reach + (requiredDepth * depthT);
                for (var lateralIndex = 0;
                     lateralIndex < Mathf.Max(3, lateralSamples);
                     lateralIndex++)
                {
                    var lateralT =
                        lateralIndex / (float)(Mathf.Max(3, lateralSamples) - 1);
                    var side = Mathf.Lerp(
                        -requiredWidth * 0.5f,
                        requiredWidth * 0.5f,
                        lateralT);
                    var rayOrigin = origin
                        + (outward * forward)
                        + (lateral * side)
                        + (Vector3.up * rayAllowance);
                    if (!TryRaycastPastVehicle(
                            rayOrigin,
                            rayAllowance + maximumHeightDelta,
                            out var hit)
                        || Vector3.Dot(hit.normal, Vector3.up) < slopeCosine)
                    {
                        return false;
                    }

                    if (!TryGetOwningIdentity(hit.collider, out var identity)
                        || (commonIdentity != null && identity != commonIdentity)
                        || Mathf.Abs(hit.point.y - origin.y) > maximumHeightDelta)
                    {
                        return false;
                    }

                    if (commonIdentity == null)
                    {
                        commonIdentity = identity;
                        firstPoint = hit.point;
                        firstNormal = hit.normal;
                    }
                }
            }

            if (commonIdentity == null
                || !CorridorIsClear(origin, outward, lateral, reach, commonIdentity))
            {
                return false;
            }

            surface = new AirshipLandingSurface(
                commonIdentity.StableId,
                firstPoint,
                firstNormal,
                commonIdentity);
            return true;
        }

        private bool TryRaycastPastVehicle(
            Vector3 origin,
            float maximumDistance,
            out RaycastHit selected)
        {
            selected = default;
            var hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                _raycastHits,
                maximumDistance,
                disembarkationLayers,
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0 || hitCount >= MaximumHits)
            {
                return false;
            }

            var nearestExternalDistance = float.PositiveInfinity;
            for (var index = 0; index < hitCount; index++)
            {
                var hit = _raycastHits[index];
                if (hit.collider == null || IsPartOfVehicle(hit.collider.transform))
                {
                    continue;
                }

                nearestExternalDistance = Mathf.Min(
                    nearestExternalDistance,
                    hit.distance);
            }

            if (float.IsPositiveInfinity(nearestExternalDistance))
            {
                return false;
            }

            const float equalDistanceTolerance = 0.0001f;
            AirshipLandingSurfaceIdentity commonIdentity = null;
            var hasCandidate = false;
            for (var index = 0; index < hitCount; index++)
            {
                var hit = _raycastHits[index];
                if (hit.collider == null
                    || IsPartOfVehicle(hit.collider.transform)
                    || Mathf.Abs(hit.distance - nearestExternalDistance)
                        > equalDistanceTolerance)
                {
                    continue;
                }

                if (!TryGetOwningIdentity(hit.collider, out var identity))
                {
                    return false;
                }

                if (commonIdentity != null && identity != commonIdentity)
                {
                    // PhysX does not define ordering for coplanar hits. Rejecting
                    // different identities makes the discovery result independent
                    // of collider insertion and instance ids.
                    return false;
                }

                commonIdentity = identity;
                if (!hasCandidate || IsMoreConservative(hit, selected))
                {
                    selected = hit;
                    hasCandidate = true;
                }
            }

            return hasCandidate;
        }

        private bool CorridorIsClear(
            Vector3 origin,
            Vector3 outward,
            Vector3 lateral,
            float reach,
            AirshipLandingSurfaceIdentity supportingSurface)
        {
            var length = reach + requiredDepth;
            var center = origin
                + (outward * (length * 0.5f))
                + (Vector3.up * (passengerHeight * 0.5f));
            var rotation = Quaternion.LookRotation(outward, Vector3.up);
            var halfExtents = new Vector3(
                requiredWidth * 0.5f,
                passengerHeight * 0.5f,
                length * 0.5f);
            var hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _overlapHits,
                rotation,
                disembarkationLayers,
                QueryTriggerInteraction.Ignore);
            if (hitCount >= MaximumHits)
            {
                // NonAlloc APIs do not report whether more results were omitted.
                // A full buffer is therefore ambiguous and must fail closed.
                return false;
            }

            for (var index = 0; index < hitCount; index++)
            {
                var hit = _overlapHits[index];
                if (hit == null
                    || IsPartOfVehicle(hit.transform)
                    || supportingSurface.OwnsCollider(hit))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool TryGetOwningIdentity(
            Collider collider,
            out AirshipLandingSurfaceIdentity identity)
        {
            identity = collider != null
                ? collider.GetComponentInParent<AirshipLandingSurfaceIdentity>()
                : null;
            return identity != null && identity.OwnsCollider(collider);
        }

        private static bool IsMoreConservative(
            RaycastHit candidate,
            RaycastHit current)
        {
            const float tolerance = 0.000001f;
            if (candidate.normal.y < current.normal.y - tolerance)
            {
                return true;
            }

            if (candidate.normal.y > current.normal.y + tolerance)
            {
                return false;
            }

            if (candidate.normal.x < current.normal.x - tolerance)
            {
                return true;
            }

            if (candidate.normal.x > current.normal.x + tolerance)
            {
                return false;
            }

            return candidate.normal.z < current.normal.z;
        }

        private bool IsPartOfVehicle(Transform candidate)
        {
            var root = vehicleRoot != null ? vehicleRoot : transform;
            return candidate == root || candidate.IsChildOf(root);
        }

        private void OnValidate()
        {
            maximumHorizontalReach = Mathf.Max(
                minimumHorizontalReach,
                maximumHorizontalReach);
            reachStep = Mathf.Max(0.01f, reachStep);
            requiredWidth = Mathf.Max(0.8f, requiredWidth);
            requiredDepth = Mathf.Max(0.9f, requiredDepth);
            passengerRadius = Mathf.Max(0.05f, passengerRadius);
            passengerHeight = Mathf.Max(passengerRadius * 2f, passengerHeight);
            rayAllowance = Mathf.Max(0.01f, rayAllowance);
            maximumHeightDelta = Mathf.Max(0.01f, maximumHeightDelta);
        }
    }
}

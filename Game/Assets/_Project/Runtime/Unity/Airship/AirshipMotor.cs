using System;
using System.Collections.Generic;
using CML.Foundation;
using CML.Simulation.Airship;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Presentation-only projection of two committed global simulation poses.
    /// It owns no clock, accepts no gameplay input and cannot change logical state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipMotor : MonoBehaviour
    {
        private const float MillimetresToMetres = 0.001f;
        private const float WorldCollisionSkinMetres = 0.015f;
        private const int WorldQueryCapacity = 128;

        [SerializeField] private Transform vehicleRoot;
        [SerializeField] private Rigidbody collisionBody;

        private SimulationTick _previousTick;
        private SimulationTick _currentTick;
        private AirshipPoseState _previousPose;
        private AirshipPoseState _currentPose;
        private int _previousPitchTurnUnits;
        private int _currentPitchTurnUnits;
        private bool _hasCommittedPose;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private bool _hasPresentationTarget;
        private readonly RaycastHit[] _worldSweepHits =
            new RaycastHit[WorldQueryCapacity];
        private readonly Collider[] _worldOverlapHits =
            new Collider[WorldQueryCapacity];
        private readonly HashSet<Collider> _previousWorldOverlaps =
            new HashSet<Collider>();

        public Transform VehicleRoot => vehicleRoot != null ? vehicleRoot : transform;

        public Rigidbody CollisionBody => collisionBody;

        public bool HasCommittedPose => _hasCommittedPose;

        public SimulationTick CurrentTick => _currentTick;

        public AirshipPoseState PreviousCommittedPose => _previousPose;

        public AirshipPoseState CurrentCommittedPose => _currentPose;

        public int CurrentPitchTurnUnits => _currentPitchTurnUnits;

        public void Configure(
            Transform presentationRoot,
            Rigidbody kinematicCollisionBody = null)
        {
            vehicleRoot = presentationRoot != null ? presentationRoot : transform;
            collisionBody = kinematicCollisionBody != null
                ? kinematicCollisionBody
                : vehicleRoot.GetComponent<Rigidbody>();
            if (collisionBody != null)
            {
                collisionBody.isKinematic = true;
                collisionBody.useGravity = false;
                collisionBody.interpolation = RigidbodyInterpolation.Interpolate;
                collisionBody.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousSpeculative;
            }
        }

        public void CommitPose(
            SimulationTick tick,
            AirshipPoseState pose,
            int pitchTurnUnits = 0)
        {
            if (_hasCommittedPose && tick < _currentTick)
            {
                throw new InvalidOperationException(
                    "Airship presentation cannot consume a pose older than its current commit.");
            }

            if (!_hasCommittedPose)
            {
                _previousTick = tick;
                _currentTick = tick;
                _previousPose = pose;
                _currentPose = pose;
                _previousPitchTurnUnits = pitchTurnUnits;
                _currentPitchTurnUnits = pitchTurnUnits;
                _hasCommittedPose = true;
                ApplyPose(pose, pitchTurnUnits);
                return;
            }

            if (tick == _currentTick)
            {
                if (pose != _currentPose
                    || pitchTurnUnits != _currentPitchTurnUnits)
                {
                    throw new InvalidOperationException(
                        "One committed tick cannot project two different airship poses.");
                }

                return;
            }

            _previousTick = _currentTick;
            _previousPose = _currentPose;
            _previousPitchTurnUnits = _currentPitchTurnUnits;
            _currentTick = tick;
            _currentPose = pose;
            _currentPitchTurnUnits = pitchTurnUnits;
        }

        public void Render(float interpolation)
        {
            if (!_hasCommittedPose)
            {
                return;
            }

            var alpha = Mathf.Clamp01(interpolation);
            var previousPosition = ToUnityPosition(_previousPose.Position);
            var currentPosition = ToUnityPosition(_currentPose.Position);
            var position = Vector3.LerpUnclamped(previousPosition, currentPosition, alpha);
            var yawDelta = unchecked((short)(
                _currentPose.YawTurn - _previousPose.YawTurn));
            var interpolatedTurn = _previousPose.YawTurn + (yawDelta * alpha);
            var yawDegrees = interpolatedTurn * (360f / 65_536f);
            var pitchTurnUnits = Mathf.LerpUnclamped(
                _previousPitchTurnUnits,
                _currentPitchTurnUnits,
                alpha);
            var pitchDegrees = pitchTurnUnits * (360f / 65_536f);
            QueuePresentationPose(
                position,
                Quaternion.Euler(pitchDegrees, yawDegrees, 0f));
        }

        public void SnapToCurrent()
        {
            if (_hasCommittedPose)
            {
                ApplyPose(_currentPose, _currentPitchTurnUnits);
            }
        }

        /// <summary>
        /// Sweeps the canonical AIR compound hull through the authored Unity world.
        /// This catches TerrainCollider, mesh scenery and buildables that cannot be
        /// represented by the small static obstacle list in the pure simulation.
        /// Vehicle children and the disabled pilot controller are deliberately not
        /// world obstacles.
        /// </summary>
        public bool IsWorldMotionClear(
            AirshipPoseState currentPose,
            int currentPitchTurnUnits,
            AirshipPoseState candidatePose,
            int candidatePitchTurnUnits)
        {
            if (currentPose == candidatePose
                && currentPitchTurnUnits == candidatePitchTurnUnits)
            {
                return true;
            }

            var currentPosition = ToUnityPosition(currentPose.Position);
            var candidatePosition = ToUnityPosition(candidatePose.Position);
            var currentRotation = ToUnityRotation(
                currentPose.YawTurn,
                currentPitchTurnUnits);
            var candidateRotation = ToUnityRotation(
                candidatePose.YawTurn,
                candidatePitchTurnUnits);
            var displacement = candidatePosition - currentPosition;
            var distance = displacement.magnitude;
            var direction = distance > Mathf.Epsilon
                ? displacement / distance
                : Vector3.zero;
            var scale = AbsoluteLossyScale(VehicleRoot.lossyScale);

            if (!IsTerrainMotionClear(
                    currentPosition,
                    currentRotation,
                    candidatePosition,
                    candidateRotation,
                    scale,
                    distance))
            {
                return false;
            }

            CollectExternalOverlaps(
                currentPosition,
                currentRotation,
                scale,
                _previousWorldOverlaps);

            for (var hullIndex = 0;
                hullIndex < AirshipCollision.CanonicalHullCount;
                hullIndex++)
            {
                GetWorldHull(
                    hullIndex,
                    currentPosition,
                    currentRotation,
                    scale,
                    out var currentCenter,
                    out var halfExtents);
                var queryHalfExtents = ShrinkForWorldSweep(halfExtents);
                if (distance > Mathf.Epsilon)
                {
                    var hitCount = Physics.BoxCastNonAlloc(
                        currentCenter,
                        queryHalfExtents,
                        direction,
                        _worldSweepHits,
                        currentRotation,
                        distance + WorldCollisionSkinMetres,
                        Physics.AllLayers,
                        QueryTriggerInteraction.Ignore);
                    for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
                    {
                        var collider = _worldSweepHits[hitIndex].collider;
                        if (IsExternalWorldCollider(collider)
                            && !_previousWorldOverlaps.Contains(collider))
                        {
                            return false;
                        }
                    }
                }

                GetWorldHull(
                    hullIndex,
                    candidatePosition,
                    candidateRotation,
                    scale,
                    out var candidateCenter,
                    out halfExtents);
                var overlapCount = Physics.OverlapBoxNonAlloc(
                    candidateCenter,
                    ShrinkForWorldSweep(halfExtents),
                    _worldOverlapHits,
                    candidateRotation,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
                for (var overlapIndex = 0;
                    overlapIndex < overlapCount;
                    overlapIndex++)
                {
                    var collider = _worldOverlapHits[overlapIndex];
                    if (IsExternalWorldCollider(collider)
                        && !_previousWorldOverlaps.Contains(collider))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void ApplyPose(AirshipPoseState pose, int pitchTurnUnits)
        {
            var position = ToUnityPosition(pose.Position);
            var rotation = Quaternion.Euler(
                pitchTurnUnits * (360f / 65_536f),
                pose.YawTurn * (360f / 65_536f),
                0f);
            _targetPosition = position;
            _targetRotation = rotation;
            _hasPresentationTarget = true;
            if (collisionBody != null)
            {
                collisionBody.position = position;
                collisionBody.rotation = rotation;
                return;
            }

            VehicleRoot.SetPositionAndRotation(position, rotation);
        }

        private void FixedUpdate()
        {
            if (!_hasPresentationTarget || collisionBody == null)
            {
                return;
            }

            collisionBody.MovePosition(_targetPosition);
            collisionBody.MoveRotation(_targetRotation);
        }

        private void QueuePresentationPose(
            Vector3 position,
            Quaternion rotation)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _hasPresentationTarget = true;
            if (collisionBody == null)
            {
                VehicleRoot.SetPositionAndRotation(position, rotation);
            }
        }

        private void CollectExternalOverlaps(
            Vector3 rootPosition,
            Quaternion rootRotation,
            Vector3 scale,
            ISet<Collider> destination)
        {
            destination.Clear();
            for (var hullIndex = 0;
                hullIndex < AirshipCollision.CanonicalHullCount;
                hullIndex++)
            {
                GetWorldHull(
                    hullIndex,
                    rootPosition,
                    rootRotation,
                    scale,
                    out var center,
                    out var halfExtents);
                var hitCount = Physics.OverlapBoxNonAlloc(
                    center,
                    ShrinkForWorldSweep(halfExtents),
                    _worldOverlapHits,
                    rootRotation,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
                for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    var collider = _worldOverlapHits[hitIndex];
                    if (IsExternalWorldCollider(collider))
                    {
                        destination.Add(collider);
                    }
                }
            }
        }

        private bool IsExternalWorldCollider(Collider collider)
        {
            if (collider == null
                || !collider.enabled
                || collider.isTrigger
                || collider is TerrainCollider
                || (collisionBody != null
                    && collider.attachedRigidbody == collisionBody))
            {
                return false;
            }

            var root = VehicleRoot;
            return collider.transform != root
                && !collider.transform.IsChildOf(root);
        }

        private static bool IsTerrainMotionClear(
            Vector3 currentPosition,
            Quaternion currentRotation,
            Vector3 candidatePosition,
            Quaternion candidateRotation,
            Vector3 scale,
            float distance)
        {
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
            {
                return true;
            }

            var currentClearance = MinimumTerrainClearance(
                currentPosition,
                currentRotation,
                scale,
                terrains);
            var stepCount = Mathf.Max(1, Mathf.CeilToInt(distance / 0.25f));
            for (var step = 1; step <= stepCount; step++)
            {
                var interpolation = step / (float)stepCount;
                var sampledPosition = Vector3.LerpUnclamped(
                    currentPosition,
                    candidatePosition,
                    interpolation);
                var sampledRotation = Quaternion.SlerpUnclamped(
                    currentRotation,
                    candidateRotation,
                    interpolation);
                var clearance = MinimumTerrainClearance(
                    sampledPosition,
                    sampledRotation,
                    scale,
                    terrains);
                if (clearance < WorldCollisionSkinMetres
                    && clearance < currentClearance - 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static float MinimumTerrainClearance(
            Vector3 rootPosition,
            Quaternion rootRotation,
            Vector3 scale,
            Terrain[] terrains)
        {
            var minimum = float.PositiveInfinity;
            var minimumX = AirshipCollision.CanonicalVisualEnvelopeMinimum.X
                * MillimetresToMetres;
            var maximumX = AirshipCollision.CanonicalVisualEnvelopeMaximum.X
                * MillimetresToMetres;
            var minimumY = AirshipCollision.CanonicalVisualEnvelopeMinimum.Y
                * MillimetresToMetres;
            var minimumZ = AirshipCollision.CanonicalVisualEnvelopeMinimum.Z
                * MillimetresToMetres;
            var maximumZ = AirshipCollision.CanonicalVisualEnvelopeMaximum.Z
                * MillimetresToMetres;

            for (var xIndex = 0; xIndex < 3; xIndex++)
            {
                var x = Mathf.Lerp(minimumX, maximumX, xIndex * 0.5f);
                for (var zIndex = 0; zIndex < 3; zIndex++)
                {
                    var z = Mathf.Lerp(minimumZ, maximumZ, zIndex * 0.5f);
                    var localPoint = Vector3.Scale(
                        new Vector3(x, minimumY, z),
                        scale);
                    var worldPoint = rootPosition + (rootRotation * localPoint);
                    if (TryGetTerrainHeight(
                            worldPoint,
                            terrains,
                            out var terrainHeight))
                    {
                        minimum = Mathf.Min(minimum, worldPoint.y - terrainHeight);
                    }
                }
            }

            return minimum;
        }

        private static bool TryGetTerrainHeight(
            Vector3 worldPoint,
            Terrain[] terrains,
            out float height)
        {
            height = float.NegativeInfinity;
            var found = false;
            for (var index = 0; index < terrains.Length; index++)
            {
                var terrain = terrains[index];
                if (terrain == null || terrain.terrainData == null)
                {
                    continue;
                }

                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (worldPoint.x < origin.x
                    || worldPoint.x > origin.x + size.x
                    || worldPoint.z < origin.z
                    || worldPoint.z > origin.z + size.z)
                {
                    continue;
                }

                height = Mathf.Max(
                    height,
                    terrain.SampleHeight(worldPoint) + origin.y);
                found = true;
            }

            return found;
        }

        private static void GetWorldHull(
            int hullIndex,
            Vector3 rootPosition,
            Quaternion rootRotation,
            Vector3 scale,
            out Vector3 center,
            out Vector3 halfExtents)
        {
            AirshipCollision.GetCanonicalHull(
                hullIndex,
                out var localCenterMillimetres,
                out var halfXMillimetres,
                out var halfYMillimetres,
                out var halfZMillimetres);
            var localCenter = Vector3.Scale(
                new Vector3(
                    localCenterMillimetres.X * MillimetresToMetres,
                    localCenterMillimetres.Y * MillimetresToMetres,
                    localCenterMillimetres.Z * MillimetresToMetres),
                scale);
            center = rootPosition + (rootRotation * localCenter);
            halfExtents = Vector3.Scale(
                new Vector3(
                    halfXMillimetres * MillimetresToMetres,
                    halfYMillimetres * MillimetresToMetres,
                    halfZMillimetres * MillimetresToMetres),
                scale);
        }

        private static Vector3 ShrinkForWorldSweep(Vector3 halfExtents)
        {
            return new Vector3(
                Mathf.Max(0.001f, halfExtents.x - WorldCollisionSkinMetres),
                Mathf.Max(0.001f, halfExtents.y - WorldCollisionSkinMetres),
                Mathf.Max(0.001f, halfExtents.z - WorldCollisionSkinMetres));
        }

        private static Vector3 AbsoluteLossyScale(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static Quaternion ToUnityRotation(
            ushort yawTurn,
            int pitchTurnUnits)
        {
            return Quaternion.Euler(
                pitchTurnUnits * (360f / 65_536f),
                yawTurn * (360f / 65_536f),
                0f);
        }

        internal static Vector3 ToUnityPosition(AirshipVector3Millimetres value)
        {
            return new Vector3(
                value.X / 1000f,
                value.Y / 1000f,
                value.Z / 1000f);
        }
    }
}

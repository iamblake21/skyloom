using System;
using CML.Foundation;

namespace CML.Simulation.Airship
{
    internal readonly struct AirshipOrientedBox
    {
        public AirshipOrientedBox(
            AirshipVector3Millimetres localCenter,
            int halfX,
            int halfY,
            int halfZ)
        {
            LocalCenter = localCenter;
            HalfX = halfX;
            HalfY = halfY;
            HalfZ = halfZ;
        }

        public AirshipVector3Millimetres LocalCenter { get; }

        public int HalfX { get; }

        public int HalfY { get; }

        public int HalfZ { get; }
    }

    /// <summary>
    /// Authoritative integer collision against canonical obstacle geometry. Both
    /// translation and yaw are swept; a blocked candidate is rejected atomically.
    /// </summary>
    public static class AirshipCollision
    {
        /// <summary>
        /// Conservative root-local envelope for the complete production render.
        /// It includes an integer safety margin around the imported FBX bounds, so
        /// no visible part can enter canonical obstacle geometry before collision.
        /// </summary>
        public static readonly AirshipVector3Millimetres
            CanonicalVisualEnvelopeMinimum =
                new AirshipVector3Millimetres(-3_600, -100, -5_100);

        public static readonly AirshipVector3Millimetres
            CanonicalVisualEnvelopeMaximum =
                new AirshipVector3Millimetres(3_600, 3_500, 4_900);

        public const int CanonicalForwardHullMaximumZMillimetres = 5_000;

        private static readonly AirshipOrientedBox[] CanonicalHulls =
        {
            new AirshipOrientedBox(
                new AirshipVector3Millimetres(0, 1_550, 0),
                1_950,
                1_750,
                5_000),
            new AirshipOrientedBox(
                new AirshipVector3Millimetres(-2_380, 1_550, -3_350),
                1_150,
                1_600,
                1_700),
            new AirshipOrientedBox(
                new AirshipVector3Millimetres(2_380, 1_550, -3_350),
                1_150,
                1_600,
                1_700),
            new AirshipOrientedBox(
                new AirshipVector3Millimetres(2_400, 1_100, 850),
                700,
                1_150,
                900),
        };

        /// <summary>
        /// Exposes the same conservative compound hull to Unity world sweeps. The
        /// adapter receives integer-authored members rather than inventing a second
        /// flight volume that could drift away from authoritative AIR collision.
        /// </summary>
        public static int CanonicalHullCount => CanonicalHulls.Length;

        public static void GetCanonicalHull(
            int index,
            out AirshipVector3Millimetres localCenter,
            out int halfXMillimetres,
            out int halfYMillimetres,
            out int halfZMillimetres)
        {
            if (index < 0 || index >= CanonicalHulls.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var hull = CanonicalHulls[index];
            localCenter = hull.LocalCenter;
            halfXMillimetres = hull.HalfX;
            halfYMillimetres = hull.HalfY;
            halfZMillimetres = hull.HalfZ;
        }

        /// <summary>
        /// Editor/tooling contract: returns the deterministic compound member that
        /// fully contains a root-local axis-aligned renderer bound.
        /// </summary>
        public static bool TryFindContainingCanonicalHull(
            AirshipVector3Millimetres minimum,
            AirshipVector3Millimetres maximum,
            out int hullIndex)
        {
            if (minimum.X > maximum.X
                || minimum.Y > maximum.Y
                || minimum.Z > maximum.Z)
            {
                throw new ArgumentException(
                    "A local renderer bound must have ordered minimum/maximum values.");
            }

            for (var index = 0; index < CanonicalHulls.Length; index++)
            {
                var hull = CanonicalHulls[index];
                var hullMinimum = new AirshipVector3Millimetres(
                    hull.LocalCenter.X - hull.HalfX,
                    hull.LocalCenter.Y - hull.HalfY,
                    hull.LocalCenter.Z - hull.HalfZ);
                var hullMaximum = new AirshipVector3Millimetres(
                    hull.LocalCenter.X + hull.HalfX,
                    hull.LocalCenter.Y + hull.HalfY,
                    hull.LocalCenter.Z + hull.HalfZ);
                if (minimum.X >= hullMinimum.X
                    && minimum.Y >= hullMinimum.Y
                    && minimum.Z >= hullMinimum.Z
                    && maximum.X <= hullMaximum.X
                    && maximum.Y <= hullMaximum.Y
                    && maximum.Z <= hullMaximum.Z)
                {
                    hullIndex = index;
                    return true;
                }
            }

            hullIndex = -1;
            return false;
        }

        public static bool IsCandidateClear(
            AirshipSimulationState state,
            AirshipPoseState current,
            AirshipPoseState candidate)
        {
            return IsCandidateClear(state, current, 0, candidate, 0);
        }

        public static bool IsCandidateClear(
            AirshipSimulationState state,
            AirshipPoseState current,
            int currentPitchTurnUnits,
            AirshipPoseState candidate,
            int candidatePitchTurnUnits)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var delta = candidate.Position - current.Position;
            var yawDelta = AirshipIntegerMath.ShortestTurnDelta(
                current.YawTurn,
                candidate.YawTurn);
            var pitchDelta = checked(
                candidatePitchTurnUnits - currentPitchTurnUnits);
            var maximumStep = Math.Max(
                Math.Max(
                    AirshipIntegerMath.Abs(delta.X),
                    AirshipIntegerMath.Abs(delta.Y)),
                Math.Max(
                    AirshipIntegerMath.Abs(delta.Z),
                    Math.Max(
                        AirshipIntegerMath.Abs(yawDelta),
                        AirshipIntegerMath.Abs(pitchDelta))));

            if (maximumStep == 0L)
            {
                return !IntersectsAnyObstacle(
                    state,
                    candidate,
                    candidatePitchTurnUnits,
                    StableId.None);
            }

            // A legal one-tick flight candidate is bounded by 1,000 mm and 410
            // turn units. Rejecting an impossible leap protects deterministic cost.
            if (maximumStep > 2_000L)
            {
                throw new SimulationInvariantException(
                    "An airship candidate exceeds the maximum legal swept tick.");
            }

            var steps = checked((int)maximumStep);
            for (var step = 1; step <= steps; step++)
            {
                var position = new AirshipVector3Millimetres(
                    checked(current.Position.X + ((delta.X * step) / steps)),
                    checked(current.Position.Y + ((delta.Y * step) / steps)),
                    checked(current.Position.Z + ((delta.Z * step) / steps)));
                var yaw = AirshipIntegerMath.AddTurn(
                    current.YawTurn,
                    (yawDelta * step) / steps);
                var pitch = checked(
                    currentPitchTurnUnits
                    + ((pitchDelta * step) / steps));
                if (IntersectsAnyObstacle(
                    state,
                    new AirshipPoseState(position, yaw),
                    pitch,
                    StableId.None))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IntersectsAnyObstacle(
            AirshipSimulationState state,
            AirshipPoseState pose,
            StableId ignoredObstacleId)
        {
            return IntersectsAnyObstacle(state, pose, 0, ignoredObstacleId);
        }

        internal static bool IntersectsAnyObstacle(
            AirshipSimulationState state,
            AirshipPoseState pose,
            int pitchTurnUnits,
            StableId ignoredObstacleId)
        {
            FixedTurnTrig.SinCos(
                unchecked((ushort)pitchTurnUnits),
                out var pitchSine,
                out var pitchCosine);
            var absolutePitchSine = AirshipIntegerMath.Abs(pitchSine);
            var absolutePitchCosine = AirshipIntegerMath.Abs(pitchCosine);

            foreach (var obstaclePair in state.Obstacles)
            {
                if (obstaclePair.Key == ignoredObstacleId)
                {
                    continue;
                }

                for (var hullIndex = 0; hullIndex < CanonicalHulls.Length; hullIndex++)
                {
                    var hull = CanonicalHulls[hullIndex];
                    var pitchedCenter = new AirshipVector3Millimetres(
                        hull.LocalCenter.X,
                        AirshipIntegerMath.RoundDivideAwayFromZero(
                            checked(
                                (hull.LocalCenter.Y * (long)pitchCosine)
                                - (hull.LocalCenter.Z * (long)pitchSine)),
                            FixedTurnTrig.One),
                        AirshipIntegerMath.RoundDivideAwayFromZero(
                            checked(
                                (hull.LocalCenter.Y * (long)pitchSine)
                                + (hull.LocalCenter.Z * (long)pitchCosine)),
                            FixedTurnTrig.One));
                    var pitchedHalfY = checked((int)CeilingDivide(
                        checked(
                            (hull.HalfY * (long)absolutePitchCosine)
                            + (hull.HalfZ * (long)absolutePitchSine)),
                        FixedTurnTrig.One));
                    var pitchedHalfZ = checked((int)CeilingDivide(
                        checked(
                            (hull.HalfY * (long)absolutePitchSine)
                            + (hull.HalfZ * (long)absolutePitchCosine)),
                        FixedTurnTrig.One));
                    var rotatedCenter = FixedTurnTrig.RotateLocalToWorld(
                        pitchedCenter,
                        pose.YawTurn);
                    var worldCenter = pose.Position + rotatedCenter;
                    if (IntersectsOrientedBox(
                        worldCenter,
                        pose.YawTurn,
                        hull.HalfX,
                        pitchedHalfY,
                        pitchedHalfZ,
                        obstaclePair.Value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static long CeilingDivide(long numerator, long positiveDenominator)
        {
            if (numerator < 0L || positiveDenominator <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(numerator));
            }

            return checked(
                (numerator + positiveDenominator - 1L)
                / positiveDenominator);
        }

        internal static bool IntersectsOrientedBox(
            AirshipVector3Millimetres center,
            ushort yawTurn,
            int halfX,
            int halfY,
            int halfZ,
            AirshipObstacleState obstacle)
        {
            FixedTurnTrig.SinCos(yawTurn, out var sine, out var cosine);
            var absSine = AirshipIntegerMath.Abs(sine);
            var absCosine = AirshipIntegerMath.Abs(cosine);
            const long q = FixedTurnTrig.One;

            var deltaX2 = checked((center.X * 2L) - obstacle.Minimum.X - obstacle.Maximum.X);
            var deltaY2 = checked((center.Y * 2L) - obstacle.Minimum.Y - obstacle.Maximum.Y);
            var deltaZ2 = checked((center.Z * 2L) - obstacle.Minimum.Z - obstacle.Maximum.Z);
            var obstacleHalfX2 = checked(obstacle.Maximum.X - obstacle.Minimum.X);
            var obstacleHalfY2 = checked(obstacle.Maximum.Y - obstacle.Minimum.Y);
            var obstacleHalfZ2 = checked(obstacle.Maximum.Z - obstacle.Minimum.Z);
            var halfX2 = checked(halfX * 2L);
            var halfY2 = checked(halfY * 2L);
            var halfZ2 = checked(halfZ * 2L);

            if (AirshipIntegerMath.Abs(deltaY2)
                > checked(halfY2 + obstacleHalfY2))
            {
                return false;
            }

            // World X and Z axes.
            if (checked(AirshipIntegerMath.Abs(deltaX2) * q)
                > checked((halfX2 * absCosine) + (halfZ2 * absSine) + (obstacleHalfX2 * q)))
            {
                return false;
            }

            if (checked(AirshipIntegerMath.Abs(deltaZ2) * q)
                > checked((halfX2 * absSine) + (halfZ2 * absCosine) + (obstacleHalfZ2 * q)))
            {
                return false;
            }

            // Oriented-box local X and Z axes.
            var localDeltaXScaled = checked((deltaX2 * cosine) - (deltaZ2 * sine));
            if (AirshipIntegerMath.Abs(localDeltaXScaled)
                > checked((halfX2 * q) + (obstacleHalfX2 * absCosine) + (obstacleHalfZ2 * absSine)))
            {
                return false;
            }

            var localDeltaZScaled = checked((deltaX2 * sine) + (deltaZ2 * cosine));
            if (AirshipIntegerMath.Abs(localDeltaZScaled)
                > checked((halfZ2 * q) + (obstacleHalfX2 * absSine) + (obstacleHalfZ2 * absCosine)))
            {
                return false;
            }

            return true;
        }
    }
}

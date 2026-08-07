using System;
using CML.Foundation;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// Independently validates a surface id against canonical geometry. Unity
    /// raycasts are discovery/presentation only and cannot authorize landing.
    /// </summary>
    public static class AirshipLandingValidator
    {
        public static bool IsReachable(
            AirshipSimulationState state,
            AirshipPoseState airshipPose,
            StableId surfaceId)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return TryFindReach(state, airshipPose, surfaceId, out _, out _);
        }

        public static bool TryGetDisembarkPoint(
            AirshipSimulationState state,
            AirshipPoseState airshipPose,
            StableId surfaceId,
            out AirshipVector3Millimetres worldPoint)
        {
            if (TryFindReach(state, airshipPose, surfaceId, out var surface, out var reach))
            {
                var rampWorld = GetRampWorld(airshipPose);
                var point = OffsetFromRamp(rampWorld, airshipPose.YawTurn, reach, 0);
                worldPoint = new AirshipVector3Millimetres(
                    point.X,
                    surface.Center.Y,
                    point.Z);
                return true;
            }

            worldPoint = default;
            return false;
        }

        private static bool TryFindReach(
            AirshipSimulationState state,
            AirshipPoseState airshipPose,
            StableId surfaceId,
            out AirshipLandingSurfaceState surface,
            out int acceptedReach)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.TryGetLandingSurface(surfaceId, out surface))
            {
                acceptedReach = 0;
                return false;
            }

            var rampWorld = GetRampWorld(airshipPose);
            if (AirshipIntegerMath.Abs(surface.Center.Y - rampWorld.Y)
                > AirshipSimulationConstants.LandingMaximumHeightDeltaMillimetres)
            {
                acceptedReach = 0;
                return false;
            }

            for (var reach = AirshipSimulationConstants.LandingMinimumReachMillimetres;
                 reach <= AirshipSimulationConstants.LandingMaximumReachMillimetres;
                 reach += AirshipSimulationConstants.LandingSampleStepMillimetres)
            {
                if (SurfaceContainsContinuousPad(
                        surface,
                        rampWorld,
                        airshipPose.YawTurn,
                        reach)
                    && CorridorIsClear(
                        state,
                        surface,
                        rampWorld,
                        airshipPose.YawTurn,
                        reach))
                {
                    acceptedReach = reach;
                    return true;
                }
            }

            acceptedReach = 0;
            return false;
        }

        private static AirshipVector3Millimetres GetRampWorld(AirshipPoseState airshipPose)
        {
            var rampLocal = new AirshipVector3Millimetres(
                AirshipSimulationConstants.RampTipLocalXMillimetres,
                AirshipSimulationConstants.RampTipLocalYMillimetres,
                AirshipSimulationConstants.RampTipLocalZMillimetres);
            return airshipPose.Position
                + FixedTurnTrig.RotateLocalToWorld(rampLocal, airshipPose.YawTurn);
        }

        private static bool SurfaceContainsContinuousPad(
            AirshipLandingSurfaceState surface,
            AirshipVector3Millimetres rampWorld,
            ushort corridorYaw,
            int reach)
        {
            var halfWidth =
                AirshipSimulationConstants.LandingRequiredCorridorWidthMillimetres / 2;
            var depth = AirshipSimulationConstants.LandingRequiredPadDepthMillimetres;
            var step = AirshipSimulationConstants.LandingSampleStepMillimetres;

            // Sampling the whole required grid (including exact far edges) makes
            // continuity, useful width and depth explicit rather than accepting one hit.
            for (var forward = 0; forward <= depth; forward += step)
            {
                var distance = checked(reach + forward);
                for (var lateral = -halfWidth; lateral <= halfWidth; lateral += step)
                {
                    if (!SurfaceContainsPoint(
                            surface,
                            OffsetFromRamp(rampWorld, corridorYaw, distance, lateral)))
                    {
                        return false;
                    }
                }
            }

            if (depth % step != 0)
            {
                for (var lateral = -halfWidth; lateral <= halfWidth; lateral += step)
                {
                    if (!SurfaceContainsPoint(
                            surface,
                            OffsetFromRamp(rampWorld, corridorYaw, reach + depth, lateral)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SurfaceContainsPoint(
            AirshipLandingSurfaceState surface,
            AirshipVector3Millimetres worldPoint)
        {
            var relative = worldPoint - surface.Center;
            var local = FixedTurnTrig.RotateWorldToLocal(relative, surface.YawTurn);
            return AirshipIntegerMath.Abs(local.X) <= surface.HalfDepthMillimetres
                && AirshipIntegerMath.Abs(local.Z) <= surface.HalfWidthMillimetres;
        }

        private static bool CorridorIsClear(
            AirshipSimulationState state,
            AirshipLandingSurfaceState surface,
            AirshipVector3Millimetres rampWorld,
            ushort yawTurn,
            int reach)
        {
            var totalLength = checked(
                reach + AirshipSimulationConstants.LandingRequiredPadDepthMillimetres);
            var centerOffset = new AirshipVector3Millimetres(
                totalLength / 2,
                900,
                0);
            var corridorCenter = rampWorld
                + FixedTurnTrig.RotateLocalToWorld(centerOffset, yawTurn);

            foreach (var obstaclePair in state.Obstacles)
            {
                if (obstaclePair.Key == surface.SupportingObstacleId)
                {
                    continue;
                }

                if (AirshipCollision.IntersectsOrientedBox(
                    corridorCenter,
                    yawTurn,
                    (totalLength / 2) - 1,
                    899,
                    (AirshipSimulationConstants.LandingRequiredCorridorWidthMillimetres / 2) - 1,
                    obstaclePair.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static AirshipVector3Millimetres OffsetFromRamp(
            AirshipVector3Millimetres rampWorld,
            ushort yawTurn,
            int forward,
            int lateral)
        {
            return rampWorld + FixedTurnTrig.RotateLocalToWorld(
                new AirshipVector3Millimetres(forward, 0, lateral),
                yawTurn);
        }
    }
}

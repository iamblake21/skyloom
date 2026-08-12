#include "Simulation/CMLAirshipLanding.h"

#include "Simulation/CMLFixedTurnTrig.h"

namespace
{
    FCMLAirshipVector Add(const FCMLAirshipVector& A, const FCMLAirshipVector& B)
    {
        FCMLAirshipVector Result;
        Result.X = A.X + B.X;
        Result.Y = A.Y + B.Y;
        Result.Z = A.Z + B.Z;
        return Result;
    }

    FCMLAirshipVector Subtract(const FCMLAirshipVector& A, const FCMLAirshipVector& B)
    {
        FCMLAirshipVector Result;
        Result.X = A.X - B.X;
        Result.Y = A.Y - B.Y;
        Result.Z = A.Z - B.Z;
        return Result;
    }

    /** A point `Forward` ahead of and `Lateral` beside the ramp, in world space. */
    FCMLAirshipVector OffsetFromRamp(
        const FCMLAirshipVector& RampWorld,
        const uint16 YawTurn,
        const int64 Forward,
        const int64 Lateral)
    {
        FCMLAirshipVector Local;
        Local.X = Forward;
        Local.Y = 0;
        Local.Z = Lateral;
        return Add(RampWorld, FCMLFixedTurnTrig::RotateLocalToWorld(Local, YawTurn));
    }

    bool FindSurface(
        const FCMLAirshipSimulationState& State,
        const FCMLStableId& SurfaceId,
        FCMLAirshipLandingSurface& OutSurface)
    {
        for (const FCMLAirshipLandingSurface& Surface : State.LandingSurfaces)
        {
            if (Surface.Id == SurfaceId)
            {
                OutSurface = Surface;
                return true;
            }
        }
        return false;
    }

    /**
     * The pad has to be continuous over the whole corridor, sampled on a fixed
     * grid. Testing only the centre would accept a surface with a hole exactly
     * where a player would step off.
     */
    bool SurfaceContainsContinuousPad(
        const FCMLAirshipLandingSurface& Surface,
        const FCMLAirshipVector& RampWorld,
        const uint16 CorridorYaw,
        const int32 Reach)
    {
        const int32 HalfWidth = FCMLAirshipLanding::RequiredCorridorWidthMillimetres / 2;
        const int32 Depth = FCMLAirshipLanding::RequiredPadDepthMillimetres;
        const int32 Step = FCMLAirshipLanding::SampleStepMillimetres;

        for (int32 Forward = 0; Forward <= Depth; Forward += Step)
        {
            const int64 Distance = static_cast<int64>(Reach) + Forward;
            for (int32 Lateral = -HalfWidth; Lateral <= HalfWidth; Lateral += Step)
            {
                if (!FCMLAirshipLanding::SurfaceContainsPoint(
                        Surface, OffsetFromRamp(RampWorld, CorridorYaw, Distance, Lateral)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /** No obstacle may sit in the corridor between the ramp and the pad. */
    bool CorridorIsClear(
        const FCMLAirshipSimulationState& State,
        const FCMLAirshipVector& RampWorld,
        const uint16 CorridorYaw,
        const int32 Reach)
    {
        const int32 HalfWidth = FCMLAirshipLanding::RequiredCorridorWidthMillimetres / 2;
        const int32 Step = FCMLAirshipLanding::SampleStepMillimetres;

        for (int32 Forward = 0; Forward <= Reach; Forward += Step)
        {
            for (int32 Lateral = -HalfWidth; Lateral <= HalfWidth; Lateral += Step)
            {
                const FCMLAirshipVector Point =
                    OffsetFromRamp(RampWorld, CorridorYaw, Forward, Lateral);
                for (const FCMLAirshipObstacle& Obstacle : State.Obstacles)
                {
                    if (Point.X >= Obstacle.Minimum.X && Point.X <= Obstacle.Maximum.X
                        && Point.Y >= Obstacle.Minimum.Y && Point.Y <= Obstacle.Maximum.Y
                        && Point.Z >= Obstacle.Minimum.Z && Point.Z <= Obstacle.Maximum.Z)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }
}

FCMLAirshipVector FCMLAirshipLanding::GetRampWorld(const FCMLAirshipPose& Pose)
{
    FCMLAirshipVector RampLocal;
    RampLocal.X = RampTipLocalXMillimetres;
    RampLocal.Y = RampTipLocalYMillimetres;
    RampLocal.Z = RampTipLocalZMillimetres;
    return Add(
        Pose.Position,
        FCMLFixedTurnTrig::RotateLocalToWorld(RampLocal, static_cast<uint16>(Pose.YawTurn)));
}

bool FCMLAirshipLanding::SurfaceContainsPoint(
    const FCMLAirshipLandingSurface& Surface,
    const FCMLAirshipVector& WorldPoint)
{
    const FCMLAirshipVector Relative = Subtract(WorldPoint, Surface.Center);
    const FCMLAirshipVector Local =
        FCMLFixedTurnTrig::RotateWorldToLocal(Relative, static_cast<uint16>(Surface.YawTurn));
    const int64 AbsoluteX = Local.X < 0 ? -Local.X : Local.X;
    const int64 AbsoluteZ = Local.Z < 0 ? -Local.Z : Local.Z;
    return AbsoluteX <= Surface.HalfDepthMillimetres && AbsoluteZ <= Surface.HalfWidthMillimetres;
}

bool FCMLAirshipLanding::TryFindReach(
    const FCMLAirshipSimulationState& State,
    const FCMLAirshipPose& Pose,
    const FCMLStableId& SurfaceId,
    int32& OutAcceptedReach)
{
    OutAcceptedReach = 0;

    FCMLAirshipLandingSurface Surface;
    if (!FindSurface(State, SurfaceId, Surface))
    {
        return false;
    }

    const FCMLAirshipVector RampWorld = GetRampWorld(Pose);
    const int64 HeightDelta = Surface.Center.Y - RampWorld.Y;
    const int64 AbsoluteHeightDelta = HeightDelta < 0 ? -HeightDelta : HeightDelta;
    if (AbsoluteHeightDelta > MaximumHeightDeltaMillimetres)
    {
        // Too tall a step: the ramp would not meet the surface.
        return false;
    }

    // The search runs outwards, so the *nearest* legal pad wins rather than
    // whichever one happens to be tested first.
    const uint16 Yaw = static_cast<uint16>(Pose.YawTurn);
    for (int32 Reach = MinimumReachMillimetres;
         Reach <= MaximumReachMillimetres;
         Reach += SampleStepMillimetres)
    {
        if (SurfaceContainsContinuousPad(Surface, RampWorld, Yaw, Reach)
            && CorridorIsClear(State, RampWorld, Yaw, Reach))
        {
            OutAcceptedReach = Reach;
            return true;
        }
    }
    return false;
}

bool FCMLAirshipLanding::IsReachable(
    const FCMLAirshipSimulationState& State,
    const FCMLAirshipPose& Pose,
    const FCMLStableId& SurfaceId)
{
    int32 Reach = 0;
    return TryFindReach(State, Pose, SurfaceId, Reach);
}

bool FCMLAirshipLanding::TryGetDisembarkPoint(
    const FCMLAirshipSimulationState& State,
    const FCMLAirshipPose& Pose,
    const FCMLStableId& SurfaceId,
    FCMLAirshipVector& OutWorldPoint)
{
    int32 Reach = 0;
    if (!TryFindReach(State, Pose, SurfaceId, Reach))
    {
        OutWorldPoint = FCMLAirshipVector();
        return false;
    }

    FCMLAirshipLandingSurface Surface;
    FindSurface(State, SurfaceId, Surface);
    const FCMLAirshipVector RampWorld = GetRampWorld(Pose);
    const FCMLAirshipVector Point =
        OffsetFromRamp(RampWorld, static_cast<uint16>(Pose.YawTurn), Reach, 0);

    OutWorldPoint.X = Point.X;
    // The player lands on the surface, not at the ramp's height.
    OutWorldPoint.Y = Surface.Center.Y;
    OutWorldPoint.Z = Point.Z;
    return true;
}

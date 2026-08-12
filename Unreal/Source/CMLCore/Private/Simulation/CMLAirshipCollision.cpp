#include "Simulation/CMLAirshipCollision.h"

#include "Simulation/CMLFixedTurnTrig.h"

namespace
{
    int64 Absolute(const int64 Value)
    {
        return Value < 0 ? -Value : Value;
    }

    /**
     * Separating-axis test between the yaw-rotated hull and an axis-aligned
     * obstacle.
     *
     * Comparing corners is not an intersection test: a thin wall crossing the
     * middle of the hull contains none of the hull's corners, and none of its
     * own corners lie inside the hull either, so a corner comparison would call
     * a direct hit "clear". The separating-axis theorem answers the actual
     * question — is there any direction along which the two do not overlap.
     *
     * The hull only rotates about the vertical, so height is a plain interval
     * overlap and only the horizontal plane needs the four candidate axes:
     * the world's two and the hull's two.
     */
    bool OverlapsObstacle(
        const FCMLAirshipPose& Pose,
        const FCMLAirshipVector& HalfExtents,
        const FCMLAirshipObstacle& Obstacle)
    {
        const int64 HullMinY = Pose.Position.Y - HalfExtents.Y;
        const int64 HullMaxY = Pose.Position.Y + HalfExtents.Y;
        if (HullMaxY < Obstacle.Minimum.Y || HullMinY > Obstacle.Maximum.Y)
        {
            return false;
        }

        int32 SineQ30 = 0;
        int32 CosineQ30 = 0;
        FCMLFixedTurnTrig::SinCos(static_cast<uint16>(Pose.YawTurn), SineQ30, CosineQ30);

        // Hull axes in world space, kept in Q30 so the whole test stays integer.
        const int64 AxisXx = CosineQ30;
        const int64 AxisXz = -static_cast<int64>(SineQ30);
        const int64 AxisZx = SineQ30;
        const int64 AxisZz = CosineQ30;
        const int64 One = FCMLFixedTurnTrig::One;

        const int64 ObstacleCentreX = (Obstacle.Minimum.X + Obstacle.Maximum.X) / 2;
        const int64 ObstacleCentreZ = (Obstacle.Minimum.Z + Obstacle.Maximum.Z) / 2;
        const int64 ObstacleHalfX = (Obstacle.Maximum.X - Obstacle.Minimum.X) / 2;
        const int64 ObstacleHalfZ = (Obstacle.Maximum.Z - Obstacle.Minimum.Z) / 2;

        const int64 OffsetX = ObstacleCentreX - Pose.Position.X;
        const int64 OffsetZ = ObstacleCentreZ - Pose.Position.Z;

        // Axis = the four candidates, each expressed in Q30 so the projections
        // are directly comparable.
        const int64 AxesX[4] = {One, 0, AxisXx, AxisZx};
        const int64 AxesZ[4] = {0, One, AxisXz, AxisZz};

        for (int32 Index = 0; Index < 4; ++Index)
        {
            const int64 AxisX = AxesX[Index];
            const int64 AxisZ = AxesZ[Index];

            // Each dot product of two Q30 axes lands in Q60, so it is brought
            // back to Q30 *before* being scaled by a half-extent. Multiplying
            // first would overflow int64 on any real hull size.
            const int64 HullAxisXDot = (AxisXx * AxisX + AxisXz * AxisZ) / One;
            const int64 HullAxisZDot = (AxisZx * AxisX + AxisZz * AxisZ) / One;

            const int64 Distance = FMath::Abs(OffsetX * AxisX + OffsetZ * AxisZ);
            const int64 HullReach =
                FMath::Abs(HalfExtents.X * HullAxisXDot)
                + FMath::Abs(HalfExtents.Z * HullAxisZDot);
            const int64 ObstacleReach =
                FMath::Abs(ObstacleHalfX * AxisX) + FMath::Abs(ObstacleHalfZ * AxisZ);

            if (Distance > HullReach + ObstacleReach)
            {
                // A gap along this axis means no overlap anywhere.
                return false;
            }
        }
        return true;
    }
}

int32 FCMLAirshipCollision::ShortestTurnDelta(const uint16 From, const uint16 To)
{
    // Reinterpreting the difference as signed gives the short way round for
    // free: turning from 65500 to 20 is +56, not -65480.
    return static_cast<int16>(static_cast<uint16>(To - From));
}

FCMLAirshipVector FCMLAirshipCollision::GetHullHalfExtents()
{
    FCMLAirshipVector HalfExtents;
    HalfExtents.X = 2000;
    HalfExtents.Y = 900;
    HalfExtents.Z = 4000;
    return HalfExtents;
}

bool FCMLAirshipCollision::IntersectsAnyObstacle(
    const FCMLAirshipSimulationState& State,
    const FCMLAirshipPose& Pose)
{
    const FCMLAirshipVector HalfExtents = GetHullHalfExtents();
    for (const FCMLAirshipObstacle& Obstacle : State.Obstacles)
    {
        if (OverlapsObstacle(Pose, HalfExtents, Obstacle))
        {
            return true;
        }
    }
    return false;
}

bool FCMLAirshipCollision::IsCandidateClear(
    const FCMLAirshipSimulationState& State,
    const FCMLAirshipPose& Current,
    const FCMLAirshipPose& Candidate)
{
    const int64 DeltaX = Candidate.Position.X - Current.Position.X;
    const int64 DeltaY = Candidate.Position.Y - Current.Position.Y;
    const int64 DeltaZ = Candidate.Position.Z - Current.Position.Z;
    const int32 YawDelta = ShortestTurnDelta(
        static_cast<uint16>(Current.YawTurn), static_cast<uint16>(Candidate.YawTurn));

    const int64 MaximumStep = FMath::Max(
        FMath::Max(Absolute(DeltaX), Absolute(DeltaY)),
        FMath::Max(Absolute(DeltaZ), Absolute(static_cast<int64>(YawDelta))));

    if (MaximumStep == 0)
    {
        return !IntersectsAnyObstacle(State, Candidate);
    }
    if (MaximumStep > MaximumLegalSweptStep)
    {
        // Not a legal one-tick flight. Sweeping it would cost an unbounded
        // number of samples, so it is refused rather than approximated.
        return false;
    }

    // One sample per millimetre (or turn unit) of the largest component: the
    // sweep can never step over an obstacle thinner than its own resolution.
    const int32 Steps = static_cast<int32>(MaximumStep);
    for (int32 Step = 1; Step <= Steps; ++Step)
    {
        FCMLAirshipPose Sample;
        Sample.Position.X = Current.Position.X + ((DeltaX * Step) / Steps);
        Sample.Position.Y = Current.Position.Y + ((DeltaY * Step) / Steps);
        Sample.Position.Z = Current.Position.Z + ((DeltaZ * Step) / Steps);
        Sample.YawTurn = static_cast<int32>(static_cast<uint16>(
            static_cast<int64>(Current.YawTurn) + ((static_cast<int64>(YawDelta) * Step) / Steps)));

        if (IntersectsAnyObstacle(State, Sample))
        {
            return false;
        }
    }
    return true;
}

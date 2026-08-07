using System;

namespace CML.Simulation.Airship
{
    /// <summary>
    /// Integer-only trigonometry for canonical turn16 yaw. CORDIC constants are
    /// expressed in Q32 turns and results in Q30. The four cardinal turns are exact.
    /// </summary>
    public static class FixedTurnTrig
    {
        public const int FractionBits = 30;
        public const int One = 1 << FractionBits;

        private const long CordicGainInverseQ30 = 652_032_874L;

        private static readonly long[] ArcTangentQ32Turns =
        {
            536_870_912L,
            316_933_406L,
            167_458_907L,
            85_004_756L,
            42_667_331L,
            21_354_465L,
            10_679_838L,
            5_340_245L,
            2_670_163L,
            1_335_087L,
            667_544L,
            333_772L,
            166_886L,
            83_443L,
            41_722L,
            20_861L,
            10_430L,
            5_215L,
            2_608L,
            1_304L,
            652L,
            326L,
            163L,
            81L,
            41L,
            20L,
            10L,
            5L,
            3L,
            1L,
            1L,
        };

        public static void SinCos(ushort yawTurn, out int sineQ30, out int cosineQ30)
        {
            switch (yawTurn)
            {
                case 0:
                    sineQ30 = 0;
                    cosineQ30 = One;
                    return;
                case 16_384:
                    sineQ30 = One;
                    cosineQ30 = 0;
                    return;
                case 32_768:
                    sineQ30 = 0;
                    cosineQ30 = -One;
                    return;
                case 49_152:
                    sineQ30 = -One;
                    cosineQ30 = 0;
                    return;
            }

            var reducedTurn = (int)unchecked((short)yawTurn);
            var sign = 1;
            if (reducedTurn > 16_384)
            {
                reducedTurn -= 32_768;
                sign = -1;
            }
            else if (reducedTurn < -16_384)
            {
                reducedTurn += 32_768;
                sign = -1;
            }

            long x = CordicGainInverseQ30;
            long y = 0L;
            long z = checked((long)reducedTurn * 65_536L);

            for (var index = 0; index < ArcTangentQ32Turns.Length; index++)
            {
                var previousX = x;
                if (z >= 0L)
                {
                    x = checked(x - (y >> index));
                    y = checked(y + (previousX >> index));
                    z -= ArcTangentQ32Turns[index];
                }
                else
                {
                    x = checked(x + (y >> index));
                    y = checked(y - (previousX >> index));
                    z += ArcTangentQ32Turns[index];
                }
            }

            cosineQ30 = ClampQ30(checked(x * sign));
            sineQ30 = ClampQ30(checked(y * sign));
        }

        public static AirshipVector3Millimetres RotateLocalToWorld(
            AirshipVector3Millimetres local,
            ushort yawTurn)
        {
            SinCos(yawTurn, out var sine, out var cosine);
            var worldXNumerator = checked((local.X * cosine) + (local.Z * sine));
            var worldZNumerator = checked((-local.X * sine) + (local.Z * cosine));
            return new AirshipVector3Millimetres(
                AirshipIntegerMath.RoundDivideAwayFromZero(worldXNumerator, One),
                local.Y,
                AirshipIntegerMath.RoundDivideAwayFromZero(worldZNumerator, One));
        }

        public static AirshipVector3Millimetres RotateWorldToLocal(
            AirshipVector3Millimetres world,
            ushort yawTurn)
        {
            SinCos(yawTurn, out var sine, out var cosine);
            var localXNumerator = checked((world.X * cosine) - (world.Z * sine));
            var localZNumerator = checked((world.X * sine) + (world.Z * cosine));
            return new AirshipVector3Millimetres(
                AirshipIntegerMath.RoundDivideAwayFromZero(localXNumerator, One),
                world.Y,
                AirshipIntegerMath.RoundDivideAwayFromZero(localZNumerator, One));
        }

        private static int ClampQ30(long value)
        {
            if (value > One)
            {
                return One;
            }

            if (value < -One)
            {
                return -One;
            }

            return checked((int)value);
        }
    }
}

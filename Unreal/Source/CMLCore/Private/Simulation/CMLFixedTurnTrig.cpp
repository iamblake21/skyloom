#include "Simulation/CMLFixedTurnTrig.h"

namespace
{
    constexpr int64 CordicGainInverseQ30 = 652032874LL;

    /** arctan(2^-i) expressed in Q32 turns, transcribed from the Unity table. */
    constexpr int64 ArcTangentQ32Turns[] = {
        536870912LL, 316933406LL, 167458907LL, 85004756LL, 42667331LL, 21354465LL,
        10679838LL, 5340245LL, 2670163LL, 1335087LL, 667544LL, 333772LL, 166886LL,
        83443LL, 41722LL, 20861LL, 10430LL, 5215LL, 2608LL, 1304LL, 652LL, 326LL,
        163LL, 81LL, 41LL, 20LL, 10LL, 5LL, 3LL, 1LL, 1LL
    };

    int32 ClampQ30(const int64 Value)
    {
        if (Value > FCMLFixedTurnTrig::One)
        {
            return FCMLFixedTurnTrig::One;
        }
        if (Value < -FCMLFixedTurnTrig::One)
        {
            return -FCMLFixedTurnTrig::One;
        }
        return static_cast<int32>(Value);
    }
}

void FCMLFixedTurnTrig::SinCos(const uint16 YawTurn, int32& OutSineQ30, int32& OutCosineQ30)
{
    // The cardinal turns are returned exactly. CORDIC would land a bit or two
    // away, and a quarter turn that is not exactly a quarter turn shows up as
    // an airship that never quite faces along an axis.
    switch (YawTurn)
    {
    case 0:
        OutSineQ30 = 0;
        OutCosineQ30 = One;
        return;
    case 16384:
        OutSineQ30 = One;
        OutCosineQ30 = 0;
        return;
    case 32768:
        OutSineQ30 = 0;
        OutCosineQ30 = -One;
        return;
    case 49152:
        OutSineQ30 = -One;
        OutCosineQ30 = 0;
        return;
    default:
        break;
    }

    // Reinterpret as signed so the turn folds into [-16384, 16384], the range
    // the CORDIC rotation converges over; the reflection carries the sign.
    int32 ReducedTurn = static_cast<int32>(static_cast<int16>(YawTurn));
    int32 Sign = 1;
    if (ReducedTurn > 16384)
    {
        ReducedTurn -= 32768;
        Sign = -1;
    }
    else if (ReducedTurn < -16384)
    {
        ReducedTurn += 32768;
        Sign = -1;
    }

    int64 X = CordicGainInverseQ30;
    int64 Y = 0;
    int64 Z = static_cast<int64>(ReducedTurn) * 65536LL;

    for (int32 Index = 0; Index < UE_ARRAY_COUNT(ArcTangentQ32Turns); ++Index)
    {
        const int64 PreviousX = X;
        if (Z >= 0)
        {
            X = X - (Y >> Index);
            Y = Y + (PreviousX >> Index);
            Z -= ArcTangentQ32Turns[Index];
        }
        else
        {
            X = X + (Y >> Index);
            Y = Y - (PreviousX >> Index);
            Z += ArcTangentQ32Turns[Index];
        }
    }

    OutCosineQ30 = ClampQ30(X * Sign);
    OutSineQ30 = ClampQ30(Y * Sign);
}

FCMLAirshipVector FCMLFixedTurnTrig::RotateLocalToWorld(
    const FCMLAirshipVector& Local,
    const uint16 YawTurn)
{
    int32 Sine = 0;
    int32 Cosine = 0;
    SinCos(YawTurn, Sine, Cosine);

    const int64 WorldXNumerator = (Local.X * Cosine) + (Local.Z * Sine);
    const int64 WorldZNumerator = (-Local.X * Sine) + (Local.Z * Cosine);
    FCMLAirshipVector Result;
    Result.X = RoundDivideAwayFromZero(WorldXNumerator, One);
    Result.Y = Local.Y;
    Result.Z = RoundDivideAwayFromZero(WorldZNumerator, One);
    return Result;
}

FCMLAirshipVector FCMLFixedTurnTrig::RotateWorldToLocal(
    const FCMLAirshipVector& World,
    const uint16 YawTurn)
{
    int32 Sine = 0;
    int32 Cosine = 0;
    SinCos(YawTurn, Sine, Cosine);

    const int64 LocalXNumerator = (World.X * Cosine) - (World.Z * Sine);
    const int64 LocalZNumerator = (World.X * Sine) + (World.Z * Cosine);
    FCMLAirshipVector Result;
    Result.X = RoundDivideAwayFromZero(LocalXNumerator, One);
    Result.Y = World.Y;
    Result.Z = RoundDivideAwayFromZero(LocalZNumerator, One);
    return Result;
}

int64 FCMLFixedTurnTrig::RoundDivideAwayFromZero(
    const int64 Numerator,
    const int64 PositiveDenominator)
{
    if (PositiveDenominator <= 0 || Numerator == 0)
    {
        return 0;
    }

    const int64 Sign = Numerator < 0 ? -1 : 1;
    const int64 Magnitude = Numerator < 0 ? -Numerator : Numerator;
    int64 Quotient = Magnitude / PositiveDenominator;
    const int64 Remainder = Magnitude % PositiveDenominator;
    if (Remainder * 2 >= PositiveDenominator)
    {
        ++Quotient;
    }
    return Quotient * Sign;
}

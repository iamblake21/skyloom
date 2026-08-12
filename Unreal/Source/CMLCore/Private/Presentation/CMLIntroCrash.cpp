#include "Presentation/CMLIntroCrash.h"

FCMLIntroSkidState FCMLIntroCrash::Touchdown()
{
    FCMLIntroSkidState State;
    State.Speed = TouchdownSpeed;
    State.Travelled = 0.0f;
    State.HullClearance = HullClearance;
    return State;
}

bool FCMLIntroCrash::Advance(FCMLIntroSkidState& State, const float DeltaSeconds)
{
    if (State.HasStopped())
    {
        return false;
    }

    const float Previous = State.Speed;
    const float Delta = FMath::Max(0.0f, DeltaSeconds);

    // Only the part of the frame it is still moving for. Averaging the speed
    // across the *whole* frame is right until the wreck stops partway through
    // one, and then it keeps sliding for the remainder at a speed it no longer
    // has: a single long frame would carry it right past where it should rest.
    const float MovingFor = FMath::Min(Delta, Previous / Friction);
    const float EndSpeed = FMath::Max(0.0f, Previous - Friction * MovingFor);

    // Averaged rather than taken at either end: the starting speed overshoots
    // and the ending speed stops short, and over a slide this long either error
    // puts the wreck somewhere the shot was not framed for.
    State.Travelled += (Previous + EndSpeed) * 0.5f * MovingFor;
    State.Speed = EndSpeed;

    // The hull settles onto the ground as it loses way, so it is ploughing
    // while it moves and resting when it stops.
    State.HullClearance = HullClearance * (State.Speed / TouchdownSpeed);

    return State.HasStopped();
}

float FCMLIntroCrash::PredictedSkidDistance()
{
    // v^2 / 2a: constant deceleration from touchdown to rest.
    return (TouchdownSpeed * TouchdownSpeed) / (2.0f * Friction);
}

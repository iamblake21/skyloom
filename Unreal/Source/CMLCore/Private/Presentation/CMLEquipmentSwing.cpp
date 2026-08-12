#include "Presentation/CMLEquipmentSwing.h"

namespace
{
    constexpr double MetresToUnreal = 100.0;

    /** Unity local (x right, y up, z forward) to Unreal local (X forward, Y right, Z up). */
    FVector FromUnityPosition(const double X, const double Y, const double Z)
    {
        return FVector(Z * MetresToUnreal, X * MetresToUnreal, Y * MetresToUnreal);
    }

    /**
     * Unity euler angles to an Unreal rotator.
     *
     * Yaw carries over; pitch and roll change sign. Both engines are left-handed
     * but they measure those two about opposite senses, so copying the numbers
     * across unchanged would make the pickaxe swing upwards.
     */
    FRotator FromUnityEuler(const double Pitch, const double Yaw, const double Roll)
    {
        return FRotator(-Pitch, Yaw, -Roll);
    }

    // The authored swing poses, in Unreal space.
    const FVector WindupPosition = FromUnityPosition(-0.008, 0.115, -0.055);
    const FRotator WindupRotation = FromUnityEuler(-13.0, 0.0, 3.0);
    const FVector NearContactPosition = FromUnityPosition(-0.022, -0.052, 0.030);
    const FVector FarContactPosition = FromUnityPosition(-0.030, -0.205, 0.125);
    const FRotator NearContactRotation = FromUnityEuler(16.0, 0.0, 2.0);
    const FRotator FarContactRotation = FromUnityEuler(34.0, 0.0, 3.0);
    const FVector MissPosition = FromUnityPosition(-0.105, -0.720, 0.230);
    const FRotator MissRotation = FromUnityEuler(48.0, -3.0, 22.0);
    const FVector ReboundOffset = FromUnityPosition(0.004, 0.062, -0.035);
    const FRotator ReboundRotationOffset = FromUnityEuler(-9.0, 0.0, -1.0);

    float Smooth01(float Value)
    {
        Value = FMath::Clamp(Value, 0.0f, 1.0f);
        return Value * Value * (3.0f - 2.0f * Value);
    }

    float Smoother01(float Value)
    {
        Value = FMath::Clamp(Value, 0.0f, 1.0f);
        return Value * Value * Value * (Value * (Value * 6.0f - 15.0f) + 10.0f);
    }

    float CubicIn(float Value)
    {
        Value = FMath::Clamp(Value, 0.0f, 1.0f);
        return Value * Value * Value;
    }

    float CubicOut(float Value)
    {
        Value = FMath::Clamp(Value, 0.0f, 1.0f);
        const float Inverse = 1.0f - Value;
        return 1.0f - Inverse * Inverse * Inverse;
    }

    /** Rotations are slerped, not lerped per component, as the original did. */
    FRotator SlerpRotation(const FRotator& From, const FRotator& To, const float Alpha)
    {
        return FQuat::Slerp(
            From.Quaternion(), To.Quaternion(), FMath::Clamp(Alpha, 0.0f, 1.0f)).Rotator();
    }

    /**
     * Where the blow ends up.
     *
     * A miss follows through to its own pose. A hit interpolates between the
     * near and far contact poses by how far away the target is: a blow struck at
     * arm's length ends somewhere different from one at the limit of reach, and
     * without this the pickaxe hangs in the air short of what it hit.
     */
    void ResolveTerminalPose(
        const bool bHasTarget,
        const float TargetDistance,
        FVector& OutPosition,
        FRotator& OutRotation)
    {
        if (!bHasTarget)
        {
            OutPosition = MissPosition;
            OutRotation = MissRotation;
            return;
        }
        const float Weight = FMath::GetRangePct(
            FCMLEquipmentSwing::MinimumContactDistanceUnrealUnits,
            FCMLEquipmentSwing::MaximumStrikeDistanceUnrealUnits,
            FMath::Clamp(
                TargetDistance,
                FCMLEquipmentSwing::MinimumContactDistanceUnrealUnits,
                FCMLEquipmentSwing::MaximumStrikeDistanceUnrealUnits));
        OutPosition = FMath::Lerp(NearContactPosition, FarContactPosition, Weight);
        // The original lerps these per component rather than slerping, and the
        // result feeds the slerps above as an authored angle.
        OutRotation = FRotator(
            FMath::Lerp(NearContactRotation.Pitch, FarContactRotation.Pitch, Weight),
            FMath::Lerp(NearContactRotation.Yaw, FarContactRotation.Yaw, Weight),
            FMath::Lerp(NearContactRotation.Roll, FarContactRotation.Roll, Weight));
    }
}

bool FCMLEquipmentSwing::CrossedImpact(
    const float PreviousProgress, const float Progress, const bool bHasTarget)
{
    // A swing that hits nothing has no impact instant at all.
    if (!bHasTarget)
    {
        return false;
    }
    return PreviousProgress < ImpactProgress && Progress >= ImpactProgress;
}

FCMLViewmodelOffset FCMLEquipmentSwing::EvaluatePose(
    const float RawProgress, const bool bHasTarget, const float TargetDistance)
{
    const float Progress = FMath::Clamp(RawProgress, 0.0f, 1.0f);
    FCMLViewmodelOffset Offset;

    // A connecting swing winds up faster and stops dead at contact; a miss is
    // slower and follows through much further, which is what makes a whiff read
    // as a whiff rather than as a shorter hit.
    const float WindupEnd = bHasTarget ? 0.28f : 0.19f;
    if (Progress <= WindupEnd)
    {
        const float Phase = Smooth01(Progress / WindupEnd);
        Offset.Position = FMath::Lerp(FVector::ZeroVector, WindupPosition, Phase);
        Offset.Rotation = SlerpRotation(FRotator::ZeroRotator, WindupRotation, Phase);
        return Offset;
    }

    FVector TerminalPosition;
    FRotator TerminalRotation;
    ResolveTerminalPose(bHasTarget, TargetDistance, TerminalPosition, TerminalRotation);

    const float DescentEnd = bHasTarget ? 0.48f : 0.40f;
    if (Progress <= DescentEnd)
    {
        // Cubic in: the head accelerates into the blow rather than gliding.
        const float Phase = CubicIn((Progress - WindupEnd) / (DescentEnd - WindupEnd));
        Offset.Position = FMath::Lerp(WindupPosition, TerminalPosition, Phase);
        Offset.Rotation = SlerpRotation(WindupRotation, TerminalRotation, Phase);
        return Offset;
    }

    // A brief hold at contact, so the frame the blow lands on is legible.
    const float HoldEnd = bHasTarget ? 0.53f : 0.426f;
    if (Progress <= HoldEnd)
    {
        Offset.Position = TerminalPosition;
        Offset.Rotation = TerminalRotation;
        return Offset;
    }

    const FVector ReboundPosition = TerminalPosition + ReboundOffset;
    const FRotator ReboundRotation = TerminalRotation + ReboundRotationOffset;
    const float ReboundEnd = bHasTarget ? 0.68f : 0.485f;
    if (Progress <= ReboundEnd)
    {
        const float Phase = CubicOut((Progress - HoldEnd) / (ReboundEnd - HoldEnd));
        Offset.Position = FMath::Lerp(TerminalPosition, ReboundPosition, Phase);
        Offset.Rotation = SlerpRotation(TerminalRotation, ReboundRotation, Phase);
        return Offset;
    }

    const float Settle = Smoother01((Progress - ReboundEnd) / (1.0f - ReboundEnd));
    Offset.Position = FMath::Lerp(ReboundPosition, FVector::ZeroVector, Settle);
    Offset.Rotation = SlerpRotation(ReboundRotation, FRotator::ZeroRotator, Settle);
    return Offset;
}

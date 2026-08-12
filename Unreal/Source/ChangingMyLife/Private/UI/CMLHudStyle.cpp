#include "UI/CMLHudStyle.h"

namespace
{
    /**
     * The style sheet's values are sRGB. Converting once here is the whole
     * point: feeding 8-bit sRGB straight into an FLinearColor would wash the
     * HUD out, and doing the conversion at each call site would eventually be
     * forgotten at one of them.
     */
    FLinearColor FromSrgb(const uint8 R, const uint8 G, const uint8 B, const float Alpha = 1.0f)
    {
        FLinearColor Colour = FLinearColor::FromSRGBColor(FColor(R, G, B, 255));
        Colour.A = Alpha;
        return Colour;
    }
}

const FLinearColor& CMLHudStyle::Cream()
{
    static const FLinearColor Value = FromSrgb(242, 227, 192);
    return Value;
}

const FLinearColor& CMLHudStyle::Gold()
{
    static const FLinearColor Value = FromSrgb(215, 165, 45);
    return Value;
}

FLinearColor CMLHudStyle::CreamAlpha(const float Alpha)
{
    FLinearColor Colour = Cream();
    Colour.A = Alpha;
    return Colour;
}

FLinearColor CMLHudStyle::Glass(const float Alpha)
{
    // White, not cream: the glass is neutral and the cream lives in the edges.
    return FLinearColor(1.0f, 1.0f, 1.0f, Alpha);
}

const FLinearColor& CMLHudStyle::Backdrop()
{
    static const FLinearColor Value = FromSrgb(18, 15, 12, 0.52f);
    return Value;
}

const FLinearColor& CMLHudStyle::Invalid()
{
    static const FLinearColor Value = FromSrgb(216, 102, 78, 0.75f);
    return Value;
}

FLinearColor CMLHudStyle::DurabilityFill(const float Durability01)
{
    static const FLinearColor Healthy = FromSrgb(95, 190, 112);
    static const FLinearColor Warning = FromSrgb(224, 188, 84);
    static const FLinearColor Critical = FromSrgb(216, 102, 78);

    const float Wear = FMath::Clamp(Durability01, 0.0f, 1.0f);
    // Two segments, meeting at half: a single lerp from green to red would pass
    // through a muddy brown instead of through yellow.
    return Wear >= 0.5f
        ? FMath::Lerp(Warning, Healthy, (Wear - 0.5f) * 2.0f)
        : FMath::Lerp(Critical, Warning, Wear * 2.0f);
}

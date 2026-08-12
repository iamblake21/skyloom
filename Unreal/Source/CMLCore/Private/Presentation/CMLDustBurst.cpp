#include "Presentation/CMLDustBurst.h"

namespace
{
    TArray<uint8> BuildSpritePixels()
    {
        constexpr int32 Size = FCMLDustBurst::SpriteSize;
        const double Centre = (Size - 1) * 0.5;

        TArray<uint8> Pixels;
        Pixels.SetNumUninitialized(Size * Size * 4);
        for (int32 Y = 0; Y < Size; ++Y)
        {
            for (int32 X = 0; X < Size; ++X)
            {
                const double DeltaX = (X - Centre) / Centre;
                const double DeltaY = (Y - Centre) / Centre;
                const double Distance = FMath::Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
                double Alpha = FMath::Clamp(1.0 - Distance, 0.0, 1.0);
                // Smoothstep, spelled out. A linear ramp still shows an edge.
                Alpha = Alpha * Alpha * (3.0 - 2.0 * Alpha);

                const int32 Offset = (Y * Size + X) * 4;
                Pixels[Offset + 0] = 255;
                Pixels[Offset + 1] = 255;
                Pixels[Offset + 2] = 255;
                Pixels[Offset + 3] = static_cast<uint8>(Alpha * 255.0);
            }
        }
        return Pixels;
    }
}

const TArray<uint8>& FCMLDustBurst::SpritePixels()
{
    static const TArray<uint8> Pixels = BuildSpritePixels();
    return Pixels;
}

FLinearColor FCMLDustBurst::DustTint(const FLinearColor& Source, const float Whiteness)
{
    const FLinearColor Pale = FMath::Lerp(Source, FLinearColor(0.94f, 0.92f, 0.87f), Whiteness);
    // Unity's Color.grayscale, whose weights are its own and not Rec. 709's.
    // Using a different luminance formula would shift every dust colour in the
    // game by a little, which is exactly the kind of drift that is impossible to
    // spot in one screenshot and obvious across a level.
    const float Grey = 0.299f * Pale.R + 0.587f * Pale.G + 0.114f * Pale.B;
    return FLinearColor(
        FMath::Lerp(Pale.R, Grey, 0.35f),
        FMath::Lerp(Pale.G, Grey, 0.35f),
        FMath::Lerp(Pale.B, Grey, 0.35f),
        1.0f);
}

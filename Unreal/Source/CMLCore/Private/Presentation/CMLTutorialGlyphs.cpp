#include "Presentation/CMLTutorialGlyphs.h"

namespace
{
    /**
     * Signed distance to a rounded box: negative inside, zero on the edge.
     * This is what lets one expression describe the mouse's whole silhouette.
     */
    float RoundedBox(const FVector2f& Point, const FVector2f& HalfSize, const float Radius)
    {
        const FVector2f Q(
            FMath::Abs(Point.X) - HalfSize.X + Radius,
            FMath::Abs(Point.Y) - HalfSize.Y + Radius);
        return FMath::Min(FMath::Max(Q.X, Q.Y), 0.0f)
            + FVector2f(FMath::Max(Q.X, 0.0f), FMath::Max(Q.Y, 0.0f)).Size()
            - Radius;
    }

    /**
     * Distance to coverage. The half-pixel band either side of the edge is what
     * antialiases the glyph; a hard threshold would give it a staircase.
     */
    float Coverage(const float SignedDistance)
    {
        return FMath::Clamp(0.5f - SignedDistance, 0.0f, 1.0f);
    }

    /** White with the coverage in alpha, so one tint colours the whole glyph. */
    void WritePixel(TArray<uint8>& Pixels, const int32 Index, const float CoverageValue)
    {
        const int32 Offset = Index * 4;
        Pixels[Offset + 0] = 255;
        Pixels[Offset + 1] = 255;
        Pixels[Offset + 2] = 255;
        Pixels[Offset + 3] = static_cast<uint8>(
            FMath::Clamp(CoverageValue, 0.0f, 1.0f) * 255.0f);
    }

    TArray<uint8> BuildMouse()
    {
        using Glyphs = FCMLTutorialGlyphs;
        constexpr int32 Width = Glyphs::MouseWidth;
        constexpr int32 Height = Glyphs::MouseHeight;
        const FVector2f HalfSize(Width * 0.5f - 7.0f, Height * 0.5f - 7.0f);
        constexpr float Radius = 34.0f;
        constexpr float Stroke = 5.5f;

        TArray<uint8> Pixels;
        Pixels.SetNumUninitialized(Width * Height * 4);
        for (int32 Y = 0; Y < Height; ++Y)
        {
            for (int32 X = 0; X < Width; ++X)
            {
                const FVector2f Point(
                    X - Width * 0.5f + 0.5f,
                    Y - Height * 0.5f + 0.5f);

                // The body is drawn as an outline: the distance to the silhouette
                // taken absolutely, then inset by half the stroke.
                float CoverageValue =
                    Coverage(FMath::Abs(RoundedBox(Point, HalfSize, Radius)) - Stroke * 0.5f);

                // The seam between the two buttons stops at the waist rather
                // than running the full height, which is what makes it read as
                // a mouse and not as a divided pill.
                if (Point.Y > 6.0f && FMath::Abs(Point.X) < HalfSize.X)
                {
                    const float Seam = FMath::Abs(Point.X) - Stroke * 0.35f;
                    const float WithinTop = 6.0f - Point.Y;
                    CoverageValue = FMath::Max(
                        CoverageValue, Coverage(FMath::Max(Seam, WithinTop)));
                }

                // The scroll wheel, a small filled capsule above the waist.
                CoverageValue = FMath::Max(CoverageValue, Coverage(RoundedBox(
                    FVector2f(Point.X, Point.Y - 26.0f), FVector2f(3.4f, 10.0f), 3.4f)));

                WritePixel(Pixels, Y * Width + X, CoverageValue);
            }
        }
        return Pixels;
    }

    TArray<uint8> BuildArrow()
    {
        constexpr int32 Size = FCMLTutorialGlyphs::ArrowSize;
        TArray<uint8> Pixels;
        Pixels.SetNumUninitialized(Size * Size * 4);

        for (int32 Y = 0; Y < Size; ++Y)
        {
            for (int32 X = 0; X < Size; ++X)
            {
                // Normalised, with the vertical centred on zero.
                const float U = (X + 0.5f) / Size;
                const float V = (Y + 0.5f) / Size - 0.5f;

                const float Shaft = FMath::Max(
                    FMath::Abs(V) - 0.085f,
                    FMath::Max(0.10f - U, U - 0.58f));

                // The head is a wedge whose half-width closes to nothing at the
                // tip, which is what gives the arrow a point rather than a blunt
                // end.
                const float HeadSpan = FMath::GetRangePct(0.50f, 0.95f, U);
                const float Head = FMath::Max(
                    FMath::Abs(V) - FMath::Lerp(0.30f, 0.0f, FMath::Clamp(HeadSpan, 0.0f, 1.0f)),
                    FMath::Max(0.50f - U, U - 0.95f));

                WritePixel(Pixels, Y * Size + X,
                    FMath::Max(Coverage(Shaft * Size), Coverage(Head * Size)));
            }
        }
        return Pixels;
    }
}

const TArray<uint8>& FCMLTutorialGlyphs::MouseGlyph()
{
    static const TArray<uint8> Pixels = BuildMouse();
    return Pixels;
}

const TArray<uint8>& FCMLTutorialGlyphs::ArrowGlyph()
{
    static const TArray<uint8> Pixels = BuildArrow();
    return Pixels;
}

float FCMLTutorialGlyphs::ScaleForScreenHeight(const float ScreenHeight)
{
    // Authored against 1080p, and clamped: below the lower bound the card stops
    // being readable, above the upper one it dominates the shot instead of
    // teaching it.
    return FMath::Clamp(ScreenHeight / 1080.0f, 0.6f, 2.2f);
}

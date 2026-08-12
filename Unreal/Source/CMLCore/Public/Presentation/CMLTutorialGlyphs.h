#pragma once

#include "CoreMinimal.h"

/**
 * The teaching card's glyphs, ported from
 * CML.Unity.Presentation.Intro.IntroTutorialPrompt.
 *
 * The opening asks the player to fly for themselves, and shows a mouse and an
 * arrow to say how. Both are rasterised at runtime from signed distance fields
 * rather than imported: the prompt then needs no sprite, no import settings and
 * no reference that can go missing from a build, and it stays crisp at any
 * screen size because the field is evaluated per pixel rather than scaled.
 *
 * Pixels come out as RGBA8, white with coverage in the alpha, so a single tint
 * at draw time colours the whole glyph.
 */
class CMLCORE_API FCMLTutorialGlyphs
{
public:
    static constexpr int32 MouseWidth = 84;
    static constexpr int32 MouseHeight = 128;
    static constexpr int32 ArrowSize = 72;

    /** The mouse: an outlined body, the seam between its buttons, and a wheel. */
    static const TArray<uint8>& MouseGlyph();

    /** The arrow: a shaft and a wedge head that closes to a point. */
    static const TArray<uint8>& ArrowGlyph();

    /**
     * Screen height to glyph scale, clamped at both ends.
     *
     * Below the lower bound the card stops being readable; above the upper one
     * it starts to dominate the shot instead of teaching it.
     */
    static float ScaleForScreenHeight(float ScreenHeight);
};

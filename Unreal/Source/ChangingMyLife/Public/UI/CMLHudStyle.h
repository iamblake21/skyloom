#pragma once

#include "CoreMinimal.h"

/**
 * The HUD's look, transcribed from `Art/UI/Inventory/InventoryHUD.uss`.
 *
 * The style sheet states its own rules in its header, and they are the reason
 * this file exists rather than colours being chosen at each call site:
 *
 *  - the panel stays almost invisible; only the slots carry the milky white
 *    glass;
 *  - edges are 1 px hairlines, never chunky borders;
 *  - no filled badges — numbers sit on the glass with a text outline;
 *  - exactly one accent, the game's warm gold, used only for selection.
 *
 * Cream and gold come from the game's canonical palette. The USS values are
 * sRGB, so they are converted once here: passing 8-bit values straight into an
 * `FLinearColor` would wash the whole HUD out.
 */
namespace CMLHudStyle
{
    /** #F2E3C0 — the cream every hairline and label is tinted from. */
    CHANGINGMYLIFE_API const FLinearColor& Cream();
    /** #D7A52D — the one accent, and only for selection. */
    CHANGINGMYLIFE_API const FLinearColor& Gold();

    /** Cream at a given opacity, for hairlines and muted text. */
    CHANGINGMYLIFE_API FLinearColor CreamAlpha(float Alpha);
    /** The milky glass the slots are made of. */
    CHANGINGMYLIFE_API FLinearColor Glass(float Alpha);

    // Metrics, in the style sheet's own pixels at a 1080p reference height.
    constexpr float SlotSize = 62.0f;
    constexpr float HotbarSlotSize = 58.0f;
    constexpr float HotbarSlotMargin = 5.0f;
    constexpr float SlotGap = 6.0f;
    constexpr float HairlineThickness = 1.0f;
    constexpr float HotbarBottomMargin = 30.0f;

    // Slot fills: empty, holding something, and selected.
    constexpr float SlotGlassAlpha = 0.20f;
    constexpr float OccupiedGlassAlpha = 0.24f;
    constexpr float SelectedGlassAlpha = 0.28f;
    constexpr float HotbarGlassAlpha = 0.18f;

    // Hairline opacities. The top edge is brightest and the bottom faintest,
    // which is what reads as a lit pane rather than a drawn box.
    constexpr float EdgeSideAlpha = 0.14f;
    constexpr float EdgeTopAlpha = 0.20f;
    constexpr float EdgeBottomAlpha = 0.09f;
    constexpr float OccupiedEdgeTopAlpha = 0.26f;

    /** The panel itself is nearly invisible: the slots carry the design. */
    constexpr float PanelGlassAlpha = 0.04f;
    constexpr float PanelEdgeAlpha = 0.13f;

    /** rgba(18, 15, 12, 0.52) — the scene dimmed behind an open panel. */
    CHANGINGMYLIFE_API const FLinearColor& Backdrop();

    /** rgba(216, 102, 78, …) — a refused slot, the only other coloured state. */
    CHANGINGMYLIFE_API const FLinearColor& Invalid();

    /** The durability bar runs green through yellow to red as wear increases. */
    CHANGINGMYLIFE_API FLinearColor DurabilityFill(float Durability01);
}

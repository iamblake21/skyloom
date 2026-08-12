#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"

#include "CMLDrillExtraction.generated.h"

/** Why a drill could not be placed, so the hologram can say which. */
UENUM(BlueprintType)
enum class ECMLDrillPlacementFailure : uint8
{
    None = 0,
    /** The deposit names an ore the catalog does not define. */
    UnknownOre = 1,
    /** The drill itself is missing from the catalog. */
    DrillMissing = 2,
    /** No extraction recipe produces that ore. */
    NoExtractionForOre = 3
};

/**
 * Which extraction recipe belongs to the deposit a drill is standing on, ported
 * from CML.Unity.Factory.MechanicalDrillPlacement.
 *
 * There is no deposit-to-ore table anywhere, and there deliberately is not one:
 * **the drill's supported recipe list is the mapping.** Each extraction recipe
 * has exactly one product, so the recipe for a deposit is the one whose product
 * is the item that deposit yields. Adding an ore is a catalog edit, not a code
 * edit.
 *
 * Finding the deposit itself needs a world and stays in the game module. This is
 * the half that decides what the drill would then produce.
 */
class CMLCORE_API FCMLDrillExtraction
{
    public:
    /**
     * How far from the machine centre the deposit's working surface may sit.
     * The surface trigger is built from the deposit's own footprint and the
     * drill straddles it, so the centres do not coincide exactly.
     */
    static constexpr double DepositSearchRadiusUnrealUnits = 75.0;

    /**
     * Resolves the recipe for an ore named by its content key.
     *
     * Refuses rather than falling back: an unknown ore, a missing drill or an
     * ore no extraction produces all reject the placement, because the
     * alternative is silently extracting something else.
     */
    static bool TryResolveExtraction(
        const FCMLGameCatalog& Catalog,
        const FString& DepositOreItemKey,
        FCMLStableId& OutRecipeId,
        ECMLDrillPlacementFailure& OutFailure);
};

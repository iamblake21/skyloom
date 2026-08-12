#pragma once

#include "CoreMinimal.h"
#include "Inventory/CMLInventoryOperations.h"
#include "CMLCraftingRule.generated.h"

/** Why a craft was refused, ported from CML.Inventory.CraftingFailure. */
UENUM(BlueprintType)
enum class ECMLCraftingFailure : uint8
{
    None = 0,
    UnknownRecipe = 1,
    WrongStation = 2,
    InvalidQuantity = 3,
    InsufficientIngredients = 4,
    InventoryFull = 5,
    InvalidDefinition = 6,
    ArithmeticOverflow = 7,
    AuthorityBusy = 8
};

/** Where a recipe may be crafted, ported from CML.Content.CraftingStationKind. */
UENUM(BlueprintType)
enum class ECMLCraftingStationKind : uint8
{
    None = 0,
    Personal = 1,
    Workbench = 2,
    Machine = 3
};

/** One side of a recipe: how much of which item. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLRecipeAmount
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    FCMLStableId ItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    int64 Quantity = 0;
};

/**
 * What kind of thing a recipe makes, ported from CML.Content.RecipeCategory.
 *
 * `Extraction` is not just a label for the HUD. It produces from a geological
 * source instead of from ingredients, and is the only category allowed to
 * declare no inputs. Stating it explicitly is what stops "no ingredients" from
 * quietly becoming a way to author a broken recipe.
 */
UENUM(BlueprintType)
enum class ECMLRecipeCategory : uint8
{
    None = 0,
    Tools = 1,
    Materials = 2,
    Structures = 3,
    Logistics = 4,
    Machinery = 5,
    Extraction = 6
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLRecipeDefinition
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    FCMLStableId RecipeId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    ECMLCraftingStationKind Station = ECMLCraftingStationKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    TArray<FCMLRecipeAmount> Inputs;

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    TArray<FCMLRecipeAmount> Outputs;

    /** How long one machine cycle of this recipe takes; zero for hand crafts. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    int64 DurationMilliseconds = 0;

    // Last so the fields fixtures set positionally stay leading.
    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    ECMLRecipeCategory Category = ECMLRecipeCategory::Materials;

    UPROPERTY(BlueprintReadOnly, Category="CML|Crafting")
    FCMLDefinitionIdentity Identity;

    bool IsExtraction() const { return Category == ECMLRecipeCategory::Extraction; }
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLRecipeCatalog
{
    GENERATED_BODY()

    UPROPERTY()
    TArray<FCMLRecipeDefinition> Recipes;

    bool TryGetRecipe(const FCMLStableId& RecipeId, FCMLRecipeDefinition& OutRecipe) const;
};

/**
 * Crafting, ported from CML.Inventory.CraftingRule.
 *
 * A craft is one transaction: inputs are taken and outputs stored against a
 * working copy, and the caller's inventory is replaced only if every step
 * succeeded. A craft that produces more than the inventory can hold consumes
 * nothing — the ingredients are not spent on an output with nowhere to go.
 */
class CMLCORE_API FCMLCraftingRule
{
public:
    static bool TryCraft(
        const FCMLInventoryState& Inventory,
        const FCMLItemCatalog& Items,
        const FCMLRecipeCatalog& Recipes,
        const FCMLStableId& RecipeId,
        ECMLCraftingStationKind Station,
        int64 CraftCount,
        int64 Capacity,
        FCMLInventoryState& OutUpdated,
        ECMLCraftingFailure& OutFailure);
};

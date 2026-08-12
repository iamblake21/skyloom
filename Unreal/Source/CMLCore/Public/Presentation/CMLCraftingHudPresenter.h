#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Presentation/CMLInventoryHudPresenter.h"

#include "CMLCraftingHudPresenter.generated.h"

/** One ingredient of a recipe: how it looks, how much is needed, how much is held. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLCraftingIngredientPresentation
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    FCMLInventorySlotPresentation Item;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") int64 Owned = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") int64 Required = 0;

    bool IsAvailable() const { return Owned >= Required; }
};

/** One recipe as the crafting panel shows it. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLCraftingRecipePresentation
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FCMLStableId RecipeId;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString DisplayName;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString Description;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    ECMLRecipeCategory Category = ECMLRecipeCategory::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    FCMLInventorySlotPresentation Output;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    TArray<FCMLCraftingIngredientPresentation> Ingredients;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") int64 CraftCount = 1;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") bool bCanCraft = false;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    ECMLCraftingFailure Failure = ECMLCraftingFailure::None;
};

/**
 * Projects one recipe for the crafting panel, ported from
 * CML.Unity.Presentation.Crafting.CraftingHudPresenter.
 *
 * Whether the craft is possible is not decided here: the panel asks
 * `FCMLCraftingRule::TryCraft` against a working copy and reports what it says.
 * A presenter that judged for itself would eventually disagree with the rule
 * that actually runs, and the button would lie.
 */
class CMLCORE_API FCMLCraftingHudPresenter
{
public:
    static bool TryProject(
        const FCMLInventoryState& Inventory,
        const FCMLGameCatalog& Catalog,
        const FCMLRecipeDefinition& Recipe,
        int64 CraftCount,
        int64 Capacity,
        FCMLCraftingRecipePresentation& OutPresentation);

    /** The tab a recipe belongs under. */
    static FString CategoryLabel(ECMLRecipeCategory Category);
};

#include "Content/CMLDrillExtraction.h"

#include "Content/CMLContentIds.h"

bool FCMLDrillExtraction::TryResolveExtraction(
    const FCMLGameCatalog& Catalog,
    const FString& DepositOreItemKey,
    FCMLStableId& OutRecipeId,
    ECMLDrillPlacementFailure& OutFailure)
{
    OutRecipeId = FCMLStableId::None();
    OutFailure = ECMLDrillPlacementFailure::None;

    // Content keys are compared ordinally, as everywhere else: a case-insensitive
    // match would let two keys that the catalog treats as distinct resolve to the
    // same item.
    FCMLStableId OreItemId = FCMLStableId::None();
    for (const FCMLItemDefinition& Item : Catalog.Items)
    {
        if (Item.Identity.Key.Equals(DepositOreItemKey, ESearchCase::CaseSensitive))
        {
            OreItemId = Item.ItemId;
            break;
        }
    }
    if (OreItemId.IsNone())
    {
        OutFailure = ECMLDrillPlacementFailure::UnknownOre;
        return false;
    }

    FCMLMachineDefinition Drill;
    if (!Catalog.TryGetMachine(CMLContentIds::MechanicalDrill, Drill))
    {
        OutFailure = ECMLDrillPlacementFailure::DrillMissing;
        return false;
    }

    for (const FCMLStableId& CandidateId : Drill.SupportedRecipeIds)
    {
        FCMLRecipeDefinition Candidate;
        if (!Catalog.TryGetRecipe(CandidateId, Candidate)
            || !Candidate.IsExtraction()
            // Exactly one product: a recipe with two would make "the ore this
            // deposit yields" ambiguous, and the mapping rests on it being not.
            || Candidate.Outputs.Num() != 1)
        {
            continue;
        }
        if (Candidate.Outputs[0].ItemId == OreItemId)
        {
            OutRecipeId = CandidateId;
            return true;
        }
    }

    OutFailure = ECMLDrillPlacementFailure::NoExtractionForOre;
    return false;
}

#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Simulation/CMLMachineState.h"

#include "CMLMachineDiagnostics.generated.h"

/**
 * Stable keys for why a node is not producing.
 *
 * They are keys and not prose: the simulation and its diagnostics stay
 * language-neutral, and the Italian text lives in the presenter, as it already
 * does for item names.
 */
namespace CMLMachineCauseKeys
{
    inline const TCHAR* Running = TEXT("machine.cause.running");
    inline const TCHAR* NoWork = TEXT("machine.cause.no_work");
    inline const TCHAR* NoRecipe = TEXT("machine.cause.no_recipe");
    inline const TCHAR* MissingInput = TEXT("machine.cause.missing_input");
    inline const TCHAR* MissingFuel = TEXT("machine.cause.missing_fuel");
    inline const TCHAR* OutputFull = TEXT("machine.cause.output_full");

    /** Every activity names a cause; an unnamed one is a programming error. */
    CMLCORE_API FString For(ECMLMachineActivity Activity);
}

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineSlotReport
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int32 SlotIndex = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FCMLStableId ItemId;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FString ItemKey;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 Quantity = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 MaxStack = 0;

    bool IsEmpty() const { return ItemId.IsNone(); }
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachinePortReport
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics")
    ECMLMachinePortKind Kind = ECMLMachinePortKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 TotalQuantity = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") TArray<FCMLMachineSlotReport> Slots;
};

/** What a recipe still needs, and how much of it the input port holds. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineShortfallReport
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FCMLStableId ItemId;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FString ItemKey;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 Required = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 Present = 0;

    int64 Missing() const { return Required > Present ? Required - Present : 0; }
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineNodeReport
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FCMLStableId NodeId;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics")
    ECMLMachineNodeKind Kind = ECMLMachineNodeKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FString DefinitionKey;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FString RecipeKey;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics")
    ECMLMachineActivity Activity = ECMLMachineActivity::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") FString CauseKey;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 ProgressMilliseconds = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 DurationMilliseconds = 0;

    /**
     * Progress in thousandths, computed with integer arithmetic. A progress bar
     * that divided the authoritative milliseconds itself would be the one place
     * where a float touched a simulated value.
     */
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int32 ProgressPermille = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") bool bIsCycleActive = false;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 CompletedCycles = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics")
    ECMLBeltLineStatus BeltLineStatus = ECMLBeltLineStatus::NotApplicable;

    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 BeltLineUsedCapacity = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") int64 BeltLineAvailableCapacity = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics") TArray<FCMLMachinePortReport> Ports;

    /**
     * Empty unless the activity is MissingInput or MissingFuel. "Missing input"
     * alone does not say which input, and the difference is what the player has
     * to act on.
     */
    UPROPERTY(BlueprintReadOnly, Category="CML|Diagnostics")
    TArray<FCMLMachineShortfallReport> Shortfalls;

    bool IsBlocked() const
    {
        return Activity != ECMLMachineActivity::Running
            && Activity != ECMLMachineActivity::Idle;
    }
};

/**
 * Read-only projection of one node of the machine graph, ported from
 * CML.Diagnostics.MachineDiagnostics.
 *
 * It never mutates and never infers: every field either comes from the
 * authoritative state or is derived from it and from the validated catalog.
 */
class CMLCORE_API FCMLMachineDiagnostics
{
public:
    static bool TryDescribe(
        const FCMLMachineSimulationState& Machines,
        const FCMLGameCatalog& Catalog,
        const FCMLStableId& NodeId,
        FCMLMachineNodeReport& OutReport);

    /** Every node, in the graph's own id order. */
    static TArray<FCMLMachineNodeReport> DescribeAll(
        const FCMLMachineSimulationState& Machines,
        const FCMLGameCatalog& Catalog);

    static FCMLMachineNodeReport Describe(
        const FCMLMachineNodeState& Node,
        const FCMLGameCatalog& Catalog);
};

#pragma once

#include "CoreMinimal.h"
#include "Diagnostics/CMLMachineDiagnostics.h"
#include "Presentation/CMLInventoryHudPresenter.h"

#include "CMLMachineHudPresenter.generated.h"

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachinePortPresentation
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    ECMLMachinePortKind Kind = ECMLMachinePortKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString Title;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") int64 TotalQuantity = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    TArray<FCMLInventorySlotPresentation> Slots;
};

USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineUiSnapshot
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FCMLStableId NodeId;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    ECMLMachineNodeKind Kind = ECMLMachineNodeKind::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString DefinitionKey;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString Title;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString RecipeName;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString CauseText;

    /** Empty unless the machine is short of an input or of fuel. */
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") FString ShortfallText;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD")
    ECMLMachineActivity Activity = ECMLMachineActivity::None;

    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") bool bIsBlocked = false;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") int32 ProgressPermille = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") int64 CompletedCycles = 0;
    UPROPERTY(BlueprintReadOnly, Category="CML|HUD") TArray<FCMLMachinePortPresentation> Ports;

    /** Progress as a percentage string for the bar's label. */
    FString ProgressText() const { return FString::FromInt(ProgressPermille / 10) + TEXT("%"); }
};

/**
 * Turns the authoritative diagnostic report of one node into presentation data,
 * ported from CML.Unity.Presentation.Machines.MachineHudPresenter.
 *
 * It never stores, moves or starts anything: like the inventory presenter it is
 * a pure projection, and it is the layer where the Italian text lives — the
 * simulation and its diagnostics stay language-neutral.
 *
 * Item appearance comes from `FCMLInventoryHudPresenter::ProjectSlot`, so a
 * plate in a press looks exactly like a plate in the backpack.
 */
class CMLCORE_API FCMLMachineHudPresenter
{
public:
    static FCMLMachineUiSnapshot Project(
        const FCMLMachineNodeReport& Report,
        const FCMLGameCatalog& Catalog);

    /**
     * Italian for a cause. Every activity has an entry: a machine that showed an
     * empty string here would read as merely stopped, which is the one thing the
     * cause line exists to prevent.
     */
    static FString CauseText(ECMLMachineActivity Activity);

    static FString PortTitle(ECMLMachinePortKind Kind);

    static FString BeltLineText(const FCMLMachineNodeReport& Report);
};

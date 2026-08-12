#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "Presentation/CMLCraftingHudPresenter.h"
#include "Presentation/CMLInventoryHudPresenter.h"
#include "Presentation/CMLMachineHudPresenter.h"
#include "CMLHUD.generated.h"

class UCMLSimulationSubsystem;

/**
 * Runtime HUD for the migrated game.
 *
 * The presenters remain pure CMLCore projections. This class is only their
 * Unreal drawing boundary: it never owns or mutates simulation state.
 *
 * Its look is transcribed from the original `InventoryHUD.uss` through
 * `CMLHudStyle`, and follows the three rules that style sheet states about
 * itself: the panel stays almost invisible, edges are one-pixel hairlines, and
 * numbers sit on the glass with an outline rather than on filled badges.
 */
UCLASS()
class CHANGINGMYLIFE_API ACMLHUD final : public AHUD
{
    GENERATED_BODY()

public:
    ACMLHUD();

    virtual void DrawHUD() override;
    virtual void NotifyHitBoxClick(FName BoxName) override;
    virtual void NotifyHitBoxRelease(FName BoxName) override;

    UFUNCTION(BlueprintCallable, Category="CML|HUD")
    void ToggleInventory();

    UFUNCTION(BlueprintCallable, Category="CML|HUD")
    void SetInventoryVisible(bool bVisible);

    UFUNCTION(BlueprintPure, Category="CML|HUD")
    bool IsInventoryVisible() const { return bInventoryVisible; }

    bool IsAnyPanelOpen() const
    {
        return bInventoryVisible || bMachineVisible || bChestVisible
            || bWorkbenchVisible || bRepairVisible;
    }

    /** Which hotbar slot the player is holding; the only selected slot shown. */
    UFUNCTION(BlueprintCallable, Category="CML|HUD")
    void SetSelectedHotbarIndex(const int32 Index) { SelectedHotbarIndex = Index; }

    UFUNCTION(BlueprintPure, Category="CML|HUD")
    int32 GetSelectedHotbarIndex() const { return SelectedHotbarIndex; }

    /** The one prompt owned by the central interactor. */
    void SetInteractionPrompt(const FText& Prompt, const FVector& WorldLocation);
    void ClearInteractionPrompt();

    /** Persistent crosshair feedback owned by the placement controller. */
    void SetBuildPlacementStatus(
        bool bVisible,
        const FString& Headline,
        const FString& Controls,
        bool bValid);

    /** Receives an already-projected machine panel from the interaction layer. */
    void ShowMachine(const FCMLMachineUiSnapshot& Snapshot);
    void OpenMachineNode(const FCMLStableId& NodeId);
    void HideMachine();

    /**
     * Opens the crate panel: the crate on the left, the backpack on the right.
     *
     * `StatusText` reports a refusal by its cause and is left empty when
     * nothing was refused â€” saying "ready" there would be noise.
     */
    void ShowChest(
        const FString& Title,
        const TArray<FCMLInventorySlotPresentation>& CrateSlots,
        const FString& StatusText);
    void HideChest();
    void OpenStorageNode(const FCMLStableId& NodeId, const FString& Title);
    bool GetActiveTransferNode(FCMLStableId& OutNodeId) const;

    /** A short-lived line naming what was just picked up. */
    void PushCollectionFeed(const FString& Line);
    void PushCollectedItem(
        const FCMLStableId& ItemId,
        const FString& DisplayName,
        ECMLInventoryIconKind IconKind,
        int64 Quantity);

    /**
     * The opening's teaching card. `Direction` is +1 to ask for a right turn
     * and -1 for a left one.
     */
    void SetTutorialCard(bool bVisible, float Direction);

    /** Arrival runs in the gameplay map but still owns the whole frame. */
    void SetCinematicSuppressed(bool bSuppressed) { bCinematicSuppressed = bSuppressed; }

    /** Full-frame flash/fade and the top/bottom eyelids used by the opening. */
    void SetCinematicOverlay(float FlashAlpha, float FadeAlpha, float Eyelid);

    /**
     * Opens the workbench: the recipe list on the left, the selected recipe's
     * detail on the right.
     */
    void ShowWorkbench(
        const TArray<FCMLCraftingRecipePresentation>& Recipes, int32 SelectedIndex);
    void OpenCraftingPanel(const FString& Title, ECMLCraftingStationKind Station);
    void HideWorkbench();
    void SelectWorkbenchRecipe(int32 Index);
    void StepWorkbenchRecipe(int32 Delta);
    bool GetSelectedCraftingRecipe(
        FCMLStableId& OutRecipeId,
        ECMLCraftingStationKind& OutStation) const;
    bool IsCraftingVisible() const { return bWorkbenchVisible; }
    void CloseInteractionPanels();

    /** One component the repair still wants. */
    struct FRepairRequirement
    {
        FCMLInventorySlotPresentation Item;
        int64 Owned = 0;
        int64 Required = 0;

        bool IsMet() const { return Owned >= Required; }
    };

    /**
     * Opens the airship repair panel.
     *
     * `CauseText` names why the repair cannot proceed and is left empty when it
     * can, in the same spirit as the crate's status line.
     */
    void ShowAirshipRepair(
        const TArray<FRepairRequirement>& Requirements,
        int32 ProgressPermille,
        const FString& CauseText);
    void OpenAirshipRepair(const FCMLStableId& AirshipId);
    bool GetActiveRepairAirship(FCMLStableId& OutAirshipId) const;
    void HideAirshipRepair();

    /** Keeps camera capture and cursor state in step with Unity's modal HUD. */
    void ApplyInputMode();

private:
    /** Style-sheet pixels to screen pixels, referenced to a 1080p height. */
    float Scale() const;

    void DrawCrosshair();
    void DrawInteractionPrompt();
    void DrawBuildPlacement();
    void DrawHotbar(const FCMLInventoryUiSnapshot& Snapshot);
    void DrawInventory(const FCMLInventoryUiSnapshot& Snapshot);
    void DrawQuickCrafting();
    void DrawMachine(const FCMLInventoryUiSnapshot& Player);
    void DrawChest(const FCMLInventoryUiSnapshot& Player);
    void DrawCollectionFeed();
    void DrawTutorialCard();
    void DrawCinematicOverlay();

    /** A scalable face for the cinematic cards, cached on first use. */
    class UFont* CardFont();
    UPROPERTY() TObjectPtr<class UFont> CachedCardFont;

    /** The 12 px UI Toolkit face used by Unity's inventory and world HUD. */
    class UFont* HudFont();
    UPROPERTY() TObjectPtr<class UFont> CachedHudFont;

    /** Turns a ported glyph's coverage bytes into a drawable texture, once. */
    class UTexture2D* GlyphTexture(
        const TArray<uint8>& Coverage, int32 Width, int32 Height,
        TObjectPtr<class UTexture2D>& Cache);
    UPROPERTY() TObjectPtr<class UTexture2D> CachedMouseGlyph;
    UPROPERTY() TObjectPtr<class UTexture2D> CachedArrowGlyph;
    void DrawWorkbench();
    void RebuildCraftingRecipes(const UCMLSimulationSubsystem& Simulation);
    void RebuildAirshipRepair(const UCMLSimulationSubsystem& Simulation);
    void DrawAirshipRepair();

    enum class EInventorySlotArea : uint8
    {
        None,
        Player,
        Chest,
        MachineInput,
        MachineFuel,
        MachineOutput
    };

    void HandleInventorySlotClick(EInventorySlotArea Area, int32 Index);
    bool IsInventorySlotOccupied(EInventorySlotArea Area, int32 Index) const;
    bool TryGetInventorySlot(
        EInventorySlotArea Area,
        int32 Index,
        FCMLInventorySlotPresentation& OutSlot) const;
    const FCMLMachinePortPresentation* FindMachinePort(
        ECMLMachinePortKind Kind) const;
    static ECMLMachinePortKind MachinePortForArea(EInventorySlotArea Area);
    static EInventorySlotArea AreaForMachinePort(ECMLMachinePortKind Kind);
    bool QuickMovePlayerSlot(int32 SourceIndex, const FCMLInventoryUiSnapshot& Inventory);
    void DrawHeldInventoryStack();
    FCMLInventorySlotPresentation SlotWithHeldQuantityRemoved(
        EInventorySlotArea Area,
        int32 Index,
        const FCMLInventorySlotPresentation& Slot) const;
    void ResetHeldInventorySlot();

    /** A grid of slots that wraps, returning the height it used. */
    float DrawSlotGrid(
        const TArray<FCMLInventorySlotPresentation>& Slots,
        float X, float Y, float AvailableWidth, float SlotPixels,
        EInventorySlotArea Area);

    /** A one-pixel rim, brighter on top and faintest at the bottom. */
    void DrawHairlineBox(
        float X, float Y, float Width, float Height,
        float SideAlpha, float TopAlpha, float BottomAlpha);

    /** Text with an outline and no badge behind it. */
    void DrawOutlinedText(
        const FString& Text,
        const FLinearColor& Colour,
        float X,
        float Y,
        float TextScale,
        class UFont* FontOverride = nullptr);

    void DrawSlot(
        const FCMLInventorySlotPresentation& Slot,
        float X,
        float Y,
        float Size,
        bool bSelected,
        float BaseGlassAlpha);

    static class UTexture2D* ResolveIconTexture(ECMLInventoryIconKind IconKind);

    UPROPERTY(Transient)
    bool bInventoryVisible = false;

    UPROPERTY(Transient)
    int32 SelectedHotbarIndex = 0;

    FText InteractionPrompt;
    FVector InteractionPromptWorldLocation = FVector::ZeroVector;
    bool bInteractionPromptVisible = false;

    bool bBuildPlacementVisible = false;
    bool bBuildPlacementValid = false;
    FString BuildPlacementHeadline;
    FString BuildPlacementControls;

    bool bMachineVisible = false;
    FCMLMachineUiSnapshot MachineSnapshot;
    FCMLStableId ActiveMachineNodeId;

    bool bChestVisible = false;
    FString ChestTitle;
    FString ChestStatus;
    TArray<FCMLInventorySlotPresentation> ChestSlots;
    FCMLStableId ActiveStorageNodeId;

    enum class ECollectionFeedPhase : uint8
    {
        Entering,
        Holding,
        Exiting
    };

    struct FCollectionFeedEntry
    {
        FCMLStableId ItemId;
        FString DisplayName;
        ECMLInventoryIconKind IconKind = ECMLInventoryIconKind::Generic;
        int64 Quantity = 0;
        ECollectionFeedPhase Phase = ECollectionFeedPhase::Entering;
        double PhaseStartedAt = 0.0;
        double PopStartedAt = -1000.0;
    };

    /** Unity-style per-item entries on the right; repeats aggregate and pop. */
    TArray<FCollectionFeedEntry> CollectionEntries;

    /** Non-collection status messages keep their old, separate toast. */
    FString CollectionLine;
    double CollectionShownAt = -1000.0;

    EInventorySlotArea HeldInventoryArea = EInventorySlotArea::None;
    int32 HeldInventorySlot = INDEX_NONE;
    int64 HeldInventoryQuantity = 0;
    FCMLInventorySlotPresentation HeldInventoryItem;

    /** Prevents Unreal's ignore-input counters from being incremented repeatedly. */
    bool bModalInputApplied = false;

    bool bTutorialCardVisible = false;
    bool bCinematicSuppressed = false;
    float TutorialDirection = 0.0f;
    /** Rises and falls so the card fades in and out rather than blinking. */
    float TutorialAlpha = 0.0f;
    float CinematicFlashAlpha = 0.0f;
    float CinematicFadeAlpha = 0.0f;
    float CinematicEyelid = 0.0f;

    bool bWorkbenchVisible = false;
    int32 WorkbenchSelection = 0;
    FString CraftingTitle = TEXT("B A N C O   D A   L A V O R O");
    ECMLCraftingStationKind CraftingStation = ECMLCraftingStationKind::Workbench;
    TArray<FCMLCraftingRecipePresentation> WorkbenchRecipes;

    bool bRepairVisible = false;
    int32 RepairProgressPermille = 0;
    FString RepairCause;
    TArray<FRepairRequirement> RepairRequirements;
    FCMLStableId ActiveRepairAirshipId;
};

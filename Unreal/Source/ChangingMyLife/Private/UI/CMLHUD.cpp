#include "UI/CMLHUD.h"

#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/Font.h"
#include "Engine/Texture2D.h"
#include "GameFramework/PlayerController.h"
#include "Simulation/CMLSimulationSubsystem.h"
#include "Content/CMLContentIds.h"
#include "Presentation/CMLTutorialGlyphs.h"
#include "UI/CMLHudStyle.h"
#include "HAL/PlatformTime.h"
#include "InputCoreTypes.h"
#include "Misc/Paths.h"

namespace
{
    using namespace CMLHudStyle;

    /** The height the style sheet's pixel values were authored against. */
    constexpr float ReferenceHeight = 1080.0f;

    /** A slot's corner radius, as a fraction of its size (13 px of 62). */
    constexpr float CornerFraction = 13.0f / 62.0f;
}

ACMLHUD::ACMLHUD()
{
    PrimaryActorTick.bCanEverTick = false;
}

float ACMLHUD::Scale() const
{
    // The style sheet is authored in pixels at 1080p. Scaling by height keeps
    // the HUD the same size relative to the screen instead of shrinking to
    // nothing on a tall display.
    return Canvas != nullptr ? FMath::Max(0.5f, Canvas->ClipY / ReferenceHeight) : 1.0f;
}

void ACMLHUD::DrawHUD()
{
    Super::DrawHUD();
    if (Canvas == nullptr)
    {
        return;
    }

    // The opening owns the whole frame. In Unity its tutorial card was the
    // only HUD element; drawing the hotbar and interaction layer here made the
    // cinematic look like gameplay running behind a broken overlay.
    if (const UWorld* World = GetWorld(); bCinematicSuppressed
        || (World != nullptr && World->GetMapName().Contains(TEXT("A_01_IntroCinematic"))))
    {
        DrawTutorialCard();
        DrawCinematicOverlay();
        return;
    }

    FCMLInventoryUiSnapshot Inventory;
    bool bHasInventory = false;
    const UCMLSimulationSubsystem* Simulation = nullptr;
    if (const UWorld* World = GetWorld())
    {
        Simulation = World->GetSubsystem<UCMLSimulationSubsystem>();
        bHasInventory =
            Simulation != nullptr && Simulation->GetPlayerInventoryPresentation(Inventory);
    }

    // The scene is dimmed only while a panel is open, and the reticle is hidden
    // then: aiming and reading are different modes.
    const bool bPanelOpen =
        bInventoryVisible || bMachineVisible || bChestVisible
        || bWorkbenchVisible || bRepairVisible;
    if (bPanelOpen)
    {
        DrawRect(Backdrop(), 0.0f, 0.0f, Canvas->ClipX, Canvas->ClipY);
    }
    else
    {
        DrawCrosshair();
    }

    if (bHasInventory)
    {
        if (bInventoryVisible && Simulation != nullptr)
        {
            CraftingStation = ECMLCraftingStationKind::Personal;
            RebuildCraftingRecipes(*Simulation);
        }
        if (bWorkbenchVisible && Simulation != nullptr)
        {
            RebuildCraftingRecipes(*Simulation);
        }
        if (bMachineVisible && Simulation != nullptr
            && !ActiveMachineNodeId.IsNone())
        {
            Simulation->GetMachinePresentation(ActiveMachineNodeId, MachineSnapshot);
        }
        if (bChestVisible && Simulation != nullptr
            && !ActiveStorageNodeId.IsNone())
        {
            Simulation->GetBufferPresentation(ActiveStorageNodeId, ChestSlots);
        }
        if (bRepairVisible && Simulation != nullptr
            && !ActiveRepairAirshipId.IsNone())
        {
            RebuildAirshipRepair(*Simulation);
        }
        DrawHotbar(Inventory);
        // One panel at a time. The crate already shows the backpack in its
        // right column, so drawing the standalone inventory behind it would be
        // a second copy of the same grid on screen.
        if (bRepairVisible)
        {
            DrawAirshipRepair();
        }
        else if (bWorkbenchVisible)
        {
            DrawWorkbench();
        }
        else if (bChestVisible)
        {
            DrawChest(Inventory);
        }
        else if (bInventoryVisible)
        {
            DrawInventory(Inventory);
        }
    }

    if (bMachineVisible && bHasInventory)
    {
        DrawMachine(Inventory);
    }

    DrawCollectionFeed();
    DrawBuildPlacement();
    DrawTutorialCard();
    DrawHeldInventoryStack();
}

void ACMLHUD::SetBuildPlacementStatus(
    const bool bVisible,
    const FString& Headline,
    const FString& Controls,
    const bool bValid)
{
    bBuildPlacementVisible = bVisible;
    bBuildPlacementValid = bValid;
    BuildPlacementHeadline = Headline;
    BuildPlacementControls = Controls;
}

void ACMLHUD::DrawBuildPlacement()
{
    if (!bBuildPlacementVisible || IsAnyPanelOpen() || Canvas == nullptr)
    {
        return;
    }
    UFont* Font = HudFont();
    const float S = Scale();
    const auto DrawCentred = [this, Font](
        const FString& Text, const FLinearColor& Colour, const float Y, const float TextScale)
    {
        float Width = 0.0f;
        float Height = 0.0f;
        Canvas->StrLen(Font, Text, Width, Height);
        DrawOutlinedText(
            Text,
            Colour,
            Canvas->ClipX * 0.5f - Width * TextScale * 0.5f,
            Y,
            TextScale,
            Font);
    };
    DrawCentred(
        BuildPlacementHeadline.ToUpper(),
        bBuildPlacementValid
            ? FLinearColor(0.97f, 0.98f, 0.94f, 0.96f)
            : FLinearColor(1.0f, 0.72f, 0.48f, 0.98f),
        Canvas->ClipY * 0.5f - 82.0f * S,
        1.20f * S);
    DrawCentred(
        BuildPlacementControls.ToUpper(),
        FLinearColor(0.97f, 0.98f, 0.94f, 0.92f),
        Canvas->ClipY * 0.5f + 68.0f * S,
        1.02f * S);
}

void ACMLHUD::ToggleInventory()
{
    SetInventoryVisible(!bInventoryVisible);
}

void ACMLHUD::SetInventoryVisible(const bool bVisible)
{
    if (bVisible)
    {
        HideWorkbench();
        HideChest();
        HideMachine();
        HideAirshipRepair();
    }
    bInventoryVisible = bVisible;
    if (!bVisible)
    {
        ResetHeldInventorySlot();
    }
    ApplyInputMode();
}

void ACMLHUD::NotifyHitBoxClick(const FName BoxName)
{
    Super::NotifyHitBoxClick(BoxName);
    const FString Name = BoxName.ToString();
    const auto ReadIndex = [&Name](const TCHAR* Prefix)
    {
        return FCString::Atoi(*Name.RightChop(FCString::Strlen(Prefix)));
    };

    if (Name.StartsWith(TEXT("PlayerSlot_")))
    {
        HandleInventorySlotClick(
            EInventorySlotArea::Player, ReadIndex(TEXT("PlayerSlot_")));
        return;
    }
    if (Name.StartsWith(TEXT("ChestSlot_")))
    {
        HandleInventorySlotClick(
            EInventorySlotArea::Chest, ReadIndex(TEXT("ChestSlot_")));
        return;
    }
    if (Name.StartsWith(TEXT("MachineInputSlot_")))
    {
        HandleInventorySlotClick(
            EInventorySlotArea::MachineInput,
            ReadIndex(TEXT("MachineInputSlot_")));
        return;
    }
    if (Name.StartsWith(TEXT("MachineFuelSlot_")))
    {
        HandleInventorySlotClick(
            EInventorySlotArea::MachineFuel,
            ReadIndex(TEXT("MachineFuelSlot_")));
        return;
    }
    if (Name.StartsWith(TEXT("MachineOutputSlot_")))
    {
        HandleInventorySlotClick(
            EInventorySlotArea::MachineOutput,
            ReadIndex(TEXT("MachineOutputSlot_")));
        return;
    }

    if (Name.StartsWith(TEXT("QuickCraft_")))
    {
        const int32 Index = ReadIndex(TEXT("QuickCraft_"));
        if (WorkbenchRecipes.IsValidIndex(Index) && WorkbenchRecipes[Index].bCanCraft)
        {
            WorkbenchSelection = Index;
            if (UWorld* World = GetWorld())
            {
                if (UCMLSimulationSubsystem* Simulation =
                        World->GetSubsystem<UCMLSimulationSubsystem>())
                {
                    Simulation->RequestCraftPlayerItem(
                        WorkbenchRecipes[Index].RecipeId,
                        ECMLCraftingStationKind::Personal, 1);
                }
            }
        }
        return;
    }

    if (Name.StartsWith(TEXT("WorkbenchRecipe_")))
    {
        SelectWorkbenchRecipe(ReadIndex(TEXT("WorkbenchRecipe_")));
        return;
    }

    if (Name == TEXT("WorkbenchCraft")
        && WorkbenchRecipes.IsValidIndex(WorkbenchSelection)
        && WorkbenchRecipes[WorkbenchSelection].bCanCraft)
    {
        if (UWorld* World = GetWorld())
        {
            if (UCMLSimulationSubsystem* Simulation =
                    World->GetSubsystem<UCMLSimulationSubsystem>())
            {
                Simulation->RequestCraftPlayerItem(
                    WorkbenchRecipes[WorkbenchSelection].RecipeId,
                    CraftingStation, 1);
            }
        }
    }
}

void ACMLHUD::NotifyHitBoxRelease(const FName BoxName)
{
    Super::NotifyHitBoxRelease(BoxName);
}

bool ACMLHUD::IsInventorySlotOccupied(
    const EInventorySlotArea Area, const int32 Index) const
{
    FCMLInventorySlotPresentation Slot;
    return TryGetInventorySlot(Area, Index, Slot) && Slot.IsOccupied();
}

const FCMLMachinePortPresentation* ACMLHUD::FindMachinePort(
    const ECMLMachinePortKind Kind) const
{
    for (const FCMLMachinePortPresentation& Port : MachineSnapshot.Ports)
    {
        if (Port.Kind == Kind)
        {
            return &Port;
        }
    }
    return nullptr;
}

ECMLMachinePortKind ACMLHUD::MachinePortForArea(const EInventorySlotArea Area)
{
    switch (Area)
    {
    case EInventorySlotArea::MachineInput:  return ECMLMachinePortKind::Input;
    case EInventorySlotArea::MachineFuel:   return ECMLMachinePortKind::Fuel;
    case EInventorySlotArea::MachineOutput: return ECMLMachinePortKind::Output;
    default:                                return ECMLMachinePortKind::None;
    }
}

ACMLHUD::EInventorySlotArea ACMLHUD::AreaForMachinePort(
    const ECMLMachinePortKind Kind)
{
    switch (Kind)
    {
    case ECMLMachinePortKind::Input:  return EInventorySlotArea::MachineInput;
    case ECMLMachinePortKind::Fuel:   return EInventorySlotArea::MachineFuel;
    case ECMLMachinePortKind::Output: return EInventorySlotArea::MachineOutput;
    default:                          return EInventorySlotArea::None;
    }
}

bool ACMLHUD::TryGetInventorySlot(
    const EInventorySlotArea Area,
    const int32 Index,
    FCMLInventorySlotPresentation& OutSlot) const
{
    OutSlot = FCMLInventorySlotPresentation();
    if (Area == EInventorySlotArea::Chest)
    {
        if (!ChestSlots.IsValidIndex(Index))
        {
            return false;
        }
        OutSlot = ChestSlots[Index];
        return true;
    }
    if (Area == EInventorySlotArea::Player)
    {
        FCMLInventoryUiSnapshot Inventory;
        const UWorld* World = GetWorld();
        const UCMLSimulationSubsystem* Simulation = World != nullptr
            ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
        if (Simulation == nullptr
            || !Simulation->GetPlayerInventoryPresentation(Inventory)
            || !Inventory.Slots.IsValidIndex(Index))
        {
            return false;
        }
        OutSlot = Inventory.Slots[Index];
        return true;
    }
    if (const FCMLMachinePortPresentation* Port =
            FindMachinePort(MachinePortForArea(Area));
        Port != nullptr && Port->Slots.IsValidIndex(Index))
    {
        OutSlot = Port->Slots[Index];
        return true;
    }
    return false;
}

void ACMLHUD::ResetHeldInventorySlot()
{
    HeldInventoryArea = EInventorySlotArea::None;
    HeldInventorySlot = INDEX_NONE;
    HeldInventoryQuantity = 0;
    HeldInventoryItem = FCMLInventorySlotPresentation();
}

bool ACMLHUD::QuickMovePlayerSlot(
    const int32 SourceIndex,
    const FCMLInventoryUiSnapshot& Inventory)
{
    if (!Inventory.Slots.IsValidIndex(SourceIndex)
        || !Inventory.Slots[SourceIndex].IsOccupied())
    {
        return false;
    }
    const int32 HotbarCount = FMath::Min(
        FCMLInventoryHudPresenter::HotbarSlotCount, Inventory.Slots.Num());
    const bool bFromHotbar = SourceIndex < HotbarCount;
    const int32 Start = bFromHotbar ? HotbarCount : 0;
    const int32 End = bFromHotbar ? Inventory.Slots.Num() : HotbarCount;
    int32 Destination = INDEX_NONE;
    for (int32 Index = Start; Index < End; ++Index)
    {
        if (!Inventory.Slots[Index].IsOccupied())
        {
            Destination = Index;
            break;
        }
    }
    if (Destination == INDEX_NONE)
    {
        for (int32 Index = Start; Index < End; ++Index)
        {
            if (Inventory.Slots[Index].ItemId == Inventory.Slots[SourceIndex].ItemId)
            {
                Destination = Index;
                break;
            }
        }
    }
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation = World != nullptr
        ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    return Simulation != nullptr && Destination != INDEX_NONE
        && Simulation->RequestMovePlayerSlot(SourceIndex, Destination);
}

FCMLInventorySlotPresentation ACMLHUD::SlotWithHeldQuantityRemoved(
    const EInventorySlotArea Area,
    const int32 Index,
    const FCMLInventorySlotPresentation& Slot) const
{
    if (HeldInventoryArea != Area || HeldInventorySlot != Index
        || HeldInventoryQuantity <= 0 || Slot.ItemId != HeldInventoryItem.ItemId)
    {
        return Slot;
    }
    const int64 Remaining = Slot.Quantity - HeldInventoryQuantity;
    return Remaining > 0
        ? Slot.WithQuantity(Remaining)
        : FCMLInventoryHudPresenter::EmptySlot(Index);
}

void ACMLHUD::DrawHeldInventoryStack()
{
    if (HeldInventoryArea == EInventorySlotArea::None
        || HeldInventoryQuantity <= 0 || !HeldInventoryItem.IsOccupied())
    {
        return;
    }
    FCMLInventorySlotPresentation Current;
    if (!TryGetInventorySlot(HeldInventoryArea, HeldInventorySlot, Current)
        || !Current.IsOccupied() || Current.ItemId != HeldInventoryItem.ItemId
        || Current.Quantity < HeldInventoryQuantity)
    {
        ResetHeldInventorySlot();
        return;
    }
    APlayerController* PlayerController = GetOwningPlayerController();
    float MouseX = 0.0f;
    float MouseY = 0.0f;
    if (PlayerController == nullptr
        || !PlayerController->GetMousePosition(MouseX, MouseY))
    {
        return;
    }
    const float Size = 58.0f * Scale();
    DrawSlot(
        HeldInventoryItem.WithQuantity(HeldInventoryQuantity),
        MouseX - Size * 0.5f,
        MouseY - Size * 0.5f,
        Size,
        true,
        0.30f);
}

void ACMLHUD::HandleInventorySlotClick(
    const EInventorySlotArea Area, const int32 Index)
{
    if (Index < 0 || Area == EInventorySlotArea::None)
    {
        return;
    }
    UWorld* World = GetWorld();
    UCMLSimulationSubsystem* Simulation = World != nullptr
        ? World->GetSubsystem<UCMLSimulationSubsystem>() : nullptr;
    APlayerController* PlayerController = GetOwningPlayerController();
    if (Simulation == nullptr || PlayerController == nullptr)
    {
        return;
    }

    const bool bShift = PlayerController->IsInputKeyDown(EKeys::LeftShift)
        || PlayerController->IsInputKeyDown(EKeys::RightShift);
    const bool bRight = PlayerController->IsInputKeyDown(EKeys::RightMouseButton);
    FCMLInventorySlotPresentation ClickedSlot;
    if (!TryGetInventorySlot(Area, Index, ClickedSlot))
    {
        return;
    }

    if (bShift && !bRight && HeldInventoryArea == EInventorySlotArea::None
        && ClickedSlot.IsOccupied())
    {
        if (bChestVisible
            && (Area == EInventorySlotArea::Player
                || Area == EInventorySlotArea::Chest))
        {
            Simulation->RequestQuickTransferContainerSlot(
                ActiveStorageNodeId, Area == EInventorySlotArea::Player, Index);
        }
        else if (bMachineVisible)
        {
            const ECMLMachinePortKind SourcePort = MachinePortForArea(Area);
            ECMLMachinePortKind DestinationPort = ECMLMachinePortKind::None;
            if (Area == EInventorySlotArea::Player)
            {
                DestinationPort = Simulation->PreferredMachinePortForItem(
                    ActiveMachineNodeId, ClickedSlot.ItemId);
            }
            const int64 Amount = Simulation->AllowedMachineTransferQuantity(
                ActiveMachineNodeId,
                SourcePort,
                DestinationPort,
                ClickedSlot.ItemId,
                ClickedSlot.Quantity);
            if (Amount > 0)
            {
                Simulation->RequestTransferMachineItem(
                    ActiveMachineNodeId,
                    SourcePort,
                    DestinationPort,
                    ClickedSlot.ItemId,
                    Amount);
            }
        }
        else if (Area == EInventorySlotArea::Player)
        {
            FCMLInventoryUiSnapshot Inventory;
            if (Simulation->GetPlayerInventoryPresentation(Inventory))
            {
                QuickMovePlayerSlot(Index, Inventory);
            }
        }
        ResetHeldInventorySlot();
        return;
    }

    if (HeldInventoryArea == EInventorySlotArea::None)
    {
        if (ClickedSlot.IsOccupied())
        {
            HeldInventoryArea = Area;
            HeldInventorySlot = Index;
            HeldInventoryItem = ClickedSlot;
            HeldInventoryQuantity = bRight
                ? (ClickedSlot.Quantity + 1) / 2
                : ClickedSlot.Quantity;
        }
        return;
    }
    if (HeldInventoryArea == Area && HeldInventorySlot == Index)
    {
        ResetHeldInventorySlot();
        return;
    }

    int64 Requested = bRight ? 1 : HeldInventoryQuantity;
    bool bSubmitted = false;
    if (ClickedSlot.IsOccupied()
        && ClickedSlot.ItemId != HeldInventoryItem.ItemId
        && Requested < HeldInventoryQuantity
        && (Area == EInventorySlotArea::Player || Area == EInventorySlotArea::Chest))
    {
        return;
    }

    if (HeldInventoryArea == EInventorySlotArea::Player
        && Area == EInventorySlotArea::Player)
    {
        bSubmitted = Simulation->RequestMovePlayerSlot(
            HeldInventorySlot, Index, Requested);
    }
    else if (bChestVisible
        && (HeldInventoryArea == EInventorySlotArea::Player
            || HeldInventoryArea == EInventorySlotArea::Chest)
        && (Area == EInventorySlotArea::Player
            || Area == EInventorySlotArea::Chest))
    {
        bSubmitted = Simulation->RequestMoveContainerSlot(
            ActiveStorageNodeId,
            HeldInventoryArea == EInventorySlotArea::Player,
            HeldInventorySlot,
            Area == EInventorySlotArea::Player,
            Index,
            Requested);
    }
    else if (bMachineVisible)
    {
        const ECMLMachinePortKind SourcePort = MachinePortForArea(HeldInventoryArea);
        const ECMLMachinePortKind DestinationPort = MachinePortForArea(Area);
        Requested = Simulation->AllowedMachineTransferQuantity(
            ActiveMachineNodeId,
            SourcePort,
            DestinationPort,
            HeldInventoryItem.ItemId,
            Requested);
        bSubmitted = Requested > 0
            && Simulation->RequestTransferMachineItem(
                ActiveMachineNodeId,
                SourcePort,
                DestinationPort,
                HeldInventoryItem.ItemId,
                Requested);
    }

    if (bSubmitted)
    {
        if (Requested >= HeldInventoryQuantity)
        {
            ResetHeldInventorySlot();
        }
        else
        {
            HeldInventoryQuantity -= Requested;
        }
    }
}

void ACMLHUD::ApplyInputMode()
{
    APlayerController* PlayerController = GetOwningPlayerController();
    if (PlayerController == nullptr)
    {
        return;
    }

    const bool bModal = IsAnyPanelOpen();
    PlayerController->bShowMouseCursor = bModal;
    PlayerController->bEnableClickEvents = bModal;
    PlayerController->bEnableMouseOverEvents = bModal;
    PlayerController->ClickEventKeys.AddUnique(EKeys::LeftMouseButton);
    PlayerController->ClickEventKeys.AddUnique(EKeys::RightMouseButton);
    if (bModal)
    {
        if (!bModalInputApplied)
        {
            PlayerController->SetIgnoreLookInput(true);
            PlayerController->SetIgnoreMoveInput(true);
            bModalInputApplied = true;
        }
        FInputModeGameAndUI InputMode;
        InputMode.SetHideCursorDuringCapture(false);
        InputMode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
        PlayerController->SetInputMode(InputMode);
    }
    else
    {
        // SetIgnore* uses counters, not booleans. Resetting is intentional: a
        // panel may refresh or hand off to another panel more than once, and a
        // single false would otherwise leave one count behind and freeze look.
        PlayerController->ResetIgnoreLookInput();
        PlayerController->ResetIgnoreMoveInput();
        bModalInputApplied = false;
        FInputModeGameOnly InputMode;
        InputMode.SetConsumeCaptureMouseDown(false);
        PlayerController->SetInputMode(InputMode);
    }
}

void ACMLHUD::SetInteractionPrompt(const FText& Prompt, const FVector& WorldLocation)
{
    InteractionPrompt = Prompt;
    InteractionPromptWorldLocation = WorldLocation;
    bInteractionPromptVisible = !Prompt.IsEmpty();
}

void ACMLHUD::ClearInteractionPrompt()
{
    InteractionPrompt = FText::GetEmpty();
    bInteractionPromptVisible = false;
}

void ACMLHUD::DrawInteractionPrompt()
{
    if (!bInteractionPromptVisible || Canvas == nullptr)
    {
        return;
    }

    const FString Text = FString::Printf(TEXT("E   %s"), *InteractionPrompt.ToString().ToUpper());
    const FVector Screen = Project(InteractionPromptWorldLocation);
    const float Unit = Scale();
    float Width = 0.0f;
    float Height = 0.0f;
    GetTextSize(Text, Width, Height, HudFont(), 0.82f * Unit);
    const float X = FMath::Clamp(
        Screen.X - Width * 0.5f,
        12.0f * Unit,
        Canvas->ClipX - Width - 12.0f * Unit);
    const float Y = FMath::Clamp(
        Screen.Y - Height * 0.5f,
        12.0f * Unit,
        Canvas->ClipY - Height - 12.0f * Unit);
    DrawOutlinedText(Text, CreamAlpha(0.94f), X, Y, 0.82f * Unit);
}

void ACMLHUD::ShowMachine(const FCMLMachineUiSnapshot& Snapshot)
{
    MachineSnapshot = Snapshot;
    bMachineVisible = true;
    ApplyInputMode();
}

void ACMLHUD::OpenMachineNode(const FCMLStableId& NodeId)
{
    CloseInteractionPanels();
    ActiveMachineNodeId = NodeId;
    bMachineVisible = true;
    ApplyInputMode();
}

void ACMLHUD::HideMachine()
{
    bMachineVisible = false;
    MachineSnapshot = FCMLMachineUiSnapshot();
    ActiveMachineNodeId = FCMLStableId::None();
    ApplyInputMode();
}

void ACMLHUD::DrawHairlineBox(
    const float X, const float Y, const float Width, const float Height,
    const float SideAlpha, const float TopAlpha, const float BottomAlpha)
{
    const float Thickness = FMath::Max(1.0f, HairlineThickness * Scale());
    // One pixel, never a chunky border: the edges are meant to read as the rim
    // of a pane of glass, not as a drawn frame.
    DrawRect(CreamAlpha(TopAlpha), X, Y, Width, Thickness);
    DrawRect(CreamAlpha(BottomAlpha), X, Y + Height - Thickness, Width, Thickness);
    DrawRect(CreamAlpha(SideAlpha), X, Y, Thickness, Height);
    DrawRect(CreamAlpha(SideAlpha), X + Width - Thickness, Y, Thickness, Height);
}

void ACMLHUD::DrawCrosshair()
{
    const float Unit = Scale();
    const float X = Canvas->ClipX * 0.5f;
    const float Y = Canvas->ClipY * 0.5f;

    // A 10 px ring drawn as four hairlines, and a 2 px dot at its centre. The
    // whole reticle sits at 0.78 opacity so it never competes with the scene.
    constexpr float Opacity = 0.78f;
    const float Ring = 10.0f * Unit;
    const float Thickness = FMath::Max(1.0f, Unit);
    const FLinearColor RingColour = CreamAlpha(0.58f * Opacity);
    DrawRect(RingColour, X - Ring * 0.5f, Y - Ring * 0.5f, Ring, Thickness);
    DrawRect(RingColour, X - Ring * 0.5f, Y + Ring * 0.5f - Thickness, Ring, Thickness);
    DrawRect(RingColour, X - Ring * 0.5f, Y - Ring * 0.5f, Thickness, Ring);
    DrawRect(RingColour, X + Ring * 0.5f - Thickness, Y - Ring * 0.5f, Thickness, Ring);

    const float Dot = FMath::Max(2.0f, 2.0f * Unit);
    DrawRect(FLinearColor(1.0f, 1.0f, 1.0f, 0.92f * Opacity),
        X - Dot * 0.5f, Y - Dot * 0.5f, Dot, Dot);
}

void ACMLHUD::DrawHotbar(const FCMLInventoryUiSnapshot& Snapshot)
{
    const float Unit = Scale() * 1.3f;
    const float Size = HotbarSlotSize * Unit;
    const float Pitch = Size + HotbarSlotMargin * 2.0f * Unit;
    const int32 Count = FMath::Min(
        FCMLInventoryHudPresenter::HotbarSlotCount, Snapshot.Slots.Num());

    const float Width = Count * Pitch;
    const float StartX = (Canvas->ClipX - Width) * 0.5f + HotbarSlotMargin * Unit;
    const float Y = Canvas->ClipY - Size - HotbarBottomMargin * Unit;

    for (int32 Index = 0; Index < Count; ++Index)
    {
        const float X = StartX + Index * Pitch;
        DrawSlot(SlotWithHeldQuantityRemoved(
                EInventorySlotArea::Player, Index, Snapshot.Slots[Index]),
            X, Y, Size, Index == SelectedHotbarIndex,
            HotbarGlassAlpha);

        // The slot's number, quiet in the corner. No badge behind it: the rule
        // is that numbers sit on the glass.
        DrawText(FString::FromInt(Index + 1), CreamAlpha(0.38f),
            X + 7.0f * Unit, Y + 5.0f * Unit,
            GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.75f * Unit);
    }

    // The held item's name floats above the row with no pill behind it, kept
    // legible by its outline alone.
    if (Snapshot.Slots.IsValidIndex(SelectedHotbarIndex)
        && Snapshot.Slots[SelectedHotbarIndex].IsOccupied())
    {
        const FString& Name = Snapshot.Slots[SelectedHotbarIndex].DisplayName;
        float TextWidth = 0.0f;
        float TextHeight = 0.0f;
        GetTextSize(Name, TextWidth, TextHeight, HudFont(), Unit);
        DrawOutlinedText(Name, CreamAlpha(0.95f),
            (Canvas->ClipX - TextWidth) * 0.5f, Y - 10.0f * Unit - TextHeight, Unit);
    }
}

void ACMLHUD::DrawInventory(const FCMLInventoryUiSnapshot& Snapshot)
{
    const float Unit = Scale();
    const float Size = SlotSize * Unit;
    const float Gap = SlotGap * Unit;
    constexpr int32 Columns = 8;

    const int32 Count = Snapshot.Slots.Num();
    const int32 Rows = FMath::DivideAndRoundUp(Count, Columns);
    const float GridWidth = Columns * Size + (Columns - 1) * Gap;
    const float GridHeight = Rows * Size + FMath::Max(0, Rows - 1) * Gap;

    const float PaddingX = 26.0f * Unit;
    const float PaddingTop = 22.0f * Unit;
    const float PaddingBottom = 26.0f * Unit;
    const float HeaderHeight = 34.0f * Unit;

    const float InventoryColumnWidth = 558.0f * Unit;
    const float CraftColumnWidth = 326.0f * Unit;
    const float DividerGap = 52.0f * Unit;
    const float PanelWidth = 936.0f * Unit;
    const float PanelHeight = GridHeight + PaddingTop + PaddingBottom + HeaderHeight;
    const float PanelX = (Canvas->ClipX - PanelWidth) * 0.5f;
    // 44% rather than centred: the style sheet lifts the panel so the hotbar
    // below it stays in view.
    const float PanelY = Canvas->ClipY * 0.44f - PanelHeight * 0.5f;

    // Almost invisible. The slots carry the design, not the panel.
    DrawRect(Glass(PanelGlassAlpha), PanelX, PanelY, PanelWidth, PanelHeight);
    DrawHairlineBox(PanelX, PanelY, PanelWidth, PanelHeight,
        PanelEdgeAlpha, 0.20f, PanelEdgeAlpha);

    DrawText(TEXT("INVENTARIO"), CreamAlpha(0.92f),
        PanelX + PaddingX, PanelY + PaddingTop,
        GEngine != nullptr ? GEngine->GetMediumFont() : nullptr, Unit);

    int64 TotalQuantity = 0;
    for (const FCMLInventorySlot& SourceSlot : Snapshot.Source.Slots)
    {
        if (SourceSlot.bHasStack)
        {
            TotalQuantity += SourceSlot.Stack.Quantity.Value;
        }
    }
    DrawText(FString::Printf(TEXT("%lld oggetti  ·  16 slot"), TotalQuantity),
        CreamAlpha(0.42f), PanelX + InventoryColumnWidth - 150.0f * Unit,
        PanelY + PaddingTop + 3.0f * Unit,
        GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.78f * Unit);

    DrawText(TEXT("BARRA RAPIDA"), CreamAlpha(0.55f), PanelX + PaddingX,
        PanelY + PaddingTop + HeaderHeight - 18.0f * Unit,
        GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.72f * Unit);

    const float GridX = PanelX + PaddingX;
    const float GridY = PanelY + PaddingTop + HeaderHeight;
    for (int32 Index = 0; Index < Count; ++Index)
    {
        const int32 Row = Index / Columns;
        const int32 Column = Index % Columns;
        const float SlotX = GridX + Column * (Size + Gap);
        const float SlotY = GridY + Row * (Size + Gap);
        DrawSlot(SlotWithHeldQuantityRemoved(
                EInventorySlotArea::Player, Index, Snapshot.Slots[Index]),
            SlotX, SlotY, Size,
            HeldInventoryArea == EInventorySlotArea::Player
                && Index == HeldInventorySlot,
            SlotGlassAlpha);
        AddHitBox(FVector2D(SlotX, SlotY), FVector2D(Size, Size),
            FName(*FString::Printf(TEXT("PlayerSlot_%d"), Index)), true, 20);
    }


    const float DividerX = PanelX + InventoryColumnWidth + 26.0f * Unit;
    DrawRect(CreamAlpha(0.13f), DividerX, PanelY + 18.0f * Unit,
        FMath::Max(1.0f, Unit), PanelHeight - 36.0f * Unit);
    DrawQuickCrafting();
}

void ACMLHUD::DrawQuickCrafting()
{
    const float Unit = Scale();
    const float PanelWidth = 936.0f * Unit;
    const float PanelX = (Canvas->ClipX - PanelWidth) * 0.5f;
    const float PanelY = Canvas->ClipY * 0.44f - 282.0f * Unit * 0.5f;
    const float X = PanelX + 610.0f * Unit;
    DrawText(TEXT("CRAFTING RAPIDO"), CreamAlpha(0.92f), X, PanelY + 22.0f * Unit,
        GEngine != nullptr ? GEngine->GetMediumFont() : nullptr, Unit);
    DrawText(TEXT("SENZA POSTAZIONE"), CreamAlpha(0.42f), X + 188.0f * Unit,
        PanelY + 25.0f * Unit,
        GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.68f * Unit);

    for (int32 Index = 0; Index < WorkbenchRecipes.Num(); ++Index)
    {
        const FCMLCraftingRecipePresentation& Recipe = WorkbenchRecipes[Index];
        const float Y = PanelY + 58.0f * Unit + Index * 82.0f * Unit;
        const float Height = 72.0f * Unit;
        DrawRect(Glass(Recipe.bCanCraft ? 0.06f : 0.035f), X, Y, 300.0f * Unit, Height);
        DrawHairlineBox(X, Y, 300.0f * Unit, Height,
            Recipe.bCanCraft ? 0.16f : 0.09f, Recipe.bCanCraft ? 0.22f : 0.12f, 0.07f);
        DrawText(Recipe.DisplayName, CreamAlpha(Recipe.bCanCraft ? 0.94f : 0.56f),
            X + 74.0f * Unit, Y + 11.0f * Unit,
            GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.82f * Unit);

        FString Ingredients;
        for (int32 IngredientIndex = 0; IngredientIndex < Recipe.Ingredients.Num(); ++IngredientIndex)
        {
            const FCMLCraftingIngredientPresentation& Ingredient = Recipe.Ingredients[IngredientIndex];
            if (!Ingredients.IsEmpty()) Ingredients += TEXT("   ");
            Ingredients += FString::Printf(TEXT("%s %lld/%lld"),
                *Ingredient.Item.DisplayName, Ingredient.Owned, Ingredient.Required);
        }
        DrawText(Ingredients, CreamAlpha(Recipe.bCanCraft ? 0.48f : 0.34f),
            X + 74.0f * Unit, Y + 36.0f * Unit,
            GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.66f * Unit);
        DrawText(TEXT("CREA"), CreamAlpha(Recipe.bCanCraft ? 0.92f : 0.42f),
            X + 248.0f * Unit, Y + 26.0f * Unit,
            GEngine != nullptr ? GEngine->GetSmallFont() : nullptr, 0.72f * Unit);
        if (Recipe.bCanCraft)
        {
            AddHitBox(FVector2D(X + 228.0f * Unit, Y),
                FVector2D(72.0f * Unit, Height),
                FName(*FString::Printf(TEXT("QuickCraft_%d"), Index)), true, 30);
        }
        DrawSlot(Recipe.Output, X + 8.0f * Unit, Y + 7.0f * Unit,
            58.0f * Unit, Index == WorkbenchSelection, 0.12f);
    }
}

void ACMLHUD::DrawMachine(const FCMLInventoryUiSnapshot& Player)
{
    const float Unit = Scale();
    const float Width = 930.0f * Unit;
    const float Height = 500.0f * Unit;
    const float X = (Canvas->ClipX - Width) * 0.5f;
    const float Y = Canvas->ClipY * 0.46f - Height * 0.5f;
    const float Padding = 24.0f * Unit;
    const float HeaderHeight = 122.0f * Unit;
    const float LeftWidth = 392.0f * Unit;
    const float DividerGap = 34.0f * Unit;
    const float RightX = X + Padding + LeftWidth + DividerGap;
    const float RightWidth = Width - Padding * 2.0f - LeftWidth - DividerGap;
    const float SlotPixels = 58.0f * Unit;
    UFont* Small = GEngine != nullptr ? GEngine->GetSmallFont() : nullptr;

    DrawRect(Glass(PanelGlassAlpha), X, Y, Width, Height);
    DrawHairlineBox(X, Y, Width, Height, PanelEdgeAlpha, 0.20f, PanelEdgeAlpha);

    DrawText(MachineSnapshot.Title, CreamAlpha(0.92f),
        X + Padding, Y + 18.0f * Unit,
        GEngine != nullptr ? GEngine->GetMediumFont() : nullptr, Unit);
    DrawText(MachineSnapshot.CauseText, CreamAlpha(0.62f),
        X + Padding, Y + 52.0f * Unit, Small, 0.82f * Unit);
    if (!MachineSnapshot.RecipeName.IsEmpty())
    {
        DrawText(MachineSnapshot.RecipeName, CreamAlpha(0.92f),
            X + 250.0f * Unit, Y + 52.0f * Unit, Small, 0.82f * Unit);
    }
    if (!MachineSnapshot.ShortfallText.IsEmpty())
    {
        DrawText(MachineSnapshot.ShortfallText, Invalid(),
            X + Padding, Y + 77.0f * Unit, Small, 0.76f * Unit);
    }

    const float BarX = X + Padding;
    const float BarY = Y + 103.0f * Unit;
    const float BarWidth = Width - Padding * 2.0f;
    const float BarHeight = 3.0f * Unit;
    const float Progress =
        FMath::Clamp(static_cast<float>(MachineSnapshot.ProgressPermille) / 1000.0f, 0.0f, 1.0f);
    DrawRect(CreamAlpha(0.16f), BarX, BarY, BarWidth, BarHeight);
    DrawRect(Gold(), BarX, BarY, BarWidth * Progress, BarHeight);
    DrawText(MachineSnapshot.ProgressText(), CreamAlpha(0.62f),
        BarX + BarWidth - 42.0f * Unit, BarY - 20.0f * Unit,
        Small, 0.72f * Unit);

    const float BodyY = Y + HeaderHeight;
    const float DividerX = X + Padding + LeftWidth + DividerGap * 0.5f;
    DrawRect(CreamAlpha(0.13f), DividerX, BodyY,
        FMath::Max(1.0f, Unit), Height - HeaderHeight - 45.0f * Unit);

    DrawText(TEXT("MACCHINARIO"), CreamAlpha(0.48f),
        X + Padding, BodyY, Small, 0.72f * Unit);
    float PortY = BodyY + 24.0f * Unit;
    for (const FCMLMachinePortPresentation& Port : MachineSnapshot.Ports)
    {
        const EInventorySlotArea Area = AreaForMachinePort(Port.Kind);
        if (Area == EInventorySlotArea::None)
        {
            continue;
        }
        DrawText(Port.Title, CreamAlpha(0.72f),
            X + Padding, PortY, Small, 0.78f * Unit);
        DrawText(FString::Printf(TEXT("%lld"), Port.TotalQuantity),
            CreamAlpha(0.40f), X + Padding + LeftWidth - 34.0f * Unit,
            PortY, Small, 0.70f * Unit);
        const float Used = DrawSlotGrid(
            Port.Slots,
            X + Padding,
            PortY + 20.0f * Unit,
            LeftWidth,
            SlotPixels,
            Area);
        PortY += 34.0f * Unit + Used;
    }

    DrawText(TEXT("ZAINO"), CreamAlpha(0.72f),
        RightX, BodyY, Small, 0.82f * Unit);
    DrawText(FString::Printf(TEXT("%d SLOT"), Player.Slots.Num()),
        CreamAlpha(0.40f), RightX + RightWidth - 54.0f * Unit,
        BodyY, Small, 0.66f * Unit);
    DrawSlotGrid(
        Player.Slots,
        RightX,
        BodyY + 24.0f * Unit,
        RightWidth,
        SlotPixels,
        EInventorySlotArea::Player);

    DrawText(
        TEXT("SINISTRO: PRENDI/POSA  ·  DESTRO: METÀ/1  ·  SHIFT+SINISTRO: SPOSTA RAPIDO  ·  E: CHIUDI"),
        CreamAlpha(0.44f), X + Padding, Y + Height - 27.0f * Unit,
        Small, 0.65f * Unit);
}

float ACMLHUD::DrawSlotGrid(
    const TArray<FCMLInventorySlotPresentation>& Slots,
    const float X, const float Y, const float AvailableWidth, const float SlotPixels,
    const EInventorySlotArea Area)
{
    const float Unit = Scale();
    const float Gap = 6.0f * Unit;
    const int32 Columns = FMath::Max(1,
        FMath::FloorToInt((AvailableWidth + Gap) / (SlotPixels + Gap)));

    for (int32 Index = 0; Index < Slots.Num(); ++Index)
    {
        const int32 Row = Index / Columns;
        const int32 Column = Index % Columns;
        const float SlotX = X + Column * (SlotPixels + Gap);
        const float SlotY = Y + Row * (SlotPixels + Gap);
        DrawSlot(SlotWithHeldQuantityRemoved(Area, Index, Slots[Index]),
            SlotX, SlotY, SlotPixels,
            HeldInventoryArea == Area && HeldInventorySlot == Index,
            SlotGlassAlpha);
        const TCHAR* Prefix = TEXT("PlayerSlot");
        switch (Area)
        {
        case EInventorySlotArea::Chest:         Prefix = TEXT("ChestSlot"); break;
        case EInventorySlotArea::MachineInput:  Prefix = TEXT("MachineInputSlot"); break;
        case EInventorySlotArea::MachineFuel:   Prefix = TEXT("MachineFuelSlot"); break;
        case EInventorySlotArea::MachineOutput: Prefix = TEXT("MachineOutputSlot"); break;
        default: break;
        }
        AddHitBox(FVector2D(SlotX, SlotY), FVector2D(SlotPixels, SlotPixels),
            FName(*FString::Printf(TEXT("%s_%d"), Prefix, Index)), true, 20);
    }

    const int32 Rows = FMath::DivideAndRoundUp(Slots.Num(), Columns);
    return Rows * SlotPixels + FMath::Max(0, Rows - 1) * Gap;
}

void ACMLHUD::DrawChest(const FCMLInventoryUiSnapshot& Player)
{
    const float Unit = Scale();
    // 720 px wide with two equal columns: the crate is not more important than
    // the backpack, and a hairline between them is cheaper than a second panel.
    const float PanelWidth = 720.0f * Unit;
    const float ColumnPadding = 12.0f * Unit;
    const float PaddingX = 26.0f * Unit;
    const float PaddingTop = 22.0f * Unit;
    const float HeaderHeight = 34.0f * Unit;
    const float SectionHeading = 26.0f * Unit;
    const float SlotPixels = 58.0f * Unit;
    const float ColumnWidth = (PanelWidth - PaddingX * 2.0f) * 0.5f - ColumnPadding * 2.0f;

    auto GridHeight = [&](const int32 Count)
    {
        const float Gap = 6.0f * Unit;
        const int32 Columns = FMath::Max(1,
            FMath::FloorToInt((ColumnWidth + Gap) / (SlotPixels + Gap)));
        const int32 Rows = FMath::DivideAndRoundUp(Count, Columns);
        return Rows * SlotPixels + FMath::Max(0, Rows - 1) * Gap;
    };

    const float BodyHeight =
        SectionHeading + FMath::Max(GridHeight(ChestSlots.Num()), GridHeight(Player.Slots.Num()));
    const float FooterHeight = 30.0f * Unit;
    const float PanelHeight =
        PaddingTop + HeaderHeight + BodyHeight + FooterHeight + 26.0f * Unit;
    const float PanelX = (Canvas->ClipX - PanelWidth) * 0.5f;
    const float PanelY = Canvas->ClipY * 0.44f - PanelHeight * 0.5f;

    DrawRect(Glass(PanelGlassAlpha), PanelX, PanelY, PanelWidth, PanelHeight);
    DrawHairlineBox(PanelX, PanelY, PanelWidth, PanelHeight,
        PanelEdgeAlpha, 0.20f, PanelEdgeAlpha);

    UFont* const Medium = GEngine != nullptr ? GEngine->GetMediumFont() : nullptr;
    UFont* const Small = GEngine != nullptr ? GEngine->GetSmallFont() : nullptr;

    DrawText(ChestTitle.IsEmpty() ? TEXT("CASSA") : *ChestTitle, CreamAlpha(0.92f),
        PanelX + PaddingX, PanelY + PaddingTop, Medium, Unit);
    DrawText(TEXT("CLICK: prendi / posa / scambia   ·   SHIFT+CLICK: trasferisci"), CreamAlpha(0.45f),
        PanelX + PaddingX + 190.0f * Unit, PanelY + PaddingTop + 4.0f * Unit,
        Small, 0.85f * Unit);

    const float BodyY = PanelY + PaddingTop + HeaderHeight;
    const float LeftX = PanelX + PaddingX + ColumnPadding;
    const float RightX = PanelX + PanelWidth * 0.5f + ColumnPadding;

    // The divider between the columns is a hairline, not a border.
    DrawRect(CreamAlpha(0.10f), PanelX + PanelWidth * 0.5f, BodyY,
        FMath::Max(1.0f, Unit), BodyHeight);

    DrawText(TEXT("CONTENUTO"), CreamAlpha(0.62f), LeftX, BodyY, Small, 0.85f * Unit);
    DrawText(TEXT("ZAINO"), CreamAlpha(0.62f), RightX, BodyY, Small, 0.85f * Unit);

    DrawSlotGrid(ChestSlots, LeftX, BodyY + SectionHeading, ColumnWidth, SlotPixels,
        EInventorySlotArea::Chest);
    DrawSlotGrid(Player.Slots, RightX, BodyY + SectionHeading, ColumnWidth, SlotPixels,
        EInventorySlotArea::Player);

    const float FooterY = BodyY + BodyHeight + 8.0f * Unit;
    DrawRect(CreamAlpha(0.10f), PanelX + PaddingX, FooterY,
        PanelWidth - PaddingX * 2.0f, FMath::Max(1.0f, Unit));

    // The status line names a refusal by its cause and stays empty otherwise.
    // Saying "ready" there would be noise the player learns to ignore.
    if (!ChestStatus.IsEmpty())
    {
        DrawText(ChestStatus, Invalid(),
            PanelX + PaddingX, FooterY + 9.0f * Unit, Small, 0.8f * Unit);
    }
}

void ACMLHUD::DrawCollectionFeed()
{
    constexpr double EnterDuration = 0.14;
    constexpr double HoldDuration = 3.40;
    constexpr double ExitDuration = 0.30;
    constexpr double PopDuration = 0.18;
    constexpr float EnterDistance = 74.0f;
    constexpr float ExitDistance = 28.0f;
    const double Now = FPlatformTime::Seconds();

    for (int32 Index = CollectionEntries.Num() - 1; Index >= 0; --Index)
    {
        FCollectionFeedEntry& Entry = CollectionEntries[Index];
        const double Age = Now - Entry.PhaseStartedAt;
        if (Entry.Phase == ECollectionFeedPhase::Entering && Age >= EnterDuration)
        {
            Entry.Phase = ECollectionFeedPhase::Holding;
            Entry.PhaseStartedAt = Now;
        }
        else if (Entry.Phase == ECollectionFeedPhase::Holding && Age >= HoldDuration)
        {
            Entry.Phase = ECollectionFeedPhase::Exiting;
            Entry.PhaseStartedAt = Now;
        }
        else if (Entry.Phase == ECollectionFeedPhase::Exiting && Age >= ExitDuration)
        {
            CollectionEntries.RemoveAt(Index);
        }
    }

    const float Unit = Scale();
    const float FeedUnit = Unit * 2.0f;
    UFont* const Small = GEngine != nullptr ? GEngine->GetSmallFont() : nullptr;
    float StackY = Canvas->ClipY * 0.35f;
    for (const FCollectionFeedEntry& Entry : CollectionEntries)
    {
        float Alpha = 1.0f;
        float OffsetX = 0.0f;
        float OffsetY = 0.0f;
        const float PhaseAge = static_cast<float>(Now - Entry.PhaseStartedAt);
        if (Entry.Phase == ECollectionFeedPhase::Entering)
        {
            const float T = FMath::Clamp(PhaseAge / static_cast<float>(EnterDuration), 0.0f, 1.0f);
            const float EaseOutCubic = 1.0f - FMath::Pow(1.0f - T, 3.0f);
            OffsetX = EnterDistance * (1.0f - EaseOutCubic) * FeedUnit;
            Alpha = EaseOutCubic;
        }
        else if (Entry.Phase == ECollectionFeedPhase::Exiting)
        {
            const float T = FMath::Clamp(PhaseAge / static_cast<float>(ExitDuration), 0.0f, 1.0f);
            const float EaseIn = T * T;
            OffsetY = ExitDistance * EaseIn * FeedUnit;
            Alpha = 1.0f - EaseIn;
        }

        float PopScale = 1.0f;
        const float PopAge = static_cast<float>(Now - Entry.PopStartedAt);
        if (PopAge >= 0.0f && PopAge < PopDuration)
        {
            const float T = PopAge / static_cast<float>(PopDuration);
            PopScale = T < 0.32f
                ? FMath::Lerp(1.0f, 1.13f, T / 0.32f)
                : FMath::Lerp(1.13f, 1.0f, (T - 0.32f) / 0.68f);
        }

        const FString Label = FString::Printf(
            TEXT("%s (%lld)"), *Entry.DisplayName.ToUpper(), Entry.Quantity);
        float TextWidth = 0.0f;
        float TextHeight = 0.0f;
        GetTextSize(Label, TextWidth, TextHeight, Small, 0.78f * FeedUnit);
        const float Width = FMath::Clamp(TextWidth + 48.0f * FeedUnit,
            126.0f * FeedUnit, 218.0f * FeedUnit) * PopScale;
        const float Height = 34.0f * FeedUnit * PopScale;
        const float X = Canvas->ClipX - 24.0f * FeedUnit - Width + OffsetX;
        const float Y = StackY + OffsetY - (Height - 34.0f * FeedUnit) * 0.5f;

        DrawRect(FLinearColor(1.0f, 1.0f, 1.0f, 0.18f * Alpha), X, Y, Width, Height);
        const float IconSize = 23.0f * FeedUnit * PopScale;
        const float IconX = X + 7.0f * FeedUnit * PopScale;
        const float IconY = Y + (Height - IconSize) * 0.5f;
        if (UTexture2D* Icon = ResolveIconTexture(Entry.IconKind))
        {
            FCanvasTileItem Tile(FVector2D(IconX, IconY), Icon->GetResource(),
                FVector2D(IconSize, IconSize), FLinearColor(1.0f, 1.0f, 1.0f, Alpha));
            Tile.BlendMode = SE_BLEND_Translucent;
            Canvas->DrawItem(Tile);
        }
        DrawText(Label, FLinearColor(0.98f, 0.94f, 0.85f, 0.94f * Alpha),
            IconX + IconSize + 7.0f * FeedUnit * PopScale,
            Y + (Height - TextHeight) * 0.5f,
            Small, 0.78f * FeedUnit * PopScale);
        StackY += 40.0f * FeedUnit;
    }

    constexpr double ToastVisibleSeconds = 2.6;
    constexpr double ToastFadeSeconds = 0.6;
    const double ToastAge = Now - CollectionShownAt;
    if (!CollectionLine.IsEmpty() && ToastAge <= ToastVisibleSeconds)
    {
        const float Alpha = ToastAge <= ToastVisibleSeconds - ToastFadeSeconds
            ? 1.0f
            : static_cast<float>((ToastVisibleSeconds - ToastAge) / ToastFadeSeconds);
        float TextWidth = 0.0f;
        float TextHeight = 0.0f;
        GetTextSize(CollectionLine, TextWidth, TextHeight, HudFont(), 0.92f * Unit);
        DrawOutlinedText(CollectionLine, CreamAlpha(0.95f * Alpha),
            (Canvas->ClipX - TextWidth) * 0.5f, Canvas->ClipY * 0.72f, 0.92f * Unit);
    }
}

void ACMLHUD::ShowChest(
    const FString& Title,
    const TArray<FCMLInventorySlotPresentation>& CrateSlots,
    const FString& StatusText)
{
    bChestVisible = true;
    ChestTitle = Title;
    ChestSlots = CrateSlots;
    ChestStatus = StatusText;
    ApplyInputMode();
}

void ACMLHUD::HideChest()
{
    bChestVisible = false;
    ResetHeldInventorySlot();
    ChestSlots.Reset();
    ChestStatus.Reset();
    ActiveStorageNodeId = FCMLStableId::None();
    ApplyInputMode();
}

void ACMLHUD::OpenStorageNode(const FCMLStableId& NodeId, const FString& Title)
{
    CloseInteractionPanels();
    ActiveStorageNodeId = NodeId;
    ChestTitle = Title;
    ChestStatus.Reset();
    bChestVisible = true;
    ApplyInputMode();
}

bool ACMLHUD::GetActiveTransferNode(FCMLStableId& OutNodeId) const
{
    if (bMachineVisible && !ActiveMachineNodeId.IsNone())
    {
        OutNodeId = ActiveMachineNodeId;
        return true;
    }
    if (bChestVisible && !ActiveStorageNodeId.IsNone())
    {
        OutNodeId = ActiveStorageNodeId;
        return true;
    }
    OutNodeId = FCMLStableId::None();
    return false;
}

void ACMLHUD::PushCollectionFeed(const FString& Line)
{
    CollectionLine = Line;
    CollectionShownAt = FPlatformTime::Seconds();
}

void ACMLHUD::PushCollectedItem(
    const FCMLStableId& ItemId,
    const FString& DisplayName,
    const ECMLInventoryIconKind IconKind,
    const int64 Quantity)
{
    if (ItemId.IsNone() || Quantity <= 0)
    {
        return;
    }
    const double Now = FPlatformTime::Seconds();
    const int32 ExistingIndex = CollectionEntries.IndexOfByPredicate(
        [&ItemId](const FCollectionFeedEntry& Entry) { return Entry.ItemId == ItemId; });
    if (ExistingIndex != INDEX_NONE)
    {
        FCollectionFeedEntry Entry = MoveTemp(CollectionEntries[ExistingIndex]);
        CollectionEntries.RemoveAt(ExistingIndex);
        Entry.Quantity += Quantity;
        Entry.DisplayName = DisplayName;
        Entry.IconKind = IconKind;
        Entry.Phase = ECollectionFeedPhase::Holding;
        Entry.PhaseStartedAt = Now;
        Entry.PopStartedAt = Now;
        CollectionEntries.Add(MoveTemp(Entry));
        return;
    }
    FCollectionFeedEntry& Entry = CollectionEntries.AddDefaulted_GetRef();
    Entry.ItemId = ItemId;
    Entry.DisplayName = DisplayName;
    Entry.IconKind = IconKind;
    Entry.Quantity = Quantity;
    Entry.Phase = ECollectionFeedPhase::Entering;
    Entry.PhaseStartedAt = Now;
}

void ACMLHUD::ShowWorkbench(
    const TArray<FCMLCraftingRecipePresentation>& Recipes, const int32 SelectedIndex)
{
    bWorkbenchVisible = true;
    WorkbenchRecipes = Recipes;
    WorkbenchSelection = Recipes.IsValidIndex(SelectedIndex) ? SelectedIndex : 0;
}

void ACMLHUD::OpenCraftingPanel(
    const FString& Title,
    const ECMLCraftingStationKind Station)
{
    bWorkbenchVisible = true;
    CraftingTitle = Title;
    CraftingStation = Station;
    WorkbenchSelection = 0;
    WorkbenchRecipes.Reset();
    ApplyInputMode();
}

void ACMLHUD::HideWorkbench()
{
    bWorkbenchVisible = false;
    WorkbenchRecipes.Reset();
    WorkbenchSelection = 0;
    ApplyInputMode();
}

void ACMLHUD::SelectWorkbenchRecipe(const int32 Index)
{
    if (WorkbenchRecipes.IsValidIndex(Index))
    {
        WorkbenchSelection = Index;
    }
}

void ACMLHUD::StepWorkbenchRecipe(const int32 Delta)
{
    if (WorkbenchRecipes.IsEmpty())
    {
        return;
    }
    WorkbenchSelection =
        (WorkbenchSelection + Delta + WorkbenchRecipes.Num()) % WorkbenchRecipes.Num();
}

bool ACMLHUD::GetSelectedCraftingRecipe(
    FCMLStableId& OutRecipeId,
    ECMLCraftingStationKind& OutStation) const
{
    OutRecipeId = FCMLStableId::None();
    OutStation = ECMLCraftingStationKind::None;
    if (!bWorkbenchVisible || !WorkbenchRecipes.IsValidIndex(WorkbenchSelection))
    {
        return false;
    }
    OutRecipeId = WorkbenchRecipes[WorkbenchSelection].RecipeId;
    OutStation = CraftingStation;
    return true;
}

void ACMLHUD::CloseInteractionPanels()
{
    bInventoryVisible = false;
    ResetHeldInventorySlot();
    HideWorkbench();
    HideChest();
    HideMachine();
    HideAirshipRepair();
    ApplyInputMode();
}

void ACMLHUD::RebuildCraftingRecipes(const UCMLSimulationSubsystem& Simulation)
{
    FCMLInventoryState Inventory;
    if (!Simulation.GetPlayerInventory(Inventory))
    {
        WorkbenchRecipes.Reset();
        return;
    }
    FCMLContainerDefinition Container;
    const FCMLGameCatalog& Catalog = Simulation.GetCatalog();
    if (!Catalog.TryGetContainer(Inventory.ContainerDefinitionId, Container))
    {
        WorkbenchRecipes.Reset();
        return;
    }

    const FCMLStableId PreviouslySelected = WorkbenchRecipes.IsValidIndex(WorkbenchSelection)
        ? WorkbenchRecipes[WorkbenchSelection].RecipeId : FCMLStableId::None();
    TArray<FCMLCraftingRecipePresentation> Next;
    int32 NextSelection = 0;
    for (const FCMLRecipeDefinition& Recipe : Catalog.Recipes)
    {
        if (Recipe.Station != CraftingStation)
        {
            continue;
        }
        FCMLCraftingRecipePresentation Presentation;
        if (FCMLCraftingHudPresenter::TryProject(
                Inventory, Catalog, Recipe, 1, Container.Capacity, Presentation))
        {
            if (Recipe.RecipeId == PreviouslySelected)
            {
                NextSelection = Next.Num();
            }
            Next.Add(MoveTemp(Presentation));
        }
    }
    WorkbenchRecipes = MoveTemp(Next);
    WorkbenchSelection = WorkbenchRecipes.IsValidIndex(NextSelection) ? NextSelection : 0;
}

void ACMLHUD::DrawWorkbench()
{
    const float Unit = Scale();
    // 900 x 570, split 568 / 280 with a hairline between the columns.
    const float PanelWidth = 900.0f * Unit;
    const float PanelHeight = 570.0f * Unit;
    const float PanelX = (Canvas->ClipX - PanelWidth) * 0.5f;
    const float PanelY = Canvas->ClipY * 0.44f - PanelHeight * 0.5f;
    const float PaddingX = 20.0f * Unit;

    DrawRect(Glass(PanelGlassAlpha), PanelX, PanelY, PanelWidth, PanelHeight);
    DrawHairlineBox(PanelX, PanelY, PanelWidth, PanelHeight,
        PanelEdgeAlpha, 0.20f, PanelEdgeAlpha);

    UFont* const Medium = GEngine != nullptr ? GEngine->GetMediumFont() : nullptr;
    UFont* const Small = GEngine != nullptr ? GEngine->GetSmallFont() : nullptr;

    const float HeaderHeight = 30.0f * Unit;
    DrawText(CraftingTitle, CreamAlpha(0.92f),
        PanelX + PaddingX, PanelY + 9.0f * Unit, Medium, Unit);
    DrawRect(CreamAlpha(0.10f), PanelX, PanelY + HeaderHeight,
        PanelWidth, FMath::Max(1.0f, Unit));

    const float ListX = PanelX + PaddingX;
    const float ListWidth = 568.0f * Unit;
    const float DetailX = PanelX + ListWidth + PaddingX * 2.0f;
    const float BodyY = PanelY + HeaderHeight + 42.0f * Unit;

    // The divider is a hairline, like every other edge in this HUD.
    DrawRect(CreamAlpha(0.10f), DetailX - PaddingX, BodyY,
        FMath::Max(1.0f, Unit), 350.0f * Unit);

    DrawText(TEXT("RICETTE"), CreamAlpha(0.62f), ListX, BodyY - 20.0f * Unit,
        Small, 0.8f * Unit);

    // Two columns of 164 px tiles.
    const float TileWidth = 164.0f * Unit;
    const float TileHeight = 54.0f * Unit;
    const float TileGap = 8.0f * Unit;
    constexpr int32 TileColumns = 3;
    for (int32 Index = 0; Index < WorkbenchRecipes.Num(); ++Index)
    {
        const FCMLCraftingRecipePresentation& Recipe = WorkbenchRecipes[Index];
        const int32 Row = Index / TileColumns;
        const int32 Column = Index % TileColumns;
        const float X = ListX + Column * (TileWidth + TileGap);
        const float Y = BodyY + Row * (TileHeight + TileGap);
        if (Y + TileHeight > BodyY + 316.0f * Unit)
        {
            break;
        }

        const bool bSelected = Index == WorkbenchSelection;
        // Selection is milkier glass and a brighter top edge, never a coloured
        // ring: the one accent belongs to nothing else in this HUD.
        DrawRect(Glass(bSelected ? 0.28f : 0.20f), X, Y, TileWidth, TileHeight);
        DrawHairlineBox(X, Y, TileWidth, TileHeight,
            bSelected ? 0.42f : 0.24f, bSelected ? 0.62f : 0.34f, 0.16f);

        // A recipe that cannot be made is dimmed rather than hidden: the player
        // has to be able to see what they are working towards.
        DrawText(Recipe.DisplayName, CreamAlpha(Recipe.bCanCraft ? 0.92f : 0.42f),
            X + 9.0f * Unit, Y + 9.0f * Unit, Small, 0.85f * Unit);
        DrawText(FString::Printf(TEXT("x%lld"), Recipe.Output.Quantity),
            CreamAlpha(0.55f), X + 9.0f * Unit, Y + 30.0f * Unit, Small, 0.75f * Unit);
        AddHitBox(FVector2D(X, Y), FVector2D(TileWidth, TileHeight),
            FName(*FString::Printf(TEXT("WorkbenchRecipe_%d"), Index)), true, 20);
    }

    if (!WorkbenchRecipes.IsValidIndex(WorkbenchSelection))
    {
        return;
    }
    const FCMLCraftingRecipePresentation& Selected = WorkbenchRecipes[WorkbenchSelection];

    DrawText(Selected.DisplayName, CreamAlpha(0.92f), DetailX, BodyY - 20.0f * Unit,
        Medium, Unit);
    DrawText(Selected.Description, CreamAlpha(0.55f), DetailX, BodyY + 8.0f * Unit,
        Small, 0.75f * Unit);

    float RowY = BodyY + 44.0f * Unit;
    for (const FCMLCraftingIngredientPresentation& Ingredient : Selected.Ingredients)
    {
        // Held over required, and the pair is coloured by whether it is met:
        // the count alone makes the player do the comparison themselves.
        const FLinearColor Colour = Ingredient.IsAvailable() ? CreamAlpha(0.85f) : Invalid();
        DrawText(Ingredient.Item.DisplayName, CreamAlpha(0.75f),
            DetailX, RowY, Small, 0.8f * Unit);
        DrawText(FString::Printf(TEXT("%lld / %lld"), Ingredient.Owned, Ingredient.Required),
            Colour, DetailX + 180.0f * Unit, RowY, Small, 0.8f * Unit);
        RowY += 22.0f * Unit;
    }

    // The one accent in the whole HUD: the craft is possible.
    const float ButtonY = PanelY + PanelHeight - 56.0f * Unit;
    DrawRect(Glass(Selected.bCanCraft ? 0.26f : 0.14f),
        DetailX, ButtonY, 240.0f * Unit, 30.0f * Unit);
    DrawHairlineBox(DetailX, ButtonY, 240.0f * Unit, 30.0f * Unit,
        Selected.bCanCraft ? 0.45f : 0.18f, Selected.bCanCraft ? 0.70f : 0.24f, 0.14f);
    DrawText(Selected.bCanCraft ? TEXT("COSTRUISCI") : TEXT("MATERIALI INSUFFICIENTI"),
        Selected.bCanCraft ? Gold() : CreamAlpha(0.38f),
        DetailX + 12.0f * Unit, ButtonY + 9.0f * Unit, Small, 0.8f * Unit);
    if (Selected.bCanCraft)
    {
        AddHitBox(FVector2D(DetailX, ButtonY),
            FVector2D(240.0f * Unit, 30.0f * Unit),
            TEXT("WorkbenchCraft"), true, 30);
    }
}

void ACMLHUD::ShowAirshipRepair(
    const TArray<FRepairRequirement>& Requirements,
    const int32 ProgressPermille,
    const FString& CauseText)
{
    bRepairVisible = true;
    RepairRequirements = Requirements;
    RepairProgressPermille = FMath::Clamp(ProgressPermille, 0, 1000);
    RepairCause = CauseText;
    ApplyInputMode();
}

void ACMLHUD::OpenAirshipRepair(const FCMLStableId& AirshipId)
{
    CloseInteractionPanels();
    ActiveRepairAirshipId = AirshipId;
    bRepairVisible = true;
    ApplyInputMode();
}

bool ACMLHUD::GetActiveRepairAirship(FCMLStableId& OutAirshipId) const
{
    OutAirshipId = ActiveRepairAirshipId;
    return bRepairVisible && !ActiveRepairAirshipId.IsNone();
}

void ACMLHUD::RebuildAirshipRepair(const UCMLSimulationSubsystem& Simulation)
{
    FCMLAirshipEntityState Airship;
    if (!Simulation.GetAirshipState(ActiveRepairAirshipId, Airship))
    {
        RepairRequirements.Reset();
        RepairProgressPermille = 0;
        RepairCause = TEXT("Aeronave non disponibile");
        return;
    }

    RepairRequirements.Reset();
    const auto AddRequirement = [this, &Simulation](
        const FCMLStableId& ItemId, const int64 Installed, const int64 Required)
    {
        FCMLItemDefinition Definition;
        if (!Simulation.GetCatalog().TryGetItem(ItemId, Definition))
        {
            return;
        }
        FRepairRequirement Row;
        Row.Item = FCMLInventoryHudPresenter::ProjectSlot(0, ItemId, Installed, Definition);
        Row.Owned = Installed;
        Row.Required = Required;
        RepairRequirements.Add(MoveTemp(Row));
    };
    AddRequirement(CMLContentIds::IronPlate, Airship.InstalledIronPlates, 4);
    AddRequirement(CMLContentIds::InsulatedCable, Airship.InstalledInsulatedCables, 2);

    if (Airship.RepairStatus == ECMLAirshipRepairStatus::Repairing)
    {
        RepairProgressPermille = FMath::Clamp(
            1000 - static_cast<int32>(Airship.RepairTicksRemaining * 1000 / 160), 0, 1000);
        RepairCause.Reset();
    }
    else if (Airship.RepairStatus == ECMLAirshipRepairStatus::Repaired)
    {
        RepairProgressPermille = 1000;
        RepairCause = TEXT("Riparazione completata — chiudi con E e pilota l'aeronave");
    }
    else
    {
        const int64 Installed = Airship.InstalledIronPlates + Airship.InstalledInsulatedCables;
        RepairProgressPermille = static_cast<int32>(Installed * 1000 / 6);
        RepairCause = TEXT("Seleziona piastre o cavi nella hotbar e premi LMB per installarli");
    }
}

void ACMLHUD::HideAirshipRepair()
{
    bRepairVisible = false;
    RepairRequirements.Reset();
    RepairCause.Reset();
    ActiveRepairAirshipId = FCMLStableId::None();
    ApplyInputMode();
}

void ACMLHUD::DrawAirshipRepair()
{
    const float Unit = Scale();
    // 960 wide, with the hull preview taking a fixed 380 on the left.
    const float PanelWidth = 960.0f * Unit;
    const float PreviewWidth = 380.0f * Unit;
    const float PreviewHeight = 300.0f * Unit;
    const float PaddingX = 26.0f * Unit;
    const float PaddingTop = 22.0f * Unit;
    const float HeaderHeight = 34.0f * Unit;
    const float RowHeight = 70.0f * Unit;

    const float BodyHeight = FMath::Max(
        PreviewHeight, RepairRequirements.Num() * RowHeight + 20.0f * Unit);
    const float PanelHeight = PaddingTop + HeaderHeight + BodyHeight + 96.0f * Unit;
    const float PanelX = (Canvas->ClipX - PanelWidth) * 0.5f;
    const float PanelY = Canvas->ClipY * 0.44f - PanelHeight * 0.5f;

    // Translucent like every other panel. Making this one opaque would put it
    // at odds with the whole HUD's language.
    DrawRect(Glass(PanelGlassAlpha), PanelX, PanelY, PanelWidth, PanelHeight);
    DrawHairlineBox(PanelX, PanelY, PanelWidth, PanelHeight,
        PanelEdgeAlpha, 0.20f, PanelEdgeAlpha);

    UFont* const Medium = GEngine != nullptr ? GEngine->GetMediumFont() : nullptr;
    UFont* const Small = GEngine != nullptr ? GEngine->GetSmallFont() : nullptr;

    DrawText(TEXT("A E R O N A V E   D A N N E G G I A T A"), CreamAlpha(0.92f),
        PanelX + PaddingX, PanelY + PaddingTop, Medium, Unit);

    const float BodyY = PanelY + PaddingTop + HeaderHeight;
    const float PreviewX = PanelX + PaddingX;
    DrawText(TEXT("SCAFO"), CreamAlpha(0.62f), PreviewX, BodyY - 18.0f * Unit,
        Small, 0.8f * Unit);
    // The hull preview is a framed viewport the renderer fills; the panel only
    // reserves and outlines it.
    DrawRect(Glass(0.06f), PreviewX, BodyY, PreviewWidth, PreviewHeight);
    DrawHairlineBox(PreviewX, BodyY, PreviewWidth, PreviewHeight, 0.24f, 0.34f, 0.16f);

    const float ListX = PreviewX + PreviewWidth + PaddingX;
    DrawText(TEXT("COMPONENTI RICHIESTI"), CreamAlpha(0.62f), ListX, BodyY - 18.0f * Unit,
        Small, 0.8f * Unit);

    float RowY = BodyY;
    for (const FRepairRequirement& Requirement : RepairRequirements)
    {
        DrawSlot(Requirement.Item, ListX, RowY, 62.0f * Unit, false, SlotGlassAlpha);

        const float TextX = ListX + 74.0f * Unit;
        DrawText(Requirement.Item.DisplayName, CreamAlpha(0.92f),
            TextX, RowY + 10.0f * Unit, Small, 0.9f * Unit);

        // Held over required, coloured by whether it is met. A met requirement
        // goes gold rather than merely stopping being red: the panel should
        // read as progress, not only as a list of what is wrong.
        DrawText(FString::Printf(TEXT("%lld / %lld"), Requirement.Owned, Requirement.Required),
            Requirement.IsMet() ? Gold() : Invalid(),
            TextX, RowY + 34.0f * Unit, Small, 0.85f * Unit);

        RowY += RowHeight;
        // A hairline under each row, as the style sheet has it.
        DrawRect(CreamAlpha(0.08f), ListX, RowY - 6.0f * Unit,
            PanelWidth - (ListX - PanelX) - PaddingX, FMath::Max(1.0f, Unit));
    }

    // The same 3 px track the machine panel and the durability bar use, so one
    // repair reading looks like every other measurement in the game.
    const float BarX = PanelX + PaddingX;
    const float BarY = PanelY + PanelHeight - 60.0f * Unit;
    const float BarWidth = PanelWidth - PaddingX * 2.0f;
    const float BarHeight = 3.0f * Unit;
    const float Progress = RepairProgressPermille / 1000.0f;
    DrawRect(CreamAlpha(0.16f), BarX, BarY, BarWidth, BarHeight);
    DrawRect(RepairCause.IsEmpty() ? Gold() : Invalid(),
        BarX, BarY, BarWidth * Progress, BarHeight);
    DrawText(FString::Printf(TEXT("%d%%"), RepairProgressPermille / 10),
        CreamAlpha(0.62f), BarX, BarY + 8.0f * Unit, Small, 0.8f * Unit);

    // A dot beside the cause, so a blocked repair is legible at a glance and
    // not only by reading. Empty when nothing is blocking.
    if (!RepairCause.IsEmpty())
    {
        const float DotSize = 6.0f * Unit;
        const float CauseY = BarY + 26.0f * Unit;
        DrawRect(Invalid(), BarX, CauseY + 2.0f * Unit, DotSize, DotSize);
        DrawText(RepairCause, Invalid(), BarX + DotSize + 8.0f * Unit, CauseY,
            Small, 0.8f * Unit);
    }
}

void ACMLHUD::SetTutorialCard(const bool bVisible, const float Direction)
{
    bTutorialCardVisible = bVisible;
    if (bVisible)
    {
        // Kept while fading out, so the arrow does not flip as the card goes.
        TutorialDirection = Direction;
    }
}

UFont* ACMLHUD::CardFont()
{
    // Unity's legacy GUI skin uses a bold sans face for this card. Unreal's
    // /Engine/EngineFonts/Roboto is an offline atlas and magnifies into the
    // visibly wrong soft regular face. Build the runtime composite from the
    // engine's distributable Roboto-Bold TTF instead, so this stays sharp and
    // works in a cooked build rather than depending on an editor-only asset.
    if (CachedCardFont == nullptr)
    {
        CachedCardFont = NewObject<UFont>(this, TEXT("CMLIntroCardFont"));
        CachedCardFont->FontCacheType = EFontCacheType::Runtime;
        CachedCardFont->LegacyFontSize = 34;
        CachedCardFont->GetMutableInternalCompositeFont() = FCompositeFont(
            TEXT("Bold"),
            FPaths::EngineContentDir() / TEXT("Slate/Fonts/Roboto-Bold.ttf"),
            EFontHinting::Auto,
            EFontLoadingPolicy::LazyLoad);
        if (CachedCardFont == nullptr && GEngine != nullptr)
        {
            CachedCardFont = GEngine->GetMediumFont();
        }
    }
    return CachedCardFont;
}

UFont* ACMLHUD::HudFont()
{
    // UI Toolkit's authored hotbar is a compact 12 px medium-weight sans.
    // Keeping it separate from the 34 px cinematic card font is essential:
    // sharing that font made labels render at 34 px while their layout was
    // still measured against Unreal's small engine font.
    if (CachedHudFont == nullptr)
    {
        CachedHudFont = NewObject<UFont>(this, TEXT("CMLHudFont"));
        CachedHudFont->FontCacheType = EFontCacheType::Runtime;
        CachedHudFont->LegacyFontSize = 12;
        CachedHudFont->GetMutableInternalCompositeFont() = FCompositeFont(
            TEXT("Medium"),
            FPaths::EngineContentDir() / TEXT("Slate/Fonts/Roboto-Medium.ttf"),
            EFontHinting::Auto,
            EFontLoadingPolicy::LazyLoad);
        if (CachedHudFont == nullptr && GEngine != nullptr)
        {
            CachedHudFont = GEngine->GetSmallFont();
        }
    }
    return CachedHudFont;
}

UTexture2D* ACMLHUD::GlyphTexture(
    const TArray<uint8>& Coverage, const int32 Width, const int32 Height,
    TObjectPtr<UTexture2D>& Cache)
{
    if (Cache != nullptr)
    {
        return Cache;
    }
    if (Coverage.Num() < Width * Height || Width <= 0 || Height <= 0)
    {
        return nullptr;
    }

    UTexture2D* Texture = UTexture2D::CreateTransient(Width, Height, PF_B8G8R8A8);
    if (Texture == nullptr)
    {
        return nullptr;
    }
    // Nearest filtering would show the glyph's own antialiasing as stair steps
    // at the sizes this is drawn at.
    Texture->Filter = TextureFilter::TF_Trilinear;
    Texture->AddressX = TextureAddress::TA_Clamp;
    Texture->AddressY = TextureAddress::TA_Clamp;
    Texture->SRGB = false;

    FTexture2DMipMap& Mip = Texture->GetPlatformData()->Mips[0];
    uint8* Pixels = static_cast<uint8*>(Mip.BulkData.Lock(LOCK_READ_WRITE));
    for (int32 Index = 0; Index < Width * Height; ++Index)
    {
        // White, with the ported coverage as alpha; the card tints it.
        // Glyph builders emit RGBA pixels; coverage lives in the alpha byte.
        // Sampling Coverage[Index] turned their white RGB into opaque blocks.
        const uint8 Alpha = Coverage.Num() >= Width * Height * 4
            ? Coverage[Index * 4 + 3]
            : Coverage[Index];
        Pixels[Index * 4 + 0] = 255;
        Pixels[Index * 4 + 1] = 255;
        Pixels[Index * 4 + 2] = 255;
        Pixels[Index * 4 + 3] = Alpha;
    }
    Mip.BulkData.Unlock();
    Texture->UpdateResource();

    Cache = Texture;
    return Texture;
}

void ACMLHUD::DrawTutorialCard()
{
    // The card eases in and out rather than appearing: the opening is a filmed
    // sequence, and a hard cut to a UI element breaks that reading.
    const float Target = bTutorialCardVisible ? 1.0f : 0.0f;
    const float Delta = GetWorld() != nullptr ? GetWorld()->GetDeltaSeconds() : 0.0f;
    TutorialAlpha = FMath::FInterpConstantTo(TutorialAlpha, Target, Delta, 3.0f);
    if (TutorialAlpha <= 0.002f)
    {
        return;
    }

    // Unity applies the additional .62 reduction only to the raster glyphs,
    // not to its 34 pt text and spacing.
    const float ScreenScale = FCMLTutorialGlyphs::ScaleForScreenHeight(Canvas->ClipY);
    const float IconScale = ScreenScale * 0.62f;
    const float MouseW = FCMLTutorialGlyphs::MouseWidth * IconScale;
    const float MouseH = FCMLTutorialGlyphs::MouseHeight * IconScale;
    const float ArrowW = FCMLTutorialGlyphs::ArrowSize * IconScale;
    const float Gap = 14.0f * ScreenScale;

    const FString Before = TEXT("Muovi");
    const FString After = TutorialDirection >= 0.0f
        ? TEXT("per girare a destra") : TEXT("per girare a sinistra");
    UFont* const Font = CardFont();

    float BeforeW = 0.0f, BeforeH = 0.0f, AfterW = 0.0f, AfterH = 0.0f;
    GetTextSize(Before, BeforeW, BeforeH, Font, ScreenScale);
    GetTextSize(After, AfterW, AfterH, Font, ScreenScale);

    const float TotalW = BeforeW + Gap + MouseW + Gap + AfterW;
    const float Left = (Canvas->ClipX - TotalW) * 0.5f;
    const float CentreY = Canvas->ClipY * 0.5f;
    // The row is centred on the mouse body, which is taller than the text, so
    // the glyph column does not drag the line off centre.
    const float IconTop = CentreY - MouseH * 0.5f;
    const float TextTop = CentreY - BeforeH * 0.5f;

    // Preserve the incoming asteroid behind Unity's 52% frozen-frame dim.
    DrawRect(FLinearColor(0.14f, 0.15f, 0.17f, 0.52f * TutorialAlpha),
        0.0f, 0.0f, Canvas->ClipX, Canvas->ClipY);

    const FLinearColor Tint = CreamAlpha(0.95f * TutorialAlpha);
    DrawOutlinedText(Before, Tint, Left, TextTop, ScreenScale, Font);

    // The mouse itself, from the glyph FCMLTutorialGlyphs builds — the same
    // signed-distance body, button seam and scroll wheel Unity draws. It was
    // ported, and this card drew a translucent rectangle with a hairline
    // border instead, so there was no mouse on screen to read at all.
    const float MouseX = Left + BeforeW + Gap;
    if (UTexture2D* MouseTexture = GlyphTexture(
            FCMLTutorialGlyphs::MouseGlyph(),
            FCMLTutorialGlyphs::MouseWidth, FCMLTutorialGlyphs::MouseHeight,
            CachedMouseGlyph))
    {
        DrawTexture(MouseTexture, MouseX, IconTop, MouseW, MouseH,
            0.0f, 0.0f, 1.0f, 1.0f, Tint, BLEND_Translucent);
    }

    // The arrow points the way being asked for; mirroring its UVs is what makes
    // one glyph serve both halves of the lesson.
    // Unity places the direction beneath the device, as the motion the device
    // makes. Putting it inline made the prompt read like a mouse-button icon.
    const float ArrowX = MouseX + (MouseW - ArrowW) * 0.5f;
    const float ArrowY = IconTop + MouseH + 6.0f * ScreenScale;
    if (UTexture2D* ArrowTexture = GlyphTexture(
            FCMLTutorialGlyphs::ArrowGlyph(),
            FCMLTutorialGlyphs::ArrowSize, FCMLTutorialGlyphs::ArrowSize,
            CachedArrowGlyph))
    {
        const bool bRight = TutorialDirection >= 0.0f;
        DrawTexture(ArrowTexture, ArrowX, ArrowY, ArrowW, ArrowW,
            bRight ? 0.0f : 1.0f, 0.0f, bRight ? 1.0f : -1.0f, 1.0f,
            Tint, BLEND_Translucent);
    }

    DrawOutlinedText(After, Tint, MouseX + MouseW + Gap, TextTop, ScreenScale, Font);
}

void ACMLHUD::SetCinematicOverlay(
    const float FlashAlpha, const float FadeAlpha, const float Eyelid)
{
    CinematicFlashAlpha = FMath::Clamp(FlashAlpha, 0.0f, 1.0f);
    CinematicFadeAlpha = FMath::Clamp(FadeAlpha, 0.0f, 1.0f);
    CinematicEyelid = FMath::Clamp(Eyelid, 0.0f, 1.0f);
}

void ACMLHUD::DrawCinematicOverlay()
{
    if (Canvas == nullptr)
    {
        return;
    }
    // Same draw order as Unity: discharge, blackout, then two real eyelids.
    // A uniform fade in place of the lids was the reason the crash looked like
    // a generic transition rather than the pilot losing consciousness.
    if (CinematicFlashAlpha > 0.001f)
    {
        DrawRect(FLinearColor(0.88f, 0.95f, 1.0f, CinematicFlashAlpha),
            0.0f, 0.0f, Canvas->ClipX, Canvas->ClipY);
    }
    if (CinematicFadeAlpha > 0.001f)
    {
        DrawRect(FLinearColor(0.0f, 0.0f, 0.0f, CinematicFadeAlpha),
            0.0f, 0.0f, Canvas->ClipX, Canvas->ClipY);
    }
    if (CinematicEyelid > 0.001f)
    {
        const float Lid = Canvas->ClipY * 0.5f * CinematicEyelid;
        DrawRect(FLinearColor::Black, 0.0f, 0.0f, Canvas->ClipX, Lid);
        DrawRect(FLinearColor::Black, 0.0f, Canvas->ClipY - Lid, Canvas->ClipX, Lid);
    }
}

void ACMLHUD::DrawOutlinedText(
    const FString& Text, const FLinearColor& Colour, const float X, const float Y,
    const float TextScale, UFont* FontOverride)
{
    // No filled badge behind any number or name; legibility comes from an
    // outline instead. Four offset draws is what Canvas gives us for that.
    UFont* Font = FontOverride != nullptr ? FontOverride : HudFont();
    const FLinearColor OutlineColour(0.055f, 0.043f, 0.035f, 0.85f * Colour.A);
    const float Offset = FMath::Max(1.0f, TextScale);
    for (int32 Index = 0; Index < 4; ++Index)
    {
        const float OffsetX = (Index == 0) ? -Offset : (Index == 1 ? Offset : 0.0f);
        const float OffsetY = (Index == 2) ? -Offset : (Index == 3 ? Offset : 0.0f);
        DrawText(Text, OutlineColour, X + OffsetX, Y + OffsetY, Font, TextScale);
    }
    DrawText(Text, Colour, X, Y, Font, TextScale);
}

void ACMLHUD::DrawSlot(
    const FCMLInventorySlotPresentation& Slot,
    const float X,
    const float Y,
    const float Size,
    const bool bSelected,
    const float BaseGlassAlpha)
{
    const float Unit = Scale();

    // Three states, and they differ only by how milky the glass is and how
    // bright its top edge reads. Selection stays quiet: no coloured ring, no
    // badge â€” that is the style sheet's rule and it is what keeps the row calm.
    float GlassAlpha = BaseGlassAlpha;
    float TopEdge = EdgeTopAlpha;
    if (bSelected)
    {
        GlassAlpha = SelectedGlassAlpha;
        TopEdge = 0.42f;
    }
    else if (Slot.IsOccupied())
    {
        GlassAlpha = OccupiedGlassAlpha;
        TopEdge = OccupiedEdgeTopAlpha;
    }

    DrawRect(Glass(GlassAlpha), X, Y, Size, Size);
    DrawHairlineBox(X, Y, Size, Size, EdgeSideAlpha, TopEdge, EdgeBottomAlpha);

    // The rounded corners of the original are approximated by clipping the
    // glass at each corner: Canvas draws rectangles, and a square slot among
    // rounded ones would be the most visible difference of all.
    const float Corner = Size * CornerFraction * 0.5f;
    const FLinearColor Cut(0.0f, 0.0f, 0.0f, 0.0f);
    DrawRect(Cut, X, Y, Corner, Corner);

    if (!Slot.IsOccupied())
    {
        return;
    }

    // Unity uses renders of the actual 3D items. These textures were migrated
    // with the rest of the UI and must be drawn directly, never replaced with
    // coloured rectangles.
    const float IconInset = 3.0f * Unit;
    if (UTexture2D* Icon = ResolveIconTexture(Slot.IconKind))
    {
        FCanvasTileItem Tile(FVector2D(X + IconInset, Y + IconInset),
            Icon->GetResource(), FVector2D(Size - IconInset * 2.0f, Size - 14.0f * Unit),
            FLinearColor::White);
        Tile.BlendMode = SE_BLEND_Translucent;
        Canvas->DrawItem(Tile);
    }
    else
    {
        const FString Initial = Slot.DisplayName.IsEmpty()
            ? TEXT("?") : Slot.DisplayName.Left(1).ToUpper();
        float TextWidth = 0.0f;
        float TextHeight = 0.0f;
        GetTextSize(Initial, TextWidth, TextHeight, HudFont(), 1.25f * Unit);
        DrawOutlinedText(Initial, CreamAlpha(0.62f),
            X + (Size - TextWidth) * 0.5f,
            Y + (Size - TextHeight) * 0.42f,
            1.25f * Unit);
    }

    // Quantity: bottom-right, outlined, no badge.
    if (Slot.Quantity > 1)
    {
        const FString Quantity = FString::Printf(TEXT("%lld"), Slot.Quantity);
        float TextWidth = 0.0f;
        float TextHeight = 0.0f;
        GetTextSize(Quantity, TextWidth, TextHeight, HudFont(), Unit);
        DrawOutlinedText(Quantity, FLinearColor(1.0f, 1.0f, 1.0f, 0.94f),
            X + Size - 8.0f * Unit - TextWidth,
            Y + Size - 3.0f * Unit - TextHeight, Unit);
    }

    // A tool's wear, as a 3 px track inset from both edges.
    if (Slot.bHasDurability)
    {
        const float TrackX = X + 8.0f * Unit;
        const float TrackWidth = Size - 16.0f * Unit;
        const float TrackY = Y + Size - 5.0f * Unit;
        const float TrackHeight = 3.0f * Unit;
        DrawRect(CreamAlpha(0.16f), TrackX, TrackY, TrackWidth, TrackHeight);
        DrawRect(DurabilityFill(Slot.Durability01),
            TrackX, TrackY, TrackWidth * FMath::Clamp(Slot.Durability01, 0.0f, 1.0f),
            TrackHeight);
    }
}

UTexture2D* ACMLHUD::ResolveIconTexture(const ECMLInventoryIconKind IconKind)
{
    static TMap<ECMLInventoryIconKind, TObjectPtr<UTexture2D>> Cache;
    if (TObjectPtr<UTexture2D>* Found = Cache.Find(IconKind))
    {
        return Found->Get();
    }
    const TCHAR* Name = nullptr;
    switch (IconKind)
    {
    case ECMLInventoryIconKind::Ore: Name = TEXT("RawIron"); break;
    case ECMLInventoryIconKind::Ingot: Name = TEXT("IronIngot"); break;
    case ECMLInventoryIconKind::Plate: Name = TEXT("IronPlate"); break;
    case ECMLInventoryIconKind::CrudePickaxe:
    case ECMLInventoryIconKind::IronPickaxe: Name = TEXT("PickaxeCrude"); break;
    case ECMLInventoryIconKind::Stone: Name = TEXT("Stone"); break;
    case ECMLInventoryIconKind::WoodLog: Name = TEXT("WoodLog"); break;
    case ECMLInventoryIconKind::PlantFiber: Name = TEXT("PlantFiber"); break;
    case ECMLInventoryIconKind::Stick: Name = TEXT("Stick"); break;
    default: break;
    }
    UTexture2D* Loaded = Name != nullptr
        ? LoadObject<UTexture2D>(nullptr, *FString::Printf(
            TEXT("/Game/Migrated/Project/Art/UI/Icons/ICON_%s.ICON_%s"), Name, Name))
        : nullptr;
    Cache.Add(IconKind, Loaded);
    return Loaded;
}

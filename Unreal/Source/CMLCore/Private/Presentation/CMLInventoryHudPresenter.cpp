#include "Presentation/CMLInventoryHudPresenter.h"

#include "Content/CMLContentIds.h"

namespace
{
    /**
     * One item's presentation: the name the player reads, its icon and its
     * accent colour. Held in one table rather than a chain of comparisons so
     * that adding an item is one line and cannot half-happen.
     */
    struct FItemAppearance
    {
        const TCHAR* DisplayName;
        ECMLInventoryIconKind IconKind;
        FLinearColor AccentColor;
    };

    const TMap<FCMLStableId, FItemAppearance>& Appearances()
    {
        static const TMap<FCMLStableId, FItemAppearance> Table = []
        {
            using namespace CMLContentIds;
            TMap<FCMLStableId, FItemAppearance> Map;
            Map.Add(RawIron, {TEXT("Ferro grezzo"), ECMLInventoryIconKind::Ore,
                FLinearColor(0.38f, 0.49f, 0.50f, 1.0f)});
            Map.Add(IronIngot, {TEXT("Lingotto di ferro"), ECMLInventoryIconKind::Ingot,
                FLinearColor(0.52f, 0.65f, 0.65f, 1.0f)});
            Map.Add(IronPlate, {TEXT("Piastra di ferro"), ECMLInventoryIconKind::Plate,
                FLinearColor(0.46f, 0.59f, 0.60f, 1.0f)});
            Map.Add(RawCopper, {TEXT("Rame grezzo"), ECMLInventoryIconKind::Ore,
                FLinearColor(0.70f, 0.39f, 0.22f, 1.0f)});
            Map.Add(RawTin, {TEXT("Stagno grezzo"), ECMLInventoryIconKind::Ore,
                FLinearColor(0.60f, 0.65f, 0.66f, 1.0f)});
            Map.Add(InsulatedCable, {TEXT("Cavo isolato"), ECMLInventoryIconKind::Generic,
                FLinearColor(0.72f, 0.50f, 0.20f, 1.0f)});
            Map.Add(Stone, {TEXT("Pietra"), ECMLInventoryIconKind::Stone,
                FLinearColor(0.46f, 0.49f, 0.48f, 1.0f)});
            Map.Add(WoodLog, {TEXT("Tronco"), ECMLInventoryIconKind::WoodLog,
                FLinearColor(0.52f, 0.34f, 0.18f, 1.0f)});
            Map.Add(PlantFiber, {TEXT("Fibra vegetale"), ECMLInventoryIconKind::PlantFiber,
                FLinearColor(0.42f, 0.60f, 0.24f, 1.0f)});
            Map.Add(Stick, {TEXT("Bastone"), ECMLInventoryIconKind::Stick,
                FLinearColor(0.60f, 0.47f, 0.32f, 1.0f)});
            Map.Add(WorkbenchItem, {TEXT("Banco da lavoro"), ECMLInventoryIconKind::Generic,
                FLinearColor(0.58f, 0.42f, 0.25f, 1.0f)});
            Map.Add(WoodenCrateItem, {TEXT("Cassa di legno"), ECMLInventoryIconKind::WoodenCrate,
                FLinearColor(0.58f, 0.42f, 0.25f, 1.0f)});
            Map.Add(MechanicalPressItem, {TEXT("Pressa meccanica"),
                ECMLInventoryIconKind::MechanicalPress, FLinearColor(0.46f, 0.53f, 0.50f, 1.0f)});
            Map.Add(CrudeFurnaceItem, {TEXT("Fornace rudimentale"), ECMLInventoryIconKind::Generic,
                FLinearColor(0.48f, 0.34f, 0.24f, 1.0f)});
            Map.Add(MechanicalDrillItem, {TEXT("Estrattore meccanico"),
                ECMLInventoryIconKind::MechanicalDrill, FLinearColor(0.44f, 0.46f, 0.47f, 1.0f)});
            Map.Add(BeltFunnel, {TEXT("Imbuto"), ECMLInventoryIconKind::BeltFunnel,
                FLinearColor(0.55f, 0.45f, 0.30f, 1.0f)});
            Map.Add(BeltStraight, {TEXT("Nastro trasportatore"),
                ECMLInventoryIconKind::BeltStraight, FLinearColor(0.52f, 0.40f, 0.28f, 1.0f)});
            Map.Add(BeltCurve, {TEXT("Nastro curvo destro"), ECMLInventoryIconKind::BeltCurve,
                FLinearColor(0.52f, 0.40f, 0.28f, 1.0f)});
            Map.Add(BeltCurveLeft, {TEXT("Nastro curvo sinistro"),
                ECMLInventoryIconKind::BeltCurveLeft, FLinearColor(0.52f, 0.40f, 0.28f, 1.0f)});
            Map.Add(BeltIncline, {TEXT("Nastro inclinato"), ECMLInventoryIconKind::BeltIncline,
                FLinearColor(0.52f, 0.40f, 0.28f, 1.0f)});
            Map.Add(BeltSupport, {TEXT("Supporto per nastro"), ECMLInventoryIconKind::BeltSupport,
                FLinearColor(0.48f, 0.43f, 0.36f, 1.0f)});
            Map.Add(BeltDriveUnit, {TEXT("Nastro motrice"), ECMLInventoryIconKind::BeltDriveUnit,
                FLinearColor(0.61f, 0.43f, 0.25f, 1.0f)});
            Map.Add(CrudePickaxe, {TEXT("Piccone rudimentale"),
                ECMLInventoryIconKind::CrudePickaxe, FLinearColor(0.50f, 0.37f, 0.24f, 1.0f)});
            Map.Add(IronPickaxe, {TEXT("Piccone di ferro"), ECMLInventoryIconKind::IronPickaxe,
                FLinearColor(0.48f, 0.56f, 0.57f, 1.0f)});
            return Map;
        }();
        return Table;
    }

    /**
     * A readable name for an item the table does not cover: the last segment of
     * its content key, underscores opened out, first letter capitalised. Better
     * than showing an id, and it makes a missing table entry obvious rather than
     * invisible.
     */
    FString HumanizeKey(const FString& Key)
    {
        const FString Trimmed = Key.TrimStartAndEnd();
        if (Trimmed.IsEmpty())
        {
            return TEXT("Oggetto");
        }
        int32 Separator = INDEX_NONE;
        FString Value = Trimmed.FindLastChar(TEXT('.'), Separator)
            ? Trimmed.RightChop(Separator + 1)
            : Trimmed;
        Value = Value.Replace(TEXT("_"), TEXT(" "));
        if (Value.IsEmpty())
        {
            return TEXT("Oggetto");
        }
        return FString::Chr(FChar::ToUpper(Value[0])) + Value.RightChop(1);
    }
}

FCMLInventorySlotPresentation FCMLInventorySlotPresentation::WithQuantity(
    const int64 NewQuantity) const
{
    FCMLInventorySlotPresentation Copy = *this;
    Copy.Quantity = NewQuantity;
    return Copy;
}

FCMLInventorySlotPresentation FCMLInventoryHudPresenter::EmptySlot(const int32 SlotIndex)
{
    FCMLInventorySlotPresentation Slot;
    Slot.SlotIndex = SlotIndex;
    Slot.AccentColor = FLinearColor::Transparent;
    return Slot;
}

FCMLInventorySlotPresentation FCMLInventoryHudPresenter::ProjectSlot(
    const int32 SlotIndex,
    const FCMLStableId& ItemId,
    const int64 Quantity,
    const FCMLItemDefinition& Definition)
{
    FCMLInventorySlotPresentation Slot;
    Slot.SlotIndex = SlotIndex;
    Slot.ItemId = ItemId;
    Slot.Quantity = Quantity;

    if (const FItemAppearance* Appearance = Appearances().Find(ItemId))
    {
        Slot.DisplayName = Appearance->DisplayName;
        Slot.IconKind = Appearance->IconKind;
        Slot.AccentColor = Appearance->AccentColor;
    }
    else
    {
        Slot.DisplayName = HumanizeKey(Definition.Identity.Key);
        Slot.IconKind = ECMLInventoryIconKind::Generic;
        Slot.AccentColor = FLinearColor(0.55f, 0.67f, 0.55f, 1.0f);
    }
    return Slot;
}

FCMLInventorySlotPresentation FCMLInventoryHudPresenter::ProjectToolSlot(
    const int32 SlotIndex,
    const FCMLStableId& ItemId,
    const int64 Quantity,
    const FCMLItemDefinition& Definition,
    const int32 CurrentDurability,
    const int32 MaximumDurability)
{
    FCMLInventorySlotPresentation Slot =
        ProjectSlot(SlotIndex, ItemId, Quantity, Definition);
    if (MaximumDurability <= 0)
    {
        return Slot;
    }
    Slot.bHasDurability = true;
    Slot.CurrentDurability = FMath::Clamp(CurrentDurability, 0, MaximumDurability);
    Slot.MaximumDurability = MaximumDurability;
    Slot.Durability01 =
        static_cast<float>(Slot.CurrentDurability) / static_cast<float>(MaximumDurability);
    return Slot;
}

bool FCMLInventoryHudPresenter::TryProject(
    const FCMLInventoryState& Inventory,
    const FCMLItemCatalog& Catalog,
    FCMLInventoryUiSnapshot& OutSnapshot)
{
    OutSnapshot = FCMLInventoryUiSnapshot();
    if (Inventory.Slots.Num() != PlayerSlotCount)
    {
        // The HUD draws a fixed grid. A different slot count means this is not
        // the player's inventory, and drawing it anyway would put items in the
        // wrong squares.
        return false;
    }

    OutSnapshot.Source = Inventory;
    OutSnapshot.Slots.Reserve(PlayerSlotCount);
    for (int32 Index = 0; Index < PlayerSlotCount; ++Index)
    {
        const FCMLInventorySlot& Slot = Inventory.Slots[Index];
        if (!Slot.bHasStack)
        {
            OutSnapshot.Slots.Add(EmptySlot(Index));
            continue;
        }

        FCMLItemDefinition Definition;
        if (!Catalog.TryGetItem(Slot.Stack.ItemId, Definition))
        {
            // An item the catalog does not define means the HUD and the
            // simulation disagree about the world. Better caught than drawn.
            OutSnapshot = FCMLInventoryUiSnapshot();
            return false;
        }

        // Deliberately without wear. Unity read durability off the stack; this
        // port keeps it in FCMLToolState, outside the canonical inventory, so
        // the projection has none to read. Showing a full bar here would be an
        // invention, and a worn pickaxe would draw as new. A caller that holds
        // the tool state calls ProjectToolSlot for that slot instead.
        const int64 Quantity = static_cast<int64>(Slot.Stack.Quantity.Value);
        OutSnapshot.Slots.Add(ProjectSlot(Index, Slot.Stack.ItemId, Quantity, Definition));
    }
    return true;
}

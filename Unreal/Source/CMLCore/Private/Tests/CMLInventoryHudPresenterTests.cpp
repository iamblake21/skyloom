#include "Presentation/CMLInventoryHudPresenter.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"

namespace
{
    FCMLItemDefinition Item(const FCMLStableId& Id, const TCHAR* Key, const int32 Durability = 0)
    {
        FCMLItemDefinition Definition;
        Definition.ItemId = Id;
        Definition.MaxStack = Durability > 0 ? 1 : 99;
        Definition.MaximumDurability = Durability;
        Definition.Identity.Key = Key;
        Definition.Identity.NameKey = FString(TEXT("name.")) + Key;
        return Definition;
    }

    const FCMLStableId Unlisted(CMLContentIds::ItemKind, 0xFF);

    FCMLItemCatalog MakeCatalog()
    {
        FCMLItemCatalog Catalog;
        Catalog.Items.Add(Item(CMLContentIds::RawIron, TEXT("item.raw_iron")));
        Catalog.Items.Add(Item(CMLContentIds::Stone, TEXT("item.stone")));
        Catalog.Items.Add(Item(CMLContentIds::CrudePickaxe, TEXT("item.crude_pickaxe"), 60));
        Catalog.Items.Add(Item(Unlisted, TEXT("item.experimental_widget")));
        return Catalog;
    }

    FCMLInventoryState MakeInventory(const int32 SlotCount)
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = CMLContentIds::PlayerInventory;
        Inventory.Slots.SetNum(SlotCount);
        return Inventory;
    }

    void Place(FCMLInventoryState& Inventory, const int32 Index, const FCMLStableId& Id, const uint64 Quantity)
    {
        Inventory.Slots[Index].bHasStack = true;
        Inventory.Slots[Index].Stack.ItemId = Id;
        Inventory.Slots[Index].Stack.Quantity.Value = Quantity;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLInventoryHudPresenterTest,
    "CML.Core.Presentation.InventoryHud",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLInventoryHudPresenterTest::RunTest(const FString& Parameters)
{
    const FCMLItemCatalog Catalog = MakeCatalog();
    const int32 SlotCount = FCMLInventoryHudPresenter::PlayerSlotCount;

    // Every slot is projected, in place, empty ones included: which position
    // holds what is what the grid draws.
    {
        FCMLInventoryState Inventory = MakeInventory(SlotCount);
        Place(Inventory, 0, CMLContentIds::RawIron, 12);
        Place(Inventory, 5, CMLContentIds::Stone, 3);

        FCMLInventoryUiSnapshot Snapshot;
        TestTrue(TEXT("A well-formed inventory projects"),
            FCMLInventoryHudPresenter::TryProject(Inventory, Catalog, Snapshot));
        TestEqual(TEXT("Every slot is present"), Snapshot.Slots.Num(), SlotCount);

        TestTrue(TEXT("The first slot is occupied"), Snapshot.Slots[0].IsOccupied());
        TestEqual(TEXT("Its name is the Italian one"),
            Snapshot.Slots[0].DisplayName, FString(TEXT("Ferro grezzo")));
        TestEqual(TEXT("Its icon is the ore icon"),
            static_cast<int32>(Snapshot.Slots[0].IconKind),
            static_cast<int32>(ECMLInventoryIconKind::Ore));
        TestEqual(TEXT("Its quantity carries over"),
            Snapshot.Slots[0].Quantity, static_cast<int64>(12));
        TestEqual(TEXT("Slot indices are their positions"), Snapshot.Slots[5].SlotIndex, 5);
        TestEqual(TEXT("Stone keeps its own name"),
            Snapshot.Slots[5].DisplayName, FString(TEXT("Pietra")));

        TestFalse(TEXT("An untouched slot is empty"), Snapshot.Slots[1].IsOccupied());
        TestEqual(TEXT("An empty slot still knows its index"), Snapshot.Slots[1].SlotIndex, 1);
        TestEqual(TEXT("An empty slot holds nothing"),
            Snapshot.Slots[1].Quantity, static_cast<int64>(0));
    }

    // An item with no table entry still shows something readable rather than an
    // id, which is what makes a missing entry visible instead of invisible.
    {
        FCMLInventoryState Inventory = MakeInventory(SlotCount);
        Place(Inventory, 2, Unlisted, 1);

        FCMLInventoryUiSnapshot Snapshot;
        TestTrue(TEXT("An unlisted item still projects"),
            FCMLInventoryHudPresenter::TryProject(Inventory, Catalog, Snapshot));
        TestEqual(TEXT("Its name comes from its content key"),
            Snapshot.Slots[2].DisplayName, FString(TEXT("Experimental widget")));
        TestEqual(TEXT("It draws the generic icon"),
            static_cast<int32>(Snapshot.Slots[2].IconKind),
            static_cast<int32>(ECMLInventoryIconKind::Generic));
    }

    // Disagreement between the HUD and the simulation is caught, not drawn.
    {
        FCMLInventoryState Inventory = MakeInventory(SlotCount);
        Place(Inventory, 0, FCMLStableId(CMLContentIds::ItemKind, 0xDEAD), 1);
        FCMLInventoryUiSnapshot Snapshot;
        TestFalse(TEXT("An item the catalog does not define is refused"),
            FCMLInventoryHudPresenter::TryProject(Inventory, Catalog, Snapshot));
        TestEqual(TEXT("Nothing is half-projected"), Snapshot.Slots.Num(), 0);

        FCMLInventoryState Wrong = MakeInventory(SlotCount + 1);
        TestFalse(TEXT("An inventory of the wrong size is refused"),
            FCMLInventoryHudPresenter::TryProject(Wrong, Catalog, Snapshot));
    }

    // Wear is not invented. Durability lives outside the canonical inventory in
    // this port, so the plain projection must not claim a tool is undamaged.
    {
        FCMLInventoryState Inventory = MakeInventory(SlotCount);
        Place(Inventory, 3, CMLContentIds::CrudePickaxe, 1);

        FCMLInventoryUiSnapshot Snapshot;
        TestTrue(TEXT("A tool projects"),
            FCMLInventoryHudPresenter::TryProject(Inventory, Catalog, Snapshot));
        TestEqual(TEXT("It is named as a tool"),
            Snapshot.Slots[3].DisplayName, FString(TEXT("Piccone rudimentale")));
        TestFalse(TEXT("The plain projection shows no durability"),
            Snapshot.Slots[3].bHasDurability);

        FCMLItemDefinition Pickaxe;
        TestTrue(TEXT("The tool is in the catalog"),
            Catalog.TryGetItem(CMLContentIds::CrudePickaxe, Pickaxe));
        const FCMLInventorySlotPresentation Worn =
            FCMLInventoryHudPresenter::ProjectToolSlot(
                3, CMLContentIds::CrudePickaxe, 1, Pickaxe, 15, 60);
        TestTrue(TEXT("A tool projected with its state shows durability"), Worn.bHasDurability);
        TestEqual(TEXT("The bar is the ratio"), Worn.Durability01, 0.25f, 1e-6f);
        TestEqual(TEXT("It keeps the tool's name"),
            Worn.DisplayName, FString(TEXT("Piccone rudimentale")));

        // A broken tool reads zero, not "no durability": the two must not look
        // the same, which is why the flag is separate from the value.
        const FCMLInventorySlotPresentation Broken =
            FCMLInventoryHudPresenter::ProjectToolSlot(
                3, CMLContentIds::CrudePickaxe, 1, Pickaxe, 0, 60);
        TestTrue(TEXT("A broken tool still reports durability"), Broken.bHasDurability);
        TestEqual(TEXT("Its bar is empty"), Broken.Durability01, 0.0f, 1e-6f);
    }

    // The cursor preview keeps the item and changes only the amount held.
    {
        FCMLItemDefinition Ore;
        TestTrue(TEXT("Ore is in the catalog"), Catalog.TryGetItem(CMLContentIds::RawIron, Ore));
        const FCMLInventorySlotPresentation Full =
            FCMLInventoryHudPresenter::ProjectSlot(0, CMLContentIds::RawIron, 12, Ore);
        const FCMLInventorySlotPresentation Held = Full.WithQuantity(5);
        TestEqual(TEXT("Only the quantity changes"), Held.Quantity, static_cast<int64>(5));
        TestEqual(TEXT("The name is unchanged"), Held.DisplayName, Full.DisplayName);
        TestEqual(TEXT("The icon is unchanged"),
            static_cast<int32>(Held.IconKind), static_cast<int32>(Full.IconKind));
        TestTrue(TEXT("The colour is unchanged"), Held.AccentColor == Full.AccentColor);
    }
    return true;
}
#endif

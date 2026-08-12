#include "Simulation/CMLHarvestSession.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Misc/AutomationTest.h"

namespace
{
    const FCMLStableId RockId(0, 8001);
    const FCMLStableId OtherRockId(0, 8002);
    const FCMLStableId TuftId(0, 8003);
    constexpr int64 Capacity = 500;

    FCMLItemCatalog MakeCatalog(const int64 MaxStack = 99)
    {
        using namespace CMLContentIds;
        FCMLItemCatalog Catalog;
        for (const FCMLStableId& Id : {PlantFiber, Stick, Stone, RawIron})
        {
            FCMLItemDefinition Item;
            Item.ItemId = Id;
            Item.MaxStack = MaxStack;
            Catalog.Items.Add(Item);
        }
        return Catalog;
    }

    FCMLInventoryState MakeInventory(const int32 SlotCount = 16)
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = CMLContentIds::PlayerInventory;
        Inventory.ContainerDefinitionId = CMLContentIds::PlayerInventory;
        Inventory.Slots.SetNum(SlotCount);
        return Inventory;
    }

    FCMLToolState MakePickaxe(const int32 Current = 60)
    {
        FCMLToolState Tool;
        Tool.ItemId = CMLContentIds::CrudePickaxe;
        Tool.Current = Current;
        Tool.Maximum = 60;
        return Tool;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLHarvestSessionTest,
    "CML.Core.Simulation.HarvestSession",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLHarvestSessionTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLItemCatalog Catalog = MakeCatalog();

    // Blows accumulate against one source until it gives, and the source is
    // only removable once the new inventory has been published.
    {
        FCMLHarvestSession Session;
        FCMLInventoryState Inventory = MakeInventory();
        FCMLToolState Tool = MakePickaxe();

        int32 Blows = 0;
        FCMLHarvestOutcome Outcome;
        do
        {
            Outcome = Session.Strike(
                Inventory, Catalog, Tool, RockId,
                ECMLMiningTarget::EnvironmentalStone, Capacity);
            Inventory = Outcome.UpdatedInventory;
            Tool = Outcome.UpdatedTool;
            ++Blows;
        }
        while (!Outcome.bProduced && Blows < 32);

        TestTrue(TEXT("The rock eventually gives"), Outcome.bProduced);
        TestTrue(TEXT("It took more than one blow"), Blows > 1);
        TestTrue(TEXT("And produced stone"), Outcome.ProducedItemId == Stone);
        TestEqual(TEXT("Which is in the pack"),
            FCMLInventoryOperations::Count(Inventory, Stone), static_cast<int64>(1));
        TestTrue(TEXT("The tool wore down"), Tool.Current < 60);

        // Cleared, so a fresh rock does not inherit this one's tally.
        TestEqual(TEXT("The finished source's progress is forgotten"),
            Session.GetCompletedHits(RockId), 0);
    }

    // Progress is per source: working one rock does not advance another.
    {
        FCMLHarvestSession Session;
        const FCMLInventoryState Inventory = MakeInventory();
        const FCMLToolState Tool = MakePickaxe();

        Session.Strike(Inventory, Catalog, Tool, RockId,
            ECMLMiningTarget::EnvironmentalStone, Capacity);
        TestTrue(TEXT("The struck rock has progress"),
            Session.GetCompletedHits(RockId) > 0);
        TestEqual(TEXT("The untouched one has none"),
            Session.GetCompletedHits(OtherRockId), 0);

        Session.Forget(RockId);
        TestEqual(TEXT("Forgetting a source clears it"),
            Session.GetCompletedHits(RockId), 0);
    }

    // A blow that cannot be stored still counts. Discarding the progress would
    // let a player mine forever without ever finishing.
    {
        FCMLHarvestSession Session;
        const FCMLToolState Tool = MakePickaxe();
        // One slot, already full of something else.
        FCMLInventoryState Cramped = MakeInventory(1);
        Cramped.Slots[0].bHasStack = true;
        Cramped.Slots[0].Stack.ItemId = PlantFiber;
        Cramped.Slots[0].Stack.Quantity.Value = 99;

        int32 Previous = 0;
        bool bSawFull = false;
        for (int32 Blow = 0; Blow < 32; ++Blow)
        {
            const FCMLHarvestOutcome Outcome = Session.Strike(
                Cramped, Catalog, Tool, RockId,
                ECMLMiningTarget::EnvironmentalStone, Capacity);
            if (Outcome.MiningStatus == ECMLMiningImpactStatus::InventoryFull)
            {
                bSawFull = true;
                TestFalse(TEXT("Nothing was produced"), Outcome.bProduced);
                TestFalse(TEXT("And the rock stays standing"), Outcome.bSourceExhausted);
                break;
            }
            TestTrue(TEXT("Progress advances"), Outcome.CompletedHits > Previous);
            Previous = Outcome.CompletedHits;
        }
        TestTrue(TEXT("A full pack is reported, not silently ignored"), bSawFull);
        TestTrue(TEXT("And the blows still counted"),
            Session.GetCompletedHits(RockId) > 0);
    }

    // A refused blow changes nothing at all — not even the tally.
    {
        FCMLHarvestSession Session;
        const FCMLInventoryState Inventory = MakeInventory();
        const FCMLToolState Broken = MakePickaxe(0);

        const FCMLHarvestOutcome Outcome = Session.Strike(
            Inventory, Catalog, Broken, RockId,
            ECMLMiningTarget::EnvironmentalStone, Capacity);
        TestFalse(TEXT("A broken tool produces nothing"), Outcome.bProduced);
        TestEqual(TEXT("And does not wear the rock"),
            Session.GetCompletedHits(RockId), 0);
    }

    // Gathering is one gesture: it takes the whole source or leaves it standing.
    {
        FCMLHarvestSession Session;
        const FCMLInventoryState Inventory = MakeInventory();

        const FCMLHarvestOutcome Picked = Session.Gather(
            Inventory, Catalog, TuftId, ECMLHandGatherTarget::WildFiberTuft, 2, Capacity);
        TestTrue(TEXT("A tuft is picked in one go"), Picked.bProduced);
        TestTrue(TEXT("It yields fibre"), Picked.ProducedItemId == PlantFiber);
        TestEqual(TEXT("Both of them"), Picked.ProducedQuantity, static_cast<int64>(2));
        TestTrue(TEXT("And the tuft is gone"), Picked.bSourceExhausted);
        TestEqual(TEXT("Which is in the pack"),
            FCMLInventoryOperations::Count(Picked.UpdatedInventory, PlantFiber),
            static_cast<int64>(2));

        FCMLInventoryState Full = MakeInventory(1);
        Full.Slots[0].bHasStack = true;
        Full.Slots[0].Stack.ItemId = Stone;
        Full.Slots[0].Stack.Quantity.Value = 99;
        const FCMLHarvestOutcome Refused = Session.Gather(
            Full, Catalog, TuftId, ECMLHandGatherTarget::WildFiberTuft, 2, Capacity);
        TestFalse(TEXT("A full pack picks nothing"), Refused.bProduced);
        TestFalse(TEXT("And leaves the tuft standing"), Refused.bSourceExhausted);
        TestEqual(TEXT("So no matter is created"),
            FCMLInventoryOperations::Count(Refused.UpdatedInventory, PlantFiber),
            static_cast<int64>(0));
    }
    return true;
}
#endif

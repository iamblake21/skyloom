#include "Simulation/CMLHarvestRules.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"

namespace
{
    FCMLItemCatalog MakeCatalog(const int64 MaxStack = 10)
    {
        FCMLItemCatalog Catalog;
        Catalog.Items.Add({FCMLHarvestRules::PlantFiber(), MaxStack});
        Catalog.Items.Add({FCMLHarvestRules::Stick(), MaxStack});
        Catalog.Items.Add({FCMLHarvestRules::Stone(), MaxStack});
        Catalog.Items.Add({FCMLHarvestRules::RawIron(), MaxStack});
        Catalog.Items.Add({FCMLHarvestRules::RawCopper(), MaxStack});
        Catalog.Items.Add({FCMLHarvestRules::RawTin(), MaxStack});
        return Catalog;
    }

    FCMLInventoryState MakeInventory(const int32 SlotCount)
    {
        FCMLInventoryState Inventory;
        Inventory.InventoryId = FCMLStableId(0, 7);
        Inventory.Slots.AddDefaulted(SlotCount);
        return Inventory;
    }

    FCMLToolState MakePickaxe(const int32 Current = 10)
    {
        FCMLToolState Tool;
        Tool.ItemId = FCMLHarvestRules::CrudePickaxe();
        Tool.Current = Current;
        Tool.Maximum = 10;
        return Tool;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLHarvestRulesTest,
    "CML.Core.Simulation.HarvestRules",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLHarvestRulesTest::RunTest(const FString& Parameters)
{
    const FCMLItemCatalog Catalog = MakeCatalog();
    const int64 Capacity = 1000;

    // The ids are hashed into the canonical state, so they are checked against
    // the values Unity's ContentIds declares rather than assumed.
    TestEqual(TEXT("Stone id low half"),
        FCMLHarvestRules::Stone().Low, static_cast<uint64>(0x10));
    TestEqual(TEXT("Stone id high half"),
        FCMLHarvestRules::Stone().High, static_cast<uint64>(0x1000000000000000ULL));
    TestEqual(TEXT("PlantFiber id"),
        FCMLHarvestRules::PlantFiber().Low, static_cast<uint64>(0x19));
    TestEqual(TEXT("Stick id"), FCMLHarvestRules::Stick().Low, static_cast<uint64>(0x1A));
    TestEqual(TEXT("RawIron id"), FCMLHarvestRules::RawIron().Low, static_cast<uint64>(0x01));
    TestEqual(TEXT("RawCopper id"), FCMLHarvestRules::RawCopper().Low, static_cast<uint64>(0x16));
    TestEqual(TEXT("RawTin id"), FCMLHarvestRules::RawTin().Low, static_cast<uint64>(0x17));
    TestEqual(TEXT("CrudePickaxe id"),
        FCMLHarvestRules::CrudePickaxe().Low, static_cast<uint64>(0x0F));
    TestEqual(TEXT("IronPickaxe id"),
        FCMLHarvestRules::IronPickaxe().Low, static_cast<uint64>(0x11));

    // Gathering by hand needs no tool and commits its whole yield at once.
    {
        const FCMLInventoryState Inventory = MakeInventory(2);
        const FCMLHandGatherResult Result = FCMLHarvestRules::Gather(
            Inventory, Catalog, ECMLHandGatherTarget::WildFiberTuft, 2, Capacity);
        TestTrue(TEXT("A tuft is gathered"), Result.Gathered());
        TestTrue(TEXT("Fibre is produced"), Result.ProducedItemId == FCMLHarvestRules::PlantFiber());
        TestEqual(TEXT("Both units arrive"),
            FCMLInventoryOperations::Count(Result.UpdatedInventory, FCMLHarvestRules::PlantFiber()),
            static_cast<int64>(2));
    }

    // A pebble yields the same Stone a pickaxe frees from a boulder: picked-up
    // matter is the same matter, not a separate currency.
    {
        const FCMLHandGatherResult Result = FCMLHarvestRules::Gather(
            MakeInventory(1), Catalog, ECMLHandGatherTarget::LoosePebble, 1, Capacity);
        TestTrue(TEXT("A pebble yields Stone"), Result.ProducedItemId == FCMLHarvestRules::Stone());
    }

    // The whole yield commits or none of it does: a partial store would let a
    // nearly full inventory swallow one fibre of two and still consume the tuft.
    {
        FCMLInventoryState Full = MakeInventory(1);
        Full.Slots[0].bHasStack = true;
        Full.Slots[0].Stack.ItemId = FCMLHarvestRules::PlantFiber();
        Full.Slots[0].Stack.Quantity = FCMLNonNegativeQuantity(9);

        const FCMLHandGatherResult Result = FCMLHarvestRules::Gather(
            Full, Catalog, ECMLHandGatherTarget::WildFiberTuft, 2, Capacity);
        TestFalse(TEXT("A partial fit is refused"), Result.Gathered());
        TestEqual(TEXT("Status is InventoryFull"),
            static_cast<int32>(Result.Status),
            static_cast<int32>(ECMLHandGatherStatus::InventoryFull));
        TestEqual(TEXT("Not one fibre was taken"),
            FCMLInventoryOperations::Count(Result.UpdatedInventory, FCMLHarvestRules::PlantFiber()),
            static_cast<int64>(9));
    }

    {
        const FCMLHandGatherResult Zero = FCMLHarvestRules::Gather(
            MakeInventory(2), Catalog, ECMLHandGatherTarget::WildFiberTuft, 0, Capacity);
        TestEqual(TEXT("A zero yield is refused"),
            static_cast<int32>(Zero.Status), static_cast<int32>(ECMLHandGatherStatus::InvalidYield));
        const FCMLHandGatherResult Unknown = FCMLHarvestRules::Gather(
            MakeInventory(2), Catalog, ECMLHandGatherTarget::None, 1, Capacity);
        TestEqual(TEXT("An unknown target is refused"),
            static_cast<int32>(Unknown.Status),
            static_cast<int32>(ECMLHandGatherStatus::InvalidTarget));
    }

    // Mining is written around a tool. An empty hand is refused, which is the
    // check that stops a player mining stone with their fists.
    {
        const FCMLMiningImpactResult BareHand = FCMLHarvestRules::Impact(
            MakeInventory(2), Catalog, FCMLToolState(),
            ECMLMiningTarget::EnvironmentalStone, 0, Capacity);
        TestEqual(TEXT("An empty hand cannot mine"),
            static_cast<int32>(BareHand.Status),
            static_cast<int32>(ECMLMiningImpactStatus::WrongTool));

        FCMLToolState Broken = MakePickaxe(0);
        const FCMLMiningImpactResult BrokenResult = FCMLHarvestRules::Impact(
            MakeInventory(2), Catalog, Broken, ECMLMiningTarget::EnvironmentalStone, 0, Capacity);
        TestEqual(TEXT("A broken tool cannot mine"),
            static_cast<int32>(BrokenResult.Status),
            static_cast<int32>(ECMLMiningImpactStatus::BrokenTool));
    }

    // A crude pickaxe needs four impacts. The first three only progress, and
    // cost no durability: the tool is spent on what it produces.
    {
        TestEqual(TEXT("A crude pickaxe needs four hits"),
            FCMLHarvestRules::RequiredHits(FCMLHarvestRules::CrudePickaxe()), 4);
        TestEqual(TEXT("An iron pickaxe needs two"),
            FCMLHarvestRules::RequiredHits(FCMLHarvestRules::IronPickaxe()), 2);

        FCMLInventoryState Inventory = MakeInventory(2);
        FCMLToolState Tool = MakePickaxe();
        int32 Progress = 0;
        for (int32 Hit = 0; Hit < 3; ++Hit)
        {
            const FCMLMiningImpactResult Step = FCMLHarvestRules::Impact(
                Inventory, Catalog, Tool, ECMLMiningTarget::EnvironmentalStone, Progress, Capacity);
            TestEqual(TEXT("The impact only progresses"),
                static_cast<int32>(Step.Status),
                static_cast<int32>(ECMLMiningImpactStatus::Progressed));
            TestEqual(TEXT("Progress costs no durability"), Step.UpdatedTool.Current, Tool.Current);
            Progress = Step.NextHitProgress;
        }
        TestEqual(TEXT("Three hits are recorded"), Progress, 3);

        const FCMLMiningImpactResult Final = FCMLHarvestRules::Impact(
            Inventory, Catalog, Tool, ECMLMiningTarget::EnvironmentalStone, Progress, Capacity);
        TestEqual(TEXT("The fourth impact produces"),
            static_cast<int32>(Final.Status),
            static_cast<int32>(ECMLMiningImpactStatus::Produced));
        TestEqual(TEXT("Stone is produced"),
            FCMLInventoryOperations::Count(Final.UpdatedInventory, FCMLHarvestRules::Stone()),
            static_cast<int64>(1));
        TestEqual(TEXT("One durability point is spent"), Final.UpdatedTool.Current, 9);
        TestEqual(TEXT("Progress resets"), Final.NextHitProgress, 0);
        TestTrue(TEXT("A loose rock is exhausted"), Final.bSourceExhausted);
    }

    // A deposit surface survives being mined; a loose rock does not.
    {
        const FCMLMiningImpactResult Deposit = FCMLHarvestRules::Impact(
            MakeInventory(2), Catalog, MakePickaxe(),
            ECMLMiningTarget::IronDepositSurface, 3, Capacity);
        TestEqual(TEXT("The deposit produces ore"),
            static_cast<int32>(Deposit.Status),
            static_cast<int32>(ECMLMiningImpactStatus::Produced));
        TestFalse(TEXT("A deposit surface survives"), Deposit.bSourceExhausted);
    }

    // Every deposit carries its own raw ore contract. Raised modules exhaust;
    // the G0 floor modules remain infinite for hand mining and drill placement.
    {
        const FCMLMiningImpactResult CopperRock = FCMLHarvestRules::Impact(
            MakeInventory(2), Catalog, MakePickaxe(),
            ECMLMiningTarget::CopperOreRock, 3, Capacity);
        TestTrue(TEXT("A copper rock yields raw copper"),
            CopperRock.ProducedItemId == FCMLHarvestRules::RawCopper());
        TestTrue(TEXT("A copper rock is finite"), CopperRock.bSourceExhausted);

        const FCMLMiningImpactResult TinSurface = FCMLHarvestRules::Impact(
            MakeInventory(2), Catalog, MakePickaxe(),
            ECMLMiningTarget::TinDepositSurface, 3, Capacity);
        TestTrue(TEXT("A tin floor yields raw tin"),
            TinSurface.ProducedItemId == FCMLHarvestRules::RawTin());
        TestFalse(TEXT("A tin floor is infinite"), TinSurface.bSourceExhausted);
    }

    // A full inventory keeps the source one impact from completion, so the
    // next real impact retries the whole transaction.
    {
        FCMLInventoryState Full = MakeInventory(1);
        Full.Slots[0].bHasStack = true;
        Full.Slots[0].Stack.ItemId = FCMLHarvestRules::Stick();
        Full.Slots[0].Stack.Quantity = FCMLNonNegativeQuantity(10);

        const FCMLToolState Tool = MakePickaxe();
        const FCMLMiningImpactResult Result = FCMLHarvestRules::Impact(
            Full, Catalog, Tool, ECMLMiningTarget::EnvironmentalStone, 3, Capacity);
        TestEqual(TEXT("A full inventory blocks the yield"),
            static_cast<int32>(Result.Status),
            static_cast<int32>(ECMLMiningImpactStatus::InventoryFull));
        TestEqual(TEXT("The source waits one impact from completion"), Result.NextHitProgress, 3);
        TestEqual(TEXT("No durability is spent on a refused impact"),
            Result.UpdatedTool.Current, Tool.Current);
        TestFalse(TEXT("The source is not exhausted"), Result.bSourceExhausted);
    }
    return true;
}
#endif

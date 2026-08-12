#include "Presentation/CMLMachineHudPresenter.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "Content/CMLContentIds.h"
#include "Misc/AutomationTest.h"

namespace
{
    FCMLDefinitionIdentity Named(const TCHAR* Key)
    {
        FCMLDefinitionIdentity Identity;
        Identity.Key = Key;
        Identity.NameKey = FString(TEXT("name.")) + Key;
        return Identity;
    }

    FCMLGameCatalog MakeCatalog()
    {
        using namespace CMLContentIds;
        FCMLGameCatalog Catalog;
        Catalog.Revision.Value = TEXT("rev-1");
        Catalog.Items.Add({RawIron, 99, 0, Named(TEXT("item.raw_iron"))});
        Catalog.Items.Add({IronIngot, 99, 0, Named(TEXT("item.iron_ingot"))});
        Catalog.Items.Add({WoodLog, 99, 0, Named(TEXT("item.wood_log"))});

        FCMLRecipeDefinition Recipe;
        Recipe.RecipeId = SmeltIronIngot;
        Recipe.Station = ECMLCraftingStationKind::Machine;
        Recipe.Inputs.Add({RawIron, 2});
        Recipe.Outputs.Add({IronIngot, 1});
        Recipe.DurationMilliseconds = 4000;
        Recipe.Identity = Named(TEXT("recipe.smelt_iron_ingot"));
        Catalog.Recipes.Add(Recipe);

        FCMLMachineDefinition Furnace;
        Furnace.Id = CrudeFurnace;
        Furnace.InputSlots = 2;
        Furnace.OutputSlots = 2;
        Furnace.RequiredEnergyKind = ECMLEnergyKind::Thermal;
        Furnace.RequiredPower = 0;
        Furnace.SupportedRecipeIds.Add(SmeltIronIngot);
        Furnace.FuelSlots = 1;
        Furnace.FuelItemId = WoodLog;
        Furnace.FuelQuantityPerCycle = 3;
        Furnace.Identity = Named(TEXT("machine.crude_furnace"));
        Catalog.Machines.Add(Furnace);

        Catalog.Containers.Add({WoodenCrate, 8, 100, Named(TEXT("container.wooden_crate"))});
        return Catalog;
    }

    FCMLMachinePort MakePort(const ECMLMachinePortKind Kind, const int32 SlotCount)
    {
        FCMLMachinePort Port;
        Port.Kind = Kind;
        Port.Slots.SetNum(SlotCount);
        return Port;
    }

    void Fill(FCMLMachinePort& Port, const int32 Index, const FCMLStableId& Id, const uint64 Quantity)
    {
        Port.Slots[Index].ItemId = Id;
        Port.Slots[Index].Quantity.Value = Quantity;
    }

    FCMLMachineNodeState MakeFurnaceNode()
    {
        using namespace CMLContentIds;
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, 500);
        Node.Kind = ECMLMachineNodeKind::Machine;
        Node.DefinitionId = CrudeFurnace;
        Node.ActiveRecipeId = SmeltIronIngot;
        Node.Input = MakePort(ECMLMachinePortKind::Input, 2);
        Node.Output = MakePort(ECMLMachinePortKind::Output, 2);
        Node.bHasFuelPort = true;
        Node.Fuel = MakePort(ECMLMachinePortKind::Fuel, 1);
        return Node;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(
    FCMLMachineHudPresenterTest,
    "CML.Core.Presentation.MachineHud",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FCMLMachineHudPresenterTest::RunTest(const FString& Parameters)
{
    using namespace CMLContentIds;
    const FCMLGameCatalog Catalog = MakeCatalog();

    // A running furnace: the panel names the machine, the recipe, and shows
    // progress as integer thousandths of the authoritative milliseconds.
    {
        FCMLMachineNodeState Node = MakeFurnaceNode();
        Node.Activity = ECMLMachineActivity::Running;
        Node.bIsCycleActive = true;
        Node.ProgressMilliseconds = 1000;
        Node.CompletedCycles = 7;
        Fill(Node.Input, 0, RawIron, 4);
        Fill(Node.Fuel, 0, WoodLog, 5);
        Fill(Node.Output, 0, IronIngot, 2);

        const FCMLMachineNodeReport Report = FCMLMachineDiagnostics::Describe(Node, Catalog);
        const FCMLMachineUiSnapshot Snapshot =
            FCMLMachineHudPresenter::Project(Report, Catalog);

        TestEqual(TEXT("The machine is named in Italian"),
            Snapshot.Title, FString(TEXT("Fornace rudimentale")));
        TestEqual(TEXT("The recipe is named by its product"),
            Snapshot.RecipeName, FString(TEXT("Lingotto di ferro")));
        TestEqual(TEXT("The cause reads as running"),
            Snapshot.CauseText, FString(TEXT("In lavorazione")));
        TestFalse(TEXT("A running machine is not blocked"), Snapshot.bIsBlocked);
        TestEqual(TEXT("Progress is a quarter of the cycle"), Snapshot.ProgressPermille, 250);
        TestEqual(TEXT("The bar's label rounds down to a percentage"),
            Snapshot.ProgressText(), FString(TEXT("25%")));
        TestEqual(TEXT("Completed cycles carry over"),
            Snapshot.CompletedCycles, static_cast<int64>(7));
        TestTrue(TEXT("A running machine reports no shortfall"),
            Snapshot.ShortfallText.IsEmpty());

        TestEqual(TEXT("Input, fuel and output are all shown"), Snapshot.Ports.Num(), 3);
        TestEqual(TEXT("The input port is titled"),
            Snapshot.Ports[0].Title, FString(TEXT("INGRESSO")));
        TestEqual(TEXT("The fuel port is titled"),
            Snapshot.Ports[1].Title, FString(TEXT("COMBUSTIBILE")));
        TestEqual(TEXT("The output port is titled"),
            Snapshot.Ports[2].Title, FString(TEXT("USCITA")));
        TestEqual(TEXT("A port slot looks like the same item in the backpack"),
            Snapshot.Ports[0].Slots[0].DisplayName, FString(TEXT("Ferro grezzo")));
        TestFalse(TEXT("An unfilled port slot is empty"),
            Snapshot.Ports[0].Slots[1].IsOccupied());
        TestEqual(TEXT("The port total is the sum of its slots"),
            Snapshot.Ports[0].TotalQuantity, static_cast<int64>(4));
    }

    // "Missing input" alone does not say which input. Naming the item and the
    // amount is what the player has to act on.
    {
        FCMLMachineNodeState Node = MakeFurnaceNode();
        Node.Activity = ECMLMachineActivity::MissingInput;
        Fill(Node.Input, 0, RawIron, 1);

        const FCMLMachineNodeReport Report = FCMLMachineDiagnostics::Describe(Node, Catalog);
        TestEqual(TEXT("One input is short"), Report.Shortfalls.Num(), 1);
        TestEqual(TEXT("It is short by one"),
            Report.Shortfalls[0].Missing(), static_cast<int64>(1));

        const FCMLMachineUiSnapshot Snapshot =
            FCMLMachineHudPresenter::Project(Report, Catalog);
        TestTrue(TEXT("A stalled machine is blocked"), Snapshot.bIsBlocked);
        TestEqual(TEXT("The shortfall names the item and the amount"),
            Snapshot.ShortfallText, FString(TEXT("1 × Ferro grezzo")));
    }

    // Fuel is reported the same way, against the machine's own fuel item.
    {
        FCMLMachineNodeState Node = MakeFurnaceNode();
        Node.Activity = ECMLMachineActivity::MissingFuel;

        const FCMLMachineNodeReport Report = FCMLMachineDiagnostics::Describe(Node, Catalog);
        const FCMLMachineUiSnapshot Snapshot =
            FCMLMachineHudPresenter::Project(Report, Catalog);
        TestEqual(TEXT("The cause reads as missing fuel"),
            Snapshot.CauseText, FString(TEXT("Manca combustibile")));
        TestEqual(TEXT("The whole cycle's fuel is missing"),
            Snapshot.ShortfallText, FString(TEXT("3 × Tronco")));
    }

    // A buffer's input and output are the same port. Reporting it twice would
    // show a crate's contents in two panels and double its total.
    {
        FCMLMachineNodeState Node;
        Node.Id = FCMLStableId(0, 501);
        Node.Kind = ECMLMachineNodeKind::Buffer;
        Node.DefinitionId = WoodenCrate;
        Node.Activity = ECMLMachineActivity::Idle;
        Node.Input = MakePort(ECMLMachinePortKind::Storage, 2);
        Fill(Node.Input, 0, IronIngot, 6);
        Node.bInputOutputAliased = true;
        Node.Output = Node.Input;

        const FCMLMachineNodeReport Report = FCMLMachineDiagnostics::Describe(Node, Catalog);
        const FCMLMachineUiSnapshot Snapshot =
            FCMLMachineHudPresenter::Project(Report, Catalog);
        TestEqual(TEXT("An aliased port is shown once"), Snapshot.Ports.Num(), 1);
        TestEqual(TEXT("It is titled as contents"),
            Snapshot.Ports[0].Title, FString(TEXT("CONTENUTO")));
        TestEqual(TEXT("A crate is named as a crate"),
            Snapshot.Title, FString(TEXT("Cassa di legno")));
        TestEqual(TEXT("An idle buffer reads as storage"),
            Snapshot.CauseText, FString(TEXT("Deposito")));
        TestFalse(TEXT("An idle buffer is not blocked"), Snapshot.bIsBlocked);
    }

    // The belt line's state joins the status line, and shares its separator with
    // any shortfall rather than replacing it.
    {
        FCMLMachineNodeState Node = MakeFurnaceNode();
        Node.Kind = ECMLMachineNodeKind::BeltModule;
        Node.DefinitionId = BeltStraight;
        Node.ActiveRecipeId = FCMLStableId::None();
        Node.Activity = ECMLMachineActivity::Idle;
        Node.bHasFuelPort = false;
        Node.BeltLineStatus = ECMLBeltLineStatus::Overloaded;
        Node.BeltLineUsedCapacity = 12;
        Node.BeltLineAvailableCapacity = 8;

        const FCMLMachineNodeReport Report = FCMLMachineDiagnostics::Describe(Node, Catalog);
        const FCMLMachineUiSnapshot Snapshot =
            FCMLMachineHudPresenter::Project(Report, Catalog);
        TestEqual(TEXT("The overload is spelled out"),
            Snapshot.ShortfallText, FString(TEXT("Sovraccarico linea - 12/8 elementi")));
        TestEqual(TEXT("An unnamed recipe shows nothing"), Snapshot.RecipeName, FString());
        TestEqual(TEXT("Progress with no recipe is zero"), Snapshot.ProgressPermille, 0);

        Node.BeltLineStatus = ECMLBeltLineStatus::MissingDrive;
        const FCMLMachineUiSnapshot Missing = FCMLMachineHudPresenter::Project(
            FCMLMachineDiagnostics::Describe(Node, Catalog), Catalog);
        TestEqual(TEXT("A driveless line says so"),
            Missing.ShortfallText, FString(TEXT("Nastro motore mancante")));
    }

    // Every activity has a cause key and Italian text: a machine that showed an
    // empty string would read as merely stopped.
    {
        const ECMLMachineActivity Every[] = {
            ECMLMachineActivity::Idle, ECMLMachineActivity::Running,
            ECMLMachineActivity::NoRecipe, ECMLMachineActivity::MissingInput,
            ECMLMachineActivity::OutputFull, ECMLMachineActivity::MissingFuel};
        for (const ECMLMachineActivity Activity : Every)
        {
            TestFalse(TEXT("The cause key is not empty"),
                CMLMachineCauseKeys::For(Activity).IsEmpty());
            TestFalse(TEXT("The Italian text is not empty"),
                FCMLMachineHudPresenter::CauseText(Activity).IsEmpty());
        }
    }
    return true;
}
#endif

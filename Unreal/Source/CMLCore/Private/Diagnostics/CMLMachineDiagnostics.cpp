#include "Diagnostics/CMLMachineDiagnostics.h"

namespace
{
    /** How much of one item a port holds, across all its slots. */
    int64 CountInPort(const FCMLMachinePort& Port, const FCMLStableId& ItemId)
    {
        int64 Total = 0;
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId == ItemId)
            {
                Total += static_cast<int64>(Slot.Quantity.Value);
            }
        }
        return Total;
    }

    int64 TotalInPort(const FCMLMachinePort& Port)
    {
        int64 Total = 0;
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            Total += static_cast<int64>(Slot.Quantity.Value);
        }
        return Total;
    }

    FCMLMachinePortReport DescribePort(
        const FCMLMachinePort& Port, const FCMLGameCatalog& Catalog)
    {
        FCMLMachinePortReport Report;
        Report.Kind = Port.Kind;
        Report.TotalQuantity = TotalInPort(Port);
        Report.Slots.Reserve(Port.Slots.Num());
        for (int32 Index = 0; Index < Port.Slots.Num(); ++Index)
        {
            const FCMLMachineSlot& Slot = Port.Slots[Index];
            FCMLMachineSlotReport& SlotReport = Report.Slots.AddDefaulted_GetRef();
            SlotReport.SlotIndex = Index;
            SlotReport.ItemId = Slot.ItemId;
            SlotReport.Quantity = static_cast<int64>(Slot.Quantity.Value);

            FCMLItemDefinition Item;
            if (!Slot.ItemId.IsNone() && Catalog.TryGetItem(Slot.ItemId, Item))
            {
                SlotReport.ItemKey = Item.Identity.Key;
                SlotReport.MaxStack = Item.MaxStack;
            }
        }
        return Report;
    }

    /** The content key naming what a node *is*, which depends on its kind. */
    FString DescribeDefinition(
        const FCMLMachineNodeState& Node, const FCMLGameCatalog& Catalog)
    {
        if (Node.Kind == ECMLMachineNodeKind::Buffer)
        {
            FCMLContainerDefinition Container;
            return Catalog.TryGetContainer(Node.DefinitionId, Container)
                ? Container.Identity.Key : FString();
        }
        // A funnel and a belt module are placeable items, not containers and not
        // machines, so their key comes from the item table.
        if (Node.Kind == ECMLMachineNodeKind::Funnel
            || Node.Kind == ECMLMachineNodeKind::BeltModule)
        {
            FCMLItemDefinition Item;
            return Catalog.TryGetItem(Node.DefinitionId, Item)
                ? Item.Identity.Key : FString();
        }
        FCMLMachineDefinition Machine;
        return Catalog.TryGetMachine(Node.DefinitionId, Machine)
            ? Machine.Identity.Key : FString();
    }

    TArray<FCMLMachineShortfallReport> DescribeShortfalls(
        const FCMLMachineNodeState& Node,
        const FCMLRecipeDefinition& Recipe,
        const bool bHasRecipe,
        const FCMLGameCatalog& Catalog)
    {
        TArray<FCMLMachineShortfallReport> Shortfalls;

        if (Node.Activity == ECMLMachineActivity::MissingFuel)
        {
            FCMLMachineDefinition Machine;
            if (!Catalog.TryGetMachine(Node.DefinitionId, Machine) || Machine.FuelSlots <= 0)
            {
                return Shortfalls;
            }
            FCMLItemDefinition FuelItem;
            FCMLMachineShortfallReport& Report = Shortfalls.AddDefaulted_GetRef();
            Report.ItemId = Machine.FuelItemId;
            Report.ItemKey = Catalog.TryGetItem(Machine.FuelItemId, FuelItem)
                ? FuelItem.Identity.Key : FString();
            Report.Required = Machine.FuelQuantityPerCycle;
            Report.Present = Node.bHasFuelPort ? CountInPort(Node.Fuel, Machine.FuelItemId) : 0;
            return Shortfalls;
        }

        if (Node.Activity != ECMLMachineActivity::MissingInput || !bHasRecipe)
        {
            return Shortfalls;
        }

        for (const FCMLRecipeAmount& Input : Recipe.Inputs)
        {
            const int64 Present = CountInPort(Node.Input, Input.ItemId);
            if (Present >= Input.Quantity)
            {
                continue;
            }
            FCMLItemDefinition Item;
            FCMLMachineShortfallReport& Report = Shortfalls.AddDefaulted_GetRef();
            Report.ItemId = Input.ItemId;
            Report.ItemKey = Catalog.TryGetItem(Input.ItemId, Item)
                ? Item.Identity.Key : FString();
            Report.Required = Input.Quantity;
            Report.Present = Present;
        }
        return Shortfalls;
    }

    /** Integer thousandths: no float ever touches an authoritative value. */
    int32 Permille(const int64 Progress, const int64 Duration)
    {
        if (Duration <= 0 || Progress <= 0)
        {
            return 0;
        }
        if (Progress >= Duration)
        {
            return 1000;
        }
        return static_cast<int32>(Progress * 1000 / Duration);
    }
}

FString CMLMachineCauseKeys::For(const ECMLMachineActivity Activity)
{
    switch (Activity)
    {
        case ECMLMachineActivity::Running:      return Running;
        case ECMLMachineActivity::Idle:         return NoWork;
        case ECMLMachineActivity::NoRecipe:     return NoRecipe;
        case ECMLMachineActivity::MissingInput: return MissingInput;
        case ECMLMachineActivity::MissingFuel:  return MissingFuel;
        case ECMLMachineActivity::OutputFull:   return OutputFull;
        default:
            checkf(false, TEXT("Every activity has to name a cause; this one names none."));
            return FString();
    }
}

FCMLMachineNodeReport FCMLMachineDiagnostics::Describe(
    const FCMLMachineNodeState& Node,
    const FCMLGameCatalog& Catalog)
{
    FCMLMachineNodeReport Report;
    Report.NodeId = Node.Id;
    Report.Kind = Node.Kind;
    Report.DefinitionKey = DescribeDefinition(Node, Catalog);
    Report.Activity = Node.Activity;
    Report.CauseKey = CMLMachineCauseKeys::For(Node.Activity);
    Report.ProgressMilliseconds = Node.ProgressMilliseconds;
    Report.bIsCycleActive = Node.bIsCycleActive;
    Report.CompletedCycles = Node.CompletedCycles;
    Report.BeltLineStatus = Node.BeltLineStatus;
    Report.BeltLineUsedCapacity = Node.BeltLineUsedCapacity;
    Report.BeltLineAvailableCapacity = Node.BeltLineAvailableCapacity;

    FCMLRecipeDefinition Recipe;
    bool bHasRecipe = false;
    if (!Node.ActiveRecipeId.IsNone() && Catalog.TryGetRecipe(Node.ActiveRecipeId, Recipe))
    {
        bHasRecipe = true;
        Report.RecipeKey = Recipe.Identity.Key;
        Report.DurationMilliseconds = Recipe.DurationMilliseconds;
    }
    Report.ProgressPermille = Permille(Report.ProgressMilliseconds, Report.DurationMilliseconds);

    Report.Ports.Add(DescribePort(Node.Input, Catalog));
    if (Node.bHasFuelPort)
    {
        Report.Ports.Add(DescribePort(Node.Fuel, Catalog));
    }
    // A buffer's input and output are the same port. Reporting it twice would
    // show a crate's contents in two panels and double its total.
    if (!Node.bInputOutputAliased)
    {
        Report.Ports.Add(DescribePort(Node.Output, Catalog));
    }

    Report.Shortfalls = DescribeShortfalls(Node, Recipe, bHasRecipe, Catalog);
    return Report;
}

bool FCMLMachineDiagnostics::TryDescribe(
    const FCMLMachineSimulationState& Machines,
    const FCMLGameCatalog& Catalog,
    const FCMLStableId& NodeId,
    FCMLMachineNodeReport& OutReport)
{
    for (const FCMLMachineNodeState& Node : Machines.Nodes)
    {
        if (Node.Id == NodeId)
        {
            OutReport = Describe(Node, Catalog);
            return true;
        }
    }
    OutReport = FCMLMachineNodeReport();
    return false;
}

TArray<FCMLMachineNodeReport> FCMLMachineDiagnostics::DescribeAll(
    const FCMLMachineSimulationState& Machines,
    const FCMLGameCatalog& Catalog)
{
    // The nodes are already held in canonical id order, which is the order the
    // hash depends on; reporting them in any other order would make two panels
    // built from the same state disagree about which machine is which.
    TArray<FCMLMachineNodeReport> Reports;
    Reports.Reserve(Machines.Nodes.Num());
    for (const FCMLMachineNodeState& Node : Machines.Nodes)
    {
        Reports.Add(Describe(Node, Catalog));
    }
    return Reports;
}

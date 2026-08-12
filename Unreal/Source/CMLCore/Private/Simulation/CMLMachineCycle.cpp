#include "Simulation/CMLMachineCycle.h"

namespace
{
    int64 CountInPort(const FCMLMachinePort& Port, const FCMLStableId& ItemId)
    {
        int64 Total = 0;
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (Slot.ItemId == ItemId)
            {
                Total += Slot.Quantity.Value;
            }
        }
        return Total;
    }

    /** Removes an exact amount, draining from the last slot towards the first. */
    bool TryConsumeFromPort(FCMLMachinePort& Port, const FCMLStableId& ItemId, int64 Amount)
    {
        if (CountInPort(Port, ItemId) < Amount)
        {
            return false;
        }
        for (int32 Index = Port.Slots.Num() - 1; Index >= 0 && Amount > 0; --Index)
        {
            FCMLMachineSlot& Slot = Port.Slots[Index];
            if (Slot.ItemId != ItemId)
            {
                continue;
            }
            const int64 Taken = FMath::Min(Slot.Quantity.Value, Amount);
            Amount -= Taken;
            const int64 Left = Slot.Quantity.Value - Taken;
            if (Left == 0)
            {
                Slot = FCMLMachineSlot();
            }
            else
            {
                Slot.Quantity = FCMLNonNegativeQuantity(Left);
            }
        }
        return Amount == 0;
    }

    bool IsPortEmpty(const FCMLMachinePort& Port)
    {
        for (const FCMLMachineSlot& Slot : Port.Slots)
        {
            if (!Slot.ItemId.IsNone() && Slot.Quantity.Value > 0)
            {
                return false;
            }
        }
        return true;
    }

    /** All-or-nothing deposit, so a partial output can never be produced. */
    bool TryDepositOutputs(
        FCMLMachinePort& Port,
        const FCMLRecipeDefinition& Recipe,
        const FCMLGameCatalog& Catalog)
    {
        TArray<FCMLMachineSlot> Working = Port.Slots;
        for (const FCMLRecipeAmount& Output : Recipe.Outputs)
        {
            FCMLItemDefinition Item;
            if (!Catalog.TryGetItem(Output.ItemId, Item) || Item.MaxStack <= 0)
            {
                return false;
            }
            int64 Remaining = Output.Quantity;
            for (FCMLMachineSlot& Slot : Working)
            {
                if (Remaining == 0)
                {
                    break;
                }
                if (Slot.ItemId != Output.ItemId)
                {
                    continue;
                }
                const int64 Space = FMath::Max<int64>(0, Item.MaxStack - Slot.Quantity.Value);
                const int64 Moved = FMath::Min(Space, Remaining);
                Slot.Quantity = FCMLNonNegativeQuantity(Slot.Quantity.Value + Moved);
                Remaining -= Moved;
            }
            for (FCMLMachineSlot& Slot : Working)
            {
                if (Remaining == 0)
                {
                    break;
                }
                if (!Slot.ItemId.IsNone())
                {
                    continue;
                }
                const int64 Moved = FMath::Min(Item.MaxStack, Remaining);
                Slot.ItemId = Output.ItemId;
                Slot.Quantity = FCMLNonNegativeQuantity(Moved);
                Remaining -= Moved;
            }
            if (Remaining != 0)
            {
                return false;
            }
        }
        Port.Slots = MoveTemp(Working);
        return true;
    }

    bool HasFuel(const FCMLMachineNodeState& Node, const FCMLMachineDefinition& Machine)
    {
        if (!Node.bHasFuelPort || Machine.FuelSlots <= 0)
        {
            return true;
        }
        return CountInPort(Node.Fuel, Machine.FuelItemId) >= Machine.FuelQuantityPerCycle;
    }
}

bool FCMLMachineCycle::HasInputs(const FCMLMachinePort& Port, const FCMLRecipeDefinition& Recipe)
{
    for (const FCMLRecipeAmount& Input : Recipe.Inputs)
    {
        if (CountInPort(Port, Input.ItemId) < Input.Quantity)
        {
            return false;
        }
    }
    return true;
}

bool FCMLMachineCycle::Admits(
    const FCMLMachineNodeState& Node,
    const FCMLStableId& ItemId,
    const FCMLGameCatalog& Catalog)
{
    FCMLRecipeDefinition Recipe;
    if (!Catalog.TryGetRecipe(Node.ActiveRecipeId, Recipe))
    {
        return false;
    }
    for (const FCMLRecipeAmount& Input : Recipe.Inputs)
    {
        if (Input.ItemId == ItemId)
        {
            return true;
        }
    }
    return false;
}

void FCMLMachineCycle::AdvanceCycles(
    FCMLMachineSimulationState& State,
    const FCMLGameCatalog& Catalog)
{
    for (FCMLMachineNodeState& Node : State.Nodes)
    {
        if (Node.Kind != ECMLMachineNodeKind::Machine)
        {
            continue;
        }
        if (Node.ActiveRecipeId.IsNone())
        {
            Node.Activity = ECMLMachineActivity::NoRecipe;
            continue;
        }

        FCMLRecipeDefinition Recipe;
        if (!Catalog.TryGetRecipe(Node.ActiveRecipeId, Recipe))
        {
            Node.Activity = ECMLMachineActivity::NoRecipe;
            continue;
        }
        FCMLMachineDefinition Machine;
        Catalog.TryGetMachine(Node.DefinitionId, Machine);

        if (!Node.bIsCycleActive)
        {
            // Every refusal below leaves the machine idle with a reason rather
            // than half-starting a cycle.
            if (!IsPortEmpty(Node.Output))
            {
                Node.Activity = ECMLMachineActivity::OutputFull;
                continue;
            }
            if (!HasInputs(Node.Input, Recipe))
            {
                Node.Activity = ECMLMachineActivity::MissingInput;
                continue;
            }
            if (!HasFuel(Node, Machine))
            {
                Node.Activity = ECMLMachineActivity::MissingFuel;
                continue;
            }

            // Inputs and fuel are spent here, at the start. A cycle that later
            // cannot deposit its output has already paid for itself and must
            // not pay twice.
            for (const FCMLRecipeAmount& Input : Recipe.Inputs)
            {
                TryConsumeFromPort(Node.Input, Input.ItemId, Input.Quantity);
            }
            if (Node.bHasFuelPort && Machine.FuelSlots > 0)
            {
                TryConsumeFromPort(Node.Fuel, Machine.FuelItemId, Machine.FuelQuantityPerCycle);
            }
            Node.bIsCycleActive = true;
            Node.ProgressMilliseconds = 0;
        }

        if (Node.ProgressMilliseconds >= Recipe.DurationMilliseconds)
        {
            // Finished but not yet deposited: phase 8 owns this state. Advancing
            // further would be work done twice on the same cycle.
            continue;
        }

        Node.ProgressMilliseconds = FMath::Min(
            Recipe.DurationMilliseconds, Node.ProgressMilliseconds + MillisecondsPerTick);
        Node.Activity = ECMLMachineActivity::Running;
    }
}

void FCMLMachineCycle::CompleteCycles(
    FCMLMachineSimulationState& State,
    const FCMLGameCatalog& Catalog)
{
    for (FCMLMachineNodeState& Node : State.Nodes)
    {
        if (Node.Kind != ECMLMachineNodeKind::Machine || !Node.bIsCycleActive)
        {
            continue;
        }
        FCMLRecipeDefinition Recipe;
        if (!Catalog.TryGetRecipe(Node.ActiveRecipeId, Recipe))
        {
            continue;
        }
        if (Node.ProgressMilliseconds < Recipe.DurationMilliseconds)
        {
            continue;
        }
        if (!TryDepositOutputs(Node.Output, Recipe, Catalog))
        {
            // The cycle stays finished and active; it will deposit as soon as
            // the output port has room.
            Node.Activity = ECMLMachineActivity::OutputFull;
            continue;
        }

        Node.bIsCycleActive = false;
        Node.ProgressMilliseconds = 0;
        ++Node.CompletedCycles;
        Node.Activity = ECMLMachineActivity::Running;
    }
}

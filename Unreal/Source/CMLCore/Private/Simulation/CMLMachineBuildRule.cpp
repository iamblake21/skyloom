#include "Simulation/CMLMachineBuildRule.h"

#include "Content/CMLContentIds.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Simulation/CMLTransferRule.h"

namespace
{
    FCMLMachinePort MakePort(const ECMLMachinePortKind Kind, const int32 SlotCount)
    {
        FCMLMachinePort Port;
        Port.Kind = Kind;
        Port.Slots.SetNum(FMath::Max(0, SlotCount));
        return Port;
    }

    bool Supports(const FCMLMachineDefinition& Machine, const FCMLStableId& RecipeId)
    {
        return Machine.SupportedRecipeIds.Contains(RecipeId);
    }

    /**
     * True when every recipe the machine supports is extraction, which is what
     * makes it an extractor rather than a transformer. A machine with no recipes
     * at all is simply unconfigured and does not qualify.
     */
    bool ExtractsOnly(const FCMLMachineDefinition& Machine, const FCMLGameCatalog& Catalog)
    {
        if (Machine.SupportedRecipeIds.IsEmpty())
        {
            return false;
        }
        for (const FCMLStableId& RecipeId : Machine.SupportedRecipeIds)
        {
            FCMLRecipeDefinition Recipe;
            if (!Catalog.TryGetRecipe(RecipeId, Recipe) || !Recipe.IsExtraction())
            {
                return false;
            }
        }
        return true;
    }

    bool IsPlacementCellFree(
        const FCMLMachineSimulationState& Machines, const FCMLMachineBuildPose& Pose)
    {
        for (const FCMLMachineNodeState& Node : Machines.Nodes)
        {
            if (Node.bHasPlacementPose
                && Node.PlacementPose.XMillimetres == Pose.XMillimetres
                && Node.PlacementPose.YMillimetres == Pose.YMillimetres
                && Node.PlacementPose.ZMillimetres == Pose.ZMillimetres)
            {
                return false;
            }
        }
        return true;
    }

    /**
     * Every buildable is priced at one of itself, and the pairing is checked
     * rather than trusted: a command claiming a crate costs one plant fibre
     * would otherwise be honoured.
     */
    bool IsCorrectBuildCost(const FCMLMachineBuildSpecification& Specification)
    {
        using namespace CMLContentIds;
        if (Specification.CostQuantity != 1)
        {
            return false;
        }
        switch (Specification.Kind)
        {
            case ECMLMachineBuildKind::Buffer:
                return Specification.PrimaryId == WoodenCrate
                    && Specification.CostItemId == WoodenCrateItem;

            case ECMLMachineBuildKind::Machine:
                return (Specification.PrimaryId == MechanicalPress
                        && Specification.CostItemId == MechanicalPressItem)
                    || (Specification.PrimaryId == CrudeFurnace
                        && Specification.CostItemId == CrudeFurnaceItem)
                    || (Specification.PrimaryId == MechanicalDrill
                        && Specification.CostItemId == MechanicalDrillItem);

            case ECMLMachineBuildKind::Funnel:
                return Specification.PrimaryId == BeltFunnel
                    && Specification.CostItemId == BeltFunnel;

            case ECMLMachineBuildKind::BeltModule:
                // A curve is admitted like the rest: adjacency derives from the
                // pose, so turning changes nothing for the topology.
                return Specification.PrimaryId == Specification.CostItemId
                    && (Specification.PrimaryId == BeltStraight
                        || Specification.PrimaryId == BeltDriveUnit
                        || Specification.PrimaryId == BeltCurve
                        || Specification.PrimaryId == BeltIncline
                        || Specification.PrimaryId == BeltCurveLeft);

            default:
                return false;
        }
    }

    FCMLMachineBuildSpecification Make(
        const ECMLMachineBuildKind Kind,
        const FCMLStableId& PrimaryId,
        const FCMLStableId& CostItemId,
        const int64 CostQuantity,
        const FCMLMachineBuildPose& Pose)
    {
        FCMLMachineBuildSpecification Specification;
        Specification.Kind = Kind;
        Specification.PrimaryId = PrimaryId;
        Specification.CostItemId = CostItemId;
        Specification.CostQuantity = CostQuantity;
        Specification.Pose = Pose;
        return Specification;
    }
}

FCMLMachineBuildSpecification FCMLMachineBuildSpecification::Buffer(
    const FCMLStableId& ContainerId, const FCMLStableId& CostItemId,
    const int64 CostQuantity, const FCMLMachineBuildPose& Pose)
{
    return Make(ECMLMachineBuildKind::Buffer, ContainerId, CostItemId, CostQuantity, Pose);
}

FCMLMachineBuildSpecification FCMLMachineBuildSpecification::Machine(
    const FCMLStableId& MachineId, const FCMLStableId& RecipeId,
    const FCMLStableId& CostItemId, const int64 CostQuantity,
    const FCMLMachineBuildPose& Pose)
{
    FCMLMachineBuildSpecification Specification =
        Make(ECMLMachineBuildKind::Machine, MachineId, CostItemId, CostQuantity, Pose);
    Specification.SecondaryId = RecipeId;
    return Specification;
}

FCMLMachineBuildSpecification FCMLMachineBuildSpecification::Funnel(
    const FCMLStableId& ItemId, const FCMLStableId& CostItemId,
    const int64 CostQuantity, const FCMLMachineBuildPose& Pose)
{
    return Make(ECMLMachineBuildKind::Funnel, ItemId, CostItemId, CostQuantity, Pose);
}

FCMLMachineBuildSpecification FCMLMachineBuildSpecification::BeltModule(
    const FCMLStableId& ItemId, const FCMLStableId& CostItemId,
    const int64 CostQuantity, const FCMLMachineBuildPose& Pose)
{
    return Make(ECMLMachineBuildKind::BeltModule, ItemId, CostItemId, CostQuantity, Pose);
}

FCMLMachineNodeState FCMLMachineBuildRule::CreateBuffer(
    const FCMLStableId& Id, const FCMLStableId& ContainerId,
    const int32 SlotCount, const FCMLMachineBuildPose& Pose)
{
    FCMLMachineNodeState Node;
    Node.Id = Id;
    Node.Kind = ECMLMachineNodeKind::Buffer;
    Node.DefinitionId = ContainerId;
    // A crate has one store reached through both faces.
    Node.Input = MakePort(ECMLMachinePortKind::Storage, SlotCount);
    Node.bInputOutputAliased = true;
    Node.Output = Node.Input;
    Node.Activity = ECMLMachineActivity::Idle;
    Node.bHasPlacementPose = true;
    Node.PlacementPose = Pose;
    return Node;
}

FCMLMachineNodeState FCMLMachineBuildRule::CreateMachine(
    const FCMLStableId& Id, const FCMLStableId& MachineId,
    const int32 InputSlots, const int32 OutputSlots, const int32 FuelSlots,
    const FCMLMachineBuildPose& Pose)
{
    FCMLMachineNodeState Node;
    Node.Id = Id;
    Node.Kind = ECMLMachineNodeKind::Machine;
    Node.DefinitionId = MachineId;
    Node.Input = MakePort(ECMLMachinePortKind::Input, InputSlots);
    Node.Output = MakePort(ECMLMachinePortKind::Output, OutputSlots);
    Node.bHasFuelPort = FuelSlots > 0;
    if (Node.bHasFuelPort)
    {
        Node.Fuel = MakePort(ECMLMachinePortKind::Fuel, FuelSlots);
    }
    Node.Activity = ECMLMachineActivity::NoRecipe;
    Node.bHasPlacementPose = true;
    Node.PlacementPose = Pose;
    return Node;
}

FCMLMachineNodeState FCMLMachineBuildRule::CreateFunnel(
    const FCMLStableId& Id, const FCMLStableId& ItemId, const FCMLMachineBuildPose& Pose)
{
    FCMLMachineNodeState Node;
    Node.Id = Id;
    Node.Kind = ECMLMachineNodeKind::Funnel;
    Node.DefinitionId = ItemId;
    // One slot, reached from both sides: a piece pulled in has to be visible on
    // the way out, or it would go in and never come out.
    Node.Input = MakePort(ECMLMachinePortKind::Storage, 1);
    Node.bInputOutputAliased = true;
    Node.Output = Node.Input;
    Node.Activity = ECMLMachineActivity::Idle;
    Node.bHasPlacementPose = true;
    Node.PlacementPose = Pose;
    return Node;
}

FCMLMachineNodeState FCMLMachineBuildRule::CreateBeltModule(
    const FCMLStableId& Id, const FCMLStableId& ItemId, const FCMLMachineBuildPose& Pose)
{
    FCMLMachineNodeState Node;
    Node.Id = Id;
    Node.Kind = ECMLMachineNodeKind::BeltModule;
    Node.DefinitionId = ItemId;
    Node.Input = MakePort(ECMLMachinePortKind::Storage, 1);
    Node.bInputOutputAliased = true;
    Node.Output = Node.Input;
    Node.Activity = ECMLMachineActivity::Idle;
    // A freshly placed belt has no drive on its line until one is found.
    Node.BeltLineStatus = ECMLBeltLineStatus::MissingDrive;
    Node.bHasPlacementPose = true;
    Node.PlacementPose = Pose;
    return Node;
}

bool FCMLMachineBuildRule::TryPreflightTopology(
    const FCMLMachineSimulationState& Machines,
    const FCMLGameCatalog& Catalog,
    const FCMLMachineBuildSpecification& Specification,
    ECMLBuildRejection& OutRejection)
{
    OutRejection = ECMLBuildRejection::None;

    FCMLItemDefinition CostItem;
    if (!IsCorrectBuildCost(Specification)
        || Specification.CostItemId.IsNone()
        || Specification.CostQuantity <= 0
        || !Catalog.TryGetItem(Specification.CostItemId, CostItem))
    {
        OutRejection = ECMLBuildRejection::BuildDefinitionMissing;
        return false;
    }

    // Only a belt carries a travel polarity. Anything else declaring one is a
    // malformed command rather than a harmless extra field.
    if (Specification.Kind != ECMLMachineBuildKind::BeltModule
        && Specification.BeltTravelDirection != ECMLBeltTravelDirection::Stopped)
    {
        OutRejection = ECMLBuildRejection::BuildMalformed;
        return false;
    }

    if (!IsPlacementCellFree(Machines, Specification.Pose))
    {
        OutRejection = ECMLBuildRejection::BuildTopologyInvalid;
        return false;
    }

    switch (Specification.Kind)
    {
        case ECMLMachineBuildKind::Buffer:
        {
            FCMLContainerDefinition Container;
            if (Specification.PrimaryId.IsNone()
                || !Catalog.TryGetContainer(Specification.PrimaryId, Container))
            {
                OutRejection = ECMLBuildRejection::BuildDefinitionMissing;
                return false;
            }
            return true;
        }

        case ECMLMachineBuildKind::Machine:
        {
            FCMLMachineDefinition Machine;
            if (Specification.PrimaryId.IsNone()
                || !Catalog.TryGetMachine(Specification.PrimaryId, Machine))
            {
                OutRejection = ECMLBuildRejection::BuildDefinitionMissing;
                return false;
            }
            if (!Specification.SecondaryId.IsNone()
                && !Supports(Machine, Specification.SecondaryId))
            {
                OutRejection = ECMLBuildRejection::BuildTopologyInvalid;
                return false;
            }
            // An extractor draws from the deposit it stands on, so the deposit
            // chooses its recipe. Without one there is nothing under the machine
            // and the build is refused — not accepted and left on NoRecipe
            // forever, which is what would happen if this lived only in the UI.
            if (Specification.SecondaryId.IsNone() && ExtractsOnly(Machine, Catalog))
            {
                OutRejection = ECMLBuildRejection::BuildTopologyInvalid;
                return false;
            }
            return true;
        }

        case ECMLMachineBuildKind::Funnel:
        case ECMLMachineBuildKind::BeltModule:
        {
            FCMLItemDefinition Item;
            if (Specification.PrimaryId.IsNone()
                || !Catalog.TryGetItem(Specification.PrimaryId, Item))
            {
                OutRejection = ECMLBuildRejection::BuildDefinitionMissing;
                return false;
            }
            // A belt is placed stopped; its polarity is published later by the
            // drive on its line, not chosen at build time.
            if (Specification.Kind == ECMLMachineBuildKind::BeltModule
                && Specification.BeltTravelDirection != ECMLBeltTravelDirection::Stopped)
            {
                OutRejection = ECMLBuildRejection::BuildDefinitionMissing;
                return false;
            }
            return true;
        }

        default:
            OutRejection = ECMLBuildRejection::BuildMalformed;
            return false;
    }
}

bool FCMLMachineBuildRule::TryPreflight(
    const FCMLMachineSimulationState& Machines,
    const FCMLInventorySimulationState& Inventories,
    const FCMLGameCatalog& Catalog,
    const FCMLStableId& SourceInventoryId,
    const FCMLMachineBuildSpecification& Specification,
    ECMLBuildRejection& OutRejection)
{
    if (!TryPreflightTopology(Machines, Catalog, Specification, OutRejection))
    {
        return false;
    }

    const int32 Index = Inventories.Inventories.IndexOfByPredicate(
        [&SourceInventoryId](const FCMLInventoryState& Candidate)
        {
            return Candidate.InventoryId == SourceInventoryId;
        });
    if (SourceInventoryId.IsNone() || Index == INDEX_NONE)
    {
        OutRejection = ECMLBuildRejection::BuildSourceMissing;
        return false;
    }
    if (FCMLInventoryOperations::Count(Inventories.Inventories[Index], Specification.CostItemId)
        < Specification.CostQuantity)
    {
        OutRejection = ECMLBuildRejection::InsufficientQuantity;
        return false;
    }
    return true;
}

bool FCMLMachineBuildRule::TryApply(
    const FCMLMachineSimulationState& Machines,
    const FCMLInventorySimulationState& Inventories,
    const FCMLGameCatalog& Catalog,
    const FCMLStableId& SourceInventoryId,
    const FCMLStableId& CreatedId,
    const FCMLMachineBuildSpecification& Specification,
    FCMLMachineSimulationState& OutMachines,
    FCMLInventorySimulationState& OutInventories,
    ECMLBuildRejection& OutRejection)
{
    OutMachines = Machines;
    OutInventories = Inventories;

    if (!TryPreflight(
            Machines, Inventories, Catalog, SourceInventoryId, Specification, OutRejection))
    {
        return false;
    }
    if (CreatedId.IsNone())
    {
        OutRejection = ECMLBuildRejection::BuildMalformed;
        return false;
    }

    const int32 Index = OutInventories.Inventories.IndexOfByPredicate(
        [&SourceInventoryId](const FCMLInventoryState& Candidate)
        {
            return Candidate.InventoryId == SourceInventoryId;
        });
    FCMLInventoryState Paid;
    ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
    if (!FCMLInventoryOperations::TryTakeEntire(
            OutInventories.Inventories[Index], Specification.CostItemId,
            Specification.CostQuantity, Paid, Failure))
    {
        // The preflight said it could pay. C# treated this as a broken invariant
        // and threw; refusing leaves the world untouched instead.
        OutRejection = ECMLBuildRejection::InsufficientQuantity;
        OutMachines = Machines;
        OutInventories = Inventories;
        return false;
    }
    OutInventories.Inventories[Index] = MoveTemp(Paid);

    switch (Specification.Kind)
    {
        case ECMLMachineBuildKind::Buffer:
        {
            FCMLContainerDefinition Container;
            Catalog.TryGetContainer(Specification.PrimaryId, Container);
            OutMachines.Nodes.Add(CreateBuffer(
                CreatedId, Specification.PrimaryId, Container.SlotCount, Specification.Pose));
            break;
        }
        case ECMLMachineBuildKind::Machine:
        {
            FCMLMachineDefinition Machine;
            Catalog.TryGetMachine(Specification.PrimaryId, Machine);
            FCMLMachineNodeState Node = CreateMachine(
                CreatedId, Specification.PrimaryId,
                Machine.InputSlots, Machine.OutputSlots, Machine.FuelSlots, Specification.Pose);
            if (!Specification.SecondaryId.IsNone())
            {
                Node.ActiveRecipeId = Specification.SecondaryId;
                // It starts wanting its first ingredient, not idle: a machine
                // built with a recipe has work it cannot yet do.
                Node.Activity = ECMLMachineActivity::MissingInput;
            }
            OutMachines.Nodes.Add(MoveTemp(Node));
            break;
        }
        case ECMLMachineBuildKind::Funnel:
            OutMachines.Nodes.Add(
                CreateFunnel(CreatedId, Specification.PrimaryId, Specification.Pose));
            break;

        case ECMLMachineBuildKind::BeltModule:
            OutMachines.Nodes.Add(
                CreateBeltModule(CreatedId, Specification.PrimaryId, Specification.Pose));
            break;

        default:
            OutRejection = ECMLBuildRejection::BuildMalformed;
            OutMachines = Machines;
            OutInventories = Inventories;
            return false;
    }

    // The graph is held in canonical id order, which the hash depends on.
    OutMachines.Sort();
    OutRejection = ECMLBuildRejection::None;
    return true;
}

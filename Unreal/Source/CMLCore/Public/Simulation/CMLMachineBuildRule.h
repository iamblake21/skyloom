#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Simulation/CMLInventoryState.h"
#include "Simulation/CMLMachineState.h"

#include "CMLMachineBuildRule.generated.h"

UENUM(BlueprintType)
enum class ECMLMachineBuildKind : uint8
{
    None = 0,
    Buffer = 1,
    Machine = 2,
    Funnel = 3,
    BeltModule = 4
};

/** Why a build was refused, using the Unity rejection codes it needs. */
UENUM(BlueprintType)
enum class ECMLBuildRejection : uint8
{
    None = 0,
    /** The specification names content the catalog does not define. */
    BuildDefinitionMissing = 1,
    /** The specification is internally inconsistent. */
    BuildMalformed = 2,
    /** The placement cannot exist in the graph. */
    BuildTopologyInvalid = 3,
    /** The paying inventory does not exist. */
    BuildSourceMissing = 4,
    /** It exists but cannot pay. */
    InsufficientQuantity = 5
};

/** What to build, where, and what it costs. */
USTRUCT(BlueprintType)
struct CMLCORE_API FCMLMachineBuildSpecification
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    ECMLMachineBuildKind Kind = ECMLMachineBuildKind::None;

    /** The container, machine or item definition being placed. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    FCMLStableId PrimaryId;

    /** For a machine, the recipe it starts on. Optional except for extractors. */
    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    FCMLStableId SecondaryId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    FCMLStableId CostItemId;

    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    int64 CostQuantity = 0;

    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    FCMLMachineBuildPose Pose;

    UPROPERTY(BlueprintReadOnly, Category="CML|Build")
    ECMLBeltTravelDirection BeltTravelDirection = ECMLBeltTravelDirection::Stopped;

    static FCMLMachineBuildSpecification Buffer(
        const FCMLStableId& ContainerId,
        const FCMLStableId& CostItemId,
        int64 CostQuantity,
        const FCMLMachineBuildPose& Pose);

    static FCMLMachineBuildSpecification Machine(
        const FCMLStableId& MachineId,
        const FCMLStableId& RecipeId,
        const FCMLStableId& CostItemId,
        int64 CostQuantity,
        const FCMLMachineBuildPose& Pose);

    static FCMLMachineBuildSpecification Funnel(
        const FCMLStableId& ItemId,
        const FCMLStableId& CostItemId,
        int64 CostQuantity,
        const FCMLMachineBuildPose& Pose);

    static FCMLMachineBuildSpecification BeltModule(
        const FCMLStableId& ItemId,
        const FCMLStableId& CostItemId,
        int64 CostQuantity,
        const FCMLMachineBuildPose& Pose);
};

/**
 * Placement of machines, crates, funnels and belts, ported from
 * CML.Simulation.Machines.MachineBuildRule.
 *
 * The rule lives in the simulation and not in the placement UI, which matters
 * for one case in particular: an extractor draws from the deposit it stands on,
 * so the deposit is what chooses its recipe. A build with no recipe has nothing
 * under it and must be *refused* — not accepted and left sitting on `NoRecipe`
 * forever, which is exactly what would happen if this check lived only in the
 * hologram.
 *
 * Costs are not free-form either. Every buildable is priced at one of itself,
 * and the pairing is checked rather than trusted: a command that claimed a crate
 * cost one plant fibre would otherwise be honoured.
 */
class CMLCORE_API FCMLMachineBuildRule
{
public:
    static constexpr int32 BeltSegmentLengthMillimetres = 1000;

    /** Everything that depends only on the graph and the catalog. */
    static bool TryPreflightTopology(
        const FCMLMachineSimulationState& Machines,
        const FCMLGameCatalog& Catalog,
        const FCMLMachineBuildSpecification& Specification,
        ECMLBuildRejection& OutRejection);

    /** The topology checks plus the payer's ability to cover the cost. */
    static bool TryPreflight(
        const FCMLMachineSimulationState& Machines,
        const FCMLInventorySimulationState& Inventories,
        const FCMLGameCatalog& Catalog,
        const FCMLStableId& SourceInventoryId,
        const FCMLMachineBuildSpecification& Specification,
        ECMLBuildRejection& OutRejection);

    /**
     * Takes the cost and adds the node. All or nothing: neither half is applied
     * unless both can be.
     */
    static bool TryApply(
        const FCMLMachineSimulationState& Machines,
        const FCMLInventorySimulationState& Inventories,
        const FCMLGameCatalog& Catalog,
        const FCMLStableId& SourceInventoryId,
        const FCMLStableId& CreatedId,
        const FCMLMachineBuildSpecification& Specification,
        FCMLMachineSimulationState& OutMachines,
        FCMLInventorySimulationState& OutInventories,
        ECMLBuildRejection& OutRejection);

    /** Node factories, which decide port kinds and which ports are shared. */
    static FCMLMachineNodeState CreateBuffer(
        const FCMLStableId& Id, const FCMLStableId& ContainerId,
        int32 SlotCount, const FCMLMachineBuildPose& Pose);
    static FCMLMachineNodeState CreateMachine(
        const FCMLStableId& Id, const FCMLStableId& MachineId,
        int32 InputSlots, int32 OutputSlots, int32 FuelSlots,
        const FCMLMachineBuildPose& Pose);
    static FCMLMachineNodeState CreateFunnel(
        const FCMLStableId& Id, const FCMLStableId& ItemId, const FCMLMachineBuildPose& Pose);
    static FCMLMachineNodeState CreateBeltModule(
        const FCMLStableId& Id, const FCMLStableId& ItemId, const FCMLMachineBuildPose& Pose);
};

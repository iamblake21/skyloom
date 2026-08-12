#pragma once

#include "CoreMinimal.h"
#include "Content/CMLGameCatalog.h"
#include "Foundation/CMLCoreTypes.h"
#include "Inventory/CMLCraftingRule.h"
#include "Presentation/CMLInventoryHudPresenter.h"
#include "Simulation/CMLSimulationEngine.h"
#include "Simulation/CMLHarvestRules.h"
#include "Presentation/CMLMachineHudPresenter.h"
#include "Subsystems/WorldSubsystem.h"
#include "CMLSimulationSubsystem.generated.h"

enum class ECMLBuildRejection : uint8;

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FCMLSimulationAdvanced, int64, PublishedTick);
DECLARE_DYNAMIC_MULTICAST_DELEGATE(FCMLPlayerInventoryChanged);
DECLARE_MULTICAST_DELEGATE_ThreeParams(
    FCMLRuntimeCommandResolved,
    const FCMLSimulationCommand&,
    bool,
    bool);

struct FCMLRuntimeCommandHandle
{
    FCMLSimulationTick Tick;
    uint64 Sequence = MAX_uint64;

    bool IsValid() const { return Sequence != MAX_uint64; }
};

/** Owns the authoritative 20 Hz simulation boundary in Unreal. */
UCLASS()
class CHANGINGMYLIFE_API UCMLSimulationSubsystem final : public UTickableWorldSubsystem
{
    GENERATED_BODY()

public:
    virtual void Initialize(FSubsystemCollectionBase& Collection) override;
    virtual void Deinitialize() override;
    virtual void Tick(float DeltaTime) override;
    virtual TStatId GetStatId() const override;
    virtual bool IsTickableInEditor() const override { return false; }

    UFUNCTION(BlueprintPure, Category="CML|Simulation")
    int64 GetPublishedTick() const { return static_cast<int64>(Engine.GetState().Tick.Value); }

    UFUNCTION(BlueprintPure, Category="CML|Simulation")
    bool IsRuntimeReady() const { return bRuntimeReady; }

    UPROPERTY(BlueprintAssignable, Category="CML|Simulation")
    FCMLSimulationAdvanced OnSimulationAdvanced;

    UPROPERTY(BlueprintAssignable, Category="CML|Inventory")
    FCMLPlayerInventoryChanged OnPlayerInventoryChanged;

    /** The presenter's current immutable view of the player inventory. */
    bool GetPlayerInventoryPresentation(FCMLInventoryUiSnapshot& OutSnapshot) const;

    /** Read-only access for interaction and machine presentation boundaries. */
    const FCMLGameCatalog& GetCatalog() const { return Catalog; }
    const FCMLSimulationState& GetPublishedState() const { return Engine.GetState(); }
    bool GetPlayerInventory(FCMLInventoryState& OutInventory) const;

    /** Queue authoritative changes for the next 20 Hz tick. */
    bool RequestStorePlayerItem(const FCMLStableId& ItemId, int64 Amount);
    bool RequestCraftPlayerItem(
        const FCMLStableId& RecipeId,
        ECMLCraftingStationKind Station,
        int64 CraftCount = 1);
    bool RequestMovePlayerSlot(int32 SourceSlotIndex, int32 DestinationSlotIndex, int64 Amount = 0);
    bool RequestMoveContainerSlot(
        const FCMLStableId& NodeId,
        bool bSourceIsPlayer,
        int32 SourceSlotIndex,
        bool bDestinationIsPlayer,
        int32 DestinationSlotIndex,
        int64 Amount = 0);
    bool RequestQuickTransferContainerSlot(
        const FCMLStableId& NodeId,
        bool bSourceIsPlayer,
        int32 SourceSlotIndex);
    /**
     * Minecraft-style cursor transfer between the player and a logical machine
     * port. `None` identifies the player inventory; machine ports keep their
     * canonical Input/Fuel/Output identity.
     */
    bool RequestTransferMachineItem(
        const FCMLStableId& NodeId,
        ECMLMachinePortKind SourcePort,
        ECMLMachinePortKind DestinationPort,
        const FCMLStableId& ItemId,
        int64 Amount);
    /** Largest prefix of a cursor stack the authoritative transfer rule admits. */
    int64 AllowedMachineTransferQuantity(
        const FCMLStableId& NodeId,
        ECMLMachinePortKind SourcePort,
        ECMLMachinePortKind DestinationPort,
        const FCMLStableId& ItemId,
        int64 RequestedAmount) const;
    ECMLMachinePortKind PreferredMachinePortForItem(
        const FCMLStableId& NodeId,
        const FCMLStableId& ItemId) const;
    bool RequestHandGather(
        const FCMLStableId& SourceId,
        ECMLHandGatherTarget Target,
        int32 Units,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestMiningImpact(
        const FCMLStableId& SourceId,
        ECMLMiningTarget Target,
        int32 EquippedSlotIndex,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestTreeImpact(
        const FCMLStableId& SourceId,
        int32 EquippedSlotIndex,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestQuickTransfer(
        const FCMLStableId& NodeId,
        int32 SelectedPlayerSlot,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestBuild(
        const FCMLStableId& BuildItemId,
        const FCMLMachineBuildPose& Pose,
        const FCMLStableId& ExtractionRecipeId,
        const FVector& VisualWorldLocation,
        FCMLRuntimeCommandHandle& OutHandle);

    /** Presentation-only transform paired with a deterministic build command. */
    bool ConsumePendingBuildVisual(
        const FCMLSimulationCommand& Command, FVector& OutWorldLocation);

    /** The same catalog, topology and inventory check used by command apply. */
    bool TryPreflightBuild(
        const FCMLStableId& BuildItemId,
        const FCMLMachineBuildPose& Pose,
        const FCMLStableId& ExtractionRecipeId,
        ECMLBuildRejection& OutRejection) const;
    bool RequestAirshipRepairInstall(
        const FCMLStableId& AirshipId,
        const FCMLStableId& ItemId,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestAirshipPilotBegin(
        const FCMLStableId& AirshipId,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestAirshipPilotEnd(
        const FCMLStableId& AirshipId,
        FCMLRuntimeCommandHandle& OutHandle);
    bool RequestAirshipPilotInput(
        const FCMLStableId& AirshipId,
        int32 ThrottlePermille,
        int32 LiftPermille,
        int32 YawPermille,
        int32 PitchPermille);

    /** Registers pre-authored world stations before the first simulation tick. */
    bool RegisterWorldMachine(
        const FCMLStableId& NodeId,
        const FCMLStableId& MachineDefinitionId,
        const FCMLStableId& ActiveRecipeId,
        const FCMLMachineBuildPose& Pose);
    bool RegisterWorldBuffer(
        const FCMLStableId& NodeId,
        const FCMLStableId& ContainerDefinitionId,
        const FCMLMachineBuildPose& Pose);
    bool RegisterWorldAirship(
        const FCMLStableId& AirshipId,
        const FCMLAirshipPose& Pose);
    /** Seeds an authored world container before the first gameplay tick. */
    bool SeedWorldBufferItem(
        const FCMLStableId& NodeId,
        const FCMLStableId& ItemId,
        int64 Amount);
    bool GetAirshipState(
        const FCMLStableId& AirshipId,
        FCMLAirshipEntityState& OutState) const;
    bool GetLocalPilotedAirship(FCMLStableId& OutAirshipId) const;
    bool GetMachinePresentation(
        const FCMLStableId& NodeId,
        FCMLMachineUiSnapshot& OutSnapshot) const;
    bool GetBufferPresentation(
        const FCMLStableId& NodeId,
        TArray<FCMLInventorySlotPresentation>& OutSlots) const;

    /** Native because interaction components match the full command identity. */
    FCMLRuntimeCommandResolved OnRuntimeCommandResolved;

private:
    void AdvanceOneStep();
    bool EnqueueForNextTick(
        FCMLSimulationCommand& Command,
        FCMLRuntimeCommandHandle* OutHandle = nullptr);
    void RebuildPlayerPresentation();
    void ResolveCommandsForTick(const FCMLSimulationTick& Tick);
    bool TryGetToolStateForSlot(int32 SlotIndex, FCMLToolState& OutTool) const;

    FCMLFixedStepClock Clock;
    FCMLGameCatalog Catalog;
    FCMLSimulationEngine Engine;
    FCMLInventoryUiSnapshot PlayerInventoryPresentation;
    FCMLSimulationTick PendingSequenceTick;
    uint64 NextPendingSequence = 0;
    TArray<FCMLSimulationCommand> PendingRuntimeCommands;
    TMap<uint64, FVector> PendingBuildVisualLocations;
    bool bRuntimeReady = false;
};

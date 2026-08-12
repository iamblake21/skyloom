#include "Simulation/CMLSimulationSubsystem.h"

#include "Content/CMLBootstrapCatalog.h"
#include "Content/CMLContentIds.h"
#include "Engine/World.h"
#include "Inventory/CMLInventoryOperations.h"
#include "Simulation/CMLSlotMoveCommand.h"
#include "Simulation/CMLHarvestRules.h"
#include "Simulation/CMLMachineBuildRule.h"
#include "Simulation/CMLMachineCycle.h"
#include "Simulation/CMLMachineSpatialTopology.h"
#include "Simulation/CMLTransferRule.h"
#include "Simulation/CMLAirshipControls.h"
#include "Simulation/CMLAirshipIntegration.h"
#include "Simulation/CMLAirshipCollision.h"
#include "Simulation/CMLBeltLineRules.h"
#include "Diagnostics/CMLMachineDiagnostics.h"

DEFINE_LOG_CATEGORY_STATIC(LogCMLSimulation, Log, All);

namespace
{
    constexpr const TCHAR* StoreItemKind = TEXT("StoreInventoryItem");
    constexpr const TCHAR* CraftItemKind = TEXT("CraftInventoryRecipe");
    constexpr const TCHAR* GatherItemKind = TEXT("GatherHandSource");
    constexpr const TCHAR* MiningImpactKind = TEXT("MiningImpact");
    constexpr const TCHAR* TreeImpactKind = TEXT("TreeImpact");
    constexpr const TCHAR* QuickTransferKind = TEXT("QuickTransfer");
    constexpr const TCHAR* ContainerSlotMoveKind = TEXT("MoveContainerSlot");
    constexpr const TCHAR* MachineItemTransferKind = TEXT("TransferMachineItem");
    constexpr const TCHAR* BuildNodeKind = TEXT("BuildNode");
    constexpr const TCHAR* AirshipRepairInstallKind = TEXT("AirshipRepairInstall");
    constexpr const TCHAR* AirshipPilotBeginKind = TEXT("AirshipPilotBegin");
    constexpr const TCHAR* AirshipPilotEndKind = TEXT("AirshipPilotEnd");
    constexpr const TCHAR* AirshipPilotInputKind = TEXT("AirshipPilotInput");
    constexpr int32 RequiredAirshipIronPlates = 4;
    constexpr int32 RequiredAirshipCables = 2;
    constexpr int32 AirshipRepairDurationTicks = 8 * FCMLSimulationTick::TicksPerSecond;
    const FCMLStableId RuntimePlayerId(0x7900000000000000ULL, 1);

    void AppendU64(TArray<uint8>& Bytes, const uint64 Value)
    {
        for (int32 Shift = 56; Shift >= 0; Shift -= 8)
        {
            Bytes.Add(static_cast<uint8>((Value >> Shift) & 0xFF));
        }
    }

    uint64 ReadU64(const TArray<uint8>& Bytes, const int32 Offset)
    {
        uint64 Value = 0;
        for (int32 Index = 0; Index < 8; ++Index)
        {
            Value = (Value << 8) | Bytes[Offset + Index];
        }
        return Value;
    }

    void AppendStableId(TArray<uint8>& Bytes, const FCMLStableId& Id)
    {
        AppendU64(Bytes, Id.High);
        AppendU64(Bytes, Id.Low);
    }

    FCMLStableId ReadStableId(const TArray<uint8>& Bytes, const int32 Offset)
    {
        return FCMLStableId(ReadU64(Bytes, Offset), ReadU64(Bytes, Offset + 8));
    }

    void AppendI32(TArray<uint8>& Bytes, const int32 Value)
    {
        const uint32 Bits = static_cast<uint32>(Value);
        Bytes.Add(static_cast<uint8>((Bits >> 24) & 0xFF));
        Bytes.Add(static_cast<uint8>((Bits >> 16) & 0xFF));
        Bytes.Add(static_cast<uint8>((Bits >> 8) & 0xFF));
        Bytes.Add(static_cast<uint8>(Bits & 0xFF));
    }

    int32 ReadI32(const TArray<uint8>& Bytes, const int32 Offset)
    {
        const uint32 Bits = (static_cast<uint32>(Bytes[Offset]) << 24)
            | (static_cast<uint32>(Bytes[Offset + 1]) << 16)
            | (static_cast<uint32>(Bytes[Offset + 2]) << 8)
            | static_cast<uint32>(Bytes[Offset + 3]);
        return static_cast<int32>(Bits);
    }

    bool BuildSpecificationFor(
        const FCMLStableId& BuildItemId,
        const FCMLMachineBuildPose& Pose,
        const FCMLStableId& ExtractionRecipeId,
        FCMLMachineBuildSpecification& OutSpecification)
    {
        using namespace CMLContentIds;
        if (BuildItemId == WoodenCrateItem)
        {
            OutSpecification = FCMLMachineBuildSpecification::Buffer(
                WoodenCrate, WoodenCrateItem, 1, Pose);
            return true;
        }
        if (BuildItemId == MechanicalPressItem)
        {
            OutSpecification = FCMLMachineBuildSpecification::Machine(
                MechanicalPress, PressIronPlate, MechanicalPressItem, 1, Pose);
            return true;
        }
        if (BuildItemId == CrudeFurnaceItem)
        {
            OutSpecification = FCMLMachineBuildSpecification::Machine(
                CrudeFurnace, SmeltIronIngot, CrudeFurnaceItem, 1, Pose);
            return true;
        }
        if (BuildItemId == MechanicalDrillItem)
        {
            OutSpecification = FCMLMachineBuildSpecification::Machine(
                MechanicalDrill, ExtractionRecipeId, MechanicalDrillItem, 1, Pose);
            return true;
        }
        if (BuildItemId == BeltFunnel)
        {
            OutSpecification = FCMLMachineBuildSpecification::Funnel(
                BeltFunnel, BeltFunnel, 1, Pose);
            return true;
        }
        if (BuildItemId == BeltStraight || BuildItemId == BeltCurve
            || BuildItemId == BeltCurveLeft || BuildItemId == BeltIncline
            || BuildItemId == BeltDriveUnit)
        {
            OutSpecification = FCMLMachineBuildSpecification::BeltModule(
                BuildItemId, BuildItemId, 1, Pose);
            return true;
        }
        return false;
    }

    FCMLStableId MiningProgressKey(const FCMLStableId& SourceId)
    {
        return FCMLStableId(
            0x7400000000000000ULL,
            HashCombineFast(::GetTypeHash(SourceId.High), ::GetTypeHash(SourceId.Low)));
    }

    FCMLStableId ToolDurabilityKey(const int32 SlotIndex)
    {
        return FCMLStableId(0x7300000000000000ULL, static_cast<uint64>(SlotIndex + 1));
    }

    FCMLStableId ToolIdentityKey(const int32 SlotIndex)
    {
        return FCMLStableId(0x7310000000000000ULL, static_cast<uint64>(SlotIndex + 1));
    }

    FCMLStableId TreeProgressKey(const FCMLStableId& SourceId)
    {
        return FCMLStableId(
            0x7410000000000000ULL,
            HashCombineFast(::GetTypeHash(SourceId.High), ::GetTypeHash(SourceId.Low)));
    }

    int64 ReadQuantity(
        const FCMLSimulationState& State,
        const FCMLStableId Key,
        bool* bOutFound = nullptr)
    {
        for (const TPair<FCMLStableId, FCMLNonNegativeQuantity>& Pair : State.Quantities)
        {
            if (Pair.Key == Key)
            {
                if (bOutFound != nullptr) *bOutFound = true;
                return Pair.Value.Value;
            }
        }
        if (bOutFound != nullptr) *bOutFound = false;
        return 0;
    }

    void WriteQuantity(
        FCMLSimulationState& State,
        const FCMLStableId Key,
        const int64 Value)
    {
        for (TPair<FCMLStableId, FCMLNonNegativeQuantity>& Pair : State.Quantities)
        {
            if (Pair.Key == Key)
            {
                Pair.Value = FCMLNonNegativeQuantity(Value);
                return;
            }
        }
        State.Quantities.Emplace(Key, FCMLNonNegativeQuantity(Value));
    }

    int32 FindInventoryIndex(
        const FCMLInventorySimulationState& Inventories,
        const FCMLStableId InventoryId)
    {
        for (int32 Index = 0; Index < Inventories.Inventories.Num(); ++Index)
        {
            if (Inventories.Inventories[Index].InventoryId == InventoryId)
            {
                return Index;
            }
        }
        return INDEX_NONE;
    }

    int32 FindNodeIndex(
        const FCMLMachineSimulationState& Machines,
        const FCMLStableId NodeId)
    {
        for (int32 Index = 0; Index < Machines.Nodes.Num(); ++Index)
        {
            if (Machines.Nodes[Index].Id == NodeId)
            {
                return Index;
            }
        }
        return INDEX_NONE;
    }

    int32 FindAirshipIndex(
        const FCMLAirshipSimulationState& Airships,
        const FCMLStableId AirshipId)
    {
        for (int32 Index = 0; Index < Airships.Airships.Num(); ++Index)
        {
            if (Airships.Airships[Index].Id == AirshipId)
            {
                return Index;
            }
        }
        return INDEX_NONE;
    }

    int32 FindAirshipPlayerIndex(
        const FCMLAirshipSimulationState& Airships,
        const FCMLStableId PlayerId)
    {
        for (int32 Index = 0; Index < Airships.Players.Num(); ++Index)
        {
            if (Airships.Players[Index].Id == PlayerId)
            {
                return Index;
            }
        }
        return INDEX_NONE;
    }

    void ResetAirshipMotion(FCMLAirshipEntityState& Airship)
    {
        Airship.ForwardSpeedMillimetresPerSecond = 0;
        Airship.StrafeSpeedMillimetresPerSecond = 0;
        Airship.VerticalSpeedMillimetresPerSecond = 0;
        Airship.YawRateTurnUnitsPerSecond = 0;
        Airship.ForwardIntegrationRemainder = 0;
        Airship.StrafeIntegrationRemainder = 0;
        Airship.VerticalIntegrationRemainder = 0;
        Airship.YawIntegrationRemainder = 0;
    }

    int64 CapacityFor(const FCMLGameCatalog& Catalog, const FCMLInventoryState& Inventory)
    {
        FCMLContainerDefinition Container;
        return Catalog.TryGetContainer(Inventory.ContainerDefinitionId, Container)
            ? Container.Capacity
            : 0;
    }

    int64 ToolIdentityValue(const FCMLStableId& ItemId)
    {
        if (ItemId == CMLContentIds::CrudePickaxe) return 1;
        if (ItemId == CMLContentIds::IronPickaxe) return 2;
        return 0;
    }

    int32 ReadToolDurability(
        const FCMLSimulationState& State,
        const FCMLGameCatalog& Catalog,
        const FCMLInventoryState& Inventory,
        const int32 SlotIndex)
    {
        if (!Inventory.Slots.IsValidIndex(SlotIndex)
            || !Inventory.Slots[SlotIndex].bHasStack)
        {
            return 0;
        }

        const FCMLStableId ItemId = Inventory.Slots[SlotIndex].Stack.ItemId;
        FCMLItemDefinition Definition;
        if (!Catalog.TryGetItem(ItemId, Definition) || Definition.MaximumDurability <= 0)
        {
            return 0;
        }

        bool bHasIdentity = false;
        const int64 StoredIdentity = ReadQuantity(
            State, ToolIdentityKey(SlotIndex), &bHasIdentity);
        bool bHasDurability = false;
        const int64 StoredDurability = ReadQuantity(
            State, ToolDurabilityKey(SlotIndex), &bHasDurability);
        return bHasIdentity && bHasDurability
            && StoredIdentity == ToolIdentityValue(ItemId)
            ? static_cast<int32>(StoredDurability)
            : Definition.MaximumDurability;
    }

    void WriteToolDurability(
        FCMLSimulationState& State,
        const int32 SlotIndex,
        const FCMLStableId& ItemId,
        const int32 Durability)
    {
        WriteQuantity(State, ToolIdentityKey(SlotIndex), ToolIdentityValue(ItemId));
        WriteQuantity(State, ToolDurabilityKey(SlotIndex), Durability);
    }

    void SynchronizeToolDurability(
        FCMLSimulationState& State,
        const FCMLGameCatalog& Catalog,
        const FCMLInventoryState& Before,
        const FCMLInventoryState& After)
    {
        struct FPreviousTool
        {
            FCMLStableId ItemId;
            int32 SlotIndex = INDEX_NONE;
            int32 Durability = 0;
            bool bUsed = false;
        };

        TArray<FPreviousTool> PreviousTools;
        for (int32 SlotIndex = 0; SlotIndex < Before.Slots.Num(); ++SlotIndex)
        {
            if (!Before.Slots[SlotIndex].bHasStack)
            {
                continue;
            }
            FCMLItemDefinition Definition;
            const FCMLStableId ItemId = Before.Slots[SlotIndex].Stack.ItemId;
            if (Catalog.TryGetItem(ItemId, Definition) && Definition.MaximumDurability > 0)
            {
                FPreviousTool& Tool = PreviousTools.AddDefaulted_GetRef();
                Tool.ItemId = ItemId;
                Tool.SlotIndex = SlotIndex;
                Tool.Durability = ReadToolDurability(State, Catalog, Before, SlotIndex);
            }
        }

        for (int32 SlotIndex = 0; SlotIndex < After.Slots.Num(); ++SlotIndex)
        {
            WriteQuantity(State, ToolIdentityKey(SlotIndex), 0);
            WriteQuantity(State, ToolDurabilityKey(SlotIndex), 0);
        }

        for (int32 SlotIndex = 0; SlotIndex < After.Slots.Num(); ++SlotIndex)
        {
            if (!After.Slots[SlotIndex].bHasStack)
            {
                continue;
            }
            FCMLItemDefinition Definition;
            const FCMLStableId ItemId = After.Slots[SlotIndex].Stack.ItemId;
            if (!Catalog.TryGetItem(ItemId, Definition) || Definition.MaximumDurability <= 0)
            {
                continue;
            }

            FPreviousTool* Match = PreviousTools.FindByPredicate(
                [SlotIndex, &ItemId](const FPreviousTool& Candidate)
                {
                    return !Candidate.bUsed && Candidate.SlotIndex == SlotIndex
                        && Candidate.ItemId == ItemId;
                });
            if (Match == nullptr)
            {
                Match = PreviousTools.FindByPredicate(
                    [&ItemId](const FPreviousTool& Candidate)
                    {
                        return !Candidate.bUsed && Candidate.ItemId == ItemId;
                    });
            }

            const int32 Durability = Match != nullptr
                ? Match->Durability
                : Definition.MaximumDurability;
            if (Match != nullptr)
            {
                Match->bUsed = true;
            }
            WriteToolDurability(State, SlotIndex, ItemId, Durability);
        }
    }

    ECMLCommandRejectionReason InventoryFailureToRejection(const ECMLInventoryFailure Failure)
    {
        switch (Failure)
        {
        case ECMLInventoryFailure::UnknownItem:
            return ECMLCommandRejectionReason::TransferUnknownItem;
        case ECMLInventoryFailure::CapacityExceeded:
            return ECMLCommandRejectionReason::TransferDestinationFull;
        case ECMLInventoryFailure::InsufficientQuantity:
            return ECMLCommandRejectionReason::InsufficientQuantity;
        case ECMLInventoryFailure::ArithmeticOverflow:
            return ECMLCommandRejectionReason::QuantityOverflow;
        default:
            return ECMLCommandRejectionReason::TransferMalformed;
        }
    }

    void Reject(
        FCMLSimulationState& State,
        const FCMLSimulationTick Tick,
        const FCMLSimulationCommand& Command,
        const ECMLCommandRejectionReason Reason)
    {
        FCMLCommandRejection Rejection;
        Rejection.Tick = Tick;
        Rejection.Command = Command;
        Rejection.Reason = Reason;
        State.CommandRejections.Add(MoveTemp(Rejection));
    }

    class FCMLRuntimeInventoryCommandSystem final : public ICMLSimulationPhaseSystem
    {
    public:
        explicit FCMLRuntimeInventoryCommandSystem(const FCMLGameCatalog* InCatalog)
            : Catalog(InCatalog)
        {
        }

        virtual ECMLSimulationPhase GetPhase() const override
        {
            return ECMLSimulationPhase::CommandsAndConfiguration;
        }
        virtual int32 GetOrder() const override { return 0; }
        virtual FCMLStableId GetStableOrderId() const override
        {
            return FCMLStableId(0x434D4C52554E5449ULL, 1);
        }
        virtual FString GetTypeName() const override
        {
            return TEXT("CMLRuntimeInventoryCommands");
        }

        virtual bool Execute(FCMLSimulationPhaseContext& Context, FString& OutFailureCause) override
        {
            if (Catalog == nullptr || Context.WorkingState == nullptr)
            {
                OutFailureCause = TEXT("The runtime catalog or working state is unavailable.");
                return false;
            }

            for (const FCMLSimulationCommand& Command : Context.DueCommands)
            {
                if (Command.Kind == StoreItemKind)
                {
                    ExecuteStore(Context, Command);
                }
                else if (Command.Kind == CraftItemKind)
                {
                    ExecuteCraft(Context, Command);
                }
                else if (Command.Kind == FCMLSlotMoveCommandPayload::CommandKind())
                {
                    ExecuteSlotMove(Context, Command);
                }
                else if (Command.Kind == GatherItemKind)
                {
                    ExecuteGather(Context, Command);
                }
                else if (Command.Kind == MiningImpactKind)
                {
                    ExecuteMiningImpact(Context, Command);
                }
                else if (Command.Kind == TreeImpactKind)
                {
                    ExecuteTreeImpact(Context, Command);
                }
                else if (Command.Kind == QuickTransferKind)
                {
                    ExecuteQuickTransfer(Context, Command);
                }
                else if (Command.Kind == ContainerSlotMoveKind)
                {
                    ExecuteContainerSlotMove(Context, Command);
                }
                else if (Command.Kind == MachineItemTransferKind)
                {
                    ExecuteMachineItemTransfer(Context, Command);
                }
                else if (Command.Kind == BuildNodeKind)
                {
                    ExecuteBuild(Context, Command);
                }
                else if (Command.Kind == AirshipRepairInstallKind)
                {
                    ExecuteAirshipRepairInstall(Context, Command);
                }
                else if (Command.Kind == AirshipPilotBeginKind)
                {
                    ExecuteAirshipPilotBegin(Context, Command);
                }
                else if (Command.Kind == AirshipPilotEndKind)
                {
                    ExecuteAirshipPilotEnd(Context, Command);
                }
                else if (Command.Kind == AirshipPilotInputKind)
                {
                    ExecuteAirshipPilotInput(Context, Command);
                }
            }
            return true;
        }

    private:
        void ExecuteStore(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            if (InventoryIndex == INDEX_NONE)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferDestinationMissing);
                return;
            }
            if (Command.DestinationId.IsNone() || Command.QuantizedValue <= 0)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const FCMLInventoryState& Inventory = State.Inventories.Inventories[InventoryIndex];
            FCMLInventoryState Updated;
            ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
            if (!FCMLInventoryOperations::TryStoreEntire(
                    Inventory,
                    Catalog->ToItemCatalog(),
                    Command.DestinationId,
                    Command.QuantizedValue,
                    CapacityFor(*Catalog, Inventory),
                    Updated,
                    Failure))
            {
                Reject(State, Context.ExecutingTick, Command, InventoryFailureToRejection(Failure));
                return;
            }
            SynchronizeToolDurability(State, *Catalog, Inventory, Updated);
            State.Inventories.Inventories[InventoryIndex] = MoveTemp(Updated);
        }

        void ExecuteCraft(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            if (InventoryIndex == INDEX_NONE)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferSourceMissing);
                return;
            }
            if (Command.Payload.Num() != 1 || Command.DestinationId.IsNone()
                || Command.QuantizedValue <= 0)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const FCMLInventoryState& Inventory = State.Inventories.Inventories[InventoryIndex];
            FCMLInventoryState Updated;
            ECMLCraftingFailure Failure = ECMLCraftingFailure::None;
            if (!FCMLCraftingRule::TryCraft(
                    Inventory,
                    Catalog->ToItemCatalog(),
                    Catalog->ToRecipeCatalog(),
                    Command.DestinationId,
                    static_cast<ECMLCraftingStationKind>(Command.Payload[0]),
                    Command.QuantizedValue,
                    CapacityFor(*Catalog, Inventory),
                    Updated,
                    Failure))
            {
                ECMLCommandRejectionReason Reason = ECMLCommandRejectionReason::TransferMalformed;
                if (Failure == ECMLCraftingFailure::InsufficientIngredients)
                {
                    Reason = ECMLCommandRejectionReason::InsufficientQuantity;
                }
                else if (Failure == ECMLCraftingFailure::InventoryFull)
                {
                    Reason = ECMLCommandRejectionReason::TransferDestinationFull;
                }
                Reject(State, Context.ExecutingTick, Command, Reason);
                return;
            }
            SynchronizeToolDurability(State, *Catalog, Inventory, Updated);
            State.Inventories.Inventories[InventoryIndex] = MoveTemp(Updated);
        }

        void ExecuteSlotMove(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLStableId InventoryId;
            int32 SourceIndex = 0;
            int32 DestinationIndex = 0;
            int64 Amount = 0;
            FCMLSimulationState& State = *Context.WorkingState;
            if (!FCMLSlotMoveCommandPayload::TryDecode(
                    Command, InventoryId, SourceIndex, DestinationIndex, Amount))
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::SlotMoveMalformed);
                return;
            }
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, InventoryId);
            if (InventoryIndex == INDEX_NONE)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::SlotMoveInventoryMissing);
                return;
            }

            const FCMLInventoryState& Inventory =
                State.Inventories.Inventories[InventoryIndex];
            FCMLInventoryState Updated;
            ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
            if (!FCMLInventoryOperations::TryMoveWithinInventory(
                    Inventory,
                    Catalog->ToItemCatalog(),
                    SourceIndex,
                    DestinationIndex,
                    Amount,
                    Updated,
                    Failure))
            {
                Reject(State, Context.ExecutingTick, Command,
                    Failure == ECMLInventoryFailure::InvalidDefinition
                        ? ECMLCommandRejectionReason::SlotMoveSlotOutOfRange
                        : ECMLCommandRejectionReason::SlotMoveBlocked);
                return;
            }
            SynchronizeToolDurability(State, *Catalog, Inventory, Updated);
            State.Inventories.Inventories[InventoryIndex] = MoveTemp(Updated);
        }

        void ExecuteGather(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            if (InventoryIndex == INDEX_NONE)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferDestinationMissing);
                return;
            }
            if (Command.DestinationId.IsNone() || Command.Payload.Num() != 1
                || Command.QuantizedValue <= 0 || Command.QuantizedValue > MAX_int32)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const FCMLInventoryState& Inventory = State.Inventories.Inventories[InventoryIndex];
            const FCMLHandGatherResult Result = FCMLHarvestRules::Gather(
                Inventory,
                Catalog->ToItemCatalog(),
                static_cast<ECMLHandGatherTarget>(Command.Payload[0]),
                static_cast<int32>(Command.QuantizedValue),
                CapacityFor(*Catalog, Inventory));
            if (!Result.Gathered())
            {
                Reject(State, Context.ExecutingTick, Command,
                    Result.Status == ECMLHandGatherStatus::InventoryFull
                        ? ECMLCommandRejectionReason::TransferDestinationFull
                        : ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            SynchronizeToolDurability(
                State, *Catalog, Inventory, Result.UpdatedInventory);
            State.Inventories.Inventories[InventoryIndex] = Result.UpdatedInventory;
        }

        void ExecuteMiningImpact(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            if (InventoryIndex == INDEX_NONE || Command.DestinationId.IsNone()
                || Command.Payload.Num() != 3)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            const int32 SlotIndex = (Command.Payload[1] << 8) | Command.Payload[2];
            const FCMLInventoryState& Inventory = State.Inventories.Inventories[InventoryIndex];
            if (!Inventory.Slots.IsValidIndex(SlotIndex)
                || !Inventory.Slots[SlotIndex].bHasStack)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }

            FCMLItemDefinition ToolDefinition;
            const FCMLStableId ToolItemId = Inventory.Slots[SlotIndex].Stack.ItemId;
            if (!Catalog->TryGetItem(ToolItemId, ToolDefinition)
                || ToolDefinition.MaximumDurability <= 0)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            FCMLToolState Tool;
            Tool.ItemId = ToolItemId;
            Tool.Maximum = ToolDefinition.MaximumDurability;
            Tool.Current = ReadToolDurability(
                State, *Catalog, Inventory, SlotIndex);

            const FCMLStableId ProgressKey = MiningProgressKey(Command.DestinationId);
            const int32 CompletedHits = static_cast<int32>(ReadQuantity(State, ProgressKey));
            const FCMLMiningImpactResult Result = FCMLHarvestRules::Impact(
                Inventory,
                Catalog->ToItemCatalog(),
                Tool,
                static_cast<ECMLMiningTarget>(Command.Payload[0]),
                CompletedHits,
                CapacityFor(*Catalog, Inventory));
            if (Result.Status == ECMLMiningImpactStatus::WrongTool
                || Result.Status == ECMLMiningImpactStatus::BrokenTool
                || Result.Status == ECMLMiningImpactStatus::InvalidTarget)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }

            WriteQuantity(State, ProgressKey, Result.NextHitProgress);
            if (Result.Status == ECMLMiningImpactStatus::Produced)
            {
                SynchronizeToolDurability(
                    State, *Catalog, Inventory, Result.UpdatedInventory);
                State.Inventories.Inventories[InventoryIndex] = Result.UpdatedInventory;
                WriteToolDurability(
                    State, SlotIndex, ToolItemId, Result.UpdatedTool.Current);
            }
            else if (Result.Status == ECMLMiningImpactStatus::InventoryFull)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferDestinationFull);
            }
        }

        void ExecuteTreeImpact(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            constexpr int32 HitsRequired = 5;
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            if (InventoryIndex == INDEX_NONE || Command.DestinationId.IsNone()
                || Command.Payload.Num() != 2)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            const int32 SlotIndex = (Command.Payload[0] << 8) | Command.Payload[1];
            const FCMLInventoryState& Inventory = State.Inventories.Inventories[InventoryIndex];
            if (!Inventory.Slots.IsValidIndex(SlotIndex)
                || !Inventory.Slots[SlotIndex].bHasStack)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            FCMLItemDefinition ToolDefinition;
            const FCMLStableId ToolItemId = Inventory.Slots[SlotIndex].Stack.ItemId;
            if (!Catalog->TryGetItem(ToolItemId, ToolDefinition)
                || FCMLHarvestRules::RequiredHits(ToolItemId) == 0)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            const int64 CurrentDurability = ReadToolDurability(
                State, *Catalog, Inventory, SlotIndex);
            if (CurrentDurability <= 0)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }

            const FCMLStableId ProgressKey = TreeProgressKey(Command.DestinationId);
            const int32 CompletedHits = static_cast<int32>(ReadQuantity(State, ProgressKey));
            if (CompletedHits + 1 < HitsRequired)
            {
                WriteQuantity(State, ProgressKey, CompletedHits + 1);
                return;
            }

            // Unity's deterministic yield is 3..5 from the stable tree id.
            const int64 Yield = 3 + static_cast<int64>(Command.DestinationId.Low % 3ULL);
            FCMLInventoryState Updated;
            ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
            if (!FCMLInventoryOperations::TryStoreEntire(
                    Inventory,
                    Catalog->ToItemCatalog(),
                    CMLContentIds::WoodLog,
                    Yield,
                    CapacityFor(*Catalog, Inventory),
                    Updated,
                    Failure))
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferDestinationFull);
                return;
            }
            SynchronizeToolDurability(State, *Catalog, Inventory, Updated);
            State.Inventories.Inventories[InventoryIndex] = MoveTemp(Updated);
            WriteToolDurability(
                State, SlotIndex, ToolItemId, static_cast<int32>(CurrentDurability - 1));
            WriteQuantity(State, ProgressKey, 0);
        }

        void ExecuteQuickTransfer(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            const int32 NodeIndex = FindNodeIndex(State.Machines, Command.DestinationId);
            if (InventoryIndex == INDEX_NONE || NodeIndex == INDEX_NONE
                || Command.Payload.Num() != 2)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const int32 SlotIndex = (Command.Payload[0] << 8) | Command.Payload[1];
            const FCMLInventoryState& Player = State.Inventories.Inventories[InventoryIndex];
            FCMLMachineNodeState& Node = State.Machines.Nodes[NodeIndex];
            FCMLTransferEndpoint Source;
            FCMLTransferEndpoint Destination;
            FCMLStableId ItemId;
            int64 Amount = 1;

            // Taking completed output is always the first intent. It prevents a
            // full output from being hidden behind a selected input item.
            for (const FCMLMachineSlot& Slot : Node.Output.Slots)
            {
                if (!Slot.ItemId.IsNone() && Slot.Quantity.Value > 0)
                {
                    Source = FCMLTransferEndpoint::Port(
                        Node.Id,
                        Node.bInputOutputAliased
                            ? ECMLMachinePortKind::Storage
                            : ECMLMachinePortKind::Output);
                    Destination = FCMLTransferEndpoint::Inventory(Player.InventoryId);
                    ItemId = Slot.ItemId;
                    break;
                }
            }

            if (ItemId.IsNone() && Player.Slots.IsValidIndex(SlotIndex)
                && Player.Slots[SlotIndex].bHasStack)
            {
                ItemId = Player.Slots[SlotIndex].Stack.ItemId;
                Source = FCMLTransferEndpoint::Inventory(Player.InventoryId);
                ECMLMachinePortKind PortKind = ECMLMachinePortKind::Input;
                if (Node.Kind == ECMLMachineNodeKind::Buffer)
                {
                    PortKind = ECMLMachinePortKind::Storage;
                }
                else
                {
                    FCMLMachineDefinition Definition;
                    if (Catalog->TryGetMachine(Node.DefinitionId, Definition)
                        && Definition.RequiresFuel()
                        && ItemId == Definition.FuelItemId)
                    {
                        PortKind = ECMLMachinePortKind::Fuel;
                    }
                }
                Destination = FCMLTransferEndpoint::Port(Node.Id, PortKind);
            }
            else if (ItemId.IsNone() && Node.Kind == ECMLMachineNodeKind::Buffer)
            {
                for (const FCMLMachineSlot& Slot : Node.Input.Slots)
                {
                    if (!Slot.ItemId.IsNone() && Slot.Quantity.Value > 0)
                    {
                        ItemId = Slot.ItemId;
                        Source = FCMLTransferEndpoint::Port(
                            Node.Id, ECMLMachinePortKind::Storage);
                        Destination = FCMLTransferEndpoint::Inventory(Player.InventoryId);
                        break;
                    }
                }
            }

            if (ItemId.IsNone())
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            FCMLInventorySimulationState UpdatedInventories;
            FCMLMachineSimulationState UpdatedMachines;
            ECMLTransferFailure Failure = ECMLTransferFailure::None;
            if (!FCMLTransferRule::TryTransfer(
                    State.Inventories,
                    State.Machines,
                    *Catalog,
                    Source,
                    Destination,
                    ItemId,
                    Amount,
                    UpdatedInventories,
                    UpdatedMachines,
                    Failure))
            {
                Reject(State, Context.ExecutingTick, Command,
                    Failure == ECMLTransferFailure::DestinationFull
                        ? ECMLCommandRejectionReason::TransferDestinationFull
                        : ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            const int32 UpdatedPlayerIndex = FindInventoryIndex(
                UpdatedInventories, Player.InventoryId);
            if (UpdatedPlayerIndex != INDEX_NONE)
            {
                SynchronizeToolDurability(
                    State, *Catalog, Player,
                    UpdatedInventories.Inventories[UpdatedPlayerIndex]);
            }
            State.Inventories = MoveTemp(UpdatedInventories);
            State.Machines = MoveTemp(UpdatedMachines);
        }

        void ExecuteContainerSlotMove(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            const int32 NodeIndex = FindNodeIndex(State.Machines, Command.DestinationId);
            if (InventoryIndex == INDEX_NONE || NodeIndex == INDEX_NONE
                || Command.Payload.Num() != 7)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const bool bQuick = Command.Payload[0] != 0;
            const bool bSourcePlayer = Command.Payload[1] != 0;
            const bool bDestinationPlayer = Command.Payload[2] != 0;
            const int32 SourceIndex = (Command.Payload[3] << 8) | Command.Payload[4];
            const int32 DestinationIndex = (Command.Payload[5] << 8) | Command.Payload[6];
            const FCMLInventoryState& Player = State.Inventories.Inventories[InventoryIndex];
            FCMLMachineNodeState& Node = State.Machines.Nodes[NodeIndex];
            if (Node.Kind != ECMLMachineNodeKind::Buffer || !Node.bInputOutputAliased)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }

            FCMLInventoryState UpdatedPlayer = Player;
            FCMLMachinePort UpdatedStorage = Node.Input;
            const auto MachineAsInventorySlot = [](const FCMLMachineSlot& Slot)
            {
                FCMLInventorySlot Result;
                Result.bHasStack = !Slot.ItemId.IsNone() && Slot.Quantity.Value > 0;
                if (Result.bHasStack)
                {
                    Result.Stack.ItemId = Slot.ItemId;
                    Result.Stack.Quantity.Value = Slot.Quantity.Value;
                }
                return Result;
            };
            const auto WriteMachineSlot = [](FCMLMachineSlot& Destination, const FCMLInventorySlot& Source)
            {
                Destination.ItemId = Source.bHasStack
                    ? Source.Stack.ItemId : FCMLStableId::None();
                Destination.Quantity.Value = Source.bHasStack
                    ? Source.Stack.Quantity.Value : 0;
            };

            if (bQuick)
            {
                if (bSourcePlayer)
                {
                    if (!UpdatedPlayer.Slots.IsValidIndex(SourceIndex)
                        || !UpdatedPlayer.Slots[SourceIndex].bHasStack)
                    {
                        Reject(State, Context.ExecutingTick, Command,
                            ECMLCommandRejectionReason::TransferNotAdmitted);
                        return;
                    }
                    const FCMLItemStack Stack = UpdatedPlayer.Slots[SourceIndex].Stack;
                    if (Stack.Quantity.Value > static_cast<uint64>(MAX_int64)
                        || !FCMLMachinePortOperations::TryStore(
                            UpdatedStorage, Stack.ItemId,
                            static_cast<int64>(Stack.Quantity.Value), *Catalog))
                    {
                        Reject(State, Context.ExecutingTick, Command,
                            ECMLCommandRejectionReason::TransferDestinationFull);
                        return;
                    }
                    UpdatedPlayer.Slots[SourceIndex] = FCMLInventorySlot();
                }
                else
                {
                    if (!UpdatedStorage.Slots.IsValidIndex(SourceIndex))
                    {
                        Reject(State, Context.ExecutingTick, Command,
                            ECMLCommandRejectionReason::TransferNotAdmitted);
                        return;
                    }
                    const FCMLMachineSlot Stack = UpdatedStorage.Slots[SourceIndex];
                    if (Stack.ItemId.IsNone() || Stack.Quantity.Value == 0
                        || Stack.Quantity.Value > static_cast<uint64>(MAX_int64))
                    {
                        Reject(State, Context.ExecutingTick, Command,
                            ECMLCommandRejectionReason::TransferNotAdmitted);
                        return;
                    }
                    ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
                    FCMLInventoryState StoredPlayer;
                    if (!FCMLInventoryOperations::TryStoreEntire(
                            UpdatedPlayer, Catalog->ToItemCatalog(), Stack.ItemId,
                            static_cast<int64>(Stack.Quantity.Value),
                            CapacityFor(*Catalog, UpdatedPlayer), StoredPlayer, Failure))
                    {
                        Reject(State, Context.ExecutingTick, Command,
                            ECMLCommandRejectionReason::TransferDestinationFull);
                        return;
                    }
                    UpdatedPlayer = MoveTemp(StoredPlayer);
                    UpdatedStorage.Slots[SourceIndex] = FCMLMachineSlot();
                }
            }
            else
            {
                const bool bSourceValid = bSourcePlayer
                    ? UpdatedPlayer.Slots.IsValidIndex(SourceIndex)
                    : UpdatedStorage.Slots.IsValidIndex(SourceIndex);
                const bool bDestinationValid = bDestinationPlayer
                    ? UpdatedPlayer.Slots.IsValidIndex(DestinationIndex)
                    : UpdatedStorage.Slots.IsValidIndex(DestinationIndex);
                if (!bSourceValid || !bDestinationValid
                    || (bSourcePlayer == bDestinationPlayer
                        && SourceIndex == DestinationIndex))
                {
                    Reject(State, Context.ExecutingTick, Command,
                        ECMLCommandRejectionReason::TransferMalformed);
                    return;
                }

                FCMLInventoryState Pair;
                Pair.Slots.SetNum(2);
                Pair.Slots[0] = bSourcePlayer
                    ? UpdatedPlayer.Slots[SourceIndex]
                    : MachineAsInventorySlot(UpdatedStorage.Slots[SourceIndex]);
                Pair.Slots[1] = bDestinationPlayer
                    ? UpdatedPlayer.Slots[DestinationIndex]
                    : MachineAsInventorySlot(UpdatedStorage.Slots[DestinationIndex]);
                FCMLInventoryState MovedPair;
                ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
                if (!FCMLInventoryOperations::TryMoveWithinInventory(
                        Pair, Catalog->ToItemCatalog(), 0, 1,
                        Command.QuantizedValue, MovedPair, Failure))
                {
                    Reject(State, Context.ExecutingTick, Command,
                        ECMLCommandRejectionReason::SlotMoveBlocked);
                    return;
                }
                if (bSourcePlayer)
                    UpdatedPlayer.Slots[SourceIndex] = MovedPair.Slots[0];
                else
                    WriteMachineSlot(UpdatedStorage.Slots[SourceIndex], MovedPair.Slots[0]);
                if (bDestinationPlayer)
                    UpdatedPlayer.Slots[DestinationIndex] = MovedPair.Slots[1];
                else
                    WriteMachineSlot(UpdatedStorage.Slots[DestinationIndex], MovedPair.Slots[1]);

            }

            int64 PlayerQuantity = 0;
            for (const FCMLInventorySlot& Slot : UpdatedPlayer.Slots)
                if (Slot.bHasStack) PlayerQuantity += static_cast<int64>(Slot.Stack.Quantity.Value);
            int64 StorageQuantity = 0;
            for (const FCMLMachineSlot& Slot : UpdatedStorage.Slots)
                StorageQuantity += static_cast<int64>(Slot.Quantity.Value);
            FCMLContainerDefinition Container;
            if (PlayerQuantity > CapacityFor(*Catalog, UpdatedPlayer)
                || !Catalog->TryGetContainer(Node.DefinitionId, Container)
                || StorageQuantity > Container.Capacity)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferDestinationFull);
                return;
            }

            SynchronizeToolDurability(State, *Catalog, Player, UpdatedPlayer);
            State.Inventories.Inventories[InventoryIndex] = MoveTemp(UpdatedPlayer);
            Node.Input = MoveTemp(UpdatedStorage);
            Node.Output = Node.Input;
        }

        void ExecuteMachineItemTransfer(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 InventoryIndex = FindInventoryIndex(
                State.Inventories, Command.InitiatorId);
            const int32 NodeIndex = FindNodeIndex(
                State.Machines, Command.DestinationId);
            if (InventoryIndex == INDEX_NONE || NodeIndex == INDEX_NONE
                || Command.Payload.Num() != 26)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const ECMLMachinePortKind SourcePort =
                static_cast<ECMLMachinePortKind>(Command.Payload[0]);
            const ECMLMachinePortKind DestinationPort =
                static_cast<ECMLMachinePortKind>(Command.Payload[1]);
            const FCMLStableId ItemId = ReadStableId(Command.Payload, 2);
            const uint64 AmountBits = ReadU64(Command.Payload, 18);
            if (ItemId.IsNone() || AmountBits == 0
                || AmountBits > static_cast<uint64>(MAX_int64)
                || (SourcePort == ECMLMachinePortKind::None
                    && DestinationPort == ECMLMachinePortKind::None)
                || SourcePort == DestinationPort)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }

            const FCMLInventoryState& Player =
                State.Inventories.Inventories[InventoryIndex];
            const FCMLMachineNodeState& Node = State.Machines.Nodes[NodeIndex];
            const FCMLTransferEndpoint Source =
                SourcePort == ECMLMachinePortKind::None
                    ? FCMLTransferEndpoint::Inventory(Player.InventoryId)
                    : FCMLTransferEndpoint::Port(Node.Id, SourcePort);
            const FCMLTransferEndpoint Destination =
                DestinationPort == ECMLMachinePortKind::None
                    ? FCMLTransferEndpoint::Inventory(Player.InventoryId)
                    : FCMLTransferEndpoint::Port(Node.Id, DestinationPort);

            FCMLInventorySimulationState UpdatedInventories;
            FCMLMachineSimulationState UpdatedMachines;
            ECMLTransferFailure Failure = ECMLTransferFailure::None;
            if (!FCMLTransferRule::TryTransfer(
                    State.Inventories,
                    State.Machines,
                    *Catalog,
                    Source,
                    Destination,
                    ItemId,
                    static_cast<int64>(AmountBits),
                    UpdatedInventories,
                    UpdatedMachines,
                    Failure))
            {
                Reject(State, Context.ExecutingTick, Command,
                    Failure == ECMLTransferFailure::DestinationFull
                        ? ECMLCommandRejectionReason::TransferDestinationFull
                        : ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }

            const int32 UpdatedPlayerIndex = FindInventoryIndex(
                UpdatedInventories, Player.InventoryId);
            if (UpdatedPlayerIndex != INDEX_NONE)
            {
                SynchronizeToolDurability(
                    State,
                    *Catalog,
                    Player,
                    UpdatedInventories.Inventories[UpdatedPlayerIndex]);
            }
            State.Inventories = MoveTemp(UpdatedInventories);
            State.Machines = MoveTemp(UpdatedMachines);
        }

        void ExecuteBuild(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            constexpr int32 PayloadLength = 16 + 8 + 8 + 8 + 4 + 16;
            FCMLSimulationState& State = *Context.WorkingState;
            if (Command.DestinationId.IsNone() || Command.Payload.Num() != PayloadLength)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::BuildMalformed);
                return;
            }
            const FCMLStableId BuildItemId = ReadStableId(Command.Payload, 0);
            FCMLMachineBuildPose Pose;
            Pose.XMillimetres = static_cast<int64>(ReadU64(Command.Payload, 16));
            Pose.YMillimetres = static_cast<int64>(ReadU64(Command.Payload, 24));
            Pose.ZMillimetres = static_cast<int64>(ReadU64(Command.Payload, 32));
            Pose.YawQuarterTurns = static_cast<int32>(
                (static_cast<uint32>(Command.Payload[40]) << 24)
                | (static_cast<uint32>(Command.Payload[41]) << 16)
                | (static_cast<uint32>(Command.Payload[42]) << 8)
                | static_cast<uint32>(Command.Payload[43]));
            const FCMLStableId ExtractionRecipeId = ReadStableId(Command.Payload, 44);

            // The authored support is structural dressing, not a transport
            // cell in Unity's graph. It still consumes its crafted item and is
            // spawned after commit, but deliberately creates no belt node.
            if (BuildItemId == CMLContentIds::BeltSupport)
            {
                const int32 InventoryIndex = FindInventoryIndex(
                    State.Inventories, Command.InitiatorId);
                if (InventoryIndex == INDEX_NONE)
                {
                    Reject(State, Context.ExecutingTick, Command,
                        ECMLCommandRejectionReason::BuildSourceMissing);
                    return;
                }
                FCMLInventoryState Updated;
                ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
                if (!FCMLInventoryOperations::TryTakeEntire(
                        State.Inventories.Inventories[InventoryIndex],
                        BuildItemId, 1, Updated, Failure))
                {
                    Reject(State, Context.ExecutingTick, Command,
                        ECMLCommandRejectionReason::InsufficientQuantity);
                    return;
                }
                SynchronizeToolDurability(
                    State, *Catalog,
                    State.Inventories.Inventories[InventoryIndex], Updated);
                State.Inventories.Inventories[InventoryIndex] = MoveTemp(Updated);
                return;
            }

            FCMLMachineBuildSpecification Specification;
            if (!BuildSpecificationFor(BuildItemId, Pose, ExtractionRecipeId, Specification))
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::BuildDefinitionMissing);
                return;
            }
            FCMLMachineSimulationState UpdatedMachines;
            FCMLInventorySimulationState UpdatedInventories;
            ECMLBuildRejection Rejection = ECMLBuildRejection::None;
            if (!FCMLMachineBuildRule::TryApply(
                    State.Machines,
                    State.Inventories,
                    *Catalog,
                    Command.InitiatorId,
                    Command.DestinationId,
                    Specification,
                    UpdatedMachines,
                    UpdatedInventories,
                    Rejection))
            {
                ECMLCommandRejectionReason Reason = ECMLCommandRejectionReason::BuildMalformed;
                if (Rejection == ECMLBuildRejection::InsufficientQuantity)
                    Reason = ECMLCommandRejectionReason::InsufficientQuantity;
                else if (Rejection == ECMLBuildRejection::BuildTopologyInvalid)
                    Reason = ECMLCommandRejectionReason::BuildTopologyInvalid;
                else if (Rejection == ECMLBuildRejection::BuildDefinitionMissing)
                    Reason = ECMLCommandRejectionReason::BuildDefinitionMissing;
                Reject(State, Context.ExecutingTick, Command, Reason);
                return;
            }
            const int32 BeforePlayerIndex = FindInventoryIndex(
                State.Inventories, Command.InitiatorId);
            const int32 AfterPlayerIndex = FindInventoryIndex(
                UpdatedInventories, Command.InitiatorId);
            if (BeforePlayerIndex != INDEX_NONE && AfterPlayerIndex != INDEX_NONE)
            {
                SynchronizeToolDurability(
                    State, *Catalog,
                    State.Inventories.Inventories[BeforePlayerIndex],
                    UpdatedInventories.Inventories[AfterPlayerIndex]);
            }
            State.Machines = MoveTemp(UpdatedMachines);
            State.Inventories = MoveTemp(UpdatedInventories);
        }

        void ExecuteAirshipRepairInstall(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 AirshipIndex = FindAirshipIndex(State.Airship, Command.DestinationId);
            const int32 InventoryIndex = FindInventoryIndex(State.Inventories, Command.InitiatorId);
            if (AirshipIndex == INDEX_NONE || InventoryIndex == INDEX_NONE
                || Command.Payload.Num() != 16 || Command.QuantizedValue != 1)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            FCMLAirshipEntityState& Airship = State.Airship.Airships[AirshipIndex];
            if (Airship.RepairStatus != ECMLAirshipRepairStatus::Damaged)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            const FCMLStableId ItemId = ReadStableId(Command.Payload, 0);
            int64* Installed = nullptr;
            int64 Required = 0;
            if (ItemId == CMLContentIds::IronPlate)
            {
                Installed = &Airship.InstalledIronPlates;
                Required = RequiredAirshipIronPlates;
            }
            else if (ItemId == CMLContentIds::InsulatedCable)
            {
                Installed = &Airship.InstalledInsulatedCables;
                Required = RequiredAirshipCables;
            }
            if (Installed == nullptr || *Installed >= Required)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            const FCMLInventoryState& PlayerInventory =
                State.Inventories.Inventories[InventoryIndex];
            FCMLInventoryState Updated;
            ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
            if (!FCMLInventoryOperations::TryTakeEntire(
                    PlayerInventory, ItemId, 1, Updated, Failure))
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::InsufficientQuantity);
                return;
            }
            SynchronizeToolDurability(State, *Catalog, PlayerInventory, Updated);
            State.Inventories.Inventories[InventoryIndex] = MoveTemp(Updated);
            ++(*Installed);
            if (Airship.InstalledIronPlates >= RequiredAirshipIronPlates
                && Airship.InstalledInsulatedCables >= RequiredAirshipCables)
            {
                Airship.RepairStatus = ECMLAirshipRepairStatus::Repairing;
                Airship.RepairTicksRemaining = AirshipRepairDurationTicks;
            }
        }

        void ExecuteAirshipPilotBegin(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 AirshipIndex = FindAirshipIndex(State.Airship, Command.DestinationId);
            const int32 PlayerIndex = FindAirshipPlayerIndex(State.Airship, RuntimePlayerId);
            if (AirshipIndex == INDEX_NONE || PlayerIndex == INDEX_NONE || !Command.Payload.IsEmpty())
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            FCMLAirshipEntityState& Airship = State.Airship.Airships[AirshipIndex];
            FCMLAirshipPlayerState& Player = State.Airship.Players[PlayerIndex];
            if (Airship.RepairStatus != ECMLAirshipRepairStatus::Repaired
                || !Airship.PilotId.IsNone() || Player.bIsPiloting)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            Airship.PilotId = RuntimePlayerId;
            Airship.HeldInput = FCMLAirshipPilotInput();
            Player.FrameKind = ECMLAirshipPlayerFrameKind::Airship;
            Player.FrameAirshipId = Airship.Id;
            Player.bIsPiloting = true;
        }

        void ExecuteAirshipPilotEnd(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 AirshipIndex = FindAirshipIndex(State.Airship, Command.DestinationId);
            const int32 PlayerIndex = FindAirshipPlayerIndex(State.Airship, RuntimePlayerId);
            if (AirshipIndex == INDEX_NONE || PlayerIndex == INDEX_NONE || !Command.Payload.IsEmpty())
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            FCMLAirshipEntityState& Airship = State.Airship.Airships[AirshipIndex];
            FCMLAirshipPlayerState& Player = State.Airship.Players[PlayerIndex];
            if (Airship.PilotId != RuntimePlayerId || !Player.bIsPiloting)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            Airship.PilotId = FCMLStableId::None();
            Airship.HeldInput = FCMLAirshipPilotInput();
            Player.bIsPiloting = false;
        }

        void ExecuteAirshipPilotInput(
            FCMLSimulationPhaseContext& Context,
            const FCMLSimulationCommand& Command) const
        {
            FCMLSimulationState& State = *Context.WorkingState;
            const int32 AirshipIndex = FindAirshipIndex(State.Airship, Command.DestinationId);
            if (AirshipIndex == INDEX_NONE || Command.Payload.Num() != 16)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferMalformed);
                return;
            }
            FCMLAirshipEntityState& Airship = State.Airship.Airships[AirshipIndex];
            if (Airship.PilotId != RuntimePlayerId
                || Airship.RepairStatus != ECMLAirshipRepairStatus::Repaired)
            {
                Reject(State, Context.ExecutingTick, Command,
                    ECMLCommandRejectionReason::TransferNotAdmitted);
                return;
            }
            FCMLAirshipPilotInput Input;
            Input.ThrottleChangePermille = FMath::Clamp(ReadI32(Command.Payload, 0), -1000, 1000);
            Input.LiftPermille = FMath::Clamp(ReadI32(Command.Payload, 4), -1000, 1000);
            Input.YawDeltaPermille = FMath::Clamp(ReadI32(Command.Payload, 8), -1000, 1000);
            Input.PitchDeltaPermille = FMath::Clamp(ReadI32(Command.Payload, 12), -1000, 1000);
            if (Airship.Mode == ECMLAirshipFlightMode::Anchored
                && Input.ThrottleChangePermille != 0)
            {
                Airship.Mode = ECMLAirshipFlightMode::Flying;
                Airship.DockedLandingSurfaceId = FCMLStableId::None();
                ResetAirshipMotion(Airship);
            }
            Airship.HeldInput = Input;
        }

        const FCMLGameCatalog* Catalog = nullptr;
    };

    class FCMLMachinePhaseSystem final : public ICMLSimulationPhaseSystem
    {
    public:
        FCMLMachinePhaseSystem(
            const FCMLGameCatalog* InCatalog,
            const ECMLSimulationPhase InPhase,
            const uint64 InStableOrdinal)
            : Catalog(InCatalog), Phase(InPhase), StableOrdinal(InStableOrdinal)
        {
        }

        virtual ECMLSimulationPhase GetPhase() const override { return Phase; }
        virtual int32 GetOrder() const override { return 0; }
        virtual FCMLStableId GetStableOrderId() const override
        {
            return FCMLStableId(0x434D4C4D41434849ULL, StableOrdinal);
        }
        virtual FString GetTypeName() const override
        {
            return FString::Printf(TEXT("CMLMachinePhase%u"), static_cast<uint8>(Phase));
        }
        virtual bool Execute(FCMLSimulationPhaseContext& Context, FString& OutFailureCause) override
        {
            if (Catalog == nullptr || Context.WorkingState == nullptr)
            {
                OutFailureCause = TEXT("The machine phase has no catalog or working state.");
                return false;
            }
            if (Phase == ECMLSimulationPhase::ItemFluidFlowAndReservations)
            {
                FCMLMachineSpatialTopology::Advance(Context.WorkingState->Machines, *Catalog);
            }
            else if (Phase == ECMLSimulationPhase::CyclesNeedsAndTimers)
            {
                FCMLMachineCycle::AdvanceCycles(Context.WorkingState->Machines, *Catalog);
            }
            else if (Phase == ECMLSimulationPhase::CompletionDamageAndEventStaging)
            {
                FCMLMachineCycle::CompleteCycles(Context.WorkingState->Machines, *Catalog);
            }
            return true;
        }

    private:
        const FCMLGameCatalog* Catalog = nullptr;
        ECMLSimulationPhase Phase = ECMLSimulationPhase::None;
        uint64 StableOrdinal = 0;
    };

    class FCMLAirshipMovementPhaseSystem final : public ICMLSimulationPhaseSystem
    {
    public:
        virtual ECMLSimulationPhase GetPhase() const override
        {
            return ECMLSimulationPhase::MovementAndPortalDetection;
        }
        virtual int32 GetOrder() const override { return 100; }
        virtual FCMLStableId GetStableOrderId() const override
        {
            return FCMLStableId(0x4149525F4D4F5645ULL, 1);
        }
        virtual FString GetTypeName() const override { return TEXT("CMLAirshipMovement"); }
        virtual bool Execute(FCMLSimulationPhaseContext& Context, FString& OutFailureCause) override
        {
            if (Context.WorkingState == nullptr)
            {
                OutFailureCause = TEXT("The airship movement phase has no working state.");
                return false;
            }
            FCMLAirshipSimulationState& State = Context.WorkingState->Airship;
            for (FCMLAirshipEntityState& Airship : State.Airships)
            {
                if (Airship.RepairStatus == ECMLAirshipRepairStatus::Repairing)
                {
                    Airship.RepairTicksRemaining = FMath::Max<int64>(
                        0, Airship.RepairTicksRemaining - 1);
                    if (Airship.RepairTicksRemaining == 0)
                    {
                        Airship.RepairStatus = ECMLAirshipRepairStatus::Repaired;
                    }
                }
                if (Airship.Mode != ECMLAirshipFlightMode::Flying)
                {
                    Airship.HeldInput = FCMLAirshipPilotInput();
                    ResetAirshipMotion(Airship);
                    continue;
                }

                const FCMLAirshipEntityState Before = Airship;
                FCMLAirshipControls::UpdateFlightControls(Airship);
                FCMLAirshipIntegration::IntegrateFlight(Airship);
                if (!FCMLAirshipCollision::IsCandidateClear(
                        State, Before.Pose, Airship.Pose))
                {
                    const FCMLAirshipPilotInput Held = Airship.HeldInput;
                    Airship = Before;
                    Airship.HeldInput = Held;
                    ResetAirshipMotion(Airship);
                }
            }
            return true;
        }
    };

    class FCMLBeltLinePhaseSystem final : public ICMLSimulationPhaseSystem
    {
    public:
        virtual ECMLSimulationPhase GetPhase() const override
        {
            return ECMLSimulationPhase::LocalTopologyChanges;
        }
        virtual int32 GetOrder() const override { return 900; }
        virtual FCMLStableId GetStableOrderId() const override
        {
            return FCMLStableId(0x42454C545F4C494EULL, 0x4500000000000001ULL);
        }
        virtual FString GetTypeName() const override { return TEXT("CMLBeltLineTopology"); }
        virtual bool Execute(FCMLSimulationPhaseContext& Context, FString& OutFailureCause) override
        {
            if (Context.WorkingState == nullptr)
            {
                OutFailureCause = TEXT("The belt topology phase has no working state.");
                return false;
            }
            FCMLBeltLineRules::Recompute(Context.WorkingState->Machines);
            return true;
        }
    };
}

void UCMLSimulationSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
    Super::Initialize(Collection);
    Clock.Reset();
    PendingSequenceTick = FCMLSimulationTick();
    NextPendingSequence = 0;
    bRuntimeReady = false;

    Catalog = FCMLBootstrapCatalog::Create();
    ECMLCatalogFailure CatalogFailure = ECMLCatalogFailure::None;
    FCMLStableId FailingId;
    if (!Catalog.Validate(CatalogFailure, FailingId))
    {
        UE_LOG(LogCMLSimulation, Error,
            TEXT("Bootstrap catalog refused (failure %d, id %s)."),
            static_cast<int32>(CatalogFailure), *FailingId.ToString());
        return;
    }

    FCMLInventoryState PlayerInventory;
    PlayerInventory.InventoryId = CMLContentIds::PlayerInventory;
    PlayerInventory.ContainerDefinitionId = CMLContentIds::PlayerInventory;
    FCMLContainerDefinition PlayerContainer;
    if (!Catalog.TryGetContainer(CMLContentIds::PlayerInventory, PlayerContainer))
    {
        UE_LOG(LogCMLSimulation, Error, TEXT("The bootstrap catalog has no player container."));
        return;
    }
    PlayerInventory.Slots.SetNum(PlayerContainer.SlotCount);

#if WITH_EDITOR
    // Temporary editor-only kit used to audit every placeable imported from
    // Unity. It is deliberately unavailable in packaged builds and on maps
    // other than the Starter Island, so it cannot alter progression or the
    // production balance. Keeping one stack per definition also puts every
    // module directly in reach of the hotbar/inventory UI during the audit.
    if (const UWorld* World = GetWorld();
        World != nullptr
        && World->GetMapName().Contains(TEXT("A_10_StarterIsland_AxisPreview")))
    {
        constexpr int64 TestQuantity = 20;
        const FCMLStableId BuildTestItems[] = {
            CMLContentIds::BeltStraight,
            CMLContentIds::BeltCurve,
            CMLContentIds::BeltCurveLeft,
            CMLContentIds::BeltIncline,
            CMLContentIds::BeltSupport,
            CMLContentIds::BeltDriveUnit,
            CMLContentIds::BeltFunnel,
            CMLContentIds::MechanicalPressItem,
            CMLContentIds::MechanicalDrillItem,
        };
        const FCMLItemCatalog ItemCatalog = Catalog.ToItemCatalog();
        for (const FCMLStableId& ItemId : BuildTestItems)
        {
            FCMLInventoryState Updated;
            ECMLInventoryFailure Failure = ECMLInventoryFailure::None;
            if (!FCMLInventoryOperations::TryStoreEntire(
                    PlayerInventory,
                    ItemCatalog,
                    ItemId,
                    TestQuantity,
                    PlayerContainer.Capacity,
                    Updated,
                    Failure))
            {
                UE_LOG(LogCMLSimulation, Error,
                    TEXT("Could not seed editor build-test item %s (failure %d)."),
                    *ItemId.ToString(), static_cast<int32>(Failure));
                break;
            }
            PlayerInventory = MoveTemp(Updated);
        }
    }
#endif

    FCMLSimulationState InitialState;
    InitialState.ContentRevision = Catalog.Revision.Value;
    InitialState.CatalogSchemaVersion = Catalog.SchemaVersion;
    InitialState.Inventories.Inventories.Add(MoveTemp(PlayerInventory));
    InitialState.SortForCanonicalEncoding();
    Engine.SetState(InitialState);
    Engine.RegisterSystem(MakeShared<FCMLRuntimeInventoryCommandSystem>(&Catalog));
    Engine.RegisterSystem(MakeShared<FCMLAirshipMovementPhaseSystem>());
    Engine.RegisterSystem(MakeShared<FCMLBeltLinePhaseSystem>());
    Engine.RegisterSystem(MakeShared<FCMLMachinePhaseSystem>(
        &Catalog, ECMLSimulationPhase::ItemFluidFlowAndReservations, 1));
    Engine.RegisterSystem(MakeShared<FCMLMachinePhaseSystem>(
        &Catalog, ECMLSimulationPhase::CyclesNeedsAndTimers, 2));
    Engine.RegisterSystem(MakeShared<FCMLMachinePhaseSystem>(
        &Catalog, ECMLSimulationPhase::CompletionDamageAndEventStaging, 3));

    bRuntimeReady = true;
    RebuildPlayerPresentation();
}

void UCMLSimulationSubsystem::Deinitialize()
{
    Clock.Reset();
    PendingBuildVisualLocations.Reset();
    bRuntimeReady = false;
    Super::Deinitialize();
}

void UCMLSimulationSubsystem::Tick(const float DeltaTime)
{
    if (!bRuntimeReady)
    {
        return;
    }
    const int32 Steps = Clock.Accumulate(DeltaTime);
    for (int32 StepIndex = 0; StepIndex < Steps; ++StepIndex)
    {
        AdvanceOneStep();
    }
}

TStatId UCMLSimulationSubsystem::GetStatId() const
{
    RETURN_QUICK_DECLARE_CYCLE_STAT(UCMLSimulationSubsystem, STATGROUP_Tickables);
}

void UCMLSimulationSubsystem::AdvanceOneStep()
{
    const FCMLSimulationTickResult Result = Engine.AdvanceOneTick();
    if (!Result.bCommitted)
    {
        UE_LOG(LogCMLSimulation, Error, TEXT("Simulation tick aborted in phase %d: %s"),
            static_cast<int32>(Result.FailedPhase), *Result.FailureCause);
        return;
    }

    RebuildPlayerPresentation();
    ResolveCommandsForTick(Result.ExecutingTick);
    OnPlayerInventoryChanged.Broadcast();
    OnSimulationAdvanced.Broadcast(static_cast<int64>(Result.ExecutingTick.Value));
}

bool UCMLSimulationSubsystem::GetPlayerInventoryPresentation(
    FCMLInventoryUiSnapshot& OutSnapshot) const
{
    OutSnapshot = PlayerInventoryPresentation;
    return OutSnapshot.Slots.Num() == FCMLInventoryHudPresenter::PlayerSlotCount;
}

bool UCMLSimulationSubsystem::GetPlayerInventory(FCMLInventoryState& OutInventory) const
{
    const int32 Index = FindInventoryIndex(Engine.GetState().Inventories, CMLContentIds::PlayerInventory);
    if (Index == INDEX_NONE)
    {
        OutInventory = FCMLInventoryState();
        return false;
    }
    OutInventory = Engine.GetState().Inventories.Inventories[Index];
    return true;
}

bool UCMLSimulationSubsystem::EnqueueForNextTick(
    FCMLSimulationCommand& Command,
    FCMLRuntimeCommandHandle* OutHandle)
{
    if (!bRuntimeReady)
    {
        return false;
    }
    FCMLSimulationTick TargetTick;
    if (!Engine.GetState().Tick.TryNext(TargetTick))
    {
        return false;
    }
    if (PendingSequenceTick.Value != TargetTick.Value)
    {
        PendingSequenceTick = TargetTick;
        NextPendingSequence = 0;
    }
    Command.TargetTick = TargetTick;
    Command.Sequence = NextPendingSequence;
    if (Command.Kind == BuildNodeKind && Command.DestinationId.IsNone())
    {
        // A placed node needs an id before the command enters the deterministic
        // queue. Tick + sequence is unique inside this simulation lifetime and
        // does not depend on pointer values or actor iteration order.
        Command.DestinationId = FCMLStableId(
            0x7800000000000000ULL,
            (TargetTick.Value << 20) ^ Command.Sequence ^ 0x434D4C4255494C44ULL);
    }
    if (!Engine.TryEnqueueCommand(Command))
    {
        return false;
    }
    PendingRuntimeCommands.Add(Command);
    if (OutHandle != nullptr)
    {
        OutHandle->Tick = TargetTick;
        OutHandle->Sequence = Command.Sequence;
    }
    ++NextPendingSequence;
    return true;
}

bool UCMLSimulationSubsystem::RequestStorePlayerItem(
    const FCMLStableId& ItemId,
    const int64 Amount)
{
    FCMLSimulationCommand Command;
    Command.Kind = StoreItemKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = ItemId;
    Command.QuantizedValue = Amount;
    return EnqueueForNextTick(Command);
}

bool UCMLSimulationSubsystem::RequestCraftPlayerItem(
    const FCMLStableId& RecipeId,
    const ECMLCraftingStationKind Station,
    const int64 CraftCount)
{
    FCMLSimulationCommand Command;
    Command.Kind = CraftItemKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = RecipeId;
    Command.QuantizedValue = CraftCount;
    Command.Payload.Add(static_cast<uint8>(Station));
    return EnqueueForNextTick(Command);
}

bool UCMLSimulationSubsystem::RequestMovePlayerSlot(
    const int32 SourceSlotIndex,
    const int32 DestinationSlotIndex,
    const int64 Amount)
{
    FCMLSimulationCommand Command;
    Command.Kind = FCMLSlotMoveCommandPayload::CommandKind();
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.QuantizedValue = Amount;
    if (!FCMLSlotMoveCommandPayload::TryEncode(
            SourceSlotIndex, DestinationSlotIndex, Command.Payload))
    {
        return false;
    }
    return EnqueueForNextTick(Command);
}

bool UCMLSimulationSubsystem::RequestMoveContainerSlot(
    const FCMLStableId& NodeId,
    const bool bSourceIsPlayer,
    const int32 SourceSlotIndex,
    const bool bDestinationIsPlayer,
    const int32 DestinationSlotIndex,
    const int64 Amount)
{
    if (NodeId.IsNone()
        || SourceSlotIndex < 0 || SourceSlotIndex > MAX_uint16
        || DestinationSlotIndex < 0 || DestinationSlotIndex > MAX_uint16
        || Amount < 0)
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = ContainerSlotMoveKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = NodeId;
    Command.QuantizedValue = Amount;
    Command.Payload = {
        0,
        static_cast<uint8>(bSourceIsPlayer ? 1 : 0),
        static_cast<uint8>(bDestinationIsPlayer ? 1 : 0),
        static_cast<uint8>((SourceSlotIndex >> 8) & 0xFF),
        static_cast<uint8>(SourceSlotIndex & 0xFF),
        static_cast<uint8>((DestinationSlotIndex >> 8) & 0xFF),
        static_cast<uint8>(DestinationSlotIndex & 0xFF)};
    return EnqueueForNextTick(Command);
}

bool UCMLSimulationSubsystem::RequestQuickTransferContainerSlot(
    const FCMLStableId& NodeId,
    const bool bSourceIsPlayer,
    const int32 SourceSlotIndex)
{
    if (NodeId.IsNone() || SourceSlotIndex < 0 || SourceSlotIndex > MAX_uint16)
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = ContainerSlotMoveKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = NodeId;
    Command.Payload = {
        1,
        static_cast<uint8>(bSourceIsPlayer ? 1 : 0),
        static_cast<uint8>(bSourceIsPlayer ? 0 : 1),
        static_cast<uint8>((SourceSlotIndex >> 8) & 0xFF),
        static_cast<uint8>(SourceSlotIndex & 0xFF),
        0,
        0};
    return EnqueueForNextTick(Command);
}

bool UCMLSimulationSubsystem::RequestTransferMachineItem(
    const FCMLStableId& NodeId,
    const ECMLMachinePortKind SourcePort,
    const ECMLMachinePortKind DestinationPort,
    const FCMLStableId& ItemId,
    const int64 Amount)
{
    if (NodeId.IsNone() || ItemId.IsNone() || Amount <= 0
        || SourcePort == DestinationPort
        || (SourcePort == ECMLMachinePortKind::None
            && DestinationPort == ECMLMachinePortKind::None))
    {
        return false;
    }

    FCMLSimulationCommand Command;
    Command.Kind = MachineItemTransferKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = NodeId;
    Command.Payload.Add(static_cast<uint8>(SourcePort));
    Command.Payload.Add(static_cast<uint8>(DestinationPort));
    AppendStableId(Command.Payload, ItemId);
    AppendU64(Command.Payload, static_cast<uint64>(Amount));
    return EnqueueForNextTick(Command);
}

int64 UCMLSimulationSubsystem::AllowedMachineTransferQuantity(
    const FCMLStableId& NodeId,
    const ECMLMachinePortKind SourcePort,
    const ECMLMachinePortKind DestinationPort,
    const FCMLStableId& ItemId,
    const int64 RequestedAmount) const
{
    const FCMLSimulationState& State = Engine.GetState();
    const int32 InventoryIndex = FindInventoryIndex(
        State.Inventories, CMLContentIds::PlayerInventory);
    const int32 NodeIndex = FindNodeIndex(State.Machines, NodeId);
    if (InventoryIndex == INDEX_NONE || NodeIndex == INDEX_NONE
        || ItemId.IsNone() || RequestedAmount <= 0
        || SourcePort == DestinationPort
        || (SourcePort == ECMLMachinePortKind::None
            && DestinationPort == ECMLMachinePortKind::None))
    {
        return 0;
    }

    const FCMLInventoryState& Player = State.Inventories.Inventories[InventoryIndex];
    const FCMLMachineNodeState& Node = State.Machines.Nodes[NodeIndex];
    const FCMLTransferEndpoint Source = SourcePort == ECMLMachinePortKind::None
        ? FCMLTransferEndpoint::Inventory(Player.InventoryId)
        : FCMLTransferEndpoint::Port(Node.Id, SourcePort);
    const FCMLTransferEndpoint Destination = DestinationPort == ECMLMachinePortKind::None
        ? FCMLTransferEndpoint::Inventory(Player.InventoryId)
        : FCMLTransferEndpoint::Port(Node.Id, DestinationPort);

    // The core transfer is deliberately all-or-nothing. A cursor move is not:
    // like Unity/Minecraft it deposits as much as fits and leaves the remainder
    // attached to the cursor. Admission is monotonic, so find that prefix
    // without mutating the published state.
    int64 Low = 1;
    int64 High = RequestedAmount;
    int64 Best = 0;
    while (Low <= High)
    {
        const int64 Candidate = Low + (High - Low) / 2;
        FCMLInventorySimulationState UpdatedInventories;
        FCMLMachineSimulationState UpdatedMachines;
        ECMLTransferFailure Failure = ECMLTransferFailure::None;
        if (FCMLTransferRule::TryTransfer(
                State.Inventories,
                State.Machines,
                Catalog,
                Source,
                Destination,
                ItemId,
                Candidate,
                UpdatedInventories,
                UpdatedMachines,
                Failure))
        {
            Best = Candidate;
            Low = Candidate + 1;
        }
        else if (Failure == ECMLTransferFailure::DestinationFull
            || Failure == ECMLTransferFailure::InsufficientSource)
        {
            High = Candidate - 1;
        }
        else
        {
            return 0;
        }
    }
    return Best;
}

ECMLMachinePortKind UCMLSimulationSubsystem::PreferredMachinePortForItem(
    const FCMLStableId& NodeId,
    const FCMLStableId& ItemId) const
{
    const int32 NodeIndex = FindNodeIndex(Engine.GetState().Machines, NodeId);
    if (NodeIndex == INDEX_NONE || ItemId.IsNone())
    {
        return ECMLMachinePortKind::None;
    }
    const FCMLMachineNodeState& Node = Engine.GetState().Machines.Nodes[NodeIndex];
    if (Node.Kind == ECMLMachineNodeKind::Buffer)
    {
        return ECMLMachinePortKind::Storage;
    }
    FCMLMachineDefinition Definition;
    if (Catalog.TryGetMachine(Node.DefinitionId, Definition)
        && Definition.RequiresFuel()
        && Definition.FuelItemId == ItemId)
    {
        return ECMLMachinePortKind::Fuel;
    }
    return ECMLMachinePortKind::Input;
}

bool UCMLSimulationSubsystem::RequestHandGather(
    const FCMLStableId& SourceId,
    const ECMLHandGatherTarget Target,
    const int32 Units,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    FCMLSimulationCommand Command;
    Command.Kind = GatherItemKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = SourceId;
    Command.QuantizedValue = Units;
    Command.Payload.Add(static_cast<uint8>(Target));
    return EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestMiningImpact(
    const FCMLStableId& SourceId,
    const ECMLMiningTarget Target,
    const int32 EquippedSlotIndex,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    if (EquippedSlotIndex < 0 || EquippedSlotIndex > MAX_uint16)
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = MiningImpactKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = SourceId;
    Command.Payload = {
        static_cast<uint8>(Target),
        static_cast<uint8>((EquippedSlotIndex >> 8) & 0xFF),
        static_cast<uint8>(EquippedSlotIndex & 0xFF)};
    return EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestTreeImpact(
    const FCMLStableId& SourceId,
    const int32 EquippedSlotIndex,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    if (EquippedSlotIndex < 0 || EquippedSlotIndex > MAX_uint16)
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = TreeImpactKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = SourceId;
    Command.Payload = {
        static_cast<uint8>((EquippedSlotIndex >> 8) & 0xFF),
        static_cast<uint8>(EquippedSlotIndex & 0xFF)};
    return EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestQuickTransfer(
    const FCMLStableId& NodeId,
    const int32 SelectedPlayerSlot,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    if (SelectedPlayerSlot < 0 || SelectedPlayerSlot > MAX_uint16)
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = QuickTransferKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = NodeId;
    Command.Payload = {
        static_cast<uint8>((SelectedPlayerSlot >> 8) & 0xFF),
        static_cast<uint8>(SelectedPlayerSlot & 0xFF)};
    return EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestBuild(
    const FCMLStableId& BuildItemId,
    const FCMLMachineBuildPose& Pose,
    const FCMLStableId& ExtractionRecipeId,
    const FVector& VisualWorldLocation,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    if (BuildItemId.IsNone()
        || Pose.YawQuarterTurns < 0 || Pose.YawQuarterTurns > 3)
    {
        return false;
    }

    FCMLSimulationCommand Command;
    Command.Kind = BuildNodeKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    AppendStableId(Command.Payload, BuildItemId);
    AppendU64(Command.Payload, static_cast<uint64>(Pose.XMillimetres));
    AppendU64(Command.Payload, static_cast<uint64>(Pose.YMillimetres));
    AppendU64(Command.Payload, static_cast<uint64>(Pose.ZMillimetres));
    const uint32 Yaw = static_cast<uint32>(Pose.YawQuarterTurns);
    Command.Payload.Add(static_cast<uint8>((Yaw >> 24) & 0xFF));
    Command.Payload.Add(static_cast<uint8>((Yaw >> 16) & 0xFF));
    Command.Payload.Add(static_cast<uint8>((Yaw >> 8) & 0xFF));
    Command.Payload.Add(static_cast<uint8>(Yaw & 0xFF));
    AppendStableId(Command.Payload, ExtractionRecipeId);
    if (!EnqueueForNextTick(Command, &OutHandle))
    {
        return false;
    }
    // This is deliberately outside Command.Payload: visual root correction is
    // not canonical simulation state and must not alter Unity/Unreal hashes.
    const uint64 PresentationKey =
        (OutHandle.Tick.Value * 0x9E3779B185EBCA87ULL) ^ OutHandle.Sequence;
    PendingBuildVisualLocations.Add(PresentationKey, VisualWorldLocation);
    return true;
}

bool UCMLSimulationSubsystem::ConsumePendingBuildVisual(
    const FCMLSimulationCommand& Command, FVector& OutWorldLocation)
{
    const uint64 PresentationKey =
        (Command.TargetTick.Value * 0x9E3779B185EBCA87ULL) ^ Command.Sequence;
    if (const FVector* Location = PendingBuildVisualLocations.Find(PresentationKey))
    {
        OutWorldLocation = *Location;
        PendingBuildVisualLocations.Remove(PresentationKey);
        return true;
    }
    return false;
}

bool UCMLSimulationSubsystem::TryPreflightBuild(
    const FCMLStableId& BuildItemId,
    const FCMLMachineBuildPose& Pose,
    const FCMLStableId& ExtractionRecipeId,
    ECMLBuildRejection& OutRejection) const
{
    OutRejection = ECMLBuildRejection::None;
    if (!bRuntimeReady)
    {
        OutRejection = ECMLBuildRejection::BuildSourceMissing;
        return false;
    }

    // Unity treats the support as structural dressing: it consumes its own
    // item but deliberately does not add a transport node.
    if (BuildItemId == CMLContentIds::BeltSupport)
    {
        FCMLInventoryState Inventory;
        if (!GetPlayerInventory(Inventory))
        {
            OutRejection = ECMLBuildRejection::BuildSourceMissing;
            return false;
        }
        if (FCMLInventoryOperations::Count(Inventory, BuildItemId) < 1)
        {
            OutRejection = ECMLBuildRejection::InsufficientQuantity;
            return false;
        }
        return true;
    }

    FCMLMachineBuildSpecification Specification;
    if (!BuildSpecificationFor(BuildItemId, Pose, ExtractionRecipeId, Specification))
    {
        OutRejection = ECMLBuildRejection::BuildDefinitionMissing;
        return false;
    }
    const FCMLSimulationState& State = Engine.GetState();
    return FCMLMachineBuildRule::TryPreflight(
        State.Machines,
        State.Inventories,
        Catalog,
        CMLContentIds::PlayerInventory,
        Specification,
        OutRejection);
}

bool UCMLSimulationSubsystem::RequestAirshipRepairInstall(
    const FCMLStableId& AirshipId,
    const FCMLStableId& ItemId,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    if (AirshipId.IsNone() || ItemId.IsNone())
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = AirshipRepairInstallKind;
    Command.InitiatorId = CMLContentIds::PlayerInventory;
    Command.DestinationId = AirshipId;
    Command.QuantizedValue = 1;
    AppendStableId(Command.Payload, ItemId);
    return EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestAirshipPilotBegin(
    const FCMLStableId& AirshipId,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    FCMLSimulationCommand Command;
    Command.Kind = AirshipPilotBeginKind;
    Command.InitiatorId = RuntimePlayerId;
    Command.DestinationId = AirshipId;
    return !AirshipId.IsNone() && EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestAirshipPilotEnd(
    const FCMLStableId& AirshipId,
    FCMLRuntimeCommandHandle& OutHandle)
{
    OutHandle = FCMLRuntimeCommandHandle();
    FCMLSimulationCommand Command;
    Command.Kind = AirshipPilotEndKind;
    Command.InitiatorId = RuntimePlayerId;
    Command.DestinationId = AirshipId;
    return !AirshipId.IsNone() && EnqueueForNextTick(Command, &OutHandle);
}

bool UCMLSimulationSubsystem::RequestAirshipPilotInput(
    const FCMLStableId& AirshipId,
    const int32 ThrottlePermille,
    const int32 LiftPermille,
    const int32 YawPermille,
    const int32 PitchPermille)
{
    if (AirshipId.IsNone())
    {
        return false;
    }
    FCMLSimulationCommand Command;
    Command.Kind = AirshipPilotInputKind;
    Command.InitiatorId = RuntimePlayerId;
    Command.DestinationId = AirshipId;
    AppendI32(Command.Payload, FMath::Clamp(ThrottlePermille, -1000, 1000));
    AppendI32(Command.Payload, FMath::Clamp(LiftPermille, -1000, 1000));
    AppendI32(Command.Payload, FMath::Clamp(YawPermille, -1000, 1000));
    AppendI32(Command.Payload, FMath::Clamp(PitchPermille, -1000, 1000));
    return EnqueueForNextTick(Command);
}

bool UCMLSimulationSubsystem::RegisterWorldMachine(
    const FCMLStableId& NodeId,
    const FCMLStableId& MachineDefinitionId,
    const FCMLStableId& ActiveRecipeId,
    const FCMLMachineBuildPose& Pose)
{
    if (!bRuntimeReady || NodeId.IsNone()
        || FindNodeIndex(Engine.GetState().Machines, NodeId) != INDEX_NONE)
    {
        return false;
    }
    FCMLMachineDefinition Definition;
    if (!Catalog.TryGetMachine(MachineDefinitionId, Definition))
    {
        return false;
    }
    FCMLSimulationState State = Engine.GetState();
    FCMLMachineNodeState Node = FCMLMachineBuildRule::CreateMachine(
        NodeId,
        MachineDefinitionId,
        Definition.InputSlots,
        Definition.OutputSlots,
        Definition.FuelSlots,
        Pose);
    Node.ActiveRecipeId = ActiveRecipeId;
    Node.Activity = ECMLMachineActivity::Idle;
    State.Machines.Nodes.Add(MoveTemp(Node));
    State.SortForCanonicalEncoding();
    Engine.SetState(State);
    return true;
}

bool UCMLSimulationSubsystem::RegisterWorldBuffer(
    const FCMLStableId& NodeId,
    const FCMLStableId& ContainerDefinitionId,
    const FCMLMachineBuildPose& Pose)
{
    if (!bRuntimeReady || NodeId.IsNone()
        || FindNodeIndex(Engine.GetState().Machines, NodeId) != INDEX_NONE)
    {
        return false;
    }
    FCMLContainerDefinition Definition;
    if (!Catalog.TryGetContainer(ContainerDefinitionId, Definition))
    {
        return false;
    }
    FCMLSimulationState State = Engine.GetState();
    State.Machines.Nodes.Add(FCMLMachineBuildRule::CreateBuffer(
        NodeId, ContainerDefinitionId, Definition.SlotCount, Pose));
    State.SortForCanonicalEncoding();
    Engine.SetState(State);
    return true;
}

bool UCMLSimulationSubsystem::RegisterWorldAirship(
    const FCMLStableId& AirshipId,
    const FCMLAirshipPose& Pose)
{
    if (!bRuntimeReady || AirshipId.IsNone()
        || FindAirshipIndex(Engine.GetState().Airship, AirshipId) != INDEX_NONE)
    {
        return false;
    }
    FCMLSimulationState State = Engine.GetState();
    FCMLAirshipEntityState Airship;
    Airship.Id = AirshipId;
    Airship.Pose = Pose;
    Airship.Mode = ECMLAirshipFlightMode::Anchored;
    Airship.RepairStatus = ECMLAirshipRepairStatus::Damaged;
    State.Airship.Airships.Add(MoveTemp(Airship));

    if (FindAirshipPlayerIndex(State.Airship, RuntimePlayerId) == INDEX_NONE)
    {
        FCMLAirshipPlayerState Player;
        Player.Id = RuntimePlayerId;
        Player.FrameKind = ECMLAirshipPlayerFrameKind::World;
        State.Airship.Players.Add(MoveTemp(Player));
    }
    if (FindInventoryIndex(State.Inventories, CMLContentIds::AirshipHold) == INDEX_NONE)
    {
        FCMLContainerDefinition HoldDefinition;
        if (Catalog.TryGetContainer(CMLContentIds::AirshipHold, HoldDefinition))
        {
            FCMLInventoryState Hold;
            Hold.InventoryId = CMLContentIds::AirshipHold;
            Hold.ContainerDefinitionId = CMLContentIds::AirshipHold;
            Hold.Slots.SetNum(HoldDefinition.SlotCount);
            State.Inventories.Inventories.Add(MoveTemp(Hold));
        }
    }
    State.SortForCanonicalEncoding();
    Engine.SetState(State);
    return true;
}

bool UCMLSimulationSubsystem::SeedWorldBufferItem(
    const FCMLStableId& NodeId,
    const FCMLStableId& ItemId,
    const int64 Amount)
{
    if (!bRuntimeReady || NodeId.IsNone() || ItemId.IsNone() || Amount <= 0)
    {
        return false;
    }
    FCMLSimulationState State = Engine.GetState();
    const int32 NodeIndex = FindNodeIndex(State.Machines, NodeId);
    if (NodeIndex == INDEX_NONE
        || State.Machines.Nodes[NodeIndex].Kind != ECMLMachineNodeKind::Buffer
        || !FCMLMachinePortOperations::TryStore(
            State.Machines.Nodes[NodeIndex].Input, ItemId, Amount, Catalog))
    {
        return false;
    }
    State.Machines.Nodes[NodeIndex].Output = State.Machines.Nodes[NodeIndex].Input;
    State.SortForCanonicalEncoding();
    Engine.SetState(State);
    return true;
}

bool UCMLSimulationSubsystem::GetAirshipState(
    const FCMLStableId& AirshipId,
    FCMLAirshipEntityState& OutState) const
{
    const int32 Index = FindAirshipIndex(Engine.GetState().Airship, AirshipId);
    if (Index == INDEX_NONE)
    {
        OutState = FCMLAirshipEntityState();
        return false;
    }
    OutState = Engine.GetState().Airship.Airships[Index];
    return true;
}

bool UCMLSimulationSubsystem::GetLocalPilotedAirship(FCMLStableId& OutAirshipId) const
{
    OutAirshipId = FCMLStableId::None();
    const int32 PlayerIndex = FindAirshipPlayerIndex(Engine.GetState().Airship, RuntimePlayerId);
    if (PlayerIndex == INDEX_NONE)
    {
        return false;
    }
    const FCMLAirshipPlayerState& Player = Engine.GetState().Airship.Players[PlayerIndex];
    if (!Player.bIsPiloting || Player.FrameAirshipId.IsNone())
    {
        return false;
    }
    OutAirshipId = Player.FrameAirshipId;
    return true;
}

bool UCMLSimulationSubsystem::GetMachinePresentation(
    const FCMLStableId& NodeId,
    FCMLMachineUiSnapshot& OutSnapshot) const
{
    FCMLMachineNodeReport Report;
    if (!FCMLMachineDiagnostics::TryDescribe(
            Engine.GetState().Machines, Catalog, NodeId, Report))
    {
        OutSnapshot = FCMLMachineUiSnapshot();
        return false;
    }
    OutSnapshot = FCMLMachineHudPresenter::Project(Report, Catalog);
    return true;
}

bool UCMLSimulationSubsystem::GetBufferPresentation(
    const FCMLStableId& NodeId,
    TArray<FCMLInventorySlotPresentation>& OutSlots) const
{
    OutSlots.Reset();
    const int32 Index = FindNodeIndex(Engine.GetState().Machines, NodeId);
    if (Index == INDEX_NONE)
    {
        return false;
    }
    const FCMLMachineNodeState& Node = Engine.GetState().Machines.Nodes[Index];
    if (Node.Kind != ECMLMachineNodeKind::Buffer)
    {
        return false;
    }
    OutSlots.Reserve(Node.Input.Slots.Num());
    for (int32 SlotIndex = 0; SlotIndex < Node.Input.Slots.Num(); ++SlotIndex)
    {
        const FCMLMachineSlot& Slot = Node.Input.Slots[SlotIndex];
        if (Slot.ItemId.IsNone() || Slot.Quantity.Value <= 0)
        {
            OutSlots.Add(FCMLInventoryHudPresenter::EmptySlot(SlotIndex));
            continue;
        }
        FCMLItemDefinition Definition;
        if (!Catalog.TryGetItem(Slot.ItemId, Definition))
        {
            return false;
        }
        OutSlots.Add(FCMLInventoryHudPresenter::ProjectSlot(
            SlotIndex, Slot.ItemId, Slot.Quantity.Value, Definition));
    }
    return true;
}

void UCMLSimulationSubsystem::ResolveCommandsForTick(const FCMLSimulationTick& Tick)
{
    const FCMLSimulationState& State = Engine.GetState();
    for (int32 Index = PendingRuntimeCommands.Num() - 1; Index >= 0; --Index)
    {
        const FCMLSimulationCommand& Command = PendingRuntimeCommands[Index];
        if (Command.TargetTick.Value != Tick.Value)
        {
            continue;
        }
        bool bRejected = false;
        for (const FCMLCommandRejection& Rejection : State.CommandRejections)
        {
            if (Rejection.Tick.Value == Tick.Value
                && Rejection.Command.Sequence == Command.Sequence)
            {
                bRejected = true;
                break;
            }
        }
        bool bWorldCommitted = !bRejected && Command.Kind == GatherItemKind;
        if (!bRejected && Command.Kind == MiningImpactKind)
        {
            const FCMLStableId ProgressKey = MiningProgressKey(Command.DestinationId);
            const ECMLMiningTarget Target = Command.Payload.Num() == 3
                ? static_cast<ECMLMiningTarget>(Command.Payload[0])
                : ECMLMiningTarget::None;
            bWorldCommitted = ReadQuantity(State, ProgressKey) == 0
                && Target != ECMLMiningTarget::None;
        }
        else if (!bRejected && Command.Kind == TreeImpactKind)
        {
            bWorldCommitted = ReadQuantity(State, TreeProgressKey(Command.DestinationId)) == 0;
        }
        else if (!bRejected && Command.Kind == BuildNodeKind)
        {
            bWorldCommitted = true;
        }
        OnRuntimeCommandResolved.Broadcast(Command, !bRejected, bWorldCommitted);
        PendingRuntimeCommands.RemoveAt(Index);
    }
}

void UCMLSimulationSubsystem::RebuildPlayerPresentation()
{
    FCMLInventoryState Inventory;
    if (!GetPlayerInventory(Inventory)
        || !FCMLInventoryHudPresenter::TryProject(
            Inventory, Catalog.ToItemCatalog(), PlayerInventoryPresentation))
    {
        PlayerInventoryPresentation = FCMLInventoryUiSnapshot();
        UE_LOG(LogCMLSimulation, Error,
            TEXT("The authoritative player inventory could not be projected for the HUD."));
        return;
    }

    for (int32 SlotIndex = 0; SlotIndex < Inventory.Slots.Num(); ++SlotIndex)
    {
        const FCMLInventorySlot& Slot = Inventory.Slots[SlotIndex];
        if (!Slot.bHasStack)
        {
            continue;
        }
        FCMLItemDefinition Definition;
        if (!Catalog.TryGetItem(Slot.Stack.ItemId, Definition) || !Definition.HasDurability())
        {
            continue;
        }
        FCMLToolState Tool;
        if (TryGetToolStateForSlot(SlotIndex, Tool))
        {
            PlayerInventoryPresentation.Slots[SlotIndex] =
                FCMLInventoryHudPresenter::ProjectToolSlot(
                    SlotIndex,
                    Slot.Stack.ItemId,
                    Slot.Stack.Quantity.Value,
                    Definition,
                    Tool.Current,
                    Tool.Maximum);
        }
    }
}

bool UCMLSimulationSubsystem::TryGetToolStateForSlot(
    const int32 SlotIndex,
    FCMLToolState& OutTool) const
{
    OutTool = FCMLToolState();
    FCMLInventoryState Inventory;
    if (!GetPlayerInventory(Inventory) || !Inventory.Slots.IsValidIndex(SlotIndex)
        || !Inventory.Slots[SlotIndex].bHasStack)
    {
        return false;
    }
    FCMLItemDefinition Definition;
    const FCMLStableId ItemId = Inventory.Slots[SlotIndex].Stack.ItemId;
    if (!Catalog.TryGetItem(ItemId, Definition) || !Definition.HasDurability())
    {
        return false;
    }
    OutTool.ItemId = ItemId;
    OutTool.Maximum = Definition.MaximumDurability;
    OutTool.Current = ReadToolDurability(
        Engine.GetState(), Catalog, Inventory, SlotIndex);
    return true;
}

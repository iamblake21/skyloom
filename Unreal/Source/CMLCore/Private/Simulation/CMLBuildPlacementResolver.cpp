#include "Simulation/CMLBuildPlacementResolver.h"

#include "Content/CMLContentIds.h"
#include "Simulation/CMLBeltModuleShape.h"
#include "Simulation/CMLMachineSpatialTopology.h"

namespace
{
    uint8 Opposite(const uint8 Yaw)
    {
        return static_cast<uint8>((Yaw + 2) & 3);
    }

    const FCMLMachineNodeState* FindNode(
        const FCMLMachineSimulationState& State, const FCMLStableId& Id)
    {
        for (const FCMLMachineNodeState& Candidate : State.Nodes)
        {
            if (Candidate.Id == Id)
            {
                return &Candidate;
            }
        }
        return nullptr;
    }

    bool TryDirectionBetween(
        const FCMLMachineBuildPose& Source,
        const FCMLMachineBuildPose& Target,
        const int32 AllowedRiseMillimetres,
        uint8& OutDirection)
    {
        const int64 DeltaX = Target.XMillimetres - Source.XMillimetres;
        const int64 DeltaY = Target.YMillimetres - Source.YMillimetres;
        const int64 DeltaZ = Target.ZMillimetres - Source.ZMillimetres;
        if (DeltaY != 0
            && (AllowedRiseMillimetres == 0
                || FMath::Abs(DeltaY) != FMath::Abs(static_cast<int64>(AllowedRiseMillimetres))))
        {
            OutDirection = 0;
            return false;
        }

        constexpr int64 Cell = FCMLMachineSpatialTopology::GridCellSizeMillimetres;
        if (DeltaX == 0 && DeltaZ == Cell) { OutDirection = 0; return true; }
        if (DeltaX == Cell && DeltaZ == 0) { OutDirection = 1; return true; }
        if (DeltaX == 0 && DeltaZ == -Cell) { OutDirection = 2; return true; }
        if (DeltaX == -Cell && DeltaZ == 0) { OutDirection = 3; return true; }
        OutDirection = 0;
        return false;
    }

    uint8 DirectionFromTo(
        const FCMLMachineBuildPose& Source,
        const FCMLMachineBuildPose& Target,
        const uint8 Fallback)
    {
        uint8 Direction = 0;
        if (TryDirectionBetween(Source, Target, 0, Direction))
        {
            return Direction;
        }
        const int64 DeltaX = Target.XMillimetres - Source.XMillimetres;
        const int64 DeltaZ = Target.ZMillimetres - Source.ZMillimetres;
        if (DeltaX == 0 && DeltaZ == 0)
        {
            return static_cast<uint8>(Fallback & 3);
        }
        if (FMath::Abs(DeltaX) > FMath::Abs(DeltaZ))
        {
            return DeltaX >= 0 ? 1 : 3;
        }
        return DeltaZ >= 0 ? 0 : 2;
    }

    bool IsBehind(
        const FCMLMachineBuildPose& Funnel, const FCMLMachineBuildPose& Other)
    {
        uint8 Direction = 0;
        return TryDirectionBetween(Funnel, Other, 0, Direction)
            && Direction == Opposite(static_cast<uint8>(Funnel.YawQuarterTurns));
    }

    bool IsInFront(
        const FCMLMachineBuildPose& Funnel, const FCMLMachineBuildPose& Other)
    {
        uint8 Direction = 0;
        return TryDirectionBetween(Funnel, Other, 0, Direction)
            && Direction == static_cast<uint8>(Funnel.YawQuarterTurns);
    }

    bool IsOnAxis(const uint8 Direction, const uint8 Yaw)
    {
        return Direction == (Yaw & 3) || Direction == Opposite(Yaw);
    }

    bool TryConnectionHeight(
        const ECMLMachineNodeKind Kind,
        const FCMLMachineBuildPose& Pose,
        const FCMLStableId& DefinitionId,
        const uint8 SideYaw,
        int64& OutHeight)
    {
        OutHeight = Pose.YMillimetres;
        if (Kind != ECMLMachineNodeKind::BeltModule
            && Kind != ECMLMachineNodeKind::Machine)
        {
            return false;
        }
        int32 EndpointHeight = 0;
        if (!FCMLBeltModuleShape::TryGetEndpointHeightMillimetres(
                DefinitionId,
                static_cast<uint8>(Pose.YawQuarterTurns),
                SideYaw,
                EndpointHeight))
        {
            return false;
        }
        OutHeight += EndpointHeight;
        return true;
    }
}

FCMLMachineBuildPose FCMLBuildPlacementResolver::ResolveFromTarget(
    const FCMLMachineSimulationState& State,
    const FCMLStableId& TargetId,
    const FCMLMachineBuildPose& Desired,
    const ECMLMachineBuildKind HeldKind,
    const uint8 AimedSideYaw,
    const uint8 HeldYaw,
    const bool bYawExplicitlyRotated,
    const FCMLStableId& HeldDefinitionId)
{
    const FCMLMachineNodeState* Target = FindNode(State, TargetId);
    if (Target == nullptr || !Target->bHasPlacementPose)
    {
        return Desired;
    }
    ensureAlwaysMsgf(
        Target->Id == TargetId,
        TEXT("Placement resolver selected a different target node."));

    uint8 Side = static_cast<uint8>(AimedSideYaw & 3);
    uint8 Yaw = static_cast<uint8>(HeldYaw & 3);
    uint8 TargetBeltEntryYaw = static_cast<uint8>(Target->PlacementPose.YawQuarterTurns);
    uint8 TargetBeltExitYaw = FCMLBeltModuleShape::ForwardExitYaw(
        Target->DefinitionId, static_cast<uint8>(Target->PlacementPose.YawQuarterTurns));
    uint8 TargetBeltEntrySide = Opposite(TargetBeltEntryYaw);
    uint8 ResolvedEntryYaw = 0;
    uint8 ResolvedExitYaw = 0;
    if (Target->Kind == ECMLMachineNodeKind::BeltModule
        && FCMLBeltModuleShape::TryResolveTravelYaws(
            Target->DefinitionId,
            static_cast<uint8>(Target->PlacementPose.YawQuarterTurns),
            Target->BeltTravelDirection,
            ResolvedEntryYaw,
            ResolvedExitYaw))
    {
        TargetBeltEntryYaw = ResolvedEntryYaw;
        TargetBeltExitYaw = ResolvedExitYaw;
        TargetBeltEntrySide = Opposite(TargetBeltEntryYaw);
    }

    if (Target->Kind == ECMLMachineNodeKind::Buffer)
    {
        Side = bYawExplicitlyRotated ? Yaw : Side;
        return Offset(Target->PlacementPose, Side, Side);
    }

    if (HeldKind == ECMLMachineBuildKind::Buffer
        && Target->Kind == ECMLMachineNodeKind::Funnel)
    {
        Side = Opposite(static_cast<uint8>(Target->PlacementPose.YawQuarterTurns));
    }
    else if (HeldKind == ECMLMachineBuildKind::BeltModule
        && Target->Kind == ECMLMachineNodeKind::Funnel)
    {
        Side = static_cast<uint8>(Target->PlacementPose.YawQuarterTurns);
        if (!bYawExplicitlyRotated) Yaw = Side;
    }
    else if (HeldKind == ECMLMachineBuildKind::Funnel
        && Target->Kind == ECMLMachineNodeKind::BeltModule)
    {
        if (!bYawExplicitlyRotated) Yaw = Opposite(Side);
    }
    else if (HeldKind == ECMLMachineBuildKind::BeltModule
        && Target->Kind == ECMLMachineNodeKind::BeltModule)
    {
        if (Side != TargetBeltExitYaw && Side != TargetBeltEntrySide)
        {
            Side = TargetBeltExitYaw;
        }
        if (!bYawExplicitlyRotated)
        {
            Yaw = Side == TargetBeltExitYaw
                ? TargetBeltExitYaw
                : FCMLBeltModuleShape::PlacementYawForForwardExit(
                    HeldDefinitionId, TargetBeltEntryYaw);
        }
    }
    else if (HeldKind == ECMLMachineBuildKind::Machine
        && Target->Kind == ECMLMachineNodeKind::BeltModule)
    {
        Side = static_cast<uint8>(Target->PlacementPose.YawQuarterTurns);
        if (!bYawExplicitlyRotated) Yaw = Side;
    }
    else if (HeldKind == ECMLMachineBuildKind::BeltModule
        && Target->Kind == ECMLMachineNodeKind::Machine)
    {
        const uint8 MachineExit = static_cast<uint8>(Target->PlacementPose.YawQuarterTurns);
        const uint8 MachineEntry = Opposite(MachineExit);
        if (Side != MachineExit && Side != MachineEntry) Side = MachineExit;
        if (!bYawExplicitlyRotated) Yaw = MachineExit;
    }

    int32 TargetEndpointHeight = 0;
    if (Target->Kind == ECMLMachineNodeKind::BeltModule)
    {
        FCMLBeltModuleShape::TryGetEndpointHeightMillimetres(
            Target->DefinitionId,
            static_cast<uint8>(Target->PlacementPose.YawQuarterTurns),
            Side,
            TargetEndpointHeight);
    }
    int32 HeldEndpointHeight = 0;
    if (HeldKind == ECMLMachineBuildKind::BeltModule)
    {
        FCMLBeltModuleShape::TryGetEndpointHeightMillimetres(
            HeldDefinitionId, Yaw, Opposite(Side), HeldEndpointHeight);
    }
    return Offset(
        Target->PlacementPose, Side, Yaw,
        TargetEndpointHeight - HeldEndpointHeight);
}

FCMLMachineBuildPose FCMLBuildPlacementResolver::ResolveOccupiedConnectorCell(
    const FCMLMachineSimulationState& State,
    const FCMLMachineBuildPose& SnappedPose,
    const ECMLMachineBuildKind HeldKind,
    const bool bInheritConnectorYaw,
    const FCMLStableId& HeldDefinitionId)
{
    const FCMLMachineNodeState* Occupant = FindOccupant(State, SnappedPose);
    if (Occupant == nullptr)
    {
        return SnappedPose;
    }
    return ResolveFromTarget(
        State,
        Occupant->Id,
        SnappedPose,
        HeldKind,
        static_cast<uint8>(SnappedPose.YawQuarterTurns),
        static_cast<uint8>(SnappedPose.YawQuarterTurns),
        !bInheritConnectorYaw,
        HeldDefinitionId);
}

bool FCMLBuildPlacementResolver::ArePortsAdjacent(
    const ECMLMachineNodeKind LeftKind,
    const FCMLMachineBuildPose& Left,
    const FCMLStableId& LeftDefinitionId,
    const ECMLMachineNodeKind RightKind,
    const FCMLMachineBuildPose& Right,
    const FCMLStableId& RightDefinitionId)
{
    const int32 AllowedRise = FMath::Max(
        FMath::Abs(FCMLBeltModuleShape::RiseMillimetres(LeftDefinitionId)),
        FMath::Abs(FCMLBeltModuleShape::RiseMillimetres(RightDefinitionId)));
    uint8 LeftToRight = 0;
    if (!TryDirectionBetween(Left, Right, AllowedRise, LeftToRight))
    {
        return false;
    }

    if ((LeftKind == ECMLMachineNodeKind::Buffer && RightKind == ECMLMachineNodeKind::Funnel)
        || (LeftKind == ECMLMachineNodeKind::Funnel && RightKind == ECMLMachineNodeKind::Buffer))
    {
        return LeftKind == ECMLMachineNodeKind::Funnel
            ? IsBehind(Left, Right) : IsBehind(Right, Left);
    }
    if ((LeftKind == ECMLMachineNodeKind::Machine
            && RightKind == ECMLMachineNodeKind::Funnel
            && LeftDefinitionId == CMLContentIds::MechanicalDrill)
        || (LeftKind == ECMLMachineNodeKind::Funnel
            && RightKind == ECMLMachineNodeKind::Machine
            && RightDefinitionId == CMLContentIds::MechanicalDrill))
    {
        return LeftKind == ECMLMachineNodeKind::Funnel
            ? IsBehind(Left, Right) : IsBehind(Right, Left);
    }
    if ((LeftKind == ECMLMachineNodeKind::Funnel && RightKind == ECMLMachineNodeKind::BeltModule)
        || (LeftKind == ECMLMachineNodeKind::BeltModule && RightKind == ECMLMachineNodeKind::Funnel))
    {
        const FCMLMachineBuildPose& Funnel = LeftKind == ECMLMachineNodeKind::Funnel ? Left : Right;
        const FCMLMachineBuildPose& Belt = LeftKind == ECMLMachineNodeKind::BeltModule ? Left : Right;
        return IsInFront(Funnel, Belt)
            && IsOnAxis(DirectionFromTo(Funnel, Belt,
                static_cast<uint8>(Funnel.YawQuarterTurns)),
                static_cast<uint8>(Belt.YawQuarterTurns));
    }
    if ((LeftKind == ECMLMachineNodeKind::BeltModule
            && RightKind == ECMLMachineNodeKind::BeltModule)
        || (LeftKind == ECMLMachineNodeKind::BeltModule
            && RightKind == ECMLMachineNodeKind::Machine)
        || (LeftKind == ECMLMachineNodeKind::Machine
            && RightKind == ECMLMachineNodeKind::BeltModule))
    {
        int64 LeftHeight = 0;
        int64 RightHeight = 0;
        return TryConnectionHeight(
                LeftKind, Left, LeftDefinitionId, LeftToRight, LeftHeight)
            && TryConnectionHeight(
                RightKind, Right, RightDefinitionId, Opposite(LeftToRight), RightHeight)
            && LeftHeight == RightHeight;
    }
    return false;
}

const FCMLMachineNodeState* FCMLBuildPlacementResolver::FindOccupant(
    const FCMLMachineSimulationState& State, const FCMLMachineBuildPose& Pose)
{
    for (const FCMLMachineNodeState& Candidate : State.Nodes)
    {
        if (Candidate.bHasPlacementPose
            && Candidate.PlacementPose.XMillimetres == Pose.XMillimetres
            && Candidate.PlacementPose.YMillimetres == Pose.YMillimetres
            && Candidate.PlacementPose.ZMillimetres == Pose.ZMillimetres)
        {
            return &Candidate;
        }
    }
    return nullptr;
}

FCMLMachineBuildPose FCMLBuildPlacementResolver::Offset(
    const FCMLMachineBuildPose& Source,
    const uint8 DirectionYaw,
    const uint8 HeldYaw,
    const int32 RiseMillimetres)
{
    FCMLMachineBuildPose Result = Source;
    constexpr int32 Cell = FCMLMachineSpatialTopology::GridCellSizeMillimetres;
    switch (DirectionYaw & 3)
    {
        case 0: Result.ZMillimetres += Cell; break;
        case 1: Result.XMillimetres += Cell; break;
        case 2: Result.ZMillimetres -= Cell; break;
        default: Result.XMillimetres -= Cell; break;
    }
    Result.YMillimetres += RiseMillimetres;
    Result.YawQuarterTurns = HeldYaw & 3;
    return Result;
}

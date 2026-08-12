#include "Simulation/CMLBeltModuleShape.h"

#include "Content/CMLContentIds.h"

namespace
{
    uint8 OppositeYaw(const uint8 Yaw)
    {
        return static_cast<uint8>((Yaw + 2) & 3);
    }
}

int32 FCMLBeltModuleShape::TurnQuarterTurns(const FCMLStableId& DefinitionId)
{
    if (DefinitionId == CMLContentIds::BeltCurve)
    {
        return 1;
    }
    // 3 is -1: the mirrored variant turns the other way, and that is the only
    // way to have both turns.
    return DefinitionId == CMLContentIds::BeltCurveLeft ? 3 : 0;
}

uint8 FCMLBeltModuleShape::ForwardExitYaw(
    const FCMLStableId& DefinitionId, const uint8 PlacementYaw)
{
    return static_cast<uint8>((PlacementYaw + TurnQuarterTurns(DefinitionId)) & 3);
}

bool FCMLBeltModuleShape::TryResolveTravelYaws(
    const FCMLStableId& DefinitionId,
    const uint8 PlacementYaw,
    const ECMLBeltTravelDirection Direction,
    uint8& OutEntryYaw,
    uint8& OutExitYaw)
{
    OutEntryYaw = 0;
    OutExitYaw = 0;
    if (PlacementYaw > 3)
    {
        return false;
    }

    const uint8 ForwardExit = ForwardExitYaw(DefinitionId, PlacementYaw);
    switch (Direction)
    {
        case ECMLBeltTravelDirection::Forward:
            OutEntryYaw = PlacementYaw;
            OutExitYaw = ForwardExit;
            return true;

        case ECMLBeltTravelDirection::Reverse:
            OutEntryYaw = OppositeYaw(ForwardExit);
            OutExitYaw = OppositeYaw(PlacementYaw);
            return true;

        default:
            return false;
    }
}

int32 FCMLBeltModuleShape::RiseMillimetres(const FCMLStableId& DefinitionId)
{
    return DefinitionId == CMLContentIds::BeltIncline ? InclineRiseMillimetres : 0;
}

bool FCMLBeltModuleShape::TryGetEndpointHeightMillimetres(
    const FCMLStableId& DefinitionId,
    const uint8 PlacementYaw,
    const uint8 SideYaw,
    int32& OutHeightMillimetres)
{
    OutHeightMillimetres = 0;
    if (PlacementYaw > 3 || SideYaw > 3)
    {
        return false;
    }

    if (SideYaw == OppositeYaw(PlacementYaw))
    {
        return true;
    }
    if (SideYaw != ForwardExitYaw(DefinitionId, PlacementYaw))
    {
        return false;
    }
    OutHeightMillimetres = RiseMillimetres(DefinitionId);
    return true;
}

uint8 FCMLBeltModuleShape::PlacementYawForForwardExit(
    const FCMLStableId& DefinitionId, const uint8 ExitYaw)
{
    return static_cast<uint8>((ExitYaw - TurnQuarterTurns(DefinitionId) + 4) & 3);
}

bool FCMLBeltModuleShape::IsStraightAndLevel(const FCMLStableId& DefinitionId)
{
    return TurnQuarterTurns(DefinitionId) == 0 && RiseMillimetres(DefinitionId) == 0;
}

bool FCMLBeltModuleShape::TryGetBeltTravelYaw(
    const FCMLMachineNodeState& Belt, uint8& OutTravelYaw)
{
    OutTravelYaw = 0;
    if (Belt.Kind != ECMLMachineNodeKind::BeltModule
        || !Belt.bHasPlacementPose
        || Belt.BeltTravelDirection == ECMLBeltTravelDirection::Stopped)
    {
        return false;
    }
    uint8 UnusedExit = 0;
    return TryResolveTravelYaws(
        Belt.DefinitionId,
        static_cast<uint8>(Belt.PlacementPose.YawQuarterTurns),
        Belt.BeltTravelDirection,
        OutTravelYaw,
        UnusedExit);
}

bool FCMLBeltModuleShape::TryGetBeltExitYaw(
    const FCMLMachineNodeState& Belt, uint8& OutExitYaw)
{
    OutExitYaw = 0;
    if (Belt.Kind != ECMLMachineNodeKind::BeltModule || !Belt.bHasPlacementPose)
    {
        return false;
    }
    uint8 UnusedEntry = 0;
    return TryResolveTravelYaws(
        Belt.DefinitionId,
        static_cast<uint8>(Belt.PlacementPose.YawQuarterTurns),
        Belt.BeltTravelDirection,
        UnusedEntry,
        OutExitYaw);
}

int32 FCMLBeltModuleShape::BeltRiseMillimetres(const FCMLMachineNodeState& Belt)
{
    if (Belt.Kind != ECMLMachineNodeKind::BeltModule)
    {
        return 0;
    }
    const int32 Rise = RiseMillimetres(Belt.DefinitionId);
    if (Rise == 0)
    {
        return 0;
    }
    return Belt.BeltTravelDirection == ECMLBeltTravelDirection::Forward ? Rise : -Rise;
}

#pragma once

#include "CoreMinimal.h"
#include "Foundation/CMLCoreTypes.h"
#include "Simulation/CMLMachineBuildRule.h"
#include "Simulation/CMLMachineState.h"

/**
 * Pure, deterministic placement geometry shared by the hologram and tests.
 *
 * A connection is never stored as presentation state.  This resolver chooses
 * the adjacent canonical cell and facing; MachineSpatialTopology can then
 * derive the exact same connection from the committed poses.
 */
class CMLCORE_API FCMLBuildPlacementResolver
{
public:
    static FCMLMachineBuildPose ResolveFromTarget(
        const FCMLMachineSimulationState& State,
        const FCMLStableId& TargetId,
        const FCMLMachineBuildPose& Desired,
        ECMLMachineBuildKind HeldKind,
        uint8 AimedSideYaw,
        uint8 HeldYaw,
        bool bYawExplicitlyRotated,
        const FCMLStableId& HeldDefinitionId = FCMLStableId());

    static FCMLMachineBuildPose ResolveOccupiedConnectorCell(
        const FCMLMachineSimulationState& State,
        const FCMLMachineBuildPose& SnappedPose,
        ECMLMachineBuildKind HeldKind,
        bool bInheritConnectorYaw = true,
        const FCMLStableId& HeldDefinitionId = FCMLStableId());

    static bool ArePortsAdjacent(
        ECMLMachineNodeKind LeftKind,
        const FCMLMachineBuildPose& Left,
        const FCMLStableId& LeftDefinitionId,
        ECMLMachineNodeKind RightKind,
        const FCMLMachineBuildPose& Right,
        const FCMLStableId& RightDefinitionId);

    static const FCMLMachineNodeState* FindOccupant(
        const FCMLMachineSimulationState& State,
        const FCMLMachineBuildPose& Pose);

private:
    static FCMLMachineBuildPose Offset(
        const FCMLMachineBuildPose& Source,
        uint8 DirectionYaw,
        uint8 HeldYaw,
        int32 RiseMillimetres = 0);
};

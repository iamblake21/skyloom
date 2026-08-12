#pragma once

#include "CoreMinimal.h"
#include "Simulation/CMLMachineState.h"

/** Resolves drive, direction and capacity for physically connected belt lines. */
class CMLCORE_API FCMLBeltLineRules
{
public:
    static constexpr int32 CapacityPerDrive = 12;
    static void Recompute(FCMLMachineSimulationState& State);
};

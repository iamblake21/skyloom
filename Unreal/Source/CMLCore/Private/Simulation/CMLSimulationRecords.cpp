#include "Simulation/CMLSimulationRecords.h"

bool FCMLSimulationCommand::TryCreate(
    const FCMLSimulationTick& TargetTick,
    const uint64 Sequence,
    const FString& Kind,
    FCMLSimulationCommand& OutCommand)
{
    if (Kind.TrimStartAndEnd().IsEmpty())
    {
        return false;
    }

    OutCommand = FCMLSimulationCommand();
    OutCommand.TargetTick = TargetTick;
    OutCommand.Sequence = Sequence;
    OutCommand.Kind = Kind;
    return true;
}

#include "Simulation/CMLSimulationCommandQueue.h"

int32 FCMLSimulationCommandQueue::IndexOfTick(const FCMLSimulationTick& Tick) const
{
    for (int32 Index = 0; Index < Buckets.Num(); ++Index)
    {
        if (Buckets[Index].Tick.Value == Tick.Value)
        {
            return Index;
        }
    }
    return INDEX_NONE;
}

int32 FCMLSimulationCommandQueue::FindOrAddTick(const FCMLSimulationTick& Tick)
{
    const int32 Existing = IndexOfTick(Tick);
    if (Existing != INDEX_NONE)
    {
        return Existing;
    }

    // Buckets stay ordered by tick so ToCanonicalList never has to sort them.
    int32 Insert = 0;
    while (Insert < Buckets.Num() && Buckets[Insert].Tick.Value < Tick.Value)
    {
        ++Insert;
    }
    FTickBucket Bucket;
    Bucket.Tick = Tick;
    Buckets.Insert(MoveTemp(Bucket), Insert);
    return Insert;
}

uint64 FCMLSimulationCommandQueue::GetNextSequenceFor(const FCMLSimulationTick& TargetTick) const
{
    const int32 Index = IndexOfTick(TargetTick);
    if (Index == INDEX_NONE || Buckets[Index].Commands.Num() == 0)
    {
        return 0;
    }
    return Buckets[Index].Commands.Last().Sequence + 1;
}

bool FCMLSimulationCommandQueue::TryEnqueue(const FCMLSimulationCommand& Command)
{
    return TryEnqueueAt(Command, GetNextSequenceFor(Command.TargetTick));
}

bool FCMLSimulationCommandQueue::TryEnqueueAt(
    const FCMLSimulationCommand& Command,
    const uint64 ExpectedSequence)
{
    // A command with no kind is malformed; accepting it would put an
    // unidentifiable entry into the canonical command list.
    if (Command.Kind.TrimStartAndEnd().IsEmpty())
    {
        return false;
    }
    if (Command.Sequence != ExpectedSequence)
    {
        return false;
    }

    const int32 Index = FindOrAddTick(Command.TargetTick);
    for (const FCMLSimulationCommand& Existing : Buckets[Index].Commands)
    {
        if (Existing.Sequence == Command.Sequence)
        {
            return false;
        }
    }

    Buckets[Index].Commands.Add(Command);
    ++Count;
    return true;
}

int32 FCMLSimulationCommandQueue::GetCommandCountFor(const FCMLSimulationTick& TargetTick) const
{
    const int32 Index = IndexOfTick(TargetTick);
    return Index == INDEX_NONE ? 0 : Buckets[Index].Commands.Num();
}

void FCMLSimulationCommandQueue::GetCommandsFor(
    const FCMLSimulationTick& Tick,
    TArray<FCMLSimulationCommand>& OutCommands) const
{
    OutCommands.Reset();
    const int32 Index = IndexOfTick(Tick);
    if (Index != INDEX_NONE)
    {
        OutCommands = Buckets[Index].Commands;
    }
}

void FCMLSimulationCommandQueue::RemoveCommandsFor(const FCMLSimulationTick& Tick)
{
    const int32 Index = IndexOfTick(Tick);
    if (Index == INDEX_NONE)
    {
        return;
    }
    Count -= Buckets[Index].Commands.Num();
    Buckets.RemoveAt(Index);
}

void FCMLSimulationCommandQueue::ToCanonicalList(TArray<FCMLSimulationCommand>& OutCommands) const
{
    OutCommands.Reset(Count);
    for (const FTickBucket& Bucket : Buckets)
    {
        OutCommands.Append(Bucket.Commands);
    }
}

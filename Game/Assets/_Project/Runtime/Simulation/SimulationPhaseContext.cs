using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;

namespace CML.Simulation
{
    /// <summary>
    /// The only mutation surface available to a phase system. It points to a
    /// phase-local working copy, never to the currently authoritative state.
    /// </summary>
    public sealed class SimulationPhaseContext
    {
        private readonly SimulationState _workingState;
        private readonly List<CreationKey> _stagedCreations = new List<CreationKey>();

        internal SimulationPhaseContext(
            SimulationState workingState,
            SimulationTick executingTick,
            SimulationPhase phase,
            IReadOnlyList<SimulationCommand> commands,
            GameCatalog catalog)
        {
            _workingState = workingState ?? throw new ArgumentNullException(nameof(workingState));
            ExecutingTick = executingTick;
            Phase = phase;
            Commands = commands ?? Array.Empty<SimulationCommand>();
            Catalog = catalog;
        }

        public SimulationTick ExecutingTick { get; }

        /// <summary>
        /// The validated content the engine was built with, or null when it was built
        /// without any. Content is immutable and its revision is part of the logical
        /// hash, so reading it inside a phase stays deterministic; it is exposed here
        /// rather than held by a system because a system may own no reference state.
        /// </summary>
        public GameCatalog Catalog { get; }

        public SimulationPhase Phase { get; }

        public IReadOnlyList<SimulationCommand> Commands { get; }

        public NonNegativeQuantity GetQuantity(StableId ownerId)
        {
            return _workingState.GetQuantity(ownerId);
        }

        public void SetQuantity(StableId ownerId, NonNegativeQuantity quantity)
        {
            _workingState.SetQuantity(ownerId, quantity);
        }

        public void AddQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            _workingState.AddQuantity(ownerId, amount);
        }

        public void RemoveQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            _workingState.RemoveQuantity(ownerId, amount);
        }

        public bool TryAddQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            return _workingState.TryAddQuantity(ownerId, amount);
        }

        public bool TryRemoveQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            return _workingState.TryRemoveQuantity(ownerId, amount);
        }

        public bool TryGetAccumulator(AccumulatorKey key, out RemainderAccumulator accumulator)
        {
            return _workingState.TryGetAccumulator(key, out accumulator);
        }

        public void SetAccumulator(AccumulatorKey key, RemainderAccumulator accumulator)
        {
            _workingState.SetAccumulator(key, accumulator);
        }

        public RemainderAdvance AdvanceAccumulator(AccumulatorKey key, ulong numerator)
        {
            if (!_workingState.TryGetAccumulator(key, out var accumulator))
            {
                throw new SimulationInvariantException($"Accumulator {key} does not exist.");
            }

            var advance = accumulator.Advance(numerator);
            _workingState.SetAccumulator(key, advance.Accumulator);
            return advance;
        }

        public void StageCreation(CreationKey key)
        {
            if (key.Phase != Phase)
            {
                throw new SimulationInvariantException(
                    $"Creation phase {key.Phase} does not match active phase {Phase}.");
            }

            _stagedCreations.Add(key);
        }

        public void AbortTick(string cause)
        {
            throw new SimulationInvariantException(cause);
        }

        internal IReadOnlyList<CreationKey> GetStagedCreations()
        {
            return _stagedCreations;
        }

        internal AirshipSimulationState GetAirshipMutable()
        {
            return _workingState.GetAirshipMutable();
        }

        internal MachineSimulationState GetMachineMutable()
        {
            return _workingState.GetMachineMutable();
        }

        internal InventorySimulationState GetInventoriesMutable()
        {
            return _workingState.GetInventoriesMutable();
        }

        /// <summary>
        /// The commands due this tick, for the phases that own a command kind of their
        /// own. <see cref="Commands"/> is filled by the engine only for phase 1; the
        /// queue itself still holds them, because the tick clears it after phase 12.
        /// </summary>
        internal IReadOnlyList<SimulationCommand> GetCommandsForExecutingTick()
        {
            return _workingState.GetCommandsFor(ExecutingTick);
        }

        internal bool TryGetCreatedEntityId(
            CreationKey key,
            out StableId entityId)
        {
            return _workingState.TryGetCreatedEntityId(
                ExecutingTick,
                key,
                out entityId);
        }

        internal void RecordCommandRejection(
            SimulationCommand command,
            CommandRejectionReason reason)
        {
            _workingState.RecordCommandRejection(
                new CommandRejection(ExecutingTick, command, reason));
        }
    }
}

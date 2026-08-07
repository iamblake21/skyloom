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
    /// Engine-independent authoritative state. External callers receive read-only
    /// projections; a tick mutates one deep working copy and publishes it after phase 12.
    /// </summary>
    [Serializable]
    public sealed class SimulationState
    {
        // Revision 4 adds the MCH subtree: the machine graph, its ports and its
        // logical transport links. Revision 5 adds INV, the authoritative inventories,
        // which until then existed only where they were drawn. A new future-affecting
        // field requires a logical schema revision, and a new authoritative subsystem
        // is also a rules change, so a replay recorded before either existed is
        // correctly refused.
        // Revision 7: LOG-001 replaced the instantaneous logical link with belt lanes
        // that have length, so transport is no longer free. Revision 8: LOG-002 made the
        // funnel the extractor, so a belt no longer reaches into a crate by itself. A
        // replay recorded under either older rule must be refused, and that gate reads
        // the root. Revision 10 makes the press a true one-workpiece machine and gives
        // funnels physical attachments; lane direction determines extract/insert and
        // direct belt-to-inventory transfers no longer exist. Revision 11 replaces
        // endpoint-authored gameplay transport with placed one-cell belt and funnel
        // modules. Their quantized poses derive topology; gaps and facing now decide
        // whether material can move.
        public const uint CurrentLogicalSchemaRevision = 12U;
        public const uint CurrentRulesRevision = 12U;
        public const uint CurrentGeneratorRevision = 1U;

        private readonly SortedDictionary<StableId, NonNegativeQuantity> _quantities;
        private readonly SortedDictionary<AccumulatorKey, RemainderAccumulator> _accumulators;
        private readonly List<CreationRecord> _creationRecords;
        private readonly List<CommandRejection> _commandRejections;
        private readonly AirshipSimulationState _airship;
        private readonly MachineSimulationState _machines;
        private readonly InventorySimulationState _inventories;
        private StableIdAllocator _idAllocator;
        private SimulationCommandQueue _commandQueue;
        private SimulationCommandQueue _commandInbox;

        public SimulationState(SimulationTick tick, CatalogRevision contentRevision)
            : this(tick, contentRevision, StableId.First, false)
        {
        }

        public SimulationState(
            SimulationTick tick,
            CatalogRevision contentRevision,
            AirshipSimulationState initialAirshipState)
            : this(tick, contentRevision, initialAirshipState, new MachineSimulationState())
        {
        }

        public SimulationState(
            SimulationTick tick,
            CatalogRevision contentRevision,
            AirshipSimulationState initialAirshipState,
            MachineSimulationState initialMachineState)
            : this(
                tick,
                contentRevision,
                initialAirshipState,
                initialMachineState,
                new InventorySimulationState())
        {
        }

        /// <summary>
        /// Initial boundary for a world that already contains a machine graph and
        /// declared inventories. The allocator is derived from every persistent id in
        /// all three subtrees, so an id chosen above the airship ids cannot collide
        /// with the next allocation.
        /// </summary>
        public SimulationState(
            SimulationTick tick,
            CatalogRevision contentRevision,
            AirshipSimulationState initialAirshipState,
            MachineSimulationState initialMachineState,
            InventorySimulationState initialInventoryState)
            : this(
                tick,
                contentRevision,
                CurrentLogicalSchemaRevision,
                CurrentRulesRevision,
                CurrentGeneratorRevision,
                CreateAllocatorForInitialState(
                    initialAirshipState,
                    initialMachineState,
                    initialInventoryState),
                new SortedDictionary<StableId, NonNegativeQuantity>(),
                new SortedDictionary<AccumulatorKey, RemainderAccumulator>(),
                new SimulationCommandQueue(),
                new SimulationCommandQueue(),
                new List<CreationRecord>(),
                new List<CommandRejection>(),
                initialAirshipState?.DeepClone()
                    ?? throw new ArgumentNullException(nameof(initialAirshipState)),
                initialMachineState?.DeepClone()
                    ?? throw new ArgumentNullException(nameof(initialMachineState)),
                initialInventoryState?.DeepClone()
                    ?? throw new ArgumentNullException(nameof(initialInventoryState)))
        {
        }

        /// <summary>
        /// Restore constructor used by persistence and allocator exhaustion tests.
        /// </summary>
        public SimulationState(
            SimulationTick tick,
            CatalogRevision contentRevision,
            StableId nextEntityId,
            bool isEntityIdSpaceExhausted)
            : this(
                tick,
                contentRevision,
                CurrentLogicalSchemaRevision,
                CurrentRulesRevision,
                CurrentGeneratorRevision,
                new StableIdAllocator(nextEntityId, isEntityIdSpaceExhausted),
                new SortedDictionary<StableId, NonNegativeQuantity>(),
                new SortedDictionary<AccumulatorKey, RemainderAccumulator>(),
                new SimulationCommandQueue(),
                new SimulationCommandQueue(),
                new List<CreationRecord>(),
                new List<CommandRejection>(),
                new AirshipSimulationState(),
                new MachineSimulationState(),
                new InventorySimulationState())
        {
        }

        /// <summary>
        /// Restore boundary for a checkpoint that already contains AIR entities.
        /// The saved allocator must remain strictly beyond every live persistent ID.
        /// </summary>
        public SimulationState(
            SimulationTick tick,
            CatalogRevision contentRevision,
            StableId nextEntityId,
            bool isEntityIdSpaceExhausted,
            AirshipSimulationState restoredAirshipState)
            : this(
                tick,
                contentRevision,
                CurrentLogicalSchemaRevision,
                CurrentRulesRevision,
                CurrentGeneratorRevision,
                new StableIdAllocator(nextEntityId, isEntityIdSpaceExhausted),
                new SortedDictionary<StableId, NonNegativeQuantity>(),
                new SortedDictionary<AccumulatorKey, RemainderAccumulator>(),
                new SimulationCommandQueue(),
                new SimulationCommandQueue(),
                new List<CreationRecord>(),
                new List<CommandRejection>(),
                restoredAirshipState?.DeepClone()
                    ?? throw new ArgumentNullException(nameof(restoredAirshipState)),
                new MachineSimulationState(),
                new InventorySimulationState())
        {
            ValidateInvariants();
        }

        private SimulationState(
            SimulationTick tick,
            CatalogRevision contentRevision,
            uint logicalSchemaRevision,
            uint rulesRevision,
            uint generatorRevision,
            StableIdAllocator idAllocator,
            SortedDictionary<StableId, NonNegativeQuantity> quantities,
            SortedDictionary<AccumulatorKey, RemainderAccumulator> accumulators,
            SimulationCommandQueue commandQueue,
            SimulationCommandQueue commandInbox,
            List<CreationRecord> creationRecords,
            List<CommandRejection> commandRejections,
            AirshipSimulationState airship,
            MachineSimulationState machines,
            InventorySimulationState inventories)
        {
            if (string.IsNullOrWhiteSpace(contentRevision.Value))
            {
                throw new ArgumentException(
                    "An authoritative state requires a non-empty catalog revision.",
                    nameof(contentRevision));
            }

            if (logicalSchemaRevision == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalSchemaRevision));
            }

            if (rulesRevision == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(rulesRevision));
            }

            if (generatorRevision == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(generatorRevision));
            }

            Tick = tick;
            ContentRevision = contentRevision;
            LogicalSchemaRevision = logicalSchemaRevision;
            RulesRevision = rulesRevision;
            GeneratorRevision = generatorRevision;
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _quantities = quantities ?? throw new ArgumentNullException(nameof(quantities));
            _accumulators = accumulators ?? throw new ArgumentNullException(nameof(accumulators));
            _commandQueue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
            _commandInbox = commandInbox ?? throw new ArgumentNullException(nameof(commandInbox));
            _creationRecords = creationRecords ?? throw new ArgumentNullException(nameof(creationRecords));
            _commandRejections = commandRejections ?? throw new ArgumentNullException(nameof(commandRejections));
            _airship = airship ?? throw new ArgumentNullException(nameof(airship));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
        }

        /// <summary>The latest completely committed checkpoint tick.</summary>
        public SimulationTick Tick { get; private set; }

        public CatalogRevision ContentRevision { get; }

        public uint LogicalSchemaRevision { get; }

        public uint RulesRevision { get; }

        public uint GeneratorRevision { get; }

        public StableId NextEntityId => _idAllocator.NextId;

        public bool IsEntityIdSpaceExhausted => _idAllocator.IsExhausted;

        public int ScheduledCommandCount => _commandQueue.Count;

        public int InboxCommandCount => _commandInbox.Count;

        public int PendingCommandCount => checked(_commandQueue.Count + _commandInbox.Count);

        /// <summary>
        /// Read-only projection by ownership: every call returns a detached deep
        /// snapshot, never the authoritative mutable subtree.
        /// </summary>
        public AirshipSimulationState GetAirshipSnapshot()
        {
            return _airship.DeepClone();
        }

        /// <summary>
        /// Read-only projection of the machine graph, detached by the same ownership
        /// rule as the airship subtree.
        /// </summary>
        public MachineSimulationState GetMachineSnapshot()
        {
            return _machines.DeepClone();
        }

        /// <summary>
        /// Read-only projection of the authoritative inventories. The entries are
        /// immutable, so the detachment is the dictionary alone.
        /// </summary>
        public InventorySimulationState GetInventorySnapshot()
        {
            return _inventories.DeepClone();
        }

        public NonNegativeQuantity GetQuantity(StableId ownerId)
        {
            return _quantities.TryGetValue(ownerId, out var quantity)
                ? quantity
                : NonNegativeQuantity.Zero;
        }

        public bool TryGetAccumulator(
            AccumulatorKey key,
            out RemainderAccumulator accumulator)
        {
            return _accumulators.TryGetValue(key, out accumulator);
        }

        public IReadOnlyList<KeyValuePair<StableId, NonNegativeQuantity>> GetQuantitiesCanonical()
        {
            return CopyEntries(_quantities);
        }

        public IReadOnlyList<KeyValuePair<AccumulatorKey, RemainderAccumulator>>
            GetAccumulatorsCanonical()
        {
            return CopyEntries(_accumulators);
        }

        public IReadOnlyList<SimulationCommand> GetPendingCommandsCanonical()
        {
            return _commandQueue.ToCanonicalList();
        }

        public IReadOnlyList<SimulationCommand> GetInboxCommandsCanonical()
        {
            return _commandInbox.ToCanonicalList();
        }

        /// <summary>
        /// Canonical command projection. Inbox versus scheduled queue is a latch
        /// implementation detail and cannot affect hash/replay equivalence.
        /// </summary>
        public IReadOnlyList<SimulationCommand> GetAcceptedCommandsCanonical()
        {
            var accepted = new List<SimulationCommand>(PendingCommandCount);
            accepted.AddRange(_commandQueue.ToCanonicalList());
            accepted.AddRange(_commandInbox.ToCanonicalList());
            accepted.Sort(SimulationCommandComparer.Instance);
            return accepted;
        }

        public IReadOnlyList<CreationRecord> GetCreationRecordsCanonical()
        {
            var copy = _creationRecords.ToArray();
            Array.Sort(copy);
            return copy;
        }

        public IReadOnlyList<CommandRejection> GetCommandRejectionsCanonical()
        {
            var copy = _commandRejections.ToArray();
            Array.Sort(copy);
            return copy;
        }

        public bool TryGetCreatedEntityId(
            SimulationTick tick,
            CreationKey key,
            out StableId entityId)
        {
            for (var index = 0; index < _creationRecords.Count; index++)
            {
                var record = _creationRecords[index];
                if (record.Tick == tick && record.Key == key)
                {
                    entityId = record.EntityId;
                    return true;
                }
            }

            entityId = StableId.None;
            return false;
        }

        public SimulationState DeepClone()
        {
            return new SimulationState(
                Tick,
                ContentRevision,
                LogicalSchemaRevision,
                RulesRevision,
                GeneratorRevision,
                _idAllocator.Clone(),
                new SortedDictionary<StableId, NonNegativeQuantity>(_quantities),
                new SortedDictionary<AccumulatorKey, RemainderAccumulator>(_accumulators),
                _commandQueue.Clone(),
                _commandInbox.Clone(),
                new List<CreationRecord>(_creationRecords),
                new List<CommandRejection>(_commandRejections),
                _airship.DeepClone(),
                _machines.DeepClone(),
                _inventories.DeepClone());
        }

        internal AirshipSimulationState GetAirshipMutable()
        {
            return _airship;
        }

        internal MachineSimulationState GetMachineMutable()
        {
            return _machines;
        }

        internal InventorySimulationState GetInventoriesMutable()
        {
            return _inventories;
        }

        internal ulong GetNextCommandSequence(SimulationTick targetTick)
        {
            var scheduled = (ulong)_commandQueue.GetCommandCountFor(targetTick);
            var inbox = (ulong)_commandInbox.GetCommandCountFor(targetTick);
            return checked(scheduled + inbox);
        }

        internal void AcceptCommandToInbox(SimulationCommand command)
        {
            SimulationCommandIngress.Validate(command);
            if (command.TargetTick <= Tick)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    $"Command target tick {command.TargetTick} must be newer than committed tick {Tick}.");
            }

            var expectedSequence = GetNextCommandSequence(command.TargetTick);
            if (command.Sequence != expectedSequence)
            {
                throw new ArgumentException(
                    $"Command sequence gap for tick {command.TargetTick}. "
                    + $"Expected {expectedSequence}, got {command.Sequence}.",
                    nameof(command));
            }

            _commandInbox.Enqueue(command, expectedSequence);
        }

        internal void LatchInbox()
        {
            var commands = _commandInbox.ToCanonicalList();
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                _commandQueue.Enqueue(
                    command,
                    (ulong)_commandQueue.GetCommandCountFor(command.TargetTick));
            }

            _commandInbox = new SimulationCommandQueue();
        }

        internal IReadOnlyList<SimulationCommand> GetCommandsFor(SimulationTick tick)
        {
            return _commandQueue.GetCommandsFor(tick);
        }

        internal void RemoveCommandsFor(SimulationTick tick)
        {
            _commandQueue.RemoveCommandsFor(tick);
        }

        internal void SetCommittedTick(SimulationTick tick)
        {
            if (tick <= Tick)
            {
                throw new SimulationInvariantException(
                    $"A commit tick must advance monotonically. Current={Tick}, requested={tick}.");
            }

            Tick = tick;
        }

        internal void PublishCreationBatch(
            SimulationTick executingTick,
            IReadOnlyList<CreationKey> stagedKeys)
        {
            if (stagedKeys == null || stagedKeys.Count == 0)
            {
                return;
            }

            var ordered = new CreationKey[stagedKeys.Count];
            for (var index = 0; index < stagedKeys.Count; index++)
            {
                ordered[index] = stagedKeys[index];
            }

            Array.Sort(ordered);
            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index].Phase == 0)
                {
                    throw new SimulationInvariantException("A staged creation has an invalid phase.");
                }

                if (index > 0 && ordered[index] == ordered[index - 1])
                {
                    throw new SimulationInvariantException(
                        $"Duplicate creation key in tick {executingTick}: {ordered[index]}.");
                }

                if (TryGetCreatedEntityId(executingTick, ordered[index], out _))
                {
                    throw new SimulationInvariantException(
                        $"Creation key already published in tick {executingTick}.");
                }
            }

            var stagedAllocator = _idAllocator.Clone();
            var stagedRecords = new CreationRecord[ordered.Length];
            for (var index = 0; index < ordered.Length; index++)
            {
                if (!stagedAllocator.TryAllocate(out var id))
                {
                    throw new SimulationInvariantException(
                        "RESOURCE_EXHAUSTED: creation batch cannot reserve all persistent IDs.");
                }

                stagedRecords[index] = new CreationRecord(executingTick, ordered[index], id);
            }

            _idAllocator = stagedAllocator;
            _creationRecords.AddRange(stagedRecords);
            _creationRecords.Sort();
        }

        internal void SetQuantity(StableId ownerId, NonNegativeQuantity quantity)
        {
            RequirePersistentOwner(ownerId);
            if (quantity.IsZero)
            {
                _quantities.Remove(ownerId);
                return;
            }

            _quantities[ownerId] = quantity;
        }

        internal void AddQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            SetQuantity(ownerId, GetQuantity(ownerId).Add(amount));
        }

        internal void RemoveQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            SetQuantity(ownerId, GetQuantity(ownerId).Subtract(amount));
        }

        internal bool TryAddQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            if (!GetQuantity(ownerId).TryAdd(amount, out var result))
            {
                return false;
            }

            SetQuantity(ownerId, result);
            return true;
        }

        internal bool TryRemoveQuantity(StableId ownerId, NonNegativeQuantity amount)
        {
            if (!GetQuantity(ownerId).TrySubtract(amount, out var result))
            {
                return false;
            }

            SetQuantity(ownerId, result);
            return true;
        }

        internal void RecordCommandRejection(CommandRejection rejection)
        {
            _commandRejections.Add(rejection);
            _commandRejections.Sort();
        }

        internal void SetAccumulator(AccumulatorKey key, RemainderAccumulator accumulator)
        {
            RequireAccumulatorKey(key);
            _accumulators[key] = accumulator;
        }

        internal void ValidateInvariants()
        {
            if (!_idAllocator.IsExhausted && _idAllocator.NextId.IsNone)
            {
                throw new SimulationInvariantException("The next entity ID cannot be zero.");
            }

            foreach (var entry in _quantities)
            {
                RequirePersistentOwner(entry.Key);
                if (entry.Value.IsZero)
                {
                    throw new SimulationInvariantException(
                        $"Canonical quantity map contains a zero entry for owner {entry.Key}.");
                }
            }

            foreach (var entry in _accumulators)
            {
                RequireAccumulatorKey(entry.Key);
                if (entry.Value.OwnerDenominator.IsZero
                    || entry.Value.Remainder >= entry.Value.OwnerDenominator)
                {
                    throw new SimulationInvariantException(
                        $"Accumulator {entry.Key} violates its Euclidean remainder invariant.");
                }
            }

            ValidateCommands(_commandQueue.ToCanonicalList(), "scheduled");
            ValidateCommands(_commandInbox.ToCanonicalList(), "inbox");
            ValidateCombinedCommandSequences();
            _airship.ValidateInvariants();

            // Structural only: the authoritative state does not hold the catalog, so
            // item, recipe and definition references are checked where content is in
            // hand — when the graph is built and on every reducer operation.
            _machines.ValidateInvariants(null);
            _inventories.ValidateInvariants(null);

            var persistentIds = new HashSet<StableId>();
            var airshipIds = _airship.GetPersistentIdsCanonical();
            for (var index = 0; index < airshipIds.Count; index++)
            {
                RegisterPersistentId(persistentIds, airshipIds[index], "AIR state");
            }

            var machineIds = _machines.GetPersistentIdsCanonical();
            for (var index = 0; index < machineIds.Count; index++)
            {
                RegisterPersistentId(persistentIds, machineIds[index], "MCH state");
            }

            var inventoryIds = _inventories.GetPersistentIdsCanonical();
            for (var index = 0; index < inventoryIds.Count; index++)
            {
                RegisterPersistentId(persistentIds, inventoryIds[index], "INV state");
            }

            var records = GetCreationRecordsCanonical();
            var creationIds = new HashSet<StableId>();
            for (var index = 0; index < records.Count; index++)
            {
                RegisterPersistentId(
                    creationIds,
                    records[index].EntityId,
                    "creation record");
                // A creation record identifies the live entity; it is not a second
                // entity that competes for the same ID. Keep the union for allocator
                // lineage while still rejecting duplicate IDs among records above.
                persistentIds.Add(records[index].EntityId);
                if (index > 0
                    && records[index - 1].Tick == records[index].Tick
                    && records[index - 1].Key == records[index].Key)
                {
                    throw new SimulationInvariantException("Duplicate canonical creation record.");
                }
            }

            ValidateAllocatorLineage(persistentIds);
        }

        private void ValidateCommands(IReadOnlyList<SimulationCommand> commands, string location)
        {
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                SimulationCommandIngress.Validate(command);
                if (command.TargetTick <= Tick)
                {
                    throw new SimulationInvariantException(
                        $"{location} command {command.Sequence} targets committed tick {command.TargetTick}.");
                }
            }
        }

        private void ValidateCombinedCommandSequences()
        {
            var all = new List<SimulationCommand>(PendingCommandCount);
            all.AddRange(_commandQueue.ToCanonicalList());
            all.AddRange(_commandInbox.ToCanonicalList());
            all.Sort(SimulationCommandComparer.Instance);

            var hasTick = false;
            var currentTick = default(SimulationTick);
            var expected = 0UL;
            for (var index = 0; index < all.Count; index++)
            {
                var command = all[index];
                if (!hasTick || command.TargetTick != currentTick)
                {
                    hasTick = true;
                    currentTick = command.TargetTick;
                    expected = 0UL;
                }

                if (command.Sequence != expected)
                {
                    throw new SimulationInvariantException(
                        $"Command sequence gap for target tick {currentTick}. "
                        + $"Expected {expected}, got {command.Sequence}.");
                }

                expected = checked(expected + 1UL);
            }
        }

        private static IReadOnlyList<KeyValuePair<TKey, TValue>> CopyEntries<TKey, TValue>(
            SortedDictionary<TKey, TValue> source)
        {
            var result = new KeyValuePair<TKey, TValue>[source.Count];
            var index = 0;
            foreach (var entry in source)
            {
                result[index++] = entry;
            }

            return result;
        }

        private static void RequirePersistentOwner(StableId ownerId)
        {
            if (ownerId.IsNone)
            {
                throw new SimulationInvariantException("A persistent state owner cannot use the reserved zero ID.");
            }
        }

        private static void RequireAccumulatorKey(AccumulatorKey key)
        {
            if (string.IsNullOrWhiteSpace(key.SystemKind)
                || string.IsNullOrWhiteSpace(key.ResourceKind)
                || key.EntityId.IsNone)
            {
                throw new SimulationInvariantException("Accumulator key is not canonical.");
            }
        }

        private static StableIdAllocator CreateAllocatorForInitialState(
            AirshipSimulationState initialAirshipState,
            MachineSimulationState initialMachineState,
            InventorySimulationState initialInventoryState)
        {
            if (initialAirshipState == null)
            {
                throw new ArgumentNullException(nameof(initialAirshipState));
            }

            if (initialMachineState == null)
            {
                throw new ArgumentNullException(nameof(initialMachineState));
            }

            if (initialInventoryState == null)
            {
                throw new ArgumentNullException(nameof(initialInventoryState));
            }

            initialAirshipState.ValidateInvariants();
            initialMachineState.ValidateInvariants(null);
            initialInventoryState.ValidateInvariants(null);

            var maximum = StableId.None;
            AbsorbMaximum(initialAirshipState.GetPersistentIdsCanonical(), ref maximum);
            AbsorbMaximum(initialMachineState.GetPersistentIdsCanonical(), ref maximum);
            AbsorbMaximum(initialInventoryState.GetPersistentIdsCanonical(), ref maximum);
            if (maximum.IsNone)
            {
                return new StableIdAllocator(StableId.First, false);
            }

            if (maximum == StableId.MaxValue)
            {
                return new StableIdAllocator(StableId.MaxValue, true);
            }

            var next = maximum.Low == ulong.MaxValue
                ? new StableId(checked(maximum.High + 1UL), 0UL)
                : new StableId(maximum.High, maximum.Low + 1UL);
            return new StableIdAllocator(next, false);
        }

        private static void AbsorbMaximum(IReadOnlyList<StableId> ids, ref StableId maximum)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                if (ids[index] > maximum)
                {
                    maximum = ids[index];
                }
            }
        }

        private static void RegisterPersistentId(
            ISet<StableId> persistentIds,
            StableId id,
            string source)
        {
            if (id.IsNone)
            {
                throw new SimulationInvariantException(
                    $"{source} cannot use the reserved zero persistent ID.");
            }

            if (!persistentIds.Add(id))
            {
                throw new SimulationInvariantException(
                    $"Persistent ID {id} is assigned more than once in the authoritative state.");
            }
        }

        private void ValidateAllocatorLineage(ISet<StableId> persistentIds)
        {
            if (_idAllocator.IsExhausted || persistentIds.Count == 0)
            {
                return;
            }

            var maximum = StableId.None;
            foreach (var id in persistentIds)
            {
                if (id > maximum)
                {
                    maximum = id;
                }
            }

            if (_idAllocator.NextId <= maximum)
            {
                throw new SimulationInvariantException(
                    $"Allocator next_id {_idAllocator.NextId} must be strictly greater "
                    + $"than maximum persistent ID {maximum}.");
            }
        }
    }
}

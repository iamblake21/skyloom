using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;

namespace CML.Simulation
{
    /// <summary>
    /// Executes the normative twelve-phase pipeline on one working clone. The clone
    /// is discarded on any failure and becomes authoritative only after phase 12.
    /// Command ingress uses a separate boundary inbox so concurrent/reentrant input
    /// is never written into the clone currently being committed.
    /// </summary>
    public sealed class SimulationEngine
    {
        private static readonly SimulationPhase[] CanonicalPhases =
        {
            SimulationPhase.CommandsAndConfiguration,
            SimulationPhase.MovementAndPortalDetection,
            SimulationPhase.LocalTopologyChanges,
            SimulationPhase.WirelessNetworkState,
            SimulationPhase.PowerSupplyAndAllocation,
            SimulationPhase.ItemFluidFlowAndReservations,
            SimulationPhase.CyclesNeedsAndTimers,
            SimulationPhase.CompletionDamageAndEventStaging,
            SimulationPhase.ValidatedTransferCommit,
            SimulationPhase.SchedulingAndPoweredJobStart,
            SimulationPhase.CriticalTransactionPublication,
            SimulationPhase.ObjectivesDiagnosticsAndNotifications
        };

        private readonly object _ingressGate = new object();
        private readonly IReadOnlyList<RegisteredSystem> _systems;
        private readonly List<SimulationPhase> _lastPhaseTrace = new List<SimulationPhase>(12);
        private readonly List<SimulationCommand> _deferredIngress = new List<SimulationCommand>();
        private readonly GameCatalog _catalog;
        private SimulationState _state;
        private bool _isAdvancing;
        private SimulationTick _executingTick;
        private ulong _tickWorkingCloneCount;

        /// <summary>
        /// The catalog is optional so that the many states with no machine graph can be
        /// driven without content. A state that does contain machines and is advanced
        /// without a catalog aborts its tick with that reason, rather than standing
        /// still and looking healthy.
        /// </summary>
        public SimulationEngine(
            SimulationState initialState,
            IEnumerable<ISimulationPhaseSystem> systems = null,
            GameCatalog catalog = null)
        {
            _state = initialState?.DeepClone() ?? throw new ArgumentNullException(nameof(initialState));
            _state.ValidateInvariants();
            _catalog = catalog;
            _systems = OrderAndValidateSystems(systems);
        }

        public SimulationState State => _state;

        public IReadOnlyList<SimulationPhase> LastPhaseTrace => _lastPhaseTrace.ToArray();

        /// <summary>Non-authoritative diagnostic proving one working clone per attempted tick.</summary>
        public ulong TickWorkingCloneCount => _tickWorkingCloneCount;

        public bool IsAdvancing
        {
            get
            {
                lock (_ingressGate)
                {
                    return _isAdvancing;
                }
            }
        }

        /// <summary>
        /// Non-authoritative boundary notification for adapters and deterministic
        /// ingress tests. Subscribers may submit commands but cannot mutate state.
        /// </summary>
        public event Action<SimulationPhase> PhaseBoundaryReached;

        public SimulationCommandAcceptance EnqueueCommand(SimulationCommand command)
        {
            lock (_ingressGate)
            {
                SimulationCommandIngress.Validate(command);
                var acceptedAfterTick = _state.Tick;
                var effectiveCommand = command;
                var wasRetargeted = false;
                if (_isAdvancing && command.TargetTick <= _executingTick)
                {
                    effectiveCommand = command.WithTargetTick(_executingTick.Next());
                    wasRetargeted = true;
                }

                if (effectiveCommand.TargetTick <= _state.Tick)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(command),
                        $"Command target tick {effectiveCommand.TargetTick} is already committed.");
                }

                var expectedSequence = _state.GetNextCommandSequence(effectiveCommand.TargetTick);
                for (var index = 0; index < _deferredIngress.Count; index++)
                {
                    if (_deferredIngress[index].TargetTick == effectiveCommand.TargetTick)
                    {
                        expectedSequence = checked(expectedSequence + 1UL);
                    }
                }

                if (wasRetargeted)
                {
                    effectiveCommand = effectiveCommand.WithSchedule(
                        effectiveCommand.TargetTick,
                        expectedSequence);
                }
                else if (effectiveCommand.Sequence != expectedSequence)
                {
                    throw new ArgumentException(
                        $"Command sequence gap for target tick {effectiveCommand.TargetTick}. "
                        + $"Expected {expectedSequence}, got {effectiveCommand.Sequence}.",
                        nameof(command));
                }

                if (_isAdvancing)
                {
                    _deferredIngress.Add(effectiveCommand);
                }
                else
                {
                    _state.AcceptCommandToInbox(effectiveCommand);
                }

                return new SimulationCommandAcceptance(acceptedAfterTick, effectiveCommand);
            }
        }

        /// <summary>
        /// Publishes an immutable inventory successor at an adapter boundary.
        ///
        /// Manual gathering currently produces its successor before it has a native
        /// simulation command. Keeping this transition on the engine prevents the HUD
        /// and the factory from becoming two authorities. The method is deliberately
        /// rejected during a tick and cannot create a new inventory id.
        /// </summary>
        public bool TryPublishInventorySuccessor(InventoryState successor)
        {
            if (successor == null)
            {
                throw new ArgumentNullException(nameof(successor));
            }

            lock (_ingressGate)
            {
                if (_isAdvancing)
                {
                    return false;
                }

                var working = _state.DeepClone();
                var inventories = working.GetInventoriesMutable();
                if (!inventories.TryGet(successor.InventoryId, out var current))
                {
                    return false;
                }

                if (ReferenceEquals(current, successor))
                {
                    return true;
                }

                inventories.Replace(successor);
                inventories.ValidateInvariants(_catalog);
                working.ValidateInvariants();
                _state = working;
                return true;
            }
        }

        /// <summary>
        /// Resolves a collision reported by the Unity world immediately after a
        /// committed AIR movement tick and before that pose is projected. The global
        /// checkpoint is cloned and republished so presentation can never become a
        /// second authority for the vehicle pose.
        /// </summary>
        public bool TryResolveAirshipWorldCollision(
            StableId airshipId,
            AirshipPoseState lastSafePose,
            int lastSafePitchTurnUnits)
        {
            if (airshipId.IsNone)
            {
                throw new ArgumentException(
                    "A world-collision resolution requires an airship id.",
                    nameof(airshipId));
            }

            lock (_ingressGate)
            {
                if (_isAdvancing)
                {
                    return false;
                }

                var working = _state.DeepClone();
                if (!AirshipReducer.ResolveWorldCollision(
                        working.GetAirshipMutable(),
                        airshipId,
                        lastSafePose,
                        lastSafePitchTurnUnits))
                {
                    return false;
                }

                working.ValidateInvariants();
                _state = working;
                return true;
            }
        }

        public SimulationTickResult AdvanceOneTick()
        {
            SimulationState tickWorkingState;
            SimulationTick executingTick;
            lock (_ingressGate)
            {
                if (_isAdvancing)
                {
                    throw new InvalidOperationException("The simulation engine cannot advance reentrantly.");
                }

                executingTick = _state.Tick.Next();
                tickWorkingState = _state.DeepClone();
                var nextCloneCount = checked(_tickWorkingCloneCount + 1UL);

                _executingTick = executingTick;
                _tickWorkingCloneCount = nextCloneCount;
                _lastPhaseTrace.Clear();
                _isAdvancing = true;
            }

            var result = SimulationTickResult.Abort(
                executingTick,
                SimulationPhase.CommandsAndConfiguration,
                "Tick did not reach its commit boundary.");

            try
            {
                foreach (var phase in CanonicalPhases)
                {
                    var context = new SimulationPhaseContext(
                        tickWorkingState,
                        executingTick,
                        phase,
                        Array.Empty<SimulationCommand>(),
                        _catalog);

                    try
                    {
                        if (phase == SimulationPhase.CommandsAndConfiguration)
                        {
                            tickWorkingState.LatchInbox();
                            var dueCommands = tickWorkingState.GetCommandsFor(executingTick);
                            context = new SimulationPhaseContext(
                                tickWorkingState,
                                executingTick,
                                phase,
                                dueCommands,
                                _catalog);
                            ApplyCoreCommands(context);
                        }

                        ExecuteSystemsForPhase(phase, context);
                        tickWorkingState.PublishCreationBatch(
                            executingTick,
                            context.GetStagedCreations());
                        tickWorkingState.ValidateInvariants();
                        _lastPhaseTrace.Add(phase);
                        NotifyPhaseBoundary(phase);
                    }
                    catch (Exception exception)
                    {
                        result = SimulationTickResult.Abort(
                            executingTick,
                            phase,
                            $"{exception.GetType().Name}: {exception.Message}");
                        return FinishAdvance(result, null);
                    }
                }

                try
                {
                    tickWorkingState.RemoveCommandsFor(executingTick);
                    tickWorkingState.SetCommittedTick(executingTick);
                    tickWorkingState.ValidateInvariants();
                }
                catch (Exception exception)
                {
                    result = SimulationTickResult.Abort(
                        executingTick,
                        SimulationPhase.ObjectivesDiagnosticsAndNotifications,
                        $"{exception.GetType().Name}: {exception.Message}");
                    return FinishAdvance(result, null);
                }

                result = SimulationTickResult.Success(executingTick);
                return FinishAdvance(result, tickWorkingState);
            }
            catch (Exception exception)
            {
                result = SimulationTickResult.Abort(
                    executingTick,
                    SimulationPhase.CommandsAndConfiguration,
                    $"{exception.GetType().Name}: {exception.Message}");
                return FinishAdvance(result, null);
            }
        }

        private SimulationTickResult FinishAdvance(
            SimulationTickResult result,
            SimulationState committedWorkingState)
        {
            lock (_ingressGate)
            {
                if (result.Committed)
                {
                    _state = committedWorkingState;
                }

                // These commands arrived after the phase-1 latch. They become the
                // authoritative inbox of the resulting checkpoint (success or abort).
                for (var index = 0; index < _deferredIngress.Count; index++)
                {
                    _state.AcceptCommandToInbox(_deferredIngress[index]);
                }

                _deferredIngress.Clear();
                _isAdvancing = false;
                return result;
            }
        }

        private static IReadOnlyList<RegisteredSystem> OrderAndValidateSystems(
            IEnumerable<ISimulationPhaseSystem> systems)
        {
            var core = new ISimulationPhaseSystem[]
            {
                new AirshipCommandPhaseSystem(),
                new AirshipMovementPhaseSystem(),
                new MachineBuildTopologyPhaseSystem(),
                new MachineSalvagePhaseSystem(),
                new BeltLineTopologyPhaseSystem(),
                new MachineItemFlowPhaseSystem(),
                new MachineCyclePhaseSystem(),
                new MachineCompletionPhaseSystem(),
                new TransferCommitPhaseSystem(),
                new InventorySlotMovePhaseSystem(),
                new AirshipRepairCommitPhaseSystem(),
            };
            var ordered = new List<RegisteredSystem>(core.Length);
            for (var index = 0; index < core.Length; index++)
            {
                StatelessSystemContract.Validate(core[index]);
                ordered.Add(new RegisteredSystem(core[index]));
            }

            if (systems != null)
            {
                foreach (var system in systems)
                {
                    if (system == null)
                    {
                        throw new ArgumentException(
                            "A simulation system collection cannot contain null.",
                            nameof(systems));
                    }

                    if ((byte)system.Phase < 1 || (byte)system.Phase > 12)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(systems),
                            $"System {system.GetType().FullName} has an invalid phase.");
                    }

                    StatelessSystemContract.Validate(system);
                    ordered.Add(new RegisteredSystem(system));
                }
            }

            ordered.Sort(RegisteredSystem.Compare);
            for (var index = 1; index < ordered.Count; index++)
            {
                if (RegisteredSystem.Compare(ordered[index - 1], ordered[index]) == 0)
                {
                    throw new ArgumentException(
                        $"Systems {ordered[index - 1].TypeName} and {ordered[index].TypeName} "
                        + "share the same deterministic order key.",
                        nameof(systems));
                }
            }

            return ordered;
        }

        private void ExecuteSystemsForPhase(SimulationPhase phase, SimulationPhaseContext context)
        {
            for (var index = 0; index < _systems.Count; index++)
            {
                var registration = _systems[index];
                if (registration.Phase == phase)
                {
                    registration.System.Execute(context);
                }
            }
        }

        private void NotifyPhaseBoundary(SimulationPhase phase)
        {
            var handlers = PhaseBoundaryReached;
            if (handlers == null)
            {
                return;
            }

            // Adapter/diagnostic callbacks are deliberately outside the
            // authoritative failure path. One faulty subscriber must neither
            // abort the tick nor prevent other subscribers from observing it.
            foreach (Action<SimulationPhase> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(phase);
                }
                catch (Exception)
                {
                    // Non-authoritative observer failures have no logical effect.
                }
            }
        }

        private static void ApplyCoreCommands(SimulationPhaseContext context)
        {
            foreach (var command in context.Commands)
            {
                switch (command.Kind)
                {
                    case SimulationCommandKinds.NoOp:
                        break;

                    case SimulationCommandKinds.Transfer:
                        // Committed by TransferCommitPhaseSystem in phase 9.
                        break;

                    case SimulationCommandKinds.MoveInventorySlot:
                        // Committed by InventorySlotMovePhaseSystem in phase 9,
                        // subito dopo i trasferimenti dello stesso tick.
                        break;

                    case SimulationCommandKinds.BuildMachineGraphElement:
                        if (!MachineBuildCommandPayload.TryDecode(
                                command,
                                out var buildSpecification))
                        {
                            context.RecordCommandRejection(
                                command,
                                CommandRejectionReason.BuildMalformed);
                            break;
                        }

                        if (!MachineBuildRule.TryPreflight(
                                context.GetMachineMutable(),
                                context.GetInventoriesMutable(),
                                context.Catalog,
                                command.InitiatorId,
                                buildSpecification,
                                out var buildRejection))
                        {
                            context.RecordCommandRejection(command, buildRejection);
                            break;
                        }

                        MachineBuildRule.ConsumeCost(
                            context.GetInventoriesMutable(),
                            command.InitiatorId,
                            buildSpecification);
                        context.StageCreation(
                            MachineBuildCommandPayload.CreationKey(command));
                        break;

                    case SimulationCommandKinds.SalvageMachineGraphElement:
                        // Validated and applied together by MachineSalvagePhaseSystem
                        // in LocalTopologyChanges, the phase where the graph may
                        // legally lose a node. Repeating the preflight here would
                        // record a second rejection for the same command, and unlike
                        // a build there is no id to allocate ahead of time.
                        break;

                    case SimulationCommandKinds.SetQuantity:
                        context.SetQuantity(
                            command.DestinationId,
                            new NonNegativeQuantity(command.QuantizedValue));
                        break;

                    case SimulationCommandKinds.AddQuantity:
                        if (!context.TryAddQuantity(
                                command.DestinationId,
                                new NonNegativeQuantity(command.QuantizedValue)))
                        {
                            context.RecordCommandRejection(
                                command,
                                CommandRejectionReason.QuantityOverflow);
                        }
                        break;

                    case SimulationCommandKinds.RemoveQuantity:
                        if (!context.TryRemoveQuantity(
                                command.DestinationId,
                                new NonNegativeQuantity(command.QuantizedValue)))
                        {
                            context.RecordCommandRejection(
                                command,
                                CommandRejectionReason.InsufficientQuantity);
                        }
                        break;

                    default:
                        if (AirshipCommandKinds.IsAirshipCommand(command.Kind))
                        {
                            break;
                        }

                        throw new SimulationInvariantException(
                            $"No authoritative handler exists for command kind '{command.Kind}'.");
                }
            }
        }

        private sealed class RegisteredSystem
        {
            public RegisteredSystem(ISimulationPhaseSystem system)
            {
                System = system;
                Phase = system.Phase;
                Order = system.Order;
                StableOrderId = system.StableOrderId;
                TypeName = system.GetType().FullName ?? system.GetType().Name;
            }

            public ISimulationPhaseSystem System { get; }

            public SimulationPhase Phase { get; }

            public int Order { get; }

            public StableId StableOrderId { get; }

            public string TypeName { get; }

            public static int Compare(RegisteredSystem left, RegisteredSystem right)
            {
                var comparison = left.Phase.CompareTo(right.Phase);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = left.Order.CompareTo(right.Order);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = left.StableOrderId.CompareTo(right.StableOrderId);
                return comparison != 0
                    ? comparison
                    : string.Compare(left.TypeName, right.TypeName, StringComparison.Ordinal);
            }
        }
    }
}

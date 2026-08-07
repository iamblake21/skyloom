using System;
using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// The authoritative machine rules, split across the three phases they belong to:
    /// item flow in phase 6, timers in phase 7, completion in phase 8.
    ///
    /// The order matters and is not arbitrary. Flow first means a machine that was
    /// starved last tick can start this tick with what just arrived. Completion last
    /// means the deposit of a finished cycle is visible to the next tick's flow, not to
    /// this one, so a link never carries an item that was produced after it ran.
    ///
    /// Nothing here clamps or discards silently. Every operation is all-or-nothing, and
    /// an impossible state throws: the engine drops the whole working clone on any
    /// exception, so a failed tick cannot leave half a transfer behind.
    /// </summary>
    internal static class MachineReducer
    {
        /// <summary>The 20 Hz authoritative step, in the milliseconds recipes are written in.</summary>
        public const long MillisecondsPerTick = 1000L / SimulationTick.TicksPerSecond;

        /// <summary>Phase 6: carry items along every belt lane, in lane id order.</summary>
        public static void AdvanceItemFlow(MachineSimulationState state, GameCatalog catalog)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.IsEmpty)
            {
                return;
            }

            RequireCatalog(catalog);
            MachineSpatialTopology.Advance(state, catalog);
            DrawExtractingFunnels(state, catalog);
            AdvanceLanes(state, catalog);
            PushInsertingFunnels(state, catalog);
        }

        /// <summary>
        /// A funnel draws only when it is the source of a lane. The same physical
        /// funnel inserts instead when it is the destination of a lane.
        ///
        /// Funnels run before lanes so that a unit drawn this tick is available to the
        /// belt this tick: the extractor adds no latency of its own, it only decides where
        /// material may leave a holder from.
        /// </summary>
        private static void DrawExtractingFunnels(
            MachineSimulationState state,
            GameCatalog catalog)
        {
            var one = new NonNegativeQuantity(1L);
            foreach (var pair in state.Nodes)
            {
                var funnel = pair.Value;
                if (funnel.Kind != MachineNodeKind.Funnel
                    || funnel.AttachedNodeId.IsNone
                    || !TryGetOutgoingLane(state, funnel.Id, out var outgoing))
                {
                    continue;
                }

                if (!state.TryGetNodeMutable(
                        funnel.AttachedNodeId,
                        out var attached))
                {
                    throw new SimulationInvariantException(
                        $"Funnel {funnel.Id} attaches to {funnel.AttachedNodeId}, which does "
                        + "not exist.");
                }

                if (state.TryGetNodeMutable(
                        outgoing.DestinationNodeId,
                        out var destination)
                    && IsMachineUnavailableForInput(destination))
                {
                    // When the press ram is moving (or its previous plate still waits
                    // at the output), the upstream line is genuinely stopped.
                    continue;
                }

                var slot = funnel.Input;
                if (!slot.IsEmpty)
                {
                    // One slot, so a funnel holding something waits for it to be taken.
                    // That is what makes a stalled belt stop draining the crate behind it
                    // instead of quietly emptying it into a queue nobody asked for.
                    continue;
                }

                var itemId = outgoing.ItemFilter;
                if (itemId.IsNone
                    && !attached.Output.TryPeekFirstItem(out itemId))
                {
                    continue;
                }

                if (attached.Output.Count(itemId).Value < 1L
                    || !slot.TryStore(itemId, one, catalog))
                {
                    continue;
                }

                if (!attached.Output.TryTake(itemId, one))
                {
                    throw new SimulationInvariantException(
                        $"Funnel {funnel.Id} stored a unit of {itemId} it could not then "
                        + "take from its attached node.");
                }
            }
        }

        /// <summary>
        /// Per lane, in lane id order: advance, deliver, load.
        ///
        /// That order and no other. Advancing first means an item that reaches the exit
        /// this tick is delivered this tick, so the latency is exactly
        /// ceil(length / speed) and not one more. Delivering before loading means the room
        /// a delivery frees is available to the entry immediately, so a saturated lane
        /// recovers in one tick instead of two.
        /// </summary>
        private static void AdvanceLanes(MachineSimulationState state, GameCatalog catalog)
        {
            foreach (var pair in state.Lanes)
            {
                var lane = pair.Value;
                if (!state.TryGetNodeMutable(lane.SourceNodeId, out var source)
                    || !state.TryGetNodeMutable(lane.DestinationNodeId, out var destination))
                {
                    throw new SimulationInvariantException(
                        $"Lane {lane.Id} references a node that does not exist.");
                }

                if (IsMachineUnavailableForInput(destination))
                {
                    continue;
                }

                AdvanceLaneItems(lane);
                DeliverLaneItems(lane, destination, catalog);
                LoadLaneEntry(lane, source, destination, catalog);
            }
        }

        /// <summary>
        /// Moves every item forward, front first. An item never passes the one ahead of it
        /// closer than the spacing and never moves backwards, which is what makes a queue
        /// form from the exit backwards when the destination refuses.
        /// </summary>
        private static void AdvanceLaneItems(BeltLaneState lane)
        {
            var ceilingAhead = lane.LengthMillimetres;
            for (var index = 0; index < lane.ItemCount; index++)
            {
                var item = lane.Items[index];
                var target = Math.Min(
                    ceilingAhead,
                    checked(item.PositionMillimetres + lane.SpeedMillimetresPerTick));
                if (target > item.PositionMillimetres)
                {
                    lane.SetItemAt(index, item.MovedTo(target));
                }
                else
                {
                    target = item.PositionMillimetres;
                }

                ceilingAhead = target - lane.SpacingMillimetres;
                if (ceilingAhead < 0)
                {
                    ceilingAhead = 0;
                }
            }
        }

        private static void DeliverLaneItems(
            BeltLaneState lane,
            MachineNodeState destination,
            GameCatalog catalog)
        {
            var one = new NonNegativeQuantity(1L);
            while (lane.ItemCount > 0)
            {
                var front = lane.Items[0];
                if (front.PositionMillimetres < lane.LengthMillimetres)
                {
                    return;
                }

                var port = destination.Input;
                if (!Admits(destination, front.ItemId, catalog)
                    || port.StorableQuantity(front.ItemId, catalog).Value < 1L)
                {
                    // The destination will not take it. The item stays at the exit and the
                    // ones behind it queue up against it: that is backpressure with a
                    // length, which a logical link could not express.
                    return;
                }

                if (!port.TryStore(front.ItemId, one, catalog))
                {
                    throw new SimulationInvariantException(
                        $"Lane {lane.Id} measured room for {front.ItemId} and was then refused.");
                }

                lane.RemoveFront();
                lane.DeliveredUnits = checked(lane.DeliveredUnits + 1UL);
            }
        }

        /// <summary>
        /// Takes at most one unit per tick from the source. One per tick is the entry
        /// ceiling, and together with the spacing it is what
        /// <see cref="BeltLaneState.ThroughputPerThousandTicks"/> declares.
        /// </summary>
        private static void LoadLaneEntry(
            BeltLaneState lane,
            MachineNodeState source,
            MachineNodeState destination,
            GameCatalog catalog)
        {
            if (!lane.HasRoomAtEntry())
            {
                return;
            }

            // A belt never reaches into a crate. It may load from an extracting funnel
            // or from a machine's explicit output port.
            if (source.Kind != MachineNodeKind.Funnel
                && source.Kind != MachineNodeKind.Machine)
            {
                return;
            }

            var sourcePort = source.Output;
            var itemId = lane.ItemFilter;
            if (itemId.IsNone && !sourcePort.TryPeekFirstItem(out itemId))
            {
                return;
            }

            // Capacity is deliberately not checked here. A full destination must make
            // the cargo ride to the exit and form a visible queue on the belt. Only a
            // permanently incompatible item is refused at entry.
            if (!AcceptsItemType(destination, itemId, catalog))
            {
                return;
            }

            var one = new NonNegativeQuantity(1L);
            if (sourcePort.Count(itemId).Value < 1L || !sourcePort.TryTake(itemId, one))
            {
                return;
            }

            lane.AddAtEntry(itemId);
        }

        /// <summary>
        /// A destination funnel owns one visible holding slot. After every lane has
        /// moved, it pushes at most one unit through its physical attachment. A full
        /// crate therefore backs up the funnel, then the belt, without deleting cargo.
        /// </summary>
        private static void PushInsertingFunnels(
            MachineSimulationState state,
            GameCatalog catalog)
        {
            var one = new NonNegativeQuantity(1L);
            foreach (var pair in state.Nodes)
            {
                var funnel = pair.Value;
                if (funnel.Kind != MachineNodeKind.Funnel
                    || funnel.AttachedNodeId.IsNone
                    || !IsLaneDestination(state, funnel.Id)
                    || funnel.Input.IsEmpty
                    || !funnel.Input.TryPeekFirstItem(out var itemId))
                {
                    continue;
                }

                if (!state.TryGetNodeMutable(
                        funnel.AttachedNodeId,
                        out var attached))
                {
                    throw new SimulationInvariantException(
                        $"Funnel {funnel.Id} attaches to {funnel.AttachedNodeId}, which does "
                        + "not exist.");
                }

                if (!Admits(attached, itemId, catalog)
                    || attached.Input.StorableQuantity(itemId, catalog).Value < 1L)
                {
                    continue;
                }

                if (!attached.Input.TryStore(itemId, one, catalog))
                {
                    throw new SimulationInvariantException(
                        $"Funnel {funnel.Id} measured room for {itemId} in "
                        + $"{attached.Id} and was then refused.");
                }

                if (!funnel.Output.TryTake(itemId, one))
                {
                    throw new SimulationInvariantException(
                        $"Funnel {funnel.Id} inserted a unit of {itemId} it did not hold.");
                }
            }
        }

        private static bool TryGetOutgoingLane(
            MachineSimulationState state,
            StableId nodeId,
            out BeltLaneState outgoing)
        {
            foreach (var pair in state.Lanes)
            {
                if (pair.Value.SourceNodeId == nodeId)
                {
                    outgoing = pair.Value;
                    return true;
                }
            }

            outgoing = null;
            return false;
        }

        private static bool IsLaneDestination(
            MachineSimulationState state,
            StableId nodeId)
        {
            foreach (var pair in state.Lanes)
            {
                if (pair.Value.DestinationNodeId == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMachineUnavailableForInput(MachineNodeState node)
        {
            return node.Kind == MachineNodeKind.Machine
                && (node.IsCycleActive || !node.Output.IsEmpty);
        }

        /// <summary>
        /// Phase 7: start a cycle where the inputs allow it and advance the cycles in
        /// flight. Inputs are consumed at the start, which is what lets a cycle survive
        /// an empty input port without losing its progress.
        /// </summary>
        public static void AdvanceCycles(MachineSimulationState state, GameCatalog catalog)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.IsEmpty)
            {
                return;
            }

            RequireCatalog(catalog);

            foreach (var pair in state.Nodes)
            {
                var node = pair.Value;
                if (node.Kind != MachineNodeKind.Machine)
                {
                    continue;
                }

                if (node.ActiveRecipeId.IsNone)
                {
                    node.Activity = MachineActivity.NoRecipe;
                    continue;
                }

                var recipe = ResolveRecipe(node, catalog);
                var machine = ResolveMachine(node, catalog);

                if (!node.IsCycleActive)
                {
                    if (!node.Output.IsEmpty)
                    {
                        node.Activity = MachineActivity.OutputFull;
                        continue;
                    }

                    if (!HasInputs(node.Input, recipe))
                    {
                        node.Activity = MachineActivity.MissingInput;
                        continue;
                    }

                    if (!HasFuel(node, machine))
                    {
                        node.Activity = MachineActivity.MissingFuel;
                        continue;
                    }

                    if (!TryConsumeInputs(node.Input, recipe))
                    {
                        throw new SimulationInvariantException(
                            $"Machine {node.Id} lost recipe inputs between "
                            + "preflight and cycle start.");
                    }

                    if (!TryConsumeFuel(node, machine))
                    {
                        throw new SimulationInvariantException(
                            $"Machine {node.Id} lost fuel between preflight "
                            + "and cycle start.");
                    }

                    node.IsCycleActive = true;
                    node.ProgressMilliseconds = 0L;
                }

                if (node.ProgressMilliseconds >= recipe.DurationMilliseconds)
                {
                    // Finished but not yet deposited: phase 8 owns this state and the
                    // reason it reports. Advancing progress further would be work done
                    // twice on the same cycle.
                    continue;
                }

                node.ProgressMilliseconds = Math.Min(
                    recipe.DurationMilliseconds,
                    checked(node.ProgressMilliseconds + MillisecondsPerTick));
                node.Activity = MachineActivity.Running;
            }
        }

        /// <summary>
        /// Phase 8: deposit the outputs of finished cycles. A cycle whose outputs do not
        /// fit stays finished and keeps everything it holds, which is the whole of
        /// backpressure: the line stops, and no input has been destroyed to stop it.
        /// </summary>
        public static void CompleteCycles(MachineSimulationState state, GameCatalog catalog)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.IsEmpty)
            {
                return;
            }

            RequireCatalog(catalog);

            foreach (var pair in state.Nodes)
            {
                var node = pair.Value;
                if (node.Kind != MachineNodeKind.Machine || !node.IsCycleActive)
                {
                    continue;
                }

                var recipe = ResolveRecipe(node, catalog);
                if (node.ProgressMilliseconds < recipe.DurationMilliseconds)
                {
                    continue;
                }

                if (!TryDepositOutputs(node.Output, recipe, catalog))
                {
                    node.Activity = MachineActivity.OutputFull;
                    continue;
                }

                node.IsCycleActive = false;
                node.ProgressMilliseconds = 0L;
                node.CompletedCycles = checked(node.CompletedCycles + 1UL);
                node.Activity = MachineActivity.Running;
            }
        }

        /// <summary>
        /// Whether a node's input port accepts this item at all. A machine takes only
        /// what its active recipe consumes: without this a press fed a mixed crate would
        /// fill its two slots with plates it can never use and deadlock itself.
        /// </summary>
        private static bool Admits(MachineNodeState node, StableId itemId, GameCatalog catalog)
        {
            if (itemId.IsNone)
            {
                return false;
            }

            if (node.Kind == MachineNodeKind.Funnel)
            {
                return !node.AttachedNodeId.IsNone
                    && node.Input.Count(itemId).Value == 0L;
            }

            if (node.Kind == MachineNodeKind.Buffer)
            {
                return true;
            }

            if (IsMachineUnavailableForInput(node)
                || node.ActiveRecipeId.IsNone
                || !catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe)
                || recipe.Inputs == null)
            {
                return false;
            }

            var required = 0L;
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                if (recipe.Inputs[index].ItemId == itemId)
                {
                    required = checked(required + recipe.Inputs[index].Quantity);
                }
            }

            if (required <= 0L
                || !catalog.TryGetMachine(node.DefinitionId, out var machine))
            {
                return false;
            }

            return node.Input.Count(itemId).Value
                < machine.InputBufferCapacityPerItem;
        }

        private static bool AcceptsItemType(
            MachineNodeState node,
            StableId itemId,
            GameCatalog catalog)
        {
            if (itemId.IsNone || node.Kind == MachineNodeKind.Buffer)
            {
                return false;
            }

            if (node.Kind == MachineNodeKind.Funnel)
            {
                return true;
            }

            if (node.ActiveRecipeId.IsNone
                || !catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe)
                || recipe.Inputs == null)
            {
                return false;
            }

            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                if (recipe.Inputs[index].ItemId == itemId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Takes every input of the recipe, or nothing at all. The attempt runs on a
        /// copy first: a recipe that names the same item twice would otherwise be able
        /// to pass an item-by-item check and then fail halfway through the real takes.
        /// </summary>
        private static bool TryConsumeInputs(MachinePort port, RecipeDefinition recipe)
        {
            if (recipe.Inputs == null || recipe.Inputs.Count == 0)
            {
                return true;
            }

            var trial = port.DeepClone();
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var input = recipe.Inputs[index];
                if (!trial.TryTake(input.ItemId, new NonNegativeQuantity(input.Quantity)))
                {
                    return false;
                }
            }

            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var input = recipe.Inputs[index];
                if (!port.TryTake(input.ItemId, new NonNegativeQuantity(input.Quantity)))
                {
                    throw new SimulationInvariantException(
                        $"Recipe '{recipe.Key}' passed its input trial and then failed to consume "
                        + $"{input.Quantity} of {input.ItemId}.");
                }
            }

            return true;
        }

        private static bool HasInputs(
            MachinePort port,
            RecipeDefinition recipe)
        {
            if (recipe.Inputs == null || recipe.Inputs.Count == 0)
            {
                return true;
            }

            var trial = port.DeepClone();
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var input = recipe.Inputs[index];
                if (!trial.TryTake(
                        input.ItemId,
                        new NonNegativeQuantity(input.Quantity)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasFuel(
            MachineNodeState node,
            MachineDefinition machine)
        {
            return !machine.RequiresFuel
                || node.Fuel != null
                && node.Fuel.Count(machine.FuelItemId).Value >=
                    machine.FuelQuantityPerCycle;
        }

        private static bool TryConsumeFuel(
            MachineNodeState node,
            MachineDefinition machine)
        {
            return !machine.RequiresFuel
                || node.Fuel != null
                && node.Fuel.TryTake(
                    machine.FuelItemId,
                    new NonNegativeQuantity(
                        machine.FuelQuantityPerCycle));
        }

        /// <summary>Stores every output of the recipe, or nothing at all.</summary>
        private static bool TryDepositOutputs(
            MachinePort port,
            RecipeDefinition recipe,
            GameCatalog catalog)
        {
            if (recipe.Outputs == null || recipe.Outputs.Count == 0)
            {
                return true;
            }

            var trial = port.DeepClone();
            for (var index = 0; index < recipe.Outputs.Count; index++)
            {
                var output = recipe.Outputs[index];
                if (!trial.TryStore(
                        output.ItemId,
                        new NonNegativeQuantity(output.Quantity),
                        catalog))
                {
                    return false;
                }
            }

            for (var index = 0; index < recipe.Outputs.Count; index++)
            {
                var output = recipe.Outputs[index];
                if (!port.TryStore(
                        output.ItemId,
                        new NonNegativeQuantity(output.Quantity),
                        catalog))
                {
                    throw new SimulationInvariantException(
                        $"Recipe '{recipe.Key}' passed its output trial and then failed to store "
                        + $"{output.Quantity} of {output.ItemId}.");
                }
            }

            return true;
        }

        private static RecipeDefinition ResolveRecipe(MachineNodeState node, GameCatalog catalog)
        {
            if (!catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe))
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} references recipe {node.ActiveRecipeId}, which the "
                    + "validated catalog does not contain.");
            }

            if (recipe.DurationMilliseconds <= 0L)
            {
                throw new SimulationInvariantException(
                    $"Recipe '{recipe.Key}' declares a non-positive duration.");
            }

            return recipe;
        }

        private static MachineDefinition ResolveMachine(
            MachineNodeState node,
            GameCatalog catalog)
        {
            if (!catalog.TryGetMachine(node.DefinitionId, out var machine))
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} references missing definition "
                    + $"{node.DefinitionId}.");
            }

            return machine;
        }

        private static void RequireCatalog(GameCatalog catalog)
        {
            if (catalog == null)
            {
                throw new SimulationInvariantException(
                    "A state that contains a machine graph cannot advance without the "
                    + "validated catalog that defines its recipes and stack limits.");
            }
        }
    }
}

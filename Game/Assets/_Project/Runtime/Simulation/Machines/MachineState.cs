using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    public enum MachineNodeKind : byte
    {
        Buffer = 1,
        Machine,
        Funnel,
        BeltModule,
    }

    public enum MachineActivity : byte
    {
        Idle = 1,
        Running,
        NoRecipe,
        MissingInput,
        OutputFull,
        MissingFuel,
    }

    /// <summary>
    /// Authoritative travel direction published by the Belt Drive on the connected
    /// logistics line. Placement yaw describes geometry; this value describes flow.
    /// </summary>
    public enum BeltTravelDirection : byte
    {
        Stopped = 0,
        Forward = 1,
        Reverse = 2,
    }

    public enum BeltLineStatus : byte
    {
        NotApplicable = 0,
        MissingDrive = 1,
        Operational = 2,
        Overloaded = 3,
        DirectionConflict = 4,
    }

    [Serializable]
    public sealed class MachineNodeState
    {
        public StableId Id { get; }

        public MachineNodeKind Kind { get; }

        public StableId DefinitionId { get; }

        public MachinePort Input { get; }

        public MachinePort Output { get; }

        /// <summary>
        /// Optional auxiliary port owned only by fuel-driven machines. Keeping
        /// it distinct from Input lets UI and future logistics route ore and
        /// combustible without guessing from the item currently present.
        /// </summary>
        public MachinePort Fuel { get; }

        public StableId ActiveRecipeId { get; internal set; }

        public long ProgressMilliseconds { get; internal set; }

        public bool IsCycleActive { get; internal set; }

        public MachineActivity Activity { get; internal set; }

        public ulong CompletedCycles { get; internal set; }

        /// <summary>
        /// Quantized world placement used by the pure geometry resolver. Authored
        /// bootstrap nodes may omit it because their legacy lanes already define
        /// topology; every player-built node and loose logistics module carries it.
        /// </summary>
        public bool HasPlacementPose { get; private set; }

        public MachineBuildPose PlacementPose { get; private set; }

        /// <summary>
        /// Position of the single workpiece on a straight belt module. The item lives
        /// in the aliased storage port; this value only says how far it travelled.
        /// </summary>
        public int TransportProgressMillimetres { get; internal set; }

        public BeltTravelDirection BeltTravelDirection { get; internal set; }

        public BeltLineStatus BeltLineStatus { get; internal set; }

        public int BeltLineUsedCapacity { get; internal set; }

        public int BeltLineAvailableCapacity { get; internal set; }

        /// <summary>
        /// The physical inventory or machine port touching this funnel. A detached
        /// funnel keeps <see cref="StableId.None"/> and is inert.
        /// </summary>
        public StableId AttachedNodeId { get; private set; }

        [Obsolete("Use AttachedNodeId. A funnel can insert as well as extract.")]
        public StableId IntakeNodeId => AttachedNodeId;

        private MachineNodeState(
            StableId id,
            MachineNodeKind kind,
            StableId definitionId,
            MachinePort input,
            MachinePort output,
            MachinePort fuel = null)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("A machine graph node requires a stable id.", nameof(id));
            }

            if (definitionId.IsNone)
            {
                throw new ArgumentException(
                    "A machine graph node requires a content definition.",
                    nameof(definitionId));
            }

            Id = id;
            Kind = kind;
            DefinitionId = definitionId;
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Output = output ?? throw new ArgumentNullException(nameof(output));
            Fuel = fuel;
            ActiveRecipeId = StableId.None;
            Activity = kind == MachineNodeKind.Machine
                ? MachineActivity.NoRecipe
                : MachineActivity.Idle;
        }

        public static MachineNodeState CreateBuffer(
            StableId id,
            StableId containerDefinitionId,
            int slotCount)
        {
            var storage = new MachinePort(MachinePortKind.Storage, slotCount);
            return new MachineNodeState(
                id,
                MachineNodeKind.Buffer,
                containerDefinitionId,
                storage,
                storage);
        }

        public static MachineNodeState CreateBuffer(
            StableId id,
            StableId containerDefinitionId,
            int slotCount,
            MachineBuildPose pose)
        {
            var node = CreateBuffer(id, containerDefinitionId, slotCount);
            node.SetPlacementPose(pose);
            return node;
        }

        /// <summary>
        /// Spatial funnel module. Its inventory side is one metre behind the pose
        /// facing and its belt side is one metre ahead. No endpoint is stored.
        /// </summary>
        public static MachineNodeState CreateFunnel(
            StableId id,
            StableId itemDefinitionId,
            MachineBuildPose pose)
        {
            var storage = new MachinePort(MachinePortKind.Storage, 1);
            var node = new MachineNodeState(
                id,
                MachineNodeKind.Funnel,
                itemDefinitionId,
                storage,
                storage);
            node.SetPlacementPose(pose);
            return node;
        }

        /// <summary>
        /// Transitional construction for unposed bootstrap graphs. New gameplay
        /// must use the pose overload; the attachment is never used by spatial flow.
        /// </summary>
        public static MachineNodeState CreateFunnel(
            StableId id,
            StableId itemDefinitionId,
            StableId attachedNodeId)
        {
            if (!attachedNodeId.IsNone && attachedNodeId == id)
            {
                throw new ArgumentException(
                    "A funnel cannot attach to itself.",
                    nameof(attachedNodeId));
            }

            var storage = new MachinePort(MachinePortKind.Storage, 1);
            return new MachineNodeState(
                id,
                MachineNodeKind.Funnel,
                itemDefinitionId,
                storage,
                storage)
            {
                AttachedNodeId = attachedNodeId,
            };
        }

        public static MachineNodeState CreateMachine(
            StableId id,
            StableId machineDefinitionId,
            int inputSlots,
            int outputSlots,
            int fuelSlots = 0)
        {
            return new MachineNodeState(
                id,
                MachineNodeKind.Machine,
                machineDefinitionId,
                new MachinePort(MachinePortKind.Input, inputSlots),
                new MachinePort(MachinePortKind.Output, outputSlots),
                fuelSlots > 0
                    ? new MachinePort(MachinePortKind.Fuel, fuelSlots)
                    : null);
        }

        public static MachineNodeState CreateMachine(
            StableId id,
            StableId machineDefinitionId,
            int inputSlots,
            int outputSlots,
            MachineBuildPose pose,
            int fuelSlots = 0)
        {
            var node = CreateMachine(
                id,
                machineDefinitionId,
                inputSlots,
                outputSlots,
                fuelSlots);
            node.SetPlacementPose(pose);
            return node;
        }

        public static MachineNodeState CreateBeltModule(
            StableId id,
            StableId itemDefinitionId,
            MachineBuildPose pose)
        {
            var storage = new MachinePort(MachinePortKind.Storage, 1);
            var node = new MachineNodeState(
                id,
                MachineNodeKind.BeltModule,
                itemDefinitionId,
                storage,
                storage);
            node.SetPlacementPose(pose);
            node.BeltLineStatus = BeltLineStatus.MissingDrive;
            return node;
        }

        internal void SetPlacementPose(MachineBuildPose pose)
        {
            PlacementPose = pose;
            HasPlacementPose = true;
        }

        /// <summary>
        /// Forgets the node this one was bolted to, for when that node is salvaged.
        /// A funnel left naming a removed node fails the reducer's invariant check.
        /// </summary>
        internal void Detach()
        {
            AttachedNodeId = StableId.None;
        }

        public MachineNodeState DeepClone()
        {
            var input = Input.DeepClone();
            var output = Input == Output ? input : Output.DeepClone();
            var fuel = Fuel?.DeepClone();
            return new MachineNodeState(
                Id,
                Kind,
                DefinitionId,
                input,
                output,
                fuel)
            {
                ActiveRecipeId = ActiveRecipeId,
                ProgressMilliseconds = ProgressMilliseconds,
                IsCycleActive = IsCycleActive,
                Activity = Activity,
                CompletedCycles = CompletedCycles,
                AttachedNodeId = AttachedNodeId,
                HasPlacementPose = HasPlacementPose,
                PlacementPose = PlacementPose,
                TransportProgressMillimetres = TransportProgressMillimetres,
                BeltTravelDirection = BeltTravelDirection,
                BeltLineStatus = BeltLineStatus,
                BeltLineUsedCapacity = BeltLineUsedCapacity,
                BeltLineAvailableCapacity = BeltLineAvailableCapacity,
            };
        }
    }

    public sealed class MachineSimulationState
    {
        private readonly SortedDictionary<StableId, MachineNodeState> _nodes =
            new SortedDictionary<StableId, MachineNodeState>();

        private readonly SortedDictionary<StableId, BeltLaneState> _lanes =
            new SortedDictionary<StableId, BeltLaneState>();

        internal IEnumerable<KeyValuePair<StableId, MachineNodeState>> Nodes => _nodes;

        internal IEnumerable<KeyValuePair<StableId, BeltLaneState>> Lanes => _lanes;

        public int NodeCount => _nodes.Count;

        public int LaneCount => _lanes.Count;

        public bool IsEmpty => _nodes.Count == 0 && _lanes.Count == 0;

        internal void AddNode(MachineNodeState node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            _nodes.Add(node.Id, node);
        }

        /// <summary>
        /// Removes one node and every lane that touched it, then detaches any funnel
        /// that still named it.
        ///
        /// The detaching is not tidiness. A funnel whose AttachedNodeId points at a
        /// node that no longer exists makes the reducer throw a SimulationInvariantException
        /// on the next tick, so leaving the reference behind would turn salvaging a
        /// crate into a crash. Ordering stays canonical for free: _nodes and _lanes
        /// are sorted by id, so removal cannot disturb the serialized sequence.
        /// </summary>
        internal bool RemoveNode(StableId id)
        {
            if (!_nodes.Remove(id))
            {
                return false;
            }

            var orphanedLanes = new List<StableId>();
            foreach (var pair in _lanes)
            {
                if (pair.Value.SourceNodeId == id
                    || pair.Value.DestinationNodeId == id)
                {
                    orphanedLanes.Add(pair.Key);
                }
            }

            for (var index = 0; index < orphanedLanes.Count; index++)
            {
                _lanes.Remove(orphanedLanes[index]);
            }

            foreach (var pair in _nodes)
            {
                if (pair.Value.AttachedNodeId == id)
                {
                    pair.Value.Detach();
                }
            }

            return true;
        }

        public bool TryGetNode(StableId id, out MachineNodeState node)
        {
            if (_nodes.TryGetValue(id, out var stored))
            {
                node = stored.DeepClone();
                return true;
            }

            node = null;
            return false;
        }

        internal void AddLane(BeltLaneState lane)
        {
            if (lane == null)
            {
                throw new ArgumentNullException(nameof(lane));
            }

            _lanes.Add(lane.Id, lane);
        }

        public bool TryGetLane(StableId id, out BeltLaneState lane)
        {
            if (_lanes.TryGetValue(id, out var stored))
            {
                lane = stored.DeepClone();
                return true;
            }

            lane = null;
            return false;
        }

        public IReadOnlyList<StableId> GetLaneIdsCanonical()
        {
            var ids = new StableId[_lanes.Count];
            var index = 0;
            foreach (var lane in _lanes)
            {
                ids[index++] = lane.Key;
            }

            return ids;
        }

        internal bool TryGetNodeMutable(StableId id, out MachineNodeState node)
        {
            return _nodes.TryGetValue(id, out node);
        }

        public IReadOnlyList<StableId> GetNodeIdsCanonical()
        {
            var ids = new StableId[_nodes.Count];
            var index = 0;
            foreach (var node in _nodes)
            {
                ids[index++] = node.Key;
            }

            return ids;
        }

        public MachineSimulationState DeepClone()
        {
            var clone = new MachineSimulationState();
            foreach (var node in _nodes)
            {
                clone._nodes.Add(node.Key, node.Value.DeepClone());
            }

            foreach (var lane in _lanes)
            {
                clone._lanes.Add(lane.Key, lane.Value.DeepClone());
            }

            return clone;
        }

        internal IReadOnlyList<StableId> GetPersistentIdsCanonical()
        {
            var ids = new StableId[checked(_nodes.Count + _lanes.Count)];
            var index = 0;
            foreach (var node in _nodes)
            {
                ids[index++] = node.Key;
            }

            foreach (var lane in _lanes)
            {
                ids[index++] = lane.Key;
            }

            Array.Sort(ids);
            return ids;
        }

        public void ValidateInvariants(GameCatalog catalog)
        {
            var occupiedPlacementCells = new HashSet<MachinePlacementCell>();
            foreach (var pair in _nodes)
            {
                var node = pair.Value;
                if (pair.Key != node.Id)
                {
                    throw new SimulationInvariantException(
                        "A machine node dictionary key does not match its id.");
                }

                var isMachine = node.Kind == MachineNodeKind.Machine;
                if (isMachine == (node.Input == node.Output))
                {
                    throw new SimulationInvariantException(
                        $"Node {node.Id} violates the port aliasing rule: a machine keeps " +
                        "two ports and everything else shares one.");
                }

                node.Input.ValidateInvariants(catalog, $"Node {node.Id} input");
                if (isMachine)
                {
                    node.Output.ValidateInvariants(catalog, $"Node {node.Id} output");
                    node.Fuel?.ValidateInvariants(
                        catalog,
                        $"Node {node.Id} fuel");
                }
                else if (node.Fuel != null)
                {
                    throw new SimulationInvariantException(
                        $"Non-machine node {node.Id} exposes a fuel port.");
                }

                if (node.HasPlacementPose)
                {
                    var pose = node.PlacementPose;
                    var cell = new MachinePlacementCell(
                        pose.XMillimetres,
                        pose.YMillimetres,
                        pose.ZMillimetres);
                    if (!occupiedPlacementCells.Add(cell))
                    {
                        throw new SimulationInvariantException(
                            $"More than one machine node occupies placement cell "
                            + $"({pose.XMillimetres}, {pose.YMillimetres}, "
                            + $"{pose.ZMillimetres}).");
                    }
                }

                if (node.Kind == MachineNodeKind.Funnel)
                {
                    if (node.Input.SlotCount != 1)
                    {
                        throw new SimulationInvariantException(
                            $"Funnel {node.Id} must have exactly one holding slot.");
                    }

                    if (node.HasPlacementPose && !node.AttachedNodeId.IsNone)
                    {
                        throw new SimulationInvariantException(
                            $"Spatial funnel {node.Id} persists an authored attachment. "
                            + "Its neighbours must be derived from its pose.");
                    }

                    if (!node.HasPlacementPose
                        && !node.AttachedNodeId.IsNone
                        && (!_nodes.TryGetValue(node.AttachedNodeId, out var attached)
                            || attached.Kind == MachineNodeKind.Funnel
                            || attached.Kind == MachineNodeKind.BeltModule))
                    {
                        throw new SimulationInvariantException(
                            $"Legacy funnel {node.Id} attaches to "
                            + $"{node.AttachedNodeId}, which is not a compatible "
                            + "inventory or machine node of this graph.");
                    }
                }

                if (node.Kind != MachineNodeKind.Funnel && !node.AttachedNodeId.IsNone)
                {
                    throw new SimulationInvariantException(
                        $"Node {node.Id} carries an attachment, which only a funnel has.");
                }

                if (node.Kind == MachineNodeKind.BeltModule)
                {
                    if (!node.HasPlacementPose)
                    {
                        throw new SimulationInvariantException(
                            $"Belt module {node.Id} has no physical placement pose.");
                    }

                    if (node.Input.SlotCount != 1)
                    {
                        throw new SimulationInvariantException(
                            $"Belt module {node.Id} must hold at most one workpiece.");
                    }
                }

                ValidateNodeProgress(node, catalog);
            }

            var connectedFunnels = new HashSet<StableId>();
            foreach (var pair in _lanes)
            {
                var lane = pair.Value;
                if (pair.Key != lane.Id)
                {
                    throw new SimulationInvariantException(
                        "A belt lane dictionary key does not match its id.");
                }

                if (!_nodes.TryGetValue(lane.SourceNodeId, out var source) ||
                    !_nodes.TryGetValue(lane.DestinationNodeId, out var destination))
                {
                    throw new SimulationInvariantException(
                        $"Lane {lane.Id} references a node that does not exist.");
                }

                if (source.HasPlacementPose
                    || destination.HasPlacementPose
                    || source.Kind == MachineNodeKind.BeltModule
                    || destination.Kind == MachineNodeKind.BeltModule)
                {
                    throw new SimulationInvariantException(
                        $"Legacy lane {lane.Id} touches a spatial node. Spatial logistics "
                        + "derive adjacency exclusively from placement poses.");
                }

                if (source.Kind == MachineNodeKind.Buffer ||
                    destination.Kind == MachineNodeKind.Buffer)
                {
                    throw new SimulationInvariantException(
                        $"Lane {lane.Id} connects directly to a buffer. Items may enter or " +
                        "leave an inventory only through a physically attached funnel.");
                }

                if (source.Kind == MachineNodeKind.Funnel)
                {
                    ValidateConnectedFunnel(source, lane.Id, connectedFunnels);
                }

                if (destination.Kind == MachineNodeKind.Funnel)
                {
                    ValidateConnectedFunnel(destination, lane.Id, connectedFunnels);
                }

                if (!lane.ItemFilter.IsNone &&
                    catalog != null &&
                    !catalog.TryGetItem(lane.ItemFilter, out var definition))
                {
                    throw new SimulationInvariantException(
                        $"Lane {lane.Id} filters on item {lane.ItemFilter}, which the " +
                        "validated catalog does not contain.");
                }

                lane.ValidateInvariants();
                if (catalog == null)
                {
                    continue;
                }

                for (var i = 0; i < lane.ItemCount; i++)
                {
                    if (!catalog.TryGetItem(lane.Items[i].ItemId, out definition))
                    {
                        throw new SimulationInvariantException(
                            $"Lane {lane.Id} carries item {lane.Items[i].ItemId}, which " +
                            "the validated catalog does not contain.");
                    }
                }
            }
        }

        private static void ValidateConnectedFunnel(
            MachineNodeState funnel,
            StableId laneId,
            ISet<StableId> connectedFunnels)
        {
            if (!connectedFunnels.Add(funnel.Id))
            {
                throw new SimulationInvariantException(
                    $"Funnel {funnel.Id} is connected to more than one lane. Its single " +
                    "physical belt mouth permits exactly one direction.");
            }
        }

        private static void ValidateNodeProgress(MachineNodeState node, GameCatalog catalog)
        {
            if (node.ProgressMilliseconds < 0)
            {
                throw new SimulationInvariantException(
                    $"Node {node.Id} holds negative cycle progress.");
            }

            if (node.Kind == MachineNodeKind.BeltModule)
            {
                if (node.BeltTravelDirection < BeltTravelDirection.Stopped
                    || node.BeltTravelDirection > BeltTravelDirection.Reverse)
                {
                    throw new SimulationInvariantException(
                        $"Belt module {node.Id} has invalid travel direction "
                        + $"{node.BeltTravelDirection}.");
                }

                if (node.TransportProgressMillimetres < 0
                    || node.TransportProgressMillimetres
                        > MachineSpatialTopology.BeltLengthMillimetres)
                {
                    throw new SimulationInvariantException(
                        $"Belt module {node.Id} holds transport progress "
                        + $"{node.TransportProgressMillimetres} mm outside the physical "
                        + $"0..{MachineSpatialTopology.BeltLengthMillimetres} mm span.");
                }

                if (node.Input.IsEmpty && node.TransportProgressMillimetres != 0)
                {
                    throw new SimulationInvariantException(
                        $"Empty belt module {node.Id} retains transport progress.");
                }
            }
            else if (node.TransportProgressMillimetres != 0
                || node.BeltTravelDirection != BeltTravelDirection.Stopped)
            {
                throw new SimulationInvariantException(
                    $"{node.Kind} {node.Id} carries belt-only transport state.");
            }

            if (node.BeltLineStatus < BeltLineStatus.NotApplicable
                || node.BeltLineStatus > BeltLineStatus.DirectionConflict
                || node.BeltLineUsedCapacity < 0
                || node.BeltLineAvailableCapacity < 0)
            {
                throw new SimulationInvariantException(
                    $"Node {node.Id} has invalid belt-line state.");
            }

            if (node.Kind != MachineNodeKind.Machine)
            {
                if (node.IsCycleActive ||
                    node.ProgressMilliseconds != 0L ||
                    !node.ActiveRecipeId.IsNone)
                {
                    throw new SimulationInvariantException(
                        $"{node.Kind} {node.Id} carries cycle state, which only a machine has.");
                }

                if (node.Activity != MachineActivity.Idle)
                {
                    throw new SimulationInvariantException(
                        $"{node.Kind} {node.Id} reports activity {node.Activity}; it is Idle.");
                }

                if (catalog != null
                    && (node.Kind == MachineNodeKind.Funnel
                        || node.Kind == MachineNodeKind.BeltModule)
                    && !catalog.TryGetItem(node.DefinitionId, out _))
                {
                    throw new SimulationInvariantException(
                        $"{node.Kind} {node.Id} references item {node.DefinitionId}, "
                        + "which the validated catalog does not contain.");
                }

                return;
            }

            if (node.Activity == MachineActivity.Idle)
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} reports Idle, which names no cause.");
            }

            if (!node.IsCycleActive && node.ProgressMilliseconds != 0L)
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} holds progress without a cycle in flight.");
            }

            if (node.IsCycleActive && node.ActiveRecipeId.IsNone)
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} runs a cycle without a recipe.");
            }

            if (catalog == null || node.ActiveRecipeId.IsNone)
            {
                return;
            }

            if (!catalog.TryGetRecipe(node.ActiveRecipeId, out var recipe))
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} references recipe {node.ActiveRecipeId}, which the " +
                    "validated catalog does not contain.");
            }

            if (node.ProgressMilliseconds > recipe.DurationMilliseconds)
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} holds progress {node.ProgressMilliseconds} ms above " +
                    $"the {recipe.DurationMilliseconds} ms of '{recipe.Key}'.");
            }

            if (!catalog.TryGetMachine(node.DefinitionId, out var machine))
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} references machine definition {node.DefinitionId}, " +
                    "which the validated catalog does not contain.");
            }

            var supportsRecipe = false;
            for (var i = 0; i < machine.SupportedRecipeIds.Count; i++)
            {
                if (machine.SupportedRecipeIds[i] == node.ActiveRecipeId)
                {
                    supportsRecipe = true;
                    break;
                }
            }

            if (!supportsRecipe)
            {
                throw new SimulationInvariantException(
                    $"Machine {node.Id} runs '{recipe.Key}', which '{machine.Key}' does not support.");
            }
        }

        private readonly struct MachinePlacementCell : IEquatable<MachinePlacementCell>
        {
            public MachinePlacementCell(int xMillimetres, int yMillimetres, int zMillimetres)
            {
                XMillimetres = xMillimetres;
                YMillimetres = yMillimetres;
                ZMillimetres = zMillimetres;
            }

            public int XMillimetres { get; }

            public int YMillimetres { get; }

            public int ZMillimetres { get; }

            public bool Equals(MachinePlacementCell other) =>
                XMillimetres == other.XMillimetres
                && YMillimetres == other.YMillimetres
                && ZMillimetres == other.ZMillimetres;

            public override bool Equals(object obj) =>
                obj is MachinePlacementCell other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((XMillimetres * 397) ^ YMillimetres) * 397
                        ^ ZMillimetres;
                }
            }
        }
    }

}

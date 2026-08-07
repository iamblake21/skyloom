using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Resolves the logistics line described by placed Belt modules and in-line
    /// machines. A Belt Drive publishes direction and twelve capacity units to its
    /// connected component. There is no external mechanical-power network.
    /// </summary>
    public static class BeltLineRules
    {
        public const int CapacityPerDrive = 12;

        public static void Recompute(MachineSimulationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var elements = CollectElements(state);
            var adjacency = BuildAdjacency(elements);
            var visited = new HashSet<StableId>();

            foreach (var pair in state.Nodes)
            {
                Reset(pair.Value);
            }

            foreach (var pair in state.Nodes)
            {
                if (!elements.ContainsKey(pair.Key) || !visited.Add(pair.Key))
                {
                    continue;
                }

                var component = CollectComponent(pair.Key, elements, adjacency, visited);
                ResolveComponent(component, adjacency);
            }
        }

        private static Dictionary<StableId, LineElement> CollectElements(
            MachineSimulationState state)
        {
            var elements = new Dictionary<StableId, LineElement>();
            foreach (var pair in state.Nodes)
            {
                var node = pair.Value;
                if (!TryCreateElement(node, out var element))
                {
                    continue;
                }

                elements.Add(node.Id, element);
            }

            return elements;
        }

        private static bool TryCreateElement(
            MachineNodeState node,
            out LineElement element)
        {
            element = default;
            if (node == null || !node.HasPlacementPose)
            {
                return false;
            }

            var pose = node.PlacementPose;
            if (node.Kind == MachineNodeKind.BeltModule)
            {
                element = new LineElement(
                    node,
                    new Endpoint(
                        OppositeYaw(pose.YawQuarterTurns),
                        0),
                    new Endpoint(
                        BeltModuleShape.ForwardExitYaw(
                            node.DefinitionId,
                            pose.YawQuarterTurns),
                        BeltModuleShape.RiseMillimetres(node.DefinitionId)));
                return true;
            }

            if (node.Kind == MachineNodeKind.Machine
                && node.DefinitionId == ContentIds.MechanicalPress)
            {
                element = new LineElement(
                    node,
                    new Endpoint(OppositeYaw(pose.YawQuarterTurns), 0),
                    new Endpoint(pose.YawQuarterTurns, 0));
                return true;
            }

            return false;
        }

        private static Dictionary<StableId, List<Connection>> BuildAdjacency(
            IReadOnlyDictionary<StableId, LineElement> elements)
        {
            var ids = new List<StableId>(elements.Keys);
            ids.Sort();
            var adjacency = new Dictionary<StableId, List<Connection>>();
            for (var leftIndex = 0; leftIndex < ids.Count; leftIndex++)
            {
                var left = elements[ids[leftIndex]];
                for (var rightIndex = leftIndex + 1; rightIndex < ids.Count; rightIndex++)
                {
                    var right = elements[ids[rightIndex]];
                    if (!TryConnect(left, right, out var leftEndpoint, out var rightEndpoint))
                    {
                        continue;
                    }

                    AddConnection(
                        adjacency,
                        left.Node.Id,
                        new Connection(right.Node.Id, leftEndpoint, rightEndpoint));
                    AddConnection(
                        adjacency,
                        right.Node.Id,
                        new Connection(left.Node.Id, rightEndpoint, leftEndpoint));
                }
            }

            return adjacency;
        }

        private static bool TryConnect(
            LineElement left,
            LineElement right,
            out byte leftEndpoint,
            out byte rightEndpoint)
        {
            leftEndpoint = 0;
            rightEndpoint = 0;
            for (byte leftIndex = 0; leftIndex < 2; leftIndex++)
            {
                var leftPort = left.GetEndpoint(leftIndex);
                for (byte rightIndex = 0; rightIndex < 2; rightIndex++)
                {
                    var rightPort = right.GetEndpoint(rightIndex);
                    if (OppositeYaw(leftPort.SideYaw) != rightPort.SideYaw
                        || !FacesNode(left, leftPort, right)
                        || !FacesNode(right, rightPort, left)
                        || checked(
                                left.Node.PlacementPose.YMillimetres
                                + leftPort.HeightMillimetres)
                            != checked(
                                right.Node.PlacementPose.YMillimetres
                                + rightPort.HeightMillimetres))
                    {
                        continue;
                    }

                    leftEndpoint = leftIndex;
                    rightEndpoint = rightIndex;
                    return true;
                }
            }

            return false;
        }

        private static bool FacesNode(
            LineElement origin,
            Endpoint endpoint,
            LineElement target)
        {
            var x = origin.Node.PlacementPose.XMillimetres;
            var z = origin.Node.PlacementPose.ZMillimetres;
            Offset(endpoint.SideYaw, ref x, ref z);
            return x == target.Node.PlacementPose.XMillimetres
                && z == target.Node.PlacementPose.ZMillimetres;
        }

        private static List<LineElement> CollectComponent(
            StableId start,
            IReadOnlyDictionary<StableId, LineElement> elements,
            IReadOnlyDictionary<StableId, List<Connection>> adjacency,
            ISet<StableId> visited)
        {
            var result = new List<LineElement>();
            var queue = new Queue<StableId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                result.Add(elements[id]);
                if (!adjacency.TryGetValue(id, out var connections))
                {
                    continue;
                }

                connections.Sort((left, right) => left.NeighbourId.CompareTo(right.NeighbourId));
                for (var index = 0; index < connections.Count; index++)
                {
                    var neighbour = connections[index].NeighbourId;
                    if (visited.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            return result;
        }

        private static void ResolveComponent(
            IReadOnlyList<LineElement> component,
            IReadOnlyDictionary<StableId, List<Connection>> adjacency)
        {
            var drives = 0;
            var usedCapacity = 0;
            var directions = new Dictionary<StableId, BeltTravelDirection>();
            var queue = new Queue<StableId>();
            var conflict = false;

            for (var index = 0; index < component.Count; index++)
            {
                var node = component[index].Node;
                if (IsDrive(node))
                {
                    drives++;
                    conflict |= !Assign(
                        node.Id,
                        BeltTravelDirection.Forward,
                        directions,
                        queue);
                    continue;
                }

                usedCapacity = checked(usedCapacity + 1);
                if (node.Kind == MachineNodeKind.Machine)
                {
                    // The press owns fixed input/output ports. It does not supply motion,
                    // but it constrains the valid direction through that point.
                    conflict |= !Assign(
                        node.Id,
                        BeltTravelDirection.Forward,
                        directions,
                        queue);
                }
            }

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                if (!adjacency.TryGetValue(currentId, out var connections))
                {
                    continue;
                }

                var currentDirection = directions[currentId];
                for (var index = 0; index < connections.Count; index++)
                {
                    var connection = connections[index];
                    var currentExitEndpoint =
                        currentDirection == BeltTravelDirection.Forward ? 1 : 0;
                    var neighbourMustEnter =
                        connection.CurrentEndpoint == currentExitEndpoint;
                    var neighbourDirection =
                        connection.NeighbourEndpoint == (neighbourMustEnter ? 0 : 1)
                            ? BeltTravelDirection.Forward
                            : BeltTravelDirection.Reverse;
                    if (!Assign(
                            connection.NeighbourId,
                            neighbourDirection,
                            directions,
                            queue))
                    {
                        conflict = true;
                    }
                }
            }

            var availableCapacity = checked(drives * CapacityPerDrive);
            var status = drives == 0
                ? BeltLineStatus.MissingDrive
                : conflict
                    ? BeltLineStatus.DirectionConflict
                    : usedCapacity > availableCapacity
                        ? BeltLineStatus.Overloaded
                        : BeltLineStatus.Operational;

            for (var index = 0; index < component.Count; index++)
            {
                var node = component[index].Node;
                node.BeltLineStatus = status;
                node.BeltLineUsedCapacity = usedCapacity;
                node.BeltLineAvailableCapacity = availableCapacity;
                if (node.Kind == MachineNodeKind.BeltModule
                    && status == BeltLineStatus.Operational
                    && directions.TryGetValue(node.Id, out var direction))
                {
                    node.BeltTravelDirection = direction;
                }
            }
        }

        private static bool Assign(
            StableId id,
            BeltTravelDirection direction,
            IDictionary<StableId, BeltTravelDirection> directions,
            Queue<StableId> queue)
        {
            if (directions.TryGetValue(id, out var existing))
            {
                return existing == direction;
            }

            directions.Add(id, direction);
            queue.Enqueue(id);
            return true;
        }

        private static bool IsDrive(MachineNodeState node) =>
            node.Kind == MachineNodeKind.BeltModule
            && node.DefinitionId == ContentIds.BeltDriveUnit;

        private static void Reset(MachineNodeState node)
        {
            node.BeltLineStatus = BeltLineStatus.NotApplicable;
            node.BeltLineUsedCapacity = 0;
            node.BeltLineAvailableCapacity = 0;
            if (node.Kind == MachineNodeKind.BeltModule)
            {
                node.BeltTravelDirection = BeltTravelDirection.Stopped;
                node.BeltLineStatus = BeltLineStatus.MissingDrive;
            }
        }

        private static void AddConnection(
            IDictionary<StableId, List<Connection>> adjacency,
            StableId nodeId,
            Connection connection)
        {
            if (!adjacency.TryGetValue(nodeId, out var connections))
            {
                connections = new List<Connection>();
                adjacency.Add(nodeId, connections);
            }

            connections.Add(connection);
        }

        private static void Offset(byte yaw, ref int x, ref int z)
        {
            switch (yaw & 3)
            {
                case 0:
                    z = checked(z + MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
                case 1:
                    x = checked(x + MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
                case 2:
                    z = checked(z - MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
                case 3:
                    x = checked(x - MachineSpatialTopology.GridCellSizeMillimetres);
                    break;
            }
        }

        private static byte OppositeYaw(byte yaw) => (byte)((yaw + 2) & 3);

        private readonly struct Endpoint
        {
            public Endpoint(byte sideYaw, int heightMillimetres)
            {
                SideYaw = sideYaw;
                HeightMillimetres = heightMillimetres;
            }

            public byte SideYaw { get; }
            public int HeightMillimetres { get; }
        }

        private readonly struct LineElement
        {
            public LineElement(
                MachineNodeState node,
                Endpoint first,
                Endpoint second)
            {
                Node = node;
                First = first;
                Second = second;
            }

            public MachineNodeState Node { get; }
            public Endpoint First { get; }
            public Endpoint Second { get; }

            public Endpoint GetEndpoint(byte index) => index == 0 ? First : Second;
        }

        private readonly struct Connection
        {
            public Connection(
                StableId neighbourId,
                byte currentEndpoint,
                byte neighbourEndpoint)
            {
                NeighbourId = neighbourId;
                CurrentEndpoint = currentEndpoint;
                NeighbourEndpoint = neighbourEndpoint;
            }

            public StableId NeighbourId { get; }
            public byte CurrentEndpoint { get; }
            public byte NeighbourEndpoint { get; }
        }
    }

    internal sealed class BeltLineTopologyPhaseSystem : ISimulationPhaseSystem
    {
        public SimulationPhase Phase => SimulationPhase.LocalTopologyChanges;
        public int Order => 900;
        public StableId StableOrderId =>
            new StableId(0x42454C545F4C494EUL, 0x4500000000000001UL);

        public void Execute(SimulationPhaseContext context)
        {
            BeltLineRules.Recompute(context.GetMachineMutable());
        }
    }
}

using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Resolves the physical logistics graph from persistent node poses.
    ///
    /// No edge is authored or stored. A belt or funnel works only while the required
    /// neighbours occupy the exact adjacent cells with compatible facings. Removing
    /// any physical module therefore disconnects the line on the next authoritative
    /// tick without leaving a stale logical connection behind.
    /// </summary>
    public static class MachineSpatialTopology
    {
        public const int GridCellSizeMillimetres = 1000;
        public const int BeltLengthMillimetres = GridCellSizeMillimetres;
        public const int BeltSpeedMillimetresPerTick = 100;

        private static readonly NonNegativeQuantity One = new NonNegativeQuantity(1L);

        internal static void Advance(MachineSimulationState state, GameCatalog catalog)
        {
            var nodes = new SpatialNodeIndex(state);
            PullExtractingFunnels(state, nodes, catalog);
            AdvanceBelts(state, nodes);
            DeliverBelts(state, nodes, catalog);
            LoadBelts(state, nodes, catalog);
            PushInsertingFunnels(state, nodes, catalog);
        }

        private static void PullExtractingFunnels(
            MachineSimulationState state,
            SpatialNodeIndex nodes,
            GameCatalog catalog)
        {
            foreach (var pair in state.Nodes)
            {
                var funnel = pair.Value;
                if (!TryResolveFunnel(
                        funnel,
                        nodes,
                        out var buffer,
                        out _,
                        out var direction)
                    || direction != FunnelDirection.Extract
                    || !funnel.Input.IsEmpty
                    || !buffer.Output.TryPeekFirstItem(out var itemId))
                {
                    continue;
                }

                MoveOne(
                    buffer.Output,
                    funnel.Input,
                    itemId,
                    catalog,
                    $"extracting funnel {funnel.Id}");
            }
        }

        private static void AdvanceBelts(
            MachineSimulationState state,
            SpatialNodeIndex nodes)
        {
            foreach (var pair in state.Nodes)
            {
                var belt = pair.Value;
                if (belt.Kind != MachineNodeKind.BeltModule
                    || !belt.HasPlacementPose
                    || belt.BeltTravelDirection == BeltTravelDirection.Stopped
                    || belt.Input.IsEmpty
                    || !TryResolveBeltDestination(belt, nodes, out _))
                {
                    continue;
                }

                belt.TransportProgressMillimetres = Math.Min(
                    BeltLengthMillimetres,
                    checked(
                        belt.TransportProgressMillimetres
                        + BeltSpeedMillimetresPerTick));
            }
        }

        private static void DeliverBelts(
            MachineSimulationState state,
            SpatialNodeIndex nodes,
            GameCatalog catalog)
        {
            // Nodes are a SortedDictionary, so simultaneous deliveries are resolved by
            // StableId rather than by construction order or hash-map iteration.
            foreach (var pair in state.Nodes)
            {
                var belt = pair.Value;
                if (belt.Kind != MachineNodeKind.BeltModule
                    || belt.TransportProgressMillimetres < BeltLengthMillimetres
                    || !belt.Input.TryPeekFirstItem(out var itemId)
                    || !TryResolveBeltDestination(belt, nodes, out var destination))
                {
                    continue;
                }

                MachinePort destinationPort;
                switch (destination.Kind)
                {
                    case MachineNodeKind.BeltModule:
                        if (!destination.Input.IsEmpty)
                        {
                            continue;
                        }

                        destinationPort = destination.Input;
                        break;

                    case MachineNodeKind.Machine:
                        if (!MachineAdmits(destination, itemId, catalog))
                        {
                            continue;
                        }

                        destinationPort = destination.Input;
                        break;

                    case MachineNodeKind.Funnel:
                        if (!destination.Input.IsEmpty
                            || !TryResolveFunnel(
                                destination,
                                nodes,
                                out _,
                                out _,
                                out var direction)
                            || direction != FunnelDirection.Insert)
                        {
                            continue;
                        }

                        destinationPort = destination.Input;
                        break;

                    default:
                        continue;
                }

                if (!MoveOne(
                        belt.Output,
                        destinationPort,
                        itemId,
                        catalog,
                        $"belt {belt.Id} delivery"))
                {
                    continue;
                }

                belt.TransportProgressMillimetres = 0;
                if (destination.Kind == MachineNodeKind.BeltModule)
                {
                    destination.TransportProgressMillimetres = 0;
                }
            }
        }

        private static void LoadBelts(
            MachineSimulationState state,
            SpatialNodeIndex nodes,
            GameCatalog catalog)
        {
            foreach (var pair in state.Nodes)
            {
                var belt = pair.Value;
                if (belt.Kind != MachineNodeKind.BeltModule
                    || !belt.HasPlacementPose
                    || !belt.Input.IsEmpty
                    || !nodes.TryGetTravelBack(belt, out var source))
                {
                    continue;
                }

                switch (source.Kind)
                {
                    case MachineNodeKind.Funnel:
                        if (!TryResolveFunnel(
                                source,
                                nodes,
                                out _,
                                out var connectedBelt,
                                out var direction)
                            || direction != FunnelDirection.Extract
                            || connectedBelt.Id != belt.Id)
                        {
                            continue;
                        }

                        break;

                    case MachineNodeKind.Machine:
                        if (!TravelFaces(belt, source.PlacementPose.YawQuarterTurns))
                        {
                            continue;
                        }

                        break;

                    default:
                        // Belt-to-belt transfer was already handled by DeliverBelts.
                        // Buffers never load a belt directly.
                        continue;
                }

                if (!source.Output.TryPeekFirstItem(out var itemId)
                    || !MoveOne(
                        source.Output,
                        belt.Input,
                        itemId,
                        catalog,
                        $"belt {belt.Id} loading"))
                {
                    continue;
                }

                belt.TransportProgressMillimetres = 0;
            }
        }

        private static void PushInsertingFunnels(
            MachineSimulationState state,
            SpatialNodeIndex nodes,
            GameCatalog catalog)
        {
            foreach (var pair in state.Nodes)
            {
                var funnel = pair.Value;
                if (!TryResolveFunnel(
                        funnel,
                        nodes,
                        out var buffer,
                        out _,
                        out var direction)
                    || direction != FunnelDirection.Insert
                    || !funnel.Output.TryPeekFirstItem(out var itemId))
                {
                    continue;
                }

                MoveOne(
                    funnel.Output,
                    buffer.Input,
                    itemId,
                    catalog,
                    $"inserting funnel {funnel.Id}");
            }
        }

        /// <summary>
        /// What may sit behind a funnel.
        ///
        /// Until now the answer was "a crate, and nothing else", which is why a
        /// funnel placed against a machine stayed inert. A machine's output port
        /// is an output port like any other and the transfer rule never cared
        /// what kind of node it belonged to, so the restriction lived only here.
        ///
        /// It is widened by machine, not to machines in general, because the
        /// funnel physically clamps into a recessed seat and only a model that
        /// authors that seat can receive it. The drill has one — the socket at
        /// 0.4375 m is part of its geometry. The furnace is left out
        /// deliberately: its model has to be revised first, so this is a content
        /// decision and not a technical limit.
        /// </summary>
        private static bool CanFunnelAttachTo(MachineNodeState node)
        {
            if (node.Kind == MachineNodeKind.Buffer)
            {
                return true;
            }

            return node.Kind == MachineNodeKind.Machine
                && node.DefinitionId == ContentIds.MechanicalDrill;
        }

        private static bool TryResolveFunnel(
            MachineNodeState funnel,
            SpatialNodeIndex nodes,
            out MachineNodeState attached,
            out MachineNodeState belt,
            out FunnelDirection direction)
        {
            attached = null;
            belt = null;
            direction = FunnelDirection.None;

            if (funnel.Kind != MachineNodeKind.Funnel
                || !funnel.HasPlacementPose
                || !funnel.AttachedNodeId.IsNone
                || !nodes.TryGetBack(funnel, out attached)
                || !CanFunnelAttachTo(attached)
                || !nodes.TryGetFront(funnel, out belt)
                || belt.Kind != MachineNodeKind.BeltModule)
            {
                return false;
            }

            // Qui c'era un controllo aggiuntivo che pretendeva la macchina
            // orientata con la propria cella "dietro" occupata dall'Imbuto. Era
            // dedotto per ragionamento sulle convenzioni degli assi invece che
            // letto, e reggeva soltanto se Imbuto e macchina avevano rotazioni
            // opposte: con rotazioni uguali rifiutava l'aggancio corretto e la
            // Trivella non scaricava.
            //
            // Il lato da cui l'Imbuto può agganciarsi è già vincolato al
            // piazzamento da ArePortsAdjacent/IsBehind, quindi il controllo non
            // aggiungeva una garanzia: aggiungeva solo un modo di sbagliare.

            if (TravelFaces(belt, funnel.PlacementPose.YawQuarterTurns))
            {
                direction = FunnelDirection.Extract;
                return true;
            }

            if (TravelFaces(
                    belt,
                    OppositeYaw(funnel.PlacementPose.YawQuarterTurns)))
            {
                direction = FunnelDirection.Insert;
                return true;
            }

            return false;
        }

        private static bool TryResolveBeltDestination(
            MachineNodeState belt,
            SpatialNodeIndex nodes,
            out MachineNodeState destination)
        {
            destination = null;
            if (belt.Kind != MachineNodeKind.BeltModule
                || !belt.HasPlacementPose
                || belt.BeltTravelDirection == BeltTravelDirection.Stopped
                || !nodes.TryGetTravelFront(belt, out destination))
            {
                return false;
            }

            switch (destination.Kind)
            {
                case MachineNodeKind.BeltModule:
                    return SameTravelDirection(belt, destination);

                case MachineNodeKind.Machine:
                    return TravelFaces(
                        belt,
                        destination.PlacementPose.YawQuarterTurns);

                case MachineNodeKind.Funnel:
                    return TryResolveFunnel(
                            destination,
                            nodes,
                            out _,
                            out var connectedBelt,
                            out var direction)
                        && direction == FunnelDirection.Insert
                        && connectedBelt.Id == belt.Id;

                default:
                    // In particular, Buffer is deliberately absent: inventories are
                    // crossed only by a physical Funnel node.
                    return false;
            }
        }

        private static bool MachineAdmits(
            MachineNodeState machine,
            StableId itemId,
            GameCatalog catalog)
        {
            if (machine.Kind != MachineNodeKind.Machine
                || itemId.IsNone
                || machine.IsCycleActive
                || !machine.Output.IsEmpty
                || machine.ActiveRecipeId.IsNone
                || !catalog.TryGetRecipe(machine.ActiveRecipeId, out var recipe)
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
                || !catalog.TryGetMachine(
                    machine.DefinitionId,
                    out var definition))
            {
                return false;
            }

            return machine.Input.Count(itemId).Value
                    < definition.InputBufferCapacityPerItem
                && machine.Input.StorableQuantity(itemId, catalog).Value > 0L;
        }

        private static bool MoveOne(
            MachinePort source,
            MachinePort destination,
            StableId itemId,
            GameCatalog catalog,
            string operation)
        {
            if (itemId.IsNone
                || source.Count(itemId).Value < 1L
                || destination.StorableQuantity(itemId, catalog).Value < 1L)
            {
                return false;
            }

            if (!destination.TryStore(itemId, One, catalog))
            {
                throw new SimulationInvariantException(
                    $"{operation} measured room for {itemId} and was then refused.");
            }

            if (!source.TryTake(itemId, One))
            {
                throw new SimulationInvariantException(
                    $"{operation} stored {itemId} that its source did not hold.");
            }

            return true;
        }

        /// <summary>
        /// Il pezzo passa se l'uscita di monte guarda dove guarda l'ingresso di
        /// valle. Prima si confrontavano le due marce, il che equivaleva a
        /// pretendere che nessuno dei due svoltasse.
        /// </summary>
        private static bool SameTravelDirection(
            MachineNodeState left,
            MachineNodeState right) =>
            TryGetBeltExitYaw(left, out var leftExit)
            && TryGetBeltTravelYaw(right, out var rightEntry)
            && leftExit == rightEntry;

        private static bool TravelFaces(MachineNodeState belt, byte yaw) =>
            TryGetBeltTravelYaw(belt, out var travelYaw)
            && travelYaw == yaw;

        /// <summary>
        /// Resolves world-space travel yaw from the belt's geometric yaw and the
        /// independent polarity published by the Belt Drive on its line.
        /// </summary>
        public static bool TryGetBeltTravelYaw(
            MachineNodeState belt,
            out byte travelYaw)
        {
            travelYaw = 0;
            if (belt == null
                || belt.Kind != MachineNodeKind.BeltModule
                || !belt.HasPlacementPose
                || belt.BeltTravelDirection == BeltTravelDirection.Stopped)
            {
                return false;
            }

            return BeltModuleShape.TryResolveTravelYaws(
                belt.DefinitionId,
                belt.PlacementPose.YawQuarterTurns,
                belt.BeltTravelDirection,
                out travelYaw,
                out _);
        }

        /// <summary>
        /// Direzione con cui il pezzo lascia il modulo.
        ///
        /// Su un rettilineo coincide con quella d'ingresso, ed è per questo che
        /// finora bastava una sola nozione di "marcia". Su una Curva no: è la
        /// distinzione fra le due che permette al percorso di svoltare.
        /// </summary>
        public static bool TryGetBeltExitYaw(
            MachineNodeState belt,
            out byte exitYaw)
        {
            exitYaw = 0;
            if (belt == null
                || belt.Kind != MachineNodeKind.BeltModule
                || !belt.HasPlacementPose)
            {
                return false;
            }

            return BeltModuleShape.TryResolveTravelYaws(
                belt.DefinitionId,
                belt.PlacementPose.YawQuarterTurns,
                belt.BeltTravelDirection,
                out _,
                out exitYaw);
        }

        /// <summary>
        /// Dislivello che il modulo aggiunge al percorso, col segno della marcia:
        /// una Salita percorsa al contrario scende.
        /// </summary>
        public static int BeltRiseMillimetres(MachineNodeState belt)
        {
            if (belt == null || belt.Kind != MachineNodeKind.BeltModule)
            {
                return 0;
            }

            var rise = BeltModuleShape.RiseMillimetres(belt.DefinitionId);
            if (rise == 0)
            {
                return 0;
            }

            return belt.BeltTravelDirection == BeltTravelDirection.Forward
                ? rise
                : -rise;
        }

        private static byte OppositeYaw(byte yaw) => (byte)((yaw + 2) & 3);

        private enum FunnelDirection : byte
        {
            None = 0,
            Extract,
            Insert,
        }

        private readonly struct Cell : IEquatable<Cell>
        {
            public Cell(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public int X { get; }
            public int Y { get; }
            public int Z { get; }

            public bool Equals(Cell other) => X == other.X && Y == other.Y && Z == other.Z;

            public override bool Equals(object obj) => obj is Cell other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((X * 397) ^ Y) * 397 ^ Z;
                }
            }
        }

        private sealed class SpatialNodeIndex
        {
            private readonly Dictionary<Cell, MachineNodeState> _nodes =
                new Dictionary<Cell, MachineNodeState>();

            public SpatialNodeIndex(MachineSimulationState state)
            {
                foreach (var pair in state.Nodes)
                {
                    var node = pair.Value;
                    if (!node.HasPlacementPose)
                    {
                        continue;
                    }

                    var pose = node.PlacementPose;
                    _nodes.Add(
                        new Cell(
                            pose.XMillimetres,
                            pose.YMillimetres,
                            pose.ZMillimetres),
                        node);
                }
            }

            public bool TryGetFront(
                MachineNodeState node,
                out MachineNodeState neighbour) =>
                TryGetOffset(node, 1, out neighbour);

            public bool TryGetBack(
                MachineNodeState node,
                out MachineNodeState neighbour) =>
                TryGetOffset(node, -1, out neighbour);

            public bool TryGetTravelFront(
                MachineNodeState belt,
                out MachineNodeState neighbour)
            {
                neighbour = null;
                // Si esce dal lato d'uscita e alla quota d'uscita: su una Curva
                // il primo differisce dall'ingresso, su una Salita il secondo.
                if (!TryGetBeltExitYaw(belt, out var exitYaw))
                {
                    return false;
                }

                return TryGetConnectedOffset(
                    belt,
                    exitYaw,
                    out neighbour);
            }

            public bool TryGetTravelBack(
                MachineNodeState belt,
                out MachineNodeState neighbour)
            {
                neighbour = null;
                if (!TryGetBeltTravelYaw(belt, out var travelYaw))
                {
                    return false;
                }

                // A monte si torna dal lato fisico opposto alla marcia. La sua
                // quota può essere quella alta di una Salita percorsa al
                // contrario, quindi non può essere dedotta dal solo segno.
                return TryGetConnectedOffset(
                    belt,
                    OppositeYaw(travelYaw),
                    out neighbour);
            }

            /// <summary>
            /// Cerca il vicino nella cella indicata e confronta la quota delle
            /// due estremità fisiche. Il vecchio indice cercava soltanto la
            /// radice a Y + rise: funzionava salendo, ma non poteva rappresentare
            /// una Salita ruotata la cui radice è più bassa del modulo a monte.
            /// </summary>
            private bool TryGetConnectedOffset(
                MachineNodeState node,
                byte sideYaw,
                out MachineNodeState neighbour)
            {
                neighbour = null;
                if (node == null || !node.HasPlacementPose)
                {
                    return false;
                }

                var pose = node.PlacementPose;
                var x = pose.XMillimetres;
                var z = pose.ZMillimetres;
                switch (sideYaw & 3)
                {
                    case 0:
                        z = checked(z + GridCellSizeMillimetres);
                        break;
                    case 1:
                        x = checked(x + GridCellSizeMillimetres);
                        break;
                    case 2:
                        z = checked(z - GridCellSizeMillimetres);
                        break;
                    case 3:
                        x = checked(x - GridCellSizeMillimetres);
                        break;
                }

                var nodeEndpointHeight = 0;
                if (node.Kind == MachineNodeKind.BeltModule
                    && !BeltModuleShape.TryGetEndpointHeightMillimetres(
                        node.DefinitionId,
                        pose.YawQuarterTurns,
                        sideYaw,
                        out nodeEndpointHeight))
                {
                    return false;
                }

                var contactHeight = checked(
                    pose.YMillimetres + nodeEndpointHeight);
                foreach (var candidate in _nodes.Values)
                {
                    if (ReferenceEquals(candidate, node)
                        || !candidate.HasPlacementPose)
                    {
                        continue;
                    }

                    var candidatePose = candidate.PlacementPose;
                    if (candidatePose.XMillimetres != x
                        || candidatePose.ZMillimetres != z)
                    {
                        continue;
                    }

                    var candidateEndpointHeight = 0;
                    if (candidate.Kind == MachineNodeKind.BeltModule
                        && !BeltModuleShape.TryGetEndpointHeightMillimetres(
                            candidate.DefinitionId,
                            candidatePose.YawQuarterTurns,
                            OppositeYaw(sideYaw),
                            out candidateEndpointHeight))
                    {
                        continue;
                    }

                    if (checked(
                            candidatePose.YMillimetres
                            + candidateEndpointHeight)
                        != contactHeight)
                    {
                        continue;
                    }

                    neighbour = candidate;
                    return true;
                }

                return false;
            }

            private bool TryGetOffset(
                MachineNodeState node,
                int sign,
                out MachineNodeState neighbour)
            {
                neighbour = null;
                if (!node.HasPlacementPose)
                {
                    return false;
                }

                var pose = node.PlacementPose;
                return TryGetOffset(
                    node,
                    pose.YawQuarterTurns,
                    sign,
                    0,
                    out neighbour);
            }

            private bool TryGetOffset(
                MachineNodeState node,
                byte yaw,
                int sign,
                int riseMillimetres,
                out MachineNodeState neighbour)
            {
                neighbour = null;
                if (!node.HasPlacementPose)
                {
                    return false;
                }

                var pose = node.PlacementPose;
                var x = pose.XMillimetres;
                var z = pose.ZMillimetres;
                switch (yaw)
                {
                    case 0:
                        z = checked(z + sign * GridCellSizeMillimetres);
                        break;
                    case 1:
                        x = checked(x + sign * GridCellSizeMillimetres);
                        break;
                    case 2:
                        z = checked(z - sign * GridCellSizeMillimetres);
                        break;
                    case 3:
                        x = checked(x - sign * GridCellSizeMillimetres);
                        break;
                    default:
                        throw new SimulationInvariantException(
                            $"Node {node.Id} has invalid quarter-turn facing "
                            + $"{yaw}.");
                }

                return _nodes.TryGetValue(
                    new Cell(
                        x,
                        checked(pose.YMillimetres + riseMillimetres),
                        z),
                    out neighbour);
            }
        }
    }
}

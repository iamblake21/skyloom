using System;
using System.Collections.Generic;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// One item riding a belt. Its position is millimetres from the entry, so the whole
    /// system is integer arithmetic and two machines cannot disagree about where it is.
    /// </summary>
    [Serializable]
    public readonly struct BeltItemState : IEquatable<BeltItemState>
    {
        public BeltItemState(StableId itemId, int positionMillimetres)
        {
            if (itemId.IsNone)
            {
                throw new ArgumentException("An item on a belt needs an identity.", nameof(itemId));
            }

            if (positionMillimetres < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(positionMillimetres));
            }

            ItemId = itemId;
            PositionMillimetres = positionMillimetres;
        }

        public StableId ItemId { get; }

        public int PositionMillimetres { get; }

        public BeltItemState MovedTo(int positionMillimetres)
        {
            return new BeltItemState(ItemId, positionMillimetres);
        }

        public bool Equals(BeltItemState other)
        {
            return ItemId == other.ItemId
                && PositionMillimetres == other.PositionMillimetres;
        }

        public override bool Equals(object obj) => obj is BeltItemState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (ItemId.GetHashCode() * 397) ^ PositionMillimetres;
            }
        }
    }

    /// <summary>
    /// A belt lane: it takes items from the source node's output port, carries them along
    /// its length, and hands them to the destination node's input port.
    ///
    /// This is what the logical link of MACH-001 was standing in for. The difference is
    /// not decoration. A link moved a quantity instantly, so a machine downstream saw its
    /// input the same tick the upstream produced it; a lane has length, so there is
    /// latency, and a fixed spacing between items, so there is a real throughput ceiling.
    /// It also means backpressure is visible: when the destination refuses, items pile up
    /// from the far end backwards, and the lane stops accepting at the entry when the
    /// queue reaches it. On a link, backpressure was a number that simply stopped moving.
    ///
    /// Items are ordered front first: index 0 is nearest the exit. Every rule reads that
    /// order, so nothing depends on how the collection happens to be traversed.
    /// </summary>
    [Serializable]
    public sealed class BeltLaneState
    {
        private readonly List<BeltItemState> _items = new List<BeltItemState>();

        public BeltLaneState(
            StableId id,
            StableId sourceNodeId,
            StableId destinationNodeId,
            StableId itemFilter,
            int lengthMillimetres,
            int speedMillimetresPerTick,
            int spacingMillimetres)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("A lane requires a stable id.", nameof(id));
            }

            if (sourceNodeId.IsNone || destinationNodeId.IsNone)
            {
                throw new ArgumentException("A lane requires both endpoints.");
            }

            if (sourceNodeId == destinationNodeId)
            {
                throw new ArgumentException("A lane cannot join a node to itself.");
            }

            if (lengthMillimetres <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lengthMillimetres),
                    lengthMillimetres,
                    "A lane with no length is a logical link, not a belt.");
            }

            if (speedMillimetresPerTick <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(speedMillimetresPerTick));
            }

            if (spacingMillimetres <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spacingMillimetres),
                    spacingMillimetres,
                    "Items need a positive spacing, or a lane would hold infinitely many.");
            }

            Id = id;
            SourceNodeId = sourceNodeId;
            DestinationNodeId = destinationNodeId;
            ItemFilter = itemFilter;
            LengthMillimetres = lengthMillimetres;
            SpeedMillimetresPerTick = speedMillimetresPerTick;
            SpacingMillimetres = spacingMillimetres;
        }

        public StableId Id { get; }

        public StableId SourceNodeId { get; }

        public StableId DestinationNodeId { get; }

        /// <summary>The only item this lane carries, or <c>None</c> to carry anything.</summary>
        public StableId ItemFilter { get; }

        public int LengthMillimetres { get; }

        public int SpeedMillimetresPerTick { get; }

        /// <summary>Minimum distance between two items, which sets the throughput ceiling.</summary>
        public int SpacingMillimetres { get; }

        /// <summary>Front first: index 0 is the item nearest the exit.</summary>
        public IReadOnlyList<BeltItemState> Items => _items;

        public int ItemCount => _items.Count;

        /// <summary>Units handed to the destination. Measurable, not future-affecting.</summary>
        public ulong DeliveredUnits { get; internal set; }

        /// <summary>
        /// Ticks an unobstructed item needs to cross, rounded up: the last partial step
        /// still costs a whole tick, because a tick is the smallest thing that happens.
        /// </summary>
        public int LatencyTicks =>
            (LengthMillimetres + SpeedMillimetresPerTick - 1) / SpeedMillimetresPerTick;

        /// <summary>
        /// Items per thousand ticks the lane can sustain.
        ///
        /// One item is loaded per tick at most, and the next can only be loaded once the
        /// previous has cleared the spacing, which takes ceil(spacing / speed) ticks. So
        /// the ceiling is the reciprocal of that, and it has nothing to do with length —
        /// length buys latency, not throughput.
        /// </summary>
        public int ThroughputPerThousandTicks
        {
            get
            {
                var ticksPerItem =
                    (SpacingMillimetres + SpeedMillimetresPerTick - 1)
                    / SpeedMillimetresPerTick;
                return 1000 / Math.Max(1, ticksPerItem);
            }
        }

        internal void AddAtEntry(StableId itemId)
        {
            _items.Add(new BeltItemState(itemId, 0));
        }

        internal void SetItemAt(int index, BeltItemState item)
        {
            _items[index] = item;
        }

        internal void RemoveFront()
        {
            _items.RemoveAt(0);
        }

        /// <summary>
        /// Whether a new item fits at the entry. The last item in the list is the one
        /// nearest the entry, so it is the only one that can be in the way.
        /// </summary>
        internal bool HasRoomAtEntry()
        {
            if (_items.Count == 0)
            {
                return true;
            }

            return _items[_items.Count - 1].PositionMillimetres >= SpacingMillimetres;
        }

        public BeltLaneState DeepClone()
        {
            var clone = new BeltLaneState(
                Id,
                SourceNodeId,
                DestinationNodeId,
                ItemFilter,
                LengthMillimetres,
                SpeedMillimetresPerTick,
                SpacingMillimetres)
            {
                DeliveredUnits = DeliveredUnits,
            };
            clone._items.AddRange(_items);
            return clone;
        }

        internal void ValidateInvariants()
        {
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                if (item.PositionMillimetres > LengthMillimetres)
                {
                    throw new SimulationInvariantException(
                        $"Lane {Id} holds an item at {item.PositionMillimetres} mm, past its "
                        + $"{LengthMillimetres} mm.");
                }

                if (index == 0)
                {
                    continue;
                }

                // Front first, so each item must sit strictly behind the one before it and
                // no closer than the spacing. A violation here means two items overlap,
                // which would let the lane carry more than it declares.
                var ahead = _items[index - 1];
                if (ahead.PositionMillimetres - item.PositionMillimetres < SpacingMillimetres)
                {
                    throw new SimulationInvariantException(
                        $"Lane {Id} holds items {index - 1} and {index} "
                        + $"{ahead.PositionMillimetres - item.PositionMillimetres} mm apart, "
                        + $"below its {SpacingMillimetres} mm spacing.");
                }
            }
        }
    }
}

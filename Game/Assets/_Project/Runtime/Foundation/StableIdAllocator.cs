using System;

namespace CML.Foundation
{
    /// <summary>
    /// Monotonic 128-bit ID allocator. Zero is reserved and IDs are never reused.
    /// The exhausted flag is explicit so MaxValue can be allocated without wrap.
    /// </summary>
    [Serializable]
    public sealed class StableIdAllocator
    {
        public StableIdAllocator()
            : this(StableId.First, false)
        {
        }

        public StableIdAllocator(StableId nextId, bool isExhausted)
        {
            if (!isExhausted && nextId.IsNone)
            {
                throw new ArgumentException("The next persistent ID cannot be zero.", nameof(nextId));
            }

            if (isExhausted && nextId != StableId.MaxValue)
            {
                throw new ArgumentException(
                    "An exhausted persistent ID allocator must retain MaxValue as next_id.",
                    nameof(nextId));
            }

            NextId = nextId;
            IsExhausted = isExhausted;
        }

        public StableId NextId { get; private set; }

        public bool IsExhausted { get; private set; }

        public bool TryAllocate(out StableId allocated)
        {
            if (IsExhausted)
            {
                allocated = StableId.None;
                return false;
            }

            allocated = NextId;
            if (NextId == StableId.MaxValue)
            {
                IsExhausted = true;
                return true;
            }

            NextId = NextId.Low == ulong.MaxValue
                ? new StableId(checked(NextId.High + 1UL), 0UL)
                : new StableId(NextId.High, NextId.Low + 1UL);
            return true;
        }

        public StableId Allocate()
        {
            if (!TryAllocate(out var allocated))
            {
                throw new InvalidOperationException("RESOURCE_EXHAUSTED: the persistent ID space is exhausted.");
            }

            return allocated;
        }

        public StableIdAllocator Clone()
        {
            return new StableIdAllocator(NextId, IsExhausted);
        }
    }
}

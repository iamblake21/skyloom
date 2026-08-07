using System;
using UnityEngine;

namespace CML.Unity.Gathering
{
    /// <summary>
    /// Authoring identity for a source the player harvests with bare hands.
    ///
    /// Deliberately separate from <c>ManualMiningSourceIdentity</c>: that one
    /// describes targets struck with a tool, and every rule reached through it
    /// asks for a pickaxe, its durability and its required hit count. A fibre
    /// tuft has none of those, and folding it in would mean teaching the mining
    /// rule to accept an empty hand -- which is exactly the case it exists to
    /// refuse.
    ///
    /// This component owns identity and yield only. The authoritative commit
    /// lives in the pure rule, and the world reacts to it afterwards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandGatherSourceIdentity : MonoBehaviour
    {
        [SerializeField] private HandGatherSourceKind sourceKind =
            HandGatherSourceKind.WildFiberTuft;
        [SerializeField] private string sourceId = string.Empty;
        [SerializeField, Min(1)] private int yield = 2;

        private bool _committed;

        public HandGatherSourceKind SourceKind => sourceKind;

        public string SourceId => sourceId;

        /// <summary>
        /// Units produced by one gather. Gathering is instant: there is no
        /// duration to configure.
        /// </summary>
        public int Yield => Mathf.Max(1, yield);

        /// <summary>
        /// True once the reward has been committed. The world object survives
        /// for the frames it takes to despawn, and must not be harvestable in
        /// any of them.
        /// </summary>
        public bool IsCommitted => _committed;

        public bool CanBeGathered => !_committed && isActiveAndEnabled;

        public void Configure(
            HandGatherSourceKind kind,
            string stableSourceId,
            int units)
        {
            if (string.IsNullOrWhiteSpace(stableSourceId))
            {
                throw new ArgumentException(
                    "A gather source requires a stable identifier.",
                    nameof(stableSourceId));
            }

            if (units < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(units),
                    "A gather source must yield at least one unit.");
            }

            sourceKind = kind;
            sourceId = stableSourceId.Trim();
            yield = units;
        }

        /// <summary>
        /// Closes the source the instant the reward is committed, before Unity
        /// destroys the object. Without this a second interaction started in
        /// the same frame would harvest an already-paid tuft.
        /// </summary>
        public void MarkCommitted()
        {
            _committed = true;
            var colliders = GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }
    }

    public enum HandGatherSourceKind
    {
        WildFiberTuft = 0,
        FallenSticks = 1,
        LoosePebble = 2
    }
}

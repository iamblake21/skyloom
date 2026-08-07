using CML.Simulation.Gathering;
using CML.Unity.Factory;
using CML.Unity.Presentation.Inventory;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CML.Unity.Gathering
{
    /// <summary>
    /// Unity boundary for gathering with bare hands.
    ///
    /// It owns no input. The central interactor already resolves what the
    /// player is looking at and consumes the E edge, so a tuft is picked the
    /// same way a chest is opened: one press, one result. An earlier version
    /// held the key for two seconds to honour the canonical "two fibres in two
    /// seconds" rate; that rate describes throughput on paper and turned out to
    /// be a brake in the hand, so the wait is gone.
    ///
    /// Nothing here decides what a source is worth. The reward, the atomicity
    /// and the refusal on a full inventory live in <see cref="HandGatherRule"/>,
    /// and the world may only remove the object after that rule has published a
    /// new inventory.
    /// </summary>
    [DefaultExecutionOrder(330)]
    [DisallowMultipleComponent]
    public sealed class HandGatherController : MonoBehaviour
    {
        [SerializeField] private InventoryHudController inventoryHud;
        [SerializeField] private CollectionFeedHudController collectionFeedHud;
        [SerializeField] private FactoryHudOrchestrator hud;

        /// <summary>
        /// The one controller in the scene. Sources are scattered by the
        /// hundred and must not each carry a reference to it.
        /// </summary>
        public static HandGatherController Active { get; private set; }

        public void Configure(
            InventoryHudController hudController,
            FactoryHudOrchestrator hudOrchestrator)
        {
            inventoryHud = hudController;
            hud = hudOrchestrator;
        }

        private void OnEnable()
        {
            Active = this;
            ResolveDependencies();
        }

        private void OnDisable()
        {
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }
        }

        /// <summary>
        /// Commits one gather. Returns false and changes nothing when the
        /// source is spent, the session is not ready or the backpack is full.
        /// </summary>
        public bool TryGather(HandGatherSourceIdentity source)
        {
            if (source == null || !source.CanBeGathered)
            {
                return false;
            }

            ResolveDependencies();
            if (inventoryHud == null
                || inventoryHud.BoundState == null
                || inventoryHud.BoundCatalog == null)
            {
                return false;
            }

            if (hud != null && hud.AnyModalOpen)
            {
                return false;
            }

            var result = HandGatherRule.Gather(
                inventoryHud.BoundState,
                MapTarget(source.SourceKind),
                source.Yield);
            if (!result.Gathered)
            {
                // A full backpack leaves the source standing, so no matter is
                // produced and none is lost.
                return false;
            }

            inventoryHud.BindInventory(
                result.UpdatedInventory,
                inventoryHud.BoundCatalog);
            collectionFeedHud?.ShowCommittedCollection(
                result.ProducedItemId,
                result.ProducedQuantity,
                inventoryHud.BoundCatalog);

            // Close the source before the object dies: another press in the
            // same frame must not find it gatherable.
            source.MarkCommitted();
            Destroy(source.gameObject);
            return true;
        }

        private void ResolveDependencies()
        {
            if (inventoryHud == null)
            {
                inventoryHud =
                    Object.FindFirstObjectByType<InventoryHudController>();
            }

            if (collectionFeedHud == null && inventoryHud != null)
            {
                collectionFeedHud =
                    CollectionFeedHudController.EnsureFor(inventoryHud);
            }

            if (hud == null)
            {
                hud = Object.FindFirstObjectByType<FactoryHudOrchestrator>();
            }
        }

        private static HandGatherTargetKind MapTarget(
            HandGatherSourceKind sourceKind)
        {
            switch (sourceKind)
            {
                case HandGatherSourceKind.FallenSticks:
                    return HandGatherTargetKind.FallenSticks;

                case HandGatherSourceKind.LoosePebble:
                    return HandGatherTargetKind.LoosePebble;

                case HandGatherSourceKind.WildFiberTuft:
                default:
                    return HandGatherTargetKind.WildFiberTuft;
            }
        }
    }
}

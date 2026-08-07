using CML.Content;
using CML.Foundation;
using CML.Unity.Presentation.Inventory;
using UnityEngine;

namespace CML.Unity.Wood
{
    /// <summary>
    /// Commits WOOD-004 exactly once after the controlled fall has completed
    /// and the intact tree has remained visible for three seconds.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class FelledTreeRewardController : MonoBehaviour
    {
        private const float VisibleAfterSettlement = 3f;

        private FellableTreeIdentity _sourceTree;
        private IntactTreeFallAnimator _fallingTree;
        private InventoryHudController _inventoryHud;
        private CollectionFeedHudController _collectionFeed;
        private int _yield;
        private float _settledAt = -1f;
        private bool _committed;

        public void Configure(
            FellableTreeIdentity sourceTree,
            IntactTreeFallAnimator fallingTree,
            InventoryHudController inventoryHud,
            CollectionFeedHudController collectionFeed,
            int yield)
        {
            _sourceTree = sourceTree;
            _fallingTree = fallingTree;
            _inventoryHud = inventoryHud;
            _collectionFeed = collectionFeed;
            _yield = Mathf.Clamp(yield, 3, 5);
        }

        private void Update()
        {
            if (_committed
                || _sourceTree == null
                || _fallingTree == null
                || !_fallingTree.IsComplete)
            {
                return;
            }

            if (_settledAt < 0f)
            {
                _settledAt = Time.time;
                return;
            }

            if (Time.time - _settledAt
                < VisibleAfterSettlement)
            {
                return;
            }

            if (_inventoryHud == null)
            {
                _inventoryHud =
                    Object.FindFirstObjectByType<
                        InventoryHudController>();
            }

            if (_inventoryHud == null
                || _inventoryHud.BoundState == null
                || _inventoryHud.BoundCatalog == null)
            {
                return;
            }

            var amount = new NonNegativeQuantity(_yield);
            if (!_inventoryHud.BoundState.TryStoreEntire(
                    ContentIds.WoodLog,
                    amount,
                    out var rewarded,
                    out _))
            {
                // Capacity could have been consumed during the fall. Keeping
                // the fallen tree is the lossless reservation fallback:
                // removal resumes as soon as the full yield fits.
                return;
            }

            _committed = true;
            _inventoryHud.BindInventory(
                rewarded,
                _inventoryHud.BoundCatalog);
            _collectionFeed ??=
                CollectionFeedHudController.EnsureFor(
                    _inventoryHud);
            _collectionFeed?.ShowCommittedCollection(
                ContentIds.WoodLog,
                _yield,
                _inventoryHud.BoundCatalog);

            Destroy(gameObject);
        }
    }
}

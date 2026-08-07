using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using UnityEngine;

namespace CML.Unity.Presentation.Crafting
{
    /// <summary>
    /// Single adapter boundary used by every manual crafting surface. It never
    /// owns an inventory: it publishes an atomically planned successor to the
    /// scene's existing simulation engine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraftingCommandBridge : MonoBehaviour
    {
        private SimulationEngine _engine;
        private GameCatalog _catalog;

        public bool IsAttached => _engine != null && _catalog != null;

        public GameCatalog Catalog =>
            _catalog ?? throw new InvalidOperationException(
                "The crafting bridge has no catalog.");

        public void Attach(SimulationEngine engine, GameCatalog catalog)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public bool TryGetInventory(
            StableId inventoryId,
            out InventoryState inventory)
        {
            inventory = null;
            return IsAttached
                && _engine.State.GetInventorySnapshot().TryGet(
                    inventoryId,
                    out inventory);
        }

        public bool TryCraft(
            StableId inventoryId,
            StableId recipeId,
            CraftingStationKind station,
            long craftCount,
            out CraftingFailure failure)
        {
            failure = CraftingFailure.InvalidDefinition;
            if (!TryGetInventory(inventoryId, out var inventory))
            {
                return false;
            }

            if (!CraftingRule.TryCraft(
                    inventory,
                    Catalog,
                    recipeId,
                    station,
                    craftCount,
                    out var successor,
                    out failure))
            {
                return false;
            }

            if (!_engine.TryPublishInventorySuccessor(successor))
            {
                failure = CraftingFailure.AuthorityBusy;
                return false;
            }

            return true;
        }
    }
}

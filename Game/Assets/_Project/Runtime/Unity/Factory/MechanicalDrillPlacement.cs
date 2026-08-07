using CML.Content;
using CML.Foundation;
using CML.Unity.Mining;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Answers the one question the mechanical drill's hologram cannot answer on
    /// its own: which ore is under it, and therefore which extraction recipe the
    /// build command should carry.
    ///
    /// There is no deposit-to-ore table anywhere. The drill's supported recipe
    /// list *is* the mapping: each extraction recipe has exactly one product, so
    /// the recipe for a deposit is the one whose product is the item that
    /// deposit yields. Adding an ore is a catalog edit, not a code edit.
    /// </summary>
    public static class MechanicalDrillPlacement
    {
        /// <summary>
        /// How far from the machine centre the deposit's working surface may sit.
        /// The surface trigger is built from the deposit's own footprint and the
        /// drill straddles it, so the centres do not coincide exactly.
        /// </summary>
        public const float DepositSearchRadius = 0.75f;

        private static readonly Collider[] Overlaps = new Collider[16];

        /// <summary>
        /// Finds the deposit the drill would stand on and the recipe that
        /// belongs to it. Returns false when there is no deposit, when the
        /// deposit names an ore the catalog does not know, or when no extraction
        /// recipe produces that ore — all three refuse the placement rather than
        /// silently extracting something else.
        /// </summary>
        public static bool TryResolveExtraction(
            GameCatalog catalog,
            Vector3 worldPosition,
            out StableId recipeId,
            out string diagnostic)
        {
            recipeId = StableId.None;

            if (catalog == null)
            {
                diagnostic = "catalogo non disponibile";
                return false;
            }

            if (!TryFindDeposit(worldPosition, out var deposit))
            {
                diagnostic = "la Trivella si piazza soltanto su un giacimento";
                return false;
            }

            var oreKey = deposit.DepositOreItemKey;
            if (!TryFindItemByKey(catalog, oreKey, out var oreItemId))
            {
                diagnostic = $"il giacimento dichiara un minerale sconosciuto ({oreKey})";
                return false;
            }

            if (!catalog.TryGetMachine(ContentIds.MechanicalDrill, out var drill))
            {
                diagnostic = "l'Estrattore meccanico manca dal catalogo";
                return false;
            }

            for (var index = 0; index < drill.SupportedRecipeIds.Count; index++)
            {
                var candidateId = drill.SupportedRecipeIds[index];
                if (!catalog.TryGetRecipe(candidateId, out var candidate)
                    || candidate.Category != RecipeCategory.Extraction
                    || candidate.Outputs == null
                    || candidate.Outputs.Count != 1)
                {
                    continue;
                }

                if (candidate.Outputs[0].ItemId == oreItemId)
                {
                    recipeId = candidateId;
                    diagnostic = oreKey;
                    return true;
                }
            }

            diagnostic = $"nessuna estrazione produce {oreKey}";
            return false;
        }

        /// <summary>
        /// The deposit's working surface is the trigger that
        /// <see cref="ManualMiningSourceIdentity.EnsureInfiniteDepositSurface"/>
        /// already builds for manual mining. Reusing it means the drill and the
        /// pickaxe agree on where a deposit is, instead of each having its own
        /// idea of it.
        /// </summary>
        public static bool TryFindDeposit(
            Vector3 worldPosition,
            out ManualMiningSourceIdentity deposit)
        {
            deposit = null;
            var count = Physics.OverlapSphereNonAlloc(
                worldPosition,
                DepositSearchRadius,
                Overlaps,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            var bestDistance = float.MaxValue;
            for (var index = 0; index < count; index++)
            {
                var collider = Overlaps[index];
                if (collider == null)
                {
                    continue;
                }

                var identity = collider.GetComponent<ManualMiningSourceIdentity>();
                if (identity == null || !identity.IsMachineExtractable)
                {
                    continue;
                }

                var distance = Vector3.SqrMagnitude(
                    collider.bounds.center - worldPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    deposit = identity;
                }
            }

            return deposit != null;
        }

        private static bool TryFindItemByKey(
            GameCatalog catalog,
            string itemKey,
            out StableId itemId)
        {
            for (var index = 0; index < catalog.Items.Count; index++)
            {
                var item = catalog.Items[index];
                if (item != null && string.Equals(item.Key, itemKey, System.StringComparison.Ordinal))
                {
                    itemId = item.Id;
                    return true;
                }
            }

            itemId = StableId.None;
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using CML.Simulation.Mining;
using CML.Unity.Presentation.Equipment;
using CML.Unity.Presentation.Inventory;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CML.Unity.Mining
{
    /// <summary>
    /// Unity boundary for MINE-004. It converts the exact collision captured by
    /// the approved pickaxe animation into one pure mining impact, then applies
    /// world destruction only after the inventory successor commits.
    /// </summary>
    [DefaultExecutionOrder(325)]
    [DisallowMultipleComponent]
    public sealed class ManualMiningController : MonoBehaviour
    {
        [SerializeField] private FirstPersonEquipmentView equipmentView;
        [SerializeField] private InventoryHudController inventoryHud;
        [SerializeField] private CollectionFeedHudController collectionFeedHud;

        private readonly Dictionary<string, int> _sourceHitProgress =
            new Dictionary<string, int>();
        private FirstPersonEquipmentMotion _subscribedMotion;

        public void Configure(
            FirstPersonEquipmentView view,
            InventoryHudController hud)
        {
            equipmentView = view;
            inventoryHud = hud;
            RefreshSubscription();
        }

        private void OnEnable()
        {
            // Also runs after a domain reload during Play Mode, so an already
            // loaded review scene receives every mining query surface without
            // requiring a scene rebuild first.
            EnsureMiningQuerySurfaces();
            ResolveDependencies();
            RefreshSubscription();
        }

        private void LateUpdate()
        {
            ResolveDependencies();
            RefreshSubscription();
        }

        private void OnDisable()
        {
            SubscribeTo(null);
        }

        private void ResolveDependencies()
        {
            if (equipmentView == null)
            {
                equipmentView =
                    GetComponent<FirstPersonEquipmentView>();
            }

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
        }

        private void RefreshSubscription()
        {
            SubscribeTo(equipmentView != null
                ? equipmentView.Motion
                : null);
        }

        private void SubscribeTo(FirstPersonEquipmentMotion motion)
        {
            if (_subscribedMotion == motion)
            {
                return;
            }

            if (_subscribedMotion != null)
            {
                _subscribedMotion.PickaxeMiningSourceHit -= HandleImpact;
            }

            _subscribedMotion = motion;
            if (_subscribedMotion != null)
            {
                _subscribedMotion.PickaxeMiningSourceHit += HandleImpact;
            }
        }

        private void HandleImpact(ManualMiningSourceIdentity source)
        {
            if (inventoryHud == null
                || inventoryHud.BoundState == null
                || inventoryHud.BoundCatalog == null
                || equipmentView == null
                || !equipmentView.IsShowingCrudePickaxe
                || source == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(source.SourceId))
            {
                // Ordinary terrain intentionally carries no identity and
                // yields nothing. Authored rocks, including the small path
                // pebbles, are configured as finite stone sources.
                return;
            }

            var key = source.SourceId;
            _sourceHitProgress.TryGetValue(
                key,
                out var completedHits);
            var result = ManualMiningRule.ApplyImpact(
                inventoryHud.BoundState,
                inventoryHud.SelectedHotbarIndex,
                MapTarget(source.SourceKind),
                completedHits);

            switch (result.Status)
            {
                case ManualMiningImpactStatus.Progressed:
                case ManualMiningImpactStatus.InventoryFull:
                    _sourceHitProgress[key] =
                        result.NextHitProgress;
                    return;

                case ManualMiningImpactStatus.Produced:
                    _sourceHitProgress.Remove(key);
                    inventoryHud.BindInventory(
                        result.UpdatedInventory,
                        inventoryHud.BoundCatalog);
                    collectionFeedHud?.ShowCommittedCollection(
                        result.ProducedItemId,
                        1L,
                        inventoryHud.BoundCatalog);
                    if (result.SourceExhausted)
                    {
                        source.DisableForCommittedExtraction();
                        Destroy(source.gameObject);
                    }

                    return;

                default:
                    return;
            }
        }

        private static ManualMiningTargetKind MapTarget(
            ManualMiningSourceKind sourceKind)
        {
            switch (sourceKind)
            {
                case ManualMiningSourceKind.EnvironmentalStone:
                    return ManualMiningTargetKind.EnvironmentalStone;

                case ManualMiningSourceKind.IronOreRock:
                    return ManualMiningTargetKind.IronOreRock;

                case ManualMiningSourceKind.IronDepositSurface:
                    return ManualMiningTargetKind.IronDepositSurface;

                default:
                    return 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForActivePlayer()
        {
            EnsureMiningQuerySurfaces();

            var camera = Camera.main;
            var hud =
                Object.FindFirstObjectByType<InventoryHudController>();
            if (camera == null || hud == null)
            {
                return;
            }

            var view =
                camera.GetComponent<FirstPersonEquipmentView>();
            if (view == null)
            {
                view =
                    camera.gameObject.AddComponent<
                        FirstPersonEquipmentView>();
                view.Configure(hud);
            }

            var controller =
                camera.GetComponent<ManualMiningController>();
            if (controller == null)
            {
                controller =
                    camera.gameObject.AddComponent<
                        ManualMiningController>();
            }

            controller.Configure(view, hud);
        }

        private static void EnsureMiningQuerySurfaces()
        {
            var deposit = GameObject.Find("MINE_IronDeposit");
            if (deposit != null)
            {
                var identity = ManualMiningSourceIdentity.EnsureInfiniteDepositSurface(
                    deposit,
                    "starter-island.iron-deposit.surface.ground");
                identity.ConfigureDepositOre("item.raw_iron");
            }

            // New ore-deposit prefabs carry the same extractor anchor as the
            // authored starter deposit. Repair any scene instance that was
            // placed before the prefab contract gained its trigger floor; this
            // is intentionally runtime-only and never saves the scene.
            var transforms = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate == null ||
                    candidate.Find("REF_ExtractorAnchor") == null)
                {
                    continue;
                }

                var surface =
                    ManualMiningSourceIdentity.EnsureInfiniteDepositSurface(
                        candidate.gameObject,
                        "runtime.ore-deposit." + candidate.name);
                surface.ConfigureDepositOre(
                    InferDepositOreItemKey(candidate.name));
                EnsureDepositRockSources(candidate);
            }

            var sources =
                Object.FindObjectsByType<ManualMiningSourceIdentity>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (var index = 0; index < sources.Length; index++)
            {
                if (sources[index] != null && sources[index].IsFinite)
                {
                    // Painted Starter Island rocks predate mining authoring.
                    // Their components were serialized with an empty id, so
                    // valid physics contacts were silently rejected later by
                    // HandleImpact. Repair the canonical id at runtime as a
                    // safety net even after the scene migration has run.
                    sources[index].TryConfigureFromAuthoredRockName();
                    sources[index].EnsureMiningMeshColliders();
                }
            }
        }

        private static string InferDepositOreItemKey(string objectName)
        {
            if (objectName.IndexOf("copper", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "item.raw_copper";
            }

            if (objectName.IndexOf("tin", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "item.raw_tin";
            }

            return "item.raw_iron";
        }

        private static void EnsureDepositRockSources(Transform deposit)
        {
            // The manual mining rule currently has a finite iron-ore reward;
            // copper and tin deposits remain drill-only until their hand-mining
            // reward contracts are authored.
            if (InferDepositOreItemKey(deposit.name) != "item.raw_iron")
            {
                return;
            }

            var rockIndex = 0;
            var modules = deposit.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < modules.Length; index++)
            {
                var module = modules[index];
                if (module == deposit ||
                    !module.name.StartsWith("MOD_", StringComparison.Ordinal) ||
                    module.name.IndexOf("_G0", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                rockIndex++;
                var identity =
                    module.GetComponent<ManualMiningSourceIdentity>();
                if (identity == null)
                {
                    identity =
                        module.gameObject.AddComponent<
                            ManualMiningSourceIdentity>();
                }

                if (string.IsNullOrWhiteSpace(identity.SourceId))
                {
                    identity.Configure(
                        ManualMiningSourceKind.IronOreRock,
                        "runtime.ore-deposit." + deposit.name +
                        ".rock." + rockIndex.ToString("00"));
                }

                if (identity.SourceKind == ManualMiningSourceKind.IronOreRock)
                {
                    identity.EnsureMiningMeshColliders();
                }
            }
        }
    }
}

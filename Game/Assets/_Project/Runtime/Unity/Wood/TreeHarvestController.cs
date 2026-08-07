using System;
using CML.Content;
using CML.Inventory;
using CML.Unity.Presentation.Equipment;
using CML.Unity.Presentation.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Unity.Wood
{
    /// <summary>
    /// Connects the approved first-person impact to the existing starter-island
    /// tree objects. WOOD-003 begins the controlled intact-tree fall after
    /// the fifth committed impact; reward remains outside this slice.
    /// </summary>
    [DefaultExecutionOrder(326)]
    [DisallowMultipleComponent]
    public sealed class TreeHarvestController : MonoBehaviour
    {
        [SerializeField] private FirstPersonEquipmentView equipmentView;
        [SerializeField] private InventoryHudController inventoryHud;
        [SerializeField] private CollectionFeedHudController collectionFeedHud;
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
            EnsureTreeTargets();
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
                    UnityEngine.Object.FindFirstObjectByType<
                        InventoryHudController>();
            }

            if (collectionFeedHud == null
                && inventoryHud != null)
            {
                collectionFeedHud =
                    CollectionFeedHudController.EnsureFor(
                        inventoryHud);
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
                _subscribedMotion.PickaxeTreeHit -= HandleTreeImpact;
            }

            _subscribedMotion = motion;
            if (_subscribedMotion != null)
            {
                _subscribedMotion.PickaxeTreeHit += HandleTreeImpact;
            }
        }

        private void HandleTreeImpact(
            FellableTreeIdentity tree,
            RaycastHit hit)
        {
            if (tree == null
                || equipmentView == null
                || !equipmentView.IsShowingCrudePickaxe)
            {
                return;
            }

            var isFinalImpact =
                tree.HitCount == FellableTreeIdentity.HitsRequired - 1;
            var yield = tree.DetermineDeterministicYield();
            InventoryState durabilitySuccessor = null;
            if (isFinalImpact)
            {
                if (inventoryHud == null
                    || inventoryHud.BoundState == null
                    || inventoryHud.BoundCatalog == null
                    || inventoryHud.BoundState.StorableQuantity(
                        ContentIds.WoodLog).Value < yield
                    || !inventoryHud.BoundState.TryConsumeDurabilityAtSlot(
                        inventoryHud.SelectedHotbarIndex,
                        out durabilitySuccessor,
                        out _))
                {
                    return;
                }
            }

            var strikeDirection = hit.point - transform.position;
            if (!tree.RegisterImpact(hit, strikeDirection)
                || !tree.IsReadyForFelling)
            {
                return;
            }

            if (!tree.BeginPhysicalFall())
            {
                tree.RollBackFinalImpact();
                return;
            }

            inventoryHud.BindInventory(
                durabilitySuccessor,
                inventoryHud.BoundCatalog);
            var rewardController =
                tree.FallingTree.gameObject.AddComponent<
                    FelledTreeRewardController>();
            rewardController.Configure(
                tree,
                tree.FallingTree,
                inventoryHud,
                collectionFeedHud,
                yield);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForActivePlayer()
        {
            EnsureTreeTargets();
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var hud = UnityEngine.Object.FindFirstObjectByType<
                InventoryHudController>();
            var view =
                camera.GetComponent<FirstPersonEquipmentView>();
            if (view == null && hud != null)
            {
                view = camera.gameObject.AddComponent<
                    FirstPersonEquipmentView>();
                view.Configure(hud);
            }

            if (view == null)
            {
                return;
            }

            var controller =
                camera.GetComponent<TreeHarvestController>();
            if (controller == null)
            {
                controller =
                    camera.gameObject.AddComponent<
                        TreeHarvestController>();
            }

            controller.Configure(view, hud);
        }

        public static void EnsureTreeTargets()
        {
            var configuredAnyCollider = false;
            var transforms = UnityEngine.Object.FindObjectsByType<
                Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                var candidate = transforms[index];
                if (candidate == null
                    || !candidate.name.StartsWith(
                        "DEC_Tree_",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var identity =
                    candidate.GetComponent<FellableTreeIdentity>();
                if (identity == null)
                {
                    identity = candidate.gameObject.AddComponent<
                        FellableTreeIdentity>();
                }

                var sceneName = candidate.gameObject.scene.IsValid()
                    ? candidate.gameObject.scene.name
                    : SceneManager.GetActiveScene().name;
                identity.Configure(
                    $"{sceneName}.tree.{candidate.name}");
                configuredAnyCollider |=
                    identity.ResolveAuthoredTrunkCollider() != null;
            }

            if (configuredAnyCollider)
            {
                // Components are created during scene bootstrap: publish
                // their transforms before movement or the first swing can
                // query the physics world.
                Physics.SyncTransforms();
            }
        }
    }
}

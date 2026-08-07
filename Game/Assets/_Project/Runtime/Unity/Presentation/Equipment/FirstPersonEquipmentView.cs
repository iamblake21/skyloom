using CML.Content;
using CML.Foundation;
using CML.Unity.Airship;
using CML.Unity.Presentation.Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Unity.Presentation.Equipment
{
    /// <summary>
    /// Read-only first-person projection of the item selected in the authoritative
    /// player Hotbar. It never creates inventory contents and never decides which
    /// item is equipped.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FirstPersonEquipmentView : MonoBehaviour
    {
        private const string CrudePickaxeResourcePath =
            "Equipment/PF_PickaxeCrudeView";
        private const string PoseProfileResourcePath =
            FirstPersonEquipmentPose.ResourcesPath;

        private static readonly Vector3 PickaxePosition =
            new Vector3(0.26f, -0.44f, 0.72f);
        private static readonly Vector3 PickaxeEuler =
            new Vector3(7f, -9f, -11f);
        private static readonly Vector3 PickaxeScale =
            new Vector3(0.74f, 0.74f, 0.74f);
        // Measured from TOOL_PickaxeCrude.fbx. The handle extends from
        // local Y -0.22 to +0.70; this point sits just above its lower end.
        private static readonly Vector3 PickaxeGripLocalPoint =
            new Vector3(0f, -0.14f, 0f);
        [SerializeField] private InventoryHudController inventoryHud;
        [SerializeField] private GameObject crudePickaxePrefab;
        [SerializeField] private FirstPersonEquipmentPose poseProfile;

        private GameObject _viewRoot;
        private Transform _motionRoot;
        private Transform _swingRoot;
        private GameObject _pickaxe;
        private FirstPersonEquipmentMotion _motion;
        private FirstPersonImpactFeedback _impactFeedback;
        private FirstPersonEquipmentCollision _collision;
        private StableId _shownItemId;

        public bool IsShowingCrudePickaxe =>
            _viewRoot != null
            && _viewRoot.activeSelf
            && _shownItemId == ContentIds.CrudePickaxe;

        public FirstPersonEquipmentMotion Motion => _motion;

        public void Configure(
            InventoryHudController controller,
            GameObject pickaxePrefab = null)
        {
            inventoryHud = controller;
            if (pickaxePrefab != null)
            {
                crudePickaxePrefab = pickaxePrefab;
            }
        }

        private void Awake()
        {
            ResolveDependencies();
            SetViewVisible(false);
        }

        private void LateUpdate()
        {
            ResolveDependencies();

            var shouldShow = TryGetSelectedItem(out var itemId)
                && itemId == ContentIds.CrudePickaxe
                && inventoryHud.GameplayPresentationVisible
                && !inventoryHud.InventoryOpen;

            if (!shouldShow)
            {
                _shownItemId = StableId.None;
                SetViewVisible(false);
                return;
            }

            EnsureCrudePickaxeView();
            _shownItemId = ContentIds.CrudePickaxe;
            SetViewVisible(true);
        }

        private bool TryGetSelectedItem(out StableId itemId)
        {
            itemId = StableId.None;
            return inventoryHud != null
                && inventoryHud.TryGetSelectedHotbarItem(
                    out itemId,
                    out var quantity)
                && quantity > 0L;
        }

        private void ResolveDependencies()
        {
            if (inventoryHud == null)
            {
                inventoryHud =
                    Object.FindFirstObjectByType<InventoryHudController>();
            }

            if (crudePickaxePrefab == null)
            {
                crudePickaxePrefab =
                    Resources.Load<GameObject>(CrudePickaxeResourcePath);
            }

            if (poseProfile == null)
            {
                poseProfile =
                    Resources.Load<FirstPersonEquipmentPose>(
                        PoseProfileResourcePath);
            }
        }

        private void EnsureCrudePickaxeView()
        {
            if (_pickaxe != null || crudePickaxePrefab == null)
            {
                EnsureImpactFeedback();
                return;
            }

            _viewRoot = new GameObject("VIEW_FirstPersonEquipment");
            _viewRoot.transform.SetParent(transform, false);
            _motionRoot = new GameObject(
                "MOTION_FirstPersonEquipment").transform;
            _motionRoot.SetParent(_viewRoot.transform, false);
            _swingRoot = new GameObject(
                "SWING_FirstPersonEquipment").transform;
            _swingRoot.SetParent(_motionRoot, false);

            _pickaxe = Instantiate(
                crudePickaxePrefab,
                _swingRoot,
                worldPositionStays: false);
            _pickaxe.name = "VIEW_CrudePickaxe";
            var pickaxePose = poseProfile != null
                ? poseProfile.Pickaxe
                : new LocalViewPose(
                    PickaxePosition,
                    PickaxeEuler,
                    PickaxeScale);
            var poseRotation =
                Quaternion.Euler(pickaxePose.LocalEulerAngles);
            var scaledGrip = Vector3.Scale(
                PickaxeGripLocalPoint,
                pickaxePose.LocalScale);
            _swingRoot.localPosition =
                pickaxePose.LocalPosition + poseRotation * scaledGrip;
            _swingRoot.localRotation = poseRotation;
            _swingRoot.localScale = pickaxePose.LocalScale;
            _pickaxe.transform.localPosition = -PickaxeGripLocalPoint;
            _pickaxe.transform.localRotation = Quaternion.identity;
            _pickaxe.transform.localScale = Vector3.one;

            StripGameplayComponents(_pickaxe);
            ConfigureViewmodelRenderers(_pickaxe);
            _collision =
                _pickaxe.AddComponent<FirstPersonEquipmentCollision>();
            var characterMotor =
                GetComponentInParent<FirstPersonCharacterMotor>();
            _collision.Configure(_swingRoot, characterMotor);
            _motion = GetComponent<FirstPersonEquipmentMotion>();
            if (_motion == null)
            {
                _motion =
                    gameObject.AddComponent<FirstPersonEquipmentMotion>();
            }

            _motion.Configure(
                _motionRoot,
                _swingRoot,
                characterMotor,
                _collision);
            EnsureImpactFeedback();
        }

        private void EnsureImpactFeedback()
        {
            if (_motion == null)
            {
                return;
            }

            if (_impactFeedback == null)
            {
                _impactFeedback =
                    GetComponent<FirstPersonImpactFeedback>();
            }

            if (_impactFeedback == null)
            {
                _impactFeedback = gameObject.AddComponent<
                    FirstPersonImpactFeedback>();
            }

            _impactFeedback.Configure(_motion);
        }

        private static void StripGameplayComponents(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                Destroy(colliders[index]);
            }

            var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (var index = 0; index < rigidbodies.Length; index++)
            {
                Destroy(rigidbodies[index]);
            }
        }

        private static void ConfigureViewmodelRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                renderers[index].shadowCastingMode = ShadowCastingMode.Off;
                renderers[index].receiveShadows = false;
            }
        }

        private void SetViewVisible(bool visible)
        {
            if (_viewRoot != null && _viewRoot.activeSelf != visible)
            {
                _viewRoot.SetActive(visible);
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForActivePlayer()
        {
            var camera = Camera.main;
            var hud =
                Object.FindFirstObjectByType<InventoryHudController>();
            if (camera == null || hud == null)
            {
                return;
            }

            var view = camera.GetComponent<FirstPersonEquipmentView>();
            if (view == null)
            {
                view = camera.gameObject.AddComponent<FirstPersonEquipmentView>();
            }

            view.Configure(hud);
        }
    }
}

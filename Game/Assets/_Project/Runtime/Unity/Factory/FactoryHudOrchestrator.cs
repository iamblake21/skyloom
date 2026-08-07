using System;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Simulation;
using CML.Unity.Airship;
using CML.Unity.Presentation.Airship;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Crafting;
using CML.Unity.Presentation.Machines;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Joins the already-authored inventory, chest and machine panels to one simulation
    /// engine. This class does not advance the engine and never creates another one.
    /// It only refreshes detached read models and routes chest transfers through the
    /// existing authoritative <see cref="TransferCommandBridge"/>.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TransferCommandBridge))]
    public sealed class FactoryHudOrchestrator : MonoBehaviour
    {
        [SerializeField] private InventoryHudController inventoryHud;
        [SerializeField] private ChestHudController chestHud;
        [SerializeField] private MachineHudController machineHud;
        [SerializeField] private WorkbenchHudController workbenchHud;
        [SerializeField] private AirshipRepairHudController airshipRepairHud;
        [SerializeField] private TransferCommandBridge transferBridge;
        [SerializeField] private CraftingCommandBridge craftingBridge;
        [SerializeField] private FirstPersonMouseLook mouseLook;
        [SerializeField] private AirshipInputAdapter gameplayInput;

        private SimulationEngine _engine;
        private GameCatalog _catalog;
        private StableId _playerInventoryId;
        private StableId _activeChestId;
        private StableId _activeMachineId;
        private StableId _activeWorkbenchId;
        private ChestLidAnimator _activeChestLid;
        private AirshipSimulationBridge _airshipRepairBridge;
        private ulong _lastRefreshedTick = ulong.MaxValue;
        private bool _lastModalState;
        private bool _cinematicSuppressed;

        public event Action<bool> ModalStateChanged;

        public bool IsAttached => _engine != null && _catalog != null;

        public bool CinematicSuppressed => _cinematicSuppressed;

        public SimulationEngine Engine =>
            _engine ?? throw new InvalidOperationException(
                "The factory HUD is not attached to a SimulationEngine.");

        public GameCatalog Catalog =>
            _catalog ?? throw new InvalidOperationException(
                "The factory HUD is not attached to a catalog.");

        public StableId PlayerInventoryId => _playerInventoryId;

        public InventoryHudController InventoryHud => inventoryHud;

        public AirshipRepairHudController AirshipRepairHud => airshipRepairHud;

        public void ConfigureAirshipRepairHud(AirshipRepairHudController controller)
        {
            airshipRepairHud = controller;
        }

        public bool InteractionPanelOpen =>
            (chestHud != null && chestHud.PanelOpen)
            || (machineHud != null && machineHud.PanelOpen)
            || (workbenchHud != null && workbenchHud.PanelOpen)
            || (airshipRepairHud != null && airshipRepairHud.PanelOpen);

        public bool AnyModalOpen =>
            (inventoryHud != null && inventoryHud.InventoryOpen)
            || InteractionPanelOpen;

        public bool CanAcceptWorldInteraction =>
            IsAttached && !AnyModalOpen && !_cinematicSuppressed;

        /// <summary>
        /// Hides the whole player-facing HUD for the duration of a cutscene
        /// without disabling any <see cref="UnityEngine.UIElements.UIDocument"/>.
        /// Disabling the document would make UI Toolkit clone a brand new
        /// visual tree on restore and leave every panel bound to the discarded
        /// one; the presentation flag the piloting camera already uses does the
        /// same job and survives the round trip.
        /// </summary>
        public void SetCinematicSuppressed(bool suppressed)
        {
            if (_cinematicSuppressed == suppressed)
            {
                return;
            }

            _cinematicSuppressed = suppressed;
            if (suppressed)
            {
                inventoryHud?.SetInventoryOpen(false);
                ClosePanelsWithoutApplyingModal();
                inventoryHud?.SetGameplayPresentationVisible(false);
                PublishModalState(false);
                return;
            }

            ApplyModalState();
        }

        public bool TryGetSelectedHotbarItem(
            out StableId itemId,
            out long quantity)
        {
            itemId = StableId.None;
            quantity = 0L;
            return inventoryHud != null
                && inventoryHud.GameplayPresentationVisible
                && inventoryHud.TryGetSelectedHotbarItem(out itemId, out quantity);
        }

        public void ConfigureUi(
            InventoryHudController inventoryController,
            ChestHudController chestController,
            MachineHudController machineController,
            TransferCommandBridge bridge,
            FirstPersonMouseLook firstPersonMouseLook = null,
            AirshipInputAdapter gameplayInput = null,
            WorkbenchHudController workbenchController = null,
            CraftingCommandBridge craftingCommandBridge = null)
        {
            inventoryHud = inventoryController;
            chestHud = chestController;
            machineHud = machineController;
            workbenchHud = workbenchController;
            transferBridge = bridge != null
                ? bridge
                : GetComponent<TransferCommandBridge>();
            craftingBridge = craftingCommandBridge != null
                ? craftingCommandBridge
                : GetComponent<CraftingCommandBridge>();
            mouseLook = firstPersonMouseLook;
            this.gameplayInput = gameplayInput;

            // Input ownership is centralized here. The existing HUD controllers keep
            // their visual and command responsibilities but do not depend on an
            // AirshipInputAdapter or on an airship simulation bridge.
            inventoryHud?.ConfigureGameplayInput(
                gameplayInput,
                firstPersonMouseLook,
                useReviewContents: false);
            // Senza il ponte il pannello TAB resta di sola lettura: riordinare
            // uno slot e' una mutazione canonica e passa dai comandi.
            inventoryHud?.ConfigureCommandBridge(transferBridge);
            inventoryHud?.ConfigureCrafting(craftingBridge);
            chestHud?.ConfigureGameplayInput(
                transferBridge,
                gameplayInput,
                firstPersonMouseLook);
            machineHud?.ConfigureGameplayInput(
                gameplayInput,
                firstPersonMouseLook);
            machineHud?.ConfigureTransfers(
                transferBridge,
                _playerInventoryId);
            workbenchHud?.ConfigureCrafting(craftingBridge);
            workbenchHud?.ConfigureInventoryCommands(transferBridge);

            if (IsAttached)
            {
                transferBridge.Attach(_engine, _catalog);
                craftingBridge?.Attach(_engine, _catalog);
                _lastRefreshedTick = ulong.MaxValue;
                RefreshFromAuthoritativeState(force: true);
                ApplyModalState();
            }
        }

        public void AttachSimulation(
            SimulationEngine engine,
            GameCatalog catalog,
            StableId playerInventoryId)
        {
            if (playerInventoryId.IsNone)
            {
                throw new ArgumentException(
                    "The HUD needs the authoritative player inventory id.",
                    nameof(playerInventoryId));
            }

            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _playerInventoryId = playerInventoryId;

            if (transferBridge == null)
            {
                transferBridge = GetComponent<TransferCommandBridge>();
            }

            transferBridge.Attach(_engine, _catalog);
            craftingBridge?.Attach(_engine, _catalog);
            inventoryHud?.ConfigureCommandBridge(transferBridge);
            inventoryHud?.ConfigureCrafting(craftingBridge);
            workbenchHud?.ConfigureCrafting(craftingBridge);
            workbenchHud?.ConfigureInventoryCommands(transferBridge);
            chestHud?.ConfigureGameplayInput(
                transferBridge,
                gameplayInput,
                mouseLook);
            machineHud?.ConfigureTransfers(
                transferBridge,
                _playerInventoryId);
            _lastRefreshedTick = ulong.MaxValue;
            RefreshFromAuthoritativeState(force: true);
            ApplyModalState();
        }

        /// <summary>
        /// Opens the panel associated with the target. Returning false means that the
        /// target no longer exists in the authoritative graph, not that Unity failed to
        /// find its collider.
        /// </summary>
        public bool TryInteract(FactoryInteractionTarget target)
        {
            if (target == null || !target.IsConfigured || !CanAcceptWorldInteraction)
            {
                return false;
            }

            switch (target.InteractionKind)
            {
                case FactoryInteractionKind.Chest:
                    return OpenChest(target.StableId, target.gameObject);
                case FactoryInteractionKind.Machine:
                    return OpenMachine(target.StableId);
                case FactoryInteractionKind.Workbench:
                    return OpenWorkbench(target.StableId);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(target),
                        target.InteractionKind,
                        "Unknown factory interaction kind.");
            }
        }

        public bool OpenChest(StableId nodeId)
        {
            return OpenChest(nodeId, null);
        }

        private bool OpenChest(StableId nodeId, GameObject chestObject)
        {
            if (!IsAttached
                || nodeId.IsNone
                || chestHud == null
                || !MachineDiagnostics.TryDescribe(
                    Engine.State,
                    Catalog,
                    nodeId,
                    out var report)
                || report.Kind != CML.Simulation.Machines.MachineNodeKind.Buffer)
            {
                return false;
            }

            ClosePanelsWithoutApplyingModal();
            inventoryHud?.SetInventoryOpen(false);
            _activeChestId = nodeId;
            _activeMachineId = StableId.None;
            chestHud.Bind(nodeId, _playerInventoryId);
            chestHud.SetPanelOpen(true);
            if (chestObject != null)
            {
                _activeChestLid = ChestLidAnimator.EnsureFor(chestObject);
                _activeChestLid?.SetOpen(true);
            }

            ApplyModalState();
            return true;
        }

        public bool OpenMachine(StableId nodeId)
        {
            if (!IsAttached
                || nodeId.IsNone
                || machineHud == null
                || !MachineDiagnostics.TryDescribe(
                    Engine.State,
                    Catalog,
                    nodeId,
                    out var report))
            {
                return false;
            }

            ClosePanelsWithoutApplyingModal();
            inventoryHud?.SetInventoryOpen(false);
            _activeChestId = StableId.None;
            _activeMachineId = nodeId;
            var inventories = Engine.State.GetInventorySnapshot();
            InventoryUiSnapshot playerSnapshot = null;
            if (inventories.TryGet(_playerInventoryId, out var playerInventory))
            {
                playerSnapshot = InventoryHudPresenter.Project(
                    playerInventory,
                    Catalog);
            }

            machineHud.Bind(
                MachineHudPresenter.Project(report, Catalog),
                playerSnapshot);
            machineHud.SetPanelOpen(true);
            ApplyModalState();
            return true;
        }

        public bool OpenWorkbench(StableId workbenchId)
        {
            if (!IsAttached
                || workbenchId.IsNone
                || workbenchHud == null
                || !Engine.State.GetInventorySnapshot().TryGet(
                    _playerInventoryId,
                    out var inventory))
            {
                return false;
            }

            ClosePanelsWithoutApplyingModal();
            inventoryHud?.SetInventoryOpen(false);
            _activeChestId = StableId.None;
            _activeMachineId = StableId.None;
            _activeWorkbenchId = workbenchId;
            workbenchHud.Bind(inventory, Catalog);
            workbenchHud.SetPanelOpen(true);
            ApplyModalState();
            return true;
        }

        /// <summary>
        /// Opens the damaged panel of the airship. Called from the pilot
        /// station's inspection event, which fires instead of piloting while the
        /// hull is not airworthy.
        /// </summary>
        public bool OpenAirshipRepair(AirshipSimulationBridge bridge)
        {
            if (!IsAttached
                || bridge == null
                || airshipRepairHud == null
                || !Engine.State.GetInventorySnapshot().TryGet(
                    _playerInventoryId,
                    out var inventory))
            {
                return false;
            }

            ClosePanelsWithoutApplyingModal();
            inventoryHud?.SetInventoryOpen(false);
            _airshipRepairBridge = bridge;
            airshipRepairHud.ConfigureTransfers(transferBridge);
            Engine.State.GetInventorySnapshot().TryGet(
                FactoryLineSimulationRoot.AirshipHoldId,
                out var hold);
            airshipRepairHud.Bind(
                bridge,
                inventory,
                hold,
                Catalog,
                _playerInventoryId);
            airshipRepairHud.SetPanelOpen(true);
            ApplyModalState();
            return true;
        }

        /// <summary>E closes the currently open world-interaction panel as well as opens it.</summary>
        public bool CloseInteractionPanel()
        {
            if (!InteractionPanelOpen)
            {
                return false;
            }

            ClosePanelsWithoutApplyingModal();
            ApplyModalState();
            return true;
        }

        public void CloseAllPanels()
        {
            inventoryHud?.SetInventoryOpen(false);
            ClosePanelsWithoutApplyingModal();
            ApplyModalState();
        }

        public void RefreshFromAuthoritativeState(bool force = false)
        {
            if (!IsAttached)
            {
                return;
            }

            var tick = Engine.State.Tick.Value;
            if (!force && tick == _lastRefreshedTick)
            {
                return;
            }

            _lastRefreshedTick = tick;
            var inventories = Engine.State.GetInventorySnapshot();
            if (inventoryHud != null
                && inventories.TryGet(_playerInventoryId, out var playerInventory))
            {
                inventoryHud.BindInventory(playerInventory, Catalog);
            }

            if (chestHud != null && chestHud.PanelOpen && !_activeChestId.IsNone)
            {
                chestHud.Refresh();
            }

            // The repair counters follow the committed state, not the button
            // press, so they are re-read here like every other open panel.
            if (airshipRepairHud != null
                && airshipRepairHud.PanelOpen
                && _airshipRepairBridge != null
                && inventories.TryGet(_playerInventoryId, out var repairInventory))
            {
                inventories.TryGet(
                    FactoryLineSimulationRoot.AirshipHoldId,
                    out var repairHold);
                airshipRepairHud.Refresh(repairInventory, repairHold);
            }

            if (machineHud != null
                && machineHud.PanelOpen
                && !_activeMachineId.IsNone
                && MachineDiagnostics.TryDescribe(
                    Engine.State,
                    Catalog,
                    _activeMachineId,
                    out var report))
            {
                InventoryUiSnapshot playerSnapshot = null;
                if (inventories.TryGet(
                        _playerInventoryId,
                        out var machinePlayerInventory))
                {
                    playerSnapshot = InventoryHudPresenter.Project(
                        machinePlayerInventory,
                        Catalog);
                }

                machineHud.Bind(
                    MachineHudPresenter.Project(report, Catalog),
                    playerSnapshot);
            }

            if (workbenchHud != null
                && workbenchHud.PanelOpen
                && !_activeWorkbenchId.IsNone
                && inventories.TryGet(_playerInventoryId, out var workbenchInventory))
            {
                workbenchHud.Bind(workbenchInventory, Catalog);
            }
        }

        private void Awake()
        {
            if (transferBridge == null)
            {
                transferBridge = GetComponent<TransferCommandBridge>();
            }
        }

        private void Update()
        {
            if (_cinematicSuppressed)
            {
                // The authoritative read model still has to advance so the
                // Hotbar shows the right stacks the frame the cutscene ends.
                inventoryHud?.SetGameplayPresentationVisible(false);
                RefreshFromAuthoritativeState();
                return;
            }

            var passenger = gameplayInput != null
                && gameplayInput.SimulationBridge != null
                    ? gameplayInput.SimulationBridge.Passenger
                    : null;
            inventoryHud?.SetGameplayPresentationVisible(
                passenger == null || !passenger.IsPiloting);
            RefreshFromAuthoritativeState();

            // InventoryHudController owns Tab. If Tab opened the backpack while a
            // machine panel was open, the backpack wins and the world panel closes.
            if (inventoryHud != null
                && inventoryHud.InventoryOpen
                && InteractionPanelOpen)
            {
                ClosePanelsWithoutApplyingModal();
            }

            // Escape is handled by the individual panel controller. Mirror that
            // state back to the world presenter so the physical lid never stays
            // open after the modal has disappeared.
            if (_activeChestLid != null
                && (chestHud == null || !chestHud.PanelOpen))
            {
                CloseActiveChestLid();
                _activeChestId = StableId.None;
            }

            ApplyModalState();
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            mouseLook?.SetUiInputSuppressed(false);
            CloseActiveChestLid();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            PublishModalState(false);
        }

        private void ClosePanelsWithoutApplyingModal()
        {
            CloseActiveChestLid();
            chestHud?.SetPanelOpen(false);
            machineHud?.SetPanelOpen(false);
            workbenchHud?.SetPanelOpen(false);
            airshipRepairHud?.SetPanelOpen(false);
            _activeChestId = StableId.None;
            _activeMachineId = StableId.None;
            _activeWorkbenchId = StableId.None;
        }

        private void CloseActiveChestLid()
        {
            _activeChestLid?.SetOpen(false);
            _activeChestLid = null;
        }

        private void ApplyModalState()
        {
            var modal = AnyModalOpen;
            mouseLook?.SetUiInputSuppressed(modal);

            if (modal)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (isActiveAndEnabled && Application.isFocused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            PublishModalState(modal);
        }

        private void PublishModalState(bool modal)
        {
            if (_lastModalState == modal)
            {
                return;
            }

            _lastModalState = modal;
            ModalStateChanged?.Invoke(modal);
        }
    }
}

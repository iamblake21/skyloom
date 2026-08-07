using System.Collections;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CML.Tests.PlayMode
{
    public sealed class InventoryHudStackInteractionPlayModeTests
        : InputTestFixture
    {
        private const string PrefabPath =
            "Assets/_Project/Art/UI/Inventory/PF_InventoryHUD.prefab";
        private const string MachinePrefabPath =
            "Assets/_Project/Art/UI/Machine/PF_MachineHUD.prefab";
        private const string ChestPrefabPath =
            "Assets/_Project/Art/UI/Chest/PF_ChestHUD.prefab";

        private static readonly StableId Backpack =
            new StableId(0x9800000000000000UL, 1UL);

        private GameObject _instance;
        private GameObject _machineHud;
        private GameObject _chestHud;
        private GameObject _eventSystemObject;
        private InventoryHudController _controller;
        private SimulationEngine _engine;

        public override void Setup()
        {
            base.Setup();

#if UNITY_EDITOR
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                Backpack,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.RawIron,
                        new NonNegativeQuantity(9))
                });
            _engine = new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    new CatalogRevision(
                        CatalogSchema.BootstrapContentRevision),
                    new AirshipSimulationState(),
                    new MachineSimulationState(),
                    InventorySimulationState.Create(
                        catalog,
                        inventory)),
                null,
                catalog);

            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"missing {PrefabPath}");
            _instance = Object.Instantiate(prefab);

            // Reproduce the real scene ordering. These two documents sort above
            // the inventory and used to swallow its clicks even while closed.
            _machineHud = InstantiatePrefab(MachinePrefabPath);
            _chestHud = InstantiatePrefab(ChestPrefabPath);

            _controller =
                _instance.GetComponent<InventoryHudController>();
            var bridge =
                _instance.AddComponent<TransferCommandBridge>();
            bridge.Attach(_engine, catalog);
            _controller.ConfigureCommandBridge(bridge);
            _controller.BindInventory(inventory, catalog);
            _controller.SetInventoryOpen(true);

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                _eventSystemObject = new GameObject(
                    "InventoryHudStackInteraction_EventSystem");
                _eventSystemObject.AddComponent<EventSystem>();
                _eventSystemObject.AddComponent<
                    InputSystemUIInputModule>();
            }
#endif
        }

        public override void TearDown()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
            }

            if (_machineHud != null)
            {
                Object.DestroyImmediate(_machineHud);
            }

            if (_chestHud != null)
            {
                Object.DestroyImmediate(_chestHud);
            }

            if (_eventSystemObject != null)
            {
                Object.DestroyImmediate(_eventSystemObject);
            }

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator RightClickSplitsAndDepositsOneWhileLeftPlacesRemainder()
        {
#if !UNITY_EDITOR
            Assert.Ignore(
                "The inventory prefab is editor-only test data.");
            yield break;
#else
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
            yield return null;

            var root =
                _instance.GetComponent<UIDocument>()
                    .rootVisualElement;
            var source =
                root.Q<VisualElement>("inventory-slot-0");
            var firstTarget =
                root.Q<VisualElement>("inventory-slot-8");
            var secondTarget =
                root.Q<VisualElement>("inventory-slot-9");
            Assert.That(source, Is.Not.Null);
            Assert.That(firstTarget, Is.Not.Null);
            Assert.That(secondTarget, Is.Not.Null);
            Assert.That(
                source.worldBound.width,
                Is.GreaterThan(0f));

            var sourcePosition =
                ToScreen(source.worldBound.center, root.worldBound);
            var firstPosition =
                ToScreen(
                    firstTarget.worldBound.center,
                    root.worldBound);
            var secondPosition =
                ToScreen(
                    secondTarget.worldBound.center,
                    root.worldBound);

            yield return PhysicalClick(
                mouse,
                mouse.rightButton,
                sourcePosition);

            var cursor =
                root.Q<VisualElement>("inventory-cursor-stack");
            Assert.That(cursor, Is.Not.Null);
            Assert.That(
                cursor.Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("5"),
                "Right click must pick the rounded-up half.");
            Assert.That(
                source.Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("4"),
                "The source must show the half that was not picked.");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty);

            yield return PhysicalClick(
                mouse,
                mouse.rightButton,
                firstPosition);
            cursor =
                root.Q<VisualElement>("inventory-cursor-stack");
            Assert.That(cursor, Is.Not.Null);
            Assert.That(
                cursor.Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("4"),
                "A right click deposits exactly one and keeps the rest held.");

            yield return PhysicalClick(
                mouse,
                mouse.leftButton,
                secondPosition);
            Assert.That(
                root.Q<VisualElement>("inventory-cursor-stack"),
                Is.Null,
                "A left click places the complete held remainder.");

            var pending =
                _engine.State.GetAcceptedCommandsCanonical();
            Assert.That(pending, Has.Count.EqualTo(2));
            Assert.That(
                SlotMoveCommandPayload.TryDecode(
                    pending[0],
                    out var firstInventory,
                    out var firstSource,
                    out var firstDestination,
                    out var firstAmount),
                Is.True);
            Assert.That(firstInventory, Is.EqualTo(Backpack));
            Assert.That(firstSource, Is.EqualTo(0));
            Assert.That(firstDestination, Is.EqualTo(8));
            Assert.That(firstAmount.Value, Is.EqualTo(1));
            Assert.That(
                SlotMoveCommandPayload.TryDecode(
                    pending[1],
                    out _,
                    out var secondSource,
                    out var secondDestination,
                    out var secondAmount),
                Is.True);
            Assert.That(secondSource, Is.EqualTo(0));
            Assert.That(secondDestination, Is.EqualTo(9));
            Assert.That(secondAmount.Value, Is.EqualTo(4));

            var result = _engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(
                _engine.State.GetInventorySnapshot()
                    .TryGet(Backpack, out var updated),
                Is.True);
            Assert.That(
                updated.GetSlot(0).Stack.Value.Quantity.Value,
                Is.EqualTo(4));
            Assert.That(
                updated.GetSlot(8).Stack.Value.Quantity.Value,
                Is.EqualTo(1));
            Assert.That(
                updated.GetSlot(9).Stack.Value.Quantity.Value,
                Is.EqualTo(4));
#endif
        }

        private IEnumerator PhysicalClick(
            Mouse mouse,
            ButtonControl button,
            Vector2 position)
        {
            Move(mouse.position, position);
            yield return null;
            Press(button);
            yield return null;
            Release(button);
            yield return null;
        }

        private static Vector2 ToScreen(
            Vector2 panelPosition,
            Rect panelBounds)
        {
            var normalized = new Vector2(
                (panelPosition.x - panelBounds.xMin)
                / panelBounds.width,
                (panelPosition.y - panelBounds.yMin)
                / panelBounds.height);
            return new Vector2(
                normalized.x * Screen.width,
                (1f - normalized.y) * Screen.height);
        }

#if UNITY_EDITOR
        private static GameObject InstantiatePrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"missing {path}");
            return Object.Instantiate(prefab);
        }
#endif
    }
}

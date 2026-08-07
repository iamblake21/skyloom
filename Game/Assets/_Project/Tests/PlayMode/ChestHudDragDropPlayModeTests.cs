using System.Collections;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CML.Tests.PlayMode
{
    /// <summary>
    /// Exercises the chest panel through a real Input System mouse and the runtime
    /// UI Toolkit panel. EditMode tests cover both directions; this proves the player
    /// two-click gesture reaches the same authoritative bridge in PlayMode, with the
    /// mouse button released while the held stack follows the cursor.
    /// </summary>
    public sealed class ChestHudDragDropPlayModeTests : InputTestFixture
    {
        private const string PrefabPath =
            "Assets/_Project/Art/UI/Chest/PF_ChestHUD.prefab";

        private static readonly StableId Crate =
            new StableId(0x9700000000000000UL, 1UL);
        private static readonly StableId Backpack =
            new StableId(0x9700000000000000UL, 2UL);

        private GameObject _instance;
        private GameObject _eventSystemObject;
        private ChestHudController _controller;
        private SimulationEngine _engine;

        public override void Setup()
        {
            base.Setup();

#if UNITY_EDITOR
            var catalog = BootstrapCatalog.Load();
            var machines = new MachineSimulationStateBuilder(catalog)
                .AddBuffer(Crate, ContentIds.WoodenCrate)
                .Store(Crate, ContentIds.IronPlate, 40)
                .Build();
            var inventories = InventorySimulationState.Create(
                catalog,
                InventoryState.Restore(
                    Backpack,
                    catalog,
                    ContentIds.PlayerInventory,
                    new[]
                    {
                        new InventoryStackRecord(
                            0,
                            ContentIds.IronIngot,
                            new NonNegativeQuantity(25))
                    }));
            _engine = new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    new CatalogRevision(
                        CatalogSchema.BootstrapContentRevision),
                    new AirshipSimulationState(),
                    machines,
                    inventories),
                null,
                catalog);

            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"missing {PrefabPath}");
            _instance = Object.Instantiate(prefab);
            _controller = _instance.GetComponent<ChestHudController>();
            var bridge = _instance.GetComponent<TransferCommandBridge>();
            Assert.That(_controller, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);
            bridge.Attach(_engine, catalog);
            _controller.Bind(Crate, Backpack);
            _controller.SetPanelOpen(true);

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                _eventSystemObject =
                    new GameObject("ChestHudDragDropPlayMode_EventSystem");
                _eventSystemObject.AddComponent<EventSystem>();
                _eventSystemObject.AddComponent<InputSystemUIInputModule>();
            }
#endif
        }

        public override void TearDown()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
            }

            if (_eventSystemObject != null)
            {
                Object.DestroyImmediate(_eventSystemObject);
            }

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator TwoReleasedClicksMoveBackpackStackToCrateAuthoritatively()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The diagnostic UI prefab is editor-only test data.");
            yield break;
#else
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
            yield return null;

            var root =
                _instance.GetComponent<UIDocument>().rootVisualElement;
            var source = root.Q<VisualElement>("player-slot-0");
            var destination = root.Q<VisualElement>("crate-slot-5");
            Assert.That(source, Is.Not.Null);
            Assert.That(destination, Is.Not.Null);
            Assert.That(source.worldBound.width, Is.GreaterThan(0f));
            Assert.That(destination.worldBound.width, Is.GreaterThan(0f));

            var from = ToScreen(
                source.worldBound.center,
                root.worldBound);
            var to = ToScreen(
                destination.worldBound.center,
                root.worldBound);
            yield return PhysicalClick(mouse, from);

            Assert.That(mouse.leftButton.isPressed, Is.False);
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty,
                "The pick-up click must not enqueue a transfer.");
            var cursorStack =
                root.Q<VisualElement>("chest-cursor-stack");
            Assert.That(
                cursorStack,
                Is.Not.Null,
                "The whole stack must remain attached to the cursor after release.");
            var cursorBeforeMove = cursorStack.worldBound.center;
            Assert.That(
                source.Q<VisualElement>(className: "slot-icon-host")
                    .resolvedStyle.visibility,
                Is.EqualTo(Visibility.Hidden),
                "The picked stack must be drawn only under the cursor.");

            // Move with no button held. The root PointerMoveEvent, rather than a drag,
            // keeps the ghost under the cursor until the independent second click.
            Move(mouse.position, to, to - from);
            yield return null;
            Assert.That(mouse.leftButton.isPressed, Is.False);
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack"),
                Is.Not.Null);
            Assert.That(
                Vector2.Distance(
                    cursorBeforeMove,
                    cursorStack.worldBound.center),
                Is.GreaterThan(1f),
                "The ghost must follow pointer movement with no button held.");
            Assert.That(
                source.Q<VisualElement>(className: "slot-icon-host")
                    .resolvedStyle.visibility,
                Is.EqualTo(Visibility.Hidden));
            yield return PhysicalClick(mouse, to);
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack"),
                Is.Null,
                "The destination click must release the cursor stack.");

            var pending =
                _engine.State.GetAcceptedCommandsCanonical();
            Assert.That(
                pending,
                Has.Count.EqualTo(1),
                "The independent destination click must enqueue one transfer.");
            Assert.That(
                TransferCommandPayload.TryDecode(
                    pending[0],
                    out var sourceEndpoint,
                    out var destinationEndpoint,
                    out var item,
                    out var quantity),
                Is.True);
            Assert.That(
                sourceEndpoint,
                Is.EqualTo(TransferEndpoint.Inventory(Backpack)));
            Assert.That(
                destinationEndpoint,
                Is.EqualTo(
                    TransferEndpoint.Port(
                        Crate,
                        MachinePortKind.Storage)));
            Assert.That(item, Is.EqualTo(ContentIds.IronIngot));
            Assert.That(quantity.Value, Is.EqualTo(25L));

            var result = _engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(
                _engine.State.GetInventorySnapshot()
                    .TryGet(Backpack, out var backpack),
                Is.True);
            Assert.That(
                backpack.Count(ContentIds.IronIngot).Value,
                Is.Zero);
            Assert.That(
                _engine.State.GetMachineSnapshot()
                    .TryGetNode(Crate, out var crate),
                Is.True);
            Assert.That(
                crate.Output.Count(ContentIds.IronIngot).Value,
                Is.EqualTo(25L));
#endif
        }

        [UnityTest]
        public IEnumerator TwoReleasedClicksMoveCrateStackToBackpackAuthoritatively()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The diagnostic UI prefab is editor-only test data.");
            yield break;
#else
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return null;
            yield return null;

            var root =
                _instance.GetComponent<UIDocument>().rootVisualElement;
            var source = root.Q<VisualElement>("crate-slot-0");
            var destination = root.Q<VisualElement>("player-slot-5");
            Assert.That(source, Is.Not.Null);
            Assert.That(destination, Is.Not.Null);
            Assert.That(source.worldBound.width, Is.GreaterThan(0f));
            Assert.That(destination.worldBound.width, Is.GreaterThan(0f));

            var from = ToScreen(
                source.worldBound.center,
                root.worldBound);
            var to = ToScreen(
                destination.worldBound.center,
                root.worldBound);
            yield return PhysicalClick(mouse, from);

            Assert.That(mouse.leftButton.isPressed, Is.False);
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty);
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack"),
                Is.Not.Null);

            Move(mouse.position, to, to - from);
            yield return null;
            Assert.That(mouse.leftButton.isPressed, Is.False);
            yield return PhysicalClick(mouse, to);

            var pending =
                _engine.State.GetAcceptedCommandsCanonical();
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(
                TransferCommandPayload.TryDecode(
                    pending[0],
                    out var sourceEndpoint,
                    out var destinationEndpoint,
                    out var item,
                    out var quantity),
                Is.True);
            Assert.That(
                sourceEndpoint,
                Is.EqualTo(
                    TransferEndpoint.Port(
                        Crate,
                        MachinePortKind.Storage)));
            Assert.That(
                destinationEndpoint,
                Is.EqualTo(TransferEndpoint.Inventory(Backpack)));
            Assert.That(item, Is.EqualTo(ContentIds.IronPlate));
            Assert.That(quantity.Value, Is.EqualTo(40L));

            var result = _engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(
                _engine.State.GetMachineSnapshot()
                    .TryGetNode(Crate, out var crate),
                Is.True);
            Assert.That(
                crate.Output.Count(ContentIds.IronPlate).Value,
                Is.Zero);
            Assert.That(
                _engine.State.GetInventorySnapshot()
                    .TryGet(Backpack, out var backpack),
                Is.True);
            Assert.That(
                backpack.Count(ContentIds.IronPlate).Value,
                Is.EqualTo(40L));
#endif
        }

        [UnityTest]
        public IEnumerator OutsideClickKeepsHeldStackAndEscapeCancelsWithoutACommand()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The diagnostic UI prefab is editor-only test data.");
            yield break;
#else
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            yield return null;

            var root =
                _instance.GetComponent<UIDocument>().rootVisualElement;
            var source = root.Q<VisualElement>("player-slot-0");
            var outside = root.Q<Label>("chest-title");
            Assert.That(source, Is.Not.Null);
            Assert.That(outside, Is.Not.Null);
            yield return PhysicalClick(
                mouse,
                ToScreen(source.worldBound.center, root.worldBound));

            yield return PhysicalClick(
                mouse,
                ToScreen(outside.worldBound.center, root.worldBound));
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack"),
                Is.Not.Null,
                "Clicking outside every slot must keep the stack held.");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty);

            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;

            Assert.That(_controller.PanelOpen, Is.True);
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack"),
                Is.Null);
            Assert.That(
                source.Q<VisualElement>(className: "slot-icon-host")
                    .resolvedStyle.visibility,
                Is.EqualTo(Visibility.Visible));
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty,
                "Cancelling a cursor stack must not touch authoritative state.");
#endif
        }

        private IEnumerator PhysicalClick(Mouse mouse, Vector2 position)
        {
            Move(mouse.position, position);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
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
    }
}

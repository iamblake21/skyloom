using System.Collections;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CML.Tests.Unity.Presentation
{
    /// <summary>
    /// UI-CONT-001. The panel is the first place the interface writes to the simulation,
    /// so the tests are about the write path: click-pick then click-drop produces a
    /// command, the rule decides, quantities are conserved, and opening or closing the
    /// panel changes nothing.
    /// </summary>
    public sealed class ChestHudTests
    {
        private const string PrefabPath = "Assets/_Project/Art/UI/Chest/PF_ChestHUD.prefab";
        private const string CratePrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_Crate.prefab";

        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Crate = new StableId(0x9600000000000000UL, 1UL);
        private static readonly StableId Backpack = new StableId(0x9600000000000000UL, 2UL);

        private GameCatalog _catalog;
        private GameObject _instance;
        private ChestHudController _controller;
        private TransferCommandBridge _bridge;
        private SimulationEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();

            var machines = new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Crate, ContentIds.WoodenCrate)
                .Store(Crate, ContentIds.IronPlate, 40)
                .Build();
            var inventories = InventorySimulationState.Create(
                _catalog,
                InventoryState.Restore(
                    Backpack,
                    _catalog,
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
                    Revision,
                    new AirshipSimulationState(),
                    machines,
                    inventories),
                null,
                _catalog);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"missing {PrefabPath}");
            _instance = Object.Instantiate(prefab);
            _controller = _instance.GetComponent<ChestHudController>();
            _bridge = _instance.GetComponent<TransferCommandBridge>();
            Assert.That(_controller, Is.Not.Null);
            Assert.That(_bridge, Is.Not.Null, "the prefab must carry its write path");

            _bridge.Attach(_engine, _catalog);
            _controller.Bind(Crate, Backpack);
            _controller.SetPanelOpen(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
            }
        }

        [Test]
        public void TheCratePanelShowsBothHoldersFromTheAuthoritativeState()
        {
            var root = _instance.GetComponent<UIDocument>().rootVisualElement;

            Assert.That(root.Q<Label>("chest-title").text, Is.EqualTo("CASSA DI LEGNO"));
            Assert.That(root.Q<Label>("crate-note").text, Does.StartWith("40 oggetti"));
            Assert.That(root.Q<Label>("player-note").text, Does.StartWith("25 oggetti"));
            Assert.That(root.Q<VisualElement>("crate-slot-0"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("player-slot-0"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator EverySlotResolvesInsideTheChestBackground()
        {
            _controller.SetPanelOpen(true);
            yield return null;
            yield return null;

            var root = _instance.GetComponent<UIDocument>().rootVisualElement;
            var panel = root.Q<VisualElement>("chest-panel");
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.worldBound.width, Is.GreaterThan(0f));
            Assert.That(panel.worldBound.height, Is.GreaterThan(0f));

            AssertSlotsInside(root, panel, "crate-slot", 24);
            AssertSlotsInside(root, panel, "player-slot", 16);
        }

        [Test]
        public void TwoClicksMoveACrateStackToTheBackpack()
        {
            Click("crate-slot-0");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty,
                "Picking a stack up must not submit a transfer.");
            Click("player-slot-5");
            Advance(1);
            _controller.Refresh();

            Assert.That(Crate40Plates(), Is.EqualTo(0L), "the crate should have given up its stack");
            Assert.That(BackpackPlates(), Is.EqualTo(40L));
            Assert.That(_engine.State.GetCommandRejectionsCanonical(), Is.Empty);
        }

        [Test]
        public void TwoClicksMoveABackpackStackToTheCrate()
        {
            Click("player-slot-0");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty,
                "Picking a stack up must not submit a transfer.");
            Click("crate-slot-5");
            Advance(1);
            _controller.Refresh();

            Assert.That(CrateIngots(), Is.EqualTo(25L));
            Assert.That(BackpackIngots(), Is.EqualTo(0L));
        }

        [Test]
        public void RightClickSplitsAndDepositsOneBeforeLeftPlacesTheRemainder()
        {
            var root = _instance.GetComponent<UIDocument>().rootVisualElement;

            Click("player-slot-0", button: 1);
            Assert.That(
                root.Q<VisualElement>("player-slot-0")
                    .Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("12"),
                "The source must show only the half that remains there.");
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack")
                    .Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("13"));

            Click("crate-slot-5", button: 1);
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack")
                    .Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("12"),
                "Right click deposits one and keeps the remainder held.");

            Click("crate-slot-5");
            Assert.That(
                root.Q<VisualElement>("chest-cursor-stack"),
                Is.Null);

            var pending = _engine.State.GetAcceptedCommandsCanonical();
            Assert.That(pending, Has.Count.EqualTo(2));
            Assert.That(
                TransferCommandPayload.TryDecode(
                    pending[0],
                    out _,
                    out _,
                    out _,
                    out var firstQuantity),
                Is.True);
            Assert.That(firstQuantity.Value, Is.EqualTo(1L));
            Assert.That(
                TransferCommandPayload.TryDecode(
                    pending[1],
                    out _,
                    out _,
                    out _,
                    out var remainingQuantity),
                Is.True);
            Assert.That(remainingQuantity.Value, Is.EqualTo(12L));

            Advance(1);
            _controller.Refresh();
            Assert.That(CrateIngots(), Is.EqualTo(13L));
            Assert.That(BackpackIngots(), Is.EqualTo(12L));
        }

        [Test]
        public void ShiftLeftQuickMovesTheLargestQuantityTheCrateCanTake()
        {
            Click("player-slot-0", shift: true);

            Assert.That(
                _instance.GetComponent<UIDocument>()
                    .rootVisualElement.Q<VisualElement>("chest-cursor-stack"),
                Is.Null,
                "A quick move must not create a cursor stack.");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Has.Count.EqualTo(1));

            Advance(1);
            _controller.Refresh();
            Assert.That(CrateIngots(), Is.EqualTo(25L));
            Assert.That(BackpackIngots(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator PickingACrateStackThenClickingTheBackpackQueuesOneAuthoritativeMove()
        {
            _controller.SetPanelOpen(true);
            yield return null;
            yield return null;

            Click("crate-slot-0");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty);
            Click("player-slot-5");
            AssertPendingTransfer(
                TransferEndpoint.Port(Crate, MachinePortKind.Storage),
                TransferEndpoint.Inventory(Backpack),
                ContentIds.IronPlate,
                40L);

            Advance(1);
            _controller.Refresh();
            Assert.That(Crate40Plates(), Is.Zero);
            Assert.That(BackpackPlates(), Is.EqualTo(40L));
        }

        [UnityTest]
        public IEnumerator PickingABackpackStackThenClickingTheCrateQueuesOneAuthoritativeMove()
        {
            _controller.SetPanelOpen(true);
            yield return null;
            yield return null;

            Click("player-slot-0");
            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty);
            Click("crate-slot-5");
            AssertPendingTransfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(Crate, MachinePortKind.Storage),
                ContentIds.IronIngot,
                25L);

            Advance(1);
            _controller.Refresh();
            Assert.That(CrateIngots(), Is.EqualTo(25L));
            Assert.That(BackpackIngots(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ClickingAnotherSlotInTheSameHolderKeepsTheStackOnTheCursor()
        {
            _controller.SetPanelOpen(true);
            yield return null;
            yield return null;

            Click("crate-slot-0");
            Click("crate-slot-5");

            Assert.That(
                _engine.State.GetAcceptedCommandsCanonical(),
                Is.Empty,
                "Only a slot in the other holder is a valid transfer destination.");
            Assert.That(
                _instance.GetComponent<UIDocument>()
                    .rootVisualElement.Q<VisualElement>("chest-cursor-stack"),
                Is.Not.Null,
                "An invalid destination must leave the stack on the cursor.");
            Assert.That(Crate40Plates(), Is.EqualTo(40L));
            Assert.That(BackpackPlates(), Is.Zero);
            Click("crate-slot-0");
            Assert.That(
                _instance.GetComponent<UIDocument>()
                    .rootVisualElement.Q<VisualElement>("chest-cursor-stack"),
                Is.Null,
                "Clicking the source slot again cancels the held stack.");
        }

        [Test]
        public void ClickingAnEmptySlotSubmitsNothing()
        {
            var before = LogicalStateHasher.ComputeHashHex(_engine.State);

            Click("crate-slot-5");
            Advance(1);

            // The tick still advanced, so the hash moves; what must not happen is a
            // command being queued for a slot that holds nothing.
            Assert.That(_engine.State.GetCommandRejectionsCanonical(), Is.Empty);
            Assert.That(Crate40Plates(), Is.EqualTo(40L));
            Assert.That(before, Is.Not.Null);
        }

        [Test]
        public void AMoveIsConservedAndSurvivesBeingClickedBackAndForth()
        {
            var total = Crate40Plates() + BackpackPlates();

            for (var round = 0; round < 12; round++)
            {
                if (round % 2 == 0)
                {
                    Click("crate-slot-0");
                    Click("player-slot-5");
                }
                else
                {
                    Click("player-slot-1");
                    Click("crate-slot-5");
                }

                Advance(1);
                _controller.Refresh();
                Assert.That(
                    Crate40Plates() + BackpackPlates(),
                    Is.EqualTo(total),
                    $"plates were lost or duplicated at round {round}");
            }
        }

        [Test]
        public void OpeningAndClosingThePanelDoesNotChangeTheLogicalState()
        {
            var before = LogicalStateHasher.ComputeHashHex(_engine.State);

            for (var repeat = 0; repeat < 5; repeat++)
            {
                _controller.SetPanelOpen(true);
                _controller.Refresh();
                _controller.SetPanelOpen(false);
                _controller.Refresh();
            }

            Assert.That(LogicalStateHasher.ComputeHashHex(_engine.State), Is.EqualTo(before));
        }

        [Test]
        public void WorldInteractionTargetsTheMatchingPhysicalCrateLid()
        {
            _controller.SetPanelOpen(false);
            var systems = new GameObject("ChestHudOrchestratorTest");
            var crateObject = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath));
            try
            {
                var hud = systems.AddComponent<FactoryHudOrchestrator>();
                hud.ConfigureUi(null, _controller, null, _bridge);
                hud.AttachSimulation(_engine, _catalog, Backpack);

                var target = crateObject.AddComponent<FactoryInteractionTarget>();
                target.Configure(
                    Crate,
                    FactoryInteractionKind.Chest,
                    "Apri Cassa di legno");

                Assert.That(hud.TryInteract(target), Is.True);
                var animator = crateObject.GetComponent<ChestLidAnimator>();
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.Lid, Is.Not.Null);
                Assert.That(animator.Hinge, Is.Not.Null);
                Assert.That(animator.Hinge.localPosition.x, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(animator.Hinge.localPosition.y, Is.EqualTo(0.54f).Within(0.0001f));
                Assert.That(animator.Hinge.localPosition.z, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(animator.TargetOpen, Is.True);

                Assert.That(hud.CloseInteractionPanel(), Is.True);
                Assert.That(animator.TargetOpen, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(crateObject);
                Object.DestroyImmediate(systems);
            }
        }

        [Test]
        public void TheHashAfterASequenceOfMovesDoesNotDependOnWhenThePanelWasOpen()
        {
            // Same two-click transfers, different opening pattern. The panel is a view: if it were
            // able to influence the result, this is where it would show.
            Click("crate-slot-0");
            Click("player-slot-5");
            Advance(1);
            Click("player-slot-0");
            Click("crate-slot-5");
            Advance(1);
            var quiet = LogicalStateHasher.ComputeHashHex(_engine.State);

            TearDown();
            SetUp();

            _controller.SetPanelOpen(true);
            Click("crate-slot-0");
            Click("player-slot-5");
            Advance(1);
            _controller.SetPanelOpen(false);
            _controller.SetPanelOpen(true);
            Click("player-slot-0");
            Click("crate-slot-5");
            Advance(1);
            _controller.SetPanelOpen(false);

            Assert.That(LogicalStateHasher.ComputeHashHex(_engine.State), Is.EqualTo(quiet));
        }

        [Test]
        public void APartialMoveFillsTheCrateAndKeepsTheRemainderOnTheCursor()
        {
            // A one-slot crate has room for five more ingots. The furnace-style
            // gesture moves exactly those five and keeps the other twenty held.
            TearDown();
            _catalog = BootstrapCatalog.Load();
            var machines = new MachineSimulationStateBuilder(_catalog)
                .AddNarrowBuffer(Crate, ContentIds.WoodenCrate, 1)
                .Store(Crate, ContentIds.IronIngot, 95)
                .Build();
            var inventories = InventorySimulationState.Create(
                _catalog,
                InventoryState.Restore(
                    Backpack,
                    _catalog,
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
                    Revision,
                    new AirshipSimulationState(),
                    machines,
                    inventories),
                null,
                _catalog);

            _instance = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            _controller = _instance.GetComponent<ChestHudController>();
            _bridge = _instance.GetComponent<TransferCommandBridge>();
            _bridge.Attach(_engine, _catalog);
            _controller.Bind(Crate, Backpack);
            _controller.SetPanelOpen(true);

            Click("player-slot-0");
            Click("crate-slot-0");

            var pending = _engine.State.GetAcceptedCommandsCanonical();
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.That(
                TransferCommandPayload.TryDecode(
                    pending[0],
                    out _,
                    out _,
                    out _,
                    out var quantity),
                Is.True);
            Assert.That(quantity.Value, Is.EqualTo(5L));
            Assert.That(
                _instance.GetComponent<UIDocument>()
                    .rootVisualElement
                    .Q<VisualElement>("chest-cursor-stack")
                    .Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("20"));

            Advance(1);

            _controller.Refresh();
            Assert.That(
                _engine.State.GetCommandRejectionsCanonical(),
                Is.Empty);
            Assert.That(CrateIngots(), Is.EqualTo(100L));
            Assert.That(BackpackIngots(), Is.EqualTo(20L));
            Assert.That(
                _instance.GetComponent<UIDocument>()
                    .rootVisualElement
                    .Q<VisualElement>("chest-cursor-stack")
                    .Q<Label>(className: "slot-quantity").text,
                Is.EqualTo("20"));
        }

        private void Click(
            string slotName,
            int button = 0,
            bool shift = false)
        {
            var root = _instance.GetComponent<UIDocument>().rootVisualElement;
            var slot = root.Q<VisualElement>(slotName);
            Assert.That(slot, Is.Not.Null, $"missing slot '{slotName}'");
            using (var pointer = PointerDownEvent.GetPooled(
                new Event
                {
                    type = EventType.MouseDown,
                    button = button,
                    modifiers = shift
                        ? EventModifiers.Shift
                        : EventModifiers.None,
                    mousePosition = slot.worldBound.center
                }))
            {
                pointer.target = slot;
                slot.SendEvent(pointer);
            }
        }

        private void AssertPendingTransfer(
            TransferEndpoint expectedSource,
            TransferEndpoint expectedDestination,
            StableId expectedItem,
            long expectedQuantity)
        {
            var pending = _engine.State.GetAcceptedCommandsCanonical();
            Assert.That(
                pending,
                Has.Count.EqualTo(1),
                "The second click must submit exactly one command.");
            Assert.That(
                TransferCommandPayload.TryDecode(
                    pending[0],
                    out var source,
                    out var destination,
                    out var item,
                    out var quantity),
                Is.True);
            Assert.That(source, Is.EqualTo(expectedSource));
            Assert.That(destination, Is.EqualTo(expectedDestination));
            Assert.That(item, Is.EqualTo(expectedItem));
            Assert.That(quantity.Value, Is.EqualTo(expectedQuantity));
        }

        private static void AssertSlotsInside(
            VisualElement root,
            VisualElement panel,
            string prefix,
            int slotCount)
        {
            var panelBounds = panel.worldBound;
            for (var index = 0; index < slotCount; index++)
            {
                var slot = root.Q<VisualElement>($"{prefix}-{index}");
                Assert.That(slot, Is.Not.Null, $"missing {prefix}-{index}");
                Assert.That(slot.worldBound.width, Is.GreaterThan(0f));
                Assert.That(slot.worldBound.height, Is.GreaterThan(0f));
                Assert.That(
                    panelBounds.Contains(slot.worldBound.min),
                    Is.True,
                    $"{slot.name} begins outside the chest background");
                Assert.That(
                    panelBounds.Contains(slot.worldBound.max - Vector2.one * 0.01f),
                    Is.True,
                    $"{slot.name} protrudes beyond the chest background");
            }
        }

        private void Advance(int ticks)
        {
            for (var index = 0; index < ticks; index++)
            {
                var result = _engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"tick aborted in {result.FailedPhase}: {result.FailureCause}");
            }
        }

        private long Crate40Plates()
        {
            Assert.That(
                _engine.State.GetMachineSnapshot().TryGetNode(Crate, out var node),
                Is.True);
            return node.Output.Count(ContentIds.IronPlate).Value;
        }

        private long CrateIngots()
        {
            Assert.That(
                _engine.State.GetMachineSnapshot().TryGetNode(Crate, out var node),
                Is.True);
            return node.Output.Count(ContentIds.IronIngot).Value;
        }

        private long BackpackPlates()
        {
            Assert.That(
                _engine.State.GetInventorySnapshot().TryGet(Backpack, out var inventory),
                Is.True);
            return inventory.Count(ContentIds.IronPlate).Value;
        }

        private long BackpackIngots()
        {
            Assert.That(
                _engine.State.GetInventorySnapshot().TryGet(Backpack, out var inventory),
                Is.True);
            return inventory.Count(ContentIds.IronIngot).Value;
        }
    }
}

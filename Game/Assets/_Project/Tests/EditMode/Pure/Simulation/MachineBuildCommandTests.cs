using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class MachineBuildCommandTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Builder =
            new StableId(0xB017D00000000000UL, 1UL);

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void SpatialModulePayloadRoundTripsWithoutEndpoints()
        {
            var pose = new MachineBuildPose(-1250, 500, 9876, 3);
            var expected = MachineBuildSpecification.BeltModule(
                ContentIds.BeltStraight,
                pose);
            var command = Command(new SimulationTick(1UL), 0UL, expected);

            Assert.That(command.Payload.Count, Is.EqualTo(MachineBuildCommandPayload.Length));
            Assert.That(MachineBuildCommandPayload.TryDecode(command, out var actual), Is.True);
            Assert.That(actual.Kind, Is.EqualTo(MachineBuildKind.BeltModule));
            Assert.That(actual.PrimaryId, Is.EqualTo(ContentIds.BeltStraight));
            Assert.That(actual.SecondaryId, Is.EqualTo(StableId.None));
            Assert.That(actual.TertiaryId, Is.EqualTo(StableId.None));
            Assert.That(actual.CostItemId, Is.EqualTo(ContentIds.BeltStraight));
            Assert.That(actual.CostQuantity, Is.EqualTo(1L));
            Assert.That(actual.LengthMillimetres, Is.Zero);
            Assert.That(actual.SpeedMillimetresPerTick, Is.Zero);
            Assert.That(actual.SpacingMillimetres, Is.Zero);
            Assert.That(actual.Pose, Is.EqualTo(pose));
            Assert.That(
                actual.BeltTravelDirection,
                Is.EqualTo(BeltTravelDirection.Forward));
        }

        [Test]
        public void CommandsCreateOnlyPlacedNodesForTheWholePhysicalLine()
        {
            var engine = NewEngine(
                new MachineSimulationState(),
                Stack(0, ContentIds.WoodenCrateItem, 2L),
                Stack(1, ContentIds.MechanicalPressItem, 1L),
                Stack(2, ContentIds.BeltFunnel, 2L),
                Stack(3, ContentIds.BeltStraight, 8L));

            var ids = new StableId[13];
            ids[0] = BuildAndResolve(
                engine,
                MachineBuildSpecification.Buffer(
                    ContentIds.WoodenCrate,
                    ContentIds.WoodenCrateItem,
                    1L,
                    Pose(-7, 0)));
            ids[1] = BuildAndResolve(
                engine,
                MachineBuildSpecification.Funnel(
                    ContentIds.BeltFunnel,
                    Pose(-6, 0)));
            for (var z = -5; z <= -2; z++)
            {
                ids[z + 7] = BuildAndResolve(
                    engine,
                    MachineBuildSpecification.BeltModule(
                        ContentIds.BeltStraight,
                        Pose(z, 0)));
            }

            ids[6] = BuildAndResolve(
                engine,
                MachineBuildSpecification.Machine(
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    ContentIds.MechanicalPressItem,
                    1L,
                    Pose(-1, 0)));
            for (var z = 0; z <= 3; z++)
            {
                ids[z + 7] = BuildAndResolve(
                    engine,
                    MachineBuildSpecification.BeltModule(
                        ContentIds.BeltStraight,
                        Pose(z, 0)));
            }

            ids[11] = BuildAndResolve(
                engine,
                MachineBuildSpecification.Funnel(
                    ContentIds.BeltFunnel,
                    Pose(4, 2)));
            ids[12] = BuildAndResolve(
                engine,
                MachineBuildSpecification.Buffer(
                    ContentIds.WoodenCrate,
                    ContentIds.WoodenCrateItem,
                    1L,
                    Pose(5, 0)));

            var graph = engine.State.GetMachineSnapshot();
            Assert.That(graph.NodeCount, Is.EqualTo(13));
            Assert.That(graph.LaneCount, Is.Zero, "spatial adjacency must not persist edges");
            for (var index = 0; index < ids.Length; index++)
            {
                Assert.That(graph.TryGetNode(ids[index], out var node), Is.True);
                Assert.That(node.HasPlacementPose, Is.True);
                Assert.That(node.AttachedNodeId, Is.EqualTo(StableId.None));
            }

            Assert.That(InventoryCount(engine, ContentIds.WoodenCrateItem), Is.Zero);
            Assert.That(InventoryCount(engine, ContentIds.MechanicalPressItem), Is.Zero);
            Assert.That(InventoryCount(engine, ContentIds.BeltFunnel), Is.Zero);
            Assert.That(InventoryCount(engine, ContentIds.BeltStraight), Is.Zero);
        }

        [Test]
        public void ADisconnectedFunnelCanBePlacedButPersistsNoAttachment()
        {
            var engine = NewEngine(
                new MachineSimulationState(),
                Stack(0, ContentIds.BeltFunnel, 1L));

            var id = BuildAndResolve(
                engine,
                MachineBuildSpecification.Funnel(
                    ContentIds.BeltFunnel,
                    Pose(0, 0)));

            var graph = engine.State.GetMachineSnapshot();
            Assert.That(graph.TryGetNode(id, out var node), Is.True);
            Assert.That(node.Kind, Is.EqualTo(MachineNodeKind.Funnel));
            Assert.That(node.HasPlacementPose, Is.True);
            Assert.That(node.AttachedNodeId, Is.EqualTo(StableId.None));
            Assert.That(graph.LaneCount, Is.Zero);
        }

        [Test]
        public void OccupiedPlacementCellRejectsBeforeConsumingTheHeldModule()
        {
            var occupied = new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(
                    new StableId(0xB017D00000000000UL, 10UL),
                    ContentIds.WoodenCrate,
                    Pose(0, 0))
                .Build();
            var engine = NewEngine(
                occupied,
                Stack(0, ContentIds.BeltStraight, 1L));
            var command = Command(
                new SimulationTick(1UL),
                0UL,
                MachineBuildSpecification.BeltModule(
                    ContentIds.BeltStraight,
                    Pose(0, 1)));

            engine.EnqueueCommand(command);
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.GetMachineSnapshot().NodeCount, Is.EqualTo(1));
            Assert.That(engine.State.GetCreationRecordsCanonical(), Is.Empty);
            Assert.That(
                LastRejection(engine),
                Is.EqualTo(CommandRejectionReason.BuildTopologyInvalid));
            Assert.That(InventoryCount(engine, ContentIds.BeltStraight), Is.EqualTo(1L));
        }

        [Test]
        public void MissingPlaceableItemRejectsWithoutCreatingAnything()
        {
            var engine = NewEngine(new MachineSimulationState());
            var command = Command(
                new SimulationTick(1UL),
                0UL,
                MachineBuildSpecification.Buffer(
                    ContentIds.WoodenCrate,
                    ContentIds.WoodenCrateItem,
                    1L,
                    Pose(0, 0)));

            engine.EnqueueCommand(command);
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.GetMachineSnapshot().NodeCount, Is.Zero);
            Assert.That(engine.State.GetCreationRecordsCanonical(), Is.Empty);
            Assert.That(LastRejection(engine), Is.EqualTo(CommandRejectionReason.InsufficientQuantity));
        }

        [Test]
        public void TwoBuildCommandsCompetingForOneInventoryItemCreateOnlyOne()
        {
            var engine = NewEngine(
                new MachineSimulationState(),
                Stack(0, ContentIds.BeltStraight, 1L));
            var tick = new SimulationTick(1UL);
            var first = Command(
                tick,
                0UL,
                MachineBuildSpecification.BeltModule(ContentIds.BeltStraight, Pose(0, 0)));
            var second = Command(
                tick,
                1UL,
                MachineBuildSpecification.BeltModule(ContentIds.BeltStraight, Pose(1, 0)));

            engine.EnqueueCommand(first);
            engine.EnqueueCommand(second);
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.GetMachineSnapshot().NodeCount, Is.EqualTo(1));
            Assert.That(engine.State.GetCreationRecordsCanonical().Count, Is.EqualTo(1));
            Assert.That(
                engine.State.TryGetCreatedEntityId(
                    tick,
                    MachineBuildCommandPayload.CreationKey(first),
                    out _),
                Is.True);
            Assert.That(
                engine.State.TryGetCreatedEntityId(
                    tick,
                    MachineBuildCommandPayload.CreationKey(second),
                    out _),
                Is.False);
            Assert.That(LastRejection(engine), Is.EqualTo(CommandRejectionReason.InsufficientQuantity));
            Assert.That(InventoryCount(engine, ContentIds.BeltStraight), Is.Zero);
        }

        [Test]
        public void EveryBeltModuleConsumesExactlyOneStraight()
        {
            var engine = NewEngine(
                new MachineSimulationState(),
                Stack(0, ContentIds.BeltStraight, 2L));

            var belt = BuildAndResolve(
                engine,
                MachineBuildSpecification.BeltModule(
                    ContentIds.BeltStraight,
                    Pose(0, 0)));

            var graph = engine.State.GetMachineSnapshot();
            Assert.That(graph.TryGetNode(belt, out var node), Is.True);
            Assert.That(node.Kind, Is.EqualTo(MachineNodeKind.BeltModule));
            Assert.That(InventoryCount(engine, ContentIds.BeltStraight), Is.EqualTo(1L));
        }

        [Test]
        public void SecondBeltWithoutASecondItemRejectsWithoutPartialMutation()
        {
            var engine = NewEngine(
                new MachineSimulationState(),
                Stack(0, ContentIds.BeltStraight, 1L));
            BuildAndResolve(
                engine,
                MachineBuildSpecification.BeltModule(
                    ContentIds.BeltStraight,
                    Pose(0, 0)));
            var command = Command(
                engine.State.Tick.Next(),
                0UL,
                MachineBuildSpecification.BeltModule(
                    ContentIds.BeltStraight,
                    Pose(1, 0)));

            engine.EnqueueCommand(command);
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.GetMachineSnapshot().NodeCount, Is.EqualTo(1));
            Assert.That(LastRejection(engine), Is.EqualTo(CommandRejectionReason.InsufficientQuantity));
            Assert.That(InventoryCount(engine, ContentIds.BeltStraight), Is.Zero);
        }

        [Test]
        public void MalformedPayloadIsRefusedAtIngress()
        {
            var malformed = new SimulationCommand(
                new SimulationTick(1UL),
                0UL,
                SimulationCommandKinds.BuildMachineGraphElement,
                Builder,
                StableId.None,
                0L,
                new byte[MachineBuildCommandPayload.Length - 1]);
            var engine = NewEngine(new MachineSimulationState());

            Assert.Throws<ArgumentException>(() => engine.EnqueueCommand(malformed));
            Assert.That(engine.State.PendingCommandCount, Is.Zero);
        }

        private StableId BuildAndResolve(
            SimulationEngine engine,
            MachineBuildSpecification specification)
        {
            var tick = engine.State.Tick.Next();
            var command = Command(tick, 0UL, specification);
            engine.EnqueueCommand(command);
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(
                engine.State.TryGetCreatedEntityId(
                    tick,
                    MachineBuildCommandPayload.CreationKey(command),
                    out var createdId),
                Is.True);
            Assert.That(createdId.IsNone, Is.False);
            return createdId;
        }

        private SimulationEngine NewEngine(
            MachineSimulationState machines,
            params InventoryStackRecord[] stacks)
        {
            var inventory = InventoryState.Restore(
                Builder,
                _catalog,
                ContentIds.PlayerInventory,
                stacks ?? Array.Empty<InventoryStackRecord>());
            var inventories = InventorySimulationState.Create(_catalog, inventory);
            return new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    Revision,
                    new AirshipSimulationState(),
                    machines ?? new MachineSimulationState(),
                    inventories),
                null,
                _catalog);
        }

        private static MachineBuildPose Pose(int zCell, byte yaw) =>
            new MachineBuildPose(
                0,
                0,
                checked(zCell * MachineSpatialTopology.GridCellSizeMillimetres),
                yaw);

        private static InventoryStackRecord Stack(
            int slotIndex,
            StableId itemId,
            long quantity) =>
            new InventoryStackRecord(
                slotIndex,
                itemId,
                new NonNegativeQuantity(quantity));

        private static long InventoryCount(SimulationEngine engine, StableId itemId)
        {
            Assert.That(
                engine.State.GetInventorySnapshot().TryGet(Builder, out var inventory),
                Is.True);
            return inventory.Count(itemId).Value;
        }

        private static CommandRejectionReason LastRejection(SimulationEngine engine)
        {
            var rejections = engine.State.GetCommandRejectionsCanonical();
            Assert.That(rejections, Is.Not.Empty);
            return rejections[rejections.Count - 1].Reason;
        }

        private static SimulationCommand Command(
            SimulationTick tick,
            ulong sequence,
            MachineBuildSpecification specification) =>
            new SimulationCommand(
                tick,
                sequence,
                SimulationCommandKinds.BuildMachineGraphElement,
                Builder,
                StableId.None,
                0L,
                MachineBuildCommandPayload.Encode(specification));
    }
}

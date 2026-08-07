using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class SpatialLogisticsModuleTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Source = Id(1);
        private static readonly StableId ExtractFunnel = Id(2);
        private static readonly StableId FeedBeltA = Id(3);
        private static readonly StableId FeedBeltB = Id(4);
        private static readonly StableId Press = Id(5);
        private static readonly StableId DrainBeltA = Id(6);
        private static readonly StableId DrainBeltB = Id(7);
        private static readonly StableId InsertFunnel = Id(8);
        private static readonly StableId Sink = Id(9);
        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void CompletePlacedLineTurnsOneIngotIntoOnePlate()
        {
            var engine = NewEngine(CompleteLine(1));

            Advance(engine, 200);

            Assert.That(Held(engine, Source, ContentIds.IronIngot), Is.Zero);
            Assert.That(Held(engine, Sink, ContentIds.IronPlate), Is.EqualTo(1L));
            Assert.That(Node(engine, Press).CompletedCycles, Is.EqualTo(1UL));
            Assert.That(Node(engine, Press).IsCycleActive, Is.False);
            Assert.That(engine.State.GetMachineSnapshot().LaneCount, Is.Zero);
            Assert.That(
                CountAcrossNodes(engine, ContentIds.IronIngot)
                + CountAcrossNodes(engine, ContentIds.IronPlate),
                Is.EqualTo(1L),
                "the ingot may change definition, but no unit may appear or disappear");
        }

        [Test]
        public void BeltDriveDirectionDecidesFunnelRole()
        {
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Source, ContentIds.WoodenCrate, Pose(2, 0))
                .AddFunnel(ExtractFunnel, ContentIds.BeltFunnel, Pose(1, 2))
                .AddBeltModule(
                    FeedBeltA,
                    ContentIds.BeltDriveUnit,
                    Pose(0, 2))
                .Store(Source, ContentIds.IronIngot, 1)
                .Build();
            var engine = NewEngine(state);

            Advance(engine, 1);

            Assert.That(Held(engine, Source, ContentIds.IronIngot), Is.Zero);
            Assert.That(Held(engine, FeedBeltA, ContentIds.IronIngot), Is.EqualTo(1L));
            Assert.That(
                MachineSpatialTopology.TryGetBeltTravelYaw(
                    Node(engine, FeedBeltA),
                    out var travelYaw),
                Is.True);
            Assert.That(travelYaw, Is.EqualTo(2));
        }

        [Test]
        public void FunnelWithAGapOnEitherSideIsInert()
        {
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Source, ContentIds.WoodenCrate, Pose(0, 0))
                .AddFunnel(ExtractFunnel, ContentIds.BeltFunnel, Pose(1, 0))
                // The belt is two cells away: there is a real empty metre at z=2.
                .AddBeltModule(FeedBeltA, ContentIds.BeltStraight, Pose(3, 0))
                .Store(Source, ContentIds.IronIngot, 3)
                .Build();
            var engine = NewEngine(state);

            Advance(engine, 100);

            Assert.That(Held(engine, Source, ContentIds.IronIngot), Is.EqualTo(3L));
            Assert.That(Held(engine, ExtractFunnel, ContentIds.IronIngot), Is.Zero);
            Assert.That(Held(engine, FeedBeltA, ContentIds.IronIngot), Is.Zero);
        }

        [Test]
        public void BufferNeverLoadsABeltWithoutAFunnel()
        {
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Source, ContentIds.WoodenCrate, Pose(0, 0))
                .AddBeltModule(FeedBeltA, ContentIds.BeltStraight, Pose(1, 0))
                .AddMachine(
                    Press,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    Pose(2, 0))
                .Store(Source, ContentIds.IronIngot, 2)
                .Build();
            var engine = NewEngine(state);

            Advance(engine, 100);

            Assert.That(Held(engine, Source, ContentIds.IronIngot), Is.EqualTo(2L));
            Assert.That(Held(engine, FeedBeltA, ContentIds.IronIngot), Is.Zero);
            Assert.That(Node(engine, Press).CompletedCycles, Is.Zero);
        }

        [Test]
        public void WrongFacingDisconnectsOtherwiseAdjacentModules()
        {
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Source, ContentIds.WoodenCrate, Pose(0, 0))
                .AddFunnel(ExtractFunnel, ContentIds.BeltFunnel, Pose(1, 0))
                .AddBeltModule(FeedBeltA, ContentIds.BeltStraight, Pose(2, 1))
                .Store(Source, ContentIds.IronIngot, 2)
                .Build();
            var engine = NewEngine(state);

            Advance(engine, 100);

            Assert.That(Held(engine, Source, ContentIds.IronIngot), Is.EqualTo(2L));
            Assert.That(Held(engine, ExtractFunnel, ContentIds.IronIngot), Is.Zero);
            Assert.That(Held(engine, FeedBeltA, ContentIds.IronIngot), Is.Zero);
        }

        [Test]
        public void BeltDoesNotAdvanceCargoIntoEmptySpace()
        {
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(FeedBeltA, ContentIds.BeltStraight, Pose(0, 0))
                .AddBeltModule(FeedBeltB, ContentIds.BeltStraight, Pose(2, 0))
                .Store(FeedBeltA, ContentIds.IronIngot, 1)
                .Build();
            var engine = NewEngine(state);

            Advance(engine, 100);

            Assert.That(Held(engine, FeedBeltA, ContentIds.IronIngot), Is.EqualTo(1L));
            Assert.That(Node(engine, FeedBeltA).TransportProgressMillimetres, Is.Zero);
            Assert.That(Held(engine, FeedBeltB, ContentIds.IronIngot), Is.Zero);
        }

        [Test]
        public void FullSinkBacksUpOneVisibleWorkpiecePerModuleWithoutLoss()
        {
            var front = Id(20);
            var middle = Id(21);
            var rear = Id(22);
            var funnel = Id(23);
            var sink = Id(24);
            var drive = Id(26);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(
                    drive,
                    ContentIds.BeltDriveUnit,
                    Pose(-1, 0))
                .AddBeltModule(rear, ContentIds.BeltStraight, Pose(0, 0))
                .AddBeltModule(middle, ContentIds.BeltStraight, Pose(1, 0))
                .AddBeltModule(front, ContentIds.BeltStraight, Pose(2, 0))
                .AddFunnel(funnel, ContentIds.BeltFunnel, Pose(3, 2))
                .AddNarrowBuffer(sink, ContentIds.WoodenCrate, 1, Pose(4, 0))
                .Store(rear, ContentIds.IronPlate, 1)
                .Store(middle, ContentIds.IronPlate, 1)
                .Store(front, ContentIds.IronPlate, 1)
                .Store(sink, ContentIds.IronPlate, 100)
                .Build();
            var engine = NewEngine(state);

            Advance(engine, 100);

            Assert.That(Held(engine, sink, ContentIds.IronPlate), Is.EqualTo(100L));
            Assert.That(Held(engine, funnel, ContentIds.IronPlate), Is.EqualTo(1L));
            Assert.That(
                Held(engine, rear, ContentIds.IronPlate)
                + Held(engine, middle, ContentIds.IronPlate)
                + Held(engine, front, ContentIds.IronPlate),
                Is.EqualTo(2L));
            Assert.That(CountAcrossNodes(engine, ContentIds.IronPlate), Is.EqualTo(103L));
        }

        [Test]
        public void PressAcceptsNoSecondIngotWhileItsRamCycleIsActive()
        {
            var engine = NewEngine(CompleteLine(3));

            for (var tick = 0; tick < 80 && !Node(engine, Press).IsCycleActive; tick++)
            {
                Advance(engine, 1);
            }

            var started = Node(engine, Press);
            Assert.That(started.IsCycleActive, Is.True);
            Assert.That(started.Input.Count(ContentIds.IronIngot).Value, Is.Zero);
            var completed = started.CompletedCycles;

            Advance(engine, 20);

            var active = Node(engine, Press);
            Assert.That(active.IsCycleActive, Is.True);
            Assert.That(active.CompletedCycles, Is.EqualTo(completed));
            Assert.That(active.Input.Count(ContentIds.IronIngot).Value, Is.Zero);
            Assert.That(
                CountAcrossNodes(engine, ContentIds.IronIngot)
                + CountAcrossNodes(engine, ContentIds.IronPlate)
                + (active.IsCycleActive ? 1L : 0L),
                Is.EqualTo(3L));
        }

        [Test]
        public void ConstructionOrderCannotChangeSpatialFlowOrCanonicalBytes()
        {
            var first = NewEngine(DeterministicGraph(false));
            var second = NewEngine(DeterministicGraph(true));

            Advance(first, 37);
            Advance(second, 37);

            Assert.That(
                MachineCanonicalSerializer.Serialize(first.State.GetMachineSnapshot()),
                Is.EqualTo(
                    MachineCanonicalSerializer.Serialize(second.State.GetMachineSnapshot())));
        }

        [Test]
        public void PoseAndBeltProgressAreCanonicalButDerivedEdgesAreNotPersistent()
        {
            var first = NewEngine(
                new MachineSimulationStateBuilder(_catalog)
                    .AddMachine(
                        Press,
                        ContentIds.MechanicalPress,
                        ContentIds.PressIronPlate,
                        Pose(1, 0))
                    .AddBeltModule(FeedBeltA, ContentIds.BeltDriveUnit, Pose(2, 0))
                    .Store(FeedBeltA, ContentIds.IronIngot, 1)
                    .Build());
            var rotated = NewEngine(
                new MachineSimulationStateBuilder(_catalog)
                    .AddMachine(
                        Press,
                        ContentIds.MechanicalPress,
                        ContentIds.PressIronPlate,
                        Pose(1, 0))
                    .AddBeltModule(FeedBeltA, ContentIds.BeltDriveUnit, Pose(2, 1))
                    .Store(FeedBeltA, ContentIds.IronIngot, 1)
                    .Build());

            Advance(first, 3);

            Assert.That(Node(first, FeedBeltA).TransportProgressMillimetres, Is.EqualTo(300));
            Assert.That(first.State.GetMachineSnapshot().LaneCount, Is.Zero);
            Assert.That(rotated.State.GetMachineSnapshot().LaneCount, Is.Zero);
            Assert.That(
                MachineCanonicalSerializer.Serialize(first.State.GetMachineSnapshot()),
                Is.Not.EqualTo(
                    MachineCanonicalSerializer.Serialize(rotated.State.GetMachineSnapshot())));
        }

        private MachineSimulationState CompleteLine(long ingots)
        {
            return new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Source, ContentIds.WoodenCrate, Pose(0, 0))
                .AddFunnel(ExtractFunnel, ContentIds.BeltFunnel, Pose(1, 0))
                .AddBeltModule(
                    FeedBeltA,
                    ContentIds.BeltDriveUnit,
                    Pose(2, 0))
                .AddBeltModule(FeedBeltB, ContentIds.BeltStraight, Pose(3, 0))
                .AddMachine(
                    Press,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    Pose(4, 0))
                .AddBeltModule(DrainBeltA, ContentIds.BeltStraight, Pose(5, 0))
                .AddBeltModule(DrainBeltB, ContentIds.BeltStraight, Pose(6, 0))
                .AddFunnel(InsertFunnel, ContentIds.BeltFunnel, Pose(7, 2))
                .AddBuffer(Sink, ContentIds.WoodenCrate, Pose(8, 0))
                .Store(Source, ContentIds.IronIngot, ingots)
                .Build();
        }

        private MachineSimulationState DeterministicGraph(bool reverse)
        {
            var builder = new MachineSimulationStateBuilder(_catalog);
            if (reverse)
            {
                builder
                    .AddMachine(
                        Press,
                        ContentIds.MechanicalPress,
                        ContentIds.PressIronPlate,
                        Pose(3, 0))
                    .AddBeltModule(FeedBeltB, ContentIds.BeltStraight, Pose(2, 0))
                    .AddBeltModule(FeedBeltA, ContentIds.BeltDriveUnit, Pose(1, 0))
                    .AddFunnel(ExtractFunnel, ContentIds.BeltFunnel, Pose(0, 0))
                    .AddBuffer(Source, ContentIds.WoodenCrate, Pose(-1, 0));
            }
            else
            {
                builder
                    .AddBuffer(Source, ContentIds.WoodenCrate, Pose(-1, 0))
                    .AddFunnel(ExtractFunnel, ContentIds.BeltFunnel, Pose(0, 0))
                    .AddBeltModule(FeedBeltA, ContentIds.BeltDriveUnit, Pose(1, 0))
                    .AddBeltModule(FeedBeltB, ContentIds.BeltStraight, Pose(2, 0))
                    .AddMachine(
                        Press,
                        ContentIds.MechanicalPress,
                        ContentIds.PressIronPlate,
                        Pose(3, 0));
            }

            return builder.Store(Source, ContentIds.IronIngot, 2).Build();
        }

        private SimulationEngine NewEngine(MachineSimulationState state)
        {
            return new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    Revision,
                    new AirshipSimulationState(),
                    state),
                null,
                _catalog);
        }

        private static void Advance(SimulationEngine engine, int ticks)
        {
            for (var index = 0; index < ticks; index++)
            {
                var result = engine.AdvanceOneTick();
                Assert.That(result.Committed, Is.True, result.FailureCause);
            }
        }

        private static MachineNodeState Node(SimulationEngine engine, StableId id)
        {
            Assert.That(engine.State.GetMachineSnapshot().TryGetNode(id, out var node), Is.True);
            return node;
        }

        private static long Held(SimulationEngine engine, StableId nodeId, StableId itemId) =>
            Node(engine, nodeId).Input.Count(itemId).Value;

        private static long CountAcrossNodes(SimulationEngine engine, StableId itemId)
        {
            var graph = engine.State.GetMachineSnapshot();
            var total = 0L;
            var ids = graph.GetNodeIdsCanonical();
            for (var index = 0; index < ids.Count; index++)
            {
                Assert.That(graph.TryGetNode(ids[index], out var node), Is.True);
                total = checked(total + node.Input.Count(itemId).Value);
                if (!ReferenceEquals(node.Input, node.Output))
                {
                    total = checked(total + node.Output.Count(itemId).Value);
                }
            }

            return total;
        }

        private static MachineBuildPose Pose(int zCell, byte yaw) =>
            new MachineBuildPose(
                0,
                0,
                checked(zCell * MachineSpatialTopology.GridCellSizeMillimetres),
                yaw);

        private static MachineBuildPose PoseXZ(
            int xCell,
            int zCell,
            byte yaw) =>
            new MachineBuildPose(
                checked(xCell * MachineSpatialTopology.GridCellSizeMillimetres),
                0,
                checked(zCell * MachineSpatialTopology.GridCellSizeMillimetres),
                yaw);

        private static StableId Id(ulong low) =>
            new StableId(0xA510000000000000UL, low);
    }
}

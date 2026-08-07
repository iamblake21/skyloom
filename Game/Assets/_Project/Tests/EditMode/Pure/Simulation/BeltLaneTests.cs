using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    /// <summary>
    /// LOG-001. A lane has length, so an item takes time to cross; it has spacing, so
    /// there is a throughput ceiling; and it holds its cargo, so a refused destination
    /// makes a queue instead of a number that stops moving.
    /// </summary>
    public sealed class BeltLaneTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Source = new StableId(0x9700000000000000UL, 1UL);
        private static readonly StableId Sink = new StableId(0x9700000000000000UL, 2UL);
        private static readonly StableId SecondSink = new StableId(0x9700000000000000UL, 3UL);
        private static readonly StableId Funnel = new StableId(0x9700000000000000UL, 4UL);
        private static readonly StableId SinkFunnel = new StableId(0x9700000000000000UL, 5UL);
        private static readonly StableId SecondSinkFunnel =
            new StableId(0x9700000000000000UL, 6UL);
        private static readonly StableId DetachedFunnel =
            new StableId(0x9700000000000000UL, 7UL);
        private static readonly StableId Lane = new StableId(0x9710000000000000UL, 1UL);
        private static readonly StableId SecondLane = new StableId(0x9710000000000000UL, 2UL);

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void AnItemCrossesAndTheInsertionFunnelDepositsItInTheCrate()
        {
            // 1000 mm at 100 mm per tick: ten ticks to cross, plus the tick that loads it.
            var engine = NewEngine(
                Graph()
                    .Store(Source, ContentIds.IronPlate, 1)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 1000, 100, 250)
                    .Build());

            Advance(engine, 1);
            Assert.That(LaneOf(engine).ItemCount, Is.EqualTo(1), "the item should be aboard");
            Assert.That(LaneOf(engine).Items[0].PositionMillimetres, Is.EqualTo(0));
            Assert.That(Held(engine, Source, ContentIds.IronPlate), Is.EqualTo(0L));
            Assert.That(Held(engine, Sink, ContentIds.IronPlate), Is.EqualTo(0L));

            Advance(engine, 5);
            Assert.That(LaneOf(engine).Items[0].PositionMillimetres, Is.EqualTo(500));
            Assert.That(
                Held(engine, Sink, ContentIds.IronPlate),
                Is.EqualTo(0L),
                "an item in transit has not arrived: that is the whole point of a length");

            Advance(engine, 5);
            Assert.That(LaneOf(engine).ItemCount, Is.EqualTo(0));
            Assert.That(Held(engine, Sink, ContentIds.IronPlate), Is.EqualTo(1L));
            Assert.That(LaneOf(engine).DeliveredUnits, Is.EqualTo(1UL));
        }

        [Test]
        public void TheDeclaredLatencyIsTheLatencyMeasured()
        {
            var engine = NewEngine(
                Graph()
                    .Store(Source, ContentIds.IronPlate, 1)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 950, 100, 250)
                    .Build());

            var declared = LaneOf(engine).LatencyTicks;
            Assert.That(declared, Is.EqualTo(10), "950 mm at 100 mm per tick rounds up to 10");

            Advance(engine, 1); // loads at position 0
            var ticks = 0;
            while (Held(engine, Sink, ContentIds.IronPlate) == 0L && ticks < 100)
            {
                Advance(engine, 1);
                ticks++;
            }

            Assert.That(ticks, Is.EqualTo(declared));
        }

        [Test]
        public void TheMeasuredThroughputMatchesTheDeclaredOne()
        {
            // Spacing 250 at 100 mm per tick: an item can enter once every three ticks,
            // so the ceiling is 333 per thousand ticks.
            var engine = NewEngine(
                Graph()
                    .Store(Source, ContentIds.IronPlate, 100)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 1000, 100, 250)
                    .Build());

            var declared = LaneOf(engine).ThroughputPerThousandTicks;
            Assert.That(declared, Is.EqualTo(333));

            const int ticks = 300;
            Advance(engine, ticks);

            var delivered = (long)LaneOf(engine).DeliveredUnits;
            var expected = (long)declared * ticks / 1000L;

            // The tolerance is the quantisation the acceptance allows: the run starts empty
            // so the first item pays the latency, and the last few are still in transit.
            Assert.That(
                delivered,
                Is.EqualTo(expected).Within(LaneOf(engine).LatencyTicks / 3 + 2),
                $"declared {declared} per 1000 ticks, delivered {delivered} in {ticks}");
        }

        [Test]
        public void ARefusedDestinationMakesAQueueFromTheExitBackwards()
        {
            // A one-slot sink already full: nothing can be delivered, so the lane fills up
            // and stops taking. On a logical link this was a number that stopped moving;
            // here the items are somewhere, and that somewhere is the lane.
            var engine = NewEngine(
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddNarrowBuffer(Sink, ContentIds.WoodenCrate, 1)
                    .AddFunnel(Funnel, ContentIds.BeltFunnel, Source)
                    .AddFunnel(SinkFunnel, ContentIds.BeltFunnel, Sink)
                    .Store(Source, ContentIds.IronPlate, 40)
                    .Store(Sink, ContentIds.IronPlate, 100)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 1000, 100, 250)
                    .Build());

            Advance(engine, 200);

            var lane = LaneOf(engine);
            Assert.That(
                lane.DeliveredUnits,
                Is.EqualTo(1UL),
                "one plate reaches the insertion funnel before backpressure reaches the belt");
            Assert.That(lane.ItemCount, Is.EqualTo(5), "1000 mm at 250 mm spacing holds five");
            Assert.That(lane.Items[0].PositionMillimetres, Is.EqualTo(1000));
            Assert.That(lane.Items[4].PositionMillimetres, Is.EqualTo(0));

            // Conservation across the whole graph, cargo included.
            Assert.That(
                Held(engine, Source, ContentIds.IronPlate)
                + Held(engine, Funnel, ContentIds.IronPlate)
                + Held(engine, Sink, ContentIds.IronPlate)
                + Held(engine, SinkFunnel, ContentIds.IronPlate)
                + lane.ItemCount,
                Is.EqualTo(140L));
        }

        [Test]
        public void ItemsNeverOverlapHoweverLongTheRun()
        {
            var engine = NewEngine(
                Graph()
                    .Store(Source, ContentIds.IronPlate, 60)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 1200, 70, 300)
                    .Build());

            // The spacing invariant is checked on every phase boundary by the state's own
            // validation, so a run that commits at all has never overlapped two items.
            Advance(engine, 400);

            Assert.That(LaneOf(engine).DeliveredUnits, Is.GreaterThan(0UL));
            Assert.That(
                Held(engine, Source, ContentIds.IronPlate)
                + Held(engine, Funnel, ContentIds.IronPlate)
                + LaneOf(engine).ItemCount
                + Held(engine, SinkFunnel, ContentIds.IronPlate)
                + Held(engine, Sink, ContentIds.IronPlate),
                Is.EqualTo(60L),
                "every plate is in the crate, on the belt, or in the sink");
        }

        [Test]
        public void ALaneWillNotPickUpWhatItsDestinationCannotAccept()
        {
            // Without this the plate would ride to the exit and sit there for ever, holding
            // the lane closed behind it.
            var engine = NewEngine(
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddMachine(Sink, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .AddFunnel(Funnel, ContentIds.BeltFunnel, Source)
                    .Store(Source, ContentIds.IronPlate, 10)
                    .AddLane(Lane, Funnel, Sink, StableId.None, 800, 100, 250)
                    .Build());

            Advance(engine, 40);

            Assert.That(LaneOf(engine).ItemCount, Is.EqualTo(0));
            Assert.That(LaneOf(engine).DeliveredUnits, Is.EqualTo(0UL));
            Assert.That(Held(engine, Source, ContentIds.IronPlate), Is.EqualTo(9L));
            Assert.That(
                Held(engine, Funnel, ContentIds.IronPlate),
                Is.EqualTo(1L),
                "the funnel may extract one plate, but the belt must refuse it");
        }

        [Test]
        public void APhysicalFunnelCannotFeedTwoDifferentLanes()
        {
            var builder =
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddBuffer(Sink, ContentIds.WoodenCrate)
                    .AddBuffer(SecondSink, ContentIds.WoodenCrate)
                    .AddFunnel(Funnel, ContentIds.BeltFunnel, Source)
                    .AddFunnel(SinkFunnel, ContentIds.BeltFunnel, Sink)
                    .AddFunnel(SecondSinkFunnel, ContentIds.BeltFunnel, SecondSink)
                    .Store(Source, ContentIds.IronPlate, 1)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 500, 100, 250)
                    .AddLane(
                        SecondLane,
                        Funnel,
                        SecondSinkFunnel,
                        ContentIds.IronPlate,
                        500,
                        100,
                        250);

            Assert.Throws<SimulationInvariantException>(() => builder.Build());
        }

        [Test]
        public void ADirectCrateToBeltConnectionIsRejected()
        {
            var builder =
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddBuffer(Sink, ContentIds.WoodenCrate)
                    .AddFunnel(SinkFunnel, ContentIds.BeltFunnel, Sink)
                    .Store(Source, ContentIds.IronPlate, 10)
                    .AddLane(
                        Lane,
                        Source,
                        SinkFunnel,
                        ContentIds.IronPlate,
                        500,
                        100,
                        250);

            Assert.Throws<SimulationInvariantException>(() => builder.Build());
        }

        [Test]
        public void ADirectBeltToCrateConnectionIsRejected()
        {
            var builder =
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddBuffer(Sink, ContentIds.WoodenCrate)
                    .AddFunnel(Funnel, ContentIds.BeltFunnel, Source)
                    .Store(Source, ContentIds.IronPlate, 10)
                    .AddLane(Lane, Funnel, Sink, ContentIds.IronPlate, 500, 100, 250);

            Assert.Throws<SimulationInvariantException>(() => builder.Build());
        }

        [Test]
        public void ADetachedFunnelIsValidButCannotMoveAnyItem()
        {
            var engine = NewEngine(
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddMachine(Sink, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .AddFunnel(DetachedFunnel, ContentIds.BeltFunnel, StableId.None)
                    .Store(Source, ContentIds.IronIngot, 10)
                    .AddLane(
                        Lane,
                        DetachedFunnel,
                        Sink,
                        ContentIds.IronIngot,
                        500,
                        100,
                        250)
                    .Build());

            Advance(engine, 100);

            Assert.That(LaneOf(engine).ItemCount, Is.Zero);
            Assert.That(LaneOf(engine).DeliveredUnits, Is.Zero);
            Assert.That(Held(engine, Source, ContentIds.IronIngot), Is.EqualTo(10L));
            Assert.That(Held(engine, DetachedFunnel, ContentIds.IronIngot), Is.Zero);
            Assert.That(Node(engine, Sink).Input.Count(ContentIds.IronIngot).Value, Is.Zero);
        }

        [Test]
        public void ADetachedInsertionFunnelCannotReceiveFromAMachine()
        {
            var engine = NewEngine(
                new MachineSimulationStateBuilder(_catalog)
                    .AddMachine(Source, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .AddFunnel(DetachedFunnel, ContentIds.BeltFunnel, StableId.None)
                    .StoreInOutput(Source, ContentIds.IronPlate, 1)
                    .AddLane(
                        Lane,
                        Source,
                        DetachedFunnel,
                        ContentIds.IronPlate,
                        500,
                        100,
                        250)
                    .Build());

            Advance(engine, 100);

            Assert.That(
                LaneOf(engine).ItemCount,
                Is.EqualTo(1),
                "the belt may carry the plate to the dead end, but the detached "
                + "funnel may not swallow it");
            Assert.That(LaneOf(engine).DeliveredUnits, Is.Zero);
            Assert.That(Held(engine, Source, ContentIds.IronPlate), Is.Zero);
            Assert.That(Held(engine, DetachedFunnel, ContentIds.IronPlate), Is.Zero);
        }

        [Test]
        public void OneFunnelCannotBeBothAnExtractorAndAnInserter()
        {
            var builder =
                new MachineSimulationStateBuilder(_catalog)
                    .AddBuffer(Source, ContentIds.WoodenCrate)
                    .AddBuffer(Sink, ContentIds.WoodenCrate)
                    .AddFunnel(Funnel, ContentIds.BeltFunnel, Source)
                    .AddFunnel(SinkFunnel, ContentIds.BeltFunnel, Sink)
                    .Store(Source, ContentIds.IronPlate, 2)
                    .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 500, 100, 250)
                    .AddLane(
                        SecondLane,
                        SinkFunnel,
                        Funnel,
                        ContentIds.IronPlate,
                        500,
                        100,
                        250);

            Assert.Throws<SimulationInvariantException>(() => builder.Build());
        }

        [Test]
        public void TwoIdenticalBeltRunsAgreeOnEveryHash()
        {
            var first = NewEngine(BusyLine());
            var second = NewEngine(BusyLine());

            for (var tick = 0; tick < 150; tick++)
            {
                Advance(first, 1);
                Advance(second, 1);
                Assert.That(
                    LogicalStateHasher.ComputeHashHex(first.State),
                    Is.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)),
                    $"the two belts diverged at tick {tick + 1}");
            }
        }

        [Test]
        public void APositionOnTheLaneIsPartOfTheHash()
        {
            // Two graphs identical except that one lane has run a tick longer. If cargo
            // positions were left out of the encoding, these would hash the same and a
            // reload would deliver at the wrong time.
            var early = NewEngine(BusyLine());
            var late = NewEngine(BusyLine());
            Advance(early, 2);
            Advance(late, 3);

            Assert.That(
                LogicalStateHasher.ComputeHashHex(early.State),
                Is.Not.EqualTo(LogicalStateHasher.ComputeHashHex(late.State)));
        }

        private MachineSimulationStateBuilder Graph()
        {
            return new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(Source, ContentIds.WoodenCrate)
                .AddBuffer(Sink, ContentIds.WoodenCrate)
                .AddFunnel(Funnel, ContentIds.BeltFunnel, Source)
                .AddFunnel(SinkFunnel, ContentIds.BeltFunnel, Sink);
        }

        private MachineSimulationState BusyLine()
        {
            return Graph()
                .Store(Source, ContentIds.IronPlate, 30)
                .AddLane(Lane, Funnel, SinkFunnel, ContentIds.IronPlate, 900, 110, 260)
                .Build();
        }

        private SimulationEngine NewEngine(MachineSimulationState machines)
        {
            return new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    Revision,
                    new AirshipSimulationState(),
                    machines),
                null,
                _catalog);
        }

        private static BeltLaneState LaneOf(SimulationEngine engine)
        {
            return LaneOf(engine, Lane);
        }

        private static BeltLaneState LaneOf(SimulationEngine engine, StableId laneId)
        {
            Assert.That(
                engine.State.GetMachineSnapshot().TryGetLane(laneId, out var lane),
                Is.True,
                $"lane {laneId} is missing");
            return lane;
        }

        private static MachineNodeState Node(SimulationEngine engine, StableId nodeId)
        {
            Assert.That(
                engine.State.GetMachineSnapshot().TryGetNode(nodeId, out var node),
                Is.True,
                $"node {nodeId} is missing");
            return node;
        }

        private static long Held(SimulationEngine engine, StableId nodeId, StableId itemId)
        {
            return Node(engine, nodeId).Output.Count(itemId).Value;
        }

        private static void Advance(SimulationEngine engine, int ticks)
        {
            for (var index = 0; index < ticks; index++)
            {
                var result = engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"tick aborted in {result.FailedPhase}: {result.FailureCause}");
            }
        }
    }
}

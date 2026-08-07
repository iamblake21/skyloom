using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    /// <summary>
    /// MACH-001. The subject is not "does the press work" but the properties the plan
    /// asks for: a full output stops the line without destroying material, underfeeding
    /// does not throw away progress, and room restarts the line.
    ///
    /// Forking and the behaviour of a saturated branch were verified here against the
    /// logical link of MACH-001. That link has since been removed by LOG-001, which
    /// replaced it with belt lanes, so those two properties are now verified on lanes in
    /// <c>BeltLaneTests</c> rather than kept here against a transport that no longer
    /// exists.
    /// </summary>
    public sealed class MachineSimulationTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId SourceCrate = new StableId(0x9000000000000000UL, 1UL);
        private static readonly StableId Press = new StableId(0x9000000000000000UL, 2UL);
        private static readonly StableId FullSink = new StableId(0x9000000000000000UL, 3UL);
        private static readonly StableId FeedFunnel = new StableId(0x9000000000000000UL, 4UL);
        private static readonly StableId DrainFunnel = new StableId(0x9000000000000000UL, 5UL);
        private static readonly StableId FeedLane = new StableId(0x9100000000000000UL, 1UL);
        private static readonly StableId DrainLane = new StableId(0x9100000000000000UL, 2UL);

        /// <summary>1 iron ingot into 1 iron plate over 5000 ms, i.e. 100 ticks at 20 Hz.</summary>
        private const long CycleTicks = 100L;

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void OneTickOfProgressIsOneAuthoritativeStep()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Store(Press, ContentIds.IronIngot, 1)
                    .Build());

            Advance(engine, 1);

            var press = Node(engine, Press);
            Assert.That(press.ProgressMilliseconds, Is.EqualTo(50L));
            Assert.That(press.IsCycleActive, Is.True);
            Assert.That(press.Activity, Is.EqualTo(MachineActivity.Running));

            // The cycle consumed its input the instant it started. That ingot now exists
            // only as this cycle, which is what the next test depends on.
            Assert.That(press.Input.Count(ContentIds.IronIngot).Value, Is.EqualTo(0L));
        }

        [Test]
        public void AFullOutputHoldsTheFinishedCycleAndDestroysNothing()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .StoreInOutput(Press, ContentIds.IronPlate, 100)
                    .WithCycleInFlight(Press, 5000L)
                    .Build());

            // Four hundred ticks is twenty seconds of a machine that has finished its
            // work and has nowhere to put it. Nothing may drift in that time.
            Advance(engine, 400);

            var press = Node(engine, Press);
            Assert.That(press.Activity, Is.EqualTo(MachineActivity.OutputFull));
            Assert.That(press.IsCycleActive, Is.True);
            Assert.That(press.ProgressMilliseconds, Is.EqualTo(5000L));
            Assert.That(press.CompletedCycles, Is.EqualTo(0UL));
            Assert.That(press.Output.Count(ContentIds.IronPlate).Value, Is.EqualTo(100L));
        }

        [Test]
        public void RoomInTheOutputRestartsTheHeldCycle()
        {
            // The same held cycle as above, with room for exactly one more plate. The
            // player action that frees a full crate mid-run is a transfer command and
            // belongs to MACH-002; what is isolated here is the rule it relies on.
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .StoreInOutput(Press, ContentIds.IronPlate, 99)
                    .WithCycleInFlight(Press, 5000L)
                    .Build());

            Advance(engine, 1);

            var press = Node(engine, Press);
            Assert.That(press.CompletedCycles, Is.EqualTo(1UL));
            Assert.That(press.Output.Count(ContentIds.IronPlate).Value, Is.EqualTo(100L));
            Assert.That(press.IsCycleActive, Is.False);
            Assert.That(press.ProgressMilliseconds, Is.EqualTo(0L));
        }

        [Test]
        public void UnderfeedingKeepsTheProgressOfTheCycleAlreadyStarted()
        {
            // Exactly one cycle's worth of input and nothing behind it.
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Store(Press, ContentIds.IronIngot, 1)
                    .Build());

            Advance(engine, 10);
            var midway = Node(engine, Press);
            Assert.That(midway.ProgressMilliseconds, Is.EqualTo(500L));
            Assert.That(midway.Activity, Is.EqualTo(MachineActivity.Running));

            // Starvation does not reach back into work already done.
            Advance(engine, (int)CycleTicks - 10);
            var finished = Node(engine, Press);
            Assert.That(finished.CompletedCycles, Is.EqualTo(1UL));
            Assert.That(finished.Output.Count(ContentIds.IronPlate).Value, Is.EqualTo(1L));

            Advance(engine, 20);
            var starved = Node(engine, Press);
            Assert.That(
                starved.Activity,
                Is.EqualTo(MachineActivity.OutputFull),
                "without an output lane the completed plate physically blocks the press");
            Assert.That(starved.IsCycleActive, Is.False);
            Assert.That(starved.ProgressMilliseconds, Is.EqualTo(0L));
            Assert.That(starved.CompletedCycles, Is.EqualTo(1UL));
        }

        [Test]
        public void AnActivePressAdmitsNoSecondIngotAndFreezesItsFeedLane()
        {
            // A long feed lane has time to hold several ingots before the first reaches
            // the press. Once that first ingot starts the one-batch cycle, the remaining
            // cargo must stay physically where it is until the ram has gone down and up.
            var engine = NewEngine(
                Graph()
                    .AddBuffer(SourceCrate, ContentIds.WoodenCrate)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .AddFunnel(FeedFunnel, ContentIds.BeltFunnel, SourceCrate)
                    .Store(SourceCrate, ContentIds.IronIngot, 4)
                    .AddLane(
                        FeedLane,
                        FeedFunnel,
                        Press,
                        ContentIds.IronIngot,
                        1000,
                        100,
                        250)
                    .Build());

            for (var tick = 0; tick < 30 && !Node(engine, Press).IsCycleActive; tick++)
            {
                Advance(engine, 1);
            }

            var pressAtStart = Node(engine, Press);
            var laneAtStart = Lane(engine, FeedLane);
            Assert.That(pressAtStart.IsCycleActive, Is.True);
            Assert.That(pressAtStart.Input.Count(ContentIds.IronIngot).Value, Is.Zero);
            Assert.That(
                laneAtStart.ItemCount,
                Is.GreaterThan(0),
                "at least one following ingot must be waiting on the feed belt");

            var positions = new int[laneAtStart.ItemCount];
            for (var index = 0; index < laneAtStart.ItemCount; index++)
            {
                positions[index] = laneAtStart.Items[index].PositionMillimetres;
            }

            var delivered = laneAtStart.DeliveredUnits;
            Advance(engine, 20);

            var pressWhileActive = Node(engine, Press);
            var laneWhileActive = Lane(engine, FeedLane);
            Assert.That(pressWhileActive.IsCycleActive, Is.True);
            Assert.That(
                pressWhileActive.Input.Count(ContentIds.IronIngot).Value,
                Is.Zero,
                "a running press has capacity for its active batch only");
            Assert.That(laneWhileActive.DeliveredUnits, Is.EqualTo(delivered));
            Assert.That(laneWhileActive.ItemCount, Is.EqualTo(positions.Length));
            for (var index = 0; index < positions.Length; index++)
            {
                Assert.That(
                    laneWhileActive.Items[index].PositionMillimetres,
                    Is.EqualTo(positions[index]),
                    $"waiting ingot {index} moved while the press was cycling");
            }
        }

        [Test]
        public void TheFullLineConservesMaterialWhileTheOutputIsSaturated()
        {
            // Cassa -> Imbuto -> Nastro -> Pressa -> Nastro -> Imbuto -> Cassa,
            // with the destination crate already full so the press must stall.
            var engine = NewEngine(
                Graph()
                    .AddBuffer(SourceCrate, ContentIds.WoodenCrate)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .AddNarrowBuffer(FullSink, ContentIds.WoodenCrate, 1)
                    .AddFunnel(
                        FeedFunnel,
                        ContentIds.BeltFunnel,
                        SourceCrate)
                    .AddFunnel(
                        DrainFunnel,
                        ContentIds.BeltFunnel,
                        FullSink)
                    .Store(SourceCrate, ContentIds.IronIngot, 20)
                    .Store(FullSink, ContentIds.IronPlate, 100)
                    .AddLane(FeedLane, FeedFunnel, Press, ContentIds.IronIngot, 200, 100, 150)
                    .AddLane(DrainLane, Press, DrainFunnel, ContentIds.IronPlate, 100, 100, 150)
                    .Build());

            // Three completed plates fill, in order, the insertion funnel, the drain
            // belt and the press output. At that point physical backpressure reaches
            // the ram and no fourth batch may start.
            Advance(engine, 450);

            var source = Node(engine, SourceCrate);
            var press = Node(engine, Press);
            var sink = Node(engine, FullSink);

            // The drain lane pulls what room it has and then jams against the full sink,
            // so the press fills its own output and holds a finished cycle.
            Assert.That(press.Activity, Is.EqualTo(MachineActivity.OutputFull));
            Assert.That(press.IsCycleActive, Is.False);
            Assert.That(press.CompletedCycles, Is.EqualTo(3UL));
            Assert.That(press.Output.Count(ContentIds.IronPlate).Value, Is.EqualTo(1L));
            Assert.That(sink.Output.Count(ContentIds.IronPlate).Value, Is.EqualTo(100L));
            Assert.That(
                Node(engine, DrainFunnel).Output.Count(ContentIds.IronPlate).Value,
                Is.EqualTo(1L),
                "the first blocked plate is visibly held by the insertion funnel");
            Assert.That(
                Lane(engine, DrainLane).ItemCount,
                Is.EqualTo(1),
                "a 100 mm lane at 150 mm spacing holds one plate, stuck at the exit");

            // Conservation includes every physical holding place: both Imbuti, both
            // belts, the press ports, the crates and the cycle currently under the ram.
            var ingots =
                source.Output.Count(ContentIds.IronIngot).Value
                + Node(engine, FeedFunnel).Output.Count(ContentIds.IronIngot).Value
                + press.Input.Count(ContentIds.IronIngot).Value
                + Lane(engine, FeedLane).ItemCount;
            var plates =
                press.Output.Count(ContentIds.IronPlate).Value
                + Node(engine, DrainFunnel).Output.Count(ContentIds.IronPlate).Value
                + sink.Output.Count(ContentIds.IronPlate).Value
                + Lane(engine, DrainLane).ItemCount;
            var cyclesInFlight = press.IsCycleActive ? 1L : 0L;

            Assert.That(
                ingots + plates + cyclesInFlight,
                Is.EqualTo(120L),
                "all 20 ingots and 100 pre-existing plates must still exist exactly once");
        }

        [Test]
        public void TheCompleteLineTurnsEveryIngotIntoAPlateInTheFinalCrate()
        {
            var engine = NewEngine(
                Graph()
                    .AddBuffer(SourceCrate, ContentIds.WoodenCrate)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .AddBuffer(FullSink, ContentIds.WoodenCrate)
                    .AddFunnel(
                        FeedFunnel,
                        ContentIds.BeltFunnel,
                        SourceCrate)
                    .AddFunnel(
                        DrainFunnel,
                        ContentIds.BeltFunnel,
                        FullSink)
                    .Store(SourceCrate, ContentIds.IronIngot, 3)
                    .AddLane(FeedLane, FeedFunnel, Press, ContentIds.IronIngot, 200, 100, 150)
                    .AddLane(DrainLane, Press, DrainFunnel, ContentIds.IronPlate, 200, 100, 150)
                    .Build());

            Advance(engine, 340);

            Assert.That(Node(engine, SourceCrate).Output.Count(ContentIds.IronIngot).Value, Is.Zero);
            Assert.That(Node(engine, FeedFunnel).Output.Count(ContentIds.IronIngot).Value, Is.Zero);
            Assert.That(Lane(engine, FeedLane).ItemCount, Is.Zero);
            Assert.That(Node(engine, Press).Input.Count(ContentIds.IronIngot).Value, Is.Zero);
            Assert.That(Node(engine, Press).Output.Count(ContentIds.IronPlate).Value, Is.Zero);
            Assert.That(Node(engine, DrainFunnel).Output.Count(ContentIds.IronPlate).Value, Is.Zero);
            Assert.That(Lane(engine, DrainLane).ItemCount, Is.Zero);
            Assert.That(Node(engine, FullSink).Output.Count(ContentIds.IronPlate).Value, Is.EqualTo(3L));
            Assert.That(Node(engine, Press).CompletedCycles, Is.EqualTo(3UL));
        }

        [Test]
        public void AMachineWithoutARecipeNamesItsCause()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, StableId.None)
                    .Build());

            Advance(engine, 3);

            Assert.That(Node(engine, Press).Activity, Is.EqualTo(MachineActivity.NoRecipe));
        }

        [Test]
        public void TwoIdenticalLinesAgreeOnEveryHash()
        {
            var first = NewEngine(SaturatedLine());
            var second = NewEngine(SaturatedLine());

            for (var tick = 0; tick < 120; tick++)
            {
                Advance(first, 1);
                Advance(second, 1);
                Assert.That(
                    LogicalStateHasher.ComputeHashHex(first.State),
                    Is.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)),
                    $"the two lines diverged at tick {tick + 1}");
            }
        }

        [Test]
        public void TheMachineSnapshotIsDetachedFromTheAuthoritativeGraph()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Store(Press, ContentIds.IronIngot, 1)
                    .Build());

            Advance(engine, 4);
            var snapshot = engine.State.GetMachineSnapshot();
            Assert.That(snapshot.TryGetNode(Press, out var beforeMore), Is.True);
            var progressWhenTaken = beforeMore.ProgressMilliseconds;

            Advance(engine, 4);

            Assert.That(snapshot.TryGetNode(Press, out var stillHeld), Is.True);
            Assert.That(stillHeld.ProgressMilliseconds, Is.EqualTo(progressWhenTaken));
            Assert.That(
                Node(engine, Press).ProgressMilliseconds,
                Is.EqualTo(progressWhenTaken + 200L));
        }

        [Test]
        public void AGraphCannotAdvanceWithoutTheContentThatDefinesIt()
        {
            var engine = new SimulationEngine(
                NewState(
                    Graph()
                        .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                        .Store(Press, ContentIds.IronIngot, 1)
                        .Build()));

            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.False);
            Assert.That(result.FailureCause, Does.Contain("validated catalog"));
            Assert.That(
                result.FailedPhase,
                Is.EqualTo(SimulationPhase.ItemFluidFlowAndReservations));
        }

        [Test]
        public void AnEmptyGraphAdvancesWithoutContent()
        {
            var engine = new SimulationEngine(NewState(new MachineSimulationState()));

            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
        }

        private MachineSimulationStateBuilder Graph()
        {
            return new MachineSimulationStateBuilder(_catalog);
        }

        private MachineSimulationState SaturatedLine()
        {
            return Graph()
                .AddBuffer(SourceCrate, ContentIds.WoodenCrate)
                .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                .AddNarrowBuffer(FullSink, ContentIds.WoodenCrate, 1)
                .AddFunnel(
                    FeedFunnel,
                    ContentIds.BeltFunnel,
                    SourceCrate)
                .AddFunnel(
                    DrainFunnel,
                    ContentIds.BeltFunnel,
                    FullSink)
                .Store(SourceCrate, ContentIds.IronIngot, 20)
                .Store(FullSink, ContentIds.IronPlate, 100)
                .AddLane(FeedLane, FeedFunnel, Press, ContentIds.IronIngot, 200, 100, 150)
                .AddLane(DrainLane, Press, DrainFunnel, ContentIds.IronPlate, 100, 100, 150)
                .Build();
        }

        private SimulationState NewState(MachineSimulationState machines)
        {
            return new SimulationState(
                new SimulationTick(0UL),
                Revision,
                new AirshipSimulationState(),
                machines);
        }

        private SimulationEngine NewEngine(MachineSimulationState machines)
        {
            return new SimulationEngine(NewState(machines), null, _catalog);
        }

        private static void Advance(SimulationEngine engine, int ticks)
        {
            for (var index = 0; index < ticks; index++)
            {
                var result = engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"tick {result.ExecutingTick} aborted in {result.FailedPhase}: "
                    + result.FailureCause);
            }
        }

        private static MachineNodeState Node(SimulationEngine engine, StableId id)
        {
            Assert.That(
                engine.State.GetMachineSnapshot().TryGetNode(id, out var node),
                Is.True,
                $"node {id} is missing from the graph");
            return node;
        }

        private static BeltLaneState Lane(SimulationEngine engine, StableId id)
        {
            Assert.That(
                engine.State.GetMachineSnapshot().TryGetLane(id, out var lane),
                Is.True,
                $"lane {id} is missing from the graph");
            return lane;
        }
    }
}

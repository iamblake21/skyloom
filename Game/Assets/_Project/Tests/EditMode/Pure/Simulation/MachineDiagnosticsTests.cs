using System;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    /// <summary>
    /// MACH-003. The acceptance is a negative one — no state may be reported as merely
    /// stopped — so the tests are written to fail if a cause is ever missing, empty or
    /// shared between two different situations.
    /// </summary>
    public sealed class MachineDiagnosticsTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Crate = new StableId(0x9300000000000000UL, 1UL);
        private static readonly StableId Press = new StableId(0x9300000000000000UL, 2UL);

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void EveryActivityMapsToItsOwnCause()
        {
            var activities = (MachineActivity[])Enum.GetValues(typeof(MachineActivity));
            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (var activity in activities)
            {
                var key = MachineCauseKeys.For(activity);
                Assert.That(key, Is.Not.Empty, $"activity {activity} names no cause");
                Assert.That(
                    seen.Add(key),
                    Is.True,
                    $"activity {activity} reuses the cause key '{key}' of another state");
            }

            Assert.That(seen.Count, Is.EqualTo(activities.Length));
        }

        [Test]
        public void AStarvedMachineNamesWhichInputIsShort()
        {
            // No ingot where the recipe wants one: the machine is blocked, and the
            // difference is the only thing the player can act on.
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Build());
            Advance(engine, 1);

            var report = Describe(engine, Press);

            Assert.That(report.Activity, Is.EqualTo(MachineActivity.MissingInput));
            Assert.That(report.CauseKey, Is.EqualTo(MachineCauseKeys.MissingInput));
            Assert.That(report.IsBlocked, Is.True);
            Assert.That(report.Shortfalls.Count, Is.EqualTo(1));
            Assert.That(report.Shortfalls[0].ItemKey, Is.EqualTo("item.iron_ingot"));
            Assert.That(report.Shortfalls[0].Required, Is.EqualTo(1L));
            Assert.That(report.Shortfalls[0].Present, Is.EqualTo(0L));
            Assert.That(report.Shortfalls[0].Missing, Is.EqualTo(1L));
        }

        [Test]
        public void AHeldCycleNamesTheFullOutputAndReportsNoShortfall()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .StoreInOutput(Press, ContentIds.IronPlate, 100)
                    .WithCycleInFlight(Press, 5000L)
                    .Build());
            Advance(engine, 1);

            var report = Describe(engine, Press);

            Assert.That(report.Activity, Is.EqualTo(MachineActivity.OutputFull));
            Assert.That(report.CauseKey, Is.EqualTo(MachineCauseKeys.OutputFull));
            Assert.That(report.IsBlocked, Is.True);
            Assert.That(report.ProgressPermille, Is.EqualTo(1000));
            Assert.That(report.IsCycleActive, Is.True);
            Assert.That(
                report.Shortfalls,
                Is.Empty,
                "an output that is full is not an input that is missing");
        }

        [Test]
        public void AMachineWithoutARecipeNamesThatAndNotStarvation()
        {
            var engine = NewEngine(
                Graph().AddMachine(Press, ContentIds.MechanicalPress, StableId.None).Build());
            Advance(engine, 1);

            var report = Describe(engine, Press);

            Assert.That(report.CauseKey, Is.EqualTo(MachineCauseKeys.NoRecipe));
            Assert.That(report.RecipeKey, Is.Empty);
            Assert.That(report.DurationMilliseconds, Is.EqualTo(0L));
            Assert.That(report.IsBlocked, Is.True);
        }

        [Test]
        public void ACrateIsNotBlockedBecauseItHasNoWork()
        {
            var engine = NewEngine(Graph().AddBuffer(Crate, ContentIds.WoodenCrate).Build());
            Advance(engine, 1);

            var report = Describe(engine, Crate);

            Assert.That(report.Activity, Is.EqualTo(MachineActivity.Idle));
            Assert.That(report.CauseKey, Is.EqualTo(MachineCauseKeys.NoWork));
            Assert.That(report.IsBlocked, Is.False);
        }

        [Test]
        public void ProgressIsReportedInThousandths()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Store(Press, ContentIds.IronIngot, 1)
                    .Build());

            // 50 ticks at 50 ms is 2500 ms of a 5000 ms recipe: exactly half.
            Advance(engine, 50);

            var report = Describe(engine, Press);
            Assert.That(report.ProgressMilliseconds, Is.EqualTo(2500L));
            Assert.That(report.DurationMilliseconds, Is.EqualTo(5000L));
            Assert.That(report.ProgressPermille, Is.EqualTo(500));
            Assert.That(report.Activity, Is.EqualTo(MachineActivity.Running));
        }

        [Test]
        public void ACrateReportsOnePortAndAMachineReportsTwo()
        {
            var engine = NewEngine(
                Graph()
                    .AddBuffer(Crate, ContentIds.WoodenCrate)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Build());
            Advance(engine, 1);

            var crate = Describe(engine, Crate);
            var press = Describe(engine, Press);

            Assert.That(crate.Ports.Count, Is.EqualTo(1));
            Assert.That(crate.Ports[0].Kind, Is.EqualTo(MachinePortKind.Storage));
            Assert.That(crate.Ports[0].SlotCount, Is.EqualTo(24));

            Assert.That(press.Ports.Count, Is.EqualTo(2));
            Assert.That(press.Ports[0].Kind, Is.EqualTo(MachinePortKind.Input));
            Assert.That(press.Ports[1].Kind, Is.EqualTo(MachinePortKind.Output));
            Assert.That(press.Ports[0].SlotCount, Is.EqualTo(1));
            Assert.That(press.Ports[1].SlotCount, Is.EqualTo(1));
        }

        [Test]
        public void ADescribedPortKeepsItsSlotPositions()
        {
            var engine = NewEngine(
                Graph()
                    .AddBuffer(Crate, ContentIds.WoodenCrate)
                    .Store(Crate, ContentIds.IronPlate, 30)
                    .Build());
            Advance(engine, 1);

            var port = Describe(engine, Crate).Ports[0];

            Assert.That(port.TotalQuantity, Is.EqualTo(30L));
            Assert.That(port.Slots[0].ItemKey, Is.EqualTo("item.iron_plate"));
            Assert.That(port.Slots[0].Quantity, Is.EqualTo(30L));
            Assert.That(port.Slots[0].MaxStack, Is.EqualTo(100L));
            Assert.That(port.Slots[1].IsEmpty, Is.True);
            Assert.That(port.Slots[1].SlotIndex, Is.EqualTo(1));
        }

        [Test]
        public void EveryNodeOfTheGraphIsDescribedInIdOrder()
        {
            var engine = NewEngine(
                Graph()
                    .AddBuffer(Crate, ContentIds.WoodenCrate)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Build());
            Advance(engine, 1);

            var reports = MachineDiagnostics.DescribeAll(engine.State, _catalog);

            Assert.That(reports.Count, Is.EqualTo(2));
            Assert.That(reports[0].NodeId, Is.EqualTo(Crate));
            Assert.That(reports[1].NodeId, Is.EqualTo(Press));
            foreach (var report in reports)
            {
                Assert.That(report.CauseKey, Is.Not.Empty);
                Assert.That(report.DefinitionKey, Is.Not.Empty);
            }
        }

        [Test]
        public void AnAbsentNodeIsReportedAsAbsentAndNotAsBlocked()
        {
            var engine = NewEngine(Graph().AddBuffer(Crate, ContentIds.WoodenCrate).Build());

            Assert.That(
                MachineDiagnostics.TryDescribe(
                    engine.State,
                    _catalog,
                    new StableId(0x9300000000000000UL, 77UL),
                    out var report),
                Is.False);
            Assert.That(report, Is.Null);
        }

        private MachineSimulationStateBuilder Graph()
        {
            return new MachineSimulationStateBuilder(_catalog);
        }

        private SimulationEngine NewEngine(MachineSimulationState machines)
        {
            var state = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                new AirshipSimulationState(),
                machines);
            return new SimulationEngine(state, null, _catalog);
        }

        private MachineNodeReport Describe(SimulationEngine engine, StableId nodeId)
        {
            Assert.That(
                MachineDiagnostics.TryDescribe(engine.State, _catalog, nodeId, out var report),
                Is.True,
                $"node {nodeId} has no report");
            return report;
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

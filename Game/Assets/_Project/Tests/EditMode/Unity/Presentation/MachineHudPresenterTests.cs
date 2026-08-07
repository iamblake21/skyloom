using System;
using System.Collections.Generic;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;

namespace CML.Tests.Unity.Presentation
{
    /// <summary>
    /// UI-MACH-001, projection half. The panel must say what is happening and why, in
    /// Italian, and an item must look the same here as it does in the backpack.
    /// </summary>
    public sealed class MachineHudPresenterTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Crate = new StableId(0x9400000000000000UL, 1UL);
        private static readonly StableId Press = new StableId(0x9400000000000000UL, 2UL);
        private static readonly StableId Backpack = new StableId(0x9400000000000000UL, 3UL);

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void EveryActivityHasItsOwnItalianText()
        {
            var activities = (MachineActivity[])Enum.GetValues(typeof(MachineActivity));
            var seen = new HashSet<string>();

            foreach (var activity in activities)
            {
                var text = MachineHudPresenter.CauseText(activity);
                Assert.That(text, Is.Not.Empty, $"activity {activity} has no Italian text");
                Assert.That(
                    seen.Add(text),
                    Is.True,
                    $"activity {activity} reuses the text '{text}' of another state");
            }
        }

        [Test]
        public void AStarvedPressSaysWhatIsMissingAndHowMuch()
        {
            var snapshot = Snapshot(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Build(),
                Press,
                1);

            // Natural case: the uppercase of the header is letter-spaced typography and
            // belongs to the view, exactly as "INVENTARIO" is written into the UXML.
            Assert.That(snapshot.Title, Is.EqualTo("Pressa meccanica"));
            Assert.That(snapshot.RecipeName, Is.EqualTo("Piastra di ferro"));
            Assert.That(snapshot.CauseText, Is.EqualTo("Manca materiale in ingresso"));
            Assert.That(snapshot.ShortfallText, Is.EqualTo("1 × Lingotto di ferro"));
            Assert.That(snapshot.IsBlocked, Is.True);
        }

        [Test]
        public void AHeldCycleReadsAsFullAndBlocked()
        {
            var snapshot = Snapshot(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .StoreInOutput(Press, ContentIds.IronPlate, 100)
                    .WithCycleInFlight(Press, 5000L)
                    .Build(),
                Press,
                1);

            Assert.That(snapshot.CauseText, Is.EqualTo("Uscita piena"));
            Assert.That(snapshot.ProgressPermille, Is.EqualTo(1000));
            Assert.That(snapshot.ProgressText, Is.EqualTo("100%"));
            Assert.That(snapshot.IsBlocked, Is.True);
            Assert.That(snapshot.ShortfallText, Is.Empty);
        }

        [Test]
        public void AHalfDoneCycleReadsAsFiftyPerCent()
        {
            var snapshot = Snapshot(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Store(Press, ContentIds.IronIngot, 2)
                    .Build(),
                Press,
                50);

            Assert.That(snapshot.CauseText, Is.EqualTo("In lavorazione"));
            Assert.That(snapshot.ProgressText, Is.EqualTo("50%"));
            Assert.That(snapshot.IsBlocked, Is.False);
        }

        [Test]
        public void APressShowsAnInputAndAnOutputPort()
        {
            var snapshot = Snapshot(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Build(),
                Press,
                1);

            Assert.That(snapshot.Kind, Is.EqualTo(MachineNodeKind.Machine));
            Assert.That(snapshot.Ports.Count, Is.EqualTo(2));
            Assert.That(snapshot.Ports[0].Title, Is.EqualTo("INGRESSO"));
            Assert.That(snapshot.Ports[1].Title, Is.EqualTo("USCITA"));
            Assert.That(
                snapshot.Ports[0].Slots.Count,
                Is.EqualTo(1),
                "The press accepts exactly one workpiece at a time.");
            Assert.That(snapshot.Ports[1].Slots.Count, Is.EqualTo(1));
        }

        [Test]
        public void ACrateShowsOneContentPortAndNoRecipe()
        {
            var snapshot = Snapshot(
                Graph()
                    .AddBuffer(Crate, ContentIds.WoodenCrate)
                    .Store(Crate, ContentIds.IronPlate, 30)
                    .Build(),
                Crate,
                1);

            Assert.That(snapshot.Kind, Is.EqualTo(MachineNodeKind.Buffer));
            Assert.That(snapshot.Title, Is.EqualTo("Cassa di legno"));
            Assert.That(snapshot.RecipeName, Is.Empty);
            Assert.That(snapshot.CauseText, Is.EqualTo("Deposito"));
            Assert.That(snapshot.IsBlocked, Is.False);
            Assert.That(snapshot.Ports.Count, Is.EqualTo(1));
            Assert.That(snapshot.Ports[0].Title, Is.EqualTo("CONTENUTO"));
            Assert.That(snapshot.Ports[0].TotalQuantity, Is.EqualTo(30L));
        }

        [Test]
        public void APlateLooksTheSameInAPressAsInTheBackpack()
        {
            // The reason MachineHudPresenter calls InventoryHudPresenter.ProjectSlot
            // instead of keeping its own table. If this ever fails, the two panels have
            // started to disagree about what an item is.
            var machineSnapshot = Snapshot(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .StoreInOutput(Press, ContentIds.IronPlate, 3)
                    .Build(),
                Press,
                1);
            var inMachine = machineSnapshot.Ports[1].Slots[0];

            var backpack = InventoryHudPresenter.Project(
                InventoryState.Restore(
                    Backpack,
                    _catalog,
                    ContentIds.PlayerInventory,
                    new[]
                    {
                        new InventoryStackRecord(
                            0,
                            ContentIds.IronPlate,
                            new NonNegativeQuantity(3))
                    }),
                _catalog);
            var inBackpack = backpack.Slots[0];

            Assert.That(inMachine.DisplayName, Is.EqualTo(inBackpack.DisplayName));
            Assert.That(inMachine.IconKind, Is.EqualTo(inBackpack.IconKind));
            Assert.That(inMachine.AccentColor, Is.EqualTo(inBackpack.AccentColor));
            Assert.That(inMachine.Quantity, Is.EqualTo(inBackpack.Quantity));
        }

        [Test]
        public void ProjectingThePanelDoesNotTouchTheLogicalState()
        {
            var engine = NewEngine(
                Graph()
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .Store(Press, ContentIds.IronIngot, 2)
                    .Build());
            Advance(engine, 5);

            var before = LogicalStateHasher.ComputeHashHex(engine.State);
            for (var repeat = 0; repeat < 10; repeat++)
            {
                Assert.That(
                    MachineDiagnostics.TryDescribe(engine.State, _catalog, Press, out var report),
                    Is.True);
                MachineHudPresenter.Project(report, _catalog);
            }

            Assert.That(LogicalStateHasher.ComputeHashHex(engine.State), Is.EqualTo(before));
        }

        private MachineSimulationStateBuilder Graph()
        {
            return new MachineSimulationStateBuilder(_catalog);
        }

        private MachineUiSnapshot Snapshot(
            MachineSimulationState machines,
            StableId nodeId,
            int ticks)
        {
            var engine = NewEngine(machines);
            Advance(engine, ticks);
            Assert.That(
                MachineDiagnostics.TryDescribe(engine.State, _catalog, nodeId, out var report),
                Is.True,
                $"node {nodeId} has no report");
            return MachineHudPresenter.Project(report, _catalog);
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

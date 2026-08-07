using System;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Airship
{
    /// <summary>
    /// The authoritative half of the damaged-hull opening: piloting is refused
    /// until the bill is paid, each installation moves exactly one component out
    /// of the inventory, and a refusal changes nothing on either side.
    /// </summary>
    public sealed class AirshipRepairTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId AirshipId = new StableId(1, 10);
        private static readonly StableId PlayerId = new StableId(1, 20);
        private static readonly StableId InventoryId = new StableId(1, 50);

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void ADamagedHullStartsWithNothingInstalled()
        {
            var engine = NewEngine();
            var airship = GetAirship(engine);

            Assert.That(
                airship.RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Damaged));
            Assert.That(airship.InstalledIronPlates, Is.EqualTo(0));
            Assert.That(airship.InstalledInsulatedCables, Is.EqualTo(0));
            Assert.That(airship.IsBillSatisfied, Is.False);
        }

        [Test]
        public void AnUntouchedAirshipIsAirworthySoExistingFixturesStillFly()
        {
            var builder = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default);

            Assert.That(
                GetAirship(builder.Build()).RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Repaired));
        }

        [Test]
        public void PilotingIsRefusedWhileTheHullIsDamaged()
        {
            var engine = NewEngine();

            engine.EnqueueCommand(AirshipCommandCodec.PilotBegin(
                new SimulationTick(1),
                0,
                PlayerId,
                AirshipId));
            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(GetAirship(engine).PilotId.IsNone, Is.True);
        }

        [Test]
        public void InstallingTakesExactlyOneComponentAndRaisesOneCounter()
        {
            var engine = NewEngine(
                new InventoryStackRecord(0, ContentIds.IronPlate, new NonNegativeQuantity(4L)));

            Install(engine, 1, ContentIds.IronPlate, 1L);

            Assert.That(GetAirship(engine).InstalledIronPlates, Is.EqualTo(1));
            Assert.That(GetAirship(engine).InstalledInsulatedCables, Is.EqualTo(0));
            Assert.That(Count(engine, ContentIds.IronPlate), Is.EqualTo(3L));
            Assert.That(
                GetAirship(engine).RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Damaged));
        }

        [Test]
        public void InstallingMoreThanTheHullStillNeedsChangesNothing()
        {
            var engine = NewEngine(
                new InventoryStackRecord(0, ContentIds.IronPlate, new NonNegativeQuantity(9L)));

            // Five plates against a bill of four: the surplus must not be
            // swallowed, and the four must not be taken either.
            Install(engine, 1, ContentIds.IronPlate, 5L);

            Assert.That(GetAirship(engine).InstalledIronPlates, Is.EqualTo(0));
            Assert.That(Count(engine, ContentIds.IronPlate), Is.EqualTo(9L));
        }

        [Test]
        public void AnItemOutsideTheBillIsRefusedAndNotConsumed()
        {
            var engine = NewEngine(
                new InventoryStackRecord(0, ContentIds.Stone, new NonNegativeQuantity(8L)));

            Install(engine, 1, ContentIds.Stone, 1L);

            Assert.That(Count(engine, ContentIds.Stone), Is.EqualTo(8L));
            Assert.That(GetAirship(engine).InstalledIronPlates, Is.EqualTo(0));
            Assert.That(GetAirship(engine).InstalledInsulatedCables, Is.EqualTo(0));
        }

        [Test]
        public void InstallingWithoutOwningTheComponentChangesNothing()
        {
            var engine = NewEngine();

            Install(engine, 1, ContentIds.IronPlate, 1L);

            Assert.That(GetAirship(engine).InstalledIronPlates, Is.EqualTo(0));
            Assert.That(
                GetAirship(engine).RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Damaged));
        }

        [Test]
        public void SatisfyingTheBillStartsTheEightSecondCountdown()
        {
            var engine = NewFullyStockedEngine();

            PayTheWholeBill(engine);

            var airship = GetAirship(engine);
            Assert.That(
                airship.RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Repairing));
            Assert.That(
                airship.RepairTicksRemaining,
                Is.EqualTo(AirshipRepairBill.RepairDurationTicks));
            Assert.That(Count(engine, ContentIds.IronPlate), Is.EqualTo(0L));
            Assert.That(Count(engine, ContentIds.InsulatedCable), Is.EqualTo(0L));
        }

        [Test]
        public void TheCountdownFinishesRepairedAndUnlocksPiloting()
        {
            var engine = NewFullyStockedEngine();
            PayTheWholeBill(engine);

            for (var index = 0; index < AirshipRepairBill.RepairDurationTicks; index++)
            {
                Assert.That(
                    engine.AdvanceOneTick().Committed,
                    Is.True,
                    "the repair countdown must never abort a tick");
            }

            Assert.That(
                GetAirship(engine).RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Repaired));
            Assert.That(GetAirship(engine).RepairTicksRemaining, Is.EqualTo(0));

            var tick = engine.State.Tick.Value + 1UL;
            engine.EnqueueCommand(AirshipCommandCodec.PilotBegin(
                new SimulationTick(tick),
                0,
                PlayerId,
                AirshipId));
            engine.AdvanceOneTick();

            Assert.That(GetAirship(engine).PilotId, Is.EqualTo(PlayerId));
        }

        [Test]
        public void InstallingIntoAnAlreadyRepairedHullIsRefused()
        {
            var engine = NewFullyStockedEngine(spare: 1L);
            PayTheWholeBill(engine);
            for (var index = 0; index < AirshipRepairBill.RepairDurationTicks; index++)
            {
                engine.AdvanceOneTick();
            }

            var before = Count(engine, ContentIds.IronPlate);
            Install(engine, engine.State.Tick.Value + 1UL, ContentIds.IronPlate, 1L);

            Assert.That(Count(engine, ContentIds.IronPlate), Is.EqualTo(before));
            Assert.That(
                GetAirship(engine).InstalledIronPlates,
                Is.EqualTo(AirshipRepairBill.RequiredIronPlates));
        }

        [Test]
        public void TheRepairStateIsPartOfTheCanonicalEncoding()
        {
            var damaged = new AirshipSimulationStateBuilder()
                .AddDamagedAirship(AirshipId, default)
                .Build();
            var repaired = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .Build();

            Assert.That(
                AirshipCanonicalSerializer.Serialize(damaged),
                Is.Not.EqualTo(AirshipCanonicalSerializer.Serialize(repaired)),
                "a grounded hull and a flyable one are not the same world");
        }

        [Test]
        public void TheRepairStateSurvivesADeepClone()
        {
            var state = new AirshipSimulationStateBuilder()
                .AddDamagedAirship(AirshipId, default)
                .Build();
            state.TryGetAirship(AirshipId, out var original);

            Assert.That(
                AirshipCanonicalSerializer.Serialize(state.DeepClone()),
                Is.EqualTo(AirshipCanonicalSerializer.Serialize(state)));
            Assert.That(
                original.RepairStatus,
                Is.EqualTo(AirshipRepairStatus.Damaged));
        }

        private void PayTheWholeBill(SimulationEngine engine)
        {
            var tick = engine.State.Tick.Value;
            for (var index = 0; index < AirshipRepairBill.RequiredIronPlates; index++)
            {
                Install(engine, ++tick, ContentIds.IronPlate, 1L);
            }

            for (var index = 0; index < AirshipRepairBill.RequiredInsulatedCables; index++)
            {
                Install(engine, ++tick, ContentIds.InsulatedCable, 1L);
            }
        }

        private void Install(
            SimulationEngine engine,
            ulong tick,
            StableId itemId,
            long amount)
        {
            engine.EnqueueCommand(AirshipCommandCodec.RepairInstall(
                new SimulationTick(tick),
                0,
                PlayerId,
                AirshipId,
                InventoryId,
                itemId,
                amount));
            var result = engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
        }

        private long Count(SimulationEngine engine, StableId itemId)
        {
            Assert.That(
                engine.State.GetInventorySnapshot()
                    .TryGet(InventoryId, out var inventory),
                Is.True,
                "the fixture inventory must exist");
            return inventory.Count(itemId).Value;
        }

        private SimulationEngine NewFullyStockedEngine(long spare = 0L)
        {
            return NewEngine(
                new InventoryStackRecord(
                    0,
                    ContentIds.IronPlate,
                    new NonNegativeQuantity(
                        AirshipRepairBill.RequiredIronPlates + spare)),
                new InventoryStackRecord(
                    1,
                    ContentIds.InsulatedCable,
                    new NonNegativeQuantity(
                        AirshipRepairBill.RequiredInsulatedCables)));
        }

        private SimulationEngine NewEngine(params InventoryStackRecord[] stacks)
        {
            var airships = new AirshipSimulationStateBuilder()
                .AddDamagedAirship(AirshipId, default)
                .AddAboardPlayer(
                    PlayerId,
                    AirshipId,
                    new AirshipPoseState(
                        AirshipSimulationConstants.PilotSeatCenter,
                        0),
                    false)
                .Build();
            var inventory = InventoryState.Restore(
                InventoryId,
                _catalog,
                ContentIds.PlayerInventory,
                stacks ?? Array.Empty<InventoryStackRecord>());
            return new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    Revision,
                    airships,
                    new MachineSimulationState(),
                    InventorySimulationState.Create(_catalog, inventory)),
                null,
                _catalog);
        }

        private static AirshipEntityState GetAirship(SimulationEngine engine)
        {
            return GetAirship(engine.State.GetAirshipSnapshot());
        }

        private static AirshipEntityState GetAirship(AirshipSimulationState state)
        {
            Assert.That(
                state.TryGetAirship(AirshipId, out var airship),
                Is.True,
                "the fixture airship must exist");
            return airship;
        }
    }
}

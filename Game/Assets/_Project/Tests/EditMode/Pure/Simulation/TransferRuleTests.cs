using System.Collections.Generic;
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
    /// <summary>
    /// MACH-002. The rule is one function used by every mover of items, so the tests are
    /// about the properties that a second implementation would eventually break:
    /// conservation, atomicity, and a refusal that names its cause.
    /// </summary>
    public sealed class TransferRuleTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Backpack = new StableId(0x9200000000000000UL, 1UL);
        private static readonly StableId WideChest = new StableId(0x9200000000000000UL, 2UL);
        private static readonly StableId NarrowChest = new StableId(0x9200000000000000UL, 3UL);
        private static readonly StableId Press = new StableId(0x9200000000000000UL, 4UL);

        private static readonly StableId[] TradedItems =
        {
            ContentIds.RawIron,
            ContentIds.IronIngot,
            ContentIds.IronPlate
        };

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void TwentyThousandTransfersConserveEveryItem()
        {
            var engine = NewEngine(SeededInventories(), StorageGraph());
            var expected = Totals(engine);

            // A deterministic generator, not a random one: a conservation bug that only
            // shows on one seed is a bug that cannot be reproduced from the report.
            var random = new Lcg(0x5EED1234U);
            var attempted = 0;

            for (var round = 0; round < 1000; round++)
            {
                var targetTick = engine.State.Tick.Next();
                for (var sequence = 0UL; sequence < 20UL; sequence++)
                {
                    engine.EnqueueCommand(
                        RandomTransfer(random, targetTick, sequence));
                    attempted++;
                }

                var result = engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"round {round} aborted in {result.FailedPhase}: {result.FailureCause}");

                var actual = Totals(engine);
                AssertSameTotals(expected, actual, $"after round {round}");
            }

            Assert.That(attempted, Is.EqualTo(20000));

            // Conservation is trivially true if nothing ever moved, so the run has to
            // prove it also did the work: most draws are refused by design, and what is
            // left has to be a substantial number of real moves.
            var refused = engine.State.GetCommandRejectionsCanonical().Count;
            var moved = attempted - refused;
            Assert.That(
                moved,
                Is.GreaterThan(2000),
                $"only {moved} of {attempted} transfers actually moved items");
        }

        [Test]
        public void TheSameSequenceOfTransfersProducesTheSameHash()
        {
            var first = NewEngine(SeededInventories(), StorageGraph());
            var second = NewEngine(SeededInventories(), StorageGraph());
            var firstRandom = new Lcg(0x0BADF00DU);
            var secondRandom = new Lcg(0x0BADF00DU);

            for (var round = 0; round < 50; round++)
            {
                var tick = first.State.Tick.Next();
                for (var sequence = 0UL; sequence < 8UL; sequence++)
                {
                    first.EnqueueCommand(RandomTransfer(firstRandom, tick, sequence));
                    second.EnqueueCommand(RandomTransfer(secondRandom, tick, sequence));
                }

                Assert.That(first.AdvanceOneTick().Committed, Is.True);
                Assert.That(second.AdvanceOneTick().Committed, Is.True);
                Assert.That(
                    LogicalStateHasher.ComputeHashHex(first.State),
                    Is.EqualTo(LogicalStateHasher.ComputeHashHex(second.State)),
                    $"the two runs diverged at round {round}");
            }
        }

        [Test]
        public void AnImpossibleTransferLeavesBothSidesExactlyAsTheyWere()
        {
            var engine = NewEngine(SeededInventories(), StorageGraph());
            var before = LogicalStateHasher.ComputeHashHex(engine.State);

            // The backpack holds 300 raw iron and the narrow chest has two slots, so it
            // can take 200 at most. All or nothing means nothing.
            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(NarrowChest, MachinePortKind.Storage),
                ContentIds.RawIron,
                300));

            Assert.That(
                Port(engine, NarrowChest).Count(ContentIds.RawIron).Value,
                Is.EqualTo(0L));
            Assert.That(
                Inventory(engine, Backpack).Count(ContentIds.RawIron).Value,
                Is.EqualTo(300L));
            AssertRejectedBecause(engine, CommandRejectionReason.TransferDestinationFull);

            // The hash covers the rejection record, so it must differ from the hash
            // before the attempt while the two holders are untouched.
            Assert.That(LogicalStateHasher.ComputeHashHex(engine.State), Is.Not.EqualTo(before));
        }

        [Test]
        public void APossibleTransferMovesExactlyWhatItSaid()
        {
            var engine = NewEngine(SeededInventories(), StorageGraph());

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(WideChest, MachinePortKind.Storage),
                ContentIds.RawIron,
                175));

            Assert.That(
                Inventory(engine, Backpack).Count(ContentIds.RawIron).Value,
                Is.EqualTo(125L));
            Assert.That(
                Port(engine, WideChest).Count(ContentIds.RawIron).Value,
                Is.EqualTo(175L));
            Assert.That(engine.State.GetCommandRejectionsCanonical(), Is.Empty);
        }

        [Test]
        public void AMachineInputAdmitsOnlyOneRecipeBatch()
        {
            var engine = NewEngine(SeededInventories(), LineGraph());

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(Press, MachinePortKind.Input),
                ContentIds.IronIngot,
                1));
            Assert.That(
                Node(engine, Press).Input.Count(ContentIds.IronIngot).Value,
                Is.EqualTo(1L));

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(Press, MachinePortKind.Input),
                ContentIds.IronIngot,
                1));
            Assert.That(
                Node(engine, Press).Input.Count(ContentIds.IronIngot).Value,
                Is.Zero,
                "the original batch is now visibly represented by the active cycle");
            Assert.That(Node(engine, Press).IsCycleActive, Is.True);
            AssertRejectedBecause(engine, CommandRejectionReason.TransferDestinationFull);
        }

        [Test]
        public void AMachineInputRejectsItemsItsRecipeDoesNotConsume()
        {
            var engine = NewEngine(SeededInventories(), LineGraph());

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(Press, MachinePortKind.Input),
                ContentIds.RawIron,
                10));
            Assert.That(
                Node(engine, Press).Input.Count(ContentIds.RawIron).Value,
                Is.EqualTo(0L));
            AssertRejectedBecause(engine, CommandRejectionReason.TransferNotAdmitted);
        }

        [Test]
        public void AnActiveMachineRejectsASecondBatchEvenWhenItsInputPortIsEmpty()
        {
            var engine = NewEngine(
                SeededInventories(),
                new MachineSimulationStateBuilder(_catalog)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .WithCycleInFlight(Press, 1000L)
                    .Build());

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(Press, MachinePortKind.Input),
                ContentIds.IronIngot,
                1));

            Assert.That(
                Node(engine, Press).Input.Count(ContentIds.IronIngot).Value,
                Is.Zero,
                "an empty port is not spare capacity while its one batch is under the ram");
            AssertRejectedBecause(engine, CommandRejectionReason.TransferDestinationFull);
        }

        [Test]
        public void AMachineOutputCannotBeFilledByHand()
        {
            // A plate placed in the output by hand would be indistinguishable from one
            // the press made, and every count of production would become a guess.
            var engine = NewEngine(SeededInventories(), LineGraph());

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(Press, MachinePortKind.Output),
                ContentIds.IronPlate,
                5));

            Assert.That(
                Node(engine, Press).Output.Count(ContentIds.IronPlate).Value,
                Is.EqualTo(0L));
            AssertRejectedBecause(engine, CommandRejectionReason.TransferNotAdmitted);
        }

        [Test]
        public void AMachineOutputCanBeEmptiedByHand()
        {
            var engine = NewEngine(
                SeededInventories(),
                new MachineSimulationStateBuilder(_catalog)
                    .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                    .StoreInOutput(Press, ContentIds.IronPlate, 7)
                    .Build());

            Commit(engine, Transfer(
                TransferEndpoint.Port(Press, MachinePortKind.Output),
                TransferEndpoint.Inventory(Backpack),
                ContentIds.IronPlate,
                7));

            Assert.That(
                Node(engine, Press).Output.Count(ContentIds.IronPlate).Value,
                Is.EqualTo(0L));
            Assert.That(
                Inventory(engine, Backpack).Count(ContentIds.IronPlate).Value,
                Is.EqualTo(107L));
        }

        [Test]
        public void ACrateCannotTransferOntoItself()
        {
            // Moving a pile onto itself is not a transfer, and letting it through would
            // run the take and the store against the same storage.
            var engine = NewEngine(SeededInventories(), StorageGraph());

            Commit(engine, Transfer(
                TransferEndpoint.Port(WideChest, MachinePortKind.Storage),
                TransferEndpoint.Port(WideChest, MachinePortKind.Storage),
                ContentIds.RawIron,
                1));

            AssertRejectedBecause(engine, CommandRejectionReason.TransferSameEndpoint);
        }

        [Test]
        public void AMissingHolderIsNamedAsMissing()
        {
            var engine = NewEngine(SeededInventories(), StorageGraph());
            var absent = new StableId(0x9200000000000000UL, 99UL);

            Commit(engine, Transfer(
                TransferEndpoint.Inventory(Backpack),
                TransferEndpoint.Port(absent, MachinePortKind.Storage),
                ContentIds.RawIron,
                1));

            AssertRejectedBecause(engine, CommandRejectionReason.TransferDestinationMissing);
        }

        [Test]
        public void AShortSourceIsNamedAsShort()
        {
            var engine = NewEngine(SeededInventories(), StorageGraph());

            Commit(engine, Transfer(
                TransferEndpoint.Port(WideChest, MachinePortKind.Storage),
                TransferEndpoint.Inventory(Backpack),
                ContentIds.RawIron,
                1));

            AssertRejectedBecause(engine, CommandRejectionReason.InsufficientQuantity);
        }

        [Test]
        public void AMalformedTransferIsRefusedAtTheBoundary()
        {
            var engine = NewEngine(SeededInventories(), StorageGraph());

            Assert.Throws<System.ArgumentException>(() =>
                engine.EnqueueCommand(
                    new SimulationCommand(
                        engine.State.Tick.Next(),
                        0UL,
                        SimulationCommandKinds.Transfer,
                        Backpack,
                        WideChest,
                        1L,
                        new byte[] { 1, 2, 3 })));
        }

        private SimulationCommand Transfer(
            TransferEndpoint source,
            TransferEndpoint destination,
            StableId itemId,
            long amount)
        {
            return new SimulationCommand(
                default,
                0UL,
                SimulationCommandKinds.Transfer,
                source.OwnerId,
                destination.OwnerId,
                amount,
                TransferCommandPayload.Encode(source, destination, itemId));
        }

        private SimulationCommand RandomTransfer(Lcg random, SimulationTick tick, ulong sequence)
        {
            var source = RandomEndpoint(random);
            var destination = RandomEndpoint(random);
            var itemId = TradedItems[random.Next(TradedItems.Length)];

            // Amounts deliberately reach past what any holder has or can take, so most
            // draws exercise a refusal and the successful ones are not all tiny.
            var amount = 1L + random.Next(160);
            return new SimulationCommand(
                tick,
                sequence,
                SimulationCommandKinds.Transfer,
                source.OwnerId,
                destination.OwnerId,
                amount,
                TransferCommandPayload.Encode(source, destination, itemId));
        }

        private static TransferEndpoint RandomEndpoint(Lcg random)
        {
            switch (random.Next(3))
            {
                case 0:
                    return TransferEndpoint.Inventory(Backpack);
                case 1:
                    return TransferEndpoint.Port(WideChest, MachinePortKind.Storage);
                default:
                    return TransferEndpoint.Port(NarrowChest, MachinePortKind.Storage);
            }
        }

        private static void Commit(SimulationEngine engine, SimulationCommand template)
        {
            var tick = engine.State.Tick.Next();
            engine.EnqueueCommand(template.WithSchedule(tick, 0UL));
            var result = engine.AdvanceOneTick();
            Assert.That(
                result.Committed,
                Is.True,
                $"tick aborted in {result.FailedPhase}: {result.FailureCause}");
        }

        private static void AssertRejectedBecause(
            SimulationEngine engine,
            CommandRejectionReason reason)
        {
            var rejections = engine.State.GetCommandRejectionsCanonical();
            Assert.That(rejections, Is.Not.Empty, "the transfer was expected to be refused");
            Assert.That(
                rejections[rejections.Count - 1].Reason,
                Is.EqualTo(reason));
        }

        private static IDictionary<StableId, long> Totals(SimulationEngine engine)
        {
            var totals = new SortedDictionary<StableId, long>();
            var inventories = engine.State.GetInventorySnapshot();
            var machines = engine.State.GetMachineSnapshot();

            for (var index = 0; index < TradedItems.Length; index++)
            {
                var itemId = TradedItems[index];
                var total = 0L;
                if (inventories.TryGet(Backpack, out var backpack))
                {
                    total += backpack.Count(itemId).Value;
                }

                if (machines.TryGetNode(WideChest, out var wide))
                {
                    total += wide.Output.Count(itemId).Value;
                }

                if (machines.TryGetNode(NarrowChest, out var narrow))
                {
                    total += narrow.Output.Count(itemId).Value;
                }

                totals[itemId] = total;
            }

            return totals;
        }

        private static void AssertSameTotals(
            IDictionary<StableId, long> expected,
            IDictionary<StableId, long> actual,
            string because)
        {
            foreach (var pair in expected)
            {
                Assert.That(
                    actual[pair.Key],
                    Is.EqualTo(pair.Value),
                    $"item {pair.Key} changed total {because}");
            }
        }

        private InventorySimulationState SeededInventories()
        {
            return InventorySimulationState.Create(
                _catalog,
                InventoryState.Restore(
                    Backpack,
                    _catalog,
                    ContentIds.PlayerInventory,
                    new[]
                    {
                        new InventoryStackRecord(0, ContentIds.RawIron, new NonNegativeQuantity(100)),
                        new InventoryStackRecord(1, ContentIds.RawIron, new NonNegativeQuantity(100)),
                        new InventoryStackRecord(2, ContentIds.RawIron, new NonNegativeQuantity(100)),
                        new InventoryStackRecord(3, ContentIds.IronIngot, new NonNegativeQuantity(100)),
                        new InventoryStackRecord(4, ContentIds.IronIngot, new NonNegativeQuantity(100)),
                        new InventoryStackRecord(5, ContentIds.IronPlate, new NonNegativeQuantity(100))
                    }));
        }

        private MachineSimulationState StorageGraph()
        {
            return new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(WideChest, ContentIds.WoodenCrate)
                .AddNarrowBuffer(NarrowChest, ContentIds.WoodenCrate, 2)
                .Build();
        }

        private MachineSimulationState LineGraph()
        {
            return new MachineSimulationStateBuilder(_catalog)
                .AddBuffer(WideChest, ContentIds.WoodenCrate)
                .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate)
                .Build();
        }

        private SimulationEngine NewEngine(
            InventorySimulationState inventories,
            MachineSimulationState machines)
        {
            var state = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                new AirshipSimulationState(),
                machines,
                inventories);
            return new SimulationEngine(state, null, _catalog);
        }

        private static InventoryState Inventory(SimulationEngine engine, StableId id)
        {
            Assert.That(
                engine.State.GetInventorySnapshot().TryGet(id, out var inventory),
                Is.True,
                $"inventory {id} is missing");
            return inventory;
        }

        private static MachineNodeState Node(SimulationEngine engine, StableId id)
        {
            Assert.That(
                engine.State.GetMachineSnapshot().TryGetNode(id, out var node),
                Is.True,
                $"node {id} is missing");
            return node;
        }

        private static MachinePort Port(SimulationEngine engine, StableId id)
        {
            return Node(engine, id).Output;
        }

        /// <summary>
        /// A 32-bit linear congruential generator, written out so the sequence is fixed
        /// by this file and not by the runtime's implementation of Random.
        /// </summary>
        private sealed class Lcg
        {
            private uint _state;

            public Lcg(uint seed)
            {
                _state = seed == 0U ? 1U : seed;
            }

            public int Next(int exclusiveBound)
            {
                _state = unchecked((_state * 1664525U) + 1013904223U);
                return (int)((_state >> 8) % (uint)exclusiveBound);
            }
        }
    }
}

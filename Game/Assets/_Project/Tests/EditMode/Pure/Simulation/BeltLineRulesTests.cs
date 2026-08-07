using CML.Content;
using CML.Foundation;
using CML.Simulation.Machines;
using NUnit.Framework;

namespace CML.Tests.Pure.Simulation
{
    public sealed class BeltLineRulesTests
    {
        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void ConnectedDrivePublishesDirectionAndTwelveCapacityUnits()
        {
            var drive = Id(1);
            var straight = Id(2);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(drive, ContentIds.BeltDriveUnit, Pose(0, 0))
                .AddBeltModule(straight, ContentIds.BeltStraight, Pose(0, 1))
                .Build();

            BeltLineRules.Recompute(state);

            Assert.That(Node(state, drive).BeltLineStatus, Is.EqualTo(BeltLineStatus.Operational));
            Assert.That(Node(state, straight).BeltTravelDirection, Is.EqualTo(BeltTravelDirection.Forward));
            Assert.That(Node(state, straight).BeltLineUsedCapacity, Is.EqualTo(1));
            Assert.That(Node(state, straight).BeltLineAvailableCapacity, Is.EqualTo(12));
        }

        [Test]
        public void LineWithoutDriveStops()
        {
            var straight = Id(10);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(straight, ContentIds.BeltStraight, Pose(0, 0))
                .Build();

            BeltLineRules.Recompute(state);

            Assert.That(Node(state, straight).BeltLineStatus, Is.EqualTo(BeltLineStatus.MissingDrive));
            Assert.That(Node(state, straight).BeltTravelDirection, Is.EqualTo(BeltTravelDirection.Stopped));
        }

        [Test]
        public void ThirteenthConsumerOverloadsOneDrive()
        {
            var builder = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(Id(20), ContentIds.BeltDriveUnit, Pose(0, 0));
            for (var index = 1; index <= 13; index++)
            {
                builder.AddBeltModule(
                    Id((ulong)(20 + index)),
                    ContentIds.BeltStraight,
                    Pose(0, index));
            }

            var state = builder.Build();
            BeltLineRules.Recompute(state);

            Assert.That(Node(state, Id(20)).BeltLineStatus, Is.EqualTo(BeltLineStatus.Overloaded));
            Assert.That(Node(state, Id(20)).BeltLineUsedCapacity, Is.EqualTo(13));
            Assert.That(Node(state, Id(20)).BeltLineAvailableCapacity, Is.EqualTo(12));
        }

        private static MachineNodeState Node(
            MachineSimulationState state,
            StableId id)
        {
            Assert.That(state.TryGetNode(id, out var node), Is.True);
            return node;
        }

        private static StableId Id(ulong value) =>
            new StableId(0xBE17000000000000UL, value);

        private static MachineBuildPose Pose(int x, int z) =>
            new MachineBuildPose(x * 1000, 0, z * 1000, 0);
    }
}

using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Runtime glue tests for M0.4B. Pure simulation tests already prove each reducer;
    /// these tests prove that the Unity composition root constructs the promised graph,
    /// that its UI bridge writes to that same authority, and that BUILD-001 resolves
    /// the entity created by its authoritative command.
    /// </summary>
    public sealed class FactoryRuntimeContractTests
    {
        private GameObject _systems;
        private TransferCommandBridge _bridge;
        private FactoryHudOrchestrator _hud;
        private FactoryLineSimulationRoot _root;

        [SetUp]
        public void SetUp()
        {
            _systems = new GameObject("FactoryRuntimeContractTests");
            _bridge = _systems.AddComponent<TransferCommandBridge>();
            _hud = _systems.AddComponent<FactoryHudOrchestrator>();
            _root = _systems.AddComponent<FactoryLineSimulationRoot>();
            _hud.ConfigureUi(null, null, null, _bridge);
            _root.Configure(_bridge, null, _hud);
            _root.InitializeNow();
        }

        [TearDown]
        public void TearDown()
        {
            if (_systems != null)
            {
                Object.DestroyImmediate(_systems);
            }
        }

        [Test]
        public void CompositionRootBuildsOneLineAndSharesOneEngineWithTheUiBridge()
        {
            Assert.That(_bridge.Engine, Is.SameAs(_root.Engine));
            Assert.That(_hud.Engine, Is.SameAs(_root.Engine));

            var machines = _root.Engine.State.GetMachineSnapshot();
            Assert.That(machines.NodeCount, Is.EqualTo(13));
            Assert.That(machines.LaneCount, Is.Zero);
            AssertNode(machines, FactoryLineSimulationRoot.SourceCrateId, MachineNodeKind.Buffer);
            AssertNode(machines, FactoryLineSimulationRoot.InputFunnelId, MachineNodeKind.Funnel);
            AssertNode(machines, FactoryLineSimulationRoot.FeedBelt01Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.FeedBelt02Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.FeedBelt03Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.FeedBelt04Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.PressId, MachineNodeKind.Machine);
            AssertNode(machines, FactoryLineSimulationRoot.DrainBelt01Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.DrainBelt02Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.DrainBelt03Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.DrainBelt04Id, MachineNodeKind.BeltModule);
            AssertNode(machines, FactoryLineSimulationRoot.OutputFunnelId, MachineNodeKind.Funnel);
            AssertNode(machines, FactoryLineSimulationRoot.SinkCrateId, MachineNodeKind.Buffer);

            Assert.That(
                _root.TryGetPlayerInventory(out var inventory),
                Is.True);
            Assert.That(
                inventory.Count(ContentIds.IronIngot).Value,
                Is.EqualTo(12L));
            Assert.That(
                inventory.Count(ContentIds.WoodenCrateItem).Value,
                Is.EqualTo(2L));
            Assert.That(
                inventory.Count(ContentIds.BeltFunnel).Value,
                Is.EqualTo(4L));
            Assert.That(
                inventory.Count(ContentIds.BeltStraight).Value,
                Is.EqualTo(24L));
            Assert.That(
                inventory.Count(ContentIds.MechanicalPressItem).Value,
                Is.EqualTo(1L));
        }

        [Test]
        public void ChestBridgeFeedsTheCompletePreplacedLineWithoutASecondEngine()
        {
            var accepted = _bridge.SubmitTransfer(
                TransferEndpoint.Inventory(FactoryLineSimulationRoot.PlayerInventoryId),
                TransferEndpoint.Port(
                    FactoryLineSimulationRoot.SourceCrateId,
                    MachinePortKind.Storage),
                ContentIds.IronIngot,
                new NonNegativeQuantity(12));
            Assert.That(
                accepted.Command.TargetTick,
                Is.EqualTo(_root.Engine.State.Tick.Next()));

            const int maximumTicks = 2_000;
            var reachedSink = false;
            for (var index = 0; index < maximumTicks; index++)
            {
                var result = _root.Engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"tick {result.ExecutingTick} failed in {result.FailedPhase}: "
                    + result.FailureCause);

                if (Node(FactoryLineSimulationRoot.SinkCrateId)
                    .Input.Count(ContentIds.IronPlate).Value == 12L)
                {
                    reachedSink = true;
                    break;
                }
            }

            Assert.That(
                reachedSink,
                Is.True,
                "The twelve ingots never completed the preplaced line.");
            Assert.That(
                _root.TryGetPlayerInventory(out var player),
                Is.True);
            Assert.That(player.Count(ContentIds.IronIngot).Value, Is.Zero);
            Assert.That(
                Node(FactoryLineSimulationRoot.SourceCrateId)
                    .Input.Count(ContentIds.IronIngot).Value,
                Is.Zero);
            Assert.That(
                Node(FactoryLineSimulationRoot.InputFunnelId)
                    .Input.Count(ContentIds.IronIngot).Value,
                Is.Zero);
            Assert.That(
                Node(FactoryLineSimulationRoot.PressId).CompletedCycles,
                Is.EqualTo(12UL));
            Assert.That(
                Node(FactoryLineSimulationRoot.PressId)
                    .Input.Count(ContentIds.IronIngot).Value,
                Is.Zero);
            Assert.That(
                Node(FactoryLineSimulationRoot.PressId)
                    .Output.Count(ContentIds.IronPlate).Value,
                Is.Zero);
            Assert.That(
                Node(FactoryLineSimulationRoot.OutputFunnelId)
                    .Input.Count(ContentIds.IronPlate).Value,
                Is.Zero);
            Assert.That(Node(FactoryLineSimulationRoot.FeedBelt01Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.FeedBelt02Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.FeedBelt03Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.FeedBelt04Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.DrainBelt01Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.DrainBelt02Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.DrainBelt03Id).Input.IsEmpty, Is.True);
            Assert.That(Node(FactoryLineSimulationRoot.DrainBelt04Id).Input.IsEmpty, Is.True);
        }

        [Test]
        public void BuildSubmissionConsumesOwnedItemAndResolvesTheCommittedEntity()
        {
            Assert.That(_root.TryGetPlayerInventory(out var before), Is.True);
            var cratesBefore = before.Count(ContentIds.WoodenCrateItem).Value;
            var specification = MachineBuildSpecification.Buffer(
                ContentIds.WoodenCrate,
                ContentIds.WoodenCrateItem,
                1L,
                new MachineBuildPose(6_000, 0, 2_000, 1));
            var accepted = _root.SubmitBuild(specification);

            var result = _root.Engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True);
            Assert.That(
                _root.TryResolveCreatedEntity(accepted.Command, out var createdId),
                Is.True);
            Assert.That(createdId.IsNone, Is.False);
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().TryGetNode(
                    createdId,
                    out var created),
                Is.True);
            Assert.That(created.Kind, Is.EqualTo(MachineNodeKind.Buffer));
            Assert.That(created.DefinitionId, Is.EqualTo(ContentIds.WoodenCrate));
            Assert.That(_root.TryGetPlayerInventory(out var after), Is.True);
            Assert.That(
                after.Count(ContentIds.WoodenCrateItem).Value,
                Is.EqualTo(cratesBefore - 1L));
        }

        [Test]
        public void RejectedBuildDoesNotConsumeTheOwnedItem()
        {
            Assert.That(_root.TryGetPlayerInventory(out var before), Is.True);
            var cratesBefore = before.Count(ContentIds.WoodenCrateItem).Value;
            var nodeCountBefore =
                _root.Engine.State.GetMachineSnapshot().NodeCount;
            var specification = MachineBuildSpecification.Buffer(
                ContentIds.WoodenCrate,
                ContentIds.WoodenCrateItem,
                2L,
                new MachineBuildPose(6_000, 0, 2_000, 1));
            var accepted = _root.SubmitBuild(specification);

            var result = _root.Engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(
                _root.TryResolveCreatedEntity(accepted.Command, out _),
                Is.False);
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().NodeCount,
                Is.EqualTo(nodeCountBefore));
            var rejections =
                _root.Engine.State.GetCommandRejectionsCanonical();
            Assert.That(rejections.Count, Is.EqualTo(1));
            Assert.That(
                rejections[0].Reason,
                Is.EqualTo(CommandRejectionReason.BuildDefinitionMissing));
            Assert.That(_root.TryGetPlayerInventory(out var after), Is.True);
            Assert.That(
                after.Count(ContentIds.WoodenCrateItem).Value,
                Is.EqualTo(cratesBefore));
        }

        [Test]
        public void ClickingFunnelOverhangResolvesBeltToItsFreeFrontCell()
        {
            var crateId =
                new StableId(0xB001000000000001UL, 1UL);
            var funnelId =
                new StableId(0xB001000000000001UL, 2UL);
            var machines = new MachineSimulationStateBuilder(_root.Catalog)
                .AddBuffer(
                    crateId,
                    ContentIds.WoodenCrate,
                    new MachineBuildPose(0, 0, 0, 0))
                .AddFunnel(
                    funnelId,
                    ContentIds.BeltFunnel,
                    new MachineBuildPose(0, 0, 1_000, 0))
                .Build();
            var clickedOccupiedCell =
                new MachineBuildPose(0, 0, 1_000, 0);

            var resolved =
                FactoryBuildPlacementResolver.ResolveOccupiedConnectorCell(
                    machines,
                    clickedOccupiedCell,
                    MachineBuildKind.BeltModule);

            Assert.That(resolved.XMillimetres, Is.Zero);
            Assert.That(resolved.YMillimetres, Is.Zero);
            Assert.That(resolved.ZMillimetres, Is.EqualTo(2_000));
            var resolvedFromShiftedVisual =
                FactoryBuildPlacementResolver.ResolveConnectorCell(
                    machines,
                    funnelId,
                    new MachineBuildPose(1_000, 0, 1_000, 0),
                    MachineBuildKind.BeltModule);
            Assert.That(resolvedFromShiftedVisual.XMillimetres, Is.Zero);
            Assert.That(resolvedFromShiftedVisual.ZMillimetres, Is.EqualTo(2_000));
            var belt = MachineBuildSpecification.BeltModule(
                ContentIds.BeltStraight,
                resolved);
            Assert.That(
                MachineBuildRule.TryPreflightTopology(
                    machines,
                    _root.Catalog,
                    belt,
                    out var rejection),
                Is.True,
                rejection.ToString());
        }

        [Test]
        public void SalvageReturnsTheComponentAndItsContentsThenRemovesTheNode()
        {
            Assert.That(_root.TryGetPlayerInventory(out var before), Is.True);
            var cratesBefore = before.Count(ContentIds.WoodenCrateItem).Value;
            var nodesBefore =
                _root.Engine.State.GetMachineSnapshot().NodeCount;

            // Build a crate, put twelve ingots in it, then take the crate back. The
            // ingots must come back too: salvage is lossless or it refuses.
            var accepted = _root.SubmitBuild(
                MachineBuildSpecification.Buffer(
                    ContentIds.WoodenCrate,
                    ContentIds.WoodenCrateItem,
                    1L,
                    new MachineBuildPose(9_000, 0, 9_000, 0)));
            Assert.That(_root.Engine.AdvanceOneTick().Committed, Is.True);
            Assert.That(
                _root.TryResolveCreatedEntity(accepted.Command, out var crateId),
                Is.True);

            _bridge.SubmitTransfer(
                TransferEndpoint.Inventory(
                    FactoryLineSimulationRoot.PlayerInventoryId),
                TransferEndpoint.Port(crateId, MachinePortKind.Storage),
                ContentIds.IronIngot,
                new NonNegativeQuantity(12));
            Assert.That(_root.Engine.AdvanceOneTick().Committed, Is.True);
            Assert.That(
                Node(crateId).Input.Count(ContentIds.IronIngot).Value,
                Is.EqualTo(12L));
            Assert.That(_root.TryGetPlayerInventory(out var stocked), Is.True);
            Assert.That(stocked.Count(ContentIds.IronIngot).Value, Is.Zero);

            _root.SubmitSalvage(crateId);
            var salvaged = _root.Engine.AdvanceOneTick();

            Assert.That(salvaged.Committed, Is.True, salvaged.FailureCause);
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().TryGetNode(crateId, out _),
                Is.False,
                "the salvaged crate is still in the graph");
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().NodeCount,
                Is.EqualTo(nodesBefore));
            Assert.That(_root.TryGetPlayerInventory(out var after), Is.True);
            Assert.That(
                after.Count(ContentIds.WoodenCrateItem).Value,
                Is.EqualTo(cratesBefore),
                "the crate itself did not come back");
            Assert.That(
                after.Count(ContentIds.IronIngot).Value,
                Is.EqualTo(12L),
                "the twelve ingots inside the crate were lost");
        }

        [Test]
        public void SalvagingOutOfThePreplacedLineLeavesTheGraphTickable()
        {
            // Removing a node from the middle of a working line is where a dangling
            // reference would surface: the reducer throws a SimulationInvariantException
            // on the tick after, not on the tick that removed it. So the assertion that
            // matters is that the graph keeps committing afterwards.
            var beltId = FactoryLineSimulationRoot.FeedBelt02Id;
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().TryGetNode(beltId, out _),
                Is.True);

            _root.SubmitSalvage(beltId);
            var removal = _root.Engine.AdvanceOneTick();

            Assert.That(removal.Committed, Is.True, removal.FailureCause);
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().TryGetNode(beltId, out _),
                Is.False);
            for (var index = 0; index < 20; index++)
            {
                var result = _root.Engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"tick {result.ExecutingTick} failed in {result.FailedPhase}: "
                    + result.FailureCause);
            }

            Assert.That(_root.TryGetPlayerInventory(out var after), Is.True);
            Assert.That(
                after.Count(ContentIds.BeltStraight).Value,
                Is.EqualTo(25L),
                "the salvaged belt module did not return to the inventory");
        }

        [Test]
        public void ClickingACrateWithABeltPlacesItOnAFreeSideInsteadOfRefusing()
        {
            // A belt does not connect to a crate - that is the funnel's job, and
            // the simulation still enforces it. But not connecting is no reason
            // to forbid the belt from occupying the cell next door.
            var crateId = new StableId(0xB001000000000002UL, 1UL);
            var machines = new MachineSimulationStateBuilder(_root.Catalog)
                .AddBuffer(
                    crateId,
                    ContentIds.WoodenCrate,
                    new MachineBuildPose(0, 0, 0, 0))
                .Build();

            var resolved =
                FactoryBuildPlacementResolver.ResolveConnectorCell(
                    machines,
                    crateId,
                    new MachineBuildPose(0, 0, 0, 0),
                    MachineBuildKind.BeltModule);

            Assert.That(
                FactoryBuildPlacementResolver.TryGetOccupant(
                    machines,
                    resolved,
                    out _),
                Is.False,
                "the belt was steered back onto the occupied crate cell");
            var belt = MachineBuildSpecification.BeltModule(
                ContentIds.BeltStraight,
                resolved);
            Assert.That(
                MachineBuildRule.TryPreflightTopology(
                    machines,
                    _root.Catalog,
                    belt,
                    out var rejection),
                Is.True,
                rejection.ToString());
        }

        [Test]
        public void OccupiedCrateFaceIsRejectedInsteadOfMovingTheFunnelElsewhere()
        {
            // The crosshair and yaw selected the north face. If that exact face is
            // occupied, the preview must stay there and turn red. Moving the piece
            // to another side is an unrequested decision by the building system.
            var crateId = new StableId(0xB001000000000003UL, 1UL);
            var beltId = new StableId(0xB001000000000003UL, 2UL);
            var machines = new MachineSimulationStateBuilder(_root.Catalog)
                .AddBuffer(
                    crateId,
                    ContentIds.WoodenCrate,
                    new MachineBuildPose(0, 0, 0, 0))
                .AddBeltModule(
                    beltId,
                    ContentIds.BeltStraight,
                    new MachineBuildPose(0, 0, 1_000, 0))
                .Build();

            var resolved =
                FactoryBuildPlacementResolver.ResolveConnectorCell(
                    machines,
                    crateId,
                    new MachineBuildPose(0, 0, 0, 0),
                    MachineBuildKind.Funnel);

            Assert.That(
                FactoryBuildPlacementResolver.TryGetOccupant(
                    machines,
                    resolved,
                    out _),
                Is.True,
                "the funnel silently wandered away from the selected crate face");
            var funnel = MachineBuildSpecification.Funnel(
                ContentIds.BeltFunnel,
                resolved);
            Assert.That(
                MachineBuildRule.TryPreflightTopology(
                    machines,
                    _root.Catalog,
                    funnel,
                    out var rejection),
                Is.False);
            Assert.That(
                rejection,
                Is.EqualTo(CommandRejectionReason.BuildTopologyInvalid));
        }

        [Test]
        public void OneNeutralCrateAcceptsAFunnelOnEachOfItsFourFaces()
        {
            var crateId = new StableId(0xB001000000000004UL, 1UL);
            var machines = new MachineSimulationStateBuilder(_root.Catalog)
                .AddBuffer(
                    crateId,
                    ContentIds.WoodenCrate,
                    new MachineBuildPose(0, 0, 0, 3))
                .Build();
            var expectedX = new[] { 0, 1_000, 0, -1_000 };
            var expectedZ = new[] { 1_000, 0, -1_000, 0 };

            for (byte side = 0; side < 4; side++)
            {
                var resolved =
                    FactoryBuildPlacementResolver.ResolveFromTarget(
                        machines,
                        crateId,
                        new MachineBuildPose(0, 0, 0, side),
                        MachineBuildKind.Funnel,
                        side,
                        side,
                        yawExplicitlyRotated: false);

                Assert.That(
                    resolved.XMillimetres,
                    Is.EqualTo(expectedX[side]),
                    $"wrong X on crate side {side}");
                Assert.That(
                    resolved.ZMillimetres,
                    Is.EqualTo(expectedZ[side]),
                    $"wrong Z on crate side {side}");
                Assert.That(resolved.YawQuarterTurns, Is.EqualTo(side));
                Assert.That(
                    FactoryBuildPlacementResolver.ArePortsAdjacent(
                        MachineNodeKind.Buffer,
                        new MachineBuildPose(0, 0, 0, 3),
                        MachineNodeKind.Funnel,
                        resolved),
                    Is.True,
                    $"crate rotation incorrectly disabled side {side}");
            }
        }

        [Test]
        public void PlacingTheCrateAfterTheFunnelUsesTheSamePhysicalContract()
        {
            var funnelId = new StableId(0xB001000000000005UL, 1UL);
            var funnelPose = new MachineBuildPose(2_000, 0, 3_000, 1);
            var machines = new MachineSimulationStateBuilder(_root.Catalog)
                .AddFunnel(
                    funnelId,
                    ContentIds.BeltFunnel,
                    funnelPose)
                .Build();

            var cratePose =
                FactoryBuildPlacementResolver.ResolveFromTarget(
                    machines,
                    funnelId,
                    funnelPose,
                    MachineBuildKind.Buffer,
                    aimedSideYaw: 0,
                    heldYaw: 2,
                    yawExplicitlyRotated: true);

            Assert.That(cratePose.XMillimetres, Is.EqualTo(1_000));
            Assert.That(cratePose.ZMillimetres, Is.EqualTo(3_000));
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.Funnel,
                    funnelPose,
                    MachineNodeKind.Buffer,
                    cratePose),
                Is.True);
        }

        [Test]
        public void StandaloneModulesNeedNoEndpointToPassTopologyPreflight()
        {
            var machines = new MachineSimulationStateBuilder(_root.Catalog)
                .Build();
            var belt = MachineBuildSpecification.BeltModule(
                ContentIds.BeltStraight,
                new MachineBuildPose(12_000, 0, -7_000, 3));
            var funnel = MachineBuildSpecification.Funnel(
                ContentIds.BeltFunnel,
                new MachineBuildPose(-8_000, 0, 4_000, 1));

            Assert.That(
                MachineBuildRule.TryPreflightTopology(
                    machines,
                    _root.Catalog,
                    belt,
                    out var beltRejection),
                Is.True,
                beltRejection.ToString());
            Assert.That(
                MachineBuildRule.TryPreflightTopology(
                    machines,
                    _root.Catalog,
                    funnel,
                    out var funnelRejection),
                Is.True,
                funnelRejection.ToString());
        }

        private MachineNodeState Node(StableId id)
        {
            Assert.That(
                _root.Engine.State.GetMachineSnapshot().TryGetNode(id, out var node),
                Is.True,
                $"Missing node {id}.");
            return node;
        }

        private static void AssertNode(
            MachineSimulationState machines,
            StableId id,
            MachineNodeKind expectedKind)
        {
            Assert.That(
                machines.TryGetNode(id, out var node),
                Is.True,
                $"Missing node {id}.");
            Assert.That(node.Kind, Is.EqualTo(expectedKind));
        }
    }

}

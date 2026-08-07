using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Replay;
using NUnit.Framework;

namespace CML.Tests.Pure.Airship
{
    public sealed class AirshipSimulationTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId AirshipId = new StableId(1, 10);
        private static readonly StableId PlayerId = new StableId(1, 20);
        private static readonly StableId SurfaceId = new StableId(1, 30);
        private static readonly StableId ObstacleId = new StableId(1, 40);

        [Test]
        public void Turn16CardinalsAndWrapAreExact()
        {
            AssertSinCos(0, 0, FixedTurnTrig.One);
            AssertSinCos(16_384, FixedTurnTrig.One, 0);
            AssertSinCos(32_768, 0, -FixedTurnTrig.One);
            AssertSinCos(49_152, -FixedTurnTrig.One, 0);

            var forward = new AirshipVector3Millimetres(0, 0, 1_000);
            Assert.That(
                FixedTurnTrig.RotateLocalToWorld(forward, 16_384),
                Is.EqualTo(new AirshipVector3Millimetres(1_000, 0, 0)));
            Assert.That(
                FixedTurnTrig.RotateLocalToWorld(forward, 49_152),
                Is.EqualTo(new AirshipVector3Millimetres(-1_000, 0, 0)));
        }

        [Test]
        public void AnchoredThrottleAutoTakesOffAndAdvancesOnTheSameGlobalTick()
        {
            var engine = CreatePilotedEngine();
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                new SimulationTick(1),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(1_000, 0, 0, 0)));

            var result = engine.AdvanceOneTick();

            Assert.That(result.Committed, Is.True, result.FailureCause);
            Assert.That(engine.State.Tick.Value, Is.EqualTo(1));
            Assert.That(
                GetAirship(engine).Mode,
                Is.EqualTo(AirshipFlightMode.Flying));
            Assert.That(
                GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(250));
            Assert.That(GetAirship(engine).Pose.Position.Z, Is.EqualTo(12));
            Assert.That(
                GetAirship(engine).ForwardIntegrationRemainder,
                Is.EqualTo(10));
        }

        [Test]
        public void ReverseMotionAlwaysStoresEuclideanRemainders()
        {
            var engine = CreatePilotedEngine();
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                new SimulationTick(1),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(-1_000, -1_000, -1_000, -1_000)));

            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            var airship = GetAirship(engine);
            Assert.That(airship.ForwardSpeedMillimetresPerSecond, Is.EqualTo(-250));
            Assert.That(airship.PitchTurnUnits, Is.EqualTo(-192));
            Assert.That(airship.ForwardIntegrationRemainder, Is.InRange(0, 19));
            Assert.That(airship.StrafeIntegrationRemainder, Is.Zero);
            Assert.That(airship.VerticalIntegrationRemainder, Is.InRange(0, 19));
            Assert.That(airship.YawIntegrationRemainder, Is.InRange(0, 19));
        }

        [Test]
        public void ForwardCollisionRejectsWholePoseAndClearsAllMotion()
        {
            var engine = CreatePilotedEngine(builder => builder.AddObstacle(
                ObstacleId,
                new AirshipVector3Millimetres(-100, 5_000, 7_055),
                new AirshipVector3Millimetres(100, 5_700, 7_065)));

            FlyOneTick(engine, new AirshipPilotInputState(1_000, 0, 0, 0));

            AssertBlockedAtOrigin(GetAirship(engine));
        }

        [Test]
        public void PitchCollisionRejectsTranslationAndPitchAtomically()
        {
            var engine = CreatePilotedEngine(builder => builder.AddObstacle(
                ObstacleId,
                new AirshipVector3Millimetres(-100, 7_655, 0),
                new AirshipVector3Millimetres(100, 7_665, 100)));

            FlyOneTick(engine, new AirshipPilotInputState(1_000, 0, 0, 0));
            var poseBeforePitch = GetAirship(engine).Pose;
            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 0, 0, 1_000)));

            AssertBlockedAtPose(GetAirship(engine), poseBeforePitch, 0);
        }

        [Test]
        public void ForwardYawCollisionDoesNotPenetrateOrCommitYaw()
        {
            var engine = CreatePilotedEngine(builder => builder.AddObstacle(
                ObstacleId,
                new AirshipVector3Millimetres(2_755, 5_000, 6_500),
                new AirshipVector3Millimetres(2_900, 5_700, 6_800)));

            AccelerateForward(engine, 16);
            var poseBeforeYaw = GetAirship(engine).Pose;
            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 0, 1_000, 0)));

            AssertBlockedAtPose(GetAirship(engine), poseBeforeYaw, 0);
        }

        [Test]
        public void MouseYawAndPitchHaveNoAuthorityAtZeroForwardSpeed()
        {
            var engine = CreatePilotedEngine();
            FlyOneTick(engine, new AirshipPilotInputState(1_000, 0, 0, 0));
            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(-1_000, 0, 1_000, 1_000)));

            var airship = GetAirship(engine);
            Assert.That(airship.Mode, Is.EqualTo(AirshipFlightMode.Flying));
            Assert.That(airship.ForwardSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(airship.Pose.YawTurn, Is.Zero);
            Assert.That(airship.YawRateTurnUnitsPerSecond, Is.Zero);
            Assert.That(airship.PitchTurnUnits, Is.Zero);
        }

        [Test]
        public void MouseYawAuthorityScalesWithSpeedAndIsFullAtFourMetresPerSecond()
        {
            var halfAuthority = CreatePilotedEngine();
            AccelerateForward(halfAuthority, 8);
            AdvanceWith(halfAuthority, AirshipCommandCodec.PilotInput(
                halfAuthority.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 0, 1_000, 0)));

            Assert.That(
                GetAirship(halfAuthority).ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(2_000));
            Assert.That(
                GetAirship(halfAuthority).YawRateTurnUnitsPerSecond,
                Is.EqualTo(4_096));

            var fullAuthority = CreatePilotedEngine();
            AccelerateForward(fullAuthority, 16);
            AdvanceWith(fullAuthority, AirshipCommandCodec.PilotInput(
                fullAuthority.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 0, 1_000, 0)));

            Assert.That(
                GetAirship(fullAuthority).ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(4_000));
            Assert.That(
                GetAirship(fullAuthority).YawRateTurnUnitsPerSecond,
                Is.EqualTo(8_192));
        }

        [Test]
        public void MousePitchControlsNoseAndAddsExpectedVerticalMotion()
        {
            var noseDown = CreatePilotedEngine();
            AccelerateForward(noseDown, 16);
            AdvanceWith(noseDown, AirshipCommandCodec.PilotInput(
                noseDown.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 0, 0, 1_000)));

            Assert.That(GetAirship(noseDown).PitchTurnUnits, Is.EqualTo(192));
            Assert.That(GetAirship(noseDown).Pose.Position.Y, Is.LessThan(0));

            var noseUp = CreatePilotedEngine();
            AccelerateForward(noseUp, 16);
            AdvanceWith(noseUp, AirshipCommandCodec.PilotInput(
                noseUp.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 0, 0, -1_000)));

            Assert.That(GetAirship(noseUp).PitchTurnUnits, Is.EqualTo(-192));
            Assert.That(GetAirship(noseUp).Pose.Position.Y, Is.GreaterThan(0));
        }

        [Test]
        public void LiftInputRemainsDirectVerticalControlAtZeroForwardSpeed()
        {
            var engine = CreatePilotedEngine();
            FlyOneTick(engine, new AirshipPilotInputState(1_000, 0, 0, 0));
            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(-1_000, 0, 0, 0)));
            var beforeLift = GetAirship(engine).Pose.Position;

            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(0, 1_000, 1_000, 1_000)));

            var airship = GetAirship(engine);
            Assert.That(airship.ForwardSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(airship.VerticalSpeedMillimetresPerSecond, Is.EqualTo(75));
            Assert.That(airship.Pose.Position.Y - beforeLift.Y, Is.EqualTo(3));
            Assert.That(airship.Pose.Position.Z, Is.EqualTo(beforeLift.Z));
            Assert.That(airship.Pose.YawTurn, Is.Zero);
            Assert.That(airship.PitchTurnUnits, Is.Zero);
        }

        [Test]
        public void LandingRequiresCanonicalContinuousWideSurfaceAndClearCorridor()
        {
            var valid = BuildLandingGeometry(400, false);
            var tooNarrow = BuildLandingGeometry(399, false);
            var obstructed = BuildLandingGeometry(400, true);
            var pose = new AirshipPoseState(
                new AirshipVector3Millimetres(0, 0, 12),
                0);

            Assert.That(
                AirshipLandingValidator.IsReachable(valid, pose, SurfaceId),
                Is.True);
            Assert.That(
                AirshipLandingValidator.IsReachable(tooNarrow, pose, SurfaceId),
                Is.False);
            Assert.That(
                AirshipLandingValidator.IsReachable(obstructed, pose, SurfaceId),
                Is.False);
            Assert.That(
                AirshipLandingValidator.IsReachable(valid, pose, new StableId(9, 9)),
                Is.False);
        }

        [Test]
        public void StabilizationLocksCockpitAndCannotArmNextTakeoff()
        {
            var engine = CreatePilotedEngine(AddValidLandingSurface);
            FlyOneTick(engine, new AirshipPilotInputState(1_000, 0, 0, 0));

            var landingTick = engine.State.Tick.Next();
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                landingTick,
                0,
                PlayerId,
                AirshipId,
                AirshipPilotInputState.None));
            engine.EnqueueCommand(AirshipCommandCodec.LandingRequest(
                landingTick,
                1,
                PlayerId,
                AirshipId,
                SurfaceId));
            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            Assert.That(GetAirship(engine).Mode, Is.EqualTo(AirshipFlightMode.Stabilizing));

            var blockedTick = engine.State.Tick.Next();
            engine.EnqueueCommand(AirshipCommandCodec.PilotEnd(
                blockedTick,
                0,
                PlayerId,
                AirshipId));
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                blockedTick,
                1,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(1_000, 1_000, 1_000, 1_000)));
            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            Assert.That(GetAirship(engine).PilotId, Is.EqualTo(PlayerId));
            Assert.That(GetAirship(engine).HeldInput, Is.EqualTo(AirshipPilotInputState.None));

            while (GetAirship(engine).Mode == AirshipFlightMode.Stabilizing)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            }

            var anchoredPose = GetAirship(engine).Pose;
            engine.EnqueueCommand(AirshipCommandCodec.Takeoff(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId));
            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            Assert.That(GetAirship(engine).Pose, Is.EqualTo(anchoredPose));
            Assert.That(
                GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                Is.Zero);
        }

        [Test]
        public void CoreCommandsCompleteBoardFlyLandAndDisembarkCycle()
        {
            var airshipState = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .AddPlayer(
                    PlayerId,
                    new AirshipPoseState(
                        new AirshipVector3Millimetres(
                            AirshipSimulationConstants.BoardingVolumeCenter.X,
                            0,
                            AirshipSimulationConstants.BoardingVolumeCenter.Z),
                        0))
                .AddLandingSurface(
                    SurfaceId,
                    new AirshipVector3Millimetres(4_890, 0, 1_432),
                    0,
                    400,
                    450,
                    StableId.None)
                .Build();
            var engine = new SimulationEngine(
                new SimulationState(new SimulationTick(0), Revision, airshipState));

            AdvanceWith(engine, AirshipCommandCodec.Board(
                new SimulationTick(1), 0, PlayerId, AirshipId));
            AssertPlayerFrame(engine, AirshipPlayerFrameKind.Airship, false);

            var pilotTick = engine.State.Tick.Next();
            AdvanceWith(
                engine,
                AirshipCommandCodec.PilotBegin(
                    pilotTick, 0, PlayerId, AirshipId));
            AssertPlayerFrame(engine, AirshipPlayerFrameKind.Airship, true);

            var takeoffTick = engine.State.Tick.Next();
            AdvanceWith(
                engine,
                AirshipCommandCodec.PilotInput(
                    takeoffTick,
                    0,
                    PlayerId,
                    AirshipId,
                    new AirshipPilotInputState(1_000, 0, 0, 0)));
            Assert.That(GetAirship(engine).Pose.Position.Z, Is.EqualTo(12));

            var landingTick = engine.State.Tick.Next();
            AdvanceWith(
                engine,
                AirshipCommandCodec.PilotInput(
                    landingTick,
                    0,
                    PlayerId,
                    AirshipId,
                    AirshipPilotInputState.None),
                AirshipCommandCodec.LandingRequest(
                    landingTick, 1, PlayerId, AirshipId, SurfaceId));
            Assert.That(
                GetAirship(engine).Mode,
                Is.EqualTo(AirshipFlightMode.Stabilizing),
                GetAirship(engine).LastLandingRequestResult.ToString());

            while (GetAirship(engine).Mode == AirshipFlightMode.Stabilizing)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            }

            AdvanceWith(engine, AirshipCommandCodec.PilotEnd(
                engine.State.Tick.Next(), 0, PlayerId, AirshipId));
            AssertPlayerFrame(engine, AirshipPlayerFrameKind.Airship, false);

            var disembarkTick = engine.State.Tick.Next();
            AdvanceWith(
                engine,
                AirshipCommandCodec.Disembark(
                    disembarkTick, 0, PlayerId, AirshipId));

            AssertPlayerFrame(engine, AirshipPlayerFrameKind.World, false);
        }

        [Test]
        public void PilotBeginSnapsEveryValidApproachToOneAuthoredCockpitPose()
        {
            var approachPoses = new[]
            {
                new AirshipPoseState(
                    new AirshipVector3Millimetres(-550, 100, 2_750),
                    11_111),
                new AirshipPoseState(
                    new AirshipVector3Millimetres(575, 1_900, 4_175),
                    49_152),
            };
            var committedHashes = new List<string>();
            var expectedPose = new AirshipPoseState(
                AirshipSimulationConstants.PilotViewBodyRootPosition,
                0);

            foreach (var approachPose in approachPoses)
            {
                var state = new AirshipSimulationStateBuilder()
                    .AddAirship(AirshipId, default)
                    .AddAboardPlayer(
                        PlayerId,
                        AirshipId,
                        approachPose,
                        false)
                    .Build();
                var engine = new SimulationEngine(
                    new SimulationState(
                        new SimulationTick(0),
                        Revision,
                        state));

                AdvanceWith(engine, AirshipCommandCodec.PilotBegin(
                    new SimulationTick(1),
                    0,
                    PlayerId,
                    AirshipId));

                Assert.That(
                    engine.State.GetAirshipSnapshot().TryGetPlayer(
                        PlayerId,
                        out var player),
                    Is.True);
                Assert.That(player.IsPiloting, Is.True);
                Assert.That(player.QuantizedPose, Is.EqualTo(expectedPose));
                committedHashes.Add(
                    LogicalStateHasher.ComputeHashHex(engine.State));
            }

            Assert.That(committedHashes[1], Is.EqualTo(committedHashes[0]));
        }

        [Test]
        public void DisembarkCommandTrustsUnityGateAndRequiresDockedSurface()
        {
            var interiorEngine = CreateAboardNonPilotEngine(
                new AirshipVector3Millimetres(0, 450, 3_450),
                ConfigureDockedSurface);
            AdvanceWith(interiorEngine, AirshipCommandCodec.Disembark(
                new SimulationTick(1), 0, PlayerId, AirshipId));
            AssertPlayerFrame(interiorEngine, AirshipPlayerFrameKind.World, false);

            var obstructedEngine = CreateAboardNonPilotEngine(
                new AirshipVector3Millimetres(4_000, 450, 1_450),
                builder =>
                {
                    ConfigureDockedSurface(builder);
                    builder.AddObstacle(
                        ObstacleId,
                        new AirshipVector3Millimetres(4_300, 0, 1_300),
                        new AirshipVector3Millimetres(4_600, 1_800, 1_600));
                });
            AdvanceWith(obstructedEngine, AirshipCommandCodec.Disembark(
                new SimulationTick(1), 0, PlayerId, AirshipId));
            AssertPlayerFrame(
                obstructedEngine,
                AirshipPlayerFrameKind.Airship,
                false);

            var voidEngine = CreateAboardNonPilotEngine(
                new AirshipVector3Millimetres(4_000, 450, 1_450));
            AdvanceWith(voidEngine, AirshipCommandCodec.Disembark(
                new SimulationTick(1), 0, PlayerId, AirshipId));
            AssertPlayerFrame(voidEngine, AirshipPlayerFrameKind.Airship, false);
        }

        [Test]
        public void ThrottlePersistsWhenReleasedAndCrossesThroughZeroIntoReverse()
        {
            var engine = CreatePilotedEngine();
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                new SimulationTick(1),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(1_000, 0, 0, 0)));

            for (var tick = 1; tick <= 80; tick++)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
                Assert.That(
                    GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                    Is.EqualTo(tick * 250));
            }

            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                AirshipPilotInputState.None));
            for (var tick = 0; tick < 10; tick++)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
                Assert.That(
                    GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                    Is.EqualTo(20_000));
            }

            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(-1_000, 0, 0, 0)));
            for (var tick = 1; tick <= 104; tick++)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
                Assert.That(
                    GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                    Is.EqualTo(Math.Max(-6_000, 20_000 - (tick * 250))));
            }

            Assert.That(
                GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(-6_000));

            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                AirshipPilotInputState.None));
            for (var tick = 0; tick < 10; tick++)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
                Assert.That(
                    GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                    Is.EqualTo(-6_000));
            }
        }

        [Test]
        public void LandingSpeedThresholdIsStrictlyBelowFourMetresPerSecond()
        {
            var engine = CreatePilotedEngine(AddWideLandingSurface);
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                new SimulationTick(1),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(1_000, 0, 0, 0)));
            for (var tick = 0; tick < 16; tick++)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            }

            Assert.That(
                GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(4_000));
            var thresholdTick = engine.State.Tick.Next();
            AdvanceWith(
                engine,
                AirshipCommandCodec.PilotInput(
                    thresholdTick,
                    0,
                    PlayerId,
                    AirshipId,
                    AirshipPilotInputState.None),
                AirshipCommandCodec.LandingRequest(
                    thresholdTick,
                    1,
                    PlayerId,
                    AirshipId,
                    SurfaceId));
            Assert.That(
                GetAirship(engine).LastLandingRequestResult,
                Is.EqualTo(AirshipLandingRequestResult.TooFast));
            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                engine.State.Tick.Next(),
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(-1_000, 0, 0, 0)));
            Assert.That(
                GetAirship(engine).ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(3_750));

            AdvanceWith(engine, AirshipCommandCodec.LandingRequest(
                engine.State.Tick.Next(), 0, PlayerId, AirshipId, SurfaceId));
            Assert.That(
                GetAirship(engine).LastLandingRequestResult,
                Is.EqualTo(AirshipLandingRequestResult.Accepted));
        }

        [Test]
        public void StabilizationConsumesExactlyOneHundredTwentyPhaseTwoTicks()
        {
            var state = CreateLandingReadyAirshipState();
            AirshipReducer.ApplyPhaseOneCommands(
                state,
                new SimulationTick(2),
                new[]
                {
                    AirshipCommandCodec.LandingRequest(
                        new SimulationTick(2), 0, PlayerId, AirshipId, SurfaceId),
                });
            Assert.That(state.TryGetAirship(AirshipId, out var before), Is.True);
            Assert.That(before.LandingTicksRemaining, Is.EqualTo(120));

            for (var elapsed = 1; elapsed < 120; elapsed++)
            {
                AirshipReducer.AdvancePhaseTwo(state);
                Assert.That(state.TryGetAirship(AirshipId, out var current), Is.True);
                Assert.That(current.Mode, Is.EqualTo(AirshipFlightMode.Stabilizing));
                Assert.That(current.LandingTicksRemaining, Is.EqualTo(120 - elapsed));
            }

            AirshipReducer.AdvancePhaseTwo(state);
            Assert.That(state.TryGetAirship(AirshipId, out var anchored), Is.True);
            Assert.That(anchored.Mode, Is.EqualTo(AirshipFlightMode.Anchored));
            Assert.That(anchored.LandingTicksRemaining, Is.Zero);
        }

        [Test]
        public void EveryStabilizationCheckpointFromOneTwentyThroughOneContinuesExactly()
        {
            var state = CreateLandingReadyAirshipState();
            AirshipReducer.ApplyPhaseOneCommands(
                state,
                new SimulationTick(2),
                new[]
                {
                    AirshipCommandCodec.LandingRequest(
                        new SimulationTick(2), 0, PlayerId, AirshipId, SurfaceId),
                });

            for (var expected = 120; expected >= 1; expected--)
            {
                Assert.That(state.TryGetAirship(AirshipId, out var current), Is.True);
                Assert.That(current.LandingTicksRemaining, Is.EqualTo(expected));
                var left = state.DeepClone();
                var right = state.DeepClone();
                AirshipReducer.AdvancePhaseTwo(left);
                AirshipReducer.AdvancePhaseTwo(right);
                Assert.That(
                    AirshipCanonicalSerializer.Serialize(left),
                    Is.EqualTo(AirshipCanonicalSerializer.Serialize(right)));
                state = left;
            }
        }

        [Test]
        public void AirshipReplayMatchesIntermediateAndFinalCheckpoints()
        {
            var initial = new SimulationState(
                new SimulationTick(0),
                Revision,
                CreatePilotedAirshipState());
            var commands = new[]
            {
                AirshipCommandCodec.Takeoff(
                    new SimulationTick(1), 0, PlayerId, AirshipId),
                AirshipCommandCodec.PilotInput(
                    new SimulationTick(1),
                    1,
                    PlayerId,
                    AirshipId,
                    new AirshipPilotInputState(800, 300, 100, 400)),
                AirshipCommandCodec.PilotInput(
                    new SimulationTick(3),
                    0,
                    PlayerId,
                    AirshipId,
                    AirshipPilotInputState.None),
            };
            var source = new SimulationEngine(initial.DeepClone());
            foreach (var command in commands)
            {
                source.EnqueueCommand(command);
            }

            var checkpointHashes = new Dictionary<ulong, string>();
            for (var tick = 1UL; tick <= 5UL; tick++)
            {
                Assert.That(source.AdvanceOneTick().Committed, Is.True);
                if (tick == 1 || tick == 3 || tick == 5)
                {
                    checkpointHashes.Add(tick, LogicalStateHasher.ComputeHashHex(source.State));
                }
            }

            var replay = new ReplayLog("airship-golden", initial, new SimulationTick(5));
            replay.Append(new ReplayEvent(0, 0, new SimulationTick(0), commands[0]));
            replay.Append(new ReplayEvent(1, 0, new SimulationTick(0), commands[1]));
            replay.Append(new ReplayEvent(2, 0, new SimulationTick(0), commands[2]));
            foreach (var pair in checkpointHashes)
            {
                replay.AddCheckpoint(new SimulationTick(pair.Key), pair.Value);
            }

            var result = ReplayRunner.Run(initial, replay);
            Assert.That(
                result.FinalHash,
                Is.EqualTo(checkpointHashes[5]));
        }

        [Test]
        public void RemovingPilotClearsOccupancyAndHeldInputAuthoritatively()
        {
            var state = CreatePilotedAirshipState();
            AirshipReducer.ApplyPhaseOneCommands(
                state,
                new SimulationTick(1),
                new[]
                {
                    AirshipCommandCodec.Takeoff(
                        new SimulationTick(1), 0, PlayerId, AirshipId),
                    AirshipCommandCodec.PilotInput(
                        new SimulationTick(1),
                        1,
                        PlayerId,
                        AirshipId,
                        new AirshipPilotInputState(1_000, 0, 0, 0)),
                });

            Assert.That(state.TryGetAirship(AirshipId, out var armed), Is.True);
            Assert.That(armed.HeldInput, Is.Not.EqualTo(AirshipPilotInputState.None));

            AirshipReducer.ApplyPhaseOneCommands(
                state,
                new SimulationTick(2),
                new[]
                {
                    AirshipCommandCodec.PlayerDestroyed(
                        new SimulationTick(2), 0, PlayerId),
                });
            Assert.That(state.TryGetAirship(AirshipId, out var cleaned), Is.True);
            Assert.That(cleaned.PilotId, Is.EqualTo(StableId.None));
            Assert.That(cleaned.HeldInput, Is.EqualTo(AirshipPilotInputState.None));
        }

        [Test]
        public void DeepSnapshotsContinueIdenticallyInAllPlayerAndFlightFrames()
        {
            var checkpoints = new List<SimulationState>
            {
                CreateGroundState(),
                CreateAboardState(),
                CreateFlyingState(),
                CreateStabilizingState(),
            };

            foreach (var checkpoint in checkpoints)
            {
                var left = new SimulationEngine(checkpoint.DeepClone());
                var right = new SimulationEngine(checkpoint.DeepClone());
                Assert.That(left.AdvanceOneTick().Committed, Is.True);
                Assert.That(right.AdvanceOneTick().Committed, Is.True);
                Assert.That(
                    LogicalStateHasher.ComputeHashHex(left.State),
                    Is.EqualTo(LogicalStateHasher.ComputeHashHex(right.State)));
            }
        }

        [Test]
        public void AirshipSnapshotIsDetachedFromPublishedAuthoritativeState()
        {
            var engine = CreatePilotedEngine();
            var before = LogicalStateHasher.ComputeHashHex(engine.State);
            var detached = engine.State.GetAirshipSnapshot();

            AirshipReducer.ApplyPhaseOneCommands(
                detached,
                new SimulationTick(1),
                new[]
                {
                    AirshipCommandCodec.Takeoff(
                        new SimulationTick(1), 0, PlayerId, AirshipId),
                });
            AirshipReducer.AdvancePhaseTwo(detached);

            Assert.That(LogicalStateHasher.ComputeHashHex(engine.State), Is.EqualTo(before));
            Assert.That(GetAirship(engine).Mode, Is.EqualTo(AirshipFlightMode.Anchored));
        }

        [Test]
        public void AirshipCanonicalOrderIsIndependentOfConstructionOrder()
        {
            var first = new AirshipSimulationStateBuilder()
                .AddAirship(new StableId(0, 2), default)
                .AddAirship(new StableId(0, 1), default)
                .AddPlayer(new StableId(0, 4), default)
                .AddPlayer(new StableId(0, 3), default)
                .AddObstacle(
                    new StableId(0, 6),
                    new AirshipVector3Millimetres(10, 20, 30),
                    new AirshipVector3Millimetres(40, 50, 60))
                .AddObstacle(
                    new StableId(0, 5),
                    new AirshipVector3Millimetres(-60, -50, -40),
                    new AirshipVector3Millimetres(-30, -20, -10))
                .Build();
            var second = new AirshipSimulationStateBuilder()
                .AddObstacle(
                    new StableId(0, 5),
                    new AirshipVector3Millimetres(-60, -50, -40),
                    new AirshipVector3Millimetres(-30, -20, -10))
                .AddObstacle(
                    new StableId(0, 6),
                    new AirshipVector3Millimetres(10, 20, 30),
                    new AirshipVector3Millimetres(40, 50, 60))
                .AddPlayer(new StableId(0, 3), default)
                .AddPlayer(new StableId(0, 4), default)
                .AddAirship(new StableId(0, 1), default)
                .AddAirship(new StableId(0, 2), default)
                .Build();

            Assert.That(
                AirshipCanonicalSerializer.Serialize(first),
                Is.EqualTo(AirshipCanonicalSerializer.Serialize(second)));
        }

        [Test]
        public void NonEmptyAirshipStateMatchesGoldenBytesAndHash()
        {
            var state = new AirshipSimulationStateBuilder()
                .AddAirship(
                    new StableId(0, 2),
                    new AirshipPoseState(
                        new AirshipVector3Millimetres(100, -200, 300),
                        65_000))
                .AddPlayer(
                    new StableId(0, 3),
                    new AirshipPoseState(
                        new AirshipVector3Millimetres(-400, 500, -600),
                        123))
                .AddObstacle(
                    new StableId(0, 4),
                    new AirshipVector3Millimetres(-10, -20, -30),
                    new AirshipVector3Millimetres(10, 20, 30))
                .AddLandingSurface(
                    new StableId(0, 5),
                    new AirshipVector3Millimetres(700, 800, 900),
                    16_384,
                    1_000,
                    2_000,
                    StableId.None)
                .Build();
            var bytes = AirshipCanonicalSerializer.Serialize(state);
            var hex = ToHex(bytes);
            var hash = Sha256Hex(bytes);

            Assert.That(
                hex,
                Is.EqualTo(
                    "050104025501531201050201000202021102010a0301c801028f0303d80402e8fb"
                    + "0303000400050006000700080009000a000b000c000d090401000200030004000e"
                    + "0502010002000f0502010002001005020100020011001200032601240501050201"
                    + "000203020003050201000200040f02010a03019f0602e80703af09027b0500041c"
                    + "011a030105020100020402070301130227033b03070301140228033c0527012506"
                    + "01050201000205020a0301f80a02c00c03880e0380800104d00f05a01f060502"
                    + "01000200"));
            Assert.That(
                hash,
                Is.EqualTo("e3d356416aa08083ee7682c6d874c6be4ab0a59f81c4b966716e0ba738f7d8d8"));
        }

        [Test]
        public void AirshipSubtreeChangesRootLogicalHash()
        {
            var empty = new SimulationState(new SimulationTick(0), Revision);
            var withAirship = new SimulationState(
                new SimulationTick(0),
                Revision,
                new AirshipSimulationStateBuilder()
                    .AddAirship(AirshipId, default)
                    .Build());

            Assert.That(
                LogicalStateHasher.ComputeHashHex(withAirship),
                Is.Not.EqualTo(LogicalStateHasher.ComputeHashHex(empty)));
        }

        [Test]
        public void DockedSurfaceIdentityChangesCanonicalHash()
        {
            var firstSurface = new StableId(0, 101);
            var secondSurface = new StableId(0, 102);
            var first = BuildWithDock(firstSurface, firstSurface, secondSurface);
            var second = BuildWithDock(secondSurface, firstSurface, secondSurface);

            Assert.That(
                Sha256Hex(AirshipCanonicalSerializer.Serialize(first)),
                Is.Not.EqualTo(Sha256Hex(AirshipCanonicalSerializer.Serialize(second))));
        }

        [Test]
        public void ThirtySixtyAndOneFortyFourFpsProduceIdenticalAirshipHash()
        {
            var hashes = new List<string>();
            foreach (var frameRate in new[] { 30U, 60U, 144U })
            {
                var engine = CreatePilotedEngine();
                engine.EnqueueCommand(AirshipCommandCodec.Takeoff(
                    new SimulationTick(1), 0, PlayerId, AirshipId));
                engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                    new SimulationTick(1),
                    1,
                    PlayerId,
                    AirshipId,
                    new AirshipPilotInputState(1_000, 500, 250, 750)));
                var clock = new FixedStepSimulationClock();
                var pacer = new ExactFramePacer(frameRate);
                for (var frame = 0; frame < frameRate * 10U; frame++)
                {
                    Assert.That(
                        clock.Advance(pacer.NextFrameDuration(), engine).Succeeded,
                        Is.True);
                }

                Assert.That(engine.State.Tick.Value, Is.EqualTo(200));
                hashes.Add(LogicalStateHasher.ComputeHashHex(engine.State));
            }

            Assert.That(hashes[1], Is.EqualTo(hashes[0]));
            Assert.That(hashes[2], Is.EqualTo(hashes[0]));
        }

        private static void AssertSinCos(ushort yaw, int sine, int cosine)
        {
            FixedTurnTrig.SinCos(yaw, out var actualSine, out var actualCosine);
            Assert.That(actualSine, Is.EqualTo(sine));
            Assert.That(actualCosine, Is.EqualTo(cosine));
        }

        private static SimulationEngine CreatePilotedEngine(
            Action<AirshipSimulationStateBuilder> configure = null)
        {
            var builder = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .AddAboardPlayer(
                    PlayerId,
                    AirshipId,
                    new AirshipPoseState(
                        AirshipSimulationConstants.PilotSeatCenter,
                        0),
                    true);
            configure?.Invoke(builder);
            return new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0),
                    Revision,
                    builder.Build()));
        }

        private static AirshipSimulationState CreatePilotedAirshipState()
        {
            return new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .AddAboardPlayer(
                    PlayerId,
                    AirshipId,
                    new AirshipPoseState(
                        AirshipSimulationConstants.PilotSeatCenter,
                        0),
                    true)
                .Build();
        }

        private static void FlyOneTick(
            SimulationEngine engine,
            AirshipPilotInputState input)
        {
            engine.EnqueueCommand(AirshipCommandCodec.PilotInput(
                new SimulationTick(1), 0, PlayerId, AirshipId, input));
            var result = engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
        }

        private static void AssertBlockedAtOrigin(AirshipEntityState airship)
        {
            AssertBlockedAtPose(airship, default, 0);
        }

        private static void AssertBlockedAtPose(
            AirshipEntityState airship,
            AirshipPoseState expectedPose,
            int expectedPitchTurnUnits)
        {
            Assert.That(airship.Pose, Is.EqualTo(expectedPose));
            Assert.That(airship.PitchTurnUnits, Is.EqualTo(expectedPitchTurnUnits));
            Assert.That(airship.ForwardSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(airship.StrafeSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(airship.VerticalSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(airship.YawRateTurnUnitsPerSecond, Is.Zero);
            Assert.That(airship.ForwardIntegrationRemainder, Is.Zero);
            Assert.That(airship.StrafeIntegrationRemainder, Is.Zero);
            Assert.That(airship.VerticalIntegrationRemainder, Is.Zero);
            Assert.That(airship.YawIntegrationRemainder, Is.Zero);
        }

        private static void AccelerateForward(
            SimulationEngine engine,
            int ticks)
        {
            Assert.That(ticks, Is.GreaterThan(0));
            var firstTick = engine.State.Tick.Next();
            AdvanceWith(engine, AirshipCommandCodec.PilotInput(
                firstTick,
                0,
                PlayerId,
                AirshipId,
                new AirshipPilotInputState(1_000, 0, 0, 0)));
            for (var index = 1; index < ticks; index++)
            {
                var result = engine.AdvanceOneTick();
                Assert.That(result.Committed, Is.True, result.FailureCause);
            }
        }

        private static AirshipSimulationState BuildLandingGeometry(
            int halfWidth,
            bool obstructed)
        {
            var builder = new AirshipSimulationStateBuilder()
                .AddLandingSurface(
                    SurfaceId,
                    new AirshipVector3Millimetres(4_890, 0, 1_432),
                    0,
                    halfWidth,
                    450,
                    StableId.None);
            if (obstructed)
            {
                builder.AddObstacle(
                    ObstacleId,
                    new AirshipVector3Millimetres(4_400, 1, 1_400),
                    new AirshipVector3Millimetres(4_450, 1_700, 1_460));
            }

            return builder.Build();
        }

        private static void AddValidLandingSurface(AirshipSimulationStateBuilder builder)
        {
            builder.AddLandingSurface(
                SurfaceId,
                new AirshipVector3Millimetres(4_890, 0, 1_432),
                0,
                400,
                450,
                StableId.None);
        }

        private static void AddWideLandingSurface(AirshipSimulationStateBuilder builder)
        {
            builder.AddLandingSurface(
                SurfaceId,
                new AirshipVector3Millimetres(4_890, 0, 1_420),
                0,
                100_000,
                450,
                StableId.None);
        }

        private static void ConfigureDockedSurface(AirshipSimulationStateBuilder builder)
        {
            builder
                .AddLandingSurface(
                    SurfaceId,
                    new AirshipVector3Millimetres(4_890, 0, 1_420),
                    0,
                    400,
                    450,
                    StableId.None)
                .DockAirship(AirshipId, SurfaceId);
        }

        private static AirshipSimulationState BuildWithDock(
            StableId docked,
            StableId firstSurface,
            StableId secondSurface)
        {
            return new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .AddLandingSurface(
                    firstSurface,
                    new AirshipVector3Millimetres(4_890, 0, 1_420),
                    0,
                    400,
                    450,
                    StableId.None)
                .AddLandingSurface(
                    secondSurface,
                    new AirshipVector3Millimetres(-4_890, 0, -1_420),
                    32_768,
                    400,
                    450,
                    StableId.None)
                .DockAirship(AirshipId, docked)
                .Build();
        }

        private static AirshipSimulationState CreateLandingReadyAirshipState()
        {
            var state = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .AddAboardPlayer(
                    PlayerId,
                    AirshipId,
                    new AirshipPoseState(
                        AirshipSimulationConstants.PilotSeatCenter,
                        0),
                    true)
                .AddLandingSurface(
                    SurfaceId,
                    new AirshipVector3Millimetres(4_890, 0, 1_420),
                    0,
                    400,
                    450,
                    StableId.None)
                .Build();
            AirshipReducer.ApplyPhaseOneCommands(
                state,
                new SimulationTick(1),
                new[]
                {
                    AirshipCommandCodec.Takeoff(
                        new SimulationTick(1), 0, PlayerId, AirshipId),
                });
            AirshipReducer.AdvancePhaseTwo(state);
            return state;
        }

        private static SimulationEngine CreateAboardNonPilotEngine(
            AirshipVector3Millimetres localPosition,
            Action<AirshipSimulationStateBuilder> configure = null)
        {
            var builder = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipId, default)
                .AddAboardPlayer(
                    PlayerId,
                    AirshipId,
                    new AirshipPoseState(localPosition, 0),
                    false);
            configure?.Invoke(builder);
            return new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0),
                    Revision,
                    builder.Build()));
        }

        private static void AdvanceWith(
            SimulationEngine engine,
            params SimulationCommand[] commands)
        {
            for (var index = 0; index < commands.Length; index++)
            {
                engine.EnqueueCommand(commands[index]);
            }

            var result = engine.AdvanceOneTick();
            Assert.That(result.Committed, Is.True, result.FailureCause);
        }

        private static void AssertPlayerFrame(
            SimulationEngine engine,
            AirshipPlayerFrameKind frame,
            bool piloting)
        {
            Assert.That(
                engine.State.GetAirshipSnapshot().TryGetPlayer(PlayerId, out var player),
                Is.True);
            Assert.That(player.FrameKind, Is.EqualTo(frame));
            Assert.That(player.IsPiloting, Is.EqualTo(piloting));
        }

        private static SimulationState CreateGroundState()
        {
            return new SimulationState(
                new SimulationTick(0),
                Revision,
                new AirshipSimulationStateBuilder()
                    .AddAirship(AirshipId, default)
                    .AddPlayer(
                        PlayerId,
                        new AirshipPoseState(
                            AirshipSimulationConstants.BoardingVolumeCenter,
                            0))
                    .Build());
        }

        private static SimulationState CreateAboardState()
        {
            return new SimulationState(
                new SimulationTick(0),
                Revision,
                new AirshipSimulationStateBuilder()
                    .AddAirship(AirshipId, default)
                    .AddAboardPlayer(
                        PlayerId,
                        AirshipId,
                        new AirshipPoseState(
                            AirshipSimulationConstants.BoardingVolumeCenter,
                            0),
                        false)
                    .Build());
        }

        private static SimulationState CreateFlyingState()
        {
            var engine = CreatePilotedEngine();
            FlyOneTick(engine, new AirshipPilotInputState(500, 0, 0, 250));
            return engine.State.DeepClone();
        }

        private static SimulationState CreateStabilizingState()
        {
            var engine = CreatePilotedEngine(AddValidLandingSurface);
            FlyOneTick(engine, new AirshipPilotInputState(1_000, 0, 0, 0));
            engine.EnqueueCommand(AirshipCommandCodec.LandingRequest(
                new SimulationTick(2), 0, PlayerId, AirshipId, SurfaceId));
            Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            Assert.That(
                GetAirship(engine).Mode,
                Is.EqualTo(AirshipFlightMode.Stabilizing),
                GetAirship(engine).LastLandingRequestResult.ToString());
            return engine.State.DeepClone();
        }

        private static AirshipEntityState GetAirship(SimulationEngine engine)
        {
            Assert.That(
                engine.State.GetAirshipSnapshot().TryGetAirship(AirshipId, out var airship),
                Is.True);
            return airship;
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }
    }
}

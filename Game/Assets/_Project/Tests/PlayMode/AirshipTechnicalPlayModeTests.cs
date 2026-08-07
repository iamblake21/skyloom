using System;
using System.Collections;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace CML.Tests.PlayMode
{
    public sealed class AirshipTechnicalPlayModeTests
    {
        private const string TechnicalScene =
            "Assets/_Project/Scenes/90_Technical.unity";

        [UnityTest]
        public IEnumerator ProductionSceneCompletesPilotFlyLandAndExitCycle()
        {
            yield return LoadTechnicalScene();
            var ready = GameObject.Find("AIR_TechnicalReady");
            Assert.That(ready, Is.Not.Null);
            var scenario = ready.GetComponent<AirshipTechnicalScenario>();
            Assert.That(scenario, Is.Not.Null);
            for (var frame = 0; frame < 120 && !scenario.IsReady; frame++)
            {
                yield return null;
            }

            Assert.That(scenario.IsReady, Is.True);
            var bridge = scenario.Bridge;
            var passenger = scenario.Passenger;
            var input = passenger.GetComponent<AirshipInputAdapter>();
            var frameComponent = bridge.GetComponent<AirshipFrame>();
            Assert.That(bridge, Is.Not.Null);
            Assert.That(passenger, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(frameComponent, Is.Not.Null);
            Assert.That(
                bridge.gameObject.name,
                Is.EqualTo("PF_Airship"));
            Assert.That(
                bridge.GetAirshipSnapshot().ObstacleCount,
                Is.GreaterThanOrEqualTo(3));

            var pilotCameraReference = Array.Find(
                bridge.GetComponentsInChildren<Transform>(true),
                value => value.name == "REF_PilotCamera");
            var pilotExitReference = Array.Find(
                bridge.GetComponentsInChildren<Transform>(true),
                value => value.name == "REF_PilotExit");
            var playerCamera = passenger.GetComponentInChildren<Camera>(true);
            Assert.That(pilotCameraReference, Is.Not.Null);
            Assert.That(pilotExitReference, Is.Not.Null);
            Assert.That(playerCamera, Is.Not.Null);
            Assert.That(
                Vector3.Distance(
                    playerCamera.transform.position,
                    pilotExitReference.position + Vector3.up * 1.6f),
                Is.LessThan(0.001f),
                "The player must begin standing at the authored pilot exit.");
            Assert.That(
                Vector3.Dot(
                    playerCamera.transform.forward,
                    bridge.Motor.VehicleRoot.forward),
                Is.GreaterThan(0.999f),
                "The cockpit camera must face the bow, not the rear propeller.");
            var cockpitConsole = Array.Find(
                bridge.GetComponentsInChildren<Transform>(true),
                value => value.name == "GEO_CockpitConsole");
            var rearPropeller = Array.Find(
                bridge.GetComponentsInChildren<Transform>(true),
                value => value.name == "ANM_PropellerRotor");
            Assert.That(cockpitConsole, Is.Not.Null);
            Assert.That(rearPropeller, Is.Not.Null);
            Assert.That(
                Vector3.Dot(
                    playerCamera.transform.forward,
                    cockpitConsole.position - playerCamera.transform.position),
                Is.GreaterThan(0f),
                "The cockpit controls must be in front of the playable camera.");
            Assert.That(
                Vector3.Dot(
                    playerCamera.transform.forward,
                    rearPropeller.position - playerCamera.transform.position),
                Is.LessThan(0f),
                "The rear propeller must remain behind the playable camera.");

            bridge.enabled = false;
            input.enabled = false;

            var beforePilot = GetPlayer(bridge);
            Assert.That(
                beforePilot.FrameKind,
                Is.EqualTo(AirshipPlayerFrameKind.Airship));
            Assert.That(beforePilot.IsPiloting, Is.False);
            Assert.That(passenger.IsAboard, Is.True);
            Assert.That(
                passenger.BodyRoot.parent,
                Is.Null,
                "A walking CharacterController must remain outside the "
                + "airship collider hierarchy.");

            var mouseLook =
                passenger.GetComponent<FirstPersonMouseLook>();
            Assert.That(mouseLook, Is.Not.Null);
            mouseLook.ApplyLookDelta(new Vector2(350f, -100f));
            Assert.That(
                input.QueueSampleForNextTick(Sample(strafe: 1_000)),
                Is.True);
            AdvanceAndRender(bridge);
            Assert.That(
                GetPlayer(bridge).QuantizedPose,
                Is.Not.EqualTo(new AirshipPoseState(
                    AirshipSimulationConstants.PilotViewBodyRootPosition,
                    0)),
                "The regression setup must approach the controls off-center.");

            Assert.That(
                input.QueueSampleForNextTick(Sample(togglePilot: true)),
                Is.True);
            Assert.That(
                GetPlayer(bridge).IsPiloting,
                Is.False,
                "Input must not mutate the core before its Tsim commit.");
            AdvanceAndRender(bridge);
            Assert.That(passenger.IsPiloting, Is.True);
            Assert.That(
                GetPlayer(bridge).QuantizedPose,
                Is.EqualTo(new AirshipPoseState(
                    AirshipSimulationConstants.PilotViewBodyRootPosition,
                    0)));
            Assert.That(
                Vector3.Distance(
                    playerCamera.transform.position,
                    pilotCameraReference.position),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Dot(
                    playerCamera.transform.forward,
                    bridge.Motor.VehicleRoot.forward),
                Is.GreaterThan(0.999f));

            var passengerLocalBeforeFlight = passenger.BodyRoot.localPosition;
            input.QueueSampleForNextTick(Sample(forward: 1_000));
            Assert.That(
                GetAirship(bridge).Mode,
                Is.EqualTo(AirshipFlightMode.Anchored));
            AdvanceAndRender(bridge);
            var firstFlying = GetAirship(bridge);
            Assert.That(firstFlying.Mode, Is.EqualTo(AirshipFlightMode.Flying));
            Assert.That(firstFlying.Pose.Position.Z, Is.EqualTo(12));
            Assert.That(
                passenger.BodyRoot.localPosition,
                Is.EqualTo(passengerLocalBeforeFlight)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            AssertPassengerRelation(passenger, frameComponent);

            input.QueueSampleForNextTick(Sample());
            AdvanceAndRender(bridge);
            var coasting = GetAirship(bridge);
            Assert.That(
                coasting.ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(firstFlying.ForwardSpeedMillimetresPerSecond),
                "Releasing W must preserve the selected throttle speed.");
            Assert.That(
                coasting.Pose.Position.Z,
                Is.GreaterThan(firstFlying.Pose.Position.Z));
            AssertPassengerRelation(passenger, frameComponent);

            input.QueueSampleForNextTick(Sample(forward: -1_000));
            AdvanceAndRender(bridge);
            var stopped = GetAirship(bridge);
            Assert.That(
                stopped.ForwardSpeedMillimetresPerSecond,
                Is.Zero,
                "S must decrement throttle until the airship stops.");
            Assert.That(stopped.Pose, Is.EqualTo(coasting.Pose));
            AssertPassengerRelation(passenger, frameComponent);

            Assert.That(
                Vector3.Distance(
                    bridge.LandingProbe.GangwayOrigin.localPosition,
                    new Vector3(4.04f, 0f, 1.42f)),
                Is.LessThan(0.0001f));
            var selfOccluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            selfOccluder.name = "AIR_TestSelfCollider";
            selfOccluder.transform.SetParent(bridge.Motor.VehicleRoot, false);
            selfOccluder.transform.localPosition =
                new Vector3(4.89f, 0.2f, 1.42f);
            selfOccluder.transform.localScale =
                new Vector3(1f, 0.1f, 1f);
            Physics.SyncTransforms();
            Assert.That(
                bridge.LandingProbe.TryFindLandingSurface(
                    GetAirship(bridge).Pose,
                    out var physicallyDiscoveredSurface),
                Is.True);
            Assert.That(
                physicallyDiscoveredSurface.SurfaceId,
                Is.EqualTo(AirshipTechnicalIds.LandingSurface));
            input.QueueSampleForNextTick(Sample(requestLanding: true));
            AdvanceAndRender(bridge);
            var stabilizing = GetAirship(bridge);
            Assert.That(
                stabilizing.Mode,
                Is.EqualTo(AirshipFlightMode.Stabilizing),
                stabilizing.LastLandingRequestResult.ToString());
            Assert.That(
                stabilizing.LastLandingRequestResult,
                Is.EqualTo(AirshipLandingRequestResult.Accepted));

            var landingTicks = 1;
            while (GetAirship(bridge).Mode == AirshipFlightMode.Stabilizing)
            {
                Assert.That(
                    passenger.IsPiloting,
                    Is.True,
                    "The pilot cannot be ejected during stabilization.");
                AdvanceAndRender(bridge);
                AssertPassengerRelation(passenger, frameComponent);
                landingTicks++;
            }

            Assert.That(
                landingTicks,
                Is.EqualTo(AirshipSimulationConstants.LandingDurationTicks));
            Assert.That(
                GetAirship(bridge).Mode,
                Is.EqualTo(AirshipFlightMode.Anchored));
            Assert.That(passenger.CharacterController.enabled, Is.False);

            input.QueueSampleForNextTick(Sample(togglePilot: true));
            AdvanceAndRender(bridge);
            Assert.That(passenger.IsPiloting, Is.False);
            Assert.That(passenger.CharacterController.enabled, Is.True);
            Assert.That(
                Vector3.Distance(
                    passenger.BodyRoot.position,
                    pilotExitReference.position),
                Is.LessThan(0.05f),
                "Leaving the controls must place the walking controller at "
                + "the authored Unity exit point in world space.");
            Assert.That(
                passenger.BodyRoot.parent,
                Is.Null,
                "After leaving the controls, Unity must own the walking "
                + "controller outside the airship transform hierarchy.");

            var boardingVolume =
                bridge.GetComponentInChildren<AirshipBoardingVolume>(true);
            Assert.That(boardingVolume, Is.Not.Null);
            Assert.That(
                boardingVolume.NotifyPassengerEntered(passenger),
                Is.True);
            input.QueueSampleForNextTick(Sample(toggleBoarding: true));
            AdvanceAndRender(bridge);
            Assert.That(passenger.IsAboard, Is.False);
            Assert.That(
                GetPlayer(bridge).FrameKind,
                Is.EqualTo(AirshipPlayerFrameKind.World));
            Assert.That(passenger.BodyRoot.parent, Is.Null);

            bridge.QueuePlayerDestroyed();
            var cleanupResult = bridge.AdvanceOneTick();
            bridge.RenderPresentation(1f);
            Assert.That(
                cleanupResult.Committed,
                Is.True,
                cleanupResult.FailureCause);
            Assert.That(
                bridge.GetAirshipSnapshot().TryGetPlayer(
                    AirshipTechnicalIds.Player,
                    out _),
                Is.False);
            var pendingBeforePresentationDestroy =
                bridge.Engine.State.PendingCommandCount;
            var playerObject = passenger.gameObject;
            UnityEngine.Object.Destroy(playerObject);
            yield return null;
            Assert.That(
                bridge.Engine.State.PendingCommandCount,
                Is.EqualTo(pendingBeforePresentationDestroy),
                "Destroying presentation must not enqueue authoritative commands.");
            var projectionTickBefore = bridge.Motor.CurrentTick;
            var projectionResult = bridge.AdvanceOneTick();
            bridge.RenderPresentation(1f);
            Assert.That(
                projectionResult.Committed,
                Is.True,
                projectionResult.FailureCause);
            Assert.That(
                bridge.Motor.CurrentTick.Value,
                Is.EqualTo(projectionTickBefore.Value + 1UL),
                "Airship projection must continue without a player presentation.");
            Assert.That(
                () => bridge.QueuePilotInput(AirshipPilotInputState.None),
                Throws.InvalidOperationException);
        }

        [UnityTest]
        public IEnumerator ProductionInteriorUsesUnityCharacterCollisionOnly()
        {
            yield return LoadTechnicalScene();
            var ready = GameObject.Find("AIR_TechnicalReady");
            Assert.That(ready, Is.Not.Null);
            var scenario = ready.GetComponent<AirshipTechnicalScenario>();
            Assert.That(scenario, Is.Not.Null);
            for (var frame = 0; frame < 120 && !scenario.IsReady; frame++)
            {
                yield return null;
            }

            Assert.That(scenario.IsReady, Is.True);
            var bridge = scenario.Bridge;
            var passenger = scenario.Passenger;
            var input = passenger.GetComponent<AirshipInputAdapter>();
            var motor = passenger.GetComponent<FirstPersonCharacterMotor>();
            var controller = passenger.CharacterController;
            var frameComponent = bridge.GetComponent<AirshipFrame>();
            bridge.enabled = false;
            input.enabled = false;

            Assert.That(motor, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(frameComponent, Is.Not.Null);
            Assert.That(controller.enabled, Is.True);
            Assert.That(
                controller.attachedRigidbody,
                Is.Null,
                "The player must not be part of the airship compound collider.");
            Assert.That(
                Array.FindAll(
                    bridge.GetComponentsInChildren<BoxCollider>(true),
                    value => value.name.StartsWith(
                        "COL_PLAYER_",
                        StringComparison.Ordinal)),
                Has.Length.EqualTo(22));

            motor.ViewYawPivot.localRotation = Quaternion.identity;
            var clearAisleStart =
                frameComponent.ToLocalPoint(passenger.BodyRoot.position);
            for (var step = 0; step < 13; step++)
            {
                motor.Move(-1_000, 0, 0.05f);
            }

            var clearAisleEnd =
                frameComponent.ToLocalPoint(passenger.BodyRoot.position);
            var floorCollider = Array.Find(
                bridge.GetComponentsInChildren<BoxCollider>(true),
                value => value.name == "COL_WALK_Floor");
            Assert.That(
                clearAisleEnd.z,
                Is.LessThan(clearAisleStart.z - 2.2f),
                "An empty aisle must not contain an invisible blocker. Last "
                + $"Unity collider: {motor.LastCollision?.name ?? "<none>"}. "
                + $"Start={clearAisleStart}, End={clearAisleEnd}, "
                + $"FloorBounds={floorCollider?.bounds.ToString() ?? "<none>"}.");

            controller.enabled = false;
            passenger.BodyRoot.position =
                frameComponent.ToWorldPoint(new Vector3(0f, 0.05f, 0f));
            controller.enabled = true;
            motor.ResetVerticalVelocity();
            Physics.SyncTransforms();
            var hitRearBulkhead = false;
            for (var step = 0; step < 30; step++)
            {
                hitRearBulkhead |= (motor.Move(-1_000, 0, 0.05f)
                    & CollisionFlags.Sides) != 0;
            }

            var rearContact =
                frameComponent.ToLocalPoint(passenger.BodyRoot.position);
            Assert.That(hitRearBulkhead, Is.True);
            Assert.That(
                rearContact.z,
                Is.InRange(-3.82f, -3.60f),
                "The controller must stop on the visible rear bulkhead.");

            for (var step = 0; step < 4; step++)
            {
                motor.Move(-707, 707, 0.05f);
            }

            Assert.That(
                frameComponent.ToLocalPoint(passenger.BodyRoot.position).x,
                Is.GreaterThan(rearContact.x + 0.2f),
                "CharacterController must slide along a wall instead of "
                + "cancelling the whole diagonal step.");
            Assert.That(
                frameComponent.ToLocalPoint(passenger.BodyRoot.position).z,
                Is.GreaterThanOrEqualTo(rearContact.z - 0.03f));
        }

        [UnityTest]
        public IEnumerator ProductionSceneVisibleObstacleRejectsSweptFlight()
        {
            yield return LoadTechnicalScene();
            var scenario = GameObject.Find("AIR_TechnicalReady")
                .GetComponent<AirshipTechnicalScenario>();
            for (var frame = 0; frame < 120 && !scenario.IsReady; frame++)
            {
                yield return null;
            }

            Assert.That(scenario.IsReady, Is.True);
            var bridge = scenario.Bridge;
            var passenger = scenario.Passenger;
            var input = passenger.GetComponent<AirshipInputAdapter>();
            var frameComponent = bridge.GetComponent<AirshipFrame>();
            bridge.enabled = false;
            input.enabled = false;
            BoardWalkAndBeginPiloting(input, bridge);

            var visibleObstacleObject = GameObject.Find("AIR_FlightTestObstacle");
            Assert.That(visibleObstacleObject, Is.Not.Null);
            Assert.That(visibleObstacleObject.GetComponent<Renderer>(), Is.Not.Null);
            Assert.That(visibleObstacleObject.GetComponent<Collider>().enabled, Is.True);
            var obstacleIdentity =
                visibleObstacleObject.GetComponent<AirshipObstacleIdentity>();
            Assert.That(obstacleIdentity, Is.Not.Null);
            Assert.That(
                obstacleIdentity.StableId,
                Is.EqualTo(AirshipTechnicalIds.FlightTestObstacle));
            Assert.That(
                bridge.GetAirshipSnapshot().TryGetObstacle(
                    AirshipTechnicalIds.FlightTestObstacle,
                    out var obstacle),
                Is.True);

            input.QueueSampleForNextTick(Sample(forward: 1_000));
            var collided = false;
            var previous = GetAirship(bridge);
            for (var tick = 0; tick < 80 && !collided; tick++)
            {
                AdvanceAndRender(bridge);
                var current = GetAirship(bridge);
                collided = current.Mode == AirshipFlightMode.Flying
                    && current.Pose == previous.Pose
                    && previous.Pose.Position.Z > 0
                    && current.ForwardSpeedMillimetresPerSecond == 0;
                previous = current;
            }

            Assert.That(collided, Is.True);
            var stopped = GetAirship(bridge);
            Assert.That(stopped.ForwardSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(stopped.StrafeSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(stopped.VerticalSpeedMillimetresPerSecond, Is.Zero);
            Assert.That(stopped.YawRateTurnUnitsPerSecond, Is.Zero);
            Assert.That(
                stopped.Pose.Position.Z
                    + AirshipCollision.CanonicalForwardHullMaximumZMillimetres,
                Is.LessThan(obstacle.Minimum.Z),
                "The committed forward hull must not penetrate the visible obstacle.");
            var visibleNose = float.NegativeInfinity;
            var renderers =
                bridge.Motor.VehicleRoot.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                visibleNose = Mathf.Max(
                    visibleNose,
                    renderers[index].bounds.max.z);
            }

            Assert.That(
                visibleNose,
                Is.LessThan(
                    visibleObstacleObject.GetComponent<Collider>().bounds.min.z),
                "No rendered part of the airship may enter the obstacle.");
            AssertPassengerRelation(passenger, frameComponent);
        }

        [UnityTest]
        public IEnumerator PersistentThrottleReverseAndMouseImpulsesFollowFlightContract()
        {
            var rig = PresentationRig.Create(60);
            var initial = GetAirship(rig.Bridge);
            Assert.That(initial.Mode, Is.EqualTo(AirshipFlightMode.Anchored));
            Assert.That(initial.ForwardSpeedMillimetresPerSecond, Is.Zero);

            rig.Input.QueueSampleForNextTick(Sample(forward: 1_000));
            AdvanceAndRender(rig.Bridge);
            var launched = GetAirship(rig.Bridge);
            Assert.That(
                launched.Mode,
                Is.EqualTo(AirshipFlightMode.Flying),
                "Giving gas must release the anchored airship without a takeoff key.");
            Assert.That(
                launched.ForwardSpeedMillimetresPerSecond,
                Is.GreaterThan(0));

            rig.Input.QueueSampleForNextTick(Sample());
            AdvanceAndRender(rig.Bridge);
            var coasting = GetAirship(rig.Bridge);
            Assert.That(
                coasting.ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(launched.ForwardSpeedMillimetresPerSecond));
            Assert.That(
                coasting.Pose.Position.Z,
                Is.GreaterThan(launched.Pose.Position.Z));

            rig.Input.QueueSampleForNextTick(Sample(forward: -1_000));
            AdvanceAndRender(rig.Bridge);
            var stationary = GetAirship(rig.Bridge);
            Assert.That(stationary.ForwardSpeedMillimetresPerSecond, Is.Zero);

            rig.Input.QueueSampleForNextTick(Sample(
                yaw: 1_000,
                pitch: -1_000));
            AdvanceAndRender(rig.Bridge);
            var stationaryAfterMouse = GetAirship(rig.Bridge);
            Assert.That(
                stationaryAfterMouse.Pose,
                Is.EqualTo(stationary.Pose),
                "Mouse orientation must have no authority while stationary.");
            Assert.That(stationaryAfterMouse.PitchTurnUnits, Is.Zero);

            rig.Input.QueueSampleForNextTick(Sample(
                forward: -1_000,
                strafe: 1_000));
            AdvanceAndRender(rig.Bridge);
            var reversing = GetAirship(rig.Bridge);
            Assert.That(
                reversing.ForwardSpeedMillimetresPerSecond,
                Is.LessThan(0),
                "Holding S past zero must select reverse.");
            Assert.That(
                reversing.Pose.Position.Z,
                Is.LessThan(stationaryAfterMouse.Pose.Position.Z));
            Assert.That(
                reversing.StrafeSpeedMillimetresPerSecond,
                Is.Zero,
                "Flight has no lateral strafe control.");

            rig.Input.QueueSampleForNextTick(Sample(forward: 1_000));
            AdvanceAndRender(rig.Bridge);
            var stoppedFromReverse = GetAirship(rig.Bridge);
            Assert.That(
                stoppedFromReverse.ForwardSpeedMillimetresPerSecond,
                Is.Zero);

            rig.Input.QueueSampleForNextTick(Sample(
                forward: 1_000,
                vertical: 1_000,
                yaw: 1_000,
                pitch: -1_000));
            AdvanceAndRender(rig.Bridge);
            var steered = GetAirship(rig.Bridge);
            Assert.That(
                steered.ForwardSpeedMillimetresPerSecond,
                Is.GreaterThan(0));
            Assert.That(
                steered.VerticalSpeedMillimetresPerSecond,
                Is.GreaterThan(0));
            Assert.That(
                steered.Pose.Position.Y,
                Is.GreaterThan(stoppedFromReverse.Pose.Position.Y));
            Assert.That(
                steered.Pose.YawTurn,
                Is.Not.EqualTo(stoppedFromReverse.Pose.YawTurn));
            Assert.That(steered.PitchTurnUnits, Is.LessThan(0));

            rig.Input.QueueSampleForNextTick(Sample());
            AdvanceAndRender(rig.Bridge);
            var impulsesConsumed = GetAirship(rig.Bridge);
            Assert.That(
                impulsesConsumed.ForwardSpeedMillimetresPerSecond,
                Is.EqualTo(steered.ForwardSpeedMillimetresPerSecond));
            Assert.That(
                impulsesConsumed.Pose.YawTurn,
                Is.EqualTo(steered.Pose.YawTurn),
                "Mouse yaw is a one-tick impulse, not a held steering axis.");
            Assert.That(
                impulsesConsumed.PitchTurnUnits,
                Is.EqualTo(steered.PitchTurnUnits),
                "Mouse pitch is a one-tick impulse, not a held steering axis.");

            rig.Destroy();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThirtySixtyAndOneFortyFourFpsKeepCoreAndPassengerIdentical()
        {
            var hashes = new List<string>();
            var poses = new List<AirshipPoseState>();
            var rigs = new List<PresentationRig>();
            foreach (var frameRate in new[] { 30, 60, 144 })
            {
                var rig = PresentationRig.Create(frameRate);
                rigs.Add(rig);
                rig.Input.QueueSampleForNextTick(Sample(
                    forward: 500,
                    vertical: 125,
                    yaw: 750,
                    pitch: -500));
                for (var frame = 0; frame < frameRate * 10; frame++)
                {
                    var result = rig.Bridge.AdvanceFrameSeconds(1d / frameRate);
                    Assert.That(result.Succeeded, Is.True);
                }

                Assert.That(rig.Bridge.Engine.State.Tick.Value, Is.EqualTo(200UL));
                var hash = LogicalStateHasher.ComputeHash(rig.Bridge.Engine.State);
                hashes.Add(BitConverter.ToString(hash));
                poses.Add(GetAirship(rig.Bridge).Pose);
                Assert.That(rig.Passenger.CharacterController.enabled, Is.False);
                AssertPassengerRelation(rig.Passenger, rig.Frame);
            }

            Assert.That(hashes[1], Is.EqualTo(hashes[0]));
            Assert.That(hashes[2], Is.EqualTo(hashes[0]));
            Assert.That(poses[1], Is.EqualTo(poses[0]));
            Assert.That(poses[2], Is.EqualTo(poses[0]));

            for (var index = 0; index < rigs.Count; index++)
            {
                rigs[index].Destroy();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator LandingDiscoveryUsesCanonicalPoseAtEveryPresentationRate()
        {
            var hashes = new List<string>();
            var renderedPositions = new List<Vector3>();
            var selectedSurfaceIds = new List<StableId>();
            foreach (var frameRate in new[] { 30, 60, 144 })
            {
                var rig = LandingPacingRig.Create(frameRate);
                rig.Input.QueueSampleForNextTick(Sample(forward: 1_000));
                AdvanceFramesUntilTick(rig.Bridge, frameRate, 1UL);
                Assert.That(GetAirship(rig.Bridge).Pose.Position.Z, Is.EqualTo(12));
                renderedPositions.Add(rig.VehicleRoot.position);

                Assert.That(
                    rig.Bridge.QueueLandingFromProbe(),
                    Is.True,
                    "Landing discovery must use the committed pose, not the "
                    + "frame-rate-dependent interpolated Transform.");
                var accepted =
                    rig.Bridge.Engine.State.GetAcceptedCommandsCanonical();
                Assert.That(accepted, Has.Count.EqualTo(1));
                Assert.That(
                    accepted[0].Kind,
                    Is.EqualTo(AirshipCommandKinds.LandingRequest));
                selectedSurfaceIds.Add(
                    AirshipCommandCodec.DecodeLandingSurfaceId(accepted[0]));
                rig.Input.QueueSampleForNextTick(Sample());
                AdvanceFramesUntilTick(rig.Bridge, frameRate, 2UL);
                Assert.That(
                    GetAirship(rig.Bridge).LastLandingRequestResult,
                    Is.EqualTo(AirshipLandingRequestResult.Accepted));
                hashes.Add(BitConverter.ToString(
                    LogicalStateHasher.ComputeHash(rig.Bridge.Engine.State)));

                rig.Destroy();
                yield return null;
            }

            Assert.That(hashes[1], Is.EqualTo(hashes[0]));
            Assert.That(hashes[2], Is.EqualTo(hashes[0]));
            Assert.That(
                selectedSurfaceIds,
                Is.All.EqualTo(AirshipTechnicalIds.LandingSurface));
            Assert.That(
                Vector3.Distance(renderedPositions[0], renderedPositions[1]),
                Is.GreaterThan(0.001f),
                "The regression requires genuinely different presentation poses.");
        }

        private static IEnumerator LoadTechnicalScene()
        {
            var operation = SceneManager.LoadSceneAsync(
                TechnicalScene,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            while (!operation.isDone)
            {
                yield return null;
            }

            yield return null;
        }

        private static void Move(
            AirshipInputAdapter input,
            AirshipSimulationBridge bridge,
            int forward,
            int strafe,
            int ticks)
        {
            input.QueueSampleForNextTick(Sample(
                forward: forward,
                strafe: strafe));
            for (var tick = 0; tick < ticks; tick++)
            {
                AdvanceAndRender(bridge);
            }
        }

        private static void BoardWalkAndBeginPiloting(
            AirshipInputAdapter input,
            AirshipSimulationBridge bridge)
        {
            if (GetPlayer(bridge).FrameKind == AirshipPlayerFrameKind.World)
            {
                input.QueueSampleForNextTick(Sample(toggleBoarding: true));
                AdvanceAndRender(bridge);
                Move(input, bridge, 0, -1_000, 8);
                Move(input, bridge, 1_000, 0, 10);
                Move(input, bridge, 0, -1_000, 6);
            }

            input.QueueSampleForNextTick(Sample(togglePilot: true));
            AdvanceAndRender(bridge);
            Assert.That(GetPlayer(bridge).IsPiloting, Is.True);
        }

        private static void AdvanceAndRender(
            AirshipSimulationBridge bridge,
            int ticks = 1)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                var result = bridge.AdvanceOneTick();
                Assert.That(result.Committed, Is.True, result.FailureCause);
                bridge.RenderPresentation(1f);
            }
        }

        private static void AdvanceFramesUntilTick(
            AirshipSimulationBridge bridge,
            int frameRate,
            ulong targetTick)
        {
            var guard = 0;
            while (bridge.Engine.State.Tick.Value < targetTick)
            {
                var result = bridge.AdvanceFrameSeconds(1d / frameRate);
                Assert.That(result.Succeeded, Is.True);
                guard++;
                Assert.That(guard, Is.LessThan(frameRate + 2));
            }

            Assert.That(bridge.Engine.State.Tick.Value, Is.EqualTo(targetTick));
        }

        private static AirshipEntityState GetAirship(
            AirshipSimulationBridge bridge)
        {
            Assert.That(
                bridge.GetAirshipSnapshot().TryGetAirship(
                    bridge.AirshipId,
                    out var airship),
                Is.True);
            return airship;
        }

        private static AirshipPlayerState GetPlayer(
            AirshipSimulationBridge bridge)
        {
            Assert.That(
                bridge.GetAirshipSnapshot().TryGetPlayer(
                    bridge.PlayerId,
                    out var player),
                Is.True);
            return player;
        }

        private static void AssertPassengerRelation(
            AirshipRelativePassenger passenger,
            AirshipFrame frame)
        {
            var expectedWorld = frame.PassengerSpace.TransformPoint(
                passenger.BodyRoot.localPosition);
            Assert.That(
                Vector3.Distance(expectedWorld, passenger.BodyRoot.position),
                Is.LessThan(0.0001f));
        }

        private static AirshipControlSample Sample(
            int forward = 0,
            int strafe = 0,
            int vertical = 0,
            int yaw = 0,
            int pitch = 0,
            bool requestLanding = false,
            bool togglePilot = false,
            bool toggleBoarding = false)
        {
            return new AirshipControlSample(
                forward,
                strafe,
                vertical,
                yaw,
                requestLanding,
                togglePilot,
                toggleBoarding,
                pitch);
        }

        private sealed class PresentationRig
        {
            private readonly GameObject _ship;
            private readonly GameObject _player;

            private PresentationRig(
                GameObject ship,
                GameObject player,
                AirshipSimulationBridge bridge,
                AirshipFrame frame,
                AirshipRelativePassenger passenger,
                AirshipInputAdapter input)
            {
                _ship = ship;
                _player = player;
                Bridge = bridge;
                Frame = frame;
                Passenger = passenger;
                Input = input;
            }

            public AirshipSimulationBridge Bridge { get; }

            public AirshipFrame Frame { get; }

            public AirshipRelativePassenger Passenger { get; }

            public AirshipInputAdapter Input { get; }

            public static PresentationRig Create(int frameRate)
            {
                var ship = new GameObject($"AIR_FrameRate_{frameRate}");
                var motor = ship.AddComponent<AirshipMotor>();
                motor.Configure(ship.transform);
                var passengerSpace = new GameObject("SYS_PassengerSpace").transform;
                passengerSpace.SetParent(ship.transform, false);
                var frame = ship.AddComponent<AirshipFrame>();
                frame.Configure(passengerSpace, motor);
                var bridge = ship.AddComponent<AirshipSimulationBridge>();

                var player = new GameObject($"AIR_Player_{frameRate}");
                var controller = player.AddComponent<CharacterController>();
                var passenger = player.AddComponent<AirshipRelativePassenger>();
                var input = player.AddComponent<AirshipInputAdapter>();
                passenger.Configure(player.transform, controller, bridge);
                bridge.Configure(motor, frame, passenger, null, false);
                input.Configure(bridge, null);

                var state = new AirshipSimulationStateBuilder()
                    .AddAirship(AirshipTechnicalIds.Airship, default)
                    .AddAboardPlayer(
                        AirshipTechnicalIds.Player,
                        AirshipTechnicalIds.Airship,
                        new AirshipPoseState(
                            AirshipSimulationConstants.PilotSeatCenter,
                            0),
                        true)
                    .Build();
                bridge.Initialize(
                    new SimulationState(
                        new SimulationTick(0),
                        new CatalogRevision(
                            CatalogSchema.BootstrapContentRevision),
                        state),
                    AirshipTechnicalIds.Airship,
                    AirshipTechnicalIds.Player);
                return new PresentationRig(
                    ship,
                    player,
                    bridge,
                    frame,
                    passenger,
                    input);
            }

            public void Destroy()
            {
                UnityEngine.Object.Destroy(_player);
                UnityEngine.Object.Destroy(_ship);
            }
        }

        private sealed class LandingPacingRig
        {
            private readonly GameObject _ship;
            private readonly GameObject _player;
            private readonly GameObject _platform;

            private LandingPacingRig(
                GameObject ship,
                GameObject player,
                GameObject platform,
                AirshipSimulationBridge bridge,
                AirshipInputAdapter input)
            {
                _ship = ship;
                _player = player;
                _platform = platform;
                Bridge = bridge;
                Input = input;
            }

            public AirshipSimulationBridge Bridge { get; }

            public AirshipInputAdapter Input { get; }

            public Transform VehicleRoot => Bridge.Motor.VehicleRoot;

            public static LandingPacingRig Create(int frameRate)
            {
                var ship = new GameObject($"AIR_LandingPacing_{frameRate}");
                var motor = ship.AddComponent<AirshipMotor>();
                motor.Configure(ship.transform);
                var passengerSpace = new GameObject("SYS_PassengerSpace").transform;
                passengerSpace.SetParent(ship.transform, false);
                var frame = ship.AddComponent<AirshipFrame>();
                frame.Configure(passengerSpace, motor);
                var probeOrigin =
                    new GameObject("SYS_DisembarkProbeOrigin").transform;
                probeOrigin.SetParent(ship.transform, false);
                probeOrigin.localPosition = new Vector3(4.04f, 0f, 1.42f);
                probeOrigin.localRotation =
                    Quaternion.LookRotation(Vector3.right, Vector3.up);
                var probe = ship.AddComponent<AirshipLandingSurfaceProbe>();
                probe.Configure(
                    ship.transform,
                    probeOrigin,
                    ~0,
                    minimumReach: 0.4f,
                    maximumReach: 0.4f);
                var bridge = ship.AddComponent<AirshipSimulationBridge>();

                var player = new GameObject($"AIR_LandingPlayer_{frameRate}");
                var controller = player.AddComponent<CharacterController>();
                var passenger = player.AddComponent<AirshipRelativePassenger>();
                var input = player.AddComponent<AirshipInputAdapter>();
                passenger.Configure(player.transform, controller, bridge);
                bridge.Configure(motor, frame, passenger, probe, false);
                input.Configure(bridge, null);

                var platform =
                    GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = $"AIR_LandingSurface_{frameRate}";
                platform.transform.SetPositionAndRotation(
                    new Vector3(104.891f, -0.05f, 1.432f),
                    Quaternion.identity);
                platform.transform.localScale =
                    new Vector3(0.902f, 0.1f, 0.802f);
                var obstacleIdentity =
                    platform.AddComponent<AirshipObstacleIdentity>();
                obstacleIdentity.Configure(
                    AirshipTechnicalIds.PlatformObstacle);
                var surfaceIdentity =
                    platform.AddComponent<AirshipLandingSurfaceIdentity>();
                surfaceIdentity.Configure(
                    AirshipTechnicalIds.LandingSurface,
                    AirshipTechnicalIds.PlatformObstacle);
                Physics.SyncTransforms();
                var obstacle = obstacleIdentity.BuildLogicalState();
                var surface = surfaceIdentity.BuildLogicalState();
                var initialPose = new AirshipPoseState(
                    new AirshipVector3Millimetres(100_000, 0, 0),
                    0);
                var state = new AirshipSimulationStateBuilder()
                    .AddAirship(AirshipTechnicalIds.Airship, initialPose)
                    .AddAboardPlayer(
                        AirshipTechnicalIds.Player,
                        AirshipTechnicalIds.Airship,
                        new AirshipPoseState(
                            AirshipSimulationConstants.PilotSeatCenter,
                            0),
                        true)
                    .AddObstacle(
                        obstacle.Id,
                        obstacle.Minimum,
                        obstacle.Maximum)
                    .AddLandingSurface(
                        surface.Id,
                        surface.Center,
                        surface.YawTurn,
                        surface.HalfWidthMillimetres,
                        surface.HalfDepthMillimetres,
                        surface.SupportingObstacleId)
                    .DockAirship(
                        AirshipTechnicalIds.Airship,
                        AirshipTechnicalIds.LandingSurface)
                    .Build();
                bridge.Initialize(
                    new SimulationState(
                        new SimulationTick(0),
                        new CatalogRevision(
                            CatalogSchema.BootstrapContentRevision),
                        state),
                    AirshipTechnicalIds.Airship,
                    AirshipTechnicalIds.Player);
                return new LandingPacingRig(
                    ship,
                    player,
                    platform,
                    bridge,
                    input);
            }

            public void Destroy()
            {
                _ship.SetActive(false);
                _player.SetActive(false);
                _platform.SetActive(false);
                UnityEngine.Object.Destroy(_player);
                UnityEngine.Object.Destroy(_platform);
                UnityEngine.Object.Destroy(_ship);
            }
        }
    }
}

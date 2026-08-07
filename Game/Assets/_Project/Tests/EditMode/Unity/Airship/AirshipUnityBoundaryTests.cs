using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using CML.Unity.Bootstrap;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace CML.Tests.Unity.Airship
{
    public sealed class AirshipUnityBoundaryTests
    {
        private const string PrefabPath =
            "Assets/_Project/Art/Vehicles/Airship/Prefabs/PF_Airship.prefab";
        private const string BootstrapScenePath =
            "Assets/_Project/Scenes/00_Bootstrap.unity";
        private const string TechnicalScenePath =
            "Assets/_Project/Scenes/90_Technical.unity";
        private const string LandingSurfaceScriptGuid =
            "d4f06c9b3a884c36a8c1f92b7e5d0137";
        private const string ObstacleScriptGuid =
            "56208f17f9efd6f4ca52fd951abb938f";
        private const string SceneRevisionScriptGuid =
            "e61d7f34a56b4c27b9d0138fe0a264bc";

        [Test]
        public void MouseLookRotatesViewPivotsWithoutMovingAuthoritativeRoot()
        {
            var root = new GameObject("player");
            try
            {
                var yaw = new GameObject("yaw").transform;
                yaw.SetParent(root.transform, false);
                var pitch = new GameObject("pitch").transform;
                pitch.SetParent(yaw, false);
                var mouseLook = root.AddComponent<FirstPersonMouseLook>();
                mouseLook.Configure(yaw, pitch);
                var ordinaryDelta = new Vector2(20f, -15f);
                typeof(FirstPersonMouseLook).GetMethod(
                        "SuppressNextMouseDelta",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(mouseLook, null);
                Assert.That(
                    mouseLook.FilterMouseDelta(ordinaryDelta),
                    Is.EqualTo(Vector2.zero));
                Assert.That(
                    mouseLook.FilterMouseDelta(ordinaryDelta),
                    Is.EqualTo(ordinaryDelta));
                Assert.That(
                    mouseLook.FilterMouseDelta(new Vector2(900f, -700f)),
                    Is.EqualTo(Vector2.zero));

                var originalPosition = root.transform.position;
                var originalRotation = root.transform.rotation;
                mouseLook.ApplyLookDelta(new Vector2(100f, 1_000f));

                Assert.That(
                    Mathf.DeltaAngle(yaw.localEulerAngles.y, 12f),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    Mathf.DeltaAngle(pitch.localEulerAngles.x, -85f),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(root.transform.position, Is.EqualTo(originalPosition));
                Assert.That(root.transform.rotation, Is.EqualTo(originalRotation));

                mouseLook.ApplyLookDelta(new Vector2(0f, -2_000f));
                Assert.That(
                    Mathf.DeltaAngle(pitch.localEulerAngles.x, 85f),
                    Is.EqualTo(0f).Within(0.001f));

                mouseLook.SetPiloting(true);
                Assert.That(
                    Mathf.DeltaAngle(yaw.localEulerAngles.y, 0f),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    Mathf.DeltaAngle(pitch.localEulerAngles.x, 0f),
                    Is.EqualTo(0f).Within(0.001f));
                mouseLook.ApplyLookDelta(new Vector2(500f, 500f));
                Assert.That(
                    Mathf.DeltaAngle(yaw.localEulerAngles.y, 0f),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    Mathf.DeltaAngle(pitch.localEulerAngles.x, 0f),
                    Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MotorIsProjectionOnlyAndInterpolatesCommittedYawAndPitch()
        {
            var root = new GameObject("view");
            try
            {
                var motor = root.AddComponent<AirshipMotor>();
                motor.Configure(root.transform);
                motor.CommitPose(new SimulationTick(0), default);
                motor.CommitPose(
                    new SimulationTick(1),
                    new AirshipPoseState(
                        new AirshipVector3Millimetres(2_000, 4_000, 6_000),
                        16_384),
                    AirshipSimulationConstants.MaximumPitchTurnUnits);
                motor.Render(0.5f);

                Assert.That(root.transform.position, Is.EqualTo(new Vector3(1, 2, 3))
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    Mathf.DeltaAngle(root.transform.eulerAngles.y, 45f),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    Mathf.DeltaAngle(root.transform.eulerAngles.x, 7.5f),
                    Is.EqualTo(0f).Within(0.01f));
                Assert.That(
                    typeof(AirshipMotor).GetMethod(
                        "Update",
                        BindingFlags.Instance | BindingFlags.NonPublic),
                    Is.Null);
                Assert.That(typeof(AirshipMotor).GetMethod("TryTakeOff"), Is.Null);
                Assert.That(typeof(AirshipMotor).GetMethod("SetPilotInput"), Is.Null);
                Assert.That(typeof(AirshipMotor).GetMethod("AdvanceOneSimulationTick"), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BridgeQueuesWithoutMutatingUntilGlobalTickCommits()
        {
            using (var rig = BoundaryRig.Create())
            {
                rig.Bridge.QueueTakeoff();
                Assert.That(
                    GetAirship(rig.Bridge).Mode,
                    Is.EqualTo(AirshipFlightMode.Anchored));
                Assert.That(rig.Motor.CurrentTick.Value, Is.Zero);

                var result = rig.Bridge.AdvanceOneTick();

                Assert.That(result.Committed, Is.True, result.FailureCause);
                Assert.That(
                    GetAirship(rig.Bridge).Mode,
                    Is.EqualTo(AirshipFlightMode.Flying));
                Assert.That(rig.Motor.CurrentTick.Value, Is.EqualTo(1));
            }
        }

        [Test]
        public void PrefabHasNoStandaloneOrMissingLegacyScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponents<AirshipMotor>(), Has.Length.EqualTo(1));
            Assert.That(
                prefab.GetComponents<AirshipSimulationBridge>(),
                Has.Length.EqualTo(1));
            Assert.That(
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab),
                Is.Zero);
            var yaml = File.ReadAllText(Path.GetFullPath(PrefabPath));
            Assert.That(yaml, Does.Not.Contain("runStandaloneSimulation"));
            Assert.That(yaml, Does.Not.Contain("AirshipPhysicsCollisionResolver"));
        }

        [Test]
        public void GeneratedScenesHaveCurrentRevisionAndExternalMonoScripts()
        {
            AssertGeneratedSceneRevision(
                BootstrapScenePath,
                GeneratedSceneRevision.BootstrapSceneId,
                GeneratedSceneRevision.CurrentBootstrapRevision);
            AssertGeneratedSceneRevision(
                TechnicalScenePath,
                GeneratedSceneRevision.TechnicalSceneId,
                GeneratedSceneRevision.CurrentTechnicalRevision);

            var bootstrapYaml =
                File.ReadAllText(Path.GetFullPath(BootstrapScenePath));
            var technicalYaml =
                File.ReadAllText(Path.GetFullPath(TechnicalScenePath));
            Assert.That(bootstrapYaml, Does.Not.Contain("--- !u!115"));
            Assert.That(technicalYaml, Does.Not.Contain("--- !u!115"));
            Assert.That(
                technicalYaml,
                Does.Contain(
                    "m_Script: {fileID: 11500000, guid: "
                    + LandingSurfaceScriptGuid
                    + ", type: 3}"));
            Assert.That(
                technicalYaml,
                Does.Contain(
                    "m_Script: {fileID: 11500000, guid: "
                    + ObstacleScriptGuid
                    + ", type: 3}"));
            Assert.That(
                bootstrapYaml,
                Does.Contain(
                    "m_Script: {fileID: 11500000, guid: "
                    + SceneRevisionScriptGuid
                    + ", type: 3}"));
            Assert.That(
                technicalYaml,
                Does.Contain(
                    "m_Script: {fileID: 11500000, guid: "
                    + SceneRevisionScriptGuid
                    + ", type: 3}"));
        }

        [Test]
        public void CanonicalCollisionEnvelopeContainsEveryProductionRenderer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.That(instance, Is.Not.Null);
            try
            {
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                var minimum = new Vector3(
                    float.PositiveInfinity,
                    float.PositiveInfinity,
                    float.PositiveInfinity);
                var maximum = new Vector3(
                    float.NegativeInfinity,
                    float.NegativeInfinity,
                    float.NegativeInfinity);
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                for (var rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    var bounds = renderers[rendererIndex].bounds;
                    var rendererMinimum = new Vector3(
                        float.PositiveInfinity,
                        float.PositiveInfinity,
                        float.PositiveInfinity);
                    var rendererMaximum = new Vector3(
                        float.NegativeInfinity,
                        float.NegativeInfinity,
                        float.NegativeInfinity);
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var world = new Vector3(
                            (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                            (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                            (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                        var local = instance.transform.InverseTransformPoint(world);
                        rendererMinimum = Vector3.Min(rendererMinimum, local);
                        rendererMaximum = Vector3.Max(rendererMaximum, local);
                        minimum = Vector3.Min(minimum, local);
                        maximum = Vector3.Max(maximum, local);
                    }

                    var quantizedMinimum = new AirshipVector3Millimetres(
                        (long)Math.Floor(rendererMinimum.x * 1000d),
                        (long)Math.Floor(rendererMinimum.y * 1000d),
                        (long)Math.Floor(rendererMinimum.z * 1000d));
                    var quantizedMaximum = new AirshipVector3Millimetres(
                        (long)Math.Ceiling(rendererMaximum.x * 1000d),
                        (long)Math.Ceiling(rendererMaximum.y * 1000d),
                        (long)Math.Ceiling(rendererMaximum.z * 1000d));
                    Assert.That(
                        AirshipCollision.TryFindContainingCanonicalHull(
                            quantizedMinimum,
                            quantizedMaximum,
                            out _),
                        Is.True,
                        $"{HierarchyPath(renderers[rendererIndex].transform, instance.transform)} "
                        + $"bounds min={rendererMinimum} max={rendererMaximum} "
                        + "is not contained by one canonical compound hull.");
                }

                var canonicalMinimum =
                    AirshipCollision.CanonicalVisualEnvelopeMinimum;
                var canonicalMaximum =
                    AirshipCollision.CanonicalVisualEnvelopeMaximum;
                const float tolerance = 0.0005f;
                Assert.That(
                    minimum.x,
                    Is.GreaterThanOrEqualTo(
                        (canonicalMinimum.X / 1000f) - tolerance));
                Assert.That(
                    minimum.y,
                    Is.GreaterThanOrEqualTo(
                        (canonicalMinimum.Y / 1000f) - tolerance));
                Assert.That(
                    minimum.z,
                    Is.GreaterThanOrEqualTo(
                        (canonicalMinimum.Z / 1000f) - tolerance));
                Assert.That(
                    maximum.x,
                    Is.LessThanOrEqualTo(
                        (canonicalMaximum.X / 1000f) + tolerance));
                Assert.That(
                    maximum.y,
                    Is.LessThanOrEqualTo(
                        (canonicalMaximum.Y / 1000f) + tolerance));
                Assert.That(
                    maximum.z,
                    Is.LessThanOrEqualTo(
                        (canonicalMaximum.Z / 1000f) + tolerance));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TwoAutomatedAirshipRebuildsProduceIdenticalPrefabHash()
        {
            var setupType = Type.GetType(
                "CML.Editor.Art.AirshipAssetSetup, CML.Editor",
                throwOnError: true);
            var run = setupType.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(run, Is.Not.Null);

            run.Invoke(null, null);
            AssetDatabase.SaveAssets();
            var first = HashPrefab();
            run.Invoke(null, null);
            AssetDatabase.SaveAssets();
            var second = HashPrefab();

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void BindingOverrideChangesEffectiveControlWithoutChangingCoreState()
        {
            using (var rig = BoundaryRig.Create())
            {
                var before = LogicalStateHasher.ComputeHash(
                    rig.Bridge.Engine.State);
                Assert.That(
                    rig.Input.ApplyBindingOverride(
                        AirshipInputAdapter.TogglePilotAction,
                        "Primary",
                        "<Keyboard>/b"),
                    Is.True);
                var action = rig.Input.Controls.FindAction(
                    AirshipInputAdapter.TogglePilotAction,
                    true);
                string effectivePath = null;
                for (var index = 0; index < action.bindings.Count; index++)
                {
                    if (string.Equals(
                        action.bindings[index].name,
                        "Primary",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        effectivePath = action.bindings[index].effectivePath;
                        break;
                    }
                }

                Assert.That(action.enabled, Is.True);
                Assert.That(effectivePath, Is.EqualTo("<Keyboard>/b"));
                Assert.That(
                    LogicalStateHasher.ComputeHash(rig.Bridge.Engine.State),
                    Is.EqualTo(before));
            }
        }

        [Test]
        public void DefaultBindingsMatchThePlayerFacingFlightContract()
        {
            using (var rig = BoundaryRig.Create())
            {
                Assert.That(
                    EffectivePath(
                        rig.Input.Controls.FindAction(
                            AirshipInputAdapter.TogglePilotAction,
                            true),
                        "Primary"),
                    Is.EqualTo("<Keyboard>/e"));
                Assert.That(
                    EffectivePath(
                        rig.Input.Controls.FindAction(
                            AirshipInputAdapter.VerticalAction,
                            true),
                        "Negative"),
                    Is.EqualTo("<Keyboard>/leftShift"));
                Assert.That(
                    EffectivePath(
                        rig.Input.Controls.FindAction(
                            AirshipInputAdapter.VerticalAction,
                            true),
                        "Positive"),
                    Is.EqualTo("<Keyboard>/space"));
                Assert.That(
                    EffectivePath(
                        rig.Input.Controls.FindAction(
                            AirshipInputAdapter.LookAction,
                            true),
                        "Primary"),
                    Is.EqualTo("<Mouse>/delta"));
                Assert.That(
                    rig.Input.Controls.FindAction("Takeoff", false),
                    Is.Null);
                Assert.That(
                    rig.Input.Controls.FindAction("Yaw", false),
                    Is.Null);
            }
        }

        [Test]
        public void UnityCharacterMotorUsesCameraHeadingAndNormalizesDiagonalInput()
        {
            var player = new GameObject("unity-character-motor");
            try
            {
                var controller = player.AddComponent<CharacterController>();
                var passenger = player.AddComponent<AirshipRelativePassenger>();
                var yawPivot = new GameObject("view-yaw").transform;
                yawPivot.SetParent(player.transform, false);
                yawPivot.localRotation = Quaternion.Euler(0f, 90f, 0f);
                var motor = player.AddComponent<FirstPersonCharacterMotor>();
                passenger.Configure(player.transform, controller, null);
                motor.Configure(controller, yawPivot, passenger);

                motor.Move(1_000, 0, 0.05f);
                var cameraForwardPosition = player.transform.position;
                Assert.That(cameraForwardPosition.x, Is.EqualTo(0.2f).Within(0.01f));
                Assert.That(cameraForwardPosition.z, Is.EqualTo(0f).Within(0.01f));
                Assert.That(controller.attachedRigidbody, Is.Null);

                player.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                motor.ResetVerticalVelocity();
                motor.Move(707, 707, 0.05f);
                var planarDistance = new Vector2(
                    player.transform.position.x,
                    player.transform.position.z).magnitude;
                Assert.That(planarDistance, Is.LessThanOrEqualTo(0.201f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void PilotMouseImpulsesMergeOnceAndDoNotRepeatNextTick()
        {
            using (var rig = BoundaryRig.Create())
            {
                rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                    1_000,
                    0,
                    0,
                    300,
                    false,
                    false,
                    false,
                    200));
                rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                    1_000,
                    0,
                    0,
                    400,
                    false,
                    false,
                    false,
                    -50));

                Assert.That(rig.Bridge.AdvanceOneTick().Committed, Is.True);
                var first = GetAirship(rig.Bridge);
                Assert.That(first.Mode, Is.EqualTo(AirshipFlightMode.Flying));
                Assert.That(first.PitchTurnUnits, Is.EqualTo(29));
                Assert.That(first.YawRateTurnUnitsPerSecond, Is.GreaterThan(0));

                Assert.That(rig.Bridge.AdvanceOneTick().Committed, Is.True);
                var second = GetAirship(rig.Bridge);
                Assert.That(second.PitchTurnUnits, Is.EqualTo(first.PitchTurnUnits));
                Assert.That(second.YawRateTurnUnitsPerSecond, Is.Zero);
            }
        }

        [Test]
        public void LaterEdgeForSameUpcomingTickIsLatchedUntilCommit()
        {
            using (var rig = BoundaryRig.Create())
            {
                Assert.That(
                    rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                        0,
                        0,
                        0,
                        0,
                        false,
                        false)),
                    Is.True);
                Assert.That(
                    rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                        0,
                        0,
                        0,
                        0,
                        false,
                        true)),
                    Is.True);

                Assert.That(GetPlayer(rig.Bridge).IsPiloting, Is.True);
                var result = rig.Bridge.AdvanceOneTick();

                Assert.That(result.Committed, Is.True, result.FailureCause);
                Assert.That(GetPlayer(rig.Bridge).IsPiloting, Is.False);
            }
        }

        [Test]
        public void PilotCommitImmediatelyCentersBodyAndMouseLook()
        {
            using (var rig = BoundaryRig.Create())
            {
                rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                    0,
                    0,
                    0,
                    0,
                    false,
                    true));
                Assert.That(rig.Bridge.AdvanceOneTick().Committed, Is.True);
                Assert.That(GetPlayer(rig.Bridge).IsPiloting, Is.False);

                rig.MouseLook.ApplyLookDelta(new Vector2(500f, -200f));
                Assert.That(
                    rig.MouseLook.YawPivot.localRotation,
                    Is.Not.EqualTo(Quaternion.identity));
                Assert.That(
                    rig.MouseLook.PitchPivot.localRotation,
                    Is.Not.EqualTo(Quaternion.identity));

                rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                    0,
                    0,
                    0,
                    0,
                    false,
                    true));
                Assert.That(rig.Bridge.AdvanceOneTick().Committed, Is.True);

                var player = GetPlayer(rig.Bridge);
                var passenger =
                    rig.Input.GetComponent<AirshipRelativePassenger>();
                Assert.That(player.IsPiloting, Is.True);
                Assert.That(
                    player.QuantizedPose,
                    Is.EqualTo(new AirshipPoseState(
                        AirshipSimulationConstants.PilotViewBodyRootPosition,
                        0)));
                Assert.That(
                    passenger.BodyRoot.localPosition,
                    Is.EqualTo(new Vector3(0f, 0.05f, 3.5f))
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    passenger.BodyRoot.localRotation,
                    Is.EqualTo(Quaternion.identity)
                        .Using(QuaternionEqualityComparer.Instance));
                Assert.That(
                    rig.MouseLook.YawPivot.localRotation,
                    Is.EqualTo(Quaternion.identity)
                        .Using(QuaternionEqualityComparer.Instance));
                Assert.That(
                    rig.MouseLook.PitchPivot.localRotation,
                    Is.EqualTo(Quaternion.identity)
                        .Using(QuaternionEqualityComparer.Instance));
            }
        }

        [Test]
        public void DestroyedPlayerDoesNotAbortAirshipProjectionOrAcceptMoreInput()
        {
            using (var rig = BoundaryRig.Create())
            {
                rig.Bridge.QueuePlayerDestroyed();
                var result = rig.Bridge.AdvanceOneTick();

                Assert.That(result.Committed, Is.True, result.FailureCause);
                Assert.That(
                    rig.Bridge.GetAirshipSnapshot().TryGetPlayer(
                        AirshipTechnicalIds.Player,
                        out _),
                    Is.False);
                Assert.That(rig.Motor.CurrentTick.Value, Is.EqualTo(1));
                Assert.That(
                    () => rig.Bridge.QueuePilotInput(
                        AirshipPilotInputState.None),
                    Throws.InvalidOperationException);
            }
        }

        [Test]
        public void HitchUsesFreshCorePlayerStateForEveryCaughtUpTick()
        {
            using (var rig = BoundaryRig.Create())
            {
                rig.Input.QueueSampleForNextTick(new AirshipControlSample(
                    1_000,
                    0,
                    0,
                    0,
                    false,
                    true));

                var result = rig.Bridge.AdvanceFrame(
                    TimeSpan.FromMilliseconds(100));
                var player = GetPlayer(rig.Bridge);

                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.CommittedTicks, Is.EqualTo(2UL));
                Assert.That(player.IsPiloting, Is.False);
                Assert.That(
                    player.QuantizedPose.Position.Z,
                    Is.EqualTo(
                        AirshipSimulationConstants
                            .PilotExitBodyRootPosition.Z));
            }
        }

        [Test]
        public void ThrowingProjectionObserverCannotUndoCommittedTick()
        {
            using (var rig = BoundaryRig.Create())
            {
                rig.Bridge.StateProjected += _ =>
                    throw new InvalidOperationException("observer-failure");
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: observer-failure"));
                rig.Bridge.QueueTakeoff();

                var result = rig.Bridge.AdvanceOneTick();

                Assert.That(result.Committed, Is.True, result.FailureCause);
                Assert.That(rig.Bridge.Engine.State.Tick.Value, Is.EqualTo(1UL));
                Assert.That(
                    GetAirship(rig.Bridge).Mode,
                    Is.EqualTo(AirshipFlightMode.Flying));
            }
        }

        [Test]
        public void DisablingOrDestroyingPilotStationDoesNotMutateAuthoritativeState()
        {
            using (var rig = BoundaryRig.Create())
            {
                var station =
                    rig.Bridge.GetComponentInChildren<AirshipPilotStation>(true);
                Assert.That(station, Is.Not.Null);
                Assert.That(GetPlayer(rig.Bridge).IsPiloting, Is.True);
                var beforeHash =
                    LogicalStateHasher.ComputeHash(rig.Bridge.Engine.State);
                var beforePending = rig.Bridge.Engine.State.PendingCommandCount;

                station.enabled = false;

                Assert.That(
                    rig.Bridge.Engine.State.PendingCommandCount,
                    Is.EqualTo(beforePending));
                Assert.That(
                    LogicalStateHasher.ComputeHash(rig.Bridge.Engine.State),
                    Is.EqualTo(beforeHash));
                Assert.That(GetPlayer(rig.Bridge).IsPiloting, Is.True);

                UnityEngine.Object.DestroyImmediate(station.gameObject);

                Assert.That(
                    rig.Bridge.Engine.State.PendingCommandCount,
                    Is.EqualTo(beforePending));
                Assert.That(
                    LogicalStateHasher.ComputeHash(rig.Bridge.Engine.State),
                    Is.EqualTo(beforeHash));
                Assert.That(GetPlayer(rig.Bridge).IsPiloting, Is.True);
            }
        }

        [Test]
        public void ThrowingPassengerObserversCannotDisableControllerOrBlockProjection()
        {
            var ship = new GameObject("observer-ship");
            var player = new GameObject("observer-player");
            try
            {
                var motor = ship.AddComponent<AirshipMotor>();
                motor.Configure(ship.transform);
                var passengerSpace = new GameObject("passenger-space").transform;
                passengerSpace.SetParent(ship.transform, false);
                var frame = ship.AddComponent<AirshipFrame>();
                frame.Configure(passengerSpace, motor);
                var controller = player.AddComponent<CharacterController>();
                var passenger = player.AddComponent<AirshipRelativePassenger>();
                passenger.Configure(player.transform, controller, null);

                var world = BuildPlayerState(aboard: false);
                var aboard = BuildPlayerState(aboard: true);
                passenger.CommitState(new SimulationTick(0), world, frame);

                var boardedFollowerCalled = false;
                passenger.Boarded += _ =>
                    throw new InvalidOperationException("boarded-observer-failure");
                passenger.Boarded += _ => boardedFollowerCalled = true;
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex(
                        "InvalidOperationException: boarded-observer-failure"));
                passenger.CommitState(new SimulationTick(1), aboard, frame);

                Assert.That(boardedFollowerCalled, Is.True);
                Assert.That(passenger.IsAboard, Is.True);
                Assert.That(controller.enabled, Is.True);
                Assert.That(frame.PassengerCount, Is.EqualTo(1));

                var disembarkedFollowerCalled = false;
                passenger.Disembarked += _ =>
                    throw new InvalidOperationException(
                        "disembarked-observer-failure");
                passenger.Disembarked += _ =>
                    disembarkedFollowerCalled = true;
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex(
                        "InvalidOperationException: disembarked-observer-failure"));
                passenger.CommitState(new SimulationTick(2), world, frame);

                Assert.That(disembarkedFollowerCalled, Is.True);
                Assert.That(passenger.IsAboard, Is.False);
                Assert.That(controller.enabled, Is.True);
                Assert.That(frame.PassengerCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void PilotTransitionSnapsPassengerPoseWithoutInterpolation()
        {
            var ship = new GameObject("pilot-snap-ship");
            var playerObject = new GameObject("pilot-snap-player");
            try
            {
                var motor = ship.AddComponent<AirshipMotor>();
                motor.Configure(ship.transform);
                var passengerSpace =
                    new GameObject("pilot-snap-passenger-space").transform;
                passengerSpace.SetParent(ship.transform, false);
                var frame = ship.AddComponent<AirshipFrame>();
                frame.Configure(passengerSpace, motor);
                var controller =
                    playerObject.AddComponent<CharacterController>();
                var passenger =
                    playerObject.AddComponent<AirshipRelativePassenger>();
                passenger.Configure(playerObject.transform, controller, null);

                var approachPose = new AirshipPoseState(
                    new AirshipVector3Millimetres(-550, 100, 2_750),
                    11_111);
                var cockpitPose = new AirshipPoseState(
                    AirshipSimulationConstants.PilotViewBodyRootPosition,
                    0);
                var approach = BuildAboardPlayerState(
                    approachPose,
                    false);
                var piloting = BuildAboardPlayerState(
                    cockpitPose,
                    true);

                passenger.CommitState(
                    new SimulationTick(0),
                    approach,
                    frame);
                passenger.CommitState(
                    new SimulationTick(1),
                    piloting,
                    frame);
                passenger.Render(0f);

                Assert.That(
                    passenger.BodyRoot.localPosition,
                    Is.EqualTo(new Vector3(0f, 0.05f, 3.5f))
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    passenger.BodyRoot.localRotation,
                    Is.EqualTo(Quaternion.identity)
                        .Using(QuaternionEqualityComparer.Instance));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void LandingProbeRejectsCoplanarDifferentSurfaceIdentities()
        {
            var root = new GameObject("probe-root");
            var first = default(GameObject);
            var second = default(GameObject);
            try
            {
                var probe = CreateProbe(root);
                first = CreateLandingSurface(
                    "first-surface",
                    new StableId(0, 101),
                    new Vector3(4.89f, -0.05f, 1.42f),
                    new Vector3(1.4f, 0.1f, 1f));
                second = CreateLandingSurface(
                    "second-surface",
                    new StableId(0, 102),
                    new Vector3(4.89f, -0.05f, 1.42f),
                    new Vector3(1.4f, 0.1f, 1f));

                Assert.That(
                    probe.TryFindLandingSurface(default, out _),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LandingProbeFailsClosedWhenCorridorHitBufferSaturates()
        {
            var root = new GameObject("saturated-probe-root");
            var surface = default(GameObject);
            try
            {
                var probe = CreateProbe(root);
                surface = CreateLandingSurface(
                    "saturation-surface",
                    new StableId(0, 103),
                    new Vector3(4.89f, -0.05f, 1.42f),
                    new Vector3(1.4f, 0.1f, 1f));
                for (var index = 0; index < 64; index++)
                {
                    var self = new GameObject($"self-collider-{index}");
                    self.transform.SetParent(root.transform, false);
                    self.transform.localPosition = new Vector3(
                        4.5f + ((index % 4) * 0.01f),
                        1.2f,
                        1.42f + (((index / 4) % 4) * 0.01f));
                    var collider = self.AddComponent<BoxCollider>();
                    collider.size = new Vector3(0.05f, 0.05f, 0.05f);
                }

                Assert.That(
                    probe.TryFindLandingSurface(default, out _),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LandingSurfaceLogicalContractRejectsColliderCenterAndSizeDrift()
        {
            var surface = CreateLandingSurface(
                "contract-surface",
                new StableId(0, 104),
                new Vector3(4.89f, -0.05f, 1.42f),
                new Vector3(1.4f, 0.1f, 1f));
            try
            {
                var identity =
                    surface.GetComponent<AirshipLandingSurfaceIdentity>();
                var collider = surface.GetComponent<BoxCollider>();
                var logical = identity.BuildLogicalState();
                Assert.That(identity.MatchesLogicalState(logical), Is.True);
                Assert.That(logical.HalfDepthMillimetres, Is.EqualTo(700));
                Assert.That(logical.HalfWidthMillimetres, Is.EqualTo(500));

                collider.center += new Vector3(0.01f, 0f, 0f);
                Assert.That(identity.MatchesLogicalState(logical), Is.False);
                collider.center -= new Vector3(0.01f, 0f, 0f);
                collider.size += new Vector3(0.02f, 0f, 0f);
                Assert.That(identity.MatchesLogicalState(logical), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        private static AirshipPlayerState BuildPlayerState(bool aboard)
        {
            var builder = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipTechnicalIds.Airship, default);
            if (aboard)
            {
                builder.AddAboardPlayer(
                    AirshipTechnicalIds.Player,
                    AirshipTechnicalIds.Airship,
                    default,
                    false);
            }
            else
            {
                builder.AddPlayer(AirshipTechnicalIds.Player, default);
            }

            var state = builder.Build();
            Assert.That(
                state.TryGetPlayer(AirshipTechnicalIds.Player, out var player),
                Is.True);
            return player;
        }

        private static AirshipPlayerState BuildAboardPlayerState(
            AirshipPoseState pose,
            bool piloting)
        {
            var state = new AirshipSimulationStateBuilder()
                .AddAirship(AirshipTechnicalIds.Airship, default)
                .AddAboardPlayer(
                    AirshipTechnicalIds.Player,
                    AirshipTechnicalIds.Airship,
                    pose,
                    piloting)
                .Build();
            Assert.That(
                state.TryGetPlayer(
                    AirshipTechnicalIds.Player,
                    out var player),
                Is.True);
            return player;
        }

        private static AirshipLandingSurfaceProbe CreateProbe(GameObject root)
        {
            var origin = new GameObject("probe-origin").transform;
            origin.SetParent(root.transform, false);
            origin.localPosition = new Vector3(4.04f, 0f, 1.42f);
            origin.localRotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up);
            var probe = root.AddComponent<AirshipLandingSurfaceProbe>();
            probe.Configure(
                root.transform,
                origin,
                ~0,
                minimumReach: 0.4f,
                maximumReach: 0.4f);
            return probe;
        }

        private static GameObject CreateLandingSurface(
            string name,
            StableId id,
            Vector3 position,
            Vector3 scale)
        {
            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = name;
            surface.transform.SetPositionAndRotation(
                position,
                Quaternion.identity);
            surface.transform.localScale = scale;
            var identity =
                surface.AddComponent<AirshipLandingSurfaceIdentity>();
            identity.Configure(id, StableId.None);
            return surface;
        }

        private static AirshipEntityState GetAirship(AirshipSimulationBridge bridge)
        {
            Assert.That(
                bridge.GetAirshipSnapshot().TryGetAirship(
                    AirshipTechnicalIds.Airship,
                    out var state),
                Is.True);
            return state;
        }

        private static AirshipPlayerState GetPlayer(
            AirshipSimulationBridge bridge)
        {
            Assert.That(
                bridge.GetAirshipSnapshot().TryGetPlayer(
                    AirshipTechnicalIds.Player,
                    out var state),
                Is.True);
            return state;
        }

        private static string HashPrefab()
        {
            using (var sha = SHA256.Create())
            {
                var bytes = File.ReadAllBytes(Path.GetFullPath(PrefabPath));
                return BitConverter.ToString(sha.ComputeHash(bytes));
            }
        }

        private static string EffectivePath(
            UnityEngine.InputSystem.InputAction action,
            string bindingName)
        {
            for (var index = 0; index < action.bindings.Count; index++)
            {
                if (string.Equals(
                    action.bindings[index].name,
                    bindingName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return action.bindings[index].effectivePath;
                }
            }

            return null;
        }

        private static void AssertGeneratedSceneRevision(
            string scenePath,
            string expectedSceneId,
            int expectedRevision)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var closeAfterInspection = !scene.IsValid() || !scene.isLoaded;
            if (closeAfterInspection)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                GeneratedSceneRevision marker = null;
                var markerCount = 0;
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0;
                     rootIndex < roots.Length;
                     rootIndex++)
                {
                    var markers = roots[rootIndex]
                        .GetComponentsInChildren<GeneratedSceneRevision>(true);
                    markerCount += markers.Length;
                    if (markers.Length > 0)
                    {
                        marker = markers[0];
                    }
                }

                Assert.That(markerCount, Is.EqualTo(1));
                Assert.That(marker, Is.Not.Null);
                Assert.That(
                    marker.Matches(expectedSceneId, expectedRevision),
                    Is.True);
            }
            finally
            {
                if (closeAfterInspection)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static string HierarchyPath(
            Transform value,
            Transform root)
        {
            var result = value.name;
            while (value.parent != null && value.parent != root.parent)
            {
                value = value.parent;
                result = value.name + "/" + result;
                if (value == root)
                {
                    break;
                }
            }

            return result;
        }

        private sealed class BoundaryRig : IDisposable
        {
            private readonly GameObject _ship;
            private readonly GameObject _player;

            private BoundaryRig(
                GameObject ship,
                GameObject player,
                AirshipMotor motor,
                AirshipSimulationBridge bridge,
                AirshipInputAdapter input,
                FirstPersonMouseLook mouseLook)
            {
                _ship = ship;
                _player = player;
                Motor = motor;
                Bridge = bridge;
                Input = input;
                MouseLook = mouseLook;
            }

            public AirshipMotor Motor { get; }

            public AirshipSimulationBridge Bridge { get; }

            public AirshipInputAdapter Input { get; }

            public FirstPersonMouseLook MouseLook { get; }

            public static BoundaryRig Create()
            {
                var ship = new GameObject("ship");
                var motor = ship.AddComponent<AirshipMotor>();
                motor.Configure(ship.transform);
                var passengerSpace = new GameObject("passengers").transform;
                passengerSpace.SetParent(ship.transform, false);
                var frame = ship.AddComponent<AirshipFrame>();
                var bridge = ship.AddComponent<AirshipSimulationBridge>();
                frame.Configure(passengerSpace, motor);

                var player = new GameObject("player");
                var controller = player.AddComponent<CharacterController>();
                var passenger = player.AddComponent<AirshipRelativePassenger>();
                var input = player.AddComponent<AirshipInputAdapter>();
                var yawPivot = new GameObject("view-yaw").transform;
                yawPivot.SetParent(player.transform, false);
                var pitchPivot = new GameObject("view-pitch").transform;
                pitchPivot.SetParent(yawPivot, false);
                var mouseLook = player.AddComponent<FirstPersonMouseLook>();
                mouseLook.Configure(yawPivot, pitchPivot);
                passenger.Configure(player.transform, controller, bridge);
                bridge.Configure(motor, frame, passenger, null, false);
                var stationObject = new GameObject("pilot-station");
                stationObject.transform.SetParent(ship.transform, false);
                stationObject.transform.localPosition = new Vector3(
                    AirshipSimulationConstants.PilotSeatCenter.X / 1000f,
                    AirshipSimulationConstants.PilotSeatCenter.Y / 1000f,
                    AirshipSimulationConstants.PilotSeatCenter.Z / 1000f);
                var station = stationObject.AddComponent<AirshipPilotStation>();
                station.Configure(
                    frame,
                    bridge,
                    passenger,
                    stationObject.transform,
                    2f);
                input.Configure(bridge, station);
                var airshipState = new AirshipSimulationStateBuilder()
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
                        new CatalogRevision(CatalogSchema.BootstrapContentRevision),
                        airshipState),
                    AirshipTechnicalIds.Airship,
                    AirshipTechnicalIds.Player);
                return new BoundaryRig(
                    ship,
                    player,
                    motor,
                    bridge,
                    input,
                    mouseLook);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_player);
                UnityEngine.Object.DestroyImmediate(_ship);
            }
        }
    }
}

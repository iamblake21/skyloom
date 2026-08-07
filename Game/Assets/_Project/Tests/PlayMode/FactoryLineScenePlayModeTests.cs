using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CML.Content;
using CML.Foundation;
using CML.Simulation.Inventories;
using CML.Simulation.Machines;
using CML.Unity.Airship;
using CML.Unity.Factory;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace CML.Tests.PlayMode
{
    public sealed class FactoryLineScenePlayModeTests
    {
        private const string FactoryScene =
            "Assets/_Project/Scenes/92_M04B_FactoryLine_Test.unity";
        private const int MaximumTicks = 2_000;

        [UnityTest]
        public IEnumerator SpatialModuleLineRunsEndToEndOnOneAuthority()
        {
            yield return LoadFactoryScene();
            var scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.EqualTo(FactoryScene));
            AssertNoMissingScripts(scene);

            var root = Only<FactoryLineSimulationRoot>(scene);
            var bridge = Only<TransferCommandBridge>(scene);
            var hud = Only<FactoryHudOrchestrator>(scene);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.IsHalted, Is.False);
            Assert.That(bridge.Engine, Is.SameAs(root.Engine));
            Assert.That(hud.Engine, Is.SameAs(root.Engine));
            Assert.That(bridge.Catalog, Is.SameAs(root.Catalog));
            Assert.That(hud.Catalog, Is.SameAs(root.Catalog));

            var graph = root.Engine.State.GetMachineSnapshot();
            Assert.That(graph.NodeCount, Is.EqualTo(13));
            Assert.That(graph.LaneCount, Is.Zero,
                "The retired endpoint-lane authority must not coexist with modules.");
            AssertNode(
                graph,
                FactoryLineSimulationRoot.SourceCrateId,
                MachineNodeKind.Buffer,
                ContentIds.WoodenCrate,
                -4,
                -7,
                0);
            AssertNode(
                graph,
                FactoryLineSimulationRoot.InputFunnelId,
                MachineNodeKind.Funnel,
                ContentIds.BeltFunnel,
                -4,
                -6,
                0);
            AssertNode(
                graph,
                FactoryLineSimulationRoot.PressId,
                MachineNodeKind.Machine,
                ContentIds.MechanicalPress,
                -4,
                -1,
                0);
            AssertNode(
                graph,
                FactoryLineSimulationRoot.OutputFunnelId,
                MachineNodeKind.Funnel,
                ContentIds.BeltFunnel,
                -4,
                4,
                2);
            AssertNode(
                graph,
                FactoryLineSimulationRoot.SinkCrateId,
                MachineNodeKind.Buffer,
                ContentIds.WoodenCrate,
                -4,
                5,
                0);
            AssertBelt(graph, FactoryLineSimulationRoot.FeedBelt01Id, -5);
            AssertBelt(graph, FactoryLineSimulationRoot.FeedBelt02Id, -4);
            AssertBelt(graph, FactoryLineSimulationRoot.FeedBelt03Id, -3);
            AssertBelt(graph, FactoryLineSimulationRoot.FeedBelt04Id, -2);
            AssertBelt(graph, FactoryLineSimulationRoot.DrainBelt01Id, 0);
            AssertBelt(graph, FactoryLineSimulationRoot.DrainBelt02Id, 1);
            AssertBelt(graph, FactoryLineSimulationRoot.DrainBelt03Id, 2);
            AssertBelt(graph, FactoryLineSimulationRoot.DrainBelt04Id, 3);

            Assert.That(
                FindInScene<FactoryBeltLanePresenter>(scene),
                Is.Empty,
                "The scene still contains the old lane presenter.");
            var modulePresenters =
                FindInScene<FactoryLogisticsModulePresenter>(scene);
            Assert.That(modulePresenters, Has.Count.EqualTo(10));
            foreach (var presenter in modulePresenters)
            {
                Assert.That(presenter.IsAttached, Is.True);
                Assert.That(
                    graph.TryGetNode(presenter.NodeId, out var node),
                    Is.True);
                Assert.That(presenter.NodeKind, Is.EqualTo(node.Kind));
            }

            var interactions = FindInScene<FactoryInteractionTarget>(scene);
            Assert.That(interactions, Has.Count.EqualTo(3),
                "Only two chests and the press need an E interaction.");
            foreach (var target in interactions)
            {
                if (target.InteractionKind == FactoryInteractionKind.Chest)
                {
                    Assert.That(target.Prompt, Is.EqualTo("Apri Cassa di legno"));
                }
            }

            root.enabled = false;
            var accepted = bridge.SubmitTransfer(
                TransferEndpoint.Inventory(
                    FactoryLineSimulationRoot.PlayerInventoryId),
                TransferEndpoint.Port(
                    FactoryLineSimulationRoot.SourceCrateId,
                    MachinePortKind.Storage),
                ContentIds.IronIngot,
                new NonNegativeQuantity(12));
            Assert.That(
                accepted.Command.TargetTick,
                Is.EqualTo(root.Engine.State.Tick.Next()));

            var pressPresenter = Only<FactoryPressPresenter>(scene);
            var ram = FindNamedTransform(pressPresenter.transform, "ANM_PressRam");
            Assert.That(ram, Is.Not.Null);
            var ramRest = ram.localPosition;
            var sawBeltCargo = false;
            var sawPressCycle = false;
            var sawRamMove = false;
            var sawBackpressure = false;
            var completed = false;

            for (var tick = 0; tick < MaximumTicks; tick++)
            {
                var result = root.Engine.AdvanceOneTick();
                Assert.That(
                    result.Committed,
                    Is.True,
                    $"tick {result.ExecutingTick} failed in "
                    + $"{result.FailedPhase}: {result.FailureCause}");

                foreach (var presenter in modulePresenters)
                {
                    presenter.RefreshImmediate();
                    sawBeltCargo |= presenter.NodeKind == MachineNodeKind.BeltModule
                        && presenter.HasCargoVisual;
                }

                pressPresenter.RefreshImmediate();
                if (pressPresenter.IsPresentingCycle)
                {
                    sawPressCycle = true;
                    sawRamMove |= Vector3.Distance(
                        ram.localPosition,
                        ramRest) > 0.005f;
                }

                var press = Node(root, FactoryLineSimulationRoot.PressId);
                var feedLast = Node(
                    root,
                    FactoryLineSimulationRoot.FeedBelt04Id);
                sawBackpressure |= press.IsCycleActive && !feedLast.Input.IsEmpty;

                if (Node(root, FactoryLineSimulationRoot.SinkCrateId)
                    .Input.Count(ContentIds.IronPlate).Value == 12L)
                {
                    completed = true;
                    break;
                }

                if ((tick & 15) == 15)
                {
                    yield return null;
                }
            }

            Assert.That(completed, Is.True,
                "The twelve ingots never reached the sink chest as plates.");
            Assert.That(sawBeltCargo, Is.True,
                "No authoritative belt cargo was ever presented.");
            Assert.That(sawPressCycle, Is.True,
                "The press never entered its authoritative cycle.");
            Assert.That(sawRamMove, Is.True,
                "The press ram did not move vertically during a cycle.");
            Assert.That(sawBackpressure, Is.True,
                "The line never stopped behind the one-ingot press capacity.");
            Assert.That(
                Node(root, FactoryLineSimulationRoot.PressId).CompletedCycles,
                Is.EqualTo(12UL));
            Assert.That(root.Engine.State.GetMachineSnapshot().LaneCount, Is.Zero);
            foreach (var id in BeltIds())
            {
                Assert.That(Node(root, id).Input.IsEmpty, Is.True);
            }
        }

        private static void AssertBelt(
            MachineSimulationState graph,
            StableId id,
            int zMetres)
        {
            AssertNode(
                graph,
                id,
                MachineNodeKind.BeltModule,
                ContentIds.BeltStraight,
                -4,
                zMetres,
                0);
        }

        private static void AssertNode(
            MachineSimulationState graph,
            StableId id,
            MachineNodeKind kind,
            StableId definition,
            int xMetres,
            int zMetres,
            byte yaw)
        {
            Assert.That(graph.TryGetNode(id, out var node), Is.True);
            Assert.That(node.Kind, Is.EqualTo(kind));
            Assert.That(node.DefinitionId, Is.EqualTo(definition));
            Assert.That(node.HasPlacementPose, Is.True);
            Assert.That(
                node.PlacementPose,
                Is.EqualTo(
                    new MachineBuildPose(
                        xMetres * 1_000,
                        0,
                        zMetres * 1_000,
                        yaw)));
        }

        private static IReadOnlyList<StableId> BeltIds() =>
            new[]
            {
                FactoryLineSimulationRoot.FeedBelt01Id,
                FactoryLineSimulationRoot.FeedBelt02Id,
                FactoryLineSimulationRoot.FeedBelt03Id,
                FactoryLineSimulationRoot.FeedBelt04Id,
                FactoryLineSimulationRoot.DrainBelt01Id,
                FactoryLineSimulationRoot.DrainBelt02Id,
                FactoryLineSimulationRoot.DrainBelt03Id,
                FactoryLineSimulationRoot.DrainBelt04Id
            };

        private static MachineNodeState Node(
            FactoryLineSimulationRoot root,
            StableId id)
        {
            Assert.That(
                root.Engine.State.GetMachineSnapshot().TryGetNode(id, out var node),
                Is.True,
                $"Missing node {id}.");
            return node;
        }

        internal static IEnumerator LoadFactoryScene()
        {
#if UNITY_EDITOR
            var operation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                FactoryScene,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            var operation = SceneManager.LoadSceneAsync(
                FactoryScene,
                LoadSceneMode.Single);
#endif
            Assert.That(operation, Is.Not.Null);
            while (!operation.isDone)
            {
                yield return null;
            }

            yield return null;
        }

        internal static T Only<T>(Scene scene)
            where T : Component
        {
            var values = FindInScene<T>(scene);
            Assert.That(values, Has.Count.EqualTo(1),
                $"Scene requires exactly one {typeof(T).Name}.");
            return values[0];
        }

        internal static List<T> FindInScene<T>(Scene scene)
            where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return result;
        }

        internal static GameObject FindNamed(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(child.name, name, StringComparison.Ordinal))
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }

        internal static Transform FindNamedTransform(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static void AssertNoMissingScripts(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var component in child.GetComponents<Component>())
                    {
                        Assert.That(component, Is.Not.Null,
                            $"Missing script below {child.name}.");
                    }
                }
            }
        }
    }

    public sealed class FactoryBuildScenePlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator HeldModulesPlaceWithOneClickAndConsumeExactlyOne()
        {
            yield return FactoryLineScenePlayModeTests.LoadFactoryScene();
            var scene = SceneManager.GetActiveScene();
            var root = FactoryLineScenePlayModeTests
                .Only<FactoryLineSimulationRoot>(scene);
            var controller = FactoryLineScenePlayModeTests
                .Only<FactoryBuildController>(scene);
            var camera = FactoryLineScenePlayModeTests.Only<Camera>(scene);
            var runtimeBuilds = FactoryLineScenePlayModeTests.FindNamed(
                scene,
                "M04B_RuntimeBuilds");
            Assert.That(runtimeBuilds, Is.Not.Null);

            FactoryLineScenePlayModeTests
                .Only<FactoryFirstPersonInput>(scene).enabled = false;
            FactoryLineScenePlayModeTests
                .Only<FirstPersonMouseLook>(scene).enabled = false;

            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            var initialNodes = root.Engine.State.GetMachineSnapshot().NodeCount;
            Assert.That(root.TryGetPlayerInventory(out var inventory), Is.True);
            var initialBelts = inventory.Count(ContentIds.BeltStraight).Value;
            var initialFunnels = inventory.Count(ContentIds.BeltFunnel).Value;
            Assert.That(initialNodes, Is.EqualTo(13));
            Assert.That(initialBelts, Is.EqualTo(24L));
            Assert.That(initialFunnels, Is.EqualTo(4L));

            AimAtGround(camera, new Vector3(7f, 0.05f, 0f));
            yield return null;

            // Slot 1 is an ingot. B does not create a catalog or a hidden mode.
            Assert.That(controller.HasPlaceableHeld, Is.False);
            Assert.That(Preview(controller), Is.Null);
            yield return Tap(keyboard.bKey);
            Assert.That(controller.HasPlaceableHeld, Is.False);
            Assert.That(Preview(controller), Is.Null);
            Assert.That(root.Engine.State.PendingCommandCount, Is.Zero);

            // Slot 4 contains belts. Selection alone creates one ground hologram.
            yield return Tap(keyboard.digit4Key);
            yield return WaitUntilOrFail(
                () => controller.HasPlaceableHeld
                    && controller.PreviewValid
                    && Preview(controller) != null,
                "The selected belt did not produce its ground hologram.");
            var beltSpecification =
                PrivateValue<MachineBuildSpecification>(
                    controller,
                    "_previewSpecification");
            Assert.That(
                beltSpecification.Kind,
                Is.EqualTo(MachineBuildKind.BeltModule));
            Assert.That(
                beltSpecification.CostItemId,
                Is.EqualTo(ContentIds.BeltStraight));
            Assert.That(beltSpecification.CostQuantity, Is.EqualTo(1L));
            Assert.That(beltSpecification.PrimaryId, Is.EqualTo(ContentIds.BeltStraight));
            Assert.That(beltSpecification.SecondaryId.IsNone, Is.True);
            Assert.That(beltSpecification.TertiaryId.IsNone, Is.True);

            var beforeRotation = Preview(controller).transform.rotation;
            yield return Tap(keyboard.rKey);
            Assert.That(
                Quaternion.Angle(
                    beforeRotation,
                    Preview(controller).transform.rotation),
                Is.EqualTo(90f).Within(0.1f));
            beltSpecification =
                PrivateValue<MachineBuildSpecification>(
                    controller,
                    "_previewSpecification");
            Assert.That(beltSpecification.Pose.YawQuarterTurns, Is.EqualTo(1));

            yield return Tap(mouse.leftButton);
            yield return WaitUntilOrFail(
                () => root.Engine.State.GetMachineSnapshot().NodeCount
                    == initialNodes + 1,
                "One belt click did not commit exactly one node.");
            Assert.That(root.TryGetPlayerInventory(out inventory), Is.True);
            Assert.That(
                inventory.Count(ContentIds.BeltStraight).Value,
                Is.EqualTo(initialBelts - 1L));
            var builtBelts = RuntimeAnchors(
                runtimeBuilds.transform,
                MachineNodeKind.BeltModule);
            Assert.That(builtBelts, Has.Count.EqualTo(1));
            Assert.That(
                root.Engine.State.GetMachineSnapshot().TryGetNode(
                    builtBelts[0].NodeId,
                    out var beltNode),
                Is.True);
            Assert.That(beltNode.HasPlacementPose, Is.True);
            Assert.That(beltNode.PlacementPose.YawQuarterTurns, Is.EqualTo(1));
            Assert.That(
                builtBelts[0].GetComponent<FactoryLogisticsModulePresenter>(),
                Is.Not.Null);

            // Funnel is equally free-standing: no source or destination is selected.
            // Its visual yaw adapts the authored asset, while the authored belt port
            // itself must land exactly on the logical cell's front boundary.
            AimAtGround(camera, new Vector3(9f, 0.05f, 2f));
            yield return Tap(keyboard.digit3Key);
            yield return WaitUntilOrFail(
                () => controller.HasPlaceableHeld
                    && controller.PreviewValid
                    && Preview(controller) != null,
                "The selected funnel did not produce a free ground hologram.");
            var funnelSpecification =
                PrivateValue<MachineBuildSpecification>(
                    controller,
                    "_previewSpecification");
            Assert.That(funnelSpecification.Kind, Is.EqualTo(MachineBuildKind.Funnel));
            Assert.That(funnelSpecification.CostQuantity, Is.EqualTo(1L));
            Assert.That(funnelSpecification.SecondaryId.IsNone, Is.True);
            Assert.That(funnelSpecification.Pose.YawQuarterTurns, Is.Zero);
            Assert.That(
                Mathf.Abs(
                    Mathf.DeltaAngle(
                        Preview(controller).transform.eulerAngles.y,
                        180f)),
                Is.LessThan(0.1f));
            var funnelBeltPort =
                FactoryLineScenePlayModeTests.FindNamedTransform(
                    Preview(controller).transform,
                    "PORT_Belt");
            Assert.That(funnelBeltPort, Is.Not.Null);
            Assert.That(
                funnelBeltPort.position.x,
                Is.EqualTo(funnelSpecification.Pose.XMillimetres / 1_000f)
                    .Within(0.001f));
            Assert.That(
                funnelBeltPort.position.z,
                Is.EqualTo(
                        funnelSpecification.Pose.ZMillimetres / 1_000f + 0.5f)
                    .Within(0.001f));

            yield return Tap(mouse.leftButton);
            yield return WaitUntilOrFail(
                () => root.Engine.State.GetMachineSnapshot().NodeCount
                    == initialNodes + 2,
                "One funnel click did not commit exactly one node.");
            Assert.That(root.TryGetPlayerInventory(out inventory), Is.True);
            Assert.That(
                inventory.Count(ContentIds.BeltFunnel).Value,
                Is.EqualTo(initialFunnels - 1L));
            var builtFunnels = RuntimeAnchors(
                runtimeBuilds.transform,
                MachineNodeKind.Funnel);
            Assert.That(builtFunnels, Has.Count.EqualTo(1));
            Assert.That(
                root.Engine.State.GetMachineSnapshot().TryGetNode(
                    builtFunnels[0].NodeId,
                    out var funnelNode),
                Is.True);
            Assert.That(funnelNode.AttachedNodeId.IsNone, Is.True);
            Assert.That(funnelNode.Input.IsEmpty, Is.True);

            for (var tick = 0; tick < 40; tick++)
            {
                var result = root.Engine.AdvanceOneTick();
                Assert.That(result.Committed, Is.True);
            }

            Assert.That(
                root.Engine.State.GetMachineSnapshot().TryGetNode(
                    builtFunnels[0].NodeId,
                    out funnelNode),
                Is.True);
            Assert.That(funnelNode.Input.IsEmpty, Is.True,
                "An isolated Funnel must not transfer items magically.");
        }

        [UnityTest]
        public IEnumerator EOpensTheGenericWoodenCratePanel()
        {
            yield return FactoryLineScenePlayModeTests.LoadFactoryScene();
            var scene = SceneManager.GetActiveScene();
            var camera = FactoryLineScenePlayModeTests.Only<Camera>(scene);
            var interactor = FactoryLineScenePlayModeTests
                .Only<FactoryCentralInteractor>(scene);
            var hud = FactoryLineScenePlayModeTests
                .Only<FactoryHudOrchestrator>(scene);
            var chestHud = FactoryLineScenePlayModeTests
                .Only<ChestHudController>(scene);
            var sourceCrate = FactoryLineScenePlayModeTests.FindNamed(
                scene,
                "M04B_SourceCrate");
            Assert.That(sourceCrate, Is.Not.Null);

            FactoryLineScenePlayModeTests
                .Only<FactoryFirstPersonInput>(scene).enabled = false;
            FactoryLineScenePlayModeTests
                .Only<FirstPersonMouseLook>(scene).enabled = false;
            var target = sourceCrate.GetComponent<FactoryInteractionTarget>();
            var collider = sourceCrate.GetComponentInChildren<Collider>();
            Assert.That(target, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(target.Prompt, Is.EqualTo("Apri Cassa di legno"));

            var focus = collider.bounds.center;
            var position = focus + sourceCrate.transform.forward * 2f;
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(focus - position, Vector3.up));
            Physics.SyncTransforms();

            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            Assert.That(interactor.CurrentTarget, Is.SameAs(target));
            Assert.That(
                interactor.CurrentPrompt,
                Is.EqualTo("E  Apri Cassa di legno"));
            yield return Tap(keyboard.eKey);
            Assert.That(hud.InteractionPanelOpen, Is.True);
            Assert.That(chestHud.PanelOpen, Is.True);
            var lidAnimator = sourceCrate.GetComponent<ChestLidAnimator>();
            Assert.That(
                lidAnimator,
                Is.Not.Null,
                "Opening a world crate must attach its lid presenter.");
            Assert.That(lidAnimator.Lid, Is.Not.Null);
            Assert.That(lidAnimator.Hinge, Is.Not.Null);
            yield return WaitUntilOrFail(
                () => lidAnimator.OpenAmount >= 0.99f,
                "The wooden crate lid did not finish opening.");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    lidAnimator.Hinge.localRotation),
                Is.GreaterThan(100f));

            yield return Tap(keyboard.eKey);
            Assert.That(hud.InteractionPanelOpen, Is.False);
            Assert.That(chestHud.PanelOpen, Is.False);
            yield return WaitUntilOrFail(
                () => lidAnimator.OpenAmount <= 0.01f,
                "The wooden crate lid did not finish closing.");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    lidAnimator.Hinge.localRotation),
                Is.LessThan(1f));
        }

        private static void AimAtGround(Camera camera, Vector3 target)
        {
            var position = target + new Vector3(0f, 6f, -4f);
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(target - position, Vector3.up));
            Physics.SyncTransforms();
        }

        private static List<FactoryNodeAnchor> RuntimeAnchors(
            Transform root,
            MachineNodeKind kind)
        {
            var result = new List<FactoryNodeAnchor>();
            foreach (var anchor in root.GetComponentsInChildren<
                FactoryNodeAnchor>(true))
            {
                if (anchor.NodeKind == kind)
                {
                    result.Add(anchor);
                }
            }

            return result;
        }

        private static GameObject Preview(FactoryBuildController controller) =>
            PrivateValue<GameObject>(controller, "_nodePreview");

        private static T PrivateValue<T>(
            FactoryBuildController controller,
            string fieldName)
        {
            var field = typeof(FactoryBuildController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"FactoryBuildController no longer has {fieldName}.");
            return (T)field.GetValue(controller);
        }

        private IEnumerator Tap(
            UnityEngine.InputSystem.Controls.ButtonControl button)
        {
            Press(button);
            yield return null;
            Release(button);
            yield return null;
        }

        private static IEnumerator WaitUntilOrFail(
            Func<bool> condition,
            string failureMessage)
        {
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, failureMessage);
        }
    }
}

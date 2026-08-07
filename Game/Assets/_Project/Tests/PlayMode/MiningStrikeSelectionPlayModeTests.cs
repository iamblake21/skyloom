using System.Reflection;
using CML.Unity.Mining;
using CML.Unity.Presentation.Equipment;
using CML.Unity.Wood;
using NUnit.Framework;
using UnityEngine;

namespace CML.Tests.PlayMode
{
    public sealed class MiningStrikeSelectionPlayModeTests
    {
        private const int TestLayer = 30;
        private const float TestSwingDuration = 0.08f;
        private GameObject _cameraObject;
        private GameObject _rockObject;
        private GameObject _obstacleObject;
        private FirstPersonEquipmentMotion _motion;

        [TearDown]
        public void TearDown()
        {
            DestroyImpactBursts();
            DestroyImmediateIfPresent(_obstacleObject);
            DestroyImmediateIfPresent(_rockObject);
            DestroyImmediateIfPresent(_cameraObject);
        }

        [Test]
        public void RequestSwingEmitsMiningSourceForExactMeshNearEdge()
        {
            CreateMotion(strikeAssistRadius: 0.06f);
            var identity = CreateMeshRock(
                new Vector3(0.42f, 0f, 2f),
                out var meshCollider);
            Physics.SyncTransforms();

            var sourceHitCount = 0;
            ManualMiningSourceIdentity selectedSource = null;
            RaycastHit selectedHit = default;
            _motion.PickaxeMiningSourceHit += source =>
            {
                sourceHitCount++;
                selectedSource = source;
            };
            _motion.PickaxeImpactHit += hit => selectedHit = hit;

            Assert.That(_motion.RequestSwing(), Is.True);
            CompleteSwing(_motion);

            Assert.That(sourceHitCount, Is.EqualTo(1));
            Assert.That(selectedSource, Is.SameAs(identity));
            Assert.That(selectedHit.collider, Is.SameAs(meshCollider));
            Assert.That(selectedHit.collider, Is.TypeOf<MeshCollider>());

            var horizontalEdgeFraction = Mathf.Abs(
                selectedHit.point.x - meshCollider.bounds.center.x) /
                meshCollider.bounds.extents.x;
            Assert.That(
                horizontalEdgeFraction,
                Is.GreaterThan(0.70f),
                "The reticle must select the exact mesh near its silhouette, " +
                "not only near the rock centre.");

            var burst = GameObject.Find(
                PickaxeImpactBurst.StoneObjectName);
            Assert.That(burst, Is.Not.Null);
            Assert.That(
                Vector3.Distance(
                    burst.transform.position,
                    selectedHit.point),
                Is.LessThan(0.03f));
            Assert.That(
                Vector3.Dot(
                    burst.transform.forward,
                    selectedHit.normal.normalized),
                Is.GreaterThan(0.999f));
        }

        [Test]
        public void RequestSwingEmitsWoodBurstForExactTreeMesh()
        {
            CreateMotion(strikeAssistRadius: 0.06f);
            _rockObject = GameObject.CreatePrimitive(
                PrimitiveType.Cylinder);
            _rockObject.name = "DEC_Tree_PlayModeImpact";
            _rockObject.layer = TestLayer;
            _rockObject.transform.position = new Vector3(0f, 0f, 2f);
            Object.DestroyImmediate(
                _rockObject.GetComponent<CapsuleCollider>());
            var collider = _rockObject.AddComponent<MeshCollider>();
            collider.sharedMesh =
                _rockObject.GetComponent<MeshFilter>().sharedMesh;
            collider.convex = false;
            var tree = _rockObject.AddComponent<FellableTreeIdentity>();
            tree.Configure("tests.playmode.impact-tree");
            Physics.SyncTransforms();

            RaycastHit selectedHit = default;
            _motion.PickaxeImpactHit += hit => selectedHit = hit;

            Assert.That(_motion.RequestSwing(), Is.True);
            CompleteSwing(_motion);

            var burst = GameObject.Find(
                PickaxeImpactBurst.WoodObjectName);
            Assert.That(burst, Is.Not.Null);
            Assert.That(
                GameObject.Find(PickaxeImpactBurst.StoneObjectName),
                Is.Null);
            Assert.That(selectedHit.collider, Is.SameAs(collider));
            Assert.That(
                Vector3.Distance(
                    burst.transform.position,
                    selectedHit.point),
                Is.LessThan(0.03f));
            Assert.That(
                Vector3.Dot(
                    burst.transform.forward,
                    selectedHit.normal.normalized),
                Is.GreaterThan(0.999f));
        }

        [Test]
        public void CloserOffAxisSolidBlocksMiningSphereAssist()
        {
            const float assistRadius = 0.06f;
            CreateMotion(assistRadius);
            var identity = CreateMeshRock(
                new Vector3(0.525f, 0f, 2f),
                out var meshCollider);
            Physics.SyncTransforms();

            var centreRay = new Ray(
                _cameraObject.transform.position,
                _cameraObject.transform.forward);
            Assert.That(
                meshCollider.Raycast(centreRay, out _, 4f),
                Is.False,
                "This setup must require sphere assistance rather than a " +
                "direct centre ray.");

            var sourceHitCount = 0;
            ManualMiningSourceIdentity selectedSource = null;
            _motion.PickaxeMiningSourceHit += source =>
            {
                sourceHitCount++;
                selectedSource = source;
            };

            Assert.That(_motion.RequestSwing(), Is.True);
            CompleteSwing(_motion);
            Assert.That(sourceHitCount, Is.EqualTo(1));
            Assert.That(
                selectedSource,
                Is.SameAs(identity),
                "The off-axis mesh must first prove that it is inside the " +
                "configured assist radius.");

            sourceHitCount = 0;
            selectedSource = null;
            var obstacleCollider = CreateOffAxisObstacle();
            Physics.SyncTransforms();
            Assert.That(
                obstacleCollider.Raycast(centreRay, out _, 4f),
                Is.False,
                "The blocker must exercise the sphere sweep, not the direct " +
                "centre ray.");

            Assert.That(_motion.RequestSwing(), Is.True);
            CompleteSwing(_motion);

            Assert.That(
                sourceHitCount,
                Is.Zero,
                "A nearer solid touched by the same sphere sweep must block " +
                "assistance from selecting the rock behind it.");
            Assert.That(selectedSource, Is.Null);
        }

        private void CreateMotion(float strikeAssistRadius)
        {
            _cameraObject = new GameObject(
                "MiningStrikeSelectionTests_Camera");
            _cameraObject.layer = TestLayer;
            var camera = _cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            var motionRoot = new GameObject("MotionRoot").transform;
            motionRoot.SetParent(_cameraObject.transform, false);
            var swingRoot = new GameObject("SwingRoot").transform;
            swingRoot.SetParent(motionRoot, false);

            _motion = _cameraObject.AddComponent<
                FirstPersonEquipmentMotion>();
            _motion.Configure(
                motionRoot,
                swingRoot,
                motor: null,
                collision: null);
            SetPrivateField(
                _motion,
                "swingDuration",
                TestSwingDuration);
            SetPrivateField(
                _motion,
                "missSwingDuration",
                TestSwingDuration);
            SetPrivateField(
                _motion,
                "maximumStrikeDistance",
                4f);
            SetPrivateField(
                _motion,
                "strikeAssistRadius",
                strikeAssistRadius);
            SetPrivateField(
                _motion,
                "strikeLayers",
                (LayerMask)(1 << TestLayer));
        }

        private ManualMiningSourceIdentity CreateMeshRock(
            Vector3 position,
            out MeshCollider meshCollider)
        {
            _rockObject = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);
            _rockObject.name = "DEC_Rock_PlayModeEdge";
            _rockObject.layer = TestLayer;
            _rockObject.transform.position = position;
            Object.DestroyImmediate(
                _rockObject.GetComponent<SphereCollider>());

            var identity = _rockObject.AddComponent<
                ManualMiningSourceIdentity>();
            identity.Configure(
                ManualMiningSourceKind.EnvironmentalStone,
                "tests.playmode.edge-mesh-rock");
            meshCollider = identity.EnsureMiningMeshColliders();
            Assert.That(meshCollider, Is.Not.Null);
            Assert.That(meshCollider.sharedMesh, Is.SameAs(
                _rockObject.GetComponent<MeshFilter>().sharedMesh));
            return identity;
        }

        private Collider CreateOffAxisObstacle()
        {
            _obstacleObject = new GameObject(
                "MiningStrikeSelectionTests_OffAxisBlocker");
            _obstacleObject.layer = TestLayer;
            _obstacleObject.transform.position =
                new Vector3(0.045f, 0f, 0.9f);
            var collider = _obstacleObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.04f, 0.7f, 0.20f);
            return collider;
        }

        private static void CompleteSwing(
            FirstPersonEquipmentMotion motion)
        {
            var evaluateSwing = motion.GetType().GetMethod(
                "EvaluateSwing",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(
                evaluateSwing,
                Is.Not.Null,
                "FirstPersonEquipmentMotion no longer exposes its internal " +
                "swing evaluation seam.");
            var arguments = new object[]
            {
                TestSwingDuration,
                Vector3.zero,
                Quaternion.identity
            };
            evaluateSwing.Invoke(motion, arguments);

            Assert.That(
                motion.IsSwinging,
                Is.False,
                "The deterministic test step did not finish the swing.");
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static void DestroyImmediateIfPresent(
            GameObject target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }

        private static void DestroyImpactBursts()
        {
            var systems = Object.FindObjectsByType<ParticleSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < systems.Length; index++)
            {
                var host = systems[index] != null
                    ? systems[index].gameObject
                    : null;
                if (host != null &&
                    (host.name == PickaxeImpactBurst.StoneObjectName ||
                     host.name == PickaxeImpactBurst.WoodObjectName))
                {
                    Object.DestroyImmediate(host);
                }
            }
        }
    }
}

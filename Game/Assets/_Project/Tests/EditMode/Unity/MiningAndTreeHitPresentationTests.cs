using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CML.Unity.Mining;
using CML.Unity.Presentation.Equipment;
using CML.Unity.Wood;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class MiningAndTreeHitPresentationTests
    {
        [Test]
        public void StoneImpactKeepsLightmapRendererInPlace()
        {
            GameObject cameraObject = null;
            GameObject motionRootObject = null;
            GameObject swingRootObject = null;
            GameObject rock = null;
            Material originalMaterial = null;

            try
            {
                var shader = Shader.Find(
                    "CML/Environment/Starter Island Stylized Surface");
                Assert.That(shader, Is.Not.Null);

                originalMaterial = new Material(shader);
                Assert.That(
                    originalMaterial.HasProperty("_CMLHitOffsetWS"),
                    Is.True);
                rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = "StoneImpactLightmapTest";
                var renderer = rock.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = originalMaterial;
                renderer.lightmapIndex = 7;
                renderer.lightmapScaleOffset =
                    new Vector4(0.73f, 0.64f, 0.12f, 0.21f);
                var collider = rock.GetComponent<Collider>();
                Physics.SyncTransforms();
                Assert.That(
                    collider.Raycast(
                        new Ray(new Vector3(0f, 0f, -3f), Vector3.forward),
                        out var hit,
                        10f),
                    Is.True);

                cameraObject = new GameObject(
                    "StoneImpactLightmapTest_Camera");
                cameraObject.AddComponent<Camera>().enabled = false;
                motionRootObject = new GameObject("MotionRoot");
                motionRootObject.transform.SetParent(
                    cameraObject.transform,
                    false);
                swingRootObject = new GameObject("SwingRoot");
                swingRootObject.transform.SetParent(
                    motionRootObject.transform,
                    false);
                var motion = cameraObject.AddComponent<
                    FirstPersonEquipmentMotion>();
                motion.Configure(
                    motionRootObject.transform,
                    swingRootObject.transform,
                    motor: null,
                    collision: null);
                var feedback = cameraObject.AddComponent<
                    FirstPersonImpactFeedback>();
                feedback.Configure(motion);

                InvokePrivate(
                    feedback,
                    "Play",
                    rock.transform,
                    hit,
                    PickaxeImpactSurface.Stone);
                var prepared = (bool)InvokePrivate(
                    feedback,
                    "PrepareVisualFeedback");

                Assert.That(prepared, Is.True);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.lightmapIndex, Is.EqualTo(7));
                Assert.That(
                    renderer.lightmapScaleOffset,
                    Is.EqualTo(new Vector4(0.73f, 0.64f, 0.12f, 0.21f)));
                Assert.That(
                    rock.transform.Find("FEEL_HitObjectVisual"),
                    Is.Null);

                var impactMaterial = renderer.sharedMaterial;
                Assert.That(
                    impactMaterial.GetVector("_CMLHitOffsetWS"),
                    Is.EqualTo(Vector4.zero));
                InvokePrivate(
                    feedback,
                    "ApplyRendererOffset",
                    new Vector3(0.01f, -0.02f, 0.03f));
                Assert.That(
                    impactMaterial.GetVector("_CMLHitOffsetWS"),
                    Is.EqualTo(new Vector4(0.01f, -0.02f, 0.03f, 0f)));

                InvokePrivate(feedback, "StopAndReset");
                Assert.That(renderer.sharedMaterial, Is.SameAs(originalMaterial));
            }
            finally
            {
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                if (motionRootObject != null)
                {
                    Object.DestroyImmediate(motionRootObject);
                }

                if (swingRootObject != null)
                {
                    Object.DestroyImmediate(swingRootObject);
                }

                if (rock != null)
                {
                    Object.DestroyImmediate(rock);
                }

                if (originalMaterial != null)
                {
                    Object.DestroyImmediate(originalMaterial);
                }
            }
        }

        [Test]
        public void DepositFloorImpactDoesNotStartRockShake()
        {
            GameObject cameraObject = null;
            GameObject motionRootObject = null;
            GameObject swingRootObject = null;
            GameObject depositSurface = null;

            try
            {
                depositSurface = GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
                depositSurface.name =
                    ManualMiningSourceIdentity.InfiniteDepositSurfaceName;
                var source = depositSurface.AddComponent<
                    ManualMiningSourceIdentity>();
                source.Configure(
                    ManualMiningSourceKind.IronDepositSurface,
                    "tests.infinite-deposit-surface");
                var collider = depositSurface.GetComponent<Collider>();
                Physics.SyncTransforms();
                Assert.That(
                    collider.Raycast(
                        new Ray(new Vector3(0f, 0f, -3f), Vector3.forward),
                        out var hit,
                        10f),
                    Is.True);

                cameraObject = new GameObject(
                    "DepositFloorImpactTest_Camera");
                cameraObject.AddComponent<Camera>().enabled = false;
                motionRootObject = new GameObject("MotionRoot");
                motionRootObject.transform.SetParent(
                    cameraObject.transform,
                    false);
                swingRootObject = new GameObject("SwingRoot");
                swingRootObject.transform.SetParent(
                    motionRootObject.transform,
                    false);
                var motion = cameraObject.AddComponent<
                    FirstPersonEquipmentMotion>();
                motion.Configure(
                    motionRootObject.transform,
                    swingRootObject.transform,
                    motor: null,
                    collision: null);
                var feedback = cameraObject.AddComponent<
                    FirstPersonImpactFeedback>();
                feedback.Configure(motion);

                InvokePrivate(feedback, "HandlePhysicalImpact", hit);
                InvokePrivate(feedback, "HandleStoneImpact", source);

                var activeTarget = (Transform)typeof(
                    FirstPersonImpactFeedback).GetField(
                        "_activeTarget",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(feedback);
                Assert.That(activeTarget, Is.Null);
                Assert.That(
                    GameObject.Find("FEEL_HitObjectVisual"),
                    Is.Null);
            }
            finally
            {
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                if (motionRootObject != null)
                {
                    Object.DestroyImmediate(motionRootObject);
                }

                if (swingRootObject != null)
                {
                    Object.DestroyImmediate(swingRootObject);
                }

                if (depositSurface != null)
                {
                    Object.DestroyImmediate(depositSurface);
                }
            }
        }

        [Test]
        public void PickaxeImpactBurstFacesSurfaceAndUsesDistinctTints()
        {
            GameObject stone = null;
            GameObject wood = null;
            var stonePoint = new Vector3(2f, 3f, 4f);
            var woodPoint = new Vector3(-2f, 1f, -3f);

            try
            {
                PickaxeImpactBurst.Play(
                    stonePoint,
                    Vector3.right,
                    PickaxeImpactSurface.Stone);
                PickaxeImpactBurst.Play(
                    woodPoint,
                    Vector3.up,
                    PickaxeImpactSurface.Wood);

                stone = GameObject.Find(
                    PickaxeImpactBurst.StoneObjectName);
                wood = GameObject.Find(
                    PickaxeImpactBurst.WoodObjectName);
                Assert.That(stone, Is.Not.Null);
                Assert.That(wood, Is.Not.Null);

                Assert.That(
                    Vector3.Dot(stone.transform.forward, Vector3.right),
                    Is.GreaterThan(0.999f));
                Assert.That(
                    Vector3.Dot(wood.transform.forward, Vector3.up),
                    Is.GreaterThan(0.999f));
                Assert.That(
                    Vector3.Dot(
                        stone.transform.position - stonePoint,
                        Vector3.right),
                    Is.InRange(0.015f, 0.025f));
                Assert.That(
                    Vector3.Dot(
                        wood.transform.position - woodPoint,
                        Vector3.up),
                    Is.InRange(0.015f, 0.025f));

                var stoneSystem = stone.GetComponent<ParticleSystem>();
                var woodSystem = wood.GetComponent<ParticleSystem>();
                Assert.That(stoneSystem, Is.Not.Null);
                Assert.That(woodSystem, Is.Not.Null);
                Assert.That(
                    stoneSystem.shape.shapeType,
                    Is.EqualTo(ParticleSystemShapeType.Cone));
                Assert.That(
                    woodSystem.shape.shapeType,
                    Is.EqualTo(ParticleSystemShapeType.Cone));

                var stoneRenderer = stone.GetComponent<
                    ParticleSystemRenderer>();
                var woodRenderer = wood.GetComponent<
                    ParticleSystemRenderer>();
                Assert.That(
                    stoneRenderer.renderMode,
                    Is.EqualTo(ParticleSystemRenderMode.Mesh));
                Assert.That(
                    woodRenderer.renderMode,
                    Is.EqualTo(ParticleSystemRenderMode.Mesh));
                Assert.That(stoneRenderer.mesh, Is.Not.Null);
                Assert.That(woodRenderer.mesh, Is.Not.Null);

                var stoneRenderers = stone.GetComponentsInChildren<
                    ParticleSystemRenderer>(true);
                var woodRenderers = wood.GetComponentsInChildren<
                    ParticleSystemRenderer>(true);
                Assert.That(stoneRenderers, Has.Length.EqualTo(2));
                Assert.That(woodRenderers, Has.Length.EqualTo(2));
                Assert.That(
                    stoneRenderers.All(HasValidMeshRenderer),
                    Is.True,
                    "Impact feedback must never regress to visible quads.");
                Assert.That(
                    woodRenderers.All(HasValidMeshRenderer),
                    Is.True,
                    "Wood dust and splinters must both be 3D meshes.");

                var stoneMaterial = stoneRenderer.sharedMaterial;
                var woodMaterial = woodRenderer.sharedMaterial;
                var stoneFragmentMaterial = stoneRenderers
                    .Single(renderer => renderer != stoneRenderer)
                    .sharedMaterial;
                var woodFragmentMaterial = woodRenderers
                    .Single(renderer => renderer != woodRenderer)
                    .sharedMaterial;
                Assert.That(stoneMaterial, Is.Not.Null);
                Assert.That(woodMaterial, Is.Not.Null);
                Assert.That(stoneFragmentMaterial, Is.Not.Null);
                Assert.That(woodFragmentMaterial, Is.Not.Null);
                Assert.That(woodMaterial, Is.Not.SameAs(stoneMaterial));
                Assert.That(
                    stoneMaterial.shader.name,
                    Is.EqualTo("CML/Effects/Impact Smoke Mesh"));
                Assert.That(
                    woodMaterial.shader.name,
                    Is.EqualTo("CML/Effects/Impact Smoke Mesh"));
                Assert.That(
                    stoneFragmentMaterial.shader.name,
                    Is.EqualTo("CML/Effects/Impact Fragment Mesh"));
                Assert.That(
                    woodFragmentMaterial.shader.name,
                    Is.EqualTo("CML/Effects/Impact Fragment Mesh"));
                Assert.That(stoneMaterial.shader.isSupported, Is.True);
                Assert.That(stoneFragmentMaterial.shader.isSupported, Is.True);
                Assert.That(
                    ShaderUtil.ShaderHasError(stoneMaterial.shader),
                    Is.False);
                Assert.That(
                    ShaderUtil.ShaderHasError(stoneFragmentMaterial.shader),
                    Is.False);
                Assert.That(
                    MaterialTint(woodMaterial).r,
                    Is.GreaterThan(MaterialTint(stoneMaterial).r + 0.15f),
                    "Fresh wood dust must read warmer than stone dust.");
                Assert.That(
                    MaterialTint(woodMaterial).b,
                    Is.LessThan(MaterialTint(stoneMaterial).b - 0.25f));
                Assert.That(
                    MaterialTint(stoneMaterial).r,
                    Is.GreaterThan(MaterialTint(stoneMaterial).b + 0.04f),
                    "Stone dust must stay neutral-warm instead of blue.");
                Assert.That(
                    MaterialTint(stoneFragmentMaterial).r,
                    Is.GreaterThan(
                        MaterialTint(stoneFragmentMaterial).b + 0.03f),
                    "Stone chips must stay neutral-warm instead of blue.");
                Assert.That(
                    stoneMaterial.GetFloat("_FogInfluence"),
                    Is.Zero,
                    "Blue scene fog must not recolour stone dust.");
                Assert.That(
                    stoneFragmentMaterial.GetFloat("_FogInfluence"),
                    Is.Zero,
                    "Blue scene fog must not recolour stone chips.");
            }
            finally
            {
                if (stone != null)
                {
                    Object.DestroyImmediate(stone);
                }

                if (wood != null)
                {
                    Object.DestroyImmediate(wood);
                }
            }
        }

        private static bool HasValidMeshRenderer(
            ParticleSystemRenderer renderer)
        {
            return renderer != null &&
                renderer.renderMode == ParticleSystemRenderMode.Mesh &&
                renderer.mesh != null &&
                renderer.sharedMaterial != null;
        }

        private static object InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method '{methodName}'.");
            return method.Invoke(target, arguments);
        }

        [Test]
        public void AuthoredRockRepairUsesItsExactVisibleMesh()
        {
            var root = new GameObject("DEC_Rock_999");
            var identity =
                root.AddComponent<ManualMiningSourceIdentity>();
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "GEO_Rock";
            visual.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            var legacyProxy = new GameObject(
                ManualMiningSourceIdentity.MiningHitProxyName);
            legacyProxy.transform.SetParent(root.transform, false);
            legacyProxy.AddComponent<BoxCollider>().isTrigger = true;

            try
            {
                Assert.That(
                    identity.TryConfigureFromAuthoredRockName(),
                    Is.True);
                var collider = identity.EnsureMiningMeshColliders();
                var filter = visual.GetComponent<MeshFilter>();

                Assert.That(
                    identity.SourceId,
                    Is.EqualTo("starter-island.environment-rock.999"));
                Assert.That(
                    root.transform.Find(
                        ManualMiningSourceIdentity.MiningHitProxyName),
                    Is.Null);
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.gameObject, Is.SameAs(visual));
                Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
                Assert.That(collider.enabled, Is.True);
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(collider.convex, Is.False);
                Assert.That(
                    root.GetComponentsInChildren<BoxCollider>(true),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MiningMeshColliderRepairRemovesOwnedExtrasOnly()
        {
            var root = new GameObject("DEC_Rock_998");
            var identity =
                root.AddComponent<ManualMiningSourceIdentity>();
            identity.Configure(
                ManualMiningSourceKind.EnvironmentalStone,
                "test.environment-rock.998");

            var wrongMesh = new Mesh { name = "WrongColliderMesh" };
            root.AddComponent<BoxCollider>();
            root.AddComponent<MeshCollider>().sharedMesh = wrongMesh;

            var structuralGroup = new GameObject("RockGeometryRoot");
            structuralGroup.transform.SetParent(root.transform, false);
            structuralGroup.AddComponent<BoxCollider>();
            structuralGroup.AddComponent<MeshCollider>().sharedMesh =
                wrongMesh;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "GEO_Rock";
            visual.transform.SetParent(
                structuralGroup.transform,
                false);
            var visualFilter = visual.GetComponent<MeshFilter>();
            var firstExact = visual.AddComponent<MeshCollider>();
            firstExact.sharedMesh = visualFilter.sharedMesh;
            var duplicateExact = visual.AddComponent<MeshCollider>();
            duplicateExact.sharedMesh = visualFilter.sharedMesh;
            var wrongCollider = visual.AddComponent<MeshCollider>();
            wrongCollider.sharedMesh = wrongMesh;

            var external = new GameObject("InteractionVolume");
            external.transform.SetParent(root.transform, false);
            var externalBox = external.AddComponent<BoxCollider>();
            var externalMesh = external.AddComponent<MeshCollider>();
            externalMesh.sharedMesh = wrongMesh;

            var nested = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nested.name = "NestedMiningSource";
            nested.transform.SetParent(root.transform, false);
            var nestedBox = nested.GetComponent<BoxCollider>();
            var nestedIdentity =
                nested.AddComponent<ManualMiningSourceIdentity>();
            nestedIdentity.Configure(
                ManualMiningSourceKind.EnvironmentalStone,
                "test.environment-rock.nested");

            var physicsChild = new GameObject("ExternalPhysics");
            physicsChild.transform.SetParent(root.transform, false);
            var body = physicsChild.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var physicsVisual =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            physicsVisual.transform.SetParent(
                physicsChild.transform,
                false);
            var physicsBox = physicsVisual.GetComponent<BoxCollider>();

            try
            {
                var firstResult = identity.EnsureMiningMeshColliders();
                var secondResult = identity.EnsureMiningMeshColliders();

                Assert.That(firstResult, Is.SameAs(firstExact));
                Assert.That(secondResult, Is.SameAs(firstExact));
                Assert.That(
                    visual.GetComponents<MeshCollider>(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    visual.GetComponent<MeshCollider>().sharedMesh,
                    Is.SameAs(visualFilter.sharedMesh));
                Assert.That(
                    visual.GetComponents<BoxCollider>(),
                    Is.Empty);
                Assert.That(
                    root.GetComponents<MeshCollider>(),
                    Is.Empty);
                Assert.That(
                    root.GetComponents<BoxCollider>(),
                    Is.Empty);
                Assert.That(
                    structuralGroup.GetComponents<MeshCollider>(),
                    Is.Empty);
                Assert.That(
                    structuralGroup.GetComponents<BoxCollider>(),
                    Is.Empty);
                Assert.That(duplicateExact == null, Is.True);
                Assert.That(wrongCollider == null, Is.True);

                Assert.That(
                    external.GetComponent<BoxCollider>(),
                    Is.SameAs(externalBox));
                Assert.That(
                    external.GetComponent<MeshCollider>(),
                    Is.SameAs(externalMesh));
                Assert.That(
                    nested.GetComponent<BoxCollider>(),
                    Is.SameAs(nestedBox));
                Assert.That(
                    nested.GetComponents<MeshCollider>(),
                    Is.Empty);
                Assert.That(
                    physicsVisual.GetComponent<BoxCollider>(),
                    Is.SameAs(physicsBox));
                Assert.That(
                    physicsVisual.GetComponents<MeshCollider>(),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(wrongMesh);
            }
        }

        [Test]
        public void SphereAssistCannotSelectSourceBehindCloserSolid()
        {
            const int testLayer = 30;
            const int testMask = 1 << testLayer;
            var equipment = new GameObject("EquipmentMotion");
            equipment.layer = testLayer;
            var motion =
                equipment.AddComponent<FirstPersonEquipmentMotion>();
            SetPrivateField(motion, "strikeLayers", (LayerMask)testMask);
            SetPrivateField(motion, "strikeAssistRadius", 0.055f);
            SetPrivateField(motion, "maximumStrikeDistance", 4.5f);

            var sourceObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourceObject.name = "AssistedMiningSource";
            sourceObject.layer = testLayer;
            sourceObject.transform.position =
                new Vector3(0.05f, 0f, 2f);
            sourceObject.transform.localScale =
                new Vector3(0.02f, 0.3f, 0.02f);
            Object.DestroyImmediate(
                sourceObject.GetComponent<BoxCollider>());
            var sourceFilter = sourceObject.GetComponent<MeshFilter>();
            var sourceCollider =
                sourceObject.AddComponent<MeshCollider>();
            sourceCollider.sharedMesh = sourceFilter.sharedMesh;
            var source =
                sourceObject.AddComponent<ManualMiningSourceIdentity>();
            source.Configure(
                ManualMiningSourceKind.EnvironmentalStone,
                "test.assisted-source");

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "CloserSolid";
            blocker.layer = testLayer;
            blocker.transform.position = new Vector3(0.05f, 0f, 1f);
            blocker.transform.localScale =
                new Vector3(0.02f, 0.3f, 0.02f);

            try
            {
                Physics.SyncTransforms();
                var centerRay = new Ray(
                    equipment.transform.position,
                    equipment.transform.forward);
                Assert.That(
                    Physics.Raycast(
                        centerRay,
                        4.5f,
                        testMask,
                        QueryTriggerInteraction.Collide),
                    Is.False,
                    "The fixture must exercise sphere assist, not the ray.");

                InvokeResolveStrikeTarget(motion);
                Assert.That(
                    GetSelectedMiningSource(motion),
                    Is.Null,
                    "The nearest assisted solid must occlude the source.");

                blocker.SetActive(false);
                Physics.SyncTransforms();
                InvokeResolveStrikeTarget(motion);
                Assert.That(
                    GetSelectedMiningSource(motion),
                    Is.SameAs(source),
                    "The same off-axis source should be selectable when clear.");
            }
            finally
            {
                Object.DestroyImmediate(blocker);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(equipment);
            }
        }

        [Test]
        public void EmbeddedToleranceDoesNotBypassNonTerrainSolid()
        {
            const int testLayer = 30;
            const int testMask = 1 << testLayer;
            var equipment = new GameObject("EquipmentMotion");
            var motion =
                equipment.AddComponent<FirstPersonEquipmentMotion>();
            SetPrivateField(motion, "strikeLayers", (LayerMask)testMask);
            SetPrivateField(motion, "strikeAssistRadius", 0.055f);
            SetPrivateField(motion, "embeddedSourceTolerance", 0.06f);
            SetPrivateField(motion, "maximumStrikeDistance", 4.5f);

            var sourceObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourceObject.name = "NearlyEmbeddedMiningSource";
            sourceObject.layer = testLayer;
            sourceObject.transform.position =
                new Vector3(0.06f, 0f, 1.05f);
            sourceObject.transform.localScale =
                new Vector3(0.02f, 0.3f, 0.02f);
            Object.DestroyImmediate(
                sourceObject.GetComponent<BoxCollider>());
            var sourceFilter = sourceObject.GetComponent<MeshFilter>();
            var sourceCollider =
                sourceObject.AddComponent<MeshCollider>();
            sourceCollider.sharedMesh = sourceFilter.sharedMesh;
            var source =
                sourceObject.AddComponent<ManualMiningSourceIdentity>();
            source.Configure(
                ManualMiningSourceKind.EnvironmentalStone,
                "test.nearly-embedded-source");

            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "NonTerrainSurface";
            blocker.layer = testLayer;
            blocker.transform.position = new Vector3(0f, 0f, 1f);
            blocker.transform.localScale =
                new Vector3(0.1f, 0.3f, 0.02f);

            try
            {
                Physics.SyncTransforms();
                Assert.That(
                    Physics.Raycast(
                        new Ray(Vector3.zero, Vector3.forward),
                        4.5f,
                        testMask,
                        QueryTriggerInteraction.Collide),
                    Is.True,
                    "The solid must be authoritative on the center ray.");

                InvokeResolveStrikeTarget(motion);
                Assert.That(
                    GetSelectedMiningSource(motion),
                    Is.Null,
                    "Embedded tolerance must not pass through a wall.");

                blocker.SetActive(false);
                Physics.SyncTransforms();
                InvokeResolveStrikeTarget(motion);
                Assert.That(
                    GetSelectedMiningSource(motion),
                    Is.SameAs(source));
            }
            finally
            {
                Object.DestroyImmediate(blocker);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(equipment);
            }
        }

        [Test]
        public void SwingClearanceIgnoresOnlyTheFrozenStrikeCollider()
        {
            var player = new GameObject("ClearancePlayer");
            var swing = new GameObject("SwingRoot");
            swing.transform.SetParent(player.transform, false);
            var equipment = new GameObject("PickaxeCollision");
            equipment.transform.SetParent(swing.transform, false);
            var collision =
                equipment.AddComponent<FirstPersonEquipmentCollision>();
            collision.Configure(swing.transform, null);

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "FrozenStrikeTarget";
            target.transform.position = new Vector3(0f, 0.24f, 0f);
            target.transform.localScale = new Vector3(0.20f, 0.8f, 0.20f);
            var targetCollider = target.GetComponent<Collider>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "IndependentWall";
            wall.transform.position = target.transform.position;
            wall.transform.localScale = target.transform.localScale;
            wall.SetActive(false);

            try
            {
                Physics.SyncTransforms();
                Assert.That(
                    collision.FindRequiredRetraction(
                        Vector3.zero,
                        Quaternion.identity),
                    Is.GreaterThan(0f),
                    "The intended contact blocks ordinary idle clearance.");
                Assert.That(
                    collision.FindRequiredRetraction(
                        Vector3.zero,
                        Quaternion.identity,
                        targetCollider),
                    Is.EqualTo(0f).Within(0.0001f),
                    "A swing must not snap away from its frozen target.");

                wall.SetActive(true);
                Physics.SyncTransforms();
                Assert.That(
                    collision.FindRequiredRetraction(
                        Vector3.zero,
                        Quaternion.identity,
                        targetCollider),
                    Is.GreaterThan(0f),
                    "Ignoring the target must not bypass another obstacle.");
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TreeOpeningUsesEnlargedFootprintAtEveryStage()
        {
            const float sectionWidth = 0.30f;
            var previousWidth = 0f;
            var previousHeight = 0f;
            for (var stage = 1;
                 stage <= FellableTreeIdentity.HitsRequired;
                 stage++)
            {
                var carverType = typeof(FellableTreeIdentity).Assembly
                    .GetType("CML.Unity.Wood.TreeChopVoxelCarver");
                Assert.That(carverType, Is.Not.Null);
                var resolver = carverType.GetMethod(
                    "ResolveOpeningSize",
                    BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.Public);
                Assert.That(resolver, Is.Not.Null);
                var arguments = new object[]
                {
                    sectionWidth,
                    stage,
                    0f,
                    0f,
                    0f
                };
                resolver.Invoke(null, arguments);
                var width = (float)arguments[2];
                var height = (float)arguments[3];
                var progress = (stage - 1f) /
                    (FellableTreeIdentity.HitsRequired - 1f);
                Assert.That(
                    width,
                    Is.EqualTo(
                        0.20f * Mathf.Lerp(0.55f, 1f, progress) *
                        1.20f).Within(0.0001f));
                Assert.That(
                    height,
                    Is.EqualTo(
                        0.28f * Mathf.Lerp(0.52f, 1f, progress) *
                        1.20f).Within(0.0001f));
                Assert.That(width, Is.GreaterThan(previousWidth));
                Assert.That(height, Is.GreaterThan(previousHeight));
                previousWidth = width;
                previousHeight = height;
            }
        }

        [Test]
        public void TreeHitDeformsVisibleTrunkAndExactColliderWithoutOverlay()
        {
            var tree = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tree.name = "DEC_Tree_Test";
            var primitiveCollider = tree.GetComponent<Collider>();
            Object.DestroyImmediate(primitiveCollider);
            var filter = tree.GetComponent<MeshFilter>();
            var originalMesh = filter.sharedMesh;
            var trunkCollider = tree.AddComponent<MeshCollider>();
            trunkCollider.sharedMesh = originalMesh;
            trunkCollider.convex = false;
            var identity = tree.AddComponent<FellableTreeIdentity>();
            identity.Configure("test.tree.damage-conformance");

            try
            {
                Physics.SyncTransforms();
                var ray = new Ray(
                    new Vector3(0f, 0.1f, -2f),
                    Vector3.forward);
                Assert.That(
                    trunkCollider.Raycast(ray, out var hit, 4f),
                    Is.True);
                Assert.That(
                    identity.RegisterImpact(
                        hit,
                        hit.point - ray.origin),
                    Is.True);

                var deformedMesh = filter.sharedMesh;
                Assert.That(deformedMesh, Is.Not.Null);
                Assert.That(
                    deformedMesh,
                    Is.Not.SameAs(originalMesh));
                Assert.That(
                    deformedMesh.name,
                    Does.StartWith("MESH_WOOD_VoxelCarvedTrunk_"));
                Assert.That(
                    deformedMesh.vertexCount,
                    Is.GreaterThan(originalMesh.vertexCount),
                    "The impacted surface must be refined before carving.");
                Assert.That(
                    deformedMesh.triangles.Length / 3,
                    Is.LessThanOrEqualTo(24000),
                    "A local voxel cut must stay inside its runtime budget.");
                Assert.That(
                    deformedMesh.vertices.All(vertex =>
                        !float.IsNaN(vertex.x) &&
                        !float.IsNaN(vertex.y) &&
                        !float.IsNaN(vertex.z) &&
                        !float.IsInfinity(vertex.x) &&
                        !float.IsInfinity(vertex.y) &&
                        !float.IsInfinity(vertex.z)),
                    Is.True,
                    "Voxel reconstruction must not emit invalid geometry.");
                Assert.That(
                    trunkCollider.sharedMesh,
                    Is.SameAs(deformedMesh),
                    "Rendering and hit physics must use the same carved mesh.");
                Assert.That(
                    tree.GetComponentsInChildren<MeshRenderer>(true).Length,
                    Is.EqualTo(1),
                    "A tree hit must not spawn an overlay renderer.");
                Assert.That(
                    tree.GetComponentsInChildren<MeshFilter>(true).Length,
                    Is.EqualTo(1),
                    "A tree hit must deform the trunk, not add a wound mesh.");
                Assert.That(
                    tree.GetComponentsInChildren<Collider>(true).Length,
                    Is.EqualTo(1));

                Physics.SyncTransforms();
                Assert.That(
                    trunkCollider.Raycast(ray, out var carvedHit, 4f),
                    Is.True);
                Assert.That(
                    Vector3.Dot(
                        carvedHit.point - hit.point,
                        hit.normal.normalized),
                    Is.LessThan(-0.003f),
                    "The collider surface must recede with the visible cut.");

                var renderer = tree.GetComponent<MeshRenderer>();
                Assert.That(
                    renderer.HasPropertyBlock(),
                    Is.False,
                    "A hit must not change the whole renderer's lighting " +
                    "path with a MaterialPropertyBlock.");
                var generatedBounds =
                    GetGeneratedSurfaceBounds(deformedMesh);
                Assert.That(
                    generatedBounds.size.y,
                    Is.GreaterThan(generatedBounds.size.x * 1.2f),
                    "The physical scrape must be vertical, like torn birch bark.");
                Assert.That(
                    generatedBounds.size.z,
                    Is.GreaterThan(0.003f));
                Assert.That(
                    deformedMesh.bounds.center,
                    Is.EqualTo(originalMesh.bounds.center),
                    "The authored probe-sampling centre must stay stable.");
            }
            finally
            {
                Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void FiveTreeHitsProgressOneCarvedMeshAndRemainRaycastable()
        {
            var tree = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tree.name = "DEC_Tree_Progression_Test";
            Object.DestroyImmediate(tree.GetComponent<Collider>());
            var filter = tree.GetComponent<MeshFilter>();
            var collider = tree.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            var identity = tree.AddComponent<FellableTreeIdentity>();
            identity.Configure("test.tree.damage-progression");
            var ray = new Ray(
                new Vector3(0f, 0.1f, -2f),
                Vector3.forward);

            try
            {
                var previousWidth = 0f;
                var previousDepth = 0f;
                var fourthHitPoint = Vector3.zero;
                var fourthHitNormal = Vector3.zero;
                var fourthStrikeDirection = Vector3.zero;
                for (var hitNumber = 1;
                     hitNumber <= FellableTreeIdentity.HitsRequired;
                     hitNumber++)
                {
                    Physics.SyncTransforms();
                    Assert.That(
                        collider.Raycast(ray, out var hit, 4f),
                        Is.True,
                        $"Deformed collider missed hit {hitNumber}.");
                    Assert.That(
                        identity.RegisterImpact(
                            hit,
                            hit.point - ray.origin),
                        Is.True);
                    Assert.That(identity.HitCount, Is.EqualTo(hitNumber));
                    Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
                    Assert.That(
                        filter.sharedMesh.triangles.Length / 3,
                        Is.LessThanOrEqualTo(24000),
                        $"Hit {hitNumber} exceeded the runtime mesh budget.");

                    var generatedBounds =
                        GetGeneratedSurfaceBounds(filter.sharedMesh);
                    Assert.That(
                        generatedBounds.size.x + 0.005f,
                        Is.GreaterThanOrEqualTo(previousWidth));
                    Assert.That(
                        generatedBounds.size.z + 0.005f,
                        Is.GreaterThanOrEqualTo(previousDepth));
                    previousWidth = generatedBounds.size.x;
                    previousDepth = generatedBounds.size.z;
                    if (hitNumber == 4)
                    {
                        fourthHitPoint = identity.FinalHitPoint;
                        fourthHitNormal = identity.FinalHitNormal;
                        fourthStrikeDirection =
                            identity.FinalStrikeDirection;
                    }
                }

                Assert.That(identity.IsReadyForFelling, Is.True);
                Assert.That(
                    tree.GetComponentsInChildren<MeshRenderer>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    tree.GetComponentsInChildren<Collider>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    tree.GetComponent<MeshRenderer>().HasPropertyBlock(),
                    Is.False,
                    "Voxel data must remain per-vertex across progression.");

                identity.RollBackFinalImpact();
                Assert.That(identity.HitCount, Is.EqualTo(4));
                Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
                Assert.That(identity.FinalHitPoint, Is.EqualTo(fourthHitPoint));
                Assert.That(identity.FinalHitNormal, Is.EqualTo(fourthHitNormal));
                Assert.That(
                    identity.FinalStrikeDirection,
                    Is.EqualTo(fourthStrikeDirection));
            }
            finally
            {
                Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void TreeHitInsideOneLargeTriangleStillRefinesAndRecedes()
        {
            var tree = new GameObject("DEC_Tree_LargeTriangle_Test");
            var authoredMesh = new Mesh
            {
                name = "MESH_LargeTrunkTriangle",
                vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(1f, -1f, 0f),
                    new Vector3(0f, 1f, 0f)
                },
                uv = new[]
                {
                    Vector2.zero,
                    Vector2.right,
                    Vector2.up
                },
                triangles = new[] { 0, 2, 1 }
            };
            authoredMesh.RecalculateNormals();
            authoredMesh.RecalculateBounds();
            var filter = tree.AddComponent<MeshFilter>();
            filter.sharedMesh = authoredMesh;
            tree.AddComponent<MeshRenderer>();
            var collider = tree.AddComponent<MeshCollider>();
            collider.sharedMesh = authoredMesh;
            collider.convex = false;
            var identity = tree.AddComponent<FellableTreeIdentity>();
            identity.Configure("test.tree.triangle-interior");
            var ray = new Ray(
                new Vector3(0f, 0f, -1f),
                Vector3.forward);

            try
            {
                Physics.SyncTransforms();
                Assert.That(collider.Raycast(ray, out var hit, 2f), Is.True);
                Assert.That(
                    identity.RegisterImpact(
                        hit,
                        hit.point - ray.origin),
                    Is.True);
                Assert.That(
                    filter.sharedMesh.vertexCount,
                    Is.GreaterThan(authoredMesh.vertexCount),
                    "A hit contained by one triangle must force refinement.");
                Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));

                Physics.SyncTransforms();
                Assert.That(
                    collider.Raycast(ray, out var carvedHit, 2f),
                    Is.True);
                Assert.That(
                    Vector3.Dot(
                        carvedHit.point - hit.point,
                        hit.normal.normalized),
                    Is.LessThanOrEqualTo(-0.003f));
            }
            finally
            {
                Object.DestroyImmediate(tree);
                Object.DestroyImmediate(authoredMesh);
            }
        }

        private static void InvokeResolveStrikeTarget(
            FirstPersonEquipmentMotion motion)
        {
            var method = typeof(FirstPersonEquipmentMotion).GetMethod(
                "ResolveStrikeTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(motion, null);
        }

        private static Bounds GetGeneratedSurfaceBounds(Mesh mesh)
        {
            var chopData = new List<Vector4>(mesh.vertexCount);
            mesh.GetUVs(1, chopData);
            var vertices = mesh.vertices;
            var found = false;
            var bounds = default(Bounds);
            for (var index = 0; index < vertices.Length; index++)
            {
                if (index >= chopData.Count || chopData[index].w < 16.5f)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = new Bounds(vertices[index], Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(vertices[index]);
                }
            }

            Assert.That(
                found,
                Is.True,
                "The runtime mesh must encode fresh wood with the signed " +
                "UV marker that authored foliage cannot produce.");
            return bounds;
        }

        private static ManualMiningSourceIdentity GetSelectedMiningSource(
            FirstPersonEquipmentMotion motion)
        {
            var field = typeof(FirstPersonEquipmentMotion).GetField(
                "_swingMiningSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (ManualMiningSourceIdentity)field.GetValue(motion);
        }

        private static void SetPrivateField<T>(
            FirstPersonEquipmentMotion motion,
            string fieldName,
            T value)
        {
            var field = typeof(FirstPersonEquipmentMotion).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(motion, value);
        }

        private static Color MaterialTint(Material material)
        {
            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }

            return material.HasProperty("_Color")
                ? material.GetColor("_Color")
                : Color.white;
        }
    }
}

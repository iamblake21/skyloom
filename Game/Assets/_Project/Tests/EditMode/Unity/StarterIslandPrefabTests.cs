using System;
using System.Collections.Generic;
using CML.Unity.Bootstrap;
using CML.Unity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CML.Tests.Unity
{
    public sealed class StarterIslandPrefabTests
    {
        private const string Root =
            "Assets/_Project/Art/Environment/StarterIsland";
        private const string ModelPath =
            Root + "/Models/ENV_StarterIsland.fbx";
        private const string PrefabPath =
            Root + "/Prefabs/PF_StarterIsland.prefab";
        private const string ReviewScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Review.unity";
        private const string FoliageRoot =
            Root + "/Foliage";
        private const string FoliagePrefabsRoot =
            FoliageRoot + "/Prefabs";
        private const string IslandMeshName = "GEO_IslandMass";

        private static readonly string[] ExpectedFoliagePrefabs =
        {
            "PF_Tree_CommonTall_A",
            "PF_Tree_CommonBroad_B",
            "PF_Tree_CommonYoung_C",
            "PF_Shrub_Round_A",
            "PF_Shrub_Low_B",
            "PF_Grass_Clump_A",
            "PF_Grass_Clump_B",
            "PF_Flower_White_A",
            "PF_Flower_Orange_B"
        };

        private static readonly string[] RequiredMarkers =
        {
            "REF_PlayerSpawn",
            "REF_AirshipDock",
            "REF_TutorialCenter",
            "REF_FactoryCenter",
            "REF_FactoryCorner_SW",
            "REF_FactoryCorner_SE",
            "REF_FactoryCorner_NW",
            "REF_FactoryCorner_NE",
            "REF_AgricultureCenter",
            "REF_PortalAnchor",
            "REF_SpringSource",
            "REF_PondCenter",
            "REF_WaterfallLip",
            "REF_DepositAnchor_Stone",
            "REF_DepositAnchor_Iron",
            "REF_DepositAnchor_Copper",
            "REF_DepositAnchor_Clay"
        };

        private static readonly string[] DryTraversalMarkers =
        {
            "REF_PlayerSpawn",
            "REF_TutorialCenter",
            "REF_FactoryCenter",
            "REF_FactoryCorner_SW",
            "REF_FactoryCorner_SE",
            "REF_FactoryCorner_NW",
            "REF_FactoryCorner_NE",
            "REF_AgricultureCenter",
            "REF_PortalAnchor",
            "REF_DepositAnchor_Stone",
            "REF_DepositAnchor_Iron",
            "REF_DepositAnchor_Copper",
            "REF_DepositAnchor_Clay"
        };

        [Test]
        public void ImporterUsesRealMetresAndNeverGeneratesCollision()
        {
            RequireSourceModel();
            var importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.useFileScale, Is.True);
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.sortHierarchyByName, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(
                importer.importNormals,
                Is.EqualTo(ModelImporterNormals.Import));
            Assert.That(
                importer.importTangents,
                Is.EqualTo(ModelImporterTangents.CalculateMikk));
            Assert.That(importer.generateSecondaryUV, Is.False);
            Assert.That(importer.isReadable, Is.False);
        }

        [Test]
        public void PrefabHasOneVisibleMeshColliderAndNoSecondPhysicsSystem()
        {
            var prefab = RequireBuiltPrefab();
            AssertIdentity(prefab.transform, PrefabPath);

            var island = FindRecursive(
                prefab.transform,
                IslandMeshName);
            Assert.That(island, Is.Not.Null);
            AssertUnitScaleChain(island, prefab.transform);

            var filter = island.GetComponent<MeshFilter>();
            var renderer = island.GetComponent<MeshRenderer>();
            var collider = island.GetComponent<MeshCollider>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(6));
            Assert.That(
                filter.sharedMesh.HasVertexAttribute(
                    VertexAttribute.Color),
                Is.True,
                "Terrain mesh lost its authored Color vertex attribute.");

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Joint>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<MonoBehaviour>(true),
                Is.Empty);
            Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
            Assert.That(collider.convex, Is.False);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(collider.enabled, Is.True);

            var bounds = renderer.bounds;
            Assert.That(
                bounds.size.x,
                Is.EqualTo(230f).Within(0.12f));
            Assert.That(
                bounds.size.z,
                Is.EqualTo(175f).Within(0.12f));
            Assert.That(bounds.size.y, Is.InRange(110f, 135f));
            Assert.That(bounds.max.y, Is.InRange(20f, 23f));
            Assert.That(bounds.min.y, Is.InRange(-112f, -90f));

            var flags =
                GameObjectUtility.GetStaticEditorFlags(island.gameObject);
            Assert.That(
                flags.HasFlag(StaticEditorFlags.OccluderStatic),
                Is.True);
            Assert.That(
                flags.HasFlag(StaticEditorFlags.OccludeeStatic),
                Is.True);
            Assert.That(
                flags.HasFlag(StaticEditorFlags.ReflectionProbeStatic),
                Is.True);
        }

        [Test]
        public void PrefabMaterialsMarkersAndNonCollidingWaterMatchContract()
        {
            var prefab = RequireBuiltPrefab();
            var island =
                FindRecursive(prefab.transform, IslandMeshName);
            var renderer = island.GetComponent<MeshRenderer>();
            Assert.That(
                MaterialNames(renderer),
                Is.EqualTo(new[]
                {
                    "M_StarterIsland_GrassSun",
                    "M_StarterIsland_GrassMid",
                    "M_StarterIsland_GrassDeep",
                    "M_StarterIsland_CliffWarm",
                    "M_StarterIsland_CliffMid",
                    "M_StarterIsland_CliffDeep"
                }));

            foreach (var marker in RequiredMarkers)
            {
                Assert.That(
                    CountTransforms(prefab.transform, marker),
                    Is.EqualTo(1),
                    $"Expected one exact marker '{marker}'.");
            }

            var waterCount = 0;
            var pathCount = 0;
            foreach (var childRenderer in
                     prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsWater(childRenderer.name))
                {
                    waterCount++;
                    Assert.That(
                        MaterialNames(childRenderer),
                        Is.EqualTo(new[]
                        {
                            "M_StarterIsland_WaterGuide"
                        }));
                    Assert.That(
                        childRenderer.GetComponents<Collider>(),
                        Is.Empty);
                    Assert.That(
                        childRenderer.shadowCastingMode,
                        Is.EqualTo(ShadowCastingMode.Off));
                }
                else if (childRenderer.name.StartsWith(
                             "GEO_Path_",
                             StringComparison.Ordinal))
                {
                    pathCount++;
                    Assert.That(
                        MaterialNames(childRenderer),
                        Is.EqualTo(new[]
                        {
                            "M_StarterIsland_Dirt"
                        }));
                    Assert.That(
                        childRenderer.GetComponents<Collider>(),
                        Is.Empty);
                }
            }

            Assert.That(waterCount, Is.GreaterThanOrEqualTo(5));
            Assert.That(pathCount, Is.EqualTo(3));

            AssertMaterialShader(
                "M_StarterIsland_GrassSun",
                "CML/Environment/Starter Island Stylized Surface");
            AssertMaterialShader(
                "M_StarterIsland_GrassMid",
                "CML/Environment/Starter Island Stylized Surface");
            AssertMaterialShader(
                "M_StarterIsland_GrassDeep",
                "CML/Environment/Starter Island Stylized Surface");
            AssertMaterialShader(
                "M_StarterIsland_Dirt",
                "CML/Environment/Starter Island Stylized Surface");
            AssertMaterialShader(
                "M_StarterIsland_WaterGuide",
                "CML/Environment/Starter Island Stylized Water");
        }

        [Test]
        public void OptionalFoliageKitUsesOneSharedMaterialAndNoPhysics()
        {
            var modelGuids = AssetDatabase.FindAssets(
                "t:GameObject",
                new[] { FoliageRoot + "/Models" });
            if (modelGuids.Length == 0)
            {
                Assert.Pass("Optional Starter Island foliage is absent.");
            }

            var firstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FoliagePrefabsRoot + "/" +
                ExpectedFoliagePrefabs[0] + ".prefab");
            if (firstPrefab == null)
            {
                Assert.Pass(
                    "Foliage sources exist; generated prefab checks activate " +
                    "after CML/Art/Rebuild Starter Island.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                FoliageRoot +
                "/Materials/M_StarterIsland_FoliageAtlas.mat");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo(
                    "CML/Environment/Starter Island Foliage"));

            foreach (var prefabName in ExpectedFoliagePrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    FoliagePrefabsRoot + "/" + prefabName + ".prefab");
                Assert.That(prefab, Is.Not.Null, prefabName);
                Assert.That(
                    prefab.GetComponentsInChildren<MeshFilter>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    prefab.GetComponentsInChildren<MeshRenderer>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    prefab.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    prefab.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    prefab.GetComponentsInChildren<MonoBehaviour>(true),
                    Is.Empty);

                var filter =
                    prefab.GetComponentInChildren<MeshFilter>(true);
                var renderer =
                    prefab.GetComponentInChildren<MeshRenderer>(true);
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(
                    filter.sharedMesh.HasVertexAttribute(
                        VertexAttribute.Color),
                    Is.True,
                    prefabName);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(material),
                    prefabName);
                Assert.That(
                    CountTransforms(
                        prefab.transform,
                        "REF_Placement"),
                    Is.EqualTo(1),
                    prefabName);

                var isTree = prefabName.StartsWith(
                    "PF_Tree_",
                    StringComparison.Ordinal);
                Assert.That(
                    CountTransforms(
                        prefab.transform,
                        "REF_ChopPoint"),
                    Is.EqualTo(isTree ? 1 : 0),
                    prefabName);
                Assert.That(
                    CountTransforms(
                        prefab.transform,
                        "REF_CanopyCenter"),
                    Is.EqualTo(isTree ? 1 : 0),
                    prefabName);
            }
        }

        [Test]
        public void AuthoredTraversalAnchorsHitTheOnlyIslandCollider()
        {
            var prefab = RequireBuiltPrefab();
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                Physics.SyncTransforms();

                var collider =
                    instance.GetComponentInChildren<MeshCollider>(true);
                var factoryHeights = new List<float>();
                foreach (var markerName in DryTraversalMarkers)
                {
                    var marker =
                        FindRecursive(instance.transform, markerName);
                    Assert.That(marker, Is.Not.Null);

                    var ray = new Ray(
                        marker.position + Vector3.up * 120f,
                        Vector3.down);
                    Assert.That(
                        collider.Raycast(ray, out var hit, 260f),
                        Is.True,
                        $"Marker '{markerName}' has no ground below it.");
                    Assert.That(hit.collider, Is.SameAs(collider));

                    if (markerName.StartsWith(
                            "REF_Factory",
                            StringComparison.Ordinal))
                    {
                        factoryHeights.Add(hit.point.y);
                    }
                }

                Assert.That(factoryHeights, Has.Count.EqualTo(5));
                Assert.That(
                    Maximum(factoryHeights) - Minimum(factoryHeights),
                    Is.LessThan(0.08f),
                    "The guaranteed factory rectangle is no longer flat.");

                var spawn =
                    FindRecursive(instance.transform, "REF_PlayerSpawn");
                var spawnRay = new Ray(
                    spawn.position + Vector3.up * 20f,
                    Vector3.down);
                Assert.That(
                    collider.Raycast(spawnRay, out var spawnHit, 80f),
                    Is.True);
                var radius = 0.3f;
                var bottom =
                    spawnHit.point + Vector3.up * (radius + 0.16f);
                var top = spawnHit.point +
                          Vector3.up * (1.8f - radius + 0.16f);
                Assert.That(
                    Physics.CheckCapsule(
                        bottom,
                        top,
                        radius,
                        Physics.AllLayers,
                        QueryTriggerInteraction.Ignore),
                    Is.False,
                    "The player spawn capsule overlaps invisible geometry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ReviewSceneUsesNormalUnityPlayerAndNativeIslandPhysics()
        {
            RequireSourceModel();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    ReviewScenePath) == null)
            {
                Assert.Fail(
                    "Starter Island source exists but review scene has not " +
                    "been generated. Run CML/Art/Rebuild Starter Island.");
            }

            var alreadyLoaded =
                SceneManager.GetSceneByPath(ReviewScenePath);
            var openedHere =
                !alreadyLoaded.IsValid() || !alreadyLoaded.isLoaded;
            var scene = openedHere
                ? EditorSceneManager.OpenScene(
                    ReviewScenePath,
                    OpenSceneMode.Additive)
                : alreadyLoaded;

            try
            {
                var island = FindInScene(scene, "PF_StarterIsland");
                var player = FindInScene(
                    scene,
                    "ENV_StarterIsland_ReviewPlayer");
                var reviewRoot = FindInScene(
                    scene,
                    "ENV_StarterIsland_Review");
                var foliageRoot = FindInScene(
                    scene,
                    "ENV_StarterIsland_Foliage");
                Assert.That(island, Is.Not.Null);
                Assert.That(player, Is.Not.Null);
                Assert.That(reviewRoot, Is.Not.Null);
                Assert.That(foliageRoot, Is.Not.Null);
                Assert.That(
                    island.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1));
                Assert.That(
                    island.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    player.GetComponent<CharacterController>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponent<StarterIslandReviewPlayer>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponent<Rigidbody>(),
                    Is.Null);
                Assert.That(
                    player.GetComponentInChildren<Camera>(true),
                    Is.Not.Null);

                var revision =
                    reviewRoot.GetComponent<GeneratedSceneRevision>();
                Assert.That(revision, Is.Not.Null);
                Assert.That(
                    revision.Matches(
                        "cml.environment.starter_island.review",
                        1),
                    Is.True);

                var builtFoliageCount = 0;
                foreach (var prefabName in ExpectedFoliagePrefabs)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(
                            FoliagePrefabsRoot + "/" +
                            prefabName + ".prefab") != null)
                    {
                        builtFoliageCount++;
                    }
                }

                if (builtFoliageCount == ExpectedFoliagePrefabs.Length)
                {
                    Assert.That(
                        foliageRoot.transform.childCount,
                        Is.GreaterThan(1),
                        "The complete foliage kit was not scattered.");
                    Assert.That(
                        foliageRoot.GetComponentsInChildren<Collider>(true),
                        Is.Empty);
                    Assert.That(
                        CountTransforms(
                            foliageRoot.transform,
                            "ENV_Foliage_SourceCount_09"),
                        Is.EqualTo(1));
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var behaviour in
                             root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        Assert.That(behaviour, Is.Not.Null);
                        Assert.That(
                            behaviour.GetType().Name,
                            Is.Not.EqualTo("AirshipObstacleIdentity"));
                        Assert.That(
                            behaviour.GetType().Name,
                            Is.Not.EqualTo("AirshipTechnicalScenario"));
                    }
                }
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            Assert.That(
                Array.Exists(
                    EditorBuildSettings.scenes,
                    entry =>
                        entry.enabled &&
                        string.Equals(
                            entry.path,
                            ReviewScenePath,
                            StringComparison.Ordinal)),
                Is.True,
                "Review scene must be loadable by PlayMode validation.");
        }

        private static GameObject RequireBuiltPrefab()
        {
            RequireSourceModel();
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Assert.Fail(
                    "Starter Island source exists but production prefab has " +
                    "not been generated. Run CML/Art/Rebuild Starter Island.");
            }

            return prefab;
        }

        private static void RequireSourceModel()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                Assert.Pass(
                    "Starter Island FBX has not been generated yet; Unity " +
                    "pipeline tests activate after Blender export.");
            }
        }

        private static void AssertMaterialShader(
            string materialName,
            string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                Root + "/Materials/" + materialName + ".mat");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(shaderName));
        }

        private static string[] MaterialNames(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            var result = new string[materials.Length];
            for (var index = 0; index < materials.Length; index++)
            {
                result[index] =
                    materials[index] != null
                        ? materials[index].name
                        : string.Empty;
            }

            return result;
        }

        private static void AssertIdentity(
            Transform transform,
            string context)
        {
            Assert.That(
                transform.localPosition,
                Is.EqualTo(Vector3.zero),
                context);
            Assert.That(
                transform.localRotation,
                Is.EqualTo(Quaternion.identity),
                context);
            Assert.That(
                transform.localScale,
                Is.EqualTo(Vector3.one),
                context);
        }

        private static void AssertUnitScaleChain(
            Transform leaf,
            Transform expectedRoot)
        {
            for (var current = leaf;
                 current != null;
                 current = current.parent)
            {
                Assert.That(
                    current.localScale,
                    Is.EqualTo(Vector3.one),
                    $"Collider ancestor '{current.name}' has non-unit scale.");
                if (current == expectedRoot)
                {
                    return;
                }
            }

            Assert.Fail("Island collider is outside the prefab hierarchy.");
        }

        private static bool IsWater(string objectName)
        {
            return objectName.StartsWith(
                       "GEO_Water_",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       objectName,
                       "GEO_Waterfall",
                       StringComparison.Ordinal);
        }

        private static int CountTransforms(
            Transform root,
            string name)
        {
            var count = string.Equals(
                root.name,
                name,
                StringComparison.Ordinal)
                ? 1
                : 0;
            for (var index = 0; index < root.childCount; index++)
            {
                count += CountTransforms(root.GetChild(index), name);
            }

            return count;
        }

        private static Transform FindRecursive(
            Transform root,
            string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindRecursive(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindInScene(
            Scene scene,
            string name)
        {
            GameObject result = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(
                            transform.name,
                            name,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (result != null)
                    {
                        return null;
                    }

                    result = transform.gameObject;
                }
            }

            return result;
        }

        private static float Maximum(IReadOnlyList<float> values)
        {
            var result = float.NegativeInfinity;
            for (var index = 0; index < values.Count; index++)
            {
                result = Mathf.Max(result, values[index]);
            }

            return result;
        }

        private static float Minimum(IReadOnlyList<float> values)
        {
            var result = float.PositiveInfinity;
            for (var index = 0; index < values.Count; index++)
            {
                result = Mathf.Min(result, values[index]);
            }

            return result;
        }
    }
}

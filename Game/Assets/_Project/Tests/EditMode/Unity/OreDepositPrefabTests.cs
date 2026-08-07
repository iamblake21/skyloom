using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class OreDepositPrefabTests
    {
        private const string Root = "Assets/_Project/Art/Environment/OreDeposit";
        private const string ModelsRoot = Root + "/Models";
        private const string ModulePrefabsRoot = Root + "/Prefabs/Modules";
        private const string DepositPrefabsRoot = Root + "/Prefabs";

        private static readonly string[] ModuleSuffixes =
        {
            "L01_LandmarkWedge",
            "M01_TallWedge",
            "M02_BroadSteps",
            "M03_TwinSpire",
            "S01_LowBlade",
            "S02_FracturedBlock",
            "S03_RockPair",
            "S04_OreShelf",
            "S05_CompactNode",
            "G01_LongPlate",
            "G02_RoundPlate",
            "G03_SplitPlate",
            "R01_LargeRubble",
            "R02_SmallRubble"
        };

        private static readonly string[] Ores = { "Iron", "Copper", "Tin" };
        private static readonly string[] Layouts = { "A", "B", "C" };
        private static readonly Dictionary<string, int> ExpectedTriangles =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "L01_LandmarkWedge", 722 },
                { "M01_TallWedge", 360 },
                { "M02_BroadSteps", 432 },
                { "M03_TwinSpire", 386 },
                { "S01_LowBlade", 276 },
                { "S02_FracturedBlock", 276 },
                { "S03_RockPair", 350 },
                { "S04_OreShelf", 276 },
                { "S05_CompactNode", 188 },
                { "G01_LongPlate", 402 },
                { "G02_RoundPlate", 324 },
                { "G03_SplitPlate", 282 },
                { "R01_LargeRubble", 324 },
                { "R02_SmallRubble", 240 }
            };

        private static readonly Dictionary<string, Vector3> ExpectedBounds =
            new Dictionary<string, Vector3>(StringComparer.Ordinal)
            {
                { "L01_LandmarkWedge", new Vector3(2.33f, 2.93f, 1.58f) },
                { "M01_TallWedge", new Vector3(1.31f, 1.79f, 1.06f) },
                { "M02_BroadSteps", new Vector3(1.97f, 1.20f, 1.17f) },
                { "M03_TwinSpire", new Vector3(1.56f, 1.49f, 1.07f) },
                { "S01_LowBlade", new Vector3(1.17f, 0.94f, 0.70f) },
                { "S02_FracturedBlock", new Vector3(1.18f, 0.80f, 0.85f) },
                { "S03_RockPair", new Vector3(1.37f, 0.74f, 0.74f) },
                { "S04_OreShelf", new Vector3(1.19f, 0.59f, 0.75f) },
                { "S05_CompactNode", new Vector3(0.64f, 0.63f, 0.55f) },
                { "G01_LongPlate", new Vector3(1.97f, 0.28f, 0.88f) },
                { "G02_RoundPlate", new Vector3(1.31f, 0.25f, 0.90f) },
                { "G03_SplitPlate", new Vector3(1.70f, 0.23f, 0.64f) },
                { "R01_LargeRubble", new Vector3(0.98f, 0.29f, 0.60f) },
                { "R02_SmallRubble", new Vector3(0.66f, 0.23f, 0.37f) }
            };

        [Test]
        public void ModulePrefabsMatchAuthoredContract()
        {
            foreach (var suffix in ModuleSuffixes)
            {
                var path = ModulePrefabsRoot + "/PF_OreModule_" + suffix + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, $"Missing module prefab {path}.");

                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    AssertIdentity(instance.transform, path);
                    Assert.That(instance.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(1));
                    Assert.That(instance.GetComponentsInChildren<MeshFilter>(true), Has.Length.EqualTo(1));
                    Assert.That(instance.GetComponentsInChildren<MeshCollider>(true), Has.Length.EqualTo(1));
                    Assert.That(instance.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                    Assert.That(instance.GetComponentsInChildren<Terrain>(true), Is.Empty);
                    Assert.That(instance.GetComponentsInChildren<Camera>(true), Is.Empty);
                    Assert.That(instance.GetComponentsInChildren<Light>(true), Is.Empty);
                    Assert.That(instance.GetComponentsInChildren<Animation>(true), Is.Empty);
                    Assert.That(instance.GetComponentsInChildren<Animator>(true), Is.Empty);

                    foreach (var markerName in new[]
                             {
                                 "REF_Placement",
                                 "REF_Hit",
                                 "REF_Drop"
                             })
                    {
                        Assert.That(
                            CountTransforms(instance.transform, markerName),
                            Is.EqualTo(1),
                            $"{path} must contain one exact {markerName} marker.");
                    }

                    var renderer = instance.GetComponentInChildren<MeshRenderer>(true);
                    var filter = instance.GetComponentInChildren<MeshFilter>(true);
                    var collider = instance.GetComponentInChildren<MeshCollider>(true);
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(2));
                    long triangleCount = 0;
                    for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                    {
                        triangleCount += filter.sharedMesh.GetIndexCount(subMesh) / 3L;
                    }

                    Assert.That(
                        triangleCount,
                        Is.EqualTo(ExpectedTriangles[suffix]),
                        $"{path} triangle count changed.");

                    var actualBounds = renderer.bounds.size;
                    var expectedBounds = ExpectedBounds[suffix];
                    Assert.That(
                        Vector3.Distance(actualBounds, expectedBounds),
                        Is.LessThan(0.035f),
                        $"{path} bounds {actualBounds} differ from {expectedBounds}.");
                    Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(2));
                    Assert.That(renderer.sharedMaterials[0].name, Is.EqualTo("M_OreDeposit_Rock"));
                    Assert.That(renderer.sharedMaterials[1].name, Is.EqualTo("M_OreDeposit_Iron"));
                    Assert.That(
                        renderer.sharedMaterials[0].shader.name,
                        Is.EqualTo("CML/Environment/Starter Island Stylized Surface"),
                        $"{path} must render with the island stone shader.");
                    Assert.That(
                        renderer.sharedMaterials[0].GetFloat("_RockContactBlend"),
                        Is.GreaterThan(0f),
                        $"{path} must blend into the terrain it stands on.");
                    Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
                    Assert.That(collider.convex, Is.False);
                    Assert.That(collider.isTrigger, Is.False);
                    Assert.That(collider.enabled, Is.True);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void DepositPrefabsMatchProductionContract()
        {
            foreach (var ore in Ores)
            {
                foreach (var layout in Layouts)
                {
                    var path = DepositPath(ore, layout);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Assert.That(prefab, Is.Not.Null, $"Missing deposit prefab {path}.");

                    var instance = UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        AssertIdentity(instance.transform, path);
                        Assert.That(
                            CountTransforms(instance.transform, "REF_DepositCenter"),
                            Is.EqualTo(1));
                        Assert.That(
                            CountTransforms(instance.transform, "REF_ExtractorAnchor"),
                            Is.EqualTo(1));
                        Assert.That(instance.GetComponentsInChildren<Terrain>(true), Is.Empty);
                        Assert.That(instance.GetComponentsInChildren<Rigidbody>(true), Is.Empty);

                        var moduleRoots = DirectModuleChildren(instance.transform);
                        Assert.That(moduleRoots, Has.Count.EqualTo(22));

                        long triangleCount = 0;
                        foreach (var moduleRoot in moduleRoots)
                        {
                            Assert.That(
                                Mathf.Abs(moduleRoot.localScale.x - moduleRoot.localScale.y),
                                Is.LessThan(0.0001f));
                            Assert.That(
                                Mathf.Abs(moduleRoot.localScale.x - moduleRoot.localScale.z),
                                Is.LessThan(0.0001f));
                            Assert.That(moduleRoot.localScale.x, Is.InRange(0.84f, 1.10f));

                            var renderers =
                                moduleRoot.GetComponentsInChildren<MeshRenderer>(true);
                            var filters =
                                moduleRoot.GetComponentsInChildren<MeshFilter>(true);
                            var colliders =
                                moduleRoot.GetComponentsInChildren<MeshCollider>(true);
                            Assert.That(renderers, Has.Length.EqualTo(1));
                            Assert.That(filters, Has.Length.EqualTo(1));
                            Assert.That(colliders, Has.Length.EqualTo(1));
                            Assert.That(filters[0].sharedMesh, Is.Not.Null);
                            Assert.That(colliders[0].sharedMesh, Is.SameAs(filters[0].sharedMesh));
                            Assert.That(colliders[0].convex, Is.False);
                            Assert.That(renderers[0].sharedMaterials, Has.Length.EqualTo(2));
                            Assert.That(
                                renderers[0].sharedMaterials[0].name,
                                Is.EqualTo("M_OreDeposit_Rock"));
                            Assert.That(
                                renderers[0].sharedMaterials[1].name,
                                Is.EqualTo("M_OreDeposit_" + ore));

                            for (var subMesh = 0;
                                 subMesh < filters[0].sharedMesh.subMeshCount;
                                 subMesh++)
                            {
                                triangleCount +=
                                    filters[0].sharedMesh.GetIndexCount(subMesh) / 3L;
                            }
                        }

                        Assert.That(triangleCount, Is.GreaterThan(0));
                        Assert.That(triangleCount, Is.LessThan(30000));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }
        }

        [Test]
        public void OreVariantsShareIdenticalGeometryAndPlacement()
        {
            foreach (var layout in Layouts)
            {
                var reference = AssetDatabase.LoadAssetAtPath<GameObject>(
                    DepositPath("Copper", layout));
                Assert.That(reference, Is.Not.Null);
                var referenceModules = DirectModuleChildren(reference.transform);

                foreach (var ore in new[] { "Iron", "Tin" })
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(
                        DepositPath(ore, layout));
                    Assert.That(candidate, Is.Not.Null);
                    var candidateModules = DirectModuleChildren(candidate.transform);
                    Assert.That(candidateModules, Has.Count.EqualTo(referenceModules.Count));

                    for (var index = 0; index < referenceModules.Count; index++)
                    {
                        var expected = referenceModules[index];
                        var actual = candidateModules[index];
                        Assert.That(actual.name, Is.EqualTo(expected.name));
                        Assert.That(actual.localPosition, Is.EqualTo(expected.localPosition));
                        Assert.That(actual.localRotation, Is.EqualTo(expected.localRotation));
                        Assert.That(actual.localScale, Is.EqualTo(expected.localScale));
                        Assert.That(
                            actual.GetComponentInChildren<MeshFilter>(true).sharedMesh,
                            Is.SameAs(
                                expected.GetComponentInChildren<MeshFilter>(true).sharedMesh));
                    }
                }
            }
        }

        [Test]
        public void ImportersMatchTheIslandRockContract()
        {
            foreach (var suffix in ModuleSuffixes)
            {
                var modelPath = ModelsRoot + "/ENV_Ore_" + suffix + ".fbx";
                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                Assert.That(importer, Is.Not.Null, $"Missing model importer for {modelPath}.");
                Assert.That(importer.globalScale, Is.EqualTo(1f));
                Assert.That(importer.importAnimation, Is.False);
                Assert.That(importer.addCollider, Is.False);
                Assert.That(importer.preserveHierarchy, Is.True);
                Assert.That(importer.sortHierarchyByName, Is.False);
                Assert.That(importer.importTangents, Is.EqualTo(ModelImporterTangents.None));
                Assert.That(importer.generateSecondaryUV, Is.False);

                // The deposit is smooth-shaded exactly like the Starter Island
                // rock kit. Importing authored flat normals instead is what made
                // the previous kit read as faceted crystal next to those rocks.
                Assert.That(
                    importer.importNormals,
                    Is.EqualTo(ModelImporterNormals.Calculate),
                    $"{modelPath} must recompute smooth normals.");
                Assert.That(
                    importer.normalSmoothingAngle,
                    Is.EqualTo(120f),
                    $"{modelPath} must use the island smoothing angle.");
            }
        }

        private static string DepositPath(string ore, string layout)
        {
            return DepositPrefabsRoot + "/PF_OreDeposit_" + ore + "_" + layout + ".prefab";
        }

        private static List<Transform> DirectModuleChildren(Transform root)
        {
            var result = new List<Transform>();
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child.name.StartsWith("MOD_", StringComparison.Ordinal))
                {
                    result.Add(child);
                }
            }

            return result;
        }

        private static int CountTransforms(Transform root, string name)
        {
            var count = string.Equals(root.name, name, StringComparison.Ordinal) ? 1 : 0;
            for (var index = 0; index < root.childCount; index++)
            {
                count += CountTransforms(root.GetChild(index), name);
            }

            return count;
        }

        private static void AssertIdentity(Transform transform, string context)
        {
            Assert.That(transform.localPosition, Is.EqualTo(Vector3.zero), context);
            Assert.That(transform.localRotation, Is.EqualTo(Quaternion.identity), context);
            Assert.That(transform.localScale, Is.EqualTo(Vector3.one), context);
        }
    }
}

using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class CrudePickaxePrefabTests
    {
        private const string PrefabPath =
            "Assets/_Project/Art/Tools/Pickaxe/Prefabs/PF_PickaxeCrude.prefab";

        [Test]
        public void ProductionPrefabMatchesAuthoredContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing production prefab at {PrefabPath}.");

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(instance.transform.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));

                var requiredTransforms = new[]
                {
                    "GEO_Handle",
                    "GEO_StoneHead_Active",
                    "GEO_StoneHead_Back",
                    "GEO_Binding_Head",
                    "GEO_Binding_Grip",
                    "REF_GripPrimary",
                    "REF_GripSupport",
                    "REF_ImpactTip",
                    "REF_ImpactBack"
                };
                foreach (var transformName in requiredTransforms)
                {
                    Assert.That(
                        FindRecursive(instance.transform, transformName),
                        Is.Not.Null,
                        $"Missing required transform {transformName}.");
                }

                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                var filters = instance.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(renderers, Has.Length.EqualTo(5));
                Assert.That(filters, Has.Length.EqualTo(5));

                long triangleCount = 0;
                foreach (var filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                    {
                        triangleCount += filter.sharedMesh.GetIndexCount(subMesh) / 3L;
                    }
                }

                Assert.That(triangleCount, Is.EqualTo(2876));

                var grip = FindRecursive(instance.transform, "REF_GripPrimary");
                var tip = FindRecursive(instance.transform, "REF_ImpactTip");
                Assert.That(
                    Vector3.Distance(
                        instance.transform.InverseTransformPoint(grip.position),
                        Vector3.zero),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Distance(
                        instance.transform.InverseTransformPoint(tip.position),
                        new Vector3(0f, 0.674f, 0.450f)),
                    Is.LessThan(0.005f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindRecursive(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}

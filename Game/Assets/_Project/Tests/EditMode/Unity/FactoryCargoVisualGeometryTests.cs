using System;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class FactoryCargoVisualGeometryTests
    {
        private const string BeltPrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_Straight.prefab";
        private const string FunnelPrefabPath =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/PF_Belt_Funnel.prefab";
        private const string IngotPrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_IronIngot.prefab";
        private const string PlatePrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_IronPlate.prefab";

        [TestCase(IngotPrefabPath, 0.100f, 0.056f, 0.664f)]
        [TestCase(PlatePrefabPath, 0.040f, 0.086f, 0.634f)]
        public void CargoBottomUsesRenderedBoundsInsteadOfOnePivotHeight(
            string cargoPath,
            float expectedThickness,
            float expectedOldGap,
            float expectedAlignedPivotHeight)
        {
            var beltPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(BeltPrefabPath);
            var cargoPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(cargoPath);
            Assert.That(beltPrefab, Is.Not.Null);
            Assert.That(cargoPrefab, Is.Not.Null);

            var belt = UnityEngine.Object.Instantiate(beltPrefab);
            var cargo = UnityEngine.Object.Instantiate(cargoPrefab);
            try
            {
                var supportRenderer = Require(
                        belt.transform,
                        "MEC_Belt_Straight_Batten_01")
                    .GetComponent<Renderer>();
                Assert.That(supportRenderer, Is.Not.Null);
                var supportHeight = supportRenderer.bounds.max.y;
                Assert.That(
                    supportHeight,
                    Is.EqualTo(
                        FactoryCargoVisualGeometry
                            .BeltKitSupportHeightMetres)
                        .Within(0.002f),
                    "The belt cargo contract changed: battens define its "
                    + "physical support surface.");

                cargo.transform.SetPositionAndRotation(
                    new Vector3(0f, 0.72f, 0f),
                    belt.transform.rotation);
                Assert.That(
                    FactoryCargoVisualGeometry.TryGetWorldProjection(
                        cargo.transform,
                        Vector3.up,
                        out var minimum,
                        out var maximum),
                    Is.True);
                Assert.That(
                    maximum - minimum,
                    Is.EqualTo(expectedThickness).Within(0.002f));
                Assert.That(
                    minimum - supportHeight,
                    Is.EqualTo(expectedOldGap).Within(0.002f),
                    "The old fixed 0.72 m cargo pivot no longer reproduces "
                    + "the documented hover gap.");

                Assert.That(
                    FactoryCargoVisualGeometry.AlignMinimumToPlane(
                        cargo.transform,
                        new Vector3(0f, supportHeight, 0f),
                        Vector3.up,
                        out _),
                    Is.True);
                Assert.That(
                    cargo.transform.position.y,
                    Is.EqualTo(expectedAlignedPivotHeight).Within(0.002f));
                Assert.That(
                    FactoryCargoVisualGeometry.TryGetMinimumClearance(
                        cargo.transform,
                        new Vector3(0f, supportHeight, 0f),
                        Vector3.up,
                        out var clearance),
                    Is.True);
                Assert.That(clearance, Is.EqualTo(0f).Within(0.0005f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cargo);
                UnityEngine.Object.DestroyImmediate(belt);
            }
        }

        [TestCase(IngotPrefabPath, -0.080f, 0.080f)]
        [TestCase(PlatePrefabPath, -0.120f, 0.120f)]
        public void FunnelPortRequiresCargoDepthClearance(
            string cargoPath,
            float expectedOldPenetration,
            float expectedOutwardCorrection)
        {
            var funnelPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(FunnelPrefabPath);
            var cargoPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(cargoPath);
            Assert.That(funnelPrefab, Is.Not.Null);
            Assert.That(cargoPrefab, Is.Not.Null);

            var funnel = UnityEngine.Object.Instantiate(funnelPrefab);
            var cargo = UnityEngine.Object.Instantiate(cargoPrefab);
            try
            {
                var port = Require(funnel.transform, "PORT_Belt");
                var inventoryPort =
                    Require(funnel.transform, "PORT_Inventory");
                var towardFunnelInterior =
                    (inventoryPort.position - port.position).normalized;
                Assert.That(
                    Vector3.Dot(port.forward, towardFunnelInterior),
                    Is.GreaterThan(0.85f),
                    "The authored PORT_Belt forward axis points into the "
                    + "funnel; release clearance must use its negative.");
                var outward = -port.forward;
                Assert.That(
                    Mathf.Abs(Vector3.Dot(outward, Vector3.up)),
                    Is.LessThan(0.001f),
                    "PORT_Belt must provide a horizontal connection normal.");

                // This is the current presenter pose. Its 0.12 m vertical lift
                // changes no depth coordinate, so half the cargo remains behind
                // the connection plane and intersects the funnel mouth.
                cargo.transform.SetPositionAndRotation(
                    port.position + Vector3.up * 0.12f,
                    funnel.transform.rotation);
                Assert.That(
                    FactoryCargoVisualGeometry.TryGetMinimumClearance(
                        cargo.transform,
                        port.position,
                        outward,
                        out var oldClearance),
                    Is.True);
                Assert.That(
                    oldClearance,
                    Is.EqualTo(expectedOldPenetration).Within(0.002f));

                Assert.That(
                    FactoryCargoVisualGeometry.AlignMinimumToPlane(
                        cargo.transform,
                        port.position,
                        outward,
                        out var correction),
                    Is.True);
                Assert.That(
                    correction,
                    Is.EqualTo(expectedOutwardCorrection).Within(0.002f));
                Assert.That(
                    FactoryCargoVisualGeometry.TryGetMinimumClearance(
                        cargo.transform,
                        port.position,
                        outward,
                        out var correctedClearance),
                    Is.True);
                Assert.That(
                    correctedClearance,
                    Is.EqualTo(0f).Within(0.0005f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cargo);
                UnityEngine.Object.DestroyImmediate(funnel);
            }
        }

        private static Transform Require(Transform root, string name)
        {
            foreach (var transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(
                    transform.name,
                    name,
                    StringComparison.Ordinal))
                {
                    return transform;
                }
            }

            Assert.Fail($"Missing required transform '{name}'.");
            return null;
        }
    }
}

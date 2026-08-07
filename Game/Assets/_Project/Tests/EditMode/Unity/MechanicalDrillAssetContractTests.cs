using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Guards the two things about the mechanical drill that break silently.
    ///
    /// The first is the prefab reference. Every other placeable is wired into
    /// the build controller through a serialized field or an instance already
    /// present in the scene; the drill has neither, because it is built from
    /// nothing onto a deposit. It is therefore loaded from Resources at
    /// runtime, and a Resources path is a string that no compiler checks — move
    /// the asset and placement stops working with a null reference at the
    /// moment the player aims, not at build time.
    ///
    /// The second is the funnel socket, which survives an FBX round trip and an
    /// axis conversion and looks identical in the inspector when it no longer
    /// fits.
    /// </summary>
    public sealed class MechanicalDrillAssetContractTests
    {
        /// <summary>
        /// Must stay identical to <c>FactoryBuildController.DrillPrefabResourcePath</c>
        /// and to <c>MechanicalDrillAssetSetup.PrefabResourcePath</c>. Repeating
        /// it here is the point: the test fails if any of the three drifts.
        /// </summary>
        private const string DrillResourcePath = "Machinery/PF_MechanicalDrill";

        private const string DrillPrefabAssetPath =
            "Assets/_Project/Resources/Machinery/PF_MechanicalDrill.prefab";

        private const float FunnelPortHeight = 0.4375f;
        private const float FunnelClampDepth = 0.075f;
        private const float FunnelMouthWidth = 0.44f;

        [Test]
        public void TheDrillPrefabResolvesThroughTheResourcesPathTheControllerUses()
        {
            var prefab = Resources.Load<GameObject>(DrillResourcePath);
            Assert.That(
                prefab,
                Is.Not.Null,
                $"Resources.Load<GameObject>(\"{DrillResourcePath}\") returned null. " +
                "The build controller resolves the drill this way and has no " +
                "serialized fallback, so placement would fail at aim time.");

            Assert.That(
                AssetDatabase.GetAssetPath(prefab),
                Is.EqualTo(DrillPrefabAssetPath),
                "The drill resolved from an unexpected asset, which means a " +
                "second prefab with the same Resources name exists.");
        }

        [Test]
        public void TheDrillPrefabCarriesItsAnimationPivotAndCompoundCollision()
        {
            var prefab = RequireDrillPrefab();

            Assert.That(
                FindChild(prefab.transform, "ANM_DrillBit"),
                Is.Not.Null,
                "The drill lost the pivot that spins and lowers the tool.");

            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders.Length, Is.EqualTo(6));
            foreach (var collider in colliders)
            {
                Assert.That(
                    collider,
                    Is.InstanceOf<BoxCollider>(),
                    "Drill collision must stay BoxCollider only.");
            }
        }

        [Test]
        public void TheOutputSocketStillSeatsTheFunnelClamp()
        {
            var prefab = RequireDrillPrefab();

            var port = FindChild(prefab.transform, "PORT_ItemOut");
            Assert.That(port, Is.Not.Null);

            var portLocal = prefab.transform.InverseTransformPoint(port.position);
            Assert.That(
                portLocal.y,
                Is.EqualTo(FunnelPortHeight).Within(0.002f),
                "PORT_ItemOut must sit at the height the funnel presents its " +
                "inventory side at.");
            Assert.That(
                Mathf.Abs(portLocal.x),
                Is.LessThan(0.003f),
                "PORT_ItemOut must stay on the machine centre line.");

            var frameFace = MaxZ(prefab, "GEO_SocketFrame_");
            var pocketFloor = MaxZ(prefab, "GEO_SocketBack_");
            Assert.That(
                frameFace - pocketFloor,
                Is.GreaterThanOrEqualTo(FunnelClampDepth - 0.008f),
                "The socket pocket became too shallow for the funnel clamp to seat.");

            var mouthWidth = Mathf.Min(
                -RequireRenderer(prefab, "GEO_SocketBack_L").bounds.max.x,
                RequireRenderer(prefab, "GEO_SocketBack_R").bounds.min.x) * 2f;
            Assert.That(
                mouthWidth,
                Is.GreaterThanOrEqualTo(FunnelMouthWidth - 0.012f),
                "The socket mouth became too narrow for the funnel section.");
        }

        private static GameObject RequireDrillPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DrillPrefabAssetPath);
            Assert.That(
                prefab,
                Is.Not.Null,
                $"{DrillPrefabAssetPath} is missing. Run CML/Art/Rebuild Mechanical Drill.");
            return prefab;
        }

        private static float MaxZ(GameObject prefab, string namePrefix)
        {
            var best = float.NegativeInfinity;
            foreach (var tag in new[] { "T", "B", "L", "R" })
            {
                best = Mathf.Max(best, RequireRenderer(prefab, namePrefix + tag).bounds.max.z);
            }

            return best;
        }

        private static Renderer RequireRenderer(GameObject prefab, string name)
        {
            var child = FindChild(prefab.transform, name);
            Assert.That(child, Is.Not.Null, $"The drill is missing '{name}'.");
            var renderer = child.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null, $"'{name}' carries no Renderer.");
            return renderer;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}

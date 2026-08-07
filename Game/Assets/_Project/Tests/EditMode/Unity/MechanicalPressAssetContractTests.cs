using System;
using System.Collections.Generic;
using CML.Foundation;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class MechanicalPressAssetContractTests
    {
        private const string ModelPath =
            "Assets/_Project/Art/MechanicalEra/Models/MEC_MechanicalPress.fbx";
        private const string PressPrefabPath =
            "Assets/_Project/Art/MechanicalEra/Prefabs/PF_MechanicalPress.prefab";
        private const string IronIngotPrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_IronIngot.prefab";
        private const string IronPlatePrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_IronPlate.prefab";

        private static readonly HashSet<string> ExpectedRamRenderers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "GEO_PressRam_PistonRod",
                "GEO_PressRam_Crosshead",
                "GEO_PressRam_CrossheadFace",
                "GEO_PressRam_GuideShoe_L",
                "GEO_PressRam_GuideShoe_R",
                "GEO_PressRam_DieStem",
                "GEO_PressRam_Die",
                "GEO_PressRam_WorkingFace"
            };

        private static readonly string[] StaticVerticalDriveParts =
        {
            "GEO_VerticalDriveHousing",
            "GEO_VerticalDriveCylinder",
            "GEO_RamGuide_L",
            "GEO_RamGuide_R"
        };

        private static readonly string[] ForbiddenLegacyDriveFragments =
        {
            "DriveFlywheel",
            "DriveShaft",
            "PressLink",
            "RamClevis",
            "Eccentric"
        };

        [Test]
        public void AuthoredPressHasOneReadableVerticalMotionHierarchy()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null, $"Missing press model at {ModelPath}.");

            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                var ram = Require(instance.transform, "ANM_PressRam");
                var actualRamRenderers = new HashSet<string>(StringComparer.Ordinal);
                foreach (var renderer in ram.GetComponentsInChildren<Renderer>(true))
                {
                    actualRamRenderers.Add(renderer.name);
                }

                Assert.That(
                    actualRamRenderers,
                    Is.EquivalentTo(ExpectedRamRenderers),
                    "ANM_PressRam must own all and only vertically moving geometry.");

                foreach (var staticPartName in StaticVerticalDriveParts)
                {
                    var staticPart = Require(instance.transform, staticPartName);
                    Assert.That(
                        staticPart.IsChildOf(ram),
                        Is.False,
                        $"{staticPartName} is fixed and cannot travel with the ram.");
                }

                foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        transform.name,
                        Is.Not.EqualTo("Cube")
                            .And.Not.EqualTo("Camera")
                            .And.Not.EqualTo("Light"),
                        "The exported production hierarchy contains a Blender default object.");
                    foreach (var fragment in ForbiddenLegacyDriveFragments)
                    {
                        Assert.That(
                            transform.name,
                            Does.Not.Contain(fragment),
                            $"Obsolete horizontal-drive geometry remains at {transform.name}.");
                    }
                }

                var rod = Require(instance.transform, "GEO_PressRam_PistonRod")
                    .GetComponent<Renderer>();
                var cylinder = Require(instance.transform, "GEO_VerticalDriveCylinder")
                    .GetComponent<Renderer>();
                Assert.That(rod, Is.Not.Null);
                Assert.That(cylinder, Is.Not.Null);
                Assert.That(
                    VerticalOverlap(rod.bounds, cylinder.bounds),
                    Is.GreaterThan(0.12f),
                    "The moving piston rod must visibly enter the fixed cylinder.");

                var guideLeft = Require(instance.transform, "GEO_RamGuide_L");
                var guideRight = Require(instance.transform, "GEO_RamGuide_R");
                Assert.That(
                    Mathf.Sign(guideLeft.position.x),
                    Is.Not.EqualTo(Mathf.Sign(guideRight.position.x)),
                    "The two fixed guides must flank the press centre.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ItemPortsSpanTheBedAtBeltHeightAndWorkpieceIsCentred()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null, $"Missing press model at {ModelPath}.");

            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                var itemIn = Require(instance.transform, "PORT_ItemIn");
                var itemOut = Require(instance.transform, "PORT_ItemOut");
                var workpiece = Require(instance.transform, "REF_Workpiece");

                Assert.That(itemIn.position.y, Is.EqualTo(0.60f).Within(0.002f));
                Assert.That(itemOut.position.y, Is.EqualTo(0.60f).Within(0.002f));
                Assert.That(
                    Vector3.Distance(itemIn.position, itemOut.position),
                    Is.GreaterThan(1.50f));

                var workpieceLocal =
                    instance.transform.InverseTransformPoint(workpiece.position);
                Assert.That(workpieceLocal.x, Is.EqualTo(0f).Within(0.002f));
                Assert.That(workpieceLocal.z, Is.EqualTo(0f).Within(0.002f));
                Assert.That(
                    workpiece.position.y,
                    Is.GreaterThan(itemIn.position.y),
                    "The workpiece reference belongs on the press bed, above lane height.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WorkpiecesStayFlatRegardlessOfReferenceMarkerAxes()
        {
            var pressPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PressPrefabPath);
            var ingotPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(IronIngotPrefabPath);
            var platePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(IronPlatePrefabPath);
            Assert.That(pressPrefab, Is.Not.Null);
            Assert.That(ingotPrefab, Is.Not.Null);
            Assert.That(platePrefab, Is.Not.Null);

            var instance = UnityEngine.Object.Instantiate(pressPrefab);
            try
            {
                instance.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                var ram = Require(instance.transform, "ANM_PressRam");
                var workpiece = Require(instance.transform, "REF_Workpiece");

                // A model-import marker may carry an axis conversion. It supplies
                // the bed position only and must never rotate cargo upright.
                workpiece.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var presenter =
                    instance.AddComponent<FactoryPressPresenter>();
                presenter.ConfigureAuthoring(
                    new StableId(0x4D303442UL, 0x5052455353UL),
                    ram,
                    workpiece,
                    ingotPrefab,
                    platePrefab);
                presenter.SetVisualPrefabs(ingotPrefab, platePrefab);

                var ingotVisual = Require(
                    instance.transform,
                    "PF_IronIngot_PressWorkpiece");
                var plateVisual = Require(
                    instance.transform,
                    "PF_IronPlate_PressWorkpiece");

                AssertWorkpieceFlat(instance.transform, workpiece, ingotVisual);
                AssertWorkpieceFlat(instance.transform, workpiece, plateVisual);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void RamEndpointRemainsCapturedAndDieCannotCrossBed(
            float normalizedCompression)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null, $"Missing press model at {ModelPath}.");

            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                var ram = Require(instance.transform, "ANM_PressRam");
                var rod = Require(instance.transform, "GEO_PressRam_PistonRod")
                    .GetComponent<Renderer>();
                var cylinder = Require(instance.transform, "GEO_VerticalDriveCylinder")
                    .GetComponent<Renderer>();
                var crosshead = Require(instance.transform, "GEO_PressRam_Crosshead")
                    .GetComponent<Renderer>();
                var leftShoe = Require(
                        instance.transform,
                        "GEO_PressRam_GuideShoe_L")
                    .GetComponent<Renderer>();
                var rightShoe = Require(
                        instance.transform,
                        "GEO_PressRam_GuideShoe_R")
                    .GetComponent<Renderer>();
                var leftGuide = Require(instance.transform, "GEO_RamGuide_L")
                    .GetComponent<Renderer>();
                var rightGuide = Require(instance.transform, "GEO_RamGuide_R")
                    .GetComponent<Renderer>();
                var face = Require(instance.transform, "GEO_PressRam_WorkingFace")
                    .GetComponent<Renderer>();
                var bed = Require(instance.transform, "GEO_PressBed_Insert")
                    .GetComponent<Renderer>();

                ram.localPosition += Vector3.down
                    * (FactoryPressPresenter.ProductionRamTravelMetres
                        * normalizedCompression);

                Assert.That(
                    VerticalOverlap(rod.bounds, cylinder.bounds),
                    Is.GreaterThanOrEqualTo(0.24f),
                    "The piston rod detached from its fixed cylinder.");

                var guideMin = Mathf.Min(
                    leftGuide.bounds.min.y,
                    rightGuide.bounds.min.y);
                var guideMax = Mathf.Max(
                    leftGuide.bounds.max.y,
                    rightGuide.bounds.max.y);
                AssertCapturedByGuides(crosshead, guideMin, guideMax);
                AssertCapturedByGuides(leftShoe, guideMin, guideMax);
                AssertCapturedByGuides(rightShoe, guideMin, guideMax);

                var dieBedGap = face.bounds.min.y - bed.bounds.max.y;
                var expectedGap =
                    FactoryPressPresenter.ProductionRamTravelMetres
                    * (1f - normalizedCompression);
                Assert.That(
                    dieBedGap,
                    Is.EqualTo(expectedGap).Within(0.003f),
                    "The endpoint no longer matches the authored die-bed clearance.");
                Assert.That(
                    dieBedGap,
                    Is.GreaterThanOrEqualTo(-0.002f),
                    "The press die crosses the bed/frame.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertCapturedByGuides(
            Renderer movingPart,
            float guideMin,
            float guideMax)
        {
            Assert.That(movingPart, Is.Not.Null);
            Assert.That(
                movingPart.bounds.min.y,
                Is.GreaterThanOrEqualTo(guideMin - 0.002f),
                $"{movingPart.name} exits below the fixed guides.");
            Assert.That(
                movingPart.bounds.max.y,
                Is.LessThanOrEqualTo(guideMax + 0.002f),
                $"{movingPart.name} exits above the fixed guides.");
        }

        private static float VerticalOverlap(Bounds first, Bounds second)
        {
            return Mathf.Min(first.max.y, second.max.y)
                - Mathf.Max(first.min.y, second.min.y);
        }

        private static void AssertWorkpieceFlat(
            Transform press,
            Transform marker,
            Transform workpiece)
        {
            Assert.That(
                Vector3.Distance(workpiece.position, marker.position),
                Is.LessThan(0.001f),
                $"{workpiece.name} no longer sits on REF_Workpiece.");
            Assert.That(
                Quaternion.Angle(workpiece.rotation, press.rotation),
                Is.LessThan(0.01f),
                $"{workpiece.name} inherited the marker axes instead of "
                + "the press/belt cargo orientation.");

            var renderer = workpiece.GetComponentInChildren<Renderer>(true);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(
                renderer.bounds.size.y,
                Is.LessThan(renderer.bounds.size.x)
                    .And.LessThan(renderer.bounds.size.z),
                $"{workpiece.name} is standing upright instead of lying flat.");
        }

        private static Transform Require(Transform root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, name, StringComparison.Ordinal))
                {
                    return transform;
                }
            }

            Assert.Fail($"Missing required Mechanical Press transform '{name}'.");
            return null;
        }
    }
}

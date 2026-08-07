using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class ManualEraPrefabTests
    {
        private const string ModelPath =
            "Assets/_Project/Art/ManualEra/Models/STR_Workbench.fbx";
        private const string PrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_Workbench.prefab";
        private const string SharedMaterialPath =
            "Assets/_Project/Art/Shared/ManualEra/Materials/M_ManualEra_OpaqueAtlas.mat";
        private const string FireMaterialPath =
            "Assets/_Project/Art/Shared/ManualEra/Materials/" +
            "M_ManualEra_FireEmissive.mat";
        private const string SharedBaseTexturePath =
            "Assets/_Project/Art/Shared/ManualEra/Textures/" +
            "T_ManualEra_BaseColor.png";
        private const string SharedMaskTexturePath =
            "Assets/_Project/Art/Shared/ManualEra/Textures/" +
            "T_ManualEra_Mask.png";
        private const string ExpectedNonIronBaseHash =
            "5C09E1306D5066DC76EA2B5D8EDE928736B79FBB5116DFE404B84E8351FA532E";
        private const string ExpectedNonIronMaskHash =
            "851E2C84B1BECA57F118F6D295965398434D50F693BF2D73B3F231C144079570";
        private const string ExpectedIronBaseHash =
            "CABEEC8D865E29CEC08DFC10965EE342CC50FF1DE385E3FC7751692F3C91720C";
        private const string ExpectedIronMaskHash =
            "0472FB7E326B3CB45B6AC0B2AB5AB6E9DA36DB1F3B0ED10E1C033C17455F497F";
        private const string FurnaceModelPath =
            "Assets/_Project/Art/ManualEra/Models/STR_CrudeFurnace.fbx";
        private const string FurnacePrefabPath =
            "Assets/_Project/Art/ManualEra/Prefabs/PF_CrudeFurnace.prefab";
        private const int ExpectedFurnaceMeshCount = 4;
        private const long ExpectedFurnaceTriangleCount = 2737;
        private static readonly Vector3 ExpectedFurnaceBounds =
            new Vector3(1.557375f, 1.300000f, 1.078125f);
        private static readonly Vector3 ExpectedFurnaceBoundsCenter =
            new Vector3(0.141313f, 0.650000f, -0.000937f);

        private static readonly string[] RequiredMeshes =
        {
            "GEO_Worktop",
            "GEO_Legs",
            "GEO_StoneFeet",
            "GEO_Frame",
            "GEO_LowerShelf",
            "GEO_Joinery"
        };

        private static readonly MarkerExpectation[] RequiredMarkers =
        {
            new MarkerExpectation("REF_Placement", Vector3.zero),
            new MarkerExpectation("REF_Interact", new Vector3(0f, 0.760f, -0.480f)),
            new MarkerExpectation("REF_WorkSurface", new Vector3(0f, 0.925f, 0f)),
            new MarkerExpectation("REF_Output", new Vector3(0.395f, 0.940f, -0.020f)),
            new MarkerExpectation(
                "REF_NetworkConnector",
                new Vector3(0.420f, 0.485f, 0.390f))
        };

        private static readonly ColliderExpectation[] ExpectedColliders =
        {
            new ColliderExpectation(
                "COL_Upper",
                new Vector3(0f, 0.800f, 0f),
                new Vector3(1.450f, 0.240f, 0.720f)),
            new ColliderExpectation(
                "COL_TrestleLeft",
                new Vector3(-0.550f, 0.400f, 0f),
                new Vector3(0.340f, 0.800f, 0.720f)),
            new ColliderExpectation(
                "COL_TrestleRight",
                new Vector3(0.550f, 0.400f, 0f),
                new Vector3(0.340f, 0.800f, 0.720f))
        };

        private static readonly string[] RequiredFurnaceMeshes =
        {
            "GEO_StoneBlocks",
            "GEO_CavityLining",
            "GEO_ConstructionLogs",
            "GEO_Fire",
        };

        private static readonly MarkerExpectation[] RequiredFurnaceMarkers =
        {
            new MarkerExpectation("REF_Placement", Vector3.zero),
            new MarkerExpectation(
                "REF_Interact",
                new Vector3(0f, 0.620f, -0.740f)),
            new MarkerExpectation(
                "PORT_MineralInput",
                new Vector3(0f, 0.940f, -0.555f)),
            new MarkerExpectation(
                "PORT_FuelInput",
                new Vector3(0f, 0.340f, -0.555f)),
            new MarkerExpectation(
                "PORT_ProductOutput",
                new Vector3(0.935f, 0.405f, -0.055f)),
            new MarkerExpectation(
                "REF_NetworkConnector",
                new Vector3(0f, 0.640f, 0.555f))
        };

        private static readonly PortExpectation[] RequiredFurnacePorts =
        {
            new PortExpectation("PORT_MineralInput", Vector3.back),
            new PortExpectation("PORT_FuelInput", Vector3.back),
            new PortExpectation("PORT_ProductOutput", Vector3.right)
        };

        private static readonly ColliderExpectation[] ExpectedFurnaceColliders =
        {
            new ColliderExpectation(
                "COL_Core",
                new Vector3(0f, 0.650f, 0.130f),
                new Vector3(1.200f, 1.280f, 0.780f)),
            new ColliderExpectation(
                "COL_FrontLeft",
                new Vector3(-0.485f, 0.650f, -0.400f),
                new Vector3(0.290f, 1.260f, 0.280f)),
            new ColliderExpectation(
                "COL_FrontRight",
                new Vector3(0.485f, 0.650f, -0.400f),
                new Vector3(0.290f, 1.260f, 0.280f)),
            new ColliderExpectation(
                "COL_FrontBridge",
                new Vector3(0f, 0.7575f, -0.400f),
                new Vector3(0.680f, 0.095f, 0.280f)),
            new ColliderExpectation(
                "COL_FrontTop",
                new Vector3(0f, 1.185f, -0.400f),
                new Vector3(0.680f, 0.230f, 0.280f)),
            new ColliderExpectation(
                "COL_ProductTray",
                new Vector3(0.780f, 0.360f, -0.055f),
                new Vector3(0.280f, 0.090f, 0.380f))
        };

        private static readonly ClearanceExpectation[] ExpectedPortClearances =
        {
            new ClearanceExpectation(
                "PORT_MineralInput",
                new Vector3(0f, 0.940f, -0.405f),
                new Vector3(0.320f, 0.230f, 0.270f)),
            new ClearanceExpectation(
                "PORT_FuelInput",
                new Vector3(0f, 0.340f, -0.405f),
                new Vector3(0.360f, 0.300f, 0.270f)),
            new ClearanceExpectation(
                "PORT_ProductOutput",
                new Vector3(0.800f, 0.5275f, -0.055f),
                new Vector3(0.250f, 0.225f, 0.300f))
        };

        private static readonly SimplePropExpectation[] RemainingKitProps =
        {
            new SimplePropExpectation(
                "Crate",
                "Assets/_Project/Art/ManualEra/Models/STR_Crate.fbx",
                "Assets/_Project/Art/ManualEra/Prefabs/PF_Crate.prefab",
                "PF_Crate",
                "STR_Crate",
                new[]
                {
                    "GEO_CrateBody",
                    "GEO_CrateLid"
                },
                new[]
                {
                    new MarkerExpectation("REF_Placement", Vector3.zero),
                    new MarkerExpectation(
                        "REF_Interact",
                        new Vector3(0f, 0.590f, -0.480f)),
                    new MarkerExpectation(
                        "PORT_ItemIO",
                        new Vector3(0f, 0.4375f, -0.375f)),
                    new MarkerExpectation(
                        "REF_NetworkConnector",
                        new Vector3(0f, 0.300f, 0.375f))
                },
                new[]
                {
                    new PortExpectation("REF_Interact", Vector3.back),
                    new PortExpectation("PORT_ItemIO", Vector3.back),
                    new PortExpectation(
                        "REF_NetworkConnector",
                        Vector3.forward)
                },
                new[]
                {
                    new ColliderExpectation(
                        "COL_Body",
                        new Vector3(0f, 0.290f, 0f),
                        new Vector3(1.000f, 0.580f, 0.720f))
                },
                1276,
                new Vector3(1.000f, 0.580f, 0.720f),
                new Vector3(0f, 0.290f, 0f),
                0.012f),
            new SimplePropExpectation(
                "Iron Ingot",
                "Assets/_Project/Art/ManualEra/Models/ITM_IronIngot.fbx",
                "Assets/_Project/Art/ManualEra/Prefabs/PF_IronIngot.prefab",
                "PF_IronIngot",
                "ITM_IronIngot",
                new[]
                {
                    "GEO_IronIngot"
                },
                new[]
                {
                    new MarkerExpectation("REF_Placement", Vector3.zero),
                    new MarkerExpectation(
                        "REF_Pickup",
                        new Vector3(0f, 0.065f, 0f))
                },
                Array.Empty<PortExpectation>(),
                new[]
                {
                    new ColliderExpectation(
                        "COL_Body",
                        new Vector3(0f, 0.050f, 0f),
                        new Vector3(0.320f, 0.100f, 0.160f))
                },
                60,
                new Vector3(0.320f, 0.100f, 0.160f),
                new Vector3(0f, 0.050f, 0f),
                0.003f),
            new SimplePropExpectation(
                "Iron Plate",
                "Assets/_Project/Art/ManualEra/Models/ITM_IronPlate.fbx",
                "Assets/_Project/Art/ManualEra/Prefabs/PF_IronPlate.prefab",
                "PF_IronPlate",
                "ITM_IronPlate",
                new[]
                {
                    "GEO_IronPlate"
                },
                new[]
                {
                    new MarkerExpectation("REF_Placement", Vector3.zero),
                    new MarkerExpectation(
                        "REF_Pickup",
                        new Vector3(0f, 0.050f, 0f))
                },
                Array.Empty<PortExpectation>(),
                new[]
                {
                    new ColliderExpectation(
                        "COL_Body",
                        new Vector3(0f, 0.020f, 0f),
                        new Vector3(0.340f, 0.040f, 0.240f))
                },
                150,
                new Vector3(0.340f, 0.040f, 0.240f),
                new Vector3(0f, 0.020f, 0f),
                0.003f)
        };

        [Test]
        public void WorkbenchPrefabMatchesAuthoredProductionContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing production prefab at {PrefabPath}.");
            var expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(
                expectedMaterial,
                Is.Not.Null,
                $"Missing shared Manual Era material at {SharedMaterialPath}.");

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                AssertIdentity(instance.transform, PrefabPath);
                Assert.That(instance.name, Does.StartWith("PF_Workbench"));

                foreach (var meshName in RequiredMeshes)
                {
                    Assert.That(
                        CountTransforms(instance.transform, meshName),
                        Is.EqualTo(1),
                        $"Expected one exact {meshName} transform.");
                    var meshTransform = FindRecursive(instance.transform, meshName);
                    Assert.That(
                        meshTransform.GetComponent<MeshFilter>(),
                        Is.Not.Null,
                        $"{meshName} is not a MeshFilter transform.");
                    Assert.That(
                        meshTransform.GetComponent<Renderer>(),
                        Is.Not.Null,
                        $"{meshName} is not a renderer transform.");
                }

                foreach (var marker in RequiredMarkers)
                {
                    Assert.That(
                        CountTransforms(instance.transform, marker.Name),
                        Is.EqualTo(1),
                        $"Expected one exact {marker.Name} marker.");
                    var markerTransform =
                        FindRecursive(instance.transform, marker.Name);
                    var actualPosition =
                        instance.transform.InverseTransformPoint(
                            markerTransform.position);
                    Assert.That(
                        Vector3.Distance(actualPosition, marker.Position),
                        Is.LessThan(0.006f),
                        $"{marker.Name} moved from its authored position.");
                }

                var renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                var filters =
                    instance.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(renderers, Has.Length.EqualTo(6));
                Assert.That(filters, Has.Length.EqualTo(6));

                foreach (var renderer in renderers)
                {
                    Assert.That(
                        renderer.sharedMaterials,
                        Has.Length.EqualTo(1),
                        $"{renderer.name} must use one material slot.");
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(expectedMaterial),
                        $"{renderer.name} does not use the shared Manual Era atlas.");
                }

                long triangleCount = 0;
                foreach (var filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    Assert.That(
                        filter.sharedMesh.subMeshCount,
                        Is.EqualTo(1),
                        $"{filter.name} must contain one submesh.");
                    Assert.That(
                        filter.sharedMesh.HasVertexAttribute(
                            UnityEngine.Rendering.VertexAttribute.TexCoord0),
                        Is.True,
                        $"{filter.name} has no UV0 coordinates.");
                    triangleCount += filter.sharedMesh.GetIndexCount(0) / 3L;
                }

                Assert.That(
                    triangleCount,
                    Is.EqualTo(1502),
                    "Workbench production topology changed.");

                var bounds = CollectBounds(renderers);
                Assert.That(
                    Mathf.Abs(bounds.size.x - 1.45f),
                    Is.LessThan(0.015f),
                    $"Unexpected width {bounds.size.x:F4} m.");
                Assert.That(
                    Mathf.Abs(bounds.size.y - 0.92f),
                    Is.LessThan(0.012f),
                    $"Unexpected height {bounds.size.y:F4} m.");
                Assert.That(
                    Mathf.Abs(bounds.size.z - 0.72f),
                    Is.LessThan(0.010f),
                    $"Unexpected depth {bounds.size.z:F4} m.");
                Assert.That(
                    Mathf.Abs(bounds.min.y),
                    Is.LessThan(0.006f),
                    $"Workbench is not grounded; minimum Y is {bounds.min.y:F4}.");

                var boxColliders =
                    instance.GetComponentsInChildren<BoxCollider>(true);
                Assert.That(boxColliders, Has.Length.EqualTo(3));
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(3),
                    "Only the three authored BoxColliders are permitted.");

                foreach (var expected in ExpectedColliders)
                {
                    Assert.That(
                        CountTransforms(instance.transform, expected.Name),
                        Is.EqualTo(1),
                        $"Expected one exact {expected.Name} collider transform.");
                    var colliderTransform =
                        FindRecursive(instance.transform, expected.Name);
                    Assert.That(
                        Vector3.Distance(
                            colliderTransform.localPosition,
                            expected.Position),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        colliderTransform.localRotation,
                        Is.EqualTo(Quaternion.identity));
                    Assert.That(
                        colliderTransform.localScale,
                        Is.EqualTo(Vector3.one));

                    var collider =
                        colliderTransform.GetComponent<BoxCollider>();
                    Assert.That(collider, Is.Not.Null);
                    Assert.That(
                        Vector3.Distance(collider.center, Vector3.zero),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        Vector3.Distance(collider.size, expected.Size),
                        Is.LessThan(0.0001f));
                    Assert.That(collider.isTrigger, Is.False);
                }

                Assert.That(
                    instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animation>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WorkbenchImporterAndMaterialRemapMatchContract()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, $"Missing ModelImporter for {ModelPath}.");
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.useFileScale, Is.True);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(
                importer.importNormals,
                Is.EqualTo(ModelImporterNormals.Import));
            Assert.That(
                importer.importTangents,
                Is.EqualTo(ModelImporterTangents.CalculateMikk));
            Assert.That(importer.generateSecondaryUV, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(
                importer.meshCompression,
                Is.EqualTo(ModelImporterMeshCompression.Off));
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.sortHierarchyByName, Is.False);
            Assert.That(importer.addCollider, Is.False);

            var expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(expectedMaterial, Is.Not.Null);
            var sourceIdentifier =
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    "M_ManualEra_OpaqueAtlas");
            var remaps = importer.GetExternalObjectMap();
            Assert.That(
                remaps.ContainsKey(sourceIdentifier),
                Is.True,
                "The FBX material slot is not remapped to the shared atlas.");
            Assert.That(
                remaps[sourceIdentifier],
                Is.SameAs(expectedMaterial),
                "The model remap points to a duplicated or incorrect material.");
        }

        [Test]
        public void CrudeFurnacePrefabMatchesAuthoredProductionContract()
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(FurnacePrefabPath);
            Assert.That(
                prefab,
                Is.Not.Null,
                $"Missing production prefab at {FurnacePrefabPath}.");
            var expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(
                expectedMaterial,
                Is.Not.Null,
                $"Missing shared Manual Era material at {SharedMaterialPath}.");
            var expectedFireMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(FireMaterialPath);
            Assert.That(
                expectedFireMaterial,
                Is.Not.Null,
                $"Missing shared Manual Era fire material at {FireMaterialPath}.");

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                AssertIdentity(instance.transform, FurnacePrefabPath);
                Assert.That(
                    instance.name,
                    Does.StartWith("PF_CrudeFurnace"));
                var authoredRoot =
                    FindRecursive(
                        instance.transform,
                        "GEO_StoneBlocks").parent;
                if (authoredRoot != instance.transform)
                {
                    Assert.That(
                        authoredRoot.parent,
                        Is.SameAs(instance.transform),
                        "Only one Unity import wrapper is permitted.");
                    Assert.That(
                        authoredRoot.name,
                        Is.EqualTo("STR_CrudeFurnace"));
                }

                foreach (var meshName in RequiredFurnaceMeshes)
                {
                    Assert.That(
                        CountTransforms(instance.transform, meshName),
                        Is.EqualTo(1),
                        $"Expected one exact {meshName} transform.");
                    var meshTransform =
                        FindRecursive(instance.transform, meshName);
                    Assert.That(
                        meshTransform.GetComponent<MeshFilter>(),
                        Is.Not.Null,
                        $"{meshName} is not a MeshFilter transform.");
                    Assert.That(
                        meshTransform.GetComponent<Renderer>(),
                        Is.Not.Null,
                        $"{meshName} is not a renderer transform.");
                    Assert.That(
                        meshTransform.parent,
                        Is.SameAs(authoredRoot),
                        $"{meshName} must be a direct child of the authored " +
                        "Furnace root.");
                }

                foreach (var marker in RequiredFurnaceMarkers)
                {
                    Assert.That(
                        CountTransforms(instance.transform, marker.Name),
                        Is.EqualTo(1),
                        $"Expected one exact {marker.Name} marker.");
                    var markerTransform =
                        FindRecursive(instance.transform, marker.Name);
                    var actualPosition =
                        instance.transform.InverseTransformPoint(
                            markerTransform.position);
                    Assert.That(
                        Vector3.Distance(actualPosition, marker.Position),
                        Is.LessThan(0.006f),
                        $"{marker.Name} moved from its authored position.");
                    Assert.That(
                        markerTransform.parent,
                        Is.SameAs(authoredRoot),
                        $"{marker.Name} must be a direct child of the authored " +
                        "Furnace root.");
                }

                foreach (var port in RequiredFurnacePorts)
                {
                    var portTransform =
                        FindRecursive(instance.transform, port.Name);
                    var actualForward =
                        instance.transform.InverseTransformDirection(
                            portTransform.forward);
                    Assert.That(
                        Vector3.Angle(actualForward, port.Forward),
                        Is.LessThan(0.1f),
                        $"{port.Name} faces the wrong direction.");
                    var actualUp =
                        instance.transform.InverseTransformDirection(
                            portTransform.up);
                    Assert.That(
                        Vector3.Angle(actualUp, Vector3.up),
                        Is.LessThan(0.1f),
                        $"{port.Name} has a tilted up axis.");
                }

                var fireTransform =
                    FindRecursive(instance.transform, "GEO_Fire");
                Assert.That(
                    fireTransform.gameObject.activeSelf,
                    Is.False,
                    "The Crude Furnace prefab must start unlit.");

                var renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                var filters =
                    instance.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(
                    renderers,
                    Has.Length.EqualTo(ExpectedFurnaceMeshCount));
                Assert.That(
                    filters,
                    Has.Length.EqualTo(ExpectedFurnaceMeshCount));

                foreach (var renderer in renderers)
                {
                    var requiredMaterial = string.Equals(
                        renderer.transform.name,
                        "GEO_Fire",
                        StringComparison.Ordinal)
                        ? expectedFireMaterial
                        : expectedMaterial;
                    Assert.That(
                        renderer.sharedMaterials,
                        Has.Length.EqualTo(1),
                        $"{renderer.name} must use one material slot.");
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(requiredMaterial),
                        $"{renderer.name} does not use its required shared " +
                        "Manual Era material.");
                }

                long triangleCount = 0;
                foreach (var filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    Assert.That(
                        filter.sharedMesh.subMeshCount,
                        Is.EqualTo(1),
                        $"{filter.name} must contain one submesh.");
                    Assert.That(
                        filter.sharedMesh.HasVertexAttribute(
                            UnityEngine.Rendering.VertexAttribute.TexCoord0),
                        Is.True,
                        $"{filter.name} has no UV0 coordinates.");
                    triangleCount += filter.sharedMesh.GetIndexCount(0) / 3L;
                }

                Assert.That(
                    triangleCount,
                    Is.EqualTo(ExpectedFurnaceTriangleCount),
                    "Crude Furnace production topology changed.");

                var bounds = CollectBounds(renderers);
                Assert.That(
                    Mathf.Abs(
                        bounds.size.x - ExpectedFurnaceBounds.x),
                    Is.LessThan(0.015f),
                    $"Unexpected width {bounds.size.x:F4} m.");
                Assert.That(
                    Mathf.Abs(
                        bounds.size.y - ExpectedFurnaceBounds.y),
                    Is.LessThan(0.015f),
                    $"Unexpected height {bounds.size.y:F4} m.");
                Assert.That(
                    Mathf.Abs(
                        bounds.size.z - ExpectedFurnaceBounds.z),
                    Is.LessThan(0.015f),
                    $"Unexpected depth {bounds.size.z:F4} m.");
                Assert.That(
                    Mathf.Abs(bounds.min.y),
                    Is.LessThan(0.006f),
                    $"Furnace is not grounded; minimum Y is " +
                    $"{bounds.min.y:F4}.");
                Assert.That(
                    Vector3.Distance(
                        bounds.center,
                        ExpectedFurnaceBoundsCenter),
                    Is.LessThan(0.012f),
                    $"Unexpected Furnace bounds center {bounds.center}.");

                var boxColliders =
                    instance.GetComponentsInChildren<BoxCollider>(true);
                Assert.That(
                    boxColliders,
                    Has.Length.EqualTo(ExpectedFurnaceColliders.Length));
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(ExpectedFurnaceColliders.Length),
                    "Only the authored compound BoxColliders are permitted.");

                foreach (var expected in ExpectedFurnaceColliders)
                {
                    Assert.That(
                        CountTransforms(instance.transform, expected.Name),
                        Is.EqualTo(1),
                        $"Expected one exact {expected.Name} collider transform.");
                    var colliderTransform =
                        FindRecursive(instance.transform, expected.Name);
                    Assert.That(
                        Vector3.Distance(
                            colliderTransform.localPosition,
                            expected.Position),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        colliderTransform.localRotation,
                        Is.EqualTo(Quaternion.identity));
                    Assert.That(
                        colliderTransform.localScale,
                        Is.EqualTo(Vector3.one));

                    var collider =
                        colliderTransform.GetComponent<BoxCollider>();
                    Assert.That(collider, Is.Not.Null);
                    Assert.That(
                        Vector3.Distance(
                            collider.center,
                            Vector3.zero),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        Vector3.Distance(
                            collider.size,
                            expected.Size),
                        Is.LessThan(0.0001f));
                    Assert.That(collider.isTrigger, Is.False);
                }

                foreach (var clearance in ExpectedPortClearances)
                {
                    var clearanceBounds = new Bounds(
                        clearance.Position,
                        clearance.Size);
                    foreach (var collider in ExpectedFurnaceColliders)
                    {
                        var colliderBounds = new Bounds(
                            collider.Position,
                            collider.Size);
                        Assert.That(
                            BoundsInteriorsOverlap(
                                colliderBounds,
                                clearanceBounds),
                            Is.False,
                            $"{collider.Name} blocks the physical opening " +
                                $"{clearance.Name}.");
                    }

                    var markerTransform =
                        FindRecursive(instance.transform, clearance.Name);
                    var port = FindPortExpectation(clearance.Name);
                    var markerLocalPosition =
                        instance.transform.InverseTransformPoint(
                            markerTransform.position);
                    var sightStart =
                        markerLocalPosition +
                        port.Forward * 0.10f;
                    foreach (var collider in ExpectedFurnaceColliders)
                    {
                        var colliderBounds = new Bounds(
                            collider.Position,
                            collider.Size);
                        Assert.That(
                            SegmentIntersectsBounds(
                                sightStart,
                                clearance.Position,
                                colliderBounds),
                            Is.False,
                            $"{collider.Name} blocks line of sight from " +
                            $"{clearance.Name} to its opening.");
                    }
                }

                Assert.That(
                    instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animation>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CrudeFurnaceImporterAndMaterialRemapMatchContract()
        {
            var importer =
                AssetImporter.GetAtPath(FurnaceModelPath) as ModelImporter;
            Assert.That(
                importer,
                Is.Not.Null,
                $"Missing ModelImporter for {FurnaceModelPath}.");
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.useFileScale, Is.True);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(importer.importVisibility, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importConstraints, Is.False);
            Assert.That(
                importer.importNormals,
                Is.EqualTo(ModelImporterNormals.Import));
            Assert.That(
                importer.importTangents,
                Is.EqualTo(ModelImporterTangents.CalculateMikk));
            Assert.That(importer.generateSecondaryUV, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(
                importer.meshCompression,
                Is.EqualTo(ModelImporterMeshCompression.Off));
            Assert.That(importer.optimizeMeshPolygons, Is.True);
            Assert.That(importer.optimizeMeshVertices, Is.True);
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.sortHierarchyByName, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.ImportStandard));
            Assert.That(
                importer.materialLocation,
                Is.EqualTo(ModelImporterMaterialLocation.InPrefab));
            Assert.That(
                importer.materialName,
                Is.EqualTo(ModelImporterMaterialName.BasedOnMaterialName));

            var expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(expectedMaterial, Is.Not.Null);
            var sourceIdentifier =
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    "M_ManualEra_OpaqueAtlas");
            var remaps = importer.GetExternalObjectMap();
            Assert.That(
                remaps,
                Has.Count.EqualTo(2),
                "Crude Furnace must have exactly the opaque and fire " +
                "shared-material remaps.");
            Assert.That(
                remaps.ContainsKey(sourceIdentifier),
                Is.True,
                "The Furnace FBX material slot is not remapped to the " +
                "shared atlas.");
            Assert.That(
                remaps[sourceIdentifier],
                Is.SameAs(expectedMaterial),
                "The Furnace model remap points to a duplicated or " +
                "incorrect material.");

            var expectedFireMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(FireMaterialPath);
            Assert.That(
                expectedFireMaterial,
                Is.Not.Null,
                $"Missing shared fire material at {FireMaterialPath}.");
            var fireSourceIdentifier =
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    "M_ManualEra_FireEmissive");
            Assert.That(
                remaps.ContainsKey(fireSourceIdentifier),
                Is.True,
                "The Furnace FBX fire material slot is not remapped.");
            Assert.That(
                remaps[fireSourceIdentifier],
                Is.SameAs(expectedFireMaterial),
                "The Furnace fire remap points to a duplicated or " +
                "incorrect material.");

            Assert.That(
                expectedFireMaterial.shader,
                Is.Not.Null);
            Assert.That(
                expectedFireMaterial.shader.name,
                Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(
                expectedFireMaterial.renderQueue,
                Is.EqualTo(
                    (int)UnityEngine.Rendering.RenderQueue.Geometry));
            Assert.That(
                expectedFireMaterial.GetTag("RenderType", false),
                Is.EqualTo("Opaque"));
            Assert.That(
                expectedFireMaterial.IsKeywordEnabled("_EMISSION"),
                Is.True);
            if (expectedFireMaterial.HasProperty("_Surface"))
            {
                Assert.That(
                    expectedFireMaterial.GetFloat("_Surface"),
                    Is.EqualTo(0f).Within(0.0001f));
            }

            if (expectedFireMaterial.HasProperty("_EmissionColor"))
            {
                Assert.That(
                    expectedFireMaterial
                        .GetColor("_EmissionColor")
                        .maxColorComponent,
                    Is.GreaterThan(1f),
                    "Fire emission must remain HDR.");
            }

            var sharedBaseTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    SharedBaseTexturePath);
            Assert.That(
                sharedBaseTexture,
                Is.Not.Null,
                $"Missing shared Manual Era atlas at " +
                $"{SharedBaseTexturePath}.");
            foreach (var textureProperty in
                     expectedFireMaterial.GetTexturePropertyNames())
            {
                var texture =
                    expectedFireMaterial.GetTexture(textureProperty);
                Assert.That(
                    texture == null || texture == sharedBaseTexture,
                    Is.True,
                    $"Fire material references a private or unexpected " +
                    $"texture in '{textureProperty}'.");
            }
            Assert.That(
                expectedFireMaterial.GetTexture("_BaseMap"),
                Is.SameAs(sharedBaseTexture));
            Assert.That(
                expectedFireMaterial.GetTexture("_EmissionMap"),
                Is.SameAs(sharedBaseTexture));
        }

        [TestCase("Crate")]
        [TestCase("Iron Ingot")]
        [TestCase("Iron Plate")]
        public void RemainingManualEraPropPrefabMatchesAuthoredContract(
            string label)
        {
            var definition = FindSimplePropExpectation(label);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                definition.PrefabPath);
            Assert.That(
                prefab,
                Is.Not.Null,
                $"Missing production prefab at {definition.PrefabPath}.");
            var expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(
                expectedMaterial,
                Is.Not.Null,
                $"Missing shared Manual Era material at {SharedMaterialPath}.");

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                AssertIdentity(instance.transform, definition.PrefabPath);
                Assert.That(
                    instance.name,
                    Does.StartWith(definition.PrefabName));

                var firstMesh = FindRecursive(
                    instance.transform,
                    definition.MeshNames[0]);
                Assert.That(firstMesh, Is.Not.Null);
                var authoredRoot = firstMesh.parent;
                if (authoredRoot != instance.transform)
                {
                    Assert.That(
                        authoredRoot.parent,
                        Is.EqualTo(instance.transform),
                        "Only one direct FBX import wrapper is permitted.");
                    Assert.That(
                        authoredRoot.name,
                        Is.EqualTo(definition.AuthoredRootName));
                }

                foreach (var meshName in definition.MeshNames)
                {
                    Assert.That(
                        CountTransforms(instance.transform, meshName),
                        Is.EqualTo(1),
                        $"Expected one exact {meshName} transform.");
                    var meshTransform = FindRecursive(
                        instance.transform,
                        meshName);
                    Assert.That(
                        meshTransform.parent,
                        Is.EqualTo(authoredRoot));
                    Assert.That(
                        meshTransform.GetComponent<MeshFilter>(),
                        Is.Not.Null);
                    Assert.That(
                        meshTransform.GetComponent<Renderer>(),
                        Is.Not.Null);
                }

                foreach (var marker in definition.Markers)
                {
                    Assert.That(
                        CountTransforms(instance.transform, marker.Name),
                        Is.EqualTo(1),
                        $"Expected one exact {marker.Name} marker.");
                    var markerTransform = FindRecursive(
                        instance.transform,
                        marker.Name);
                    var actualPosition =
                        instance.transform.InverseTransformPoint(
                            markerTransform.position);
                    Assert.That(
                        Vector3.Distance(actualPosition, marker.Position),
                        Is.LessThan(0.006f),
                        $"{definition.Label}/{marker.Name} moved from its " +
                        "authored position.");
                    Assert.That(
                        markerTransform.parent,
                        Is.EqualTo(authoredRoot));
                    Assert.That(
                        markerTransform.GetComponent<Renderer>(),
                        Is.Null);
                    Assert.That(
                        markerTransform.GetComponent<Collider>(),
                        Is.Null);
                }

                foreach (var marker in definition.OrientedMarkers)
                {
                    var markerTransform = FindRecursive(
                        instance.transform,
                        marker.Name);
                    var actualForward =
                        instance.transform.InverseTransformDirection(
                            markerTransform.forward);
                    var actualUp =
                        instance.transform.InverseTransformDirection(
                            markerTransform.up);
                    Assert.That(
                        Vector3.Angle(actualForward, marker.Forward),
                        Is.LessThan(0.1f),
                        $"{definition.Label}/{marker.Name} forward axis is " +
                        $"{actualForward}.");
                    Assert.That(
                        Vector3.Angle(actualUp, Vector3.up),
                        Is.LessThan(0.1f),
                        $"{definition.Label}/{marker.Name} up axis is " +
                        $"{actualUp}.");

                    var markerPosition =
                        instance.transform.InverseTransformPoint(
                            markerTransform.position);
                    foreach (var collider in definition.Colliders)
                    {
                        var colliderBounds = new Bounds(
                            collider.Position,
                            collider.Size);
                        colliderBounds.Expand(-0.004f);
                        Assert.That(
                            colliderBounds.Contains(markerPosition),
                            Is.False,
                            $"{definition.Label}/{collider.Name} encloses " +
                            $"accessible marker {marker.Name}.");
                    }
                }

                var renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                var filters =
                    instance.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(
                    renderers,
                    Has.Length.EqualTo(definition.MeshNames.Length));
                Assert.That(
                    filters,
                    Has.Length.EqualTo(definition.MeshNames.Length));

                foreach (var renderer in renderers)
                {
                    Assert.That(
                        renderer.sharedMaterials,
                        Has.Length.EqualTo(1));
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(expectedMaterial),
                        $"{renderer.name} does not use the shared atlas.");
                }

                long triangleCount = 0;
                foreach (var filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    Assert.That(
                        filter.sharedMesh.subMeshCount,
                        Is.EqualTo(1));
                    Assert.That(
                        filter.sharedMesh.HasVertexAttribute(
                            UnityEngine.Rendering.VertexAttribute.TexCoord0),
                        Is.True,
                        $"{filter.name} has no UV0 coordinates.");
                    triangleCount +=
                        filter.sharedMesh.GetIndexCount(0) / 3L;
                }

                Assert.That(
                    definition.ExpectedTriangleCount,
                    Is.GreaterThan(0),
                    "The frozen topology audit was not recorded.");
                Assert.That(
                    triangleCount,
                    Is.EqualTo(definition.ExpectedTriangleCount),
                    $"{definition.Label} production topology changed.");

                var bounds = CollectBounds(renderers);
                Assert.That(
                    Mathf.Abs(
                        bounds.size.x - definition.ExpectedBounds.x),
                    Is.LessThan(definition.BoundsTolerance));
                Assert.That(
                    Mathf.Abs(
                        bounds.size.y - definition.ExpectedBounds.y),
                    Is.LessThan(definition.BoundsTolerance));
                Assert.That(
                    Mathf.Abs(
                        bounds.size.z - definition.ExpectedBounds.z),
                    Is.LessThan(definition.BoundsTolerance));
                Assert.That(
                    Vector3.Distance(
                        bounds.center,
                        definition.ExpectedBoundsCenter),
                    Is.LessThan(definition.BoundsTolerance));
                Assert.That(
                    Mathf.Abs(bounds.min.y),
                    Is.LessThan(0.002f),
                    $"{definition.Label} is not grounded.");

                var boxColliders =
                    instance.GetComponentsInChildren<BoxCollider>(true);
                Assert.That(
                    boxColliders,
                    Has.Length.EqualTo(definition.Colliders.Length));
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(definition.Colliders.Length),
                    "Only the production BoxColliders are permitted.");

                foreach (var expected in definition.Colliders)
                {
                    Assert.That(
                        CountTransforms(instance.transform, expected.Name),
                        Is.EqualTo(1));
                    var colliderTransform = FindRecursive(
                        instance.transform,
                        expected.Name);
                    Assert.That(
                        Vector3.Distance(
                            colliderTransform.localPosition,
                            expected.Position),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        colliderTransform.localRotation,
                        Is.EqualTo(Quaternion.identity));
                    Assert.That(
                        colliderTransform.localScale,
                        Is.EqualTo(Vector3.one));

                    var collider =
                        colliderTransform.GetComponent<BoxCollider>();
                    Assert.That(collider, Is.Not.Null);
                    Assert.That(
                        Vector3.Distance(
                            collider.center,
                            Vector3.zero),
                        Is.LessThan(0.0001f));
                    Assert.That(
                        Vector3.Distance(
                            collider.size,
                            expected.Size),
                        Is.LessThan(0.0001f));
                    Assert.That(collider.isTrigger, Is.False);
                }

                Assert.That(
                    instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animation>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [TestCase("Crate")]
        [TestCase("Iron Ingot")]
        [TestCase("Iron Plate")]
        public void RemainingManualEraPropImporterAndMaterialRemapMatchContract(
            string label)
        {
            var definition = FindSimplePropExpectation(label);
            var importer = AssetImporter.GetAtPath(
                definition.ModelPath) as ModelImporter;
            Assert.That(
                importer,
                Is.Not.Null,
                $"Missing ModelImporter for {definition.ModelPath}.");
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.useFileScale, Is.True);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(importer.importVisibility, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importConstraints, Is.False);
            Assert.That(
                importer.importNormals,
                Is.EqualTo(ModelImporterNormals.Import));
            Assert.That(
                importer.importTangents,
                Is.EqualTo(ModelImporterTangents.CalculateMikk));
            Assert.That(importer.generateSecondaryUV, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(
                importer.meshCompression,
                Is.EqualTo(ModelImporterMeshCompression.Off));
            Assert.That(importer.optimizeMeshPolygons, Is.True);
            Assert.That(importer.optimizeMeshVertices, Is.True);
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.sortHierarchyByName, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.ImportStandard));
            Assert.That(
                importer.materialLocation,
                Is.EqualTo(ModelImporterMaterialLocation.InPrefab));
            Assert.That(
                importer.materialName,
                Is.EqualTo(ModelImporterMaterialName.BasedOnMaterialName));

            var expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(expectedMaterial, Is.Not.Null);
            var sourceIdentifier =
                new AssetImporter.SourceAssetIdentifier(
                    typeof(Material),
                    "M_ManualEra_OpaqueAtlas");
            var remaps = importer.GetExternalObjectMap();
            Assert.That(
                remaps,
                Has.Count.EqualTo(1),
                $"{definition.Label} must contain exactly one shared " +
                "material remap.");
            Assert.That(
                remaps.ContainsKey(sourceIdentifier),
                Is.True);
            Assert.That(
                remaps[sourceIdentifier],
                Is.SameAs(expectedMaterial));
        }

        [Test]
        public void SharedManualEraAtlasSeparatesIronFromNonMetalSurfaces()
        {
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            var baseTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    SharedBaseTexturePath);
            var maskTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    SharedMaskTexturePath);
            Assert.That(material, Is.Not.Null);
            Assert.That(baseTexture, Is.Not.Null);
            Assert.That(maskTexture, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(
                material.GetTexture("_BaseMap"),
                Is.SameAs(baseTexture));
            Assert.That(
                material.GetTexture("_MetallicGlossMap"),
                Is.SameAs(maskTexture));
            Assert.That(
                material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"),
                Is.True,
                "The shared mask is assigned but its URP keyword is disabled.");
            if (material.HasProperty("_Metallic"))
            {
                Assert.That(
                    material.GetFloat("_Metallic"),
                    Is.EqualTo(1f).Within(0.0001f));
            }

            if (material.HasProperty("_Smoothness"))
            {
                Assert.That(
                    material.GetFloat("_Smoothness"),
                    Is.EqualTo(1f).Within(0.0001f));
            }

            var baseImporter =
                AssetImporter.GetAtPath(
                    SharedBaseTexturePath) as TextureImporter;
            var maskImporter =
                AssetImporter.GetAtPath(
                    SharedMaskTexturePath) as TextureImporter;
            Assert.That(baseImporter, Is.Not.Null);
            Assert.That(maskImporter, Is.Not.Null);
            Assert.That(baseImporter.sRGBTexture, Is.True);
            Assert.That(maskImporter.sRGBTexture, Is.False);
            Assert.That(baseImporter.mipmapEnabled, Is.False);
            Assert.That(maskImporter.mipmapEnabled, Is.False);

            var decodedBase = LoadSourcePng(SharedBaseTexturePath);
            var decodedMask = LoadSourcePng(SharedMaskTexturePath);
            try
            {
                Assert.That(decodedBase.width, Is.EqualTo(512));
                Assert.That(decodedBase.height, Is.EqualTo(512));
                Assert.That(decodedMask.width, Is.EqualTo(512));
                Assert.That(decodedMask.height, Is.EqualTo(512));
                Assert.That(
                    ComputeAtlasRegionHash(decodedBase, includeIron: false),
                    Is.EqualTo(ExpectedNonIronBaseHash),
                    "A shared-atlas edit altered one or more non-iron base " +
                    "swatches used by wood, stone, fibre or cavity surfaces.");
                Assert.That(
                    ComputeAtlasRegionHash(decodedMask, includeIron: false),
                    Is.EqualTo(ExpectedNonIronMaskHash),
                    "A shared-atlas edit altered the non-iron material response.");
                Assert.That(
                    ComputeAtlasRegionHash(decodedBase, includeIron: true),
                    Is.EqualTo(ExpectedIronBaseHash),
                    "The canonical cool-grey iron palette changed.");
                Assert.That(
                    ComputeAtlasRegionHash(decodedMask, includeIron: true),
                    Is.EqualTo(ExpectedIronMaskHash),
                    "The canonical iron metallic/smoothness response changed.");

                var decodedMaskPixels = decodedMask.GetPixels32();
                foreach (var expected in new[]
                         {
                             (
                                 Cell: new Vector2Int(3, 1),
                                 Mask: new Color32(122, 122, 255, 133)),
                             (
                                 Cell: new Vector2Int(0, 0),
                                 Mask: new Color32(102, 112, 255, 143)),
                             (
                                 Cell: new Vector2Int(1, 0),
                                 Mask: new Color32(140, 133, 255, 122))
                         })
                {
                    var pixelX = expected.Cell.x * 128 + 64;
                    var pixelY = expected.Cell.y * 128 + 64;
                    var ironMask =
                        decodedMaskPixels[
                            pixelY * decodedMask.width + pixelX];
                    Assert.That(
                        ironMask,
                        Is.EqualTo(expected.Mask),
                        $"Iron swatch ({expected.Cell.x}," +
                        $"{expected.Cell.y}) differs from the approved mask.");
                    Assert.That(
                        ironMask.r,
                        Is.GreaterThanOrEqualTo(100),
                        $"Iron swatch ({expected.Cell.x}," +
                        $"{expected.Cell.y}) is not metallic " +
                        "enough to read as iron in URP.");
                    Assert.That(
                        ironMask.a,
                        Is.InRange(120, 150),
                        $"Iron swatch ({expected.Cell.x}," +
                        $"{expected.Cell.y}) does not provide a " +
                        "usable smoothness response.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decodedBase);
                UnityEngine.Object.DestroyImmediate(decodedMask);
            }
        }

        private static Bounds CollectBounds(Renderer[] renderers)
        {
            Assert.That(renderers, Is.Not.Empty);
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static Texture2D LoadSourcePng(string assetPath)
        {
            var absolutePath = Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length));
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                true);
            if (!ImageConversion.LoadImage(
                    texture,
                    File.ReadAllBytes(absolutePath),
                    false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                Assert.Fail($"Could not decode source atlas {assetPath}.");
            }

            return texture;
        }

        private static string ComputeAtlasRegionHash(
            Texture2D texture,
            bool includeIron)
        {
            const int cellSize = 128;
            var pixels = texture.GetPixels32();
            using (var stream = new MemoryStream())
            {
                for (var row = 0; row < 4; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        var isIron =
                            row == 1 && column == 3 ||
                            row == 0 && (column == 0 || column == 1);
                        if (isIron != includeIron)
                        {
                            continue;
                        }

                        for (var y = row * cellSize;
                             y < (row + 1) * cellSize;
                             y++)
                        {
                            for (var x = column * cellSize;
                                 x < (column + 1) * cellSize;
                                 x++)
                            {
                                var pixel = pixels[y * texture.width + x];
                                stream.WriteByte(pixel.r);
                                stream.WriteByte(pixel.g);
                                stream.WriteByte(pixel.b);
                                stream.WriteByte(pixel.a);
                            }
                        }
                    }
                }

                stream.Position = 0;
                using (var sha = SHA256.Create())
                {
                    return BitConverter
                        .ToString(sha.ComputeHash(stream))
                        .Replace("-", string.Empty);
                }
            }
        }

        private static int CountTransforms(Transform root, string name)
        {
            var count = string.Equals(root.name, name, StringComparison.Ordinal)
                ? 1
                : 0;
            for (var index = 0; index < root.childCount; index++)
            {
                count += CountTransforms(root.GetChild(index), name);
            }

            return count;
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

        private static SimplePropExpectation FindSimplePropExpectation(
            string label)
        {
            foreach (var definition in RemainingKitProps)
            {
                if (string.Equals(
                        definition.Label,
                        label,
                        StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            Assert.Fail($"Missing simple-prop expectation for {label}.");
            return default;
        }

        private static PortExpectation FindPortExpectation(string name)
        {
            foreach (var port in RequiredFurnacePorts)
            {
                if (string.Equals(
                        port.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    return port;
                }
            }

            Assert.Fail($"Missing port expectation for {name}.");
            return default;
        }

        private static bool SegmentIntersectsBounds(
            Vector3 start,
            Vector3 end,
            Bounds bounds)
        {
            bounds.Expand(-0.004f);
            var delta = end - start;
            var length = delta.magnitude;
            if (length <= 0.0001f)
            {
                return bounds.Contains(start);
            }

            return bounds.IntersectRay(
                       new Ray(start, delta / length),
                       out var distance) &&
                   distance < length - 0.001f;
        }

        private static bool BoundsInteriorsOverlap(
            Bounds first,
            Bounds second)
        {
            first.Expand(-0.004f);
            second.Expand(-0.004f);
            return first.Intersects(second);
        }

        private static void AssertIdentity(
            Transform transform,
            string context)
        {
            Assert.That(transform.localPosition, Is.EqualTo(Vector3.zero), context);
            Assert.That(
                transform.localRotation,
                Is.EqualTo(Quaternion.identity),
                context);
            Assert.That(transform.localScale, Is.EqualTo(Vector3.one), context);
        }

        private readonly struct MarkerExpectation
        {
            public MarkerExpectation(string name, Vector3 position)
            {
                Name = name;
                Position = position;
            }

            public string Name { get; }
            public Vector3 Position { get; }
        }

        private readonly struct PortExpectation
        {
            public PortExpectation(
                string name,
                Vector3 forward)
            {
                Name = name;
                Forward = forward;
            }

            public string Name { get; }
            public Vector3 Forward { get; }
        }

        private readonly struct ColliderExpectation
        {
            public ColliderExpectation(
                string name,
                Vector3 position,
                Vector3 size)
            {
                Name = name;
                Position = position;
                Size = size;
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Size { get; }
        }

        private readonly struct SimplePropExpectation
        {
            public SimplePropExpectation(
                string label,
                string modelPath,
                string prefabPath,
                string prefabName,
                string authoredRootName,
                string[] meshNames,
                MarkerExpectation[] markers,
                PortExpectation[] orientedMarkers,
                ColliderExpectation[] colliders,
                long expectedTriangleCount,
                Vector3 expectedBounds,
                Vector3 expectedBoundsCenter,
                float boundsTolerance)
            {
                Label = label;
                ModelPath = modelPath;
                PrefabPath = prefabPath;
                PrefabName = prefabName;
                AuthoredRootName = authoredRootName;
                MeshNames = meshNames;
                Markers = markers;
                OrientedMarkers = orientedMarkers;
                Colliders = colliders;
                ExpectedTriangleCount = expectedTriangleCount;
                ExpectedBounds = expectedBounds;
                ExpectedBoundsCenter = expectedBoundsCenter;
                BoundsTolerance = boundsTolerance;
            }

            public string Label { get; }
            public string ModelPath { get; }
            public string PrefabPath { get; }
            public string PrefabName { get; }
            public string AuthoredRootName { get; }
            public string[] MeshNames { get; }
            public MarkerExpectation[] Markers { get; }
            public PortExpectation[] OrientedMarkers { get; }
            public ColliderExpectation[] Colliders { get; }
            public long ExpectedTriangleCount { get; }
            public Vector3 ExpectedBounds { get; }
            public Vector3 ExpectedBoundsCenter { get; }
            public float BoundsTolerance { get; }

        }

        private readonly struct ClearanceExpectation
        {
            public ClearanceExpectation(
                string name,
                Vector3 position,
                Vector3 size)
            {
                Name = name;
                Position = position;
                Size = size;
            }

            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Size { get; }
        }
    }
}

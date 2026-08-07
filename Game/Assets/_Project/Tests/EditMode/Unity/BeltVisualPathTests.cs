using System;
using System.Collections.Generic;
using System.Reflection;
using CML.Unity.Presentation.Logistics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Regressioni dell'animazione visiva del piano nastro. La topologia degli
    /// oggetti trasportati è autorevole altrove; qui si provano i listelli e lo
    /// scroll della tela che il giocatore vede.
    /// </summary>
    public sealed class BeltVisualPathTests
    {
        private const string PrefabRoot =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/";
        private const string CanvasMaterialPath =
            "Assets/_Project/Art/Logistics/BeltKit/Materials/"
            + "M_BeltKit_Canvas.mat";
        private const string BattenToken = "_Batten_";

        [Test]
        public void IlistelliDellaSalitaAvanzanoLungoLaPendenzaCompleta()
        {
            var instance = Instantiate("PF_Belt_Incline");
            try
            {
                var visuals = RequireVisuals(instance);
                Invoke(visuals, "CacheBattens");
                var battens = Battens(instance);
                var before = RootPositions(instance.transform, battens);
                var beforeRotations =
                    RootRotations(instance.transform, battens);
                var direction =
                    (before[before.Length - 1] - before[0]).normalized;
                const float travel = 0.08f;

                Invoke(visuals, "AdvanceBattens", travel);

                for (var index = 0; index < battens.Length; index++)
                {
                    var after = instance.transform.InverseTransformPoint(
                        battens[index].position);
                    Assert.That(
                        Vector3.Distance(
                            after,
                            before[index] + direction * travel),
                        Is.LessThan(0.0005f),
                        $"{battens[index].name} non segue il piano inclinato.");

                    var afterRotation =
                        Quaternion.Inverse(instance.transform.rotation)
                        * battens[index].rotation;
                    Assert.That(
                        Quaternion.Angle(
                            beforeRotations[index],
                            afterRotation),
                        Is.LessThan(0.05f),
                        $"{battens[index].name} cambia inclinazione durante "
                        + "una tratta rettilinea.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [TestCase("PF_Belt_Curve")]
        [TestCase("PF_Belt_CurveLeft")]
        public void IlistelliDellaCurvaRestanoRadialiAncheDuranteIlRiciclo(
            string prefabName)
        {
            var instance = Instantiate(prefabName);
            try
            {
                var visuals = RequireVisuals(instance);
                Invoke(visuals, "CacheBattens");
                var battens = Battens(instance);
                var before = RootPositions(instance.transform, battens);
                var beforeRotations =
                    RootRotations(instance.transform, battens);
                FindCircle(
                    ToPlane(before[0]),
                    ToPlane(before[before.Length / 2]),
                    ToPlane(before[before.Length - 1]),
                    out var centre,
                    out var radius);
                const float travel = 0.20f;

                Invoke(visuals, "AdvanceBattens", travel);

                for (var index = 0; index < battens.Length; index++)
                {
                    var after = instance.transform.InverseTransformPoint(
                        battens[index].position);
                    var beforeRadial = new Vector3(
                        before[index].x - centre.x,
                        0f,
                        before[index].z - centre.y).normalized;
                    var afterRadial = new Vector3(
                        after.x - centre.x,
                        0f,
                        after.z - centre.y).normalized;
                    var expectedRotation =
                        Quaternion.FromToRotation(beforeRadial, afterRadial)
                        * beforeRotations[index];
                    var afterRotation =
                        Quaternion.Inverse(instance.transform.rotation)
                        * battens[index].rotation;

                    Assert.That(
                        Mathf.Abs(
                            Vector2.Distance(ToPlane(after), centre) - radius),
                        Is.LessThan(0.0005f),
                        $"{battens[index].name} abbandona l'arco.");
                    Assert.That(
                        Quaternion.Angle(expectedRotation, afterRotation),
                        Is.LessThan(0.05f),
                        $"{battens[index].name} non resta radiale all'arco.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LoScrollDellaTelaÈPerIstanzaENonModificaIlMaterialeCondiviso()
        {
            var forward = Instantiate("PF_Belt_Straight");
            var reverse = Instantiate("PF_Belt_Straight");
            try
            {
                var material =
                    AssetDatabase.LoadAssetAtPath<Material>(CanvasMaterialPath);
                Assert.That(material, Is.Not.Null);
                var originalOffset = material.GetTextureOffset("_BaseMap");

                var forwardVisuals = RequireVisuals(forward);
                var reverseVisuals = RequireVisuals(reverse);
                Invoke(forwardVisuals, "CacheBandRenderers");
                Invoke(reverseVisuals, "CacheBandRenderers");
                Invoke(forwardVisuals, "AdvanceBand", 0.10f);
                Invoke(reverseVisuals, "AdvanceBand", -0.10f);

                Assert.That(
                    material.GetTextureOffset("_BaseMap"),
                    Is.EqualTo(originalOffset),
                    "L'animazione ha sporcato il materiale condiviso.");

                var forwardTransform = TextureTransform(forward, material);
                var reverseTransform = TextureTransform(reverse, material);
                Assert.That(
                    Mathf.Abs(forwardTransform.w - reverseTransform.w),
                    Is.GreaterThan(0.10f),
                    "Due nastri in versi opposti condividono ancora lo "
                    + "stesso offset visivo.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(forward);
                UnityEngine.Object.DestroyImmediate(reverse);
            }
        }

        private static GameObject Instantiate(string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + prefabName + ".prefab");
            Assert.That(prefab, Is.Not.Null, $"Prefab mancante: {prefabName}");
            return UnityEngine.Object.Instantiate(prefab);
        }

        private static BeltVisuals RequireVisuals(GameObject instance)
        {
            var visuals = instance.GetComponent<BeltVisuals>();
            Assert.That(visuals, Is.Not.Null);
            return visuals;
        }

        private static Transform[] Battens(GameObject instance)
        {
            var found = new List<Transform>();
            foreach (var child in
                     instance.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf(
                        BattenToken,
                        StringComparison.Ordinal) >= 0)
                {
                    found.Add(child);
                }
            }

            found.Sort((left, right) =>
                string.CompareOrdinal(left.name, right.name));
            Assert.That(found, Has.Count.GreaterThanOrEqualTo(3));
            return found.ToArray();
        }

        private static Vector3[] RootPositions(
            Transform root,
            IReadOnlyList<Transform> transforms)
        {
            var result = new Vector3[transforms.Count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] =
                    root.InverseTransformPoint(transforms[index].position);
            }

            return result;
        }

        private static Quaternion[] RootRotations(
            Transform root,
            IReadOnlyList<Transform> transforms)
        {
            var result = new Quaternion[transforms.Count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] =
                    Quaternion.Inverse(root.rotation)
                    * transforms[index].rotation;
            }

            return result;
        }

        private static Vector4 TextureTransform(
            GameObject instance,
            Material material)
        {
            foreach (var renderer in
                     instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    if (materials[materialIndex] != material)
                    {
                        continue;
                    }

                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties, materialIndex);
                    return properties.GetVector(
                        Shader.PropertyToID("_BaseMap_ST"));
                }
            }

            Assert.Fail("Renderer della tela non trovato.");
            return default;
        }

        private static void Invoke(
            BeltVisuals target,
            string methodName,
            params object[] arguments)
        {
            var method = typeof(BeltVisuals).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Metodo mancante: {methodName}");
            method.Invoke(target, arguments);
        }

        private static Vector2 ToPlane(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        private static void FindCircle(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out Vector2 centre,
            out float radius)
        {
            var determinant =
                2f * (a.x * (b.y - c.y)
                    + b.x * (c.y - a.y)
                    + c.x * (a.y - b.y));
            Assert.That(
                Mathf.Abs(determinant),
                Is.GreaterThan(0.0001f),
                "I listelli della curva non definiscono un arco.");

            var aSquared = a.sqrMagnitude;
            var bSquared = b.sqrMagnitude;
            var cSquared = c.sqrMagnitude;
            centre = new Vector2(
                (aSquared * (b.y - c.y)
                    + bSquared * (c.y - a.y)
                    + cSquared * (a.y - b.y)) / determinant,
                (aSquared * (c.x - b.x)
                    + bSquared * (a.x - c.x)
                    + cSquared * (b.x - a.x)) / determinant);
            radius = Vector2.Distance(centre, a);
        }
    }
}

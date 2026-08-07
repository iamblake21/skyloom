using CML.Content;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Verifica che la manualità visibile della Curva coincida con quella usata
    /// da posa, trasporto e potenza. I nomi dei due export artistici attuali sono
    /// invertiti: questo test impedisce di ricollegarli ingenuamente e ricreare
    /// una curva che sembra uscire da un lato ma funziona dall'altro.
    /// </summary>
    public sealed class BeltCurveAssetBindingTests
    {
        private const string PrefabRoot =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/";

        [Test]
        public void LeDefinizioniUsanoIlPrefabConLaStessaUscitaVisibile()
        {
            var exportedCurve = Load("PF_Belt_Curve");
            var exportedCurveLeft = Load("PF_Belt_CurveLeft");

            var right = FactoryBuildController.ResolveCurveVisualPrefab(
                ContentIds.BeltCurve,
                exportedCurve,
                exportedCurveLeft);
            var left = FactoryBuildController.ResolveCurveVisualPrefab(
                ContentIds.BeltCurveLeft,
                exportedCurve,
                exportedCurveLeft);

            Assert.That(
                OutputLocalX(right),
                Is.GreaterThan(0.35f),
                "BeltCurve deve mostrare l'uscita a +X, la stessa usata dalla "
                + "topologia per una svolta a destra.");
            Assert.That(
                OutputLocalX(left),
                Is.LessThan(-0.35f),
                "BeltCurveLeft deve mostrare l'uscita a -X, la stessa usata "
                + "dalla topologia per una svolta a sinistra.");
        }

        private static GameObject Load(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, $"Prefab mancante: {name}");
            return prefab;
        }

        private static float OutputLocalX(GameObject prefab)
        {
            foreach (var child in
                     prefab.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("ANM_Roller_Out"))
                {
                    return prefab.transform
                        .InverseTransformPoint(child.position)
                        .x;
                }
            }

            Assert.Fail($"{prefab.name} non contiene ANM_Roller_Out.");
            return 0f;
        }
    }
}

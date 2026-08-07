using CML.Content;
using CML.Foundation;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Il rifiuto che si vede in gioco come preview rosso.
    ///
    /// L'adiacenza logica e la propagazione del moto sono coperte altrove, ma
    /// non bastano: prima di accettare una posa il costruttore confronta gli
    /// ingombri fisici e rifiuta se due moduli si compenetrano oltre un
    /// centimetro. È quel controllo, non l'adiacenza, che teneva rosso il
    /// preview della Curva accanto a un nastro e accanto alla Pressa.
    ///
    /// Qui si montano i prefab veri alle pose della griglia e si applica la
    /// stessa regola del costruttore. Se un modulo torna a sporgere nella cella
    /// del vicino, fallisce qui invece che sotto le mani del giocatore.
    /// </summary>
    public sealed class BeltPhysicalOverlapTests
    {
        private const float ToleranceMetres = 0.01f;
        private const float Cell = 1.0f;

        private const string PrefabRoot =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/";

        private static readonly StableId[] Definitions =
        {
            ContentIds.BeltStraight,
            ContentIds.BeltDriveUnit,
            ContentIds.BeltCurve,
            ContentIds.BeltCurveLeft,
            ContentIds.BeltIncline,
        };

        [TestCase("PF_Belt_Straight", 0, "PF_Belt_Curve", 0, 0f, 1f,
            TestName = "Curva davanti a un rettilineo")]
        [TestCase("PF_Belt_Curve", 0, "PF_Belt_Straight", 1, 1f, 0f,
            TestName = "Rettilineo sull uscita laterale della Curva")]
        [TestCase("PF_Belt_Curve", 2, "PF_Belt_Straight", 3, -1f, 0f,
            TestName = "Curva girata dall altra parte")]
        [TestCase("PF_Belt_Straight", 0, "PF_Belt_Incline", 0, 0f, 1f,
            TestName = "Salita dopo un rettilineo")]
        [TestCase("PF_Belt_Straight", 0, "PF_Belt_Straight", 0, 0f, 1f,
            TestName = "Due rettilinei in fila, il caso che ha sempre funzionato")]
        public void ModuliAdiacentiNonSiCompenetrano(
            string firstPrefab,
            int firstYaw,
            string secondPrefab,
            int secondYaw,
            float offsetX,
            float offsetZ)
        {
            var first = Instantiate(firstPrefab, Vector3.zero, firstYaw);
            var second = Instantiate(
                secondPrefab,
                new Vector3(offsetX * Cell, 0f, offsetZ * Cell),
                secondYaw);
            try
            {
                Assert.That(
                    TryColliderBounds(first, out var boundsFirst),
                    Is.True,
                    $"{firstPrefab} non ha collider da confrontare.");
                Assert.That(
                    TryColliderBounds(second, out var boundsSecond),
                    Is.True,
                    $"{secondPrefab} non ha collider da confrontare.");

                var overlap = OverlapPerAxis(boundsFirst, boundsSecond);
                var intersects = overlap.x > ToleranceMetres
                    && overlap.y > ToleranceMetres
                    && overlap.z > ToleranceMetres;

                Assert.That(
                    intersects,
                    Is.False,
                    $"{firstPrefab} e {secondPrefab} si compenetrano di "
                    + $"({overlap.x * 100f:F1}, {overlap.y * 100f:F1}, "
                    + $"{overlap.z * 100f:F1}) cm: il costruttore rifiuterebbe "
                    + "questa posa e il preview resterebbe rosso.");
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void OgniModuloNastroRestaDentroLaPropriaCella()
        {
            // La causa profonda del rifiuto: un collider che sporge nella cella
            // del vicino rende impossibile qualunque aggancio da quel lato,
            // qualunque cosa dica l'adiacenza. È già successo con la Motrice,
            // rimasta a 1.10 di profondità dopo che la mesh era scesa a 1.00.
            var names = new[]
            {
                "PF_Belt_Straight",
                "PF_Belt_Curve",
                "PF_Belt_Incline",
                "PF_Belt_DriveUnit"
            };

            foreach (var name in names)
            {
                var instance = Instantiate(name, Vector3.zero, 0);
                try
                {
                    Assert.That(
                        TryColliderBounds(instance, out var bounds),
                        Is.True,
                        $"{name} non ha collider.");

                    var limit = Cell * 0.5f + ToleranceMetres;
                    Assert.That(
                        Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)),
                        Is.LessThanOrEqualTo(limit),
                        $"{name} sporge dalla cella in X.");
                    Assert.That(
                        Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z)),
                        Is.LessThanOrEqualTo(limit),
                        $"{name} sporge dalla cella in Z.");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void TutteLeCombinazioniCostruiteConIPrefabRealiNonSiCompenetrano()
        {
            var catalog = BootstrapCatalog.Load();
            foreach (var targetDefinition in Definitions)
            foreach (var heldDefinition in Definitions)
            for (byte targetYaw = 0; targetYaw < 4; targetYaw++)
            {
                var targetId =
                    new StableId(0x504859534943414CUL, targetYaw + 1UL);
                var targetPose = new MachineBuildPose(0, 0, 0, targetYaw);
                var state = new MachineSimulationStateBuilder(catalog)
                    .AddBeltModule(
                        targetId,
                        targetDefinition,
                        targetPose)
                    .Build();
                var targetExit = BeltModuleShape.ForwardExitYaw(
                    targetDefinition,
                    targetYaw);
                var heldPose =
                    FactoryBuildPlacementResolver.ResolveFromTarget(
                        state,
                        targetId,
                        targetPose,
                        MachineBuildKind.BeltModule,
                        targetExit,
                        heldYaw: 0,
                        yawExplicitlyRotated: false,
                        heldDefinitionId: heldDefinition);

                var target = Instantiate(
                    PrefabFor(targetDefinition),
                    WorldPosition(targetPose),
                    targetPose.YawQuarterTurns);
                var held = Instantiate(
                    PrefabFor(heldDefinition),
                    WorldPosition(heldPose),
                    heldPose.YawQuarterTurns);
                try
                {
                    Assert.That(
                        TryColliderBounds(target, out var targetBounds),
                        Is.True);
                    Assert.That(
                        TryColliderBounds(held, out var heldBounds),
                        Is.True);
                    var overlap = OverlapPerAxis(targetBounds, heldBounds);
                    var intersects = overlap.x > ToleranceMetres
                        && overlap.y > ToleranceMetres
                        && overlap.z > ToleranceMetres;
                    Assert.That(
                        intersects,
                        Is.False,
                        $"{targetDefinition} yaw {targetYaw} -> "
                        + $"{heldDefinition}: compenetrazione "
                        + $"({overlap.x:F3}, {overlap.y:F3}, "
                        + $"{overlap.z:F3}) m.");
                }
                finally
                {
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(held);
                }
            }
        }

        private static Vector3 OverlapPerAxis(Bounds left, Bounds right) =>
            new Vector3(
                Mathf.Min(left.max.x, right.max.x) - Mathf.Max(left.min.x, right.min.x),
                Mathf.Min(left.max.y, right.max.y) - Mathf.Max(left.min.y, right.min.y),
                Mathf.Min(left.max.z, right.max.z) - Mathf.Max(left.min.z, right.min.z));

        private static GameObject Instantiate(
            string prefabName,
            Vector3 position,
            int yawQuarterTurns)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + prefabName + ".prefab");
            Assert.That(prefab, Is.Not.Null, $"Prefab mancante: {prefabName}");
            var instance = Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, yawQuarterTurns * 90f, 0f));
            return instance;
        }

        private static GameObject Instantiate(
            GameObject prefab,
            Vector3 position,
            int yawQuarterTurns)
        {
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(0f, yawQuarterTurns * 90f, 0f));
            return instance;
        }

        private static GameObject PrefabFor(StableId definitionId)
        {
            var straight = Load("PF_Belt_Straight");
            var drive = Load("PF_Belt_DriveUnit");
            var incline = Load("PF_Belt_Incline");
            var exportedCurve = Load("PF_Belt_Curve");
            var exportedCurveLeft = Load("PF_Belt_CurveLeft");
            if (definitionId == ContentIds.BeltStraight)
            {
                return straight;
            }

            if (definitionId == ContentIds.BeltDriveUnit)
            {
                return drive;
            }

            if (definitionId == ContentIds.BeltIncline)
            {
                return incline;
            }

            return FactoryBuildController.ResolveCurveVisualPrefab(
                definitionId,
                exportedCurve,
                exportedCurveLeft);
        }

        private static GameObject Load(string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + prefabName + ".prefab");
            Assert.That(prefab, Is.Not.Null, $"Prefab mancante: {prefabName}");
            return prefab;
        }

        private static Vector3 WorldPosition(MachineBuildPose pose)
        {
            return new Vector3(
                pose.XMillimetres / 1_000f,
                pose.YMillimetres / 1_000f,
                pose.ZMillimetres / 1_000f);
        }

        /// <summary>
        /// Ingombro dei BoxCollider, cioè esattamente quello che il costruttore
        /// misura: non i renderer, che darebbero un'altra risposta.
        /// </summary>
        private static bool TryColliderBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            var colliders = instance.GetComponentsInChildren<BoxCollider>(true);
            if (colliders.Length == 0)
            {
                return false;
            }

            var found = false;
            foreach (var collider in colliders)
            {
                var half = collider.size * 0.5f;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = collider.center + new Vector3(
                        (corner & 1) == 0 ? -half.x : half.x,
                        (corner & 2) == 0 ? -half.y : half.y,
                        (corner & 4) == 0 ? -half.z : half.z);
                    var world = collider.transform.TransformPoint(local);
                    if (!found)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        found = true;
                        continue;
                    }

                    bounds.Encapsulate(world);
                }
            }

            return found;
        }
    }
}

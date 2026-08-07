using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Tutti i lati di mira, non solo quello comodo.
    ///
    /// Le prove precedenti miravano sempre al lato d'uscita del modulo — il caso
    /// che avevo in mente scrivendole. Il giocatore invece mira dove capita col
    /// mirino, e il risolutore prende strade che nessun test aveva percorso: è
    /// il motivo per cui la suite era verde mentre in gioco la Curva restava
    /// ferma e mal posata.
    ///
    /// Qui ogni caso parte da un lato di mira diverso e arriva fino in fondo:
    /// la posa la calcola il risolutore, i prefab veri vengono montati a quella
    /// posa, e si verifica sia che gli ingombri non si compenetrino — il
    /// controllo che tiene rosso il preview — sia che il moto attraversi la
    /// svolta.
    /// </summary>
    public sealed class BeltCurveAllAimedSidesTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private const int Cell = MachineSpatialTopology.GridCellSizeMillimetres;
        private const float ToleranceMetres = 0.01f;

        private const string PrefabRoot =
            "Assets/_Project/Art/Logistics/BeltKit/Prefabs/";

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        public void UnaCurvaPosataDaOgniLatoNonCompenetraIlNastroDiPartenza(
            byte aimedSideYaw)
        {
            var straightId = Id(1);
            var straightPose = Pose(0, 0);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(straightId, ContentIds.BeltStraight, straightPose)
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                straightId,
                Pose(0, 1),
                MachineBuildKind.BeltModule,
                aimedSideYaw,
                heldYaw: aimedSideYaw,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            // La posa deve cadere in una cella vicina, mai sopra il bersaglio.
            var deltaX = Mathf.Abs(resolved.XMillimetres - straightPose.XMillimetres);
            var deltaZ = Mathf.Abs(resolved.ZMillimetres - straightPose.ZMillimetres);
            Assert.That(
                deltaX + deltaZ,
                Is.EqualTo(Cell),
                $"Lato {aimedSideYaw}: la Curva non finisce in una cella adiacente.");

            var straight = Instantiate(
                "PF_Belt_Straight",
                WorldPosition(straightPose),
                straightPose.YawQuarterTurns);
            var curve = Instantiate(
                "PF_Belt_Curve",
                WorldPosition(resolved),
                resolved.YawQuarterTurns);
            try
            {
                Assert.That(TryColliderBounds(straight, out var a), Is.True);
                Assert.That(TryColliderBounds(curve, out var b), Is.True);

                var overlap = new Vector3(
                    Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x),
                    Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y),
                    Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z));
                var intersects = overlap.x > ToleranceMetres
                    && overlap.y > ToleranceMetres
                    && overlap.z > ToleranceMetres;

                Assert.That(
                    intersects,
                    Is.False,
                    $"Lato {aimedSideYaw}: la Curva si compenetra col nastro di "
                    + $"({overlap.x * 100f:F1}, {overlap.y * 100f:F1}, "
                    + $"{overlap.z * 100f:F1}) cm, quindi il preview resterebbe "
                    + "rosso e non si potrebbe piazzare.");
            }
            finally
            {
                Object.DestroyImmediate(straight);
                Object.DestroyImmediate(curve);
            }
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        public void UnaCurvaPosataDaOgniLatoRisultaAdiacenteAlNastro(byte aimedSideYaw)
        {
            var straightId = Id(11);
            var straightPose = Pose(0, 0);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(straightId, ContentIds.BeltStraight, straightPose)
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                straightId,
                Pose(0, 1),
                MachineBuildKind.BeltModule,
                aimedSideYaw,
                heldYaw: aimedSideYaw,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    straightPose,
                    ContentIds.BeltStraight,
                    MachineNodeKind.BeltModule,
                    resolved,
                    ContentIds.BeltCurve),
                Is.True,
                $"Lato {aimedSideYaw}: il risolutore posa la Curva dove "
                + "l'adiacenza poi la rifiuta. Le due metà non concordano.");
        }

        [Test]
        public void DallaPressaLaCurvaSiPosaSenzaCompenetrarla()
        {
            var pressId = Id(21);
            var pressPose = Pose(0, 0);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddMachine(
                    pressId,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    pressPose)
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                pressId,
                Pose(0, 1),
                MachineBuildKind.BeltModule,
                aimedSideYaw: 0,
                heldYaw: 0,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.Machine,
                    pressPose,
                    ContentIds.MechanicalPress,
                    MachineNodeKind.BeltModule,
                    resolved,
                    ContentIds.BeltCurve),
                Is.True,
                "La Curva posata all'uscita della Pressa deve risultarle adiacente.");

            // La Pressa è larga quasi due metri e sconfina di suo nelle celle
            // vicine: il piazzamento accanto a lei regge solo perché il bersaglio
            // a cui ci si aggancia viene escluso dal controllo. Qui si verifica
            // che la posa sia comunque quella giusta.
            Assert.That(resolved.ZMillimetres, Is.EqualTo(Cell));
            Assert.That(resolved.XMillimetres, Is.EqualTo(0));
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        public void IlMotoAttraversaLaCurvaPosataDaOgniLato(byte aimedSideYaw)
        {
            var pressId = Id(31);
            var straightId = Id(33);
            var curveId = Id(34);

            // Un rettilineo alimentato dalla Pressa, e la Curva posata mirando
            // al lato in prova.
            var probe = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(straightId, ContentIds.BeltStraight, Pose(0, 1))
                .Build();
            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                probe,
                straightId,
                Pose(0, 2),
                MachineBuildKind.BeltModule,
                aimedSideYaw,
                heldYaw: aimedSideYaw,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            // Solo il lato d'uscita del rettilineo può alimentare: gli altri tre
            // sono ingresso o fianchi, e lì la Curva resta legittimamente ferma.
            if (aimedSideYaw != 0)
            {
                Assert.Pass(
                    $"Lato {aimedSideYaw}: non è il lato d'uscita, nessun moto atteso.");
            }

            var live = new MachineSimulationStateBuilder(_catalog)
                .AddMachine(
                    pressId,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    Pose(0, 0))
                .AddBeltModule(straightId, ContentIds.BeltDriveUnit, Pose(0, 1))
                .AddBeltModule(curveId, ContentIds.BeltCurve, resolved)
                .Store(pressId, ContentIds.IronIngot, 1)
                .Build();

            var engine = new SimulationEngine(
                new SimulationState(
                    new SimulationTick(0UL),
                    Revision,
                    new AirshipSimulationState(),
                    live),
                null,
                _catalog);
            Assert.That(engine.AdvanceOneTick().Committed, Is.True);

            Assert.That(
                engine.State.GetMachineSnapshot().TryGetNode(curveId, out var node),
                Is.True);
            Assert.That(
                node.BeltTravelDirection,
                Is.Not.EqualTo(BeltTravelDirection.Stopped),
                $"Lato {aimedSideYaw}: la Curva posata dal risolutore resta ferma.");
        }

        private static Vector3 WorldPosition(MachineBuildPose pose) =>
            new Vector3(
                pose.XMillimetres / 1000f,
                pose.YMillimetres / 1000f,
                pose.ZMillimetres / 1000f);

        private static MachineBuildPose Pose(int xCell, int zCell, byte yaw = 0) =>
            new MachineBuildPose(
                checked(xCell * Cell),
                0,
                checked(zCell * Cell),
                yaw);

        private static StableId Id(ulong low) =>
            new StableId(0x414C4C5349444500UL, low);

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

        private static bool TryColliderBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            var colliders = instance.GetComponentsInChildren<BoxCollider>(true);
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

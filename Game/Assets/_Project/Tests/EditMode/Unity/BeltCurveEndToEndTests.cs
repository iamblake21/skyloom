using CML.Content;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// L'anello che mancava.
    ///
    /// Le altre prove costruiscono lo stato a mano, con le pose che chi scrive
    /// il test ritiene giuste, e verificano adiacenza e moto su quelle. Passano
    /// senza dire nulla di utile: in gioco le pose non le sceglie nessuno a
    /// mano, le calcola <see cref="FactoryBuildPlacementResolver"/> quando il
    /// giocatore mira e piazza.
    ///
    /// Qui la posa arriva dal risolutore e viene data così com'è alla
    /// simulazione. Se le due metà non parlano la stessa lingua — se il
    /// risolutore posa la Curva dove la topologia non la cerca — è qui che si
    /// vede, e non sotto le mani del giocatore.
    /// </summary>
    public sealed class BeltCurveEndToEndTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private const int Cell = MachineSpatialTopology.GridCellSizeMillimetres;

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void LaPosaCheIlRisolutoreDaAllaCurvaEQuellaCheLaTopologiaCerca()
        {
            // Un rettilineo esistente in (0,1) rivolto a +Z. Si mira al suo lato
            // d'uscita tenendo in mano una Curva: la posa che ne esce è quella
            // che il giocatore otterrebbe cliccando.
            var straightId = Id(1);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(straightId, ContentIds.BeltStraight, Pose(0, 1))
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                straightId,
                Pose(0, 2),
                MachineBuildKind.BeltModule,
                aimedSideYaw: 0,
                heldYaw: 0,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            // Prima verifica: la Curva finisce nella cella davanti al rettilineo.
            Assert.That(resolved.XMillimetres, Is.EqualTo(0));
            Assert.That(
                resolved.ZMillimetres,
                Is.EqualTo(2 * Cell),
                "La Curva deve finire nella cella successiva, non altrove.");

            // Seconda verifica, quella che conta: montata a quella posa, la
            // catena si alimenta davvero fino oltre la svolta.
            var exitYaw = (byte)((resolved.YawQuarterTurns
                + BeltModuleShape.TurnQuarterTurns(ContentIds.BeltCurve)) & 3);
            var afterPose = Offset(resolved, exitYaw);

            var press = Id(10);
            var curve = Id(12);
            var after = Id(13);
            var live = new MachineSimulationStateBuilder(_catalog)
                .AddMachine(
                    press,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    Pose(0, 0))
                .AddBeltModule(straightId, ContentIds.BeltDriveUnit, Pose(0, 1))
                .AddBeltModule(curve, ContentIds.BeltCurve, resolved)
                .AddBeltModule(after, ContentIds.BeltStraight, afterPose)
                .Store(press, ContentIds.IronIngot, 1)
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

            var snapshot = engine.State.GetMachineSnapshot();
            Assert.That(snapshot.TryGetNode(curve, out var curveNode), Is.True);
            Assert.That(
                curveNode.BeltTravelDirection,
                Is.Not.EqualTo(BeltTravelDirection.Stopped),
                "Alla posa che il risolutore stesso produce, la Curva deve "
                + "ricevere il moto. Se resta ferma, risolutore e topologia non "
                + "concordano su dove sta il modulo.");

            Assert.That(snapshot.TryGetNode(after, out var afterNode), Is.True);
            Assert.That(
                afterNode.BeltTravelDirection,
                Is.Not.EqualTo(BeltTravelDirection.Stopped),
                "E il moto deve proseguire oltre la svolta.");
        }

        [Test]
        public void IlRisolutoreOrientaLaCurvaComeIlRettilineoCheLaPrecede()
        {
            var straightId = Id(21);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(straightId, ContentIds.BeltStraight, Pose(0, 1))
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                straightId,
                Pose(0, 2),
                MachineBuildKind.BeltModule,
                aimedSideYaw: 0,
                heldYaw: 0,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            Assert.That(
                resolved.YawQuarterTurns,
                Is.EqualTo(0),
                "Una Curva posata dopo un rettilineo rivolto a +Z deve entrare "
                + "sullo stesso asse: altrimenti il pezzo le arriva di fianco.");
        }

        [Test]
        public void UnaCurvaAlimentataAlContrarioPropagaAiRettilineiCheSeguono()
        {
            // La Pressa si trova a est e spinge verso ovest. Entra quindi dalla
            // testata laterale della Curva, che viene percorsa in Reverse e deve
            // scaricare verso sud. È il caso che la posa trattava ancora come se
            // la Curva fosse sempre percorsa nel verso modellato.
            var press = Id(31);
            var curve = Id(33);
            var after = Id(34);
            var curvePose = Pose(0, 0);
            var reverseProbe = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(
                    curve,
                    ContentIds.BeltCurve,
                    curvePose)
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                reverseProbe,
                curve,
                Pose(0, 1),
                MachineBuildKind.BeltModule,
                aimedSideYaw: 0,
                heldYaw: 0,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltStraight);

            Assert.That(resolved.XMillimetres, Is.EqualTo(0));
            Assert.That(
                resolved.ZMillimetres,
                Is.EqualTo(-Cell),
                "Il rettilineo seguente deve finire sull'uscita reale a sud, "
                + "non sull'uscita geometrica a est.");
            Assert.That(
                resolved.YawQuarterTurns,
                Is.EqualTo(2),
                "Il rettilineo deve ereditare il verso reale con cui il pezzo "
                + "lascia la Curva.");

            var live = new MachineSimulationStateBuilder(_catalog)
                .AddMachine(
                    press,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    Pose(1, 0, 3))
                .AddBeltModule(curve, ContentIds.BeltCurve, curvePose)
                .AddBeltModule(after, ContentIds.BeltDriveUnit, resolved)
                .Store(press, ContentIds.IronIngot, 1)
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

            var snapshot = engine.State.GetMachineSnapshot();
            Assert.That(snapshot.TryGetNode(curve, out var curveNode), Is.True);
            Assert.That(
                curveNode.BeltTravelDirection,
                Is.EqualTo(BeltTravelDirection.Reverse));
            Assert.That(snapshot.TryGetNode(after, out var afterNode), Is.True);
            Assert.That(
                afterNode.BeltTravelDirection,
                Is.Not.EqualTo(BeltTravelDirection.Stopped),
                "La potenza deve uscire dalla Curva e raggiungere il "
                + "rettilineo costruito dopo di lei.");
        }

        [Test]
        public void MirandoIlFiancoDellaPressaLaCurvaVaSuUnaSuaTestata()
        {
            var press = Id(41);
            var pressPose = Pose(0, 0);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddMachine(
                    press,
                    ContentIds.MechanicalPress,
                    ContentIds.PressIronPlate,
                    pressPose)
                .Build();

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                press,
                Pose(1, 0),
                MachineBuildKind.BeltModule,
                aimedSideYaw: 1,
                heldYaw: 0,
                yawExplicitlyRotated: false,
                heldDefinitionId: ContentIds.BeltCurve);

            Assert.That(resolved.XMillimetres, Is.EqualTo(0));
            Assert.That(
                resolved.ZMillimetres,
                Is.EqualTo(Cell),
                "Il fianco largo della Pressa non è una porta: la Curva deve "
                + "agganciarsi alla testata d'uscita.");
            Assert.That(resolved.YawQuarterTurns, Is.EqualTo(0));
        }

        [TestCase(1, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 1)]
        [TestCase(0, -1)]
        public void IlRettilineoDopoLaCurvaNonArretraDiMezzoModulo(
            int xCell,
            int zCell)
        {
            var curvePose = Pose(0, 0);
            var straightPose = Pose(xCell, zCell);
            var expected = new Vector3(xCell, 0f, zCell);

            var aligned =
                FactoryBuildPlacementResolver.ResolveBeltVisualPosition(
                    expected,
                    straightPose,
                    Vector3.zero,
                    curvePose);

            Assert.That(
                aligned.x,
                Is.EqualTo(expected.x).Within(0.0001f),
                "Il rettilineo è arretrato dentro la Curva sull'asse X.");
            Assert.That(
                aligned.z,
                Is.EqualTo(expected.z).Within(0.0001f),
                "Il rettilineo è arretrato dentro la Curva sull'asse Z.");
        }

        private static MachineBuildPose Offset(MachineBuildPose source, byte yaw)
        {
            var x = source.XMillimetres;
            var z = source.ZMillimetres;
            switch (yaw & 3)
            {
                case 0: z += Cell; break;
                case 1: x += Cell; break;
                case 2: z -= Cell; break;
                default: x -= Cell; break;
            }

            return new MachineBuildPose(x, source.YMillimetres, z, (byte)(yaw & 3));
        }

        private static MachineBuildPose Pose(int xCell, int zCell, byte yaw = 0) =>
            new MachineBuildPose(
                checked(xCell * Cell),
                0,
                checked(zCell * Cell),
                yaw);

        private static StableId Id(ulong low) =>
            new StableId(0x4532453245320000UL, low);
    }
}

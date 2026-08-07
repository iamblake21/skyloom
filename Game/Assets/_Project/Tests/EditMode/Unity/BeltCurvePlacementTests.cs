using CML.Content;
using CML.Foundation;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using NUnit.Framework;

namespace CML.Tests.Unity
{
    /// <summary>
    /// L'altra metà del problema: che i moduli non dritti si possano *posare*.
    ///
    /// L'adiacenza pretendeva che entrambi i moduli giacessero sullo stesso asse
    /// della direzione che li univa, e rifiutava qualunque differenza di quota.
    /// Una Curva esiste per cambiare asse e una Salita per cambiare quota, quindi
    /// entrambe cadevano fuori: il preview restava rosso e non si agganciava.
    ///
    /// Queste prove fissano il contratto. Se qualcuno rimette l'assunzione
    /// "ogni nastro è dritto e in piano", falliscono qui invece che in gioco.
    /// </summary>
    public sealed class BeltCurvePlacementTests
    {
        private const int Cell = MachineSpatialTopology.GridCellSizeMillimetres;

        [Test]
        public void UnaCurvaSiAggancieDietroUnRettilineo()
        {
            // Rettilineo in (0,0) rivolto a +Z; Curva nella cella davanti.
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    Pose(0, 0),
                    ContentIds.BeltStraight,
                    MachineNodeKind.BeltModule,
                    Pose(0, 1),
                    ContentIds.BeltCurve),
                Is.True,
                "Una Curva davanti a un rettilineo deve risultare adiacente.");
        }

        [Test]
        public void UnaCurvaSiAggancieAllUscitaDiUnaPressa()
        {
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.Machine,
                    Pose(0, 0),
                    ContentIds.MechanicalPress,
                    MachineNodeKind.BeltModule,
                    Pose(0, 1),
                    ContentIds.BeltCurve),
                Is.True,
                "Il caso che l'utente compone per primo: dalla Pressa alla Curva.");
        }

        [Test]
        public void IlRettilineoRaccoglieLUscitaLateraleDellaCurva()
        {
            // La Curva in (0,1) svolta a destra: il modulo che ne raccoglie
            // l'uscita sta in (1,1) e guarda +X. Prima falliva perché il
            // controllo pretendeva che la Curva stesse sull'asse che la unisce
            // al vicino, cosa vera solo per un modulo dritto.
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    Pose(0, 1),
                    ContentIds.BeltCurve,
                    MachineNodeKind.BeltModule,
                    Pose(1, 1, yaw: 1),
                    ContentIds.BeltStraight),
                Is.True,
                "L'uscita laterale della Curva deve essere un aggancio valido.");
        }

        [Test]
        public void LaCurvaSiAggancieAncheGirataDallAltraParte()
        {
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    Pose(0, 1, yaw: 2),
                    ContentIds.BeltCurve,
                    MachineNodeKind.BeltModule,
                    Pose(-1, 1, yaw: 3),
                    ContentIds.BeltStraight),
                Is.True,
                "Girata di mezzo giro la Curva svolta dall'altra parte, e anche "
                + "quell'uscita deve agganciarsi.");
        }

        [Test]
        public void DopoUnaSalitaIlModuloSuperioreEAdiacente()
        {
            var lower = Pose(0, 1);
            var upper = new MachineBuildPose(
                0,
                BeltModuleShape.InclineRiseMillimetres,
                2 * Cell,
                0);

            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    lower,
                    ContentIds.BeltIncline,
                    MachineNodeKind.BeltModule,
                    upper,
                    ContentIds.BeltStraight),
                Is.True,
                "Il modulo dopo una Salita sta 30 cm più in alto: rifiutare "
                + "qualunque dislivello impediva di proseguire a salire.");
        }

        [Test]
        public void DueRettilineiAQuoteDiverseNonSonoAdiacenti()
        {
            // Il permesso vale solo dove un modulo dichiara di salire: due
            // rettilinei sfalsati in altezza restano non adiacenti, altrimenti
            // si potrebbero comporre percorsi che fluttuano.
            var lower = Pose(0, 1);
            var upper = new MachineBuildPose(
                0,
                BeltModuleShape.InclineRiseMillimetres,
                2 * Cell,
                0);

            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    lower,
                    ContentIds.BeltStraight,
                    MachineNodeKind.BeltModule,
                    upper,
                    ContentIds.BeltStraight),
                Is.False,
                "Senza un modulo che dichiari il dislivello, quote diverse "
                + "devono restare non adiacenti.");
        }


        [Test]
        public void LaCurvaSinistraSvoltaDallaParteOpposta()
        {
            // La sinistra e una mesh specchiata, non la destra ruotata: una
            // destra ruotata resta una destra, perche girano insieme ingresso e
            // uscita. Qui si fissa che le due varianti svoltino davvero al
            // contrario, altrimenti la sinistra non serve a nulla.
            Assert.That(
                BeltModuleShape.TurnQuarterTurns(ContentIds.BeltCurve),
                Is.EqualTo(1),
                "La Curva standard svolta a destra.");
            Assert.That(
                BeltModuleShape.TurnQuarterTurns(ContentIds.BeltCurveLeft),
                Is.EqualTo(3),
                "La variante specchiata deve svoltare dalla parte opposta.");
        }

        [Test]
        public void IlRettilineoRaccoglieLUscitaDellaCurvaSinistra()
        {
            // Curva sinistra in (0,1) rivolta a +Z: esce in -X.
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    Pose(0, 1),
                    ContentIds.BeltCurveLeft,
                    MachineNodeKind.BeltModule,
                    Pose(-1, 1, yaw: 3),
                    ContentIds.BeltStraight),
                Is.True,
                "L uscita della Curva sinistra deve essere un aggancio valido.");
        }

        private static MachineBuildPose Pose(int xCell, int zCell, byte yaw = 0) =>
            new MachineBuildPose(
                checked(xCell * Cell),
                0,
                checked(zCell * Cell),
                yaw);
    }
}

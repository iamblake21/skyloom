using CML.Content;
using CML.Foundation;
using CML.Simulation.Machines;
using CML.Unity.Factory;
using NUnit.Framework;
using UnityEngine;

namespace CML.Tests.Unity
{
    /// <summary>
    /// Matrice geometrica del kit nastro.
    ///
    /// Non prova soltanto il caso rettilineo -> curva visto a schermo: combina
    /// entrambe le estremità di rettilineo, motrice, due curve e salita in ogni
    /// quarto di giro. Ogni posa deve chiudere la stessa estremità sia per la
    /// simulazione sia per il disegno, senza compensazioni visive nascoste.
    /// </summary>
    public sealed class BeltModuleAlignmentMatrixTests
    {
        private static readonly StableId[] Definitions =
        {
            ContentIds.BeltStraight,
            ContentIds.BeltDriveUnit,
            ContentIds.BeltCurve,
            ContentIds.BeltCurveLeft,
            ContentIds.BeltIncline,
        };

        private const int Cell =
            MachineSpatialTopology.GridCellSizeMillimetres;

        private GameCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = BootstrapCatalog.Load();
        }

        [Test]
        public void OgniModuloSiAllineaAValleDiOgniAltroModulo()
        {
            foreach (var targetDefinition in Definitions)
            foreach (var heldDefinition in Definitions)
            for (byte targetYaw = 0; targetYaw < 4; targetYaw++)
            {
                AssertDownstreamPair(
                    targetDefinition,
                    heldDefinition,
                    targetYaw,
                    BeltTravelDirection.Stopped);
                AssertDownstreamPair(
                    targetDefinition,
                    heldDefinition,
                    targetYaw,
                    BeltTravelDirection.Forward);
                AssertDownstreamPair(
                    targetDefinition,
                    heldDefinition,
                    targetYaw,
                    BeltTravelDirection.Reverse);
            }
        }

        [Test]
        public void OgniModuloSiAllineaAncheAMonteDiOgniAltroModulo()
        {
            foreach (var targetDefinition in Definitions)
            foreach (var heldDefinition in Definitions)
            for (byte targetYaw = 0; targetYaw < 4; targetYaw++)
            {
                var targetId = Id(2);
                var targetPose = Pose(0, 0, 0, targetYaw);
                var state = new MachineSimulationStateBuilder(_catalog)
                    .AddBeltModule(
                        targetId,
                        targetDefinition,
                        targetPose)
                    .Build();
                var side = Opposite(targetYaw);
                var resolved =
                    FactoryBuildPlacementResolver.ResolveFromTarget(
                        state,
                        targetId,
                        targetPose,
                        MachineBuildKind.BeltModule,
                        side,
                        heldYaw: 0,
                        yawExplicitlyRotated: false,
                        heldDefinitionId: heldDefinition);

                AssertPair(
                    targetDefinition,
                    targetPose,
                    heldDefinition,
                    resolved,
                    side,
                    $"a monte; target={targetDefinition}; "
                    + $"held={heldDefinition}; yaw={targetYaw}");
                Assert.That(
                    BeltModuleShape.ForwardExitYaw(
                        heldDefinition,
                        resolved.YawQuarterTurns),
                    Is.EqualTo(targetYaw),
                    "L'uscita geometrica del nuovo modulo deve guardare "
                    + "l'ingresso del bersaglio.");
            }
        }

        [Test]
        public void SalitaRuotataInDiscesaAbbassaLaRadiceEChiudeEntrambiILati()
        {
            var upperId = Id(3);
            var upper = Pose(0, 0, 0, yaw: 0);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(upperId, ContentIds.BeltStraight, upper)
                .Build();

            // Il lato alto della Salita, ruotata di 180 gradi, guarda il
            // rettilineo superiore. La radice deve quindi stare 30 cm sotto.
            var incline = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                upperId,
                upper,
                MachineBuildKind.BeltModule,
                aimedSideYaw: 0,
                heldYaw: 2,
                yawExplicitlyRotated: true,
                heldDefinitionId: ContentIds.BeltIncline);

            Assert.That(
                incline.YMillimetres,
                Is.EqualTo(-BeltModuleShape.InclineRiseMillimetres));
            AssertPair(
                ContentIds.BeltStraight,
                upper,
                ContentIds.BeltIncline,
                incline,
                sideFromTargetToHeld: 0,
                "rettilineo superiore -> discesa");

            var lower = Pose(
                0,
                2,
                -BeltModuleShape.InclineRiseMillimetres,
                yaw: 0);
            AssertPair(
                ContentIds.BeltIncline,
                incline,
                ContentIds.BeltStraight,
                lower,
                sideFromTargetToHeld: 0,
                "discesa -> rettilineo inferiore");
        }

        [Test]
        public void UnaCurvaNonPossiedeAgganciSullaMetaOppostaDeiSuoiBracci()
        {
            var curve = Pose(0, 0, 0, yaw: 0);
            var wrongHalf = Pose(0, 1, 0, yaw: 0);

            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    curve,
                    ContentIds.BeltCurve,
                    MachineNodeKind.BeltModule,
                    wrongHalf,
                    ContentIds.BeltStraight),
                Is.False,
                "La Curva destra yaw 0 esce a +X: +Z è il prolungamento "
                + "inesistente del braccio d'ingresso, non un terzo aggancio.");
        }

        private void AssertDownstreamPair(
            StableId targetDefinition,
            StableId heldDefinition,
            byte targetYaw,
            BeltTravelDirection targetDirection)
        {
            var targetId = Id(1);
            var targetPose = Pose(0, 0, 0, targetYaw);
            var state = new MachineSimulationStateBuilder(_catalog)
                .AddBeltModule(
                    targetId,
                    targetDefinition,
                    targetPose)
                .Build();

            var targetExit = BeltModuleShape.ForwardExitYaw(
                targetDefinition,
                targetYaw);
            if (targetDirection == BeltTravelDirection.Reverse)
            {
                targetExit = Opposite(targetYaw);
            }

            var resolved = FactoryBuildPlacementResolver.ResolveFromTarget(
                state,
                targetId,
                targetPose,
                MachineBuildKind.BeltModule,
                targetExit,
                heldYaw: 0,
                yawExplicitlyRotated: false,
                heldDefinitionId: heldDefinition);

            AssertPair(
                targetDefinition,
                targetPose,
                heldDefinition,
                resolved,
                targetExit,
                $"a valle; target={targetDefinition}; held={heldDefinition}; "
                + $"yaw={targetYaw}; direction={targetDirection}");
            Assert.That(
                resolved.YawQuarterTurns,
                Is.EqualTo(targetExit),
                "L'ingresso geometrico del nuovo modulo deve guardare il "
                + "modulo precedente.");
        }

        private static void AssertPair(
            StableId targetDefinition,
            MachineBuildPose target,
            StableId heldDefinition,
            MachineBuildPose held,
            byte sideFromTargetToHeld,
            string context)
        {
            var deltaX = Mathf.Abs(
                held.XMillimetres - target.XMillimetres);
            var deltaZ = Mathf.Abs(
                held.ZMillimetres - target.ZMillimetres);
            Assert.That(
                deltaX + deltaZ,
                Is.EqualTo(Cell),
                $"{context}: le radici devono occupare celle confinanti.");
            Assert.That(
                FactoryBuildPlacementResolver.ArePortsAdjacent(
                    MachineNodeKind.BeltModule,
                    target,
                    targetDefinition,
                    MachineNodeKind.BeltModule,
                    held,
                    heldDefinition),
                Is.True,
                $"{context}: le due estremità fisiche non coincidono.");

            Assert.That(
                BeltModuleShape.TryGetEndpointHeightMillimetres(
                    targetDefinition,
                    target.YawQuarterTurns,
                    sideFromTargetToHeld,
                    out var targetEndpoint),
                Is.True,
                $"{context}: il bersaglio non possiede quel lato.");
            Assert.That(
                BeltModuleShape.TryGetEndpointHeightMillimetres(
                    heldDefinition,
                    held.YawQuarterTurns,
                    Opposite(sideFromTargetToHeld),
                    out var heldEndpoint),
                Is.True,
                $"{context}: il nuovo modulo non guarda il bersaglio.");
            Assert.That(
                target.YMillimetres + targetEndpoint,
                Is.EqualTo(held.YMillimetres + heldEndpoint),
                $"{context}: le superfici di scorrimento hanno quote diverse.");

            var targetWorld = WorldPosition(target);
            var heldWorld = WorldPosition(held);
            var visual = FactoryBuildPlacementResolver.ResolveBeltVisualPosition(
                heldWorld,
                held,
                targetWorld,
                target);
            Assert.That(
                visual,
                Is.EqualTo(heldWorld),
                $"{context}: il disegno non deve richiedere un offset nascosto "
                + "rispetto alla posa autorevole.");
        }

        private static MachineBuildPose Pose(
            int xCell,
            int zCell,
            int yMillimetres,
            byte yaw)
        {
            return new MachineBuildPose(
                checked(xCell * Cell),
                yMillimetres,
                checked(zCell * Cell),
                yaw);
        }

        private static Vector3 WorldPosition(MachineBuildPose pose)
        {
            return new Vector3(
                pose.XMillimetres / 1_000f,
                pose.YMillimetres / 1_000f,
                pose.ZMillimetres / 1_000f);
        }

        private static byte Opposite(byte yaw) => (byte)((yaw + 2) & 3);

        private static StableId Id(ulong low)
        {
            return new StableId(0x414C49474E000000UL, low);
        }
    }
}

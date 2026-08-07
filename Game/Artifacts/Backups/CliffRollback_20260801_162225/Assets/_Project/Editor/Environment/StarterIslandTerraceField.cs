using UnityEngine;

namespace CML.Editor.Art
{
    /// <summary>
    /// Campo di quota della Starter Island, dichiarativo.
    ///
    /// Sostituisce la costruzione precedente, che sommava oltre venti campane
    /// di rumore e poi ci appoggiava sei ripiani con dissolvenze larghe decine
    /// di metri. Quella struttura non poteva produrre un livello leggibile: la
    /// somma di campane morbide è per costruzione una superficie continua, e
    /// una dissolvenza da cinquanta metri non è un bordo. La misura sul campo
    /// vecchio lo diceva: il ripiano del portale dichiarava quota 80 e nel suo
    /// cuore oscillava di 11,63 m, e il prato centrale in quattordici direzioni
    /// su sedici non perdeva otto metri entro duecentosessanta.
    ///
    /// Qui l'isola è una scala di ripiani dichiarati, composti dal più basso al
    /// più alto con un massimo. Un ripiano più alto vince sempre dentro la sua
    /// impronta, quindi ogni ripiano produce da sé la propria parete: alta
    /// quanto il salto e larga quanto <see cref="Terrace.EdgeMetres"/>. Le due
    /// montagne sono pile concentriche, così il profilo legge come una torta a
    /// gradini e non come un dosso.
    ///
    /// Le pareti non vanno dipinte: con un salto di otto metri su quattro di
    /// larghezza la pendenza supera i 60°, e lo strato di roccia del terreno
    /// scatta già sopra i 34°.
    /// </summary>
    public static class StarterIslandTerraceField
    {
        /// <summary>
        /// Un ripiano: impronta ellittica deformata, quota piatta, larghezza
        /// della parete che lo delimita.
        /// </summary>
        public readonly struct Terrace
        {
            public Terrace(
                string name,
                float centerX,
                float centerZ,
                float radiusX,
                float radiusZ,
                float height,
                float edgeMetres,
                float outlinePhase)
            {
                Name = name;
                CenterX = centerX;
                CenterZ = centerZ;
                RadiusX = radiusX;
                RadiusZ = radiusZ;
                Height = height;
                EdgeMetres = edgeMetres;
                OutlinePhase = outlinePhase;
            }

            public string Name { get; }
            public float CenterX { get; }
            public float CenterZ { get; }
            public float RadiusX { get; }
            public float RadiusZ { get; }
            public float Height { get; }
            public float EdgeMetres { get; }
            public float OutlinePhase { get; }
        }

        /// <summary>
        /// La scala, dal basso verso l'alto. L'ordine è vincolante: la
        /// composizione assume che ogni ripiano sia alto almeno quanto i
        /// precedenti, perché è così che la parete di un ripiano scende su
        /// quello che gli sta sotto invece di tagliarlo.
        ///
        /// Le quote sono quelle del progetto di gioco: arrivo 19, tutorial 26,
        /// prato centrale 35, fattoria 43, pozze 62, portale 80, sorgente 82.
        /// I gradini a 30, 50 e 71 sono nuovi e servono a coprire i salti che
        /// altrimenti superavano i venti metri, cioè le pareti invalicabili.
        /// </summary>
        public static readonly Terrace[] Terraces =
        {
            new Terrace(
                "ArrivalShelf", -270f, -190f, 86f, 62f, 19.2f, 4.0f, 0.40f),
            new Terrace(
                "SouthCoast", -40f, -206f, 188f, 62f, 22.5f, 4.5f, 1.15f),
            new Terrace(
                "TutorialShelf", -205f, -150f, 92f, 72f, 26.2f, 6.5f, 2.05f),
            new Terrace(
                "WestShelf", -180f, -25f, 95f, 85f, 30.0f, 7.0f, 3.30f),
            new Terrace(
                "CentralMeadow", -10f, -17f, 152f, 114f, 35.3f, 5.0f, 0.75f),
            new Terrace(
                "FarmShelf", 204f, -126f, 110f, 76f, 43.0f, 4.5f, 4.20f),
            new Terrace(
                "NorthWestRing1", -190f, 105f, 95f, 80f, 50.0f, 8.5f, 1.60f),
            new Terrace(
                "NorthEastRing1", 200f, 75f, 100f, 82f, 50.0f, 8.0f, 5.10f),
            new Terrace(
                "NorthWestRing2", -197f, 122f, 72f, 60f, 62.0f, 7.0f, 2.90f),
            new Terrace(
                "NorthEastRing2", 210f, 92f, 76f, 62f, 62.0f, 7.0f, 0.30f),
            new Terrace(
                "NorthWestRing3", -202f, 134f, 55f, 45f, 71.0f, 5.8f, 3.75f),
            new Terrace(
                "NorthEastRing3", 216f, 104f, 58f, 46f, 71.0f, 5.8f, 1.90f),
            new Terrace(
                "PortalCrown", 220f, 115f, 48f, 36f, 80.0f, 5.2f, 4.60f),
            new Terrace(
                "SpringCrown", -205f, 145f, 40f, 30f, 82.0f, 5.2f, 2.35f)
        };

        /// <summary>
        /// Vero se il punto appartiene alla superficie visibile del ripiano
        /// indicato: dentro la sua impronta e fuori da quelle di tutti i
        /// ripiani più alti, che gli stanno sopra. Serve alla verifica di
        /// planarità: il cuore geometrico del primo anello è coperto dalla
        /// montagna che gli sta sopra, quindi misurarlo misurerebbe la cima.
        /// </summary>
        public static bool IsVisibleTop(
            float worldX,
            float worldZ,
            int terraceIndex)
        {
            if (OutlineDistance(worldX, worldZ, Terraces[terraceIndex]) >= 1f)
            {
                return false;
            }

            for (var above = terraceIndex + 1;
                 above < Terraces.Length;
                 above++)
            {
                if (OutlineDistance(worldX, worldZ, Terraces[above]) < 1f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Quota del terreno nel punto richiesto, senza rotte e senza acqua:
        /// quelle restano compito del chiamante, che le applica dopo.
        /// </summary>
        public static float Evaluate(float worldX, float worldZ)
        {
            // Grembo costiero: la quota di partenza su cui poggia la scala.
            // Appena convesso verso il centro, così il bordo dell'isola non è
            // una lastra piatta, ma resta sotto il primo ripiano a 19,2.
            var apron =
                14.2f +
                (Mathf.PerlinNoise(
                     (worldX + 610f) * 0.0043f,
                     (worldZ + 480f) * 0.0043f) - 0.5f) * 2.4f;
            var height = apron;

            for (var index = 0; index < Terraces.Length; index++)
            {
                var terrace = Terraces[index];
                var distance = OutlineDistance(worldX, worldZ, terrace);
                // La parete è espressa in metri e convertita in unità di
                // impronta sul raggio medio, così un ripiano grande e uno
                // piccolo hanno una parete della stessa larghezza reale.
                var meanRadius =
                    (terrace.RadiusX + terrace.RadiusZ) * 0.5f;
                // A 0.64 m heightmap pitch, a 5-8 m wall only owns a handful
                // of samples. Near-vertical band transitions then expose the
                // underlying triangle fan. Mountain walls receive 12-20 m of
                // honest collision-bearing profile; lower shelves remain
                // compact but still span enough samples for stable normals.
                var featherMetres = index >= 6
                    ? Mathf.Max(28f, terrace.EdgeMetres * 3.20f)
                    : Mathf.Max(7f, terrace.EdgeMetres * 1.45f);
                var feather =
                    Mathf.Max(
                        0.004f,
                        featherMetres / meanRadius);
                var weight = EvaluateSculptedWallWeight(
                    worldX,
                    worldZ,
                    terrace,
                    index,
                    distance,
                    feather);
                if (weight <= 0f)
                {
                    continue;
                }

                var targetHeight = terrace.Height;
                if (index >= 6)
                {
                    var crestAngle = Mathf.Atan2(
                        (worldZ - terrace.CenterZ) /
                        Mathf.Max(terrace.RadiusZ, 0.001f),
                        (worldX - terrace.CenterX) /
                        Mathf.Max(terrace.RadiusX, 0.001f));
                    var crestSignal =
                        Mathf.Sin(
                            crestAngle * 3f +
                            terrace.OutlinePhase * 1.13f) * 0.66f +
                        Mathf.Sin(
                            crestAngle * 5f -
                            terrace.OutlinePhase * 0.71f) * 0.34f;
                    var crestRim = SmootherStep(0.82f, 1.03f, distance);
                    targetHeight += crestSignal * crestRim * 1.15f;
                }

                height = Mathf.Max(
                    height,
                    Mathf.Lerp(height, targetHeight, weight));
            }

            // Grana fine sui ripiani: ±0,15 m, quanto basta perché un piano non
            // sembri un tavolo da biliardo e poco perché resti costruibile e
            // dentro il criterio di mezzo metro di oscillazione.
            height +=
                (Mathf.PerlinNoise(
                     (worldX + 240f) * 0.045f,
                     (worldZ + 175f) * 0.045f) - 0.5f) * 0.30f;
            return height;
        }

        /// <summary>
        /// Distanza normalizzata dal centro del ripiano: uno sul bordo. Il
        /// perimetro è un'ellisse deformata da tre armoniche e da un rumore,
        /// perché un'isola di ellissi perfette si legge come generata a
        /// formula anche quando le quote sono giuste.
        /// </summary>
        public static float OutlineDistance(
            float worldX,
            float worldZ,
            Terrace terrace)
        {
            var deltaX = worldX - terrace.CenterX;
            var deltaZ = worldZ - terrace.CenterZ;
            var unitX = deltaX / terrace.RadiusX;
            var unitZ = deltaZ / terrace.RadiusZ;
            var radius = Mathf.Sqrt(unitX * unitX + unitZ * unitZ);
            if (radius <= 0.0001f)
            {
                return 0f;
            }

            var angle = Mathf.Atan2(unitZ, unitX);
            // Poche masse grandi, non rumore distribuito. Le armoniche a due
            // e tre lobi fanno avanzare e arretrare l'intera parete per
            // decine di metri; viste di fronte diventano spalle e gole che
            // ricevono davvero luce diversa. Le frequenze più alte restano
            // subordinate e servono solo a evitare un contorno da ellisse.
            var outline =
                1f +
                0.115f * Mathf.Sin(
                    angle * 2f + terrace.OutlinePhase * 0.72f) +
                0.068f * Mathf.Sin(
                    angle * 3f - terrace.OutlinePhase * 1.18f) +
                0.028f * Mathf.Sin(
                    angle * 6f + terrace.OutlinePhase * 0.55f) +
                (Mathf.PerlinNoise(
                     terrace.OutlinePhase * 11.3f + Mathf.Cos(angle) * 1.35f,
                     terrace.OutlinePhase * 7.9f + Mathf.Sin(angle) * 1.35f) -
                 0.5f) * 0.055f;
            return radius / outline;
        }

        /// <summary>
        /// Produces a monotonic but visibly stratified cliff profile.
        ///
        /// A single smoother-step across the complete wall makes every terrace
        /// read as one inflated sheet, irrespective of texture quality.  This
        /// profile keeps the authored top and bottom heights exact, but divides
        /// the intervening face into broad shelves separated by short, steeper
        /// risers.  Angular macro lobes shift those shelves in and out so they
        /// break into shoulders and gullies instead of forming contour lines.
        /// Because this function still owns the Terrain height, all readable
        /// ledges remain honest collision rather than collider-free dressing.
        /// </summary>
        private static float EvaluateSculptedWallWeight(
            float worldX,
            float worldZ,
            Terrace terrace,
            int terraceIndex,
            float outlineDistance,
            float feather)
        {
            if (outlineDistance <= 1f)
            {
                return 1f;
            }

            if (outlineDistance >= 1f + feather)
            {
                return 0f;
            }

            var deltaX = worldX - terrace.CenterX;
            var deltaZ = worldZ - terrace.CenterZ;
            var angle = Mathf.Atan2(
                deltaZ / Mathf.Max(terrace.RadiusZ, 0.001f),
                deltaX / Mathf.Max(terrace.RadiusX, 0.001f));
            var progress = Mathf.Clamp01(
                1f - (outlineDistance - 1f) / feather);
            var faceEnvelope = Mathf.Sin(progress * Mathf.PI);

            // Eight-to-thirty metre shoulders and narrow gullies are authored
            // as low-frequency angular lobes.  They alter the collision-bearing
            // face itself, not merely its shading normal.
            var macroShoulders =
                Mathf.Sin(
                    angle * 2f + terrace.OutlinePhase * 1.37f) * 0.58f +
                Mathf.Sin(
                    angle * 3f - terrace.OutlinePhase * 0.73f) * 0.27f +
                Mathf.Sin(
                    angle * 5f + terrace.OutlinePhase * 2.11f) * 0.15f;
            var localBreakup =
                (Mathf.PerlinNoise(
                     terraceIndex * 7.13f + Mathf.Cos(angle) * 2.15f,
                     terrace.OutlinePhase * 5.71f +
                     Mathf.Sin(angle) * 2.15f) - 0.5f) * 2f;
            progress +=
                (macroShoulders * 0.115f + localBreakup * 0.032f) *
                faceEnvelope;
            progress = Mathf.Clamp01(progress);

            // Two or three deliberately broad strata.  Most of each band is a
            // shallow shelf; only its final portion climbs to the next band.
            // The varying transition point and phase prevent repeated rings.
            var strataCount = terraceIndex >= 6
                ? 3
                : 2 + (terraceIndex & 1);
            var phaseWarp =
                (Mathf.Sin(
                     angle * (4f + (terraceIndex % 3)) +
                     terrace.OutlinePhase * 1.91f) * 0.032f +
                 localBreakup * 0.014f) *
                faceEnvelope;
            var stratifiedProgress =
                Mathf.Clamp01(progress + phaseWarp);
            var scaled =
                Mathf.Min(
                    stratifiedProgress * strataCount,
                    strataCount - 0.0001f);
            var band = Mathf.Floor(scaled);
            var withinBand = scaled - band;
            var transitionStart = Mathf.Clamp(
                0.52f + macroShoulders * 0.045f,
                0.45f,
                0.59f);
            var bandRise = SmootherStep(
                transitionStart,
                0.93f,
                withinBand);
            var terraced = (band + bandRise) / strataCount;

            // A small smooth component keeps the 1025 heightfield free of
            // razor-thin spikes while preserving unmistakable ledge plateaus.
            var smooth = SmootherStep(0f, 1f, progress);
            // Ledges occupy broad authored sectors, never a complete green
            // belt around the mountain. The soft angular threshold gives
            // 8-20 m fade lengths on these radii and leaves most sectors as
            // one uninterrupted rock shoulder.
            var sectorAngle =
                angle + terrace.OutlinePhase * 0.71f;
            var shelfSignal =
                Mathf.Sin(
                    sectorAngle * (4f + band) +
                    band * 2.15f) * 0.68f +
                Mathf.Sin(
                    sectorAngle * 7.3f -
                    band * 1.10f +
                    1.20f) * 0.32f;
            var shelfSector = SmootherStep(0.10f, 0.46f, shelfSignal);
            if (terraceIndex == 6)
            {
                // The review-facing wall is composed as three explicit,
                // staggered geological zones: upper fracture beside the
                // waterfall, central shoulder, and a lower eastern shelf.
                // No two bands own the same angular span.
                var bandIndex = Mathf.Clamp((int)band, 0, 2);
                var sectorCenter =
                    new[] { -0.92f, -1.18f, -1.42f }[bandIndex];
                var innerHalfWidth =
                    new[] { 0.09f, 0.08f, 0.07f }[bandIndex];
                var outerHalfWidth =
                    new[] { 0.17f, 0.18f, 0.15f }[bandIndex];
                var angularDelta = Mathf.Abs(
                    Mathf.DeltaAngle(
                        angle * Mathf.Rad2Deg,
                        sectorCenter * Mathf.Rad2Deg) *
                    Mathf.Deg2Rad);
                shelfSector =
                    1f -
                    SmootherStep(
                        innerHalfWidth,
                        outerHalfWidth,
                        angularDelta);
            }
            var strataStrength = terraceIndex >= 6
                ? Mathf.Lerp(0.04f, 0.80f, shelfSector)
                : Mathf.Lerp(0.04f, 0.62f, shelfSector);
            return Mathf.Clamp01(
                Mathf.Lerp(smooth, terraced, strataStrength));
        }

        private static float SmootherStep(float edge0, float edge1, float value)
        {
            if (edge1 - edge0 <= 0.000001f)
            {
                return value < edge0 ? 0f : 1f;
            }

            var t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}

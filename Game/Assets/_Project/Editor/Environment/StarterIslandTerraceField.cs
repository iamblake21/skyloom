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
                var feather =
                    Mathf.Max(0.004f, terrace.EdgeMetres / meanRadius);
                var weight = distance <= 1f
                    ? 1f
                    : distance >= 1f + feather
                        ? 0f
                        : SmootherStep(
                            0f,
                            1f,
                            1f - (distance - 1f) / feather);
                if (weight <= 0f)
                {
                    continue;
                }

                height = Mathf.Max(
                    height,
                    Mathf.Lerp(height, terrace.Height, weight));
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

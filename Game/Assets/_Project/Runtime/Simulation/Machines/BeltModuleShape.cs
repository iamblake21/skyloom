using CML.Content;
using CML.Foundation;

namespace CML.Simulation.Machines
{
    /// <summary>
    /// Cosa fa un modulo nastro al percorso che lo attraversa.
    ///
    /// Fino a qui il modello dava per scontato che ogni nastro fosse dritto e in
    /// piano: l'adiacenza pretendeva stesso asse e stessa quota fra due moduli.
    /// Va bene finché esistono solo rettilinei, ma la Curva serve proprio a
    /// cambiare asse e la Salita proprio a cambiare quota, quindi entrambe
    /// cadevano fuori dal modello — si potevano posare ma non alimentavano
    /// niente, e non era una svista di cablaggio: mancava il concetto.
    ///
    /// Qui ogni definizione dichiara le due sole cose che servono a chiudere il
    /// percorso: di quanto ruota l'uscita rispetto all'ingresso, e di quanto
    /// sale. Tutto il resto della topologia continua a derivare dalla posa.
    /// </summary>
    public static class BeltModuleShape
    {
        /// <summary>Dislivello della Salita, in millimetri. Mezzo piano nastro.</summary>
        public const int InclineRiseMillimetres = 300;

        /// <summary>
        /// Quarti di giro fra ingresso e uscita, percorrendo il modulo in avanti.
        /// Zero per i moduli dritti.
        ///
        /// La Curva vale 1: svolta a destra. Il valore è verificato in gioco, non
        /// dedotto dall'export — l'esportazione FBX ribalta un asse rispetto a
        /// Blender, e leggendo le coordinate del sorgente si ottiene il verso
        /// opposto a quello che poi si vede.
        /// </summary>
        public static int TurnQuarterTurns(StableId definitionId)
        {
            if (definitionId == ContentIds.BeltCurve)
            {
                return 1;
            }

            // 3 equivale a -1: la variante specchiata svolta dalla parte
            // opposta, ed e' l'unico modo di avere entrambe le svolte.
            return definitionId == ContentIds.BeltCurveLeft ? 3 : 0;
        }

        /// <summary>
        /// Direzione mondiale dell'uscita geometrica quando il modulo viene
        /// percorso nel verso in cui è stato modellato.
        /// </summary>
        public static byte ForwardExitYaw(
            StableId definitionId,
            byte placementYaw)
        {
            return (byte)((placementYaw + TurnQuarterTurns(definitionId)) & 3);
        }

        /// <summary>
        /// Risolve ingresso e uscita reali del moto.
        ///
        /// Su un rettilineo il verso inverso è semplicemente l'opposto della
        /// posa. Su una curva, invece, il moto inverso entra dall'uscita
        /// geometrica laterale e lascia il modulo dal suo ingresso geometrico.
        /// </summary>
        public static bool TryResolveTravelYaws(
            StableId definitionId,
            byte placementYaw,
            BeltTravelDirection direction,
            out byte entryYaw,
            out byte exitYaw)
        {
            entryYaw = 0;
            exitYaw = 0;
            if (placementYaw > 3)
            {
                return false;
            }

            var forwardExit = ForwardExitYaw(definitionId, placementYaw);
            switch (direction)
            {
                case BeltTravelDirection.Forward:
                    entryYaw = placementYaw;
                    exitYaw = forwardExit;
                    return true;

                case BeltTravelDirection.Reverse:
                    entryYaw = OppositeYaw(forwardExit);
                    exitYaw = OppositeYaw(placementYaw);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Quanto alza il piano di scorrimento fra ingresso e uscita, percorrendo
        /// il modulo in avanti. Zero per tutti tranne la Salita.
        /// </summary>
        public static int RiseMillimetres(StableId definitionId)
        {
            return definitionId == ContentIds.BeltIncline
                ? InclineRiseMillimetres
                : 0;
        }

        /// <summary>
        /// Restituisce la quota fisica dell'estremità che guarda
        /// <paramref name="sideYaw"/>, relativa alla radice del modulo.
        ///
        /// Non è un controllo "di asse": una Curva possiede due semirette,
        /// non due assi infiniti. Accettare anche il lato opposto di uno dei
        /// bracci faceva agganciare un rettilineo alla metà sbagliata della
        /// Curva. Sulla Salita, inoltre, ingresso e uscita hanno quote diverse.
        /// Questo è quindi il contratto geometrico unico usato da posa,
        /// simulazione e potenza.
        /// </summary>
        public static bool TryGetEndpointHeightMillimetres(
            StableId definitionId,
            byte placementYaw,
            byte sideYaw,
            out int heightMillimetres)
        {
            heightMillimetres = 0;
            if (placementYaw > 3 || sideYaw > 3)
            {
                return false;
            }

            var inputSide = OppositeYaw(placementYaw);
            if (sideYaw == inputSide)
            {
                return true;
            }

            var outputSide = ForwardExitYaw(definitionId, placementYaw);
            if (sideYaw != outputSide)
            {
                return false;
            }

            heightMillimetres = RiseMillimetres(definitionId);
            return true;
        }

        /// <summary>
        /// Posa necessaria perché l'uscita geometrica in avanti guardi nella
        /// direzione richiesta. Serve quando si aggiunge un modulo a monte:
        /// per un rettilineo coincide con l'uscita, per una Curva no.
        /// </summary>
        public static byte PlacementYawForForwardExit(
            StableId definitionId,
            byte exitYaw)
        {
            return (byte)((exitYaw - TurnQuarterTurns(definitionId) + 4) & 3);
        }

        /// <summary>Vero se il modulo non cambia né asse né quota.</summary>
        public static bool IsStraightAndLevel(StableId definitionId)
        {
            return TurnQuarterTurns(definitionId) == 0
                && RiseMillimetres(definitionId) == 0;
        }

        private static byte OppositeYaw(byte yaw) => (byte)((yaw + 2) & 3);
    }
}

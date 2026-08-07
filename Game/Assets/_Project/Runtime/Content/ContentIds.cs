using CML.Foundation;

namespace CML.Content
{
    /// <summary>
    /// Stable, persistence-safe identifiers for the M0 catalog slice.
    /// Values are append-only: renaming a key must never change its identifier.
    /// </summary>
    public static class ContentIds
    {
        public static readonly StableId RawIron = new StableId(0x1000000000000000UL, 0x0000000000000001UL);
        public static readonly StableId IronIngot = new StableId(0x1000000000000000UL, 0x0000000000000002UL);
        public static readonly StableId IronPlate = new StableId(0x1000000000000000UL, 0x0000000000000003UL);

        // Kit della logistica meccanica (ART-002).  Sono oggetti trasportabili e
        // piazzabili: il giocatore li tiene nell'inventario e li costruisce.
        // Il comportamento di trasporto arriva con la Macchina generica e il
        // grafo dei Nastri; qui vengono fissati soltanto gli ID stabili e la
        // rappresentazione in inventario.
        public static readonly StableId BeltStraight = new StableId(0x1000000000000000UL, 0x0000000000000004UL);
        public static readonly StableId BeltCurve = new StableId(0x1000000000000000UL, 0x0000000000000005UL);
        public static readonly StableId BeltSupport = new StableId(0x1000000000000000UL, 0x0000000000000006UL);
        public static readonly StableId BeltFunnel = new StableId(0x1000000000000000UL, 0x0000000000000007UL);
        public static readonly StableId BeltDriveUnit = new StableId(0x1000000000000000UL, 0x0000000000000008UL);
        public static readonly StableId WoodenCrateItem = new StableId(0x1000000000000000UL, 0x0000000000000009UL);
        public static readonly StableId MechanicalPressItem = new StableId(0x1000000000000000UL, 0x000000000000000AUL);
        // 0x...000B e 0x...000C erano assegnati al vecchio Rotore e alla
        // Cinghia rotazionale. Restano riservati e non vengono riutilizzati.
        // Nastro in salita: una cella in pianta, 0.30 m di dislivello. Aggiunto
        // in coda perché gli ID sono stabili e non vanno riordinati.
        public static readonly StableId BeltIncline = new StableId(0x1000000000000000UL, 0x000000000000000DUL);
        // Curva sinistra: mesh specchiata, non la destra ruotata. Ruotando
        // un modulo girano insieme ingresso e uscita, quindi una destra
        // resta destra a qualunque rotazione.
        public static readonly StableId BeltCurveLeft = new StableId(0x1000000000000000UL, 0x000000000000000EUL);
        // Primo utensile realmente equipaggiabile. Viene accodato senza
        // riutilizzare gli ID rimossi della vecchia potenza rotazionale.
        public static readonly StableId CrudePickaxe = new StableId(0x1000000000000000UL, 0x000000000000000FUL);
        // Prime risorse/varianti introdotte dalla raccolta manuale. Gli ID
        // restano in coda per non cambiare mai quelli già pubblicati.
        public static readonly StableId Stone = new StableId(0x1000000000000000UL, 0x0000000000000010UL);
        public static readonly StableId IronPickaxe = new StableId(0x1000000000000000UL, 0x0000000000000011UL);
        // Prima risorsa della filiera del legno. È accodata: gli ID pubblicati
        // non vengono mai riordinati o riutilizzati.
        public static readonly StableId WoodLog = new StableId(0x1000000000000000UL, 0x0000000000000012UL);
        // Oggetto recuperato quando il banco da lavoro viene smontato.
        // Accodato per mantenere stabili tutti gli ID precedenti.
        public static readonly StableId WorkbenchItem = new StableId(0x1000000000000000UL, 0x0000000000000013UL);
        // Prima macchina termica piazzabile. La Fornace usa il ciclo generico
        // delle macchine, con una porta combustibile separata.
        public static readonly StableId CrudeFurnaceItem = new StableId(0x1000000000000000UL, 0x0000000000000014UL);
        // Estrattore meccanico. Si piazza soltanto su un giacimento e produce
        // il grezzo di quel giacimento, quindi non ha ingresso oggetti.
        public static readonly StableId MechanicalDrillItem = new StableId(0x1000000000000000UL, 0x0000000000000015UL);
        // Grezzi degli altri due giacimenti dell'Era Meccanica. Esistono già come
        // oggetti perché è la Trivella a introdurli: il piccone lavora finora
        // soltanto il ferro.
        public static readonly StableId RawCopper = new StableId(0x1000000000000000UL, 0x0000000000000016UL);
        public static readonly StableId RawTin = new StableId(0x1000000000000000UL, 0x0000000000000017UL);
        // Secondo componente della riparazione dell'aeronave. L'oggetto esiste
        // qui prima di essere ottenibile: la distinta di riparazione lo nomina
        // adesso, mentre la catena Rame -> Matassa -> Cavo che lo produce
        // arriva con il proprio ticket. Un ID in coda, mai riordinato.
        public static readonly StableId InsulatedCable = new StableId(0x1000000000000000UL, 0x0000000000000018UL);
        // Prima risorsa raccoglibile a mani nude. Serve al Piccone, quindi
        // rompe il cerchio per cui l'apertura richiedeva già un utensile per
        // ottenere qualunque cosa. ID in coda, mai riordinato.
        public static readonly StableId PlantFiber = new StableId(0x1000000000000000UL, 0x0000000000000019UL);
        // Legna dell'apertura. Un Tronco è il prodotto dell'abbattimento e
        // richiede un utensile; un Bastone si raccoglie da terra a mani nude,
        // ed è quello che rompe il cerchio Piccone -> Tronco -> Piccone.
        public static readonly StableId Stick = new StableId(0x1000000000000000UL, 0x000000000000001AUL);

        public static readonly StableId PressIronPlate = new StableId(0x2000000000000000UL, 0x0000000000000001UL);
        public static readonly StableId CraftCrudePickaxe = new StableId(0x2000000000000000UL, 0x0000000000000002UL);
        public static readonly StableId CraftWoodenCrate = new StableId(0x2000000000000000UL, 0x0000000000000003UL);
        public static readonly StableId WorkbenchIronPlate = new StableId(0x2000000000000000UL, 0x0000000000000004UL);
        public static readonly StableId WorkbenchBeltStraight = new StableId(0x2000000000000000UL, 0x0000000000000005UL);
        public static readonly StableId WorkbenchBeltSupport = new StableId(0x2000000000000000UL, 0x0000000000000006UL);
        public static readonly StableId WorkbenchBeltFunnel = new StableId(0x2000000000000000UL, 0x0000000000000007UL);
        public static readonly StableId WorkbenchMechanicalPress = new StableId(0x2000000000000000UL, 0x0000000000000008UL);
        public static readonly StableId WorkbenchIronPickaxe = new StableId(0x2000000000000000UL, 0x0000000000000009UL);
        public static readonly StableId SmeltIronIngot = new StableId(0x2000000000000000UL, 0x000000000000000AUL);
        public static readonly StableId WorkbenchCrudeFurnace = new StableId(0x2000000000000000UL, 0x000000000000000BUL);
        public static readonly StableId WorkbenchMechanicalDrill = new StableId(0x2000000000000000UL, 0x000000000000000CUL);
        // Una recipe di estrazione per giacimento. La lista è la mappatura
        // giacimento -> minerale: il piazzamento sceglie quella il cui unico
        // prodotto è il grezzo del giacimento sotto la macchina. Aggiungere un
        // minerale domani significa aggiungere una riga qui, non del codice.
        public static readonly StableId DrillRawIron = new StableId(0x2000000000000000UL, 0x000000000000000DUL);
        public static readonly StableId DrillRawCopper = new StableId(0x2000000000000000UL, 0x000000000000000EUL);
        public static readonly StableId DrillRawTin = new StableId(0x2000000000000000UL, 0x000000000000000FUL);
        public static readonly StableId MechanicalPress = new StableId(0x3000000000000000UL, 0x0000000000000001UL);
        public static readonly StableId CrudeFurnace = new StableId(0x3000000000000000UL, 0x0000000000000002UL);
        public static readonly StableId MechanicalDrill = new StableId(0x3000000000000000UL, 0x0000000000000003UL);
        public static readonly StableId WoodenCrate = new StableId(0x4000000000000000UL, 0x0000000000000001UL);
        public static readonly StableId PlayerInventory = new StableId(0x4000000000000000UL, 0x0000000000000002UL);
        // Stiva dell'aeronave. È un contenitore come gli altri: nessuna regola
        // nuova, solo un'altra istanza nella collezione già serializzata.
        public static readonly StableId AirshipHold = new StableId(0x4000000000000000UL, 0x0000000000000003UL);
        // Gli ID 0x500...001 e 0x500...002 appartenevano alle sorgenti
        // rotazionali eliminate e restano riservati.
        public static readonly StableId MeadowIsland = new StableId(0x6000000000000000UL, 0x0000000000000001UL);
        public static readonly StableId HighlandIsland = new StableId(0x6000000000000000UL, 0x0000000000000002UL);
    }
}

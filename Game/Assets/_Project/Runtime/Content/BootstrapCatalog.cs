namespace CML.Content
{
    public static class BootstrapCatalog
    {
        public static CatalogDocument CreateDocument()
        {
            var items = new[]
            {
                new ItemDefinition(ContentIds.RawIron, "item.raw_iron", "item.raw_iron.name", 100),
                new ItemDefinition(ContentIds.IronIngot, "item.iron_ingot", "item.iron_ingot.name", 100),
                new ItemDefinition(ContentIds.IronPlate, "item.iron_plate", "item.iron_plate.name", 100),
                new ItemDefinition(ContentIds.Stone, "item.stone", "item.stone.name", 100),
                new ItemDefinition(ContentIds.WoodLog, "item.wood_log", "item.wood_log.name", 100),
                new ItemDefinition(
                    ContentIds.PlantFiber,
                    "item.plant_fiber",
                    "item.plant_fiber.name",
                    100),
                new ItemDefinition(
                    ContentIds.Stick,
                    "item.stick",
                    "item.stick.name",
                    100),
                new ItemDefinition(
                    ContentIds.WorkbenchItem,
                    "item.workbench",
                    "item.workbench.name",
                    10),
                new ItemDefinition(
                    ContentIds.CrudeFurnaceItem,
                    "item.crude_furnace",
                    "item.crude_furnace.name",
                    10),

                // Kit della logistica meccanica: oggetti piazzabili, quindi pile
                // corte come le strutture e non come i materiali sfusi.
                new ItemDefinition(
                    ContentIds.BeltStraight,
                    "item.belt_straight",
                    "item.belt_straight.name",
                    50),
                new ItemDefinition(ContentIds.BeltCurve, "item.belt_curve", "item.belt_curve.name", 50),
                new ItemDefinition(ContentIds.BeltIncline, "item.belt_incline", "item.belt_incline.name", 50),
                new ItemDefinition(ContentIds.BeltCurveLeft, "item.belt_curve_left", "item.belt_curve_left.name", 50),
                new ItemDefinition(ContentIds.BeltSupport, "item.belt_support", "item.belt_support.name", 50),
                new ItemDefinition(ContentIds.BeltFunnel, "item.belt_funnel", "item.belt_funnel.name", 20),
                new ItemDefinition(
                    ContentIds.BeltDriveUnit,
                    "item.belt_drive_unit",
                    "item.belt_drive_unit.name",
                    20),
                new ItemDefinition(
                    ContentIds.WoodenCrateItem,
                    "item.wooden_crate",
                    "item.wooden_crate.name",
                    20),
                new ItemDefinition(
                    ContentIds.MechanicalPressItem,
                    "item.mechanical_press",
                    "item.mechanical_press.name",
                    10),
                new ItemDefinition(
                    ContentIds.CrudePickaxe,
                    "item.crude_pickaxe",
                    "item.crude_pickaxe.name",
                    1,
                    24),
                // Il modello e la ricetta arrivano con il nodo Utensili in
                // ferro; la definizione esiste già perché MINE-004 fissa ora
                // la regola autorevole dei due colpi e dei 120 utilizzi.
                new ItemDefinition(
                    ContentIds.IronPickaxe,
                    "item.iron_pickaxe",
                    "item.iron_pickaxe.name",
                    1,
                    120),
                new ItemDefinition(
                    ContentIds.MechanicalDrillItem,
                    "item.mechanical_drill",
                    "item.mechanical_drill.name",
                    10),
                new ItemDefinition(ContentIds.RawCopper, "item.raw_copper", "item.raw_copper.name", 100),
                new ItemDefinition(ContentIds.RawTin, "item.raw_tin", "item.raw_tin.name", 100),
                // Nominato dalla distinta di riparazione dell'aeronave. Non ha
                // ancora una ricetta: la catena del rame che lo produce arriva
                // dopo, e fino ad allora il pannello lo mostra correttamente
                // come mancante.
                new ItemDefinition(
                    ContentIds.InsulatedCable,
                    "item.insulated_cable",
                    "item.insulated_cable.name",
                    50)
            };

            var recipes = new[]
            {
                new RecipeDefinition(
                    ContentIds.PressIronPlate,
                    "recipe.press_iron_plate",
                    "recipe.press_iron_plate.name",
                    new[] { new RecipeAmountDefinition(ContentIds.IronIngot, 1) },
                    new[] { new RecipeAmountDefinition(ContentIds.IronPlate, 1) },
                    5000,
                    CraftingStationKind.Machine,
                    RecipeCategory.Materials),
                new RecipeDefinition(
                    ContentIds.CraftCrudePickaxe,
                    "recipe.craft_crude_pickaxe",
                    "recipe.craft_crude_pickaxe.name",
                    // Bastone e non Tronco: il Tronco richiede di abbattere un
                    // albero, che richiede il Piccone che questa ricetta deve
                    // produrre. Tutti e tre gli ingredienti si raccolgono a
                    // mani nude, quindi l'avvio a freddo si chiude.
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.Stone, 2),
                        new RecipeAmountDefinition(ContentIds.Stick, 1),
                        new RecipeAmountDefinition(ContentIds.PlantFiber, 2)
                    },
                    new[] { new RecipeAmountDefinition(ContentIds.CrudePickaxe, 1) },
                    5000,
                    CraftingStationKind.Personal,
                    RecipeCategory.Tools),
                new RecipeDefinition(
                    ContentIds.CraftWoodenCrate,
                    "recipe.craft_wooden_crate",
                    "recipe.craft_wooden_crate.name",
                    new[] { new RecipeAmountDefinition(ContentIds.WoodLog, 4) },
                    new[] { new RecipeAmountDefinition(ContentIds.WoodenCrateItem, 1) },
                    2000,
                    CraftingStationKind.Personal,
                    RecipeCategory.Structures),
                new RecipeDefinition(
                    ContentIds.WorkbenchIronPlate,
                    "recipe.workbench_iron_plate",
                    "recipe.workbench_iron_plate.name",
                    new[] { new RecipeAmountDefinition(ContentIds.IronIngot, 1) },
                    new[] { new RecipeAmountDefinition(ContentIds.IronPlate, 1) },
                    2000,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Materials),
                new RecipeDefinition(
                    ContentIds.WorkbenchBeltStraight,
                    "recipe.workbench_belt_straight",
                    "recipe.workbench_belt_straight.name",
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.IronPlate, 1),
                        new RecipeAmountDefinition(ContentIds.WoodLog, 1)
                    },
                    new[] { new RecipeAmountDefinition(ContentIds.BeltStraight, 2) },
                    2000,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Logistics),
                new RecipeDefinition(
                    ContentIds.WorkbenchBeltSupport,
                    "recipe.workbench_belt_support",
                    "recipe.workbench_belt_support.name",
                    new[] { new RecipeAmountDefinition(ContentIds.IronPlate, 1) },
                    new[] { new RecipeAmountDefinition(ContentIds.BeltSupport, 2) },
                    1500,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Logistics),
                new RecipeDefinition(
                    ContentIds.WorkbenchBeltFunnel,
                    "recipe.workbench_belt_funnel",
                    "recipe.workbench_belt_funnel.name",
                    new[] { new RecipeAmountDefinition(ContentIds.IronPlate, 2) },
                    new[] { new RecipeAmountDefinition(ContentIds.BeltFunnel, 1) },
                    2500,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Logistics),
                new RecipeDefinition(
                    ContentIds.WorkbenchMechanicalPress,
                    "recipe.workbench_mechanical_press",
                    "recipe.workbench_mechanical_press.name",
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.IronPlate, 4),
                        new RecipeAmountDefinition(ContentIds.WoodLog, 2)
                    },
                    new[] { new RecipeAmountDefinition(ContentIds.MechanicalPressItem, 1) },
                    5000,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Machinery),
                new RecipeDefinition(
                    ContentIds.WorkbenchIronPickaxe,
                    "recipe.workbench_iron_pickaxe",
                    "recipe.workbench_iron_pickaxe.name",
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.IronPlate, 2),
                        new RecipeAmountDefinition(ContentIds.WoodLog, 1)
                    },
                    new[] { new RecipeAmountDefinition(ContentIds.IronPickaxe, 1) },
                    4000,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Tools),
                new RecipeDefinition(
                    ContentIds.SmeltIronIngot,
                    "recipe.smelt_iron_ingot",
                    "recipe.smelt_iron_ingot.name",
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.RawIron, 1)
                    },
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.IronIngot, 1)
                    },
                    6000,
                    CraftingStationKind.Machine,
                    RecipeCategory.Materials),
                new RecipeDefinition(
                    ContentIds.WorkbenchCrudeFurnace,
                    "recipe.workbench_crude_furnace",
                    "recipe.workbench_crude_furnace.name",
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.Stone, 8),
                        new RecipeAmountDefinition(ContentIds.WoodLog, 4)
                    },
                    new[]
                    {
                        new RecipeAmountDefinition(
                            ContentIds.CrudeFurnaceItem,
                            1)
                    },
                    5000,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Machinery),
                new RecipeDefinition(
                    ContentIds.WorkbenchMechanicalDrill,
                    "recipe.workbench_mechanical_drill",
                    "recipe.workbench_mechanical_drill.name",
                    new[]
                    {
                        new RecipeAmountDefinition(ContentIds.IronPlate, 6),
                        new RecipeAmountDefinition(ContentIds.WoodLog, 3)
                    },
                    new[] { new RecipeAmountDefinition(ContentIds.MechanicalDrillItem, 1) },
                    6000,
                    CraftingStationKind.Workbench,
                    RecipeCategory.Machinery),

                // Estrazione. Nessun ingrediente: il grezzo viene dal giacimento
                // su cui la macchina è piazzata, non da una porta. Il
                // combustibile non compare qui perché non è un ingrediente della
                // ricetta ma una proprietà della macchina, come per la Fornace.
                //
                // 8000 ms per unità = 7,5 unità/minuto, che è la portata che il
                // GDD assegna all'Estrattore meccanico.
                new RecipeDefinition(
                    ContentIds.DrillRawIron,
                    "recipe.drill_raw_iron",
                    "recipe.drill_raw_iron.name",
                    System.Array.Empty<RecipeAmountDefinition>(),
                    new[] { new RecipeAmountDefinition(ContentIds.RawIron, 1) },
                    8000,
                    CraftingStationKind.Machine,
                    RecipeCategory.Extraction),
                new RecipeDefinition(
                    ContentIds.DrillRawCopper,
                    "recipe.drill_raw_copper",
                    "recipe.drill_raw_copper.name",
                    System.Array.Empty<RecipeAmountDefinition>(),
                    new[] { new RecipeAmountDefinition(ContentIds.RawCopper, 1) },
                    8000,
                    CraftingStationKind.Machine,
                    RecipeCategory.Extraction),
                new RecipeDefinition(
                    ContentIds.DrillRawTin,
                    "recipe.drill_raw_tin",
                    "recipe.drill_raw_tin.name",
                    System.Array.Empty<RecipeAmountDefinition>(),
                    new[] { new RecipeAmountDefinition(ContentIds.RawTin, 1) },
                    8000,
                    CraftingStationKind.Machine,
                    RecipeCategory.Extraction)
            };

            var machines = new[]
            {
                new MachineDefinition(
                    ContentIds.MechanicalPress,
                    "machine.mechanical_press",
                    "machine.mechanical_press.name",
                    1,
                    1,
                    EnergyKind.None,
                    0,
                    new[] { ContentIds.PressIronPlate }),
                new MachineDefinition(
                    ContentIds.CrudeFurnace,
                    "machine.crude_furnace",
                    "machine.crude_furnace.name",
                    1,
                    1,
                    EnergyKind.None,
                    0,
                    new[] { ContentIds.SmeltIronIngot },
                    1,
                    ContentIds.WoodLog,
                    1L),

                // Zero slot d'ingresso: la Trivella pesca dal giacimento, non da
                // una porta. È la sola configurazione che il validatore accetta
                // per una macchina le cui ricette sono tutte di estrazione.
                //
                // Le tre ricette sono la mappatura giacimento -> minerale: al
                // piazzamento ne viene attivata una sola, quella il cui prodotto
                // è il grezzo del giacimento sotto la macchina.
                new MachineDefinition(
                    ContentIds.MechanicalDrill,
                    "machine.mechanical_drill",
                    "machine.mechanical_drill.name",
                    0,
                    1,
                    EnergyKind.None,
                    0,
                    new[]
                    {
                        ContentIds.DrillRawIron,
                        ContentIds.DrillRawCopper,
                        ContentIds.DrillRawTin
                    },
                    1,
                    ContentIds.WoodLog,
                    1L)
            };

            var containers = new[]
            {
                new ContainerDefinition(
                    ContentIds.WoodenCrate,
                    "container.wooden_crate",
                    "container.wooden_crate.name",
                    24,
                    2400),
                new ContainerDefinition(
                    ContentIds.PlayerInventory,
                    "container.player_inventory",
                    "container.player_inventory.name",
                    16,
                    1600),
                // Più capiente dello zaino: la stiva è il motivo per cui vale la
                // pena volare fino a un'isola e tornare carichi.
                new ContainerDefinition(
                    ContentIds.AirshipHold,
                    "container.airship_hold",
                    "container.airship_hold.name",
                    24,
                    4800)
            };

            var energySources = System.Array.Empty<EnergySourceDefinition>();

            var islandTemplates = new[]
            {
                new IslandTemplateDefinition(
                    ContentIds.MeadowIsland,
                    "island.meadow",
                    "island.meadow.name",
                    "biome.meadow",
                    new[] { new IslandResourceDefinition(ContentIds.RawIron, 1, 2) }),
                new IslandTemplateDefinition(
                    ContentIds.HighlandIsland,
                    "island.highland",
                    "island.highland.name",
                    "biome.highland",
                    new[] { new IslandResourceDefinition(ContentIds.RawIron, 4, 6) })
            };

            return new CatalogDocument(
                CatalogSchema.CurrentVersion,
                CatalogSchema.BootstrapContentRevision,
                items,
                recipes,
                machines,
                containers,
                energySources,
                islandTemplates);
        }

        public static GameCatalog Load()
        {
            return CatalogLoader.Load(CreateDocument());
        }
    }
}

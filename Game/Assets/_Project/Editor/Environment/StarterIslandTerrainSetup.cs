using System;
using System.Collections.Generic;
using System.IO;
using CML.Editor.UI;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using CML.Unity.Bootstrap;
using CML.Unity.Factory.Editor;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Wood;
using CML.Unity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Builds the Starter Island landscape with Unity Terrain.
    ///
    /// This pipeline deliberately does not depend on the monolithic Blender
    /// island mesh. Unity Terrain owns walkable morphology, surface painting
    /// and collision. Existing authored models are reused only as decoration.
    /// </summary>
    public static class StarterIslandTerrainSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain";
        public const string TexturesRoot = Root + "/Textures";
        public const string LayersRoot = Root + "/Layers";
        public const string MaterialsRoot = Root + "/Materials";
        public const string DataRoot = Root + "/Data";
        public const string PrefabsRoot = Root + "/Prefabs";
        public const string TerrainDataPath =
            DataRoot + "/TD_StarterIsland.asset";
        public const string PrefabPath =
            PrefabsRoot + "/PF_StarterIsland_Terrain.prefab";
        public const string ReviewScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";
        private const string AirshipPrefabPath =
            "Assets/_Project/Art/Vehicles/Airship/Prefabs/PF_Airship.prefab";

        public const int HeightmapResolution = 1025;
        public const int AlphamapResolution = 1024;
        public const float TerrainWidth = 660f;
        public const float TerrainLength = 500f;
        public const float TerrainHeight = 200f;
        public const float WaterHeight = 26.5f;

        private const string SceneId =
            "cml.environment.starter_island.terrain_review";
        private const int SceneRevision = 6;
        // 256 px ripetuti ogni 14 m davano 18 pixel per metro: a 1,65 m
        // d'occhio qualunque disegno diventa tinta piatta, ed è il motivo per
        // cui erba e terra leggevano come due campiture di pastello.  Con 512
        // px e i nuovi passi di ripetizione si arriva a ~120 px/m.
        private const int SurfaceTextureResolution = 512;
        private const string TerrainObjectName = "TerrainTop";
        private const string ReviewRootName =
            "ENV_StarterIsland_Terrain_Review";
        private const string PlayerName = "AIR_FirstPersonPlayer";

        private static readonly MarkerDefinition[] Markers =
        {
            new MarkerDefinition("REF_PlayerSpawn", -272f, -190f),
            new MarkerDefinition("REF_AirshipDock", -277f, -194f),
            new MarkerDefinition("REF_TutorialCenter", -205f, -158f),
            new MarkerDefinition("REF_FactoryCenter", -12f, -18f),
            new MarkerDefinition("REF_FactoryCorner_SW", -82f, -66f),
            new MarkerDefinition("REF_FactoryCorner_SE", 62f, -66f),
            new MarkerDefinition("REF_FactoryCorner_NW", -82f, 32f),
            new MarkerDefinition("REF_FactoryCorner_NE", 62f, 32f),
            new MarkerDefinition("REF_AgricultureCenter", 200f, -125f),
            new MarkerDefinition("REF_PortalAnchor", 220f, 115f),
            new MarkerDefinition("REF_SpringSource", -205f, 150f),
            new MarkerDefinition("REF_PondCenter", -178f, -72f),
            new MarkerDefinition("REF_WaterfallLip", -202f, 116f),
            new MarkerDefinition("REF_DepositAnchor_Stone", -266f, 28f),
            new MarkerDefinition("REF_DepositAnchor_Iron", 278f, 4f),
            new MarkerDefinition("REF_DepositAnchor_Copper", 150f, 92f),
            new MarkerDefinition("REF_DepositAnchor_Clay", -210f, -102f)
        };

        private static readonly RouteDefinition[] Routes =
        {
            new RouteDefinition(
                "Arrival",
                4.4f,
                8.5f,
                new[]
                {
                    new Vector3(-274f, 18f, -192f),
                    new Vector3(-247f, 20f, -179f),
                    new Vector3(-232f, 23f, -174f),
                    new Vector3(-202f, 26f, -157f),
                    new Vector3(-166f, 29f, -137f),
                    new Vector3(-128f, 32f, -108f),
                    new Vector3(-86f, 34f, -72f),
                    new Vector3(-48f, 35f, -43f),
                    new Vector3(-12f, 35f, -18f)
                }),
            new RouteDefinition(
                "Portal",
                4.2f,
                8.0f,
                new[]
                {
                    new Vector3(26f, 35.5f, -2f),
                    new Vector3(70f, 38f, 14f),
                    new Vector3(112f, 42f, 33f),
                    new Vector3(150f, 46f, 56f),
                    new Vector3(184f, 58f, 82f),
                    new Vector3(208f, 72f, 104f),
                    new Vector3(220f, 80f, 115f)
                }),
            new RouteDefinition(
                "Spring",
                4.2f,
                8.0f,
                new[]
                {
                    new Vector3(-38f, 35.5f, 8f),
                    new Vector3(-76f, 38f, 28f),
                    new Vector3(-112f, 42f, 52f),
                    new Vector3(-145f, 53f, 79f),
                    new Vector3(-174f, 65f, 109f),
                    new Vector3(-194f, 78f, 135f),
                    new Vector3(-205f, 82f, 150f)
                }),
            new RouteDefinition(
                "FarmShelf",
                4.6f,
                8.5f,
                new[]
                {
                    new Vector3(30f, 35.5f, -32f),
                    new Vector3(72f, 37f, -48f),
                    new Vector3(116f, 39f, -70f),
                    new Vector3(158f, 41f, -96f),
                    new Vector3(200f, 43f, -125f),
                    new Vector3(244f, 43.5f, -132f)
                })
        };

        // Le rotte dell'acqua scavano i canali nel terreno e devono coincidere
        // con i nastri generati da StarterIslandWaterBuilder: se divergono,
        // l'acqua poggia su terra non scavata. Ogni tratto piatto corre sul suo
        // ripiano, ogni salto attraversa la parete fra due ripiani.
        private static readonly Vector3[] CrownCreekRoute =
            BuildBezierRoute(
                new Vector3(-204.4f, 81.96f, 138.0f),
                new Vector3(-206.8f, 81.90f, 131.0f),
                new Vector3(-202.4f, 81.86f, 123.0f),
                new Vector3(-201.0f, 81.82f, 116.5f),
                10);

        private static readonly Vector3[] CrownFallRoute =
            BuildBezierRoute(
                new Vector3(-201.0f, 81.80f, 116.2f),
                new Vector3(-200.6f, 80.20f, 114.2f),
                new Vector3(-199.4f, 72.60f, 112.6f),
                new Vector3(-199.0f, 71.05f, 110.8f),
                10);

        private static readonly Vector3[] ThirdCreekRoute =
            BuildBezierRoute(
                new Vector3(-197.6f, 70.96f, 100.0f),
                new Vector3(-199.2f, 70.92f, 96.5f),
                new Vector3(-195.4f, 70.88f, 93.0f),
                new Vector3(-196.0f, 70.84f, 90.4f),
                8);

        private static readonly Vector3[] ThirdFallRoute =
            BuildBezierRoute(
                new Vector3(-196.0f, 70.82f, 90.1f),
                new Vector3(-195.8f, 69.40f, 88.4f),
                new Vector3(-195.2f, 63.40f, 87.0f),
                new Vector3(-195.0f, 62.05f, 85.2f),
                10);

        private static readonly Vector3[] MiddleCreekRoute =
            BuildBezierRoute(
                new Vector3(-193.4f, 61.96f, 73.4f),
                new Vector3(-195.0f, 61.92f, 70.0f),
                new Vector3(-190.8f, 61.88f, 66.4f),
                new Vector3(-192.0f, 61.84f, 63.4f),
                8);

        private static readonly Vector3[] MiddleFallRoute =
            BuildBezierRoute(
                new Vector3(-192.0f, 61.82f, 63.1f),
                new Vector3(-191.6f, 60.20f, 61.4f),
                new Vector3(-190.6f, 51.60f, 60.0f),
                new Vector3(-190.0f, 50.05f, 58.2f),
                10);

        private static readonly Vector3[] ShelfCreekRoute =
            BuildBezierRoute(
                new Vector3(-187.2f, 49.96f, 42.0f),
                new Vector3(-189.0f, 49.92f, 37.0f),
                new Vector3(-184.4f, 49.88f, 32.0f),
                new Vector3(-186.0f, 49.84f, 27.4f),
                8);

        private static readonly Vector3[] MainFallRoute =
            BuildBezierRoute(
                new Vector3(-186.0f, 49.82f, 27.1f),
                new Vector3(-185.4f, 47.40f, 25.0f),
                new Vector3(-183.8f, 32.60f, 22.6f),
                new Vector3(-183.0f, 30.10f, 20.0f),
                14);

        private static readonly Vector3[] LowerCreekRoute =
            BuildBezierRoute(
                new Vector3(-183.0f, 29.96f, 19.6f),
                new Vector3(-188.0f, 29.40f, -2.0f),
                new Vector3(-172.0f, 27.80f, -24.0f),
                new Vector3(-178.4f, 26.58f, -45.0f),
                16);

        private static readonly Vector3[] StreamRoute =
            BuildCombinedWaterRoute();

        /// <summary>
        /// Le quattro conche d'acqua, in una tabella sola. La geometria della
        /// conca e la fascia di battigia dello splat leggono da qui, così non
        /// possono più divergere: prima il lago grande aveva riva e sabbia
        /// mentre le tre pozze a monte no, ed erano profonde 26 cm sul bordo
        /// sopra erba verde, cioè sembravano vetro appoggiato sul prato.
        /// I valori di superficie devono restare allineati a
        /// <see cref="StarterIslandWaterBuilder"/>, che genera i dischi.
        /// </summary>
        private readonly struct WaterBasinFootprint
        {
            public WaterBasinFootprint(
                float centerX,
                float centerZ,
                float radiusX,
                float radiusZ,
                float surfaceHeight,
                float depth,
                float shoreWidth,
                float shoreRise)
            {
                CenterX = centerX;
                CenterZ = centerZ;
                RadiusX = radiusX;
                RadiusZ = radiusZ;
                SurfaceHeight = surfaceHeight;
                Depth = depth;
                ShoreWidth = shoreWidth;
                ShoreRise = shoreRise;
            }

            public float CenterX { get; }
            public float CenterZ { get; }
            public float RadiusX { get; }
            public float RadiusZ { get; }
            public float SurfaceHeight { get; }
            public float Depth { get; }
            public float ShoreWidth { get; }
            public float ShoreRise { get; }
        }

        /// <summary>
        /// Le pozze stanno sui ripiani della scala, alla quota del ripiano che
        /// le ospita, e i salti cadono dalle pareti fra un ripiano e l'altro.
        /// Prima le quote erano indipendenti dalla forma del terreno: una pozza
        /// a 43 su un fianco a quota 50 diventava un cratere, e una cascata che
        /// scendeva su un pendio liscio non aveva un salto da cui cadere, il
        /// che è il motivo per cui leggeva come un foglio sospeso.
        ///
        /// Catena: sorgente 82 sulla corona, 71 sul terzo anello, 62 sul
        /// secondo, 50 sul primo, poi il lago a 26,5 nella conca del ripiano
        /// occidentale a quota 30.
        /// </summary>
        private static readonly WaterBasinFootprint[] WaterBasins =
        {
            new WaterBasinFootprint(
                -178f, -72f, 40f, 27f, WaterHeight, 2.15f, 0.42f, 1.70f),
            new WaterBasinFootprint(
                -205f, 145f, 10.0f, 7.2f, 82.00f, 1.05f, 0.46f, 0.85f),
            new WaterBasinFootprint(
                -198f, 106f, 9.5f, 6.8f, 71.00f, 1.05f, 0.46f, 0.85f),
            new WaterBasinFootprint(
                -194f, 80f, 10.5f, 7.4f, 62.00f, 1.15f, 0.46f, 0.85f),
            new WaterBasinFootprint(
                -188f, 50f, 11.5f, 8.5f, 50.00f, 1.10f, 0.46f, 0.85f)
        };

        private static readonly DecorationCluster[] CommonTreeClusters =
        {
            new DecorationCluster(-276f, -104f, 42f, 70f),
            new DecorationCluster(-158f, -128f, 60f, 42f),
            new DecorationCluster(-238f, -32f, 38f, 42f),
            new DecorationCluster(-258f, 112f, 62f, 80f),
            new DecorationCluster(-206f, 194f, 70f, 45f),
            new DecorationCluster(-142f, 132f, 58f, 62f),
            new DecorationCluster(-112f, 92f, 44f, 34f),
            new DecorationCluster(-64f, 82f, 72f, 38f),
            new DecorationCluster(92f, 96f, 46f, 34f),
            new DecorationCluster(154f, 132f, 54f, 60f),
            new DecorationCluster(244f, 158f, 60f, 44f),
            new DecorationCluster(282f, 8f, 30f, 94f),
            new DecorationCluster(228f, -92f, 58f, 46f),
            new DecorationCluster(166f, -150f, 48f, 30f)
        };

        private static readonly DecorationCluster[]
            OpenWoodlandTreeClusters =
        {
            // Sparse copses fill the previously empty southern half while
            // preserving the central factory clearing and the main routes.
            new DecorationCluster(-62f, -146f, 50f, 27f),
            new DecorationCluster(41f, -158f, 58f, 34f),
            new DecorationCluster(78f, -211f, 84f, 25f),
            new DecorationCluster(12f, 188f, 76f, 28f)
        };

        private static readonly DecorationCluster[] AutumnTreeClusters =
        {
            new DecorationCluster(236f, 108f, 72f, 66f),
            new DecorationCluster(112f, 164f, 86f, 40f),
            new DecorationCluster(246f, -38f, 60f, 68f)
        };

        private static readonly LayerDefinition[] LayerDefinitions =
        {
            // Passi di ripetizione stretti: la densità di texel conta più della
            // dimensione del disegno.  Due erbe con passi diversi (4.0 e 4.6)
            // evitano che la ripetizione dei due strati vada in fase.
            // I due verdi erano quasi indistinguibili, quindi a mezza distanza
            // il prato tornava una campitura unica anche con le chiazze: oltre
            // i venti metri il mip-mapping media la grana e la variazione può
            // arrivare solo dal contrasto fra gli strati. Ora GrassSun è più
            // caldo e GrassDeep più freddo e profondo.
            new LayerDefinition(
                "GrassSun",
                "#7A9440",
                "#96AC55",
                "#4E6B31",
                4.0f,
                0.02f),
            new LayerDefinition(
                "GrassDeep",
                "#3C5A34",
                "#4F6D3E",
                "#293F26",
                4.6f,
                0.015f),
            new LayerDefinition(
                "DirtPath",
                "#E1AA7B",
                "#F0C499",
                "#BC7D55",
                3.2f,
                0.01f),
            // Terracotta corallo: il valore medio resta leggibile nelle conche,
            // mentre il viola-bruno delle ombre e l'arancio delle creste
            // riprendono la reference senza trasformare la parete in sabbia.
            new LayerDefinition(
                "CliffWarm",
                "#976D65",
                "#BF8E78",
                "#675660",
                10.0f,
                0.012f)
        };

        /// <summary>
        /// Misura, senza modificare nulla, quanto i ripiani dichiarati siano
        /// davvero piatti e quanto siano larghi i loro bordi. Serve a decidere
        /// il terrazzamento su numeri invece che su impressioni: la quota di un
        /// ripiano è dichiarata nel codice, ma viene applicata con una
        /// miscelazione parziale e una dissolvenza molto ampia, e questo
        /// misura l'effetto di entrambe.
        /// </summary>
        [MenuItem("CML/Art/Audit Plateau Readability")]
        public static void AuditPlateauReadability()
        {
            // La sonda legge la scala dichiarata, così non può divergere dalla
            // forma: era proprio la divergenza fra dati duplicati ad avere
            // già prodotto due difetti in questa scena.
            var declared = StarterIslandTerraceField.Terraces;
            var plateaus = new PlateauProbe[declared.Length];
            for (var index = 0; index < declared.Length; index++)
            {
                var terrace = declared[index];
                plateaus[index] = new PlateauProbe(
                    terrace.Name,
                    terrace.CenterX,
                    terrace.CenterZ,
                    terrace.RadiusX,
                    terrace.RadiusZ,
                    terrace.Height);
            }

            for (var index = 0; index < plateaus.Length; index++)
            {
                var plateau = plateaus[index];
                var minimum = float.PositiveInfinity;
                var maximum = float.NegativeInfinity;
                var total = 0.0;
                var samples = 0;
                // Solo la superficie visibile: dentro l'impronta di questo
                // ripiano e fuori da quelle dei ripiani più alti. Misurare il
                // cuore geometrico del primo anello misurerebbe la cima della
                // montagna che gli sta sopra, e infatti dava 70,50 su 50
                // dichiarati.
                for (var step = -14; step <= 14; step++)
                {
                    for (var lateral = -14; lateral <= 14; lateral++)
                    {
                        var sampleX =
                            plateau.CenterX +
                            plateau.RadiusX * 0.92f * (step / 14f);
                        var sampleZ =
                            plateau.CenterZ +
                            plateau.RadiusZ * 0.92f * (lateral / 14f);
                        if (!StarterIslandTerraceField.IsVisibleTop(
                                sampleX,
                                sampleZ,
                                index))
                        {
                            continue;
                        }

                        // Rampe e acqua sono in pendenza per progetto: se
                        // restano nel campione misurano il dislivello della
                        // rampa e non la planarità del ripiano. Sul primo
                        // anello nord-est la rotta del portale sale da 50 a 80
                        // e da sola produceva 36 m di oscillazione.
                        if (IsInsideRampCorridor(sampleX, sampleZ) ||
                            IsNearWater(sampleX, sampleZ))
                        {
                            continue;
                        }

                        var sampled = EvaluateHeight(sampleX, sampleZ);
                        minimum = Mathf.Min(minimum, sampled);
                        maximum = Mathf.Max(maximum, sampled);
                        total += sampled;
                        samples++;
                    }
                }

                if (samples == 0)
                {
                    Debug.Log(
                        $"PLATEAU_AUDIT name={plateau.Name} " +
                        "visibleSurface=none");
                    continue;
                }

                // Larghezza del bordo, misurata in sedici direzioni: quanti
                // metri servono perché la quota si stacchi di un metro e poi
                // di otto. Una sola direzione non misura il bordo, misura la
                // direzione scelta: a est di più ripiani il terreno risale
                // verso una collina e la discesa non arriva mai.
                var centreHeight = EvaluateHeight(
                    plateau.CenterX,
                    plateau.CenterZ);
                var widths = new List<float>();
                var unbounded = 0;
                for (var spoke = 0; spoke < 16; spoke++)
                {
                    var angle = spoke * Mathf.PI * 2f / 16f;
                    var directionX = Mathf.Cos(angle);
                    var directionZ = Mathf.Sin(angle);
                    var firstDrop = -1f;
                    var eighthDrop = -1f;
                    for (var metre = 0; metre <= 260; metre++)
                    {
                        var sampled = EvaluateHeight(
                            plateau.CenterX + directionX * metre,
                            plateau.CenterZ + directionZ * metre);
                        var drop = centreHeight - sampled;
                        if (firstDrop < 0f && drop >= 1f)
                        {
                            firstDrop = metre;
                        }

                        if (firstDrop >= 0f && drop >= 8f)
                        {
                            eighthDrop = metre;
                            break;
                        }
                    }

                    if (firstDrop >= 0f && eighthDrop >= 0f)
                    {
                        widths.Add(eighthDrop - firstDrop);
                    }
                    else
                    {
                        unbounded++;
                    }
                }

                widths.Sort();
                var median = widths.Count == 0
                    ? -1f
                    : widths[widths.Count / 2];
                var narrowest = widths.Count == 0 ? -1f : widths[0];
                Debug.Log(
                    $"PLATEAU_AUDIT name={plateau.Name} " +
                    $"declared={plateau.DeclaredHeight:F2} " +
                    $"coreSpread={maximum - minimum:F2} " +
                    $"coreMean={total / samples:F2} " +
                    $"edgeMedian={median:F0} edgeNarrowest={narrowest:F0} " +
                    $"spokesWithoutDescent={unbounded}/16");
            }
        }

        private readonly struct PlateauProbe
        {
            public PlateauProbe(
                string name,
                float centerX,
                float centerZ,
                float radiusX,
                float radiusZ,
                float declaredHeight)
            {
                Name = name;
                CenterX = centerX;
                CenterZ = centerZ;
                RadiusX = radiusX;
                RadiusZ = radiusZ;
                DeclaredHeight = declaredHeight;
            }

            public string Name { get; }
            public float CenterX { get; }
            public float CenterZ { get; }
            public float RadiusX { get; }
            public float RadiusZ { get; }
            public float DeclaredHeight { get; }
        }

        /// <summary>
        /// Ricostruisce e rende in una sola invocazione dell'editor.
        ///
        /// Lanciare due volte Unity in batchmode costa due domain reload e due
        /// import completi, cioè quattro o sei minuti di avvio per un minuto di
        /// lavoro effettivo. Unendoli il ciclo si dimezza.
        /// </summary>
        [MenuItem("CML/Art/Rebuild And Render Starter Island Terrain")]
        public static void RunAndRender()
        {
            Run();
            RenderFpsSuite();
        }

        [MenuItem("CML/Art/Rebuild Starter Island Terrain")]
        public static void Run()
        {
            StarterIslandV4TreeSetup.Run();
            EnsureFolders();
            StarterIslandLeafyBushSetup.Run();
            StarterIslandPortalSetup.Run();
            var layers = BuildTerrainLayers();
            var terrainData = BuildTerrainData(layers);
            var prefab = BuildTerrainPrefab(terrainData);
            BuildReviewScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Application.isBatchMode)
            {
                Selection.activeObject = prefab;
            }

            Debug.Log(
                $"STARTER_ISLAND_TERRAIN_BUILD data={TerrainDataPath} " +
                $"prefab={PrefabPath} scene={ReviewScenePath} " +
                $"heightmap={HeightmapResolution} " +
                $"alphamap={AlphamapResolution} " +
                $"layers={layers.Length} collider=Unity.TerrainCollider " +
                "blenderDependency=0 status=PASS");
        }

        /// <summary>
        /// Rigenera esclusivamente texture, TerrainLayer e materiale del
        /// terreno. Non riscrive heightmap, vegetazione, prefab o scena.
        /// </summary>
        [MenuItem("CML/Art/Rebuild Starter Island Terrain Surface Only")]
        public static void RunSurfaceOnly()
        {
            EnsureFolders();
            var layers = BuildTerrainLayers();
            var data =
                AssetDatabase.LoadAssetAtPath<TerrainData>(
                    TerrainDataPath);
            if (data == null)
            {
                throw new FileNotFoundException(
                    $"Starter Island TerrainData is missing: " +
                    TerrainDataPath);
            }

            RepaintTerrainLayers(data, layers);
            BuildTerrainMaterial();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "STARTER_ISLAND_TERRAIN_SURFACE_ONLY " +
                "heightmap=untouched alphamap=updated " +
                "scene=untouched status=PASS");
        }

        [MenuItem("CML/Art/Rebuild Starter Island Terrain Scene Only")]
        public static void RunSceneOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    "The generated Terrain prefab is missing. Run the complete " +
                    $"Terrain setup first: {PrefabPath}");
            }

            BuildReviewScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("CML/Art/Open Starter Island Terrain Review")]
        public static void OpenReviewScene()
        {
            if (!File.Exists(
                    Path.Combine(
                        Application.dataPath,
                        ReviewScenePath.Substring("Assets/".Length))))
            {
                throw new FileNotFoundException(
                    $"Terrain review scene is missing: {ReviewScenePath}");
            }

            EditorSceneManager.OpenScene(
                ReviewScenePath,
                OpenSceneMode.Single);
            var terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            if (terrain != null)
            {
                Selection.activeGameObject = terrain.gameObject;
            }

            Debug.Log(
                $"STARTER_ISLAND_TERRAIN_OPEN scene={ReviewScenePath} " +
                "status=PASS");
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Overview")]
        public static void RenderOverview()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\overview.png",
                "OVERVIEW",
                new Vector3(-390f, 250f, -440f),
                new Vector3(-4f, -24f, 0f),
                43f);
        }

        [MenuItem("CML/Art/Render Starter Island Underbody QA")]
        public static void RenderUnderbodyView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_UNDERBODY_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\underbody.png",
                "OVERVIEW",
                new Vector3(-430f, 112f, -500f),
                new Vector3(-4f, -48f, 0f),
                48f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Hero View")]
        public static void RenderHeroView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_HERO_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\hero.png",
                "HERO",
                new Vector3(-272f, 19.65f, -190f),
                new Vector3(-182f, 38f, -25f),
                63f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Factory FPS")]
        public static void RenderFactoryView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_FACTORY_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\factory.png",
                "FACTORY",
                new Vector3(-12f, 36.65f, -18f),
                new Vector3(-205f, 82f, 150f),
                63f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Portal Approach")]
        public static void RenderPortalApproachView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_PORTAL_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\portal.png",
                "PORTAL",
                new Vector3(110f, 45.65f, 34f),
                new Vector3(220f, 80f, 115f),
                63f);
        }

        [MenuItem("CML/Art/Render Starter Island Portal Close QA")]
        public static void RenderPortalCloseView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_PORTAL_CLOSE_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\portal_close.png",
                "PORTAL_CLOSE",
                new Vector3(188f, 96f, 92f),
                new Vector3(220f, 89f, 115f),
                58f);
        }

        /// <summary>
        /// Le pozze a monte non erano coperte da nessuna vista di QA, ed è
        /// proprio dove si vedevano le giunzioni sporche fra nastri e dischi.
        /// </summary>
        [MenuItem("CML/Art/Render Starter Island Source Pool QA")]
        public static void RenderSourcePoolView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_SOURCE_POOL_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\pool_source.png",
                "WATER_AERIAL",
                new Vector3(-180f, 111f, 169f),
                new Vector3(-205f, 82f, 145f),
                42f);
        }

        [MenuItem("CML/Art/Render Starter Island Intermediate Pool QA")]
        public static void RenderIntermediatePoolView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_INTERMEDIATE_POOL_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\pool_intermediate.png",
                "WATER_AERIAL",
                new Vector3(-178f, 79f, 112f),
                new Vector3(-196f, 62f, 101f),
                42f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Cascade FPS")]
        public static void RenderCascadeView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_CASCADE_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\cascade.png",
                "WATER_AERIAL",
                new Vector3(-70f, 130f, -170f),
                new Vector3(-190f, 49f, 43f),
                44f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Cliff Material FPS")]
        public static void RenderTerrainCliffMaterialBenchmark()
        {
            // Parete sud-est del primo anello nord-occidentale osservata
            // dalla quota del giocatore. La cascata e la rampa restano fuori
            // campo: questa vista misura soltanto Terrain e materiale.
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_CLIFF_MATERIAL_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\terrain_cliff_material.png",
                "TERRAIN_CLIFF_MATERIAL",
                new Vector3(-130f, 36.95f, 0f),
                new Vector3(-151f, 42.65f, 34f),
                60f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Cliff Close FPS")]
        public static void RenderTerrainCliffCloseBenchmark()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_CLIFF_CLOSE_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\terrain_cliff_close.png",
                "TERRAIN_CLIFF_CLOSE",
                new Vector3(-145f, 36.95f, 17f),
                new Vector3(-151f, 42.65f, 34f),
                60f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Cliff QA Pair")]
        public static void RenderTerrainCliffQaPair()
        {
            RenderTerrainCliffMaterialBenchmark();
            RenderTerrainCliffCloseBenchmark();
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Cliff Point Light QA")]
        public static void RenderTerrainCliffPointLightQa()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_CLIFF_POINT_LIGHT_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\terrain_cliff_point_light.png",
                "TERRAIN_CLIFF_POINT_LIGHT",
                new Vector3(-145f, 36.95f, 17f),
                new Vector3(-151f, 42.65f, 34f),
                60f,
                scene =>
                {
                    var pointObject =
                        new GameObject("QA_CliffPointLight");
                    SceneManager.MoveGameObjectToScene(
                        pointObject,
                        scene);
                    pointObject.transform.position =
                        new Vector3(-149f, 45f, 22f);
                    var point = pointObject.AddComponent<Light>();
                    point.type = LightType.Point;
                    point.color = Html("#FFD2A1");
                    point.intensity = 52f;
                    point.range = 34f;
                    point.shadows = LightShadows.Soft;
                    point.shadowStrength = 0.82f;
                    point.shadowBias = 0.025f;
                    point.shadowNormalBias = 0.18f;
                });
        }

        [MenuItem("CML/Art/Audit Starter Island Terrain Shader")]
        public static void AuditTerrainShader()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Terrain Splat");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island Terrain shader was not found.");
            }

            var messages = ShaderUtil.GetShaderMessages(shader);
            for (var index = 0; index < messages.Length; index++)
            {
                var message = messages[index];
                Debug.Log(
                    $"STARTER_ISLAND_TERRAIN_SHADER_MESSAGE " +
                    $"severity={message.severity} " +
                    $"platform={message.platform} " +
                    $"line={message.line} message={message.message}");
            }

            Debug.Log(
                $"STARTER_ISLAND_TERRAIN_SHADER_AUDIT " +
                $"supported={shader.isSupported} " +
                $"messages={messages.Length} status=" +
                (shader.isSupported && messages.Length == 0
                    ? "PASS"
                    : "CHECK"));
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Pond FPS")]
        public static void RenderPondView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_POND_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\pond.png",
                "POND",
                new Vector3(-143f, 29f, -69f),
                new Vector3(-181f, 26.7f, -72f),
                60f);
        }

        [MenuItem("CML/Art/Render Starter Island Terrain FPS Suite")]
        public static void RenderFpsSuite()
        {
            RenderHeroView();
            RenderFactoryView();
            RenderPortalApproachView();
            RenderCascadeView();
            RenderPondView();
            RenderSourcePoolView();
            RenderIntermediatePoolView();
        }

        [MenuItem("CML/Art/Render Starter Island Wind QA Pair")]
        public static void RenderWindQaPair()
        {
            const string variable =
                "CML_STARTER_ISLAND_TERRAIN_HERO_RENDER";
            var previous =
                Environment.GetEnvironmentVariable(variable);
            try
            {
                Environment.SetEnvironmentVariable(
                    variable,
                    @"D:\CodexTemp\StarterIslandTerrain\wind_a.png");
                RenderHeroView();
                System.Threading.Thread.Sleep(1200);
                Environment.SetEnvironmentVariable(
                    variable,
                    @"D:\CodexTemp\StarterIslandTerrain\wind_b.png");
                RenderHeroView();
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    variable,
                    previous);
            }
        }

        [MenuItem("CML/Art/Render Starter Island Reference QA Suite")]
        public static void RenderReferenceQaSuite()
        {
            RenderOverview();
            RenderCascadeView();
            RenderUnderbodyView();
        }

        [MenuItem("CML/Art/Render Starter Island Terrain Airship View")]
        public static void RenderAirshipView()
        {
            RenderSceneView(
                "CML_STARTER_ISLAND_TERRAIN_AIRSHIP_RENDER",
                @"D:\CodexTemp\StarterIslandTerrain\airship.png",
                "AIRSHIP",
                new Vector3(438f, 286f, -492f),
                new Vector3(0f, 56f, 0f),
                44f);
        }

        private static void RenderSceneView(
            string outputEnvironmentVariable,
            string defaultOutputPath,
            string marker,
            Vector3 cameraPosition,
            Vector3 targetPosition,
            float fieldOfView,
            Action<Scene> configureScene = null)
        {
            if (!File.Exists(
                    Path.Combine(
                        Application.dataPath,
                        ReviewScenePath.Substring("Assets/".Length))))
            {
                throw new FileNotFoundException(
                    $"Terrain review scene is missing: {ReviewScenePath}");
            }

            var outputPath =
                Environment.GetEnvironmentVariable(
                    outputEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = defaultOutputPath;
            }

            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(
                ReviewScenePath,
                OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            configureScene?.Invoke(scene);
            var renderTerrain =
                UnityEngine.Object.FindFirstObjectByType<Terrain>();
            if (!string.Equals(
                    marker,
                    "OVERVIEW",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    marker,
                    "AIRSHIP",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    marker,
                    "WATER_AERIAL",
                    StringComparison.Ordinal))
            {
                if (renderTerrain == null)
                {
                    throw new InvalidOperationException(
                        "The HERO render requires the generated Terrain.");
                }

                cameraPosition.y =
                    renderTerrain.SampleHeight(
                        new Vector3(
                            cameraPosition.x,
                            0f,
                            cameraPosition.z)) +
                    renderTerrain.transform.position.y +
                    1.65f;
            }

            GameObject cameraObject = null;
            Camera camera = null;
            RenderTexture renderTexture = null;
            Texture2D capture = null;
            var previousActiveTexture = RenderTexture.active;
            var previousShadowDistance =
                QualitySettings.shadowDistance;
            try
            {
                cameraObject = new GameObject(
                    $"ENV_Terrain{marker}Camera_Temporary");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                cameraObject.transform.position = cameraPosition;
                cameraObject.transform.rotation = Quaternion.LookRotation(
                    targetPosition - cameraPosition,
                    Vector3.up);

                camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = fieldOfView;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1800f;
                camera.allowHDR = true;
                camera.allowMSAA = true;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.backgroundColor = Html("#9EDFF0");
                camera.depthTextureMode |= DepthTextureMode.Depth;
                ConfigureCameraRendering(camera);

                renderTexture = new RenderTexture(
                    1600,
                    900,
                    24,
                    RenderTextureFormat.ARGB32)
                {
                    name = $"RT_StarterIslandTerrain{marker}",
                    // URP's depth/opaque-texture passes must resolve into the
                    // same attachment format during manual Camera.Render.
                    // Supersampled output is preferable to an invalid MSAA
                    // attachment in this deterministic QA harness.
                    antiAliasing = 1
                };
                renderTexture.Create();
                camera.targetTexture = renderTexture;

                QualitySettings.shadowDistance = 650f;
                if (renderTerrain != null)
                {
                    renderTerrain.Flush();
                }

                // Terrain details initialize their GPU batches during the
                // first camera submission. That first submission also
                // initializes URP's VolumeManager in batch mode.
                camera.Render();
                camera.UpdateVolumeStack();
                // Capture two fully initialized frames so both the foliage
                // batches and color grading match the Game camera.
                camera.Render();
                camera.Render();

                RenderTexture.active = renderTexture;
                capture = new Texture2D(
                    renderTexture.width,
                    renderTexture.height,
                    TextureFormat.RGB24,
                    false,
                    false);
                capture.ReadPixels(
                    new Rect(
                        0f,
                        0f,
                        renderTexture.width,
                        renderTexture.height),
                    0,
                    0,
                    false);
                capture.Apply(false, false);

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(
                    outputPath,
                    capture.EncodeToPNG());
                Debug.Log(
                    $"STARTER_ISLAND_TERRAIN_{marker}_RENDER " +
                    $"path={outputPath} " +
                    $"width={renderTexture.width} " +
                    $"height={renderTexture.height} status=PASS");
            }
            finally
            {
                QualitySettings.shadowDistance =
                    previousShadowDistance;
                RenderTexture.active = previousActiveTexture;
                if (camera != null)
                {
                    camera.targetTexture = null;
                }

                if (capture != null)
                {
                    UnityEngine.Object.DestroyImmediate(capture);
                }

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }

                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder(TexturesRoot);
            EnsureFolder(LayersRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(DataRoot);
            EnsureFolder(PrefabsRoot);
            EnsureFolder("Assets/_Project/Scenes");
        }

        private static TerrainLayer[] BuildTerrainLayers()
        {
            var layers = new TerrainLayer[LayerDefinitions.Length];
            for (var index = 0; index < LayerDefinitions.Length; index++)
            {
                var definition = LayerDefinitions[index];
                var texturePath =
                    TexturesRoot + "/T_StarterIsland_" +
                    definition.Name + ".asset";
                var texture = CreateOrUpdateSurfaceTexture(
                    texturePath,
                    definition,
                    0x713B + index * 977);
                var normalTexture =
                    CreateOrUpdateSurfaceNormalTexture(
                        TexturesRoot + "/T_StarterIsland_" +
                        definition.Name + "_Normal.asset",
                        texture,
                        string.Equals(
                            definition.Name,
                            "CliffWarm",
                            StringComparison.Ordinal));
                var layerPath =
                    LayersRoot + "/TL_StarterIsland_" +
                    definition.Name + ".terrainlayer";
                var layer =
                    AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    layer = new TerrainLayer
                    {
                        name = "TL_StarterIsland_" + definition.Name
                    };
                    AssetDatabase.CreateAsset(layer, layerPath);
                }

                layer.diffuseTexture = texture;
                layer.normalMapTexture = normalTexture;
                layer.normalScale =
                    string.Equals(
                        definition.Name,
                        "CliffWarm",
                        StringComparison.Ordinal)
                        ? 0f
                        : string.Equals(
                            definition.Name,
                            "DirtPath",
                            StringComparison.Ordinal)
                            ? 0.92f
                            : 0.32f;
                layer.tileSize =
                    new Vector2(definition.TileSize, definition.TileSize);
                layer.tileOffset = Vector2.zero;
                layer.metallic = 0f;
                layer.smoothness = definition.Smoothness;
                EditorUtility.SetDirty(layer);
                layers[index] = layer;
            }

            return layers;
        }

        private static Texture2D CreateOrUpdateSurfaceTexture(
            string path,
            LayerDefinition definition,
            int seed)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(
                    SurfaceTextureResolution,
                    SurfaceTextureResolution,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(texture, path);
            }
            else if (texture.width != SurfaceTextureResolution ||
                     texture.height != SurfaceTextureResolution)
            {
                texture.Reinitialize(
                    SurfaceTextureResolution,
                    SurfaceTextureResolution,
                    TextureFormat.RGBA32,
                    true);
            }

            var baseColor = Html(definition.BaseColor);
            var lightColor = Html(definition.LightColor);
            var darkColor = Html(definition.DarkColor);
            var isCliff = string.Equals(
                definition.Name,
                "CliffWarm",
                StringComparison.Ordinal);
            var isDirtPath = string.Equals(
                definition.Name,
                "DirtPath",
                StringComparison.Ordinal);
            var isGrass = definition.Name.StartsWith(
                "Grass",
                StringComparison.Ordinal);
            var pixels =
                new Color[SurfaceTextureResolution * SurfaceTextureResolution];
            var offsetX = (seed & 0xFF) * 0.173f;
            var offsetY = ((seed >> 8) & 0xFF) * 0.197f;
            var pathMarks = isDirtPath
                ? BuildPathSurfaceMarks(seed, 64)
                : Array.Empty<Vector4>();

            for (var y = 0; y < SurfaceTextureResolution; y++)
            {
                for (var x = 0; x < SurfaceTextureResolution; x++)
                {
                    var u = x / (float)SurfaceTextureResolution;
                    var v = y / (float)SurfaceTextureResolution;
                    float variation;
                    if (isCliff)
                    {
                        // La parete deve leggere come roccia erosa, non come
                        // Perlin isotropo stampato. Il warp è seamless; le
                        // ottave cambiano più rapidamente in orizzontale che
                        // in verticale e generano solchi lunghi 8-18 metri.
                        var warp = SeamlessNoiseAnisotropic(
                            u,
                            v,
                            2.0f,
                            1.0f,
                            offsetX + 37.2f,
                            offsetY + 11.6f);
                        var warpedU =
                            Mathf.Repeat(
                                u + (warp - 0.5f) * 0.105f,
                                1f);
                        var broadRidges = SeamlessNoiseAnisotropic(
                            warpedU,
                            v,
                            3.2f,
                            0.82f,
                            offsetX,
                            offsetY);
                        var secondaryRidges = SeamlessNoiseAnisotropic(
                            warpedU,
                            v,
                            8.4f,
                            1.75f,
                            offsetY + 9.1f,
                            offsetX + 4.7f);
                        var fracturedDetail = SeamlessNoiseAnisotropic(
                            warpedU,
                            v,
                            24f,
                            5.2f,
                            offsetX + 17.3f,
                            offsetY + 21.9f);
                        variation = Mathf.Clamp01(
                            broadRidges * 0.60f +
                            secondaryRidges * 0.29f +
                            fracturedDetail * 0.11f);
                        variation =
                            SmoothStep(
                                0.25f,
                                0.75f,
                                variation);
                    }
                    else
                    {
                        var broad = SeamlessNoise(
                            u,
                            v,
                            4f,
                            offsetX,
                            offsetY);
                        var fine = SeamlessNoise(
                            u,
                            v,
                            13f,
                            offsetY + 9.1f,
                            offsetX + 4.7f);
                        var micro = SeamlessNoise(
                            u,
                            v,
                            31f,
                            offsetX + 17.3f,
                            offsetY + 21.9f);
                        variation = Mathf.Clamp01(
                            broad * 0.58f +
                            fine * 0.29f +
                            micro * 0.13f);
                    }
                    var variedColor = variation < 0.5f
                        ? Color.Lerp(
                            darkColor,
                            baseColor,
                            variation * 2f)
                        : Color.Lerp(
                            baseColor,
                            lightColor,
                            (variation - 0.5f) * 2f);
                    // L'erba stava al 12% e la roccia al 22%: la texture era
                    // per quasi nove decimi colore base puro, cioè tinta
                    // piatta. Il micro-contrasto era stato schiacciato per
                    // evitare l'effetto "rumore procedurale", ma così si
                    // perdeva anche la variazione larga che rende il terreno
                    // continuo invece che verniciato.
                    var color =
                        Color.Lerp(
                            baseColor,
                            variedColor,
                            isCliff
                                ? 0.82f
                                : isDirtPath
                                    ? 0.58f
                                    : 0.52f);
                    if (isCliff)
                    {
                        // Due scale di dettaglio interrompono i solchi senza
                        // cancellarne la direzione. A 12 m per tile
                        // corrispondono a masse di circa 0,8 m e grana di
                        // circa 0,3 m: leggibili vicino, assorbite dai mip
                        // nella vista d'insieme.
                        var stoneBreakup = SeamlessNoise(
                            u,
                            v,
                            15f,
                            offsetX + 54.7f,
                            offsetY + 28.1f);
                        var compactGrain = SeamlessNoise(
                            u,
                            v,
                            41f,
                            offsetY + 71.3f,
                            offsetX + 19.4f);
                        var breakup =
                            (stoneBreakup - 0.5f) * 0.095f +
                            (compactGrain - 0.5f) * 0.038f;
                        color *= 1f + breakup;

                        var shallowPits =
                            SmoothStep(
                                0.72f,
                                0.91f,
                                compactGrain);
                        color = Color.Lerp(
                            color,
                            darkColor,
                            shallowPits * 0.075f);
                    }
                    if (isDirtPath)
                    {
                        // Compacted sand has broad soft patches plus sparse,
                        // embedded grains. These are texture-scale details;
                        // the larger path stones remain physical meshes.
                        var compaction = SeamlessNoise(
                            u,
                            v,
                            5f,
                            offsetX + 31.7f,
                            offsetY + 19.3f);
                        var grain = SeamlessNoise(
                            u,
                            v,
                            39f,
                            offsetY + 47.1f,
                            offsetX + 11.9f);
                        var fineGrain = SeamlessNoise(
                            u,
                            v,
                            71f,
                            offsetX + 63.4f,
                            offsetY + 38.6f);
                        var shapedCompaction =
                            Mathf.SmoothStep(0.27f, 0.73f, compaction);
                        color = Color.Lerp(
                            color,
                            Color.Lerp(
                                darkColor,
                                lightColor,
                                shapedCompaction),
                            0.36f);
                        color *= Mathf.Lerp(
                            0.82f,
                            1.14f,
                            shapedCompaction);
                        var embeddedGrain =
                            Mathf.SmoothStep(0.68f, 0.88f, grain);
                        var grainColor = Color.Lerp(
                            darkColor,
                            Html("#817B72"),
                            0.42f);
                        color = Color.Lerp(
                            color,
                            grainColor,
                            embeddedGrain * 0.38f);
                        var paleGrain =
                            Mathf.SmoothStep(0.72f, 0.91f, fineGrain);
                        color = Color.Lerp(
                            color,
                            Html("#E8BD91"),
                            paleGrain * 0.24f);

                        var embeddedDarkMark = 0f;
                        var embeddedLightMark = 0f;
                        for (var markIndex = 0;
                             markIndex < pathMarks.Length;
                             markIndex++)
                        {
                            var mark = pathMarks[markIndex];
                            var deltaX = Mathf.Abs(u - mark.x);
                            var deltaY = Mathf.Abs(v - mark.y);
                            deltaX = Mathf.Min(deltaX, 1f - deltaX);
                            deltaY = Mathf.Min(deltaY, 1f - deltaY);
                            var ellipseDistance =
                                deltaX * deltaX / (mark.z * mark.z) +
                                deltaY * deltaY / (mark.w * mark.w);
                            var markStrength =
                                1f -
                                Mathf.SmoothStep(
                                    0.30f,
                                    1.0f,
                                    ellipseDistance);
                            if (markIndex % 4 == 0)
                            {
                                embeddedLightMark = Mathf.Max(
                                    embeddedLightMark,
                                    markStrength);
                            }
                            else
                            {
                                embeddedDarkMark = Mathf.Max(
                                    embeddedDarkMark,
                                    markStrength);
                            }
                        }

                        color = Color.Lerp(
                            color,
                            Html("#9F765D"),
                            embeddedDarkMark * 0.38f);
                        color = Color.Lerp(
                            color,
                            Html("#EDC9A5"),
                            embeddedLightMark * 0.42f);
                    }
                    if (isGrass)
                    {
                        // DirtPath aveva compattazione, grana e macchie;
                        // l'erba non aveva nulla oltre alle tre ottave. Qui
                        // arrivano i ciuffi, le lame secche schiarite, l'ombra
                        // negli avvallamenti fra i ciuffi e qualche chiazza
                        // ingiallita. Sono dettagli di texture: i fili veri
                        // restano mesh di ground cover.
                        var clump = SeamlessNoise(
                            u,
                            v,
                            23f,
                            offsetX + 51.3f,
                            offsetY + 7.7f);
                        var shapedClump =
                            Mathf.SmoothStep(0.34f, 0.78f, clump);
                        color = Color.Lerp(
                            color,
                            Color.Lerp(darkColor, lightColor, shapedClump),
                            0.30f);
                        color *= Mathf.Lerp(0.88f, 1.10f, shapedClump);

                        var blades = SeamlessNoise(
                            u,
                            v,
                            61f,
                            offsetY + 29.5f,
                            offsetX + 43.1f);
                        color = Color.Lerp(
                            color,
                            lightColor,
                            Mathf.SmoothStep(0.70f, 0.92f, blades) * 0.34f);

                        var gaps = SeamlessNoise(
                            u,
                            v,
                            47f,
                            offsetX + 73.9f,
                            offsetY + 15.2f);
                        color = Color.Lerp(
                            color,
                            darkColor,
                            Mathf.SmoothStep(0.72f, 0.94f, gaps) * 0.30f);

                        var dry = SeamlessNoise(
                            u,
                            v,
                            7f,
                            offsetX + 88.2f,
                            offsetY + 61.4f);
                        color = Color.Lerp(
                            color,
                            Html("#A8A44E"),
                            Mathf.SmoothStep(0.74f, 0.93f, dry) * 0.22f);
                    }

                    pixels[y * SurfaceTextureResolution + x] =
                        new Color(
                            color.r,
                            color.g,
                            color.b,
                            Mathf.Min(definition.Smoothness, 0.03f));
                }
            }

            texture.SetPixels(pixels);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = isDirtPath ? 8 : 4;
            texture.Apply(true, false);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Vector4[] BuildPathSurfaceMarks(
            int seed,
            int count)
        {
            var random = new System.Random(seed ^ 0x27D4EB2D);
            var marks = new Vector4[count];
            for (var index = 0; index < marks.Length; index++)
            {
                marks[index] = new Vector4(
                    NextFloat(random, 0f, 1f),
                    NextFloat(random, 0f, 1f),
                    NextFloat(random, 0.006f, 0.022f),
                    NextFloat(random, 0.004f, 0.014f));
            }

            return marks;
        }

        private static Texture2D CreateOrUpdateSurfaceNormalTexture(
            string path,
            Texture2D source,
            bool isCliff)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(
                    SurfaceTextureResolution,
                    SurfaceTextureResolution,
                    TextureFormat.RGBA32,
                    true,
                    true)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                AssetDatabase.CreateAsset(texture, path);
            }
            else if (texture.width != SurfaceTextureResolution ||
                     texture.height != SurfaceTextureResolution)
            {
                texture.Reinitialize(
                    SurfaceTextureResolution,
                    SurfaceTextureResolution,
                    TextureFormat.RGBA32,
                    true);
            }

            var sourcePixels = source.GetPixels();
            var normalPixels =
                new Color[
                    SurfaceTextureResolution *
                    SurfaceTextureResolution];
            var strength = isCliff ? 32.0f : 8.4f;
            for (var y = 0; y < SurfaceTextureResolution; y++)
            {
                var yPrevious =
                    (y - 1 + SurfaceTextureResolution) %
                    SurfaceTextureResolution;
                var yNext =
                    (y + 1) % SurfaceTextureResolution;
                for (var x = 0; x < SurfaceTextureResolution; x++)
                {
                    var xPrevious =
                        (x - 1 + SurfaceTextureResolution) %
                        SurfaceTextureResolution;
                    var xNext =
                        (x + 1) % SurfaceTextureResolution;
                    var left = sourcePixels[
                        y * SurfaceTextureResolution + xPrevious]
                        .grayscale;
                    var right = sourcePixels[
                        y * SurfaceTextureResolution + xNext]
                        .grayscale;
                    var down = sourcePixels[
                        yPrevious * SurfaceTextureResolution + x]
                        .grayscale;
                    var up = sourcePixels[
                        yNext * SurfaceTextureResolution + x]
                        .grayscale;
                    var normal = new Vector3(
                            (left - right) * strength,
                            (down - up) * strength,
                            1f)
                        .normalized;
                    normalPixels[
                        y * SurfaceTextureResolution + x] =
                        new Color(
                            normal.x * 0.5f + 0.5f,
                            normal.y * 0.5f + 0.5f,
                            normal.z * 0.5f + 0.5f,
                            1f);
                }
            }

            texture.SetPixels(normalPixels);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.Apply(true, false);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static float SeamlessNoise(
            float u,
            float v,
            float scale,
            float offsetX,
            float offsetY)
        {
            var x = u * scale;
            var y = v * scale;
            var x0 = Mathf.PerlinNoise(x + offsetX, y + offsetY);
            var x1 =
                Mathf.PerlinNoise(x - scale + offsetX, y + offsetY);
            var y0 =
                Mathf.PerlinNoise(x + offsetX, y - scale + offsetY);
            var xy =
                Mathf.PerlinNoise(
                    x - scale + offsetX,
                    y - scale + offsetY);
            var a = Mathf.Lerp(x0, x1, u);
            var b = Mathf.Lerp(y0, xy, u);
            return Mathf.Lerp(a, b, v);
        }

        private static float SeamlessNoiseAnisotropic(
            float u,
            float v,
            float scaleU,
            float scaleV,
            float offsetX,
            float offsetY)
        {
            var x = u * scaleU;
            var y = v * scaleV;
            var x0 = Mathf.PerlinNoise(x + offsetX, y + offsetY);
            var x1 =
                Mathf.PerlinNoise(
                    x - scaleU + offsetX,
                    y + offsetY);
            var y0 =
                Mathf.PerlinNoise(
                    x + offsetX,
                    y - scaleV + offsetY);
            var xy =
                Mathf.PerlinNoise(
                    x - scaleU + offsetX,
                    y - scaleV + offsetY);
            var a = Mathf.Lerp(x0, x1, u);
            var b = Mathf.Lerp(y0, xy, u);
            return Mathf.Lerp(a, b, v);
        }

        private static TerrainData BuildTerrainData(TerrainLayer[] layers)
        {
            var data =
                AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
            {
                data = new TerrainData
                {
                    name = "TD_StarterIsland"
                };
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }

            data.heightmapResolution = HeightmapResolution;
            data.alphamapResolution = AlphamapResolution;
            data.baseMapResolution = 512;
            data.size =
                new Vector3(TerrainWidth, TerrainHeight, TerrainLength);

            var heights =
                new float[HeightmapResolution, HeightmapResolution];
            for (var zIndex = 0;
                 zIndex < HeightmapResolution;
                 zIndex++)
            {
                var zNormalized =
                    zIndex / (float)(HeightmapResolution - 1);
                var worldZ = zNormalized * TerrainLength -
                             TerrainLength * 0.5f;
                for (var xIndex = 0;
                     xIndex < HeightmapResolution;
                     xIndex++)
                {
                    var xNormalized =
                        xIndex / (float)(HeightmapResolution - 1);
                    var worldX = xNormalized * TerrainWidth -
                                 TerrainWidth * 0.5f;
                    heights[zIndex, xIndex] =
                        EvaluateHeight(worldX, worldZ) / TerrainHeight;
                }
            }

            data.SetHeights(0, 0, heights);
            var holesResolution = data.holesResolution;
            var surface = new bool[holesResolution, holesResolution];
            for (var zIndex = 0; zIndex < holesResolution; zIndex++)
            {
                var zNormalized =
                    (zIndex + 0.5f) / holesResolution;
                var worldZ = zNormalized * TerrainLength -
                             TerrainLength * 0.5f;
                for (var xIndex = 0; xIndex < holesResolution; xIndex++)
                {
                    var xNormalized =
                        (xIndex + 0.5f) / holesResolution;
                    var worldX = xNormalized * TerrainWidth -
                                 TerrainWidth * 0.5f;
                    surface[zIndex, xIndex] =
                        IsInsideIslandSurface(worldX, worldZ);
                }
            }

            data.SetHoles(0, 0, surface);
            RepaintTerrainLayers(data, layers);
            // Grass is owned by Unity Terrain details so it remains editable
            // through Paint Details. The targeted adaptive installer creates
            // upright mesh prototypes and seeds only the detail maps; it does
            // not touch this heightfield, its holes or its painted splats.
            StarterIslandAdaptiveGrassInstaller
                .ConfigureTerrainDataForBuild(data);
            EditorUtility.SetDirty(data);
            return data;
        }

        private static void RepaintTerrainLayers(
            TerrainData data,
            TerrainLayer[] layers)
        {
            data.terrainLayers = layers;
            var alphamaps = new float[
                AlphamapResolution,
                AlphamapResolution,
                layers.Length];
            for (var zIndex = 0;
                 zIndex < AlphamapResolution;
                 zIndex++)
            {
                var zNormalized =
                    zIndex / (float)(AlphamapResolution - 1);
                var worldZ = zNormalized * TerrainLength -
                             TerrainLength * 0.5f;
                for (var xIndex = 0;
                     xIndex < AlphamapResolution;
                     xIndex++)
                {
                    var xNormalized =
                        xIndex / (float)(AlphamapResolution - 1);
                    var worldX = xNormalized * TerrainWidth -
                                 TerrainWidth * 0.5f;
                    EvaluateSurfaceWeights(
                        data,
                        xNormalized,
                        zNormalized,
                        worldX,
                        worldZ,
                        out var grassSun,
                        out var grassDeep,
                        out var dirt,
                        out var cliff);
                    alphamaps[zIndex, xIndex, 0] = grassSun;
                    alphamaps[zIndex, xIndex, 1] = grassDeep;
                    alphamaps[zIndex, xIndex, 2] = dirt;
                    alphamaps[zIndex, xIndex, 3] = cliff;
                }
            }

            data.SetAlphamaps(0, 0, alphamaps);
            EditorUtility.SetDirty(data);
        }

        private static void ConfigureTerrainDetails(TerrainData data)
        {
            const string foliageRoot =
                "Assets/_Project/Art/Environment/StarterIsland/" +
                "Foliage/";
            var grassA = AssetDatabase.LoadAssetAtPath<GameObject>(
                foliageRoot + "Prefabs/PF_Grass_Clump_A.prefab");
            var grassB = AssetDatabase.LoadAssetAtPath<GameObject>(
                foliageRoot + "Prefabs/PF_Grass_Clump_B.prefab");
            var flowerWhite = AssetDatabase.LoadAssetAtPath<GameObject>(
                foliageRoot + "Prefabs/PF_Flower_White_A.prefab");
            var flowerOrange = AssetDatabase.LoadAssetAtPath<GameObject>(
                foliageRoot + "Prefabs/PF_Flower_Orange_B.prefab");
            if (grassA == null ||
                grassB == null ||
                flowerWhite == null ||
                flowerOrange == null)
            {
                throw new FileNotFoundException(
                    "Starter Island Terrain details require the two grass " +
                    "and two flower prefabs.");
            }

            var foliageMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    foliageRoot +
                    "Materials/M_StarterIsland_FoliageAtlas.mat");
            ConfigureFoliageMaterial(foliageMaterial);
            var grassMaterial = BuildGroundDetailMaterial();
            var detailPrefabRoot =
                foliageRoot + "Prefabs/TerrainDetails";
            EnsureFolder(detailPrefabRoot);
            grassA = BuildTerrainDetailPrefab(
                grassA,
                grassMaterial,
                detailPrefabRoot + "/PF_TerrainDetail_Grass_A.prefab");
            grassB = BuildTerrainDetailPrefab(
                grassB,
                grassMaterial,
                detailPrefabRoot + "/PF_TerrainDetail_Grass_B.prefab");
            flowerWhite = BuildTerrainDetailPrefab(
                flowerWhite,
                foliageMaterial,
                detailPrefabRoot + "/PF_TerrainDetail_Flower_White.prefab");
            flowerOrange = BuildTerrainDetailPrefab(
                flowerOrange,
                foliageMaterial,
                detailPrefabRoot + "/PF_TerrainDetail_Flower_Orange.prefab");

            // The authored maps below contain instance counts (0/1). In
            // CoverageMode a value of 1 means only 1/255 coverage, which made
            // the log report thousands of populated cells while rendering
            // almost no grass at all.
            data.SetDetailScatterMode(
                DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(256, 16);
            var detailPrototypes = new[]
            {
                BuildDetailPrototype(
                    grassA,
                    0.62f,
                    0.95f,
                    0.58f,
                    0.88f,
                    0x1357),
                BuildDetailPrototype(
                    grassB,
                    0.55f,
                    0.86f,
                    0.50f,
                    0.78f,
                    0x2468),
                BuildDetailPrototype(
                    flowerWhite,
                    0.36f,
                    0.56f,
                    0.34f,
                    0.54f,
                    0x3579),
                BuildDetailPrototype(
                    flowerOrange,
                    0.36f,
                    0.56f,
                    0.34f,
                    0.54f,
                    0x468A)
            };
            for (var prototypeIndex = 0;
                 prototypeIndex < detailPrototypes.Length;
                 prototypeIndex++)
            {
                if (!detailPrototypes[prototypeIndex].Validate(
                        out var validationError))
                {
                    throw new InvalidOperationException(
                        $"Invalid Terrain detail prototype " +
                        $"{prototypeIndex}: {validationError}");
                }
            }

            data.detailPrototypes = detailPrototypes;
            data.RefreshPrototypes();

            var resolution = data.detailResolution;
            var detailMaps = new int[4][,];
            for (var layer = 0; layer < detailMaps.Length; layer++)
            {
                detailMaps[layer] = new int[resolution, resolution];
            }

            var grassInstances = 0;
            var flowerInstances = 0;
            for (var zIndex = 0; zIndex < resolution; zIndex++)
            {
                var zNormalized =
                    (zIndex + 0.5f) / resolution;
                var worldZ =
                    zNormalized * TerrainLength -
                    TerrainLength * 0.5f;
                for (var xIndex = 0; xIndex < resolution; xIndex++)
                {
                    var xNormalized =
                        (xIndex + 0.5f) / resolution;
                    var worldX =
                        xNormalized * TerrainWidth -
                        TerrainWidth * 0.5f;
                    if (!IsInsideIslandSurface(worldX, worldZ) ||
                        BoundaryRadius(worldX, worldZ) > 0.91f)
                    {
                        continue;
                    }

                    var slope = data.GetSteepness(
                        xNormalized,
                        zNormalized);
                    var point = new Vector2(worldX, worldZ);
                    var path = ClosestPathSample(point);
                    var factoryDistance =
                        EllipseDistance(
                            worldX,
                            worldZ,
                            -12f,
                            -18f,
                            124f,
                            94f);
                    var pondDistance =
                        EllipseDistance(
                            worldX,
                            worldZ,
                            -178f,
                            -72f,
                            46f,
                            34f);
                    var streamDistance =
                        ClosestPolylineSample(point, StreamRoute)
                            .Distance;
                    if (slope > 29f ||
                        path.Distance < 2.9f ||
                        factoryDistance < 0.35f ||
                        pondDistance < 1.10f ||
                        streamDistance < 8f)
                    {
                        continue;
                    }

                    var groveNoise =
                        Mathf.PerlinNoise(
                            (worldX + 390f) * 0.022f,
                            (worldZ + 310f) * 0.022f);
                    var fineNoise =
                        Mathf.PerlinNoise(
                            (worldX + 71f) * 0.081f,
                            (worldZ + 93f) * 0.081f);
                    var broadPatch =
                        SmoothStep(0.43f, 0.68f, groveNoise);
                    var brokenPatch =
                        SmoothStep(0.30f, 0.68f, fineNoise);
                    var grassChance =
                        Mathf.Lerp(
                            0.12f,
                            0.88f,
                            broadPatch) *
                        Mathf.Lerp(0.50f, 1f, brokenPatch);
                    if (DetailHash01(xIndex, zIndex, 0x1357) <
                        grassChance)
                    {
                        detailMaps[0][zIndex, xIndex] = 2;
                        grassInstances += 2;
                    }

                    if (DetailHash01(xIndex, zIndex, 0x2468) <
                        grassChance * 0.65f)
                    {
                        detailMaps[1][zIndex, xIndex] = 1;
                        grassInstances++;
                    }

                    var flowerBias =
                        Mathf.Clamp01(
                            (groveNoise - 0.42f) * 1.7f) *
                        (1f - Mathf.Clamp01(slope / 29f));
                    if (DetailHash01(xIndex, zIndex, 0x3579) <
                        0.110f * flowerBias)
                    {
                        detailMaps[2][zIndex, xIndex] = 1;
                        flowerInstances++;
                    }

                    var autumnBias =
                        Mathf.Clamp01((worldX - 42f) / 220f) *
                        Mathf.Clamp01((worldZ + 118f) / 250f);
                    if (DetailHash01(xIndex, zIndex, 0x468A) <
                        0.080f * flowerBias * autumnBias)
                    {
                        detailMaps[3][zIndex, xIndex] = 1;
                        flowerInstances++;
                    }
                }
            }

            for (var layer = 0; layer < detailMaps.Length; layer++)
            {
                data.SetDetailLayer(
                    0,
                    0,
                    layer,
                    detailMaps[layer]);
            }

            Debug.Log(
                $"STARTER_ISLAND_TERRAIN_DETAILS grass={grassInstances} " +
                $"flowers={flowerInstances} resolution={resolution} " +
                "status=PASS");
        }

        private static DetailPrototype BuildDetailPrototype(
            GameObject prototype,
            float minimumWidth,
            float maximumWidth,
            float minimumHeight,
            float maximumHeight,
            int seed)
        {
            return new DetailPrototype
            {
                prototype = prototype,
                renderMode = DetailRenderMode.VertexLit,
                usePrototypeMesh = true,
                // Terrain's indirect-instancing path requires a dedicated
                // procedural setup function in every material shader. These
                // authored foliage meshes use the standard batched detail
                // path, which is still GPU-friendly and renders reliably in
                // Game cameras as well as editor QA cameras.
                useInstancing = false,
                positionJitter = 0.72f,
                alignToGround = 0.18f,
                minWidth = minimumWidth,
                maxWidth = maximumWidth,
                minHeight = minimumHeight,
                maxHeight = maximumHeight,
                noiseSeed = seed,
                noiseSpread = 0.18f,
                healthyColor = Color.white,
                dryColor = Html("#D4D89A")
            };
        }

        private static GameObject BuildTerrainDetailPrefab(
            GameObject source,
            Material material,
            string destinationPath)
        {
            MeshFilter selectedFilter = null;
            var selectedScore = int.MinValue;
            foreach (var filter in
                     source.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null ||
                    string.Equals(
                        mesh.name,
                        "Cube",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = mesh.vertexCount;
                if (mesh.name.StartsWith(
                        "GEO_",
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 100000;
                }

                if (score <= selectedScore)
                {
                    continue;
                }

                selectedScore = score;
                selectedFilter = filter;
            }

            if (selectedFilter == null)
            {
                throw new InvalidOperationException(
                    $"No clean detail mesh was found below {source.name}.");
            }

            var root = new GameObject(
                Path.GetFileNameWithoutExtension(destinationPath));
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh =
                    selectedFilter.sharedMesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    destinationPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureFoliageMaterial(Material material)
        {
            if (material == null)
            {
                throw new FileNotFoundException(
                    "Starter Island foliage material is missing.");
            }

            SetColor(material, "_BaseColor", Html("#F1F4D7"));
            SetFloat(material, "_WindStrength", 0.16f);
            SetFloat(material, "_WindSpeed", 0.92f);
            SetFloat(material, "_AmbientStrength", 0.66f);
            SetFloat(material, "_ShadowFloor", 0.24f);
            if (material.HasProperty("_MainTex") &&
                material.HasProperty("_BaseMap"))
            {
                material.SetTexture(
                    "_MainTex",
                    material.GetTexture("_BaseMap"));
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        private static void BuildGroundDetailMeshes(
            Transform parent,
            TerrainData data,
            Material material)
        {
            if (material == null)
            {
                throw new FileNotFoundException(
                    "Ground detail meshes require the Starter Island " +
                    "foliage material.");
            }

            const int cellsPerChunk = 32;
            const string meshFolder =
                "Assets/_Project/Art/Environment/StarterIsland/" +
                "Terrain/Data/GroundDetail";
            EnsureFolder(meshFolder);

            var root = new GameObject("GroundDetailRoot");
            root.transform.SetParent(parent, false);
            var resolution = data.detailResolution;
            var grassA = data.GetDetailLayer(
                0,
                0,
                resolution,
                resolution,
                0);
            var grassB = data.GetDetailLayer(
                0,
                0,
                resolution,
                resolution,
                1);
            var chunkCount =
                Mathf.CeilToInt(resolution / (float)cellsPerChunk);
            var renderedClumps = 0;
            var renderedMeshes = 0;

            for (var chunkZ = 0; chunkZ < chunkCount; chunkZ++)
            {
                for (var chunkX = 0; chunkX < chunkCount; chunkX++)
                {
                    var vertices = new List<Vector3>();
                    var normals = new List<Vector3>();
                    var uv = new List<Vector2>();
                    var colors = new List<Color>();
                    var triangles = new List<int>();
                    var startX = chunkX * cellsPerChunk;
                    var startZ = chunkZ * cellsPerChunk;
                    var endX = Mathf.Min(
                        resolution,
                        startX + cellsPerChunk);
                    var endZ = Mathf.Min(
                        resolution,
                        startZ + cellsPerChunk);
                    for (var zIndex = startZ;
                         zIndex < endZ;
                         zIndex++)
                    {
                        for (var xIndex = startX;
                             xIndex < endX;
                             xIndex++)
                        {
                            var countA = grassA[zIndex, xIndex];
                            var countB = grassB[zIndex, xIndex];
                            for (var instance = 0;
                                 instance < countA;
                                 instance++)
                            {
                                AppendGrassClump(
                                    data,
                                    xIndex,
                                    zIndex,
                                    instance,
                                    0x5A17,
                                    0.42f,
                                    0.72f,
                                    vertices,
                                    normals,
                                    uv,
                                    colors,
                                    triangles);
                                renderedClumps++;
                            }

                            for (var instance = 0;
                                 instance < countB;
                                 instance++)
                            {
                                AppendGrassClump(
                                    data,
                                    xIndex,
                                    zIndex,
                                    instance,
                                    0x7B29,
                                    0.34f,
                                    0.58f,
                                    vertices,
                                    normals,
                                    uv,
                                    colors,
                                    triangles);
                                renderedClumps++;
                            }
                        }
                    }

                    if (vertices.Count == 0)
                    {
                        continue;
                    }

                    var meshPath =
                        $"{meshFolder}/MD_Grass_{chunkX}_{chunkZ}.asset";
                    var mesh =
                        AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (mesh == null)
                    {
                        mesh = new Mesh
                        {
                            name = $"GEO_Grass_{chunkX}_{chunkZ}"
                        };
                        AssetDatabase.CreateAsset(mesh, meshPath);
                    }
                    else
                    {
                        mesh.Clear();
                    }

                    mesh.indexFormat = IndexFormat.UInt32;
                    mesh.SetVertices(vertices);
                    mesh.SetNormals(normals);
                    mesh.SetUVs(0, uv);
                    mesh.SetColors(colors);
                    mesh.SetTriangles(triangles, 0, true);
                    mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh);

                    var chunk = new GameObject(
                        $"ENV_GrassChunk_{chunkX}_{chunkZ}");
                    chunk.transform.SetParent(root.transform, false);
                    chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = chunk.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderedMeshes++;
                }
            }

            Debug.Log(
                $"STARTER_ISLAND_GROUND_DETAIL clumps={renderedClumps} " +
                $"meshes={renderedMeshes} collision=0 status=PASS");
        }

        private static void AppendGrassClump(
            TerrainData data,
            int xIndex,
            int zIndex,
            int instance,
            int seed,
            float minimumHeight,
            float maximumHeight,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv,
            List<Color> colors,
            List<int> triangles)
        {
            var resolution = data.detailResolution;
            var jitterX =
                DetailHash01(
                    xIndex + instance * 29,
                    zIndex + instance * 11,
                    seed) -
                0.5f;
            var jitterZ =
                DetailHash01(
                    xIndex + instance * 7,
                    zIndex + instance * 31,
                    seed ^ 0x54A3) -
                0.5f;
            var normalizedX =
                Mathf.Clamp01(
                    (xIndex + 0.5f + jitterX * 0.78f) /
                    resolution);
            var normalizedZ =
                Mathf.Clamp01(
                    (zIndex + 0.5f + jitterZ * 0.78f) /
                    resolution);
            var center = new Vector3(
                normalizedX * TerrainWidth - TerrainWidth * 0.5f,
                data.GetInterpolatedHeight(normalizedX, normalizedZ) +
                0.015f,
                normalizedZ * TerrainLength - TerrainLength * 0.5f);
            var rotation =
                DetailHash01(
                    xIndex + instance * 17,
                    zIndex - instance * 13,
                    seed ^ 0x2C91) *
                Mathf.PI *
                2f;
            var clumpHeight = Mathf.Lerp(
                minimumHeight,
                maximumHeight,
                DetailHash01(
                    xIndex - instance * 19,
                    zIndex + instance * 23,
                    seed ^ 0x7135));
            var clumpWidth =
                Mathf.Lerp(0.028f, 0.052f, clumpHeight);
            var phase =
                DetailHash01(xIndex, zIndex, seed ^ instance);
            for (var blade = 0; blade < 6; blade++)
            {
                var angle =
                    rotation +
                    blade * (Mathf.PI / 3f) +
                    (phase - 0.5f) * 0.26f;
                var right = new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle));
                var normal = new Vector3(
                    -right.z,
                    0.24f,
                    right.x).normalized;
                var bladeHeight =
                    clumpHeight *
                    Mathf.Lerp(
                        0.78f,
                        1.06f,
                        DetailHash01(
                            xIndex + blade * 37,
                            zIndex - blade * 43,
                            seed));
                var offset =
                    right *
                    ((blade % 3) - 1f) *
                    (0.035f + phase * 0.025f);
                var lean = normal * (0.05f + phase * 0.05f);
                var baseIndex = vertices.Count;
                vertices.Add(center + offset - right * clumpWidth);
                vertices.Add(center + offset + right * clumpWidth);
                vertices.Add(
                    center + offset + lean +
                    Vector3.up * bladeHeight);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                uv.Add(new Vector2(0.07f, 0.05f));
                uv.Add(new Vector2(0.44f, 0.05f));
                uv.Add(new Vector2(0.25f, 0.45f));
                colors.Add(new Color(0f, phase, 0f, 1f));
                colors.Add(new Color(0f, phase, 0f, 1f));
                colors.Add(new Color(1f, phase, 1f, 1f));
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
            }
        }

        private static float DetailHash01(
            int x,
            int z,
            int seed)
        {
            unchecked
            {
                var hash =
                    (uint)(x * 0x1F123BB5) ^
                    (uint)(z * 0x05491333) ^
                    (uint)seed;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float EvaluateHeight(float worldX, float worldZ)
        {
            var radial = BoundaryRadius(worldX, worldZ);
            // La forma dell'isola è dichiarata in StarterIslandTerraceField:
            // una scala di ripiani piatti a quote di progetto, ciascuno con la
            // propria parete. Sentiero, fiume e bacini arrivano dopo, qui sotto.
            var height = StarterIslandTerraceField.Evaluate(worldX, worldZ);

            // Le rotte sono le uniche rampe: con pareti nette l'isola sarebbe
            // altrimenti chiusa, e si deve vedere da dove si sale.
            //
            // La rampa è la media della quota lungo il sentiero su ±24 m. Su un
            // ripiano piatto la media di una costante è la costante, quindi non
            // lo tocca; su un gradino lo distribuisce su quarantotto metri, che
            // tiene il salto più alto della scala, i venti metri fra ripiano
            // occidentale e primo anello, a 22,6° di pendenza.
            //
            // Il primo tentativo allargava invece la parete dentro il
            // corridoio, e i ripiani sconfinavano l'uno nell'altro: l'arrivo
            // dichiarava 19,2 e misurava 23,37.
            var nearestRoute = -1;
            var nearestDistance = float.PositiveInfinity;
            var nearestProgress = 0f;
            for (var index = 0; index < Routes.Length; index++)
            {
                var sample = ClosestRouteSample(
                    new Vector2(worldX, worldZ),
                    Routes[index]);
                if (sample.Distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = sample.Distance;
                nearestProgress = sample.Progress;
                nearestRoute = index;
            }

            if (nearestRoute >= 0)
            {
                var route = Routes[nearestRoute];
                var corridor =
                    1f - SmootherStep(
                        route.HalfWidth,
                        route.HalfWidth + 12f,
                        nearestDistance);
                if (corridor > 0f)
                {
                    var totalArc = RouteArcLength(route);
                    var centreArc = nearestProgress * totalArc;
                    var graded = 0f;
                    const int taps = 9;
                    for (var tap = 0; tap < taps; tap++)
                    {
                        var arc = Mathf.Clamp(
                            centreArc + (tap - taps / 2) * 6f,
                            0f,
                            totalArc);
                        var position = RoutePositionAtArc(route, arc);
                        graded += StarterIslandTerraceField.Evaluate(
                            position.x,
                            position.y);
                    }

                    height = Mathf.Lerp(
                        height,
                        graded / taps,
                        corridor);
                }
            }

            height = ApplyPondAndStream(worldX, worldZ, height);

            // La grana fine è già nel campo dei ripiani, ±0,15 m. Qui c'era una
            // seconda passata da ±0,4 m che, sommata, avrebbe riportato i piani
            // fuori dal criterio di mezzo metro di oscillazione e reso i nastri
            // di nuovo instabili.

            // Terrain owns the walkable top only. It reaches the lip without
            // diving fifty metres; the separate rock underbody owns the drop.
            // This removes stretched heightfield triangles and texture smears.
            var edgeSoftening = SmootherStep(0.965f, 0.995f, radial);
            height -= edgeSoftening *
                      (0.45f +
                       0.25f *
                       (0.5f +
                        0.5f *
                        Mathf.Sin(
                            Mathf.Atan2(worldZ, worldX) * 7f + 0.4f)));
            return Mathf.Clamp(height, 8f, TerrainHeight - 0.5f);
        }

        private static void EvaluateSurfaceWeights(
            TerrainData data,
            float xNormalized,
            float zNormalized,
            float worldX,
            float worldZ,
            out float grassSun,
            out float grassDeep,
            out float dirt,
            out float cliff)
        {
            var slope = data.GetSteepness(xNormalized, zNormalized);
            var height = data.GetInterpolatedHeight(
                xNormalized,
                zNormalized);
            var path = ClosestPathSample(new Vector2(worldX, worldZ));

            // Le pareti diventano roccia per pendenza, ma il confine non deve
            // disegnare una linea vettoriale attorno a ogni ripiano. Le due
            // frequenze spostano localmente la soglia di circa +/-4 gradi.
            var cliffEdgeNoise =
                (Mathf.PerlinNoise(
                     (worldX + 179f) * 0.045f,
                     (worldZ + 293f) * 0.045f) - 0.5f) * 6.6f +
                (Mathf.PerlinNoise(
                     (worldX + 61f) * 0.13f,
                     (worldZ + 17f) * 0.13f) - 0.5f) * 3.4f;
            cliff = SmoothStep(
                42f + cliffEdgeNoise,
                61f + cliffEdgeNoise,
                slope);

            // The path boundary is a layered organic mask, not a uniformly
            // blurred ribbon. Broad bends establish the silhouette, medium
            // noise creates tongues, and fine noise breaks the last metre.
            var broadEdgeNoise =
                (Mathf.PerlinNoise(
                     (worldX + 180f) * 0.027f,
                     (worldZ + 240f) * 0.027f) - 0.5f) * 2.20f;
            var tongueNoise =
                (Mathf.PerlinNoise(
                     (worldX + 54f) * 0.095f,
                     (worldZ + 127f) * 0.095f) - 0.5f) * 1.65f;
            var fineEdgeNoise =
                (Mathf.PerlinNoise(
                     (worldX + 311f) * 0.23f,
                     (worldZ + 83f) * 0.23f) - 0.5f) * 0.45f;
            var pathRadius =
                path.HalfWidth +
                broadEdgeNoise +
                tongueNoise +
                fineEdgeNoise +
                Mathf.Sin(
                    path.Progress * Mathf.PI * 10.6f + 0.7f) * 0.46f +
                Mathf.Sin(
                    path.Progress * Mathf.PI * 21.8f + 1.9f) * 0.22f;
            var transitionWidth =
                Mathf.Lerp(
                    0.54f,
                    0.86f,
                    Mathf.PerlinNoise(
                        (worldX + 12f) * 0.041f,
                        (worldZ + 296f) * 0.041f));
            var pathMask =
                1f - SmootherStep(
                    pathRadius - transitionWidth,
                    pathRadius + transitionWidth,
                    path.Distance);

            // Local high-noise pockets cut green bites back into the sand.
            // The centre remains readable and traversable; the irregularity
            // is concentrated along the verge like the visual reference.
            var intrusionNoise =
                Mathf.PerlinNoise(
                    (worldX + 227f) * 0.115f,
                    (worldZ + 41f) * 0.115f) * 0.68f +
                Mathf.PerlinNoise(
                    (worldX + 19f) * 0.047f,
                    (worldZ + 173f) * 0.047f) * 0.32f;
            var intrusionBand =
                1f - SmootherStep(
                    0.18f,
                    2.25f,
                    Mathf.Abs(
                        path.Distance -
                        (pathRadius - 0.30f)));
            var protectCore =
                SmoothStep(1.55f, 3.10f, path.Distance);
            var grassIntrusion =
                SmoothStep(0.52f, 0.72f, intrusionNoise) *
                intrusionBand *
                protectCore;
            pathMask *= 1f - grassIntrusion * 0.95f;

            // A second mask creates a few genuine grass islands inside the
            // verge instead of only deforming the outer silhouette. The
            // central four metres remain protected for readability.
            var islandNoise =
                Mathf.PerlinNoise(
                    (worldX + 137f) * 0.073f,
                    (worldZ + 311f) * 0.073f) * 0.62f +
                Mathf.PerlinNoise(
                    (worldX + 29f) * 0.16f,
                    (worldZ + 67f) * 0.16f) * 0.38f;
            var islandBand =
                SmoothStep(1.55f, 2.45f, path.Distance) *
                (1f -
                 SmoothStep(3.75f, 5.10f, path.Distance));
            var grassIsland =
                Mathf.Max(
                    SmoothStep(0.55f, 0.72f, islandNoise),
                    SmoothStep(
                        0.28f,
                        0.72f,
                        0.50f +
                        Mathf.Sin(
                            path.Progress * Mathf.PI * 11.4f +
                            2.1f) * 0.27f +
                        Mathf.Sin(
                            path.Progress * Mathf.PI * 18.2f +
                            0.4f) * 0.18f) * 0.88f) *
                islandBand;
            pathMask *= 1f - grassIsland * 0.98f;
            pathMask *= 1f - SmoothStep(28f, 40f, slope);
            pathMask *= IsInsideIslandSurface(worldX, worldZ) ? 1f : 0f;
            var wear =
                Mathf.Lerp(
                    0.90f,
                    1.00f,
                    SmoothStep(
                        0.28f,
                        0.74f,
                        Mathf.PerlinNoise(
                            (worldX + 71f) * 0.11f,
                            (worldZ + 93f) * 0.11f)));
            dirt = pathMask * wear;

            // Battigia del lago: una fascia stretta di sabbia bagnata a
            // cavallo del pelo dell'acqua, non un disco. Sabbiare tutto il
            // fondale rendeva l'acqua un piatto grigio-lavanda, perché il
            // fondo chiaro traspariva attraverso la trasparenza e le lavava
            // via il turchese; il fondale scuro è ciò che dà colore all'acqua.
            // Il bordo esterno è mosso da un rumore per non essere un'ellisse.
            var shoreJitter =
                (Mathf.PerlinNoise(
                     (worldX + 401f) * 0.085f,
                     (worldZ + 158f) * 0.085f) - 0.5f) * 0.09f;
            for (var index = 0; index < WaterBasins.Length; index++)
            {
                var basin = WaterBasins[index];
                var basinDistance =
                    EllipseDistance(
                        worldX,
                        worldZ,
                        basin.CenterX,
                        basin.CenterZ,
                        basin.RadiusX,
                        basin.RadiusZ);
                var basinShore =
                    (1f -
                     SmootherStep(
                         0.05f,
                         0.15f + shoreJitter,
                         Mathf.Abs(basinDistance - 1f))) *
                    (1f - cliff);
                dirt = Mathf.Max(dirt, basinShore);
            }

            // Il peso di GrassDeep arrivava al massimo a 0.28: il prato era
            // coperto per circa il 90% da un solo strato, quindi leggeva come
            // una campitura unica per costruzione, non per colpa del colore.
            // Ora i due verdi si alternano a chiazze larghe, medie e fini che
            // raggiungono pesi pieni, e la quota resta solo una tendenza.
            var patchNoise =
                Mathf.PerlinNoise(
                    (worldX + 90f) * 0.0125f,
                    (worldZ + 140f) * 0.0125f) * 0.55f +
                Mathf.PerlinNoise(
                    (worldX + 418f) * 0.043f,
                    (worldZ + 262f) * 0.043f) * 0.30f +
                Mathf.PerlinNoise(
                    (worldX + 733f) * 0.128f,
                    (worldZ + 51f) * 0.128f) * 0.15f;
            var shadeBias =
                Mathf.Clamp01((height - 58f) / 72f) * 0.10f;
            // Finestra stretta di proposito: la somma di tre Perlin si
            // concentra intorno a 0.5, quindi una finestra larga dava una
            // miscela 50/50 quasi ovunque, che mediata è piatta esattamente
            // come un solo strato. Così le chiazze saturano su un verde o
            // sull'altro e restano solo le fasce di transizione.
            var deepWeight =
                SmootherStep(0.45f, 0.57f, patchNoise + shadeBias);
            grassDeep =
                (1f - cliff) * (1f - dirt) * deepWeight;
            grassSun =
                Mathf.Max(
                    0f,
                    1f - cliff - dirt - grassDeep);

            var sum = grassSun + grassDeep + dirt + cliff;
            if (sum <= 0.0001f)
            {
                grassSun = 1f;
                grassDeep = 0f;
                dirt = 0f;
                cliff = 0f;
                return;
            }

            grassSun /= sum;
            grassDeep /= sum;
            dirt /= sum;
            cliff /= sum;
        }

        private static GameObject BuildTerrainPrefab(TerrainData data)
        {
            var root = new GameObject("PF_StarterIsland_Terrain");
            try
            {
                var terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.name = TerrainObjectName;
                terrainObject.transform.SetParent(root.transform, false);
                terrainObject.transform.localPosition = new Vector3(
                    -TerrainWidth * 0.5f,
                    0f,
                    -TerrainLength * 0.5f);
                terrainObject.transform.localRotation = Quaternion.identity;
                terrainObject.transform.localScale = Vector3.one;

                var terrain = terrainObject.GetComponent<Terrain>();
                terrain.drawInstanced = true;
                terrain.heightmapPixelError = 1f;
                terrain.basemapDistance = 1500f;
                terrain.detailObjectDistance = 92f;
                terrain.detailObjectDensity = 1f;
                terrain.drawTreesAndFoliage = true;
                terrain.shadowCastingMode = ShadowCastingMode.On;
                terrain.reflectionProbeUsage =
                    ReflectionProbeUsage.BlendProbes;
                terrain.materialTemplate = BuildTerrainMaterial();

                var collider = terrainObject.GetComponent<TerrainCollider>();
                collider.terrainData = data;
                collider.enabled = true;

                var underbodyRim =
                    StarterIslandUnderbodyBuilder.SampleRim(
                        StarterIslandUnderbodyBuilder
                            .RecommendedRimSampleCount,
                        angle => SampleUnderbodyRimPoint(data, angle));
                StarterIslandUnderbodyBuilder.BuildOrUpdate(
                    root.transform,
                    underbodyRim,
                    BuildUnderbodyMaterial());

                var gameplayRoot = new GameObject("GameplayRoot");
                gameplayRoot.transform.SetParent(root.transform, false);
                for (var index = 0; index < Markers.Length; index++)
                {
                    var markerDefinition = Markers[index];
                    var marker = new GameObject(markerDefinition.Name);
                    marker.transform.SetParent(
                        gameplayRoot.transform,
                        false);
                    marker.transform.localPosition = new Vector3(
                        markerDefinition.X,
                        SampleLocalHeight(data,
                            markerDefinition.X,
                            markerDefinition.Z) + 0.03f,
                        markerDefinition.Z);
                }

                GameObjectUtility.SetStaticEditorFlags(
                    terrainObject,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.ReflectionProbeStatic);

                var prefab =
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save Terrain prefab: {PrefabPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Material BuildTerrainMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Terrain Splat");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The production Starter Island Terrain shader is " +
                    "missing or failed to import.");
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_Terrain.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_Terrain"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.02f);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            if (material.HasProperty("_SpecularHighlights"))
            {
                material.SetFloat("_SpecularHighlights", 0f);
            }

            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (material.HasProperty("_TerrainSizeXZ"))
            {
                material.SetVector(
                    "_TerrainSizeXZ",
                    new Vector4(
                        TerrainWidth,
                        TerrainLength,
                        0f,
                        0f));
            }

            SetFloat(material, "_AmbientStrength", 0.66f);
            SetFloat(material, "_ShadowFloor", 0.16f);
            SetFloat(material, "_CliffTriplanarSharpness", 3.4f);
            // Geometry-safe surface pass: the cliff keeps the exact original
            // Terrain normal. This removes the triangular/V-shaped projection
            // artifacts without changing a single height sample.
            SetFloat(material, "_CliffNormalStrength", 0f);
            SetFloat(material, "_CliffMacroVariation", 0.045f);
            SetFloat(material, "_CliffRunoffVariation", 0.003f);
            SetFloat(material, "_CliffBrightness", 0.98f);
            SetColor(material, "_CliffShadowColor", Html("#66545E"));
            SetColor(material, "_CliffBaseColor", Html("#9A7068"));
            SetColor(material, "_CliffHighlightColor", Html("#BE8E79"));
            SetFloat(material, "_CliffPaletteStrength", 0.78f);
            SetColor(material, "_CliffCavityColor", Html("#584B56"));
            SetFloat(material, "_CliffCavityStrength", 0.20f);
            SetFloat(material, "_CliffReliefNormalStrength", 0f);
            SetFloat(material, "_CliffMicroNormalStrength", 0f);
            SetFloat(material, "_CliffLightingContrast", 0.82f);
            SetFloat(material, "_CliffAmbientReduction", 0.24f);
            SetColor(material, "_CliffStrataColor", Html("#75565B"));
            SetFloat(material, "_CliffStrataScale", 0.38f);
            SetFloat(material, "_CliffStrataStrength", 0.24f);
            SetColor(material, "_CliffLichenColor", Html("#718052"));
            SetFloat(material, "_CliffLichenScale", 0.16f);
            SetFloat(material, "_CliffLichenStrength", 0f);
            SetColor(material, "_CliffSoilColor", Html("#5A4638"));
            SetFloat(material, "_CliffSoilStrength", 0f);
            SetFloat(material, "_CliffSpecularStrength", 0.020f);
            SetFloat(material, "_CliffNormalFadeStart", 72f);
            SetFloat(material, "_CliffNormalFadeEnd", 210f);
            // Era 0.028, cioe' una variazione di brillantezza del 2,8%:
            // spenta. E' la sola variazione che sopravvive al mip-mapping,
            // quindi l'unica che puo' rompere la campitura a mezza distanza.
            SetFloat(material, "_MacroVariation", 0.13f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildGroundDetailMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Ground Detail");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island ground detail shader is unavailable.");
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_GroundDetail.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_GroundDetail"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            SetColor(material, "_BaseColor", Html("#4F7437"));
            SetColor(material, "_TipColor", Html("#789548"));
            SetColor(material, "_ShadowColor", Html("#30492B"));
            SetFloat(material, "_WindStrength", 0.080f);
            SetFloat(material, "_WindSpeed", 1.00f);
            SetFloat(material, "_AmbientStrength", 0.72f);
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture(
                    "_MainTex",
                    CreateOrUpdateDetailGrassTexture());
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D CreateOrUpdateDetailGrassTexture()
        {
            const int resolution = 8;
            var path =
                TexturesRoot +
                "/T_StarterIsland_GrassDetailColor.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(
                    resolution,
                    resolution,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name = "T_StarterIsland_GrassDetailColor"
                };
                AssetDatabase.CreateAsset(texture, path);
            }
            else if (texture.width != resolution ||
                     texture.height != resolution)
            {
                texture.Reinitialize(
                    resolution,
                    resolution,
                    TextureFormat.RGBA32,
                    true);
            }

            var baseColor = Html("#5F8F3E");
            var tipColor = Html("#92BE55");
            var pixels = new Color[resolution * resolution];
            for (var y = 0; y < resolution; y++)
            {
                var heightBlend = y / (float)(resolution - 1);
                for (var x = 0; x < resolution; x++)
                {
                    var variation =
                        ((x * 17 + y * 29) & 3) * 0.018f - 0.027f;
                    var color =
                        Color.Lerp(
                            baseColor,
                            tipColor,
                            0.18f + heightBlend * 0.70f);
                    pixels[y * resolution + x] =
                        new Color(
                            Mathf.Clamp01(color.r + variation),
                            Mathf.Clamp01(color.g + variation),
                            Mathf.Clamp01(color.b + variation * 0.45f),
                            1f);
                }
            }

            texture.SetPixels(pixels);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply(true, false);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Material BuildUnderbodyMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Stylized Surface");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island stylized surface shader is unavailable.");
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_UnderbodyCliff.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_UnderbodyCliff"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            SetColor(material, "_BaseColor", Html("#B96043"));
            SetColor(material, "_SecondaryColor", Html("#754139"));
            SetColor(material, "_WetColor", Html("#4E3737"));
            SetFloat(material, "_VertexBlend", 1f);
            SetFloat(material, "_AmbientStrength", 0.50f);
            SetFloat(material, "_ShadowFloor", 0.14f);
            SetFloat(material, "_ColorVariation", 0.085f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildReviewScene(GameObject terrainPrefab)
        {
            InventoryHudAssetSetup.EnsureAssets();
            var previousActive = SceneManager.GetActiveScene();
            var useAdditive =
                previousActive.IsValid() &&
                previousActive.isLoaded &&
                !string.IsNullOrEmpty(previousActive.path);
            if (!useAdditive &&
                !Application.isBatchMode &&
                previousActive.IsValid() &&
                previousActive.isDirty)
            {
                throw new InvalidOperationException(
                    "Save or close the current untitled scene before " +
                    "rebuilding the Terrain review scene.");
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                useAdditive
                    ? NewSceneMode.Additive
                    : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            try
            {
                var reviewRoot = new GameObject(ReviewRootName);
                reviewRoot.AddComponent<GeneratedSceneRevision>().Configure(
                    SceneId,
                    SceneRevision);

                var island = PrefabUtility.InstantiatePrefab(
                    terrainPrefab,
                    scene) as GameObject;
                if (island == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the Terrain island prefab.");
                }

                island.name = "PF_StarterIsland_Terrain";
                island.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                island.transform.localScale = Vector3.one;

                var terrain =
                    island.GetComponentInChildren<Terrain>(true);
                if (terrain == null)
                {
                    throw new InvalidOperationException(
                        "Generated Terrain prefab does not contain Terrain.");
                }

                CreateLighting();
                CreateWater(terrain);
                ScatterVegetation(terrain);
                ScatterRocks(terrain);
                StarterIslandMiningSourcesSetup.BuildIntoScene(
                    scene,
                    terrain,
                    replaceExisting: true);
                CreateReviewLandmarks(terrain);
                CreatePlayableAirshipRig(island, terrain, reviewRoot.transform);

                if (!EditorSceneManager.SaveScene(
                        scene,
                        ReviewScenePath))
                {
                    throw new InvalidOperationException(
                        $"Could not save Terrain review scene: " +
                        ReviewScenePath);
                }
            }
            finally
            {
                if (useAdditive)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (previousActive.IsValid() && previousActive.isLoaded)
                    {
                        SceneManager.SetActiveScene(previousActive);
                    }
                }
            }

            EnsureReviewSceneIncludedInBuild();
        }

        private static void CreateLighting()
        {
            RenderSettings.skybox = BuildSkyboxMaterial();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Keep the ambient contribution chromatically neutral. The old
            // green ground light contaminated every neutral asset (especially
            // the rocks), making grey albedo read as muddy olive.
            RenderSettings.ambientSkyColor = Html("#B8CDD1");
            RenderSettings.ambientEquatorColor = Html("#BDB9A8");
            RenderSettings.ambientGroundColor = Html("#66645D");
            RenderSettings.ambientIntensity = 0.48f;
            RenderSettings.defaultReflectionMode =
                DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.86f;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Html("#B8DFE8");
            RenderSettings.fogStartDistance = 900f;
            RenderSettings.fogEndDistance = 1800f;

            var sunObject = new GameObject("ENV_Sun");
            sunObject.transform.rotation =
                Quaternion.Euler(42f, -34f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Html("#FFD09C");
            sun.intensity = 1.68f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.88f;
            sun.shadowBias = 0.025f;
            sun.shadowNormalBias = 0.20f;
            RenderSettings.sun = sun;

            var volumeObject =
                new GameObject("ENV_GlobalColorGrading");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 1f;
            volume.sharedProfile = BuildColorGradingProfile();
            DynamicGI.UpdateEnvironment();
        }

        private static VolumeProfile BuildColorGradingProfile()
        {
            var path =
                MaterialsRoot + "/VP_StarterIsland_ColorGrading.asset";
            var profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "VP_StarterIsland_ColorGrading";
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.components.RemoveAll(component => component == null);
            var tonemapping =
                GetOrCreateVolumeComponent<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.Neutral);

            var whiteBalance =
                GetOrCreateVolumeComponent<WhiteBalance>(profile);
            whiteBalance.active = true;
            whiteBalance.temperature.Override(9f);
            whiteBalance.tint.Override(0f);

            var adjustments =
                GetOrCreateVolumeComponent<ColorAdjustments>(profile);
            adjustments.active = true;
            adjustments.postExposure.Override(0.42f);
            adjustments.contrast.Override(0f);
            adjustments.colorFilter.Override(Html("#FFF6EE"));
            adjustments.hueShift.Override(0f);
            adjustments.saturation.Override(-3f);

            // Grade tonal ranges separately: lifted olive-neutral shadows,
            // gently warm midtones and peach sunlight. This gives the scene
            // the reference warmth without baking beige into grey assets.
            var tonalRanges =
                GetOrCreateVolumeComponent<ShadowsMidtonesHighlights>(
                    profile);
            tonalRanges.active = true;
            tonalRanges.shadows.Override(
                new Vector4(0.985f, 1.000f, 0.970f, 0.008f));
            tonalRanges.midtones.Override(
                new Vector4(1.000f, 0.992f, 0.970f, 0.005f));
            tonalRanges.highlights.Override(
                new Vector4(1.000f, 0.978f, 0.945f, 0.002f));
            tonalRanges.shadowsStart.Override(0f);
            tonalRanges.shadowsEnd.Override(0.34f);
            tonalRanges.highlightsStart.Override(0.56f);
            tonalRanges.highlightsEnd.Override(1f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrCreateVolumeComponent<T>(
            VolumeProfile profile)
            where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component))
            {
                component = profile.Add<T>(true);
            }

            component.name = typeof(T).Name;
            if (!AssetDatabase.Contains(component))
            {
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            EditorUtility.SetDirty(component);
            return component;
        }

        private static Material BuildSkyboxMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            if (shader == null)
            {
                shader = Shader.Find("Skybox/Procedural");
            }

            if (shader == null)
            {
                return null;
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_Skybox.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_Skybox"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            SetColor(material, "_ZenithColor", Html("#2B83D2"));
            SetColor(material, "_HorizonColor", Html("#A4DDEA"));
            SetColor(material, "_LowerColor", Html("#B8CDB4"));
            SetColor(material, "_CloudColor", Html("#C8DAD7"));
            SetColor(material, "_CloudShadowColor", Html("#85A7B2"));
            SetColor(material, "_SunColor", Html("#F7CB82"));
            if (material.HasProperty("_SunDirection"))
            {
                var sunDirection =
                    -(Quaternion.Euler(42f, -34f, 0f) *
                      Vector3.forward);
                material.SetVector(
                    "_SunDirection",
                    new Vector4(
                        sunDirection.x,
                        sunDirection.y,
                        sunDirection.z,
                        0f));
            }

            SetFloat(material, "_SunSize", 0.0045f);
            SetFloat(material, "_CloudScale", 0.46f);
            SetFloat(material, "_CloudCoverage", 0.51f);
            SetFloat(material, "_CloudSoftness", 0.065f);
            SetFloat(material, "_CloudSpeed", 0.015f);
            SetFloat(material, "_CloudOpacity", 0.62f);
            SetFloat(material, "_Exposure", 0.98f);

            // Fallback values remain harmless when the atmospheric shader is
            // temporarily unavailable during its first import.
            SetColor(material, "_SkyTint", Html("#8FD9EB"));
            SetColor(material, "_GroundColor", Html("#AFCBB9"));
            SetFloat(material, "_AtmosphereThickness", 0.72f);
            SetFloat(material, "_SunSizeConvergence", 4.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateWater(Terrain terrain)
        {
            var root = new GameObject("WaterRoot");
            var waterMaterial = BuildWaterMaterial();
            StarterIslandWaterBuilder.Build(
                root.transform,
                waterMaterial);
        }

        private static void CreatePondWater(
            Transform parent,
            Material material)
        {
            const int radialSegments = 64;
            const int rings = 7;
            var vertices =
                new Vector3[1 + radialSegments * rings];
            var uv = new Vector2[vertices.Length];
            var triangles =
                new int[
                    radialSegments * 3 +
                    (rings - 1) * radialSegments * 6];

            vertices[0] = new Vector3(-178f, WaterHeight, -72f);
            uv[0] = new Vector2(0.5f, 0.5f);
            for (var ring = 1; ring <= rings; ring++)
            {
                var t = ring / (float)rings;
                for (var segment = 0;
                     segment < radialSegments;
                     segment++)
                {
                    var angle =
                        segment * Mathf.PI * 2f / radialSegments;
                    var edgeVariation =
                        1f +
                        0.055f * Mathf.Sin(angle * 3f + 0.45f) +
                        0.032f * Mathf.Sin(angle * 7f - 0.90f) +
                        0.018f * Mathf.Sin(angle * 11f + 0.20f);
                    var organicRadius =
                        Mathf.Lerp(
                            1f,
                            edgeVariation,
                            t * t * (3f - 2f * t));
                    var x =
                        Mathf.Cos(angle) * 40f * t * organicRadius;
                    var z =
                        Mathf.Sin(angle) * 27f * t *
                        (1f +
                         (edgeVariation - 1f) * 0.74f);
                    var vertex = 1 +
                                 (ring - 1) * radialSegments +
                                 segment;
                    vertices[vertex] =
                        new Vector3(
                            -178f + x,
                            WaterHeight,
                            -72f + z);
                    uv[vertex] =
                        new Vector2(
                            0.5f + x / 80f,
                            0.5f + z / 54f);
                }
            }

            var triangle = 0;
            for (var segment = 0;
                 segment < radialSegments;
                 segment++)
            {
                var next = (segment + 1) % radialSegments;
                triangles[triangle++] = 0;
                triangles[triangle++] = 1 + segment;
                triangles[triangle++] = 1 + next;
            }

            for (var ring = 1; ring < rings; ring++)
            {
                var innerStart =
                    1 + (ring - 1) * radialSegments;
                var outerStart =
                    1 + ring * radialSegments;
                for (var segment = 0;
                     segment < radialSegments;
                     segment++)
                {
                    var next = (segment + 1) % radialSegments;
                    triangles[triangle++] = innerStart + segment;
                    triangles[triangle++] = outerStart + segment;
                    triangles[triangle++] = innerStart + next;
                    triangles[triangle++] = innerStart + next;
                    triangles[triangle++] = outerStart + segment;
                    triangles[triangle++] = outerStart + next;
                }
            }

            var meshPath =
                DataRoot + "/MD_StarterIsland_PondWater.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "GEO_StarterIsland_PondWater"
                };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                mesh.Clear();
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var pond = new GameObject("ENV_PondWater");
            pond.transform.SetParent(parent, false);
            pond.AddComponent<MeshFilter>().sharedMesh = mesh;
            pond.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Taglia il percorso del ruscello dove incontra la riva dello stagno.
        /// Restituisce i punti fino al primo che entra nell'ellisse dello
        /// stagno, più un punto di confine interpolato sulla riva: così il
        /// nastro confina con lo specchio d'acqua invece di sovrapporvisi.
        /// </summary>
        private static List<Vector3> TrimRouteAtPondShore(
            IReadOnlyList<Vector3> route)
        {
            const float centreX = -178f;
            const float centreZ = -72f;
            // Riva utile leggermente interna ai raggi del disco, così la
            // giunzione cade dove l'acqua dello stagno è già presente.
            const float radiusX = 38.5f;
            const float radiusZ = 25.5f;

            static float Normalised(Vector3 point)
            {
                var dx = (point.x - centreX) / radiusX;
                var dz = (point.z - centreZ) / radiusZ;
                return dx * dx + dz * dz;
            }

            var trimmed = new List<Vector3>();
            for (var index = 0; index < route.Count; index++)
            {
                var point = route[index];
                if (Normalised(point) > 1f)
                {
                    trimmed.Add(point);
                    continue;
                }

                if (index == 0)
                {
                    break;
                }

                // Interpolazione sull'ellisse fra l'ultimo punto esterno e il
                // primo interno: la testata cade esattamente sulla riva.
                var previous = route[index - 1];
                var low = 0f;
                var high = 1f;
                for (var iteration = 0; iteration < 24; iteration++)
                {
                    var mid = (low + high) * 0.5f;
                    if (Normalised(Vector3.Lerp(previous, point, mid)) > 1f)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                var shore = Vector3.Lerp(previous, point, high);
                trimmed.Add(new Vector3(shore.x, WaterHeight + 0.01f, shore.z));
                break;
            }

            if (trimmed.Count < 2)
            {
                throw new InvalidOperationException(
                    "Il percorso del ruscello non raggiunge la riva dello stagno.");
            }

            return trimmed;
        }

        private static void CreateStreamWater(
            Terrain terrain,
            Transform parent,
            Material material)
        {
            // Il nastro del ruscello si ferma SULLA RIVA dello stagno, non
            // dentro.  Prima l'ultimo punto era (-178, WaterHeight + 0.01, -54)
            // mentre lo stagno è centrato in (-178, -72) con raggio 27 lungo Z:
            // gli ultimi ~18 m scorrevano un centimetro sopra la superficie
            // dello stagno e, con ZWrite Off e Cull Off, le due trasparenze si
            // fondevano due volte.  Da lì la banda chiara e il bordo dritto che
            // sembrava una tacca.
            var authoredPoints = TrimRouteAtPondShore(StreamRoute);
            var sampledPoints = new List<Vector3>();
            for (var segment = 0;
                 segment < authoredPoints.Count - 1;
                 segment++)
            {
                var start = authoredPoints[segment];
                var end = authoredPoints[segment + 1];
                // Le suddivisioni vanno ricavate dalla distanza REALE in 3D.
                // Con la sola distanza orizzontale una caduta verticale dava
                // ceil(~0 / 1.6) = 1: l'intera cascata era un unico quad
                // piatto, con una piega netta al ciglio e la texture stirata.
                var spanDistance = Vector3.Distance(start, end);
                var horizontalDistance =
                    Vector2.Distance(
                        new Vector2(start.x, start.z),
                        new Vector2(end.x, end.z));
                // Sui tratti ripidi si infittisce: il ciglio diventa una curva
                // di facce piccole invece di uno spigolo unico.
                var isSteep =
                    horizontalDistance <= 0.001f ||
                    Mathf.Abs(end.y - start.y) / horizontalDistance > 0.5f;
                var sampleSpacing = isSteep ? 0.55f : 1.6f;
                var subdivisions = Mathf.Max(
                    1,
                    Mathf.CeilToInt(spanDistance / sampleSpacing));
                for (var step = 0; step < subdivisions; step++)
                {
                    var t = step / (float)subdivisions;
                    var point = Vector3.Lerp(start, end, t);
                    sampledPoints.Add(
                        new Vector3(
                            point.x,
                            point.y + 0.08f,
                            point.z));
                }
            }

            var finalPoint = authoredPoints[authoredPoints.Count - 1];
            sampledPoints.Add(
                new Vector3(
                    finalPoint.x,
                    WaterHeight + 0.01f,
                    finalPoint.z));
            var points = sampledPoints.ToArray();
            var pointCount = points.Length;

            const int crossSections = 5;
            var vertices =
                new Vector3[pointCount * crossSections];
            var uv = new Vector2[vertices.Length];
            var triangles =
                new int[
                    (pointCount - 1) *
                    (crossSections - 1) * 6];
            // Larghezze precalcolate e poi lisciate: la svasatura sul ripido
            // entrava in un solo campione e produceva uno scalino visibile nel
            // profilo proprio al ciglio della cascata.
            var widths = new float[pointCount];
            for (var index = 0; index < pointCount; index++)
            {
                var previous = points[Mathf.Max(0, index - 1)];
                var next = points[Mathf.Min(pointCount - 1, index + 1)];
                var baseWidth =
                    Mathf.Lerp(
                        1.65f,
                        2.65f,
                        index / (float)(pointCount - 1));
                var horizontal =
                    Vector3.ProjectOnPlane(
                        next - previous,
                        Vector3.up).magnitude;
                var localGrade =
                    horizontal <= 0.001f
                        ? 2f
                        : Mathf.Abs(next.y - previous.y) / horizontal;
                widths[index] =
                    baseWidth +
                    SmoothStep(0.18f, 0.52f, localGrade) * 0.85f;
            }

            var smoothedWidths = new float[pointCount];
            for (var index = 0; index < pointCount; index++)
            {
                var total = 0f;
                var samples = 0;
                for (var offset = -3; offset <= 3; offset++)
                {
                    var probe = index + offset;
                    if (probe < 0 || probe >= pointCount)
                    {
                        continue;
                    }

                    total += widths[probe];
                    samples++;
                }

                smoothedWidths[index] = total / samples;
            }

            var travelled = 0f;
            for (var index = 0; index < pointCount; index++)
            {
                var previous = points[Mathf.Max(0, index - 1)];
                var next = points[Mathf.Min(pointCount - 1, index + 1)];
                var direction =
                    Vector3.ProjectOnPlane(next - previous, Vector3.up)
                        .normalized;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    // Tratto verticale: la proiezione orizzontale svanisce, va
                    // ripresa la direzione dell'ultimo tratto utile.
                    for (var probe = index - 1; probe > 0; probe--)
                    {
                        var candidate = Vector3.ProjectOnPlane(
                            points[probe] - points[probe - 1],
                            Vector3.up);
                        if (candidate.sqrMagnitude > 0.0001f)
                        {
                            direction = candidate.normalized;
                            break;
                        }
                    }
                }

                var perpendicular =
                    new Vector3(-direction.z, 0f, direction.x);
                var width = smoothedWidths[index];
                if (index > 0)
                {
                    travelled += Vector3.Distance(
                        points[index - 1],
                        points[index]);
                }

                for (var cross = 0;
                     cross < crossSections;
                     cross++)
                {
                    var crossT =
                        cross / (float)(crossSections - 1);
                    var crown =
                        Mathf.Sin(crossT * Mathf.PI) * 0.025f;
                    var vertex =
                        index * crossSections + cross;
                    vertices[vertex] =
                        points[index] +
                        perpendicular *
                        Mathf.Lerp(-width, width, crossT) +
                        Vector3.up * crown;
                    uv[vertex] =
                        new Vector2(
                            crossT,
                            travelled * 0.12f);
                }
            }

            for (var index = 0; index < pointCount - 1; index++)
            {
                for (var cross = 0;
                     cross < crossSections - 1;
                     cross++)
                {
                    var triangle =
                        (index * (crossSections - 1) + cross) * 6;
                    var vertex =
                        index * crossSections + cross;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] =
                        vertex + crossSections;
                    triangles[triangle + 2] = vertex + 1;
                    triangles[triangle + 3] = vertex + 1;
                    triangles[triangle + 4] =
                        vertex + crossSections;
                    triangles[triangle + 5] =
                        vertex + crossSections + 1;
                }
            }

            var meshPath =
                DataRoot + "/MD_StarterIsland_StreamWater.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "GEO_StarterIsland_StreamWater"
                };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                mesh.Clear();
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var stream = new GameObject("ENV_StreamWater");
            stream.transform.SetParent(parent, false);
            stream.AddComponent<MeshFilter>().sharedMesh = mesh;
            stream.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material BuildWaterMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Stylized Water");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No compatible water shader is available.");
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_TerrainWater.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_TerrainWater"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            SetColor(material, "_ShallowColor", Html("#45D0CE"));
            SetColor(material, "_DeepColor", Html("#0876A2"));
            SetColor(material, "_FoamColor", Html("#DCFDF4"));
            SetFloat(material, "_DepthRange", 4.8f);
            SetFloat(material, "_FoamDistance", 0.24f);
            SetFloat(material, "_FoamFeather", 0.18f);
            SetFloat(material, "_WaveScaleA", 0.095f);
            SetFloat(material, "_WaveScaleB", 0.31f);
            SetFloat(material, "_WaveSpeedA", 0.58f);
            SetFloat(material, "_WaveSpeedB", 1.34f);
            SetFloat(material, "_WaveStrength", 0.16f);
            SetFloat(material, "_DisplacementStrength", 0.045f);
            SetFloat(material, "_FlowScale", 1.95f);
            SetFloat(material, "_FlowSpeed", 1.52f);
            SetFloat(material, "_CascadeFoamStrength", 0.94f);
            SetFloat(material, "_FresnelPower", 3.0f);
            SetFloat(material, "_GlintPower", 74f);
            SetFloat(material, "_GlintStrength", 0.46f);
            SetFloat(material, "_RefractionStrength", 0.030f);
            SetFloat(material, "_FresnelStrength", 0.36f);
            SetFloat(material, "_Smoothness", 0.90f);
            SetFloat(material, "_ReflectionStrength", 0.74f);
            SetFloat(material, "_NormalDetailScale", 0.76f);
            SetFloat(material, "_NormalDetailSpeed", 1.82f);
            SetFloat(material, "_NormalDetailStrength", 0.090f);
            SetFloat(material, "_CascadeNormalStrength", 0.22f);
            SetFloat(material, "_ColorBoost", 1.04f);
            SetFloat(material, "_Opacity", 0.78f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ScatterVegetation(Terrain terrain)
        {
            var root = new GameObject("FoliageRoot");
            var commonTree = LoadFirst<GameObject>(
                "Assets/_Project/Art/Environment/StarterIsland/V4/" +
                "Trees/Prefabs/PF_ENV_Tree_CommonTall_A_LOD0.prefab",
                "Assets/_Project/Art/Environment/StarterIsland/V4/" +
                "Trees/Models/ENV_Tree_CommonTall_A_LOD0.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Foliage/" +
                "Prefabs/PF_Tree_CommonTall_A.prefab");
            var autumnTree = LoadFirst<GameObject>(
                "Assets/_Project/Art/Environment/StarterIsland/V4/" +
                "Trees/Prefabs/PF_ENV_Tree_Autumn_A_LOD0.prefab",
                "Assets/_Project/Art/Environment/StarterIsland/V4/" +
                "Trees/Models/ENV_Tree_Autumn_A_LOD0.fbx");
            const string bushPrefabRoot =
                "Assets/_Project/Art/Environment/StarterIsland/V4/" +
                "Bushes/Prefabs/";
            var shrubSmall =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    bushPrefabRoot +
                    "PF_CLU_Bush_Small_A.prefab");
            var shrubMedium =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    bushPrefabRoot +
                    "PF_CLU_Bush_Medium_A.prefab");
            var shrubWide =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    bushPrefabRoot +
                    "PF_CLU_Bush_Wide_A.prefab");
            var shrubAutumn =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    bushPrefabRoot +
                    "PF_CLU_Bush_Autumn_A.prefab");
            var shrubAmber =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    bushPrefabRoot +
                    "PF_CLU_Bush_Amber_A.prefab");
            var random = new System.Random(0x5EED120);
            var occupiedTreePositions = new List<Vector2>();
            var occupiedShrubPositions = new List<Vector2>();
            var treeCount = 0;
            var shrubCount = 0;
            shrubCount += PlaceHeroBushes(
                terrain,
                root.transform,
                shrubSmall,
                shrubMedium,
                shrubWide,
                shrubAutumn,
                shrubAmber,
                occupiedShrubPositions);
            if (commonTree != null)
            {
                treeCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    commonTree,
                    320,
                    32f,
                    0.92f,
                    1.28f,
                    random,
                    "DEC_Tree_Common",
                    CommonTreeClusters,
                    occupiedTreePositions,
                    7.2f);
                treeCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    commonTree,
                    72,
                    30f,
                    0.90f,
                    1.22f,
                    random,
                    "DEC_Tree_Open",
                    OpenWoodlandTreeClusters,
                    occupiedTreePositions,
                    10.5f);
            }

            if (autumnTree != null)
            {
                treeCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    autumnTree,
                    32,
                    30f,
                    0.90f,
                    1.24f,
                    random,
                    "DEC_Tree_Autumn",
                    AutumnTreeClusters,
                    occupiedTreePositions,
                    7.2f);
            }

            if (shrubSmall != null)
            {
                shrubCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    shrubSmall,
                    145,
                    34f,
                    1.05f,
                    1.55f,
                    random,
                    "DEC_Shrub_Small",
                    CommonTreeClusters,
                    occupiedShrubPositions,
                    1.30f);
            }

            if (shrubMedium != null)
            {
                shrubCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    shrubMedium,
                    150,
                    34f,
                    1.00f,
                    1.48f,
                    random,
                    "DEC_Shrub_Medium",
                    CommonTreeClusters,
                    occupiedShrubPositions,
                    1.30f);
            }

            if (shrubWide != null)
            {
                shrubCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    shrubWide,
                    97,
                    32f,
                    0.95f,
                    1.36f,
                    random,
                    "DEC_Shrub_Wide",
                    CommonTreeClusters,
                    occupiedShrubPositions,
                    1.45f);
            }

            if (shrubAutumn != null)
            {
                shrubCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    shrubAutumn,
                    24,
                    34f,
                    1.05f,
                    1.50f,
                    random,
                    "DEC_Shrub_Autumn",
                    AutumnTreeClusters,
                    occupiedShrubPositions,
                    1.30f);
            }

            if (shrubAmber != null)
            {
                shrubCount += ScatterDecoration(
                    terrain,
                    root.transform,
                    shrubAmber,
                    24,
                    34f,
                    1.05f,
                    1.50f,
                    random,
                    "DEC_Shrub_Amber",
                    AutumnTreeClusters,
                    occupiedShrubPositions,
                    1.30f);
            }

            Debug.Log(
                $"STARTER_ISLAND_TERRAIN_FOLIAGE trees={treeCount} " +
                $"shrubs={shrubCount} status=PASS");
        }

        private static int PlaceHeroBushes(
            Terrain terrain,
            Transform parent,
            GameObject small,
            GameObject medium,
            GameObject wide,
            GameObject autumn,
            GameObject amber,
            List<Vector2> occupiedPositions)
        {
            // These are compositional accents, not random filler. They sit
            // beside the routes and water so bushes remain readable at player
            // height, including the orange focal bushes from the reference.
            var placements = new[]
            {
                new HeroBushPlacement(small, -245f, -169f, 1.55f, 18f),
                new HeroBushPlacement(wide, -250f, -176f, 2.30f, 244f),
                new HeroBushPlacement(amber, -253f, -172f, 3.00f, 74f),
                new HeroBushPlacement(
                    autumn,
                    -257f,
                    -168f,
                    2.50f,
                    142f),
                new HeroBushPlacement(amber, -231f, -160f, 2.45f, 74f),
                new HeroBushPlacement(
                    autumn,
                    -235f,
                    -156f,
                    2.10f,
                    142f),
                new HeroBushPlacement(medium, -213f, -147f, 1.65f, 132f),
                new HeroBushPlacement(wide, -177f, -126f, 1.50f, 214f),
                new HeroBushPlacement(small, -143f, -119f, 1.55f, 307f),
                new HeroBushPlacement(medium, -111f, -90f, 1.55f, 41f),
                new HeroBushPlacement(amber, -76f, -74f, 2.20f, 166f),
                new HeroBushPlacement(wide, -50f, -54f, 1.45f, 251f),
                new HeroBushPlacement(wide, -45f, 12f, 2.45f, 210f),
                new HeroBushPlacement(amber, -52f, 18f, 3.10f, 32f),
                new HeroBushPlacement(
                    autumn,
                    -58f,
                    23f,
                    2.55f,
                    138f),
                new HeroBushPlacement(wide, -220f, -101f, 1.95f, 94f),
                new HeroBushPlacement(
                    autumn,
                    -178f,
                    -111f,
                    2.10f,
                    286f),
                new HeroBushPlacement(amber, -231f, -70f, 3.40f, 35f),
                new HeroBushPlacement(
                    autumn,
                    -227f,
                    -75f,
                    2.80f,
                    196f),
                new HeroBushPlacement(wide, -226f, -66f, 2.40f, 309f),
                new HeroBushPlacement(wide, -238f, -60f, 2.70f, 66f),
                new HeroBushPlacement(
                    autumn,
                    -240f,
                    -75f,
                    3.10f,
                    174f),
                new HeroBushPlacement(amber, -235f, -88f, 3.35f, 282f),
                new HeroBushPlacement(small, -98f, 65f, 1.55f, 207f),
                new HeroBushPlacement(autumn, -156f, 88f, 1.75f, 15f),
                new HeroBushPlacement(small, 54f, 13f, 1.55f, 118f),
                new HeroBushPlacement(autumn, 91f, 31f, 2.20f, 223f),
                new HeroBushPlacement(medium, 126f, 46f, 1.65f, 328f),
                new HeroBushPlacement(amber, 159f, 68f, 2.35f, 52f),
                new HeroBushPlacement(wide, 65f, -62f, 1.50f, 179f),
                new HeroBushPlacement(autumn, 102f, -78f, 2.20f, 278f),
                new HeroBushPlacement(medium, 143f, -103f, 1.65f, 33f),
                new HeroBushPlacement(amber, 178f, -116f, 2.35f, 143f)
            };

            var placed = 0;
            for (var index = 0; index < placements.Length; index++)
            {
                var placement = placements[index];
                if (placement.Source == null ||
                    !IsInsideIslandSurface(
                        placement.Position.x,
                        placement.Position.y) ||
                    !IsGroundCoverClearOfWater(placement.Position))
                {
                    continue;
                }

                var normalized = WorldToNormalized(placement.Position);
                if (terrain.terrainData.GetSteepness(
                        normalized.x,
                        normalized.y) > 34f)
                {
                    continue;
                }

                var instance =
                    PrefabUtility.InstantiatePrefab(
                        placement.Source,
                        terrain.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                var y =
                    terrain.SampleHeight(
                        new Vector3(
                            placement.Position.x,
                            0f,
                            placement.Position.y)) +
                    terrain.transform.position.y;
                instance.name = $"DEC_Shrub_Hero_{placed:00}";
                instance.transform.SetParent(parent, true);
                instance.transform.SetPositionAndRotation(
                    new Vector3(
                        placement.Position.x,
                        y,
                        placement.Position.y),
                    Quaternion.Euler(0f, placement.Yaw, 0f));
                instance.transform.localScale =
                    Vector3.one * placement.Scale;
                RemoveDecorationColliders(instance);
                GameObjectUtility.SetStaticEditorFlags(
                    instance,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic);
                occupiedPositions.Add(placement.Position);
                placed++;
            }

            return placed;
        }

        private static int ScatterDecoration(
            Terrain terrain,
            Transform parent,
            GameObject source,
            int requestedCount,
            float maximumSlope,
            float minimumScale,
            float maximumScale,
            System.Random random,
            string namePrefix,
            IReadOnlyList<DecorationCluster> clusters,
            List<Vector2> occupiedPositions,
            float minimumSpacing)
        {
            var accepted = 0;
            var attempts = requestedCount * 45;
            for (var attempt = 0;
                 attempt < attempts && accepted < requestedCount;
                 attempt++)
            {
                var cluster = clusters[random.Next(clusters.Count)];
                var angle =
                    NextFloat(random, 0f, Mathf.PI * 2f);
                var radius =
                    Mathf.Sqrt(NextFloat(random, 0f, 1f));
                var worldPoint = new Vector2(
                    cluster.Center.x +
                    Mathf.Cos(angle) * cluster.Radius.x * radius,
                    cluster.Center.y +
                    Mathf.Sin(angle) * cluster.Radius.y * radius);
                var hasClearance = true;
                for (var index = 0;
                     index < occupiedPositions.Count;
                     index++)
                {
                    if ((occupiedPositions[index] - worldPoint)
                            .sqrMagnitude >=
                        minimumSpacing * minimumSpacing)
                    {
                        continue;
                    }

                    hasClearance = false;
                    break;
                }

                if (!hasClearance)
                {
                    continue;
                }

                var path = ClosestPathSample(worldPoint);
                if (!IsInsideIslandSurface(
                        worldPoint.x,
                        worldPoint.y) ||
                    BoundaryRadius(
                        worldPoint.x,
                        worldPoint.y) > 0.90f ||
                    path.Distance < 7.5f ||
                    EllipseDistance(
                        worldPoint.x,
                        worldPoint.y,
                        -178f,
                        -72f,
                        46f,
                        34f) < 1.10f ||
                    ClosestPolylineSample(
                        worldPoint,
                        StreamRoute).Distance < 8f ||
                    IsGameplayClearance(worldPoint))
                {
                    continue;
                }

                var normalized = WorldToNormalized(worldPoint);
                var slope = terrain.terrainData.GetSteepness(
                    normalized.x,
                    normalized.y);
                if (slope > maximumSlope)
                {
                    continue;
                }

                var y = terrain.SampleHeight(
                            new Vector3(
                                worldPoint.x,
                                0f,
                                worldPoint.y)) +
                        terrain.transform.position.y;
                var instance =
                    PrefabUtility.InstantiatePrefab(
                        source,
                        terrain.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.name = $"{namePrefix}_{accepted:000}";
                instance.transform.SetParent(parent, true);
                instance.transform.SetPositionAndRotation(
                    new Vector3(worldPoint.x, y, worldPoint.y),
                    Quaternion.Euler(
                        0f,
                        NextFloat(random, 0f, 360f),
                        0f));
                var scaleRoll = NextFloat(random, 0f, 1f);
                float scale;
                if (scaleRoll < 0.18f)
                {
                    scale = NextFloat(
                        random,
                        minimumScale * 0.80f,
                        minimumScale * 0.96f);
                }
                else if (scaleRoll > 0.84f)
                {
                    scale = NextFloat(
                        random,
                        maximumScale * 1.08f,
                        maximumScale * 1.34f);
                }
                else
                {
                    scale = NextFloat(
                        random,
                        minimumScale,
                        maximumScale);
                }

                instance.transform.localScale = Vector3.one * scale;
                var isTree = namePrefix.StartsWith(
                        "DEC_Tree_",
                        StringComparison.Ordinal);
                if (!isTree)
                {
                    RemoveDecorationColliders(instance);
                }
                else
                {
                    var tree =
                        instance.GetComponent<FellableTreeIdentity>();
                    if (tree == null)
                    {
                        tree = instance.AddComponent<
                            FellableTreeIdentity>();
                    }

                    tree.Configure(
                        $"{terrain.gameObject.scene.name}.tree." +
                        $"{instance.name}");
                    tree.ResolveAuthoredTrunkCollider();
                }

                GameObjectUtility.SetStaticEditorFlags(
                    instance,
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic);
                var sourcePath = AssetDatabase.GetAssetPath(source);
                var productionTreePrefab =
                    sourcePath.StartsWith(
                        "Assets/_Project/Art/Environment/" +
                        "StarterIsland/V4/Trees/Prefabs/",
                        StringComparison.Ordinal) &&
                    sourcePath.EndsWith(
                        ".prefab",
                        StringComparison.OrdinalIgnoreCase);
                if (!productionTreePrefab &&
                    source.name.IndexOf(
                        "_LOD0",
                        StringComparison.Ordinal) >= 0)
                {
                    ApplyV4TreeMaterials(instance, source.name);
                }

                occupiedPositions.Add(worldPoint);
                accepted++;
            }

            return accepted;
        }

        private static void ApplyV4TreeMaterials(
            GameObject instance,
            string sourceName)
        {
            const string textures =
                "Assets/_Project/Art/Environment/StarterIsland/V4/" +
                "Trees/Textures/";
            var bark = BuildLitMaterial(
                "M_StarterIsland_Tree_Bark",
                textures +
                "T_ENV_Tree_CommonTall_A_Bark_BaseColor.png",
                textures +
                "T_ENV_Tree_CommonTall_A_Bark_Normal.png",
                Html("#B88459"),
                false);
            var commonLeaves = BuildLitMaterial(
                "M_StarterIsland_Tree_CommonLeaves",
                textures +
                "T_ENV_Tree_CommonTall_A_LeafAtlas_BaseColor.png",
                textures +
                "T_ENV_Tree_CommonTall_A_LeafAtlas_Normal.png",
                Html("#A4CF68"),
                true);
            var amberLeaves = BuildLitMaterial(
                "M_StarterIsland_Tree_AutumnAmber",
                textures +
                "T_ENV_Tree_Autumn_A_LeafAtlas_Amber_BaseColor.png",
                textures +
                "T_ENV_Tree_CommonTall_A_LeafAtlas_Normal.png",
                Html("#FFFFFF"),
                true);
            var orangeLeaves = BuildLitMaterial(
                "M_StarterIsland_Tree_AutumnOrange",
                textures +
                "T_ENV_Tree_Autumn_A_LeafAtlas_Orange_BaseColor.png",
                textures +
                "T_ENV_Tree_CommonTall_A_LeafAtlas_Normal.png",
                Html("#FFFFFF"),
                true);
            var redLeaves = BuildLitMaterial(
                "M_StarterIsland_Tree_AutumnRed",
                textures +
                "T_ENV_Tree_Autumn_A_LeafAtlas_Red_BaseColor.png",
                textures +
                "T_ENV_Tree_CommonTall_A_LeafAtlas_Normal.png",
                Html("#FFFFFF"),
                true);

            var autumn =
                sourceName.IndexOf(
                    "Autumn",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var renderer in
                     instance.GetComponentsInChildren<Renderer>(true))
            {
                var current = renderer.sharedMaterials;
                var replacements = new Material[current.Length];
                for (var index = 0; index < current.Length; index++)
                {
                    var materialName =
                        current[index] != null
                            ? current[index].name
                            : string.Empty;
                    if (materialName.IndexOf(
                            "Bark",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        replacements[index] = bark;
                    }
                    else if (materialName.IndexOf(
                                 "Amber",
                                 StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        replacements[index] = amberLeaves;
                    }
                    else if (materialName.IndexOf(
                                 "Orange",
                                 StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        replacements[index] = orangeLeaves;
                    }
                    else if (materialName.IndexOf(
                                 "Red",
                                 StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        replacements[index] = redLeaves;
                    }
                    else
                    {
                        replacements[index] =
                            autumn ? amberLeaves : commonLeaves;
                    }
                }

                renderer.sharedMaterials = replacements;
            }
        }

        private static Material BuildLitMaterial(
            string name,
            string baseTexturePath,
            string normalTexturePath,
            Color fallbackColor,
            bool alphaClip)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP Lit shader is unavailable.");
            }

            var path = MaterialsRoot + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = name
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            var baseTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(baseTexturePath);
            var normalTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexturePath);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", baseTexture);
            }

            SetColor(material, "_BaseColor", fallbackColor);
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normalTexture);
            }

            if (normalTexture != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }

            SetFloat(material, "_Smoothness", alphaClip ? 0.04f : 0.08f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_AlphaClip", alphaClip ? 1f : 0f);
            SetFloat(material, "_Cutoff", 0.38f);
            if (alphaClip)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                SetFloat(material, "_Cull", 0f);
                material.doubleSidedGI = true;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                SetFloat(material, "_Cull", 2f);
                material.doubleSidedGI = false;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ScatterRocks(Terrain terrain)
        {
            var root = new GameObject("RocksRoot");
            var rockPaths = new[]
            {
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderLarge_A.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderMedium_A.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderMedium_B.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderSmall_A.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_BoulderSmall_B.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_ShoreFlat_A.fbx",
                "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
                "Models/ENV_Rock_ShoreFlat_B.fbx"
            };
            var rocks = new List<GameObject>();
            for (var index = 0; index < rockPaths.Length; index++)
            {
                var rock =
                    AssetDatabase.LoadAssetAtPath<GameObject>(rockPaths[index]);
                if (rock != null)
                {
                    rocks.Add(rock);
                }
            }

            if (rocks.Count == 0)
            {
                Debug.LogWarning(
                    "Starter Island rock models are missing; the Terrain " +
                    "review scene keeps an empty RocksRoot.");
                return;
            }

            var material = BuildRockMaterial();
            var random = new System.Random(0xC11FF5);
            var placed = 0;

            for (var attempt = 0;
                 attempt < 18000 && placed < 480;
                 attempt++)
            {
                Vector2 point;
                if (placed < 90)
                {
                    if (placed < 58)
                    {
                        var angle =
                            NextFloat(random, 0f, Mathf.PI * 2f);
                        var radius =
                            NextFloat(random, 1.05f, 1.38f);
                        point = new Vector2(
                            -178f + Mathf.Cos(angle) * 40f * radius,
                            -72f + Mathf.Sin(angle) * 27f * radius);
                    }
                    else
                    {
                        var segment = random.Next(
                            1,
                            StreamRoute.Length - 1);
                        var start = new Vector2(
                            StreamRoute[segment].x,
                            StreamRoute[segment].z);
                        var end = new Vector2(
                            StreamRoute[segment + 1].x,
                            StreamRoute[segment + 1].z);
                        var direction = (end - start).normalized;
                        var perpendicular =
                            new Vector2(-direction.y, direction.x);
                        var side = random.Next(0, 2) == 0 ? -1f : 1f;
                        point =
                            Vector2.Lerp(
                                start,
                                end,
                                NextFloat(random, 0f, 1f)) +
                            perpendicular * side *
                            NextFloat(random, 4.6f, 10.5f);
                    }
                }
                else if (placed < 220)
                {
                    var portal = placed % 2 == 0;
                    var angle = NextFloat(
                        random,
                        0f,
                        Mathf.PI * 2f);
                    point = portal
                        ? new Vector2(
                            220f + Mathf.Cos(angle) *
                            NextFloat(random, 58f, 104f),
                            115f + Mathf.Sin(angle) *
                            NextFloat(random, 44f, 78f))
                        : new Vector2(
                            -205f + Mathf.Cos(angle) *
                            NextFloat(random, 68f, 124f),
                            150f + Mathf.Sin(angle) *
                            NextFloat(random, 52f, 96f));
                }
                else if (placed < 350)
                {
                    var route =
                        Routes[random.Next(Routes.Length)];
                    var segment =
                        random.Next(route.Points.Length - 1);
                    var start = new Vector2(
                        route.Points[segment].x,
                        route.Points[segment].z);
                    var end = new Vector2(
                        route.Points[segment + 1].x,
                        route.Points[segment + 1].z);
                    var direction = (end - start).normalized;
                    var perpendicular =
                        new Vector2(-direction.y, direction.x);
                    var side = random.Next(0, 2) == 0 ? -1f : 1f;
                    point =
                        Vector2.Lerp(
                            start,
                            end,
                            NextFloat(random, 0f, 1f)) +
                        perpendicular * side *
                        NextFloat(random, 7f, 15f);
                }
                else
                {
                    point = new Vector2(
                        NextFloat(random, -300f, 300f),
                        NextFloat(random, -220f, 220f));
                }

                if (!IsInsideIslandSurface(point.x, point.y) ||
                    BoundaryRadius(point.x, point.y) > 0.95f ||
                    ClosestPathSample(point).Distance < 5.2f ||
                    IsGameplayClearance(point))
                {
                    continue;
                }

                var normalized = WorldToNormalized(point);
                var slope = terrain.terrainData.GetSteepness(
                    normalized.x,
                    normalized.y);
                if (slope > 48f)
                {
                    continue;
                }

                var y = terrain.SampleHeight(
                            new Vector3(point.x, 0f, point.y)) +
                        terrain.transform.position.y;
                if (y < 3.1f)
                {
                    continue;
                }

                var source = rocks[random.Next(rocks.Count)];
                var instance =
                    PrefabUtility.InstantiatePrefab(
                        source,
                        terrain.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.name = $"DEC_Rock_{placed:000}";
                instance.transform.SetParent(root.transform, true);
                instance.transform.SetPositionAndRotation(
                    new Vector3(point.x, y - 0.15f, point.y),
                    Quaternion.Euler(
                        NextFloat(random, -8f, 8f),
                        NextFloat(random, 0f, 360f),
                        NextFloat(random, -8f, 8f)));
                var shore =
                    source.name.IndexOf(
                        "ShoreFlat",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                var waterAnchor =
                    placed < 90 && placed % 4 == 0;
                var hillAnchor =
                    placed >= 90 &&
                    placed < 220 &&
                    placed % 5 == 0;
                var pathAnchor =
                    placed >= 220 &&
                    placed < 350 &&
                    placed % 11 == 0;
                var landmark =
                    waterAnchor ||
                    hillAnchor ||
                    pathAnchor ||
                    (placed >= 350 && placed % 9 == 0);
                var scale = landmark
                    ? NextFloat(random, 2.50f, 4.60f)
                    : placed < 90
                        ? shore
                            ? NextFloat(random, 0.90f, 2.25f)
                            : NextFloat(random, 1.00f, 2.80f)
                        : shore
                            ? NextFloat(random, 0.75f, 1.70f)
                            : NextFloat(random, 0.68f, 2.30f);
                instance.transform.localScale =
                    new Vector3(
                        scale,
                        scale * NextFloat(random, 0.82f, 1.12f),
                        scale);
                RemoveDecorationColliders(instance);
                foreach (var renderer in
                         instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials =
                        new Material[renderer.sharedMaterials.Length];
                    for (var slot = 0; slot < materials.Length; slot++)
                    {
                        materials[slot] = material;
                    }

                    renderer.sharedMaterials = materials;
                }

                placed++;
            }

            var pathPebbles = PlacePathPebbles(
                terrain,
                root.transform,
                rocks,
                BuildPathPebbleMaterial());
            Debug.Log(
                $"STARTER_ISLAND_TERRAIN_ROCKS count={placed} " +
                $"pathPebbles={pathPebbles} " +
                "wallAuthority=Terrain decorationsCollision=0 status=PASS");
        }

        private static int PlacePathPebbles(
            Terrain terrain,
            Transform parent,
            IReadOnlyList<GameObject> rocks,
            Material material)
        {
            var pebbleSources = new List<GameObject>();
            for (var index = 0; index < rocks.Count; index++)
            {
                var source = rocks[index];
                if (source.name.IndexOf(
                        "Small",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    source.name.IndexOf(
                        "ShoreFlat",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    pebbleSources.Add(source);
                }
            }

            if (pebbleSources.Count == 0)
            {
                return 0;
            }

            var random = new System.Random(0x51A6D);
            var placed = 0;
            for (var routeIndex = 0;
                 routeIndex < Routes.Length;
                 routeIndex++)
            {
                var route = Routes[routeIndex];
                const int pebbleCountPerRoute = 52;
                for (var pebbleIndex = 0;
                     pebbleIndex < pebbleCountPerRoute;
                     pebbleIndex++)
                {
                    var routeProgress =
                        (pebbleIndex +
                         NextFloat(random, 0.18f, 0.82f)) /
                        pebbleCountPerRoute;
                    var scaledProgress =
                        routeProgress * (route.Points.Length - 1);
                    var segment = Mathf.Clamp(
                        Mathf.FloorToInt(scaledProgress),
                        0,
                        route.Points.Length - 2);
                    var segmentProgress =
                        scaledProgress - segment;
                    var start = new Vector2(
                        route.Points[segment].x,
                        route.Points[segment].z);
                    var end = new Vector2(
                        route.Points[segment + 1].x,
                        route.Points[segment + 1].z);
                    var direction = (end - start).normalized;
                    var perpendicular =
                        new Vector2(-direction.y, direction.x);
                    var side = random.Next(0, 2) == 0 ? -1f : 1f;
                    var point =
                        Vector2.Lerp(start, end, segmentProgress) +
                        perpendicular *
                        side *
                        NextFloat(
                            random,
                            0.35f,
                            route.HalfWidth * 0.82f);
                    if (!IsInsideIslandSurface(point.x, point.y))
                    {
                        continue;
                    }

                    var normalized = WorldToNormalized(point);
                    if (terrain.terrainData.GetSteepness(
                            normalized.x,
                            normalized.y) > 30f)
                    {
                        continue;
                    }

                    var source =
                        pebbleSources[random.Next(pebbleSources.Count)];
                    var instance =
                        PrefabUtility.InstantiatePrefab(
                            source,
                            terrain.gameObject.scene) as GameObject;
                    if (instance == null)
                    {
                        continue;
                    }

                    var y =
                        terrain.SampleHeight(
                            new Vector3(point.x, 0f, point.y)) +
                        terrain.transform.position.y;
                    instance.name =
                        $"DEC_PathPebble_{placed:000}";
                    instance.transform.SetParent(parent, true);
                    instance.transform.SetPositionAndRotation(
                        new Vector3(point.x, y - 0.025f, point.y),
                        Quaternion.Euler(
                            NextFloat(random, -5f, 5f),
                            NextFloat(random, 0f, 360f),
                            NextFloat(random, -5f, 5f)));
                    var scale = NextFloat(random, 0.18f, 0.48f);
                    instance.transform.localScale =
                        new Vector3(
                            scale,
                            scale * NextFloat(random, 0.42f, 0.68f),
                            scale);
                    RemoveDecorationColliders(instance);
                    foreach (var renderer in
                             instance.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials =
                            new Material[renderer.sharedMaterials.Length];
                        for (var slot = 0;
                             slot < materials.Length;
                             slot++)
                        {
                            materials[slot] = material;
                        }

                        renderer.sharedMaterials = materials;
                    }

                    GameObjectUtility.SetStaticEditorFlags(
                        instance,
                        StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.OccludeeStatic);
                    placed++;
                }
            }

            return placed;
        }

        private static Material BuildRockMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Stylized Surface");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island stylized surface shader is unavailable.");
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_DetailRock.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_DetailRock"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            // Albedo quasi neutro: il calore resta compito del sole e della
            // gradazione di scena, non di una roccia beige.  Rispetto alla
            // prima taratura i valori sono però alzati: con base 0.44 e
            // ShadowFloor 0.22 la faccia in ombra scendeva a ~0.10 di
            // luminanza e in una scena verde e calda le rocce leggevano nere.
            // Il pavimento d'ombra è la leva vera, la base da sola non basta.
            // Seconda passata di schiarimento: a base #8E918F e ShadowFloor
            // 0.45 le rocce restavano più scure dell'erba illuminata e
            // leggevano come massi di basalto in un prato chiaro. Ora la base
            // sta sopra la luminanza dell'erba e il pavimento d'ombra tiene la
            // faccia in ombra dentro la gamma della pietra chiara.
            SetColor(material, "_BaseColor", Html("#9FA39E"));
            SetColor(material, "_SecondaryColor", Html("#B5B8B1"));
            SetColor(material, "_WetColor", Html("#747D78"));
            SetFloat(material, "_VertexBlend", 0f);
            SetFloat(material, "_AmbientStrength", 0.92f);
            SetFloat(material, "_ShadowFloor", 0.50f);
            SetFloat(material, "_ColorVariation", 0.025f);
            SetFloat(material, "_RockDetail", 1f);
            SetColor(material, "_RockTopColor", Html("#D7CABB"));
            SetColor(material, "_RockUnderColor", Html("#727B74"));
            SetFloat(material, "_RockTopStrength", 0.68f);
            SetFloat(material, "_RockUnderStrength", 0.34f);
            SetFloat(material, "_RockMacroScale", 0.48f);
            SetFloat(material, "_RockMacroStrength", 0.105f);
            SetFloat(material, "_RockGrainScale", 4.6f);
            SetFloat(material, "_RockGrainStrength", 0.045f);
            SetFloat(material, "_RockContactBlend", 0.74f);
            SetFloat(material, "_RockContactHeight", 0.24f);
            SetFloat(material, "_RockContactFeather", 0.20f);
            SetFloat(material, "_RockContactNoise", 0.14f);
            SetColor(material, "_RockContactGrassColor", Html("#496A35"));
            SetColor(
                material,
                "_RockContactDeepGrassColor",
                Html("#314C2B"));
            SetColor(material, "_RockContactDirtColor", Html("#B78F60"));
            SetColor(material, "_RockContactCliffColor", Html("#87503F"));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildPathPebbleMaterial()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Stylized Surface");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island stylized surface shader is unavailable.");
            }

            var path =
                MaterialsRoot + "/M_StarterIsland_PathPebble.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_StarterIsland_PathPebble"
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            // Path inclusions remain neutral grey; their lighter value and
            // raised shadow floor keep them readable as stones rather than
            // black holes against the sunlit peach sand.
            SetColor(material, "_BaseColor", Html("#858A89"));
            SetColor(material, "_SecondaryColor", Html("#9A9E9C"));
            SetColor(material, "_WetColor", Html("#626766"));
            SetFloat(material, "_VertexBlend", 0f);
            SetFloat(material, "_AmbientStrength", 0.82f);
            SetFloat(material, "_ShadowFloor", 0.38f);
            SetFloat(material, "_ColorVariation", 0.02f);
            SetFloat(material, "_RockDetail", 0.72f);
            SetColor(material, "_RockTopColor", Html("#C5BBAE"));
            SetColor(material, "_RockUnderColor", Html("#666F6A"));
            SetFloat(material, "_RockTopStrength", 0.46f);
            SetFloat(material, "_RockUnderStrength", 0.28f);
            SetFloat(material, "_RockMacroScale", 0.72f);
            SetFloat(material, "_RockMacroStrength", 0.075f);
            SetFloat(material, "_RockGrainScale", 5.8f);
            SetFloat(material, "_RockGrainStrength", 0.035f);
            SetFloat(material, "_RockContactBlend", 0.62f);
            SetFloat(material, "_RockContactHeight", 0.28f);
            SetFloat(material, "_RockContactFeather", 0.24f);
            SetFloat(material, "_RockContactNoise", 0.12f);
            SetColor(material, "_RockContactGrassColor", Html("#496A35"));
            SetColor(
                material,
                "_RockContactDeepGrassColor",
                Html("#314C2B"));
            SetColor(material, "_RockContactDirtColor", Html("#B78F60"));
            SetColor(material, "_RockContactCliffColor", Html("#87503F"));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateReviewLandmarks(Terrain terrain)
        {
            var root = new GameObject("LandmarksRoot");
            var portal = AssetDatabase.LoadAssetAtPath<GameObject>(
                StarterIslandPortalSetup.PrefabPath);
            if (portal != null)
            {
                var instance = PrefabUtility.InstantiatePrefab(
                    portal,
                    terrain.gameObject.scene) as GameObject;
                if (instance != null)
                {
                    instance.name = "ENV_AncientStonePortal";
                    instance.transform.SetParent(root.transform, true);
                    instance.transform.rotation =
                        Quaternion.LookRotation(
                            new Vector3(-220f, 0f, -115f),
                            Vector3.up);
                    ScaleAndPlaceVisual(
                        terrain,
                        instance,
                        new Vector2(220f, 115f),
                        22f);
                }
            }

            var starterProps = new[]
            {
                new ReviewProp(
                    "Assets/_Project/Art/ManualEra/Prefabs/" +
                    "PF_Workbench.prefab",
                    new Vector2(-232f, -166f),
                    28f),
                new ReviewProp(
                    "Assets/_Project/Art/ManualEra/Prefabs/" +
                    "PF_CrudeFurnace.prefab",
                    new Vector2(-225f, -172f),
                    -18f),
                new ReviewProp(
                    "Assets/_Project/Art/ManualEra/Prefabs/" +
                    "PF_Crate.prefab",
                    new Vector2(-239f, -173f),
                    12f)
            };
            for (var index = 0;
                 index < starterProps.Length;
                 index++)
            {
                var definition = starterProps[index];
                var source =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        definition.AssetPath);
                if (source == null)
                {
                    continue;
                }

                var instance = PrefabUtility.InstantiatePrefab(
                    source,
                    terrain.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.name = $"ENV_StarterProp_{index:00}_{source.name}";
                instance.transform.SetParent(root.transform, true);
                var y = terrain.SampleHeight(
                            new Vector3(
                                definition.Position.x,
                                0f,
                                definition.Position.y)) +
                        terrain.transform.position.y;
                instance.transform.SetPositionAndRotation(
                    new Vector3(
                        definition.Position.x,
                        y,
                        definition.Position.y),
                    Quaternion.Euler(0f, definition.Yaw, 0f));
            }
        }

        private static void ScaleAndPlaceVisual(
            Terrain terrain,
            GameObject instance,
            Vector2 position,
            float targetHeight)
        {
            var renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            if (bounds.size.y > 0.001f)
            {
                instance.transform.localScale *=
                    targetHeight / bounds.size.y;
            }

            instance.transform.position =
                new Vector3(position.x, 0f, position.y);
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            var terrainY =
                terrain.SampleHeight(
                    new Vector3(position.x, 0f, position.y)) +
                terrain.transform.position.y;
            instance.transform.position +=
                Vector3.up * (terrainY - bounds.min.y);
        }

        private static void CreatePlayableAirshipRig(
            GameObject island,
            Terrain terrain,
            Transform reviewRoot)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            var dock =
                RequireTransform(island.transform, "REF_AirshipDock");
            var airshipPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(AirshipPrefabPath);
            if (airshipPrefab == null)
            {
                throw new FileNotFoundException(
                    $"The playable airship prefab is missing: " +
                    AirshipPrefabPath);
            }

            var airship = PrefabUtility.InstantiatePrefab(
                airshipPrefab,
                terrain.gameObject.scene) as GameObject;
            if (airship == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the playable airship prefab.");
            }

            airship.name = "PF_Airship";
            var rampLocalPosition = new Vector3(
                AirshipSimulationConstants.RampTipLocalXMillimetres / 1000f,
                AirshipSimulationConstants.RampTipLocalYMillimetres / 1000f,
                AirshipSimulationConstants.RampTipLocalZMillimetres / 1000f);
            var rampWorldXZ = dock.position + rampLocalPosition;
            var rampSurfaceY =
                terrain.SampleHeight(
                    new Vector3(rampWorldXZ.x, 0f, rampWorldXZ.z)) +
                terrain.transform.position.y;
            airship.transform.SetPositionAndRotation(
                new Vector3(
                    dock.position.x,
                    rampSurfaceY - rampLocalPosition.y + 0.05f,
                    dock.position.z),
                Quaternion.identity);
            if (reviewRoot != null)
            {
                airship.transform.SetParent(reviewRoot, true);
            }

            var bridge = airship.GetComponent<AirshipSimulationBridge>();
            var frame = airship.GetComponent<AirshipFrame>();
            var station =
                airship.GetComponentInChildren<AirshipPilotStation>(true);
            if (bridge == null ||
                bridge.Motor == null ||
                bridge.LandingProbe == null ||
                frame == null ||
                station == null)
            {
                throw new InvalidOperationException(
                    "The playable airship prefab has an incomplete AIR rig.");
            }

            var player = new GameObject(PlayerName);
            player.transform.SetPositionAndRotation(
                airship.transform.TransformPoint(
                    new Vector3(
                        AirshipSimulationConstants
                            .PilotExitBodyRootPosition.X / 1000f,
                        AirshipSimulationConstants
                            .PilotExitBodyRootPosition.Y / 1000f,
                        AirshipSimulationConstants
                            .PilotExitBodyRootPosition.Z / 1000f)),
                airship.transform.rotation);
            if (reviewRoot != null)
            {
                player.transform.SetParent(reviewRoot, true);
            }

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.30f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 52f;
            controller.stepOffset = 0.32f;
            controller.skinWidth = 0.08f;
            controller.minMoveDistance = 0.001f;

            var passenger = player.AddComponent<AirshipRelativePassenger>();
            var input = player.AddComponent<AirshipInputAdapter>();
            var characterMotor =
                player.AddComponent<FirstPersonCharacterMotor>();

            var yaw = new GameObject("AIR_ViewYaw");
            yaw.transform.SetParent(player.transform, false);
            yaw.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            var pitch = new GameObject("AIR_ViewPitch");
            pitch.transform.SetParent(yaw.transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pitch.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1800f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Html("#9EDFF0");
            camera.depthTextureMode |= DepthTextureMode.Depth;
            ConfigureCameraRendering(camera);
            cameraObject.AddComponent<AudioListener>();

            var mouseLook = player.AddComponent<FirstPersonMouseLook>();
            mouseLook.Configure(yaw.transform, pitch.transform);
            passenger.Configure(player.transform, controller, bridge);
            characterMotor.Configure(
                controller,
                yaw.transform,
                passenger);
            bridge.Configure(
                bridge.Motor,
                frame,
                passenger,
                bridge.LandingProbe,
                automaticAdvance: true);
            station.Configure(
                frame,
                bridge,
                passenger,
                RequireTransform(airship.transform, "REF_PilotControls"),
                1.50f);
            input.Configure(bridge, station);

            var inventoryHudPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    InventoryHudAssetSetup.PrefabPath);
            if (inventoryHudPrefab == null)
            {
                throw new FileNotFoundException(
                    "The generated player inventory HUD prefab is missing: " +
                    InventoryHudAssetSetup.PrefabPath);
            }

            var inventoryHud = PrefabUtility.InstantiatePrefab(
                inventoryHudPrefab,
                terrain.gameObject.scene) as GameObject;
            if (inventoryHud == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the player inventory HUD.");
            }

            inventoryHud.name = "PF_InventoryHUD";
            if (reviewRoot != null)
            {
                inventoryHud.transform.SetParent(reviewRoot, false);
            }

            var inventoryHudController =
                inventoryHud.GetComponent<InventoryHudController>();
            if (inventoryHudController == null)
            {
                throw new InvalidOperationException(
                    "The player inventory HUD prefab has no controller.");
            }

            inventoryHudController.ConfigureGameplayInput(
                input,
                mouseLook,
                useReviewContents: true);

            var scenarioObject = new GameObject("AIR_StarterIslandReady");
            if (reviewRoot != null)
            {
                scenarioObject.transform.SetParent(reviewRoot, false);
            }

            var scenario =
                scenarioObject.AddComponent<AirshipTechnicalScenario>();
            scenario.Configure(
                bridge,
                passenger,
                station,
                input,
                Array.Empty<AirshipLandingSurfaceIdentity>(),
                Array.Empty<AirshipObstacleIdentity>(),
                automaticInitialization: true);

            CanonicalFactorySceneInstaller.Install(
                terrain.gameObject.scene,
                player,
                camera,
                mouseLook,
                input,
                inventoryHudController,
                terrain,
                reviewRoot);
        }

        private static void ConfigureCameraRendering(Camera camera)
        {
            var cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing =
                AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.stopNaN = true;
            cameraData.dithering = false;
            cameraData.volumeLayerMask = ~0;
        }

        private static bool IsGameplayClearance(Vector2 point)
        {
            for (var index = 0; index < Markers.Length; index++)
            {
                var marker = Markers[index];
                float clearance;
                if (marker.Name.IndexOf(
                        "Factory",
                        StringComparison.Ordinal) >= 0)
                {
                    clearance = 22f;
                }
                else if (marker.Name.IndexOf(
                             "Portal",
                             StringComparison.Ordinal) >= 0)
                {
                    clearance = 42f;
                }
                else
                {
                    clearance = 10f;
                }
                if (Vector2.Distance(
                        point,
                        new Vector2(marker.X, marker.Z)) < clearance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAutumnRegion(Vector2 point)
        {
            return point.x > 72f && point.y > 32f;
        }

        private static void RemoveDecorationColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[index]);
            }
        }

        private static GameObject LoadFirst<TIgnored>(
            params string[] paths)
        {
            for (var index = 0; index < paths.Length; index++)
            {
                var asset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
                if (asset != null)
                {
                    return asset;
                }
            }

            return null;
        }

        private static float SampleLocalHeight(
            TerrainData data,
            float worldX,
            float worldZ)
        {
            var normalized = WorldToNormalized(
                new Vector2(worldX, worldZ));
            return data.GetInterpolatedHeight(
                normalized.x,
                normalized.y);
        }

        private static Vector3 SampleUnderbodyRimPoint(
            TerrainData data,
            float angle)
        {
            const float targetBoundaryRadius = 0.991f;
            var direction = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle));
            var minimumDistance = 0f;
            var maximumDistance = 390f;

            for (var iteration = 0; iteration < 28; iteration++)
            {
                var distance =
                    (minimumDistance + maximumDistance) * 0.5f;
                var point = direction * distance;
                if (BoundaryRadius(point.x, point.y) <
                    targetBoundaryRadius)
                {
                    minimumDistance = distance;
                }
                else
                {
                    maximumDistance = distance;
                }
            }

            var rim = direction *
                      ((minimumDistance + maximumDistance) * 0.5f);
            return new Vector3(
                rim.x,
                SampleLocalHeight(data, rim.x, rim.y) - 0.04f,
                rim.y);
        }

        private static Vector2 WorldToNormalized(Vector2 point)
        {
            return new Vector2(
                Mathf.Clamp01(
                    (point.x + TerrainWidth * 0.5f) /
                    TerrainWidth),
                Mathf.Clamp01(
                    (point.y + TerrainLength * 0.5f) /
                    TerrainLength));
        }

        private static PathSample ClosestPathSample(Vector2 point)
        {
            var bestDistance = float.PositiveInfinity;
            var bestProgress = 0f;
            var bestHeight = 0f;
            var bestHalfWidth = 4.2f;
            for (var index = 0; index < Routes.Length; index++)
            {
                var sample = ClosestRouteSample(point, Routes[index]);
                if (sample.Distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = sample.Distance;
                bestProgress = sample.Progress;
                bestHeight = sample.Height;
                bestHalfWidth = sample.HalfWidth;
            }

            return new PathSample(
                bestDistance,
                bestProgress,
                bestHeight,
                bestHalfWidth);
        }

        private static bool IsInsideRampCorridor(float worldX, float worldZ)
        {
            var point = new Vector2(worldX, worldZ);
            for (var index = 0; index < Routes.Length; index++)
            {
                var route = Routes[index];
                if (ClosestRouteSample(point, route).Distance <=
                    route.HalfWidth + 12f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNearWater(float worldX, float worldZ)
        {
            for (var index = 0; index < WaterBasins.Length; index++)
            {
                var basin = WaterBasins[index];
                if (EllipseDistance(
                        worldX,
                        worldZ,
                        basin.CenterX,
                        basin.CenterZ,
                        basin.RadiusX,
                        basin.RadiusZ) <= 1.45f)
                {
                    return true;
                }
            }

            return ClosestPolylineSample(
                new Vector2(worldX, worldZ),
                StreamRoute).Distance <= 18f;
        }

        private static float RouteArcLength(RouteDefinition route)
        {
            var total = 0f;
            for (var index = 0; index < route.Points.Length - 1; index++)
            {
                total += Vector2.Distance(
                    new Vector2(
                        route.Points[index].x,
                        route.Points[index].z),
                    new Vector2(
                        route.Points[index + 1].x,
                        route.Points[index + 1].z));
            }

            return total;
        }

        /// <summary>
        /// Posizione orizzontale sulla rotta a una data lunghezza d'arco.
        /// Serve alla rampa, che media la quota lungo il sentiero.
        /// </summary>
        private static Vector2 RoutePositionAtArc(
            RouteDefinition route,
            float arc)
        {
            var points = route.Points;
            var travelled = 0f;
            for (var index = 0; index < points.Length - 1; index++)
            {
                var start = new Vector2(points[index].x, points[index].z);
                var end =
                    new Vector2(points[index + 1].x, points[index + 1].z);
                var length = Vector2.Distance(start, end);
                if (length <= 0.0001f)
                {
                    continue;
                }

                if (travelled + length >= arc)
                {
                    return Vector2.Lerp(
                        start,
                        end,
                        (arc - travelled) / length);
                }

                travelled += length;
            }

            var last = points[points.Length - 1];
            return new Vector2(last.x, last.z);
        }

        private static PathSample ClosestRouteSample(
            Vector2 point,
            RouteDefinition route)
        {
            var sample = ClosestPolylineSample(point, route.Points);
            return new PathSample(
                sample.Distance,
                sample.Progress,
                sample.Height,
                route.HalfWidth);
        }

        private static PathSample ClosestPolylineSample(
            Vector2 point,
            IReadOnlyList<Vector3> points)
        {
            var bestDistance = float.PositiveInfinity;
            var bestProgress = 0f;
            var bestHeight = 0f;
            var totalLength = 0f;
            for (var index = 0; index < points.Count - 1; index++)
            {
                var start =
                    new Vector2(points[index].x, points[index].z);
                var end =
                    new Vector2(points[index + 1].x, points[index + 1].z);
                totalLength += Vector2.Distance(start, end);
            }

            var traversed = 0f;
            for (var index = 0; index < points.Count - 1; index++)
            {
                var start =
                    new Vector2(points[index].x, points[index].z);
                var end =
                    new Vector2(points[index + 1].x, points[index + 1].z);
                var segment = end - start;
                var segmentLength = segment.magnitude;
                var lengthSquared = segment.sqrMagnitude;
                var t = lengthSquared <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - start, segment) /
                        lengthSquared);
                var distance =
                    Vector2.Distance(point, start + segment * t);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestProgress =
                        totalLength <= 0.0001f
                            ? 0f
                            : (traversed + segmentLength * t) /
                              totalLength;
                    bestHeight = Mathf.Lerp(
                        points[index].y,
                        points[index + 1].y,
                        t);
                }

                traversed += segmentLength;
            }

            return new PathSample(
                bestDistance,
                bestProgress,
                bestHeight);
        }

        private static Vector3[] BuildBezierRoute(
            Vector3 start,
            Vector3 controlA,
            Vector3 controlB,
            Vector3 end,
            int segmentCount)
        {
            if (segmentCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segmentCount));
            }

            var points = new Vector3[segmentCount + 1];
            for (var index = 0; index <= segmentCount; index++)
            {
                var t = index / (float)segmentCount;
                var inverse = 1f - t;
                points[index] =
                    inverse * inverse * inverse * start +
                    3f * inverse * inverse * t * controlA +
                    3f * inverse * t * t * controlB +
                    t * t * t * end;
            }

            return points;
        }

        private static Vector3[] BuildCombinedWaterRoute()
        {
            var points = new List<Vector3>();
            AddWaterRoute(points, CrownCreekRoute);
            AddWaterRoute(points, CrownFallRoute);
            AddWaterRoute(points, ThirdCreekRoute);
            AddWaterRoute(points, ThirdFallRoute);
            AddWaterRoute(points, MiddleCreekRoute);
            AddWaterRoute(points, MiddleFallRoute);
            AddWaterRoute(points, ShelfCreekRoute);
            AddWaterRoute(points, MainFallRoute);
            AddWaterRoute(points, LowerCreekRoute);
            return points.ToArray();
        }

        private static void AddWaterRoute(
            List<Vector3> destination,
            IReadOnlyList<Vector3> source)
        {
            for (var index = 0; index < source.Count; index++)
            {
                if (destination.Count > 0 &&
                    index == 0 &&
                    Vector3.SqrMagnitude(
                        destination[destination.Count - 1] -
                        source[index]) < 0.0001f)
                {
                    continue;
                }

                destination.Add(source[index]);
            }
        }

        private static float ApplyPondAndStream(
            float worldX,
            float worldZ,
            float height)
        {
            for (var index = 0; index < WaterBasins.Length; index++)
            {
                var basin = WaterBasins[index];
                height = ApplyWaterBasin(
                    worldX,
                    worldZ,
                    height,
                    basin.CenterX,
                    basin.CenterZ,
                    basin.RadiusX,
                    basin.RadiusZ,
                    basin.SurfaceHeight,
                    basin.Depth,
                    basin.ShoreWidth,
                    basin.ShoreRise);
            }

            // I tratti piatti hanno una spalla larga, perché scorrono sul
            // ripiano e la valle deve leggersi. I salti hanno una spalla
            // stretta: il loro fianco è la parete del ripiano, e allargarla
            // scaverebbe una conca nel gradino, che è ciò che prima rendeva le
            // cascate una piega informe invece di un salto.
            height = ApplyWaterChannel(
                worldX, worldZ, height, CrownCreekRoute, 2.55f, 11.0f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, CrownFallRoute, 2.65f, 4.5f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, ThirdCreekRoute, 2.55f, 10.0f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, ThirdFallRoute, 2.60f, 4.5f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, MiddleCreekRoute, 2.60f, 10.0f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, MiddleFallRoute, 2.70f, 4.5f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, ShelfCreekRoute, 2.70f, 11.0f);
            height = ApplyWaterChannel(
                worldX, worldZ, height, MainFallRoute, 3.00f, 5.0f);
            return ApplyWaterChannel(
                worldX, worldZ, height, LowerCreekRoute, 3.20f, 16.0f);
        }

        private static float ApplyWaterBasin(
            float worldX,
            float worldZ,
            float height,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ,
            float surfaceHeight,
            float depth,
            float shoreWidth = 0f,
            float shoreRise = 0f)
        {
            var angle =
                Mathf.Atan2(
                    worldZ - centerZ,
                    worldX - centerX);
            var organicRadius =
                1f +
                0.045f * Mathf.Sin(angle * 3f + 0.4f) +
                0.025f * Mathf.Sin(angle * 7f - 0.8f);
            var distance =
                EllipseDistance(
                    worldX,
                    worldZ,
                    centerX,
                    centerZ,
                    radiusX,
                    radiusZ) / organicRadius;
            // Oltre il pelo dell'acqua la quota risaliva alla quota del
            // terreno macro fra distance 1.10 e 1.24, cioè in 5,6 metri sul
            // lago grande: tutta l'altezza della sponda in cinque metri, che
            // in gioco legge come una parete di terra verticale senza riva.
            // shoreWidth allunga la risalita e shoreRise le dà una pendenza
            // dolce; con i valori di default il risultato è identico a prima,
            // così le tre pozze piccole restano invariate.
            var shoreEnd = 1.24f + shoreWidth;
            var outer =
                1f - SmootherStep(0.84f, shoreEnd, distance);
            var inner =
                1f - SmootherStep(0.16f, 0.88f, distance);
            var beach =
                SmootherStep(0.94f, shoreEnd, distance) * shoreRise;
            var target =
                surfaceHeight - 0.26f - inner * depth + beach;
            // The complete visible footprint must own a bed. Restricting the
            // operation to erosion left elevated pools hanging in mid-air
            // whenever the macro terrain happened to be lower than their
            // authored water level.
            var footprintSupport =
                1f - SmootherStep(1.10f, 1.24f, distance);
            var basinBlend = Mathf.Max(
                footprintSupport,
                Mathf.Pow(outer, 1.22f) * 0.97f);
            return Mathf.Lerp(
                height,
                target,
                basinBlend);
        }

        private static float ApplyWaterChannel(
            float worldX,
            float worldZ,
            float height,
            IReadOnlyList<Vector3> route,
            float halfWidth,
            float shoulder)
        {
            var sample =
                ClosestPolylineSample(
                    new Vector2(worldX, worldZ),
                    route);
            var valley =
                1f - SmootherStep(
                    halfWidth,
                    shoulder,
                    sample.Distance);
            var bankProgress =
                Mathf.Clamp01(sample.Distance / shoulder);
            var bankTarget =
                sample.Height +
                Mathf.Lerp(
                    -0.34f,
                    5.2f,
                    SmootherStep(0f, 1f, bankProgress));
            height = Mathf.Lerp(
                height,
                Mathf.Min(height, bankTarget),
                valley * 0.78f);

            var bed =
                1f - SmootherStep(
                    halfWidth * 0.72f,
                    halfWidth * 1.18f,
                    sample.Distance);
            var bedCore =
                1f - SmootherStep(
                    halfWidth * 0.42f,
                    halfWidth * 0.78f,
                    sample.Distance);
            var bedTarget =
                sample.Height - 0.54f;
            var bedBlend =
                Mathf.Max(
                    bedCore,
                    Mathf.Pow(bed, 1.18f) * 0.90f);
            height = Mathf.Lerp(
                height,
                bedTarget,
                bedBlend);

            // The ribbon mesh reaches halfWidth. Guarantee a shallow bank
            // directly below that complete footprint, then blend back into
            // the naturally eroded valley outside the water.
            var footprint =
                1f - SmootherStep(
                    halfWidth * 1.02f,
                    halfWidth * 1.34f,
                    sample.Distance);
            var core =
                1f - SmootherStep(
                    0f,
                    halfWidth,
                    sample.Distance);
            var supportTarget =
                sample.Height - Mathf.Lerp(0.20f, 0.56f, core);
            return Mathf.Lerp(
                height,
                supportTarget,
                footprint);
        }

        private static float EvaluateRockHint(
            float worldX,
            float worldZ)
        {
            var portal =
                EllipseDistance(
                    worldX, worldZ, 220f, 115f, 104f, 78f);
            var portalRing =
                SmoothStep(0.74f, 1.02f, portal) *
                (1f - SmoothStep(1.02f, 1.25f, portal));
            var spring =
                EllipseDistance(
                    worldX, worldZ, -205f, 150f, 128f, 100f);
            var springRing =
                SmoothStep(0.78f, 1.02f, spring) *
                (1f - SmoothStep(1.02f, 1.23f, spring));
            return Mathf.Max(portalRing, springRing);
        }

        internal static bool IsGroundCoverClearOfWater(
            Vector2 point)
        {
            if (EllipseDistance(
                    point.x,
                    point.y,
                    -178f,
                    -72f,
                    46f,
                    34f) < 1.10f ||
                EllipseDistance(
                    point.x,
                    point.y,
                    -205f,
                    145f,
                    13f,
                    10f) < 1f ||
                EllipseDistance(
                    point.x,
                    point.y,
                    -196f,
                    100f,
                    13.5f,
                    10f) < 1f ||
                EllipseDistance(
                    point.x,
                    point.y,
                    -180f,
                    34f,
                    14.5f,
                    11.5f) < 1f)
            {
                return false;
            }

            return ClosestPolylineSample(point, StreamRoute).Distance >= 8f;
        }

        private static bool IsInsideIslandSurface(
            float worldX,
            float worldZ)
        {
            return BoundaryRadius(worldX, worldZ) <= 0.992f;
        }

        private static float BoundaryRadius(
            float worldX,
            float worldZ)
        {
            const float exponent = 2.65f;
            var superellipse = Mathf.Pow(
                Mathf.Pow(Mathf.Abs(worldX) / 318f, exponent) +
                Mathf.Pow(Mathf.Abs(worldZ) / 238f, exponent),
                1f / exponent);
            var angle = Mathf.Atan2(worldZ, worldX);
            var organicScale =
                0.965f +
                0.012f * Mathf.Sin(angle * 3f + 0.65f) +
                0.006f * Mathf.Sin(angle * 7f - 1.10f) +
                0.003f * Mathf.Sin(angle * 13f + 0.20f);

            organicScale += AngularLobe(
                angle,
                -2.54f,
                0.42f,
                0.245f);
            organicScale += AngularLobe(
                angle,
                0.49f,
                0.36f,
                0.040f);
            organicScale += AngularLobe(
                angle,
                -0.55f,
                0.35f,
                0.080f);
            organicScale += AngularLobe(
                angle,
                2.50f,
                0.34f,
                0.045f);
            return superellipse / organicScale;
        }

        private static float AngularLobe(
            float angle,
            float center,
            float width,
            float strength)
        {
            var delta = Mathf.Atan2(
                Mathf.Sin(angle - center),
                Mathf.Cos(angle - center));
            return strength *
                   Mathf.Exp(-Square(delta / width));
        }

        private static float EllipseDistance(
            float x,
            float z,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ)
        {
            return Mathf.Sqrt(
                Square((x - centerX) / radiusX) +
                Square((z - centerZ) / radiusZ));
        }

        private static float WarpedEllipseDistance(
            float x,
            float z,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ,
            float phase)
        {
            var angle = Mathf.Atan2(
                (z - centerZ) / radiusZ,
                (x - centerX) / radiusX);
            var radiusWarp =
                1f +
                0.052f * Mathf.Sin(angle * 3f + phase) +
                0.024f * Mathf.Sin(angle * 7f - phase * 0.63f);
            return EllipseDistance(
                       x,
                       z,
                       centerX,
                       centerZ,
                       radiusX,
                       radiusZ) /
                   radiusWarp;
        }

        private static float IrregularHill(
            float x,
            float z,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ,
            float amplitude,
            float phase)
        {
            var distance = WarpedEllipseDistance(
                x,
                z,
                centerX,
                centerZ,
                radiusX,
                radiusZ,
                phase);
            var broadVariation =
                0.96f +
                (Mathf.PerlinNoise(
                     (x + 610f + phase * 31f) * 0.012f,
                     (z + 470f - phase * 27f) * 0.012f) - 0.5f) *
                0.10f;
            return amplitude *
                   Mathf.Exp(-distance * distance * 1.28f) *
                   broadVariation;
        }

        private static float Gaussian(
            float x,
            float z,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ)
        {
            return Gaussian(
                x,
                z,
                centerX,
                centerZ,
                radiusX,
                radiusZ,
                1f);
        }

        private static float Gaussian(
            float x,
            float z,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ,
            float amplitude)
        {
            var dx = (x - centerX) / radiusX;
            var dz = (z - centerZ) / radiusZ;
            return amplitude *
                   Mathf.Exp(-(dx * dx + dz * dz) * 1.35f);
        }

        private static float LocalizedIrregularStep(
            float x,
            float z,
            float centerX,
            float edgeZ,
            float halfLength,
            float transition,
            float amplitude,
            float phase)
        {
            var normalizedX = (x - centerX) / halfLength;
            var lateralEnvelope =
                1f - SmootherStep(
                    0.62f,
                    1.05f,
                    Mathf.Abs(normalizedX));
            var warpedEdge =
                edgeZ +
                Mathf.Sin(
                    (x - centerX) * 0.082f + phase) * 3.4f +
                Mathf.Sin(
                    (x - centerX) * 0.173f - phase * 0.7f) * 1.5f;
            var upperSide =
                SmoothStep(
                    -transition,
                    transition,
                    z - warpedEdge);
            var rearFade =
                1f - SmootherStep(
                    8f,
                    22f,
                    z - edgeZ);
            var brokenStrength =
                Mathf.Lerp(
                    0.82f,
                    1.12f,
                    Mathf.PerlinNoise(
                        (x + 410f) * 0.038f,
                        (z + 290f) * 0.038f));
            return amplitude *
                   lateralEnvelope *
                   upperSide *
                   rearFade *
                   brokenStrength;
        }

        private static float SmoothStep(
            float minimum,
            float maximum,
            float value)
        {
            if (Mathf.Approximately(minimum, maximum))
            {
                return value >= maximum ? 1f : 0f;
            }

            var t = Mathf.Clamp01(
                (value - minimum) / (maximum - minimum));
            return t * t * (3f - 2f * t);
        }

        private static float SmootherStep(
            float minimum,
            float maximum,
            float value)
        {
            if (Mathf.Approximately(minimum, maximum))
            {
                return value >= maximum ? 1f : 0f;
            }

            var t = Mathf.Clamp01(
                (value - minimum) / (maximum - minimum));
            return t * t * t *
                   (t * (t * 6f - 15f) + 10f);
        }

        private static float Square(float value)
        {
            return value * value;
        }

        private static float NextFloat(
            System.Random random,
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                (float)random.NextDouble());
        }

        private static void EnsureReviewSceneIncludedInBuild()
        {
            var scenes =
                new List<EditorBuildSettingsScene>(
                    EditorBuildSettings.scenes);
            for (var index = 0; index < scenes.Count; index++)
            {
                if (string.Equals(
                        scenes[index].path,
                        ReviewScenePath,
                        StringComparison.Ordinal))
                {
                    if (!scenes[index].enabled)
                    {
                        scenes[index] =
                            new EditorBuildSettingsScene(
                                ReviewScenePath,
                                true);
                        EditorBuildSettings.scenes = scenes.ToArray();
                    }

                    return;
                }
            }

            scenes.Add(
                new EditorBuildSettingsScene(
                    ReviewScenePath,
                    true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Transform RequireTransform(
            Transform root,
            string name)
        {
            Transform found = null;
            foreach (var candidate in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(
                        candidate.name,
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"Duplicate generated marker: {name}");
                }

                found = candidate;
            }

            if (found == null)
            {
                throw new InvalidOperationException(
                    $"Generated marker is missing: {name}");
            }

            return found;
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(
                        current,
                        segments[index]);
                }

                current = next;
            }
        }

        private static Color Html(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
            {
                throw new InvalidOperationException(
                    $"Invalid HTML color: {value}");
            }

            return color;
        }

        private static void SetColor(
            Material material,
            string property,
            Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetFloat(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private readonly struct MarkerDefinition
        {
            public MarkerDefinition(
                string name,
                float x,
                float z)
            {
                Name = name;
                X = x;
                Z = z;
            }

            public string Name { get; }

            public float X { get; }

            public float Z { get; }
        }

        private readonly struct LayerDefinition
        {
            public LayerDefinition(
                string name,
                string baseColor,
                string lightColor,
                string darkColor,
                float tileSize,
                float smoothness)
            {
                Name = name;
                BaseColor = baseColor;
                LightColor = lightColor;
                DarkColor = darkColor;
                TileSize = tileSize;
                Smoothness = smoothness;
            }

            public string Name { get; }

            public string BaseColor { get; }

            public string LightColor { get; }

            public string DarkColor { get; }

            public float TileSize { get; }

            public float Smoothness { get; }
        }

        private readonly struct DecorationCluster
        {
            public DecorationCluster(
                float centerX,
                float centerZ,
                float radiusX,
                float radiusZ)
            {
                Center = new Vector2(centerX, centerZ);
                Radius = new Vector2(radiusX, radiusZ);
            }

            public Vector2 Center { get; }

            public Vector2 Radius { get; }
        }

        private readonly struct HeroBushPlacement
        {
            public HeroBushPlacement(
                GameObject source,
                float x,
                float z,
                float scale,
                float yaw)
            {
                Source = source;
                Position = new Vector2(x, z);
                Scale = scale;
                Yaw = yaw;
            }

            public GameObject Source { get; }

            public Vector2 Position { get; }

            public float Scale { get; }

            public float Yaw { get; }
        }

        private readonly struct ReviewProp
        {
            public ReviewProp(
                string assetPath,
                Vector2 position,
                float yaw)
            {
                AssetPath = assetPath;
                Position = position;
                Yaw = yaw;
            }

            public string AssetPath { get; }

            public Vector2 Position { get; }

            public float Yaw { get; }
        }

        private readonly struct RouteDefinition
        {
            public RouteDefinition(
                string name,
                float halfWidth,
                float shoulder,
                Vector3[] points)
            {
                Name = name;
                HalfWidth = halfWidth;
                Shoulder = shoulder;
                Points = points;
            }

            public string Name { get; }

            public float HalfWidth { get; }

            public float Shoulder { get; }

            public Vector3[] Points { get; }
        }

        private readonly struct PathSample
        {
            public PathSample(
                float distance,
                float progress,
                float height,
                float halfWidth = 0f)
            {
                Distance = distance;
                Progress = progress;
                Height = height;
                HalfWidth = halfWidth;
            }

            public float Distance { get; }

            public float Progress { get; }

            public float Height { get; }

            public float HalfWidth { get; }
        }
    }
}

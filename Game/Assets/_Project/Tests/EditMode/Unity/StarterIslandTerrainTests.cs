using System;
using System.Collections.Generic;
using System.Linq;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using CML.Unity.Mining;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Tests.Unity
{
    public sealed class StarterIslandTerrainTests
    {
        private const string TerrainDataPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Data/TD_StarterIsland.asset";
        private const string PrefabPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Prefabs/PF_StarterIsland_Terrain.prefab";
        private const string ReviewScenePath =
            "Assets/_Project/Scenes/" +
            "91_StarterIsland_Terrain_Review.unity";

        [Test]
        public void TerrainDataUsesProductionScaleLayersAndOrganicHoles()
        {
            var data = RequireAsset<TerrainData>(TerrainDataPath);

            Assert.That(data.heightmapResolution, Is.EqualTo(1025));
            Assert.That(data.alphamapResolution, Is.EqualTo(1024));
            Assert.That(data.size.x, Is.EqualTo(660f).Within(0.001f));
            Assert.That(data.size.y, Is.EqualTo(200f).Within(0.001f));
            Assert.That(data.size.z, Is.EqualTo(500f).Within(0.001f));
            Assert.That(data.terrainLayers, Has.Length.EqualTo(4));
            CollectionAssert.AreEqual(
                new[]
                {
                    "TL_StarterIsland_GrassSun",
                    "TL_StarterIsland_GrassDeep",
                    "TL_StarterIsland_DirtPath",
                    "TL_StarterIsland_CliffWarm"
                },
                data.terrainLayers.Select(layer => layer.name).ToArray());

            foreach (var layer in data.terrainLayers)
            {
                Assert.That(layer, Is.Not.Null);
                Assert.That(layer.diffuseTexture, Is.Not.Null);
                Assert.That(layer.tileSize.x, Is.InRange(3f, 12f));
                Assert.That(layer.tileSize.y, Is.EqualTo(layer.tileSize.x));
                Assert.That(layer.metallic, Is.EqualTo(0f));
                Assert.That(layer.smoothness, Is.InRange(0f, 0.03f));
                Assert.That(
                    layer.diffuseTexture.GetPixels32()
                        .Max(pixel => pixel.a),
                    Is.LessThanOrEqualTo(8),
                    "Terrain albedo alpha feeds URP Terrain/Lit " +
                    "smoothness and must not recreate glossy white bands.");
            }

            var holes = data.GetHoles(
                0,
                0,
                data.holesResolution,
                data.holesResolution);
            Assert.That(
                holes[data.holesResolution / 2,
                      data.holesResolution / 2],
                Is.True,
                "The island centre must remain Terrain surface.");
            Assert.That(holes[0, 0], Is.False);
            Assert.That(
                holes[0, data.holesResolution - 1],
                Is.False);
            Assert.That(
                holes[data.holesResolution - 1, 0],
                Is.False);
            Assert.That(
                holes[data.holesResolution - 1,
                      data.holesResolution - 1],
                Is.False);

            var surfaceCount = 0;
            foreach (var surface in holes)
            {
                if (surface)
                {
                    surfaceCount++;
                }
            }

            var surfaceRatio =
                surfaceCount / (float)holes.Length;
            Assert.That(
                surfaceRatio,
                Is.InRange(0.58f, 0.86f),
                "Terrain holes should expose an organic floating-island " +
                "outline without reducing it to a small patch.");
        }

        [Test]
        public void TerrainDetailsPopulateFourGrassAndFlowerLayers()
        {
            var data = RequireAsset<TerrainData>(TerrainDataPath);

            Assert.That(data.detailResolution, Is.EqualTo(512));
            Assert.That(data.detailPrototypes, Has.Length.EqualTo(4));

            var layerCounts = new long[data.detailPrototypes.Length];
            for (var layer = 0;
                 layer < data.detailPrototypes.Length;
                 layer++)
            {
                var prototype = data.detailPrototypes[layer];
                Assert.That(
                    prototype,
                    Is.Not.Null,
                    $"Missing Terrain detail prototype {layer}.");
                Assert.That(
                    prototype.prototype,
                    Is.Not.Null,
                    $"Terrain detail prototype {layer} has no prefab.");
                layerCounts[layer] =
                    CountDetailInstances(data, layer);
                Assert.That(
                    layerCounts[layer],
                    Is.GreaterThan(0L),
                    $"Terrain detail layer {layer} is empty.");
            }

            Assert.That(
                layerCounts[0] + layerCounts[1],
                Is.GreaterThan(500000L),
                "The two grass detail layers must form a dense carpet.");
            Assert.That(
                layerCounts[2] + layerCounts[3],
                Is.GreaterThan(0L),
                "The two flower detail layers must contain instances.");

            for (var index = 0; index < 2; index++)
            {
                var prototype = data.detailPrototypes[index];
                Assert.That(prototype.usePrototypeMesh, Is.True);
                Assert.That(prototype.useInstancing, Is.True);
                Assert.That(
                    prototype.renderMode,
                    Is.EqualTo(DetailRenderMode.VertexLit));
                Assert.That(
                    prototype.prototype.name,
                    Does.StartWith("PF_TerrainDetail_AdaptiveGrass_"));
                var renderer = prototype.prototype
                    .GetComponent<MeshRenderer>();
                var filter = prototype.prototype
                    .GetComponent<MeshFilter>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(
                    renderer.sharedMaterial.shader.name,
                    Is.EqualTo(
                        "CML/Environment/Starter Island Ground Detail"));
                Assert.That(renderer.sharedMaterial.enableInstancing, Is.True);
                var colors = filter.sharedMesh.colors;
                Assert.That(colors, Has.Length.EqualTo(
                    filter.sharedMesh.vertexCount));
                Assert.That(colors.Min(color => color.r), Is.EqualTo(0f));
                Assert.That(colors.Max(color => color.r), Is.EqualTo(1f));
                Assert.That(
                    filter.sharedMesh.uv,
                    Has.Length.EqualTo(filter.sharedMesh.vertexCount));
                Assert.That(
                    renderer.sharedMaterial.GetTexture("_BladeMask"),
                    Is.Not.Null,
                    "Grass cards need a clipped broad-blade silhouette.");
            }

            for (var index = 2; index < 4; index++)
            {
                var prototype = data.detailPrototypes[index];
                Assert.That(
                    prototype.prototype.name,
                    Does.StartWith("PF_TerrainDetail_UprightFlower_"));
                var filter = prototype.prototype.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.Not.Null);
                var vertices = filter.sharedMesh.vertices;
                Assert.That(vertices.Min(vertex => vertex.y),
                    Is.EqualTo(0f).Within(0.002f),
                    "Flower pivot must sit on the Terrain surface.");
                Assert.That(vertices.Max(vertex => vertex.y),
                    Is.GreaterThan(0.65f),
                    "The FBX -90 degree axis correction must be baked so " +
                    "flowers stand upright instead of lying on the ground.");
            }
        }

        [Test]
        public void AdaptiveGrassIsDenseOnGrassAndSparseButPresentOnPath()
        {
            var data = RequireAsset<TerrainData>(TerrainDataPath);
            var grassA = data.GetDetailLayer(
                0,
                0,
                data.detailWidth,
                data.detailHeight,
                0);
            var grassB = data.GetDetailLayer(
                0,
                0,
                data.detailWidth,
                data.detailHeight,
                1);
            var alphamaps = data.GetAlphamaps(
                0,
                0,
                data.alphamapWidth,
                data.alphamapHeight);
            long denseInstances = 0;
            long pathInstances = 0;
            var denseCells = 0;
            var pathCells = 0;
            var populatedPathCells = 0;
            for (var z = 0; z < data.detailHeight; z++)
            {
                var alphaZ = Mathf.Clamp(
                    Mathf.FloorToInt(
                        (z + 0.5f) / data.detailHeight *
                        data.alphamapHeight),
                    0,
                    data.alphamapHeight - 1);
                for (var x = 0; x < data.detailWidth; x++)
                {
                    var alphaX = Mathf.Clamp(
                        Mathf.FloorToInt(
                            (x + 0.5f) / data.detailWidth *
                            data.alphamapWidth),
                        0,
                        data.alphamapWidth - 1);
                    var amount = grassA[z, x] + grassB[z, x];
                    var grassWeight =
                        alphamaps[alphaZ, alphaX, 0] +
                        alphamaps[alphaZ, alphaX, 1];
                    var pathWeight = alphamaps[alphaZ, alphaX, 2];
                    if (grassWeight > 0.92f)
                    {
                        denseCells++;
                        denseInstances += amount;
                    }

                    if (pathWeight > 0.92f)
                    {
                        pathCells++;
                        pathInstances += amount;
                        if (amount > 0)
                        {
                            populatedPathCells++;
                        }
                    }
                }
            }

            Assert.That(denseCells, Is.GreaterThan(10000));
            Assert.That(pathCells, Is.GreaterThan(1000));
            Assert.That(
                denseInstances / (double)denseCells,
                Is.GreaterThan(3.5d),
                "Grass-painted surfaces need a dense carpet.");
            Assert.That(
                pathInstances / (double)pathCells,
                Is.LessThan(0.35d),
                "The path centre must remain visually open.");
            Assert.That(
                populatedPathCells,
                Is.GreaterThan(100),
                "Some path cells must contain blades so their shader can " +
                "visibly inherit the dirt texture.");
        }

        [Test]
        public void HeightFieldHasLandmarkHierarchyAndOrganicRimRelief()
        {
            var data = RequireAsset<TerrainData>(TerrainDataPath);
            var surface = data.GetHoles(
                0,
                0,
                data.holesResolution,
                data.holesResolution);
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            var maximumSlope = 0f;

            for (var z = 0; z <= 128; z++)
            {
                var normalizedZ = z / 128f;
                for (var x = 0; x <= 128; x++)
                {
                    var normalizedX = x / 128f;
                    if (!IsSurfaceAtNormalized(
                            surface,
                            normalizedX,
                            normalizedZ))
                    {
                        continue;
                    }

                    var height = data.GetInterpolatedHeight(
                        normalizedX,
                        normalizedZ);
                    minimum = Mathf.Min(minimum, height);
                    maximum = Mathf.Max(maximum, height);
                    maximumSlope = Mathf.Max(
                        maximumSlope,
                        data.GetSteepness(
                            normalizedX,
                            normalizedZ));
                }
            }

            Assert.That(minimum, Is.LessThan(5f));
            Assert.That(maximum, Is.LessThan(160f));
            Assert.That(
                maximum - minimum,
                Is.GreaterThanOrEqualTo(125f),
                "The expanded island must preserve a mountainous " +
                "height range, not become a uniformly enlarged plateau.");
            Assert.That(
                maximumSlope,
                Is.GreaterThan(55f),
                "The island rim and mesa shoulders must be Terrain-authored " +
                "rock walls, not broad gentle hills.");

            var spawn = HeightAtWorld(data, -290f, -202f);
            var factory = HeightAtWorld(data, -12f, -18f);
            var portal = HeightAtWorld(data, 220f, 115f);
            var spring = HeightAtWorld(data, -205f, 150f);
            Assert.That(spawn, Is.InRange(7f, 13f));
            Assert.That(factory, Is.InRange(31f, 39f));
            Assert.That(portal, Is.InRange(96f, 108f));
            Assert.That(spring, Is.InRange(136f, 148f));
            Assert.That(factory, Is.GreaterThan(spawn + 18f));
            Assert.That(portal, Is.GreaterThan(factory + 50f));
            Assert.That(spring, Is.GreaterThan(portal + 28f));

            var rimHeights =
                SampleSurfaceRimHeights(data, surface, 48);
            Assert.That(
                rimHeights.Max() - rimHeights.Min(),
                Is.GreaterThan(18f),
                "The organic rim needs alternating cliff and saddle " +
                "sectors instead of a level cake-edge silhouette.");
        }

        [Test]
        public void FiveRoutesArePaintedAsLightDirtOnTheTerrain()
        {
            var data = RequireAsset<TerrainData>(TerrainDataPath);
            var routeSamples = new[]
            {
                new Vector2(-170f, -143f),
                new Vector2(110f, 34f),
                new Vector2(-125f, 35f),
                new Vector2(120f, -70f),
                new Vector2(52f, 180f)
            };

            foreach (var point in routeSamples)
            {
                var normalized = WorldToNormalized(data, point);
                var x = Mathf.Clamp(
                    Mathf.RoundToInt(
                        normalized.x *
                        (data.alphamapWidth - 1)),
                    0,
                    data.alphamapWidth - 1);
                var z = Mathf.Clamp(
                    Mathf.RoundToInt(
                        normalized.y *
                        (data.alphamapHeight - 1)),
                    0,
                    data.alphamapHeight - 1);
                var weights = data.GetAlphamaps(x, z, 1, 1);
                Assert.That(
                    weights[0, 0, 2],
                    Is.GreaterThan(0.72f),
                    $"Route at {point} is not visibly painted as dirt.");
            }
        }

        [Test]
        public void PrefabUsesTerrainColliderAsItsOnlyCollisionAuthority()
        {
            var prefab = RequireAsset<GameObject>(PrefabPath);
            var terrains = prefab.GetComponentsInChildren<Terrain>(true);
            var terrainColliders =
                prefab.GetComponentsInChildren<TerrainCollider>(true);

            Assert.That(terrains, Has.Length.EqualTo(1));
            Assert.That(terrainColliders, Has.Length.EqualTo(1));
            Assert.That(
                terrainColliders[0].terrainData,
                Is.SameAs(terrains[0].terrainData));
            Assert.That(
                terrains[0].terrainData,
                Is.SameAs(RequireAsset<TerrainData>(TerrainDataPath)));
            var allColliders =
                prefab.GetComponentsInChildren<Collider>(true);
            Assert.That(
                allColliders,
                Has.Length.EqualTo(1),
                "TerrainCollider must be the prefab's sole collision " +
                "authority; decorative or visual geometry stays collider-free.");
            Assert.That(allColliders[0], Is.SameAs(terrainColliders[0]));
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(terrains[0].detailObjectDistance,
                Is.EqualTo(92f).Within(0.001f));
            Assert.That(terrains[0].detailObjectDensity,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                prefab.GetComponentsInChildren<Transform>(true)
                    .Any(candidate => string.Equals(
                        candidate.name,
                        "GroundCover_Chunked",
                        StringComparison.Ordinal)),
                Is.False,
                "Legacy baked grass must not coexist with Terrain details.");

            var underbody = prefab
                .GetComponentsInChildren<Transform>(true)
                .Single(candidate =>
                    string.Equals(
                        candidate.name,
                        "TerrainUnderbody",
                        StringComparison.Ordinal));
            var underbodyFilter = underbody.GetComponent<MeshFilter>();
            var underbodyRenderer =
                underbody.GetComponent<MeshRenderer>();
            Assert.That(underbodyFilter, Is.Not.Null);
            Assert.That(underbodyFilter.sharedMesh, Is.Not.Null);
            Assert.That(
                underbodyFilter.sharedMesh.bounds.min.y,
                Is.EqualTo(-180f).Within(0.08f));
            Assert.That(
                underbodyFilter.sharedMesh.bounds.max.y,
                Is.GreaterThan(0f));
            Assert.That(underbodyRenderer, Is.Not.Null);
            Assert.That(underbodyRenderer.sharedMaterial, Is.Not.Null);
            Assert.That(
                underbody.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The visual underbody must not create a second " +
                "landscape collision authority.");

            var buildScene = EditorBuildSettings.scenes.SingleOrDefault(
                candidate => string.Equals(
                    candidate.path,
                    ReviewScenePath,
                    StringComparison.Ordinal));
            Assert.That(buildScene, Is.Not.Null);
            Assert.That(
                buildScene.enabled,
                Is.False,
                "The QA review scene must not ship in the game build.");
        }

        [Test]
        public void ReviewSceneContainsLocalPondAndAuthoredGameplayCollision()
        {
            var scene = EditorSceneManager.OpenScene(
                ReviewScenePath,
                OpenSceneMode.Additive);
            try
            {
                var island = FindInScene(
                    scene,
                    "PF_StarterIsland_Terrain");
                var terrainTop = FindInScene(scene, "TerrainTop");
                var pond = FindInScene(scene, "ENV_PondWater");
                var stream = FindInScene(scene, "ENV_StreamWater");
                var foliage = FindInScene(scene, "FoliageRoot");
                var rocks = FindInScene(scene, "RocksRoot");
                var player = FindInScene(
                    scene,
                    "AIR_FirstPersonPlayer");
                var airship = FindInScene(scene, "PF_Airship");
                var scenarioRoot =
                    FindInScene(scene, "AIR_StarterIslandReady");

                Assert.That(island, Is.Not.Null);
                Assert.That(terrainTop, Is.Not.Null);
                Assert.That(pond, Is.Not.Null);
                Assert.That(stream, Is.Not.Null);
                Assert.That(foliage, Is.Not.Null);
                Assert.That(rocks, Is.Not.Null);
                Assert.That(
                    FindInScene(scene, "CliffRockKitRoot"),
                    Is.Null,
                    "Detached cliff modules read as medallions pasted onto " +
                    "the Terrain wall; the cliff form belongs to the Terrain.");
                Assert.That(player, Is.Not.Null);
                Assert.That(airship, Is.Not.Null);
                Assert.That(scenarioRoot, Is.Not.Null);
                Assert.That(
                    FindInScene(scene, "ENV_Water"),
                    Is.Null,
                    "A floating island must not receive an ocean plane.");
                Assert.That(
                    pond.transform.position.x,
                    Is.EqualTo(-178f).Within(0.001f));
                Assert.That(
                    pond.transform.position.y,
                    Is.EqualTo(26.5f).Within(0.001f));
                Assert.That(
                    pond.transform.position.z,
                    Is.EqualTo(-72f).Within(0.001f));
                Assert.That(
                    pond.transform.localScale.x,
                    Is.EqualTo(6.4f).Within(0.001f));
                Assert.That(
                    pond.transform.localScale.z,
                    Is.EqualTo(4.2f).Within(0.001f));
                Assert.That(pond.GetComponent<Collider>(), Is.Null);
                Assert.That(stream.GetComponent<Collider>(), Is.Null);

                Assert.That(
                    terrainTop.GetComponent<Terrain>(),
                    Is.Not.Null);
                Assert.That(
                    terrainTop.GetComponent<TerrainCollider>(),
                    Is.Not.Null);
                Assert.That(
                    foliage.transform.childCount,
                    Is.GreaterThanOrEqualTo(500),
                    "The redesigned 660 x 500 m island must retain the " +
                    "dense V4 tree scatter configured by the generator.");
                Assert.That(
                    rocks.transform.childCount,
                    Is.GreaterThanOrEqualTo(30));
                Assert.That(
                    foliage.GetComponentsInChildren<MeshCollider>(true),
                    Is.Not.Empty,
                    "Production tree trunks use authored mesh collision.");
                Assert.That(
                    rocks.GetComponentsInChildren<MeshCollider>(true),
                    Is.Not.Empty,
                    "Mineable decoration must follow the visible rock mesh.");
                Assert.That(
                    rocks.GetComponentsInChildren<BoxCollider>(true),
                    Is.Empty,
                    "Mineable rocks must not retain padded box hit proxies.");
                Assert.That(
                    player.GetComponent<CharacterController>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponent<AirshipRelativePassenger>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponent<AirshipInputAdapter>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponent<FirstPersonCharacterMotor>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponent<FirstPersonMouseLook>(),
                    Is.Not.Null);
                Assert.That(
                    player.GetComponentInChildren<Camera>(true),
                    Is.Not.Null);
                Assert.That(
                    airship.GetComponent<AirshipSimulationBridge>(),
                    Is.Not.Null);
                Assert.That(
                    airship.GetComponent<AirshipFrame>(),
                    Is.Not.Null);
                Assert.That(
                    airship.GetComponentInChildren<AirshipPilotStation>(true),
                    Is.Not.Null);
                Assert.That(
                    scenarioRoot.GetComponent<AirshipTechnicalScenario>(),
                    Is.Not.Null);
                Assert.That(
                    scene.GetRootGameObjects()
                        .SelectMany(root =>
                            root.GetComponentsInChildren<Camera>(true))
                        .Count(camera => camera.CompareTag("MainCamera")),
                    Is.EqualTo(1));
                Assert.That(
                    scene.GetRootGameObjects()
                        .SelectMany(root =>
                            root.GetComponentsInChildren<AudioListener>(true))
                        .Count(),
                    Is.EqualTo(1));

                var treeMaterials = foliage
                    .GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material =>
                        material != null &&
                        AssetDatabase.GetAssetPath(material).StartsWith(
                            "Assets/_Project/Art/Environment/" +
                            "StarterIsland/V4/Trees/Materials/",
                            StringComparison.Ordinal))
                    .ToArray();
                Assert.That(treeMaterials, Is.Not.Empty);
                Assert.That(
                    treeMaterials.Any(material =>
                        material.HasProperty("_BaseMap") &&
                        material.GetTexture("_BaseMap") != null),
                    Is.True,
                    "V4 trees must not fall back to grey/white materials.");
                Assert.That(
                    treeMaterials.Any(material =>
                        material.shader != null &&
                        string.Equals(
                            material.shader.name,
                            "CML/Environment/" +
                            "Starter Island V4 Tree Leaves",
                            StringComparison.Ordinal)),
                    Is.True,
                    "The Terrain scene must preserve the production V4 " +
                    "leaf shader instead of applying a generic override.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void MineableRocksUseExactMeshCollisionAndStableIds()
        {
            var scene = EditorSceneManager.OpenScene(
                ReviewScenePath,
                OpenSceneMode.Additive);
            try
            {
                var sources = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            ManualMiningSourceIdentity>(true))
                    .Where(source => source != null && source.IsFinite)
                    .ToArray();
                Assert.That(
                    sources.Length,
                    Is.GreaterThanOrEqualTo(600),
                    "Starter Island should expose its authored rock scatter " +
                    "as finite mining sources.");

                var stableIds = new HashSet<string>(
                    StringComparer.Ordinal);
                var authoredRockCount = 0;
                foreach (var source in sources)
                {
                    Assert.That(
                        source.SourceId,
                        Is.Not.Null.And.Not.Empty,
                        $"{source.name} would discard every committed hit.");
                    Assert.That(
                        stableIds.Add(source.SourceId),
                        Is.True,
                        $"Duplicate mining id '{source.SourceId}'.");
                    Assert.That(
                        source.transform.Find(
                            ManualMiningSourceIdentity.
                                MiningHitProxyName),
                        Is.Null,
                        $"{source.name} still has a legacy box proxy.");
                    Assert.That(
                        source.GetComponentsInChildren<BoxCollider>(true),
                        Is.Empty,
                        $"{source.name} must use mesh collision only.");

                    if (ManualMiningSourceIdentity.
                        TryGetAuthoredEnvironmentalStoneSourceId(
                            source.name,
                            out var expectedId))
                    {
                        authoredRockCount++;
                        Assert.That(
                            source.SourceKind,
                            Is.EqualTo(
                                ManualMiningSourceKind.
                                    EnvironmentalStone));
                        Assert.That(source.SourceId, Is.EqualTo(expectedId));
                    }

                    var filters = source.GetComponentsInChildren<
                        MeshFilter>(true)
                        .Where(filter => filter.sharedMesh != null)
                        .ToArray();
                    Assert.That(filters, Is.Not.Empty);
                    foreach (var filter in filters)
                    {
                        var collider = filter
                            .GetComponents<MeshCollider>()
                            .SingleOrDefault(candidate =>
                                candidate.sharedMesh == filter.sharedMesh);
                        Assert.That(
                            collider,
                            Is.Not.Null,
                            $"{filter.name} has no collider for its exact mesh.");
                        Assert.That(collider.enabled, Is.True);
                        Assert.That(collider.isTrigger, Is.False);
                        Assert.That(collider.convex, Is.False);
                    }
                }

                Assert.That(
                    authoredRockCount,
                    Is.GreaterThanOrEqualTo(600));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ReviewAirshipScenarioInitializesAndMovesFromPilotInput()
        {
            var scene = EditorSceneManager.OpenScene(
                ReviewScenePath,
                OpenSceneMode.Additive);
            try
            {
                var scenario = FindInScene(
                        scene,
                        "AIR_StarterIslandReady")
                    .GetComponent<AirshipTechnicalScenario>();
                var player = FindInScene(scene, "AIR_FirstPersonPlayer");
                var airship = FindInScene(scene, "PF_Airship");
                var bridge =
                    airship.GetComponent<AirshipSimulationBridge>();

                scenario.InitializeNow();

                Assert.That(scenario.IsReady, Is.True);
                Assert.That(bridge.IsInitialized, Is.True);
                Assert.That(
                    bridge.GetAirshipSnapshot().TryGetPlayer(
                        AirshipTechnicalIds.Player,
                        out var initialPlayer),
                    Is.True);
                Assert.That(initialPlayer.IsPiloting, Is.False);
                Assert.That(
                    player.GetComponent<AirshipRelativePassenger>().IsAboard,
                    Is.True);

                bridge.QueuePilotBegin();
                bridge.AdvanceOneTick();
                Assert.That(
                    bridge.GetAirshipSnapshot().TryGetPlayer(
                        AirshipTechnicalIds.Player,
                        out var pilot),
                    Is.True);
                Assert.That(pilot.IsPiloting, Is.True);

                Assert.That(
                    bridge.GetAirshipSnapshot().TryGetAirship(
                        AirshipTechnicalIds.Airship,
                        out var before),
                    Is.True);
                var beforePosition = before.Pose.Position;
                bridge.QueuePilotInput(
                    new AirshipPilotInputState(1000, 0, 0, 0));
                for (var tick = 0; tick < 12; tick++)
                {
                    bridge.AdvanceOneTick();
                }
                bridge.RenderPresentation(1f);

                Assert.That(
                    bridge.GetAirshipSnapshot().TryGetAirship(
                        AirshipTechnicalIds.Airship,
                        out var after),
                    Is.True);
                Assert.That(
                    after.Mode,
                    Is.EqualTo(AirshipFlightMode.Flying));
                Assert.That(after.Pose.Position, Is.Not.EqualTo(beforePosition));
                Assert.That(
                    Vector3.Distance(
                        airship.transform.position,
                        new Vector3(
                            after.Pose.Position.X / 1000f,
                            after.Pose.Position.Y / 1000f,
                            after.Pose.Position.Z / 1000f)),
                    Is.LessThan(0.01f));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ReviewPlayerWalksFromCabinAcrossRampOntoTerrain()
        {
            var scene = EditorSceneManager.OpenScene(
                ReviewScenePath,
                OpenSceneMode.Additive);
            try
            {
                var scenario = FindInScene(
                        scene,
                        "AIR_StarterIslandReady")
                    .GetComponent<AirshipTechnicalScenario>();
                var player = FindInScene(scene, "AIR_FirstPersonPlayer");
                var airship = FindInScene(scene, "PF_Airship");
                var motor =
                    player.GetComponent<FirstPersonCharacterMotor>();
                var controller =
                    player.GetComponent<CharacterController>();
                var terrain =
                    FindInScene(scene, "TerrainTop").GetComponent<Terrain>();

                scenario.InitializeNow();
                Physics.SyncTransforms();
                var start = player.transform.position;

                const float step = 1f / 60f;
                for (var frame = 0; frame < 30; frame++)
                {
                    motor.Move(-1000, 0, step);
                }

                for (var frame = 0; frame < 90; frame++)
                {
                    motor.Move(0, 1000, step);
                }

                Physics.SyncTransforms();
                var terrainHeight =
                    terrain.SampleHeight(player.transform.position) +
                    terrain.transform.position.y;

                Assert.That(
                    player.transform.position.x - airship.transform.position.x,
                    Is.GreaterThan(4.1f),
                    "The player did not cross the visible boarding ramp.");
                Assert.That(
                    player.transform.position.y,
                    Is.EqualTo(terrainHeight).Within(0.35f),
                    "The player did not settle on the Unity TerrainCollider.");
                Assert.That(
                    Vector3.Distance(start, player.transform.position),
                    Is.GreaterThan(4f));
                Assert.That(controller.enabled, Is.True);
                Assert.That(player.GetComponent<Rigidbody>(), Is.Null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static long CountDetailInstances(
            TerrainData data,
            int layer)
        {
            var instances = data.GetDetailLayer(
                0,
                0,
                data.detailWidth,
                data.detailHeight,
                layer);
            long count = 0;
            foreach (var amount in instances)
            {
                count += amount;
            }

            return count;
        }

        private static float[] SampleSurfaceRimHeights(
            TerrainData data,
            bool[,] surface,
            int sampleCount)
        {
            var heights = new float[sampleCount];
            var halfWidth = data.size.x * 0.5f;
            var halfLength = data.size.z * 0.5f;
            const int radialSteps = 320;

            for (var sample = 0; sample < sampleCount; sample++)
            {
                var angle =
                    sample * Mathf.PI * 2f / sampleCount;
                var direction = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                var xLimit =
                    Mathf.Abs(direction.x) < 0.0001f
                        ? float.PositiveInfinity
                        : halfWidth / Mathf.Abs(direction.x);
                var zLimit =
                    Mathf.Abs(direction.y) < 0.0001f
                        ? float.PositiveInfinity
                        : halfLength / Mathf.Abs(direction.y);
                var maximumDistance = Mathf.Min(xLimit, zLimit);
                var foundSurface = false;
                var rimPoint = Vector2.zero;

                for (var step = 0; step <= radialSteps; step++)
                {
                    var point =
                        direction *
                        (maximumDistance * step / radialSteps);
                    var normalized = WorldToNormalized(data, point);
                    if (IsSurfaceAtNormalized(
                            surface,
                            normalized.x,
                            normalized.y))
                    {
                        foundSurface = true;
                        rimPoint = point;
                        continue;
                    }

                    if (foundSurface)
                    {
                        break;
                    }
                }

                Assert.That(
                    foundSurface,
                    Is.True,
                    $"No Terrain surface found along rim sample {sample}.");
                heights[sample] =
                    HeightAtWorld(data, rimPoint.x, rimPoint.y);
            }

            return heights;
        }

        private static bool IsSurfaceAtNormalized(
            bool[,] surface,
            float normalizedX,
            float normalizedZ)
        {
            var x = Mathf.Clamp(
                Mathf.RoundToInt(
                    normalizedX * (surface.GetLength(1) - 1)),
                0,
                surface.GetLength(1) - 1);
            var z = Mathf.Clamp(
                Mathf.RoundToInt(
                    normalizedZ * (surface.GetLength(0) - 1)),
                0,
                surface.GetLength(0) - 1);
            return surface[z, x];
        }

        private static float HeightAtWorld(
            TerrainData data,
            float x,
            float z)
        {
            var normalized = WorldToNormalized(
                data,
                new Vector2(x, z));
            return data.GetInterpolatedHeight(
                normalized.x,
                normalized.y);
        }

        private static Vector2 WorldToNormalized(
            TerrainData data,
            Vector2 point)
        {
            return new Vector2(
                Mathf.Clamp01(
                    (point.x + data.size.x * 0.5f) /
                    data.size.x),
                Mathf.Clamp01(
                    (point.y + data.size.z * 0.5f) /
                    data.size.z));
        }

        private static GameObject FindInScene(
            Scene scene,
            string objectName)
        {
            GameObject result = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(
                            transform.name,
                            objectName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (result != null)
                    {
                        Assert.Fail(
                            $"Duplicate object in review scene: {objectName}");
                    }

                    result = transform.gameObject;
                }
            }

            return result;
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"Missing asset: {path}");
            return asset;
        }
    }
}

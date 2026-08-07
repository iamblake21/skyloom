using System;
using System.Collections.Generic;
using CML.Unity.Factory;
using CML.Unity.Gathering;
using CML.Unity.Mining;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// See FiberPlantAssetSetup: CML.Editor.Environment would shadow
// System.Environment across the whole editor assembly.
namespace CML.Editor.Art
{
    /// <summary>
    /// Scatters the wild fibre plants across the opening area of the Starter
    /// Island.
    ///
    /// It never regenerates the island. The terrain, its decoration and every
    /// authored marker are read, not rewritten: the command only owns one root
    /// object and replaces it wholesale, so running it twice cannot duplicate a
    /// single plant.
    ///
    /// Placement is deliberate rather than random. DVS-002 requires the opening
    /// resources not to depend on chance, so the first cluster sits inside the
    /// ring the player crosses on their way out of the wreck, and the seed is
    /// fixed so a rerun reproduces the same island.
    /// </summary>
    public static class FiberPlantSceneSetup
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";

        private const string SourcesRootName = "GAMEPLAY_HandGatherSources";
        private const string SpawnMarkerName = "REF_PlayerSpawn";
        private const string DockMarkerName = "REF_AirshipDock";
        private const string TutorialMarkerName = "REF_TutorialCenter";

        private const int PlacementSeed = 51_207;
        private const float MaximumSteepness = 28f;
        private const float DockClearance = 9f;
        private const float MinimumSpacing = 2.6f;

        // Two fibres per tuft in two seconds, straight from the canonical
        // opening rate in draft_graph_manual_steam.md.
        private const int YieldPerPlant = 2;

        // Picking sticks off the ground is quicker than pulling fibre out of a
        // living plant, and a big scatter is worth more than a small one.
        private const int LargeStickYield = 3;
        private const int SmallStickYield = 2;

        private const string PathPebblePrefix = "DEC_PathPebble_";
        private const int PebbleYield = 1;

        private const float GroundLift = 0.02f;
        // A rosette grows toward the sky, so it only leans a third of the way
        // to the slope. A scatter of sticks is lying on the ground and follows
        // it completely.
        private const float PlantAlignment = 0.35f;
        private const float StickAlignment = 1f;
        private const float PebbleRadius = 55f;
        private const int MaximumPebbles = 40;

        [MenuItem("CML/Gameplay/Place Fiber Plants In Starter Island")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Leave play mode before placing the fibre plants.");
            }

            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            var terrain = FindComponentInScene<Terrain>(scene);
            if (terrain == null)
            {
                throw new InvalidOperationException(
                    $"{ScenePath} has no Terrain.");
            }

            var placed = BuildIntoScene(scene, terrain);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"HAND_GATHER_SCENE sources={placed} status=PASS");
        }

        public static int BuildIntoScene(Scene scene, Terrain terrain)
        {
            var large = RequireAsset(FiberPlantAssetSetup.PlantLargePrefabPath);
            var small = RequireAsset(FiberPlantAssetSetup.PlantSmallPrefabPath);

            // Replace, never append: this is the only object the command owns.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == SourcesRootName)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    break;
                }
            }

            var sourcesRoot = new GameObject(SourcesRootName);
            SceneManager.MoveGameObjectToScene(sourcesRoot, scene);

            var terrainRoot = terrain.transform.root;
            var spawn = RequireMarker(terrainRoot, SpawnMarkerName);
            var dock = RequireMarker(terrainRoot, DockMarkerName);
            var tutorial = RequireMarker(terrainRoot, TutorialMarkerName);

            var random = new System.Random(PlacementSeed);
            var accepted = new List<Vector3>();
            var holes = terrain.terrainData.GetHoles(
                0,
                0,
                terrain.terrainData.holesResolution,
                terrain.terrainData.holesResolution);

            // 1. The guaranteed ring. The player leaves the wreck through it,
            //    so the first fibres cannot be missed.
            PlaceRing(
                terrain,
                holes,
                sourcesRoot.transform,
                large,
                small,
                accepted,
                random,
                dock.position,
                spawn.position,
                count: 5,
                minimumRadius: 7f,
                maximumRadius: 15f);

            // 2. Along the walk toward the tutorial area, so the resource keeps
            //    appearing while the player is still learning to look for it.
            var routeDirection = tutorial.position - spawn.position;
            routeDirection.y = 0f;
            var routeLength = routeDirection.magnitude;
            if (routeLength > 1f)
            {
                routeDirection /= routeLength;
                for (var index = 0; index < 8; index++)
                {
                    var along = Mathf.Lerp(
                        0.12f,
                        0.72f,
                        (index + 0.5f) / 8f) * routeLength;
                    var lateral = (float)(random.NextDouble() * 2.0 - 1.0) * 11f;
                    var side = new Vector3(
                        -routeDirection.z,
                        0f,
                        routeDirection.x);
                    var candidate = spawn.position
                        + routeDirection * along
                        + side * lateral;
                    TryPlace(
                        terrain,
                        holes,
                        sourcesRoot.transform,
                        random.NextDouble() < 0.45 ? small : large,
                        accepted,
                        random,
                        candidate,
                        dock.position);
                }
            }

            // 3. Scattered through the opening bowl, so the species reads as
            //    part of the island rather than as a quest prop.
            for (var index = 0; index < 9; index++)
            {
                var angle = (float)(random.NextDouble() * Math.PI * 2.0);
                var radius = Mathf.Lerp(
                    18f,
                    46f,
                    (float)random.NextDouble());
                var candidate = spawn.position + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);
                TryPlace(
                    terrain,
                    holes,
                    sourcesRoot.transform,
                    random.NextDouble() < 0.5 ? small : large,
                    accepted,
                    random,
                    candidate,
                    dock.position);
            }

            if (accepted.Count < 5)
            {
                throw new InvalidOperationException(
                    $"Only {accepted.Count} fibre plants found valid ground. " +
                    "The opening would not be completable.");
            }

            // 4. Fallen sticks, under the same root: they are gathered by the
            //    same gesture and belong to the same opening.
            var plants = accepted.Count;
            var stickLarge = RequireAsset(StickAssetSetup.ScatterLargePrefabPath);
            var stickSmall = RequireAsset(StickAssetSetup.ScatterSmallPrefabPath);
            for (var index = 0; index < 12; index++)
            {
                var angle = (float)(random.NextDouble() * Math.PI * 2.0);
                var radius = Mathf.Lerp(
                    6f, 40f, (float)random.NextDouble());
                var candidate = spawn.position + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);
                var bigScatter = random.NextDouble() < 0.55;
                TryPlace(
                    terrain,
                    holes,
                    sourcesRoot.transform,
                    bigScatter ? stickLarge : stickSmall,
                    accepted,
                    random,
                    candidate,
                    dock.position,
                    HandGatherSourceKind.FallenSticks,
                    bigScatter ? LargeStickYield : SmallStickYield,
                    "sticks",
                    StickAlignment);
            }

            var sticks = accepted.Count - plants;
            if (sticks < 4)
            {
                throw new InvalidOperationException(
                    $"Only {sticks} stick scatters found valid ground. The " +
                    "Piccone recipe would be unreachable.");
            }

            // 5. Loose pebbles. These are not new props: the path pebbles are
            //    already scattered through the opening and MINE-003 left them
            //    deliberately non-interactive because a pickaxe should not
            //    "mine" a pebble. Picking one up by hand is a different verb,
            //    so they become the opening's stone without adding any art.
            var pebbles = ConfigureLoosePebbles(scene, spawn.position);

            EnsureGatherController(scene);

            EditorUtility.SetDirty(sourcesRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                $"HAND_GATHER_SOURCES plants={plants} sticks={sticks} " +
                $"pebbles={pebbles} fibre={plants * YieldPerPlant}");
            return accepted.Count + pebbles;
        }

        private static void PlaceRing(
            Terrain terrain,
            bool[,] holes,
            Transform parent,
            GameObject large,
            GameObject small,
            List<Vector3> accepted,
            System.Random random,
            Vector3 dockPosition,
            Vector3 centre,
            int count,
            float minimumRadius,
            float maximumRadius)
        {
            for (var index = 0; index < count; index++)
            {
                // Evenly spaced angles with a jitter: a perfect circle of
                // plants around the spawn would read as placed by a designer.
                var angle = (float)(
                    Math.PI * 2.0 * index / count
                    + (random.NextDouble() - 0.5) * 0.7);
                var radius = Mathf.Lerp(
                    minimumRadius,
                    maximumRadius,
                    (float)random.NextDouble());
                var candidate = centre + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);
                TryPlace(
                    terrain,
                    holes,
                    parent,
                    index % 3 == 0 ? small : large,
                    accepted,
                    random,
                    candidate,
                    dockPosition);
            }
        }

        private static bool TryPlace(
            Terrain terrain,
            bool[,] holes,
            Transform parent,
            GameObject prefab,
            List<Vector3> accepted,
            System.Random random,
            Vector3 candidate,
            Vector3 dockPosition,
            HandGatherSourceKind kind = HandGatherSourceKind.WildFiberTuft,
            int units = YieldPerPlant,
            string label = "fiber",
            float alignment = PlantAlignment)
        {
            if (!TryResolveGround(terrain, holes, candidate, out var position))
            {
                return false;
            }

            var flatDock = dockPosition;
            flatDock.y = position.y;
            if (Vector3.Distance(position, flatDock) < DockClearance)
            {
                return false;
            }

            for (var index = 0; index < accepted.Count; index++)
            {
                if (Vector3.Distance(accepted[index], position)
                    < MinimumSpacing)
                {
                    return false;
                }
            }

            var instance = PrefabUtility.InstantiatePrefab(
                prefab,
                parent.gameObject.scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate {prefab.name}.");
            }

            instance.transform.SetParent(parent, worldPositionStays: true);
            // Lift by a few millimetres: sampling the heightmap exactly puts
            // the lowest vertex on the surface, and any mesh detail below the
            // pivot then pokes through it.
            instance.transform.position = position + Vector3.up * GroundLift;
            // Align to the ground, not to the world. A flat scatter of sticks
            // placed straight up on a slope buries its uphill half; that is
            // what made the first pass look like the props were sunk into the
            // terrain. Plants only lean part of the way, because a rosette
            // growing perpendicular to a hillside reads as drunk.
            var normal = TerrainNormal(terrain, position);
            var upright = Quaternion.FromToRotation(
                Vector3.up,
                Vector3.Slerp(Vector3.up, normal, alignment).normalized);
            instance.transform.rotation =
                upright
                * Quaternion.Euler(
                    0f,
                    (float)(random.NextDouble() * 360.0),
                    0f);
            var scale = Mathf.Lerp(0.88f, 1.12f, (float)random.NextDouble());
            instance.transform.localScale = new Vector3(scale, scale, scale);
            instance.name = $"GATHER_{label}_{accepted.Count:D2}";

            var identity = instance.GetComponent<HandGatherSourceIdentity>();
            if (identity == null)
            {
                identity = instance.AddComponent<HandGatherSourceIdentity>();
            }

            identity.Configure(
                kind,
                $"{label}.wild.{accepted.Count:D2}",
                units);
            if (instance.GetComponent<HandGatherHighlight>() == null)
            {
                instance.AddComponent<HandGatherHighlight>();
            }

            EditorUtility.SetDirty(instance);

            accepted.Add(position);
            return true;
        }

        /// <summary>
        /// Turns the path pebbles nearest the wreck into hand-gathered stone.
        ///
        /// Only the ones inside the opening are touched: the island's paths
        /// carry the same decoration end to end, and making all of it a
        /// resource would turn scenery into a shopping list.
        /// </summary>
        private static int ConfigureLoosePebbles(Scene scene, Vector3 spawn)
        {
            var rocksRoot = FindGameObjectInScene(scene, "RocksRoot");
            if (rocksRoot == null)
            {
                throw new InvalidOperationException(
                    "Starter Island is missing its authored RocksRoot.");
            }

            var configured = 0;
            foreach (var child in
                     rocksRoot.GetComponentsInChildren<Transform>(true))
            {
                if (configured >= MaximumPebbles
                    || !child.name.StartsWith(
                        PathPebblePrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                // A path pebble must never carry a box: an earlier pass of this
                // command added one before the mesh-collision contract was
                // noticed, so the cleanup runs on every pebble, not only the
                // ones inside the radius.
                foreach (var box in
                         child.GetComponents<BoxCollider>())
                {
                    UnityEngine.Object.DestroyImmediate(box, true);
                }

                var flatSpawn = spawn;
                flatSpawn.y = child.position.y;
                if (Vector3.Distance(child.position, flatSpawn)
                    > PebbleRadius)
                {
                    continue;
                }

                // No collider is added here. The pebbles already carry the
                // exact MeshCollider that MINE-003 gave them, and
                // StarterIslandTerrainTests forbids a box on any mining source
                // because the pickaxe raycast needs the real mesh. The gather
                // raycast hits that same collider.
                if (child.GetComponentInChildren<MeshCollider>(true) == null)
                {
                    continue;
                }

                var identity = child.GetComponent<HandGatherSourceIdentity>();
                if (identity == null)
                {
                    identity = child.gameObject
                        .AddComponent<HandGatherSourceIdentity>();
                }

                identity.Configure(
                    HandGatherSourceKind.LoosePebble,
                    $"pebble.{child.name}",
                    PebbleYield);

                if (child.GetComponent<HandGatherInteractionTarget>() == null)
                {
                    child.gameObject
                        .AddComponent<HandGatherInteractionTarget>();
                }

                if (child.GetComponent<HandGatherHighlight>() == null)
                {
                    child.gameObject.AddComponent<HandGatherHighlight>();
                }

                EditorUtility.SetDirty(child.gameObject);
                configured++;
            }

            if (configured < 6)
            {
                throw new InvalidOperationException(
                    $"Only {configured} path pebbles sit within " +
                    $"{PebbleRadius} m of the spawn. The Piccone needs two " +
                    "Stones and the opening would not be completable.");
            }

            return configured;
        }

        private static GameObject FindGameObjectInScene(
            Scene scene,
            string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var child in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Puts the held-gather controller on the player camera, next to the
        /// mining controller CanonicalFactorySceneInstaller already installs
        /// there. Both read the same centre ray, so they belong on the same
        /// object.
        /// </summary>
        private static void EnsureGatherController(Scene scene)
        {
            var interactor = FindComponentInScene<FactoryCentralInteractor>(
                scene);
            var camera = interactor != null
                ? interactor.GetComponentInChildren<Camera>(true)
                : null;
            if (camera == null)
            {
                camera = FindComponentInScene<ManualMiningController>(scene)
                    ?.GetComponent<Camera>();
            }

            if (camera == null)
            {
                throw new InvalidOperationException(
                    "The player camera was not found, so hand gathering would " +
                    "have no owner. The plants are placed but unusable.");
            }

            var controller = camera.GetComponent<HandGatherController>();
            if (controller == null)
            {
                controller = camera.gameObject
                    .AddComponent<HandGatherController>();
            }

            EditorUtility.SetDirty(controller);
        }

        private static Vector3 TerrainNormal(Terrain terrain, Vector3 world)
        {
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            return data.GetInterpolatedNormal(
                Mathf.Clamp01((world.x - origin.x) / data.size.x),
                Mathf.Clamp01((world.z - origin.z) / data.size.z));
        }

        private static bool IsSurface(
            bool[,] holes,
            float normalizedX,
            float normalizedZ)
        {
            var x = Mathf.Clamp(
                Mathf.FloorToInt(normalizedX * holes.GetLength(1)),
                0,
                holes.GetLength(1) - 1);
            var z = Mathf.Clamp(
                Mathf.FloorToInt(normalizedZ * holes.GetLength(0)),
                0,
                holes.GetLength(0) - 1);
            return holes[z, x];
        }

        private static bool TryResolveGround(
            Terrain terrain,
            bool[,] holes,
            Vector3 candidate,
            out Vector3 position)
        {
            position = default;
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            var normalizedX = (candidate.x - origin.x) / data.size.x;
            var normalizedZ = (candidate.z - origin.z) / data.size.z;
            if (normalizedX < 0.01f || normalizedX > 0.99f
                || normalizedZ < 0.01f || normalizedZ > 0.99f)
            {
                return false;
            }

            // The island is a floating shape cut by terrain holes: a plant
            // placed over one would hang in the sky above the underbody.
            // GetHoles stores true for solid surface, which is the convention
            // StarterIslandAdaptiveGrassInstaller already relies on.
            if (!IsSurface(holes, normalizedX, normalizedZ))
            {
                return false;
            }

            if (data.GetSteepness(normalizedX, normalizedZ) > MaximumSteepness)
            {
                return false;
            }

            var height = terrain.SampleHeight(
                new Vector3(candidate.x, 0f, candidate.z)) + origin.y;
            position = new Vector3(candidate.x, height, candidate.z);
            return true;
        }

        private static GameObject RequireAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Missing prefab at {path}. Run " +
                    "CML/Art/Rebuild Fiber Plant Kit first.");
            }

            return asset;
        }

        private static Transform RequireMarker(Transform root, string name)
        {
            var marker = FindTransform(root, name);
            if (marker == null)
            {
                throw new InvalidOperationException(
                    $"Starter Island marker '{name}' is missing.");
            }

            return marker;
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindTransform(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static T FindComponentInScene<T>(Scene scene)
            where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

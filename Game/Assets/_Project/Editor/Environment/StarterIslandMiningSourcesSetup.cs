using System;
using System.Collections.Generic;
using CML.Unity.Mining;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Installs the authored MINE-003 sources in the Starter Island scene.
    /// Environmental stones reuse only the Starter Island rock kit. The Iron
    /// Deposit exclusively reuses the approved ore-deposit kit and classifies
    /// its raised rocks separately from its bounded G-series extraction
    /// plates. This destructive builder is explicit-menu-only: opening the
    /// scene or leaving Play Mode must never rebuild and save it implicitly.
    /// </summary>
    public static class StarterIslandMiningSourcesSetup
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";

        private const string SourcesRootName =
            "GAMEPLAY_ManualMiningSources";
        private const string IronDepositPrefabPath =
            "Assets/_Project/Art/Environment/OreDeposit/Prefabs/" +
            "PF_OreDeposit_Iron_A.prefab";
        private const string RockModelsRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Rocks/Models/";
        private const string RockMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_DetailRock.mat";
        private const string IronAnchorName = "REF_DepositAnchor_Iron";

        private static readonly EnvironmentalStoneDefinition[] Stones =
        {
            new EnvironmentalStoneDefinition(
                "ENV_Rock_BoulderMedium_A.fbx",
                new Vector2(-246.0f, -164.0f),
                18f,
                0.82f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_BoulderSmall_A.fbx",
                new Vector2(-241.7f, -157.8f),
                128f,
                0.58f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_BoulderMedium_B.fbx",
                new Vector2(-234.3f, -151.2f),
                256f,
                0.76f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_BoulderSmall_B.fbx",
                new Vector2(-226.5f, -157.0f),
                44f,
                0.52f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_ShoreFlat_A.fbx",
                new Vector2(-219.2f, -148.5f),
                302f,
                0.46f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_BoulderMedium_A.fbx",
                new Vector2(-211.5f, -139.8f),
                172f,
                0.88f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_BoulderSmall_A.fbx",
                new Vector2(-203.8f, -147.0f),
                78f,
                0.56f),
            new EnvironmentalStoneDefinition(
                "ENV_Rock_ShoreFlat_B.fbx",
                new Vector2(-196.0f, -137.6f),
                218f,
                0.44f)
        };

        [MenuItem("CML/Gameplay/Rebuild Starter Island Mining Sources")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before rebuilding Starter Island mining " +
                    "sources.");
            }

            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            var terrain = FindComponentInScene<Terrain>(scene);
            if (terrain == null)
            {
                throw new InvalidOperationException(
                    "Starter Island does not contain a Unity Terrain.");
            }

            BuildIntoScene(scene, terrain, replaceExisting: true);
            SaveScene(scene);
        }

        public static void BuildIntoScene(
            Scene scene,
            Terrain terrain,
            bool replaceExisting)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException(
                    "The target scene must be valid and loaded.",
                    nameof(scene));
            }

            if (terrain == null || terrain.gameObject.scene != scene)
            {
                throw new ArgumentException(
                    "The target Terrain must belong to the target scene.",
                    nameof(terrain));
            }

            var previous = FindGameObject(scene, SourcesRootName);
            if (previous != null)
            {
                if (!replaceExisting)
                {
                    return;
                }

                UnityEngine.Object.DestroyImmediate(previous);
            }

            var sourcesRoot = new GameObject(SourcesRootName);
            SceneManager.MoveGameObjectToScene(sourcesRoot, scene);

            var existingRockCount =
                ConfigureExistingEnvironmentalRocks(scene);
            BuildEnvironmentalStones(
                scene,
                terrain,
                sourcesRoot.transform);
            BuildIronDeposit(
                scene,
                terrain,
                sourcesRoot.transform);

            EditorUtility.SetDirty(sourcesRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                "STARTER_ISLAND_MINING_SOURCES status=PASS " +
                $"starterStones={Stones.Length} " +
                $"existingRocks={existingRockCount} ironDeposit=1 " +
                "targets=environmental-stone,ore-rock,deposit-surface," +
                "normal-terrain rewards=OUT_OF_SCOPE");
        }

        private static int ConfigureExistingEnvironmentalRocks(Scene scene)
        {
            var rocksRoot = FindGameObject(scene, "RocksRoot");
            if (rocksRoot == null)
            {
                throw new InvalidOperationException(
                    "Starter Island is missing its authored RocksRoot.");
            }

            var configured = 0;
            foreach (var child in
                     rocksRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!TryGetAuthoredStoneSourceId(
                        child.name,
                        out var sourceId))
                {
                    continue;
                }

                EnsureMeshColliders(child.gameObject);
                var identity =
                    child.GetComponent<ManualMiningSourceIdentity>();
                if (identity == null)
                {
                    identity =
                        child.gameObject.AddComponent<
                            ManualMiningSourceIdentity>();
                }

                identity.Configure(
                    ManualMiningSourceKind.EnvironmentalStone,
                    sourceId);
                identity.EnsureMiningMeshColliders();
                EditorUtility.SetDirty(identity);
                configured++;
            }

            if (configured == 0)
            {
                throw new InvalidOperationException(
                    "Starter Island contains no authored environmental rocks " +
                    "to configure for manual mining.");
            }

            return configured;
        }

        private static void BuildEnvironmentalStones(
            Scene scene,
            Terrain terrain,
            Transform parent)
        {
            var material =
                AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);
            if (material == null)
            {
                throw new InvalidOperationException(
                    $"Starter Island rock material is missing: " +
                    RockMaterialPath);
            }

            var cluster = new GameObject("MINE_EnvironmentalStones_Finite");
            SceneManager.MoveGameObjectToScene(cluster, scene);
            cluster.transform.SetParent(parent, false);

            for (var index = 0; index < Stones.Length; index++)
            {
                var definition = Stones[index];
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                    RockModelsRoot + definition.ModelName);
                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"Environmental stone model is missing: " +
                        $"{RockModelsRoot}{definition.ModelName}");
                }

                var instance = PrefabUtility.InstantiatePrefab(source, scene)
                    as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate environmental stone " +
                        $"{definition.ModelName}.");
                }

                instance.name = $"MINE_EnvironmentalStone_{index + 1:00}";
                instance.transform.SetParent(cluster.transform, true);
                instance.transform.SetPositionAndRotation(
                    new Vector3(
                        definition.Position.x,
                        0f,
                        definition.Position.y),
                    Quaternion.Euler(0f, definition.Yaw, 0f));
                SetVisualHeight(instance, definition.Height);
                PlaceOnTerrain(terrain, instance);
                ApplyMaterial(instance, material);
                EnsureMeshColliders(instance);

                var identity =
                    instance.GetComponent<ManualMiningSourceIdentity>();
                if (identity == null)
                {
                    identity =
                        instance.AddComponent<ManualMiningSourceIdentity>();
                }

                identity.Configure(
                    ManualMiningSourceKind.EnvironmentalStone,
                    $"starter-island.environmental-stone.{index + 1:00}");
                identity.EnsureMiningMeshColliders();
                EditorUtility.SetDirty(identity);
            }
        }

        private static void BuildIronDeposit(
            Scene scene,
            Terrain terrain,
            Transform parent)
        {
            var source =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    IronDepositPrefabPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Approved Iron Deposit prefab is missing: " +
                    IronDepositPrefabPath);
            }

            var anchor = FindTransform(
                terrain.transform.root,
                IronAnchorName);
            if (anchor == null)
            {
                throw new InvalidOperationException(
                    $"Starter Island marker '{IronAnchorName}' is missing.");
            }

            var instance = PrefabUtility.InstantiatePrefab(source, scene)
                as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the approved Iron Deposit.");
            }

            instance.name = "MINE_IronDeposit";
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(
                anchor.position + Vector3.up * 0.04f,
                Quaternion.Euler(0f, 18f, 0f));

            var modules = new List<Transform>();
            foreach (var child in
                     instance.GetComponentsInChildren<Transform>(true))
            {
                if (child != instance.transform &&
                    child.name.StartsWith(
                        "MOD_",
                        StringComparison.Ordinal))
                {
                    modules.Add(child);
                }
            }

            var rockIndex = 0;
            var surfaceIndex = 0;
            for (var index = 0; index < modules.Count; index++)
            {
                var module = modules[index];
                var isSurface =
                    module.name.IndexOf(
                        "_G0",
                        StringComparison.Ordinal) >= 0;
                var identity =
                    module.GetComponent<ManualMiningSourceIdentity>();
                if (identity == null)
                {
                    identity =
                        module.gameObject.AddComponent<
                            ManualMiningSourceIdentity>();
                }

                if (isSurface)
                {
                    surfaceIndex++;
                    identity.Configure(
                        ManualMiningSourceKind.IronDepositSurface,
                        $"starter-island.iron-deposit.surface." +
                        $"{surfaceIndex:00}");
                }
                else
                {
                    rockIndex++;
                    identity.Configure(
                        ManualMiningSourceKind.IronOreRock,
                        $"starter-island.iron-deposit.rock.{rockIndex:00}");
                    identity.EnsureMiningMeshColliders();
                }

                EditorUtility.SetDirty(identity);
            }

            if (rockIndex == 0 || surfaceIndex == 0)
            {
                throw new InvalidOperationException(
                    "The approved Iron Deposit must contain both finite ore " +
                    "rocks and bounded G-series extraction surfaces.");
            }

            PlaceOnTerrain(terrain, instance);
            BuildInfiniteExtractionSurface(instance, ref surfaceIndex);
        }

        private static void BuildInfiniteExtractionSurface(
            GameObject deposit,
            ref int surfaceIndex)
        {
            surfaceIndex++;
            var identity =
                ManualMiningSourceIdentity.EnsureInfiniteDepositSurface(
                    deposit,
                    "starter-island.iron-deposit.surface.ground");
            EditorUtility.SetDirty(identity);
        }

        private static void SetVisualHeight(
            GameObject instance,
            float targetHeight)
        {
            if (!TryGetRendererBounds(instance, out var bounds) ||
                bounds.size.y <= 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Mining source '{instance.name}' has no renderable " +
                    "geometry.");
            }

            instance.transform.localScale *= targetHeight / bounds.size.y;
        }

        private static void PlaceOnTerrain(
            Terrain terrain,
            GameObject instance)
        {
            if (!TryGetRendererBounds(instance, out var bounds))
            {
                return;
            }

            var position = instance.transform.position;
            var terrainY =
                terrain.SampleHeight(
                    new Vector3(position.x, 0f, position.z)) +
                terrain.transform.position.y;
            instance.transform.position +=
                Vector3.up * (terrainY - bounds.min.y + 0.015f);
        }

        private static bool TryGetRendererBounds(
            GameObject root,
            out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return true;
        }

        private static void ApplyMaterial(
            GameObject instance,
            Material material)
        {
            foreach (var renderer in
                     instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials =
                    new Material[renderer.sharedMaterials.Length];
                for (var index = 0; index < materials.Length; index++)
                {
                    materials[index] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static void EnsureMeshColliders(GameObject instance)
        {
            foreach (var filter in
                     instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null)
                {
                    collider = filter.gameObject.AddComponent<MeshCollider>();
                }

                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                collider.isTrigger = false;
            }
        }

        private static bool TryGetAuthoredStoneSourceId(
            string objectName,
            out string sourceId)
        {
            return ManualMiningSourceIdentity.
                TryGetAuthoredEnvironmentalStoneSourceId(
                    objectName,
                    out sourceId);
        }

        private static void SaveScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    $"Could not save Starter Island scene: {ScenePath}");
            }

            AssetDatabase.SaveAssets();
        }

        private static T FindComponentInScene<T>(Scene scene)
            where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject FindGameObject(
            Scene scene,
            string objectName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindTransform(root.transform, objectName);
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static Transform FindTransform(
            Transform root,
            string objectName)
        {
            if (root.name == objectName)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindTransform(
                    root.GetChild(index),
                    objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private readonly struct EnvironmentalStoneDefinition
        {
            public EnvironmentalStoneDefinition(
                string modelName,
                Vector2 position,
                float yaw,
                float height)
            {
                ModelName = modelName;
                Position = position;
                Yaw = yaw;
                Height = height;
            }

            public string ModelName { get; }

            public Vector2 Position { get; }

            public float Yaw { get; }

            public float Height { get; }
        }
    }
}

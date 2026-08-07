using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CML.Editor.EnvironmentAssets
{
    /// <summary>
    /// Builds the item-sized models that ride the belts.
    ///
    /// Until the drill existed, only ingots and plates ever travelled, and those
    /// two had serialized references in the scene. Everything else — ore, stone,
    /// logs — moved invisibly. These prefabs close that gap.
    ///
    /// Nothing new is modelled. Ore reuses the deposit kit's own small rubble
    /// module, which is exactly what a chunk broken off a deposit looks like;
    /// stone reuses the boulder its inventory icon is already rendered from; the
    /// log is copied whole from its authored item prefab, because that one
    /// carries two materials, bark and cut ends, that rebuilding from a single
    /// mesh would throw away.
    ///
    /// They live under Resources because the belt presenter resolves item
    /// visuals at runtime and has no scene reference to bind them to — the same
    /// reason the drill prefab lives there.
    /// </summary>
    public static class CarriedItemAssetSetup
    {
        private const string PrefabRoot = "Assets/_Project/Resources/Items";

        private const string OreModelPath =
            "Assets/_Project/Art/Environment/OreDeposit/Models/" +
            "ENV_Ore_R02_SmallRubble.fbx";
        private const string OreMaterialRoot =
            "Assets/_Project/Art/Environment/OreDeposit/Materials";

        private const string StoneModelPath =
            "Assets/_Project/Art/Environment/StarterIsland/Rocks/" +
            "Models/ENV_Rock_BoulderSmall_A.fbx";
        private const string StoneMaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/" +
            "Materials/M_StarterIsland_DetailRock.mat";

        private const string LogPrefabPath =
            "Assets/_Project/Art/ManualEra/Wood/Prefabs/PF_Item_WoodLog.prefab";

        /// <summary>
        /// Widest dimension a chunk should read at on a belt. An iron ingot sits
        /// around here, and both source meshes are authored for the ground, so
        /// they come down a long way.
        /// </summary>
        private const float ItemWidthMetres = 0.20f;

        private static readonly BuiltItem[] BuiltItems =
        {
            new BuiltItem("RawIron", OreModelPath, OreMaterialRoot + "/M_OreDeposit_Iron.mat"),
            new BuiltItem("RawCopper", OreModelPath, OreMaterialRoot + "/M_OreDeposit_Copper.mat"),
            new BuiltItem("RawTin", OreModelPath, OreMaterialRoot + "/M_OreDeposit_Tin.mat"),
            new BuiltItem("Stone", StoneModelPath, StoneMaterialPath)
        };

        [MenuItem("CML/Art/Rebuild Carried Item Assets")]
        public static void Run()
        {
            EnsureFolder(PrefabRoot);

            foreach (var item in BuiltItems)
            {
                BuildFromMesh(item);
            }

            CopyAuthoredPrefab("WoodLog", LogPrefabPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"CARRIED_ITEMS_READY built={BuiltItems.Length} copied=1 status=PASS");
        }

        private static void BuildFromMesh(BuiltItem item)
        {
            var source = RequireAsset<GameObject>(item.ModelPath);
            var mesh = FindMesh(source);
            if (mesh == null)
            {
                throw new InvalidOperationException(
                    $"{item.ModelPath} carries no mesh to reuse.");
            }

            var material = RequireAsset<Material>(item.MaterialPath);
            var instance = new GameObject("PF_" + item.ItemName);
            try
            {
                instance.transform.localScale = Vector3.one * UniformScale(mesh);
                instance.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = instance.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                // No collider on purpose: an item on a belt is presentation
                // only. One would fight the placement overlap test and the
                // player capsule.
                Save(instance, item.ItemName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Copies an already authored item prefab into Resources, keeping every
        /// material and the authored scale. The copy goes stale if the original
        /// is rebuilt, which is why re-running this is the way to refresh it.
        /// </summary>
        private static void CopyAuthoredPrefab(string itemName, string sourcePath)
        {
            var source = RequireAsset<GameObject>(sourcePath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(collider);
                }

                Save(instance, itemName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void Save(GameObject instance, string itemName)
        {
            instance.name = "PF_" + itemName;
            var path = $"{PrefabRoot}/PF_{itemName}.prefab";
            if (PrefabUtility.SaveAsPrefabAsset(instance, path) == null)
            {
                throw new InvalidOperationException($"Could not save {path}.");
            }
        }

        private static float UniformScale(Mesh mesh)
        {
            var size = mesh.bounds.size;
            var widest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            return widest <= 0.0001f ? 1f : ItemWidthMetres / widest;
        }

        private static Mesh FindMesh(GameObject source)
        {
            var filters = source.GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < filters.Length; index++)
            {
                if (filters[index].sharedMesh != null)
                {
                    return filters[index].sharedMesh;
                }
            }

            return null;
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Missing required asset: {path}", path);
            }

            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            var segments = folder.Replace('\\', '/').Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private readonly struct BuiltItem
        {
            public BuiltItem(string itemName, string modelPath, string materialPath)
            {
                ItemName = itemName;
                ModelPath = modelPath;
                MaterialPath = materialPath;
            }

            public string ItemName { get; }

            public string ModelPath { get; }

            public string MaterialPath { get; }
        }
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.Art
{
    /// <summary>
    /// Imports the authored leaf-built bushes from SourceArt and turns their
    /// clean FBX roots into Unity prefabs using the proven V4 tree materials.
    /// The bushes therefore share the tree atlases, wind, alpha clipping and
    /// shadow passes instead of looking like solid faceted spheres.
    /// </summary>
    public static class StarterIslandLeafyBushSetup
    {
        public const string Root =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Bushes";
        public const string ModelsRoot = Root + "/Models";
        public const string PrefabsRoot = Root + "/Prefabs";

        private const string TreeMaterialsRoot =
            "Assets/_Project/Art/Environment/StarterIsland/V4/" +
            "Trees/Materials";

        private static readonly BushDefinition[] Definitions =
        {
            new BushDefinition(
                "CLU_Bush_Small_A",
                "M_ENV_Tree_CommonTall_A_Leaves.mat"),
            new BushDefinition(
                "CLU_Bush_Medium_A",
                "M_ENV_Tree_CommonTall_A_Leaves.mat"),
            new BushDefinition(
                "CLU_Bush_Wide_A",
                "M_ENV_Tree_CommonTall_A_Leaves.mat"),
            new BushDefinition(
                "CLU_Bush_Autumn_A",
                "M_ENV_Tree_Autumn_A_Leaves_Orange.mat"),
            new BushDefinition(
                "CLU_Bush_Amber_A",
                "M_ENV_Tree_Autumn_A_Leaves_Amber.mat")
        };

        [MenuItem("CML/Art/Rebuild Starter Island Leafy Bushes")]
        public static void Run()
        {
            EnsureFolder(Root);
            EnsureFolder(ModelsRoot);
            EnsureFolder(PrefabsRoot);

            var bark = LoadMaterial(
                TreeMaterialsRoot +
                "/M_ENV_Tree_CommonTall_A_Bark.mat");
            for (var index = 0; index < Definitions.Length; index++)
            {
                var definition = Definitions[index];
                var leaves = LoadMaterial(
                    TreeMaterialsRoot + "/" +
                    definition.LeafMaterialName);
                BuildPrefab(definition.Name, leaves, bark);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "STARTER_ISLAND_LEAFY_BUSHES prefabs=5 " +
                "greenVariants=3 autumnVariants=2 colliders=0 " +
                "status=PASS");
        }

        private static void BuildPrefab(
            string assetName,
            Material leaves,
            Material bark)
        {
            var modelPath = ModelsRoot + "/" + assetName + ".fbx";
            var model =
                AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new FileNotFoundException(
                    $"Leafy bush FBX is missing: {modelPath}");
            }

            var instance = UnityEngine.Object.Instantiate(model);
            instance.name = "PF_" + assetName;
            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                var colliders =
                    instance.GetComponentsInChildren<Collider>(true);
                for (var index = 0; index < colliders.Length; index++)
                {
                    UnityEngine.Object.DestroyImmediate(colliders[index]);
                }

                var renderers =
                    instance.GetComponentsInChildren<MeshRenderer>(true);
                var leafRendererCount = 0;
                var twigRendererCount = 0;
                for (var index = 0; index < renderers.Length; index++)
                {
                    var renderer = renderers[index];
                    var isLeaf =
                        renderer.name.IndexOf(
                            "Leaves",
                            StringComparison.OrdinalIgnoreCase) >= 0;
                    renderer.sharedMaterial = isLeaf ? leaves : bark;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage =
                        ReflectionProbeUsage.BlendProbes;
                    renderer.allowOcclusionWhenDynamic = true;
                    if (isLeaf)
                    {
                        leafRendererCount++;
                    }
                    else
                    {
                        twigRendererCount++;
                    }
                }

                if (leafRendererCount == 0 || twigRendererCount == 0)
                {
                    throw new InvalidOperationException(
                        $"{assetName} must contain both Leaves and Twigs " +
                        "MeshRenderers.");
                }

                var prefabPath =
                    PrefabsRoot + "/PF_" + assetName + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(
                    instance,
                    prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save leafy bush prefab: {prefabPath}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                throw new FileNotFoundException(
                    $"Leafy bush material is missing: {path}");
            }

            return material;
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

        private readonly struct BushDefinition
        {
            public BushDefinition(
                string name,
                string leafMaterialName)
            {
                Name = name;
                LeafMaterialName = leafMaterialName;
            }

            public string Name { get; }
            public string LeafMaterialName { get; }
        }
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Editor.EnvironmentAssets
{
    internal static class StarterIslandRockPrefabBuilder
    {
        private const string RockRoot =
            "Assets/_Project/Art/Environment/StarterIsland/Rocks";
        private const string ModelRoot = RockRoot + "/Models";
        private const string PrefabRoot = RockRoot + "/Prefabs";
        private const string MaterialPath =
            "Assets/_Project/Art/Environment/StarterIsland/Terrain/Materials/" +
            "M_StarterIsland_DetailRock.mat";
        private const string BuildMarkerPath =
            RockRoot + "/BUILD_ROCK_PREFABS.pending";

        private static readonly string[] ModelNames =
        {
            "ENV_Rock_BoulderLarge_A",
            "ENV_Rock_BoulderMedium_A",
            "ENV_Rock_BoulderMedium_B",
            "ENV_Rock_BoulderSmall_A",
            "ENV_Rock_BoulderSmall_B",
            "ENV_Rock_ShoreFlat_A",
            "ENV_Rock_ShoreFlat_B"
        };

        [InitializeOnLoadMethod]
        private static void BuildPendingRequestAfterReload()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string markerAbsolutePath = Path.Combine(
                projectRoot,
                BuildMarkerPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(markerAbsolutePath))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    BuildPrefabs();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    AssetDatabase.DeleteAsset(BuildMarkerPath);
                }
            };
        }

        [MenuItem("CML/Environment/Build Starter Island Rock Prefabs")]
        public static void BuildPrefabs()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsureFolder(PrefabRoot);

            Material rockMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (rockMaterial == null || rockMaterial.shader == null)
            {
                throw new InvalidOperationException(
                    "Starter Island detail-rock material is missing.");
            }
            const string expectedShader =
                "CML/Environment/Starter Island Stylized Surface";
            if (rockMaterial.shader.name != expectedShader)
            {
                throw new InvalidOperationException(
                    "Unexpected rock shader: " + rockMaterial.shader.name);
            }

            foreach (string modelName in ModelNames)
            {
                BuildPrefab(modelName, rockMaterial);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidatePrefabs(rockMaterial);
            Debug.Log(
                "Built and validated " + ModelNames.Length +
                " Starter Island rock prefabs in " + PrefabRoot +
                " using shader " + expectedShader + ".");
        }

        private static void BuildPrefab(string modelName, Material rockMaterial)
        {
            string modelPath = ModelRoot + "/" + modelName + ".fbx";
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException(
                    "Rock model is missing: " + modelPath);
            }

            string prefabName = "PF_" + modelName;
            string prefabPath = PrefabRoot + "/" + prefabName + ".prefab";
            GameObject root = new GameObject(prefabName);
            try
            {
                GameObject modelInstance =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate model: " + modelPath);
                }

                modelInstance.name = modelName;
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                Renderer[] renderers =
                    modelInstance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Rock model has no renderer: " + modelPath);
                }
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    if (materials.Length == 0)
                    {
                        materials = new[] { rockMaterial };
                    }
                    else
                    {
                        for (int index = 0; index < materials.Length; index++)
                        {
                            materials[index] = rockMaterial;
                        }
                    }
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                MeshFilter[] filters =
                    modelInstance.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter filter in filters)
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }
                    MeshCollider collider =
                        filter.GetComponent<MeshCollider>();
                    if (collider == null)
                    {
                        collider = filter.gameObject.AddComponent<MeshCollider>();
                    }
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ValidatePrefabs(Material rockMaterial)
        {
            foreach (string modelName in ModelNames)
            {
                string prefabPath =
                    PrefabRoot + "/PF_" + modelName + ".prefab";
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Rock prefab was not created: " + prefabPath);
                }

                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                MeshCollider[] colliders =
                    prefab.GetComponentsInChildren<MeshCollider>(true);
                if (renderers.Length == 0 || colliders.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Rock prefab lacks renderer or collider: " + prefabPath);
                }
                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != rockMaterial)
                        {
                            throw new InvalidOperationException(
                                "Rock prefab has the wrong material: " + prefabPath);
                        }
                    }
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                throw new InvalidOperationException(
                    "Invalid Unity folder path: " + path);
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

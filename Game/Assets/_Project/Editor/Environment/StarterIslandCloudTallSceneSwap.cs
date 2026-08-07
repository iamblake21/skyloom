using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Replaces the placed V4 trees in the Starter Island review scene with the
    /// CloudTall reference-match trees.
    ///
    /// The swap goes through PrefabUtility rather than rewriting the scene file:
    /// a scene's prefab instance overrides point at file ids inside the source
    /// prefab, so repointing the GUID by hand would leave every transform
    /// override dangling and drop the whole forest on the origin.
    /// </summary>
    public static class StarterIslandCloudTallSceneSwap
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";

        private const string OldSummerPrefab =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Trees/Prefabs" +
            "/PF_ENV_Tree_CommonTall_A_LOD0.prefab";
        private const string OldAutumnPrefab =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Trees/Prefabs" +
            "/PF_ENV_Tree_Autumn_A_LOD0.prefab";

        /// <summary>
        /// The trees being replaced stand 13.25 m tall against the CloudTall
        /// 10.0 m, so every instance scale is compensated to keep the canopy
        /// line of the island exactly where the scene was composed.
        /// </summary>
        private const float ScaleCompensation = 13.25f / 10.0f;

        [MenuItem("CML/Art/CloudTall Trees/Swap Scene Trees")]
        public static void Run()
        {
            var summerPrefabs = LoadVariants(string.Empty);
            var autumnPrefabs = LoadVariants("_Autumn");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Could not open {ScenePath}.");
            }

            var summerTargets = CollectInstances(scene, OldSummerPrefab);
            var autumnTargets = CollectInstances(scene, OldAutumnPrefab);
            Debug.Log(
                $"CLOUD_TALL_SWAP_FOUND summer={summerTargets.Count} " +
                $"autumn={autumnTargets.Count}");

            if (summerTargets.Count == 0 && autumnTargets.Count == 0)
            {
                throw new InvalidOperationException(
                    "No V4 tree instances were found; the scene may already " +
                    "have been swapped.");
            }

            var swapped = 0;
            swapped += Replace(summerTargets, summerPrefabs);
            swapped += Replace(autumnTargets, autumnPrefabs);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var remainingSummer = CollectInstances(scene, OldSummerPrefab).Count;
            var remainingAutumn = CollectInstances(scene, OldAutumnPrefab).Count;
            if (remainingSummer != 0 || remainingAutumn != 0)
            {
                throw new InvalidOperationException(
                    $"{remainingSummer + remainingAutumn} old tree instances " +
                    "survived the swap.");
            }

            var planted = 0;
            foreach (var prefabs in new[] { summerPrefabs, autumnPrefabs })
            {
                foreach (var prefab in prefabs)
                {
                    planted += CollectInstances(
                        scene,
                        AssetDatabase.GetAssetPath(prefab)).Count;
                }
            }

            Debug.Log(
                $"CLOUD_TALL_SWAP swapped={swapped} planted={planted} " +
                $"scaleCompensation={ScaleCompensation:F3} " +
                $"oldRemaining=0 status=PASS");
        }

        private static GameObject[] LoadVariants(string seasonSuffix)
        {
            var variants = StarterIslandCloudTallTreeSetup.Variants;
            var prefabs = new GameObject[variants.Length];
            for (var index = 0; index < variants.Length; index++)
            {
                var path = StarterIslandCloudTallTreeSetup.PrefabPath(
                    variants[index],
                    seasonSuffix);
                prefabs[index] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabs[index] == null)
                {
                    throw new InvalidOperationException(
                        $"Required CloudTall prefab is missing: {path}");
                }
            }

            return prefabs;
        }

        private static List<GameObject> CollectInstances(Scene scene, string prefabPath)
        {
            var found = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var candidate = transform.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
                    {
                        continue;
                    }

                    var source =
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate);
                    if (string.Equals(source, prefabPath, StringComparison.Ordinal))
                    {
                        found.Add(candidate);
                    }
                }
            }

            return found;
        }

        private static int Replace(
            IReadOnlyList<GameObject> targets,
            IReadOnlyList<GameObject> prefabs)
        {
            var replaced = 0;
            foreach (var old in targets)
            {
                var oldTransform = old.transform;
                var parent = oldTransform.parent;
                var siblingIndex = oldTransform.GetSiblingIndex();
                var localPosition = oldTransform.localPosition;
                var localRotation = oldTransform.localRotation;
                var localScale = oldTransform.localScale;
                var name = old.name;
                var layer = old.layer;
                var tag = old.tag;
                var staticFlags = GameObjectUtility.GetStaticEditorFlags(old);
                var active = old.activeSelf;

                // Stable per-name pick so a rerun lands the same shape on the
                // same tree and a stand never reads as one cloned model.
                var prefab = prefabs[StableIndex(name, prefabs.Count)];
                if (PrefabUtility.InstantiatePrefab(prefab, parent) is not GameObject
                    instance)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate {prefab.name} for {name}.");
                }

                var instanceTransform = instance.transform;
                instanceTransform.localPosition = localPosition;
                instanceTransform.localRotation = localRotation;
                instanceTransform.localScale = localScale * ScaleCompensation;
                instanceTransform.SetSiblingIndex(siblingIndex);
                instance.name = name;
                instance.layer = layer;
                instance.tag = tag;
                instance.SetActive(active);
                GameObjectUtility.SetStaticEditorFlags(instance, staticFlags);
                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = layer;
                }

                UnityEngine.Object.DestroyImmediate(old);
                replaced++;
            }

            return replaced;
        }

        private static int StableIndex(string value, int count)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var character in value)
                {
                    hash = (hash ^ character) * 16777619u;
                }

                return (int)(hash % (uint)count);
            }
        }
    }
}

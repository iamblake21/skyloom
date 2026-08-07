using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Replaces the placed CLU_Bush shrubs in the Starter Island review scene
    /// with the CloudBush reference-match shrubs, and nothing else.
    ///
    /// The swap goes through PrefabUtility rather than rewriting the scene file:
    /// a scene's prefab instance overrides point at file ids inside the source
    /// prefab, so repointing the GUID by hand would leave every transform
    /// override dangling and drop the whole clutter layer on the origin.
    /// </summary>
    public static class StarterIslandCloudBushSceneSwap
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Terrain_Review.unity";

        private const string OldPrefabRoot =
            "Assets/_Project/Art/Environment/StarterIsland/V4/Bushes/Prefabs";

        /// <summary>
        /// One row per shrub class being replaced.
        ///
        /// The compensation is the authored height of the old shrub over the new
        /// one, so every instance keeps the size it was placed at. The CloudBush
        /// shapes are proportionally wider than the old ones, so matching height
        /// does mean each shrub covers a little more ground than before.
        /// </summary>
        private static readonly (string OldPrefab, string Size, string Season, float Compensation)[]
            Mapping =
            {
                ("PF_CLU_Bush_Small_A", "Small", "", 0.55f / 0.86f),
                ("PF_CLU_Bush_Medium_A", "Medium", "", 0.85f / 1.26f),
                ("PF_CLU_Bush_Wide_A", "Wide", "", 0.70f / 1.04f),
                ("PF_CLU_Bush_Autumn_A", "Medium", "_Autumn", 0.80f / 1.26f),
                ("PF_CLU_Bush_Amber_A", "Wide", "_Autumn", 0.80f / 1.04f),
            };

        [MenuItem("CML/Art/CloudBush Shrubs/Report Scene Shrubs")]
        public static void Report()
        {
            var scene = OpenTargetScene();
            var report = new StringBuilder("CLOUD_BUSH_SWAP_REPORT");
            var total = 0;
            foreach (var row in Mapping)
            {
                var found = CollectInstances(scene, $"{OldPrefabRoot}/{row.OldPrefab}.prefab");
                total += found.Count;
                report.Append(
                    $" {row.OldPrefab}={found.Count}->{row.Size}{row.Season}" +
                    $"@{row.Compensation:F3}");
            }

            report.Append($" total={total}");
            Debug.Log(report.ToString());
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "CloudBush",
                    $"{total} cespugli CLU_Bush trovati nella scena.\n\n" +
                    "Nessuna modifica applicata: questo comando solo conta.",
                    "Ok");
            }
        }

        [MenuItem("CML/Art/CloudBush Shrubs/Swap Scene Shrubs")]
        public static void Run()
        {
            if (!StarterIslandCloudBushSetup.PrefabsReady())
            {
                Debug.Log("CLOUD_BUSH_SWAP building the missing CloudBush prefabs first.");
                StarterIslandCloudBushSetup.Run();
            }

            var scene = OpenTargetScene();

            var targets = new List<(GameObject Instance, GameObject Prefab, float Compensation)>();
            var perClass = new StringBuilder();
            foreach (var row in Mapping)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    StarterIslandCloudBushSetup.PrefabPath(row.Size, row.Season));
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Missing CloudBush prefab for {row.Size}{row.Season}.");
                }

                var found = CollectInstances(scene, $"{OldPrefabRoot}/{row.OldPrefab}.prefab");
                foreach (var instance in found)
                {
                    targets.Add((instance, prefab, row.Compensation));
                }

                perClass.Append($"{row.OldPrefab}={found.Count} ");
            }

            if (targets.Count == 0)
            {
                Debug.Log("CLOUD_BUSH_SWAP nothing to do: no CLU_Bush instances found.");
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(
                        "CloudBush",
                        "Nessun cespuglio CLU_Bush in scena.\n" +
                        "Probabilmente lo scambio e' gia' stato fatto.",
                        "Ok");
                }

                return;
            }

            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                    "CloudBush",
                    $"Sostituisco {targets.Count} cespugli con i CloudBush.\n\n" +
                    $"{perClass}\n\n" +
                    "Posizione, rotazione, nome, layer e static flags restano; " +
                    "la scala viene compensata per mantenere l'altezza attuale.\n\n" +
                    "Alberi e resto della scena non vengono toccati.\n" +
                    "Una copia della scena viene salvata in Artifacts/Backups.",
                    "Sostituisci",
                    "Annulla"))
            {
                Debug.Log("CLOUD_BUSH_SWAP cancelled by the user.");
                return;
            }

            BackupScene();

            var swapped = 0;
            foreach (var (instance, prefab, compensation) in targets)
            {
                Replace(instance, prefab, compensation);
                swapped++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var remaining = 0;
            foreach (var row in Mapping)
            {
                remaining +=
                    CollectInstances(scene, $"{OldPrefabRoot}/{row.OldPrefab}.prefab").Count;
            }

            if (remaining != 0)
            {
                throw new InvalidOperationException(
                    $"{remaining} old shrub instances survived the swap.");
            }

            Debug.Log(
                $"CLOUD_BUSH_SWAP swapped={swapped} oldRemaining=0 status=PASS");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "CloudBush",
                    $"Fatto: {swapped} cespugli sostituiti.\nScena salvata.",
                    "Ok");
            }
        }

        private static Scene OpenTargetScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() &&
                string.Equals(active.path, ScenePath, StringComparison.Ordinal))
            {
                // Already the scene in front: work on it in place rather than
                // reopening, which would throw away whatever is unsaved.
                return active;
            }

            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new InvalidOperationException(
                    "The swap needs the Starter Island review scene open.");
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Could not open {ScenePath}.");
            }

            return scene;
        }

        private static void BackupScene()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot?.Parent == null)
            {
                return;
            }

            var backupDirectory = Path.Combine(
                projectRoot.Parent.FullName,
                "Artifacts",
                "Backups");
            Directory.CreateDirectory(backupDirectory);
            var source = Path.Combine(projectRoot.FullName, ScenePath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var destination = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(ScenePath)}.{stamp}.pre-cloudbush.unity.bak");
            File.Copy(source, destination, overwrite: true);
            Debug.Log($"CLOUD_BUSH_SWAP backup={destination}");
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

        private static void Replace(GameObject old, GameObject prefab, float compensation)
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

            if (PrefabUtility.InstantiatePrefab(prefab, parent) is not GameObject instance)
            {
                throw new InvalidOperationException(
                    $"Could not instantiate {prefab.name} for {name}.");
            }

            var instanceTransform = instance.transform;
            instanceTransform.localPosition = localPosition;
            instanceTransform.localRotation = localRotation;
            instanceTransform.localScale = localScale * compensation;
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
        }
    }
}

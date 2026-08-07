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
    /// Narrow, idempotent migration for the already-authored mineable rocks.
    /// It deliberately does not invoke any Starter Island builder: transforms,
    /// renderers, materials, shaders, terrain and prefab selection are outside
    /// this tool's write set.
    /// </summary>
    public static class StarterIslandRockMiningRepair
    {
        private const string MenuPath =
            "CML/Gameplay/Repair Starter Island Rock Mining";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before repairing rock mining.");
            }

            var scene = SceneManager.GetActiveScene();
            if (Application.isBatchMode)
            {
                scene = EditorSceneManager.OpenScene(
                    StarterIslandMiningSourcesSetup.ScenePath,
                    OpenSceneMode.Single);
            }
            else if (!scene.IsValid() ||
                     !scene.isLoaded ||
                     !string.Equals(
                         scene.path,
                         StarterIslandMiningSourcesSetup.ScenePath,
                         StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Open the canonical Starter Island Terrain Review scene " +
                    "before running this repair. No other scene is modified.");
            }

            var report = RepairScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(
                    scene,
                    StarterIslandMiningSourcesSetup.ScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save the repaired Starter Island scene.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                "STARTER_ISLAND_ROCK_MINING_REPAIR status=PASS " +
                $"finiteSources={report.FiniteSourceCount} " +
                $"authoredRocks={report.AuthoredRockCount} " +
                $"idsRepaired={report.RepairedIdCount} " +
                $"legacyBoxesRemoved={report.RemovedProxyCount} " +
                $"exactMeshColliders={report.ExactMeshColliderCount} " +
                "materialsChanged=0 shadersChanged=0 buildersInvoked=0");
        }

        internal static RepairReport RepairScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException(
                    "The target scene must be valid and loaded.",
                    nameof(scene));
            }

            var finiteSources = FindFiniteSources(scene);
            if (finiteSources.Count == 0)
            {
                throw new InvalidOperationException(
                    "Starter Island contains no finite mining sources.");
            }

            // Validate every future id before mutating even one scene object.
            var plannedIds = new Dictionary<
                ManualMiningSourceIdentity,
                string>();
            var uniqueIds = new HashSet<string>(
                StringComparer.Ordinal);
            var authoredRockCount = 0;
            for (var index = 0; index < finiteSources.Count; index++)
            {
                var source = finiteSources[index];
                string plannedId;
                if (ManualMiningSourceIdentity.
                    TryGetAuthoredEnvironmentalStoneSourceId(
                        source.name,
                        out var authoredId))
                {
                    authoredRockCount++;
                    plannedId = authoredId;
                }
                else
                {
                    plannedId = source.SourceId;
                }

                if (string.IsNullOrWhiteSpace(plannedId))
                {
                    throw new InvalidOperationException(
                        $"Finite mining source '{HierarchyPath(source.transform)}' " +
                        "has no recoverable stable id.");
                }

                if (!uniqueIds.Add(plannedId))
                {
                    throw new InvalidOperationException(
                        $"Duplicate mining source id '{plannedId}'.");
                }

                var filters =
                    source.GetComponentsInChildren<MeshFilter>(true);
                var hasMesh = false;
                for (var filterIndex = 0;
                     filterIndex < filters.Length;
                     filterIndex++)
                {
                    hasMesh |= IsOwnedMiningMeshFilter(
                        source,
                        filters[filterIndex]);
                }

                if (!hasMesh)
                {
                    throw new InvalidOperationException(
                        $"Finite mining source '{HierarchyPath(source.transform)}' " +
                        "has no mesh to use for exact collision.");
                }

                plannedIds.Add(source, plannedId);
            }

            var repairedIdCount = 0;
            var removedProxyCount = 0;
            var exactMeshColliderCount = 0;
            for (var index = 0; index < finiteSources.Count; index++)
            {
                var source = finiteSources[index];
                var plannedId = plannedIds[source];
                var isAuthoredRock =
                    ManualMiningSourceIdentity.
                        TryGetAuthoredEnvironmentalStoneSourceId(
                            source.name,
                            out _);
                var plannedKind = isAuthoredRock
                    ? ManualMiningSourceKind.EnvironmentalStone
                    : source.SourceKind;
                if (!string.Equals(
                        source.SourceId,
                        plannedId,
                        StringComparison.Ordinal) ||
                    source.SourceKind != plannedKind)
                {
                    Undo.RecordObject(
                        source,
                        "Repair mineable rock id");

                    source.Configure(plannedKind, plannedId);
                    repairedIdCount++;
                }

                var proxy = source.transform.Find(
                    ManualMiningSourceIdentity.MiningHitProxyName);
                if (proxy != null)
                {
                    Undo.RecordObject(
                        source.transform,
                        "Remove legacy mining hit proxy");
                    Undo.DestroyObjectImmediate(proxy.gameObject);

                    removedProxyCount++;
                }

                EnsureMiningMeshCollidersWithUndo(source);
                EditorUtility.SetDirty(source);
                RecordPrefabModification(source);
                RecordPrefabModification(source.transform);

                var filters =
                    source.GetComponentsInChildren<MeshFilter>(true);
                var ownedColliderTransforms =
                    CollectOwnedColliderTransforms(source, filters);
                for (var filterIndex = 0;
                     filterIndex < filters.Length;
                     filterIndex++)
                {
                    var filter = filters[filterIndex];
                    if (!IsOwnedMiningMeshFilter(source, filter))
                    {
                        continue;
                    }

                    var meshColliders =
                        filter.GetComponents<MeshCollider>();
                    var boxColliders =
                        filter.GetComponents<BoxCollider>();
                    var exact = meshColliders.Length == 1
                        ? meshColliders[0]
                        : null;
                    if (exact == null ||
                        exact.sharedMesh != filter.sharedMesh ||
                        !exact.enabled ||
                        exact.isTrigger ||
                        exact.convex ||
                        boxColliders.Length != 0)
                    {
                        throw new InvalidOperationException(
                            $"Rock mesh '{HierarchyPath(filter.transform)}' " +
                            "does not have exactly one enabled exact " +
                            "MeshCollider and zero BoxColliders.");
                    }

                    exactMeshColliderCount++;
                    EditorUtility.SetDirty(exact);
                    RecordPrefabModification(exact);
                }

                foreach (var owner in ownedColliderTransforms)
                {
                    if (IsOwnedMiningMeshFilter(
                            source,
                            owner.GetComponent<MeshFilter>()))
                    {
                        continue;
                    }

                    if (owner.GetComponents<MeshCollider>().Length != 0 ||
                        owner.GetComponents<BoxCollider>().Length != 0)
                    {
                        throw new InvalidOperationException(
                            $"Rock hierarchy node '{HierarchyPath(owner)}' " +
                            "still owns an extra BoxCollider or MeshCollider.");
                    }
                }
            }

            return new RepairReport(
                finiteSources.Count,
                authoredRockCount,
                repairedIdCount,
                removedProxyCount,
                exactMeshColliderCount);
        }

        private static List<ManualMiningSourceIdentity>
            FindFiniteSources(Scene scene)
        {
            var result = new List<ManualMiningSourceIdentity>();
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0;
                 rootIndex < roots.Length;
                 rootIndex++)
            {
                var sources = roots[rootIndex].GetComponentsInChildren<
                    ManualMiningSourceIdentity>(true);
                for (var sourceIndex = 0;
                     sourceIndex < sources.Length;
                     sourceIndex++)
                {
                    if (sources[sourceIndex] != null &&
                        sources[sourceIndex].IsFinite)
                    {
                        result.Add(sources[sourceIndex]);
                    }
                }
            }

            return result;
        }

        private static MeshCollider EnsureMiningMeshCollidersWithUndo(
            ManualMiningSourceIdentity source)
        {
            const string undoName = "Repair mineable rock colliders";
            var result = source.EnsureMiningMeshColliders(
                owner =>
                {
                    Undo.RecordObject(owner, undoName);
                    var collider =
                        Undo.AddComponent<MeshCollider>(owner);
                    EditorUtility.SetDirty(owner);
                    EditorUtility.SetDirty(collider);
                    return collider;
                },
                target =>
                {
                    if (target != null)
                    {
                        Undo.DestroyObjectImmediate(target);
                    }
                },
                target =>
                {
                    if (target != null)
                    {
                        Undo.RecordObject(target, undoName);
                    }
                });

            var filters =
                source.GetComponentsInChildren<MeshFilter>(true);
            var ownedColliderTransforms =
                CollectOwnedColliderTransforms(source, filters);
            foreach (var owner in ownedColliderTransforms)
            {
                EditorUtility.SetDirty(owner.gameObject);
                RecordPrefabModification(owner);
            }

            for (var index = 0; index < filters.Length; index++)
            {
                var filter = filters[index];
                if (!IsOwnedMiningMeshFilter(source, filter))
                {
                    continue;
                }

                EditorUtility.SetDirty(filter.gameObject);
                RecordPrefabModification(filter.transform);
                var colliders = filter.GetComponents<MeshCollider>();
                for (var colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    EditorUtility.SetDirty(colliders[colliderIndex]);
                    RecordPrefabModification(colliders[colliderIndex]);
                }
            }

            return result;
        }

        private static HashSet<Transform> CollectOwnedColliderTransforms(
            ManualMiningSourceIdentity source,
            MeshFilter[] filters)
        {
            var result = new HashSet<Transform>
            {
                source.transform
            };
            for (var index = 0; index < filters.Length; index++)
            {
                var filter = filters[index];
                if (!IsOwnedMiningMeshFilter(source, filter))
                {
                    continue;
                }

                for (var current = filter.transform;
                     current != null;
                     current = current.parent)
                {
                    result.Add(current);
                    if (current == source.transform)
                    {
                        break;
                    }
                }
            }

            return result;
        }

        private static bool IsOwnedMiningMeshFilter(
            ManualMiningSourceIdentity source,
            MeshFilter filter)
        {
            if (source == null ||
                filter == null ||
                filter.sharedMesh == null ||
                filter.GetComponentInParent<
                    ManualMiningSourceIdentity>() != source)
            {
                return false;
            }

            var body = filter.GetComponentInParent<Rigidbody>();
            return body == null || body.transform == source.transform;
        }

        private static void RecordPrefabModification(Component component)
        {
            if (component != null &&
                PrefabUtility.IsPartOfPrefabInstance(component))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    component);
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent;
                 parent != null;
                 parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        internal readonly struct RepairReport
        {
            public RepairReport(
                int finiteSourceCount,
                int authoredRockCount,
                int repairedIdCount,
                int removedProxyCount,
                int exactMeshColliderCount)
            {
                FiniteSourceCount = finiteSourceCount;
                AuthoredRockCount = authoredRockCount;
                RepairedIdCount = repairedIdCount;
                RemovedProxyCount = removedProxyCount;
                ExactMeshColliderCount = exactMeshColliderCount;
            }

            public int FiniteSourceCount { get; }
            public int AuthoredRockCount { get; }
            public int RepairedIdCount { get; }
            public int RemovedProxyCount { get; }
            public int ExactMeshColliderCount { get; }
        }
    }
}

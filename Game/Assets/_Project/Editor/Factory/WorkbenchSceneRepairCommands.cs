using System;
using CML.Foundation;
using CML.Unity.Factory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Editor.Factory
{
    /// <summary>
    /// Surgical repairs for the authored workbench already present in the open
    /// scene. This command never rebuilds a scene, replaces a prefab instance or
    /// saves over unrelated authoring work. Every mutation participates in Undo.
    /// </summary>
    public static class WorkbenchSceneRepairCommands
    {
        private const string WorkbenchName =
            "ENV_StarterProp_00_PF_Workbench";

        private static readonly StableId StarterWorkbenchId =
            new StableId(
                0x574F524B42454E43UL,
                0x485F535441525445UL);

        [MenuItem("CML/Gameplay/Repair Existing Workbench In Open Scene")]
        public static void RepairExistingWorkbench()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before repairing the authored workbench.");
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "There is no loaded active scene to repair.");
            }

            var workbench = FindExact(scene, WorkbenchName);
            if (workbench == null)
            {
                throw new InvalidOperationException(
                    $"Scene '{scene.name}' does not contain '{WorkbenchName}'.");
            }

            Undo.RegisterFullObjectHierarchyUndo(
                workbench,
                "Repair existing workbench interaction");

            var target = workbench.GetComponent<FactoryInteractionTarget>();
            if (target == null)
            {
                target = Undo.AddComponent<FactoryInteractionTarget>(workbench);
            }

            target.Configure(
                StarterWorkbenchId,
                FactoryInteractionKind.Workbench,
                "Usa Banco da lavoro");
            EditorUtility.SetDirty(target);

            var collider = workbench.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = Undo.AddComponent<BoxCollider>(workbench);
            }

            FitColliderToRenderedWorkbench(workbench.transform, collider);
            collider.enabled = true;
            collider.isTrigger = false;
            EditorUtility.SetDirty(collider);

            var authoredColliders =
                workbench.GetComponentsInChildren<Collider>(includeInactive: true);
            for (var index = 0; index < authoredColliders.Length; index++)
            {
                if (authoredColliders[index].enabled)
                {
                    continue;
                }

                Undo.RecordObject(
                    authoredColliders[index],
                    "Enable workbench collider");
                authoredColliders[index].enabled = true;
                EditorUtility.SetDirty(authoredColliders[index]);
            }

            EditorUtility.SetDirty(workbench);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = workbench;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log(
                "CML_WORKBENCH_REPAIRED: interaction target and collider aligned "
                + $"to '{workbench.name}' in scene '{scene.name}'. "
                + "The scene was marked dirty but was not saved or regenerated.");
        }

        [MenuItem(
            "CML/Gameplay/Repair Existing Workbench In Open Scene",
            true)]
        private static bool CanRepairExistingWorkbench()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            var scene = SceneManager.GetActiveScene();
            return scene.IsValid()
                && scene.isLoaded
                && FindExact(scene, WorkbenchName) != null;
        }

        private static void FitColliderToRenderedWorkbench(
            Transform root,
            BoxCollider collider)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(
                includeInactive: true);
            var hasBounds = false;
            var localBounds = default(Bounds);
            for (var index = 0; index < renderers.Length; index++)
            {
                var worldBounds = renderers[index].bounds;
                EncapsulateWorldBounds(
                    root,
                    worldBounds,
                    ref localBounds,
                    ref hasBounds);
            }

            if (!hasBounds)
            {
                throw new InvalidOperationException(
                    $"Workbench '{root.name}' has no Renderer to fit a collider to.");
            }

            var padding = new Vector3(0.08f, 0.06f, 0.08f);
            collider.center = localBounds.center;
            collider.size = Vector3.Max(
                localBounds.size + padding,
                new Vector3(0.25f, 0.25f, 0.25f));
        }

        private static void EncapsulateWorldBounds(
            Transform root,
            Bounds worldBounds,
            ref Bounds localBounds,
            ref bool hasBounds)
        {
            var min = worldBounds.min;
            var max = worldBounds.max;
            for (var x = 0; x < 2; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var z = 0; z < 2; z++)
                    {
                        var worldPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var localPoint = root.InverseTransformPoint(worldPoint);
                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localPoint, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localPoint);
                        }
                    }
                }
            }
        }

        private static GameObject FindExact(Scene scene, string exactName)
        {
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var transforms = roots[rootIndex].GetComponentsInChildren<Transform>(
                    includeInactive: true);
                for (var index = 0; index < transforms.Length; index++)
                {
                    if (string.Equals(
                            transforms[index].name,
                            exactName,
                            StringComparison.Ordinal))
                    {
                        return transforms[index].gameObject;
                    }
                }
            }

            return null;
        }
    }
}

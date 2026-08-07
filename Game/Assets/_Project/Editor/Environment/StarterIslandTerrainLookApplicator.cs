using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CML.Editor.Art
{
    /// <summary>
    /// Copies only the Starter Island surface/rendering setup to an existing
    /// Terrain. Heights, holes, trees and details remain owned by the target.
    /// </summary>
    public static class StarterIslandTerrainLookApplicator
    {
        private const string MenuPath =
            "CML/Art/Apply Starter Island Look To Selected Terrain";

        [MenuItem(MenuPath, priority = 590)]
        private static void ApplyToSelectedTerrain()
        {
            Terrain target = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Terrain>()
                : null;

            if (target == null || target.terrainData == null)
            {
                Debug.LogError(
                    "Select a GameObject with a Terrain component first.");
                return;
            }

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StarterIslandTerrainSetup.PrefabPath);
            Terrain source = sourcePrefab != null
                ? sourcePrefab.GetComponentInChildren<Terrain>(true)
                : null;

            if (source == null || source.terrainData == null)
            {
                Debug.LogError(
                    "Starter Island source Terrain could not be loaded from " +
                    StarterIslandTerrainSetup.PrefabPath + ".");
                return;
            }

            TerrainLayer[] sourceLayers = source.terrainData.terrainLayers;
            if (source.materialTemplate == null ||
                sourceLayers == null || sourceLayers.Length == 0)
            {
                Debug.LogError(
                    "Starter Island source material or Terrain Layers are missing.");
                return;
            }

            Undo.RecordObject(target, "Apply Starter Island Terrain Look");
            Undo.RegisterCompleteObjectUndo(
                target.terrainData,
                "Apply Starter Island Terrain Layers");

            target.materialTemplate = source.materialTemplate;
            target.drawInstanced = source.drawInstanced;
            target.heightmapPixelError = source.heightmapPixelError;
            target.basemapDistance = source.basemapDistance;
            target.shadowCastingMode = source.shadowCastingMode;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.terrainData.terrainLayers = sourceLayers;

            EditorUtility.SetDirty(target.terrainData);
            EditorUtility.SetDirty(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Applied Starter Island material and {sourceLayers.Length} " +
                $"Terrain Layers to '{target.name}' without changing its shape.",
                target);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateApplyToSelectedTerrain()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponent<Terrain>() != null;
        }
    }
}

using System;
using CML.Unity.Presentation.Crafting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Editor.UI
{
    public static class WorkbenchHudAssetSetup
    {
        public const string Root = "Assets/_Project/Art/UI/Crafting";
        public const string UxmlPath = Root + "/WorkbenchHUD.uxml";
        public const string StyleSheetPath = Root + "/WorkbenchHUD.uss";
        public const string PrefabPath = Root + "/PF_WorkbenchHUD.prefab";

        [MenuItem("CML/UI/Rebuild Workbench HUD")]
        public static void Run()
        {
            EnsureAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"WORKBENCH_HUD_BUILD uxml={UxmlPath} uss={StyleSheetPath} "
                + $"prefab={PrefabPath} status=PASS");
        }

        public static GameObject EnsureAssets()
        {
            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                throw new InvalidOperationException(
                    $"Workbench UXML is missing or failed to import: {UxmlPath}");
            }

            var styleSheet =
                AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet == null)
            {
                throw new InvalidOperationException(
                    $"Workbench USS is missing or failed to import: {StyleSheetPath}");
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                InventoryHudAssetSetup.PanelSettingsPath);
            if (panel == null)
            {
                InventoryHudAssetSetup.EnsureAssets();
                panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    InventoryHudAssetSetup.PanelSettingsPath);
            }

            var root = new GameObject("PF_WorkbenchHUD");
            try
            {
                var document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 230;
                // Keep the retained tree out of Edit Mode. The controller enables
                // the document in Awake, before it builds the runtime view.
                document.enabled = false;

                var controller = root.AddComponent<WorkbenchHudController>();
                controller.ConfigureUiAsset(document, styleSheet);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}

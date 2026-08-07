using System;
using CML.Unity.Presentation.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Editor.UI
{
    /// <summary>
    /// Deterministically builds the retained-mode player inventory HUD assets.
    /// Source UXML/USS remain hand-authored; generated Unity assets are safe to
    /// recreate whenever the scene is rebuilt.
    /// </summary>
    public static class InventoryHudAssetSetup
    {
        public const string Root =
            "Assets/_Project/Art/UI/Inventory";
        public const string UxmlPath =
            Root + "/InventoryHUD.uxml";
        public const string StyleSheetPath =
            Root + "/InventoryHUD.uss";
        public const string PanelSettingsPath =
            Root + "/PS_GameHUD.asset";
        public const string PrefabPath =
            Root + "/PF_InventoryHUD.prefab";

        [MenuItem("CML/UI/Rebuild Player Inventory HUD")]
        public static void Run()
        {
            EnsureAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"INVENTORY_HUD_BUILD uxml={UxmlPath} " +
                $"uss={StyleSheetPath} panel={PanelSettingsPath} " +
                $"prefab={PrefabPath} status=PASS");
        }

        public static GameObject EnsureAssets()
        {
            EnsureFolder("Assets/_Project/Art", "UI");
            EnsureFolder("Assets/_Project/Art/UI", "Inventory");

            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                throw new InvalidOperationException(
                    $"Inventory UXML is missing or failed to import: {UxmlPath}");
            }

            var styleSheet =
                AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet == null)
            {
                throw new InvalidOperationException(
                    $"Inventory USS is missing or failed to import: " +
                    StyleSheetPath);
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.name = "PS_GameHUD";
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }

            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode =
                PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 200f;
            panel.targetTexture = null;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);

            var root = new GameObject("PF_InventoryHUD");
            try
            {
                var document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 200;
                document.enabled = false;

                var controller =
                    root.AddComponent<InventoryHudController>();
                controller.ConfigureUiAsset(document, styleSheet);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void EnsureFolder(
            string parent,
            string child)
        {
            var combined = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}

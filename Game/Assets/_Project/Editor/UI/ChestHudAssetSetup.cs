using System;
using CML.Unity.Presentation.Machines;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Editor.UI
{
    /// <summary>
    /// Builds the crate panel prefab. It carries the transfer bridge on the same object,
    /// because the panel is useless without a write path and wiring them separately would
    /// let a scene ship a panel whose clicks go nowhere.
    /// </summary>
    public static class ChestHudAssetSetup
    {
        public const string Root = "Assets/_Project/Art/UI/Chest";
        public const string UxmlPath = Root + "/ChestHUD.uxml";
        public const string StyleSheetPath = Root + "/ChestHUD.uss";
        public const string PrefabPath = Root + "/PF_ChestHUD.prefab";

        [MenuItem("CML/UI/Rebuild Chest HUD")]
        public static void Run()
        {
            EnsureAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"CHEST_HUD_BUILD uxml={UxmlPath} uss={StyleSheetPath} "
                + $"prefab={PrefabPath} status=PASS");
        }

        public static GameObject EnsureAssets()
        {
            EnsureFolder("Assets/_Project/Art", "UI");
            EnsureFolder("Assets/_Project/Art/UI", "Chest");

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                throw new InvalidOperationException(
                    $"Chest UXML is missing or failed to import: {UxmlPath}");
            }

            var chestSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (chestSheet == null)
            {
                throw new InvalidOperationException(
                    $"Chest USS is missing or failed to import: {StyleSheetPath}");
            }

            var inventorySheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                InventoryHudAssetSetup.StyleSheetPath);
            if (inventorySheet == null)
            {
                throw new InvalidOperationException(
                    "The crate panel needs the inventory stylesheet for its shared slot "
                    + $"and surface rules: {InventoryHudAssetSetup.StyleSheetPath}");
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                InventoryHudAssetSetup.PanelSettingsPath);
            if (panel == null)
            {
                throw new InvalidOperationException(
                    "PS_GameHUD is missing. Run CML/UI/Rebuild Player Inventory HUD first: "
                    + "every HUD shares it on purpose.");
            }

            var root = new GameObject("PF_ChestHUD");
            try
            {
                var document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 220;

                var bridge = root.AddComponent<TransferCommandBridge>();
                var controller = root.AddComponent<ChestHudController>();
                controller.ConfigureUiAsset(document, chestSheet, inventorySheet);
                controller.ConfigureGameplayInput(bridge, null, null);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            var combined = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}

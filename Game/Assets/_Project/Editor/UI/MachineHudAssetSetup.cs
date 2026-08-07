using System;
using CML.Unity.Presentation.Machines;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Editor.UI
{
    /// <summary>
    /// Builds the machine panel prefab. Source UXML/USS stay hand-authored, as for the
    /// inventory; the panel settings asset is shared with it, so both HUDs scale from the
    /// same reference resolution and cannot disagree about layout at 3440 × 1440.
    /// </summary>
    public static class MachineHudAssetSetup
    {
        public const string Root = "Assets/_Project/Art/UI/Machine";
        public const string UxmlPath = Root + "/MachineHUD.uxml";
        public const string StyleSheetPath = Root + "/MachineHUD.uss";
        public const string PrefabPath = Root + "/PF_MachineHUD.prefab";

        [MenuItem("CML/UI/Rebuild Machine HUD")]
        public static void Run()
        {
            EnsureAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"MACHINE_HUD_BUILD uxml={UxmlPath} uss={StyleSheetPath} "
                + $"prefab={PrefabPath} status=PASS");
        }

        public static GameObject EnsureAssets()
        {
            EnsureFolder("Assets/_Project/Art", "UI");
            EnsureFolder("Assets/_Project/Art/UI", "Machine");

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                throw new InvalidOperationException(
                    $"Machine UXML is missing or failed to import: {UxmlPath}");
            }

            var machineSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (machineSheet == null)
            {
                throw new InvalidOperationException(
                    $"Machine USS is missing or failed to import: {StyleSheetPath}");
            }

            // The inventory sheet carries the panel surface and every slot rule. The
            // machine panel reuses them rather than restating them.
            var inventorySheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                InventoryHudAssetSetup.StyleSheetPath);
            if (inventorySheet == null)
            {
                throw new InvalidOperationException(
                    "The machine panel needs the inventory stylesheet for its shared "
                    + $"slot and surface rules: {InventoryHudAssetSetup.StyleSheetPath}");
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                InventoryHudAssetSetup.PanelSettingsPath);
            if (panel == null)
            {
                throw new InvalidOperationException(
                    "PS_GameHUD is missing. Run CML/UI/Rebuild Player Inventory HUD "
                    + "first: both HUDs share it on purpose.");
            }

            var root = new GameObject("PF_MachineHUD");
            try
            {
                var document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 210;

                var controller = root.AddComponent<MachineHudController>();
                controller.ConfigureUiAsset(document, machineSheet, inventorySheet);

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

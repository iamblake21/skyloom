using System;
using CML.Unity.Presentation.Airship;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Editor.UI
{
    /// <summary>
    /// Builds the repair-panel prefab from its authored UXML and USS, mirroring
    /// <see cref="WorkbenchHudAssetSetup"/>. It creates a new prefab and never
    /// regenerates an existing one, because regenerating a prefab silently nulls
    /// the references the scene already holds.
    /// </summary>
    public static class AirshipRepairHudAssetSetup
    {
        public const string Root = "Assets/_Project/Art/UI/AirshipRepair";
        public const string UxmlPath = Root + "/AirshipRepairHUD.uxml";
        public const string StyleSheetPath = Root + "/AirshipRepairHUD.uss";

        /// <summary>
        /// Under Resources on purpose. The panel did not exist when the Starter
        /// Island was authored, and the scene is not to be regenerated or saved
        /// to add it, so the composition root loads and instantiates it at
        /// runtime — the same route the smoke shader already takes.
        /// </summary>
        public const string PrefabPath =
            "Assets/_Project/Resources/PF_AirshipRepairHUD.prefab";

        public const string PrefabResourceName = "PF_AirshipRepairHUD";

        [MenuItem("CML/UI/Rebuild Airship Repair HUD")]
        public static void Run()
        {
            EnsureAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"AIRSHIP_REPAIR_HUD_BUILD uxml={UxmlPath} uss={StyleSheetPath} "
                + $"prefab={PrefabPath} status=PASS");
        }

        public static GameObject EnsureAssets()
        {
            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                throw new InvalidOperationException(
                    $"Airship repair UXML is missing or failed to import: {UxmlPath}");
            }

            var styleSheet =
                AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet == null)
            {
                throw new InvalidOperationException(
                    $"Airship repair USS is missing or failed to import: {StyleSheetPath}");
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                InventoryHudAssetSetup.PanelSettingsPath);
            if (panel == null)
            {
                InventoryHudAssetSetup.EnsureAssets();
                panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    InventoryHudAssetSetup.PanelSettingsPath);
            }

            var root = new GameObject("PF_AirshipRepairHUD");
            try
            {
                var document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = visualTree;

                // Above the machine and workbench panels: the repair panel is
                // opened from the world and never stacks under them.
                document.sortingOrder = 240;

                // Keep the retained tree out of Edit Mode. The controller enables
                // the document in Awake, before it builds the runtime view.
                document.enabled = false;

                var controller = root.AddComponent<AirshipRepairHudController>();
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

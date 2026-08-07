using System.Collections;
using System.IO;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Unity.Presentation.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CML.Tests.Unity.Presentation
{
    public sealed class InventoryHudVisualQaTests
    {
        private static readonly StableId InventoryId =
            new StableId(0x51415F494E56454EUL, 0x544F52595F303031UL);

        [UnityTest]
        public IEnumerator RendersClosedAndOpenAtTargetAspectRatios()
        {
            var outputRoot = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    "outputs",
                    "InventoryHUD"));
            Directory.CreateDirectory(outputRoot);

            yield return Render(
                1920,
                1080,
                false,
                Path.Combine(outputRoot, "hotbar_1920x1080.png"));
            yield return Render(
                1920,
                1080,
                true,
                Path.Combine(outputRoot, "inventory_1920x1080.png"));
            yield return Render(
                3440,
                1440,
                false,
                Path.Combine(outputRoot, "hotbar_3440x1440.png"));
            yield return Render(
                3440,
                1440,
                true,
                Path.Combine(outputRoot, "inventory_3440x1440.png"));
        }

        private static IEnumerator Render(
            int width,
            int height,
            bool inventoryOpen,
            string outputPath)
        {
            const string prefabPath =
                "Assets/_Project/Art/UI/Inventory/PF_InventoryHUD.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            var instance = Object.Instantiate(prefab);
            var document = instance.GetComponent<UIDocument>();
            var controller =
                instance.GetComponent<InventoryHudController>();
            Assert.That(document, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);

            var panel = Object.Instantiate(document.panelSettings);
            panel.name = $"PS_InventoryHUD_QA_{width}x{height}";
            var target = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = $"RT_InventoryHUD_QA_{width}x{height}",
                antiAliasing = 1
            };
            target.Create();
            panel.targetTexture = target;
            document.panelSettings = panel;

            AddQaBackground(document.rootVisualElement);
            var catalog = BootstrapCatalog.Load();
            var inventory = InventoryState.Restore(
                InventoryId,
                catalog,
                ContentIds.PlayerInventory,
                new[]
                {
                    new InventoryStackRecord(
                        0,
                        ContentIds.RawIron,
                        new NonNegativeQuantity(23)),
                    new InventoryStackRecord(
                        1,
                        ContentIds.IronIngot,
                        new NonNegativeQuantity(8)),
                    new InventoryStackRecord(
                        2,
                        ContentIds.IronPlate,
                        new NonNegativeQuantity(3))
                });
            controller.BindInventory(inventory, catalog);
            controller.SetInventoryOpen(inventoryOpen);

            var firstSlot =
                document.rootVisualElement.Q<VisualElement>("hotbar-slot-0");
            Assert.That(firstSlot, Is.Not.Null);
            Assert.That(
                firstSlot.ClassListContains("inventory-slot--occupied"),
                Is.True);
            Assert.That(
                firstSlot.ClassListContains("inventory-slot--selected"),
                Is.True);
            Assert.That(
                firstSlot.Q<VisualElement>(
                    className: "slot-icon-host").childCount,
                Is.EqualTo(1));
            Assert.That(
                firstSlot.Q<Label>(
                    className: "slot-quantity").text,
                Is.EqualTo("23"));

            yield return null;
            yield return null;
            yield return null;

            var quantityLabel =
                firstSlot.Q<Label>(className: "slot-quantity");
            var iconHost =
                firstSlot.Q<VisualElement>(className: "slot-icon-host");
            var icon = iconHost.ElementAt(0);
            Assert.That(
                quantityLabel.resolvedStyle.display,
                Is.EqualTo(DisplayStyle.Flex));
            Assert.That(quantityLabel.resolvedStyle.width, Is.GreaterThan(0f));
            Assert.That(iconHost.resolvedStyle.width, Is.GreaterThan(0f));
            Assert.That(icon.resolvedStyle.width, Is.GreaterThan(0f));

            var previous = RenderTexture.active;
            var capture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false);
            try
            {
                RenderTexture.active = target;
                capture.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0,
                    false);
                capture.Apply(false, false);
                var png = capture.EncodeToPNG();
                Assert.That(png.Length, Is.GreaterThan(1024));
                File.WriteAllBytes(outputPath, png);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(panel);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static void AddQaBackground(VisualElement root)
        {
            var background = new VisualElement
            {
                name = "qa-world-background"
            };
            background.style.position = Position.Absolute;
            background.style.left = 0f;
            background.style.top = 0f;
            background.style.right = 0f;
            background.style.bottom = 0f;
            background.style.backgroundColor =
                new Color(0.45f, 0.67f, 0.48f, 1f);

            var sky = new VisualElement();
            sky.style.position = Position.Absolute;
            sky.style.left = 0f;
            sky.style.top = 0f;
            sky.style.right = 0f;
            sky.style.height = Length.Percent(46f);
            sky.style.backgroundColor =
                new Color(0.52f, 0.77f, 0.86f, 1f);
            background.Add(sky);

            var path = new VisualElement();
            path.style.position = Position.Absolute;
            path.style.left = Length.Percent(18f);
            path.style.right = Length.Percent(18f);
            path.style.bottom = 0f;
            path.style.height = Length.Percent(32f);
            path.style.backgroundColor =
                new Color(0.79f, 0.62f, 0.41f, 1f);
            path.style.borderTopLeftRadius = 220f;
            path.style.borderTopRightRadius = 220f;
            background.Add(path);

            root.Insert(0, background);
        }
    }
}

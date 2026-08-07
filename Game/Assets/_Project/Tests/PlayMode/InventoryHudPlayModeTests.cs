using System.Collections;
using CML.Unity.Airship;
using CML.Unity.Presentation.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CML.Tests.PlayMode
{
    public sealed class InventoryHudPlayModeTests : InputTestFixture
    {
        private GameObject _root;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _root = new GameObject("InventoryHudPlayModeFixture");
        }

        [TearDown]
        public override void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator TabTogglesModalInputExactlyOncePerPress()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var document = _root.AddComponent<UIDocument>();
            BuildRequiredVisualTree(document.rootVisualElement);

            var mouseLook = _root.AddComponent<FirstPersonMouseLook>();
            var input = _root.AddComponent<AirshipInputAdapter>();
            var controller = _root.AddComponent<InventoryHudController>();
            controller.ConfigureGameplayInput(
                input,
                mouseLook,
                useReviewContents: false);

            yield return null;
            Assert.That(controller.InventoryOpen, Is.False);
            Assert.That(input.UiInputSuppressed, Is.False);

            Press(keyboard.tabKey);
            yield return null;
            Assert.That(controller.InventoryOpen, Is.True);
            Assert.That(input.UiInputSuppressed, Is.True);

            for (var frame = 0; frame < 5; frame++)
            {
                yield return null;
                Assert.That(controller.InventoryOpen, Is.True);
            }

            Release(keyboard.tabKey);
            yield return null;
            Press(keyboard.tabKey);
            yield return null;
            Assert.That(controller.InventoryOpen, Is.False);
            Assert.That(input.UiInputSuppressed, Is.False);
            Release(keyboard.tabKey);
        }

        private static void BuildRequiredVisualTree(
            VisualElement root)
        {
            var screen = Named("inventory-screen");
            screen.Add(Named("inventory-backdrop"));

            var panel = Named("inventory-panel");
            panel.Add(new Label { name = "capacity-label" });
            panel.Add(Named("quickbar-grid"));
            panel.Add(Named("backpack-grid"));
            screen.Add(panel);

            screen.Add(new Label { name = "selected-item-label" });
            screen.Add(Named("hotbar"));
            root.Add(screen);
        }

        private static VisualElement Named(string name)
        {
            return new VisualElement { name = name };
        }
    }
}

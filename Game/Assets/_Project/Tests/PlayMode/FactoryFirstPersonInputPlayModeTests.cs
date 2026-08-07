using System.Collections;
using System.Reflection;
using CML.Unity.Airship;
using CML.Unity.Factory;
using CML.Unity.Presentation.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace CML.Tests.PlayMode
{
    /// <summary>
    /// Regression coverage for scene-local locomotion. Cursor ownership is a
    /// presentation detail; only an authoritative modal HUD may suppress movement.
    /// </summary>
    public sealed class FactoryFirstPersonInputPlayModeTests : InputTestFixture
    {
        private GameObject _systems;
        private GameObject _player;
        private FactoryHudOrchestrator _hud;
        private InventoryHudController _inventoryHud;

        public override void Setup()
        {
            base.Setup();

            _systems = new GameObject("FactoryFirstPersonInputTests_Systems");
            _hud = _systems.AddComponent<FactoryHudOrchestrator>();

            var inventoryObject =
                new GameObject("FactoryFirstPersonInputTests_Inventory");
            inventoryObject.SetActive(false);
            _inventoryHud =
                inventoryObject.AddComponent<InventoryHudController>();
            SetPrivateField(_hud, "inventoryHud", _inventoryHud);

            _player = new GameObject("FactoryFirstPersonInputTests_Player");
            var controller = _player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var yaw = new GameObject("ViewYaw").transform;
            yaw.SetParent(_player.transform, false);
            var pitch = new GameObject("ViewPitch").transform;
            pitch.SetParent(yaw, false);

            var motor = _player.AddComponent<FirstPersonCharacterMotor>();
            motor.Configure(controller, yaw, null);
            var mouseLook = _player.AddComponent<FirstPersonMouseLook>();
            mouseLook.Configure(yaw, pitch);
            var input = _player.AddComponent<FactoryFirstPersonInput>();
            input.Configure(motor, mouseLook, _hud);
        }

        public override void TearDown()
        {
            if (_inventoryHud != null)
            {
                Object.DestroyImmediate(_inventoryHud.gameObject);
            }

            if (_player != null)
            {
                Object.DestroyImmediate(_player);
            }

            if (_systems != null)
            {
                Object.DestroyImmediate(_systems);
            }

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator MovementStartsImmediatelyWithAnUnlockedCursor()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            // Let the freshly-created CharacterController receive one valid
            // non-zero frame delta before measuring the first input frame.
            yield return null;
            Cursor.lockState = CursorLockMode.None;
            var before = HorizontalPosition(_player.transform.position);

            Press(keyboard.wKey);
            yield return null;

            var after = HorizontalPosition(_player.transform.position);
            Assert.That(
                Vector2.Distance(before, after),
                Is.GreaterThan(0.0001f),
                "W must move on its first frame even while the cursor is unlocked.");
            Release(keyboard.wKey);
        }

        [UnityTest]
        public IEnumerator ModalHudBlocksMovementAndClosingItRestoresMovementNextFrame()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            SetPrivateField(_inventoryHud, "_inventoryOpen", true);
            Cursor.lockState = CursorLockMode.None;
            Press(keyboard.wKey);

            var beforeModalFrame =
                HorizontalPosition(_player.transform.position);
            yield return null;
            var afterModalFrame =
                HorizontalPosition(_player.transform.position);
            Assert.That(
                Vector2.Distance(beforeModalFrame, afterModalFrame),
                Is.LessThan(0.001f),
                "An open modal HUD must have exclusive ownership of movement input.");

            SetPrivateField(_inventoryHud, "_inventoryOpen", false);
            var beforeFirstClosedFrame =
                HorizontalPosition(_player.transform.position);
            yield return null;
            var afterFirstClosedFrame =
                HorizontalPosition(_player.transform.position);
            Assert.That(
                Vector2.Distance(beforeFirstClosedFrame, afterFirstClosedFrame),
                Is.GreaterThan(0.0001f),
                "Closing the modal must restore W movement without a cursor-lock delay.");
            Release(keyboard.wKey);
        }

        private static Vector2 HorizontalPosition(Vector3 position)
        {
            return new Vector2(position.x, position.z);
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }
    }
}

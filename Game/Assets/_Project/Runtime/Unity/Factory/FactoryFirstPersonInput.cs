using CML.Unity.Airship;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Small scene-local input adapter for the factory test. It deliberately has no
    /// airship dependency and delegates every collision to Unity's CharacterController.
    /// </summary>
    // InventoryHudController updates at 100 and FactoryHudOrchestrator at 150.
    // Reading their modal state afterwards makes an open/close edge effective in
    // the same frame instead of coupling locomotion to the cursor side effect.
    [DefaultExecutionOrder(175)]
    [DisallowMultipleComponent]
    public sealed class FactoryFirstPersonInput : MonoBehaviour
    {
        [SerializeField] private FirstPersonCharacterMotor motor;
        [SerializeField] private FirstPersonMouseLook mouseLook;
        [SerializeField] private FactoryHudOrchestrator hud;

        public void Configure(
            FirstPersonCharacterMotor characterMotor,
            FirstPersonMouseLook firstPersonMouseLook,
            FactoryHudOrchestrator hudOrchestrator = null)
        {
            motor = characterMotor;
            mouseLook = firstPersonMouseLook;
            hud = hudOrchestrator;
        }

        private void Awake()
        {
            if (hud == null)
            {
                hud = FindFirstObjectByType<FactoryHudOrchestrator>();
            }
        }

        private void Update()
        {
            if (motor == null || mouseLook == null)
            {
                return;
            }

            if (hud != null && hud.AnyModalOpen)
            {
                motor.Move(0, 0, Time.deltaTime);
                return;
            }

            var keyboard = Keyboard.current;
            var forward = Axis(
                keyboard?.sKey.isPressed == true,
                keyboard?.wKey.isPressed == true);
            var strafe = Axis(
                keyboard?.aKey.isPressed == true,
                keyboard?.dKey.isPressed == true);
            if (forward != 0 && strafe != 0)
            {
                forward = forward > 0 ? 707 : -707;
                strafe = strafe > 0 ? 707 : -707;
            }

            // Cursor ownership may suppress mouse-look, but never keyboard
            // locomotion. Modal HUD state is the sole movement gate above.
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var delta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;
                mouseLook.ApplyLookDelta(mouseLook.FilterMouseDelta(delta));
            }

            motor.Move(
                forward,
                strafe,
                keyboard?.leftShiftKey.isPressed == true,
                Time.deltaTime);
        }

        private static int Axis(bool negative, bool positive)
        {
            if (negative == positive)
            {
                return 0;
            }

            return positive ? 1_000 : -1_000;
        }
    }
}

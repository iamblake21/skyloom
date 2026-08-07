using UnityEngine;

namespace CML.Unity.Presentation.Equipment
{
    /// <summary>
    /// Scene bridge between movable preview transforms and the shared
    /// first-person pose asset. The custom inspector that writes the asset
    /// remains editor-only.
    /// </summary>
    [ExecuteAlways]
    public sealed class PickaxeHandsSetupAuthoring : MonoBehaviour
    {
        [SerializeField] private FirstPersonEquipmentPose sharedPose;
        [SerializeField] private Transform pickaxe;

        public FirstPersonEquipmentPose SharedPose => sharedPose;
        public Transform Pickaxe => pickaxe;

        public void Configure(
            FirstPersonEquipmentPose pose,
            Transform pickaxeTransform)
        {
            sharedPose = pose;
            pickaxe = pickaxeTransform;
        }

        public bool IsComplete =>
            sharedPose != null
            && pickaxe != null;
    }
}

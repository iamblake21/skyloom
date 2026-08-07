using System;
using UnityEngine;

namespace CML.Unity.Presentation.Equipment
{
    /// <summary>
    /// Shared authoring data for the first-person pickaxe viewmodel.
    /// The setup scene edits this asset; every gameplay scene reads it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FirstPersonEquipmentPose",
        menuName = "CML/Equipment/First Person Equipment Pose")]
    public sealed class FirstPersonEquipmentPose : ScriptableObject
    {
        public const string ResourcesPath =
            "Equipment/FirstPersonEquipmentPose";

        [SerializeField] private LocalViewPose pickaxe = new LocalViewPose(
            new Vector3(0.26f, -0.44f, 0.72f),
            new Vector3(7f, -9f, -11f),
            new Vector3(0.74f, 0.74f, 0.74f));

        public LocalViewPose Pickaxe => pickaxe;

        public void Capture(Transform pickaxeTransform)
        {
            if (pickaxeTransform == null)
            {
                throw new ArgumentNullException(nameof(pickaxeTransform));
            }

            pickaxe = LocalViewPose.Capture(pickaxeTransform);
        }
    }

    [Serializable]
    public struct LocalViewPose
    {
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale;

        public LocalViewPose(
            Vector3 position,
            Vector3 eulerAngles,
            Vector3 scale)
        {
            localPosition = position;
            localEulerAngles = eulerAngles;
            localScale = scale;
        }

        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;

        public void ApplyTo(Transform target)
        {
            if (target == null)
            {
                return;
            }

            target.localPosition = localPosition;
            target.localRotation = Quaternion.Euler(localEulerAngles);
            target.localScale = localScale;
        }

        public static LocalViewPose Capture(Transform source)
        {
            return new LocalViewPose(
                source.localPosition,
                source.localEulerAngles,
                source.localScale);
        }
    }
}

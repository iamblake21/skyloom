using System;
using System.Collections.Generic;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Coordinate frame used to project a passenger onto the moving airship.
    /// The passenger itself stays in world space so a uniformly scaled airship
    /// never scales its CharacterController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipFrame : MonoBehaviour
    {
        [SerializeField] private Transform passengerSpace;
        [SerializeField] private Transform pilotCameraAnchor;
        [SerializeField] private AirshipMotor motor;

        private readonly HashSet<AirshipRelativePassenger> _passengers =
            new HashSet<AirshipRelativePassenger>();

        public Transform PassengerSpace
        {
            get { return passengerSpace != null ? passengerSpace : transform; }
        }

        public AirshipMotor Motor
        {
            get
            {
                if (motor == null)
                {
                    motor = GetComponentInParent<AirshipMotor>();
                }

                return motor;
            }
        }

        public Transform PilotCameraAnchor
        {
            get
            {
                if (pilotCameraAnchor == null)
                {
                    var descendants = GetComponentsInChildren<Transform>(true);
                    for (var index = 0; index < descendants.Length; index++)
                    {
                        if (descendants[index].name == "REF_PilotCamera")
                        {
                            pilotCameraAnchor = descendants[index];
                            break;
                        }
                    }
                }

                return pilotCameraAnchor;
            }
        }

        public int PassengerCount
        {
            get { return _passengers.Count; }
        }

        public void Configure(
            Transform relativePassengerSpace,
            Transform authoredPilotCameraAnchor,
            AirshipMotor airshipMotor)
        {
            passengerSpace = relativePassengerSpace != null ? relativePassengerSpace : transform;
            pilotCameraAnchor = authoredPilotCameraAnchor;
            motor = airshipMotor;
            ValidatePassengerSpace();
        }

        public void Configure(
            Transform relativePassengerSpace,
            AirshipMotor airshipMotor)
        {
            Configure(relativePassengerSpace, null, airshipMotor);
        }

        public Vector3 ToWorldPoint(Vector3 localPoint)
        {
            return PassengerSpace.TransformPoint(localPoint);
        }

        public Vector3 ToLocalPoint(Vector3 worldPoint)
        {
            return PassengerSpace.InverseTransformPoint(worldPoint);
        }

        public Quaternion ToWorldRotation(Quaternion localRotation)
        {
            return PassengerSpace.rotation * localRotation;
        }

        public Quaternion ToLocalRotation(Quaternion worldRotation)
        {
            return Quaternion.Inverse(PassengerSpace.rotation) * worldRotation;
        }

        internal void Register(AirshipRelativePassenger passenger)
        {
            if (passenger == null)
            {
                throw new ArgumentNullException(nameof(passenger));
            }

            ValidatePassengerSpace();
            _passengers.Add(passenger);
        }

        internal void Unregister(AirshipRelativePassenger passenger)
        {
            if (passenger != null)
            {
                _passengers.Remove(passenger);
            }
        }

        private void ValidatePassengerSpace()
        {
            var scale = PassengerSpace.lossyScale;
            var maximum = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            var minimum = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
            var invalid =
                float.IsNaN(maximum)
                || float.IsInfinity(maximum)
                || minimum <= 0f;
            var tolerance = Mathf.Max(0.0001f, maximum * 0.0001f);
            if (invalid || maximum - minimum > tolerance)
            {
                throw new InvalidOperationException(
                    "Airship PassengerSpace requires a positive uniform world scale. "
                    + "Non-uniform scaling would corrupt passenger movement and collision.");
            }
        }
    }
}

using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Physical command source only. Trigger callbacks never reparent a player;
    /// the committed player frame projected by the bridge does that.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class AirshipBoardingVolume : MonoBehaviour
    {
        [SerializeField] private AirshipFrame frame;
        [SerializeField] private AirshipSimulationBridge simulationBridge;
        [SerializeField] private Transform outboardDirectionReference;
        private AirshipRelativePassenger _detectedPassenger;

        public AirshipFrame Frame => frame;

        public AirshipSimulationBridge SimulationBridge => simulationBridge;

        public Transform OutboardDirectionReference => outboardDirectionReference;

        public bool HasDetectedPassenger => _detectedPassenger != null;

        public void Configure(
            AirshipFrame airshipFrame,
            AirshipSimulationBridge bridge,
            Transform physicalOutboardDirectionReference)
        {
            frame = airshipFrame;
            simulationBridge = bridge;
            outboardDirectionReference = physicalOutboardDirectionReference;
            EnsureTrigger();
        }

        public bool NotifyPassengerEntered(AirshipRelativePassenger passenger)
        {
            if (passenger == null
                || simulationBridge == null)
            {
                return false;
            }

            _detectedPassenger = passenger;
            return true;
        }

        public bool NotifyPassengerExited(
            AirshipRelativePassenger passenger,
            Vector3 exitWorldPosition)
        {
            if (passenger == null
                || passenger != _detectedPassenger
                || simulationBridge == null)
            {
                return false;
            }

            _detectedPassenger = null;
            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            NotifyPassengerEntered(
                other.GetComponentInParent<AirshipRelativePassenger>());
        }

        private void OnTriggerExit(Collider other)
        {
            var relativePassenger =
                other.GetComponentInParent<AirshipRelativePassenger>();
            if (relativePassenger != null)
            {
                NotifyPassengerExited(relativePassenger, relativePassenger.BodyRoot.position);
            }
        }

        private bool IsOnOutboardSide(Vector3 worldPosition)
        {
            if (outboardDirectionReference == null)
            {
                return false;
            }

            var outward = Vector3.ProjectOnPlane(
                outboardDirectionReference.forward,
                Vector3.up).normalized;
            return outward.sqrMagnitude >= 0.99f
                && Vector3.Dot(worldPosition - transform.position, outward) > 0f;
        }

        private void EnsureTrigger()
        {
            var volume = GetComponent<Collider>();
            if (volume != null)
            {
                volume.isTrigger = true;
            }
        }
    }
}

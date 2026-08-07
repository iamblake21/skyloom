using System;
using CML.Simulation.Airship;
using CML.Unity.Presentation;
using UnityEngine;

namespace CML.Unity.Airship
{
    /// <summary>
    /// Cockpit command adapter. Occupancy is read from the committed passenger
    /// state; this object never owns a pilot or a flight model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AirshipPilotStation : MonoBehaviour,
        IWorldInteractionTarget
    {
        [SerializeField] private AirshipFrame frame;
        [SerializeField] private AirshipSimulationBridge simulationBridge;
        [SerializeField] private AirshipRelativePassenger passenger;
        [SerializeField] private Transform interactionPoint;
        [SerializeField, Min(0.1f)] private float maximumInteractionDistance = 2f;

        public AirshipRelativePassenger CurrentPilot =>
            passenger != null && passenger.IsPiloting ? passenger : null;

        public bool IsOccupied => CurrentPilot != null;

        public Transform InteractionPoint =>
            interactionPoint != null ? interactionPoint : transform;

        public AirshipSimulationBridge SimulationBridge => simulationBridge;

        public AirshipFrame Frame => frame;

        public bool IsInteractionAvailable
        {
            get
            {
                if (!enabled
                    || !gameObject.activeInHierarchy
                    || passenger == null
                    || simulationBridge == null
                    || !simulationBridge.IsInitialized)
                {
                    return false;
                }

                return !passenger.IsPiloting
                    && passenger.IsAboard
                    && passenger.CurrentFrame == frame
                    && (passenger.BodyRoot.position - InteractionPoint.position)
                        .sqrMagnitude
                        <= maximumInteractionDistance * maximumInteractionDistance;
            }
        }

        /// <summary>
        /// True while the authoritative hull is still not airworthy. The word on
        /// the prompt follows this and nothing else: presentation must never be
        /// the place that decides whether the ship can fly.
        /// </summary>
        public bool IsAwaitingRepair =>
            simulationBridge != null && simulationBridge.IsAwaitingRepair;

        public string InteractionPrompt =>
            IsAwaitingRepair ? "Ispeziona aeronave" : "Pilota aeronave";

        /// <summary>
        /// Raised when the player interacts with a hull that still needs work.
        /// The repair panel subscribes to this; the station itself owns no UI.
        /// </summary>
        public event Action<AirshipPilotStation> InspectionRequested;

        public void Configure(
            AirshipFrame airshipFrame,
            AirshipSimulationBridge bridge,
            AirshipRelativePassenger relativePassenger,
            Transform physicalInteractionPoint,
            float interactionDistance)
        {
            if (interactionDistance <= 0f
                || float.IsNaN(interactionDistance)
                || float.IsInfinity(interactionDistance))
            {
                throw new ArgumentOutOfRangeException(nameof(interactionDistance));
            }

            frame = airshipFrame;
            simulationBridge = bridge;
            passenger = relativePassenger;
            interactionPoint = physicalInteractionPoint;
            maximumInteractionDistance = interactionDistance;
        }

        public bool TryBeginPiloting(AirshipRelativePassenger candidate)
        {
            if (candidate == null
                || candidate != passenger
                || !candidate.IsAboard
                || candidate.IsPiloting
                || candidate.CurrentFrame != frame
                || simulationBridge == null
                || !simulationBridge.IsInitialized
                // The reducer refuses this too. Refusing here as well keeps a
                // doomed command out of the queue instead of letting it be
                // enqueued only to be dropped.
                || simulationBridge.IsAwaitingRepair)
            {
                return false;
            }

            var squaredDistance =
                (candidate.BodyRoot.position - InteractionPoint.position).sqrMagnitude;
            if (squaredDistance
                > maximumInteractionDistance * maximumInteractionDistance)
            {
                return false;
            }

            simulationBridge.QueuePilotBegin();
            return true;
        }

        public bool StopPiloting()
        {
            if (!IsOccupied
                || simulationBridge == null
                || !simulationBridge.IsInitialized)
            {
                return false;
            }

            simulationBridge.QueuePilotEnd();
            return true;
        }

        public bool SubmitPilotInput(AirshipPilotInputState input)
        {
            if (!IsOccupied
                || simulationBridge == null
                || !simulationBridge.IsInitialized)
            {
                return false;
            }

            simulationBridge.QueuePilotInput(input);
            return true;
        }

        public bool RequestTakeoff()
        {
            if (!IsOccupied
                || simulationBridge == null
                || !simulationBridge.IsInitialized)
            {
                return false;
            }

            simulationBridge.QueueTakeoff();
            return true;
        }

        public bool RequestLanding()
        {
            return IsOccupied
                && simulationBridge != null
                && simulationBridge.IsInitialized
                && simulationBridge.QueueLandingFromProbe();
        }

        public bool OwnsInteractionCollider(Collider collider)
        {
            return collider != null
                && (collider.transform == transform
                    || collider.transform.IsChildOf(transform));
        }

        public bool TryGetInteractionBounds(out Bounds bounds)
        {
            bounds = new Bounds(
                InteractionPoint.position,
                Vector3.one * 0.24f);
            return true;
        }

        public bool TryInteract()
        {
            if (!IsInteractionAvailable)
            {
                return false;
            }

            // Same key, same spot, two different acts. Which one it is comes
            // from the committed hull state, so the prompt the player read and
            // the action they get can never disagree.
            if (IsAwaitingRepair)
            {
                InspectionRequested?.Invoke(this);
                return true;
            }

            return TryBeginPiloting(passenger);
        }

        private void OnValidate()
        {
            maximumInteractionDistance = Mathf.Max(0.1f, maximumInteractionDistance);
        }
    }
}

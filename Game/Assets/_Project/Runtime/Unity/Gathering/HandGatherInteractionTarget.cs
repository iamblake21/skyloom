using CML.Unity.Presentation;
using UnityEngine;

namespace CML.Unity.Gathering
{
    /// <summary>
    /// Puts a gatherable source on the central interaction prompt and picks it
    /// up on the press edge.
    ///
    /// One press, one result: the same contract chests and machines already
    /// use, so the player never has to learn that some objects want a hold.
    /// </summary>
    [RequireComponent(typeof(HandGatherSourceIdentity))]
    [DisallowMultipleComponent]
    public sealed class HandGatherInteractionTarget
        : MonoBehaviour, IWorldInteractionTarget
    {
        private HandGatherSourceIdentity _identity;

        private HandGatherSourceIdentity Identity
        {
            get
            {
                if (_identity == null)
                {
                    _identity = GetComponent<HandGatherSourceIdentity>();
                }

                return _identity;
            }
        }

        public bool IsInteractionAvailable =>
            Identity != null && Identity.CanBeGathered;

        public string InteractionPrompt => "RACCOGLI";

        public bool OwnsInteractionCollider(Collider collider) =>
            collider != null
            && collider.transform.IsChildOf(transform);

        public bool TryGetInteractionBounds(out Bounds bounds) =>
            WorldInteractionBounds.TryCalculate(transform, out bounds);

        public bool TryInteract()
        {
            var controller = HandGatherController.Active;
            return controller != null && controller.TryGather(Identity);
        }
    }
}

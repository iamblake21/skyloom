using System;
using CML.Foundation;
using CML.Unity.Presentation;
using UnityEngine;

namespace CML.Unity.Factory
{
    public enum FactoryInteractionKind : byte
    {
        Chest = 1,
        Machine = 2,
        Workbench = 3
    }

    /// <summary>
    /// Identifies the authoritative object behind one or more Unity colliders. Put this
    /// component on the visual root; the interactor deliberately searches parents so
    /// authored child colliders do not each need a copy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FactoryInteractionTarget : MonoBehaviour,
        IWorldInteractionTarget
    {
        [SerializeField] private FactoryStableIdField stableId = new FactoryStableIdField();
        [SerializeField] private FactoryInteractionKind interactionKind =
            FactoryInteractionKind.Machine;
        [SerializeField] private string promptOverride = string.Empty;

        private FactoryHudOrchestrator _hud;

        public FactoryInteractionKind InteractionKind => interactionKind;

        public StableId StableId => stableId.GetValueOrNone();

        public bool IsConfigured => !StableId.IsNone;

        public bool IsInteractionAvailable =>
            enabled && gameObject.activeInHierarchy && IsConfigured;

        public string InteractionPrompt => Prompt;

        public string Prompt
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(promptOverride))
                {
                    return promptOverride.Trim();
                }

                switch (interactionKind)
                {
                    case FactoryInteractionKind.Chest:
                        return "Apri Cassa di legno";
                    case FactoryInteractionKind.Workbench:
                        return "Usa Banco da lavoro";
                    default:
                        return "Ispeziona la macchina";
                }
            }
        }

        public void Configure(
            StableId authoritativeId,
            FactoryInteractionKind kind,
            string customPrompt = null)
        {
            if (authoritativeId.IsNone)
            {
                throw new ArgumentException(
                    "An interaction target needs an authoritative stable id.",
                    nameof(authoritativeId));
            }

            stableId.Set(authoritativeId);
            interactionKind = kind;
            promptOverride = customPrompt ?? string.Empty;
        }

        public bool OwnsInteractionCollider(Collider collider)
        {
            return collider != null
                && (collider.transform == transform
                    || collider.transform.IsChildOf(transform));
        }

        public bool TryGetInteractionBounds(out Bounds bounds)
        {
            return WorldInteractionBounds.TryCalculate(transform, out bounds);
        }

        public bool TryInteract()
        {
            if (!IsInteractionAvailable)
            {
                return false;
            }

            if (_hud == null)
            {
                _hud = FindFirstObjectByType<FactoryHudOrchestrator>();
            }

            return _hud != null && _hud.TryInteract(this);
        }
    }
}

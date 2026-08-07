using UnityEngine;

namespace CML.Unity.Gathering
{
    /// <summary>
    /// Makes a gatherable object breathe with a white pulse so the player can
    /// tell it apart from the decoration it is scattered among.
    ///
    /// It writes a MaterialPropertyBlock instead of touching the material: the
    /// tufts, the sticks and the path pebbles share their materials with the
    /// island's scenery, and brightening the asset would light up every
    /// non-gatherable copy of it too.
    ///
    /// The pulse drives <c>_BaseColor</c> above one. Every shader these sources
    /// use multiplies the map by that colour, so values over white read as a
    /// glow rather than as a tint, and no new shader is needed.
    /// </summary>
    [RequireComponent(typeof(HandGatherSourceIdentity))]
    [DisallowMultipleComponent]
    public sealed class HandGatherHighlight : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        // A breath, not a blink. At the three quarters of a cycle per second
        // this used to run at, the swing between the two gains came round
        // every 1.3 seconds and the pickups read as faulty lamps rather than
        // as something asking to be noticed.
        [SerializeField, Min(0.05f)] private float cyclesPerSecond = 0.3f;
        [SerializeField, Min(0f)] private float minimumGain = 1.05f;
        [SerializeField, Min(0f)] private float maximumGain = 1.85f;

        private HandGatherSourceIdentity _identity;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _appliedGain = float.NaN;

        private void OnEnable()
        {
            _identity = GetComponent<HandGatherSourceIdentity>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            _appliedGain = float.NaN;
        }

        private void OnDisable()
        {
            Apply(1f);
        }

        private void LateUpdate()
        {
            if (_identity == null || _renderers == null)
            {
                return;
            }

            if (!_identity.CanBeGathered)
            {
                Apply(1f);
                enabled = false;
                return;
            }

            // A shared phase, not a per-object one: a field of tufts blinking
            // out of step reads as broken rather than as a set of pickups.
            var wave = 0.5f - 0.5f * Mathf.Cos(
                Time.time * cyclesPerSecond * 2f * Mathf.PI);
            Apply(Mathf.Lerp(minimumGain, maximumGain, wave));
        }

        private void Apply(float gain)
        {
            if (_renderers == null
                || Mathf.Approximately(gain, _appliedGain))
            {
                return;
            }

            _appliedGain = gain;
            var colour = new Color(gain, gain, gain, 1f);
            for (var index = 0; index < _renderers.Length; index++)
            {
                var renderer = _renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, colour);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}

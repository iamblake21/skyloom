using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CML.Unity.Presentation.Intro
{
    /// <summary>
    /// Per-frame look of a cinematic shot. Everything the director wants to
    /// animate on the image lives here so a shot only has to describe how it
    /// should look, not which override owns which field.
    /// </summary>
    public struct IntroGradeState
    {
        public float BloomIntensity;
        public float BloomThreshold;
        public Color BloomTint;
        public float ChromaticAberration;
        public float LensDistortion;
        public float VignetteIntensity;
        public Color VignetteColor;
        public float MotionBlur;
        public float FilmGrain;
        public float PostExposure;
        public float Contrast;
        public float Saturation;
        public Color ColorFilter;
        public float Panini;

        /// <summary>
        /// Neutral cruise look. Every shot starts from this and pushes only the
        /// two or three values that carry its intent.
        /// </summary>
        public static IntroGradeState Cruise()
        {
            return new IntroGradeState
            {
                BloomIntensity = 1.15f,
                BloomThreshold = 0.82f,
                BloomTint = new Color(0.78f, 0.88f, 1f, 1f),
                ChromaticAberration = 0.08f,
                LensDistortion = 0f,
                VignetteIntensity = 0.28f,
                VignetteColor = Color.black,
                MotionBlur = 0.12f,
                FilmGrain = 0.18f,
                PostExposure = 0f,
                Contrast = 12f,
                Saturation = 6f,
                ColorFilter = Color.white,
                Panini = 0f
            };
        }

        public static IntroGradeState Lerp(
            in IntroGradeState from,
            in IntroGradeState to,
            float t)
        {
            t = Mathf.Clamp01(t);
            return new IntroGradeState
            {
                BloomIntensity = Mathf.Lerp(from.BloomIntensity, to.BloomIntensity, t),
                BloomThreshold = Mathf.Lerp(from.BloomThreshold, to.BloomThreshold, t),
                BloomTint = Color.Lerp(from.BloomTint, to.BloomTint, t),
                ChromaticAberration = Mathf.Lerp(
                    from.ChromaticAberration,
                    to.ChromaticAberration,
                    t),
                LensDistortion = Mathf.Lerp(from.LensDistortion, to.LensDistortion, t),
                VignetteIntensity = Mathf.Lerp(
                    from.VignetteIntensity,
                    to.VignetteIntensity,
                    t),
                VignetteColor = Color.Lerp(from.VignetteColor, to.VignetteColor, t),
                MotionBlur = Mathf.Lerp(from.MotionBlur, to.MotionBlur, t),
                FilmGrain = Mathf.Lerp(from.FilmGrain, to.FilmGrain, t),
                PostExposure = Mathf.Lerp(from.PostExposure, to.PostExposure, t),
                Contrast = Mathf.Lerp(from.Contrast, to.Contrast, t),
                Saturation = Mathf.Lerp(from.Saturation, to.Saturation, t),
                ColorFilter = Color.Lerp(from.ColorFilter, to.ColorFilter, t),
                Panini = Mathf.Lerp(from.Panini, to.Panini, t)
            };
        }
    }

    /// <summary>
    /// Thin binding over the cinematic <see cref="Volume"/>. Reading
    /// <see cref="Volume.profile"/> instantiates a runtime copy, so animating
    /// these overrides never writes back into the authored profile asset.
    /// </summary>
    public sealed class IntroCinematicGrade
    {
        private readonly Volume _volume;
        private readonly Bloom _bloom;
        private readonly ChromaticAberration _chromaticAberration;
        private readonly LensDistortion _lensDistortion;
        private readonly Vignette _vignette;
        private readonly MotionBlur _motionBlur;
        private readonly FilmGrain _filmGrain;
        private readonly ColorAdjustments _colorAdjustments;
        private readonly PaniniProjection _panini;

        private IntroCinematicGrade(Volume volume, VolumeProfile profile)
        {
            _volume = volume;
            profile.TryGet(out _bloom);
            profile.TryGet(out _chromaticAberration);
            profile.TryGet(out _lensDistortion);
            profile.TryGet(out _vignette);
            profile.TryGet(out _motionBlur);
            profile.TryGet(out _filmGrain);
            profile.TryGet(out _colorAdjustments);
            profile.TryGet(out _panini);
            EnableOverrides();
        }

        public static IntroCinematicGrade TryCreate(Volume volume)
        {
            if (volume == null)
            {
                return null;
            }

            var profile = volume.profile;
            return profile == null ? null : new IntroCinematicGrade(volume, profile);
        }

        public void SetActive(bool active)
        {
            if (_volume != null)
            {
                _volume.enabled = active;
                _volume.weight = active ? 1f : 0f;
            }
        }

        /// <summary>
        /// Blends the cinematic look back out so handing control to the island
        /// is a dissolve and not a one-frame pop between two colour grades.
        /// </summary>
        public void SetWeight(float weight)
        {
            if (_volume != null)
            {
                _volume.weight = Mathf.Clamp01(weight);
            }
        }

        public void Apply(in IntroGradeState state)
        {
            if (_bloom != null)
            {
                _bloom.intensity.value = Mathf.Max(0f, state.BloomIntensity);
                _bloom.threshold.value = Mathf.Max(0f, state.BloomThreshold);
                _bloom.tint.value = state.BloomTint;
            }

            if (_chromaticAberration != null)
            {
                _chromaticAberration.intensity.value =
                    Mathf.Clamp01(state.ChromaticAberration);
            }

            if (_lensDistortion != null)
            {
                _lensDistortion.intensity.value =
                    Mathf.Clamp(state.LensDistortion, -1f, 1f);
                // Barrel distortion pushes the frame edges out of view. Scaling
                // back up keeps the composition intact while the lens bends.
                _lensDistortion.scale.value =
                    1f + Mathf.Max(0f, state.LensDistortion) * 0.22f;
            }

            if (_vignette != null)
            {
                _vignette.intensity.value = Mathf.Clamp01(state.VignetteIntensity);
                _vignette.color.value = state.VignetteColor;
            }

            if (_motionBlur != null)
            {
                _motionBlur.intensity.value = Mathf.Clamp01(state.MotionBlur);
            }

            if (_filmGrain != null)
            {
                _filmGrain.intensity.value = Mathf.Clamp01(state.FilmGrain);
            }

            if (_colorAdjustments != null)
            {
                _colorAdjustments.postExposure.value = state.PostExposure;
                _colorAdjustments.contrast.value =
                    Mathf.Clamp(state.Contrast, -100f, 100f);
                _colorAdjustments.saturation.value =
                    Mathf.Clamp(state.Saturation, -100f, 100f);
                _colorAdjustments.colorFilter.value = state.ColorFilter;
            }

            if (_panini != null)
            {
                _panini.distance.value = Mathf.Clamp01(state.Panini);
            }
        }

        private void EnableOverrides()
        {
            if (_bloom != null)
            {
                _bloom.active = true;
                _bloom.intensity.overrideState = true;
                _bloom.threshold.overrideState = true;
                _bloom.scatter.overrideState = true;
                _bloom.tint.overrideState = true;
            }

            if (_chromaticAberration != null)
            {
                _chromaticAberration.active = true;
                _chromaticAberration.intensity.overrideState = true;
            }

            if (_lensDistortion != null)
            {
                _lensDistortion.active = true;
                _lensDistortion.intensity.overrideState = true;
                _lensDistortion.scale.overrideState = true;
            }

            if (_vignette != null)
            {
                _vignette.active = true;
                _vignette.intensity.overrideState = true;
                _vignette.smoothness.overrideState = true;
                _vignette.color.overrideState = true;
            }

            if (_motionBlur != null)
            {
                _motionBlur.active = true;
                _motionBlur.mode.overrideState = true;
                _motionBlur.intensity.overrideState = true;
                _motionBlur.clamp.overrideState = true;
            }

            if (_filmGrain != null)
            {
                _filmGrain.active = true;
                _filmGrain.type.overrideState = true;
                _filmGrain.intensity.overrideState = true;
                _filmGrain.response.overrideState = true;
            }

            if (_colorAdjustments != null)
            {
                _colorAdjustments.active = true;
                _colorAdjustments.postExposure.overrideState = true;
                _colorAdjustments.contrast.overrideState = true;
                _colorAdjustments.saturation.overrideState = true;
                _colorAdjustments.colorFilter.overrideState = true;
            }

            if (_panini != null)
            {
                _panini.active = true;
                _panini.distance.overrideState = true;
                _panini.cropToFit.overrideState = true;
            }
        }
    }
}

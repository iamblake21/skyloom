using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CML.Unity.World
{
    /// <summary>
    /// Drives the Starter Island lighting, ambient response, fog, atmospheric
    /// sky and bloom from the measured 24-hour reference profile. The source
    /// key times are preserved. The sky receives the source material inputs
    /// directly; only engine-owned systems such as URP fog, ambient probes and
    /// bloom require explicit translation.
    /// </summary>
    // Environment-only translation; the source reference assets stay external.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MeasuredStylizedDaylight : MonoBehaviour
    {
        private const string PreferredAuthorityName =
            "ENV_MeasuredStylizedDaylight";
        private const float SourceSunIntensity = 1f;
        private const float SourceSunYawDegrees = 180f;

        private static readonly List<MeasuredStylizedDaylight>
            RegisteredControllers = new List<MeasuredStylizedDaylight>();
        private static MeasuredStylizedDaylight activeAuthority;
        private static long nextExplicitAuthorityClaim;

        [Header("Scene References")]
        [SerializeField] private Light sun;
        [SerializeField] private Material skyboxMaterial;
        [SerializeField] private Volume postProcessVolume;

        [Header("Clock")]
        [SerializeField, Range(0f, 24f)] private float timeOfDayHours = 12f;
        [SerializeField] private bool advanceClockInPlayMode = true;
        [SerializeField, Min(10f)] private float secondsPerFullDay = 1200f;
        [SerializeField, Min(0.25f)] private float environmentRefreshSeconds = 5f;

        [Header("Approved URP Daylight Baseline")]
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.92f;

        [SerializeField] private Color fogColor =
            new Color(0.62f, 0.84f, 0.86f, 1f);
        [SerializeField] private Color minimumNightFogColor =
            new Color(0.055f, 0.105f, 0.15f, 1f);
        [SerializeField, Min(0f)] private float fogStart = 200f;
        [SerializeField, Min(0f)] private float fogEnd = 1100f;

        private Material runtimeSkyboxMaterial;
        private Material runtimeSkyboxSourceMaterial;
        private Shader runtimeSkyboxSourceShader;
        private Volume resolvedPostProcessVolume;
        private GameObject runtimePostProcessOwner;
        private VolumeProfile runtimePostProcessProfile;
        private Bloom runtimeBloom;
        private Color baselineBloomTint = Color.white;
        private float baselineBloomIntensity = 0.18f;

        private bool capturedSceneBaseline;
        private Material baselineSkybox;
        private Light baselineRenderSun;
        private AmbientMode baselineAmbientMode;
        private Color baselineAmbientLight;
        private Color baselineAmbientSkyColor;
        private Color baselineAmbientEquatorColor;
        private Color baselineAmbientGroundColor;
        private float baselineAmbientIntensity;
        private SphericalHarmonicsL2 baselineAmbientProbe;
        private bool baselineFogEnabled;
        private FogMode baselineFogMode;
        private Color baselineFogColor;
        private float baselineFogDensity;
        private float baselineFogStartDistance;
        private float baselineFogEndDistance;
        private DefaultReflectionMode baselineReflectionMode;
        private Texture baselineCustomReflectionTexture;
        private float baselineReflectionIntensity;
        private bool capturedSunBaseline;
        private Light baselineDrivenSun;
        private LightType baselineSunType;
        private Quaternion baselineSunRotation;
        private Color baselineSunColor;
        private float baselineSunIntensity;
        private bool baselineSunUsesColorTemperature;
        private LightShadows baselineSunShadows;
        private float baselineSunShadowStrength;
        private float baselineSunShadowBias;
        private float baselineSunShadowNormalBias;
        private float nextEnvironmentRefreshTime;
#if UNITY_EDITOR
        private bool deferredValidationPending;
#endif
        private long explicitAuthorityClaim;

        public float TimeOfDayHours => timeOfDayHours;
        public bool IsClockRunning => advanceClockInPlayMode;
        public float DayFactor =>
            SolarpunkDayNightProfile.Evaluate(timeOfDayHours).DayFactor;
        public bool IsEnvironmentAuthority =>
            activeAuthority == this && isActiveAndEnabled;
        public static MeasuredStylizedDaylight ActiveAuthority =>
            activeAuthority != null && activeAuthority.isActiveAndEnabled
                ? activeAuthority
                : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAuthorityRegistry()
        {
            var previousAuthority = activeAuthority;
            activeAuthority = null;
            RegisteredControllers.Clear();
            nextExplicitAuthorityClaim = 0;

            if (previousAuthority != null)
            {
                previousAuthority.ReleaseRuntimeResources();
            }
        }

        private static void RegisterController(
            MeasuredStylizedDaylight controller)
        {
            PruneRegisteredControllers();
            if (!ContainsRegisteredController(controller))
            {
                RegisteredControllers.Add(controller);
            }

            ReevaluateAuthority();
        }

        private static void UnregisterController(
            MeasuredStylizedDaylight controller)
        {
            for (var index = RegisteredControllers.Count - 1;
                 index >= 0;
                 index--)
            {
                if (RegisteredControllers[index] == null ||
                    RegisteredControllers[index] == controller)
                {
                    RegisteredControllers.RemoveAt(index);
                }
            }

            if (activeAuthority != controller)
            {
                return;
            }

            activeAuthority = null;
            controller.ReleaseRuntimeResources();
            ReevaluateAuthority();
        }

        private static bool ContainsRegisteredController(
            MeasuredStylizedDaylight controller)
        {
            for (var index = 0;
                 index < RegisteredControllers.Count;
                 index++)
            {
                if (RegisteredControllers[index] == controller)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PruneRegisteredControllers()
        {
            for (var index = RegisteredControllers.Count - 1;
                 index >= 0;
                 index--)
            {
                var controller = RegisteredControllers[index];
                if (controller == null || !controller.isActiveAndEnabled)
                {
                    RegisteredControllers.RemoveAt(index);
                }
            }
        }

        private static void ReevaluateAuthority()
        {
            PruneRegisteredControllers();
            MeasuredStylizedDaylight best = null;
            for (var index = 0;
                 index < RegisteredControllers.Count;
                 index++)
            {
                var candidate = RegisteredControllers[index];
                if (best == null || IsPreferredOver(candidate, best))
                {
                    best = candidate;
                }
            }

            SetAuthority(best);
        }

        private static void SetAuthority(
            MeasuredStylizedDaylight nextAuthority)
        {
            if (activeAuthority == nextAuthority)
            {
                return;
            }

            var previousAuthority = activeAuthority;
            activeAuthority = null;
            if (previousAuthority != null)
            {
                // Restore the scene before the replacement captures its own
                // baseline. This keeps ownership transfers reversible and
                // prevents a standby from inheriting another clone/overlay.
                previousAuthority.ReleaseRuntimeResources();
            }

            activeAuthority = nextAuthority;
            if (activeAuthority != null)
            {
                activeAuthority.ApplyAsAuthority();
            }
        }

        private static bool IsPreferredOver(
            MeasuredStylizedDaylight candidate,
            MeasuredStylizedDaylight current)
        {
            if (candidate.explicitAuthorityClaim !=
                current.explicitAuthorityClaim)
            {
                return candidate.explicitAuthorityClaim >
                    current.explicitAuthorityClaim;
            }

            var candidateHasPreferredName = candidate.gameObject.name ==
                PreferredAuthorityName;
            var currentHasPreferredName = current.gameObject.name ==
                PreferredAuthorityName;
            if (candidateHasPreferredName != currentHasPreferredName)
            {
                return candidateHasPreferredName;
            }

            var candidateKey = BuildHierarchyAuthorityKey(candidate);
            var currentKey = BuildHierarchyAuthorityKey(current);
            var keyComparison = System.String.CompareOrdinal(
                candidateKey,
                currentKey);
            if (keyComparison != 0)
            {
                return keyComparison < 0;
            }

            return candidate.GetInstanceID() < current.GetInstanceID();
        }

        private static string BuildHierarchyAuthorityKey(
            MeasuredStylizedDaylight controller)
        {
            var key = string.Empty;
            var current = controller.transform;
            while (current != null)
            {
                key = current.GetSiblingIndex().ToString("D6") + "/" + key;
                current = current.parent;
            }

            var scene = controller.gameObject.scene;
            return (scene.path ?? string.Empty) + "|" +
                scene.handle.ToString("D8") + "|" + key;
        }

        private bool EnsureRegisteredAuthority()
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            if (!ContainsRegisteredController(this))
            {
                RegisterController(this);
            }

            return IsEnvironmentAuthority;
        }

        private void ApplyAsAuthority()
        {
            if (!IsEnvironmentAuthority)
            {
                return;
            }

            EnsureRuntimeResources();
            ApplyCurrentSample();
            RefreshEnvironment();
        }

        public void Configure(Light directionalLight, Material skybox)
        {
            Configure(directionalLight, skybox, null);
        }

        public void Configure(
            Light directionalLight,
            Material skybox,
            Volume volume = null)
        {
            var retainedAuthority = IsEnvironmentAuthority;
            if (retainedAuthority)
            {
                ReleaseRuntimeResources();
            }

            sun = directionalLight;
            skyboxMaterial = skybox;
            postProcessVolume = volume;
            resolvedPostProcessVolume = null;
            explicitAuthorityClaim = ++nextExplicitAuthorityClaim;

            if (!isActiveAndEnabled)
            {
                return;
            }

            RegisterController(this);
            SetAuthority(this);
            if (retainedAuthority)
            {
                Apply();
            }
        }

        public void SetTimeOfDay(float hour)
        {
            timeOfDayHours = SolarpunkDayNightProfile.WrapHour(hour);
            Apply();
        }

        public void AddHours(float hours)
        {
            SetTimeOfDay(timeOfDayHours + hours);
        }

        public void SetClockRunning(bool running)
        {
            advanceClockInPlayMode = running;
        }

        public void Apply()
        {
            if (!EnsureRegisteredAuthority())
            {
                return;
            }

            EnsureRuntimeResources();
            ApplyCurrentSample();
            RefreshEnvironment();
        }

        /// <summary>
        /// Recreates only the hidden skybox material clone from the authored
        /// material, then reapplies the current sample. Editor import hooks
        /// call this after shader/material imports have fully completed so a
        /// stale preview clone cannot survive a source asset change.
        /// </summary>
        /// <remarks>
        /// This deliberately preserves the captured scene baseline and the
        /// runtime post-processing overlay. It must not be called directly
        /// from OnValidate while Unity is importing or compiling assets.
        /// </remarks>
        public bool RebuildSkyboxPreview()
        {
            if (!EnsureRegisteredAuthority())
            {
                return false;
            }

#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorApplication.isUpdating)
            {
                return false;
            }
#endif
            if (skyboxMaterial == null ||
                !HasSkyMaterialContract(skyboxMaterial))
            {
                return false;
            }

            if (!TryReplaceRuntimeSkyboxMaterial())
            {
                return false;
            }

            ApplySkyboxCurrentSample();
            RefreshEnvironment();
            return RenderSettings.skybox == runtimeSkyboxMaterial;
        }

        private void ApplySkyboxCurrentSample()
        {
            if (!IsEnvironmentAuthority)
            {
                return;
            }

            var sample = SolarpunkDayNightProfile.Evaluate(timeOfDayHours);
            var sourceSunRotation = EvaluateSourceSunRotation(
                sample.SunPosition,
                SourceSunYawDegrees);
            ApplySkybox(sample, sourceSunRotation);
        }

        private void ApplyCurrentSample()
        {
            if (!IsEnvironmentAuthority)
            {
                return;
            }

            var sample = SolarpunkDayNightProfile.Evaluate(timeOfDayHours);
            var noon = SolarpunkDayNightProfile.Evaluate(12f);
            EvaluateTwilightWeights(
                sample.Hour,
                out var dawnWeight,
                out var duskWeight);
            var twilightWeight = Mathf.Max(dawnWeight, duskWeight);
            var nightWeight = 1f - sample.DayFactor;
            var sourceSunRotation = EvaluateSourceSunRotation(
                sample.SunPosition,
                SourceSunYawDegrees);
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.transform.rotation = sourceSunRotation;
                // Source curves are FLinearColor. Unity's Light.color is an
                // authored sRGB value and VisibleLight converts it back into
                // the active linear space for the renderer.
                sun.color = sample.SunLightColor.gamma;
                sun.intensity = SourceSunIntensity;
                sun.useColorTemperature = false;
                sun.shadows = LightShadows.Soft;
                var twilightShadowStrength = Mathf.Lerp(
                    shadowStrength,
                    0.38f,
                    twilightWeight);
                sun.shadowStrength = Mathf.Lerp(
                    twilightShadowStrength,
                    0.42f,
                    nightWeight);
                sun.shadowBias = 0.025f;
                sun.shadowNormalBias = 0.2f;
                RenderSettings.sun = sun;
            }

            // The source uses a specified HDR cubemap, not Trilight colors.
            // Its diffuse irradiance is retained as a compact L2 probe and
            // tinted by the extracted linear SkyLight curve at runtime.
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientIntensity = 1f;
            // The source foliage/terrain materials also receive a global
            // emissive fill that our unrelated Unity materials do not expose.
            // Compensate once in the diffuse environment so twilight remains
            // readable without making individual assets emissive.
            var ambientBoost =
                1f +
                dawnWeight * 2.30f +
                duskWeight * 2.10f +
                nightWeight * 0.80f;
            var ambientTint = sample.SkyLightColor * ambientBoost;
            ambientTint.a = 1f;
            RenderSettings.ambientProbe =
                SolarpunkAmbientProbe.Evaluate(ambientTint);

            var translatedFog = TranslateRelative(
                sample.FogInscatteringColor,
                noon.FogInscatteringColor,
                fogColor);
            var nightFloor = minimumNightFogColor * (1f - sample.DayFactor);
            translatedFog = MaxRgb(translatedFog, nightFloor);
            var densityBlend = Mathf.InverseLerp(0.08f, 0.12f, sample.FogDensity);
            var falloffBlend = Mathf.InverseLerp(0.05f, 0.12f, sample.FogFalloff);
            var fogBlend = densityBlend * 0.65f + falloffBlend * 0.35f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ClampColor(translatedFog);
            RenderSettings.fogStartDistance = Mathf.Lerp(fogStart + 100f, fogStart, fogBlend);
            RenderSettings.fogEndDistance = Mathf.Max(
                RenderSettings.fogStartDistance + 1f,
                Mathf.Lerp(fogEnd + 350f, fogEnd, fogBlend));
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            ApplySkybox(sample, sourceSunRotation);
            ApplyPostProcessing();
        }

        private void ApplySkybox(
            SolarpunkDayNightSample sample,
            Quaternion sourceSunRotation)
        {
            var skybox = ActiveSkybox;
            if (skybox == null)
            {
                return;
            }

            if (!HasSkyMaterialContract(skybox))
            {
                Debug.LogError(
                    "MeasuredStylizedDaylight requires the procedural " +
                    "StarterIsland atmospheric sky shader.",
                    this);
                return;
            }

            RenderSettings.skybox = skybox;
            // Keep source FLinearColor values as vectors: Unity Color
            // properties carry color-space metadata, while these values are
            // already linear shader data and must not be decoded a second time.
            // Display phases are clock-driven so identical RGB values on the
            // morning and evening curves can never swap their visual role.
            // Clear weather supplies zero for rain and snow.
            skybox.SetVector(
                "_SkyTopColorLinear",
                ToLinearVector(sample.SkyTopColor));
            skybox.SetVector(
                "_HorizonColorLinear",
                ToLinearVector(sample.HorizonColor));
            skybox.SetFloat("_Day01", sample.DayFactor);
            EvaluateDisplayPhases(
                sample.Hour,
                sample.DayFactor,
                out var noonPhase,
                out var dawnPhase,
                out var earlyDuskPhase,
                out var lateDuskPhase);
            skybox.SetFloat("_NoonPhase", noonPhase);
            skybox.SetFloat("_DawnPhase", dawnPhase);
            skybox.SetFloat("_EarlyDuskPhase", earlyDuskPhase);
            skybox.SetFloat("_LateDuskPhase", lateDuskPhase);
            skybox.SetFloat("_CloudAmount", sample.CloudOpacity);
            skybox.SetVector(
                "_CloudTopColorLinear",
                ToLinearVector(sample.CloudLayerTopColor));
            skybox.SetVector(
                "_CloudBottomColorLinear",
                ToLinearVector(sample.CloudLayerBottomColor));
            skybox.SetFloat("_RainFade1Sunny0", 0f);
            skybox.SetFloat("_SnowHailClouds", 0f);
            skybox.SetVector(
                "_SunDiscColorLinear",
                ToLinearVector(sample.SunDiscColor));
            skybox.SetVector(
                "_FogInscatteringColorLinear",
                ToLinearVector(sample.FogInscatteringColor));
            skybox.SetVector(
                "_FogDirectionalColorLinear",
                ToLinearVector(sample.FogDirectionalColor));
            skybox.SetFloat("_FogDensity", sample.FogDensity);
            skybox.SetFloat("_FogFalloff", sample.FogFalloff);

            var lightDirection = sourceSunRotation * Vector3.forward;
            // The source pixel shader dots its emitted light ray against
            // sky-surface-to-camera. Our skybox fragment uses the opposite
            // camera-to-sky view ray, so provide the negated light ray once.
            skybox.SetVector(
                "_SunDirectionWS",
                new Vector4(
                    -lightDirection.x,
                    -lightDirection.y,
                    -lightDirection.z,
                0f));
        }

        private void ApplyPostProcessing()
        {
            if (runtimeBloom == null)
            {
                return;
            }

            // C_Bloom in the source drives directional light-shaft tint, not
            // the global post-process Bloom tint. Keep the approved Volume
            // baseline here; the sky shader owns its localized solar glow.
            runtimeBloom.tint.Override(baselineBloomTint);
            runtimeBloom.intensity.Override(baselineBloomIntensity);
        }

        private void EnsureRuntimeResources()
        {
            if (!IsEnvironmentAuthority)
            {
                return;
            }

            // A reference change on the active controller must first restore
            // the previously driven light. Otherwise the new light would be
            // modified without ever having its own baseline captured.
            if (capturedSceneBaseline && baselineDrivenSun != sun)
            {
                ReleaseRuntimeResources();
            }

            CaptureSceneBaseline();
            CreateRuntimeSkyboxMaterial();

            if (runtimePostProcessOwner != null)
            {
                return;
            }

            resolvedPostProcessVolume = postProcessVolume != null
                ? postProcessVolume
                : FindHighestPriorityGlobalVolume();
            if (resolvedPostProcessVolume == null)
            {
                return;
            }

            var baseProfile = resolvedPostProcessVolume.sharedProfile;
            if (baseProfile != null && baseProfile.TryGet(out Bloom baseBloom))
            {
                baselineBloomTint = baseBloom.tint.value;
                baselineBloomIntensity = baseBloom.intensity.value;
            }

            runtimePostProcessProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            runtimePostProcessProfile.name = "CML Day Night Runtime Overrides";
            runtimePostProcessProfile.hideFlags = HideFlags.HideAndDontSave;
            runtimeBloom = runtimePostProcessProfile.Add<Bloom>(false);
            runtimeBloom.active = true;
            runtimeBloom.tint.overrideState = true;
            runtimeBloom.intensity.overrideState = true;

            runtimePostProcessOwner = new GameObject(
                "CML Day Night Runtime Post Process")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = resolvedPostProcessVolume.gameObject.layer
            };
            var overlay = runtimePostProcessOwner.AddComponent<Volume>();
            overlay.isGlobal = true;
            overlay.priority = resolvedPostProcessVolume.priority + 100f;
            overlay.weight = 1f;
            overlay.sharedProfile = runtimePostProcessProfile;
        }

        private void CreateRuntimeSkyboxMaterial()
        {
            if (skyboxMaterial == null)
            {
                return;
            }

            var sourceShader = skyboxMaterial.shader;
            var cloneMatchesSource = runtimeSkyboxMaterial != null &&
                runtimeSkyboxSourceMaterial == skyboxMaterial &&
                runtimeSkyboxSourceShader == sourceShader &&
                runtimeSkyboxMaterial.shader == sourceShader &&
                HasSkyMaterialContract(runtimeSkyboxMaterial);
            if (cloneMatchesSource)
            {
                return;
            }

            // During a shader import Unity can expose the authored material
            // before its property table is complete. Keep the last known-good
            // clone alive until a complete replacement can be constructed.
            if (!HasSkyMaterialContract(skyboxMaterial))
            {
                return;
            }

            TryReplaceRuntimeSkyboxMaterial();
        }

        private bool TryReplaceRuntimeSkyboxMaterial()
        {
            if (skyboxMaterial == null ||
                !HasSkyMaterialContract(skyboxMaterial))
            {
                return false;
            }

            var replacement = new Material(skyboxMaterial)
            {
                name = skyboxMaterial.name + " (Day Night Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };

            if (!HasSkyMaterialContract(replacement))
            {
                DestroyRuntimeObject(replacement);
                return false;
            }

            var previous = runtimeSkyboxMaterial;
            runtimeSkyboxMaterial = replacement;
            runtimeSkyboxSourceMaterial = skyboxMaterial;
            runtimeSkyboxSourceShader = skyboxMaterial.shader;

            // Keep RenderSettings valid throughout the swap. The current
            // dynamic sample is applied by the caller immediately afterwards.
            if (previous != null && RenderSettings.skybox == previous)
            {
                RenderSettings.skybox = replacement;
            }

            DestroyRuntimeObject(previous);
            return true;
        }

        private Material ActiveSkybox =>
            runtimeSkyboxMaterial != null
                ? runtimeSkyboxMaterial
                : skyboxMaterial;

        private static Volume FindHighestPriorityGlobalVolume()
        {
            var volumes = FindObjectsByType<Volume>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Volume best = null;
            for (var index = 0; index < volumes.Length; index++)
            {
                var candidate = volumes[index];
                if (candidate == null ||
                    !candidate.isActiveAndEnabled ||
                    !candidate.isGlobal ||
                    candidate.gameObject.name ==
                        "CML Day Night Runtime Post Process")
                {
                    continue;
                }

                if (best == null || candidate.priority > best.priority)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static Color TranslateRelative(
            Color source,
            Color sourceNoon,
            Color targetNoon)
        {
            var sourceLuminance = Luminance(source);
            var sourceNoonLuminance = Mathf.Max(0.0001f, Luminance(sourceNoon));
            var targetNoonLuminance = Luminance(targetNoon);
            var targetLuminance = targetNoonLuminance *
                (sourceLuminance / sourceNoonLuminance);

            // Preserve the reference hue movement without allowing a single
            // channel ratio to blow out. The former per-channel scaling could
            // turn sunset skies several times brighter than noon and create
            // incoherent Trilight SH values that evaluated to black on some
            // normals.
            var relativeTint = new Color(
                RelativeChroma(source.r, sourceLuminance, sourceNoon.r, sourceNoonLuminance),
                RelativeChroma(source.g, sourceLuminance, sourceNoon.g, sourceNoonLuminance),
                RelativeChroma(source.b, sourceLuminance, sourceNoon.b, sourceNoonLuminance),
                1f);
            var tinted = MultiplyRgb(targetNoon, relativeTint);
            var tintedLuminance = Mathf.Max(0.0001f, Luminance(tinted));
            return ClampColor(tinted * (targetLuminance / tintedLuminance));
        }

        public static Quaternion EvaluateSourceSunRotation(
            float sourcePitchDegrees,
            float sourceYawDegrees)
        {
            // Unreal's RotatorToVector uses +Pitch to raise the forward ray,
            // while Unity's Quaternion.Euler(+X) pitches Vector3.forward
            // downward. Invert the extracted UE pitch exactly once so the
            // directional-light ray has the same vertical component.
            return Quaternion.Euler(
                -sourcePitchDegrees,
                sourceYawDegrees,
                0f);
        }

        /// <summary>
        /// Verifies the complete material interface required by the daylight
        /// driver, including the authored controls that keep procedural clouds
        /// visible and animated. Editor import hooks use the same contract so
        /// readiness checks cannot drift from runtime behavior.
        /// </summary>
        public static bool HasSkyMaterialContract(Material material)
        {
            return material != null &&
                material.shader != null &&
                material.HasProperty("_SkyTopColorLinear") &&
                material.HasProperty("_HorizonColorLinear") &&
                material.HasProperty("_Day01") &&
                material.HasProperty("_NoonPhase") &&
                material.HasProperty("_DawnPhase") &&
                material.HasProperty("_EarlyDuskPhase") &&
                material.HasProperty("_LateDuskPhase") &&
                material.HasProperty("_SubsurfaceToUnlitScale") &&
                material.HasProperty("_Exposure") &&
                material.HasProperty("_CloudAmount") &&
                material.HasProperty("_CloudTopColorLinear") &&
                material.HasProperty("_CloudBottomColorLinear") &&
                material.HasProperty("_CloudColor") &&
                material.HasProperty("_CloudShadowColor") &&
                material.HasProperty("_CloudScale") &&
                material.HasProperty("_CloudCoverage") &&
                material.HasProperty("_CloudSoftness") &&
                material.HasProperty("_CloudSpeed") &&
                material.HasProperty("_CloudOpacity") &&
                material.HasProperty("_RainFade1Sunny0") &&
                material.HasProperty("_SnowHailClouds") &&
                material.HasProperty("_SunDiscColorLinear") &&
                material.HasProperty("_FogInscatteringColorLinear") &&
                material.HasProperty("_FogDirectionalColorLinear") &&
                material.HasProperty("_FogDensity") &&
                material.HasProperty("_FogFalloff") &&
                material.HasProperty("_SunDirectionWS");
        }

        private static void EvaluateTwilightWeights(
            float hour,
            out float dawn,
            out float dusk)
        {
            dawn = SmoothPulse(hour, 4.5f, 5.7f, 7.5f);
            dusk = SmoothPulse(hour, 19f, 21.15f, 22f);
        }

        private static void EvaluateDisplayPhases(
            float hour,
            float dayFactor,
            out float noon,
            out float dawn,
            out float earlyDusk,
            out float lateDusk)
        {
            dawn = SmoothPulse(hour, 4.5f, 5.7f, 7.5f);

            // Dusk owns the complete 19:00-22:00 handoff. DayFactor supplies
            // the source-authored fade into night after 21:30, so no residual
            // noon color can reappear immediately before darkness.
            var duskEnvelope =
                SmoothRange(hour, 19f, 21f) * Mathf.Clamp01(dayFactor);
            var lateBlend = SmoothRange(hour, 21f, 21.5f);
            earlyDusk = duskEnvelope * (1f - lateBlend);
            lateDusk = duskEnvelope * lateBlend;

            var noonFade = 1f - SmoothRange(hour, 19f, 21f);
            noon =
                Mathf.Clamp01(dayFactor) *
                (1f - dawn) *
                noonFade;
        }

        private static float SmoothRange(
            float value,
            float start,
            float end)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start, end, value));
        }

        private static float SmoothPulse(
            float value,
            float start,
            float peak,
            float end)
        {
            if (value <= start || value >= end)
            {
                return 0f;
            }

            if (value <= peak)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(start, peak, value));
            }

            return Mathf.SmoothStep(
                1f,
                0f,
                Mathf.InverseLerp(peak, end, value));
        }

        private static float RelativeChroma(
            float value,
            float luminance,
            float noonValue,
            float noonLuminance)
        {
            var chroma = value / Mathf.Max(0.0001f, luminance);
            var noonChroma = noonValue / Mathf.Max(0.0001f, noonLuminance);
            return Mathf.Clamp(chroma / Mathf.Max(0.0001f, noonChroma), 0.25f, 4f);
        }

        private static float Luminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        private static Vector4 ToLinearVector(Color value)
        {
            return new Vector4(value.r, value.g, value.b, value.a);
        }

        private static Color MultiplyRgb(Color left, Color right)
        {
            return new Color(
                left.r * right.r,
                left.g * right.g,
                left.b * right.b,
                1f);
        }

        private static Color ClampColor(Color color)
        {
            return new Color(
                Mathf.Clamp(color.r, 0f, 4f),
                Mathf.Clamp(color.g, 0f, 4f),
                Mathf.Clamp(color.b, 0f, 4f),
                1f);
        }

        private static Color MaxRgb(Color value, Color minimum)
        {
            return new Color(
                Mathf.Max(value.r, minimum.r),
                Mathf.Max(value.g, minimum.g),
                Mathf.Max(value.b, minimum.b),
                1f);
        }

        private void CaptureSceneBaseline()
        {
            if (capturedSceneBaseline)
            {
                return;
            }

            baselineSkybox = RenderSettings.skybox;
            baselineRenderSun = RenderSettings.sun;
            baselineAmbientMode = RenderSettings.ambientMode;
            baselineAmbientLight = RenderSettings.ambientLight;
            baselineAmbientSkyColor = RenderSettings.ambientSkyColor;
            baselineAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            baselineAmbientGroundColor = RenderSettings.ambientGroundColor;
            baselineAmbientIntensity = RenderSettings.ambientIntensity;
            baselineAmbientProbe = RenderSettings.ambientProbe;
            baselineFogEnabled = RenderSettings.fog;
            baselineFogMode = RenderSettings.fogMode;
            baselineFogColor = RenderSettings.fogColor;
            baselineFogDensity = RenderSettings.fogDensity;
            baselineFogStartDistance = RenderSettings.fogStartDistance;
            baselineFogEndDistance = RenderSettings.fogEndDistance;
            baselineReflectionMode = RenderSettings.defaultReflectionMode;
            baselineCustomReflectionTexture =
                RenderSettings.customReflectionTexture;
            baselineReflectionIntensity = RenderSettings.reflectionIntensity;

            baselineDrivenSun = sun;
            if (baselineDrivenSun != null)
            {
                capturedSunBaseline = true;
                baselineSunType = baselineDrivenSun.type;
                baselineSunRotation = baselineDrivenSun.transform.rotation;
                baselineSunColor = baselineDrivenSun.color;
                baselineSunIntensity = baselineDrivenSun.intensity;
                baselineSunUsesColorTemperature =
                    baselineDrivenSun.useColorTemperature;
                baselineSunShadows = baselineDrivenSun.shadows;
                baselineSunShadowStrength = baselineDrivenSun.shadowStrength;
                baselineSunShadowBias = baselineDrivenSun.shadowBias;
                baselineSunShadowNormalBias = baselineDrivenSun.shadowNormalBias;
            }

            capturedSceneBaseline = true;
        }

        private void RestoreSceneBaseline()
        {
            if (!capturedSceneBaseline)
            {
                return;
            }

            RenderSettings.skybox = baselineSkybox;
            RenderSettings.sun = baselineRenderSun;
            RenderSettings.ambientMode = baselineAmbientMode;
            RenderSettings.ambientLight = baselineAmbientLight;
            RenderSettings.ambientSkyColor = baselineAmbientSkyColor;
            RenderSettings.ambientEquatorColor = baselineAmbientEquatorColor;
            RenderSettings.ambientGroundColor = baselineAmbientGroundColor;
            RenderSettings.ambientIntensity = baselineAmbientIntensity;
            RenderSettings.ambientProbe = baselineAmbientProbe;
            RenderSettings.fog = baselineFogEnabled;
            RenderSettings.fogMode = baselineFogMode;
            RenderSettings.fogColor = baselineFogColor;
            RenderSettings.fogDensity = baselineFogDensity;
            RenderSettings.fogStartDistance = baselineFogStartDistance;
            RenderSettings.fogEndDistance = baselineFogEndDistance;
            RenderSettings.defaultReflectionMode = baselineReflectionMode;
            RenderSettings.customReflectionTexture =
                baselineCustomReflectionTexture;
            RenderSettings.reflectionIntensity = baselineReflectionIntensity;

            if (capturedSunBaseline && baselineDrivenSun != null)
            {
                baselineDrivenSun.type = baselineSunType;
                baselineDrivenSun.transform.rotation = baselineSunRotation;
                baselineDrivenSun.color = baselineSunColor;
                baselineDrivenSun.intensity = baselineSunIntensity;
                baselineDrivenSun.useColorTemperature =
                    baselineSunUsesColorTemperature;
                baselineDrivenSun.shadows = baselineSunShadows;
                baselineDrivenSun.shadowStrength = baselineSunShadowStrength;
                baselineDrivenSun.shadowBias = baselineSunShadowBias;
                baselineDrivenSun.shadowNormalBias =
                    baselineSunShadowNormalBias;
            }

            capturedSceneBaseline = false;
            capturedSunBaseline = false;
            baselineDrivenSun = null;
        }

        private void Update()
        {
            if (!IsEnvironmentAuthority ||
                !Application.isPlaying ||
                !advanceClockInPlayMode)
            {
                return;
            }

            var safeDayDuration = Mathf.Max(10f, secondsPerFullDay);
            timeOfDayHours = SolarpunkDayNightProfile.WrapHour(
                timeOfDayHours + Time.deltaTime * 24f / safeDayDuration);
            ApplyCurrentSample();

            if (Time.unscaledTime >= nextEnvironmentRefreshTime)
            {
                RefreshEnvironment();
                nextEnvironmentRefreshTime =
                    Time.unscaledTime + Mathf.Max(0.25f, environmentRefreshSeconds);
            }
        }

        private void OnEnable()
        {
            RegisterController(this);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            CancelDeferredValidation();
#endif
            UnregisterController(this);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            CancelDeferredValidation();
#endif
            UnregisterController(this);
        }

        private void OnTransformParentChanged()
        {
            if (isActiveAndEnabled)
            {
                ReevaluateAuthority();
            }
        }

        private void OnValidate()
        {
            timeOfDayHours = SolarpunkDayNightProfile.WrapHour(timeOfDayHours);
            secondsPerFullDay = Mathf.Max(10f, secondsPerFullDay);
            environmentRefreshSeconds = Mathf.Max(0.25f, environmentRefreshSeconds);

#if UNITY_EDITOR
            // Never create/destroy Unity objects from OnValidate. Queue the
            // preview update until validation/import has completed instead.
            QueueDeferredValidation();
#endif
        }

#if UNITY_EDITOR
        private void QueueDeferredValidation()
        {
            if (deferredValidationPending)
            {
                return;
            }

            deferredValidationPending = true;
            UnityEditor.EditorApplication.update += ApplyDeferredValidation;
        }

        private void ApplyDeferredValidation()
        {
            if (this == null || !isActiveAndEnabled)
            {
                CancelDeferredValidation();
                return;
            }

            if (UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorApplication.isUpdating)
            {
                return;
            }

            CancelDeferredValidation();
            ReevaluateAuthority();
            if (IsEnvironmentAuthority)
            {
                Apply();
            }
        }

        private void CancelDeferredValidation()
        {
            if (!deferredValidationPending)
            {
                return;
            }

            UnityEditor.EditorApplication.update -= ApplyDeferredValidation;
            deferredValidationPending = false;
        }
#endif

        private void RefreshEnvironment()
        {
            if (!IsEnvironmentAuthority)
            {
                return;
            }

            DynamicGI.UpdateEnvironment();
        }

        private void ReleaseRuntimeSkybox()
        {
            if (runtimeSkyboxMaterial == null)
            {
                runtimeSkyboxSourceMaterial = null;
                runtimeSkyboxSourceShader = null;
                return;
            }

            if (RenderSettings.skybox == runtimeSkyboxMaterial)
            {
                RenderSettings.skybox = skyboxMaterial;
            }

            DestroyRuntimeObject(runtimeSkyboxMaterial);
            runtimeSkyboxMaterial = null;
            runtimeSkyboxSourceMaterial = null;
            runtimeSkyboxSourceShader = null;
        }

        private void ReleaseRuntimeResources()
        {
            ReleaseRuntimeSkybox();
            if (runtimePostProcessOwner != null)
            {
                // Destroy is deferred in play mode. Disable the overlay first
                // so an authority hand-off never leaves two active volumes in
                // the same frame.
                runtimePostProcessOwner.SetActive(false);
            }

            DestroyRuntimeObject(runtimePostProcessOwner);
            DestroyRuntimeObject(runtimePostProcessProfile);
            runtimePostProcessOwner = null;
            runtimePostProcessProfile = null;
            runtimeBloom = null;
            RestoreSceneBaseline();
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}

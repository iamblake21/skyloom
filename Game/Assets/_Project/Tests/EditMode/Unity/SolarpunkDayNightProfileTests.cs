using System.Collections.Generic;
using System.Reflection;
using CML.Unity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Tests.Unity
{
    public sealed class SolarpunkDayNightProfileTests
    {
        [Test]
        public void NoonMatchesExtractedReferenceValues()
        {
            var sample = SolarpunkDayNightProfile.Evaluate(12f);

            Assert.That(sample.SunPosition, Is.EqualTo(-107.6359f).Within(0.001f));
            Assert.That(sample.SunLightColor.r, Is.EqualTo(1f).Within(0.000001f));
            Assert.That(sample.SunLightColor.g, Is.EqualTo(0.879622f).Within(0.000001f));
            Assert.That(sample.SunLightColor.b, Is.EqualTo(0.7605245f).Within(0.000001f));
            Assert.That(sample.DayFactor, Is.EqualTo(1f).Within(0.000001f));
            Assert.That(sample.AmbientOcclusion, Is.EqualTo(0.9f).Within(0.000001f));
            Assert.That(sample.Emissive, Is.EqualTo(1f).Within(0.000001f));
        }

        [Test]
        public void MidnightMatchesExtractedNightState()
        {
            var sample = SolarpunkDayNightProfile.Evaluate(0f);

            Assert.That(sample.SunPosition, Is.EqualTo(-45f).Within(0.000001f));
            Assert.That(sample.SunLightColor.r, Is.EqualTo(0.043137256f).Within(0.000001f));
            Assert.That(sample.SunLightColor.g, Is.EqualTo(0.09411766f).Within(0.000001f));
            Assert.That(sample.SunLightColor.b, Is.EqualTo(0.2901961f).Within(0.000001f));
            Assert.That(sample.DayFactor, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(sample.AmbientOcclusion, Is.EqualTo(0.6f).Within(0.000001f));
            Assert.That(sample.Emissive, Is.EqualTo(0.15f).Within(0.000001f));
        }

        [Test]
        public void ProfileUsesLinearInterpolationAtOriginalKeyTimes()
        {
            var transition = SolarpunkDayNightProfile.Evaluate(5.25f);
            var fogTransition = SolarpunkDayNightProfile.Evaluate(7f);

            Assert.That(transition.DayFactor, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(fogTransition.FogDensity, Is.EqualTo(0.1f).Within(0.000001f));
        }

        [Test]
        public void DiffuseEnvironmentMatchesIntegratedReferenceLighting()
        {
            var noon = SolarpunkDayNightProfile.EvaluateDiffuseAmbient(12f);
            var midnight = SolarpunkDayNightProfile.EvaluateDiffuseAmbient(0f);

            Assert.That(noon.Up.r, Is.EqualTo(0.329601f).Within(0.00001f));
            Assert.That(noon.Up.g, Is.EqualTo(0.379093f).Within(0.00001f));
            Assert.That(noon.Up.b, Is.EqualTo(0.460412f).Within(0.00001f));
            Assert.That(noon.Side.r, Is.EqualTo(0.273770f).Within(0.00001f));
            Assert.That(noon.Down.g, Is.EqualTo(0.140999f).Within(0.00001f));

            Assert.That(midnight.Up.r, Is.EqualTo(0.045701f).Within(0.00001f));
            Assert.That(midnight.Up.g, Is.EqualTo(0.071720f).Within(0.00001f));
            Assert.That(midnight.Up.b, Is.EqualTo(0.119033f).Within(0.00001f));
            Assert.That(midnight.Side.b, Is.EqualTo(0.07196564f).Within(0.00001f));
            Assert.That(midnight.Down.b, Is.EqualTo(0.01654567f).Within(0.00001f));
        }

        [Test]
        public void HoursWrapAcrossDayBoundary()
        {
            var atMidnight = SolarpunkDayNightProfile.Evaluate(0f);
            var afterOneDay = SolarpunkDayNightProfile.Evaluate(24f);
            var beforeMidnight = SolarpunkDayNightProfile.Evaluate(-24f);

            Assert.That(afterOneDay.SunPosition, Is.EqualTo(atMidnight.SunPosition));
            Assert.That(beforeMidnight.SunPosition, Is.EqualTo(atMidnight.SunPosition));
            Assert.That(afterOneDay.SunLightColor, Is.EqualTo(atMidnight.SunLightColor));
        }

        [Test]
        public void RuntimeControllerAppliesExtractedNoonSun()
        {
            var sunObject = new GameObject("DayNightTestSun");
            var controllerObject = new GameObject("DayNightTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller = controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, null);
                controller.SetTimeOfDay(12f);
                var expectedColor =
                    SolarpunkDayNightProfile.Evaluate(12f).SunLightColor.gamma;

                Assert.That(light.type, Is.EqualTo(LightType.Directional));
                AssertColor(light.color, expectedColor);
                Assert.That(light.intensity, Is.EqualTo(1f).Within(0.000001f));
                Assert.That(controller.DayFactor, Is.EqualTo(1f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void RuntimeControllerKeepsNightGeometryReadable()
        {
            var sunObject = new GameObject("DayNightTestSun");
            var controllerObject = new GameObject("DayNightTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller = controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, null);
                controller.SetTimeOfDay(0f);

                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(1f).Within(0.0001f));
                var nightAmbientTint =
                    SolarpunkDayNightProfile.Evaluate(0f).SkyLightColor * 1.8f;
                nightAmbientTint.a = 1f;
                var expected = SolarpunkAmbientProbe.Evaluate(
                    nightAmbientTint);
                AssertProbe(RenderSettings.ambientProbe, expected);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
            }
        }

        [TestCase(0f)]
        [TestCase(4.5f)]
        [TestCase(12f)]
        [TestCase(21.92f)]
        public void RuntimeControllerConvertsUnrealSunPitchToUnity(float hour)
        {
            var sunObject = new GameObject("DayNightSourcePitchTestSun");
            var controllerObject = new GameObject("DayNightSourcePitchTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller = controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, null);
                controller.SetTimeOfDay(hour);

                var sample = SolarpunkDayNightProfile.Evaluate(hour);
                var pitchRadians = sample.SunPosition * Mathf.Deg2Rad;
                var expectedRay = new Vector3(
                    0f,
                    Mathf.Sin(pitchRadians),
                    -Mathf.Cos(pitchRadians));
                var actualRay = light.transform.forward;
                Assert.That(
                    Vector3.Distance(actualRay, expectedRay),
                    Is.LessThan(0.000001f),
                    "The extracted UE pitch/yaw ray was not converted to " +
                    "Unity world space.");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void RuntimeSkyUsesSourceMaterialContractAndSourceSunDirection()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);

            var authoredMaterial = new Material(shader);
            var sunObject = new GameObject("DayNightSkyContractTestSun");
            var controllerObject = new GameObject("DayNightSkyContractTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller = controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, authoredMaterial);

                foreach (var hour in new[] { 0f, 6f, 12f, 21.5f })
                {
                    controller.SetTimeOfDay(hour);
                    var runtimeMaterial = RenderSettings.skybox;
                    Assert.That(runtimeMaterial, Is.Not.Null);
                    Assert.That(
                        runtimeMaterial.HasProperty("_SkyTopColorLinear"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_HorizonColorLinear"),
                        Is.True);
                    Assert.That(runtimeMaterial.HasProperty("_Day01"), Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudAmount"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudTopColorLinear"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudBottomColorLinear"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudColor"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudShadowColor"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudScale"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudCoverage"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudSoftness"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudSpeed"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_CloudOpacity"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_RainFade1Sunny0"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_SnowHailClouds"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_SunDiscColorLinear"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty(
                            "_FogInscatteringColorLinear"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty(
                            "_FogDirectionalColorLinear"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_FogDensity"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_FogFalloff"),
                        Is.True);
                    Assert.That(
                        runtimeMaterial.HasProperty("_SunDirectionWS"),
                        Is.True);
                    var expectedDirection = -light.transform.forward;
                    var actualDirection =
                        (Vector3)runtimeMaterial.GetVector("_SunDirectionWS");
                    Assert.That(
                        Vector3.Distance(expectedDirection, actualDirection),
                        Is.LessThan(0.000001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(authoredMaterial);
            }
        }

        [Test]
        public void AtmosphericSkyExposesCompleteProceduralCloudContract()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            try
            {
                Assert.That(
                    MeasuredStylizedDaylight.HasSkyMaterialContract(material),
                    Is.True,
                    "The sky is missing a runtime or procedural-cloud input.");

                foreach (var propertyName in new[]
                {
                    "_CloudAmount",
                    "_CloudColor",
                    "_CloudShadowColor",
                    "_CloudScale",
                    "_CloudCoverage",
                    "_CloudSoftness",
                    "_CloudSpeed",
                    "_CloudOpacity"
                })
                {
                    Assert.That(
                        material.HasProperty(propertyName),
                        Is.True,
                        $"Required cloud property '{propertyName}' is missing.");
                }
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void RebuildSkyboxPreviewReclonesStaleAuthoredValues()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var authoredMaterial = new Material(shader);
            authoredMaterial.SetFloat("_Exposure", 0.27f);
            authoredMaterial.SetFloat("_CloudCoverage", 0.37f);
            authoredMaterial.SetFloat("_CloudOpacity", 0.68f);
            authoredMaterial.SetFloat("_CloudSpeed", 0.013f);
            var sunObject = new GameObject("DayNightSkyRecloneTestSun");
            var controllerObject =
                new GameObject("DayNightSkyRecloneTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller =
                    controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, authoredMaterial);

                var staleRuntimeMaterial = RenderSettings.skybox;
                Assert.That(staleRuntimeMaterial, Is.Not.Null);
                Assert.That(
                    staleRuntimeMaterial.GetFloat("_Exposure"),
                    Is.EqualTo(0.27f).Within(0.000001f));
                Assert.That(
                    staleRuntimeMaterial.GetFloat("_CloudCoverage"),
                    Is.EqualTo(0.37f).Within(0.000001f));

                var postOwnerField = typeof(MeasuredStylizedDaylight).GetField(
                    "runtimePostProcessOwner",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(postOwnerField, Is.Not.Null);
                var originalPostOwner = postOwnerField.GetValue(controller);

                authoredMaterial.SetFloat("_Exposure", 0.91f);
                authoredMaterial.SetFloat("_CloudCoverage", 0.54f);
                authoredMaterial.SetFloat("_CloudOpacity", 0.76f);
                authoredMaterial.SetFloat("_CloudSpeed", 0.021f);
                controller.SetTimeOfDay(6f);
                var expectedDynamicCloudAmount =
                    SolarpunkDayNightProfile.Evaluate(6f).CloudOpacity;
                Assert.That(
                    staleRuntimeMaterial.GetFloat("_Exposure"),
                    Is.EqualTo(0.27f).Within(0.000001f),
                    "The pre-existing runtime clone unexpectedly tracked " +
                    "the authored material mutation.");

                Assert.That(controller.RebuildSkyboxPreview(), Is.True);
                var rebuiltRuntimeMaterial = RenderSettings.skybox;
                Assert.That(rebuiltRuntimeMaterial, Is.Not.Null);
                Assert.That(
                    rebuiltRuntimeMaterial,
                    Is.Not.SameAs(staleRuntimeMaterial));
                Assert.That(
                    rebuiltRuntimeMaterial,
                    Is.Not.SameAs(authoredMaterial));
                Assert.That(
                    rebuiltRuntimeMaterial.GetFloat("_Exposure"),
                    Is.EqualTo(0.91f).Within(0.000001f));
                Assert.That(
                    rebuiltRuntimeMaterial.GetFloat("_CloudCoverage"),
                    Is.EqualTo(0.54f).Within(0.000001f));
                Assert.That(
                    rebuiltRuntimeMaterial.GetFloat("_CloudOpacity"),
                    Is.EqualTo(0.76f).Within(0.000001f));
                Assert.That(
                    rebuiltRuntimeMaterial.GetFloat("_CloudSpeed"),
                    Is.EqualTo(0.021f).Within(0.000001f));
                Assert.That(
                    rebuiltRuntimeMaterial.GetFloat("_CloudAmount"),
                    Is.EqualTo(expectedDynamicCloudAmount).Within(0.00001f),
                    "Rebuilding the authored controls lost the active " +
                    "day/night cloud sample.");
                Assert.That(
                    postOwnerField.GetValue(controller),
                    Is.SameAs(originalPostOwner),
                    "A sky-only rebuild replaced the post-process overlay.");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(authoredMaterial);
            }
        }

        [Test]
        public void ApplyingDifferentAuthoredSkyReplacesRuntimeClone()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var firstAuthored = new Material(shader);
            var secondAuthored = new Material(shader);
            firstAuthored.SetFloat("_CloudCoverage", 0.28f);
            secondAuthored.SetFloat("_CloudCoverage", 0.63f);
            var controllerObject =
                new GameObject("DayNightSkySourceChangeTestController");
            try
            {
                var controller =
                    controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(null, firstAuthored);
                var firstRuntime = RenderSettings.skybox;
                Assert.That(firstRuntime, Is.Not.Null);
                Assert.That(
                    firstRuntime.GetFloat("_CloudCoverage"),
                    Is.EqualTo(0.28f).Within(0.000001f));

                var skyboxField = typeof(MeasuredStylizedDaylight).GetField(
                    "skyboxMaterial",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(skyboxField, Is.Not.Null);
                skyboxField.SetValue(controller, secondAuthored);
                controller.Apply();

                var secondRuntime = RenderSettings.skybox;
                Assert.That(secondRuntime, Is.Not.Null);
                Assert.That(secondRuntime, Is.Not.SameAs(firstRuntime));
                Assert.That(secondRuntime, Is.Not.SameAs(secondAuthored));
                Assert.That(
                    secondRuntime.GetFloat("_CloudCoverage"),
                    Is.EqualTo(0.63f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(firstAuthored);
                Object.DestroyImmediate(secondAuthored);
            }
        }

        [Test]
        public void OnValidateDoesNotSynchronouslyCreateRuntimeObjects()
        {
            var controllerObject =
                new GameObject("DayNightValidationLifecycleTestController");
            controllerObject.SetActive(false);
            try
            {
                var controller =
                    controllerObject.AddComponent<MeasuredStylizedDaylight>();
                var validate = typeof(MeasuredStylizedDaylight).GetMethod(
                    "OnValidate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var runtimeSkybox = typeof(MeasuredStylizedDaylight).GetField(
                    "runtimeSkyboxMaterial",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var postOwner = typeof(MeasuredStylizedDaylight).GetField(
                    "runtimePostProcessOwner",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(validate, Is.Not.Null);
                Assert.That(runtimeSkybox, Is.Not.Null);
                Assert.That(postOwner, Is.Not.Null);

                validate.Invoke(controller, null);

                Assert.That(runtimeSkybox.GetValue(controller), Is.Null);
                Assert.That(postOwner.GetValue(controller), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void ExactNamedControllerOwnsPreviewAndPromotesStandbyOnDisable()
        {
            var previousControllers = BeginAuthorityIsolation();
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var authoredMaterial = new Material(shader);
            var volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var volumeObject = new GameObject("DayNightAuthorityTestVolume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = volumeProfile;

            var standbySunObject = new GameObject("DayNightStandbySun");
            var preferredSunObject = new GameObject("DayNightPreferredSun");
            var standbyObject = new GameObject("AAA_DaylightStandby");
            var preferredObject = new GameObject(
                "ENV_MeasuredStylizedDaylight");
            standbyObject.SetActive(false);
            preferredObject.SetActive(false);

            var standbySun = standbySunObject.AddComponent<Light>();
            var preferredSun = preferredSunObject.AddComponent<Light>();
            preferredSun.type = LightType.Point;
            preferredSun.color = new Color(0.17f, 0.31f, 0.46f, 1f);
            preferredSun.intensity = 0.29f;
            var preferredBaselineColor = preferredSun.color;
            var preferredBaselineIntensity = preferredSun.intensity;

            var standby = standbyObject.AddComponent<MeasuredStylizedDaylight>();
            var preferred =
                preferredObject.AddComponent<MeasuredStylizedDaylight>();
            SetControllerReferences(
                standby,
                standbySun,
                authoredMaterial,
                volume);
            SetControllerReferences(
                preferred,
                preferredSun,
                authoredMaterial,
                volume);

            try
            {
                standbyObject.SetActive(true);
                Assert.That(standby.IsEnvironmentAuthority, Is.True);
                Assert.That(CountRuntimePostOwners(), Is.EqualTo(1));

                preferredObject.SetActive(true);

                Assert.That(
                    MeasuredStylizedDaylight.ActiveAuthority,
                    Is.SameAs(preferred));
                Assert.That(preferred.IsEnvironmentAuthority, Is.True);
                Assert.That(standby.IsEnvironmentAuthority, Is.False);
                Assert.That(RenderSettings.sun, Is.SameAs(preferredSun));
                Assert.That(GetRuntimeSkybox(standby), Is.Null);
                Assert.That(GetRuntimePostOwner(standby), Is.Null);
                Assert.That(GetRuntimeSkybox(preferred), Is.Not.Null);
                Assert.That(GetRuntimePostOwner(preferred), Is.Not.Null);
                Assert.That(CountRuntimePostOwners(), Is.EqualTo(1));

                preferred.enabled = false;

                Assert.That(
                    MeasuredStylizedDaylight.ActiveAuthority,
                    Is.SameAs(standby));
                Assert.That(standby.IsEnvironmentAuthority, Is.True);
                Assert.That(RenderSettings.sun, Is.SameAs(standbySun));
                Assert.That(preferredSun.type, Is.EqualTo(LightType.Point));
                AssertColor(preferredSun.color, preferredBaselineColor);
                Assert.That(
                    preferredSun.intensity,
                    Is.EqualTo(preferredBaselineIntensity).Within(0.000001f));
                Assert.That(GetRuntimeSkybox(preferred), Is.Null);
                Assert.That(GetRuntimePostOwner(preferred), Is.Null);
                Assert.That(GetRuntimeSkybox(standby), Is.Not.Null);
                Assert.That(CountRuntimePostOwners(), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(preferredObject);
                Object.DestroyImmediate(standbyObject);
                Object.DestroyImmediate(preferredSunObject);
                Object.DestroyImmediate(standbySunObject);
                Object.DestroyImmediate(volumeObject);
                Object.DestroyImmediate(volumeProfile);
                Object.DestroyImmediate(authoredMaterial);
                RestoreAuthorityControllers(previousControllers);
            }
        }

        [Test]
        public void ExplicitConfigureClaimsAuthorityAndStandbyCannotWrite()
        {
            var previousControllers = BeginAuthorityIsolation();
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var authoredMaterial = new Material(shader);
            var volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var volumeObject = new GameObject("DayNightClaimTestVolume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = volumeProfile;
            var preferredSunObject = new GameObject("DayNightClaimPreferredSun");
            var challengerSunObject = new GameObject("DayNightClaimChallengerSun");
            var preferredSun = preferredSunObject.AddComponent<Light>();
            var challengerSun = challengerSunObject.AddComponent<Light>();
            var preferredObject = new GameObject(
                "ENV_MeasuredStylizedDaylight");
            var challengerObject = new GameObject("DayNightConfiguredChallenger");
            preferredObject.SetActive(false);
            challengerObject.SetActive(false);
            var preferred =
                preferredObject.AddComponent<MeasuredStylizedDaylight>();
            var challenger =
                challengerObject.AddComponent<MeasuredStylizedDaylight>();
            SetControllerReferences(
                preferred,
                preferredSun,
                authoredMaterial,
                volume);
            SetControllerReferences(
                challenger,
                challengerSun,
                authoredMaterial,
                volume);

            try
            {
                challengerObject.SetActive(true);
                preferredObject.SetActive(true);
                Assert.That(preferred.IsEnvironmentAuthority, Is.True);

                challenger.Configure(
                    challengerSun,
                    authoredMaterial,
                    volume);

                Assert.That(
                    MeasuredStylizedDaylight.ActiveAuthority,
                    Is.SameAs(challenger));
                Assert.That(challenger.IsEnvironmentAuthority, Is.True);
                Assert.That(preferred.IsEnvironmentAuthority, Is.False);
                Assert.That(RenderSettings.sun, Is.SameAs(challengerSun));
                Assert.That(GetRuntimeSkybox(preferred), Is.Null);
                Assert.That(GetRuntimePostOwner(preferred), Is.Null);
                Assert.That(CountRuntimePostOwners(), Is.EqualTo(1));

                var activeSkybox = RenderSettings.skybox;
                var activeDayValue = activeSkybox.GetFloat("_Day01");
                preferred.SetTimeOfDay(0f);

                Assert.That(RenderSettings.skybox, Is.SameAs(activeSkybox));
                Assert.That(
                    activeSkybox.GetFloat("_Day01"),
                    Is.EqualTo(activeDayValue).Within(0.000001f));
                Assert.That(preferred.RebuildSkyboxPreview(), Is.False);
                Assert.That(CountRuntimePostOwners(), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(challengerObject);
                Object.DestroyImmediate(preferredObject);
                Object.DestroyImmediate(challengerSunObject);
                Object.DestroyImmediate(preferredSunObject);
                Object.DestroyImmediate(volumeObject);
                Object.DestroyImmediate(volumeProfile);
                Object.DestroyImmediate(authoredMaterial);
                RestoreAuthorityControllers(previousControllers);
            }
        }

        [Test]
        public void RuntimeSkyReceivesExactLinearDawnAndSunsetInputs()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var authoredMaterial = new Material(shader);
            var sunObject = new GameObject("TwilightSkyTestSun");
            var controllerObject = new GameObject("TwilightSkyTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller =
                    controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, authoredMaterial);

                controller.SetTimeOfDay(5.5f);
                var dawn = RenderSettings.skybox;
                AssertVector(
                    dawn.GetVector("_SkyTopColorLinear"),
                    new Vector4(
                        0.033333335f,
                        0.096078438f,
                        0.103921575f,
                        1f));
                AssertVector(
                    dawn.GetVector("_HorizonColorLinear"),
                    new Vector4(
                        1f,
                        0.829749488f,
                        0f,
                        1f));
                AssertVector(
                    dawn.GetVector("_CloudTopColorLinear"),
                    new Vector4(
                        0.499987689f,
                        0.762317317f,
                        0.844416033f,
                        1f));
                AssertVector(
                    dawn.GetVector("_CloudBottomColorLinear"),
                    new Vector4(
                        0.183375966f,
                        0.358738292f,
                        0.457715267f,
                        1f));
                AssertVector(
                    dawn.GetVector("_SunDiscColorLinear"),
                    new Vector4(
                        0.990052405f,
                        0.924041775f,
                        0.601793369f,
                        1f),
                    0.00001f);
                Assert.That(
                    dawn.GetFloat("_Day01"),
                    Is.EqualTo(0.666666667f).Within(0.000001f));
                Assert.That(
                    dawn.GetFloat("_DawnPhase"),
                    Is.EqualTo(0.9259259f).Within(0.00001f));
                Assert.That(
                    dawn.GetFloat("_NoonPhase"),
                    Is.EqualTo(0.0493827f).Within(0.00001f));
                Assert.That(
                    dawn.GetFloat("_EarlyDuskPhase"),
                    Is.EqualTo(0f).Within(0.000001f));
                Assert.That(
                    dawn.GetFloat("_LateDuskPhase"),
                    Is.EqualTo(0f).Within(0.000001f));
                Assert.That(
                    dawn.GetFloat("_CloudAmount"),
                    Is.EqualTo(1.083333333f).Within(0.00001f));
                Assert.That(
                    dawn.GetFloat("_RainFade1Sunny0"),
                    Is.EqualTo(0f).Within(0.000001f));
                Assert.That(
                    dawn.GetFloat("_SnowHailClouds"),
                    Is.EqualTo(0f).Within(0.000001f));
                AssertVector(
                    dawn.GetVector("_FogInscatteringColorLinear"),
                    new Vector4(
                        0.040522878f,
                        0.098039221f,
                        0.103267981f,
                        1f));
                AssertVector(
                    dawn.GetVector("_FogDirectionalColorLinear"),
                    new Vector4(
                        1f,
                        0.817488721f,
                        0.061512585f,
                        1f),
                    0.00001f);
                Assert.That(
                    dawn.GetFloat("_FogDensity"),
                    Is.EqualTo(0.085f).Within(0.000001f));
                Assert.That(
                    dawn.GetFloat("_FogFalloff"),
                    Is.EqualTo(0.05f).Within(0.000001f));

                controller.SetTimeOfDay(21f);
                var sunset = RenderSettings.skybox;
                AssertVector(
                    sunset.GetVector("_SkyTopColorLinear"),
                    new Vector4(
                        0.16470589f,
                        0.25882354f,
                        0.20392159f,
                        1f));
                AssertVector(
                    sunset.GetVector("_HorizonColorLinear"),
                    new Vector4(
                        1f,
                        0.7137258f,
                        0f,
                        1f));
                AssertVector(
                    sunset.GetVector("_CloudTopColorLinear"),
                    new Vector4(
                        0.30980393f,
                        0.44705886f,
                        0.49803925f,
                        1f));
                AssertVector(
                    sunset.GetVector("_CloudBottomColorLinear"),
                    new Vector4(
                        1f,
                        0.63529414f,
                        0f,
                        1f));
                AssertVector(
                    sunset.GetVector("_SunDiscColorLinear"),
                    new Vector4(
                        0.19215688f,
                        0.019607844f,
                        0f,
                        1f));
                Assert.That(
                    sunset.GetFloat("_Day01"),
                    Is.EqualTo(1f).Within(0.000001f));
                Assert.That(
                    sunset.GetFloat("_NoonPhase"),
                    Is.EqualTo(0f).Within(0.000001f));
                Assert.That(
                    sunset.GetFloat("_DawnPhase"),
                    Is.EqualTo(0f).Within(0.000001f));
                Assert.That(
                    sunset.GetFloat("_EarlyDuskPhase"),
                    Is.EqualTo(1f).Within(0.000001f));
                Assert.That(
                    sunset.GetFloat("_LateDuskPhase"),
                    Is.EqualTo(0f).Within(0.000001f));
                Assert.That(
                    sunset.GetFloat("_CloudAmount"),
                    Is.EqualTo(2.65f).Within(0.00001f));
                AssertVector(
                    sunset.GetVector("_FogInscatteringColorLinear"),
                    new Vector4(
                        0.04705883f,
                        0.10588236f,
                        0.10980393f,
                        1f));
                AssertVector(
                    sunset.GetVector("_FogDirectionalColorLinear"),
                    new Vector4(
                        1f,
                        0.1567310f,
                        0f,
                        1f),
                    0.00001f);
                Assert.That(
                    sunset.GetFloat("_FogDensity"),
                    Is.EqualTo(0.09f).Within(0.000001f));
                Assert.That(
                    sunset.GetFloat("_FogFalloff"),
                    Is.EqualTo(0.0675f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(authoredMaterial);
            }
        }

        [Test]
        public void RuntimeDisplayPhasesRemainContinuousAcrossTwilight()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var authoredMaterial = new Material(shader);
            var sunObject = new GameObject("PhaseContinuityTestSun");
            var controllerObject = new GameObject("PhaseContinuityTestController");
            try
            {
                var light = sunObject.AddComponent<Light>();
                var controller =
                    controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, authoredMaterial);

                controller.SetTimeOfDay(6.2f);
                var morning = RenderSettings.skybox;
                Assert.That(morning.GetFloat("_DawnPhase"), Is.GreaterThan(0.75f));
                Assert.That(morning.GetFloat("_EarlyDuskPhase"), Is.EqualTo(0f));
                Assert.That(morning.GetFloat("_LateDuskPhase"), Is.EqualTo(0f));

                controller.SetTimeOfDay(21.75f);
                var late = RenderSettings.skybox;
                Assert.That(late.GetFloat("_NoonPhase"), Is.EqualTo(0f));
                Assert.That(late.GetFloat("_EarlyDuskPhase"), Is.EqualTo(0f));
                Assert.That(late.GetFloat("_LateDuskPhase"), Is.EqualTo(0.5f).Within(0.00001f));

                controller.SetTimeOfDay(21.9f);
                var nearNight = RenderSettings.skybox;
                Assert.That(nearNight.GetFloat("_NoonPhase"), Is.EqualTo(0f));
                Assert.That(nearNight.GetFloat("_LateDuskPhase"), Is.EqualTo(0.2f).Within(0.00001f));

                controller.SetTimeOfDay(22f);
                var night = RenderSettings.skybox;
                Assert.That(night.GetFloat("_NoonPhase"), Is.EqualTo(0f));
                Assert.That(night.GetFloat("_DawnPhase"), Is.EqualTo(0f));
                Assert.That(night.GetFloat("_EarlyDuskPhase"), Is.EqualTo(0f));
                Assert.That(night.GetFloat("_LateDuskPhase"), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(authoredMaterial);
            }
        }

        [Test]
        public void RuntimeSkyHasNoInventedGradientOrDirectionalHazeInputs()
        {
            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            try
            {
                Assert.That(material.HasProperty("_ZenithColor"), Is.False);
                Assert.That(material.HasProperty("_HorizonColor"), Is.False);
                Assert.That(material.HasProperty("_LowerColor"), Is.False);
                Assert.That(
                    material.HasProperty("_DirectionalHazeColor"),
                    Is.False);
                Assert.That(
                    material.HasProperty("_TwilightStrength"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void DestroyingControllerRestoresDrivenSunAndRenderSettings()
        {
            var previousControllers = BeginAuthorityIsolation();
            var originalSkybox = RenderSettings.skybox;
            var originalSun = RenderSettings.sun;
            var originalAmbientMode = RenderSettings.ambientMode;
            var originalAmbientSky = RenderSettings.ambientSkyColor;
            var originalAmbientEquator = RenderSettings.ambientEquatorColor;
            var originalAmbientGround = RenderSettings.ambientGroundColor;
            var originalAmbientIntensity = RenderSettings.ambientIntensity;
            var originalAmbientProbe = RenderSettings.ambientProbe;
            var originalFogEnabled = RenderSettings.fog;
            var originalFogMode = RenderSettings.fogMode;
            var originalFogColor = RenderSettings.fogColor;
            var originalFogStart = RenderSettings.fogStartDistance;
            var originalFogEnd = RenderSettings.fogEndDistance;
            var originalReflectionMode = RenderSettings.defaultReflectionMode;
            var originalReflectionTexture = RenderSettings.customReflectionTexture;
            var originalReflectionIntensity = RenderSettings.reflectionIntensity;

            var shader = Shader.Find(
                "CML/Environment/Starter Island Atmospheric Sky");
            var authoredMaterial = new Material(shader);
            var sunObject = new GameObject("DayNightRestoreTestSun");
            var controllerObject = new GameObject("DayNightRestoreTestController");
            var light = sunObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.transform.rotation = Quaternion.Euler(13f, 29f, 7f);
            light.color = new Color(0.2f, 0.3f, 0.4f, 1f);
            light.intensity = 0.37f;
            var baselineRotation = light.transform.rotation;
            var baselineColor = light.color;
            var baselineIntensity = light.intensity;
            var baselineType = light.type;

            try
            {
                var controller = controllerObject.AddComponent<MeasuredStylizedDaylight>();
                controller.Configure(light, authoredMaterial);
                controller.SetTimeOfDay(21.5f);
                Object.DestroyImmediate(controllerObject);

                Assert.That(RenderSettings.skybox, Is.SameAs(originalSkybox));
                Assert.That(RenderSettings.sun, Is.SameAs(originalSun));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(originalAmbientMode));
                AssertColor(RenderSettings.ambientSkyColor, originalAmbientSky);
                AssertColor(RenderSettings.ambientEquatorColor, originalAmbientEquator);
                AssertColor(RenderSettings.ambientGroundColor, originalAmbientGround);
                Assert.That(
                    RenderSettings.ambientIntensity,
                    Is.EqualTo(originalAmbientIntensity).Within(0.000001f));
                AssertProbe(RenderSettings.ambientProbe, originalAmbientProbe);
                Assert.That(RenderSettings.fog, Is.EqualTo(originalFogEnabled));
                Assert.That(RenderSettings.fogMode, Is.EqualTo(originalFogMode));
                AssertColor(RenderSettings.fogColor, originalFogColor);
                Assert.That(
                    RenderSettings.fogStartDistance,
                    Is.EqualTo(originalFogStart).Within(0.000001f));
                Assert.That(
                    RenderSettings.fogEndDistance,
                    Is.EqualTo(originalFogEnd).Within(0.000001f));
                Assert.That(
                    RenderSettings.defaultReflectionMode,
                    Is.EqualTo(originalReflectionMode));
                Assert.That(
                    RenderSettings.customReflectionTexture,
                    Is.SameAs(originalReflectionTexture));
                Assert.That(
                    RenderSettings.reflectionIntensity,
                    Is.EqualTo(originalReflectionIntensity).Within(0.000001f));
                Assert.That(light.type, Is.EqualTo(baselineType));
                Assert.That(
                    Quaternion.Angle(light.transform.rotation, baselineRotation),
                    Is.LessThan(0.001f));
                AssertColor(light.color, baselineColor);
                Assert.That(
                    light.intensity,
                    Is.EqualTo(baselineIntensity).Within(0.000001f));
            }
            finally
            {
                if (controllerObject != null)
                {
                    Object.DestroyImmediate(controllerObject);
                }

                Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(authoredMaterial);
                RestoreAuthorityControllers(previousControllers);
            }
        }

        private static List<MeasuredStylizedDaylight>
            BeginAuthorityIsolation()
        {
            var previous = new List<MeasuredStylizedDaylight>();
            var controllers = Object.FindObjectsByType<MeasuredStylizedDaylight>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller != null && controller.isActiveAndEnabled)
                {
                    previous.Add(controller);
                }
            }

            InvokeAuthorityRegistryReset();
            return previous;
        }

        private static void RestoreAuthorityControllers(
            List<MeasuredStylizedDaylight> controllers)
        {
            InvokeAuthorityRegistryReset();
            foreach (var controller in controllers)
            {
                if (controller != null && controller.isActiveAndEnabled)
                {
                    controller.Apply();
                }
            }
        }

        private static void InvokeAuthorityRegistryReset()
        {
            var reset = typeof(MeasuredStylizedDaylight).GetMethod(
                "ResetAuthorityRegistry",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(reset, Is.Not.Null);
            reset.Invoke(null, null);
        }

        private static void SetControllerReferences(
            MeasuredStylizedDaylight controller,
            Light light,
            Material material,
            Volume volume)
        {
            SetPrivateField(controller, "sun", light);
            SetPrivateField(controller, "skyboxMaterial", material);
            SetPrivateField(controller, "postProcessVolume", volume);
        }

        private static void SetPrivateField(
            MeasuredStylizedDaylight controller,
            string fieldName,
            object value)
        {
            var field = typeof(MeasuredStylizedDaylight).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(controller, value);
        }

        private static Material GetRuntimeSkybox(
            MeasuredStylizedDaylight controller)
        {
            return GetPrivateField<Material>(
                controller,
                "runtimeSkyboxMaterial");
        }

        private static GameObject GetRuntimePostOwner(
            MeasuredStylizedDaylight controller)
        {
            return GetPrivateField<GameObject>(
                controller,
                "runtimePostProcessOwner");
        }

        private static T GetPrivateField<T>(
            MeasuredStylizedDaylight controller,
            string fieldName)
            where T : Object
        {
            var field = typeof(MeasuredStylizedDaylight).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return field.GetValue(controller) as T;
        }

        private static int CountRuntimePostOwners()
        {
            var owners = Resources.FindObjectsOfTypeAll<Volume>();
            var count = 0;
            foreach (var volume in owners)
            {
                if (volume != null &&
                    volume.gameObject.name ==
                    "CML Day Night Runtime Post Process")
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.000001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.000001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.000001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.000001f));
        }

        private static void AssertVector(
            Vector4 actual,
            Vector4 expected,
            float tolerance = 0.000001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance));
        }

        private static void AssertProbe(
            SphericalHarmonicsL2 actual,
            SphericalHarmonicsL2 expected)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                for (var coefficient = 0; coefficient < 9; coefficient++)
                {
                    Assert.That(
                        actual[channel, coefficient],
                        Is.EqualTo(expected[channel, coefficient])
                            .Within(0.000001f));
                }
            }
        }
    }
}

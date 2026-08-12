using System;
using System.IO;
using System.Reflection;
using CML.Unity.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CML.Editor.Art
{
    /// <summary>
    /// Opt-in, read-only visual diagnostic for the currently open scene.
    /// It renders a temporary clone of the game camera from the current Scene
    /// View pose, then restores all touched runtime state without opening or
    /// saving scenes.
    /// </summary>
    [InitializeOnLoad]
    internal static class TwilightWaterDiagnosticCapture
    {
        internal const string MarkerPath =
            "Temp/CML_CaptureTwilightWater.once";
        private const string TestMarkerPath =
            "Temp/CML_RunDayNightEditMode.once";
        // Keep visual QA outside Unity's disposable Temp directory. Unity
        // deletes Temp while shutting down and a crash previously removed the
        // only fresh dawn/noon/sunset evidence before it could be reviewed.
        private const string OutputDirectory =
            "Artifacts/Reviews/Sky/TwilightWaterCapture";
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 720;
        private const int CaptureDepthBits = 24;
        private const float SunwardDownPitchDegrees = 4f;
        private const double RequestPollIntervalSeconds = 0.5d;

        private static double nextRequestPollTime;

        private static readonly CaptureTime[] CaptureTimes =
        {
            new CaptureTime("05-30", 5.5f, true),
            new CaptureTime("06-00", 6f, true),
            new CaptureTime("12-00", 12f, false),
            new CaptureTime("21-00", 21f, true),
            new CaptureTime("21-30", 21.5f, true),
            new CaptureTime("00-00", 0f, false)
        };

        private static readonly FieldInfo ControllerSunField =
            typeof(MeasuredStylizedDaylight).GetField(
                "sun",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ControllerBloomField =
            typeof(MeasuredStylizedDaylight).GetField(
                "runtimeBloom",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ControllerHourField =
            typeof(MeasuredStylizedDaylight).GetField(
                "timeOfDayHours",
                BindingFlags.Instance | BindingFlags.NonPublic);

        static TwilightWaterDiagnosticCapture()
        {
            EditorApplication.delayCall += RunIfRequested;
            EditorApplication.update += PollForRequest;
        }

        private static void PollForRequest()
        {
            if (EditorApplication.timeSinceStartup < nextRequestPollTime)
            {
                return;
            }

            nextRequestPollTime =
                EditorApplication.timeSinceStartup + RequestPollIntervalSeconds;
            RunIfRequested();
        }

        [MenuItem("CML/Diagnostics/Capture Twilight And Water (Current Scene)")]
        private static void RunFromMenu()
        {
            Run();
        }

        [MenuItem(
            "CML/Diagnostics/Capture Twilight And Water (Current Scene)",
            true)]
        private static bool ValidateRunFromMenu()
        {
            return !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating &&
                !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void RunIfRequested()
        {
            var marker = ProjectPath(MarkerPath);
            if (!File.Exists(marker))
            {
                return;
            }

            // The targeted EditMode suite is synchronous and owns the same
            // lighting state. Let it consume its marker first, then capture
            // on the next editor tick so both diagnostics never overlap.
            if (File.Exists(ProjectPath(TestMarkerPath)))
            {
                EditorApplication.delayCall += RunIfRequested;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunIfRequested;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "CML_TWILIGHT_WATER_CAPTURE status=DEFERRED " +
                    "reason=PLAY_MODE markerRetained=1");
                return;
            }

            // Consume before work begins so a domain reload cannot duplicate
            // a capture. Failure is logged and a new request can be explicit.
            File.Delete(marker);
            Run();
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Twilight capture only runs in Edit Mode.");
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                throw new InvalidOperationException(
                    "There is no loaded active scene to capture.");
            }

            var controller = FindController(activeScene);
            var sourceCamera = FindGameCamera(activeScene);
            if (controller == null)
            {
                throw new InvalidOperationException(
                    "The open scene has no active MeasuredStylizedDaylight " +
                    "controller.");
            }

            if (sourceCamera == null)
            {
                throw new InvalidOperationException(
                    "The open scene has no active Game camera whose settings " +
                    "can be cloned.");
            }

            var sceneViewCamera = SceneView.lastActiveSceneView?.camera;
            if (sceneViewCamera == null)
            {
                throw new InvalidOperationException(
                    "Twilight capture requires an active Scene View camera. " +
                    "Open and frame the Scene View before running it; no " +
                    "Game-camera pose fallback is permitted.");
            }

            var sceneViewPosition = sceneViewCamera.transform.position;
            var sceneViewRotation = sceneViewCamera.transform.rotation;
            var sceneViewFieldOfView = sceneViewCamera.fieldOfView;
            ValidateSceneViewPose(
                sceneViewPosition,
                sceneViewRotation,
                sceneViewFieldOfView);

            var sceneWasDirty = activeScene.isDirty;
            var originalHour = controller.TimeOfDayHours;
            var controllerSun = ControllerSunField?.GetValue(controller) as Light;
            if (controllerSun == null)
            {
                throw new InvalidOperationException(
                    "Twilight sunward capture requires the active daylight " +
                    "controller to reference a directional sun.");
            }

            var renderState = new RenderSettingsState();
            var controllerSunState = new LightState(controllerSun);
            var renderSunState = new LightState(RenderSettings.sun);
            var bloom = ControllerBloomField?.GetValue(controller) as Bloom;
            var bloomState = new BloomState(bloom);
            var output = ProjectPath(OutputDirectory);
            var writtenFiles = 0;
            var sunwardFiles = 0;
            Camera camera = null;

            Directory.CreateDirectory(output);
            try
            {
                camera = CreateSceneViewCameraClone(
                    sourceCamera,
                    sceneViewPosition,
                    sceneViewRotation,
                    sceneViewFieldOfView);
                foreach (var captureTime in CaptureTimes)
                {
                    controller.SetTimeOfDay(captureTime.Hour);
                    var fullImage = Render(camera);
                    try
                    {
                        var fullPath = Path.Combine(
                            output,
                            captureTime.Label + ".png");
                        File.WriteAllBytes(fullPath, fullImage.EncodeToPNG());
                        writtenFiles++;

                        if (TryGetWaterCrop(camera, out var crop))
                        {
                            var waterImage = Crop(fullImage, crop);
                            try
                            {
                                var waterPath = Path.Combine(
                                    output,
                                    captureTime.Label + "_water.png");
                                File.WriteAllBytes(
                                    waterPath,
                                    waterImage.EncodeToPNG());
                                writtenFiles++;
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(waterImage);
                            }
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(fullImage);
                    }

                    CaptureSkyOnly(
                        camera,
                        controllerSun,
                        captureTime,
                        output,
                        ref writtenFiles);

                    if (!captureTime.CaptureSunward)
                    {
                        continue;
                    }

                    try
                    {
                        ApplySunwardPose(
                            camera,
                            sceneViewPosition,
                            controllerSun,
                            captureTime);
                        var sunwardImage = Render(camera);
                        try
                        {
                            var sunwardPath = Path.Combine(
                                output,
                                captureTime.Label + "-sunward.png");
                            File.WriteAllBytes(
                                sunwardPath,
                                sunwardImage.EncodeToPNG());
                            writtenFiles++;
                            sunwardFiles++;
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(sunwardImage);
                        }
                    }
                    finally
                    {
                        camera.transform.SetPositionAndRotation(
                            sceneViewPosition,
                            sceneViewRotation);
                    }
                }

                Debug.Log(
                    "CML_TWILIGHT_WATER_CAPTURE status=CAPTURED " +
                    $"scene={activeScene.path} camera={sourceCamera.name} " +
                    "cameraPose=SceneViewClone " +
                    $"sunwardFiles={sunwardFiles} " +
                    $"files={writtenFiles} output={output} " +
                    "sceneOpened=0 sceneSaved=0 cameraMoved=0");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "CML_TWILIGHT_WATER_CAPTURE status=FAILED " +
                    $"type={exception.GetType().Name} " +
                    $"message={exception.Message}\n{exception}");
                throw;
            }
            finally
            {
                // First ask the controller to reconstruct its original sample,
                // then overwrite every captured external state exactly. This
                // also handles a scene that was intentionally out of sync with
                // the serialized clock before the diagnostic began.
                if (controller != null)
                {
                    try
                    {
                        controller.SetTimeOfDay(originalHour);
                    }
                    catch (Exception restoreException)
                    {
                        Debug.LogException(restoreException);
                    }

                    ControllerHourField?.SetValue(controller, originalHour);
                }

                bloomState.Restore();
                renderState.Restore();
                controllerSunState.Restore();
                renderSunState.Restore();
                if (camera != null)
                {
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);
                }

                DynamicGI.UpdateEnvironment();
                if (activeScene.IsValid() &&
                    activeScene.isLoaded &&
                    activeScene.isDirty != sceneWasDirty)
                {
                    Debug.LogError(
                        "CML_TWILIGHT_WATER_CAPTURE " +
                        "status=SCENE_DIRTINESS_CHANGED_UNEXPECTEDLY " +
                        $"before={(sceneWasDirty ? 1 : 0)} " +
                        $"after={(activeScene.isDirty ? 1 : 0)}");
                }
            }
        }

        private static Texture2D Render(Camera camera)
        {
            var target = RenderTexture.GetTemporary(
                CaptureWidth,
                CaptureHeight,
                CaptureDepthBits,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default,
                1);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                // The second render lets URP refresh camera color/depth inputs
                // used by the production water while remaining deterministic.
                camera.Render();
                camera.Render();
                RenderTexture.active = target;

                var image = new Texture2D(
                    CaptureWidth,
                    CaptureHeight,
                    TextureFormat.RGB24,
                    false,
                    false);
                image.ReadPixels(
                    new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                    0,
                    0,
                    false);
                image.Apply(false, false);
                return image;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void CaptureSkyOnly(
            Camera camera,
            Light sun,
            CaptureTime captureTime,
            string output,
            ref int writtenFiles)
        {
            var originalPosition = camera.transform.position;
            var originalRotation = camera.transform.rotation;
            var originalCullingMask = camera.cullingMask;
            var originalClearFlags = camera.clearFlags;
            try
            {
                camera.cullingMask = 0;
                camera.clearFlags = CameraClearFlags.Skybox;
                if (captureTime.CaptureSunward)
                {
                    ApplySunwardPose(
                        camera,
                        Vector3.zero,
                        sun,
                        captureTime);
                }
                else
                {
                    camera.transform.SetPositionAndRotation(
                        Vector3.zero,
                        Quaternion.Euler(-12f, 0f, 0f));
                }

                var skyImage = Render(camera);
                try
                {
                    var skyPath = Path.Combine(
                        output,
                        captureTime.Label + "-sky.png");
                    File.WriteAllBytes(skyPath, skyImage.EncodeToPNG());
                    writtenFiles++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(skyImage);
                }
            }
            finally
            {
                camera.cullingMask = originalCullingMask;
                camera.clearFlags = originalClearFlags;
                camera.transform.SetPositionAndRotation(
                    originalPosition,
                    originalRotation);
            }
        }

        private static bool TryGetWaterCrop(
            Camera camera,
            out RectInt crop)
        {
            crop = default;
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            var bestArea = 0f;
            var bestViewport = default(Rect);

            foreach (var renderer in renderers)
            {
                if (renderer == null ||
                    renderer.gameObject.scene != camera.gameObject.scene ||
                    !IsWaterRenderer(renderer))
                {
                    continue;
                }

                if (!TryProjectBounds(camera, renderer.bounds, out var viewport))
                {
                    continue;
                }

                var area = viewport.width * viewport.height;
                if (area <= bestArea)
                {
                    continue;
                }

                bestArea = area;
                bestViewport = viewport;
            }

            if (bestArea < 0.0025f)
            {
                return false;
            }

            const float padding = 0.08f;
            var xMin = Mathf.Clamp01(bestViewport.xMin - padding);
            var yMin = Mathf.Clamp01(bestViewport.yMin - padding);
            var xMax = Mathf.Clamp01(bestViewport.xMax + padding);
            var yMax = Mathf.Clamp01(bestViewport.yMax + padding);
            var pixelX = Mathf.Clamp(
                Mathf.FloorToInt(xMin * CaptureWidth),
                0,
                CaptureWidth - 1);
            var pixelY = Mathf.Clamp(
                Mathf.FloorToInt(yMin * CaptureHeight),
                0,
                CaptureHeight - 1);
            var pixelXMax = Mathf.Clamp(
                Mathf.CeilToInt(xMax * CaptureWidth),
                pixelX + 1,
                CaptureWidth);
            var pixelYMax = Mathf.Clamp(
                Mathf.CeilToInt(yMax * CaptureHeight),
                pixelY + 1,
                CaptureHeight);
            crop = new RectInt(
                pixelX,
                pixelY,
                pixelXMax - pixelX,
                pixelYMax - pixelY);
            return crop.width >= 96 && crop.height >= 54;
        }

        private static bool IsWaterRenderer(Renderer renderer)
        {
            if (renderer.name.IndexOf(
                    "water",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                if (material.name.IndexOf(
                        "water",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.shader != null &&
                    material.shader.name.IndexOf(
                        "water",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryProjectBounds(
            Camera camera,
            Bounds bounds,
            out Rect viewport)
        {
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var visibleCorners = 0;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center + Vector3.Scale(
                            bounds.extents,
                            new Vector3(x, y, z));
                        var projected = camera.WorldToViewportPoint(corner);
                        if (projected.z <= camera.nearClipPlane)
                        {
                            continue;
                        }

                        visibleCorners++;
                        minimum = Vector2.Min(minimum, projected);
                        maximum = Vector2.Max(maximum, projected);
                    }
                }
            }

            if (visibleCorners == 0)
            {
                viewport = default;
                return false;
            }

            var xMin = Mathf.Clamp01(minimum.x);
            var yMin = Mathf.Clamp01(minimum.y);
            var xMax = Mathf.Clamp01(maximum.x);
            var yMax = Mathf.Clamp01(maximum.y);
            viewport = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return viewport.width > 0f && viewport.height > 0f;
        }

        private static Texture2D Crop(Texture2D source, RectInt region)
        {
            var result = new Texture2D(
                region.width,
                region.height,
                TextureFormat.RGB24,
                false,
                false);
            // GetPixels keeps the crop native-resolution and does not
            // resample diagnostic pixels.
            result.SetPixels(source.GetPixels(
                region.x,
                region.y,
                region.width,
                region.height));
            result.Apply(false, false);
            return result;
        }

        private static MeasuredStylizedDaylight FindController(Scene scene)
        {
            var authority = MeasuredStylizedDaylight.ActiveAuthority;
            if (authority != null &&
                authority.isActiveAndEnabled &&
                authority.gameObject.scene.IsValid() &&
                authority.gameObject.scene.isLoaded)
            {
                return authority;
            }

            MeasuredStylizedDaylight fallback = null;
            var controllers =
                UnityEngine.Object.FindObjectsByType<MeasuredStylizedDaylight>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (var controller in controllers)
            {
                if (controller == null ||
                    !controller.gameObject.scene.IsValid() ||
                    !controller.gameObject.scene.isLoaded)
                {
                    continue;
                }

                fallback ??= controller;
                if (controller.gameObject.scene == scene &&
                    controller.isActiveAndEnabled)
                {
                    return controller;
                }
            }

            return fallback != null && fallback.isActiveAndEnabled
                ? fallback
                : null;
        }

        private static Camera FindGameCamera(Scene scene)
        {
            Camera best = null;
            var bestScore = int.MinValue;
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (var camera in cameras)
            {
                if (camera == null ||
                    !camera.gameObject.scene.IsValid() ||
                    !camera.gameObject.scene.isLoaded ||
                    camera.cameraType != CameraType.Game)
                {
                    continue;
                }

                var score = 0;
                if (camera.gameObject.scene == scene)
                {
                    score += 100;
                }

                if (camera.isActiveAndEnabled)
                {
                    score += 40;
                }

                if (camera.CompareTag("MainCamera"))
                {
                    score += 80;
                }

                if (camera.targetTexture == null)
                {
                    score += 10;
                }

                if (camera.TryGetComponent(
                        out UniversalAdditionalCameraData cameraData) &&
                    cameraData.renderType == CameraRenderType.Base)
                {
                    score += 20;
                }

                if (score > bestScore)
                {
                    best = camera;
                    bestScore = score;
                }
            }

            return best;
        }

        private static Camera CreateSceneViewCameraClone(
            Camera sourceCamera,
            Vector3 sceneViewPosition,
            Quaternion sceneViewRotation,
            float sceneViewFieldOfView)
        {
            var owner = new GameObject(
                "CML Twilight Water Diagnostic Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            try
            {
                var camera = owner.AddComponent<Camera>();
                camera.hideFlags = HideFlags.HideAndDontSave;
                camera.CopyFrom(sourceCamera);
                camera.enabled = false;
                camera.targetTexture = null;
                camera.transform.SetPositionAndRotation(
                    sceneViewPosition,
                    sceneViewRotation);
                camera.fieldOfView = sceneViewFieldOfView;

                if (sourceCamera.TryGetComponent(
                        out UniversalAdditionalCameraData sourceCameraData))
                {
                    var cameraData =
                        owner.AddComponent<UniversalAdditionalCameraData>();
                    EditorUtility.CopySerialized(sourceCameraData, cameraData);
                    cameraData.hideFlags = HideFlags.HideAndDontSave;
                }

                return camera;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(owner);
                throw;
            }
        }

        private static void ApplySunwardPose(
            Camera camera,
            Vector3 observerPosition,
            Light sun,
            CaptureTime captureTime)
        {
            if (sun.type != LightType.Directional)
            {
                throw new InvalidOperationException(
                    $"Sunward capture {captureTime.Label} requires a " +
                    "directional sun, but the controller supplied " +
                    $"{sun.type}.");
            }

            var observerToSun = -sun.transform.forward;
            if (!IsFinite(observerToSun) ||
                observerToSun.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Sunward capture {captureTime.Label} has an invalid " +
                    "observer-to-sun direction.");
            }

            var horizontalDirection = Vector3.ProjectOnPlane(
                observerToSun,
                Vector3.up);
            if (!IsFinite(horizontalDirection) ||
                horizontalDirection.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Sunward capture {captureTime.Label} cannot project " +
                    "the observer-to-sun direction onto the horizon.");
            }

            var horizonRotation = Quaternion.LookRotation(
                horizontalDirection.normalized,
                Vector3.up);
            var sunwardRotation = horizonRotation * Quaternion.Euler(
                SunwardDownPitchDegrees,
                0f,
                0f);
            if (!IsFinite(observerPosition) || !IsFinite(sunwardRotation))
            {
                throw new InvalidOperationException(
                    $"Sunward capture {captureTime.Label} produced an " +
                    "invalid camera pose.");
            }

            camera.transform.SetPositionAndRotation(
                observerPosition,
                sunwardRotation);
        }

        private static void ValidateSceneViewPose(
            Vector3 position,
            Quaternion rotation,
            float fieldOfView)
        {
            var rotationMagnitude =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            if (!IsFinite(position) ||
                !IsFinite(rotation) ||
                rotationMagnitude <= 0.000001f ||
                !IsFinite(fieldOfView) ||
                fieldOfView <= 0f ||
                fieldOfView >= 180f)
            {
                throw new InvalidOperationException(
                    "The active Scene View camera has an invalid pose or " +
                    "field of view; capture was not started.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z) &&
                IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                relativePath));
        }

        private readonly struct CaptureTime
        {
            public CaptureTime(
                string label,
                float hour,
                bool captureSunward)
            {
                Label = label;
                Hour = hour;
                CaptureSunward = captureSunward;
            }

            public string Label { get; }
            public float Hour { get; }
            public bool CaptureSunward { get; }
        }

        private readonly struct BloomState
        {
            private readonly Bloom bloom;
            private readonly bool active;
            private readonly Color tint;
            private readonly bool tintOverride;
            private readonly float intensity;
            private readonly bool intensityOverride;

            public BloomState(Bloom bloom)
            {
                this.bloom = bloom;
                active = bloom != null && bloom.active;
                tint = bloom != null ? bloom.tint.value : Color.white;
                tintOverride = bloom != null && bloom.tint.overrideState;
                intensity = bloom != null ? bloom.intensity.value : 0f;
                intensityOverride =
                    bloom != null && bloom.intensity.overrideState;
            }

            public void Restore()
            {
                if (bloom == null)
                {
                    return;
                }

                bloom.active = active;
                bloom.tint.value = tint;
                bloom.tint.overrideState = tintOverride;
                bloom.intensity.value = intensity;
                bloom.intensity.overrideState = intensityOverride;
            }
        }

        private readonly struct LightState
        {
            private readonly Light light;
            private readonly LightType type;
            private readonly Quaternion rotation;
            private readonly Color color;
            private readonly float intensity;
            private readonly bool useColorTemperature;
            private readonly LightShadows shadows;
            private readonly float shadowStrength;
            private readonly float shadowBias;
            private readonly float shadowNormalBias;

            public LightState(Light light)
            {
                this.light = light;
                type = light != null ? light.type : LightType.Directional;
                rotation = light != null
                    ? light.transform.rotation
                    : Quaternion.identity;
                color = light != null ? light.color : Color.white;
                intensity = light != null ? light.intensity : 0f;
                useColorTemperature =
                    light != null && light.useColorTemperature;
                shadows = light != null ? light.shadows : LightShadows.None;
                shadowStrength =
                    light != null ? light.shadowStrength : 0f;
                shadowBias = light != null ? light.shadowBias : 0f;
                shadowNormalBias =
                    light != null ? light.shadowNormalBias : 0f;
            }

            public void Restore()
            {
                if (light == null)
                {
                    return;
                }

                light.type = type;
                light.transform.rotation = rotation;
                light.color = color;
                light.intensity = intensity;
                light.useColorTemperature = useColorTemperature;
                light.shadows = shadows;
                light.shadowStrength = shadowStrength;
                light.shadowBias = shadowBias;
                light.shadowNormalBias = shadowNormalBias;
            }
        }

        private sealed class RenderSettingsState
        {
            private readonly Material skybox;
            private readonly Material skyboxProperties;
            private readonly Light sun;
            private readonly AmbientMode ambientMode;
            private readonly Color ambientLight;
            private readonly Color ambientSkyColor;
            private readonly Color ambientEquatorColor;
            private readonly Color ambientGroundColor;
            private readonly float ambientIntensity;
            private readonly SphericalHarmonicsL2 ambientProbe;
            private readonly bool fog;
            private readonly FogMode fogMode;
            private readonly Color fogColor;
            private readonly float fogDensity;
            private readonly float fogStartDistance;
            private readonly float fogEndDistance;
            private readonly DefaultReflectionMode reflectionMode;
            private readonly Texture customReflection;
            private readonly float reflectionIntensity;

            public RenderSettingsState()
            {
                skybox = RenderSettings.skybox;
                skyboxProperties = skybox != null
                    ? new Material(skybox)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    }
                    : null;
                sun = RenderSettings.sun;
                ambientMode = RenderSettings.ambientMode;
                ambientLight = RenderSettings.ambientLight;
                ambientSkyColor = RenderSettings.ambientSkyColor;
                ambientEquatorColor = RenderSettings.ambientEquatorColor;
                ambientGroundColor = RenderSettings.ambientGroundColor;
                ambientIntensity = RenderSettings.ambientIntensity;
                ambientProbe = RenderSettings.ambientProbe;
                fog = RenderSettings.fog;
                fogMode = RenderSettings.fogMode;
                fogColor = RenderSettings.fogColor;
                fogDensity = RenderSettings.fogDensity;
                fogStartDistance = RenderSettings.fogStartDistance;
                fogEndDistance = RenderSettings.fogEndDistance;
                reflectionMode = RenderSettings.defaultReflectionMode;
                customReflection = RenderSettings.customReflectionTexture;
                reflectionIntensity = RenderSettings.reflectionIntensity;
            }

            public void Restore()
            {
                if (skybox != null && skyboxProperties != null)
                {
                    skybox.CopyPropertiesFromMaterial(skyboxProperties);
                }

                RenderSettings.skybox = skybox;
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientSkyColor = ambientSkyColor;
                RenderSettings.ambientEquatorColor = ambientEquatorColor;
                RenderSettings.ambientGroundColor = ambientGroundColor;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.ambientProbe = ambientProbe;
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStartDistance;
                RenderSettings.fogEndDistance = fogEndDistance;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.customReflectionTexture = customReflection;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                if (skyboxProperties != null)
                {
                    UnityEngine.Object.DestroyImmediate(skyboxProperties);
                }
            }
        }

    }
}

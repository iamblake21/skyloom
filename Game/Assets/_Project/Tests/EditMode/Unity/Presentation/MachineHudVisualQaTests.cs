using System.Collections;
using System.Collections.Generic;
using System.IO;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Simulation;
using CML.Simulation.Airship;
using CML.Simulation.Machines;
using CML.Unity.Presentation.Machines;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CML.Tests.Unity.Presentation
{
    /// <summary>
    /// UI-MACH-001, layout half. It renders the panel at both target resolutions in the
    /// three states that matter — working, starved, held — and asserts that the progress
    /// bar and the cause line actually resolve to something visible. A panel whose bar
    /// resolves to zero width is a panel that says nothing, however correct its data.
    /// </summary>
    public sealed class MachineHudVisualQaTests
    {
        private static readonly CatalogRevision Revision =
            new CatalogRevision(CatalogSchema.BootstrapContentRevision);

        private static readonly StableId Press = new StableId(0x9500000000000000UL, 1UL);

        [UnityTest]
        public IEnumerator RendersEveryStateAtTargetAspectRatios()
        {
            var outputRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "outputs", "MachineHUD"));
            Directory.CreateDirectory(outputRoot);

            yield return Render(1920, 1080, MachineState.Running, outputRoot);
            yield return Render(1920, 1080, MachineState.Starved, outputRoot);
            yield return Render(1920, 1080, MachineState.Held, outputRoot);
            yield return Render(3440, 1440, MachineState.Running, outputRoot);
            yield return Render(3440, 1440, MachineState.Held, outputRoot);
        }

        private enum MachineState
        {
            Running,
            Starved,
            Held
        }

        private static IEnumerator Render(
            int width,
            int height,
            MachineState state,
            string outputRoot)
        {
            const string prefabPath =
                "Assets/_Project/Art/UI/Machine/PF_MachineHUD.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"missing {prefabPath}");

            var instance = Object.Instantiate(prefab);
            var document = instance.GetComponent<UIDocument>();
            var controller = instance.GetComponent<MachineHudController>();
            Assert.That(document, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);

            var panel = Object.Instantiate(document.panelSettings);
            panel.name = $"PS_MachineHUD_QA_{width}x{height}";
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = $"RT_MachineHUD_QA_{width}x{height}",
                antiAliasing = 1
            };
            target.Create();
            panel.targetTexture = target;
            document.panelSettings = panel;

            AddQaBackground(document.rootVisualElement);
            controller.Bind(Snapshot(state));
            controller.SetPanelOpen(true);

            yield return null;
            yield return null;

            var root = document.rootVisualElement;
            var fill = root.Q<VisualElement>("progress-fill");
            var causeLabel = root.Q<Label>("cause-label");
            var firstInputSlot = root.Q<VisualElement>("machine-input-slot-0");
            Assert.That(fill, Is.Not.Null);
            Assert.That(causeLabel, Is.Not.Null);
            Assert.That(firstInputSlot, Is.Not.Null);

            Assert.That(
                causeLabel.text,
                Is.Not.Empty,
                "the cause line is never allowed to be blank");
            Assert.That(
                causeLabel.resolvedStyle.width,
                Is.GreaterThan(0f),
                "the cause line resolved to zero width, so it reads as nothing");
            Assert.That(firstInputSlot.resolvedStyle.width, Is.GreaterThan(0f));

            var track = root.Q<VisualElement>("progress-track");
            var panelElement = root.Q<VisualElement>("machine-panel");
            Debug.Log(
                $"QA_BAR state={state} w={width} "
                + $"trackW={track.resolvedStyle.width:F1} "
                + $"fillW={fill.resolvedStyle.width:F1} "
                + $"trackBg={track.resolvedStyle.backgroundColor} "
                + $"fillBg={fill.resolvedStyle.backgroundColor} "
                + $"blocked={panelElement.ClassListContains("machine-panel--blocked")}");

            if (state == MachineState.Running || state == MachineState.Held)
            {
                Assert.That(
                    fill.resolvedStyle.width,
                    Is.GreaterThan(0f),
                    "a machine with progress must show some bar");
            }

            var previous = RenderTexture.active;
            var capture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                capture.Apply(false, false);
                var png = capture.EncodeToPNG();
                File.WriteAllBytes(
                    Path.Combine(
                        outputRoot,
                        $"machine_{state.ToString().ToLowerInvariant()}_{width}x{height}.png"),
                    png);

                // Under -nographics there is no device and UI Toolkit draws nothing, so
                // the capture is a flat grey field. Asserting the PNG's byte length would
                // pass on that blank image and prove nothing at all; say so instead.
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    Assert.Ignore(
                        "No graphics device: layout was verified, the capture was not. "
                        + "Run the suite with graphics to check the pixels.");
                }

                AssertCaptureIsNotBlank(capture, width, height);
                AssertBarIsDrawnInItsOwnColour(capture, root, track, state);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(capture);
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(panel);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// Samples the pixels the bar actually occupies.
        ///
        /// This exists because resolvedStyle lied once already: it reported the right
        /// widths and the right colours while UI Toolkit drew the track as opaque white
        /// in every state, because a rounded 5 px box with overflow clipping degenerated.
        /// A style assertion could not have caught that; only the drawn pixels could.
        /// </summary>
        private static void AssertBarIsDrawnInItsOwnColour(
            Texture2D capture,
            VisualElement root,
            VisualElement track,
            MachineState state)
        {
            // worldBound is in the panel's logical units. PanelSettings scales those to
            // pixels, and at 3440 × 1440 the factor is neither 1 nor the width ratio, so
            // it is read off the root instead of assumed.
            var scale = capture.width / root.worldBound.width;
            var bounds = track.worldBound;
            var y = capture.height - Mathf.RoundToInt(bounds.center.y * scale);
            var sampleAt = state == MachineState.Starved
                ? (bounds.xMax - 4f) * scale  // empty bar: bare track
                : (bounds.xMin + 4f) * scale; // filled bar: inside the fill
            var x = Mathf.Clamp(Mathf.RoundToInt(sampleAt), 0, capture.width - 1);
            y = Mathf.Clamp(y, 0, capture.height - 1);
            var pixel = capture.GetPixel(x, y);

            Debug.Log($"QA_BARPIXEL state={state} scale={scale:F3} at=({x},{y}) drew={pixel}");

            Assert.That(
                pixel.r > 0.98f && pixel.g > 0.98f && pixel.b > 0.98f,
                Is.False,
                $"the {state} bar drew as opaque white at ({x}, {y}); "
                + "the element is being rendered untinted");

            if (state == MachineState.Held)
            {
                // Held means finished and going nowhere. Gold would read as working, so
                // the bar has to be visibly warmer on red than on green.
                Assert.That(
                    pixel.r,
                    Is.GreaterThan(pixel.g + 0.10f),
                    $"a held bar must read as the warning colour, drew {pixel}");
            }
        }

        /// <summary>
        /// A rendered panel has a background, glass and text, so it cannot be one colour.
        /// Counting distinct colours over a sparse grid is enough to tell a drawn panel
        /// from an empty buffer, and it costs nothing.
        /// </summary>
        private static void AssertCaptureIsNotBlank(Texture2D capture, int width, int height)
        {
            var colours = new HashSet<uint>();
            for (var y = 0; y < height; y += 8)
            {
                for (var x = 0; x < width; x += 8)
                {
                    var pixel = capture.GetPixel(x, y);
                    colours.Add(
                        ((uint)(pixel.r * 255f) << 16)
                        | ((uint)(pixel.g * 255f) << 8)
                        | (uint)(pixel.b * 255f));
                }
            }

            Assert.That(
                colours.Count,
                Is.GreaterThan(8),
                $"the capture holds {colours.Count} distinct colours, which is a blank "
                + "buffer and not a rendered panel");
        }

        private static MachineUiSnapshot Snapshot(MachineState state)
        {
            var catalog = BootstrapCatalog.Load();
            var builder = new MachineSimulationStateBuilder(catalog)
                .AddMachine(Press, ContentIds.MechanicalPress, ContentIds.PressIronPlate);
            var ticks = 1;

            switch (state)
            {
                case MachineState.Running:
                    builder.Store(Press, ContentIds.IronIngot, 12);
                    ticks = 25;
                    break;

                case MachineState.Starved:
                    builder.Store(Press, ContentIds.IronIngot, 1);
                    break;

                default:
                    builder.StoreInOutput(Press, ContentIds.IronPlate, 100);
                    builder.WithCycleInFlight(Press, 2500L);
                    break;
            }

            var simulationState = new SimulationState(
                new SimulationTick(0UL),
                Revision,
                new AirshipSimulationState(),
                builder.Build());
            var engine = new SimulationEngine(simulationState, null, catalog);
            for (var index = 0; index < ticks; index++)
            {
                Assert.That(engine.AdvanceOneTick().Committed, Is.True);
            }

            Assert.That(
                MachineDiagnostics.TryDescribe(engine.State, catalog, Press, out var report),
                Is.True);
            return MachineHudPresenter.Project(report, catalog);
        }

        private static void AddQaBackground(VisualElement root)
        {
            var background = new VisualElement { name = "qa-world-background" };
            background.style.position = Position.Absolute;
            background.style.left = 0f;
            background.style.top = 0f;
            background.style.right = 0f;
            background.style.bottom = 0f;
            background.style.backgroundColor = new Color(0.45f, 0.67f, 0.48f, 1f);

            var sky = new VisualElement();
            sky.style.position = Position.Absolute;
            sky.style.left = 0f;
            sky.style.top = 0f;
            sky.style.right = 0f;
            sky.style.height = Length.Percent(46f);
            sky.style.backgroundColor = new Color(0.52f, 0.77f, 0.86f, 1f);
            background.Add(sky);

            root.Insert(0, background);
        }
    }
}

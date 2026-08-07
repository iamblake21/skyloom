using System.IO;
using CML.Diagnostics;
using CML.Simulation.Airship;
using CML.Unity.Airship;
using CML.Unity.Bootstrap;
using UnityEngine;

namespace CML.Unity.Presentation
{
    [DisallowMultipleComponent]
    public sealed class BuildInfoOverlay : MonoBehaviour
    {
        private BuildManifestLoadResult loadResult;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private AirshipTechnicalScenario airshipScenario;

        private void Awake()
        {
            var path = Path.Combine(Application.streamingAssetsPath, BuildManifestLoader.FileName);
            loadResult = BuildManifestLoader.LoadFromPath(path);
        }

        private void OnGUI()
        {
            EnsureStyles();

            var width = Mathf.Min(560f, Screen.width - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, width, 440f), GUI.skin.box);
            GUILayout.Label("CHANGING MY LIFE — TECHNICAL BUILD", titleStyle);

            if (loadResult.Succeeded)
            {
                DrawManifest(loadResult.Manifest);
            }
            else if (Application.isEditor && loadResult.Diagnostic == DiagnosticCode.MissingBuildManifest)
            {
                GUILayout.Label("Editor development session", bodyStyle);
                GUILayout.Label($"Unity: {Application.unityVersion}", bodyStyle);
                GUILayout.Label("Build manifest: generated only for a player build", bodyStyle);
            }
            else
            {
                GUILayout.Label($"Diagnostic: {loadResult.Diagnostic}", bodyStyle);
                GUILayout.Label(loadResult.Detail, bodyStyle);
            }

            DrawFlightStatus();
            GUILayout.Space(8f);
            GUILayout.Label("CONTROLLI", titleStyle);
            GUILayout.Label("E — entra/esci dalla postazione di guida", bodyStyle);
            GUILayout.Label(
                "W / S — aumenta o riduce il gas, fino alla retromarcia",
                bodyStyle);
            GUILayout.Label(
                "Mouse — sterza e inclina l'aeronave mentre è in moto",
                bodyStyle);
            GUILayout.Label(
                "Spazio / Shift sinistro — sali / scendi",
                bodyStyle);
            GUILayout.Label(
                "L — atterraggio   ·   X — sali/scendi dall'aeronave",
                bodyStyle);

            GUILayout.EndArea();
        }

        private void DrawFlightStatus()
        {
            if (airshipScenario == null)
            {
                airshipScenario =
                    Object.FindFirstObjectByType<AirshipTechnicalScenario>();
            }

            if (airshipScenario == null
                || !airshipScenario.IsReady
                || !airshipScenario.Bridge.GetAirshipSnapshot().TryGetAirship(
                    AirshipTechnicalIds.Airship,
                    out var airship))
            {
                return;
            }

            var pitchDegrees = airship.PitchTurnUnits * (360f / 65_536f);
            GUILayout.Space(8f);
            GUILayout.Label(
                $"VOLO  {(airship.PilotId.IsNone ? "guida libera" : "guida attiva")}"
                + $"   ·   {airship.ForwardSpeedMillimetresPerSecond / 1000f:+0.00;-0.00;0.00} m/s"
                + $"   ·   quota {airship.Pose.Position.Y / 1000f:0.0} m"
                + $"   ·   assetto {pitchDegrees:+0.0;-0.0;0.0}°",
                bodyStyle);
        }

        private void DrawManifest(BuildManifest manifest)
        {
            GUILayout.Label($"Version: {manifest.productVersion}", bodyStyle);
            GUILayout.Label($"Build: {manifest.buildId}", bodyStyle);
            GUILayout.Label($"Commit: {manifest.gitCommit}{(manifest.gitDirty ? " (dirty)" : string.Empty)}", bodyStyle);
            GUILayout.Label($"Unity: {manifest.unityVersion}", bodyStyle);
            GUILayout.Label($"Save schema: {manifest.saveSchemaVersion}", bodyStyle);
            GUILayout.Label($"Catalog schema: {manifest.catalogSchemaVersion}", bodyStyle);
            GUILayout.Label($"Content: {manifest.contentRevision}", bodyStyle);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };

            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };
        }
    }
}

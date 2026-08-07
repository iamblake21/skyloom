using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CML.Content;
using CML.Diagnostics;
using CML.Persistence;
using CML.Unity.Bootstrap;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CML.Editor.Build
{
    public static class BuildEntryPoint
    {
        public static void ValidateCatalogs()
        {
            var catalog = BootstrapCatalog.Load();
            Debug.Log(
                $"CML catalog validation passed: schema={catalog.SchemaVersion}, "
                + $"revision={catalog.Revision.Value}.");
        }

        public static void BuildWindows()
        {
            ValidateCatalogs();

            var repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            var artifactsRoot = Environment.GetEnvironmentVariable("CML_ARTIFACTS_DIR");
            if (string.IsNullOrWhiteSpace(artifactsRoot))
            {
                artifactsRoot = Path.Combine(repositoryRoot, "Artifacts");
            }

            var windowsDirectory = Path.Combine(artifactsRoot, "Windows");
            Directory.CreateDirectory(windowsDirectory);

            var executablePath = Path.Combine(windowsDirectory, "ChangingMyLife.exe");
            var scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scene is configured for the Windows build.");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows build failed: {report.summary.result}, {report.summary.totalErrors} error(s).");
            }

            var manifest = CreateManifest(repositoryRoot);
            var streamingAssetsPath = Path.Combine(
                windowsDirectory,
                Path.GetFileNameWithoutExtension(executablePath) + "_Data",
                "StreamingAssets");

            Directory.CreateDirectory(streamingAssetsPath);
            var manifestPath = Path.Combine(streamingAssetsPath, BuildManifestLoader.FileName);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));

            Debug.Log($"CML Windows build completed: {executablePath}");
            Debug.Log($"CML build manifest written: {manifestPath}");
        }

        private static string[] EnabledScenes()
        {
            var enabled = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path))
                {
                    enabled.Add(scene.path);
                }
            }

            return enabled.ToArray();
        }

        private static BuildManifest CreateManifest(string repositoryRoot)
        {
            var commit = RunGit(repositoryRoot, "rev-parse --short=12 HEAD", "uncommitted");
            var dirty = !string.IsNullOrWhiteSpace(RunGit(repositoryRoot, "status --porcelain", string.Empty));
            var now = DateTime.UtcNow;

            return new BuildManifest
            {
                manifestFormat = BuildManifest.CurrentFormat,
                productVersion = PlayerSettings.bundleVersion,
                buildId = $"{now:yyyyMMddTHHmmssZ}-{commit}{(dirty ? "-dirty" : string.Empty)}",
                gitCommit = commit,
                gitDirty = dirty,
                builtAtUtc = now.ToString("O"),
                unityVersion = Application.unityVersion,
                buildTarget = BuildTarget.StandaloneWindows64.ToString(),
                saveSchemaVersion = SaveSchema.CurrentVersion,
                catalogSchemaVersion = CatalogSchema.CurrentVersion,
                contentRevision = CatalogSchema.BootstrapContentRevision
            };
        }

        private static string RunGit(string workingDirectory, string arguments, string fallback)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return fallback;
                    }

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5000);
                    return process.ExitCode == 0 ? output : fallback;
                }
            }
            catch
            {
                return fallback;
            }
        }
    }
}

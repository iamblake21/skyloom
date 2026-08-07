using System;
using System.IO;
using CML.Diagnostics;
using UnityEngine;

namespace CML.Unity.Bootstrap
{
    public readonly struct BuildManifestLoadResult
    {
        public BuildManifestLoadResult(BuildManifest manifest, DiagnosticCode diagnostic, string detail)
        {
            Manifest = manifest;
            Diagnostic = diagnostic;
            Detail = detail ?? string.Empty;
        }

        public BuildManifest Manifest { get; }

        public DiagnosticCode Diagnostic { get; }

        public string Detail { get; }

        public bool Succeeded => Diagnostic == DiagnosticCode.None && Manifest != null;
    }

    public static class BuildManifestLoader
    {
        public const string FileName = "BuildManifest.json";

        public static BuildManifestLoadResult LoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new BuildManifestLoadResult(null, DiagnosticCode.MissingBuildManifest, path ?? string.Empty);
            }

            try
            {
                var json = File.ReadAllText(path);
                var manifest = JsonUtility.FromJson<BuildManifest>(json);
                if (manifest == null || manifest.manifestFormat != BuildManifest.CurrentFormat)
                {
                    return new BuildManifestLoadResult(manifest, DiagnosticCode.UnsupportedBuildManifest, path);
                }

                if (!manifest.HasRequiredIdentity())
                {
                    return new BuildManifestLoadResult(manifest, DiagnosticCode.CorruptBuildManifest, path);
                }

                return new BuildManifestLoadResult(manifest, DiagnosticCode.None, string.Empty);
            }
            catch (Exception exception)
            {
                return new BuildManifestLoadResult(null, DiagnosticCode.CorruptBuildManifest, exception.Message);
            }
        }
    }
}

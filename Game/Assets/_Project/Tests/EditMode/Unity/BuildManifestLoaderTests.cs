using System;
using System.IO;
using CML.Diagnostics;
using CML.Unity.Bootstrap;
using NUnit.Framework;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class BuildManifestLoaderTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "CML.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, true);
            }
        }

        [Test]
        public void MissingManifestProducesExplicitDiagnostic()
        {
            var result = BuildManifestLoader.LoadFromPath(Path.Combine(temporaryDirectory, "missing.json"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostic, Is.EqualTo(DiagnosticCode.MissingBuildManifest));
        }

        [Test]
        public void CorruptManifestProducesExplicitDiagnostic()
        {
            var path = Path.Combine(temporaryDirectory, BuildManifestLoader.FileName);
            File.WriteAllText(path, "{ definitely-not-json }");

            var result = BuildManifestLoader.LoadFromPath(path);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostic, Is.Not.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void ValidManifestLoadsWithoutDiagnostic()
        {
            var path = Path.Combine(temporaryDirectory, BuildManifestLoader.FileName);
            var manifest = new BuildManifest
            {
                manifestFormat = BuildManifest.CurrentFormat,
                productVersion = "0.0.1-test",
                buildId = "test-build",
                gitCommit = "test",
                gitDirty = false,
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                buildTarget = "StandaloneWindows64",
                saveSchemaVersion = 1,
                catalogSchemaVersion = 1,
                contentRevision = "test-content"
            };
            File.WriteAllText(path, JsonUtility.ToJson(manifest));

            var result = BuildManifestLoader.LoadFromPath(path);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Manifest.buildId, Is.EqualTo(manifest.buildId));
        }
    }
}

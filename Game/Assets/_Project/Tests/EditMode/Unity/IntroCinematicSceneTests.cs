using System;
using System.Linq;
using CML.Unity.Bootstrap;
using CML.Unity.Presentation.Intro;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CML.Tests.Unity
{
    /// <summary>
    /// The opening sequence is editor-generated, so its bindings and build
    /// order need a contract test just like the gameplay scenes.
    /// </summary>
    public sealed class IntroCinematicSceneTests
    {
        private const string IntroScenePath =
            "Assets/_Project/Scenes/01_IntroCinematic.unity";
        private const string BootstrapScenePath =
            "Assets/_Project/Scenes/00_Bootstrap.unity";

        [Test]
        public void IntroSceneHasACompleteCinematicController()
        {
            var scene = EditorSceneManager.OpenScene(
                IntroScenePath,
                OpenSceneMode.Single);

            Assert.That(scene.IsValid(), Is.True);

            var controller = FindComponent<IntroCinematicController>(scene);
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.HasCinematicBindings,
                Is.True,
                "Rigenerare con CML/Cinematics/Rebuild Intro Sequence.");
            Assert.That(controller.AlertLightCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(
                controller.GameplaySceneName,
                Is.EqualTo("91_StarterIsland_Terrain_Review"));
            Assert.That(
                controller.TotalDurationSeconds,
                Is.GreaterThan(20f),
                "La sequenza deve contenere tutti e nove gli stacchi.");

            var revision = FindComponent<GeneratedSceneRevision>(scene);
            Assert.That(revision, Is.Not.Null);
            Assert.That(
                revision.Matches(
                    GeneratedSceneRevision.IntroSceneId,
                    GeneratedSceneRevision.CurrentIntroRevision),
                Is.True);
        }

        [Test]
        public void IntroSceneShipsImmediatelyAfterBootstrap()
        {
            var scenes = EditorBuildSettings.scenes;
            var bootstrapIndex = Array.FindIndex(
                scenes,
                scene => string.Equals(
                    scene.path,
                    BootstrapScenePath,
                    StringComparison.Ordinal));
            var introIndex = Array.FindIndex(
                scenes,
                scene => string.Equals(
                    scene.path,
                    IntroScenePath,
                    StringComparison.Ordinal));

            Assert.That(bootstrapIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(introIndex, Is.EqualTo(bootstrapIndex + 1));
            Assert.That(scenes[introIndex].enabled, Is.True);
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }
    }
}

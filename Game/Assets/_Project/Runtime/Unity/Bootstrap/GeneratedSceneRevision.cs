using System;
using UnityEngine;

namespace CML.Unity.Bootstrap
{
    /// <summary>
    /// Version stamp for editor-generated scenes. The setup tool may preserve a
    /// byte-identical scene only after both this stamp and its structure validate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedSceneRevision : MonoBehaviour
    {
        public const string BootstrapSceneId = "cml.bootstrap";
        public const string IntroSceneId = "cml.intro.cinematic";
        public const string TechnicalSceneId = "cml.air.technical";
        public const int CurrentBootstrapRevision = 1;
        public const int CurrentIntroRevision = 2;
        public const int CurrentTechnicalRevision = 3;

        [SerializeField] private string sceneId = string.Empty;
        [SerializeField, Min(1)] private int revision;

        public string SceneId => sceneId;

        public int Revision => revision;

        public void Configure(string generatedSceneId, int generatedRevision)
        {
            if (string.IsNullOrWhiteSpace(generatedSceneId))
            {
                throw new ArgumentException(
                    "A generated scene requires an id.",
                    nameof(generatedSceneId));
            }

            if (generatedRevision <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatedRevision));
            }

            sceneId = generatedSceneId;
            revision = generatedRevision;
        }

        public bool Matches(string expectedSceneId, int expectedRevision)
        {
            return string.Equals(
                    sceneId,
                    expectedSceneId,
                    StringComparison.Ordinal)
                && revision == expectedRevision;
        }

        private void OnValidate()
        {
            revision = Mathf.Max(0, revision);
        }
    }
}

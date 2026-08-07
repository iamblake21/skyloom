using System;

namespace CML.Diagnostics
{
    [Serializable]
    public sealed class BuildManifest
    {
        public const int CurrentFormat = 1;

        public int manifestFormat;
        public string productVersion;
        public string buildId;
        public string gitCommit;
        public bool gitDirty;
        public string builtAtUtc;
        public string unityVersion;
        public string buildTarget;
        public int saveSchemaVersion;
        public int catalogSchemaVersion;
        public string contentRevision;

        public bool HasRequiredIdentity()
        {
            return manifestFormat == CurrentFormat
                && !string.IsNullOrWhiteSpace(productVersion)
                && !string.IsNullOrWhiteSpace(buildId)
                && !string.IsNullOrWhiteSpace(unityVersion)
                && !string.IsNullOrWhiteSpace(buildTarget);
        }
    }
}

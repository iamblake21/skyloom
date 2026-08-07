using System;

namespace CML.Persistence
{
    /// <summary>
    /// Versioned persistence boundary. The complete payload starts with SAVE-001.
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope
    {
        public int schemaVersion;
        public ulong simulationTick;
        public string contentRevision;
    }
}

using System;
using CML.Foundation;
using UnityEngine;

namespace CML.Unity.Factory
{
    /// <summary>
    /// Unity cannot reliably expose the immutable <see cref="StableId"/> struct in an
    /// inspector. This field stores its canonical 32-character representation while
    /// keeping the runtime-facing API strongly typed.
    /// </summary>
    [Serializable]
    public sealed class FactoryStableIdField
    {
        [SerializeField] private string value = string.Empty;

        public string SerializedValue => value ?? string.Empty;

        public bool TryGetValue(out StableId id)
        {
            return StableId.TryParse(value, out id) && !id.IsNone;
        }

        public StableId GetValueOrNone()
        {
            return TryGetValue(out var id) ? id : StableId.None;
        }

        public void Set(StableId id)
        {
            value = id.IsNone ? string.Empty : id.ToString();
        }
    }
}

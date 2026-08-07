using System;

namespace CML.Content
{
    [Serializable]
    public readonly struct CatalogRevision : IEquatable<CatalogRevision>
    {
        public CatalogRevision(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A catalog revision cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(CatalogRevision other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is CatalogRevision other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;
    }
}

using System;
using System.Collections.Generic;

namespace CML.Content
{
    public static class CatalogLoader
    {
        public static GameCatalog Load(CatalogDocument document)
        {
            var errors = CatalogValidator.Validate(document);
            if (errors.Count != 0)
            {
                throw new CatalogValidationException(errors);
            }

            return new GameCatalog(document);
        }

        public static bool TryLoad(
            CatalogDocument document,
            out GameCatalog catalog,
            out IReadOnlyList<CatalogValidationError> errors)
        {
            errors = CatalogValidator.Validate(document);
            if (errors.Count != 0)
            {
                catalog = null;
                return false;
            }

            catalog = new GameCatalog(document);
            return true;
        }
    }

    [Serializable]
    public sealed class CatalogValidationException : Exception
    {
        public CatalogValidationException(IReadOnlyList<CatalogValidationError> errors)
            : base(CreateMessage(errors))
        {
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public IReadOnlyList<CatalogValidationError> Errors { get; }

        private static string CreateMessage(IReadOnlyList<CatalogValidationError> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            return "Catalog validation failed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors);
        }
    }
}

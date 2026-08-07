using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CML.Content;
using CML.Diagnostics;
using CML.Foundation;
using CML.Inventory;
using CML.Persistence;
using CML.Simulation;
using NUnit.Framework;

namespace CML.Tests.Pure
{
    public sealed class AssemblyBoundaryTests
    {
        [Test]
        public void PureAssembliesDoNotReferenceUnity()
        {
            foreach (var assembly in PureAssemblies())
            {
                var forbidden = assembly.GetReferencedAssemblies()
                    .Where(reference =>
                        reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)
                        || reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal)
                        || string.Equals(reference.Name, "CML.Unity", StringComparison.Ordinal))
                    .Select(reference => reference.Name)
                    .ToArray();

                Assert.That(
                    forbidden,
                    Is.Empty,
                    $"{assembly.GetName().Name} crosses the pure simulation boundary.");
            }
        }

        [Test]
        public void PersistenceAndDiagnosticsRemainSiblingAssemblies()
        {
            AssertDoesNotReference(typeof(SaveSchema).Assembly, typeof(BuildManifest).Assembly.GetName().Name);
            AssertDoesNotReference(typeof(BuildManifest).Assembly, typeof(SaveSchema).Assembly.GetName().Name);
        }

        private static IEnumerable<Assembly> PureAssemblies()
        {
            yield return typeof(StableId).Assembly;
            yield return typeof(CatalogSchema).Assembly;
            yield return typeof(InventoryState).Assembly;
            yield return typeof(SimulationState).Assembly;
            yield return typeof(SaveSchema).Assembly;
            yield return typeof(BuildManifest).Assembly;
        }

        private static void AssertDoesNotReference(Assembly source, string forbiddenName)
        {
            var references = source.GetReferencedAssemblies().Select(reference => reference.Name);
            Assert.That(references, Does.Not.Contain(forbiddenName));
        }
    }
}

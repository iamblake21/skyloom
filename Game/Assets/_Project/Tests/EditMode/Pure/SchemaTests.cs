using CML.Content;
using CML.Foundation;
using CML.Persistence;
using NUnit.Framework;

namespace CML.Tests.Pure
{
    public sealed class SchemaTests
    {
        [Test]
        public void InitialSchemaVersionsArePositive()
        {
            Assert.That(CatalogSchema.CurrentVersion, Is.GreaterThan(0));
            Assert.That(SaveSchema.CurrentVersion, Is.GreaterThan(0));
        }

        [Test]
        public void StableIdRoundTripsThroughCanonicalText()
        {
            var expected = new StableId(0x0123456789abcdefUL, 0xfedcba9876543210UL);

            var parsed = StableId.TryParse(expected.ToString(), out var actual);

            Assert.That(parsed, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void SimulationClockContractIsTwentyHertz()
        {
            Assert.That(SimulationTick.TicksPerSecond, Is.EqualTo(20));
            Assert.That(SimulationTick.MillisecondsPerTick, Is.EqualTo(50));
        }
    }
}

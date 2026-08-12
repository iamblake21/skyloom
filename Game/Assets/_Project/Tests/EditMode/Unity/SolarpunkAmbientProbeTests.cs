using CML.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace CML.Tests.Unity
{
    public sealed class SolarpunkAmbientProbeTests
    {
        [Test]
        public void WhiteSourceTintReconstructsMeasuredCardinalIrradiance()
        {
            var probe = SolarpunkAmbientProbe.Evaluate(Color.white);
            var directions = new[]
            {
                Vector3.up,
                Vector3.down,
                Vector3.forward,
                Vector3.back
            };
            var results = new Color[directions.Length];

            probe.Evaluate(directions, results);

            AssertColor(
                results[0],
                new Color(0.354689601f, 0.436219392f, 0.572413458f, 1f));
            AssertColor(
                results[1],
                new Color(0.193700683f, 0.162734935f, 0.079315952f, 1f));
            AssertColor(
                results[2],
                new Color(0.351066995f, 0.363911620f, 0.361540466f, 1f));
            AssertColor(
                results[3],
                new Color(0.279589419f, 0.309479119f, 0.339386291f, 1f));
        }

        [Test]
        public void TimelineTintScalesEachProbeChannelInLinearSpace()
        {
            var sample = SolarpunkDayNightProfile.Evaluate(12f);
            var probe = SolarpunkAmbientProbe.Evaluate(sample.SkyLightColor);

            Assert.That(
                probe[0, 0],
                Is.EqualTo(0.274627029f).Within(0.000001f));
            Assert.That(
                probe[1, 0],
                Is.EqualTo(0.277924880f).Within(0.000001f));
            Assert.That(
                probe[2, 0],
                Is.EqualTo(0.273806985f).Within(0.000001f));
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.000002f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.000002f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.000002f));
        }
    }
}

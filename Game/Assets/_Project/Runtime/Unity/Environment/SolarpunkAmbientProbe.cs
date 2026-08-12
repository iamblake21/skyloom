using UnityEngine;
using UnityEngine.Rendering;

namespace CML.Unity.World
{
    /// <summary>
    /// Builds the diffuse ambient probe measured from the source SkyLight.
    /// Only its nine low-frequency irradiance coefficients are retained; the
    /// studied panorama is not distributed with or sampled by the game.
    /// </summary>
    public static class SolarpunkAmbientProbe
    {
        private const float SourceSkyLightIntensity = 0.7f;

        // Unity stores convolved SH as polynomial coefficients in this order:
        // 1, y, z, x, xy, yz, (3z^2-1), zx, (x^2-y^2).
        // These values were integrated in linear space from the source's
        // specified cubemap, with its configured solid lower hemisphere.
        private static readonly float[,] BaseDiffuseCoefficients =
        {
            {
                0.420347464f, 0.114992085f, 0.051055411f,
                0.003343554f, 0.005004769f, 0.019581597f,
                0.015060702f, 0.013582163f, 0.013579416f
            },
            {
                0.456054306f, 0.195346041f, 0.038880358f,
                0.002193050f, 0.003904637f, 0.015598846f,
                0.012469540f, 0.010063659f, 0.015760247f
            },
            {
                0.486555924f, 0.352212504f, 0.015824410f,
                0.000618480f, 0.002079650f, 0.004500702f,
                0.007053023f, 0.004643758f, 0.013981894f
            }
        };

        /// <summary>
        /// Returns the source diffuse probe after applying the extracted
        /// time-varying SkyLight tint and its fixed component intensity.
        /// The tint is a linear-space FLinearColor value.
        /// </summary>
        public static SphericalHarmonicsL2 Evaluate(Color skyLightColor)
        {
            var probe = new SphericalHarmonicsL2();
            for (var channel = 0; channel < 3; channel++)
            {
                var scale = skyLightColor[channel] * SourceSkyLightIntensity;
                for (var coefficient = 0; coefficient < 9; coefficient++)
                {
                    probe[channel, coefficient] =
                        BaseDiffuseCoefficients[channel, coefficient] * scale;
                }
            }

            return probe;
        }
    }
}

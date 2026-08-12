using UnityEngine;

namespace CML.Unity.World
{
    /// <summary>
    /// Engine-neutral evaluation of the measured day/night curves used by the
    /// local visual reference. Values are stored at their original 0..24 hour
    /// key times and are linearly interpolated, matching the source curves.
    /// </summary>
    public static class SolarpunkDayNightProfile
    {
        private const float DayLengthHours = 24f;
        private const float SourceSkyLightIntensity = 0.7f;
        private const float LowerHemisphereSideWeight = 0.49547304f;
        private const float LowerHemisphereDownWeight = 0.99998316f;
        private static readonly Color SourceLowerHemisphereColor =
            new Color(0.27450982f, 0.23137257f, 0.1137255f, 1f);

        // Diffuse irradiance coefficients integrated from the locally studied
        // HDR environment. The texture itself is deliberately not part of the
        // Unity project; these low-frequency measurements are sufficient to
        // reproduce its lighting response with original project assets.
        private static readonly Color UpperHemisphereUpIrradiance =
            new Color(0.5044913f, 0.62206404f, 0.8181545f, 1f);
        private static readonly Color UpperHemisphereSideIrradiance =
            new Color(0.2830239f, 0.34544426f, 0.4382946f, 1f);

        private readonly struct Key
        {
            public Key(float time, float value)
            {
                Time = time;
                Value = value;
            }

            public float Time { get; }
            public float Value { get; }
        }

        private sealed class ColorCurve
        {
            public ColorCurve(Key[] red, Key[] green, Key[] blue)
            {
                Red = red;
                Green = green;
                Blue = blue;
            }

            public Key[] Red { get; }
            public Key[] Green { get; }
            public Key[] Blue { get; }

            public Color Evaluate(float hour)
            {
                return new Color(
                    EvaluateKeys(Red, hour),
                    EvaluateKeys(Green, hour),
                    EvaluateKeys(Blue, hour),
                    1f);
            }
        }

        private static readonly Key[] SunPosition = Keys(
            0f, -45f,
            4.5f, -185f,
            5f, -185f,
            21.92f, 2f,
            24f, -45f);

        private static readonly Key[] DayFactor = Keys(
            4.5f, 0f,
            6f, 1f,
            21.5f, 1f,
            22f, 0f);

        private static readonly ColorCurve SunLight = Curve(
            Keys(4.5f, 0.043137256f, 6.3f, 1f, 8.911777f, 1f,
                20.031704f, 1f, 21.7f, 1f, 21.9f, 0.043137256f),
            Keys(4.5f, 0.09411766f, 6.3f, 0.90641713f,
                8.911777f, 0.87962246f, 20.031704f, 0.8796224f,
                21.7f, 0.24804914f, 21.9f, 0.09411766f),
            Keys(4.5f, 0.2901961f, 6.3f, 0.4166667f,
                8.911777f, 0.7605245f, 20.031704f, 0.7605245f,
                21.7f, 0.037317395f, 21.9f, 0.2901961f));

        private static readonly ColorCurve SunDisc = Curve(
            Keys(4.72873f, 0.004024717f, 5.507781f, 1f, 6.3f, 1f,
                20.35f, 1f, 21f, 0.19215688f, 21.82f, 0.19215688f,
                21.9f, 0.004024717f),
            Keys(4.72873f, 0.00303527f, 5.507781f, 0.9333334f,
                6.3f, 0.59607846f, 20.35f, 0.59607846f,
                21f, 0.019607844f, 21.82f, 0.019607844f,
                21.9f, 0.00303527f),
            Keys(4.72873f, 0.002124689f, 5.507781f, 0.60784316f,
                6.3f, 0f, 20.35f, 0f, 21f, 0f, 21.82f, 0f,
                21.9f, 0.002124689f));

        private static readonly ColorCurve SkyLight = Curve(
            Keys(5f, 0.12941177f, 7f, 0.9375f, 10.005834f, 0.9333334f,
                20.013767f, 0.9333334f, 21.7f, 1f, 21.9f, 0.12941177f),
            Keys(5f, 0.16470589f, 7f, 0.9220402f, 10.005834f, 0.8705883f,
                20.013767f, 0.8705883f, 21.7f, 0.43921572f,
                21.9f, 0.16470589f),
            Keys(5f, 0.20784315f, 7f, 0.8984375f, 10.005834f, 0.80392164f,
                20.013767f, 0.80392164f, 21.7f, 0.33725488f,
                21.9f, 0.20784315f));

        private static readonly ColorCurve LowerHemisphere = Curve(
            Keys(4.487209f, 0.1254902f, 6.862122f, 0.27450982f,
                18.888287f, 0.27450982f, 21.9f, 0.1254902f),
            Keys(4.487209f, 0.33333334f, 6.862122f, 0.23137257f,
                18.888287f, 0.23137257f, 21.9f, 0.33333334f),
            Keys(4.487209f, 0.3647059f, 6.862122f, 0.1137255f,
                18.888287f, 0.1137255f, 21.9f, 0.3647059f));

        private static readonly ColorCurve SkyTop = Curve(
            Keys(5f, 0.007843138f, 6f, 0.058823533f, 9f, 0.07934569f,
                18.888287f, 0.08021982f, 21f, 0.16470589f,
                21.9f, 0.007843138f),
            Keys(5f, 0.054901965f, 6f, 0.13725491f, 9f, 0.5960129f,
                18.888287f, 0.59720176f, 21f, 0.25882354f,
                21.9f, 0.054901965f),
            Keys(5f, 0.06666667f, 6f, 0.14117648f, 9f, 0.609375f,
                18.888287f, 0.61049557f, 21f, 0.20392159f,
                21.9f, 0.06666667f));

        private static readonly ColorCurve Horizon = Curve(
            Keys(4.5f, 0.007843138f, 4.919012f, 1f, 6.34f, 1f,
                9f, 0.16862746f, 18.888287f, 0.16862746f,
                21f, 1f, 21.621912f, 1f, 21.9f, 0.007843138f),
            Keys(4.5f, 0.03137255f, 4.919012f, 0.7960785f,
                6.34f, 0.87843144f, 9f, 1f, 18.888287f, 1f,
                21f, 0.7137258f, 21.621912f, 0.4215337f,
                21.9f, 0.03137255f),
            Keys(4.5f, 0.04705883f, 4.919012f, 0f, 6.34f, 0f,
                9f, 1f, 18.888287f, 1f, 21f, 0f, 21.621912f, 0f,
                21.9f, 0.04705883f));

        private static readonly Key[] FogDensity = Keys(
            5f, 0.08f,
            9f, 0.12f,
            18f, 0.12f,
            22f, 0.08f);

        private static readonly Key[] FogFalloff = Keys(
            5.5f, 0.05f,
            7.5f, 0.12f,
            18f, 0.12f,
            22f, 0.05f);

        private static readonly ColorCurve FogInscattering = Curve(
            Keys(4.5f, 0.003921569f, 6f, 0.058823533f,
                8.892391f, 0.07058824f, 19.657072f, 0.07058824f,
                20.98018f, 0.04705883f, 21.622387f, 0.04705883f,
                21.860678f, 0.003921569f),
            Keys(4.5f, 0.019607844f, 6f, 0.13725491f,
                8.892391f, 0.6117647f, 19.657072f, 0.6117647f,
                20.98018f, 0.10588236f, 21.622387f, 0.10588236f,
                21.860678f, 0.019607844f),
            Keys(4.5f, 0.027450982f, 6f, 0.14117648f,
                8.892391f, 0.62352943f, 19.657072f, 0.62352943f,
                20.98018f, 0.10980393f, 21.622387f, 0.10980393f,
                21.860678f, 0.027450982f));

        private static readonly ColorCurve FogDirectional = Curve(
            Keys(4.5f, 0.008023193f, 4.800546f, 1f, 7f, 1f,
                8.975333f, 0.10224173f, 19.960138f, 0.10224173f,
                20.910923f, 1f, 21.787014f, 1f,
                21.87161f, 0.008023193f),
            Keys(4.5f, 0.016807375f, 4.800546f, 0.854902f,
                7f, 0.73725486f, 8.975333f, 0.3515326f,
                19.960138f, 0.3515326f, 20.910923f, 0.16470589f,
                21.787014f, 0.08627451f, 21.87161f, 0.016807375f),
            Keys(4.5f, 0.026241222f, 4.800546f, 0.09019608f,
                7f, 0f, 8.975333f, 0.47353148f,
                19.960138f, 0.47353148f, 20.910923f, 0f,
                21.787014f, 0f, 21.87161f, 0.026241222f));

        private static readonly ColorCurve CloudTop = Curve(
            Keys(4.5f, 0.06666667f, 7.366047f, 0.1274897f,
                9f, 0.23529413f, 20f, 0.23529413f,
                21.2f, 0.21856019f, 21.9f, 0.06666667f),
            Keys(4.5f, 0.21960786f, 7.366047f, 0.23451802f,
                9f, 0.70980394f, 20f, 0.70980394f,
                21.2f, 0.42879364f, 21.9f, 0.21960786f),
            Keys(4.5f, 0.20784315f, 7.366047f, 0.28645834f,
                9f, 0.7294118f, 20f, 0.7294118f,
                21.2f, 0.4375f, 21.9f, 0.20784315f));

        private static readonly ColorCurve CloudBottom = Curve(
            Keys(4.5f, 0.023529414f, 6.34f, 0.31764707f,
                9f, 0.07450981f, 18.888287f, 0.07450981f,
                21f, 0.30980393f, 21.9f, 0.023529414f),
            Keys(4.5f, 0.09019608f, 6.34f, 0.58431375f,
                9f, 0.3019608f, 18.888287f, 0.3019608f,
                21f, 0.44705886f, 21.9f, 0.09019608f),
            Keys(4.5f, 0.15294118f, 6.34f, 0.7137255f,
                9f, 0.49803925f, 18.888287f, 0.49803925f,
                21f, 0.49803925f, 21.9f, 0.15294118f));

        // These are the two colors consumed by the separate 2D cloud layer.
        // They are deliberately kept apart from CloudTop/CloudBottom above:
        // those drive the source mesh-cloud material, while these curves are
        // the broad sky layer visible in the day/night reference.
        private static readonly ColorCurve CloudLayerTop = Curve(
            Keys(4.5f, 0.9646862f, 6.34f, 0.1096409f,
                9f, 0.1367188f, 18.888287f, 0.07421356f,
                21f, 0.30980393f, 21.9f, 0.9646862f),
            Keys(4.5f, 0.9822506f, 6.34f, 0.57757336f,
                9f, 0.724438f, 18.888287f, 0.30054379f,
                21f, 0.44705886f, 21.9f, 0.9822506f),
            Keys(4.5f, 1f, 6.34f, 0.7137255f,
                9f, 0.875f, 18.888287f, 0.49693298f,
                21f, 0.49803925f, 21.9f, 1f));

        private static readonly ColorCurve CloudLayerBottom = Curve(
            Keys(4.5f, 0.023529414f, 6.34f, 0.31764707f,
                9f, 0f, 18.888287f, 0f,
                21f, 1f, 21.9f, 0.023529414f),
            Keys(4.5f, 0.09019608f, 6.34f, 0.58431375f,
                9f, 0.5686275f, 18.888287f, 0.5686275f,
                21f, 0.63529414f, 21.9f, 0.09019608f),
            Keys(4.5f, 0.15294118f, 6.34f, 0.7137255f,
                9f, 1f, 18.888287f, 1f,
                21f, 0f, 21.9f, 0.15294118f));

        private static readonly Key[] CloudOpacity = Keys(
            5f, 0.3f,
            8f, 5f,
            20f, 5f,
            22f, 0.3f);

        private static readonly ColorCurve Bloom = Curve(
            Keys(4.5f, 0.046665087f, 6.3f, 1f,
                19.939472f, 1f, 21.899105f, 0.046875f),
            Keys(4.5f, 0.046665087f, 6.3f, 0.54509807f,
                19.939472f, 0.54509807f, 21.899105f, 0.046875f),
            Keys(4.5f, 0.046665087f, 6.3f, 0.2509804f,
                19.939472f, 0.2509804f, 21.899105f, 0.046875f));

        private static readonly Key[] AmbientOcclusion = Keys(
            4.5f, 0.6f,
            6.1f, 0.9f,
            21.7f, 0.9f,
            21.83f, 0.6f);

        private static readonly Key[] Emissive = Keys(
            5f, 0.15f,
            9f, 1f,
            21.7f, 1f,
            21.83f, 0.15f);

        public static SolarpunkDayNightSample Evaluate(float hour)
        {
            var wrappedHour = WrapHour(hour);
            return new SolarpunkDayNightSample(
                wrappedHour,
                EvaluateKeys(SunPosition, wrappedHour),
                EvaluateKeys(DayFactor, wrappedHour),
                SunLight.Evaluate(wrappedHour),
                SunDisc.Evaluate(wrappedHour),
                SkyLight.Evaluate(wrappedHour),
                LowerHemisphere.Evaluate(wrappedHour),
                SkyTop.Evaluate(wrappedHour),
                Horizon.Evaluate(wrappedHour),
                EvaluateKeys(FogDensity, wrappedHour),
                EvaluateKeys(FogFalloff, wrappedHour),
                FogInscattering.Evaluate(wrappedHour),
                FogDirectional.Evaluate(wrappedHour),
                CloudTop.Evaluate(wrappedHour),
                CloudBottom.Evaluate(wrappedHour),
                CloudLayerTop.Evaluate(wrappedHour),
                CloudLayerBottom.Evaluate(wrappedHour),
                EvaluateKeys(CloudOpacity, wrappedHour),
                Bloom.Evaluate(wrappedHour),
                EvaluateKeys(AmbientOcclusion, wrappedHour),
                EvaluateKeys(Emissive, wrappedHour));
        }

        /// <summary>
        /// Evaluates the source environment as diffuse irradiance for upward,
        /// horizontal and downward normals. This keeps the hemispheres
        /// energy-consistent; treating the source tints as three unrelated
        /// Unity Trilight colors can generate negative SH lobes.
        /// </summary>
        public static SolarpunkDiffuseAmbientSample EvaluateDiffuseAmbient(
            float hour)
        {
            var sample = Evaluate(hour);
            // The source SkyLight lower hemisphere is a fixed component
            // setting. C_SkyLightLowerHemisColor is present as an unused BP
            // property and is never evaluated by the day/night bytecode.
            var lowerSide = SourceLowerHemisphereColor *
                LowerHemisphereSideWeight;
            var upper = MultiplyRgb(
                UpperHemisphereUpIrradiance,
                sample.SkyLightColor) * SourceSkyLightIntensity;
            var side = MultiplyRgb(
                UpperHemisphereSideIrradiance + lowerSide,
                sample.SkyLightColor) * SourceSkyLightIntensity;
            var down = MultiplyRgb(
                SourceLowerHemisphereColor * LowerHemisphereDownWeight,
                sample.SkyLightColor) * SourceSkyLightIntensity;
            upper.a = 1f;
            side.a = 1f;
            down.a = 1f;
            return new SolarpunkDiffuseAmbientSample(upper, side, down);
        }

        public static float WrapHour(float hour)
        {
            return Mathf.Repeat(hour, DayLengthHours);
        }

        private static ColorCurve Curve(Key[] red, Key[] green, Key[] blue)
        {
            return new ColorCurve(red, green, blue);
        }

        private static Key[] Keys(params float[] timeValuePairs)
        {
            var keys = new Key[timeValuePairs.Length / 2];
            for (var index = 0; index < keys.Length; index++)
            {
                keys[index] = new Key(
                    timeValuePairs[index * 2],
                    timeValuePairs[index * 2 + 1]);
            }

            return keys;
        }

        private static float EvaluateKeys(Key[] keys, float hour)
        {
            if (keys == null || keys.Length == 0)
            {
                return 0f;
            }

            if (hour <= keys[0].Time)
            {
                return keys[0].Value;
            }

            var last = keys[keys.Length - 1];
            if (hour >= last.Time)
            {
                return last.Value;
            }

            for (var index = 0; index < keys.Length - 1; index++)
            {
                var left = keys[index];
                var right = keys[index + 1];
                if (hour > right.Time)
                {
                    continue;
                }

                var duration = right.Time - left.Time;
                if (duration <= Mathf.Epsilon)
                {
                    return right.Value;
                }

                var blend = (hour - left.Time) / duration;
                return Mathf.LerpUnclamped(left.Value, right.Value, blend);
            }

            return last.Value;
        }

        private static Color MultiplyRgb(Color left, Color right)
        {
            return new Color(
                left.r * right.r,
                left.g * right.g,
                left.b * right.b,
                1f);
        }
    }

    public readonly struct SolarpunkDiffuseAmbientSample
    {
        public SolarpunkDiffuseAmbientSample(Color up, Color side, Color down)
        {
            Up = up;
            Side = side;
            Down = down;
        }

        public Color Up { get; }
        public Color Side { get; }
        public Color Down { get; }
    }

    public readonly struct SolarpunkDayNightSample
    {
        public SolarpunkDayNightSample(
            float hour,
            float sunPosition,
            float dayFactor,
            Color sunLightColor,
            Color sunDiscColor,
            Color skyLightColor,
            Color lowerHemisphereColor,
            Color skyTopColor,
            Color horizonColor,
            float fogDensity,
            float fogFalloff,
            Color fogInscatteringColor,
            Color fogDirectionalColor,
            Color cloudTopColor,
            Color cloudBottomColor,
            Color cloudLayerTopColor,
            Color cloudLayerBottomColor,
            float cloudOpacity,
            Color bloomColor,
            float ambientOcclusion,
            float emissive)
        {
            Hour = hour;
            SunPosition = sunPosition;
            DayFactor = dayFactor;
            SunLightColor = sunLightColor;
            SunDiscColor = sunDiscColor;
            SkyLightColor = skyLightColor;
            LowerHemisphereColor = lowerHemisphereColor;
            SkyTopColor = skyTopColor;
            HorizonColor = horizonColor;
            FogDensity = fogDensity;
            FogFalloff = fogFalloff;
            FogInscatteringColor = fogInscatteringColor;
            FogDirectionalColor = fogDirectionalColor;
            CloudTopColor = cloudTopColor;
            CloudBottomColor = cloudBottomColor;
            CloudLayerTopColor = cloudLayerTopColor;
            CloudLayerBottomColor = cloudLayerBottomColor;
            CloudOpacity = cloudOpacity;
            BloomColor = bloomColor;
            AmbientOcclusion = ambientOcclusion;
            Emissive = emissive;
        }

        public float Hour { get; }
        public float SunPosition { get; }
        public float DayFactor { get; }
        public Color SunLightColor { get; }
        public Color SunDiscColor { get; }
        public Color SkyLightColor { get; }
        public Color LowerHemisphereColor { get; }
        public Color SkyTopColor { get; }
        public Color HorizonColor { get; }
        public float FogDensity { get; }
        public float FogFalloff { get; }
        public Color FogInscatteringColor { get; }
        public Color FogDirectionalColor { get; }
        public Color CloudTopColor { get; }
        public Color CloudBottomColor { get; }
        public Color CloudLayerTopColor { get; }
        public Color CloudLayerBottomColor { get; }
        public float CloudOpacity { get; }
        public Color BloomColor { get; }
        public float AmbientOcclusion { get; }
        public float Emissive { get; }
    }
}

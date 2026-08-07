Shader "CML/Cinematics/Deep Space"
{
    Properties
    {
        _SpaceColor("Deep Space Color", Color) = (0.004, 0.006, 0.017, 1)
        _NebulaColorA("Nebula Color A", Color) = (0.18, 0.09, 0.42, 1)
        _NebulaColorB("Nebula Color B", Color) = (0.02, 0.32, 0.55, 1)
        _NebulaColorC("Nebula Rim Color", Color) = (0.72, 0.24, 0.46, 1)
        _NebulaScale("Nebula Scale", Range(0.2, 6)) = 1.35
        _NebulaCoverage("Nebula Coverage", Range(0, 1)) = 0.52
        _NebulaContrast("Nebula Contrast", Range(0.5, 6)) = 2.35
        _NebulaIntensity("Nebula Intensity", Range(0, 4)) = 1.15
        _GalaxyAxis("Galaxy Pole Axis", Vector) = (0.31, 0.86, -0.4, 0)
        _GalaxyWidth("Galaxy Band Width", Range(0.05, 1)) = 0.42
        _GalaxyIntensity("Galaxy Band Intensity", Range(0, 3)) = 0.85
        _GalaxyColor("Galaxy Band Color", Color) = (0.56, 0.62, 0.86, 1)
        _StarDensity("Star Density", Range(0, 1)) = 0.055
        _StarBrightness("Star Brightness", Range(0, 12)) = 4.2
        _StarSharpness("Star Sharpness", Range(1, 40)) = 12
        _TwinkleSpeed("Twinkle Speed", Range(0, 6)) = 1.6
        _WarpBlend("Warp Blend", Range(0, 1)) = 0
        _WarpAxis("Warp Axis", Vector) = (0, 0, 1, 0)
        _WarpStretch("Warp Stretch", Range(0, 1)) = 0.35
        _Exposure("Exposure", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "CinematicDeepSpace"
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SpaceColor;
                half4 _NebulaColorA;
                half4 _NebulaColorB;
                half4 _NebulaColorC;
                half4 _GalaxyColor;
                float4 _GalaxyAxis;
                float4 _WarpAxis;
                half _NebulaScale;
                half _NebulaCoverage;
                half _NebulaContrast;
                half _NebulaIntensity;
                half _GalaxyWidth;
                half _GalaxyIntensity;
                half _StarDensity;
                half _StarBrightness;
                half _StarSharpness;
                half _TwinkleSpeed;
                half _WarpBlend;
                half _WarpStretch;
                half _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float3 Hash33(float3 value)
            {
                value = float3(
                    dot(value, float3(127.1, 311.7, 74.7)),
                    dot(value, float3(269.5, 183.3, 246.1)),
                    dot(value, float3(113.5, 271.9, 124.6)));
                return frac(sin(value) * 43758.5453);
            }

            float ValueNoise3(float3 position)
            {
                float3 cell = floor(position);
                float3 local = frac(position);
                local = local * local * (3.0 - 2.0 * local);

                float bottomBackLeft = Hash31(cell + float3(0, 0, 0));
                float bottomBackRight = Hash31(cell + float3(1, 0, 0));
                float bottomFrontLeft = Hash31(cell + float3(0, 1, 0));
                float bottomFrontRight = Hash31(cell + float3(1, 1, 0));
                float topBackLeft = Hash31(cell + float3(0, 0, 1));
                float topBackRight = Hash31(cell + float3(1, 0, 1));
                float topFrontLeft = Hash31(cell + float3(0, 1, 1));
                float topFrontRight = Hash31(cell + float3(1, 1, 1));

                float bottom = lerp(
                    lerp(bottomBackLeft, bottomBackRight, local.x),
                    lerp(bottomFrontLeft, bottomFrontRight, local.x),
                    local.y);
                float top = lerp(
                    lerp(topBackLeft, topBackRight, local.x),
                    lerp(topFrontLeft, topFrontRight, local.x),
                    local.y);
                return lerp(bottom, top, local.z);
            }

            // Five octaves keep the cloud edges readable at the angular size a
            // 60 degree camera sees while still resolving the thin filaments
            // that separate a nebula from a plain colour wash.
            float Fbm3(float3 position)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int octave = 0; octave < 5; octave++)
                {
                    value += ValueNoise3(position) * amplitude;
                    position = position * 2.04 + float3(11.3, -7.9, 4.6);
                    amplitude *= 0.5;
                }

                return value * 1.0322;
            }

            float3 StarLayer(float3 direction, float scale, float density)
            {
                float3 position = direction * scale;
                float3 cell = floor(position);
                float3 local = frac(position) - 0.5;
                float3 random = Hash33(cell);

                // Only a fraction of the cells hold a star, otherwise the sky
                // turns into an even grid instead of a field.
                float present = step(random.z, density);
                float3 offset = (random - 0.5) * 0.72;
                float distance = length(local - offset);

                // The disc has to stay comfortably wider than a pixel at the
                // angular size of a cell, otherwise the field aliases away to
                // nothing instead of reading as stars.
                float radius = lerp(0.12, 0.46, random.x * random.x);
                float core = saturate(1.0 - distance / max(radius, 0.001));
                float star = pow(core, _StarSharpness);

                float twinkle = 0.72 + 0.28 * sin(
                    _Time.y * _TwinkleSpeed + random.y * 63.7);
                float magnitude = lerp(0.34, 1.0, random.x * random.x);

                // Real star fields read as blue-white with a few warm giants.
                float3 warm = float3(1.0, 0.78, 0.55);
                float3 cool = float3(0.62, 0.78, 1.0);
                float3 tint = lerp(
                    lerp(cool, float3(1.0, 1.0, 1.0), saturate(random.y * 1.4)),
                    warm,
                    saturate((random.y - 0.78) * 4.0));

                return tint * (star * present * magnitude * twinkle);
            }

            float3 StarField(float3 direction, float bandBoost)
            {
                float3 stars = 0.0;
                stars += StarLayer(direction, 168.0, _StarDensity * 1.30 * bandBoost) * 0.55;
                stars += StarLayer(direction, 96.0, _StarDensity * 0.85 * bandBoost) * 0.85;
                stars += StarLayer(direction, 47.0, _StarDensity * 0.42 * bandBoost) * 1.55;
                return stars;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionWS);

                // Galactic plane. The band both brightens the background and
                // multiplies the local star count, which is what separates a
                // photographed sky from a uniform sprinkle of dots.
                float3 galaxyPole = normalize(_GalaxyAxis.xyz);
                float poleDot = abs(dot(direction, galaxyPole));
                float band = 1.0 - smoothstep(0.0, max(_GalaxyWidth, 0.001), poleDot);
                float bandCore = pow(band, 2.4);

                float3 nebulaPosition = direction * _NebulaScale * 2.2;
                float warp = Fbm3(nebulaPosition * 0.55 + float3(0.0, 0.0, 3.1));
                float3 warpedPosition = nebulaPosition + (warp - 0.5) * 1.85;
                float clouds = Fbm3(warpedPosition);
                float detail = Fbm3(warpedPosition * 3.35 + float3(-5.2, 8.7, 1.4));

                float coverage = lerp(0.78, 0.34, _NebulaCoverage);
                float density = saturate(
                    (clouds * 0.78 + detail * 0.32 - coverage) * _NebulaContrast);
                density *= lerp(0.35, 1.0, 0.35 + bandCore * 0.65);

                float3 nebula = lerp(
                    _NebulaColorA.rgb,
                    _NebulaColorB.rgb,
                    saturate(detail * 1.25));
                float rim = saturate(density * density * 1.6 - 0.12);
                nebula = lerp(nebula, _NebulaColorC.rgb, rim * 0.62);
                nebula *= density * _NebulaIntensity;

                float3 color = _SpaceColor.rgb;
                color += _GalaxyColor.rgb * bandCore * _GalaxyIntensity * 0.22;
                color += nebula;

                float bandBoost = 1.0 + bandCore * 2.6;
                float3 stars = StarField(direction, bandBoost);

                // At superluminal speed the sky itself smears: sampling the same
                // field a few steps along the travel axis turns every point into
                // a trail without needing a second geometry pass.
                if (_WarpBlend > 0.001)
                {
                    float3 axis = normalize(_WarpAxis.xyz);
                    float3 smeared = 0.0;
                    float weight = 0.0;
                    for (int tap = 1; tap <= 4; tap++)
                    {
                        float travel = tap * 0.25 * _WarpStretch * _WarpBlend;
                        float3 tapDirection = normalize(direction - axis * travel);
                        float tapWeight = 1.0 - tap * 0.19;
                        smeared += StarField(tapDirection, bandBoost) * tapWeight;
                        weight += tapWeight;
                    }

                    smeared /= max(weight, 0.001);
                    stars = lerp(stars, max(stars, smeared) * 1.45, _WarpBlend);
                    stars *= lerp(1.0, 2.1, _WarpBlend);
                    stars *= lerp(
                        float3(1.0, 1.0, 1.0),
                        float3(0.72, 0.88, 1.0),
                        _WarpBlend);
                }

                color += stars * _StarBrightness;
                color *= _Exposure;
                return half4(max(color, 0.0), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

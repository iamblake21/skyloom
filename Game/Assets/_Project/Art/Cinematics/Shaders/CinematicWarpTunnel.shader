Shader "CML/Cinematics/Warp Tunnel"
{
    Properties
    {
        _CoreColor("Core Color", Color) = (0.92, 0.98, 1, 1)
        _MidColor("Mid Color", Color) = (0.24, 0.62, 1, 1)
        _EdgeColor("Edge Color", Color) = (0.34, 0.12, 0.72, 1)
        _Intensity("Intensity", Range(0, 8)) = 0
        _Speed("Scroll Speed", Range(0, 12)) = 3.4
        _StreakDensity("Streak Density", Range(8, 512)) = 168
        _StreakLength("Streak Length", Range(0.5, 8)) = 2.6
        _Turbulence("Turbulence", Range(0, 1)) = 0.28
        _Twist("Twist", Range(-2, 2)) = 0.35
        _ChromaticSplit("Chromatic Split", Range(0, 0.2)) = 0.035
        _EndFade("End Fade", Range(0.01, 0.5)) = 0.22
        _CoreGlow("Core Glow", Range(0, 4)) = 1.1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WarpTunnel"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            // The camera lives inside the tube and a ray from inside a cylinder
            // leaves through exactly one wall, so drawing both faces costs
            // nothing and removes any dependency on the mesh winding.
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _MidColor;
                half4 _EdgeColor;
                half _Intensity;
                half _Speed;
                half _StreakDensity;
                half _StreakLength;
                half _Turbulence;
                half _Twist;
                half _ChromaticSplit;
                half _EndFade;
                half _CoreGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 Hash21(float value)
            {
                float2 seed = frac(float2(value * 0.1031, value * 0.1030));
                seed += dot(seed, seed.yx + 33.33);
                return frac(float2((seed.x + seed.y) * seed.x,
                    (seed.x + seed.y) * seed.y));
            }

            float Hash11(float value)
            {
                value = frac(value * 0.1031);
                value *= value + 33.33;
                return frac(value * (value + value));
            }

            float ValueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                local = local * local * (3.0 - 2.0 * local);
                float bottomLeft = Hash11(cell.x + cell.y * 57.0);
                float bottomRight = Hash11(cell.x + 1.0 + cell.y * 57.0);
                float topLeft = Hash11(cell.x + (cell.y + 1.0) * 57.0);
                float topRight = Hash11(cell.x + 1.0 + (cell.y + 1.0) * 57.0);
                return lerp(
                    lerp(bottomLeft, bottomRight, local.x),
                    lerp(topLeft, topRight, local.x),
                    local.y);
            }

            // One filament of light. Each one owns its width, its speed and its
            // phase, so the tunnel never reads as an evenly spaced comb.
            float StreakLayer(float2 uv, float density, float scroll, float seed)
            {
                float scaled = uv.x * density + seed;
                float id = floor(scaled);
                float local = frac(scaled) - 0.5;
                float2 random = Hash21(id + seed * 31.7);

                // Thin filaments read as light. Wide ones read as painted bars,
                // which is exactly the cheap look this shader has to avoid.
                float width = lerp(0.045, 0.16, random.x);
                float lateral = exp(-(local * local) / max(width * width, 1e-4));

                float speed = lerp(0.55, 1.85, random.y);
                float travel = uv.y * _StreakLength * lerp(0.7, 1.6, random.x)
                    - scroll * speed
                    + random.y * 7.13;
                float head = frac(travel);

                // A comet is a soft leading edge and a long tail. Fading the
                // first few percent removes the hard wrap that turns every
                // filament into a rectangle.
                float comet = exp(-head * lerp(3.2, 11.0, random.y))
                    * smoothstep(0.0, 0.085, head);

                return lateral * comet * lerp(0.28, 1.0, random.x * random.x);
            }

            float TunnelEnergy(float2 uv, float scroll, float chromaticOffset)
            {
                float2 sampleUv = uv;
                sampleUv.y += chromaticOffset;

                // A slow twist plus a little turbulence stops the streaks from
                // being mathematically parallel, which is the single tell that
                // reads as a cheap tunnel.
                sampleUv.x += sampleUv.y * _Twist * 0.08;
                sampleUv.x += (ValueNoise(
                    float2(sampleUv.x * 18.0, sampleUv.y * 3.1 - scroll * 0.35))
                    - 0.5) * _Turbulence * 0.02;

                float energy = 0.0;
                energy += StreakLayer(sampleUv, _StreakDensity, scroll, 0.0) * 1.00;
                energy += StreakLayer(sampleUv, _StreakDensity * 0.47, scroll * 0.82, 13.7) * 0.72;
                energy += StreakLayer(sampleUv, _StreakDensity * 2.10, scroll * 1.31, 51.3) * 0.42;
                return energy;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Intensity <= 0.001)
                {
                    return half4(0, 0, 0, 0);
                }

                float scroll = _Time.y * _Speed;
                float energy = TunnelEnergy(input.uv, scroll, 0.0);

                // The colour of a filament comes from how hot it is, not from
                // which channel happened to sample it. Driving all three from
                // one scalar is what keeps the tunnel white-hot-to-violet
                // instead of a rainbow of primaries.
                float3 color = lerp(
                    _EdgeColor.rgb,
                    _MidColor.rgb,
                    saturate(energy * 1.9));
                color = lerp(
                    color,
                    _CoreColor.rgb,
                    saturate(pow(energy, 1.8) * 2.2));
                color *= energy;

                // A whisper of lens dispersion, sampled around the same
                // filament rather than a different one.
                if (_ChromaticSplit > 0.0001)
                {
                    float warm = TunnelEnergy(input.uv, scroll, _ChromaticSplit);
                    float cool = TunnelEnergy(input.uv, scroll, -_ChromaticSplit);
                    color.r += (warm - energy) * 0.22;
                    color.b += (cool - energy) * 0.22;
                }

                // Near end: no visible ring where the tube passes the camera.
                // Far end: a long dissolve, so looking down the axis fades into
                // depth instead of hitting a black disc.
                float fade = smoothstep(0.0, _EndFade, input.uv.y)
                    * (1.0 - smoothstep(0.52, 1.0, input.uv.y));

                // A soft axial glow fills the void between the filaments so the
                // tunnel has volume instead of being a wall of lines.
                color += _MidColor.rgb * saturate(energy) * _CoreGlow * 0.14;

                color *= fade * _Intensity;
                return half4(max(color, 0.0), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

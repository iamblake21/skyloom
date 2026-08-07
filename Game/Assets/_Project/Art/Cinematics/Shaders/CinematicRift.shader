Shader "CML/Cinematics/Rift"
{
    Properties
    {
        _CoreColor("Core Color", Color) = (1, 0.97, 0.92, 1)
        _EnergyColor("Energy Color", Color) = (0.36, 0.82, 1, 1)
        _RimColor("Rim Color", Color) = (0.62, 0.24, 1, 1)
        _VoidColor("Void Color", Color) = (0.02, 0.03, 0.09, 1)
        _Openness("Openness", Range(0, 1)) = 0
        _Width("Tear Width", Range(0, 1)) = 0.32
        _EdgeSoftness("Edge Softness", Range(0.002, 0.4)) = 0.06
        _EdgeTurbulence("Edge Turbulence", Range(0, 1)) = 0.42
        _TurbulenceScale("Turbulence Scale", Range(0.5, 24)) = 6.5
        _TurbulenceSpeed("Turbulence Speed", Range(0, 8)) = 1.7
        _Refraction("Refraction", Range(0, 0.5)) = 0.11
        _SwirlIntensity("Swirl Intensity", Range(0, 3)) = 0.9
        _SwirlSpeed("Swirl Speed", Range(0, 6)) = 1.3
        _FilamentIntensity("Filament Intensity", Range(0, 4)) = 1.2
        _Intensity("Intensity", Range(0, 8)) = 1
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
            Name "Rift"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EnergyColor;
                half4 _RimColor;
                half4 _VoidColor;
                half _Openness;
                half _Width;
                half _EdgeSoftness;
                half _EdgeTurbulence;
                half _TurbulenceScale;
                half _TurbulenceSpeed;
                half _Refraction;
                half _SwirlIntensity;
                half _SwirlSpeed;
                half _FilamentIntensity;
                half _Intensity;
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
                float4 screenPos : TEXCOORD1;
            };

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                local = local * local * (3.0 - 2.0 * local);
                float bottomLeft = Hash21(cell);
                float bottomRight = Hash21(cell + float2(1, 0));
                float topLeft = Hash21(cell + float2(0, 1));
                float topRight = Hash21(cell + float2(1, 1));
                return lerp(
                    lerp(bottomLeft, bottomRight, local.x),
                    lerp(topLeft, topRight, local.x),
                    local.y);
            }

            float Fbm(float2 uv)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    value += ValueNoise(uv) * amplitude;
                    uv = uv * 2.07 + float2(7.3, -3.9);
                    amplitude *= 0.5;
                }

                return value * 1.0667;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = (input.uv - 0.5) * 2.0;
                float time = _Time.y;

                float openness = saturate(_Openness);
                if (openness <= 0.0005)
                {
                    return half4(0, 0, 0, 0);
                }

                // The tear grows as a lens: tall first, then wide, so the rip
                // reads as space splitting open instead of a circle scaling up.
                float halfHeight = max(openness, 0.001);
                float verticalFalloff = 1.0 - saturate(
                    (centered.y / halfHeight) * (centered.y / halfHeight));
                float profile = sqrt(max(verticalFalloff, 0.0));

                float boundaryNoise = Fbm(float2(
                    centered.y * _TurbulenceScale,
                    time * _TurbulenceSpeed * 0.35)) - 0.5;
                float halfWidth = _Width * openness * profile
                    * (1.0 + boundaryNoise * _EdgeTurbulence * 1.35);

                float distanceToEdge = abs(centered.x) - max(halfWidth, 0.0);
                float softness = max(_EdgeSoftness, 0.002);

                float interior = smoothstep(softness, -softness, distanceToEdge);

                // The lips glow, the interior does not. Measuring from the
                // boundary in both directions is what keeps the middle of the
                // tear deep instead of a flat white slab.
                float edgeDistance = abs(distanceToEdge);
                float rim = exp(-edgeDistance / max(softness * 1.6, 0.002));
                float halo = exp(-edgeDistance * 7.0) * profile * 0.55;

                // Filaments arc across the opening and along its lips. They are
                // the detail that sells the tear as energy rather than a decal.
                float filamentField = Fbm(float2(
                    centered.x * 9.5 + time * 1.7,
                    centered.y * 22.0 - time * 3.1));
                float filaments = pow(saturate(filamentField * 1.6 - 0.62), 3.0)
                    * _FilamentIntensity
                    * saturate(profile * 1.4)
                    * saturate(1.0 - abs(distanceToEdge) * 3.2);

                // Everything outside is dragged inwards along a spiral.
                float radius = length(centered);
                float angle = atan2(centered.y, centered.x);
                float spiral = Fbm(float2(
                    angle * 2.2 + log(max(radius, 0.05)) * 3.1,
                    time * _SwirlSpeed * 0.4));
                float swirl = pow(saturate(spiral * 1.5 - 0.45), 2.2)
                    * _SwirlIntensity
                    * exp(-radius * 2.4)
                    * openness;

                // Gravitational lensing: the background is pulled toward the
                // tear, strongest right at the lips.
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                float2 pull = normalize(float2(centered.x, centered.y * 0.35)
                    + float2(1e-5, 0.0));
                float lensStrength = _Refraction
                    * openness
                    * exp(-max(distanceToEdge, 0.0) * 4.5)
                    * (1.0 - interior);
                float2 refractedUV = screenUV - pull * lensStrength * 0.12;
                float3 background = SampleSceneColor(refractedUV);

                // The inside of the rip is not a white slab: it is somewhere
                // else, seen through turbulence. Structure here is the whole
                // difference between a tear and a lit rectangle.
                float interiorNoise = Fbm(float2(
                    centered.x * 6.5 - time * 0.55,
                    centered.y * 3.1 + time * 0.32));
                float depth = 1.0 - saturate(
                    abs(centered.x) / max(halfWidth, 0.001));
                float3 interiorColor = lerp(
                    _VoidColor.rgb,
                    _EnergyColor.rgb,
                    saturate(interiorNoise * 1.7 - 0.22));
                interiorColor = lerp(
                    interiorColor,
                    _CoreColor.rgb,
                    pow(saturate(interiorNoise * 1.3 - 0.58), 1.8)
                        * saturate(depth * 1.4));

                // The lips carry the energy; the middle stays deep.
                float3 emissive =
                    interiorColor * interior * (0.55 + depth * 0.5)
                    + _CoreColor.rgb * rim * 2.1
                    + _EnergyColor.rgb * halo * 1.35
                    + _RimColor.rgb * (swirl + halo * 0.45)
                    + _CoreColor.rgb * filaments * 0.9;

                float coverage = saturate(interior + rim * 0.35 + halo * 0.25);
                float3 color = lerp(background, background * 0.35, coverage * 0.6)
                    * coverage
                    + emissive * _Intensity;

                return half4(max(color, 0.0), saturate(coverage));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
